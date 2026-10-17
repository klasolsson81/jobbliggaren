# ADR 0171 — The public job page carries a `JobPosting` data block: built in the frontend, kommun and län named by the handler, and the page shows the facts its markup asserts

**Date:** 2026-10-17
**Status:** Accepted 2026-10-17. The §9.2 panel's round on PR #2160 (code-reviewer, dotnet-architect, security-auditor, test-writer, design-reviewer; verdict table in the PR body) closed every finding in-block; `security-auditor` signed the ADR 0166 amendment the same day. Klas reviews the diff post-merge (CLAUDE.md §9.2).
**Deciders:** Klas Olsson (controller and product owner; two answers in chat on 2026-10-17, recorded in D7) · `senior-cto-advisor`, "the CTO" below
(decision-maker under CLAUDE.md §9.2; ruling on forks F1–F7, report-only; `docs/reviews/2026-10-17-2090-s3-plan-cto.md`) · `security-auditor`
(plan read, report-only; `docs/reviews/2026-10-17-2090-s3-plan-security-auditor.md`: her verdict, M1, M2, Minor 3 and the escalation) ·
`dotnet-architect` (form read, report-only; `docs/reviews/2026-10-17-2090-s3-plan-dotnet-architect.md`)
**Amends:** ADR 0160 (D4, D7), ADR 0165 (D4) and ADR 0166 (D4 W4, D5, D10), each by a dated amendment at the end of that file. Nothing above those
amendments is rewritten.
**Related:** ADR 0043 (the label port; Beslut A rejected an FE concept-id table; Amendment 2026-06-13 made Klass 2 an FE presentation concern) · #2090 (the epic)
· #2091 · [#2159](https://github.com/klasolsson81/jobbliggaren/issues/2159) (S3) · #1537 (Klass 2 is named by the FE catalogue `coded-taxonomy.ts`) · #841 and
ADR 0087 D8(c) (a sole trader's organization number is never published)
**Measured against:** the worktree `C:/tmp/jbl-seo-s3` at `911e48268`, 2026-10-17. Google Search Central, job posting structured data (the page is dated
2026-09-08, read 2026-10-17): https://developers.google.com/search/docs/appearance/structured-data/job-posting. What that page says, as relied on here: the
required properties are `title`, `description` (HTML), `datePosted`, `hiringOrganization` and `jobLocation`, with `addressCountry` the one required address
field; `validThrough` is "required for job postings that have an expiration date" and is omitted when there is none; `employmentType` takes one or more of the
case-sensitive values `FULL_TIME`, `PART_TIME`, `CONTRACTOR`, `TEMPORARY`, `INTERN`, `VOLUNTEER`, `PER_DIEM`, `OTHER`; "all information in the markup must be visible
on the job page"; an expired posting is removed by a `validThrough` in the past, by a 404 or 410, or by removing the markup.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. D1–D6 follow the CTO
> ruling and the two plan reads named above; D7 records Klas's two answers of 2026-10-17. The mandatory agents review the implementation in the PR. Nothing in
> the *Implementation* list is stated as measured beyond what D8 and that section say.

---

## Context

Step S3 of the SEO epic (#2090, issue #2159) puts a schema.org `JobPosting` data block on the public job page `/jobb/<uuid>` (ADR 0165), so that a search
engine's job feature can read the posting as structured data. The page and its anonymous read already exist (ADR 0160, ADR 0165). Four things were open when S3
started.

- **Labels (ADR 0160 D7).** The anonymous read leaves the five dimensions as concept ids and injects no `ITaxonomyReadModel`; "S3 decides per dimension whether a
  value is coded or named (#1537)". Google's `jobLocation` needs a place name, not an id.
- **Who owns the mapping to Google's vocabulary and the decision that an ad is complete enough for it.**
- **What the page shows.** Google states that all information in the markup must be visible on the job page. The page, as ADR 0165 D4 delivered it, shows no
  place and no employment type.
- **The legal record.** ADR 0166 rests its necessity test (D2) on S3's markup carrying the full description, but its safeguards read "metadata from structured
  fields only" (D5) and "a preview carries no free text" (W4). After S3 a reader would take that safeguard as broken, because the data block carries the full
  text. `security-auditor` asked for the record to be made true (Art. 5(2)) and for Klas, as the named reader of ADR 0166 D10, to say whether S3 fires trigger 1.

## Decision

The public job page `/jobb/<uuid>` carries one `JobPosting` JSON-LD block when the ad is complete enough for Google's schema (D3), built by a pure frontend module
from the anonymous DTO (D2); kommun and län are named by the backend handler through the existing taxonomy port (D1); the page shows the facts the markup asserts
(D6); and the legal record is made true by dated amendments to ADR 0160, ADR 0165 and ADR 0166, of which the last waits for the signature of `security-auditor`
(D7).

### D1 — Labels per dimension (ADR 0160 D7 resolved)

- **Kommun and län are named by the backend.** `GetPublicJobAdQueryHandler` injects `ITaxonomyReadModel` (the ADR 0043 port, an in-memory cached snapshot, no
  database hop) and makes one `ResolveLabelsAsync` call over the non-null municipality and region concept ids. The call is made **only after the second `Classify`
  returns Listed**: never for a 410 and never for a 404. A null id is never sent. An id the snapshot does not know gives a null name, never an exception.
- **`PublicJobAdDto` grows from 12 to 14 members:** `MunicipalityName` and `RegionName`, both nullable. The concept ids stay on the DTO. These are register data
  and proper nouns, so they are Swedish in every locale.
- **Employment type and worktime extent stay coded.** The frontend catalogue `web/jobbliggaren-web/src/lib/i18n/coded-taxonomy.ts` names them per locale (#1537).
  A backend label would be Swedish only, and mapping a label *string* to Google's vocabulary would be magic-string matching across a boundary (AGENTS.md §5).
  The mapping is keyed on the concept id (D5).
- **Occupation group is not resolved.** S3 has no consumer for it; it belongs to S6.
- **Pins.** `PublicJobAdDtoContractTests` pins the 14-member set. The wire test in `PublicJobAdEndpointTests` pins the member set of the JSON and carries a real
  snapshot label on the wire (Stockholm and Stockholms län). `TaxonomyAclLayerTests` lists the handler in its allowlist (the port's first anonymous consumer, read
  side only). `DidNotReceive` is asserted on the port on every non-listed path, including `PublicJobAdReadRaceTests`: an ad erased or archived between the two reads
  never reaches the port.

### D2 — The mapper's home is the frontend

A pure module `web/jobbliggaren-web/src/lib/job-ads/job-posting-json-ld.ts` exports `buildJobPosting(dto): JobPostingJsonLd | null`. `PublicJobAd` renders the
result through `JsonLdScript` (S0's serialiser, which writes `<` as `\u003c`).

- **Clean Architecture** (Martin 2017, ch. 22): Google's vocabulary (`FULL_TIME`, `PostalAddress`, its HTML-tag subset) belongs to one delivery mechanism, the web
  page for one search engine. Application stays agnostic of it.
- **DRY as knowledge** (Hunt/Thomas 1999): the `description` HTML must equal what the page shows, and what the page shows is decided by `parseAdDescription`, which
  runs on the frontend. A backend mapper would need a second block parser in C#, so one rule would have two homes.
- **CCP** (Martin 2017, ch. 13): the rule that the markup must be visible on the page binds the markup to the rendered page. They change together, so they live
  together.
- **This does not conflict with the epic's one publication predicate.** Eligibility here is a different piece of knowledge from publication: whether a Listed DTO
  is complete enough for Google's schema. The mapper takes no clock, reads no status, and never re-checks `expiresAt` against now. A fitness test pins that.

### D3 — Eligibility

`buildJobPosting` returns `null` (no script, the page unchanged and still indexable) unless **all** of these hold:

- `title.trim()` is non-empty;
- `companyName.trim()` is non-empty;
- `parseAdDescription` returns at least one block, and the description differs from the title (trimmed, compared case-insensitively);
- a resolved `MunicipalityName` is present. That is the one fact `addressCountry: "SE"` rests on. A län alone does **not** qualify: Google's text is ambiguous on
  it, and widening on an ambiguous reading invites Search Console warnings. This reopens at phase-1 close, on the Rich Results Test data that is on Klas's
  checklist. No issue is filed for it; it is a scheduled check, not a claim about the code;
- `employmentTypeConceptId` is not Arbete utomlands (`9Wuo_2Yb_36E`). `addressCountry: "SE"` for work abroad would be an invented fact.

### D4 — The shape

The exact key set is:

- `@context`, `@type: "JobPosting"`, `title`, `description`;
- `datePosted`, ISO 8601, from `PublishedAt`;
- `validThrough?`, ISO 8601, from `ExpiresAt`, present only when the ad has a deadline. A listed ad's deadline is never past (ADR 0160 D2);
- `hiringOrganization { @type: "Organization", name }` and **nothing else** in it: no `taxID`, `vatID`, `identifier`, `leiCode`, `sameAs`, `logo` or `address`. A sole
  trader's company name is the same string that is already in the page's company line and in `<title>` (ADR 0166 W2). There is **no** "confidential" substitution
  driven by `IsPersonnummerShaped` (ADR 0087 D8);
- `jobLocation { @type: "Place", address { @type: "PostalAddress", addressLocality: kommun, addressRegion?: län, addressCountry: "SE" } }`, with no `streetAddress` and
  no `postalCode`;
- `employmentType?`, an array, the extent value first, deduplicated, omitted when empty.

**Never emitted:** `baseSalary`, `directApply`, `identifier`, `jobLocationType`, `applicantLocationRequirements`, `url`, `occupationalCategory`, `industry`. A `url`
member holding an Arbetsförmedlingen id would put in machine-readable form the uuid-to-AF-id join that ADR 0160 D3 names as the erased-ad oracle.

**The description HTML** (`security-auditor` M1) is built from `parseAdDescription`'s blocks and never from the raw string. A heading block becomes `<p>`; a list
becomes `<ul><li>`; a soft line break inside a paragraph becomes `<br>`. Every text run is HTML-escaped (`& < > " '`) **before** a tag is wrapped around it. Only
`p`, `ul`, `li` and `br` are emitted. `JsonLdScript` then escapes `<` as `\u003c`, so the block cannot close itself. The escaping protects the consumers who render the
HTML string, which `\u003c` alone does not; `security-auditor` grades an unescaped delivery as a Blocker.

### D5 — The `employmentType` table

Keyed on the frozen Klass 2 concept ids, as an exhaustive `Record<CodedTaxonomyId, …>`, so a new id is a compile error.

| Klass 2 value | Google value |
|---|---|
| Heltid | `FULL_TIME` |
| Deltid | `PART_TIME` |
| Tidsbegränsad anställning, Vikariat, Säsongsanställning, Sommarjobb / feriejobb | `TEMPORARY` |
| Behovsanställning | `OTHER` |
| Vanlig anställning, Tillsvidareanställning | no value |
| Arbete utomlands | the whole ad is ineligible (D3) |

- **Behovsanställning is `OTHER`, not `PER_DIEM`.** Google defines `PER_DIEM` as paid by the day, which an on-call job does not say; `PER_DIEM` would be an invented fact.
- **Permanence is not a Google axis.** Calling a permanent job `OTHER` would also be false; the extent value carries what is true.
- **An id the catalogue does not know** (taxonomy drift) is ignored by the mapper and never fails the page. The zod schema reads the two ids as plain nullable
  strings, not enum-validated.

### D6 — Visibility: S3 renders the facts its markup asserts

`PublicJobAd` gains a facts row under the dates: kommun before län as the snapshot names them ("Stockholm, Stockholms län"), and the employment type and extent as
the locale catalogue names them ("Vikariat · Heltid"). Each carries an sr-only axis name (`publicjob.facts.location` = "Ort", `publicjob.facts.employment` =
"Anställningsform och omfattning"). An id the catalogue does not know is left out; the row is absent when nothing is known.

- **Ground.** Google, verbatim: "All information in the markup must be visible on the job page". `jobLocation` is required, so the location has to be visible
  anyway, and `employmentType` is in the S3 scope. Markup that claims facts the page does not show is a defect in delivered code, not something to defer.
- **Consequence for S6.** S6's "visible taxonomy labels" row loses kommun, employment type and extent, and keeps occupation group, the explainer links and related
  ads. The driving session edits the S6 row in #2090; no new issue.
- **`design-reviewer` joins the panel** for this row, and the row's absent states (no kommun, an unknown coded id, nothing at all) are rendered before her verdict
  (AGENTS.md §8 point 4).
- **CSS.** `.jp-modal__facts`, `.jp-modal__fact` and `.jp-modal__fact-icon` share the date row's geometry under their own names.
- **This is the CTO's call, not a scope change.** It changes the order of steps in the epic, not its scope. Klas may override it ("keep labels in S6, accept invisible
  markup until then").

### D7 — The legal record (ADR 0166)

`security-auditor` read S3 against ADR 0166 D10 trigger 1 and concluded that it does **not** fire, provided M1 (D4) and M2 (the amendment) hold. Her grounds: it is the
same page and the same predicate, and none of trigger 1's examples (a sitemap, a catalogue, a second source, an employer profile, a new page that carries ad text)
applies; ADR 0166 D2 already rests its necessity test on S3's markup carrying the full description; every value comes from the anonymous read the page already makes;
and Google for Jobs and Bing in their job features are features of controllers that D7 of ADR 0166 already names as "search engines". Because D10 names Klas as the
reader of the trigger list, she put two questions to him, relayed verbatim the same day. His answers, 2026-10-17, in the session's chat:

1. **"Ja, ingen trigger (Rek.)"**: S3 does **not** fire ADR 0166 D10 trigger 1.
2. **"Ja, godkänt (Rek.)"**: a dated amendment to ADR 0166, written as the ADR 0166 amendment of 2026-10-17. The `recruiterNotice` text is **not** changed: he did not
   choose the clarification that `security-auditor` offered as his option.

Her reading holds only while M1 and M2 hold. Her signature of 2026-10-12 does not cover the amendment; she signed it on 2026-10-17, on her review of PR #2160 at head `144fda24c` (the signature text is in the amendment).

### D8 — What is not measured

- How many aggregators ingest our markup.
- Google's uptake of the markup, and the Rich Results Test on a live ad (Klas's, in his own browser, at phase-1 close).
- Whether Search Console's Removals tool reaches Google's jobs experience.

## Alternatives considered

### Alt A — The chosen design (D1–D7)
**For:** one publication predicate still; nothing is invented; the PII boundary is still the DTO's pinned member set, grown by two names that are not personal data;
markup is a subset of the visible page by construction and by test; the mapping lives where the page's text is decided.
**Against:** two DTO members and a port injection on a handler that ADR 0160 deliberately kept taxonomy-free; part of S6 moves into S3; län-only ads get no rich
result in phase 1; the full description now travels in a form built for third parties to ingest (the ADR 0166 amendment carries that).

### Alt B — A separate label read (F1 b)
**For:** keeps the ad read free of the taxonomy port.
**Against:** a third API read on every 200 view, on top of the two that ADR 0165 D3 accepted under ADR 0160 D5's 60 per 60 s; it would also need a second anonymous surface.

### Alt C — A frontend table for kommun and län (F1 c)
**For:** no backend change.
**Against:** ADR 0043 Beslut A rejected it as a second source of truth for 290+ register concepts. The Klass 2 table is a different case: ten frozen ids pinned by a
parity test against the one source.

### Alt D — The mapper and eligibility in the backend (F2 b)
**For:** one place for the whole public contract.
**Against:** it puts Google's enums in Application, duplicates the block parser, and splits the markup-equals-visible agreement across two deployables.

### Alt E — Widen `jobLocation` to län-only ads (F4)
**For:** more ads would carry a rich result.
**Against:** Google's text is ambiguous (its troubleshooting line says locality or region, every example carries a locality); the cost of not widening is small because
the page is still indexed. Reopens at phase-1 close on Rich Results Test data.

### Alt F — Accept the visibility gap until S6 (F4)
**For:** S3 stays a pure markup step.
**Against:** it knowingly ships a breach of Google's content guidelines.

## Consequences

### Positive
- One publication predicate; eligibility for Google's schema is a separate, pure, testable function.
- Nothing is invented: unknown ids degrade to a missing value, `OTHER` instead of `PER_DIEM`, no `SE` for work abroad.
- The PII boundary is still the DTO's pinned member set, grown by two names that are not personal data.
- Markup is a subset of the visible page, by construction and by test.
- The legal record states what the page now does (Art. 5(2)), and ADR 0166 D10 gains triggers that catch a push, a second page and a widened key set.

### Negative
- Two DTO members and a port injection on a handler that ADR 0160 kept taxonomy-free.
- Part of S6 moves into S3.
- Län-only ads get no rich result in phase 1.
- The full description now travels in a form built for third parties to ingest. How many do is not measured (D8). The ADR 0166 amendment carries this.
- Google's jobs experience can lag behind a removal; the 410 and `validThrough` are the removal paths, and whether the Removals tool reaches that surface is not measured.

## Implementation

Files as delivered on the branch `feat/seo-s3-jobposting-jsonld`:

- **Backend:** `GetPublicJobAdQueryHandler.cs`, `PublicJobAdDto.cs`, and the tests `PublicJobAdDtoContractTests.cs`, `PublicJobAdEndpointTests.cs`,
  `PublicJobAdReadRaceTests.cs`, `PublicJobAdDeadlineAgreementTests.cs`, `TaxonomyAclLayerTests.cs`.
- **Frontend:** `src/lib/job-ads/job-posting-json-ld.ts` (+ test), `src/lib/dto/public-job-ad.ts` (+ test), `src/lib/i18n/coded-taxonomy.ts` (`isCodedTaxonomyId`
  exported), `src/components/job-ads/public-job-ad.tsx` (+ test), `src/app/(public-job)/public-job-group.test.ts`, `public-job-pages.test.tsx`, `src/app/globals.css`,
  `messages/{sv,en}/publicjob.json`, and `scripts/public-job-smoke.mjs` (one parsable block on a complete ad; none on an incomplete one or on the 410 and 503 pages).
- **Runbook:** `docs/runbooks/recruiter-pii-erasure.md` step 2.4a item 6 names the jobs-experience lag (`security-auditor` Minor 3).
- **Tests run locally 2026-10-17:** Application unit (GetPublicJobAd 13/0), Architecture 837/0, Api PublicJobAd 15/0, the touched web files 106/0. The production
  build, the smoke script and the full web suite are run by the session; results are in the PR body.
- **Recording.** This ADR lies in the ignored 0071–0199 range and needs `git add -f` and a README row (precedent 0167–0170). ADR 0160, 0165 and 0166 are tracked and
  carry their amendments in the same PR.

## References

- `docs/reviews/2026-10-17-2090-s3-plan-cto.md` · `docs/reviews/2026-10-17-2090-s3-plan-security-auditor.md` · `docs/reviews/2026-10-17-2090-s3-plan-dotnet-architect.md`
  (all three transcribed verbatim by the driving session; local)
- ADR 0160 (D2, D3, D4, D5, D7) · ADR 0165 (D3, D4) · ADR 0166 (D2, D4, D5, D7, D10) · ADR 0043 (Beslut A, Amendment 2026-06-13) · ADR 0087 D8
- Martin, *Clean Architecture* (2017), ch. 13 (CCP) and ch. 22 · Hunt/Thomas, *The Pragmatic Programmer* (1999), DRY · Evans 2003, ch. 14 (ACL)
- Google Search Central, job posting structured data (dated 2026-09-08, read 2026-10-17):
  https://developers.google.com/search/docs/appearance/structured-data/job-posting. The page changes; the date says what was read.
- #2090 · #2091 · #2159 · #1537 · #841
