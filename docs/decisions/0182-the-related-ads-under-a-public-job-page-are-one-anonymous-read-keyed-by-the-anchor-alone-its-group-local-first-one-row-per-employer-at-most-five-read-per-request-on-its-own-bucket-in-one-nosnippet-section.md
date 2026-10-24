# ADR 0182 — The related ads under a public job page are one anonymous read keyed by the anchor alone: its occupation group, same kommun then län then Sweden, one row per employer and never the anchor's, at most five, content read per request through the one predicate, on a bucket of its own, in one `data-nosnippet` section

**Date:** 2026-10-23
**Status:** Accepted 2026-10-23 (the CTO's ruling under CLAUDE.md §9.2 and Klas's answers of the same day, D6). The ADR 0166 amendment of the same date, which carries the legal outcome (D6),
is Accepted: `security-auditor` signed it at her scoped re-check of the PR on 2026-10-24. The merge waits for that signature, because a merge is a go-live (ADR 0154).
**Deciders:** Klas Olsson (controller and product owner; answers in chat on 2026-10-23, recorded in D6, and his share of `hotspot:i18n` for `messages/{sv,en}/publicjob.json` only) ·
`senior-cto-advisor`, "the CTO" below (decision-maker under CLAUDE.md §9.2; ruling on forks F0–F6, report-only, `docs/reviews/2026-10-23-2090-s6-plan-cto.md`) · `security-auditor` (plan read,
report-only, `docs/reviews/2026-10-23-2090-s6-plan-security-auditor.md`: her verdict, M1–M3, m1–m5, S1–S16, triggers 15–17 and the escalation) · `dotnet-architect` (form read, report-only,
`docs/reviews/2026-10-23-2090-s6-plan-dotnet-architect.md`) · `design-reviewer` (plan read, report-only, `docs/reviews/2026-10-23-2090-s6-plan-design-reviewer.md`)
**Amends:** ADR 0064 (rule (d)), ADR 0106 (the D9 read-path table), ADR 0160 (D2, D5), ADR 0165 (D3), ADR 0176 (D5) and ADR 0166 (the dated amendment that carries the legal outcome, D6), each
by a dated amendment at the end of that file. Nothing above those amendments is rewritten.
**Related:** [#2090](https://github.com/klasolsson81/jobbliggaren/issues/2090) (the epic) · [#2203](https://github.com/klasolsson81/jobbliggaren/issues/2203) (S6, the issue this ADR ships
under; S6a merged as #2204, this is S6b; not `mvp`) · ADR 0176 (S5, the catalogue and the six-member row) · ADR 0171 (S3, the `JobPosting` block; Alternative B) · ADR 0165 (the job page and
its probe) · ADR 0160 (the publication predicate and the anonymous read) · ADR 0113 (every `job_ads` read site carries a written lifecycle decision) · ADR 0120 (a rendered count is true or it
is absent) · ADR 0045 (budgets) · ADR 0043 (the taxonomy port) · ADR 0154 (§3: every merge is a deploy) · DPIA #456 §8-B (local) · #2198 (owns `hotspot:css`) · #2133 (the Art. 9 remedy,
closed unbuilt)
**Measured against:** the branch `feat/seo-s6b-related` in `C:/tmp/jbl-seo-s6b`, cut from `515b7424b` (main after S6a, `7c94d538b`), at its four code commits `b60b9e824`, `1220f1eed`,
`f4859702d` and `aec9e2769`, read on 2026-10-23. The four plan reads were taken in `C:/tmp/jbl-seo-s6` on 2026-10-23, before S6a merged (`security-auditor` and `design-reviewer` name
`f459e87ba`). Nothing here is merged, and the merge is the go-live.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. D1–D5 and D7 follow the CTO's ruling. The
> architect's form read is marked "(architect)", `security-auditor`'s "(security-auditor)" and `design-reviewer`'s "(design)" where each changed or added to it. D6 follows
> `security-auditor`'s plan read and records Klas's answers. The four reports are local files, transcribed verbatim by the driving session from the agents' hand-backs. Facts about the code
> were read at the four code commits on 2026-10-23. The numbers under *Measurements* are the driving session's of that date, and none was re-run for this ADR. What no source measures is
> listed under *What is not measured, and the measurements owed*. The mandatory agents review the delivered diff in the PR. The ADR lies in the ignored 0071–0199 range and needs `git add -f`
> and a README row (precedent 0167–0181).
>
> **Numbering.** 0182 was measured free on 2026-10-23, before this file was written: in this worktree, in the main copy, in every other `C:/tmp/jbl-*` worktree and in every open PR (two
> Dependabot branches).

---

## Context

Step S6b of the SEO epic (#2090, issue #2203) shows other listed ads under the article on the public job page. The CTO split S6 into two PRs on #2203 (F0, Klas: "Två PR (Rek.)"). S6a, the
occupation group in the facts row and two explainer links, merged as #2204 and fired no ADR 0166 D10 trigger. S6b adds a read model, an endpoint, a selection rule and a budget, and its legal
gate is a fired trigger 1. Nine facts shape the decision.

- **The purpose is the visitor's, not search's.** The stage-B ruling says S6 "exists for users, not for search" and goes ahead whatever the indexing branch (the CTO, quoting
  `2026-10-18-seo-audit-stage-b-cto.md:186`). So user relevance is the main axis of every fork below, and search value and privacy are constraints on it.
- **The predicate and the row are already decided.** `JobAdPublication.IsListed(now)` is the one publication predicate: source Platsbanken, status Active, deadline not passed. ADR 0160 D2
  names "S6's related ads" as a list consumer. The six-member `PublicJobAdSummaryDto` and the component `PublicJobAdList` are the catalogue's (ADR 0176 D5).
- **The erasure rule is absolute** (ADR 0160 D6; ADR 0064, Amendment 2026-10-10). Any cache is a window in which an erased ad is still listed, so a row's content is read on every request.
- **The page now carries other ads' values.** Until S6b the job page held its own ad's values only. The section puts other ads' titles and employers on it, from a read the page did not make
  before (the CTO, F6). That is why ADR 0166 D10 trigger 1 fires (D6).
- **Locality is the main relevance axis in job search.** `/jobb`'s primary filters are kommun and län, and newest-first across Sweden mostly shows jobs a local job-seeker cannot take (the CTO,
  F1).
