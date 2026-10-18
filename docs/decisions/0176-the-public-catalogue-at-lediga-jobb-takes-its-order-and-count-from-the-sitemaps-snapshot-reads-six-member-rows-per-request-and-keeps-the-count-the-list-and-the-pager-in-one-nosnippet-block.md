# ADR 0176 — The public catalogue at `/lediga-jobb` takes its order and count from the sitemap's snapshot, reads six-member rows per request, and keeps the count, the list and the pager in one `data-nosnippet` block

**Date:** 2026-10-18
**Status:** Accepted 2026-10-18 (the CTO's ruling under CLAUDE.md §9.2, its stage-A addendum and its triage of the plan reads, and Klas's answers of the same day, D13). The ADR 0166 amendment of
the same date, which carries the legal outcome (D13), is **Proposed** until `security-auditor` signs it at her scoped re-check of the S5 PR's final head. The merge waits for that signature,
because a merge is a go-live (ADR 0154).
**Deciders:** Klas Olsson (controller and product owner; answers in chat on 2026-10-18, recorded in D13, and the hand-over of the `hotspot:i18n` token from #1977, "Ja, ta över (Rek.)") ·
`senior-cto-advisor`, "the CTO" below (decision-maker under CLAUDE.md §9.2; ruling on forks F1–F18, report-only, `docs/reviews/2026-10-18-2090-s5-plan-cto.md`; its binding stage-A addendum from
the SEO/AIO-audit session, `docs/reviews/2026-10-18-seo-audit-stage-a-cto.md`; and its triage of the three plan reads, `docs/reviews/2026-10-18-2090-s5-plan-triage-cto.md`, which corrects and
supersedes parts of the ruling) · `security-auditor` (plan read, report-only, `docs/reviews/2026-10-18-2090-s5-plan-security-auditor.md`: her verdict, M1–M3, m1–m6, triggers 10–12 and the
escalation) · `dotnet-architect` (form read, report-only, `docs/reviews/2026-10-18-2090-s5-plan-dotnet-architect.md`) · `design-reviewer` (plan read, report-only,
`docs/reviews/2026-10-18-2090-s5-plan-design-reviewer.md`: one Blocker, M1–M4, m1–m4 and the escalation)
**Amends:** ADR 0160 (D2, D4, D5, D6), ADR 0174 (D1, D3, D5, D9), ADR 0165 (D3, D6), ADR 0064 (rule (d)), ADR 0106 (the D9 read-path table) and ADR 0166 (D2, D4, D5, D6(d), D8, D10, D11), each
by a dated amendment at the end of that file, and annotates ADR 0005 (local) the same way. Nothing above those amendments is rewritten. It applies ADR 0120 and ADR 0171 and amends neither.
**Related:** [#2090](https://github.com/klasolsson81/jobbliggaren/issues/2090) (the epic) · [#2172](https://github.com/klasolsson81/jobbliggaren/issues/2172) (S5, the issue this ADR ships
under) · ADR 0174 (S4, the snapshot and the sitemap) · ADR 0171 (S3, the `JobPosting` block and the place naming) · ADR 0113 (every `job_ads` read site carries a written lifecycle decision) ·
ADR 0120 (a rendered count is true or it is absent) · ADR 0045 (budgets) · ADR 0154 (§3: every merge is a deploy; §4: what needs Klas's GO) · ADR 0050 (Amendment 2026-08-04: no CDN) · DPIA #456
§8-B (local) · #1477 (Klas's rule that a person never lands outside the site's frame) · #1738 and #390 (the footer's one-door and no "Sök jobb" rules) · #1149 (ADR 0120 clause 4) ·
#2133 (the Art. 9 remedy, closed unbuilt)
**Measured against:** the branch `feat/seo-s5-public-catalogue` in `C:/tmp/jbl-seo-s5`, cut from `8ce73fa36`. The ruling, the stage-A addendum and the three plan reads were taken at
`8ce73fa36` on 2026-10-18; the triage was taken with the two preparatory commits P1 and P2 already built on it, in the form it rules. Nothing here is merged, and the
merge is the go-live.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. D1–D12 follow the CTO's ruling as amended by its
> stage-A addendum and its triage ("(triage)" marks a place where the triage changed an F-ruling). The architect's findings are marked "(architect)", `security-auditor`'s "(security-auditor)",
> and `design-reviewer`'s "(design)". D13 follows `security-auditor`'s plan read and records Klas's answers. The reports are local files, transcribed verbatim by the driving session from the
> agents' hand-backs. Facts about the code were read by those agents at `8ce73fa36` on 2026-10-18 and are not re-read for this ADR. **No measurement and no test result is recorded here
> that a source does not carry;** what no source measures is listed under *What is not measured, and the measurements owed*, and the readings are entered under *Measurements* before the
> merge. The mandatory agents review the delivered diff in the PR. The ADR lies in the ignored 0071–0199 range and needs `git add -f` and a README row (precedent 0167–0174).
>
> **Numbering.** The ruling, the stage-A addendum, the triage and the three plan reads call this record "ADR 0175", and they stay as their authors wrote them. #2173 (#1663 PR 1) merged
> its own ADR 0175 on 2026-10-18 while S5 was in review, so this record is 0176, measured free on `origin/main` (`dc1511b33`) and in every open PR the same day.

---

## Context

Step S5 of the SEO epic (#2090, issue #2172) puts a public, paginated catalogue of the listed ads at one address. After S4 (ADR 0174) crawlers find job pages through the sitemap. Eleven facts
shape the decision.

- **Nothing public links to a job page, and a visitor without an account cannot browse the open ads.** `security-auditor`'s grep of `src/` on 2026-10-18 (recorded in ADR 0174's Context): outside
  the signed-in app the only job link is the `/gast` demo's `/gast/jobb/…`, which `robots.txt` disallows. The sitemap serves crawlers only, so ADR 0166 D1's "without an account, and … a place such a search lands" is met
  for crawlers and not for people.
- **The S4 snapshot has no count and no order.** It exposes `AsOf`, `IdsIn(part)` and `IsFreshAt` only, and its ids are in id order (the CTO, brief correction 3). `JobAdPublication.IsListed`
  exists (ADR 0160 D2, ADR 0174 D1).
- **ADR 0064's rule (d) splits reads in two classes.** A recurring read of the whole corpus, identical for every reader, is precomputed. A read of one record has nothing to precompute and
  must not be cached, because the erasure rule is absolute (ADR 0160 D6; ADR 0064, Amendment 2026-10-10). Membership, order and count are the first kind; a row's content is the second.
- **The size of the set.** 42 270 ads were listed on 2026-10-18 at 12:57 UTC, 0 ads had ever been erased, and 0 of the 41 860 listed ads that carry an organization number carry a
  personnummer-shaped one (`security-auditor`'s box reads, counts only). At 20 rows a page the catalogue is about 2 112 pages (the CTO's figure).
- **The house already has a pager and a page size.** `JobAdPagination` links the first page, the last page and the current page ±1 (`buildPageItems`), `/jobb` defaults to 20 rows, and the
  company register caps its pages at 100 because OFFSET cost grows with depth (`ICompanyWatchBrowseQuery.cs:262-270`). `PagedResultContractTests` fires only for a query that has both `Page`
  and `PageSize` as inputs (the CTO, brief correction 1).
- **The landing's header shows its stats only when given a `stats` prop,** and only the landing passes one. Footer links render through `InformationLink`, a client `<Link>` with Next's default
  prefetch (`site-footer.tsx:163`), so a prefetch question applies to the shared chrome of every page of the site (the CTO, brief corrections 4 and 5).
- **Google's guidance (read by the CTO on 2026-10-18).** *Pagination and incremental page loading* (updated 2025-12-10): give each page its own canonical URL, do not use the first page as the
  canonical, and link to the next page and back to the first. The faceted-navigation page: a nonexistent pagination URL should answer 404. *Robots meta tag* (updated 2026-03-24):
  `data-nosnippet` is valid on `span`, `div` and `section`. Mueller (2017, through Search Engine Roundtable): a long-term `noindex, follow` is eventually treated as `nofollow`, which is a
  statement and not documentation. Next 16.3.8 streams metadata to Googlebot, which is not an HTML-limited bot, so a canonical can be appended to `<body>` (`generate-metadata.md:1244-1248`).
- **ADR 0166 D10 trigger 1 names "the S5 catalogue"** and fires by its own text (`security-auditor`). The conclusion in ADR 0166 is withdrawn for the catalogue, not for the page or the
  sitemap, until Klas takes it again and she signs (D13).
- **ADR 0005 (local) sketches its own route to an anonymous catalogue.** Its lines 84–86 say an anonymous public surface "kan låsas upp senare via separat ADR", and its line 106 lists
  "ta bort `.RequireAuthorization()`" from the job-ad group. This ADR is the separate ADR, and it rejects that path (Alternatives).
- **The record says two things the code does not.** `DESIGN.md:189` calls the anonymous job page "the only job-ad surface with a notice and no contact", and ADR 0166 D5 says the notice is
  "linked from every public page". Delivered, the notice is linked from the job page (`public-job-ad.tsx:98`) and from `/integritet` only (`integritet/page.tsx:77`); the footer links
  `/integritet`, not the notice (`site-footer.tsx:116`) (the CTO, brief correction 7; `security-auditor` m1).
- **The SEO/AIO audit runs in parallel.** Klas answered "Ja, S5 väntar (Rek.)", relayed by that session: S5 waited for its stage-A phasing ruling before any code or commit. That ruling, the
  addendum, binds S5 on what it must not build in advance and on three constraints that keep the namespace open (D1, D3, D8, D11).

## Decision

**The catalogue is `/lediga-jobb`, a page in the `(public-job)` group with the page number in `?sida=n`. `GET /api/v1/public/job-ads/catalogue?page=` answers one page of 20 rows. The listed
set's membership, order and count come from the snapshot that already serves the sitemap, which gains a second view of ids only from the same read; each row's content is read and
classified through `IsListed(now)` on every request. A row has six members and the page can render nothing else. The count, the list and the pager sit in one `data-nosnippet` block, and no
number reaches the title or the meta tags. The endpoint has its own rate-limit policy. "Lediga jobb" goes in the header's nav and in the footer, and no link toward the catalogue
prefetches. The legal outcome has one home, the ADR 0166 amendment of the same date.**

**Scope.** S5's scope is F1–F17 plus what its three plan reads changed. Additions for hubs, footer cities, structured data, content or tooling go to S6 or later, even when they edit a file S5
creates. A stage-B finding that S5 *as ruled* is wrong is a correction on S5's own delta: it is fixed in S5 before the merge, and comes back to the CTO if it changes an F-ruling. A better idea
is an addition (stage-A addendum A3).

### D1 — The route, the one parameter, and what is done with the rest (CTO F1, F7; stage-A A2.1)

- **The route is `/lediga-jobb`, and the page number is `?sida=n`. Page 1 is the bare URL.** A Swedish noun route (AGENTS.md §4) that matches the label "Lediga jobb" used on all three entries,
  as Google's sitelinks advice asks (short link text that matches the destination). It is outside `PROTECTED_PREFIXES`, so `proxy.ts` passes it through with no backend call, and `robots.ts`'s
  `/jobb$` and `/jobb?` rules do not prefix-match it, so robots.txt needs no change (D8).
- **Why a query parameter.** It keeps the path namespace under `/lediga-jobb/` free for phase 2's curated pages. A path segment such as `/lediga-jobb/sida/2` would reserve a slug. The query
  form is the one Google's pagination page uses in its own example.
- **The page reads exactly one parameter, `sida`.** No filter, sort or search in phase 1 (YAGNI). The epic makes facets curated pages in phase 2, "never the cartesian product"; ADR 0005's gate
  on anonymous search was never measured; and `JobAdsEndpoints.cs` carries a DoS note about `ListReadPolicy`.
- **Unknown parameters** (utm and the rest) are ignored for content and left out of the canonical, which is a self-canonical built from `sida` alone. No `noindex` on such URLs: `noindex`
  beside a canonical that points at another URL sends conflicting signals. No robots disallow either: it would hide the canonical (ADR 0165 D6). This is the house's existing choice for the
  job page's query variants.
- **The epic's "every filter or search parameter noindex"** is satisfied vacuously and is carried forward as lapse trigger 1 (D14). A future filter forces the noindex-versus-disallow
  decision against Google's faceted-navigation guidance, because the two cannot both work on one URL.
- **S5 creates `lediga-jobb/page.tsx`, the segment's not-found (D11) and nothing else below it** (stage-A A2.1): no fixed segment, no `[slug]`, and no place or occupation parameter. A place
  passed as a parameter fires trigger 1 and is the faceted-navigation pattern the epic rejects; phase 2's places are paths, and a place as a path is a new public page, which is ADR 0166
  D10 trigger 1.

### D2 — Page size 20, and a depth cap derived from the ceiling, never hand-picked (CTO F2)

- **The constants.** `PageSize = 20`. `MaxPage = ⌈Ceiling / PageSize⌉ = 4 000`, a constant expression over `ListedJobAdSnapshotLimits.Ceiling` (architect: it compiles), tested at both edges:
  `MaxPage × PageSize ≥ Ceiling` and `(MaxPage − 1) × PageSize < Ceiling` (triage).
- **What the cap bounds.** The URL space, never the listed set. Past the ceiling the snapshot publishes nothing and the catalogue fails as a 5xx; it is never truncated.
- **Why the register's `MaxPage = 100` does not transfer.** Its reason is that OFFSET cost grows with depth. Here a page is an in-memory slice whose cost does not depend on depth. A cap of 100
  would leave about 40 000 of 42 235 ads unreachable through the catalogue, which would falsify the phase-1 outcome line, as the CTO reads it: every page reachable by a chain of `<a href>`
  links from `/lediga-jobb`.
- **Reachability.** `buildPageItems` links first, last and current ±1, so every page is reachable by a chain of links. The middle of the series is deep in clicks, and that is accepted: the
  sitemap is each ad's discovery path (ADR 0174), newest-first puts each new ad on page 1 when it arrives, and depth is phase 2's job, solved by breadth (curated hubs), not by page size.
- **Why 20 and not 50.** It matches the house page size and the shared pager, it keeps the document nearer ADR 0045's 30 kB budget (AGENTS.md §2.5; measurement M7), and it makes each
  per-request read smaller. The argument for bigger pages was crawl count, and that is weak once the sitemap carries discovery.
- **A stride pager** (links at fixed strides through the middle) is not built: its value is unmeasured and it would fork the house pager's knowledge piece. It is lapse trigger 3.

### D3 — The snapshot gains a second view, ids only, from the same read (CTO F3 and F4, as amended by the architect's V1 and V2; stage-A A1)

- **One read per tick, two views.** `ListedJobAdSnapshotSampler.SampleAsync` keeps its SQL: `Where(IsListed(asOf))`, `OrderBy(Id)`, `Take(ceiling + 1)`. Only its projection widens, to
  `ListedJobAdRow(Id, PublishedAt)`, a `readonly record struct` projected after `Take` (architect). Nothing else about the read changes, and a read that throws or exceeds the ceiling
  publishes neither view.
- **What `ListedJobAdSnapshot.Create(asOf, IReadOnlyCollection<ListedJobAdRow>)` builds from those rows at one `AsOf`.**
  - the sitemap's parts, exactly as before, in id order;
  - `CatalogueOrder`, the same ids newest first;
  - `Count`, the length of that list and never a separate field.

  The snapshot's members are exactly `AsOf`, `IdsIn`, `IsFreshAt`, `CatalogueOrder` and `Count`.
- **Ids only.** No `PublishedAt` is retained once the order is built. S4's signature rests on "the list carries an address, not the ad".
- **The tie order is the read's id order (V2).** "Then `Id` ascending" has no defined comparer in memory. The codebase itself says `Guid.CompareTo` and Postgres's `uuid` order differ
  (`BrowseCriterionAdsQueryHandler.cs:33-36`; the architect did not verify that for .NET 10), and `JobAdId` is not `IComparable`. So no id is compared in memory. The parts are built first. The
  catalogue is a stable `OrderByDescending(r => r.PublishedAt)` into a new array, and the source list is never sorted in place (`List.Sort` and `Array.Sort` are unstable). Ads published at
  the same instant keep the read's SQL `ORDER BY` id order, exactly the parts' order. The oracle for ties is ordinal order on `ToString("D")`, as in `ListedJobAdSnapshotSamplerTests`.
- **Newest first (CTO F4).** New ads land on page 1, the most-crawled page, and humans read newest first. Positions shift between ticks, which is inherent to a paged moving list: the
  sitemap's membership by id is the completeness guarantee, not the catalogue walk.
- **What proves the sitemap's signed properties did not move (V1, triage).** The CTO's "S4's tests must pass unchanged" cannot be built: an overload next to the Guid factory makes
  `Create(AsOf, [])` ambiguous (CS0121), and a renamed Guid factory would leave a production factory whose catalogue view no production path creates (AGENTS.md §5, Tests). So `Create` takes
  rows. Only the arrange lines of `ListedJobAdSnapshotTests.cs` and `GetListedJobAdIdsQueryHandlerTests.cs` change, through a `Rows(params Guid[])` helper; `git diff -U0` shows no `Should*` line
  touched; and every other S4 test file is byte-identical. The wire is exactly `ids`, the parts are in id order, membership comes from `IsListed`, the bound is at most 15 minutes, and
  publication is by compare-and-swap.
- **One PR.** The widening is a preparatory commit (P1) inside the S5 PR and never merges ahead of it, because the sitemap's delivered D1 would otherwise change with no signature that reads it
  (security-auditor).
- **No kommun or län in the snapshot, and no generic keyed-view type (stage-A A1).** A hub adds one column to the same projection and one grouping in the same `Create`, ids only, as the
  preparatory commit of the first hub step; it then inherits this decision whole (membership, order and count from the snapshot, content live through `IsListed(now)`, the 15-minute bound). A
  hub never uses a second sampler, and never the `/jobb` facet counts: they count `Status == Active`, not `IsListed`, and a count taken per request is the class rule (d) bans. The reason is
  YAGNI judged by what it costs (Fowler 2015): waiting costs nothing, while carrying the view now costs review surface in a component `security-auditor` has signed, and a member nothing reads
  is Speculative Generality.
- **Docblocks are updated in the commit that makes them incomplete (architect NH5):** `ListedJobAdSnapshot.cs`, `ListedJobAdSnapshotSampler.cs`, `ListedJobAdSnapshotLimits.cs`,
  `JobAdPublication.cs` and `PublicJobAdsEndpoints.cs`, and the sampler's log template ("the sitemap answers 503"). S4's test pins the properties and the event id, not the text.

### D4 — The query: availability before range, content per request, one naming helper (CTO F3; architect V5 and the M4 oracle)

- **The query.** `GetPublicJobAdCatalogueQuery(int Page) : IQuery<PublicJobAdCataloguePage>` is neither `IAuthenticatedRequest` nor `ICapturesRecentSearch` (D6). Its validator requires `Page`
  in 1..`MaxPage` (`InclusiveBetween`). `PublicJobAdCatalogueLimits` holds `PageSize` and `MaxPage` in `JobAds/Queries/GetPublicJobAdCatalogue/`.
- **The handler** takes `IListedJobAdSnapshotReader`, `IAppDbContext`, `ITaxonomyReadModel` and `IDateTimeProvider`, and works in this order:
  1. It reads `now` and `reader.Current` once (a unit test checks `Received(1).Current`). No snapshot, or `!IsFreshAt(now)`, is `Unavailable`. **Availability is judged before the page range,**
     so an outage never reads as 404.
  2. `totalPages = max(1, ⌈Count / 20⌉)`. A page above it is `NoSuchPage`. Page 1 always exists, and with `Count = 0` it is a measured zero (ADR 0120).
  3. It slices `CatalogueOrder`. An empty slice short-circuits: no database read and no taxonomy call (`BrowseCriterionAdsQueryHandler.cs:163-165`).
  4. One round trip over typed ids: `db.JobAds.AsNoTracking().Where(j => typedIds.Contains(j.Id)).Where(JobAdPublication.IsListed(now))`, projected to a narrow anonymous row (`Id`, `Title`,
     `Company.Name`, the kommun and län concept ids, `PublishedAt`). It never materialises the entity, because of `RawPayload` (ADR 0160 D1), and it never writes `.Contains(j.Id.Value)`, the
     form that does fail (`RecruiterErasureMatchQuery.cs:314-315`). The SQL form of the predicate means the text of an ad that will not be shown is never read, as in
     `GetPublicJobAdQueryHandler`.
  5. All the page's kommun and län ids are resolved in one `PublicPlaceNames` call.
  6. The rows are re-sequenced to the slice order. A row the read did not return has ended or been erased since `AsOf`; it drops out and is **not** backfilled (precedent:
     `BrowseCriterionAdsQueryHandler.LoadPageAsync`).
  7. It returns `Available(new PagedResult<PublicJobAdSummaryDto>(items, snapshot.Count, page, PageSize))`.
- **The handler logs nothing.** No log line on either path carries a title, a company or an id.
- **One place-naming helper (P2; architect V5).** The kommun and län naming in `GetPublicJobAdQueryHandler` moves to `internal static class PublicPlaceNames`
  (`ResolveAsync(ITaxonomyReadModel, IEnumerable<string?>, ct)` and `NameOf`) in `Jobbliggaren.Application.JobAds.Queries`. It is static so that both handlers keep `ITaxonomyReadModel` in
  their constructors and `TaxonomyAclLayerTests` still lists every consumer: an injected helper class would take the detail handler out of the list and leave "never in a command or write
  use-case" unguarded. The pinned semantics are preserved (input order, a null id never sent, ordinal `Distinct`, no call for an empty set), the shared empty result is
  `FrozenDictionary<string, string>.Empty`, and the detail handler's tests pass unchanged. Two consumers of one knowledge piece ("names for the public surface; null for an id the snapshot
  does not know; a null id is never sent", ADR 0171) is the DRY threshold, because the knowledge is the same and not because of the count (Hunt/Thomas 1999). The new allowlist row in
  `TaxonomyAclLayerTests` sorts before the detail handler's.
- **The lifecycle registry (ADR 0113).** The sampler's entry text becomes `.Select((Id, PublishedAt))`. The new handler is registered `Active` under
  `…GetPublicJobAdCatalogueQueryHandler.Handle`, naming `IsListed` and the snapshot's slice; the read stays at statement level in `Handle`, because a private method would make the method's
  name the key.
- **Measurement M4 and the fallback.** The codebase contradicts itself on whether `List<JobAdId>.Contains(j.Id)` translates: five comments (in `MatchScorer.cs`, `JobAdEmployerReader.cs`,
  `PerUserJobAdSearchQuery.cs` and `ListNewFollowedCompanyAdsQueryHandler.cs`) say it fails, while `BrowseCriterionAdsQueryHandler.cs:169` and `EraseRecruiterAdsCommandHandler.cs:162-166` run it against Postgres, under Testcontainers tests that require it
  (`CompanyWatchCriteriaEndpointsTests.cs:327-358`, `RecruiterErasureIngestTests.cs`). The repo decides the key form; M4 runs anyway, as the catalogue's own Testcontainers test, with the
  composed form. If it fails, the content read moves behind an Application port (`Application/JobAds/Abstractions`) that answers with a narrow Application row. Infrastructure implements it with an
  interpolated, parameterised `FromSql` over `= ANY(…)` and composes `.Where(JobAdPublication.IsListed(now))` over it in LINQ (precedent: `MatchScorer`). **Never `FromSqlRaw` with
  concatenation, never a hand-written SQL copy of the predicate** (security-auditor; that would be trigger 11), and never a provider package in Application (AGENTS.md §2.1, axis 3). The
  handler carries a one-line pointer to its oracle test, as `EraseRecruiterAdsCommandHandler` does.
- **The five wrong comments are not corrected in S5 (triage).** Five comments, in the four files named above, claim the list form on the key does not translate. The post-`Select` `.Value`
  half of the same comments is true (`RecruiterErasureMatchQuery.cs:314-315`) and the issue keeps it. Fixing them is new prose in code owned by other lanes, across three bounded contexts, and would widen the diff
  `security-auditor` signs. An issue is filed once M4 is green, asserting only the list form on the key, with M4's date and the three existing paths as the adjudication (Implementation).

### D5 — The row: six members, and a component that can render nothing else (CTO F5; design)

- **The DTO.** `PublicJobAdSummaryDto(Guid Id, string Title, string CompanyName, string? MunicipalityName, string? RegionName, DateTimeOffset PublishedAt)`. On the wire:
  `{"id","title","companyName","municipalityName","regionName","publishedAt"}`, with `id` a lower-case GUID. `PublicJobAdSummaryDtoContractTests` pins the member set, a wire test pins the JSON
  keys, and `PublicJobAdCatalogueBoundaryTests` (Architecture) binds the web's strict row schema to the DTO's members and its strict outer schema to `PagedResult<T>`'s five serialised
  members, so that a member added on one side does not turn every catalogue page into a 500 that only production would show (architect V3, widened by security-auditor m3).
- **Never in a row.**
  - `Description`, or any excerpt: free text can name a person (ADR 0166 W1).
  - `Url` or any Arbetsförmedlingen id: W5's "the link to Arbetsförmedlingen is on the page and never in the sitemap" must hold for the catalogue too.
  - `CreatedAt` (ADR 0160 D4), and `ExpiresAt`, which the job page's DTO carries.
  - The organization number, contacts, `RawPayload`, concept ids, employment type and occupation.

  The kommun and län names are register data, and `publishedAt` is the source's own date, already public on the page.
- **Why these six.** Art. 5(1)(c): the row carries what a job-seeker needs to choose, and nothing else. The employer is in the row because ADR 0166 D1 names a search "for the work or the
  employer", and a job row without its employer fails its purpose.
- **Rendering.** The kommun, falling back to the län (nothing if neither is set), then the company, the date through the house `formatDate` and never a literal (triage), and the title as the link
  text. The list is an `<ol>`, because the order is content. There is one `h1`; each row has an `h2` that holds
  exactly one plain link. No `.jp-btn--primary` and no icon per row beyond the place marker, which is hidden from assistive technology and has its own text (design).
- **A component of its own.** `PublicJobAdList` is typed to the summary DTO and can render nothing else (ADR 0165 D4's "one forgotten prop" rule). It is never `JobAdCard`, which takes the
  private `JobAdDto`.

### D6 — The wire, and a result with three cases (CTO wire; architect NH1–NH3; triage)

`GET /api/v1/public/job-ads/catalogue?page={n}` lives in the anonymous `PublicJobAdsEndpoints` group (ADR 0160 D1). The fixed segment does not collide with `{id:guid}`. `page` is required, so a
missing parameter is the framework's 400, as for `listed-ids`.

| Status | When | Body or headers |
|---|---|---|
| 200 | the snapshot is fresh and `1 ≤ page ≤ max(1, totalPages)` | exactly `{"items":[6-member rows],"totalCount":n,"page":p,"pageSize":20,"totalPages":N}` |
| 400 | `page` is outside 1..4000, or does not bind | The two roads (validator and binder) differ; the tests check status and `no-store` |
| 404 | the snapshot is fresh and `page > max(1, totalPages)` | No body is needed: the web reads the status only |
| 429 | the endpoint's own policy (D7) | `Retry-After` |
| 503 | no snapshot, or one outside its bound | `Retry-After: 60` |

- **The 200 body has exactly five keys.** `totalPages` is `PagedResult`'s computed member, which does not apply the handler's `max(1, …)`. So **on an empty corpus page 1 answers 200 with
  `"page":1` and `"totalPages":0`:** the wire test pins the body exactly as `{"items":[],"totalCount":0,"page":1,"pageSize":20,"totalPages":0}`, and the web's strict outer schema admits a
  `totalPages` of 0 (`pagedResultWithTotalPages(strictRow).strict()`; `pagedResult` has no `totalPages` and, made strict, would reject every 200). There is no `asOf` and no stamp anywhere on
  the wire or the page.
- **`Cache-Control: no-store` is on every answer,** through the existing `/api/v1/public` prefix block in `Program.cs`, which covers the framework's own 400 and 429.
- **The 503's wait** is the endpoint's existing constant, renamed from `ListedIdsRetryAfterSeconds` to `SnapshotRetryAfterSeconds` in the endpoint's commit, because it gains a second consumer.
- **A closed result.** `PublicJobAdCataloguePage` is an abstract record with a private constructor and three cases: `Available(PagedResult<PublicJobAdSummaryDto> Rows)`, `NoSuchPage` and
  `Unavailable`. The endpoint maps it by type, and an unmapped case throws `UnreachableException`. It is not an `ErrorKind`, because 503 is availability and `ErrorKind` does not model it, as
  it does not model 401 (AGENTS.md §3). It is not a bare `PagedResult`, because the answer has arms that are not a page. **That is the union's reason: its 404 and 503 arms.**
- **The capture guards, named correctly (architect NH1; triage).** The CTO's ruling said detaching from `IRecentSearchCaptureResponse` is what stops capture. It is not. The capture behavior
  has three guards, in this order: it returns when the message is not `ICapturesRecentSearch` (`RecentJobSearchCaptureBehavior.cs:43-44`), it returns when the user is anonymous (`:58-59`), and
  only third does it look at the response type (`:55-56`; the inner `PagedResult` still carries the interface, `PagedResult.cs:11`). The union does not exist for capture. Two lines of test pin
  that the query implements neither `ICapturesRecentSearch` nor `IAuthenticatedRequest`.
- **A declared exception, not a missed heuristic (architect NH2; triage).** `PagedResultContractTests` forbids wrappers around `PagedResult` and names the house's two forms, `T?` and
  `Result<T>`. The catalogue is a third form, a closed union, because one arm is availability and no `ErrorKind` models it. `GetPublicJobAdCatalogueQuery_Available_arm_carries_PagedResult`
  names it, and a paragraph in the test's docblock says so. A future `PageSize` input still trips the heuristic as today.
- **Why AGENTS.md §5's "unpaginated list fetches" does not apply:** the answer is paginated, and the read behind it is at most 20 primary-key rows.
- **Logging.** No log line on either path carries a title, a company or an id. The handler logs nothing, the web has no `console`, and `parseResponse` logs the schema path and the error class
  only.

### D7 — The budget: its own policy, and one helper for the three IP token buckets (CTO F9; triage P3; security-auditor approved the numbers)

- **A new policy,** `public-job-ad-catalogue` (`RateLimitingOptions.PublicJobAdCatalogue`): a TokenBucket per client IP, 60 per 60 s in 6 segments, `QueueLimit` 0, and a 429 that carries
  `Retry-After`. `security-auditor` approved the numbers on 2026-10-18. The CTO proposed parity with ADR 0160 D5's anonymous read: it is a starting number for a new endpoint, not a raise, and
  ADR 0174 D5's readings measure it. The test factory raises it like the other IP policies.
- **Its own, never shared and never per user.** It never shares `public-job-ad-read` or `public-job-ad-listed-ids`, so a crawler walking the catalogue cannot starve detail reads (Saltzer &
  Schroeder 1975, least common mechanism; Nygard 2018, ch. 5). A per-user policy would give an anonymous caller `NoLimiter`.
- **What it protects.** Availability, not confidentiality: enumeration is by design. The cost of a call is one round trip of at most 20 primary-key rows and one taxonomy resolve. Row links
  are plain, unprefetched anchors (D10), so a catalogue view spends nothing from `public-job-ad-read`, whose budget does not change.
- **The client address is relayed** through `forwardedHeaders()`. A test pins it, with a row in `ForwardedClientBudgetTests`. The apex has no AAAA record (2026-10-18), so per-address
  partitioning works; ADR 0160 D5's one-bucket-per-/64 rule applies if one is ever added.
- **Tests and rows.** A wiring test (anonymous, its own policy, alone on its route), a strict 429 test, `RateLimitingOptionsTests.Defaults_PublicJobAdCatalogue_Is60Per60sIn6Segments`, the
  `ApiFactory` and `StrictRateLimitApiFactory` rows, and the two `PublicJobAdCatalogue` cleanup rows in `ApiFactory.DisposeAsync`.
- **One helper for the third copy (P3, triage).** The two existing TokenBucket blocks and the new one are one knowledge piece: "an anonymous public job-ad read is a per-client-address
  TokenBucket, `QueueLimit` 0, soft recovery". The catalogue's policy would be the third copy, so a preparatory commit extracts
  `private static void AddIpTokenBucketPolicy(RateLimiterOptions options, string policyName, RateLimitingOptions.PolicyOptions budget)` before the policy exists (Fowler 2018, ch. 3; Martin
  2017, ch. 13: they change together, as the /64 rule would). Each call site keeps its own reason comment, and the helper's docblock carries the shared mechanism. Its scope is the three
  TokenBucket blocks; `LandingPublicRead` and `HealthCheck` use another algorithm under other ADRs and are not touched. Her signature covers behaviour, and the behaviour is pinned:
  `PublicJobAdRateLimitWiringTests`, `PublicJobAdListedIdsRateLimitWiringTests`, `PublicJobAdReadRateLimitTests`, `PublicJobAdListedIdsRateLimitTests`, the two `ForwardedClientBudgetTests` rows
  and `RateLimitingOptionsTests` stay byte-unchanged and green. `TokensPerPeriod` and `ReplenishmentPeriod` are pinned by no test, so the commit moves both expressions verbatim: the diff is
  the evidence, and `code-reviewer` checks it. It is a stated delta for `security-auditor` (D13).

### D8 — The page: statuses, the fetcher and the group (CTO F11, F12, F13, F17; stage-A A2.3)

- **The statuses.**

  | Request | Answer |
  |---|---|
  | `?sida=1`, in any combination | **308** to the bare `/lediga-jobb`, by `permanentRedirect()` in the page before any read |
  | `sida` does not match `^[1-9][0-9]*$`, or appears more than once | **404, with no API call** |
  | the API answers 404 (`NoSuchPage`) or 400 | **404** |
  | the API answers 429 or 503, any other 5xx, a timeout, a network error, a redirect, or a body that fails Zod | the page **throws, so Next answers 500** |
  | otherwise | 200 |

  A nonexistent pagination URL answers 404 (Google's faceted-navigation page). The 500 is the same 5xx the job page's render-failure arm gives (ADR 0165 D2), and the epic's "5xx, never
  404/410" holds. **There is no proxy probe.** ADR 0165's probe exists because the twin must choose which internal page to render before the first byte. The catalogue decides its own 404,
  because the group has no `loading.tsx` and no Suspense; a probe would double every view's cost and edit `proxy.ts` only to change a 500 into a 503, and Google slows crawling on any 5xx. Klas
  may take it as an override, as a snapshot-only availability probe in `proxy.ts`; lapse trigger 6 reconsiders it.
- **`Cache-Control: no-store` on 200, 404 and 500.** Next sets it for dynamic renders, and the smoke script pins it.
- **The fetcher** (`lib/api/public-job-ad-catalogue.ts`) follows `public-job-ad.ts`: `cache()`, `forwardedHeaders()`, `cache: "no-store"`, `redirect: "manual"`, a 5-second timeout, and Zod
  through `pagedResultWithTotalPages(strictRow).strict()` (D6). It relays only what `pickForwardedHeaders` returns, so no `Cookie` and no `Authorization` reach the API, and a test pins the key
  set. There is no ISR, no static render, no `generateStaticParams` and no fetch revalidation.
- **The canonical and the robots meta.** Every page is `index, follow` with an absolute self-canonical (F17). One builder, `publicCatalogueUrl(page)`, makes the canonical, the pager's hrefs and
  the sitemap entry (D12); precedent `publicJobAdUrl`. Google: do not use the first page as the canonical, give each page its own, and link to the next page and back to the first, both of which
  `buildPageItems` does. `noindex, follow` is rejected: it would defeat the hub, and Mueller (2017) said a long-term `noindex` is eventually treated as `nofollow`.
  `security-auditor` approved `index, follow` and does not require `noindex`; Klas may still choose it on `?sida≥2` as an extra lever, and she does not recommend it.
- **The metadata.** The title is the label on page 1 and carries the page number from page 2 on, under the house template "%s | Jobbliggaren". The description is static and carries no number. The
  page names the shared `/opengraph-image` (ADR 0165 D4). **No row value and no count reaches the title, the description, OpenGraph or Twitter,** and a sentinel test pins it. There is **no
  JSON-LD of any kind** on the page, in line with ADR 0166 D10 trigger 6 and Google's rule of marking up the leaf page only.
- **No breadcrumb and no `BreadcrumbList`** (stage-A A2.3). A one-level crumb repeats the header's link to the same URL on the same page; a two-level crumb's middle step would be a hub that does
  not exist. Breadcrumbs belong to the first hub step or later, where `security-auditor` decides whether a second JSON-LD block on `/jobb/<uuid>` falls under ADR 0171 D4 and D10 triggers 6–7.
- **The group: `(public-job)/lediga-jobb/page.tsx`.** The group already holds the fitness functions a public job-ad surface needs: no `loading.tsx` and no Suspense, rendered per request, no ad
  text as HTML, JSON-LD only in `public-job-ad.tsx`, a closed set of API readers, and no `console`. They are one knowledge piece (CCP and CRP, Martin 2017, ch. 13), so the catalogue
  inherits them instead of getting a second copy. The group's identity becomes "the anonymous job-ad surface"; its layout docblock and `public-job-group.test.ts` change accordingly, and the
  test's page list gains `lediga-jobb/page.tsx`, its reader list gains `lib/api/public-job-ad-catalogue.ts`, and `PUBLIC_SURFACE` covers the new files. `(marketing-inner)` was rejected: it is a
  static copy group without these pins, and a new group would copy them.
- **One page for everyone** (F13). The page reads no cookie and no session, so a signed-in visitor sees the same catalogue, which is not cloaking. A row then opens `/jobb/<uuid>` with the
  cookie, and the private page answers.

### D9 — What the page says: source and order in the lede, one `data-nosnippet` block, an empty state (CTO F6 and F17, as amended by design M1 and M2; triage)

The rules below are the decision; the strings live in `messages/{sv,en}` and are not quoted here (an ADR that quotes them goes stale on the first copy edit).

- **The lede carries the source and the order, and no number.** The `h1` and the lede are the page's intro. They stay outside the block below and stay snippetable. The lede is true at every
  tick, because it states nothing that moves.
- **The count line, the list and the pager sit in one `<div data-nosnippet>`.** The attribute is valid on `span`, `div` and `section` only, so it cannot sit on the count line's paragraph; one
  common `div` serves the three. The pager's "page n of N" line is also a number. Indexing and links are untouched. Why: a snippet is a copy of the page that we cannot update, a count in it
  goes false at the next tick, a hub snippet that quotes a list is stale by construction, and it would put a sole trader's name into a copy we cannot reach (ADR 0166 W2, W4). Bing honours the
  attribute for snippets and AI answers since 2025-10-15 and still indexes the page normally (security-auditor). **This changes the CTO's first rulings (triage):** F6's count line sat outside
  F17's wrapper, which contradicted F6's own reason, and the wrapper surrounded the `<ol>` only. It also widens `security-auditor`'s plan-read condition (i), "a `div` around the `<ol>`
  only"; her reason holds and the exposure only falls, and the delta is hers to accept at code review (D13).
- **The count is shown, in the page body only.** It is the snapshot's `Count`, exact as of `AsOf` and free, so ADR 0120 clause 1 ("exact when it is cheap") allows it. It is not saturated,
  because past the ceiling the catalogue refuses and does not cap, so #1149's reading of clause 4 applies: an uncapped total may be stated, as `/jobb` already states its own. The count line is
  pluralised, so one ad reads as one. The pager's line uses `summaryPagesOnly` (`showTotalCount={false}`), because the word "träffar" is search vocabulary and the magnitude has its own line; a
  call-site pin carries ADR 0120's enforcement rule. **Never in `<title>`, the description, OpenGraph or Twitter:** a number in a search engine's copy goes false.
- **A count of 0 gives the empty state.** The page renders the house empty state with a next step, and no count line, no list and no pager, and no `role="status"` (precedent
  `job-ad-list.tsx`). The status is unchanged: page 1 on a count of 0 is 200 (D6, D8). An empty corpus has nothing to index, and in steady production the state does not occur.
- **A recorded divergence, not a defect.** The landing's "aktiva annonser" counts `Status == Active`. ADR 0160 D2 deliberately left the landing stats on their own select, so the two numbers
  carry different labels and are both true. Aligning them is a separate change-reason, ADR 0064's Worker aggregate. Measurement M2 records the gap.
- **Pins (design).** One `h1`, and an `h2` with exactly one link per row. The `<ol>`, the count line and the pager are in the same `div[data-nosnippet]`, and `main` has no number outside it.
  A count of 1 reads in the singular; 0 gives the empty state, with no `<ol>` and no pager. The pager call pins `showTotalCount={false}`. No `!` and no emoji in the new keys. The label
  "Lediga jobb" is one key: the header, the footer, the `h1` and page 1's title read it, and a test pins that every page's `h1` and page 1's title equal it, before the template.

### D10 — Links: plain anchors on rows, and nothing toward the catalogue prefetches (CTO F10; triage P4; P5 withdrawn)

- **Row links are plain `<a href>`.** Three reasons: a prefetched row costs one detail-budget token per visible row (20 rows against 60 a minute); client navigation into the twin's Flight
  path was never measured (ADR 0165 D2); and for a signed-in visitor a client navigation could be intercepted into the `@modal` slot (ADR 0165 D1). A full document is the measured path, and
  it settles the epic's "modal and Back/Forward regression" item by construction.
- **The pager's links.** `JobAdPagination` gains an optional `prefetch?: false` prop, forwarded to every `Link`; the default is unchanged, so `/jobb` is untouched (precedent
  `header-strip.tsx`'s `brandPrefetch`). A pager of its own was rejected: it would copy the house pager's markup. P4 carries the prop, tested in both polarities.
- **No link to `/lediga-jobb` anywhere prefetches** (F10): not in the header, the footer, the pager or the segment's not-found. Whether a dynamic route with no `loading.tsx` runs its data on
  prefetch is not measured, and `prefetch={false}` makes the question moot. Otherwise every page view on the site, `(app)` included, might spend a catalogue call.
- **The pager's page-number links stay ungrouped** (the CTO's re-ruling of triage item 4, recorded in the triage report's addendum). The triage first ruled them grouped as P5, on
  design-reviewer's m1. The build found #1149's rule, documented and pinned in `job-ad-pagination.test.tsx:167-198`: a link label is a target you click, not a sentence about how many there are,
  so only the summary line is grouped. m1 does not hold, and nothing was built; design-reviewer closes it.
- **Navigation.** After a pager click Next focuses the segment root (`layout-router.js:236`); the expected outcome is that `main#main` takes focus and the page scrolls up, which is walked
  through and not assumed (M6).

### D11 — The entries: the header link, the footer entry, no hero link, and the segment's not-found (CTO F14–F16, as amended by design M3 and M4 and the triage; stage-A A2.2)

- **The header.** One link, "Lediga jobb" → `/lediga-jobb`, `prefetch={false}`, is the second child of the existing `<nav>`, after the brand. It is navigation, so it belongs in the landmark;
  the stats stay outside it. It appears on every surface that mounts `SiteHeader`, the auth pages included, and it is visible at every width: it is never in a menu, never shortened and never
  hidden, because on a phone it is the landing's entry (WCAG 1.4.10). Tab order is skip link, brand, the link, language, "Logga in", one stop per row, the pager, the footer.
- **Klas's approval** ("Ja (Rek.)", 2026-10-18) of `design-reviewer`'s escalation: the link goes in the header's nav on the landing and on every surface with `SiteHeader`, which departs from
  HANDOVER-v3 §6.4 ("Endast brand + live-stats"; the file says it is broken "endast efter explicit Klas-godkännande"). The consequence he approved: **on the landing the stats are hidden below
  981 px instead of below 833 px.** A note "ÄNDRAD 2026-10-18" goes under §6.4 in the main copy (the file is gitignored), and `DESIGN.md` §6 gains a point on the public header in this PR
  (tracked; a spec edit, so `dotnet-architect` and `code-reviewer` are in the round, CLAUDE.md §9.2; no token changes).
- **`aria-current="page"` is exact (stage-A A2.2; design).** A small client island sets it when `usePathname() === "/lediga-jobb"`, an exact match. The query string is not counted, so every
  catalogue page is the current page; `/`, `/lediga-jobb/x`, `/lediga-jobbx` and `/jobb/<uuid>` are not. A prefix match is excluded, because it would mark every future hub as the current page.
  The label is passed as children from the server `SiteHeader`; the island calls no `useTranslations`, so the client payload does not change. The current page is `--jp-fw-semibold` with a 3 px
  `--jp-accent-700` bar on the row's lower edge.
- **The link's form (design M4).** It is `.jp-nav__link` as the second child of the existing `<nav>` and never the `.jp-nav` container, which hides at ≤900 px. It is in `--jp-ink-1` in rest and
  hover (the global link rule would make it accent green), with a 44 px floor at ≤768 px and the bar's formula from the existing bar. It needs `globals.css`, which is `hotspot:css`.
- **The narrow ladder, (a) (design Blocker; triage item 7).** The header's narrow steps cannot carry the link and are re-derived, keyed on the row as before. `design-reviewer` measured them
  with a harness that lives only in the session scratchpad and is not committed, calibrated against the landing's documented step; the result is no overflow, no wrap and one row (73 or 89 px)
  from 320 to 1920 px in Swedish and English, with the link in semibold.

  | Step | Today | With the link |
  |---|---|---|
  | stats on the landing | ≤832 px | ≤980 px |
  | tagline | ≤640 px | ≤680 px |
  | lockup and language name, action rows | ≤480 px | ≤620 px |
  | lockup and language name, all rows | ≤360 px (lockup only) | ≤520 px |

  A new step at ≤420 px sets the gap to 8, the inner padding to 16, hides the language chevron, and reduces the right cluster's and the link's padding. **The CTO confirmed (a), her required
  table, over (b)** (stats ≤910 and tagline ≤980 on the landing only). Klas approved the link on (a)'s consequence, and (b) would hide the tagline on the landing across 681–980 px, which he
  was never asked about. One brand lockup at a given width on every page is also kept. If she substitutes (b) at code review, the consequence goes to Klas again before the merge.
- **The footer** (F15). "Kom igång" gets "Lediga jobb" as its first entry, before the auth entry, through `InformationLink` with `prefetch={false}`. #1738's reason is "two differently named
  links to one URL"; this is a second door with its own URL and its own name, so the reason does not bar it. #390's reason for no "Sök jobb" is that `/jobb` is auth-gated; `/lediga-jobb` is
  public, and the test that pins "no Sök jobb" still holds. `/lediga-jobb` is never added to `INFORMATION_PATHS`. The footer link also appears in `(app)`, `(admin)` and `(guest)`, under the
  "one footer" rule of #256.
- **No separate hero link on the landing** (F16). The landing document already carries the entry twice, in its header nav and its footer; a third link to the same URL repeats the door (the #1738
  principle), and the landing's pins stay closed (one `.jp-btn--primary`, a five-row account card). If Klas overrides: a secondary text link labelled "Lediga jobb", outside the account card,
  never primary, with `prefetch={false}`.
- **The segment's not-found (design m2; triage item 5).** `lediga-jobb/not-found.tsx` is a file convention beside `page.tsx` in the same segment. It creates no URL, so it holds against
  constraint 1 of stage-A A2 (Next 16.3.8, `not-found.md`). An unmatched `/lediga-jobb/x` still renders the root not-found. It uses `notFoundMetadata()`, owns its `<main id="main">`, links back
  to the catalogue with `prefetch={false}`, reads nothing from the API, and falls under `PUBLIC_SURFACE` automatically. Its docblock says a future child segment inherits it unless the child has
  its own, which keeps hubs open (stage B or S6 decides). The most likely 404 is a page past the end after the list shrank, and the group's not-found offers the start page, so this file offers
  the better next step. There is no shared 404 component: the two carry different next steps, and that is the whole difference (YAGNI).
- **Tokens and DESIGN.md §8.** No token changes. `DESIGN.md:189` stays true and unchanged, because the catalogue carries no notice (D13).

### D12 — The sitemap entry (CTO F8)

- **`/lediga-jobb` is the 15th URL of `/sitemap-sidor.xml`, and no `?sida=n` URL is in any sitemap.** A page URL is a position in a moving set, not a document: that is ADR 0174's rejection of
  3a, applied one level up. The hub is the one stable entry point.
- **ADR 0166 D10 triggers 8 and 9 do not fire** (the CTO's reading, which `security-auditor` agreed to). Trigger 8 is satisfied: the entry is `loc` only, and the `loc` is page 1's own canonical
  from `publicCatalogueUrl`, with a test that pins the two as equal. Trigger 9 governs membership of the job parts; the pages part is a fixed list of site pages that carries no ad, and the hub is
  one of them. Read literally, the 14 marketing URLs would already fire it.
- **What changes.** The hub joins `lib/seo/marketing-sitemap.ts` (or a sibling list). `seo-canonical.test.ts`, `seo-metadata-routes.test.ts` and `scripts/public-job-smoke.mjs` (14 → 15) are
  updated. ADR 0174 D3's table row for the pages part reads 15 (amendment).

### D13 — The legal record has one home, and the merge waits for it (security-auditor M1–M3 and m1–m6; Klas's answers)

- **ADR 0166 D10 trigger 1 names "the S5 catalogue",** so it fires by its own text. The conclusion is withdrawn for the catalogue only; the page and the sitemap keep their conclusions. Klas
  re-takes it for the catalogue in a new dated amendment at the end of ADR 0166. **That amendment is the single home of the legal outcome;** this ADR points there and does not restate it
  (CCP, Martin 2017, ch. 13; AGENTS.md §8 point 9: one decision, one home). It is **Proposed** until `security-auditor` signs it.
- **The merge is gated.** S5 may not merge, and so may not go live (ADR 0154), until `security-auditor` has signed that amendment at her scoped re-check of the PR's final head, read with this
  ADR as delivered and with the third DPIA addendum as Klas approves its text. Merging without it is a Blocker (her M1): the processing would rest on a withdrawn conclusion, and the notice's
  "Vi har vägt de intressena mot dina rättigheter" would be untrue for it (Art. 12(1), 14(2)(b)).
- **Klas's answers, 2026-10-18,** by AskUserQuestion, to `security-auditor`'s escalation and `design-reviewer`'s. They are quoted without edits:

  | # | Asked by | His answer | What it decides | Recorded in |
  |---|---|---|---|---|
  | 1 | security-auditor, question 1 | "Ja (Rek.)" | the ADR 0166 D10 re-take for the catalogue, as her section 1 lists it | the ADR 0166 amendment |
  | 2 | security-auditor, question 2 | "Ja, tredje tillägget (Rek.)" | a third addendum to DPIA #456 §8-B and not a new full DPIA: no high risk in the Art. 36(1) sense, so no prior consultation | the ADR 0166 amendment (D8), and the addendum, a local file the driving session writes in the main copy. **His approval of its text is still owed** |
  | 3 | design-reviewer | "Ja (Rek.)" | the "Lediga jobb" link in the header's nav, departing HANDOVER-v3 §6.4, with the landing's stats hidden below 981 px | D11, `DESIGN.md` §6, and the local HANDOVER-v3 note |
  | 4 | relayed by the SEO/AIO-audit session, earlier | "Ja, S5 väntar (Rek.)" | S5 waited for that audit's stage-A phasing ruling | the stage-A addendum; D1, D3, D8, D11, D14 |

- **What else the record owes, and where.**
  - The merge-day S2-P3 reading (security-auditor M2; measurement M1): the ADR 0166 amendment's D11 item and the record table of `docs/runbooks/public-job-page-publication-count.md`. A validated
    count above 0 fires D10 trigger 2, and S5 stops.
  - The third addendum to DPIA #456 §8-B (M3): R-842-9 re-taken for the catalogue (her reading: Med × Med); a new R-842-10 for a sole trader's name in `company_name` on the page, in
    `hiringOrganization` and in the catalogue (Low × Med); the Art. 36(1) determination re-confirmed as not a high risk, on the second addendum's reasons; R-842-3, R-842-4 and R-842-8 unchanged,
    because the catalogue carries no free text; the Art. 9 row untouched. She does not sign an amendment whose DPIA states a limit that the catalogue removes.
  - The ADR 0106 D9 read-path table (m2) and ADR 0174 D9's trigger pointer (m6): the dated amendments of the same date.
  - The erasure runbook (m4): `docs/runbooks/recruiter-pii-erasure.md` §2.4a gains two sentences and no new check step. The catalogue needs no check, because each row is classified at request
    time (an integration test pins that an erased ad is absent at once) and a position in the list is not addressable; and "what cannot be reached" gains a search engine's index copy of a list
    page, refreshed at its next crawl, whose rows are not quoted in snippets. Asking a search engine to remove a list URL would take the hub out of results for months, and Art. 17(2)'s
    "reasonable steps" does not reach that far.
  - The processing register (m5), local, in the main copy before `agents-done`: one sentence at line 98 that from the merge date the listed ads' title, employer, kommun or län and publication
    date are also shown in a public paginated list, assessed in the ADR 0166 amendment (Art. 30(1)(b) and (e) unchanged); and a correction at line 83, which calls a sole trader's name "publik
    metadata eftersom JobTech redan publicerat", the ground ADR 0166 D3 rejects, to point to W2.
  - `BUILD.md`'s endpoint line and `docs/threat-model.md`'s enumeration paragraph: the catalogue makes title-and-company enumeration cheap.
- **No notice on the catalogue** (the CTO ruled it; `security-auditor` agreed). The catalogue publishes no free text, and the notice's grounding on the job page (ADR 0165 D4) is free text
  that can name a person. Art. 14(5)(b) asks that the information be publicly available, and it is. `DESIGN.md:189` stays true. ADR 0166 D5's "linked from every public page" was not true as
  delivered, and the amendment states the delivered fact (m1).
- **Scope.** S5 adds no PII field, recipient, sub-processor, transfer, migration, package, compose change or ACL change (`security-auditor` verifies that at the diff). The release is image only
  (ADR 0154 §4). A diff under `deploy/`, a migration, a new package, or an edit to `proxy.ts` or `next.config.ts` is a stop that comes back to the CTO. It is one PR, lane BE+FE: an endpoint
  merged on its own would be a contract with no consumer.
- **Stated deltas for her code-review brief (triage).** Each changes no property she signs, none needs a new plan read, and she reads the diff in the round: V1's arrange-line change with the
  `-U0` evidence (she required "S4's tests pass unchanged"; there is no buildable alternative, D3); the wrapper's wider content (D9); commit P3 (D7); and 14 → 15 in the pages part, which she
  already stated. If she does not accept the wrapper's, it comes back to the CTO.

### D14 — Lapse triggers

Single named home: this ADR. Named reader: Klas. Nothing detects a trigger automatically: each is checked by reading this list when the event is proposed or reported. This decision is
re-examined when:

1. A filter, sort, search or place parameter appears on the catalogue (D1). It forces the noindex-versus-disallow decision.
2. The first curated landing page is built. **The hub's preparatory step adds a per-place view to the snapshot from the same read, never a second sampler** (stage-A A1; D3).
3. Search Console shows listed ads unindexed while deep catalogue pages are never crawled (then the stride pager of D2, or breadth, is weighed).
4. The content read is over its ADR 0045 budget in the `LoggingBehavior` data.
5. A second process needs the snapshot. This is shared with ADR 0174 D9 trigger 1, which S5 does not fire: the catalogue is in the API process.
6. Search Console shows catalogue 5xx that affect crawl (then F11's probe, D8, is reconsidered).

**The legal triggers are ADR 0166 D10's** (1–12, with 5–7, 8–9 and 10–12 added by the amendments of 2026-10-17, 2026-10-18 (S4) and 2026-10-18 (S5)) and have their own home there
(security-auditor m6). This list's trigger 1 is an engineering trigger; ADR 0166 D10 trigger 1 is the legal one, and a place as a path fires it.

## What is not measured, and the measurements owed

**Not measured by anyone, and not claimed:**
- Google's and Bing's uptake of the catalogue: whether and when they crawl it, how deep, and how large a share of the listed ads they index. Whether "Lediga jobb" becomes a sitelink is
  informative only; Google says sitelinks "are automated".
- Whether client navigation into the twin's Flight path works (ADR 0165 D2), and whether a dynamic route with no `loading.tsx` runs its data on prefetch. D10 makes both moot.
- How Bing treats `data-nosnippet` beyond Bing's own blog (2025-10-15, read by security-auditor). A secondary report (PPC Land) says Bing uses `noarchive`, not `data-nosnippet`, for Copilot
  grounding; it was not confirmed at the primary source and is not relied on.
- What the 410 listed ads without an organization number are (2026-10-18, 12:57 UTC): a sole trader or not is unknown.
- Copies out of reach: web-archive snapshots of list pages, search-engine caches, and how many aggregators ingest a page.
- Catalogue 5xx, which are **not measurable on the box**: Caddy logs only its own 5xx, so Search Console is their only source.
- The snapshot's memory and sort cost are **computed, not measured** (architect): at the ceiling of 80 000, retained memory is about 2.6 MB (parts 1.28 MB plus the catalogue 1.28 MB, against
  about 1.28 MB in S4), the transient cost per tick is about 2.6 MB of rows and about 1.6 MB of sort keys and index, all on the large object heap once every five minutes, and the sort is about
  1.3 million comparisons on the sampler's thread, never per request.

**Owed before the merge** (the CTO's M1–M7; each is entered under *Measurements* when it is taken, and none is estimated):
- **M1, on the box, counts only (CLAUDE.md §9.2 (a)): the merge-day S2-P3 reading.** The same query blob (`8c02e37`), on the merge's UTC day, before `agents-done`; the validated count must be 0
  and `--status` consistent. It is recorded in the ADR 0166 amendment (D11) and in the publication-count runbook's record table. A count above 0 is D10 trigger 2, and S5 stops. If the merge slips
  past that UTC day, the reading is taken again.
- **M2, on the box, counts only.** At one minute: the `IsListed`-equivalent count and the `status = 'Active'` count. The gap is recorded in this ADR's Consequences, for D9's divergence.
- **M3, on the box.** ADR 0174 D5's readings 1 and 2 gain `grep -c 'Path=/api/v1/public/job-ads/catalogue Method='`. The triage lists "the box counts M2 and M3" among the items before `agents-done`.
- **M4, Testcontainers.** Whether `List<JobAdId>.Contains(j.Id)` composed with `IsListed(now)` translates (D4). Recorded: the content read's `ToQueryString()` (whether EF 10 with Npgsql binds
  a `uuid[]` parameter as `= ANY(@p)` or expands an IN list, which decides the plan cache), and `EXPLAIN (ANALYZE, BUFFERS)` for the content read and for the widened sampler read, the sampler
  before and after widening, on the same seed of about 45 000 rows written through `JobAd.Import`, with an explicit `ANALYZE job_ads` after the seed (AGENTS.md §5 Tests and §3.6 name the
  statistics regime). It is a one-off measurement with the command that regenerates it, not a committed CI test, because of the wall clock. It should show whether the content read uses the
  primary-key index with `id = ANY(…)` as the index condition, and that the sampler's plan changes only its target list.
- **M5, smoke.** The statuses run with both a browser user agent and a Googlebot user agent, because Next streams metadata to Googlebot. The canonical must sit in `<head>` for both. If it does
  not for Googlebot, that is also a defect in the delivered job page, and it comes back to the CTO as a STOPP; it is not a quiet `htmlLimitedBots` change in `next.config.ts`.
- **M6, rendered (AGENTS.md §8 point 4).** `design-reviewer` sees the 200, the empty state (count 0), the 404 and the 500 surface, and the header at narrow widths. Her matrix: page 1 at 375, 768,
  1280, 1920 and 3440 px with a long title and company, a row with a län only and a row without a place, and forced colors; `?sida=2` with both ellipses, the last page and a short page;
  the empty state; the 404 for `?sida=abc` and for the last page plus one, with `document.title`; the 500 from a stub 503, a stub 429 and a dead port, with title and focus position; the header
  at the widths of her list from 320 to 1280 px, on six kinds of page, in Swedish and English; a keyboard walk (Enter on the pager's next link, Back and Forward; signed in, a row opens the private page and not a modal);
  and Lighthouse accessibility ≥ 95 with axe at 0. **This is where the 500's framing (#1477) is judged:** the page is expected to render `(public-job)/error.tsx` into the site's frame, but
  the docblock of `error.tsx` says a server error before the first byte "is a real 500 from Next and does not reach here". If `global-error` shows without header and footer, that is a Blocker
  under #1477 and the question goes back to the CTO (F11's probe).
- **M7, the document size and a local Lighthouse run** against the stub-backed production build. Both are observe-only. Either is recorded as a number or as "not measured", never estimated.

**After go-live.** Klas's Search Console readings under ADR 0174 D5 extend to catalogue crawl and indexing; the rule for a raise is that ADR's.

## Alternatives considered

### Alt A — The chosen design (D1–D12)
**For:** one publication predicate for the sitemap and the catalogue; membership, order and count from one read at one instant, so the two agree at every `AsOf`; an erased ad is never served
from memory; the cost of a page does not depend on its depth; the group's fitness functions apply unchanged; no migration, no package, no compose change and no new recipient; hubs inherit it
whole.
**Against:** the trade-offs listed under Consequences.

### F3 — Where the order, the count and the content come from
- **(a) A live OFFSET read per request.** Rejected. A COUNT over the whole corpus inside the handler is the class ADR 0064 rule (d) bans. OFFSET cost grows with depth, and `external_source`
  and `expires_at` are residual heap filters outside the index, so one crawl over 2 112 pages visits on the order of N²/2 rows. Membership would disagree with the sitemap between requests, and the
  private search's filter is not `IsListed`.
- **(c) A snapshot that holds content.** Rejected. It would serve an erased ad's title and company for up to 15 minutes from memory, which breaks the erasure rule and Art. 17(1), and it would
  hold sole traders' names in a process cache. S4's signature rests on "the list carries an address, not the ad".
- **Keyset pagination.** Rejected. It has no page numbers, so URLs become cursors: an unbounded URL space with no stable canonical. A typed-id comparison does not translate, and an in-memory
  slice is O(1) anyway.
- **ADR 0005's path: drop `RequireAuthorization` from `/api/v1/job-ads`.** Rejected outright (the CTO and `security-auditor`). The filter is wrong: `Status == Active` admits manual rows and
  rows past their deadline (ADR 0160 D2). The DTO is wrong: `JobAdDto` carries `Url`, `Source` and `CreatedAt`. It would expose a full-text-search and filter surface, and that group's
  authorization is its PII boundary. For `security-auditor` it would be an auth-boundary Blocker in her class.
- **A second sampler for the catalogue, or kommun and län in the snapshot now, or a generic keyed-view type** (stage-A A1). Rejected as speculative generality ahead of a gate that has not
  opened; the axis, the set of places and the slug scheme are all undecided (D3).

### F4 — The order
- **By id.** Rejected: random to a human, and new ads would land at random depth.
- **By deadline ascending.** Rejected: deadlines can be null, and a human would read the order as an emphasis.
- **"Then `Id` ascending" in memory.** Rejected (architect V2): no defined comparer, and two id orders in one snapshot (D3).

### F5 — The row
- **Reusing `PublicJobAdDto`.** Rejected: it carries the description (W1 on hub pages) and the `Url` (the W5 join).
- **`JobAdCard`.** Rejected: it is the private DTO.
- **Dropping the company.** Rejected: a job row without the employer fails its purpose, and ADR 0166 D1 names the employer. W2 is handled by `data-nosnippet` and by her weighing.

### F6 and F17 — The count and the snippet
- **No count at all.** Rejected: it would decline a true, free answer, against ADR 0120 clause 1.
- **A count in the title or the meta.** Rejected: it becomes false in copies we cannot update.
- **The count line outside the wrapper** (the CTO's first ruling). Rejected in the triage: it contradicts F6's own reason (D9).
- **`noindex, follow` on deep pages.** Rejected (D8).

### F7 — Parameters
- **308 for unknown parameters.** Rejected: a fail-closed allow-list breaks on any parameter a link or the framework adds, and gains nothing over the canonical for URLs we never link.
- **A robots disallow for parameters.** Rejected: it hides the canonical.

### F8 — The sitemap
- **Deep pages in the sitemap.** Rejected: they are positions, not documents.
- **Leaving the hub out.** Rejected: it is the one stable entry point.

### F9 — The budget
- **Sharing `public-job-ad-read` or `public-job-ad-listed-ids`.** Rejected: a crawler walking the catalogue would starve detail reads (D7).
- **Deferring the helper with a third copy and an issue** (triage). Rejected: that writes a known duplicate of security-relevant knowledge to avoid touching signed text, when the behaviour is
  what was signed and it is pinned.

### F10 — Links
- **A default `<Link>` on rows.** Rejected (D10).
- **A separate pager for the catalogue.** Rejected: it would copy the house pager's markup. `ApplicationsPager` needed a different shape, not a different link mode.

### F11 — Statuses
- **The proxy probe.** Rejected (D8); Klas may take it as an override.

### F12 — The group
- **`(marketing-inner)`.** Rejected: a static copy group without the pins, whose seo and smoke derivations assume content pages.
- **A new group.** Rejected: a second copy of the pins.

### F2 — Page size and depth
- **`MaxPage = 100`.** Rejected: it falsifies the outcome line, for a reason (deep OFFSET) that is absent here (D2).
- **A stride pager.** Rejected for now: lapse trigger 3.

### Entries and the header
- **A hero link on the landing.** Rejected (D11); Klas may override.
- **A breadcrumb in S5** (stage-A A2.3). Rejected (D8).
- **Ladder (b)** (stats ≤910 and tagline ≤980, landing only). Rejected (D11).
- **A shared 404 component.** Rejected: YAGNI.
- **Fixing the five `Contains` comments in S5** (triage). Rejected: a foreign change-reason across three contexts that would widen the diff she signs (D4).
- **Filing or skipping the pager's number format** (triage; design m1). Rejected: the defect renders on S5's own surface in a file already in the diff.

### F18 — The record
- **Amendments alone, with no ADR.** Rejected, as in ADR 0174 F5: one decision would be scattered over seven files with no home of its own, and ADR 0005 asks for a separate ADR.

## Consequences

### Positive
- One publication predicate serves the sitemap and the catalogue. Any exclusion that Klas or `security-auditor` decides on goes into `JobAdPublication` and reaches the catalogue with no
  catalogue change, which is the remedy shape of ADR 0166's Amendment 2026-10-11.
- The sitemap and the catalogue agree at every `AsOf`, because both come from one read.
- An erased ad is never served from memory. Content is classified per request, so an erased ad is absent from its page at once.
- A row cannot carry what it must not: the member set is pinned by a contract test, a wire test and a boundary test, and the component is typed to it.
- The cost of a page does not depend on its depth: an in-memory slice and at most 20 primary-key rows.
- The release is image only: no migration, no package, no compose change, no ACL change and no new recipient.
- The `(public-job)` group's fitness functions apply to the catalogue unchanged.
- A hub inherits this design whole and changes no S5 member's meaning (stage-A A2).

### Negative and accepted trade-offs (the CTO's list unless marked)
- **Within the 15-minute window** a page can show fewer than 20 rows, when an ad ended or was erased after `AsOf`, and the count can be off by those ads. An erased ad vanishes from its page
  at once; the short page carries the same bit that the next tick's drop would carry, only earlier (ADR 0166 W5; the amendment states it).
- **The deep chain is crawled rarely.** Accepted for the reasons in D2.
- **A 429, the release window and the other failures D8 lists answer 500, not 503,** and the 500 is not the group's error surface (brief correction 9). Google slows crawling on any 5xx; trigger 6.
- **The landing count and the catalogue count differ,** and both are true (D9). Measurement M2 records the gap: 0, on 2026-10-18 at 14:59 UTC.
- **A signed-in visitor sees "Logga in" in the public header,** as on every marketing page.
- **An SEO PR edits S4's snapshot,** as preparatory commits, and S4's two unit-test files change their arrange lines (D3).
- **Twenty rows per page means about 2 112 pages** (D2).
- **The landing loses its header stats at 833–980 px** (D11; triage).
- **P5 changes `/jobb`'s pager for runs above 999 pages** (D10; triage).
- **A second 404 file in the group** (D11; triage).
- **The snapshot's retained memory about doubles** at the ceiling (computed, not measured; architect).
- **Footer links have no touch floor at ≤768 px** (about 23 px against `DESIGN.md:121`, measured by design-reviewer's harness, 2026-10-18, `globals.css:5228-5229`), and the new link inherits it. Fixing
  only that link would fork the column's link rule, so the remedy is the list rule's: an issue, without `mvp` (Klas, 2026-10-06: accessibility is not MVP), filed by the S5 session after a dedup
  against the audit session's own filing. The header link's 44 px floor is in-block, because it is a Major on S5's own delta.
- **Residuals the amendment carries (security-auditor):** `data-nosnippet` keeps the text out of snippets and AI answers but not out of the index, so a list URL can match a name until the next
  recrawl; web-archive snapshots of list pages are out of reach; and at hub scale, ads erased on one request and published together can vanish from the same page together.
- **Klas may override** page size 20, the absence of a hero link, the footer order, 500 rather than 503, and canonical-only for unknown parameters, without a second round (CTO).
- **The legal weight of findability by name, of the join and of the erasure inference is not carried here.** It is in the ADR 0166 amendment, and until that is signed the merge waits (D13).

## Implementation

One PR on `feat/seo-s5-public-catalogue`, lane BE+FE. It adds no migration, no new package and no diff under `deploy/`, and no edit to `proxy.ts` or `next.config.ts`; any of those is a stop that
comes back to the CTO. No GO from Klas is owed, because the release is image only (ADR 0154 §4). This ADR is promoted with `git add -f` together with its README row. ADR 0064, 0106, 0160, 0165,
0166 and 0174 carry their amendments in the same PR; ADR 0005 is local and is annotated in the main copy. The commit order is the triage's. **This section states the plan; the PR's diff and CI
are the record.** The test results below are the driving session's of 2026-10-18 and were not re-run for this ADR.

**Preparatory commits, each proving it moves nothing:**
1. **P1, `refactor(jobads)`:** the snapshot also holds the catalogue order and count. `ListedJobAdRow`, the new `Create`, the stable sort and `Count`; V1's arrange lines; the new
   snapshot unit tests; the docblocks; the registry text.
2. **P2, `refactor(jobads)`:** `PublicPlaceNames`. The detail handler's tests stay unchanged.
3. **P3, `refactor(api)`:** `AddIpTokenBucketPolicy`. All rate-limit tests stay unchanged.
4. **P4, `refactor(web)`:** `JobAdPagination` gains `prefetch?: false`, tested in both polarities.

The triage's P5 (the pager's page numbers grouped) was withdrawn (D10); the slot is empty.

**Then the feature:**
6. **`feat(jobads)`: the catalogue query.** Limits, validator, union, DTO and handler with their unit tests; the registry key; the `TaxonomyAclLayerTests` row; NH1 and NH2; the DTO contract test; the
   M4 Testcontainers class with `ToQueryString` captured for this ADR.
7. **`feat(api)`: `GET /api/v1/public/job-ads/catalogue`.** The policy, the options and their default row; the factory rows, including the dispose cleanup; the wiring test, the budget row and the
   strict 429 test; the derived-host endpoint tests (wire, the empty body, the statuses, `no-store`, `Retry-After`, both 400 roads); the constant rename; the docblocks; and the stale comment at
   `JobAdsEndpoints.cs:25-27`, which becomes a one-line English pointer (this group stays gated; the anonymous catalogue is a separate surface, ADR 0176).
8. **`feat(web)`: the schema, the reader, the URL builder and the `sida` parser.** The fetcher's header key set is pinned, and `PublicJobAdCatalogueBoundaryTests` lives here.
9. **`feat(web)`: `/lediga-jobb`.** The page, `PublicJobAdList`, the segment's not-found; the lede, the count line and the empty state; the messages and the one label key; the group test and the
   layout docblock; the render tests (wrapper, metadata sentinel, plural, empty state).
10. **`feat(web)`: the footer entry.**
11. **`feat(web)`: the header link, the island and the `.jp-head` ladder** (`hotspot:css`).
12. **`feat(seo)`: the hub as the 15th URL of the pages sitemap.** The seo tests; the smoke 14 → 15; the smoke extension under both user agents (M5).

**Then the docs:** this ADR and the amendments (13–14); `DESIGN.md` §6, `BUILD.md`'s endpoint line, `docs/threat-model.md`, and the runbooks (15); the session docs per ADR 0065 (16).
- **The smoke script** covers: 200 with rows and a clean canonical; `?sida=2` with its canonical; `?sida=1` → 308; `0`, `abc`, `02` and a repeated `sida` → 404 with no API call; a stub 404 → 404;
  a stub 503, 429, 500 or timeout → a 5xx; `?utm_source=x` → 200 with the clean canonical; `no-store` on 200, 404 and 5xx; the stub sees the call at request time; and the document's byte
  size against the stub. That a row is a plain anchor and never `next/link` cannot be told apart in server HTML, so it is pinned by the page's render test and the group test instead.
- **Backend tests** (Testcontainers Postgres, never InMemory). The exact set, the order and the erase-after-sample case run on their own database with the real sampler (the shared `[Collection("Api")]`
  database holds other classes' listed ads, and its host has no snapshot); the endpoint runs on a derived host with no timer, which the test drives itself. They cover: a manual `Source =
  "Platsbanken"` row absent; an ad erased after the sample absent from its page at once while the snapshot still counts it; an ad whose deadline passes after the sample absent; 400, 404 and 503, and
  page 1 at 200 on an empty corpus; `no-store` on 200, 400, 404, 429 and 503; the exact wire keys; the rate-limit wiring and the forwarded budget (architect V4).
- **Issues to file during S5, after a `gh issue list` dedup** (both are genuine defects in delivered code, so the filing cap does not count them, CLAUDE.md §9.6): (1) the five `Contains`
  comments, labelled `area:jobads-cv`, `area:matching`, `P3`, `BE`, no `mvp`, filed after M4 is green and with its date; (2) the footer links' missing touch floor, labelled `area:frontend`,
  `hotspot:css`, `P3`, `FE`, no `mvp`. #2172 closes at merge, and the readings ride #2090's phase-1-close row.
- **Owed outside the diff:** the third DPIA addendum, the HANDOVER-v3 note and the processing-register sentence and correction, all local files in the main copy; Klas's approval of the
  addendum's text; Klas's Search Console readings; and the measurements below.

## Measurements

Each reading below is recorded here before the merge, with its date and its source, or is recorded as "not measured". Nothing is estimated, and no number is entered that was not read.

| Reading | What | Recorded |
|---|---|---|
| M1 | the merge-day S2-P3 reading, counts only (also in the ADR 0166 amendment, D11, and the publication-count runbook) | Not yet taken. |
| M2 | the `IsListed`-equivalent count and the `status = 'Active'` count at one minute, and the gap | 2026-10-18 at 14:59:32 UTC, on the box, counts only (CLAUDE.md §9.2 (a)): 42 283 listed and 42 283 `Active`. The gap is 0. |
| M3 | the catalogue path's `grep -c` added to ADR 0174 D5's readings 1 and 2 | Not yet taken. |
| M4 | the content read's SQL; `EXPLAIN (ANALYZE, BUFFERS)` of the content read and of the widened sampler read (before and after); whether the composed `Contains` translates | 2026-10-18, Testcontainers (Postgres 18), `PublicJobAdCatalogueReadTests` 4/0. **It translates**, as one array parameter and never an IN list: the command EF sends is `SELECT j.id, j.title, j.company_name, j.municipality_concept_id, j.region_concept_id, j.published_at FROM job_ads AS j WHERE j.id = ANY (@typedIds) AND j.external_id IS NOT NULL AND j.external_source IS NOT NULL AND j.external_source = 'Platsbanken' AND j.status = 'Active' AND (j.expires_at IS NULL OR j.expires_at >= @now)` (captured by a command interceptor; the test pins `= ANY (`, the predicate's columns and no `raw_payload` or `description`). `EXPLAIN (ANALYZE, BUFFERS)`, 2026-10-18 at 15:44 UTC, 45 000 listed rows seeded through `JobAd.Import`, then `ANALYZE`. **The content read** is a bitmap index scan on `pk_job_ads` with `id = ANY(…)` as the index condition, about 0.1 ms, the same on page 1 and page 2 000. **The sampler read** keeps its plan (sequential scan, sort on `id`, limit); only its target list widens. Its sort grows from 1 537 kB to 4 701 kB. That is an in-memory quicksort under the box's `work_mem` of 8 MB (`deploy/docker-compose.yml`) and an external merge to disk under the container default of 4 MB. Computed, not measured: near the 80 000 ceiling it would pass 8 MB and spill once per tick. The readings and the class that regenerates them are in `docs/research/issues/2172-m4-explain.md`. |
| M5 | the statuses and the canonical's place in `<head>` under a browser and a Googlebot user agent | 2026-10-18, the smoke against the production build (Next 16.3.8, standalone), 43/43: on `/lediga-jobb?sida=2` the `<title>`, `<meta name="description">` and `<link rel="canonical">` sit before `</head>` for both a Chrome user agent and `Googlebot/2.1`. No STOPP. |
| M6 | design-reviewer's rendered matrix, including the 500's framing | Not yet taken. |
| M7 | the document size, and a local Lighthouse run against the stub-backed production build | 2026-10-18, a stub page of 20 rows from the standalone build: 107 061 bytes uncompressed with Next's RSC payload (the smoke), and 12 427 bytes gzip-compressed as served (`curl` with `Accept-Encoding: gzip`, 15:39 UTC), against ADR 0045's document budget of 30 720 bytes. **Lighthouse 12.6.1** (the repository's pinned `@lhci/cli`), 2026-10-18 at 15:54–15:57 UTC, three runs each of page 1 and `?sida=2` against the same build and stub, with the default mobile emulation and simulated throttling: performance 0.94, accessibility 1.0, best practices 1.0, SEO 1.0, CLS 0, TBT 25–37 ms, document 13 846–13 950 bytes transferred, total 329–331 kB, no third-party request. LCP was 3 114–3 135 ms, and the LCP element is the first row's title link. In the same runs the landing measured 3 297–3 302 ms and `/matchning` 3 265–3 302 ms, so the catalogue is not slower than the pages already delivered. A local Windows lab run is not comparable to the CI runner's (ADR 0045's 2 500 ms is that job's observe-only threshold). The one accessibility audit flagged, at weight 0, is the footer brand link's `label-content-name-mismatch`, which predates this record. |

## References

- Code, as the plan reads saw it at `8ce73fa36`: `src/Jobbliggaren.Application/JobAds/` (`Publication/`, `Listing/`, `Queries/GetPublicJobAd/`, `Queries/GetPublicJobAdCatalogue/`) ·
  `src/Jobbliggaren.Api/Endpoints/PublicJobAdsEndpoints.cs` and `JobAdsEndpoints.cs` · `src/Jobbliggaren.Api/RateLimiting/` · `src/Jobbliggaren.Api/Program.cs` (the `no-store` prefix block) ·
  `web/jobbliggaren-web/src/app/(public-job)/` · `web/jobbliggaren-web/src/components/site/` (`site-header.tsx`, `site-footer.tsx`) ·
  `web/jobbliggaren-web/src/components/job-ads/` (`job-ad-pagination.tsx`, `public-job-ad.tsx`) · `web/jobbliggaren-web/src/lib/dto/_helpers.ts` · `web/jobbliggaren-web/scripts/public-job-smoke.mjs`
- Tests: as listed under *Implementation*; the boundary tests are `tests/Jobbliggaren.Architecture.Tests/ListedJobAdSnapshotBoundaryTests.cs` and the new `PublicJobAdCatalogueBoundaryTests`.
- `docs/reviews/2026-10-18-2090-s5-plan-cto.md` · `docs/reviews/2026-10-18-seo-audit-stage-a-cto.md` · `docs/reviews/2026-10-18-2090-s5-plan-triage-cto.md` ·
  `docs/reviews/2026-10-18-2090-s5-plan-security-auditor.md` · `docs/reviews/2026-10-18-2090-s5-plan-dotnet-architect.md` · `docs/reviews/2026-10-18-2090-s5-plan-design-reviewer.md` (all
  transcribed verbatim by the driving session; local) · `docs/runbooks/recruiter-pii-erasure.md` §2.4a · `docs/runbooks/public-job-page-publication-count.md` ·
  `docs/jobbpilot-v3-bundle/HANDOVER-v3.md` §6.4 (local)
- ADR 0005 (L84–86, L106; local) · ADR 0045 · ADR 0050 (Amendment 2026-08-04) · ADR 0064 (rule (d); the amendments of 2026-10-10 and 2026-10-18) · ADR 0106 (D9) · ADR 0113 · ADR 0120 (clauses 1
  and 4, and #1149) · ADR 0154 (§3, §4) · ADR 0160 (D1–D6) · ADR 0165 (D1–D4, D6) · ADR 0166 (D1–D11, W1–W5, and the amendments of 2026-10-11, 2026-10-17 and 2026-10-18) · ADR 0171 ·
  ADR 0174 (D1, D3–D5, D9)
- AGENTS.md §2.1, §2.5, §3, §4, §5, §8, §10 · CLAUDE.md §6.5, §9.2, §9.6 · `DESIGN.md` §3, §5, §6, §8 (`DESIGN.md:121`, `:189`)
- Martin, *Clean Architecture* (2017), ch. 7, 8, 13 and 22 · Fowler, *Refactoring*, 2nd ed. (2018), ch. 2 and 3 · Fowler, "Yagni", martinfowler.com bliki (2015) · Hunt/Thomas, *The Pragmatic
  Programmer* (1999), DRY · Nygard, *Release It!*, 2nd ed. (2018), ch. 5 · Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017), ch. 2 · Saltzer & Schroeder (1975), least common
  mechanism · Winters/Manshreck/Wright, *Software Engineering at Google* (2020), ch. 9
- Google Search Central, read 2026-10-18 (the date on each page is its own last update): *Pagination and incremental page loading* (2025-12-10) · *Robots meta tag* (2026-03-24) · *AI features
  and your website* (2025-12-10) · the faceted-navigation and sitelinks pages, as the CTO's brief read them. These pages change; the dates say what was read.
- Bing Webmaster Blog, *Bing Introduces Support for the data-nosnippet HTML Attribute* (2025-10-15, read 2026-10-18) ·
  [Search Engine Roundtable: Google, long-term noindex will lead to nofollow on links](https://www.seroundtable.com/google-long-term-noindex-follow-to-nofollow-24990.html) (Mueller,
  December 2017)
- Next 16.3.8: `docs/01-app/03-api-reference/04-functions/generate-metadata.md:46-48, 1240-1248` · `03-file-conventions/not-found.md` L8, L13, L43 · `error.md` L96
- #2090 · #2172 · #1477 · #1738 · #390 · #256 · #1149 · #2133
