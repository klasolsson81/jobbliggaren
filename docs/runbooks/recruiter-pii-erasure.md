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
   escalate. **An organisationsnummer/personnummer request is served** (CTO
   ruling 2026-07-14, `docs/reviews/2026-07-14-842-pr2-employer-list-cto.md`):
   an enskild firma's org.nr IS the owner's personnummer, Art. 4(1) counts
   identification numbers as personal data, and Art. 12(2) forbids refusing the
   one exact key she has. Identity for an org.nr request means demonstrating she
   is the registered holder (Bolagsverket extract or equivalent) — escalate to
   Klas if unclear.
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
a textual identifier is matched over free text either way. **TD-75 is closed as
void.** Run the dry run once per identifier you hold: her address, her number,
her name — **and, for an enskild firma, her organisationsnummer**.

**An org.nr-shaped identifier is normalised and matched EXACTLY** (CTO ruling
2026-07-14): `556012-5790`, `5560125790` and the century-prefixed personnummer
form `19560125-7901` all normalise to the stored 10-digit form
(`OrganizationNumber.TryFromWrittenForm`) and run as exact equality against
`job_ads.organization_number` and `recent_job_searches.employer_list` — a
structured key gets structured matching, never a regex over prose. Anything not
org.nr-shaped falls back to the free-text channels; nothing is guessed.

The ads are matched on **four channels**, and each exists because the others
miss something:

1. **Full-text search** (`search_vector @@ websearch_to_tsquery`) — the exposure a
   logged-in user can actually exploit against `/jobb`. It lexemes, so it finds
   `Fagerberg, Magnus` when you search `Magnus Fagerberg`. The substring scan
   cannot.
2. **Substring over `title` / `description` / `raw_payload`** — it finds the
   identifier *as you typed it*, including forms Postgres's parser will not lexeme
   the same way.
3. **Substring over `company_name`** — and this one is not redundant. An *enskild
   firma*'s company name IS a natural person's name, `search_vector` is built from
   title + description only (so channel 1 cannot see it), and `raw_payload` is
   NULLed 30 days after publication (so channel 2 loses it for most of the corpus).
   Without channel 3, every ad older than a month would report no match while her
   name sat in plaintext in a column we scan.