- **Art. 9.** An ad for personal assistance can describe the person who is to receive the assistance. Ads about one assisted person come from one provider, in one kommun, usually published
  together (the CTO, F1; security-auditor). Klas's decision of 2026-10-11 is that no occupation group is excluded (ADR 0166, Amendment 2026-10-11), and a group carve-out is a decision he
  declined.
- **A filter surface has been rejected twice.** ADR 0176 D1 and ADR 0166 D10 trigger 12 reject an anonymous facet API, and ADR 0005's path to one was rejected outright (ADR 0176, Alternatives,
  F3). A related read that took a group or a place from its caller would be one.
- **ADR 0171 Alternative B does not bind here.** It rejected a third read on the detail path because it spent the same 60-per-60 budget and because the labels of *this* ad belong in *this*
  ad's read. Neither holds for related ads: the budget is separate, and related ads are not this ad's data (the CTO, F2).
- **The size of the sets** is in *Measurements*: the corpus the box holds, the largest occupation group and the listed-per-group distribution, read on 2026-10-23. The index on the group
  column exists, so no migration is needed (the CTO).

## Decision

**The job page `/jobb/<uuid>` shows up to five other listed ads under the article, read by `GET /api/v1/public/job-ads/{id:guid}/related`. The read is keyed by the anchor alone: the anchor's
occupation group is the only relation, and its group, kommun, län and employer are read from its row on the server and never from the caller. The rows are ordered same kommun, then same
län, then the rest of Sweden, newest first, with one row per employer and never the anchor's. Each row's content is read and classified through `JobAdPublication.IsListed(now)` on every
request, and a row is the catalogue's six-member row. The endpoint has a bucket of its own, `public-job-ad-related`. The page reads it in parallel with the ad, in the page body only, and
shows no section when it returns no rows or fails. The rows sit in one `<section data-nosnippet>` under a generic heading, and nothing of them reaches the page's title, meta tags or
JSON-LD. The legal outcome has one home, the ADR 0166 amendment of the same date.**

**Scope.** S6b's scope is the CTO's F0–F5 as they touch the related section, plus what the three plan reads changed. A link from the section to a list of the group or the place, a count, a
group name in the heading, an `ItemList`, or any key a caller can supply is a new decision and not an edit of this one (D5; ADR 0166 D10 triggers 15–17).

### D1 — The selection rule: one group, local first, distinct employers, shown from one row (CTO F1; security-auditor S7)

- **Candidates.** `IsListed(now)`, the anchor's `OccupationGroupConceptId`, `Id` not the anchor's, an organization number, which is not the anchor's, and
  `Company.Name` not the anchor's.
- **Order.** Same kommun first, then same län, then the rest; within a tier `PublishedAt` descending; ties in the read's SQL order, which is Postgres's `uuid` order, and never compared in
  memory (ADR 0176 D3, the two id orders). An anchor whose kommun or län id is null skips that tier, and a null never matches a null.
- **One row per employer.** The code walks the SQL-ordered rows and takes the first row of each new employer, until `RelatedPublicJobAdLimits.MaxRows` = 5. An employer is its organization
  number, compared ordinally, and two rows with one company name are one employer too: one definition of the same employer, for the anchor's exclusion and for the walk (the CTO's ruling on
  security-auditor's Major 1, round 1, superseding F1.3's "ordinal on the projected company name"). If the window holds fewer than five distinct employers, the section shows fewer. There
  is no second query.
- **The floor is one.** The section shows from one row, and at zero it is absent, with no empty state. The CTO rejected a floor of 3 as an unexplained threshold (AGENTS.md §5, magic
  numbers) that hides one or two true and useful rows for a look.
- **No fallback.** A null group, or a group with no other listed ad from another employer, gives no section. An anchor without an organization number gives no section either, and an ad
  without one is never a row: the employer of either cannot be told apart from a spelling. Filling from the anchor's kommun or län across groups is rejected: it would be a
  related list with no relation and, in practice, a per-ad place listing, the axis that stage-B B1 gates and that ADR 0166 `:657` places under trigger 1.

**Why local first.** Locality is the main relevance axis (Context). The CTO's rule is a user rule before it is a search rule.

**Why distinct employers, and the anchor's excluded.** Three independent reasons, each enough alone (the CTO):
- *(a) User value.* A large employer's ad page would otherwise get five near-copies of itself. That is the "content that adds no value" Google names (as the CTO's brief read the
  helpful-content page, updated 2026-10-05), and it helps no reader.
- *(b) It stops the section becoming an employer listing.* ADR 0166 D10 trigger 1 names "an employer profile", and stage-B B3 states the same rule for hubs: an employer majority turns a list
  into a company page.
- *(c) It carries Art. 9.* Ads about one assisted person come from one provider, so one row per employer, and never the anchor's, keeps two of them out of any one section, on every page and
  not only the anchor's. **The rule is therefore load-bearing for Art. 9 as well as for relevance** (security-auditor S7, who required this ADR to say so). It is one rule for every group and
  not a carve-out of one, which Klas declined. The legal weighing is in the ADR 0166 amendment (D6).

**The pre-ruled national fallback is not applied.** The CTO ruled in advance that, if `security-auditor` graded the kommun × group juxtaposition (different persons in one small kommun) a
Blocker or Major, the kommun and län tiers would be dropped: newest first in the group, all of Sweden, the employer rule kept, one `ORDER BY` term. She graded it neither. Her reasons: composition
is what Art. 9 juxtaposition could add and the employer rule blocks it; ads about different persons side by side reveal nothing about any of them beyond what each row already shows on its own
page and in the catalogue; and a national order buys no Art. 9 reduction, since it puts rows from the same group on the same number of pages and costs the section its purpose. Her Art. 9
Blocker finding keeps its grade and stays unsigned, and the override stays Klas's. Excluding a group is not the fallback; that remains his call. Applying the fallback later would be a narrowing,
which she deliberately leaves out of the legal triggers, and the CTO needs no new round for it.

### D2 — The read: a separate anonymous endpoint, keyed by the anchor alone, through the one predicate (CTO F2; architect F1–F3; security-auditor m1)

