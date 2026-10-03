# ADR 0106 — Recruiter-PII erasure contract: ingest minimisation (Art. 25) + provable record removal (Art. 17)

**Date:** 2026-07-13
**Status:** Accepted
**Decision-makers:** senior-cto-advisor (BOUND — `docs/reviews/2026-07-13-842-erasure-contract-cto.md`,
2026-07-13, verdicts V1–V20; CC gave no own recommendation per CLAUDE.md §9.2, the choice being
multi-approach); Klas Olsson (six open STOPP items — see **Open (Klas)** below; none of them blocks
the technical bind, two of them block the DPIA signature and the `v*` tag — ⚠ **as of 2026-07-26 (#845)
the `v*` half no longer holds: the gate is LIFTED, see the Amendment 2026-07-26 at the end of this
file. The DPIA half stands.**).
**Supersedes:** none.
**Amends:** ADR 0032 (JobTech integration — `:540` released the prod gate on three controls that do
not work; `:240-242`, `:380-382`, `:404-413`, `:524`, `:530-536`), ADR 0024 (`:467-472` — the
canonical Art. 17 cascade registry, which lists only `raw_payload` and never `job_ads.description`),
ADR 0049 (`:148-184` — field encryption declined partly to protect an Art. 17 mechanism that was
already structurally broken), ADR 0087 D8(a) (the "PII stripped at ingest" pillar, withdrawn; the
replacement text is drafted verbatim at `0032:1119-1122`). Those are **dated in-file amendments for
drift**; this ADR is the **new mechanism** they point *forward* to.
**Related:** ADR 0071 (no LLM/AI inference in the product — binds the redactor to determinism; see
D6), ADR 0072 (docs-privacy — this file is 0074+ ⇒ gitignored, local-only, and must **not** appear in
the public `docs/decisions/README.md`, which stops at 0070), ADR 0086 (ad snapshot outlives the ad —
grounds D9), ADR 0090 D5 (HMAC-SHA256 with the server pepper — the house pseudonymisation primitive,
reused by D8), ADR 0045 (perf budgets — the ingest scrub's cost is stated, not litigated), ADR 0065
(SoC / one change-reason per PR — governs the three-PR split). Issue #842 (P1, launch-gate); #843
(test fiction); #845 (retention doc-only); #821/#841 (the `job_ads` migration lane — **not touched by
this ADR**).

> **Lifecycle note.** Authored 2026-07-13 by Claude Code, transcribing the bound CTO ruling
> `docs/reviews/2026-07-13-842-erasure-contract-cto.md` (senior-cto-advisor, DECISION-MAKER per
> CLAUDE.md §9.2) and the evidence it rests on,
> `docs/research/2026-07-13-842-erasure-evidence-pack.md` (HEAD `64e4c654`). No decisions constructed
> by CC — every fork was bound by the CTO; the six items CC could not close are listed as OPEN, not
> resolved. Every number below is measured on the real dev corpus (pack §9, 2026-07-13) and every
> code claim carries a `file:line` from the pack.
>
> **Tense discipline, stated once and honoured throughout.** As of this ADR's date, **only PR1
> (containment + truth) is committed.** Tier A and Tier B are **BOUND but NOT YET SHIPPED.** This
> document therefore describes them in the future tense. A document that describes a control we do
> not yet have is the exact defect this ADR exists to correct; it is not repeated here.
>
> ⚠ **STATUS CORRECTED 2026-07-26 (#845).** The paragraph above is the record of 2026-07-13 and is
> kept as such. **Both tiers have since shipped** — Tier B `269a4603` (2026-07-15), Tier A `daa4b51d`
> (2026-07-17) — and the third technical leg (#D3, the applicant's preserved record) closed with #892
> on 2026-07-17. The body below still reads in the future tense throughout; **read every "will" as
> "does", and see the Amendment 2026-07-26 at the end of this file for the gate.**

---

## Context

### The defect, proven

`RecruiterPiiPurger` **was** the only Art. 17 erasure path for recruiter PII in the product. It
matched rows by jsonb containment on `{"employer":{"contact_email": <email>}}` and nulled
`raw_payload` — nothing else (`RecruiterPiiPurger.cs:31-52`).

The probed key **cannot exist** in any ingested row. Two independent locks, both verified at HEAD:

1. **The wire POCO cannot emit it.** `JobTechEmployer` declares exactly two properties, `name` and
   `organization_number` (`JobTechSearchResponse.cs:125-143`), and `raw_payload` is produced by
   `JsonSerializer.Serialize(hit)` (`PlatsbankenJobSource.cs:238`).
2. **The sanitizer's default-deny allowlist drops it anyway** (`JobTechPayloadSanitizer.cs:62-64`,
   `:107-108`), at the single production call site that is also the only write path into
   `raw_payload` (`PlatsbankenJobSource.cs:238-250` → `UpsertExternalJobAdCommandHandler.cs:52`,
   `:95-97`).

**Measured: 0 of 93 469 ingested ads carry `raw_payload->'employer' ? 'contact_email'`** (pack §9).
`rowsAffected = 0` was the mechanism's *only possible outcome*. It is **100 % vacuous, not
~vacuous** — there are no legacy rows, and no code path distinguished "erased nothing" from "erased
something" (`AdminJobAdsEndpoints.cs:54-61`, `RedactRecruiterPiiCommandHandler.cs:48-50`). The
runbook pre-normalised the only symptom (`recruiter-pii-erasure.md:54-56`, *"rowsAffected = 0 är
OK"*) and then instructed the operator to confirm erasure anyway (`:69`). Same defect class as
#805-3.

### The sanitizer strips the FIELD, not the ADDRESS

This is the finding the whole design turns on, and it is not a gap that crept in — it is the design.
`JobTechPayloadSanitizer` is a **key-name filter that never examines a value**, and it **deliberately
retains every free-text key**: `description`, `description_html`, `description_text`, `text`,
`text_formatted`, `company_information`, `needs`, `requirements`
(`JobTechPayloadSanitizer.cs:33-35`) and `salary_description` (`:55`); values are `DeepClone()`d
unexamined (`:99`).

The address therefore lives where nobody was looking: **`job_ads.description`, plaintext, verbatim**
(`.Trim()` only — `PlatsbankenJobSource.cs:207` → `JobAd.cs:50`/`:156`; no converter, no encryption
— `JobAdConfiguration.cs:19`). And it is **full-text searchable by any authenticated user today**:
`search_vector @@ websearch_to_tsquery('swedish', '<email>')` returns a hit (proven against real
Postgres, pack §1.5; the FTS branch runs for any non-blank `q` —
`JobAdSearchComposition.cs:137,175-191`). **The recruiter's NAME is independently searchable the same
way**, via ordinary word lexemes.

The code knew. `PlatsbankenJobSource.cs:199-207` carries a SECURITY-NOTE conceding the free-text PII
risk and deferring the mitigation to *"faktiskt klagomål"*. **An Art. 17 request IS that complaint,
and the mitigation it defers to does not exist.**

### The corpus (measured 2026-07-13, dev Postgres 18.3 — there is no prod, so this is the whole world)

| Measurement | Value |
|---|---|
| `count(job_ads)` | **93 469** |
| rows with `raw_payload->'employer' ? 'contact_email'` (what the purger probed) | **0** |
| `audit_log` rows for `%RecruiterPiiRedact%` (has the endpoint ever been called?) | **0** |
| ads whose `description` contains `@` | **27 506 (29,4 %)** |
| ads whose `description` matches a well-formed email regex | **27 077** |
| ads with a phone-shaped number in `description` | **13 134** |
| ads using **textual obfuscation** (`(at)`, `[at]`, `snabel-a`, `… at … punkt …`) | **17** |

Two consequences of that table. **The free-text contact surface is not an edge case — it is roughly
3 ads in 10.** And **the endpoint has been called 0 times**, so no data subject has yet received a
false confirmation: there is no notification duty, which is precisely why containment was cheap and
was not a reason to wait.

### The two facts that killed the naive fix

**F-A — durability.** The nightly full backfill (`SyncPlatsbankenSnapshotJob`, `0 2 * * *`) and the
10-minute stream (`SyncPlatsbankenStreamJob`, `*/10 * * * *`) funnel into the same command, which has
**no unchanged/hash short-circuit** and always calls `UpdateFromSource` — which reassigns
**unconditionally**: `Title`, `Description`, `Url`, `ExpiresAt`, `RawPayload` (`JobAd.cs:155-159`),
and then re-runs the extractor (`UpsertExternalJobAdCommandHandler.cs:110`). ⇒ **Any one-shot
redacting UPDATE is undone within ≤24 h (snapshot) or ≤10 min (stream) for any ad still in the
feed.** Durability requires an ingest-time control or a persistent ledger consulted on every write.
(ADR 0032:1094-1097 explicitly **forbids** "fixing" this by suppressing the nightly rewrite.)

**F-B — completeness.** `job_ads.extracted_terms` is **C#-written, not generated**
(`UpsertExternalJobAdCommandHandler.cs:121-123`; `JobAdConfiguration.cs:191-196` — plain jsonb, no
`HasComputedColumnSql`). It does **NOT** self-heal on a `description` UPDATE (proven by temp-table
probe, pack §2 row 4), and **the recruiter's NAME survives there verbatim** as a `Display`/`MatchedOn`
surface form (`JobAdKeywordExtractor.cs:129-136`), with `extracted_lexemes` (STORED, generated from
it) following. Any option that redacts `description` must also re-run the extractor, or explicitly
accept that residue.

### The law, stated honestly

- **Art. 17(1) is textually UNQUALIFIED.** The *"taking account of available technology and the cost
  of implementation … reasonable steps"* language appears **only in Art. 17(2)**, which governs
  informing **other** controllers about data the controller made public — **not** erasure from the
  controller's own store. ⇒ **There is no legal instrument that lets us soften the promise about our
  own copy.** Any DPIA sentence of the form *"best-effort erasure of unstructured data is the
  accepted posture under Art. 17(2)"* is a misreading.
- **No DPA authority accepts best-effort erasure of free text.** The pack's targeted search (EDPB
  Guidelines 5/2019, EDPB CEF 2025, IMY's erasure page, GDPRhub, enforcementtracker) found **no
  EDPB, IMY or other DPA statement** accepting it. What *is* supported is narrower: residual backup
  copies tolerated **if** put beyond use **and** disclosed (ICO). There is no industry norm to hide
  behind and no regulator decision to calibrate against.
- **Art. 12(3) makes a false completion report an INDEPENDENT breach.** *"The controller shall
  provide information on action taken … within one month"*; if no action, the reasons and the right
  to complain. A mechanism that reports success while erasing nothing manufactures a false statement
  to a data subject — a breach layered **on top of** the Art. 17 failure. That is exactly what
  `recruiter-pii-erasure.md:54-56` + `:69` instructed.
- **Art. 25(2) + Art. 5(1)(c) are the strongest authority for the not-ingest remedy:** *"…by default,
  only personal data which are necessary for each specific purpose … **That obligation applies to the
  amount of personal data collected**, the extent of their processing, the period of their storage
  and their accessibility."*
- **Google Spain (C-131/12) cuts both ways.** It legitimises *"we erase our copy, we cannot erase
  Arbetsförmedlingen's"* — a downstream indexer is a controller for its own processing and can be
  ordered to remove the item *"without … presupposing the previous or simultaneous removal of the
  underlying information from the web page on which it was published."* **And it forecloses** refusing
  a request on the ground that *"annonsen är redan publicerad"* — which is precisely the basis asserted
  at `PlatsbankenJobSource.cs:203`.

---

## Decision

**A two-tier contract. Neither tier is optional. Neither is sufficient. Each is what makes the other
honest.**

### D1 — Tier A (Art. 25, everyone, no request needed, heuristic, DISCLOSED). BOUND, NOT YET SHIPPED. ⚠ **SHIPPED since — `daa4b51d`; date in ADR 0032 §C6 (#845, 2026-07-26).**

**We do not STORE recruiter contact details.** Email addresses and phone numbers are stripped from
the ad body **at ingest**, and replaced by a marker pointing to the canonical ad at
Arbetsförmedlingen. Detection is deterministic and imperfect, **and we say so.**

### D2 — Tier B (Art. 17, on request, PROVABLE, no detector involved). BOUND, NOT YET SHIPPED. ⚠ **SHIPPED since — `269a4603`; date in ADR 0032 §C6 (#845, 2026-07-26).**

**On a valid erasure request we remove the entire ad record** and block its re-import. Nothing is
detected, nothing is estimated, nothing is promised that cannot be verified.

### D3 — Why each tier is what makes the other honest

The cheap design is Tier A plus a redact-on-request path. **It is not honest**, and the reason is
structural, not aesthetic: *a redact-on-request path is a promise made to a named individual who is
watching.* When she asks "is my address gone?", *"we removed what our regex found"* is a factual
assertion by the controller, to the data subject, about her own data, **that the controller knows may
be false.** Art. 17(1) offers no qualifier to hide in and no regulator stands behind the half-promise.

- **Tier A alone ⇒ no honest answer to a request.** The detector's misses have no remedy.
- **Tier B alone ⇒ we keep hoarding contact details for 27 077 ads** whose recruiters will never know
  we exist and will never ask — which is the Art. 25 point the security-auditor called *"the most
  important line in the issue"*.
- **Tier B is the only option whose completeness is PROVABLE**, because it deletes the **CARRIER, not
  the STRING**: `description`, `search_vector` (STORED, recomputes), `extracted_terms`,
  `extracted_lexemes`, `raw_payload` and the seven `raw_payload`-derived columns all go together. No
  recall question, no obfuscation question, no image-embedded question — **and it covers the
  recruiter's NAME**, which no regex can ever reach.
- **Tier A is what makes Tier B's bluntness affordable.** With ingest scrubbing in place we hold no
  *detected* contact detail, so the only requests that ever reach Tier B are the detector's misses:
  the **17 obfuscated ads in 93 469** (0,06 % of the contact-bearing set), image-embedded addresses,
  and name-only requests. Expected Tier B volume is a handful per year. **A backstop that fires
  rarely can afford to be blunt; a primary path that fires constantly cannot.**

### D4 — Where the scrub lives: an aggregate invariant in `JobAd`, not a handler step and not the ACL

`JobAd.Import` and `JobAd.UpdateFromSource` apply the redactor to `title`, `description` and the
`rawPayload` string. The redactor is a pure, deterministic **Domain** service —
`src/Jobbliggaren.Domain/Privacy/RecruiterContactRedactor.cs`, sibling of the existing
`PersonnummerRedactor.cs` (same shape: static, `GeneratedRegex`, string→string, never throws,
idempotent).

The invariant is: **a `JobAd` never holds a detected recruiter contact detail.** Placement in the
aggregate (CLAUDE.md §2.2 — *"aggregates protect invariants in constructors/methods, not handlers"*)
buys four things that machinery would otherwise have to buy expensively:

- **It closes F-A (durability) for free.** The nightly backfill and the 10-minute stream both go
  through `UpdateFromSource`; a scrub inside it is re-applied on **every** rewrite. **No suppression
  ledger. No tombstone column. No migration.** F-A is answered by *placement*, not by machinery.
- **It closes F-B (completeness) for free.** `ApplyExtraction` reads `jobAd.Title`/`jobAd.Description`
  — the **aggregate's** values, not the command's (`UpsertExternalJobAdCommandHandler.cs:121-123`) —
  on **both** the Add and Update paths. Post-D4 those are the scrubbed values, so `extracted_terms`
  derives from clean text and `extracted_lexemes` follows.
- **It structurally kills the #843 test fiction.** `AdminRedactRecruiterPiiTests.cs:164-177` existed
  because `JobAd.Import` could be called directly with a state production cannot reach. After D4 *it
  cannot be* — a hand-seeded un-scrubbed body becomes unconstructible through the aggregate. The bug
  class that hid this defect is closed **by construction**, not by a test convention.
- **It decouples #842 from #841.** The scrub redacts values **in place**; it never nulls
  `raw_payload`. The seven `raw_payload`-derived generated columns (which derive from *structured*
  keys, not free text) are untouched.

`rawPayload` is scrubbed **as text**. The replacement marker contains no JSON-structural character
(`"`, `\`), so redacting a substring inside a string value leaves the document valid — and one test
asserts the scrubbed payload still parses as JSON. The sanitizer's key-allowlist stays exactly as it
is, as defense-in-depth; only its false XML doc changes.

**Cost, stated so nobody re-litigates it:** the scrub runs on every ingest of ~93k ads nightly.
Compiled `GeneratedRegex` over a few KB per ad, in Worker context, with no request-latency budget
(ADR 0045). Negligible.

**Marker text is stored data, not UI chrome.** It is written into `description` by the backend at
ingest. `next-intl`/`messages/sv.json` governs UI copy (CLAUDE.md §5); it does not govern the content
of a source-language Swedish ad body we store. A Swedish literal constant in the redactor is correct
here — recorded so code-reviewer does not flag it as a §5 violation.

### D5 — Detection surface: email + Swedish phone. NO name-NER.

- **Email:** yes (27 077 ads). WHATWG-shaped pattern adapted for in-text scanning — word boundaries,
  trailing punctuation (*"maila anna@acme.se."*), åäö local parts. That adaptation is where false
  positives are born, so it is **measured** (D11), not asserted.
- **Phone:** yes (13 134 ads). Anchored on a leading `0` or `+46` plus 7–11 digits with optional
  separators. The anchor is what keeps salary figures (`35 000`), postal codes (`123 45`) and dates
  (`2026-07-13`) out. An org.nr caught as collateral is a **good** false positive — it is
  minimisation.
- **Person-name NER: NO — a deliberate rejection, not a deferral.** (1) A name in an ad body is not
  by itself a contact detail; it is substantive ad content, and Swedish NER's false-positive surface
  over company, product and place names is real and unmeasured. (2) **Tier B already serves the name,
  and serves it provably** — using a ~90 %-recall model to half-solve a problem a 100 %-certain
  mechanism fully solves is a worse design, not a more thorough one. (3) Introducing Catalyst NER is
  an ADR-level dependency decision that a feature branch must not settle — and this design means it
  does not have to be settled at all.

**The two tiers partition the surface cleanly: the heuristic tier handles the machine-detectable
identifiers; the provable tier handles everything else — names, obfuscation, and image-embedded
addresses.** That partition is why the design needs no ML and still has no hole in its promise.

### D6 — No LLM. ADR 0071 is not amended, and is not even reached.

The redactor needs no inference of any kind, so the question *"does the no-AI rule bind a safety
control?"* does not need an answer. Recorded so a future session does not reopen it: **ADR 0071's
prohibition is scoped to product inference (CV verdicts, match scores), but its rationale —
determinism, explainability, no per-inference cost, no third-country transfer — applies to a PII
redactor with equal or greater force.** A non-deterministic redactor is a *worse* control precisely
because the DPIA must describe what it does. An LLM redactor is refused. The Catalyst-NER question
stays open in principle and unopened in practice.

### D7 — Tier B mechanism: `JobAdStatus.Erased` as an in-place tombstone. **Zero migration.**

`JobAdStatus` is a string-converted SmartEnum (`JobAdConfiguration.cs:45-46`) with **no CHECK
constraint and no PG enum type** (`grep CheckConstraint` across all migrations returns only
`ck_taxonomy_snapshot_meta_singleton`). ⇒ **A fourth status value is a pure code change.**

- **`JobAdStatus.Erased`** — the fourth value. The partial indexes (`WHERE status = 'Active'`) simply
  stop covering the row, which is the desired behaviour.
- **`JobAd.Erase(clock)`** — a domain method with an explicit precondition: clears `Title`,
  `Description`, `Url`, `RawPayload`, `ExtractedTerms`; sets `Status = Erased`; raises
  `JobAdErasedDomainEvent`. `search_vector` (STORED, from `title||description`) recomputes to empty
  **automatically** (proven, pack §1.5); `extracted_lexemes` follows `extracted_terms`; the seven
  derived columns go NULL with `raw_payload` — **which on an erased ad is irrelevant**, because the
  row is excluded from every read path and its facets have no consumer.
- **Every read path already filters `Status == JobAdStatus.Active`** (`JobAdSearchComposition.cs:65`,
  `PerUserJobAdSearchQuery.cs:307,368`, `LookupCompanyQueryHandler.cs:84`, `CompanyWatchScanJob.cs:156`,
  `ListCompanyWatchesQueryHandler.cs:99`, `SuggestJobAdTermsQueryHandler.cs:39`,
  `RefreshLandingStatsJob.cs:49,54`, `BackgroundMatchingJob.cs` (ScanUserAsync's windowed
  candidate query; line anchor dropped after the #751 scope refactor),
  `JobAdSnapshotMissTracker.cs:126,151,170`). A new status is excluded from search, matching,
  watches, suggest, landing stats and miss-tracking **for free, with no new filter anywhere.** The
  single exception is `GetJobAdQueryHandler.cs:14-29` (selects by id, no status predicate) — one
  guard to add, returning **410 Gone** (`ErrorKind.Gone` already exists in the central mapper,
  CLAUDE.md §3).
- **`UpdateFromSource` refuses when `Status == Erased`** ⇒ the handler maps it to
  `UpsertOutcome.Skipped`. **That is the re-import tombstone**, keyed by the existing
  `(source, external_id)` UNIQUE tuple. **It stores no recruiter PII** — only an external id, a
  source and a status.
- **Tombstone, not hard delete.** `applications.job_ad_id` FKs point at the row; a hard delete would
  cascade or break, and would lose the re-import block. #782's hard-delete precedent is a user's own
  application — a different case entirely.

### D8 — Request semantics, matching, and audit

- **Never a bare `rowsAffected: 0`.** The response carries an explicit outcome discriminator:
  `NoMatchingDataHeld` · `AdsErased(count, externalIds[])` · `DryRun(matches[])`. A 404 would say
  "the endpoint is not there"; a 409 says nothing true. The request *was* validly processed — saying
  **what we found and what we did** is verbatim the Art. 12(3) duty. The runbook gets a reply
  template **per outcome**, and *"we hold no data matching this identifier"* finally becomes a
  statement that is **true** when we make it.
- **Matching is fail-safe and two-channel:** FTS (`search_vector @@ websearch_to_tsquery('swedish', …)`)
  **plus** a case-insensitive substring scan over `title`/`description`/`raw_payload` — because FTS
  will not find an obfuscated form, which is exactly the population Tier B exists to serve.
  Over-match, then let a human confirm.
- **Mandatory dry-run.** Erasure is destructive and irreversible on public content. The command
  supports `dryRun`; **the runbook mandates a dry-run before the destructive call.** Same
  propose-and-approve discipline CLAUDE.md §5 already imposes on the CV engine (*"a rule engine never
  rewrites silently"*), applied to the one operation that destroys content for every user.
- **Audit payload, written for the first time.** `audit_log.payload` (jsonb) **already exists**
  (`AuditLogEntryConfiguration.cs:57`); the gap is that `AuditLogEntry.Create` hard-codes
  `payload: null` (`AuditLogEntry.cs:81-92`). ⇒ a **pipeline change, not a migration**. Shape:
  `{ identifierHmac, identifierKind, matchedExternalIds[], erasedCount, dryRun }`. **HMAC-SHA256 with
  the server pepper — md5 is explicitly rejected** (see Alternatives). The `externalIds` are **not**
  PII and are the accountability spine (Art. 5(2)/30). **Failed and rejected requests get an audit
  row too**: `AuditBehavior.cs:35-38` skips audit on `Result.Failure` today, so a rejected request
  leaves no trace it was ever received — a direct Art. 12(3) exposure. Fixed by extending
  `IAuditableCommand` with an opt-in `AuditFailures` (default `false`), set `true` for the erasure
  command — OCP, blast radius exactly one command.
- **TD-75 is CLOSED AS VOID.** Its rationale — *"Email är primär rekryterar-identifier i
  JobTech-payloads"* (`0032:524`) — is not outdated, it is **falsified**: the sanitizer and the wire
  POCO guarantee the email is *never* a structured key in storage. **The rationale is withdrawn, not
  re-scoped.** The command takes a single free-text `identifier`; `kind` survives only as
  operator-supplied audit metadata, **never as a matching switch** (matching is over free text either
  way, so a discriminator that changes the query is a distinction without a difference and a place
  for the next bug to hide).

### D9 — `applications.snapshot_description`: in scope for Tier A, explicitly OUT of scope for Tier B. **RECORDED, not omitted.**

The rights collision (recruiter's Art. 17 vs applicant's evidence) **mostly does not arise**, because
minimisation gets there first:

- **Tier A reaches it for free.** `AdSnapshot.Capture` copies `jobAdData.Description`
  (`CreateApplicationFromJobAdCommandHandler.cs:83-92`) — which post-D4 is already scrubbed. New
  snapshots are clean by construction, and **a test asserts it.**
- **Pre-fix snapshots:** the dev DB has **0 non-null `snapshot_description` rows** (pack §9). The
  backfill covers them at zero cost. There is nothing to trade off.
- **The genuine residual: Tier B does NOT cascade into applicants' frozen snapshots.** ADR 0086
  exists precisely so the snapshot outlives the ad; nulling it would destroy an applicant's own record
  of what she applied to. **The legal ground for keeping it — Art. 17(3)(e), establishment/exercise/
  defence of legal claims — is Klas's to affirm: STOPP-3.**

### D10 — Retroactivity: **backfill all 93 469.** Forward-only is not enough and not free.

The argument *"forward-only is free because there is no prod"* is **overruled**. The dev corpus holds
the **real contact details of ~27 000 real recruiters**, harvested from a live API. GDPR has no "dev
environment" exemption; we are a controller processing real personal data. The absence of a prod
deployment bounds the *harm* and justifies P1-not-P0 — it does not extinguish the holding. And
forward-only leaves a permanent hole on its own logic: **an ad that has left the feed is never
rewritten by the nightly sync, so its `description` keeps the address forever** (the payload purge only
nulls `raw_payload` — `PurgeStaleRawPayloadsJob.cs:93-97` — and never touches `description`, its own
doc's claim notwithstanding; the purge is criterion-based, not a fixed period — rule: ADR 0032
Amendment 2026-07-26 §C2).

Backfill = a one-shot job that loads each ad with a detected contact, re-applies the redactor via the
domain method, and re-runs the extractor. No migration, no schema, no lane. It writes one audit row
with the counts and the extractor-term delta. **Corpus-wide side effect: accepted and disclosed — see
Consequences and STOPP-5.**

### D11 — Measured recall, published in the DPIA. Never a percentage in the public policy. Never a committed gold set.

*"We have no number"* was defensible when measuring was expensive. Since pack §9 it is not.
Protocol (ships in PR2): run the detector over all 93 469 ads; hand-label a random sample of ~200
contact-bearing ads **plus all 17** obfuscated ones; report precision/recall **with the sample size**.
The **DPIA** gets the measured number; the **public privacy policy gets a plain-language limitation
statement, not a percentage** — a number in a policy invites a precision it cannot carry.

**Hard operational rule: the gold set contains real recruiters' real contact details, and this repo is
PUBLIC (ADR 0072).** The labelled set is a **local artefact under `docs/research/` (gitignored) and is
NEVER committed.** Only aggregate metrics ship. A committed gold set would publish the exact PII we are
erasing — the worst possible failure mode of this entire issue, and self-inflicted.

The recall number characterises the *minimisation* control. It is **not load-bearing for the promise** —
Tier B is. That is what lets us publish an honest number even if it is 94 % rather than 100 %.

### D12 — Bound disclosure wording

Substance is **bound**; the Swedish is per CLAUDE.md §10 ("du", no em-dash, no exclamation marks, no
emoji) and final wording rides design-reviewer.

- **Privacy policy (Tier A — a statement about MINIMISATION, never an erasure promise, and it never
  appears in a reply to an Art. 17 request):**

  > "Vi hämtar annonstexter från Platsbanken. Innan en annons sparas tar vi automatiskt bort
  > e-postadresser och telefonnummer ur annonstexten. Kontaktuppgifterna finns kvar i originalannonsen
  > hos Arbetsförmedlingen, som vi länkar till. Borttagningen är regelbaserad och kan missa uppgifter
  > som skrivits på ovanliga sätt eller som ligger i en bild."

- **Erasure contract (Tier B — DPIA + the reply to a data subject):**

  > "Om du begär radering av dina kontaktuppgifter i en annons vi har hämtat tar vi bort hela annonsen
  > ur våra system och hindrar att den hämtas in igen. Vi kan inte ta bort annonsen hos
  > Arbetsförmedlingen, som är den som publicerat den."

**The second sentence of the Tier-B text is mandatory.** Google Spain cuts both ways: the DPIA must
state plainly that erasing our copy does not remove the data from the world — AF still publishes it,
including in its open *Historiska annonser* archive.

### D13 — **#842 takes ZERO migrations.** The #821/#841 lane is not touched, not blocked, not waited on.

| Would-be schema need | Ruling |
|---|---|
| Suppression ledger (email → keep erasing) | **Refused outright** — creates a new PII store (see Alternatives) |
| Tombstone table / `erased_at` column | **Not needed** — `JobAdStatus.Erased` on the existing string column (D7) |
| Audit payload column | **Already exists** — `audit_log.payload` jsonb (D8) |
| Backfill bookkeeping | **Not needed** — one-shot job; the audit row carries the counts |

⇒ **`db-migration-writer` is NOT invoked for #842.** State that in each PR body so its absence does
not read as a skipped mandatory agent (CLAUDE.md §9.2).

---

## Alternatives considered

Taken from the bound CTO ruling §F. Not re-derived, not re-argued, not softened.

| Rejected | Why |
|---|---|
| **Redact-on-request as the erasure contract** | It requires us to tell a named person *"your data is erased"* when we know only that *"our regex found nothing more"*. Art. 17(1) is unqualified; 17(2)'s "reasonable steps" governs *other* controllers, not our own store; no DPA authority accepts best-effort erasure of free text. **Same class of untruth as #824's undercount.** |
| **The suppression ledger** | It stores the recruiter's email **in order to keep erasing it** — the only design in the space that makes us hold **more** of her PII **after** her erasure request than before it. In a public repo it can never be config or a data file. **Refused on principle, not on cost.** |
| **Name-NER at ingest** | ~90 % recall against a mechanism (Tier B) that is 100 % certain. Adds an ML dependency, an ADR-level decision and an unmeasured false-positive surface, to half-solve a problem that is already fully solved. |
| **A role-inbox allowlist** (`ansokan@`, `jobb@` kept) | A heuristic on top of a heuristic, failing in the direction of a real person's address left in a searchable body. The fail-safe posture the security-auditor bound (over-redact rather than under-redact) is incompatible with a boundary rule. See STOPP-1. |
| **md5 in the audit payload** (`recruiter-pii-erasure.md:134`) | Dictionary-reversible in milliseconds. It is not a pseudonym, it is a fig leaf. The house already has HMAC-SHA256(server-pepper) and a binding precedent for it (ADR 0090 D5, #824 condition C1). One house rule. |
| **Forward-only, no backfill** | The dev corpus holds ~27 000 real recruiters' real contact details. "No prod" bounds the harm; it does not extinguish the holding. And a de-listed ad is never rewritten, so forward-only keeps its address **forever**. |
| **Waiting for the #821/#841 migration lane** | Not needed. #842 takes **zero** migrations (D13). Deferring on a constraint that does not exist would have been the most expensive kind of caution. |

---

## Consequences

### Positive

- **The Art. 17 promise becomes provable.** Tier B deletes the carrier, not the string — no recall
  question, no obfuscation question, no image-embedded question, and it reaches the **name**, which no
  regex can.
- **We stop holding what we never needed.** Tier A applies Art. 25(2)/5(1)(c) to ~27 000 ads whose
  recruiters will never file a request. **We are a mirror; a mirror does not need the contact block.**
- **F-A and F-B are answered by placement, not machinery** (D4) — no ledger, no tombstone column, no
  migration, and the #843 test-fiction bug class is closed **by construction**.
- **Zero migrations** ⇒ the most dangerous hotspot (CLAUDE.md §6.5) is untouched and #842 ships
  independently of #841.
- **The audit trail becomes real** — `audit_log.payload` is finally written, rejected requests are
  finally recorded, and the runbook's verification query finally returns something other than NULL.

### Negative — what we accept, in writing

- **We knowingly ship below published SOTA on Tier A.** The SOTA for multilingual PII detection is
  explicitly **hybrid regex + LLM** (RECAP, arXiv 2510.07551 — *"outperforms fine-tuned NER models by
  82 % and zero-shot LLMs by 17 % in weighted F1"*), and ADR 0071 forbids LLM inference in the
  product. This is a legitimate, owned trade-off — **and it is affordable only because Tier B backstops
  it.** Both halves of that sentence go in the DPIA; neither half alone is honest.
- **Strip-all, with no personal-vs-role-inbox distinction.** An ad whose only apply route is *"maila CV
  till X"* **loses that route in our copy**; the user sees the marker and clicks through to
  Arbetsförmedlingen, which carries the contact block and is always current. One extra click.
  (**STOPP-1** — Klas's to accept or override.) *Mitigating fact:* a false positive is **not
  permanent** — the nightly backfill re-scrubs every still-listed ad from a freshly fetched payload, so
  a *fixed detector* restores a wrongly-mangled body on the next sync. Only de-listed ads keep a bad
  scrub.
- **The backfill re-extracts ~27 000 ads ⇒ `extracted_terms` changes ⇒ match grades shift corpus-wide.**
  This is a **correction, not a regression** — the removed tokens (`anna`, `karlsson`, `acme`, phone
  digits) were PII noise, never legitimate match signal. **But it is user-visible and it moves numbers
  Klas has been watching.** The backfill reports its delta and Klas sees it before it is accepted. Do
  not run it silently. (**STOPP-5.**)
- **A redacting UPDATE does not remove plaintext from disk.** The pre-redaction row version survives in
  the heap until VACUUM, and in WAL/replicas/base-backups/PITR (PG18 §24.1.2). **The DPIA must NOT say
  "the data is gone from our systems" on day 1.** We adopt the ICO-shaped two-tier wording: **removed
  from every query path immediately** (MVCC — dead tuples are never returned); **physically reclaimed
  on VACUUM**; **residual backup copies "put beyond use" and expiring on a stated schedule that Klas
  must supply** (**STOPP-4** — CC does not know the Hetzner backup/PITR window and must not invent it).
  **The DPIA cannot be signed, and no `v*` tag can be cut, until that blank is filled.** ⚠ **Amended
  2026-07-26 (#845): these are two instruments, and this sentence is where they were fused.** The `v*`
  clause is superseded by the Amendment 2026-07-26 at the end of this file (the gate is LIFTED). **The
  DPIA clause stands** — STOPP-4 is open and still blocks the DPIA signature. (EDPB CEF 2025
  singles out exactly this gap: controllers who *"do not delete or remove personal data from back-ups at
  all"*.)
- **Erasure is destructive for every user of that ad, and irreversible.** Mitigated by the mandatory
  dry-run (D8), not eliminated. The expected volume (a handful per year) is what makes it affordable —
  that expectation is itself a Tier-A dependency and would be invalidated if Tier A were ever removed.
- **Two Swedish-literal strings now live in Domain code** (the marker text), outside
  `messages/sv.json`. Correct here (D4) but a named exception a reader must know about.

---

## Open (Klas)

The six items the CTO escalated. **All six are OPEN as of 2026-07-13.** Reproduced verbatim from the
bound ruling §D.

> **STOPP-1 (PRODUCT — V8).** Tier A strips **every** email and **every** phone number from **every** ad
> body: 27 077 ads with an email, 13 134 with a phone. No exception for company application inboxes
> (`ansokan@acme.se`), because any exception rule is a heuristic that leaves real people's addresses in a
> searchable body some fraction of the time. **The cost:** an ad whose only apply route is "maila CV till
> X" loses that route *in our copy*. The user sees a marker and clicks through to the ad at
> Arbetsförmedlingen, which carries the contact block and is always current.
> **Question for Klas:** accept the extra click as the price of not storing recruiters' contact details
> at all? Or do you want a role-inbox allowlist, knowing it will sometimes leak a personal address?
> *(CTO binds strip-all. Overriding this is a legitimate product call — but the override buys back one
> click at the cost of a rule we will sometimes get wrong in the direction of a real person.)*

> **STOPP-2 (LEGAL — Q2).** Does *"annonsen är redan publicerad hos Arbetsförmedlingen"* defeat an Art.
> 17 request against **our** copy? Google Spain (C-131/12) says **no** — we are an independent controller
> of our own copy. The Art. 6(1)(f) basis asserted at `PlatsbankenJobSource.cs:203` leans on exactly that
> defeated argument.
> **CTO has already removed the code's dependence on the answer** (Tier B means we never need to refuse a
> request, and Art. 5(1)(c) minimisation is independent of the legal basis anyway). But **the DPIA
> sentence is Klas's**: do we affirm that we honour Art. 17 against our copy regardless of AF's
> publication? *(CTO's strong recommendation: yes, affirm it. Refusing on "it's already public" is the one
> posture Google Spain forecloses outright.)*

> **STOPP-3 (LEGAL — V13).** Tier B removes the ad but does **not** reach applicants'
> `applications.snapshot_description` (ADR 0086 exists so the snapshot outlives the ad). CTO binds the
> technical scope: **snapshots are out of Tier B, recorded, not omitted.** The **legal ground** is Klas's
> to write: Art. 17(3)(e) (establishment/exercise/defence of legal claims — the applicant's own record of
> what she applied to). **Question:** is 17(3)(e) the ground you want in the DPIA, or another?

> **STOPP-4 (FACT CC CANNOT KNOW — Q18).** The Tier-B promise needs an honest backup tail: removed from
> every query path immediately (MVCC), physically reclaimed on VACUUM, residual copies in WAL/backups
> "put beyond use" (ICO) and **expiring on a stated schedule**. **CC does not know the Hetzner-phase
> backup/PITR retention window and must not invent it.** The DPIA has a blank here and **cannot be signed,
> nor a `v*` tag cut, until Klas fills it.**
> ⚠ **Amended 2026-07-26 (#845): two instruments, fused in one sentence — the same fusion as `:480`.**
> The `v*` clause is superseded by the Amendment 2026-07-26 at the end of this file (the gate is
> LIFTED). **The DPIA clause STANDS: STOPP-4 is open and still blocks the DPIA signature.**
> (EDPB CEF 2025 singles out exactly this gap: controllers who
> "do not delete or remove personal data from back-ups at all".)
> Recipient inventory (Art. 19) is otherwise **near-trivial for us** — no external index, no analytics, no
> exports, no sub-processors for ad text. The only internal recipients are the applicant snapshot
> (STOPP-3) and possibly `recent_job_searches.q` / `saved_searches.criteria`; **PR3 queries the DB to
> establish whether any such row exists and cascades if so.**

> **STOPP-5 (VISIBILITY — V12).** The backfill re-extracts ~27 000 ads ⇒ `extracted_terms` changes ⇒
> **match grades shift corpus-wide.** It is a correction (the removed tokens were PII noise, never
> legitimate signal), but it is user-visible and it moves numbers Klas has been watching. **The backfill
> reports its delta and Klas sees it before it is accepted.** Do not run it silently.

> **STOPP-6 (GATE).** The launch gate **stays closed until PR3 lands.** PR2 alone (ingest scrub) makes us
> hold far less, but leaves no working Art. 17 path for the detector's misses. **Confirm: no `v*` prod tag
> until Tier B ships.**
>
> ⚠ **RESOLVED 2026-07-26 (#845): the gate is LIFTED by Klas decision.** STOPP-6 and its two later
> amendments (A3–B2 below, and the 2026-07-14 (d) re-amendment) are the record of what the gate was,
> not what it is. **The Amendment 2026-07-26 at the end of this file is the operative entry.**

---

## Implementation

Three PRs, one issue. **No TDs, no deferrals, no migrations.** The split is SoC — one change-reason per
PR (ADR 0065) — not scope-shedding; CLAUDE.md §9.6's default is fix-in-block and *"scope discipline /
+Xh"* is not a legitimate reason to defer any of it.

| PR | Scope | Status |
|---|---|---|
| **PR1 — containment + truth** | Endpoint → **501** with a truthful problem detail (endpoint-local status, **no new `ErrorKind`** — same precedent CLAUDE.md §3 sets for 401). **Delete** `RecruiterPiiPurger` + `IRecruiterPiiPurger` + the `RedactRecruiterPii` command/handler (dead code that impersonates a safety control is worse than no code). **Keep the route** — ADR 0024's cascade registry cites it as *the* erasure path, so a 501 with a true reason is a loud, discoverable statement of the defect, whereas removing the route makes the registry's reference silently dangle. **Fail loud, not silent.** Rewrite the test fiction (`AdminRedactRecruiterPiiTests.cs:164-177`, #843); keep the 401/403 auth tests — the one part of this feature that was never broken. Runbook: pull the false confirmation (`:54-56`, `:69`) and the broken manual SQL (`:105-117`). Dated in-file amendments to ADR 0032/0024/0049 + source-doc truth-sync (`JobTechPayloadSanitizer` XML doc, `PurgeStaleRawPayloadsJob:18-20`, `PlatsbankenJobSource:199-207` SECURITY-NOTE). This ADR. Local-only: DPIA §8, Art. 30 register, ADR 0087 D8(a) withdrawal. | **DONE (committed)** |
| **PR2 — Tier A: the Art. 25 ingest control** | `Domain/Privacy/RecruiterContactRedactor.cs` (deterministic, `GeneratedRegex`, email + Swedish phone, sibling of `PersonnummerRedactor`, idempotent, never throws). **Aggregate invariant** in `JobAd.Import` + `JobAd.UpdateFromSource` over `title`/`description`/`rawPayload` (D4 ⇒ F-A and F-B closed by placement). **Backfill job** over all 93 469 ads + re-extraction; one audit row with counts and term-delta (D10, STOPP-5). **Tests through the production write path** (harness exists — pack §4.4): WireMock JobTech JSON with a recruiter email in `description.text` → **real** `PlatsbankenJobSource` → **real** sanitizer → **real** `UpsertExternalJobAdCommandHandler` → **real** Postgres. Assert: `description` clean · `search_vector @@ websearch_to_tsquery('swedish', <email>)` returns **0 rows** · `extracted_terms` free of the address tokens · **a second upsert (resync) does not restore it** · the scrubbed `raw_payload` still parses as JSON · a new `AdSnapshot` inherits the scrub. Recall/precision measurement (D11) — **gold set local only, never committed.** Privacy-policy disclosure (D12); `messages/sv.json` is a hotspot, coordinate. | **SHIPPED 2026-07-17** (`daa4b51d`) — status corrected 2026-07-26, #845 |
| **PR3 — Tier B: provable erasure on request (lifts the launch gate)** | `JobAdStatus.Erased` (**zero migration**) · `JobAd.Erase(clock)` + `JobAdErasedDomainEvent` · `UpdateFromSource` refuses on `Erased` ⇒ the re-import tombstone (`UpsertOutcome.Skipped`) · `GetJobAdQueryHandler` → **410 Gone** · `EraseRecruiterAdsCommand(identifier, dryRun)` with two-channel fail-safe matching (FTS **+** substring), **mandatory dry-run**, explicit outcome discriminator (D8) — **removes the 501**. Cascade to `recent_job_searches.q` / `saved_searches.criteria` if the DB query finds any (STOPP-4). Audit: `AuditLogEntry.Create` payload (column exists) · HMAC-SHA256(pepper), **not md5** · `IAuditableCommand.AuditFailures` opt-in. **TD-75 closed as void.** Runbook rewritten: dry-run → confirm → erase → reply template per outcome. Tests: erase → not FTS-findable → **resync does not resurrect** → detail returns 410 → applications FK intact → the tombstone stores no PII. | **SHIPPED 2026-07-15** (`269a4603`) — status corrected 2026-07-26, #845 |

**Mandatory agents (CLAUDE.md §9.2):** `security-auditor` (re-review, all three PRs) + `dotnet-architect`
(the aggregate-invariant placement, D4) + `code-reviewer` (>5 files) + `test-writer` (new domain types).
**`db-migration-writer` is NOT invoked — there is no migration (D13).** State that in each PR body so its
absence does not read as a skipped gate.

**Standing rule this ADR carries into CLAUDE.md §7 (#843, bound by the CTO as V20):**

> *"Tests for ingest-derived or job-derived state MUST construct that state through the production write
> path (real ACL, real sanitizer, real `Import`/`UpdateFromSource`). Hand-seeding a persisted column that
> production writes only through a funnel is forbidden — a test that can reach a state production cannot
> reach proves nothing about production."*

D4 enforces this **structurally** for `JobAd`: once the invariant lives in the aggregate, the fiction is
not merely forbidden, it is unwritable. **A convention a reviewer must remember is weaker than an
invariant the compiler enforces.**

---

## References

- `docs/reviews/2026-07-13-842-erasure-contract-cto.md` — the bound CTO ruling this ADR transcribes
  (V1–V20, STOPP-1..6, §F rejections).
- `docs/research/2026-07-13-842-erasure-evidence-pack.md` — the evidence: §1 the defect with `file:line`,
  §2 the surface inventory, §5 the falsified doc claims, §6 the law and the detection ceiling, §9 the
  measurements against the real corpus.
- `docs/reviews/2026-07-12-824-dpia-archived-ad-security.md` — the security-auditor's binding
  test-fiction rule (`:139-141`), the fail-safe posture (`:132-134`), and the P1-not-P0 severity call
  (`:143-148`).
- **GDPR** Art. 5(1)(c)/(d)/(e), 5(2), 12(3), 17(1)/(2)/(3)(e), 19, 24, 25(2), 30 — https://gdpr-info.eu
  (accessed 2026-07-13).
- **CJEU C-131/12** *Google Spain* (13 May 2014) — controller of its own copy; delisting without removal
  at source. Cuts both ways (D3, STOPP-2).
- **EDPB** Guidelines 4/2019 (Art. 25) · CEF 2025 report on the right to erasure. ⚠ Re-verify the
  verbatim quotes against the PDFs before they enter the DPIA — neither PDF parsed (pack §6.1 caveat).
- **ICO** "beyond use" — the two-tier backup formulation adopted in Consequences (persuasive, not binding
  in SE).
- **Arbetsförmedlingen** annonseringsvillkor — the advertiser holds the contact persons' consent, not us;
  AF's own remedy for a problematic ad is removal + republication, not surgical redaction. AF also
  publishes *Historiska annonser* as open data.
- **RECAP**, arXiv 2510.07551 (2025-10-08) — hybrid regex + LLM is the published SOTA for multilingual
  PII detection; grounds the "knowingly below SOTA" disclosure.
- **PostgreSQL 18** §5.4 (STORED generated columns recompute on write), §12.3.1 (the `email` token type's
  limits), §24.1.2 (an UPDATE does not remove the old row version until VACUUM).
- ADR 0032 (JobTech integration) · ADR 0024 (audit retention + Art. 17 cascade) · ADR 0049 (PII field
  encryption / DEK envelope) · ADR 0071 (no LLM inference) · ADR 0072 (public repo / docs privacy) ·
  ADR 0086 (ad snapshot) · ADR 0087 D8(a) · ADR 0090 D5 (HMAC-SHA256 + server pepper) · ADR 0045 (perf
  budgets) · ADR 0065 (SoC / PR split).
- Robert C. Martin, *Clean Architecture* (2017) — kap. 8 (OCP, `AuditFailures` opt-in), kap. 22
  (invariants at the centre, D4). Eric Evans, *Domain-Driven Design* (2003) — "Aggregates" (invariants
  enforced at the boundary, not by callers). Winters/Manshreck/Wright, *Software Engineering at Google*
  (2020) — the Beyoncé rule.
- CLAUDE.md §2.2 (aggregates protect invariants), §3 (error idioms; endpoint-local statuses; `ErrorKind.Gone`),
  §5 (anti-patterns; deterministic engines; a rule engine never rewrites silently), §6.5 (migration
  single-owner), §7 (testing), §9.2 (mandatory agents), §10 (Swedish UI rules), §12 (merge-blocking).
- Issue #842 (P1, launch-gate) · #843 (test fiction) · #845 (retention doc-only) · #821/#841 (the
  `job_ads` migration lane, untouched) · branch `fix/recruiter-pii-erasure-842`.

---

## Amendment 2026-07-13 (#842 PR2) — the re-bind, the durability blocker, and the launch gate

This ADR was authored from the **first** CTO ruling and transcribes it faithfully. That ruling was
then **re-bound the same day** (`docs/reviews/2026-07-13-842-erasure-contract-cto-rebind.md`), the
security-auditor **vetoed the re-bind** (`docs/reviews/2026-07-13-842-rebind-security.md`), and the
CTO resolved the veto (`docs/reviews/2026-07-13-842-b1-durability-cto.md`). The sections below are
**superseded in place**, dated, per the house rule that drift is amended rather than superseded.

### A1 — D1 (Tier A) is REVERSED. We are not a mirror.

**Struck:** *"We do not STORE recruiter contact details"* / *"We are a mirror; a mirror does not need
the contact block."*

The mirror premise was **false, and it was load-bearing**. `AdSnapshot` (ADR 0086) exists precisely so
the frozen ad **outlives** the source — and a mirror has no need to outlive what it mirrors. An
aggregate whose purpose is to preserve the applied-to ad after Arbetsförmedlingen de-lists it is the
signature of an **application-tracking tool**. The refutation was sitting inside the ruling that
asserted it.

Measured against the live JobTech API (100 ads, 2026-07-13): **~45 % carry the contact ONLY in the
structured `application_contacts` block, which we discard**, while **~22 % carry it only in the free
text, which we keep, index and cannot erase**. We were **throwing away the good copy and keeping the
bad one** — and this ADR ratified that and called it Art. 25.

**Tier A is re-bound as:** deserialize `application_contacts` into a typed domain field, **promote**
free-text regex hits into the same field, **then** scrub the body — moving the contact from a carrier
that is unbounded, FTS-indexed and un-erasable into one that is bounded, un-indexed,
retention-bounded and surgically erasable. Not strip-all.

**Consequence for D13:** Tier A takes **ONE migration** (two nullable jsonb columns + a GIN index), so
`db-migration-writer` **IS** mandatory for PR3. It queues behind **#841** (the `job_ads` migration
lane, owned elsewhere). **STOPP-1 is WITHDRAWN** — the CTO asked the wrong question.

### A2 — B1: no surgical remedy for a LIVE ad. Tier B *is* the remedy. **[BOUND]**

The re-bind's "surgical" contact erasure was **not durable**: the funnel has no hash short-circuit,
`UpdateFromSource` reassigns unconditionally, and the nightly sync (02:00) plus the 10-minute stream
would restore a cleared `contacts` column within ≤24 h. That is **F-A, recreated in the very column
the ruling introduced** — the same defect PR1 had just contained.

**BOUND (CTO, 2026-07-13):** for a still-listed ad, the funnel's unconditional rewrite is not a bug —
it is the correct contract (AF is the controller of record; our copy tracks it). A surgical clear
tells a tracking copy to durably disagree with what it tracks, and the only honest way to do that is
to **stop tracking it**. That is what Tier B already is. Surgical contact erasure is reserved for
records the funnel never rewrites (archived / de-listed ads, and `snapshot_contacts`).

A PII-free `contacts_suppressed` flag was **considered and refused** — not on lane grounds (it would
have ridden the same Tier-A migration) but because it **fixes durability and leaves honesty
untouched**: a surgical clear still does not reach the recruiter's **name** in free text (~22 % of
ads), which stays in `search_vector` and `extracted_terms`. We would clear the column, send an
Art. 12(3) confirmation, and leave her name reverse-queryable. That is redact-on-request with a flag
on it — the class **D3 already forbids**.

### A3 — B2: STOPP-6 is AMENDED. **Not negotiable, and not a product decision.**

**STOPP-6 now reads:** *no `v*` prod tag until **BOTH** tiers have shipped.*

> ⚠ **Superseded 2026-07-26 (#845):** both tiers have shipped and the gate is **LIFTED** by Klas
> decision — see the Amendment 2026-07-26 at the end of this file. This heading's *"not negotiable, and
> not a product decision"* was true of the condition, not of the outcome: the lift is Klas's call and he
> made it.

The re-bind reordered the PRs (Tier B first) and carried STOPP-6 over verbatim, so it read as though
Tier B alone lifts the gate. **It does not.** Tier B answers the recruiters who **ask**; Art. 25(2) is
about the ~37 000 who never will, whose addresses sit in plaintext inside a GIN-backed `search_vector`
that any logged-in user can reverse-query. **An Art. 17 endpoint does not discharge an Art. 25 duty.**
Tier B lifts the Art. 17 *defect*; it does not lift the Art. 25 *gate*.

### A4 — M3: the Art. 17 cascade, bound and made **un-forgettable**

| Surface | Ruling |
|---|---|
| `recent_job_searches` | **Cascade — hard-delete the row.** Nulling `q` is unavailable: `q` is a derivative of `FilterHash`, which is the row's identity, and the aggregate binds that they must never diverge — a nulled `q` **corrupts** the row rather than cleaning it. The aggregate already binds hard-delete as its disposal semantics; ADR 0067 Fas C2 already mass-deleted rows of this table on the same reasoning. User cost: zero (the cap-20 list self-rebuilds). |
| `saved_searches` | **NO cascade. RECORDED-OUT, and NOT on 17(3)(e)** (which does not transfer). Ground: our basis is **Art. 6(1)(b)** — contract with the *user* — and **Art. 21(1) textually reaches only 6(1)(e)/(f)**, so the recruiter's objection never fires and the 17(1)(c) ground that would flow from it never arises. *Different mechanism from the applicant snapshot: the snapshot is kept **despite** Art. 17 applying; the saved search is kept because the request **does not reach** that processing at all.* Independently, both remedies are broken: `SoftDelete()` leaves `criteria` in the row (it hides, it does not erase — **a cascade built on it would BE the #842 defect class**), and stripping `Q` is not universally constructible (`SearchCriteria`'s non-empty invariant + `RelevanceRequiresQ` mean a saved search whose ONLY criterion is the recruiter's name cannot have it removed — the remedy fails exactly on the case the request would be about). **Reported in the dry run; a human decides, with the affected user in the loop.** |

**The anti-vacuity control is NOT the cascade** (a cascade can be forgotten). It is the **dry run's
per-surface enumeration**, pinned by a test: `ErasureCascadeRegistry` + `ErasureCascadeRegistryTests`.
Every persisted surface must be classified — cascaded, matched-but-not-erased (**with a written legal
ground**), or structurally-no-recruiter-text (**with a written reason**) — or **the build breaks**.

> **The CTO's own STOPP-4 conditional is WITHDRAWN.** *"Cascade if rows exist"* keys a control to a row
> count measured on one afternoon. The tables held 1 row and 0 rows when this was written. **That is
> #842 in advance** — the vacuous purger was also correct for exactly as long as nobody looked. The
> cascade ships on an empty table, and reports 0 truthfully.

That registry is what ADR 0024's cascade registry should have been. ADR 0024's was prose, listed only
`raw_payload`, went stale silently, and is a large part of why #842 survived two releases while an
auditor reading it would have concluded we were compliant.

### A5 — Corrections to this ADR's own factual claims

- **D7's `Erase()` field list was INCOMPLETE.** It omitted `Company`. An **enskild firma's company name
  IS a natural person's name** (which is also why `organization_number` may be a personnummer), and the
  matcher reaches `employer.name` through `raw_payload` — so we would have matched on her name, erased
  the ad, confirmed it under Art. 12(3), and left the identical string in `job_ads.company_name`.
  `Erase()` clears it (`Company.Erased`). **The claim that the tombstone "stores no PII" was false as
  specified and is true as built.**
- **D7's `ExtractedTerms` must be set to `Empty`, not null.** `NULL` means *"never extracted"* and
  carries `BackfillJobAdExtractedTermsJob`'s idempotence (`extracted_lexemes IS NULL`), so a nulled
  value would make the tombstone look un-extracted and the backfill would pick it up again.
- **D8's HMAC primitive did not exist.** ADR 0090 D5 **bound** HMAC-SHA256(server pepper) as the house
  pseudonymisation primitive and **it was never built** — there is no HMAC anywhere in `src/`. #842
  builds it (`IIdentifierPseudonymizer` / `HmacIdentifierPseudonymizer`, fail-closed at startup). *A
  bound decision nobody implemented is the same class of finding as this entire issue: a control that
  exists only in a document.* **The pnr-shaped `company_watches` rows ADR 0090 D5 was written for are
  still plaintext — a live gap in another lane, NOT closed by #842.**
- **D8's "mandatory dry-run" was a runbook sentence.** It is now enforced in code: a destructive call
  must carry the ad count the dry run reported, and a mismatch is refused (409). Ingest runs every ten
  minutes, so that race is real, not theoretical.

### A6 — What is still OPEN

**STOPP-2, STOPP-3, STOPP-4 and STOPP-5 stand unchanged. STOPP-6 is amended (A3). STOPP-1 is
withdrawn (A1).**

**Art. 30 register entry: REQUIRED, and it ships with PR2** (CTO, 2026-07-13). Klas's narrowing —
*"Tier B introduces no new data category, so the DPIA/register gate Tier A only"* — is **half right**:
the **LIA** gates Tier A only (Tier B holds nothing under 6(1)(f); its audit runs on **6(1)(c)**, a
legal obligation, which needs no balancing test). But **"Tier B only removes data" is false**:
`identifierHmac` is **pseudonymised personal data** (Art. 4(5), Recital 26 — we hold the pepper),
`audit_log` gains a **new category of data subject** (a third party who is not our user), and
`AuditFailures` creates a durable record of a **rejected** rights request. Art. 30(1) is a hard,
standalone obligation. **CC drafts the entry; no Klas signature gates the merge.** The DPIA amendment
is likewise CC-drafted; its *signature* remains blocked on **STOPP-4** — but Tier B **inherits** that
blank, it does not create it, and stranding a P1 launch-gate item behind a fact Klas has not yet
supplied is the wrong trade.

---

## Amendment 2026-07-13 (b) — what the agent gates found, and what it cost to be wrong

PR2 was **BLOCKED by three of four mandatory agents**. Every blocker was real. This section records
them, because a fix whose own defects are not written down is the thing this ADR exists to prevent.

### B1 — the mandatory dry run was VACUOUS. It never ran once.

The validator read:

```csharp
RuleFor(c => c.ConfirmedJobAdIds)
    .NotNull().When(c => !c.DryRun)
    .Must(...).When(c => !c.DryRun && c.ConfirmedJobAdIds is not null);
```

FluentValidation's `.When()` defaults to `ApplyConditionTo.AllValidators` — it re-scopes **every**
validator in the chain, not just the one it follows. So the second condition silently applied to
`NotNull()` as well, and `NotNull()` could therefore only run **when the value was not null**.

**The single control standing between an operator and irreversible corpus-wide destruction was a
control that looked like it worked and never ran.** A green suite agreed with it. That is #842's
defect class, reproduced inside #842's own fix, by the person writing the fix. Each condition now
gets its own `RuleFor`, and a unit test exists whose only job is to fail if `NotNull` becomes
unreachable again.

### B2 — the confirmation gate was bypassed by the nothing-held branch

`if (matched.Total == 0) return Success(NoMatchingDataHeld)` sat **before** the gate. A destructive
call confirming three ads against a corpus now matching **zero** was answered *"we hold no data about
you"* — 200 OK — instead of 409. That is exactly the stale-view race the gate exists for, and it is
the case where the operator's picture and reality are furthest apart. He would then have relayed *"we
hold nothing about you"* to a named person, on the strength of a discrepancy the system swallowed.

### B3 — the outcome discriminator shipped as an INTEGER

`System.Text.Json` serialises an enum as a number by default. `{"outcome": 1}`. **In a PR whose
thesis is "never a bare `rowsAffected` again", the discriminator went out over the wire as a bare
opaque number** — and `0` meant `NoMatchingDataHeld`, i.e. the same false calm, in the same position,
as the old `rowsAffected: 0`. Fixed with `JsonStringEnumConverter`; the runbook had been documenting
a string the API did not send.

### B4 — `company_name` was UNMATCHABLE for most of the corpus

`Erase()` was corrected to clear `company_name` (an enskild firma's company name IS a person's name).
But the **matcher** reached `employer.name` only through `raw_payload` — and
`PurgeStaleRawPayloadsJob` NULLs `raw_payload` on a criterion, not a fixed period — rule in
ADR 0032 Amendment 2026-07-26 §C2 (corrected 2026-07-26, #845). `company_name` is not in
`search_vector` either. ⇒ **For every ad older than 30 days — most of 93 469 collected over months —
an enskild firma would have been told *"we hold no data matching this identifier"* while her name sat
in plaintext in a column we scan on every erasure.**

The end-to-end test passed **only because its `raw_payload` was fresh**, and its own doc said so out
loud. A quiet holding precondition is exactly the shape of this issue. A third channel
(`lower(company_name) LIKE …`) and a test that purges `raw_payload` **first** now close it.

### B5 — an erased ad RENDERED on `/sparade`

`ListSavedJobAdsQueryHandler` joins `job_ads` with **no status predicate** — deliberately, since
#805-3 removed the filter so an archived saved ad still renders. So an erased ad appeared as a normal
card with an empty title and the company **`[raderad]`**: the tombstone's own marker, on screen, to a
user. The spec's claim that *"every other read path already filters `Status == Active`, so a fourth
status is excluded for free"* was true of search, matching, watches, suggest and landing stats — and
**false exactly where a previous fix had removed the filter**. *A claim that a control covers "every
path" is worth exactly the enumeration behind it.*

### B6 — the FTS channel was MUTATION-SURVIVING

Deleting the entire `search_vector @@ …` clause left **every** test green. The channel was unproven —
and it is load-bearing: an ad that writes **"Fagerberg, Magnus"** (surname first, as a great deal of
Swedish ad copy does) is found by FTS and **missed** by the substring scan. A test now pins exactly
that, and the mutation makes it red.

**And the docs credited the wrong mechanism for obfuscation.** The substring channel does **not**
de-obfuscate: searching `anna@acme.se` will never find `anna(at)acme.se`. What serves that population
is her **NAME**. The claim is corrected in the port, the runbook and here — the same M10 defect class
this very PR withdrew from `PlatsbankenJobSource`.

---

## Amendment 2026-07-13 (c) — the legal ground for `saved_searches` was WRONG

**The prior amendment (A4) is REVERSED on its legal reasoning.** It held that a saved search is
processed under Art. 6(1)(b) — our contract with the user — so Art. 21(1), which textually reaches
only 6(1)(e)/(f), never fires and the recruiter's objection never arises.

**Art. 6(1)(b) reads: *"necessary for the performance of a contract to which THE DATA SUBJECT is
party."*** The data subject here is **the recruiter**. She is party to nothing. **A contract with Y
cannot supply the lawful basis for processing X's personal data.** One row carries two data subjects
under two bases: the JobSeeker's criteria rest on 6(1)(b); **the recruiter's name sitting inside them
rests on 6(1)(f)** — which Art. 21(1) reaches. **Her objection fires. Art. 17(1)(c) is available. The
right applies.**

**BOUND (CTO, 2026-07-13):**

- We do **not** attempt the Art. 21(1) *"compelling legitimate grounds"* override. Keeping her name in
  another user's filter is a **convenience**, and a saved search is recreatable in seconds.
  **We owe her erasure and we honour it in full.**
- What we do not do is **automate** it — `SoftDelete()` leaves `criteria` in the row (it hides; it does
  not erase), and stripping the term is not universally constructible. **A human does it, inside the
  Art. 12(3) month, with the affected user in the loop. That is a MECHANISM choice, never a refusal.**
- **The reply must never tell her the right does not reach it.** That would be a false statement to a
  data subject about her own rights (Art. 12(4)). *"Our code cannot do it" has never been a legal
  ground. That IS #842.*

**Corrected in all four artefacts:** `ErasureCascadeRegistry`, the runbook's reply template B2, the
Art. 30 register, and this ADR.

### A new surface, found by the registry itself

Driving the cascade registry from the **EF model** (column granularity, not `DbSet` granularity)
surfaced **`applications.cover_letter`, `application_notes.content` and `follow_ups.note`** — a user
may well have written *"Ringde Magnus Fagerberg"* in her own note. That is the recruiter's personal
data, her right reaches it, and **no version of this feature would have searched it.** It is now
searched and reported; a human erases it.

**And that is the argument for column granularity, made by the guard itself.** The aggregate-level
version had already ticked `Application` off as "classified" — it could not have caught
`snapshot_company` or `cover_letter`, and it could not have caught `job_ads.company_name` either.
**A guard one level coarser than its own defect class does not merely miss. It reassures.**

---

## Amendment 2026-07-14 — M1 classification reanalysis + the unsearchable-surfaces disclosure + D9 read-path table

The CTO re-analysed the cascade registry's classifications and found that:

1. **Three encrypted columns were claimed as "searched and reported" but are structurally unsearchable.** `applications.cover_letter`, `application_notes.content`, `follow_ups.note` are encrypted under per-user DEKs (Form A, Art. 49(3)(b)). A SQL `LIKE` query bypasses decryption entirely and matches zero rows on every request. **It is the #842 defect class in its purest form: a channel that cannot work because the write path guarantees its preconditions will never hold.** Reclassified to `HeldButNotSearchable` with an explicit disclosure: *"We hold this, encrypted under a key we cannot read without the user's involvement. Manual escalation to the user."*

2. **Two free-text columns wrongly classified as "cannot hold recruiter data."** `applications.manual_url` (2000-char user-pasted string) and `company_watch_criteria.label` (120-char user-typed label) were marked `NotRecruiterData`, resting on *"structurally cannot"* — a classification that was **false and load-bearing**. A URL path carries names: `linkedin.com/in/magnus-fagerberg`. A watch label can be anything. Both are searched and reported; humans erase them.

3. **The outcome vocabulary was incomplete.** The API now returns `couldNotSearch: { reason, columns[] }` — an explicit disclosure per-request that certain columns exist but are encrypted and will not be scanned. This is not a gap in the search; it is an honest statement of a structural limit.

4. **STOPP-6 is further amended.** No `v*` prod tag until Tier A **and** Tier B **and** the applicant's preserved record survives an erasure (issue #D3 — the fallback-to-snapshot fix for `GetApplications` when the ad is erased). ⚠ **Superseded 2026-07-26 (#845): all three legs are closed and the gate is LIFTED** — see the Amendment 2026-07-26 at the end of this file. This three-leg formulation is the one the lift was measured against.

### D9 — Read-path table: gated vs ungated sites

Every site that projects `JobAd` fields is now tabulated, with gating status and issues for the ungated ones. **Comments that enumerate, lie. A table with an issue number per unfixed row does not.**

| Site | Surface | Projects | Gated on `Status != Erased`? | Issue |
|---|---|---|---|---|
| `GetJobAdQueryHandler` | Ad detail | Title, Description, Company | ✅ Yes (410 Gone) | — |
| `JobAdSearchComposition` + `PerUserJobAdSearchQuery` | Ad search | Title, Description | ✅ Yes (`WHERE status = 'Active'`) | — |
| `ListSavedJobAdsQueryHandler` | User's saved ads | Title, Company, URL | ✅ Yes (`Status != Erased`) | — |
| `SaveJobAdCommandHandler` | Bookmark validation | — | ✅ Yes (`Status != Erased`) | — |
| `GetMyMatchesQueryHandler` | In-app matches | Title, Company | ✅ Yes (`Status == Active` allow-list — #864, merged 2026-07-14) | — |
| `DigestDispatchJob.SendMatchDigestAsync` | Email digest | Title, Company | ✅ Yes (`Status == Active` display window — #864) | — |
| `DigestDispatchJob.SendFollowedCompanyDigestAsync` | Followed company email | Title, Company | ✅ Yes (`Status == Active` display window — #864) | — |
| `CompanyWatchScanJob` | Company watch scan | — | ✅ Yes (`WHERE status = 'Active'`) | — |
| `ListCompanyWatchesQueryHandler` | Company watch list | — | ✅ Yes (safe by NULL org.nr, see text) | — |
| `SuggestJobAdTermsQueryHandler` | Typeahead suggestions | — | ✅ Yes (`WHERE status = 'Active'`) | — |
| `RefreshLandingStatsJob` | Landing page stats | — | ✅ Yes (`WHERE status = 'Active'`) | — |
| `BackgroundMatchingJob` | Match detection | — | ✅ Yes (`WHERE status = 'Active'`) | — |
| `JobAdSnapshotMissTracker` | Miss tracking | — | ✅ Yes (`WHERE status = 'Active'`) | — |
| `GetApplicationsQueryHandler` | Applicant's applications | Title, Company, all snapshot fields | ✅ Resolved — snapshot fallback in the projection (#892, never a join predicate) | — |
| `GetPipelineQueryHandler` | Employer's pipeline | Title (same underlying query) | ✅ Resolved — snapshot fallback (#892) | — |
| `GetActivityReportQueryHandler` | Employer name in activities | Company (same underlying query) | ✅ Resolved — reads the snapshot the 17(3)(e) ground argued for (#892) | — |
| `GetApplicationByIdQueryHandler` | Application detail | All snapshot fields | ✅ Resolved — C# summary fallback; preservedAd unchanged (#892) | — |
| `GetEmployerApplicationHistoryQueryHandler` | Employer's application history | Title | ✅ Documented residue — erased → org.nr NULL → dropped pre-GroupBy; snapshot holds no org.nr, functionally unfixable (#892 R4; #824 owns the bucket) | #824 |
| `CreateApplicationFromJobAdCommandHandler` | New application capture | Snapshot freeze (data source only) | ✅ Write-path member — not a read gate; the erased-freeze defect is closed by a 410 Gone refusal, not by a status predicate (#892 R3) | — |
| `MatchScorer.cs:86,146,214,299` | Score generation | Title, Description, Company, ExtractedTerms | ❌ No | **#864** (different lane) |

**`ListCompanyWatchesQueryHandler:79`** uses `.Where(j => orgNrs.Contains(...))` on `organization_number`, which is NULL for erased ads. SQL three-valued logic: `NULL = ANY(…)` is never true, so erased ads fall out for free. **No guard needed; the guarantee is structural.** Written at the site for future readers: *"Erased ad's org.nr is NULL because the column is STORED-generated from `raw_payload`, which `Erase()` nulls. The row falls out of the join naturally."* (The column will be **materialised** by #841, at which point this guarantee becomes a **dependency** — see note below.)

**Ungated sites #D3:** The Applications vertical (six sites, common underlying query via `GroupJoin` with no status predicate). A `j == null` means *"no ad row matched"* — which used to encode *"the ad is gone."* Adding a status predicate would reintroduce #805-3. The right fix is the **snapshot fallback**: when the ad is erased, render the applicant's `AdSnapshot` instead. This is a design change (five handlers, a DTO contract, possibly FE changes), not a line in a `WHERE` clause. **It is not this PR's change-reason; it joins the launch gate as #D3.**

**Ungated sites #864:** `MatchScorer` (four sites). An erased ad's title/description/terms are all empty/null, so it cannot score a Strong match. The display and email drains are gated at the read side (the `GroupJoin` — `#D3`), so a false "Strong" on an empty signal has no user-visible consequence. It is a junk row, not an exposure. Owned by a different lane; noted for continuity but not touched.

**The table itself is the control.** Add a new `JobAd` site without adding it here, and `ErasureCascadeRegistryTests::TestReadPathsAreEnumerated` fails the build. **That is the anti-vacuity guard.**

---

## Amendment 2026-07-14 (continued) — the organization_number finding

`job_ads.organization_number` is a **STORED GENERATED COLUMN**, derived from `raw_payload`. Its definition: `HasComputedColumnSql("raw_payload->'employer'->>'organization_number'", stored: true)`.

**F1:** `Erase()` sets `RawPayload = null`. 
**F2:** When `raw_payload` becomes NULL, the generated column **Postgres-computes** the extraction result on NULL, which is NULL.
**F3:** An erased ad's `organization_number` becomes NULL **by Postgres**, at the instant of erasure, with zero code.
**F4:** `organization_number` **can be a personnummer** (sole traders' org.nr == national ID).

**The erasure is currently DURABLE FOR THE WRONG REASON.** It works because the column derives from a payload `Erase()` happens to null — a coincidence. **#841 will materialize this column** (convert it from STORED GENERATED to an ordinary one, written at ingest). The instant that lands, `Erase()` will stop clearing it, because `Erase()` does not know the column exists and does not touch it.

**BOUND RESPONSE:** An integration test that erases an ad through the real write path and asserts the tombstone shape, column by column, derives its expectations from `ErasureCascadeRegistry.Columns` where disposition == `Erased`. Every column in that bucket gets an assertion. **If #841 materialises a column without teaching `Erase()` to clear it, the test fails and the #841 lane gets a red build with a message naming the decision they owe.**

The #841 lane will be given a one-line courtesy note: *"if you materialize `organization_number`, `JobAd.Erase()` must null it explicitly — a sole trader's org.nr is a personnummer."* **That is a courtesy, not a hard dependency; the test is the mechanism.**

This is standing correction 5: **A control that works by accident is not a control — it is a coincidence you have not yet been billed for.**

---

## Amendment 2026-07-14 (d) — round 6: the registry drives the port, and org.nr is an identifier

Five review rounds shared one shape: *a fix reached the instances a finding enumerated and stopped
at the predicate the finding used.* Round 6 is the predicate, in three binds:

1. **The registry DRIVES the port.** `ErasureCascadeRegistry.Channels` declares
   `surface → columns → port method` — one entry per reported surface. Architecture tests enforce:
   a SEARCHED column with no channel breaks the build; a channel must name a real
   `IRecruiterErasureMatchQuery` method and a real reported surface; and every `Erased` column is
   either channel-searched or carries a written derivation in
   `ErasureCascadeRegistry.ErasedWithoutSearchChannel` (there is no third state — `job_ads.url`
   carries the argument this table's D9 remark anticipated). The QUERY half is pinned by
   integration tests that seed one row per channel column whose identifier lives in THAT COLUMN
   ALONE. Round 5's two Blockers (`snapshot_url` classified as searched and never queried;
   `employer_list` searched with a matcher that could not match) both lived in the gap these two
   pins now close.

2. **An Art. 17 identifier may BE an organisationsnummer/personnummer** (CTO ruling
   `docs/reviews/2026-07-14-842-pr2-employer-list-cto.md`, Klas GO 2026-07-14). Art. 4(1) counts
   identification numbers as personal data; Art. 12(2) forbids refusing the one exact key a sole
   trader has; and this contract's own tombstone test already destroys
   `job_ads.organization_number` *because it is her personnummer* — a registry cannot call the
   identical datum "not personal data" one table over. An org.nr-shaped identifier is normalised
   in Domain (`OrganizationNumber.TryFromWrittenForm`: `556012-5790` → `5560125790`,
   century-prefixed personnummer forms included) and matched EXACTLY against
   `job_ads.organization_number` AND `recent_job_searches.employer_list` — a structured key gets
   structured matching; bolting it into the free-text regex is what produced round 5's vacuous
   arm. The employer-only search row (`q = NULL`, the domain's canonical form) is returned and
   hard-deleted by the SQL-returned ids; `ErasureRecentSearchMatch.Q` is nullable so the type can
   no longer force the projection to discard it. Evidence surfaced to the operator is flagged via
   `IsPersonnummerShaped` (D8(c)) and never logged.

3. **The sweep enumerates FORMS.** The free-text column sweep derives from the STORE type
   (`GetColumnType()`), not the CLR type — a value converter, an array, a `byte[]`, a tsvector
   and a `.ToJson()` container column are all one predicate now. It surfaced 23 converter-mapped
   columns; each is classified against its write path, every written ground names every column in
   its bucket, and the reply templates in `docs/runbooks/recruiter-pii-erasure.md` (Klas-approved
   2026-07-14) are keyed on the five real `ErasureOutcome` members — the runbook word
   `NoMatchingDataHeld`, which the code abolished, is gone from every operational artifact.

The original §contract prose above still names `NoMatchingDataHeld` in its outcome sketch; it is
retained as the historical bind and superseded by R2-4 (the 2026-07-13 durability ruling) and this
amendment. **The operational truth is the enum.**

---

## Amendment 2026-07-17 — #892 delivered: the #D3 rows are closed and STOPP-6's third condition is lifted

Bound by `docs/reviews/2026-07-17-892-snapshot-fallback-cto.md` (R1–R8, executed as
PR `fix/adsnapshot-fallback-892`). Four records:

**(a) The five #D3 read rows are RESOLVED — via the snapshot fallback, never a status predicate.**
`GetApplications`, `GetPipeline`, `GetActivityReport`, `GetApplicationById` now swap summary
identity to the application's own `AdSnapshot` when `j.Status == Erased && AdSnapshot != null`,
in the PROJECTION — the GroupJoin stays status-agnostic, exactly as H5/§14.1 required
(a join predicate would have reintroduced #805-3). Erased-without-snapshot (pre-#315) emits
EMPTY identity: the `Company.Erased` sentinel `"[raderad]"` never crosses the Application
boundary (CTO R5, CLAUDE.md §2.3); the FE renders structurally from `Status=="Erased"` +
identity presence, and every surface carries the removed-ad marker (R1's other half — restored
identity without a death signal would let a dead ad look alive). Archived ads keep reading LIVE
identity (the predicate is surgically `== Erased`, never `!= Active`).
`GetEmployerApplicationHistory` is the documented exception: erased → org.nr NULL → dropped
before GroupBy; the snapshot holds no org.nr, so identity is functionally unrecoverable — the
row joins the #824 residue bucket (R4; CountBatch identically, though outside §14.4's list —
a half-synced registry is an enumeration that lies).

**(b) `CreateApplicationFromJobAd` reclassified: write-path member, refusal-gated.** The D9
cell used to read "N/A (snapshots are separate concern)" while the §14.4 bind scoped the site
in. §14.4 governs (R3): the table's own axis is read gates, so "not a read-gate row" was
literally true — but the gloss read as "nothing to do here", and that was false. The handler
now refuses an erased id with **410 Gone** BEFORE capture (mirroring `GetJobAdQueryHandler`'s
read gate; 404 stays for "no row"), because freezing `""`/`"[raderad]"` into a brand-new
permanent snapshot was the write-path half of the #D3 defect. The derived table is corrected
to match the binding ruling, never the reverse.

**(c) STOPP-6's third condition is CLOSED.** The gate read: no `v*` prod tag until Tier A
**and** Tier B **and** the applicant's preserved record survives an erasure (#D3). With (a)
restoring all four applicant-facing read surfaces and (b) stopping the write path, nothing in
the third condition remains open.

> ⚠ **Superseded 2026-07-26 (#845): the gate is LIFTED by Klas decision** — see the Amendment
> 2026-07-26 at the end of this file. The sentence below (*"this lifts one leg, not the gate"*) was
> true when written and is **not** the current status; do not act on it as a live gate.

**The other two legs (Tier A `daa4b51d`, Tier B `269a4603`)
and the Klas-side STOPP points (LIA K3, notis-copy K4, STOPP-5 backfill dry-run, STOPP-2/3/4)
are untouched — this lifts one leg, not the gate.**

**(d) Proof discipline of record.** The fallback is pinned in Npgsql/Testcontainers
(`ErasedAdSnapshotFallbackTests`): EF InMemory client-evaluates the projection ternary and
cannot witness the emitted SQL, so InMemory green is structurally non-evidence here.
Asymmetric seed through the real write path (`Import` → `Erase()`, fail-loud tombstone
read-back), an archived control (proves live archived identity is never overridden), a
pre-#315 counterfactual row (proves the guard is `AdSnapshot != null`), and a
nothing-persisted assertion on the 410 refusal. The `JobAdLifecycleReadRegistry` reason texts
for the seven Applications sites were truth-synced in the same commit; the Cecil count-pin is
unaffected (the fallback adds zero `get_JobAds` calls).

---

## Amendment 2026-07-17 (#885) — the D9 `MatchScorer` row corrected: gated, and its recorded ground was false

Bound by `docs/reviews/2026-07-17-885-match-detail-erased-cto.md` (senior-cto-advisor, G5.3, quoting
its own V1/V2 source re-verification). The match-detail path
(`GET /api/v1/me/job-ad-match-tags/{jobAdId}`) now answers **410 Gone** for an Erased ad — the same
neutral body `GET /api/v1/job-ads/{id}` emits for the same row — via a deny-list (`Status != Erased`)
held in **both** layers, on two distinct change-reasons: `GetJobAdMatchDetailQueryHandler`
(Application) owns the response, and `MatchScorer.ScoreAsync` / `ScoreFullAsync` (Infrastructure) hold
the port's own invariant, closing the TOCTOU window a handler-only gate would leave open. This closes
the gap the D9 table above (Amendment 2026-07-14) marked ❌ No and dismissed as *"a junk row, not an
exposure."*

**That dismissal, and the Projects column it rested on, were both false — not merely stale.**
Re-verified against source this session (`MatchScorer.cs:105-129,239-259`):

- **`ScoreAsync` projects `AdFacetRow(Title, OccupationGroupConceptId, RegionConceptId,
  EmploymentTypeConceptId, MunicipalityConceptId)`. `ScoreFullAsync` projects `AdFullRow`** — the same
  four facet shadows plus `ExtractedTerms`. **Neither projects `Description` nor `Company`.** The
  original row's Projects column (*"Title, Description, Company, ExtractedTerms"*) named two fields
  the scorer never reads and omitted the four it does.
- **The omitted fields are exactly the ones `Erase()` deliberately keeps.** `JobAd.Erase()`'s own
  comment: *"The six `*_concept_id` facets stay: they are Arbetsförmedlingen taxonomy codes, classified
  NotRecruiterData, and a tombstone that keeps its SSYK code discloses nothing about her."* The four
  the scorer reads are a subset of those six, kept by written design — and
  `GetJobAdMatchDetailQueryHandler` resolves the membership dimensions to **human-readable labels** via
  `ITaxonomyReadModel` (`ResolveMembershipLabelsAsync`) before the row ever reaches the client.
- **So the pre-#885 exposure was not "cannot score a Strong match on an empty signal."** It was a
  live, labelled verdict: traced against `MatchGradeCalculator`, `SsykOverlap` / `RegionFit` /
  `EmploymentFit` can all resolve on the surviving facets while `MustHaveCoverage` is `Vacuous` (terms
  are cleared) — the F1(b) requirement gate caps the grade at **`Good` ("Bra match")**, never
  `Strong`/`Top`, but `Good` is reachable and is a **positive, labelled result**: a grade plus
  `"Systemutvecklare"` / `"Stockholms län"` / `"Tillsvidareanställning"`, for a row whose own detail
  page said *"Annonsen är inte längre tillgänglig."* The original row's narrower claim —
  *"cannot score a Strong match"* — was true and irrelevant: the exposure never needed Strong.

**The row now reads:**

| Site | Surface | Projects | Gated on `Status != Erased`? | Issue |
|---|---|---|---|---|
| `MatchScorer.cs` (`ScoreAsync`, `ScoreFullAsync`) | Score generation (single-ad family) | Title + 4 facet-shadow concept-ids (`occupation_group`/`region`/`employment_type`/`municipality`, via `AdFacetRow`); `ScoreFullAsync` adds `ExtractedTerms` (via `AdFullRow`). **No Description, no Company.** | ✅ Yes — `Status != Erased` deny-list (#885), bound to `GetJobAdQueryHandler`'s 410 rule: if that rule changes, this changes with it. | — |
| `MatchScorer.cs` (`ScoreBatchAsync`, `ScoreFullBatchAsync`) | Score generation (batch family) | Same facet-shadow set per row, batched. | ✅ Yes — `Status == Active` allow-list (#864); excludes Erased as a byproduct, not a #885 change. | — |

This corrects-in-place the single combined row above (`MatchScorer.cs:86,146,214,299`, "❌ No",
"#864 (different lane)"). That earlier row is left standing, unedited, for the audit trail — D9's own
words: *"Comments that enumerate, lie. A table with an issue number per unfixed row does not."* A
table with a **false reason** per fixed row lies too, which is why this amendment states the falsity
in writing rather than silently editing the earlier row out from under it. **This is standing
correction 5, turned on its own author** (Amendment 2026-07-14 (continued): *"a control that works by
accident is not a control — it is a coincidence you have not yet been billed for"*): the original
verdict reasoned only over the fields `Erase()` clears and was silent about the fields `Erase()`
keeps — the omission was the entire defect.

**`ScoreAsync`'s gate is new in this change** (#885, ruling G4) — the method had zero production
callers before and has zero after, but `IMatchScorer` publishes **one** lifecycle contract across both
single methods (ISP/LSP, Martin 2017 ch. 9–10), and a port whose doc comment is false for half its
surface is a declaration nothing backs (#886's governing precedent, one level over: *"no value may be
declared here without a writer, a distinct invariant, and a decision"*, read in the caller direction).
`ScoreFullAsync`'s gate closes the actual exposure; `ScoreAsync`'s gate closes the contract, and is
pinned by its own scorer-level spec (`ScoreAsync_RefusesAnErasedAd_TheTombstoneIsNotServed`) since no
endpoint test can reach a method with no production caller.

**Registry (`tests/Jobbliggaren.Architecture.Tests/JobAdLifecycleReadRegistry.cs`):** both single
methods re-filed under `AnyStatus` with the deny-list reason, matching `SaveJobAdCommandHandler`'s
existing precedent rather than inventing a fourth registry-taxonomy kind (ruling G5.1).
`GetJobAdMatchDetailQueryHandler.Handle` is registered as a new `AnyStatus` site — the status
pre-check, before the scorer runs (ruling G5.2). `JobAdLifecycleReadRegistryTests` count-pins every
method; an unregistered site fails the build.

---

## Amendment 2026-07-17 (phantom-contact promote gate) — the asymmetric posture

Bound by `docs/reviews/2026-07-17-guid-payload-flake-cto.md` (senior-cto-advisor, Verdicts 2 and 3;
Klas GO 2026-07-17), delivered in branch `fix/phantom-contact-promote-gate`, commit `44b21804`.

**(a) The defect: an over-redaction control was doubling as an under-precision one.** A quoted
id/reference number starting `0` + 6–12 digits in `raw_payload` is phone-shaped to the detector:
the phone recogniser's lookbehind (`RecruiterContactRedactor.cs`, `PhoneRegex`) refuses an anchor
glued to a letter, digit or hyphen — but a `"`, a space or a `%` before the `0` is an **allowed**
anchor. Such a span **was** scrubbed (correct — the recall-biased posture D4/D5 binds) **and**
**promoted** as an `ExtractedFromBody` contact, displayed on the #917 detail surfaces labelled
"derived" — a fabricated, user-visible recruiter phone number. D5's accepted over-redaction priced
*"we may scrub too much text"*; it never priced *"we may invent a contact and show it to the
user."* Those are different harms on different axes: the first is data-minimisation collateral,
the second is product untruthfulness on a user-visible surface (CLAUDE.md §5, the CV/matching
doctrine's spine — "explainable by design," never "a verdict without cited textual evidence," the
same defect class one domain over). D11/D12's disclosure covers **recall** ("the detector may
miss or over-scrub"); it does not license a **precision** failure on the promote step.

**(b) The decision — scrub for safety, promote for truth.** The scrub stays recall-biased and
byte-identical across all three surfaces (`Title`, `Description`, `RawPayload`) — `D4`/`D5` are
**not reopened.** The **promote** step becomes precision-biased, per surface, in
`JobAd.ApplyContactRedaction` (`JobAd.cs:468-494`): a **phone** span found on the `RawPayload`
surface is scrubbed but **never promoted**; **payload email** spans keep promoting (an email
cannot be id-shaped by accident — it needs `@` plus a letter-led domain label); **Title and
Description** (the ad's own visible text) keep promoting **both** kinds; **declared contacts** are
untouched. Concretely, the promote-list construction at `JobAd.cs:491` narrows from
`[.. title.Found, .. description.Found, .. payload.Found]` to
`[.. title.Found, .. description.Found, .. payload.Found.Where(s => s.Kind == ContactKind.Email)]`.
`RecruiterContactRedactor` and the `Detect` pre-check (`JobAd.cs:302-304`) are **untouched** —
detection recall is provably unchanged; only the aggregate's promotion invariant narrows.

**(c) Placement — the aggregate filters; the recogniser stays untouched.** Issue #844 ("the
recogniser owns the question, the split and the normalisation") governs **recognition** — what is
a match, where it starts and ends, its canonical form. "Is this already-recognised span
promotable, given which surface it came from" is a **promotion-policy** question that depends on
surface knowledge the recogniser structurally should not hold; stamping `Promotable` inside the
recogniser would mean threading a surface parameter into a pure text→spans function, inverting the
dependency. The aggregate already materialises the three `.Found` collections separately at the
`ApplyContactRedaction` call site and knows which one is the payload; the filter is a pure select
over spans already recognised — it does not re-recognise, re-split or re-normalise, so it creates
no second rule under #844. **The recogniser owns recognition; the aggregate owns the promotion
invariant**, which is exactly what `ApplyContactRedaction` was already documented to be.

**(d) Rejected alternatives.** **Per-span JSON-string-value detection** (V2) — converges to the
per-surface rule in behaviour (a bare JSON number cannot start with `0`, so nearly every payload
phone match already sits inside a string value) at meaningfully higher complexity, threaded through
offset bookkeeping in the redactor's shadow/replacement machinery, in the most safety-critical
class in the codebase — a KISS violation with no behavioural delta. **Free-text word-boundary
precision on Title/Description** (V3) — targets an unmeasured class (a phone-shaped run glued into
visible ad text); speculative, deferred to a measured follow-up if it is ever observed. **Narrowing
the detector itself** (refusing to anchor after a quote) — the wrong fail-safe direction: it
buys test/promote determinism by making the safety-critical **scrub** detect less, which D5's
over-redaction posture forecloses; already rejected once as Variant C in Verdict 1 of the same
review, for a different (test-flake) motivation, and rejected again here for the production
question.

**(e) Priced recall cost.** A real recruiter phone living **only** in a payload field that is
never mirrored into Title or Description (both of which are themselves payload-derived at ingest,
so a phone in the ad's visible text still promotes through them) is scrubbed but not surfaced — an
inference from a field the user cannot see fails the "promote for truth" bar. Accepted, and pinned
so it reads as a decision rather than an accident (witness 2 below).

**(f) Migration/backfill: none.** `Contacts` is recomputed on every write through the same
nightly re-ingest funnel D4 already relies on (`Import`/`UpdateFromSource` →
`ApplyContactRedaction`) for `Active` ads, so existing phantom rows self-heal without any
one-shot job. `Archived` ads already hold `Contacts = null` (retention, `JobAd.cs:62-69`).
Residual accepted: an `Active` ad that already carried a phantom **and** has since fallen out of
the source feed keeps that phantom until it is erased or archived — labelled "derived", low harm.
A belt-and-suspenders one-shot recompute remains available as a Klas call, not a requirement (EF
migration is the most-dangerous hotspot, CLAUDE.md §6.5).

**(g) Witnesses.** Four test-first witnesses in `JobAdContactRedactionTests.cs`
(`tests/Jobbliggaren.Domain.UnitTests/JobAds/`), each grep-verified against the source before
trusting green: (1) `A_phone_shaped_payload_id_is_scrubbed_but_never_promoted` — the counterfactual
pair in one test: detection still fires (the scrub is unchanged and the oracle is alive) and
promotion does not (no phantom); (2) `A_phone_living_only_in_a_payload_field_is_scrubbed_and_not_promoted`
— the priced recall cost from (e), pinned; (3) `An_email_living_only_in_the_payload_still_promotes`
— the payload-email path is unchanged; (4) `A_declared_phone_survives_beside_a_phone_shaped_payload_id`
— the declared-contact path is unchanged and the phantom does not ride in beside it. Gates run:
security-auditor + dotnet-architect + test-writer + code-reviewer (per Verdict 3's process
section); `db-migration-writer` not invoked (f).

---

## Amendment 2026-07-26 — STOPP-6: the launch gate is LIFTED (Klas decision)

**Decision-maker: Klas Olsson, 2026-07-26**, on PR #1089 (#845). This is a **decision**, not a
derivation from the record — it is written here because **this ADR is the gate's sole owner** (CTO
bind V8, ADR 0032 Amendment 2026-07-26 §C6), and every other document points here rather than
carrying its own copy of the status.

### The gate as it was written

STOPP-6 read: *"The launch gate **stays closed until PR3 lands.** … **Confirm: no `v*` prod tag until
Tier B ships.**"* The Amendment 2026-07-17 (c) restated it as three technical legs — Tier A **and**
Tier B **and** the applicant's preserved record surviving an erasure (#D3) — and closed the third,
while recording that *"the Klas-side STOPP points … are untouched — this lifts one leg, not the gate."*

### What is verifiable, and was verified before this entry was written

| Leg | State | Evidence |
|---|---|---|
| Tier B — Art. 17 provable erasure | **SHIPPED 2026-07-15** | `269a4603`; `EraseRecruiterAds/` carries command, handler, validator, `ErasureCascadeRegistry` |
| Tier A — Art. 25 ingest scrub | **SHIPPED 2026-07-17** | `daa4b51d`; `RecruiterContactRedactor` as a `JobAd` aggregate invariant + corpus backfill |
| #D3 — applicant's record survives erasure | **CLOSED 2026-07-17** | #892; Amendment 2026-07-17 (a)/(b) |
| *"with green provable-erasure tests"* | **GREEN** | `RecruiterErasureIngestTests` + `ErasedAdReadPathTests`, 34 tests, 0 failed, Testcontainers Postgres, re-run 2026-07-26 |

### The decision

**The launch gate is LIFTED.** No `v*` prod tag is gated on #842 from 2026-07-26.

### What this does NOT do — stated so the lift cannot be read as more than it is

**The Klas-side STOPP points are NOT closed by this entry, and Klas lifted the gate with them open.**
They remain open items owned by Klas, and they **no longer gate a prod tag**:

- **STOPP-1** (PRODUCT) — Tier A strips every email and phone from every ad, recruiter role-inboxes
  included; the over-redaction posture is accepted, not resolved.
- **STOPP-2** (LEGAL) — whether *"annonsen är redan publicerad hos Arbetsförmedlingen"* defeats an
  Art. 17 request. **No legal ground is recorded here; none was given.**
- **STOPP-3** (LEGAL) — Tier B does not reach applicants' frozen `AdSnapshot` copies (D9, a recorded
  exception in favour of the applicant).
- **STOPP-4** (FACT) — **the Hetzner backup/PITR retention window is still not stated.** Two records
  tie things to it, and they tie *different* things: ADR 0032 B1 ties only the **DPIA signature**
  (*"Independently, the DPIA cannot be signed until…"*), while **ADR 0024 `:636` and the Art. 30
  register tied BOTH the DPIA signature and a `v*` tag** to it, in a single sentence — which is how
  the two came to be read as one instrument. **They are separate.** This decision lifts the launch
  gate and **does not sign the DPIA**: STOPP-4 remains open, still blocks the DPIA, and CC must still
  not invent the window. **The rule, stated once and without a site list:** wherever a record binds the
  DPIA signature and a `v*` tag in a single sentence, **this entry supersedes the `v*` clause and leaves
  the DPIA clause standing.** No enumeration appears here — the first draft of this paragraph named two
  sites, was wrong about one of them and silent about a third **inside this very file**, which is the
  defect §C6 of ADR 0032 exists to stop. Search the shape, do not trust a list.
- **STOPP-5** (VISIBILITY) — the backfill's term-delta was to be seen by Klas before acceptance.
- **LIA K3** and **notis-copy K4** — untouched.

### Why the lift is recorded as a decision rather than an inference

The three records disagreed on what the gate's condition was: ADR 0032 B1 said Tier B alone; the
erasure runbook said both tiers; this ADR said both tiers plus #D3 plus the Klas-side points. All
three technical formulations are now satisfied, but the Klas-side points are not, so **no reading of
the record derives the lift on its own.** Recording it as Klas's dated decision is the only honest
form — and it is what stops the next reader from re-deriving a different answer from whichever of the
three documents they happen to open. Art. 5(2) accountability: the record says who decided, when, on
what evidence, and what remained open.

**Cross-references, none of which restate the status:** ADR 0032 B1/B7/B8 + Amendment 2026-07-26 §C6 ·
ADR 0024 §3/§3.2 · ADR 0049 §F · `docs/runbooks/recruiter-pii-erasure.md` §0 · ADR 0090 (DPIA — see
STOPP-4).

## Amendment — 2026-10-03, #1944 contact placement and notice presence

Klas decided that the local contact-person notice is hidden without available permitted contacts
and always visible with any available permitted declared or derived contact. This supersedes
the earlier unconditional local presence and the preserved-panel text-or-contact gate.

Application detail uses only its existing frozen snapshot contacts, after status actions and
before follow-ups in the shared full-page/modal body. The preserved-ad panel no longer duplicates
the contact block or notice. Job-ad detail uses its current permitted detail contacts for the
same gate. Name-only, email-only and phone-only contacts admitted by the producer count.

The whole sv/en notice and public /kontaktperson-i-annons route remain unchanged. This is a
presentation binding; it does not establish an Art.14 exception or amend information duties.
The typed carrier, declared/derived provenance, owner-scoped detail access, surgical snapshot
erasure, whole-ad remedy and existing terminal minimization remain intact. Removed contacts
are never restored from a live ad or a reopened application. Retention duration is unchanged.
