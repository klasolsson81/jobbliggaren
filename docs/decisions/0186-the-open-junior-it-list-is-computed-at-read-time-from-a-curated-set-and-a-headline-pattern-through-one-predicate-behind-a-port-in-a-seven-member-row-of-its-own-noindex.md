# ADR 0186 — The open junior IT list at `/junior` is computed at read time from a curated set and a headline pattern, through one predicate behind a port, in a seven-member row of its own, `noindex`, on a bucket of its own

**Date:** 2026-11-19
**Status:** Accepted 2026-11-19 (Klas's three decisions of the same day, below, on the approved plan, and the plan reads of `dotnet-architect` and `security-auditor`). The ADR 0166 amendment of the
same date, which carries the legal outcome (D8), is Accepted by Klas and signed by `security-auditor` (2026-11-20).
**D1–D4 are superseded and D9 is decided by ADR 0187, 2026-11-20; D5–D8 stand.**
**Deciders:** Klas Olsson (controller and product owner). At the plan stage, on 2026-11-19, by AskUserQuestion, he answered, verbatim: "Ny öppen sida /junior (Rek.)"; "noindex (Rek.)"; "Två PR,
/junior först (Rek.)". His answers to `security-auditor`'s three questions are quoted in the ADR 0166 amendment (D8) and are not repeated here. ·
`dotnet-architect` (plan read, report-only, `docs/reviews/2026-11-19-2260-plan-dotnet-architect.md`: Viktigt 1–5 and Nice-to-have 1–5, marked "(architect V1)" and so on below) ·
`security-auditor` (plan read, report-only, `docs/reviews/2026-11-19-2260-plan-security-auditor.md`: her M1–M4, m1–m5 and S1–S16, marked "(security-auditor m1)" and so on below)
**Amends:** ADR 0176 (D5 and F4, **for `/junior` only**: the row carries the deadline and the page can be ordered by it, D6) and ADR 0106 (the D9 read-path table, by its dated amendment of the same
date). ADR 0166 gets the dated amendment that carries the legal outcome (D8). Nothing above those amendments is rewritten.
**Related:** [#2260](https://github.com/klasolsson81/jobbliggaren/issues/2260) (PR 1, the data and the open page; P1, BE+FE; not `mvp`: a campaign page is none of the core features, CLAUDE.md §6.5) ·
[#2261](https://github.com/klasolsson81/jobbliggaren/issues/2261) (PR 2, the junior filter on `/jobb`, its own session after #2241) · ADR 0160 (the publication predicate and the anonymous read) ·
ADR 0176 (the catalogue and the six-member row) · ADR 0182 (the related section: the closest precedent for an anonymous read on its own bucket) · ADR 0113 (every `job_ads` read site carries a
written lifecycle decision) · ADR 0043 (the taxonomy port) · ADR 0071 Decision 1 (no AI/LLM in the product) · ADR 0045 (budgets) · ADR 0154 (every merge is a deploy)
**Measured against:** `feat/junior-it-list` in `C:/DOTNET-UTB/JobbPilot/.claude/worktrees/jobbliggaren-junior-list-4d2889`, cut from `354e4a926` (`origin/main`), read on
2026-11-19.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. D1–D7 follow the approved plan
> (`C:/Users/zebac/.claude/plans/pasted-content-id-cd52-bygg-en-distributed-leaf.md`, local) as the two plan reads changed it. D8 only points. D9 records a choice that is Klas's and is open.
> Facts about the code were read on 2026-11-19. The numbers under *Measurements* are the driving session's of that date, given in the brief; none was re-run for this ADR.
> What no source measures is listed under *What is not measured*. The mandatory agents review the delivered diff in the PR. The ADR lies in the ignored 0071–0199 range and needs `git add -f`
> and a README row (precedent 0167–0185).
>
> **Numbering.** 0186 was measured free on 2026-11-19, before this file was written: in this worktree, in the main copy, in every other worktree and on `origin/main`.

---

## Context

Klas is preparing a LinkedIn post from Jobbliggaren's company page. Its first comment links to a list of the junior IT jobs open right now. That list must open without an account, narrow by place,
sort, and be live: closed, removed and expired ads must drop out on their own. The post goes out only once the list is deployed. Eight facts shape the decision.

- **No open job page has a place and a sort.** `/jobb` is login-only at four layers: `proxy.ts` `PROTECTED_PREFIXES`, the `(app)` layout, the page, and `GET /api/v1/job-ads` with
  `RequireAuthorization`. `/lediga-jobb` is open but reads only `sida`; a place, filter or sort parameter there is what ADR 0166 D10's catalogue trigger (trigger 12) names, and ADR 0176's S5
  phasing locks the catalogue to "no kommun or län".
- **"Junior" is not a stored attribute.** `experience_required` is not stored. The Platsbanken (JobTech) id is stored as `ExternalReference.ExternalId` under the unique index
  `ix_job_ads_external_source_external_id`, so a set of ids from outside maps straight onto `job_ads`. The Data/IT occupation field (`apaJ_2ja_LuF`) has no column of its own: it is reached through
  the taxonomy tree (`ITaxonomyReadModel.GetTreeAsync`, `OccupationFields[].OccupationGroups[]`), and `OccupationGroupConceptId` is indexed.
- **Liveness is already in place.** `JobAdPublication.IsListed(now)` requires Platsbanken as the source, status `Active`, and `ExpiresAt` null or not yet passed. An ad leaves it by four paths: the
  stream's `removed` (`ArchiveExternalJobAdCommand`), three snapshot misses (`RetainPlatsbankenJobAdsJob`), `ExpireJobAdsJob`, and an Art. 17 `Erase()`.
- **The reading.** On 2026-11-19, 249 unique Data/IT candidates from JobTech were read in full (Klas's reading). 135 unique ads carry a list label (junior, trainee, no experience or junior-and-senior); counting the
  9 Platsbanken duplicates that point at them, that is **144 Platsbanken ids**. The reading's own headline rule (`HEAD_JUNIOR`) is right in 69 of 69 cases and finds only 51 % of the junior ads.
- **A model step is a stop.** ADR 0071 Decision 1 is "no AI/LLM of any kind in the product" (AGENTS.md §5). A model in ingestion needs a new ADR that supersedes it (D9).
- **The provider translates the regex.** `Regex.IsMatch` in an EF expression becomes `~*` in the Npgsql provider, which Application may not reference (AGENTS.md §2.1, axis 3). The precedent is
  `EF.Functions.ILike` in `EmployerDisambiguationQuery`. `\b` is not the same assertion in the two dialects: PostgreSQL's ARE reads it as a backspace and spells a word boundary `\y`.
- **A row has six members, by decision.** ADR 0176 D5 pins `PublicJobAdSummaryDto` to six members and lists `ExpiresAt` under "never in a row"; `PublicJobAdList` can render nothing else. The page
  Klas wants sorts by the deadline, and the deadline is already public on the ad's own page (ADR 0160 D4).
- **The legal gate is not this ADR's.** A new anonymous read and a new public page fire ADR 0166 D10's head clause, "any widening of the public surface". Its outcome has one home, the ADR 0166
  amendment of 2026-11-19 (D8).

## Decision

**`/junior` lists the open junior IT jobs for anyone, signed in or not: the ads listed now (`JobAdPublication.IsListed(now)`) in the Data/IT occupation field that are either on a curated list of
Platsbanken ids or have a title that matches a headline pattern. Membership is computed on every request from the app's own ad data. There is no column, no migration, no change to ingestion and no
backfill, so an ad that closes, is removed, expires or is erased leaves the list, its count and its place options on the next request, with nothing done by hand. One predicate,
`JuniorItPredicate`, lives in Infrastructure because its regex is translated by the database provider. It is reached through one resolver, `JuniorItSetResolver`, behind one Application port,
`IJuniorItJobAdQuery`, which the `/jobb` filter (#2261) is to reuse. The curated ids and the pattern live in one versioned embedded file, `junior-it-list.v1.json`, and nowhere else. The read is a
direct per-request read, never the listed-ad snapshot, and answers a seven-member row of its own, with the deadline. The page is `noindex, follow`, in no sitemap, a plain GET form with
`municipality`, `sortBy` and `page`, and the endpoint has a rate-limit bucket of its own, `public-job-ad-junior-it`. The legal outcome has one home, the ADR 0166 amendment of the same date.**

**Scope.** This is PR 1 of two (Klas: "Två PR, /junior först (Rek.)"): the data and the open page, after which the post may go out. The junior filter on `/jobb` is PR 2 (#2261), in its own
session. How new ads beyond the headline rule get labelled is open and is Klas's choice (D9); PR 1 delivers the headline rule alone.

### D1 — Membership is computed at read time: `IsListed(now)`, Data/IT, and curated or headline (plan; security-auditor m1)

An ad is on the list when all three hold:

`JobAdPublication.IsListed(now)` ∧ `OccupationGroupConceptId` ∈ the Data/IT field's groups ∧ (`(External.Source, External.ExternalId)` ∈ the curated set ∨ the title matches the headline pattern)

- **Data/IT is on both arms (security-auditor m1).** The plan first wrote `IsListed ∧ (curated ∨ (Data/IT ∧ headline))`. A curated id that sits outside the field is therefore never listed. Why that
  matters is the amendment's (D8), not this ADR's.
- **The curated arm is keyed on source and id (architect V1).** `External != null`, `External.Source == JobSource.Platsbanken` and `curated.Contains(External.ExternalId)`: an id from another source
  never matches.
- **The predicate is a list of arms joined with `Aggregate(OrElse)` (architect Nice-to-have 5),** so a reviewed-label arm (D9, option B) would land in one place. `JobAdSearchComposition.OrElse`
  goes from `private` to `internal` for it. Domain is untouched.
- **Liveness is `IsListed`, composed as the outer conjunct of both reads** (`.Where(JobAdPublication.IsListed(now))`, then `.Where(JuniorItPredicate.Matches(set))`). `JuniorItPredicate` says
  nothing about an ad's lifecycle, and its docblock says so: every caller composes it with its own gate. The four closing paths in *Context* all end in `IsListed`, so the curated file needs no edit
  when an ad ends, and is never given one for it.
- **No column.** A column filled at ingestion would be overwritten by `UpdateFromSource`, would need a migration (the single-owner hotspot, CLAUDE.md §6.5) and a backfill, and would have to be
  filled again for every change of the pattern. Reading at request time makes the list live by construction (Alternatives).

### D2 — One predicate in Infrastructure, one resolver, one port (architect V1, V2; AGENTS.md §2.1; ADR 0039 Beslut 1, ADR 0167, ADR 0043 Beslut E)

The plan put the Data/IT group resolution inside the port's implementation, where PR 2's `ApplyFilter` (internal, static, no DI) could not reach it. It is now one chain that PR 1's read and PR 2's
filter both call, so the definition of "junior IT" exists once (the single point of truth ADR 0039 and ADR 0167 apply to the search path).

- **`JuniorItPredicate.Matches(JuniorItSet)`** (`Infrastructure/JobAds/JuniorIt/`) builds the expression. `Regex.IsMatch` is provider-translated to `~*`, which is why it is here and not in
  Application. The pattern reaches the lambda as a captured parameter and is never concatenated; `RegexOptions.IgnoreCase` is a literal in the lambda, because a captured option does not translate.
  Npgsql 10.0.3 accepts the parameterised pattern (architect V1, read in `NpgsqlRegexTranslator.cs`); a Testcontainers test is the oracle for what is sent.
- **`JuniorItSetResolver`** is a singleton built from the loaded file and the taxonomy port. `ResolveAsync` returns a `JuniorItSet`: the curated ids, the Data/IT groups and the pattern. It reads the
  tree through `ITaxonomyReadModel.GetTreeAsync`, finds the file's `occupationFieldConceptId` and takes that field's groups. A field the taxonomy no longer knows, or one without groups, **throws**,
  because it would turn the headline arm off without a word. This is the field cross-check the architect asked of the loader (Nice-to-have 2); it runs at read and not at load, because the
  loader has no taxonomy. **#2261 is to reuse this resolver and this predicate, not copy them.**
- **`IJuniorItJobAdQuery`** (`Application/JobAds/Abstractions/`) is the port, and it returns raw rows with concept ids. Naming and building the DTO stay in Application. It has two members, both
  read from the same `now`:
  - `CountByMunicipalityAsync(now)`: a `GROUP BY` kommun that gives the options and the total. Ads without a kommun are one row with a null id, so the rows sum to the list's total.
  - `ReadPageAsync(now, municipalityConceptId, sortBy, skip, take)`: one page, ordered by `JobAdSearchComposition.ApplySort` with the id as the last key.
- **`JuniorItJobAdQuery`** implements it over `AppDbContext`. It reads a narrow projection and never materialises the entity, because `RawPayload` carries the pre-scrub text.
- **The handler stays in Application** and keeps `ITaxonomyReadModel` in its constructor to name kommun and län (`PublicTaxonomyNames`), so `TaxonomyAclLayerTests` lists it as the fourth
  anonymous consumer of the port.
- **Use the `JuniorIt` name throughout** (architect Nice-to-have 4).

### D3 — The versioned file is the only home of the ids and the pattern (plan; architect Nice-to-have 2; security-auditor m5)

`src/Jobbliggaren.Infrastructure/JobAds/JuniorIt/junior-it-list.v1.json` is an `<EmbeddedResource>` with a logical name, loaded once into a singleton at host build. This follows
`brand-groups.v1.json` and AGENTS.md §5's rule that rubric data is versioned data and not inline strings.

- **Six keys, and nothing else from the reading:** `//` (maintainer documentation, not mapped), `juniorItListVersion`, `readAt`, `occupationFieldConceptId`, `headlinePattern` and
  `curatedExternalIds`, the 143 ids. No label, year, place, note, title, employer or our own id. The ids were generated from Klas's reading by a one-off command outside the repository.
- **The loader fails loud at host build** (the list is instance-registered): no version; a `readAt` that is not `yyyy-MM-dd`; no field id; no pattern; a pattern that does not compile (it is
  compiled with a one-second timeout); an id that is not decimal digits (since anything else could never equal a Platsbanken id and would shrink the list unseen); or an id named
  twice. A test (written in this PR) pins the file's six keys, so a seventh cannot ride in unnoticed, together with the id form, the absence of duplicates and the count of 143. The loader itself
  ignores an unmapped key; the pin is the test's.
- **The pattern runs in PostgreSQL at request time and in .NET only in the loader's compile check and the agreement test** (D4). Titles are third-party input, so no request ever runs a .NET regex
  over one (security-auditor m5).
- **A new reading replaces the set as a new version of the file.** No id is added or removed for one ad. What binds that rule, and why, is in the ADR 0166 amendment (D8).

### D4 — The pattern: the database locale never decides a match (architect V3, Nice-to-have 3)

The pattern is a port of the reading's `HEAD_JUNIOR`. The change is that `\b` is replaced by an explicit boundary class, a `[^…]` of the ASCII letters and digits and both cases of å, ä, ö, é and ü.

- **`~*` folds case by the database's ctype.** The ctype on the box is not measured, and under C/POSIX the fold is ASCII only (architect V3, docker-library/postgres 18 Dockerfile, read
  2026-11-19). So every non-ASCII letter is written out in both cases, in the boundary class and in the term `f[öÖ]rsta jobb`, and the locale never decides a match. The agreement test's fixture
  is to hold upper-case å, ä and ö (architect V3).
- **The two engines are pinned against each other** by `JuniorItHeadlinePatternAgreementTests` (Testcontainers; named in `JuniorItPredicate`'s docblock): the same synthetic headlines, covering every
  term and its boundary (`intern` but not `internal`), through `Regex` in .NET and through the provider's `~*`.
- **Fidelity to the original is measured, dated and historical** (R2): on 2026-11-19 the ported pattern and `HEAD_JUNIOR` agreed on all 249 reading ads (69 hits each, all 69 carrying a list label) and
  on 2 100 live Data/IT headlines (56 hits each, 0 disagreements).
- **The pattern's one home is the file.** It is not copied into this ADR, the code or a test; a change to it is a change to the file, as a new version of it.

### D5 — The read: direct, per request, from one `now`; `GET /api/v1/public/job-ads/junior` (architect V2; security-auditor 1(c))

- **The endpoint.** `GET /api/v1/public/job-ads/junior?municipality=&sortBy=&page=` in the anonymous `PublicJobAdsEndpoints` group (ADR 0160 D1), anonymous by omitting `RequireAuthorization`. The
  query, `GetPublicJuniorItJobAdsQuery(string? Municipality, JobAdSortBy SortBy, int Page)`, is neither `IAuthenticatedRequest` nor `ICapturesRecentSearch`: an anonymous read never records a search.
- **Not the snapshot.** The list reads `job_ads` itself through `IsListed(now)` and does not use the listed-ad snapshot (ADR 0174, ADR 0176 D3), which holds ids and the publication date and no title
  or occupation group (security-auditor 1(c)). So there is no `unavailable` arm and no 503: the answer is a page or a page past the end.
- **One `now` for both reads.** The handler takes `clock.UtcNow` once and passes it to `CountByMunicipalityAsync` and `ReadPageAsync`. The total is the count's sum, for the chosen kommun or for all
  of them. A page past the last page (`lastPage = max(1, ceil(total / 20))`) answers `NoSuchPage`, which the endpoint maps to **404**, as the catalogue does. A kommun with no listed junior ad
  answers 200 with an empty page and the options. The registry (ADR 0113) holds two `Active` entries, `JuniorItJobAdQuery.CountByMunicipalityAsync` and `.ReadPageAsync`. (Three since the Amendment 2026-11-22, #2293: `.MeetsFloorAsync` is the third, and the handler passes it the same `now`.)
- **The options.** `places` is every kommun that has a listed junior IT ad, counted over the **whole** list whatever kommun was chosen, so the visitor can move between them. A kommun without a name in the taxonomy cannot be offered by name, so it is left out of the options and still counts in the total. Ads without a kommun count in the unfiltered
  total and appear in no option. Counts and rows come from the same composed base, so they cannot disagree about membership. No option and no count is keyed by employer.
- **The parameters.** `municipality` is a JobTech concept id (at most 32 characters); `sortBy` is `PublishedAtDesc` (the default) or `ExpiresAtAsc`; `page` is 1 to 100
  (`MaxPage`, which bounds the URL space and not the list). The validator refuses anything else with the pipeline's 400. The page size is a constant, 20. `JobAdSortBy` is reused and restricted in the
  validator to those two (architect Nice-to-have 1), and `ApplySort` puts a null deadline last under `ExpiresAtAsc`.
- **`no-store`** comes from the `/api/v1/public` prefix block in `Program.cs`, which covers every answer under the prefix (security-auditor S9; the tests read the 200, 400, 404 and 429).
- **Nothing logs a value.** The handler logs nothing; no log line carries a title, an employer or an id.

### D6 — The wire: a seven-member row of its own, which amends ADR 0176 D5 and F4 for `/junior` only (architect V4; security-auditor m2)

- **The row.** `PublicJuniorItJobAdDto(Guid Id, string Title, string CompanyName, string? MunicipalityName, string? RegionName, DateTimeOffset PublishedAt, DateTimeOffset? ExpiresAt)`: the
  catalogue's six and the deadline. On the wire it is `{id, title, companyName, municipalityName, regionName, publishedAt, expiresAt}` with a lower-case GUID `id`.
- **The envelope.** `PublicJuniorItJobAdsPage.Available(Rows, Places)`, a closed union with `NoSuchPage`: on the wire `{ rows: PagedResult<row>, places: [{municipalityConceptId, name, count}] }`.
  `PagedResultContractTests` gains a declared pin for the page arm, the same form as the catalogue's: a union whose page arm carries `PagedResult<T>`.
- **`PublicJobAdSummaryDto` and `PublicJobAdList` are unchanged.** A seventh member in either would re-open the catalogue's and the related section's row decisions (ADR 0176 D5, ADR 0182 D3). The junior
  row has a component of its own, `PublicJuniorItJobAdList`, typed to the seven members, so it can render nothing else (security-auditor m2: prefer `PublicJobAdList` byte-unchanged).
- **What amends ADR 0176, for `/junior` only.**
  - **D5's "never in a row" loses `ExpiresAt` for this row.** The deadline is in the row because the page sorts on it and the ad's own page already shows it. Everything else D5 lists as never in a
    row stays out of this row too: description or excerpt, `Url` or any Arbetsförmedlingen id, `CreatedAt`, the organization number, contacts, `RawPayload`, concept ids, employment type, occupation.
  - **F4's rejection of a deadline order does not bind `/junior`.** On the catalogue the order stays newest first. Here the visitor picks it, newest first stays the default, and a null deadline
    sorts last.
- **Pinned three ways,** as the catalogue's row is: `PublicJuniorItJobAdDtoContractTests` pins the member set, a wire test pins the keys, and `PublicJuniorItJobAdsBoundaryTests` (Architecture)
  holds the web's strict row, option and envelope schemas equal to the DTO, the option and the union's page arm, and the web's strict page to `PagedResult<T>`'s five serialised members, so that a
  member added on one side does not turn every `/junior` view into a 500 that only production would show.

### D7 — The budget: a policy of its own (architect V5)

`public-job-ad-junior-it` (`RateLimitingExtensions.PublicJobAdJuniorItPolicy`; `RateLimitingOptions.PublicJobAdJuniorIt`) is a TokenBucket per relayed client address, 60 per 60 s, through
`AddIpTokenBucketPolicy`. It is alone on its route and never shared, so a visitor paging through the list cannot drain the job pages' budget, or the reverse. 60 per 60 s is the catalogue's number
(`security-auditor` approved the number in her plan read). The options have a code default and no `ValidateOnStart`, so there is no `appsettings.Local.json.example` entry (CLAUDE.md §11).
Tests: a wiring test (anonymous, its own policy, alone on its route), a strict 429 test with `Retry-After` and `no-store`, a `ForwardedClientBudgetTests` row, and the options default. The existing
policies' tests are to stay byte-unchanged (security-auditor S10).

### D8 — The page, and where the legal outcome lives

**The page.** `web/jobbliggaren-web/src/app/(public-job)/junior/page.tsx`, a Server Component in the public group, outside `PROTECTED_PREFIXES`.
- **It reads three parameters and nothing else.** `municipality`, `sortBy` and `page`, named and valued as on `/jobb`, so PR 2's hand-over is a pure passthrough. A repeated key, an id with other
  characters, `page=0` or an unknown order is a 404 decided in `parseJuniorItParams` without asking the API; every other query key is ignored. It reads no cookie and no session: a signed-in and an
  anonymous visitor get the same answer. (Superseded for `/junior` by the Amendment 2026-11-21, #2284.)
- **`noindex, follow` on every 200,** `robots: { index: false, follow: true }`; in no sitemap; and `robots.ts` is unchanged, because a disallow would hide the `noindex` from a crawler. The title,
  description and link preview are fixed text from `messages/{sv,en}/publicjob.json`, never a row or a count. There is no JSON-LD. (Superseded for the default view by the Amendment 2026-11-22, #2293: it is `index, follow` while the floor holds and is the pages part's 16th URL, and every other answer stays `noindex, follow`. The legal outcome is ADR 0166's Amendment 2026-11-22.)
- **The form.** A plain GET `<form>`, so it works without JavaScript: `<select name="municipality">` ("Alla orter" and each kommun with its count), `<select name="sortBy">` (newest first, or the
  last day to apply, in `/jobb`'s own copy) and a "Visa" button. Changing either starts again from page 1. A count line, the rows and the pager sit in one `data-nosnippet` block, as the catalogue's
  do. The pager is the catalogue's model, with no prefetch.
- **Rows** are plain `<a href="/jobb/<id>">`, never `next/link`: a prefetched row would spend the visitor's job-page budget before a click. A full document load is same-site, so a signed-in
  visitor's session cookie goes along and the private page, with Spara and Har ansökt, opens.
- **The reader** `lib/api/public-junior-it-job-ads.ts` follows `public-job-ad-catalogue.ts`: `forwardedHeaders()` only, `cache: "no-store"`, `redirect: "manual"`, a 5-second timeout, a strict Zod
  schema in `lib/dto/`, and `cache()` so that `generateMetadata` and the page make one read. A 404 or 400 is the page's 404; every other outcome throws and is a 5xx, so an outage never reads as a
  page that does not exist. It is in the `(public-job)` group's closed list of readers and in `PUBLIC_SURFACE`, where the group test's no-ISR and no-console rules read it.
- **Copy** says nothing that grades the matching or implies it is wrong, carries no label or year from the reading, and keeps the civic tone (AGENTS.md §10).
- **States to render before the design verdict** (AGENTS.md §8, point 4): the list, a chosen kommun, an empty kommun, a 500, a page past the end, and narrow and wide widths.

**The legal outcome has one home, and the merge waits for it.** ADR 0166, "Amendment 2026-11-19 (#2260, ADR 0186)", is the single home of the legal outcome for the junior list: the re-take of the
D10 conclusion, the weights, the DPIA #456 §8-B addendum, the triggers that guard it, Klas's three answers and `security-auditor`'s signature. **This ADR points there and restates none of it** (one
decision, one home; AGENTS.md §8, point 9). PR 1 may not merge, and so may not go live (ADR 0154), until she has signed it at her scoped re-check of the final head; the curated file's first push waits
for the counts-only reading the amendment names. A change to anything that amendment's triggers name is a re-take there, not an edit here.

### D9 — How new ads beyond the headline rule get labelled is open, and is Klas's choice (scheduling, not decided, not built in PR 1)

PR 1 delivers option A alone. Measured on 2026-11-19, 67 new unique candidates were published in the last seven days and the headline rule does not catch 49 of them, about 50 a week to read.
Until Klas chooses, a junior ad whose title does not match the pattern is on the list only if it was in his reading of 2026-11-19. The options, as the plan set them out and the plan reads graded them:

| Option | Cost | Note |
|---|---|---|
| **A.** The headline rule alone (delivered) | 0 kr, 0 time | Precise (69 of 69) and finds about half (51 %) |
| **B.** A review queue in `/admin` | Klas's time, about 50 ads a week at about a minute each, 45–50 minutes a week. Build: two PRs (a table with a migration; an admin page; `experience_required` fetched as a set, as Remote is) | Deterministic, no model, no new processor; fits ADR 0071. The plan and `security-auditor` both recommend B. A reviewed-label arm joins the predicate's arm list (D1) |
| **C.** A model in ingestion | The plan priced it at about $1 (Haiku 5.5) to $20 (Sonnet 5.5) a year | **Not an option to price, a stop:** any LLM/AI inference call in the product is an AGENTS.md §5 / ADR 0071 Decision 1 breach, and needs an ADR that supersedes it (`security-auditor`). No DPA, Chapter V ground or register row makes it compliant here |
| **D.** A recurring agent run outside the product | One session and one PR a week; up to a week's delay; the list changes by a new version of the file | The run reads raw ad text, which holds recruiters' names, into an agent's transcript: CLAUDE.md §9.2 (c), with (f)'s preconditions for a model provider. Klas's decision (`security-auditor`) |

This ADR decides none of them. Whichever Klas takes is its own ADR, and B or D go through `security-auditor` first. That the ids in the file may only change by a new reading is D3's.

## What is not measured, and the measurements owed

**Not measured, and not claimed:**
- **The cost of the statements on the box's table.** Each request runs a regex over the listed ads of the Data/IT field's groups twice, once for the count and once for the page. The group index
  narrows the set and the bucket (60 per 60 s per address) bounds the rate, but no `EXPLAIN` or timing is taken for this ADR; ADR 0182's R2 is the form to follow if one is wanted. ADR 0045's hot-path
  budget is observe-only. (Measured since: ADR 0190, *Measurements*, 2026-11-22, with the read placed in ADR 0064's third class by its Amendment 2026-11-22; the box's own plan is still not measured. See the Amendment 2026-11-22 below.)
- **The database's ctype on the box** (D4). The pattern is written so that it does not matter; what the ctype is has not been read.
- **What Npgsql sends for the pattern,** beyond architect V1's reading of the translator at 10.0.3. The agreement test's Testcontainers run is the oracle, and it is re-run when the provider is
  upgraded.
- **The consequences of two statements, not one snapshot.** The count and the page are read at the same `now` in two statements. An ad that closes between them can leave a page one row short of its
  count; the next read is right. This is accepted.

**Owed by this ADR's own tests, in the same PR** (named here ahead of the panel; the PR holds them): the loader's test (D3); `JuniorItHeadlinePatternAgreementTests` (D4);
`PublicJuniorItJobAdDtoContractTests` and the wire test (D6); the validator's test; the rate-limit rows (D7); and `JuniorItJobAdQueryReadTests` (Testcontainers), whose
premises come from production paths (AGENTS.md §5, Tests): ads are seeded through `JobAd.Import`; a curated ad, a Data/IT ad with a junior headline and a non-Data/IT ad with the same headline are
each in or out as D1 says, and a non-Data/IT curated ad is out; an ad removed through `ArchiveExternalJobAdCommand`, an ad whose `ExpiresAt` the clock passes, an ad run through `ExpireJobAdsJob` and an
ad erased by `Erase()` are each absent from the rows, the total and the options at once; a kommun with no listed junior ad is no option; the kommun filter, both orders and a page past the end.

## Alternatives considered

### Alt A — The chosen design (D1–D8)
**For:** the list is live by construction; no migration, no package, no ingestion change and no backfill; one definition of "junior IT", reachable from PR 2; the provider-translated regex stays in
Infrastructure; the curated ids and the pattern have one versioned home; the row is a type of its own and the catalogue's and the related section's rows are untouched; the page works without JavaScript.
**Against:** the trade-offs listed under Consequences.

### The forks
- **A column filled at ingestion** (a flag or label on `job_ads`). Rejected: `UpdateFromSource` overwrites it on every re-import; it needs a migration, the most dangerous hotspot (CLAUDE.md §6.5),
  and a backfill; and it must be filled again for every change of the pattern. At read time the list is live by construction (plan).
- **The listed-ad snapshot as the source.** Rejected: it holds no title and no occupation group, and an erased ad would stay in it until the next tick, at most 15 minutes (ADR 0174 D1), where a
  per-request read leaves the list at once (security-auditor 1(c)).
- **A junior filter on `/lediga-jobb`.** Rejected: a place, filter or sort parameter on the catalogue is what ADR 0166 D10's catalogue trigger (trigger 12) names, and the S5 phasing locks the
  catalogue to "no kommun or län". The catalogue stays without one.
- **Opening `/jobb` to anonymous visitors.** Rejected: `/jobb` is login-only at four layers, and opening it would be the largest security change of the options. ADR 0176 rejects ADR 0005's path to the
  same end, "drop `RequireAuthorization` from `/api/v1/job-ads`", outright. Klas chose "Ny öppen sida /junior (Rek.)".
- **An indexable `/junior`.** Rejected by Klas: "noindex (Rek.)". The reasoning for it is in the amendment (D8).
- **One PR.** Rejected by Klas: "Två PR, /junior först (Rek.)". The post may go out after PR 1, and the filter on `/jobb` waits for #2241.
- **Widening `PublicJobAdSummaryDto` and `PublicJobAdList` with the deadline** (the plan's first form). Rejected (architect V4; security-auditor m2): `ExpiresAt` is listed as never in a row, the
  component can render nothing else, and a member added there would re-open two other decisions (D6).
- **The plan's rule with the curated arm outside the Data/IT field,** `IsListed ∧ (curated ∨ (Data/IT ∧ headline))`. Rejected (security-auditor m1): Data/IT is on both arms (D1).
- **A second place for the Data/IT groups,** inside the port's implementation. Rejected (architect V1): PR 2's `ApplyFilter` could not reach it, and the definition would exist twice (D2).
- **A model, or a recurring agent run, to label new ads.** Not decided here, and C is a stop (D9).

## Consequences

### Positive
- The list is live with nothing to run: an ad that closes, is removed, expires or is erased leaves the rows, the total and the options in the same request.
- PR 1 adds no migration, no package, no diff under `deploy/`, no `proxy.ts` or `robots.ts` edit and no change to ingestion, and `PublicJobAdSummaryDto`, `PublicJobAdList` and the sitemap are unchanged.
- PR 2 inherits one predicate and one resolver, and the `/jobb` filter cannot define "junior IT" a second way.
- The row cannot carry what it must not: its seven members are pinned by a contract test, a wire test and a boundary test, and the web's schemas are strict at every level.
- The curated file and the pattern have one home, a new reading is a new version, and a malformed file stops the host instead of silently listing nothing.

### Negative and accepted trade-offs
- **A regex runs per request,** twice, over the listed ads of the field's groups. The cost is unmeasured (*What is not measured*), and the 60-per-60 bucket is the bound. (Amended 2026-11-22, #2293: the cost is measured in ADR 0190 and the read's class is ADR 0064's third; the bucket bounds one address's rate and not the cost. See the Amendment 2026-11-22 below.)
- **The pattern is a heuristic.** It is precise (69 of 69) and finds about half (51 %). Until Klas chooses how new ads are labelled (D9), junior ads that the pattern misses are on the list only if
  they were in his reading of 2026-11-19, and that set ages as ads close.
- **Two engines must agree.** The .NET compile check and PostgreSQL's `~*` read one pattern, and the agreement test is the only thing that says so. A provider upgrade or an edit to the file can
  break it, and the test is where that shows.
- **The curated ids are in a public repository,** for good, from the first push. That is the amendment's to weigh (D8), and it is why the first push waits for a reading.
- **`JobAdSearchComposition.OrElse` is `internal`** where it was `private`: a seam between the search composition and the junior predicate.
- **A fourth anonymous consumer of the taxonomy port** (`TaxonomyAclLayerTests`), and one more anonymous bucket.
- **Two statements per request,** at one `now` and not one snapshot (*What is not measured*).
- **PR 2 inherits an open point.** The plan has the `/jobb` PR redirect a signed-in visitor from `/junior`, and has the capture gate leave junior searches out of saved and recent searches;
  `/junior` today answers every visitor alike and reads no session. Both are PR 2's to decide, and the redirect is to go through the ADR 0166 amendment of 2026-11-19 first. (The redirect is built by #2284, at the edge and not in the page, and went through ADR 0166's Amendment 2026-11-21; see the Amendment 2026-11-21 below.)

## What comes back

Nothing detects these automatically. Each is read when the event is proposed or reported.
- A migration, a new package, an ingestion change, a diff under `deploy/`, or an edit to `proxy.ts` or `robots.ts` (the CTO). PR 1 has none, and D9's option B would be the first.
- A change to the pattern, the Data/IT field or the id form: a new version of the file, and `security-auditor` before it merges.
- A plan over ADR 0045's hot-path budget (the CTO).
- The labelling choice of D9, when Klas takes it.
- The legal triggers are ADR 0166 D10's and have their home in that file (D8).

## Implementation

One PR on `feat/junior-it-list`, lane BE+FE, #2260. It adds no migration, no package and no diff under `deploy/`, and no edit to `proxy.ts`, `robots.ts` or the sitemap.
**Hotspots touched** (CLAUDE.md §6.5; the borrow and its return are the driving session's): `RateLimitingExtensions`/`RateLimitingOptions` and `DependencyInjection.cs` (`hotspot:di`), and
`messages/{sv,en}/publicjob.json` (`hotspot:i18n`).

- **Application:** `JobAds/Abstractions/IJuniorItJobAdQuery.cs` (the port, `JuniorItMunicipalityCount`, `JuniorItJobAdRow`); `JobAds/Queries/GetPublicJuniorItJobAds/` (`GetPublicJuniorItJobAdsQuery`,
  `…QueryHandler`, `…QueryValidator`, `PublicJuniorItJobAdDto` with `PublicJuniorItPlaceDto`, `PublicJuniorItJobAdLimits`, `PublicJuniorItJobAdsPage`).
- **Infrastructure:** `JobAds/JuniorIt/` (`junior-it-list.v1.json`, `JuniorItListLoader`, `JuniorItSetResolver` with `JuniorItSet`, `JuniorItPredicate`); `JobAds/JuniorItJobAdQuery.cs`; the
  registration in `DependencyInjection.cs` (the loaded list as a singleton at host build, the resolver as a singleton, the port scoped); the `<EmbeddedResource>` and its logical name in the
  `.csproj`; `JobAdSearchComposition.OrElse` made `internal`.
- **Api:** the `GET …/junior` route in `PublicJobAdsEndpoints.cs`, mapping `Available` to 200 and `NoSuchPage` to 404, and the policy (D7).
- **Web:** `app/(public-job)/junior/page.tsx` and `not-found.tsx`; `components/job-ads/public-junior-it-list.tsx` and `public-junior-it-job-ad-list.tsx`; `lib/api/public-junior-it-job-ads.ts`;
  `lib/dto/public-junior-it-job-ads.ts`; `lib/job-ads/junior-it-params.ts`; the `junior` block in `messages/{sv,en}/publicjob.json`; `public-job-group.test.ts` (the page list, the reader list,
  `PUBLIC_SURFACE`, and a test that the page is `noindex`, its rows plain anchors and nothing prefetched).
- **Architecture tests:** the two registry rows in `JobAdLifecycleReadRegistry`, the declared pin in `PagedResultContractTests`, the `TaxonomyAclLayerTests` allowlist row,
  `PublicJuniorItJobAdsBoundaryTests`.
- **Then the docs:** this ADR and its README row; the amendments to ADR 0166 and ADR 0106; `docs/runbooks/recruiter-pii-erasure.md` §2.4a; the session docs per ADR 0065. Then the panel, and
  `security-auditor`'s signature before the merge.

**Outside the diff, owed by the driving session** (the legal ones are listed in the amendment): the fifth addendum to DPIA #456 §8-B (local, main copy), whose text Klas approves; the processing
register's JobTech row (local, main copy); the counts-only reading before the curated file's first push; and the merge-day reading, recorded in the amendment and in
`docs/runbooks/public-job-page-publication-count.md`.

## Measurements

Each reading below is dated and has its source. Nothing is estimated. All are the driving session's of 2026-11-19, given in the brief, and none was re-run for this ADR.

| Reading | What | Recorded |
|---|---|---|
| R1 | the reading's size | 2026-11-19: 249 unique Data/IT candidates from JobTech read in full; 135 unique ads carry a list label; with the 9 Platsbanken duplicates that point at them, 144 ids, of which the file holds 143 (the ADR 0166 amendment's reading of 2026-11-20 left out the one outside the Data/IT groups). The headline rule `HEAD_JUNIOR` is right in 69 of 69 cases and finds 51 % of the junior ads. |
| R2 | the ported pattern against the original | 2026-11-19: on all 249 reading ads the ported pattern and `HEAD_JUNIOR` agreed, 69 hits each, all 69 carrying a list label; on 2 100 live Data/IT headlines they agreed, 56 hits each, 0 disagreements. A dated, historical measurement of the port; it says nothing about the pattern after a later edit. |
| R3 | the stock of new ads the rule does not reach | 2026-11-19: 67 new unique candidates published in the last seven days, 49 of them not caught by the headline rule, so about 50 a week to read. |

## References

- Code: `src/Jobbliggaren.Application/JobAds/Abstractions/IJuniorItJobAdQuery.cs` · `src/Jobbliggaren.Application/JobAds/Queries/GetPublicJuniorItJobAds/` ·
  `src/Jobbliggaren.Infrastructure/JobAds/JuniorIt/` (`JuniorItListLoader.cs`, `JuniorItPredicate.cs`, `JuniorItSetResolver.cs`, `junior-it-list.v1.json`) ·
  `src/Jobbliggaren.Infrastructure/JobAds/JuniorItJobAdQuery.cs` · `src/Jobbliggaren.Infrastructure/JobAds/JobAdSearchComposition.cs` · `src/Jobbliggaren.Application/JobAds/Publication/JobAdPublication.cs` ·
  `src/Jobbliggaren.Api/Endpoints/PublicJobAdsEndpoints.cs` · `src/Jobbliggaren.Api/RateLimiting/RateLimitingExtensions.cs` and `RateLimitingOptions.cs` (`PublicJobAdJuniorIt`) ·
  `web/jobbliggaren-web/src/app/(public-job)/junior/` · `web/jobbliggaren-web/src/components/job-ads/public-junior-it-list.tsx` and `public-junior-it-job-ad-list.tsx` ·
  `web/jobbliggaren-web/src/lib/api/public-junior-it-job-ads.ts` · `web/jobbliggaren-web/src/lib/dto/public-junior-it-job-ads.ts` · `web/jobbliggaren-web/src/lib/job-ads/junior-it-params.ts`
- Tests, in the tree: `tests/Jobbliggaren.Architecture.Tests/PublicJuniorItJobAdsBoundaryTests.cs`, `JobAdLifecycleReadRegistry.cs`, `PagedResultContractTests.cs` and `TaxonomyAclLayerTests.cs` · the web's
  `public-job-group.test.ts`. Named above and written in the same PR: `JuniorItHeadlinePatternAgreementTests`, `PublicJuniorItJobAdDtoContractTests`, `JuniorItJobAdQueryReadTests`.
- ADR 0160 · ADR 0166 (the amendment of 2026-11-19, the single home of the legal outcome) · ADR 0176 (D5, F4) · ADR 0182 · ADR 0106 (D9) · ADR 0113 · ADR 0043 · ADR 0039 · ADR 0167 · ADR 0071
  (Decision 1) · ADR 0045 · ADR 0154 · AGENTS.md §2.1, §5, §8 · CLAUDE.md §6.5, §9.2, §9.6
- `docs/reviews/2026-11-19-2260-plan-dotnet-architect.md` · `docs/reviews/2026-11-19-2260-plan-security-auditor.md` (both local, transcribed by the driving session)
- npgsql/efcore.pg v10.0.3 `NpgsqlRegexTranslator.cs` and the docker-library/postgres 18 Dockerfile, as read by `dotnet-architect` on 2026-11-19 (links may rot)

## Amendment 2026-11-20 (#2261 PR 2) — the junior filter on `/jobb` reuses D2's predicate and resolver, and A1–A5 settle the open point

> **Provenance.** Written by `adr-keeper` for the driving session from its brief; it adds no decision of its own. Additive: nothing above this heading is rewritten. A1–A5
> record `senior-cto-advisor`'s ruling of 2026-11-20 on the open points of PR 2 (the ruling's D1–D5, relabelled here so as not to collide with D1–D9 above). R4 and R5 continue
> the numbering of *Measurements*.

### What PR 2 delivers

- **Application.** `JobAdFilterCriteria` gains a required `bool JuniorIt`, AND-ed with every other axis.
- **Infrastructure.** `JobAdSearchTextResolver` takes `JuniorItSetResolver` and, only when the flag is on, builds the membership expression with `JuniorItPredicate.Matches(set)`;
  `ResolvedJobAdFilter` carries it as `JuniorItMembership`. `JobAdSearchComposition.ApplyFilter` applies it in one `Where`, after `/jobb`'s own `Status == Active` gate.
- **Api.** `GET /api/v1/job-ads` and its `/facet-counts` bind `?junior=true`.
- **Web.** `?junior=on` is a required `junior` field in `JobbUrlState`; a "Juniora IT-jobb" checkbox sits in the Filter panel, with no count.
- **Flag off.** No condition is emitted and no taxonomy is read; the SQL is pinned identical to before.
- **Unchanged.** The predicate, the resolver and the curated file, and `/junior` with its read. This is the reuse D2 asked for: "junior IT" is still defined once.

### The rulings (A1–A5)

The last bullet under *Negative and accepted trade-offs* left two points to PR 2: the capture gate and the redirect from `/junior`. A1 settles the first and A2 the second.

- **A1 — A junior search is never stored in recent searches (ruling D1).** `RecentSearchCaptureGate.Admit` returns null for it. It never strips the flag: a stripped search hashes
  as the broader one and would `Bump()` that row. No migration; the axis is not persisted.
- **A2 — PR 2 does not change `/junior` (ruling D2).** The redirect a signed-in visitor would have gotten fires ADR 0166 D10 trigger 22 (a selection keyed by sign-in state) and
  breaks the signed property "the page reads no session". A cross-site arrival carries no Strict session cookie, so the redirect would never reach the post's audience. A link on
  `/junior` waits for #2264's re-sign of that page, and goes only through `security-auditor`. This departs from the scope written in #2261. (Overridden by Klas's directive of 2026-11-21, #2284; see the Amendment 2026-11-21 below.)
- **A3 — No count on the toggle (ruling D3).** Distans has none, and a boolean has no concept-id key space (#551 PR-B D7).
- **A4 — `/jobb` keeps its own lifecycle gate, `Status == Active`, and not `JobAdPublication.IsListed` (ruling D4).** `IsListed` is ADR 0160's publication predicate. The junior
  predicate says nothing about lifecycle by design (D1), so each caller composes its own gate, and `/jobb`'s is the one it already has. The `/jobb` junior set is therefore junior
  ∧ `Status == Active`, and `/junior`'s is junior ∧ `IsListed(now)`; R4 reads how far they differed on one day.
- **A5 — PR 2 lands before #2264, with today's predicate at one call site (ruling D5).** #2264 (the stored classification, `JobAdJuniorAssessment` / `JuniorMembership.Of`)
  repoints the resolver. Whichever PR merges second rebases.

### `security-auditor`'s plan read, 2026-11-20 (0 Blocker / 0 Major)

No ADR 0166 D10 trigger fires for the `/jobb` filter. Trigger 22 protects the junior list's own public selection, and the scope of the ADR 0166 amendment of 2026-11-19 (the
public read, `/junior`, the curated file) does not reach the auth-gated `/jobb`. Her signature of 2026-11-20 stays valid only while the predicate, the resolver, the curated file,
`/junior` and its read, `robots.ts`, ADR 0166's text and the DPIA addendum are byte-unchanged. A change to any of them is trigger 22/23 and a re-take.

### Two dated readings

| Reading | What | Recorded |
|---|---|---|
| R4 | the two lists' divergence under A4, counts only | 2026-11-20T12:08:03Z, on the production box (CLAUDE.md §9.2 (a)): 146 active junior ads (the `/jobb` set); 0 of them past their `ExpiresAt`; 0 not from Platsbanken. The two classes by which that set can exceed `/junior`'s `IsListed` read (D1) were empty at that time. A dated reading, not a guarantee. No copy claims that the two lists are the same, or names a count. |
| R5 | the filter's latency against ADR 0045 class (a), p95 300 ms | 2026-11-20, `JuniorItFilterLatencyMeasurement` (`tests/Jobbliggaren.Api.IntegrationTests/JobAds/`) on a developer machine, 40 000 ads: junior list+count p95 51.4 ms; with one Data/IT group 16.1 ms; with `q` 28.0 ms; group facet with junior 40.8 ms. It reads the `/jobb` filter, not `/junior`'s read. A dated reading, not a live number: re-run the class to regenerate it (selector: AGENTS.md §7). |

### Named skips

- **Not persisted:** a junior search is not stored in recent searches, and the axis is not persisted (A1: a stripped search would `Bump()` the broader row; no migration).
- **No count on the toggle:** a boolean has no concept-id key space (A3).
- **No change to `/junior`:** it would key a selection by sign-in state, which is ADR 0166 D10 trigger 22 (A2). (Overridden by Klas's directive of 2026-11-21 and built by #2284; `security-auditor` ruled that trigger 22 does not fire. See the Amendment 2026-11-21 below and ADR 0166's.)

## Amendment 2026-11-21 (#2284) — a signed-in visitor of `/junior` is sent on to `/jobb?junior=on` at the edge

> **Provenance.** Written by `adr-keeper` for the driving session from its brief; it adds no decision of its own. Additive: nothing above this heading is rewritten except four sentences marked in place (D8's "same answer", the open point under *Negative and accepted trade-offs*, A2, and A2's named skip). It records `senior-cto-advisor`'s ruling of 2026-11-21 (`docs/reviews/2026-11-21-2284-plan-senior-cto-advisor.md`, local, transcribed by the driving session). The legal outcome is ADR 0166's Amendment 2026-11-21: this amendment points there and restates none of it (D8, one decision, one home). The mechanics are ADR 0018's Amendment 2026-11-21.

**The directive.** Klas-direktiv 2026-11-21: "inloggad besökare på /junior → /jobb?junior=on, också när länken öppnas från LinkedIn/Discord/FB".

**The mapping.** A request for exactly `/junior`, with a query the page accepts, that carries the session cookie and is a GET or a HEAD, is answered with a `307`, `no-store`, to `/jobb?[municipality=…&]junior=on[&sortBy=…]`: the list's kommun and order, and the junior filter of #2261. `page` and every other key are dropped, because the two lists are different sets (A4). Code: `web/jobbliggaren-web/src/lib/auth/junior-it-signed-in-route.ts` and `src/lib/job-ads/junior-it-handover.ts`, called from `proxy.ts`. The page, its list, params, reader, metadata and copy are unchanged, except one comment-only edit of the page's docblock, which ADR 0166's amendment declares.

**The rulings D-A to D-E** (`senior-cto-advisor`, in brief; reasons, rejected alternatives and trade-offs are in the report and in ADR 0018's Amendment 2026-11-21):
- **D-A, structure:** a sibling branch beside `publicJobAdRoute`, not a case in the bounce. The continuation answer and `hopTarget` move into a module both branches import. `strictCookieBounce`'s decisions are unchanged.
- **D-B, the marker:** the `vidare=1` that the hop leaves is removed at the edge, by a strip whose condition is the exact complement of the bounce's. It cannot loop. ADR 0165 D5's client island stays for `/jobb/<uuid>`.
- **D-C, methods:** the signed-in 307 answers GET and HEAD.
- **D-D, what travels:** validated keys only. The hop carries the list's own validated address, and the 307 carries `municipality`, `junior=on` and `sortBy`.
- **D-E, the signed-in test:** the session is validated when due, through the refresh driver, moved out of `route()` with a typed outcome, and is not only looked for. A session idle for more than 30 days leaves a cookie the server no longer knows, and a presence check would send that visitor to `/logga-in?next=/jobb`, and after signing in to `/jobb` without the filter.

**A2 is overridden by Klas's directive.** A2 was `senior-cto-advisor`'s own ruling of 2026-11-20 ("PR 2 does not change `/junior`"), and the directive of 2026-11-21 sends a signed-in visitor of `/junior` to `/jobb?junior=on`, including arrivals from other sites (CTO, Rule 5). A2 rested on two grounds:
- **The cookie ground,** that a cross-site arrival carries no Strict session cookie, so the redirect would never reach the post's audience, is answered by the bounce (ADR 0018, Amendment 2026-11-21).
- **The trigger-22 ground** is `security-auditor`'s ruling of 2026-11-21. Its home is ADR 0166's Amendment 2026-11-21, and it is not restated here. A2's own condition, that the redirect goes only through `security-auditor`, is kept: the ruling is hers (plan read of 2026-11-21), and the renewal waits for her signature in ADR 0166.

No later CTO session raises A2 again.

**The hop that most visitors pay.** Until now a signed-out visitor who followed the post from another site got the list in one request. Now every cross-site document navigation to `/junior` without a cookie first gets the continuation document, then the strip's 307, then the list. That is most of the post's audience. The directive requires it: the cookie is the only signal that a visitor is signed in, and browsers withhold it on a navigation another site starts. A link that a desktop app hands to the browser arrives with `Sec-Fetch-Site: none` and pays nothing. This is information for Klas, not an escalation.

**Residuals by pointer.** ADR 0018's Amendment 2026-11-21 holds R-a to R-f. Two of them bear on this page: **R-a,** a signed-in visitor who holds a stale cookie meets the redirect for at most 15 minutes after a revocation on the server, and **R-e,** a signed-in visitor sees the public list only in a private window, which is also where a recruiter complaint about the list is read.

## Amendment 2026-11-22 (#2293, ADR 0190) — `/junior`'s default view is indexable while a floor holds, and the list's read gains a third member

> **Provenance.** Written by `adr-keeper` for the driving session from its brief; it adds no decision of its own. Additive: nothing above this heading is rewritten except four pointers marked in place (D5's two `Active` entries, D8's "`noindex, follow` on every 200", and the sentences of *What is not measured* and *Consequences* about the cost). It points to ADR 0190, which holds Klas's answers and the rulings. The legal outcome is ADR 0166's Amendment 2026-11-22: this amendment points there and restates none of it (D8, one decision, one home).

**1. D8: "`noindex, follow` on every 200" becomes "every answer except the default view while the floor holds".**
- The default view (no kommun, newest first, page 1) is `index, follow` with its own canonical, built by `publicJuniorUrl()`, only while the floor holds; it is the 16th URL of the sitemap's pages part whether or not the floor holds (ADR 0174's amendment of the same date). Every other 200, the empty kommun included, and the 404 and the 5xx stay `noindex`, and a `noindex` answer names no canonical. The rule is ADR 0190 D4, and the single home of its legal outcome is ADR 0166's Amendment 2026-11-22.
- D8's "in no sitemap" is amended in the same way, and "`robots.ts` is unchanged" holds. The title, description and link preview stay fixed text with no row and no count, and there is no JSON-LD; the title is new text (`publicjob.junior.metaTitle`) and the `h1` is unchanged (ADR 0190 D5).
- D8's "A count line, the rows and the pager sit in one `data-nosnippet` block" now includes the form: the kommun options carry counts.
- D8's parameters, "named and valued as on `/jobb`", are unchanged by this change; renaming the page parameter to `sida` is PR 2's (ADR 0190 D1).

**2. D5: the registry holds three `Active` entries, the read passes one `now` to three members, and the envelope has three.**
- `JobAdLifecycleReadRegistry` (ADR 0113) gains `JuniorItJobAdQuery.MeetsFloorAsync`, whose note says that the organisation number and the company name stay inside the statement and **only a boolean leaves the query**. The handler passes its one `now` to `CountByMunicipalityAsync`, `ReadPageAsync` and `MeetsFloorAsync`.
- The three members compose one private base, `ListedJuniors` (`JobAdPublication.IsListed(now)` and `JuniorMembership.Of`), and each passes it its own read of `db.JobAds`. The count's and the page's statements are pinned equal to the chains they composed inline before it, so the shared base changes no SQL and ADR 0188's R1 and the count's bucket stay valid.
- The envelope is `rows`, `places` and `floorHolds`: one boolean, whether the whole list meets the floor at `now`. It is the same for every kommun, order and page. Which view may be indexed is the web's rule.
- ADR 0166's Amendment 2026-11-19 recorded the envelope as exactly two members; its Amendment 2026-11-22 records the third.

**3. The cost is measured, and the bucket bounds one address.**
- *What is not measured*, first bullet (the cost of the statements on the box's table): the cost is now measured on a 40 000-ad corpus, on a developer machine, in ADR 0190's *Measurements*, with the three statements and their plans. ADR 0064's Amendment 2026-11-22 places the read in a third class, with a bound and return conditions. The box's own plan is still not measured.
- *Consequences*, "the 60-per-60 bucket is the bound": the bucket bounds one address's rate and not the cost. The cost is what ADR 0190 measures and the class's bound holds. D7's number and its basis are read in ADR 0190, *The junior bucket*.

**4. What does not change.** The seven-member row, the validator's three parameters and their bounds, the policy `public-job-ad-junior-it` itself (its number is read in ADR 0190, *The junior bucket*), `no-store` on every answer, the reader and the signed-in hop (the Amendment 2026-11-21). The registry's other entries, `JuniorMembership` and the rules file are untouched.

ADR 0186 remains **Accepted**.