- **The endpoint.** `GET /api/v1/public/job-ads/{id:guid}/related` in the anonymous `PublicJobAdsEndpoints` group (ADR 0160 D1). **The keys come from the anchor row on the server, never from
  the caller; this is a hard condition** (the CTO). A `?group=&kommun=` form would be the anonymous facet API that ADR 0176 D1 and ADR 0166 D10 trigger 12 reject. The query record has one
  member, `Id`. It is neither `IAuthenticatedRequest` nor `ICapturesRecentSearch`, and the handler logs nothing. The anchor's company name and organization number are filter parameters and are never returned or logged; the
  candidates' organization numbers stay in the handler's private row, and the handler is on `OrganizationNumberSurfacingGuardTests`' log-boundary scan.
- **The anchor read composes the predicate (security-auditor m1; architect Nice-to-have, taken).** `Where(j.Id == id).Where(JobAdPublication.IsListed(now))` projects five columns: group,
  kommun, län, company and organization number. So every non-listed anchor reads no row and answers the same `JobAdErrors.NotFound`: unknown, never imported (a manual row with `Source = "Platsbanken"` included),
  archived, past its deadline and erased. There is no `Classify` switch that could grow a 410 arm; this changes step 1 of the CTO's F2, which read the anchor and classified it through
  `Classify`. The detail read already tells 404 from 410, so this route tells strictly less and is no new erasure oracle. A listed anchor with a null group or without an organization number answers 200 with an empty list, makes no second read and does not ask the taxonomy port.
- **The candidates read.** `.Where(IsListed(now))`, the group, `Id` and company filters, `OrderBy(CASE)`, `.ThenByDescending(PublishedAt).ThenBy(Id).Take(CandidateWindow)`, projected to a narrow
  row. It never materialises the entity, because `RawPayload` carries the pre-scrub text. The group
  is a non-null value by the time this read runs (`is not { } group`), so the predicate `occupation_group_concept_id = @group` implies the condition of the partial index on that column; a
  null-safe `OR … IS NULL` arm would switch the index off silently, and `TheCandidatesStatement_ReachesTheOccupationGroupIndex` pins it (architect).
- **The tier is an `int` CASE rank, never a boolean sort key (architect, Kritiskt).** EF's C# null semantics read the parameter at run time and rewrite `col == @p` to `col IS NULL` when `@p` is
  null, so `OrderByDescending(j => j.MunicipalityConceptId == anchorKommun)` would put every row without a kommun in the anchor's own tier, which D1 forbids. A three-valued boolean also sorts
  NULLS FIRST under `DESC` in Postgres. The form is `kommun != null && j.MunicipalityConceptId == kommun ? 0 : lan != null && j.RegionConceptId == lan ? 1 : 2`: CASE takes a branch only on TRUE
  and ELSE makes the rank non-null, and the explicit guard means a null anchor id never produces an `IS NULL` arm whether the funcletizer keeps `kommun != null` as a parameter or prunes the
  branch. Precedent: `PerUserJobAdSearchQuery.GradeRankExpression`. One C# form is enough: EF caches one SQL variant per parameter nullability (the emitted SQL is under *Measurements*).
- **The window is `CandidateWindow` = 100 (architect).** The database's work is the heap fetch of the group and a top-N sort, which does not depend on the window. The window bounds only the
  rows sent and walked, about 25 KB at 100 narrow rows. The section comes up short only when at most four employers besides the anchor's hold all of the 100 highest-ranked candidates, and then
  every row it shows is still true: a declared, harmless underfill, pinned by a unit test. **W is set by cost and not by an employer distribution**, because such a distribution cannot be
  calibrated on the box: a count grouped by employer can name a person (CLAUDE.md §9.2 (a); a sole trader is a person).
- **Rejected for the window: `DISTINCT ON` behind a port.** `FromSql` nests as a subquery. With `.Where(IsListed)` outside it, `DISTINCT ON` picks a non-listed row per employer and the filter
  then drops it, so the employer disappears silently. With the predicate inside the SQL text it is a second definition of the publication predicate (ADR 0160: "None keeps a definition of its
  own"; ADR 0166 D10 trigger 16), and `JobAdLifecycleReadRegistry` cannot see a predicate inside a `FromSql` fragment (its non-reach 3). **Rejected: `GroupBy(company)` with `ROW_NUMBER`.** It is
  exact and core EF, but EF translates it to a `GROUP BY` subquery joined to a `ROW_NUMBER` subquery, two scans of the group, and the translation with a CASE rank and the owned key is unmeasured.
  The price of either is not worth the harmless underfill.
- **Two statement-level reads, not one round trip (architect F3).** One statement would need `db.JobAds` inside an expression tree (`SelectMany` or a correlated subquery), which is the
  registry's non-reach 6 (measured 2026-10-11) and makes the read invisible to it, and one statement cannot tell a 404 from an empty 200 without returning the anchor's state. The cost is one
  extra primary-key lookup, under a millisecond. The anchor needs no second classification, because nothing of its content leaves the handler: a race (the anchor erased between the two reads)
  gives at most one 200 with other listed ads, and the detail read decides the page's status on its own.
- **One naming call.** `PublicTaxonomyNames.ResolveAsync`, one `ResolveLabelsAsync` call for the shown rows' kommun and län ids only (at most five rows, not the window). The handler keeps
  `ITaxonomyReadModel` in its constructor, so `TaxonomyAclLayerTests` lists it (the third anonymous consumer).
- **The registry (ADR 0113).** Two `Active` entries under `…GetRelatedPublicJobAdsQueryHandler.Handle`, one for the anchor read and one for the candidates read, both at statement level.

### D3 — The wire: an envelope of the catalogue's row, a `Result<T>`, and one route parameter (architect F4; security-auditor S4, S5)

- **The shape.** `RelatedPublicJobAdsDto(IReadOnlyList<PublicJobAdSummaryDto> Items)`, on the wire `{"items":[…]}`. An envelope can grow without breaking a client, which a top-level array cannot;
  the precedent is `ListedJobAdIdsDto(IReadOnlyList<Guid> Ids)`. Each item is the catalogue's six-member row, **the same type and never a copy** (ADR 0176 D5), with a lower-case `id`.
- **The idiom.** `GetRelatedPublicJobAdsQuery(Guid Id) : IQuery<Result<RelatedPublicJobAdsDto>>` with `JobAdErrors.NotFound`, and the endpoint maps it as the detail does:
  `result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemResult()`. No closed union is needed, because there is no 503 arm: no snapshot is involved (the CTO). The validator is
  `RuleFor(q => q.Id).NotEmpty()`, parity with the detail's: `{id:guid}` lets the zero GUID through, so the validator answers 400, and the zero GUID names no ad, so the answer reveals nothing.
- **The pins.** `RelatedPublicJobAdsDtoContractTests` pins the envelope's one member and its type, the query's one member and that it implements neither `IAuthenticatedRequest` nor
  `ICapturesRecentSearch`. `PublicJobAdRelatedEndpointTests` pin the wire: one `items` member of six-member rows; an empty list for a group with no other employer; **one 404 wire across
  unknown, never imported, archived, past-deadline and erased anchors** (status, headers but `Date` and `Content-Length`, body but `traceId`); query keys a caller adds change nothing; the zero
  id is an uncacheable 400. `PublicJobAdRelatedBoundaryTests` (Architecture) holds the web's strict envelope to the DTO, and `PublicJobAdCatalogueBoundaryTests` holds the row.
- **The row type stays in the catalogue's slice namespace.** The architect's Nice-to-have, moving it to `Jobbliggaren.Application.JobAds.Queries` beside `PublicTaxonomyNames`, was not taken.
  The second consumer imports it from `GetPublicJobAdCatalogue`. This is where a reader finds that fact, and ADR 0176's D5 amendment says it too.

### D4 — The budget: a policy of its own, the helper's fourth call site (CTO F2; security-auditor approved the numbers)

- **`public-job-ad-related`** (`RateLimitingOptions.PublicJobAdRelated`): a TokenBucket per relayed client address, 60 per 60 s in 6 segments, `QueueLimit` 0, through
  `AddIpTokenBucketPolicy`, which gains its fourth call site. `security-auditor` approved the numbers on 2026-10-23.
- **Its own, never shared with `public-job-ad-read`.** A shared bucket would cut a visitor's views from 30 to 20 a minute (ADR 0171 Alternative B's own objection), and a related call could then
  starve the ad itself (least common mechanism, Saltzer & Schroeder 1975).
