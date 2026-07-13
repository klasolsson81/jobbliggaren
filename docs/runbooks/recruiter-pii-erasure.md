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
  "confirmedJobAdIds": null
}
```

**One free-text identifier. There is no type discriminator, and that is
deliberate** — TD-75's premise ("email är primär rekryterar-identifier") was not
stale, it was **falsified**: the email is never a structured key in storage, so
every identifier is matched over free text either way. **TD-75 is closed as
void.** Run the dry run once per identifier you hold: her address, her number,
**and her name**.

Matching runs on **three channels**, and each exists because the others miss
something:

1. **Full-text search** — the exposure a user can actually exploit against
   `/jobb`. It lexemes, so it finds `Fagerberg, Magnus` when you search
   `Magnus Fagerberg`. The substring scan cannot.
2. **Substring over `title`, `description` and `raw_payload`** — it finds the
   identifier *as you typed it*, and it reaches `employer.name` inside the
   payload.
3. **Substring over `company_name`** — because an **enskild firma's company name
   IS a person's name**, that column is not in the FTS index, and `raw_payload`
   is nulled 30 days after publication. Without this channel she is told *"we
   hold no data matching this identifier"* for most of the corpus, while her name
   sits in plaintext in a column we scan.

The union **over-matches on purpose**: a false positive costs you a second look,
a false negative costs a false confirmation to a named person.

> ⚠ **What matching does NOT do: it does not de-obfuscate.** Searching
> `anna@acme.se` will not find an ad that reads `anna(at)acme.se` — the substring
> channel compares the string you gave it, and FTS lexemes the obfuscated form
> differently. **What serves that population is her NAME**, which sits in the body
> in plain words. This is the single most important reason to run the dry run once
> per identifier: her address, her number, **and her name**. (An earlier version of
> this runbook credited the substring channel with catching obfuscation. It does
> not. Crediting the wrong mechanism for a control's coverage is the same defect
> class this whole issue is about, so it is corrected here rather than quietly.)

The response:

```jsonc
{
  "requestId": "…",              // correlates with the audit_log row
  "outcome": "DryRun",           // NoMatchingDataHeld | DryRun | AdsErased | NothingErased
  "dryRun": true,
  "matched": {
    "jobAds": 3, "recentJobSearches": 1, "savedSearches": 2,
    "applicationSnapshots": 4, "userAuthoredText": 1
  },
  "erased": { "jobAds": 0, "recentJobSearches": 0, "savedSearches": 0,
              "applicationSnapshots": 0, "userAuthoredText": 0 },
  "matches": [                   // ← THE ADS. This is what you review.
    { "jobAdId": "…", "externalId": "…", "title": "Backend-utvecklare",
      "matchedExcerpt": "…kontakta ansvarig rekryterare Magnus Fagerberg på…" }
  ],
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
  "confirmedJobAdIds": ["…", "…"]   // the ids of the ads YOU READ, in 2.2
}
```

**You send back the ads you reviewed. Not a number.** A count cannot be reviewed:
a recruiter named *Anna* substring-matches *Johanna*, *Susanna* and *Marianna*
across thousands of ads, and an operator who reads `4127` and retypes `4127` has
reviewed nothing while irreversibly destroying 4 127 ads.

- Omit `confirmedJobAdIds` and the request is rejected (**400**). You cannot erase
  without having looked.
- Confirm an ad that no longer matches and the request is refused (**409**) and
  **nothing is destroyed** — ingest runs every ten minutes, so the match set
  genuinely moves between looking and confirming. Re-run the dry run, re-read,
  re-confirm.
- **Anything you do not confirm is not erased**, and the response reports the gap.

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
| `saved_searches` | ⚠️ **Not automatically — a HUMAN erases it, inside the Art. 12(3) month** | **HER RIGHT APPLIES. Do not tell her otherwise.** One row carries two data subjects under two bases: the user's own criteria rest on Art. 6(1)(b) (our contract with *her*), but **the recruiter's name sitting inside those criteria does not** — 6(1)(b) requires the data subject to be a **party** to the contract, and the recruiter is party to nothing. That processing rests on **Art. 6(1)(f)**, which **Art. 21(1) reaches**. So her objection fires and Art. 17(1)(c) is available. We do not attempt the "compelling legitimate grounds" override: keeping her name in another user's filter is a convenience, and a saved search is recreatable in seconds. **We owe her erasure and we honour it in full** — we simply do not AUTOMATE it, because `SoftDelete()` would leave `criteria` in the row (it hides, it does not erase) and stripping the term is not always constructible. **A human does it, with the affected user in the loop. That is a mechanism choice, never a refusal.** |
| `applications.cover_letter`, `application_notes.content`, `follow_ups.note` | ⚠️ **Not automatically — a HUMAN erases it** | A user may well have written *"Ringde Magnus Fagerberg"* in her own note. That is the recruiter's personal data, and her right reaches it (6(1)(f) → Art. 21(1)) — but a job does not silently rewrite a person's private notes about her own job hunt. Reported; a human handles it with that user. *(This surface was found by driving the cascade registry from the EF model. Nobody had enumerated it.)* |
| `applications.snapshot_company` + `snapshot_description` | ❌ **No** | The applicant's frozen record of an ad she applied to (ADR 0086 exists precisely so it outlives the ad). **And the ground is STRONGER for the company name than for the body:** a Swedish jobseeker must file an *aktivitetsrapport* to Arbetsförmedlingen **naming the employer**. The company name is the **spine** of her own legal record; the ad body is its colour. Ground: Art. 17(3)(e). **Klas's to affirm — STOPP-3, still open.** We **search and report** it precisely because we do not erase it: *a legal ground asserted over a population we never counted is a ground asserted over a silence.* |
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

**B2. Completion — `AdsErased`, but `matched.savedSearches` or
`matched.userAuthoredText` is above zero.** Use B1 and add:

> Vi har också hittat ditt namn i en sparad sökning eller i en anteckning som en
> användare själv har skrivit. Den rätten gäller även där, och vi tar bort
> uppgifterna. Det görs manuellt, tillsammans med den användare det gäller, och vi
> hör av oss när det är klart. Det sker inom en månad från det att din begäran kom
> in.

**Do NOT write that her objection does not cover it.** It does. Art. 6(1)(b)
requires the data subject to be a *party* to the contract, and she is not a party
to ours — so the processing of her name inside a user's saved search rests on
Art. 6(1)(f), which Art. 21(1) reaches. Telling her otherwise would be a false
statement to a data subject about her own rights (Art. 12(4)), which is the exact
class of thing this whole issue exists to stop us doing. **"Our code cannot do it"
is not a legal ground. It never was.**

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