4. **Exact match on `organization_number`** — when the identifier is an org.nr.
   Forced by the same 30-day logic as channel 3: after the `raw_payload` purge,
   the materialised `organization_number` column (#841) is the ONLY place a sole
   trader's org.nr — which is her personnummer — survives in the row.

The **cascade surfaces** are matched separately and reported per surface:
`recent_job_searches.q` (word boundary — see below) + `employer_list` (exact
org.nr), `saved_searches.criteria` + `saved_searches.name`,
`applications.snapshot_company` / `snapshot_title` / `snapshot_description` /
`snapshot_url` (retained, Art. 17(3)(e)), `applications.manual_company` /
`manual_title` / `manual_url`, `company_watch_criteria.label`, the CV's
plaintext metadata (`parsed_resumes.source_file_name`, `resume_files.file_name`,
`resumes.name` / `latest_role` / `top_skills` — reported as `resumeMetadata`),
and — structurally, by foreign key — `applications.job_ad_id`. The full
column-by-column map is `ErasureCascadeRegistry.Channels`; this list is written
FROM it, and the build breaks if a searched column has no channel.

The union **over-matches on purpose**: a false positive costs you a second look,
a false negative costs a false confirmation to a named person.

> ⚠ **`recent_job_searches` is the exception, and deliberately so.** It matches on
> a WORD BOUNDARY, not a naked substring, because those rows are hard-deleted with
> no per-id confirmation ceremony. **The looseness of a match must be inversely
> proportional to the strength of its review gate.** Erasing `anna` must not delete
> another user's search for `marianna`. You still see every matched string in the
> dry run before anything goes.

> ⛔ **SEVEN COLUMNS ARE NOT SEARCHED, AND YOU MUST SAY SO.**
> `applications.cover_letter`, `application_notes.content`, `follow_ups.note`,
> `parsed_resumes.raw_text`, `parsed_resumes.parsed_content_enc`,
> `resume_versions.content_enc` and `resume_files.content` — the notes, the cover
> letters, **and the CV in all three of its stored shapes, the uploaded file
> included** — are encrypted at rest under a **separate key per user**. We cannot
> scan them without decrypting every user's private texts and documents, and **we
> will not do that** — it would build a read-everyone's-content capability
> permanently, with no lawful basis toward those other users. The response carries
> this in `couldNotSearch`, on **every** outcome. **We refuse the mechanism, not
> the person:** the reply offers her the escalation route, and
> `applicationsReferencingMatchedAds` already tells her how many applications were
> written to the ads we erased.

> ⚠ **What matching does NOT do: it does not de-obfuscate.** Searching
> `anna@acme.se` will not find an ad that reads `anna(at)acme.se` — the substring
> channel compares the string you gave it, and FTS lexemes the obfuscated form
> differently. **What serves that population is her NAME**, which sits in the body
> in plain words. This is the single most important reason to run the dry run once
> per identifier: her address, her number, **and her name**. (An earlier version of
> this runbook credited the substring channel with catching obfuscation. It does
> not. Crediting the wrong mechanism for a control's coverage is the same defect
> class this whole issue is about, so it is corrected here rather than quietly.)

The response — the shape below is written FROM `EraseRecruiterAdsResponse` and
its `ErasureOutcome`; if they disagree, the TYPE is right and this document is
stale:

```jsonc
{
  "requestId": "…",              // correlates with the audit_log row
  // The five outcome words, DERIVED from the counts (the type forbids passing one):
  //   NoMatchInSearchableSurfaces | DryRun | AdsErased | CascadeErasedOnly | NothingErased
  // ("NoMatchingDataHeld" does not exist — it was abolished because "we hold no
  //  data" is the claim we can never verify. Template C is keyed on
  //  NoMatchInSearchableSurfaces.)
  "outcome": "DryRun",
  "dryRun": true,
  "matched": {                   // EIGHT surfaces — one per ErasureChannel
    "jobAds": 3, "recentJobSearches": 1, "savedSearches": 2,
    "applicationSnapshots": 4, "manualAdEntries": 1, "companyWatchCriteria": 0,
    "resumeMetadata": 1, "applicationsReferencingMatchedAds": 2
  },
  "erased": { "jobAds": 0, "recentJobSearches": 0, "savedSearches": 0,
              "applicationSnapshots": 0, "manualAdEntries": 0, "companyWatchCriteria": 0,
              "resumeMetadata": 0, "applicationsReferencingMatchedAds": 0 },
  "couldNotSearch": {            // ALWAYS present, on every outcome
    "reason": "Encrypted at rest under a per-user key envelope (… Form A in-place text: the application notes, follow-up notes, cover letters and the raw CV text; Form B encrypted shadows: the structured CV content; Form C sealed binary: the uploaded CV FILE itself) …",
    "columns": [
      "application_notes.content", "applications.cover_letter", "follow_ups.note",
      "parsed_resumes.parsed_content_enc", "parsed_resumes.raw_text",
      "resume_files.content", "resume_versions.content_enc"
    ]
  },
  "matches": [                   // ← THE ADS. This is what you review.
    { "jobAdId": "…", "externalId": "…", "title": "Backend-utvecklare",
      "matchedChannel": "Description",
      "matchedExcerpt": "…kontakta ansvarig rekryterare Magnus Fagerberg på…" }
    // An org.nr request shows matchedChannel: "OrganizationNumber" and the
    // normalised org.nr as the excerpt — suffixed "(personnummer-format)" when
    // personnummer-shaped. That is HER OWN submitted identifier echoed back.
  ],
  // The hard-deleted rows' evidence: the q term, or "arbetsgivarfilter: <org.nr>"
  // for an employer-only search (q = null) — flagged when personnummer-shaped.
  "matchedRecentSearchTerms": ["magnus fagerberg"],
  "erasedExternalIds": []
}
```

**Read `matched` against `erased`. The gap is not a bug — it is the disclosure.**
See §3.

**Also read the count at `matched.applicationSnapshots`.** These are applicants
whose saved record of the ad will show "[raderad]" (a tombstone) until we ship
the fallback-to-snapshot fix (issue #D3, separate lane). The operator and
requester should both know: erasing this ad degrades the preserved-ad display for
that many applications, temporarily. **This is disclosed rather than hidden.**

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
| `recent_job_searches` (saved search history) | ✅ **Yes**, hard-deleted | If a user searched the recruiter's name — or filtered on her org.nr in the employer filter (`employer_list`; an employer-only search has `q = NULL` and is matched exactly on the normalised org.nr) — that identifier is sitting in her search history. Auto-capture rows have no audit-trail dignity, the cap-20 list rebuilds on her next search, so the user loses nothing. |
| `saved_searches.criteria` (user's saved filters) | ⚠️ **Not automatically — a HUMAN erases it, inside the Art. 12(3) month** | **HER RIGHT APPLIES. Do not tell her otherwise.** One row carries two data subjects under two bases: the user's own criteria rest on Art. 6(1)(b) (our contract with *her*), but **the recruiter's name sitting inside those criteria does not** — 6(1)(b) requires the data subject to be a **party** to the contract, and the recruiter is party to nothing. That processing rests on **Art. 6(1)(f)**, which **Art. 21(1) reaches**. So her objection fires and Art. 17(1)(c) is available. We do not attempt the "compelling legitimate grounds" override: keeping her name in another user's filter is a convenience, and a saved search is recreatable in seconds. **We owe her erasure and we honour it in full** — we simply do not AUTOMATE it, because `SoftDelete()` would leave `criteria` in the row (it hides, it does not erase) and stripping the term is not always constructible. **A human does it, with the affected user in the loop. That is a mechanism choice, never a refusal.** |
| `applications.manual_company`, `manual_title`, `manual_url` (manually tracked applications) | ⚠️ **Not automatically — a HUMAN erases it** | A user may have pasted or typed a recruiter's name/contact into these fields when tracking an application she found outside Platsbanken. That is the recruiter's personal data, and her right reaches it (6(1)(f) → Art. 21(1)) — but a system does not silently rewrite a person's private record of her own job hunt. Reported; a human handles it with that user. *(URL can carry a name: `linkedin.com/in/magnus-fagerberg`, company contact page, etc.)* |
| `company_watch_criteria.label` (nickname for a watch predicate) | ⚠️ **Not automatically — a HUMAN erases it, inside the Art. 12(3) month** | A user might name a watch *"IT jobb med Magnus"*. Unlike `saved_searches`, the label is **optional and nullable** — the criterion is its codes (industry + municipality), and the label is just a UI nickname. **`UpdateLabel(null)` is always constructible and lossless.** We report the count; a human nulls the label with zero complexity. Same mechanism as `saved_searches`: report, human decides. *(This column was found by enforcing the cascade registry at the EF model level; it is why the guard breaks the build.)*  |
| `applications.cover_letter`, `application_notes.content`, `follow_ups.note`, `parsed_resumes.raw_text` / `parsed_content_enc`, `resume_versions.content_enc`, `resume_files.content` (**the CV, all three stored shapes**) | ⚠️ **NOT SEARCHED** — disclosed in response | A user may well have written *"Ringde Magnus Fagerberg"* in her own note — or named the recruiter she wrote to in her CV. That is the recruiter's personal data, and her right reaches it (6(1)(f) → Art. 21(1)). **But we cannot search it.** These seven columns are encrypted at rest under per-user keys (Forms A, B and C — the uploaded CV file included). A `LIKE` search would require decryption of every row under every user's key — feasible for a handful of Art. 17 requests per year but not feasible via a background job. **We hold it, we cannot scan it, and we say so explicitly in the reply.** Erase via a human, if the subject and affected user both consent. |
| `applications.snapshot_contacts` (the frozen recruiter contact block, #842 Tier A) | ✅ **Yes**, surgically | ITS OWN surface (`ApplicationSnapshotContacts`), never folded into the body columns below — one surface, one disposition, one honest Matched−Erased meaning (T2 CTO 2026-07-16). The contact block is HER data whose follow-up purpose is spent at the erasure request; 17(3)(e) retains the applicant's aktivitetsrapport spine, not the recruiter's phone number. `Application.EraseAdSnapshotContacts()` removes ONLY the contacts and leaves the applicant's record intact — durable by construction, the funnel never rewrites a snapshot. |
| `applications.snapshot_company` / `snapshot_title` / `snapshot_description` / `snapshot_url` | ❌ **No** | The applicant's frozen record of an ad she applied to (ADR 0086 exists precisely so it outlives the ad). **And the ground is STRONGER for the company name than for the body:** a Swedish jobseeker must file an *aktivitetsrapport* to Arbetsförmedlingen **naming the employer**. The company name is the **spine** of her own legal record; the ad body is its colour. Ground: Art. 17(3)(e). **Klas's to affirm — STOPP-3, still open.** We **search and report** all four — `snapshot_url` included, a URL path carries names — precisely because we do not erase them: *a legal ground asserted over a population we never counted is a ground asserted over a silence.* |
| CV metadata: `parsed_resumes.source_file_name`, `resume_files.file_name`, `resumes.name` / `latest_role` / `top_skills` (`resumeMetadata`) | ⚠️ **Not automatically — a HUMAN erases it, with the CV's owner in the loop** | The PLAINTEXT text around a user's CV: the uploaded file's name (twice — two tables, same file), the CV's own name (typed via rename), and the denormalised role/skill projections. "Ansokan_Magnus_Fagerberg.pdf" is not exotic — the repo already masks personnummer out of file names (#465) precisely because users type arbitrary text there. Searched and reported; a job does not silently rename a user's own files. The CV BODY is `couldNotSearch`, not this row. |
| Backups / WAL / PITR | ⚠️ **Unstated** | An `UPDATE` does not remove the old row version from disk until `VACUUM`, and copies remain in WAL and backups. **Do not make any statement to the data subject about backups.** The retention window is not yet decided (**STOPP-4**). Do not invent one. |

**If `matched.savedSearches > 0`, `matched.companyWatchCriteria > 0`,
`matched.manualAdEntries > 0` OR `matched.resumeMetadata > 0`, the reply must
disclose it — template B2. If `matched.applicationSnapshots > 0`, the reply must
disclose the Art. 17(3)(e) retention — template B3.** A matched surface the reply
never mentions is a search whose result never reached her; the gate lists every
human-handled surface, and `resumeMetadata` was dropped on the floor here for one
round (round-5 B5-3.4).

---

## 4. Reply templates (Klas-approved 2026-07-14 — he sends)

Swedish, "du", no exclamation marks, no emoji, no em-dash (CLAUDE.md §10).
**Never claim more than was actually done.** Do not add sentences about backups
(STOPP-4). **The templates are keyed on the five REAL `ErasureOutcome` members**
— `NoMatchInSearchableSurfaces | DryRun | AdsErased | CascadeErasedOnly |
NothingErased` — and the surface list they describe is
`ErasureCascadeRegistry.Channels`. `DryRun` has no template: nothing was done,
so there is nothing to tell her yet. An earlier version of this section keyed
template C on `NoMatchingDataHeld`, an outcome word the code had abolished, and
claimed we searched the notes — columns we structurally cannot read. **A reply
that asserts a search that never ran is the finding that opened #842; these
templates are derived from the types so that cannot recur.**

**MANDATORY CLOSING — appended to EVERY reply** (`couldNotSearch` ships on every
outcome, so every reply names what we could not look at):

> Det finns uppgifter vi inte kan söka igenom: användarnas personliga brev, egna
> anteckningar och uppladdade CV-filer är krypterade med en separat nyckel för
> varje användare, och vi kan inte läsa dem. Om du vet att dina uppgifter
> förekommer i en specifik ansökan eller ett specifikt CV, hör av dig, så
> hanterar vi det tillsammans med den berörda användaren. Du har rätt att lämna
> klagomål till Integritetsskyddsmyndigheten och att vända dig till domstol.

**A. Acknowledgement, on receipt.**

> Tack, vi har tagit emot din begäran om radering. Vi återkommer med besked om
> vilken åtgärd vi har vidtagit inom en månad från det att begäran kom in. Du når
> oss under tiden på <kontaktadress>.

**B1. Completion — `AdsErased`.**

> Vi har tagit bort hela annonsen ur våra system, och vi hindrar att den hämtas in
> igen. Annonstexten, sökindexet och de uppgifter vi har härlett ur annonsen är
> borttagna hos oss. Vi kan inte ta bort annonsen hos Arbetsförmedlingen, som är
> den som har publicerat den. Vill du att uppgifterna tas bort även där behöver du
> vända dig till Arbetsförmedlingen eller till arbetsgivaren som publicerade
> annonsen.

**B2. Addition — any human-handled surface matched
(`matched.savedSearches > 0` OR `matched.manualAdEntries > 0` OR
`matched.companyWatchCriteria > 0` OR `matched.resumeMetadata > 0`).** Append:

> Dina uppgifter förekommer också i innehåll som användare själva har skrivit,
> till exempel en sparad sökning, en egen anteckning om en ansökan eller ett CV:s
> namn eller filnamn. Din rätt till radering gäller även där. De uppgifterna tas
> bort manuellt, tillsammans med den användare det gäller, inom en månad från det
> att din begäran kom in. Vi hör av oss när det är klart.

**Do NOT write that her objection does not cover it.** It does. Art. 6(1)(b)
requires the data subject to be a *party* to the contract, and she is not a party
to ours — so the processing of her name inside a user's saved search or watch label
rests on Art. 6(1)(f), which Art. 21(1) reaches. Telling her otherwise would be a
false statement to a data subject about her own rights (Art. 12(4)), which is the
exact class of thing this whole issue exists to stop us doing. **"Our code cannot
do it" is not a legal ground. It never was.**

**B3. Addition — `matched.applicationSnapshots > 0` (Art. 17(3)(e) retention).**
Append:

> Ett antal användare har en sparad kopia av en annons de har sökt. Kopian är en
> del av deras eget underlag inför till exempel aktivitetsrapportering till
> Arbetsförmedlingen, och den behåller vi med stöd av artikel 17.3 e i
> dataskyddsförordningen. Du har rätt att invända mot den bedömningen hos oss,
> hos Integritetsskyddsmyndigheten eller i domstol.

**B4. Addition — `erased.applicationSnapshotContacts > 0` (#842 Tier A; substance
bound by T2 CTO 2026-07-16, wording rides Klas).** Append:

> Ett antal användare hade annonsens kontaktuppgifter sparade i sin egen kopia av
> annonsen. De kontaktuppgifterna är borttagna ur kopiorna. Själva annonstexten i
> användarnas kopior behåller vi med stöd av artikel 17.3 e i
> dataskyddsförordningen, som en del av deras eget underlag.

**C. `NoMatchInSearchableSurfaces`.** Says what we searched — and never claims
we searched what we cannot read (the mandatory closing carries that half):

> Vi har sökt igenom annonserna, användarnas senaste och sparade sökningar,
> bevakningar, egna annonsuppgifter och CV-uppgifter som inte är krypterade. Vi
> hittade inga uppgifter som matchar det du har uppgett.

**D. `CascadeErasedOnly`** — no ad matched, but cascade rows were erased. *"Vi
har tagit bort hela annonsen" would be a false statement here; this outcome word
exists so it cannot be sent.* **Each sentence is GATED on its own counter (T2
CTO 2026-07-16): a contacts-only clear must not claim search-history deletion,
and vice versa.**

Base:

> Ingen annons matchade det du har uppgett.

If `erased.recentJobSearches > 0`, append:

> Dina uppgifter förekom i användares sökhistorik. De posterna har vi tagit bort.

If `erased.applicationSnapshotContacts > 0`, append B4.

(+ B2/B3 if their gates fire, + the mandatory closing.)

**E. `NothingErased`** — we matched data and destroyed none of it (the operator
confirmed none of the reviewed ads, or everything matched lives on
human-handled surfaces). Pick the sentence that is true for the case at hand:

> Vi har hittat uppgifter som matchar det du har uppgett. Inget av det har
> tagits bort automatiskt. [Granskningen visade att träffarna inte avser dig. /
> Allt som matchade ligger i innehåll som hanteras manuellt tillsammans med
> berörda användare.] Vi återkommer med besked inom en månad från det att din
> begäran kom in.

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
- `docs/reviews/2026-07-14-842-pr2-employer-list-cto.md` — the CTO ruling that
  made org.nr/personnummer a first-class Art. 17 identifier (local, gitignored
  per ADR 0072).
- `docs/research/2026-07-13-842-erasure-evidence-pack.md` — the proven facts and
  the measurements quoted above (local, gitignored per ADR 0072).
- `docs/runbooks/gdpr-processing-register.md` — Art. 30 register.

### Operational prerequisite

`AuditPseudonymization:PepperBase64` must be set (gitignored
`appsettings.Local.json` locally, managed secret in ops). **The application will
not start without it** — deliberately: an HMAC under an absent key looks protected
while being trivially reversible, and a control that only appears to work is the
entire subject of this issue. Generate one with `openssl rand -base64 32`.