- **The cost of a view.** A 200 view makes two detail reads (the probe and the render) and one related read, so the detail budget binds first and ADR 0165 D3's "about 30 ads a minute"
  stands. A 404, 410 or 503 from the probe makes one read and no related read. The limit protects availability and not confidentiality: the cost of a call is one primary-key read and one
  group's indexed read.
- **`no-store`** comes from the `/api/v1/public` prefix block in `Program.cs`, on the 200, 404, 400 and 429.
- **Tests.** A wiring test (anonymous, its own policy, alone on its route, the id its only route parameter), a strict 429 test with `Retry-After` and `no-store`, a
  `ForwardedClientBudgetTests` row, the options default, and the `ApiFactory` and `StrictRateLimitApiFactory` rows. The three older policies' tests are byte-unchanged: the commit removes no
  line from them.

### D5 — The web: a fourth reader, a parallel read, and one `data-nosnippet` section (CTO F2 and the section's form; security-auditor S10, S11; design)

- **The reader.** `getPublicJobAdRelated` in `lib/api/public-job-ad-related.ts`: `forwardedHeaders()` only, which relays the two forwarding headers `pickForwardedHeaders` returns and so no cookie
  and no authorization (a test pins the key set), `cache: "no-store"`,
  `redirect: "manual"`, a 2-second timeout, Next's digest errors passed through, and every other outcome `unavailable`: any status but 200 (a 404 can only be the race after the edge's probe,
  and the ad's own read decides the page), a network error, the timeout, and a Zod failure. The envelope is strict over the catalogue's own strict row schema, so a member the backend adds
  drops the section instead of slipping into it unread (ADR 0166 D10 trigger 15). Nothing in the module logs a value. It joins the `(public-job)` group's closed list of readers, which grows
  to four, and `PUBLIC_SURFACE`.
- **The page.** `Promise.all([getPublicJobAd(id), getPublicJobAdRelated(id)])` in the page body, **never in `generateMetadata`**, and the rows are passed only on `ok`. A stall in the related
  read can delay the ad by at most the timeout and can never fail it (bulkhead and timeout, Nygard 2018, ch. 5). `proxy.ts` and the probe are unchanged.
- **The section.** `PublicJobAd` renders it inside the existing 760 px column (`mx-auto mt-10 w-full max-w-[760px]`), after the article and before the explainer block, so the order is
  article, related section, explainer links (design): content before promotion. It is one `<section aria-labelledby data-nosnippet>` with an `h2.jp-eyebrow`, then `PublicJobAdList` with
  `headingLevel={3}` and `idPrefix="related"` and `labelledBy` set to the `h2`'s id. The P1 refactor added those two closed props; the catalogue passes 2 and `"catalogue"` and its markup is
  byte-unchanged (a render of three rows hashed `abb18b7a…526a`, 5 304 B, before and after). The component stays typed to the six-member row, and its plain-anchor pin covers the related rows.
  A second list component was rejected, because it would copy the markup.
- **The heading is generic.** `publicjob.related.heading`: "Andra jobb i samma yrkesgrupp" / "Other jobs in the same occupational group". It names no group, because the label can be null and
  the group is named once in the facts row. It carries no count (a capped five is not a fact, ADR 0120), no "see more" link (the catalogue has no group filter, a link from the section to a
  list of the group is held by ADR 0166 D10 trigger 17, and such a list at its own address would be a new public page, trigger 1) and no "nearby" (false when the order falls through to the
  whole country). It stays when the group's id is known and its name is missing, because it is true then too. The
  key is in `messages/{sv,en}/publicjob.json` only, under Klas's share of `hotspot:i18n`.
- **Zero or `unavailable` means no section.** No heading, no empty container and no empty state, and the page is then S6a's page. Nothing on the page depends on the section.
- **Zero CSS.** The wrapper is utilities, the heading is `.jp-eyebrow`, the rows are `.jp-jobs` and `.jp-job`. `hotspot:css` is #2198's, so a design requirement for CSS would be a stop.
- **Nothing related reaches the head or the data block.** No related value in the title, description, OpenGraph or Twitter tags; no `ItemList` and no other JSON-LD (ADR 0166 D10 trigger 6);
  `buildJobPosting` takes the anchor's DTO only. The section appears only on a listed ad's 200 at `/jobb/<uuid>` (trigger 15).
