# Recruiter PII erasure (GDPR Art. 17) — operational procedure

> What a human does on the day a recruiter asks us to erase her contact details
> from an ad we imported from Platsbanken.
> Contract: **ADR 0106** (two-tier). Issue: **#842**.

**Last updated:** 2026-07-13 (#842 PR2 — Tier B: provable erasure on request)
**Applies to:** the state of the system **today** (post-PR2, pre-PR3).

---

## 0. STATUS — read this before anything else

**There is now a working erasure path. It removes the whole ad record and blocks
its re-import.** That is new as of PR2, and it is the first time in this
product's life that a confirmation sent under Art. 12(3) is a true statement.

What is **still missing**, and what it means:

| | State |
|---|---|
| **Tier B — erasure on request (Art. 17)** | ✅ **SHIPPED.** `POST /api/v1/admin/job-ads/redact-recruiter-pii`. §2 is the procedure. |
| **Tier A — not storing the contact at all (Art. 25)** | ❌ **NOT SHIPPED** (PR3, sequenced behind #841). We still ingest and store ~27 000 recruiters' contact details in plaintext free text, full-text indexed. |
| **Launch gate** | 🔒 **CLOSED. No `v*` prod tag until BOTH tiers have shipped.** See below — this is not negotiable and it is not a product decision. |

### Why the gate stays closed even though erasure now works

An Art. 17 endpoint answers the people who **ask**. Art. 25(2) is about the
~37 000 recruiters who will **never know we exist and will never ask**, and
whose addresses sit in `job_ads.description` in plaintext, inside a GIN-backed
`search_vector` that **any logged-in user can reverse-query**. Tier B does not
touch that population, and **an Art. 17 endpoint does not discharge an Art. 25
duty.** Shipping to production with the ingest scrub un-shipped would be the same
category of claim this issue exists to stop making.

*(This corrects the CTO re-bind, which carried STOPP-6 over verbatim while
reordering the PRs, and so read as though Tier B alone lifted the gate. It does
not. Amended by the security-auditor's B2, 2026-07-13.)*

### The history, kept because it is the reason for every rule below

The mechanism this replaces **erased nothing, on every request, always**, and
reported success. It probed `raw_payload` for
`{"employer":{"contact_email": …}}` — a key the wire POCO cannot emit and the
ingest sanitizer's default-deny allowlist drops. **Measured: 0 of 93 469
ingested ads carried it.** `rowsAffected = 0` was its only possible outcome, and
the old version of this runbook told the operator that 0 "är OK" and then told
him to *"bekräfta att radering är genomförd."* **That confirmation was never
true. Not once.**

The audit log shows the endpoint was called **0 times**, so no data subject ever
received that false confirmation. That is luck, not design.

---

## 1. If a request arrives

1. **Verify identity first.** The requester must demonstrate control of the
   address or number in question (a request from the same address, or a written
   request). A name-only request needs a different, controller-level judgement —
   escalate.
2. **Record the request and the date it was received.** Art. 12(3) starts a
   **one-month clock** for informing the data subject of the action taken — or of
   the reasons for taking none, plus her right to lodge a complaint with IMY and
   to seek a judicial remedy.
3. **Do not put the requester's email, phone number or name into the repo, into a
   commit message, into a log line, or into an issue.** This repo is public
   (ADR 0072). The identifier lives in the controller's case record. The
   application itself never stores it: the audit row carries an
   **HMAC-SHA256** of it, never the value.
4. **Klas signs off before the destructive call.** The dry run is yours to run;
   the erasure is his to approve.

---

## 2. The procedure

Admin session required. The route is `POST /api/v1/admin/job-ads/redact-recruiter-pii`.

### 2.1 Dry run — always first, and the API enforces it

```jsonc
// POST /api/v1/admin/job-ads/redact-recruiter-pii
{
  "identifier": "<email, phone number, OR name>",
  "dryRun": true,
  "confirmedJobAdCount": null
}
```

**One free-text identifier. There is no type discriminator, and that is
deliberate** — TD-75's premise ("email är primär rekryterar-identifier") was not
stale, it was **falsified**: the email is never a structured key in storage, so
every identifier is matched over free text either way. **TD-75 is closed as
void.** Run the dry run once per identifier you hold: her address, her number,
**and her name**.

Matching runs on **two channels**: full-text search (the exposure a user can
actually exploit against `/jobb`) **and** a case-insensitive substring scan over
`title`, `description` and `raw_payload`. FTS alone will not find an obfuscated
address (`anna(at)acme.se` tokenises as ordinary words), and the substring scan
reaches `employer.name` — which is how a request naming an **enskild firma's**
owner finds her ad at all. The union **over-matches on purpose**: a false
positive costs you a second look, a false negative costs a false confirmation to
a named person.

The response:

```jsonc
{
  "requestId": "…",              // correlates with the audit_log row
  "outcome": "DryRun",           // NoMatchingDataHeld | DryRun | AdsErased
  "dryRun": true,
  "matched": { "jobAds": 3, "recentJobSearches": 1, "savedSearches": 2 },
  "erased":  { "jobAds": 0, "recentJobSearches": 0, "savedSearches": 0 },
  "erasedExternalIds": []
}
```

**Read `matched` against `erased`. The gap is not a bug — it is the disclosure.**
See §3.

### 2.2 Human confirmation — mandatory, before anything destructive

A substring match is not a match. Common names produce false positives, and a
company inbox is not necessarily the requester's personal address. **A human
reads each candidate ad and confirms it is the requester's data.** Record which
`externalId`s were confirmed.

### 2.3 The erasure

```jsonc
{
  "identifier": "<the same identifier>",
  "dryRun": false,
  "confirmedJobAdCount": 3     // the number the dry run reported
}
```

**`confirmedJobAdCount` is what makes the dry run mandatory in code rather than
in this sentence.** Omit it and the request is rejected (400). Supply a number
that no longer matches reality and the request is refused (**409**) and
**nothing is destroyed** — ingest runs every ten minutes, so the match set
genuinely moves between looking and confirming. If you get a 409: re-run the dry
run, re-read the matches, re-confirm.

**Erasure is irreversible and it removes the ad for every user.**

### 2.4 What it does, and why it is provable

It deletes the **carrier**, not the **string**. `JobAd.Erase` clears `title`,
`description`, `company_name`, `url`, `raw_payload` and `extracted_terms` in one
transition, and sets `status = 'Erased'`. The STORED `search_vector` recomputes
to empty by itself, `extracted_lexemes` follows, and the seven
`raw_payload`-derived generated columns go NULL with it — among them
`organization_number`, which for an enskild firma **may be a personnummer**.

So there is **no recall question, no obfuscation question and no image-embedded
question**, and it reaches the recruiter's **name**, which no regex ever will.

**And it stays erased.** `UpdateFromSource` refuses on `Erased`, so the nightly
snapshot sync (02:00) and the 10-minute stream cannot write her back — the
re-import tombstone, keyed on the existing `(source, external_id)` tuple. It
stores **no personal data**: a source, an id and a status. *(This is why there is
no suppression ledger and never will be: a ledger would store her email in order
to keep erasing it — the one design that leaves us holding more of her data after
her request than before it.)*

`applications.job_ad_id` FKs still point at the row, so applicants keep their own
records. The ad detail endpoint returns **410 Gone**, not 404: it existed, and it
is deliberately gone.

### 2.5 The audit trail

Written automatically, by the pipeline, on **success and on rejection** — a
refused rights request that leaves no trace is its own Art. 12(3) exposure.

```sql
SELECT occurred_at, event_type, payload
FROM audit_log
WHERE aggregate_type = 'RecruiterErasureRequest'
ORDER BY occurred_at DESC;
```

`payload` carries `{ identifierHmac, dryRun, succeeded, outcome, matched, erased,
erasedExternalIds }`. The `externalId`s are Arbetsförmedlingen's public ad
identifiers — **not personal data**, and the accountability spine (Art. 5(2)/30):
they are what lets an auditor, or the recruiter, verify the erasure actually
happened.

**The identifier is HMAC-SHA256'd under the server pepper. Never md5** — an
unkeyed digest of an email is dictionary-reversible in milliseconds; it is not a
pseudonym, it is a fig leaf. *(This column was never written by any command until
PR2. The old runbook's "verification query" selected a column that was always
NULL and nobody noticed. It looked like a verification. It verified nothing.)*

---

## 3. What is NOT erased — say this, do not discover it later

The response reports `matched` and `erased` **per surface**. Where they differ,
we hold something we did not erase, and **the reply must say so.** We do not
claim to have erased what we have not erased. That is #842, applied to ourselves.

| Surface | Erased? | Why |
|---|---|---|
| `job_ads` (the ad, all of it) | ✅ **Yes** | Whole-record removal. Provable. |
| `recent_job_searches` | ✅ **Yes**, hard-deleted | If a user searched the recruiter's name, that name is sitting in her search history. Auto-capture rows have no audit-trail dignity, the cap-20 list rebuilds on her next search, so the user loses nothing. |
| `saved_searches` | ❌ **No** | A saved search is the **user's own** artefact, processed under Art. **6(1)(b)** (our contract with her). **Art. 21(1) textually reaches only 6(1)(e)/(f)**, so the recruiter's objection never fires against it, and the 17(1)(c) ground that would flow from a successful objection never arises. *(And the remedies are broken anyway: `SoftDelete()` leaves `criteria` in the row — it hides, it does not erase — and stripping the search term is not always constructible, because a saved search whose only criterion IS the recruiter's name cannot have it removed.)* **Reported, disclosed, and a human decides with the affected user in the loop.** |
| `applications.snapshot_description` | ❌ **No** | The applicant's frozen record of an ad she applied to (ADR 0086 exists precisely so it outlives the ad). Nulling it destroys **her** evidence to serve a third party's request. Ground: Art. 17(3)(e). **Klas's to affirm — STOPP-3, still open.** |
| Backups / WAL / PITR | ⚠️ **Unstated** | An `UPDATE` does not remove the old row version from disk until `VACUUM`, and copies remain in WAL and backups. **Do not make any statement to the data subject about backups.** The retention window is not yet decided (**STOPP-4**). Do not invent one. |

**If `matched.savedSearches > 0`, the reply must disclose it.** Template B2.

---

## 4. Reply templates (drafts for Klas — he approves and sends)

Swedish, "du", no exclamation marks, no emoji, no em-dash (CLAUDE.md §10).
**Never claim more than was actually done.** Do not add sentences about backups
(STOPP-4).

**A. Acknowledgement, on receipt.**

> Tack, vi har tagit emot din begäran om radering. Vi återkommer med besked om
> vilken åtgärd vi har vidtagit inom en månad från det att begäran kom in. Du når
> oss under tiden på <kontaktadress>.

**B1. Completion — `AdsErased`, and nothing was matched that we did not erase.**

> Vi har tagit bort hela annonsen ur våra system, och vi hindrar att den hämtas in
> igen. Annonstexten, sökindexet och de uppgifter vi har härlett ur annonsen är
> borttagna hos oss. Vi kan inte ta bort annonsen hos Arbetsförmedlingen, som är
> den som har publicerat den. Vill du att uppgifterna tas bort även där behöver du
> vända dig till Arbetsförmedlingen eller till arbetsgivaren som publicerade
> annonsen.

**B2. Completion — `AdsErased`, but `matched.savedSearches > 0`.** Use B1 and add:

> En eller flera användare har sparat en sökning som innehåller ditt namn eller
> dina kontaktuppgifter som söktext. Den sparade sökningen tillhör användaren och
> vi tar inte bort den automatiskt. Hör av dig om du vill att vi tittar närmare på
> det, så hanterar vi det manuellt.

**C. `NoMatchingDataHeld`.** This sentence is now **true** when we say it — both
channels ran, over every free-text surface we hold.

> Vi har sökt igenom våra system och vi har inga uppgifter som matchar det du har
> uppgett. Om du har fler uppgifter, till exempel en annan adress, ett telefonnummer
> eller namnet så som det står i annonsen, hör av dig så söker vi igen. Du har rätt
> att lämna klagomål till Integritetsskyddsmyndigheten och att vända dig till
> domstol.

---

## 5. Cross-references

- **ADR 0106** — the erasure contract: ingest minimisation (Tier A) + provable
  record removal (Tier B). The binding document.
- **ADR 0024** — audit retention and the Art. 17 cascade registry. Its scope
  listed only `raw_payload` and never `job_ads.description`. **The registry is now
  code** — `ErasureCascadeRegistry`, enforced by `ErasureCascadeRegistryTests`: a
  new persisted surface that is not classified **breaks the build**.
- **ADR 0086** — the applicant's ad snapshot (why erasure does not cascade into it).
- **ADR 0090 D5** — HMAC-SHA256 over the server pepper. Bound there, **built in
  #842** (it had never existed).
- **Issue #842** — this defect. **#843** (the test-fiction rule), **#845**,
  **#821**/**#841**.
- `docs/research/2026-07-13-842-erasure-evidence-pack.md` — the proven facts and
  the measurements quoted above (local, gitignored per ADR 0072).
- `docs/runbooks/gdpr-processing-register.md` — Art. 30 register.

### Operational prerequisite

`AuditPseudonymization:PepperBase64` must be set (gitignored
`appsettings.Local.json` locally, managed secret in ops). **The application will
not start without it** — deliberately: an HMAC under an absent key looks protected
while being trivially reversible, and a control that only appears to work is the
entire subject of this issue. Generate one with `openssl rand -base64 32`.