- **`data-nosnippet` is ADR 0176 D9's reason applied unchanged** and is `security-auditor`'s condition: a snippet of ad A quoting ad B's title or employer would be false, stale and, for a sole
  trader, a copy of a name that we cannot reach. The attribute is valid on `section` (Google, read 2026-10-18).
- **Rows are plain `<a href>`** (ADR 0176 D10), so a related row spends nothing from `public-job-ad-read`.

### D6 — The legal outcome has one home, and the merge waits for it (security-auditor M1–M3, m2, m3, S1–S3, S15, S16; Klas's answers)

- **ADR 0166 D10 trigger 1 fires on its head clause, "any widening of the public surface"** (the CTO F6; security-auditor agrees). Three reasons: a new anonymous read surface, the first since
  S5; the job page now carries other ads' titles and employers from a read it did not make before; and the section puts an occupation × place grouping of listed ads on every listed ad's page,
  the axis that stage B gates for hubs. The re-take is narrow: the related section only. The page, the sitemap, the catalogue and the Search Console read path keep their conclusions.
- **The single home** is ADR 0166, "Amendment 2026-10-23 (#2090 S6b, ADR 0182)", written after Klas's answers and Accepted only when `security-auditor` signs it. **This ADR points there and
  restates none of it** (one decision, one home; AGENTS.md §8 point 9). What it must hold is her M1: the D-by-D re-take for the section; his Art. 9 decision of 2026-10-11 and that it covers the
  section; D10 triggers 15–17 and the record that the others are not fired; the fourth addendum to DPIA #456 §8-B (local, M2); the merge-day S2-P3 reading (M3); and the renewal of her two
  signatures that lapse when trigger 1 fires, the catalogue's of 2026-10-18 and the Search Console reader's of 2026-10-19 (S15).
- **The merge is gated.** S6b may not merge, and so may not go live (ADR 0154), until `security-auditor` has signed that amendment at her scoped re-check of the PR's final head. Merging
  without it is a Blocker (her M1): the processing would rest on a withdrawn conclusion.
- **Klas's answers, 2026-10-23,** by AskUserQuestion, to `security-auditor`'s escalation and the CTO's split. They are quoted without edits:

  | # | Asked by | His answer | What it decides | Recorded in |
  |---|---|---|---|---|
  | 1 | security-auditor, question 2 | "Ja (Rek.)" | the ADR 0166 D10 re-take for the related section, including that his decision of 2026-10-11 (no occupation group is excluded) covers the section, and triggers 15–17 | the ADR 0166 amendment |
  | 2 | security-auditor, question 3 | "Ja (Rek.)" | a fourth addendum to DPIA #456 §8-B, not a new full DPIA and no prior consultation; he approves the text when the S6b session has written it | the ADR 0166 amendment, and the addendum (local) |
  | 3 | the CTO, F0 | "Två PR (Rek.)" | S6a and S6b as two PRs on #2203 | the CTO's ruling; this ADR is S6b's |

- **Triggers 15–17 are held by this ADR's decisions** and have their home in ADR 0166 D10: the section's row, placement and metadata (D3, D5), content read per request through the predicate
  (D2), and the selection rule with its cap of five and no caller key (D1, D2). A change to any of them is a re-take there, not an edit here.
- **What else the record owes** (security-auditor): `docs/runbooks/recruiter-pii-erasure.md` §2.4a (m2), the processing register (m3, local), the ADR 0106 D9 rows and the
  `docs/threat-model.md` line (S16). They are listed under *Implementation*.

### D7 — ADR 0064 rule (d): a bounded read keyed by one record is the per-record class, so nothing is precomputed (architect; CTO F5)

- **The reading.** Rule (d) sorts reads into two classes: a recurring read of the whole corpus, identical for every reader, which is precomputed, and a read of one record, "one primary-key
  lookup", which has nothing to precompute. The related read is literally neither: a primary-key read of the anchor, then one indexed read of the anchor's group.
  By the rule's own reasons it is the per-record class: there is no `COUNT` and no `OFFSET` over the corpus; the answer differs per anchor, so there is no shared hot key; its cost is bounded
  by one group and not by the corpus; and the erasure rule forbids caching it. **Nothing is precomputed**, and a related list held in a precomputed or materialised form would fire ADR 0166
  D10 trigger 16.
- **Why an amendment.** The CTO's F5 left rule (d) unamended and said the question comes back if the architect found its text ambiguous. The architect found that the text does not cover this
  read, and proposed one additive paragraph in ADR 0064, as S3, S4 and S5 each placed their reading where the rule is read. ADR 0064 gets that paragraph, with no new decision and no
  supersession.

## What is not measured, and the measurements owed

**Not measured, and not claimed:**
- The plan on the box itself. The `EXPLAIN` is a Testcontainers database seeded in the box's shape (*Measurements*, R2), not the box's own planner and statistics.
- The employer distribution inside a group. It is never counted, because a per-employer count can name a person (D2), which is also why W is set by cost.
- How often the section is absent in production. A 429, a 5xx or a timeout drops it silently, and the web logs nothing on the public surface by design; the API's `LoggingBehavior` (the message
  name and the elapsed time) is the signal (the CTO).
- Copies out of reach: a search engine's index copy of another ad's page whose related section held a row that has since been erased, which is refreshed at that page's next crawl, and
  web-archive snapshots.

**Owed before the merge:**
- **The merge-day S2-P3 reading** (security-auditor M3): the same query blob (`8c02e37`), counts only, on the merge's UTC day, before `agents-done`. The validated count must be 0; a count above 0
  is ADR 0166 D10 trigger 2 and S6b stops. It is recorded in the ADR 0166 amendment's D11 item and in the record table of `docs/runbooks/public-job-page-publication-count.md`.
- **The byte delta of the Lighthouse stub** (the CTO): the stub gains the related route, and the delta is read under §F's per-PR reader before `agents-done`.

## Alternatives considered

### Alt A — The chosen design (D1–D7)
**For:** one publication predicate on both reads; the keys are read from the anchor's row, so no caller can select; an erased ad leaves every section at once, because content is read per
request; a row cannot carry what it must not, because it is one type pinned three ways; a failure of the section cannot fail or delay the ad beyond a bound; no migration, no package, no
`deploy/` diff, no `proxy.ts` edit and no CSS.
**Against:** the trade-offs listed under Consequences.

### The forks (the CTO's unless marked)
- **(i) The related rows in the detail DTO.** Rejected. The probe would pay for them on every view and then throw the body away; a related failure would either fail the ad or need a
  swallowing catch (AGENTS.md §5); the pinned DTO would grow by other ads' data; and one read would carry two reasons to change (SRP, Martin 2017, ch. 7). The ad's own facts go in the ad's read,
  and other ads go somewhere else.
- **(iii) The rows from the listed-ad snapshot.** Rejected. It reopens ADR 0174 D1 and ADR 0176 D3 and their signatures for an in-memory view that an indexed read of at most about two
  thousand rows does not need, and stage-A A1 reserves the next projection column for the hub step.
- **A caller-keyed form (`?group=&kommun=`).** Rejected, a hard condition: an anonymous facet API (D2).
- **`DISTINCT ON` behind a port, and `GroupBy` with `ROW_NUMBER`.** Rejected (D2, architect).
- **A bool sort key** (`OrderByDescending(j => col == anchorId)`). Rejected: null semantics (D2, architect, Kritiskt).
- **A `Classify` switch on the anchor.** Rejected for `IsListed(now)` in the anchor read: no 410 arm can grow (D2, architect and security-auditor).
- **One round trip for both reads.** Rejected: the registry's non-reach 6, and a 404 cannot be told from an empty 200 (D2, architect).
- **A floor of 3 rows.** Rejected: an unexplained threshold that hides true rows (D1).
- **A kommun-only or län-only fallback, filling across groups.** Rejected: a related list with no relation, and a per-ad place listing (D1).
- **The national newest-first order.** The CTO's pre-ruled fallback; not applied, because `security-auditor` graded the juxtaposition neither Blocker nor Major (D1).
- **Naming the group in the heading.** Rejected: two copy variants and a null branch, for a fact the facts row already shows (D5).
- **`ItemList` JSON-LD.** Rejected: ADR 0166 D10 trigger 6, and Google puts `JobPosting` on single-job pages only.
- **Sequential reads (related after the ad).** Rejected: it adds the related latency to the time to first byte on every view (D5).
- **A budget shared with `public-job-ad-read`.** Rejected: it cuts views from 30 to 20 a minute (D4).
- **A second list component.** Rejected: it would copy the markup (D5).
- **Moving the row type to a shared namespace** (architect, Nice-to-have). Not taken; the import from the catalogue's slice is stated (D3).

## Consequences

### Positive
- One predicate serves the sitemap, the catalogue, the detail read and the related section. A future exclusion that Klas or `security-auditor` decides on goes into `JobAdPublication` and
  reaches related ads with no change to them (#2133's remedy, the CTO).
- An erased ad is read per request and so leaves every section at once, on every page. No check step and no removal request is owed for a related section (ADR 0106 D9 amendment).
- A row cannot carry what it must not: the member set is pinned by a contract test, a wire test and a boundary test, and the web's schema is strict at both levels.
- The section is a bulkhead: a stall or an outage in it costs the ad at most the 2-second bound and never its status.
- The release adds no migration, no package, no `deploy/` diff, no `proxy.ts` edit, no CSS and no ACL change, and no new recipient, sub-processor, transfer or PII field (security-auditor S14,
  checked against the four commits' file lists).

### Negative and accepted trade-offs (the CTO's list unless marked)
- **A third API call on every 200 view,** on its own budget, with a timeout of at most 2 seconds. The worst-case time to first byte grows by at most that bound when the detail read is fast
  and the related read stalls.
- **The section can be silently absent** (on a 429, a 5xx or a timeout), and the only signal is the API's `LoggingBehavior`.
- **The same employer's other ads never appear as related.** A small loss for the user, accepted for D1's reasons (a)–(c).
- **The race case** (the probe says 200, the render says 404) spends one related read for nothing.
- **W's declared underfill:** a section can show fewer than five rows while more employers exist below the window (D2).
- **The 426 listed ads without an organization number** (0.95 % at 18:38 UTC on 2026-10-23) never appear as related, and their own pages show no section. Their own page, the catalogue,
  the sitemap and `/jobb` are unchanged (the CTO's ruling, round 1).
- **An employer's name is on more pages and can stay on a page longer than in the catalogue** (security-auditor). For a sole trader it can be a personal name, and a job page does not shift
  its rows every five minutes as the catalogue does. Search engines index the text anyway: `data-nosnippet` keeps it out of snippets and AI answers and not out of the index, and a copy held
  by a search engine or in a web archive is out of reach. An erased ad still leaves every section at once. The weighing is in the ADR 0166 amendment (D6).
- **Ads about different persons can stand side by side** in one small kommun. `security-auditor` judged that this reveals nothing about any of them beyond what each row already shows (D1).
- **The row type is imported from a sibling slice** (D3).
- **Two PRs and two panel rounds** (Klas 2026-10-04: a new session per PR in an epic).

## Implementation

One PR on `feat/seo-s6b-related`, lane BE+FE, S6b of #2203. It adds no migration, no package and no diff under `deploy/`, and no edit to `proxy.ts`, `next.config.ts` or `globals.css`; any of
those would be a stop that comes back to the CTO. The commit order is the CTO's:

1. **P1, `refactor(web)`, `b60b9e824`:** `PublicJobAdList` takes `headingLevel: 2 | 3` and `idPrefix: "catalogue" | "related"`. The catalogue's markup is byte-unchanged; the `(public-job)` and
   job-ads suites pass unchanged (27 files, 508 tests).
2. **`feat(jobads)`, `1220f1eed`:** the query, handler, validator, limits and DTO with their unit tests; the two registry entries; the `TaxonomyAclLayerTests` row; the docblocks of
   `JobAdPublication`, `PublicTaxonomyNames` and `PublicJobAdSummaryDto`; the Testcontainers read tests and the plan test (the `EXPLAIN` before the endpoint, the CTO's oracle).
3. **`feat(api)`, `f4859702d`:** the route, the policy, its options default and the factory rows, with the endpoint, wiring and 429 tests.
4. **`feat(web)`, `aec9e2769`:** the reader, the schema, the page's parallel read, the section, the key, the group test, the stub and the smoke.
5. **Then the docs:** this ADR and the amendments to ADR 0064, 0106, 0160, 0165, 0176 and 0166; `BUILD.md`'s endpoint line and `docs/threat-model.md`'s enumeration (the CTO F5;
   security-auditor S16); `docs/runbooks/recruiter-pii-erasure.md` §2.4a, with one sentence that related lists need no check step and a second under "what cannot be reached" for an index
   copy of another ad's page (security-auditor m2); the session docs per ADR 0065. Then the panel, and `security-auditor`'s signature before the merge.

**Outside the diff, local files in the main copy:** the fourth addendum to DPIA #456 §8-B, whose text Klas approves (security-auditor M2); the processing register's one sentence that a listed
ad's six values also appear in the related section of other ads' pages (m3); and the merge-day S2-P3 reading (M3).

**Test premises (AGENTS.md §5, Tests).**
- The handler's unit tests run on InMemory, where C# semantics and `Guid.CompareTo` apply and not Postgres's `uuid` order, so they use distinct `PublishedAt` and assert no id tie.
  Ties and null-tier semantics are asserted only in Testcontainers, against the ordinal `ToString("D")` oracle.
- An anchor with a län and no kommun, or a kommun and no län, is a state production can produce, because `PlatsbankenJobSource` reads the two ids independently;
  `PlatsbankenJobSourceFacetMappingTests` names the actor `MapFacets` and pins that it passes an address block with either id alone through unpaired.
- The plan test's seed follows R1's counts, archived rows included because the group index holds them too, and is bulk-inserted in the shape the ingest writes: 150 000 rows through EF would
  measure the seed and not the read. The plan rests on the table's statistics, which production's own writers produce, and never on these rows' identity. The employer names are synthetic
  and the anchors never come from the box (security-auditor m4).
- Ads are seeded through `JobAd.Import`, and a never-imported ad through `JobAd.Create` as `POST /api/v1/job-ads` stores it. An ad erased by the production erase path and an ad past its
  deadline are each absent from the result, and a never-imported anchor answers the same 404 as the other non-listed anchors. No path in `src/` gives a never-imported ad an occupation group,
  so it is never a candidate; the predicate's provenance arm is pinned in the SQL text instead (`RelatedPublicJobAdsReadTests`).

## Measurements

Each reading below is dated and has its source. Nothing is estimated.

| Reading | What | Recorded |
|---|---|---|
| R1 | the box's shape, counts only (CLAUDE.md §9.2 (a)), grouped by status and by occupation group, never by employer | 2026-10-23 at 17:31 UTC, on the production box: 44 718 Active (all listed), 107 739 Archived, 0 Erased; 396 groups and no row without a group; the largest group 6 925 rows, 2 161 of them listed, over 281 kommuner, 19 listed without a kommun; listed per group: median 38, p90 272, maximum 2 161. |
| R2 | the candidates statement as sent, its plan and its time | 2026-10-23, after round 1, `RelatedPublicJobAdsQueryPlanTests` (Testcontainers Postgres 18, the `JobAdBrowsePlan` collection) over 152 460 rows, which are R1's 152 457 plus the test's three anchors, in R1's shape: every group spread over the last 60 days, Archived rows past their deadline, about one row in a hundred without an organization number (R5), synthetic employer names and numbers, `VACUUM (ANALYZE)` after the seed; the statements and plans below. The first run, before round 1, seeded every batch newest-first and every deadline open, which flattered the placeless anchor (dotnet-architect); its numbers are not kept. |
| R5 | the organization-number stock, counts only | 2026-10-23 at 18:38 UTC, on the production box, totals only: 44 716 listed; 44 290 with an organization number, 0 of them personnummer-shaped; 426 without. |
| R3 | the tests | 2026-10-23, the driving session: unit tests 30/30, with mutations of the two null guards, the employer exclusion, the one-row-per-employer walk, the group filter, `IsListed` on the candidates, the organization-number filter, the anchor's number, and each of the walk's two keys each reddening their tests; Testcontainers `RelatedPublicJobAdsReadTests` 16/16, including "a row without a kommun never outranks a kommun match" and ties against the ordinal `"D"` string oracle; API tests 9 new, 113 in the public and rate-limit sweep. |
| R4 | the web | 2026-10-23: the `(public-job)`, job-ads and reader suites 120/120, `tsc --noEmit` clean, the boundary tests 3/3; the smoke 52/52 against the standalone build: exactly one related read per 200 view, none when the probe answers 404, 410 or 503, and no section on a spent budget, an outage, a row wider than the six members or a 7-second stall, within the bound. Rendered at 375, 768, 1280, 1920 and 3440 px with 1 and 5 rows; 0 rows, 429, 500 and a stall at 1280 and 375 px give one identical `main` (the stall answered in 2.97 s); card, heading and row edges Δ 0 px; axe 0. The renders are local, in `C:/tmp/jobbliggaren-visual/2203-s6b/`. |

**R2, the candidates statement for an anchor with a kommun and a län, as sent:**

```sql
SELECT j.id, j.title, j.company_name, j.organization_number, j.municipality_concept_id, j.region_concept_id, j.published_at
FROM job_ads AS j
WHERE j.external_id IS NOT NULL AND j.external_source IS NOT NULL AND j.external_source = 'Platsbanken' AND j.status = 'Active' AND (j.expires_at IS NULL OR j.expires_at >= @now) AND j.occupation_group_concept_id = @group AND j.id <> @id AND j.organization_number IS NOT NULL AND j.organization_number <> @anchorNumber AND j.company_name <> @anchorName
ORDER BY CASE
    WHEN j.municipality_concept_id = @kommun THEN 0
    WHEN j.region_concept_id = @lan THEN 1
    ELSE 2
END, j.published_at DESC, j.id
LIMIT @p
```

For an anchor with neither id, EF prunes the rank: the funcletizer removes both guarded branches and the statement ends `ORDER BY (SELECT 1), j.published_at DESC, j.id`. That is one C# form and
two SQL variants cached by parameter nullability, as the architect predicted. `ToQueryString()` and `EXPLAIN` print parameter values, so both ran on synthetic seeds, and the one box read
behind R1 was grouped by status and group only (security-auditor m4).

**R2, `EXPLAIN (ANALYZE, BUFFERS)`** (every buffer read was a shared hit; the full output is kept locally at `C:/tmp/jobbliggaren-visual/2203-s6b/explain-2026-10-23-round2.txt`):
- **An anchor with a kommun and a län, in the 2 161-listed group:** `Limit` → `Sort` (top-N heapsort, 38 kB) → `Bitmap Heap Scan` (218 heap blocks, 2 161 rows, 2 removed by filter) →
  `BitmapAnd` of a `Bitmap Index Scan` on `ix_job_ads_occupation_group_concept_id` (6 927 rows) and one on `ix_job_ads_status_published_at_id`. Execution 4.4 ms. The handler over 40 warmed
  iterations: p50 8.8 ms, p95 12.2 ms.
- **A placeless anchor in the same group:** an `Index Scan` using `ix_job_ads_status_published_at_id`, newest first, with the group as a filter, 1 925 rows removed by it. Execution 2.2 ms; handler p95 10.7 ms.
- **A placeless anchor in a 108-listed group:** a `Bitmap Index Scan` on the group index and a quicksort. Execution 0.79 ms; handler p95 5.7 ms.
- All three are against ADR 0045 class (a), p95 300 ms, which is observe-only. The permanent pin, `TheCandidatesStatement_ReachesTheOccupationGroupIndex`, asserts the group index for the
  anchor with a place.

## What comes back to the CTO

Nothing detects these automatically. Each is read when the event is proposed or reported.
- A migration, a new package, a diff under `deploy/`, an edit to `proxy.ts` or an ACL change (the CTO).
- A plan over ADR 0045's hot-path budget (the CTO).
- A need for CSS on the section: `hotspot:css` is #2198's, so it is a stop (the CTO).
- Applying the national fallback of D1: the `EXPLAIN` is taken again, because the plan can turn into an index walk through `ix_job_ads_status_published_at_id`, which for a small group
  is nearly a full walk (architect).
- The legal triggers are ADR 0166 D10's, 1–17, and have their home there (D6).

## References

- Code, at the four commits: `src/Jobbliggaren.Application/JobAds/Queries/GetRelatedPublicJobAds/` (`GetRelatedPublicJobAdsQuery.cs`, `…QueryHandler.cs`, `…QueryValidator.cs`,
  `RelatedPublicJobAdLimits.cs`, `RelatedPublicJobAdsDto.cs`) · `src/Jobbliggaren.Application/JobAds/Publication/JobAdPublication.cs` ·
  `src/Jobbliggaren.Application/JobAds/Queries/PublicTaxonomyNames.cs` · `src/Jobbliggaren.Api/Endpoints/PublicJobAdsEndpoints.cs` ·
  `src/Jobbliggaren.Api/RateLimiting/RateLimitingExtensions.cs` and `RateLimitingOptions.cs` (`PublicJobAdRelated`) ·
  `web/jobbliggaren-web/src/lib/api/public-job-ad-related.ts` · `web/jobbliggaren-web/src/lib/dto/public-job-ad-related.ts` ·
  `web/jobbliggaren-web/src/components/job-ads/public-job-ad.tsx` and `public-job-ad-list.tsx` · `web/jobbliggaren-web/src/app/(public-job)/publik/jobb/[id]/page.tsx` ·
  `web/jobbliggaren-web/scripts/public-job-smoke.mjs` and `public-job-stub.mjs`
- Tests: `tests/Jobbliggaren.Application.UnitTests/JobAds/Queries/GetRelatedPublicJobAds/` · `tests/Jobbliggaren.Api.IntegrationTests/JobAds/RelatedPublicJobAdsReadTests.cs`,
  `RelatedPublicJobAdsQueryPlanTests.cs`, `PublicJobAdRelatedEndpointTests.cs` and `PublicJobAdRelatedRateLimitWiringTests.cs` ·
  `tests/Jobbliggaren.Api.IntegrationTests/RateLimiting/PublicJobAdRelatedRateLimitTests.cs` · `tests/Jobbliggaren.Architecture.Tests/PublicJobAdRelatedBoundaryTests.cs`,
  `JobAdLifecycleReadRegistry.cs` and `TaxonomyAclLayerTests.cs` · the web's `public-job-ad-related.test.ts`, `public-job-ad.test.tsx`, `public-job-pages.test.tsx` and
  `public-job-group.test.ts`
- `docs/reviews/2026-10-23-2090-s6-plan-cto.md` · `…-s6-plan-security-auditor.md` · `…-s6-plan-dotnet-architect.md` · `…-s6-plan-design-reviewer.md` (all transcribed verbatim by the driving
  session; local) · `docs/reviews/2026-10-18-seo-audit-stage-b-cto.md` (B1, B3, `:186`) · `docs/runbooks/recruiter-pii-erasure.md` §2.4a ·
  `docs/runbooks/public-job-page-publication-count.md`
- ADR 0005 (local) · ADR 0043 · ADR 0045 · ADR 0064 (rule (d); the amendments of 2026-10-10 and 2026-10-23) · ADR 0106 (D9) · ADR 0113 · ADR 0120 · ADR 0154 · ADR 0160 (D1–D6) ·
  ADR 0165 (D2, D3) · ADR 0166 (D1–D11, W1–W5, D10 triggers 1–17 and the amendments of 2026-10-11 and later) · ADR 0171 (Alternative B) · ADR 0174 · ADR 0176 (D3, D5, D9, D10)
- AGENTS.md §2.1, §3, §5, §8 · CLAUDE.md §9.2 (the reading rule (a)), §9.6
- Martin, *Clean Architecture* (2017), ch. 7, 13 and 22 · Nygard, *Release It!*, 2nd ed. (2018), ch. 5 · Saltzer & Schroeder (1975), least common mechanism · Winters/Manshreck/Wright,
  *Software Engineering at Google* (2020), ch. 9
- Google Search Central, as the CTO's brief read them on 2026-10-23: helpful content (updated 2026-10-05); and the *Robots meta tag* page, read 2026-10-18, for `data-nosnippet` on `section`.
  These pages change; the dates say what was read.
- #2090 · #2203 · #2204 · #2198 · #2133
