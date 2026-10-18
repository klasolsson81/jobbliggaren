# ADR 0174 — The job sitemap is answered from a snapshot the API takes every five minutes and refuses past fifteen: two root-level parts, no `lastmod`, and the web reads each part per request

**Date:** 2026-10-18
**Status:** Accepted 2026-10-18 (the CTO's ruling under CLAUDE.md §9.2 and Klas's four answers of the same day, D8). The ADR 0166 amendment of the same date, which carries the legal
outcome (D8), is Accepted: `security-auditor` signed it at her scoped re-check of the PR on 2026-10-18. The merge waits for that signature, because a merge is a go-live (ADR 0154).
**Deciders:** Klas Olsson (controller and product owner; four answers in chat on 2026-10-18, recorded in D8) · `senior-cto-advisor`, "the CTO" below (decision-maker under CLAUDE.md §9.2;
ruling on forks F1–F5, report-only; `docs/reviews/2026-10-18-2090-s4-plan-cto.md`) · `security-auditor` (plan read, report-only; `docs/reviews/2026-10-18-2090-s4-plan-security-auditor.md`:
her verdict, M1–M3, m1–m4 and the escalation) · `dotnet-architect` (form read, report-only; `docs/reviews/2026-10-18-2090-s4-plan-dotnet-architect.md`)
**Amends:** ADR 0160 (D2, D5, D6), ADR 0165 (D3, D6), ADR 0064 (rule (d)), ADR 0159 (D2), ADR 0106 (the D9 read-path table, and the premise of the signed-in ended/erased difference) and
ADR 0166 (D2, D4, D5, D6(d), D8, D10, D11), each by a dated amendment at the end of that file. Nothing above those amendments is rewritten.
**Related:** [#2090](https://github.com/klasolsson81/jobbliggaren/issues/2090) (the epic) · [#2167](https://github.com/klasolsson81/jobbliggaren/issues/2167) (S4, the issue this ADR ships
under) · ADR 0171 (S3, the `JobPosting` block) · ADR 0113 (every `job_ads` read site carries a written lifecycle decision) · ADR 0120 (a rendered count is true or it is absent) ·
ADR 0122 (the 8 GB box) · ADR 0154 (§3: every merge is a deploy; §4: what needs Klas's GO) · ADR 0158 (Alternative B, r4) · ADR 0050 (Amendment 2026-08-04: no CDN) · DPIA #456 §8-B
(local) · #2121 (registration open by compose default) · #1477 (Klas's rule that a person never lands outside the site's frame)
**Measured against:** the branch `feat/seo-s4-job-sitemap` in `C:/tmp/jbl-seo-s4`, cut from `d7b242b12`, at its three code commits (the preparatory `bd0f9fdc2`, then the web and the
backend), read on 2026-10-18. The three plan reads were taken at `d7b242b12`. The CTO re-read two Google pages on 2026-10-18: *Build and submit a sitemap* (updated 2026-07-08) and
*Reduce the Googlebot crawl rate* (updated 2026-10-06). Nothing here is merged, and the merge is the go-live.

> **Provenance.** Written by `adr-keeper` for the driving session from its brief (CLAUDE.md §9.2, §13); it adds no decision of its own. D1–D7 and D9 follow the CTO's ruling. The
> architect's form read, whose five Viktigt and three Nice-to-have were built into the implementation, is marked "(architect)" where it changed or added to the ruling. D8 follows
> `security-auditor`'s plan read and records Klas's four answers. The three reports are local files, transcribed verbatim by the driving session from the agents' hand-backs. Every
> statement about the code was read from the worktree on 2026-10-18. The test results under *Implementation* are the driving session's of that date, and none was re-run for this ADR.
> What no source measures is listed under *What is not measured*, and nothing else here claims to be measured. The mandatory agents review the delivered diff in the PR;
> `design-reviewer` is a named skip (D6). The ADR lies in the ignored 0071–0199 range and needs `git add -f` and a README row (precedent 0167–0173).

---

## Context

Step S4 of the SEO epic (#2090, issue #2167) puts the public job pages in a sitemap. The page (ADR 0165) and its `JobPosting` block (ADR 0171) exist, and a crawler still has no way to
find them. Seven facts shape the decision.

- **Nothing public links to a job page before S5.** `security-auditor` grepped `src/` on 2026-10-18: outside the signed-in app the only job link is the `/gast` demo's `/gast/jobb/…`,
  which `robots.txt` disallows. On the same day `/sitemap.xml` answered 200 with 14 `<loc>` and no job URL, and `robots.txt` named `Sitemap: https://jobbliggaren.se/sitemap.xml` (her
  public reads).
- **The list is the whole listed corpus, the same for every reader.** The peak listed count measured is 41 548 (2026-10-11, ADR 0166), and the last S2-P3 reading, 2026-10-12 05:15 UTC,
  was 39 415 (ADR 0166 D11). ADR 0064's amendment of 2026-10-10 puts a recurring read of the whole corpus in the class that is precomputed (rule (d)) and a read of one record in the
  class with nothing to precompute. A sitemap is the first kind.
- **ADR 0160 left the SQL form of the predicate to the first list consumer** (D2: `IsListed(now)` in the type that holds `Classify`, with an agreement test and a Testcontainers test).
  It also made `no-store` absolute, because the API is the authoritative answer to "now" (D6). A list of every listed ad is not that per-ad answer repeated.
- **A sitemap file holds at most 50 000 URLs or 50 MB.** Unless it is submitted through Search Console it covers only what lies under its own directory (Google, *Build and submit a
  sitemap*, updated 2026-07-08, read 2026-10-18). A job page is `/jobb/<uuid>`, and `/jobb` is a protected prefix.
- **Next 16.3.8 fixes the shape of the web side.** A dynamic segment is a whole folder name (`dynamic-routes.md:18`). `generateSitemaps` puts its parts at `/sitemap/[id].xml`, a
  subdirectory (`generate-sitemaps.md:20`), and prerenders the ids at build time, when the build has no `BACKEND_URL`.
- **One 8 GB box serves everything, and every merge is a release to it** (ADR 0122, ADR 0154). No box advance passes a Redis ACL change before an escrow sitting with Klas (ADR 0154 §4).
- **ADR 0166 D10 trigger 1 names "the S4 sitemap"** and fires by its own text (`security-auditor`). The conclusion in ADR 0166 is withdrawn for the sitemap, not for the page, until Klas
  takes it again and she signs (D8).

## Decision

**The API reads the listed set every five minutes into an immutable in-memory snapshot, and the API itself refuses to answer from a snapshot older than fifteen minutes.
`GET /api/v1/public/job-ads/listed-ids?part=&parts=` returns the ids of one of two parts. The web serves four sitemap files at the site root (an index, a marketing part and two job
parts), reads each job part from that endpoint on every request, and writes `loc` only: no `lastmod`, no `changefreq`, no `priority`. The list endpoint has its own rate-limit policy,
and the job page's budget does not change until it has been measured again. The legal outcome has one home, the ADR 0166 amendment of the same date.**

### D1 — Where the list is computed: an in-API sampler, and a bound the API enforces on both edges (CTO F1, 1c). ADR 0160 D6 is narrowed for the list answer only

- **The predicate.** `JobAdPublication.IsListed(DateTimeOffset now)` is an `Expression<Func<JobAd, bool>>` in the type that holds `Classify`, as ADR 0160 D2 instructs the first list
  consumer: `External != null && External.Source == PublishedSource && Status == Active && (ExpiresAt == null || ExpiresAt >= now)`. `External != null` is redundant in SQL and is kept for
  the compiled form, which the agreement test runs: without it a never-imported row would throw. The two forms share no code, and two tests pin them (Implementation).
- **The sampler.** `ListedJobAdSnapshotSampler` (Application, a singleton, over `IAppDbContext`) opens one DI scope per read, because a singleton must not capture the scoped context. A
  read is `AsNoTracking`, `Where(IsListed(asOf))`, `OrderBy` id, `Take(ceiling + 1)`, projected to the id. The sampler has no `try`/`catch` of its own: the hosted service's guard is the
  one place a failure is caught (architect).
  - A read that throws publishes nothing, so the previous snapshot is neither replaced nor re-stamped and ages out at the bound.
  - A read that exceeds the ceiling publishes nothing and logs event 6234 (Error), with the ceiling and nothing from a row.
  - A read that finds nothing is published as a measured zero (ADR 0120). `null` is the only state that means no read has succeeded.
  - It publishes by compare-and-swap, so an older read never replaces a newer one (architect: two overlapping calls are possible in a test host that keeps the timer, or after a later
    resample).
- **The snapshot.** `ListedJobAdSnapshot` is immutable and replaced whole: `AsOf`, the instant that was passed to `IsListed`, and the ids already split into the parts at the tick, held
  as read-only lists (architect), so a request does no work per id. The reader (`IListedJobAdSnapshotReader.Current`) and the sampler (`IListedJobAdSnapshotSampler.SampleAsync`) are
  separate interfaces, as in ADR 0158 and ADR 0159.
- **Why Application, over `IAppDbContext`, behind no port** (architect). The predicate is provider-neutral, which is why it lives in Application (ADR 0160 D2), and a port around plain
  LINQ would be a repository over EF (AGENTS.md §5). The lifecycle registry's IL scan (ADR 0113) reaches Application and Infrastructure only, so a read inside the Api's hosted service
  would be invisible to it. `IServiceScopeFactory` already has precedent in Application.
- **The hosted service.** `ListedJobAdSnapshotService` (Api/Hosting) runs `SampleAsync` on the shared loop (D7): once at start, then every five minutes. A failed tick logs event 6233
  (Warning) with the exception's type name. It is composed in the Api alone: `ListedJobAdSnapshotRegistration` is called from `Program.cs` beside the two other samplers, and nothing is
  added to `AddPersistence` or `AddApplication`, which the Worker loads too. There the sampler would read the whole listed set every five minutes for no reader.
  `ListedJobAdSnapshotBoundaryTests` (Architecture) holds it: neither the Worker, Infrastructure nor Application outside `JobAds.Listing` references the sampler type. The rule names the
  type and not the namespace, because the Worker's generated Mediator code legitimately references the query handler (architect).
- **The read-time verdict.** The query handler judges the snapshot against `IDateTimeProvider` when it is read (ADR 0159 D3). It reads `Current` once and judges and slices from that one
  instance, so the stamp and the ids can never come from two snapshots. `IsFreshAt(now)` is `AsOf <= now + FutureTolerance && now - AsOf <= MaxAge`. No snapshot, or one outside either
  edge, is unavailable. The upper edge is the architect's: if the wall clock steps back and the ticks then fail, `now - AsOf` turns negative, and a one-sided rule would serve the snapshot
  for the step plus fifteen minutes. ADR 0159 D3 already treats a future stamp as Unknown.
- **The constants.** `SampleInterval` 5 minutes, `MaxAge` 15 minutes, `FutureTolerance` 5 seconds, `PartCount` 2 and `Ceiling` 80 000 are constants and not options, because a setting can
  be made to disagree with the rule it serves (ADR 0159 D2), and the 15 minutes are part of the legal record (ADR 0166 D10, trigger 9). `ListedJobAdSnapshotLimitsTests` pins that `MaxAge`
  is at most 15 minutes and at least twice the interval, that `FutureTolerance` is shorter than one interval, and the ceiling's relation to the file limit (D3). A test lowers the ceiling
  through an `internal` constructor; the container only builds through the public one.
- **What the log lines carry.** The sampler's and the service's own lines carry the ceiling (6234) or the exception's type name (6233), and never a row, an id or an exception message. The
  CTO's wider sentence, that a failure logs only the type name, does not hold for an EF read (architect). EF Core logs its own `QueryIterationFailed` (Error, category
  `Microsoft.EntityFrameworkCore.Query`, with the exception in the message) and `ConnectionError` (Error, category `Microsoft.EntityFrameworkCore.Database.Connection`), and
  `appsettings.json` lowers neither category. ADR 0159 D2's identical sentence holds for its source, a raw `NpgsqlCommand` that EF's diagnostics never see. The exposure is not new:
  every failed EF read in the Api is logged that way. This ADR claims no more than its own lines.
- **ADR 0160 D6 is narrowed, not broken.** D6 governs answers about one ad, and those stay per request, with the API authoritative for "now". The list is answered from a snapshot that
  is at most fifteen minutes old, because the API refuses anything older, and every answer is still `no-store`. The snapshot's age is the only window, and it is bounded. For the list
  endpoint only, D6's "now" becomes "now within fifteen minutes". That follows from the epic's bound and not from this design: only 1e kept D6 literal, at a cost ADR 0064 forbids.
- **Why this form (CTO).** ADR 0064 rule (d) and its ban on cache-aside: the read leaves the request handler and is computed once for every reader, and a timer is not cache-aside. DIP
  (Martin 2017, ch. 11 and 22): how stale the listed set may be is publication policy, so the component that answers "listed" must also refuse an answer that is too old, which under 1a
  and 1b the web would decide. Fail fast and steady state (Nygard 2018, ch. 5): a snapshot past its bound is refused and never served, and the endpoint's cost does not grow with
  traffic. Twelve-Factor VI: the endpoint never assumes the snapshot exists.

### D2 — No `lastmod` anywhere (CTO F2, 2a)

- No `lastmod` per URL, none per index entry, and never the generation time. Every entry carries `loc` and nothing else: no `changefreq` and no `priority`. An element-set test pins it
  on both builders (`sitemap-xml.test.ts`), and the old pin on `priority` and `changefreq` in `seo-metadata-routes.test.ts` became its inverse.
- **Why.** Google uses `lastmod` only when it is "consistently and verifiably" accurate, and only as the time the page's content last changed. Bing rejects a generation time (the driving
  session's reading of Bing's July 2025 blog, as the CTO states it; not re-read for this ADR). There is no trustworthy source. The row has no `UpdatedAt` and no hash, the import
  reassigns the text unconditionally (`UpdateFromSource`), and the page also renders things the row does not hold (taxonomy names, the coded catalogue, the template). Even a new column
  (2b) could not record when the content at the URL changed, so it would fail the test it is built to pass. Epic F3 also allows no migration in phase 1.
- **Minimisation.** The files publish no per-ad change time of any kind (`security-auditor`: no per-ad timestamp leaves the service). That is one of the sitemap's own safeguards in the
  ADR 0166 amendment of the same date (its items (ii) and (iv)). If phase 2 ever needs `lastmod`, the honest source is a hash of the public projection, not a row column. Without
  `lastmod`, Google schedules recrawls itself. Anything fresher means the Indexing API, which is phase 2, Klas's call, and a push inside ADR 0166 D10 trigger 5.

### D3 — The layout: an index, a marketing part and two job parts at the site root; the id decides the part (CTO F3, 3b, K = 2)

| File | Holds | Reads |
|---|---|---|
| `/sitemap.xml` | the index: the three files below, by absolute URL | nothing |
| `/sitemap-sidor.xml` | the 14 marketing URLs | nothing |
| `/sitemap-jobb-1.xml` | the listed ids whose first hex digit is 0–7 | `listed-ids?part=1&parts=2` |
| `/sitemap-jobb-2.xml` | the listed ids whose first hex digit is 8–f | `listed-ids?part=2&parts=2` |

- `/sitemap.xml` stays the address `robots.txt` names (ADR 0165 D6). It was the whole sitemap and is now the index.
- **The partition.** An id's part is ⌊d·K/16⌋ + 1, where d is the first hex digit of the id's canonical lower-case form and K is one of 1, 2, 4, 8 and 16
  (`ListedJobAdPartition`). The split is uniform because both creation paths of a `JobAd` use `JobAdId.New()`, a v4 GUID (the CTO, at `d7b242b12`). Membership follows the id and never its
  position in a list. The index never changes, the index and the parts cannot disagree, and two parts fetched hours apart can neither skip nor repeat an ad.
- **K.** K is the smallest power of two at which the measured peak listed count fills at most half a file, so the corpus can double before it reaches the cap. The peak is 41 548
  (2026-10-11, ADR 0166). At K = 1 a file would sit at 83 % of the 50 000 limit, and at K = 2 each file sits at about 42 %. The merge-day S2-P3 reading, 42 223 listed (2026-10-18), is above
  that peak and changes neither figure by more than a point. K exists in two places: the web's `JOB_SITEMAP_PART_COUNT` and
  the API's `ListedJobAdSnapshotLimits.PartCount`, whose validator answers 400 for any other count (architect). `ListedJobAdSnapshotBoundaryTests` reads the web's constant and holds it equal
  to the API's, and in the supported set. Raising K therefore means changing both constants and adding route folders. The CTO's sentence said "a constant"; there are two.
- **Routing.** Each part is its own static route folder, `app/(sitemaps)/sitemap-jobb-1.xml/route.ts` and `-2`, because Next's dynamic segments are whole folder names. A filesystem test
  pins one route folder per job part and no other, each calling its own part (`sitemap-routes.test.ts`), and the index is built from the same part list (`job-sitemap.ts`), so the
  folders and the index's entries are one set. `app/sitemap.ts` is deleted, because it collides with `app/sitemap.xml/route.ts`; the same test file pins that no metadata sitemap remains
  under `app/`.
- **Every file at the root.** A sitemap covers only what lies under its own directory unless it is submitted through Search Console (Google), and a job page is `/jobb/<uuid>`. Nothing
  sits under `/jobb`, which is a protected prefix.
- **The guard, a fitness function** (Ford/Parsons/Kua 2017, ch. 2). A part with more than 50 000 ids is answered 503 with one count-only log line (D6). It is never served truncated and
  never over the cap. The 50 MB limit cannot bind first: 50 000 URLs come to about 4.4 MB (the CTO's figure).
- **Which guard fires first** (architect). The API's ceiling is 80 000 ids, about twice the peak of 2026-10-11. It is derived from that measured peak and not from Google's limit, because
  Google's vocabulary belongs in the web (ADR 0171 D2). At the ceiling each part holds about 40 000 ids, below 50 000. With uniform v4 ids a part reaches 50 000 only when the total is about
  100 000 (the architect's derivation; a standard deviation of about 160). The API's refusal, event 6234, is therefore the warning that comes first, and the web's 503 on a part over 50 000
  is a second line that the ceiling is meant to keep out of reach. A test pins `Ceiling / PartCount` at 45 000 or below. Both guards write a log line (Consequences).
- **One URL builder.** `publicJobAdUrl(id)` builds the sitemap's `loc` and the page's canonical (`(public-job)/publik/jobb/[id]/page.tsx`), so the two cannot differ. A test pins that they
  agree.

### D4 — The wire, and a result with two cases (CTO "Also ruled"; architect)

`GET /api/v1/public/job-ads/listed-ids?part={1..K}&parts={K}` lives in the anonymous `PublicJobAdsEndpoints` group (ADR 0160 D1), anonymous by omitting `RequireAuthorization`. The fixed
segment does not collide with `{id:guid}`.

| Status | When | Body |
|---|---|---|
| 200 | the snapshot is fresh | `{"ids": [...]}`: lower-case GUIDs in id order and nothing else (`ListedJobAdIdsDto`). A fresh snapshot with no id in the part is `{"ids": []}`, a true empty answer. |
| 400 | `part` or `parts` is not one the sitemap declares: the validator (`Parts == PartCount`, `Part` in 1..PartCount), or the framework's binding for a value that does not parse | The two roads differ, and the tests check status and `no-store` only |
| 429 | the endpoint's own policy (D5) | `Retry-After` |
| 503 | no snapshot, or one outside its bound | `Retry-After: 60` |

`Cache-Control: no-store` is on every answer, through the existing `/api/v1/public` prefix block in `Program.cs`, which covers the framework's own 400 and 429. The wire carries no `asOf`,
no count and no ad field, and a member-set test pins exactly `ids`.

- **A closed result.** `GetListedJobAdIdsQuery(Part, Parts)` is answered with `ListedJobAdPart`, an abstract record with a private constructor and two cases, `Available(Ids)` and
  `Unavailable`. The endpoint maps it by type, and an unmapped case throws `UnreachableException`. It is not a new `ErrorKind`: no domain rule failed, and 503 is an availability status that
  `ErrorKind` does not model, as it does not model 401 (AGENTS.md §3). It is not `T?`, which the codebase already uses for two different meanings (`PagedResultContractTests`). The query is
  not an `IAuthenticatedRequest`. The handler does no I/O, and the 60 seconds is a constant of the endpoint, HTTP vocabulary.
- **Why AGENTS.md §5's "unpaginated list fetches" does not apply** (architect). The rule keeps a list from growing with the data. Here the ceiling bounds the read (`Take(ceiling + 1)`)
  and the answer, because an answer is one of K parts of a set that the sampler refuses past the ceiling, and the validator locks K. No input can make either larger. The heuristic in
  `PagedResultContractTests` looks for `Page` and `PageSize` and does not fire.
- **The paged wire is rejected** (Alternatives).

### D5 — The budgets, and what "S4 measures again" means (CTO F4). ADR 0165 D3 is carried out here

- **(a) The list endpoint has its own policy,** `public-job-ad-listed-ids` (`RateLimitingOptions.PublicJobAdListedIds`): a TokenBucket per client IP, 6 per 60 s in 6 segments (a token
  limit of 6, one token per 10 s), `QueueLimit` 0, and a 429 that carries `Retry-After`. A burst of 6 is three complete reads of two parts, and the sustained rate is one call per 10 s.
  `security-auditor` approved the numbers on 2026-10-18.
  - It never shares `public-job-ad-read` (`PublicJobAdRateLimitWiringTests`) and is never a per-user policy, which would give an anonymous caller `NoLimiter`. Crawler traffic on one
    endpoint must not use up the other's bucket (Saltzer & Schroeder 1975, least common mechanism; Nygard 2018, ch. 5).
  - The bucket is per crawler, because the web relays the client address on every call. A row in `ForwardedClientBudgetTests` carries that claim.
  - The endpoint does no I/O, so the limit protects bandwidth and CPU and not the database. It protects availability and not confidentiality: an observer who compares two reads stays
    inside it (`security-auditor`).
  - The apex has no AAAA record (her public read, 2026-10-18), so per-address partitioning works. ADR 0160 D5's rule of one bucket per /64 applies if one is ever added.
- **(b) The page's budget does not change:** 60 per 60 s per IP (ADR 0160 D5). Raising it now would be a guess, and a separate budget for the page probe would be the same raise under
  another name.
- **(c) The measurement.** ADR 0160 D5 says the numbers are raised from measured crawl rates, and ADR 0165 D3 said that S4 measures again. A reading is counts only (CLAUDE.md §9.2 (a)): it
  uses `grep -c` and never prints a line (§9.2 (b)).
  1. **Before the merge, one dated baseline,** taken with the commands the later readings use: rate-limit rejections per day on the job-detail endpoint, which the probe and the render both
     read, and the 503s per day that the site answers on `/jobb/<uuid>`, both since S2 went live. If the logs no longer cover part of that window, that part is recorded as not measured and
     never as an estimate.
  2. **After the merge, two readings,** one week and four weeks after Google first fetches a job part: the same counts plus rejections on the new policy, and Search Console's Crawl Stats
     and Sitemaps report. Klas reads Search Console, because agents never sign in (§9.2 (d)).
  3. **When to raise.** A raise is warranted if Googlebot gets a 503 on job URLs on two or more days of a reading window, because Google warns that a URL answering 503 for several days may
     be dropped (*Reduce the Googlebot crawl rate*, updated 2026-10-06, read 2026-10-18). The size of the raise comes from the readings, and `security-auditor` approves it. No reading, no
     raise.
  4. **Where it is recorded.** The rule, the baseline and the readings are recorded in this ADR. The reading dates go in the phase-1-close row of #2090, and no new issue is filed
     (CLAUDE.md §9.6). The baseline is entered in the table below before the merge, and each reading is appended to this ADR as a dated amendment.

| Reading | When | What | Recorded |
|---|---|---|---|
| Baseline | 2026-10-18 05:34 UTC, before the merge | the two counts above, per day, since S2 went live | See the three readings below this table. |
| Reading 1 | one week after Google first fetches a job part | the same, plus rejections on `public-job-ad-listed-ids`; Search Console (Klas) | Not yet taken. Appended to this ADR as a dated amendment. |
| Reading 2 | four weeks after Google first fetches a job part | the same | Not yet taken. Appended to this ADR as a dated amendment. |

The baseline, counts only, read over SSH on 2026-10-18 at 05:34 UTC:

- **Rate-limit rejections on the job-detail endpoint: 0.** These are the event 2001 lines whose path is a uuid under `/api/v1/public/job-ads/`
  (`grep -cE 'Path=/api/v1/public/job-ads/[0-9a-fA-F-]{36} Method='`) in the `jobbliggaren-api` container's log. Rejections on `public-job-ad-listed-ids` are counted
  apart, on its own path (`grep -c 'Path=/api/v1/public/job-ads/listed-ids Method='`).
  - **The window is short.** That container started at 2026-10-17 19:50:16 UTC, so the window runs from then to the reading, about nine and three-quarter hours. The log holds 184 lines in that window.
  - **Most of the window since S2 went live is not measured.** Container logs are `json-file` (3 × 10 MB) and do not survive a recreate, so 2026-10-12 05:35 to 2026-10-17 19:50 is not covered.
  - **The same limit binds the later readings.** Every release recreates the container, so the long window is Search Console's Crawl Stats, which Klas reads.
- **503s the site answered on `/jobb/<uuid>`: not measured.** No log on the box records them.
  - Caddy has no access log. Its default logger writes `http.log.error` on a handler error, such as an unreachable upstream. A 503 that Next answers is a proxied response, not a handler error, and the web writes no access log.
  - Caddy showed 0 error lines in the same window, which proves nothing about 503s.
  - A 503 at the edge follows an API 429, which the count above covers, or an API failure or timeout, which it does not.

### D6 — The web: four route files, a pure XML module and an answer for every failure (CTO F1 and "Also ruled")

- **Files.** `app/sitemap.ts` is deleted. `app/(sitemaps)/{sitemap.xml,sitemap-sidor.xml,sitemap-jobb-1.xml,sitemap-jobb-2.xml}/route.ts` are all `force-dynamic`, answer
  `application/xml; charset=utf-8`, and carry `no-store` on every answer, 200 and 503 alike (as ADR 0165 D2). `lib/seo/sitemap-xml.ts` is a pure module: `loc` only, escaped by
  construction, one entry per line (so `grep -c '<loc>'` counts entries), and a `RangeError` past 50 000 entries. Next 16 writes no index and escapes nothing, so the XML is written there (the
  module's own comment). `lib/seo/marketing-sitemap.ts` holds the 14 paths, `lib/seo/job-sitemap.ts` the part count and the root paths, and `lib/seo/sitemap-responses.ts` the answers.
- **The web neither caches the list nor decides which ads are in it.** A request for a job part makes one API call (`lib/api/public-job-ad-ids.ts`): `cache: "no-store"`,
  `redirect: "manual"`, a 5-second timeout, and only the headers `pickForwardedHeaders` returns (the client address; a test pins the key set, so no `Cookie` or `Authorization` can reach the
  API). The body is validated with Zod (`lib/dto/public-job-ad-ids.ts`, a strict object whose ids are lower-case GUIDs), and the architecture test holds its keys equal to the DTO's. `part`
  and `parts` come from the route file and from constants, never from the request's query string (`security-auditor` m3 (b)).
- **A job part that cannot be read is a 503 with no body and `no-store`.** It is never an empty 200, because an empty `urlset` claims that nothing is listed (ADR 0120). It is never a 404
  or a 410, because an outage must not look like a removal. `Retry-After` is the API's own wait for a 429 or a 503, through `boundedRetryAfter` imported from
  `lib/job-ads/public-job-ad-answer.ts` and not copied. It is 30 seconds for a timeout, a network error, any other status, a body that fails Zod, or more than 50 000 ids. A part over 50 000
  ids writes one log line with the part and the count and nothing else, and a Zod failure logs only its kind (`security-auditor` m3 (c)). The index and the marketing part read no data and
  always answer 200.
- **The 503 carries no HTML page.** The resource is XML and its 200 is not framed either. Klas's #1477 rule that a person never lands outside the site's frame (ADR 0165 D4) governs pages
  a person lands on. Klas may override this reading.
- **`design-reviewer` is not invoked,** a named skip in the PR body: "S4 adds no rendered surface: XML only, and its refusals carry no document." The epic's rule "on every new public
  surface" exists for DESIGN.md and accessibility, and an XML file has neither. Klas may override.
- **The smoke script** (`scripts/public-job-smoke.mjs`, in the blocking `frontend` job): the index and the parts answer 200 XML with the
  pinned element set; a stub 429, 503, 500 or timeout gives a 503 with a bounded `Retry-After` and `no-store`; and the stub sees each part call at request time, so nothing was prerendered.

### D7 — One loop for the API's three samplers (CTO F1; ADR 0159 D2)

- ADR 0159 D2 deferred a shared loop to the third sampler, and a comment in `ServiceObservationService` named it. The listed-ad sampler is the third in the Api, so the loop is shared, in a
  preparatory commit of its own in this PR (`bd0f9fdc2`) that comes before any S4 behaviour. "A sampler in the API never stops the host" is one piece of knowledge, and a third copy of
  that guard would be three places to fix it (DRY at the third instance: Hunt/Thomas 1999; Fowler 2018, ch. 2, the Rule of Three and preparatory refactoring).
- **`SamplerLoop`** (`Api/Hosting/SamplerLoop.cs`, an `internal static` class with no static fields) owns control flow only: the first tick at start, the `PeriodicTimer`, the shutdown
  filter (a cancellation leaves the loop only when the stopping token is cancelled), and the nested guard around the failure report. It takes a tick and a failure callback, so each
  service keeps its own `LoggerMessage` and event id: 6224 (`HostObservationService`, logs the exception), 6232 (`ServiceObservationService`, the type name) and 6233
  (`ListedJobAdSnapshotService`, the type name).
- **A composed helper and not a base class** (architect). The existing tests call `service.Tick(Ct)` synchronously on the host service and `TickAsync` asynchronously on the services
  service. A base class with one async tick would force `.GetAwaiter().GetResult()` into the first, which AGENTS.md §3 forbids in a new form, or code that only the tests use.
- **Zero behaviour change** is shown by the two existing services' tests, which pass unchanged. `SamplerLoopTests` adds a throwing tick, the shutdown filter, a throwing report, the first
  tick before the interval, and repetition at a one-second interval.
- **It is not the only copy of the guard.** `WorkerMemoryTrendService` keeps its own, because the Worker cannot reference the Api and that service has no first tick at start. The "not
  merged" sentence in `ServiceObservationService` is deleted.

### D8 — The legal record has one home, and the merge waits for it (security-auditor M1–M3 and m1–m4; Klas's answers)

- ADR 0166 D10 trigger 1 names "the S4 sitemap", so it fires by its own text. The conclusion is withdrawn for the widened scope only, the public list of every listed address and the
  systematic discovery it produces, and the page keeps its conclusion. Klas took the conclusion again on 2026-10-18, and `security-auditor` signs. **The single home of the legal outcome
  is the ADR 0166 amendment of 2026-10-18.** This ADR points there and does not restate it (CCP, Martin 2017, ch. 13; AGENTS.md §8 point 9: one decision, one home).
- **The merge is gated.** S4 may not merge, and so may not go live (ADR 0154), until `security-auditor` has signed that amendment at her scoped re-check of the PR's final head, read with
  D1–D6 as delivered. Merging without it is a Blocker (her M1).
- **Klas's four answers, 2026-10-18,** to the escalation in `docs/reviews/2026-10-18-2090-s4-plan-security-auditor.md` (her block "Till Klas", relayed verbatim by the driving session
  through AskUserQuestion). They are quoted without edits:

  | # | His answer | What it decides | Recorded in |
  |---|---|---|---|
  | 1 | "Ja (Rek.)" | the re-take of ADR 0166 for the sitemap, as she listed it | the ADR 0166 amendment |
  | 2 | "Ja, andra tillägget (Rek.)" | a second addendum to DPIA #456 §8-B and not a new full DPIA: no high risk in the Art. 36(1) sense, so no prior consultation | the ADR 0166 amendment (D8), and the addendum, a local file written in the main copy |
  | 3 | "Ja, ingen trigger 5 (Rek.)" | S4 does not fire D10 trigger 5: it is pull only | the ADR 0166 amendment (D10) |
  | 4 | "Ja, behåll (Rek.)" | outside S4: the signed-in page keeps showing an ended ad while an erased ad answers 410 | the ADR 0106 amendment |

  Answer 4 does not decide whether S4 can merge.
- **What else the record owes, and where.**
  - The merge-day S2-P3 reading (M2): ADR 0166 D11 and the record table of `docs/runbooks/public-job-page-publication-count.md`. A validated count above 0 fires D10 trigger 2, and S4 stops.
  - The second addendum to DPIA #456 §8-B (M3): a local file, written in the main copy.
  - The processing-register row (m4): `docs/runbooks/gdpr-processing-register.md`, local, one sentence saying that from the merge date the listed ads' addresses are also published in
    a sitemap that carries `loc` only, assessed in the ADR 0166 amendment. Art. 30(1)(b) and (e) are unchanged.
  - The erasure runbook (m1): `docs/runbooks/recruiter-pii-erasure.md` §2.4a step 2 gains a second check, after the 410 and before step 3, so the numbering other records cite is
    unchanged. For every part, anonymously, it prints a status and a `grep -c` of the uuid. A reading counts only at 200 with an XML `urlset`, and on a 503 the reader waits the `Retry-After` and reads again. If the uuid is still listed at 200 sixteen minutes after
    the erasure, that is a defect: tell Klas, and file the removal anyway, because the 410 is what removes the URL. Asking a search engine to drop a URL that our own sitemap still lists
    would contradict itself (CTO in-block fix 18).
  - The D9 read-path table and the premise of the signed-in difference (m2): the ADR 0106 amendment.
- **Three pins the bound depends on** (m3), all in the delivered code: the snapshot is one immutable value swapped with a single reference write, and a test shows that a tick failing after
  its query leaves the old stamp in place (D1); `part` and `parts` come from the route file and from constants (D6); and no log line carries the id list or a response body (D1, D6).
- **No push.** S4 contacts no third party: no ping, no IndexNow, no Indexing API and no Search Console API call. Discovery is pull only, through the `Sitemap:` line in `robots.txt` and
  Klas's phase-0 submission.
- **Scope.** S4 adds no PII field, recipient, sub-processor, transfer, migration, package, compose change or ACL change (`security-auditor` verifies that at the diff). The release is image
  only, and no GO from Klas is needed (ADR 0154 §4). A diff under `deploy/`, a migration or a new package in the PR is a stop that comes back to the CTO.

### D9 — Lapse triggers

Single named home: this ADR. Named reader: Klas. Nothing detects a trigger automatically: each is checked by reading this list when the event is proposed or reported. This decision is
re-examined when:

1. Something outside the API process needs the listed set (a Worker job, a second service). The right shape is then 1d: the Worker computes the set and the API reads it from Redis
   (Alternatives).
2. Search Console reports sitemap fetch errors that the window after an API restart explains (D1: every release restarts the API).

The legal triggers are ADR 0166 D10's (1–9, with 8 and 9 added by the amendment of the same date) and have their own home there.

## What is not measured

- Google's uptake of the sitemap: whether and when it fetches the parts, and how large a share of the listed ads it indexes.
- How many aggregators read the sitemap.
- The crawl rate, until the readings in D5 are taken; and the share of 503s on job URLs, which no log on the box records (D5, the baseline), so Search Console is its only source.
- Whether Google indexes individual Platsbanken ads (`security-auditor`; it belongs in ADR 0166 W1).
- The value the registration setting has on the box (ADR 0106 amendment). The compose default is the only source read.

## Alternatives considered

### Alt A — The chosen design (1c, 2a, 3b with K = 2, D1–D7)
**For:** one publication predicate for every public surface; the age bound is held by the component that answers "listed"; the endpoint does no I/O at request time; no migration, no
package, no compose change and no new recipient; nothing is pushed; no per-ad timestamp leaves the service.
**Against:** the trade-offs listed under Consequences.

### F1 — Where the list is computed
- **1a — the web holds the list in Next's own cache.** Rejected. It has no hard age limit: a failed revalidation saves the old entry again with a fresh `lastModified`
  (`response-cache/index.js:301-316`, `file-system-cache.js:397-399`), so a failing API keeps serving the last list indefinitely, which breaks the epic's bound. The fetch cache drops
  anything over 2 MiB. The index and the parts age independently. A shared cache cannot relay a client address. ADR 0064's rejection of Variant D applies.
- **1b — a hand-written cache in the web, over a paged API.** Rejected. It moves the freshness rule into the web (DIP). The paged endpoint behind it is the 1e shape, protected only by
  a single-flight in another deployable, which a second replica, a restart loop or any other caller bypasses. N OFFSET pages are not one snapshot, and one page holding everything is an
  unpaginated fetch, which AGENTS.md §5 forbids and §12 makes a merge stop. It adds hand-written cache state in the web.
- **1d — the Worker computes the set into Redis and the API reads it (ADR 0064's Worker → Redis shape).** Rejected, though it is correct. It distributes the data for nothing: the API
  would read Redis instead of memory on every part request, and the Worker would gain a job whose only purpose is to hand the API its own data. The API is both the producer and the only
  consumer (Fowler 2002, ch. 7, the First Law of Distributed Object Design). It costs a Redis ACL template change and an escrow sitting with Klas, and until that sitting every parallel
  lane's merges stop going live (ADR 0154 §4). ADR 0158 Alternative B made the same trade the same way. It becomes the right shape if D9 trigger 1 fires.
- **1e — the API reads the database on every request.** Rejected. Every request reads the whole corpus, and the result is identical for every reader, which is the class ADR 0064 rule (d)
  moves out of the handler. On an anonymous surface each request would scan about 40 000 rows of the database that the signed-in product shares, on the one 8 GB box (ADR 0122). It is
  the only option that kept ADR 0160 D6 literal.
- **Not in the options, also rejected.** The API rendering the XML: it would put Google's vocabulary, and `SITE_URL` (a web build input), into the API, and ADR 0171 D2 puts that
  vocabulary in the web. Caddy serving a file the Worker writes: it has ADR 0158 Alternative B's costs, a compose bind, a file written on the host and a delivery freeze.
- **The sampler's read behind a port.** Rejected (architect): a repository over EF (D1).

### F2 — `lastmod`
- **2b — a new column as the source.** Rejected. It fails its own test (D2), needs a migration (epic F3), and adds work in `UpdateFromSource`, the guard tests and the erasure registry.
- **2c.** Rejected. It is wrong for an edited ad, and it contradicts ADR 0160 D4, which left `CreatedAt` out of the DTO because it would read as a `lastmod`.
- **The generation time as `lastmod`.** Rejected. Both search engines reject it.

### F3 — The layout
- **3a — parts by position in the list.** Rejected. A boundary moves with every insert or removal below it, so parts fetched hours apart skip or repeat an ad. A dynamic index would also
  need the count.
- **3c — one job file (K = 1).** Rejected. It would sit at 83 % of the cap. ADR 0064's amendment of 2026-07-13 says "marginalen är aldrig en egenskap hos designen", and 3c's 17 % headroom,
  on a corpus that moves by thousands a week, is that lesson in measured form.
- **K = 16.** Rejected. It needs 16 route folders, or a rewrite in a shared config file, and with no per-part `lastmod` the finer split buys nothing. The epic's F1 rejected 16 buckets
  because they solved an unmeasured problem (YAGNI). The margin measured since is a measured problem, and two parts is the smallest fix. This refines the epic's F1 and does not reverse it.
- **`generateSitemaps`.** Rejected. It puts the parts at `/sitemap/[id].xml`, a subdirectory, and it prerenders the ids at build time, when the build has no `BACKEND_URL`.
- **A rewrite in `next.config.ts` or `proxy.ts`.** Rejected. It adds a second internal URL per part and edits a shared file for the sake of two files.

### The wire
- **`?page=&pageSize=` returning `PagedResult<Guid>`.** Rejected. A page is a position in a moving set, but a part must be a membership: 3a's defect again, one layer down at the API.
  `TotalCount` would only serve a dynamic index, which this design does not have. Returning the whole set and letting the web split it would be an unpaginated fetch, and every part request
  would transfer all K parts.

### F4 — The budgets
- **Raising ADR 0160 D5's numbers now.** Rejected. It is a guess.
- **A separate budget for the page probe.** Rejected. It is the same raise under another name, and it would change ADR 0165 D2 with no measured need.
- **All calls in the web container's bucket.** Rejected. That happens only with a web cache, which F1 rejects.

### F5 — The record
- **Amendments alone, with no ADR.** Rejected. One decision would be scattered over five files with no home of its own. The epic's note "an ADR only in that case" was written with the
  light option in mind, a Next route over a paginated API. This design adds an in-API sampler for domain data, narrows ADR 0160 D6, departs from ADR 0064's Worker → Redis transport and
  refactors code in another area, and each of those is an architecture decision.

## Consequences

### Positive
- One publication predicate serves the sitemap. Any exclusion that Klas or `security-auditor` decides on goes into `JobAdPublication` and reaches the sitemap with no sitemap change, which is
  the remedy shape of ADR 0166's Amendment 2026-10-11.
- The age bound is held by the component that answers "listed". The web neither caches nor decides.
- In steady state the endpoint does no I/O and its cost does not grow with traffic. A failing sampler cannot stop the host.
- Membership follows the id: the index never changes, the parts never disagree with it, and no ad is skipped or repeated between fetches.
- No per-ad timestamp leaves the service, nothing is pushed to anyone, and the files carry the page's canonical address and nothing else.
- The release is image only: no compose change, no Redis ACL change, no escrow sitting, no migration and no new package.
- "A sampler in the API never stops the host" has one copy in the Api instead of three.

### Negative and accepted trade-offs (the CTO's list unless marked)
- **Every release restarts the API.** The job parts answer 503 until the first read completes, or for up to five minutes if that first read fails. This is the shape of ADR 0158's r4.
- **An ended or erased ad's address can stay in its part for at most fifteen minutes, normally five.** The page answers 410 with `no-store` throughout.
- **For the list endpoint only, ADR 0160 D6's "now" is "now within fifteen minutes"** (D1).
- **There is no Hangfire dashboard row.** A failing sampler shows as a log line per tick and as 503s within fifteen minutes. The early warning for the 50 000 cap is the doubling margin
  and the log line of event 6234, not a monitor. A corpus past 80 000 listed ads makes every tick fail at the ceiling, so the job parts answer 503 once the last good snapshot is fifteen
  minutes old, until the constant is raised in code.
- **EF's own failure lines are outside this ADR's control** (D1, architect).
- **Each API process holds its own snapshot.** There is one replica today, and more would be harmless because membership is decided by the id.
- **Without `lastmod`, Google schedules recrawls itself.** Anything fresher means the Indexing API, which is phase 2 and Klas's call.
- **An SEO PR changes admin hosting code,** through the preparatory refactor (D7).
- **Each part request regenerates about 1.8 MB of XML,** roughly the cost of a server render. The per-IP policy limits it.
- **There are four route files instead of one,** and K lives in two constants that a test holds equal (D3, architect).
- **The 503 is body-less and unframed, and `design-reviewer` is a named skip** (D6). Klas may override either, and K = 2, without a second round.
- **The legal weight of full-scale findability and of the erasure inference is not carried here.** It is in the ADR 0166 amendment, and until that is signed the merge waits (D8).

## Implementation

One PR on `feat/seo-s4-job-sitemap`, lane BE+FE: an endpoint merged on its own would be a contract with no consumer (epic F2). It adds no migration, no new package and no diff under
`deploy/`, and any of those would be a stop that comes back to the CTO. No GO from Klas is owed, because the release is image only (ADR 0154 §4). The shared loop is a preparatory commit of
its own (`bd0f9fdc2`). This ADR is promoted with `git add -f` together with its README row (precedent 0167–0173). ADR 0064, 0106, 0159, 0160, 0165 and 0166 are tracked and carry their
amendments in the same PR.

- **Backend.**
  - `Application/JobAds/Publication/JobAdPublication.cs` (`IsListed`).
  - `Application/JobAds/Listing/`: `ListedJobAdSnapshotLimits`, `ListedJobAdPartition`, `ListedJobAdSnapshot` with `IListedJobAdSnapshotReader` and `IListedJobAdSnapshotSampler`, and
    `ListedJobAdSnapshotSampler`.
  - `Application/JobAds/Queries/GetListedJobAdIds/`: `GetListedJobAdIdsQuery`, its validator and handler, `ListedJobAdPart` and `ListedJobAdIdsDto`.
  - `Api/Hosting/`: `SamplerLoop.cs`, `ListedJobAdSnapshotService.cs` and `ListedJobAdSnapshotRegistration.cs`, with `HostObservationService` and `ServiceObservationService` moved onto the
    loop. `Api/Program.cs` gains one registration line beside the two other samplers.
  - `Api/Endpoints/PublicJobAdsEndpoints.cs`, and `Api/RateLimiting/RateLimitingExtensions.cs` and `RateLimitingOptions.cs` for `public-job-ad-listed-ids`.
  - `tests/Jobbliggaren.Architecture.Tests/JobAdLifecycleReadRegistry.cs`: the sampler's read is registered as an ActiveOnly site that names `IsListed`.
- **Frontend (`web/jobbliggaren-web/src/`).**
  - `app/sitemap.ts` deleted. `app/(sitemaps)/{sitemap.xml,sitemap-sidor.xml,sitemap-jobb-1.xml,sitemap-jobb-2.xml}/route.ts`, all `force-dynamic`.
  - `lib/seo/sitemap-xml.ts`, `lib/seo/marketing-sitemap.ts`, `lib/seo/job-sitemap.ts` and `lib/seo/sitemap-responses.ts`.
  - `lib/api/public-job-ad-ids.ts` (per request, uncached, a 5-second timeout) and `lib/dto/public-job-ad-ids.ts`.
  - `lib/job-ads/public-job-ad-url.ts` (`publicJobAdUrl`), used by the page's canonical and by every `loc`.
  - The stale mentions of `app/sitemap.ts` in `lib/site-url.ts` and `app/robots.ts` are corrected, and the three older tests that called `sitemap()` point at the new builder.
  - `scripts/public-job-smoke.mjs`, whose step in the `frontend` job of `.github/workflows/build.yml` now names the sitemap (a comment only).
- **Tests.**
  - Application.UnitTests: `JobAdPublicationIsListedAgreementTests`, `ListedJobAdPartitionTests`, `ListedJobAdSnapshotTests`, `ListedJobAdSnapshotLimitsTests`,
    `GetListedJobAdIdsQueryHandlerTests` and `GetListedJobAdIdsQueryValidatorTests`. The agreement test builds each reachable state through a production path and declares the cells that
    no path in `src/` produces as unreachable, asserting only that both forms exclude them (AGENTS.md §5, Tests; architect).
  - Api.IntegrationTests: `ListedJobAdSnapshotSamplerTests` (Testcontainers Postgres, never InMemory, because the offset trap of ADR 0064's Amendment 2026-07-28 (II) appears only on real
    Postgres), `PublicJobAdListedIdsEndpointTests`, `PublicJobAdListedIdsRateLimitWiringTests`, `PublicJobAdListedIdsRateLimitTests`, `ListedJobAdSnapshotServiceTests`,
    `ListedJobAdSnapshotRegistrationTests` and `SamplerLoopTests`, with a row each in `RateLimitingOptionsTests` and `ForwardedClientBudgetTests`.
  - Architecture.Tests: `ListedJobAdSnapshotBoundaryTests` (Api-only composition, three facts; the web's K equals the API's; the web schema's keys equal the DTO's).
  - Web: `sitemap-routes.test.ts`, `sitemap-xml.test.ts`, `job-sitemap.test.ts`, `public-job-ad-ids.test.ts` (api and dto), and the three older tests that called `sitemap()`.
- **Docs in the same PR.** `docs/runbooks/recruiter-pii-erasure.md` §2.4a (the sitemap check, D8), and the endpoint line in `BUILD.md` and the enumeration paragraph in
  `docs/threat-model.md`, both of which name this ADR.
- **Tests run on 2026-10-18** (the driving session's; none re-run for this ADR). Application unit, the new classes plus `JobAdPublicationTests`: 83/0. Api integration, the new and adjacent
  classes: 79/0, which includes `ListedJobAdSnapshotSamplerTests` on Testcontainers Postgres (the exact set, the deadline instant ±1 µs, the ceiling, a failed read keeping the stamp, the
  measured zero, and an older read never replacing a newer one). Architecture: 844/0. Web, the touched files: 263/0. `next build` is green, with the four routes dynamic. Smoke: 28/28.
- **Taken before the merge, 2026-10-18.** The baseline in D5, and the merge-day S2-P3 reading (ADR 0166 Amendment 2026-10-18 (ix) and the publication-count runbook's record table).
- **Owed outside the diff.** The second DPIA addendum and the processing-register sentence, both local files in the main copy; Klas's two Search Console readings; and the reading dates in
  the phase-1-close row of #2090.

## References

- Code: `src/Jobbliggaren.Application/JobAds/` (`Publication/`, `Listing/`, `Queries/GetListedJobAdIds/`) · `src/Jobbliggaren.Api/Hosting/` (`SamplerLoop`, `ListedJobAdSnapshotService`,
  `ListedJobAdSnapshotRegistration`) · `src/Jobbliggaren.Api/Endpoints/PublicJobAdsEndpoints.cs` · `src/Jobbliggaren.Api/RateLimiting/` · `web/jobbliggaren-web/src/lib/seo/` ·
  `web/jobbliggaren-web/src/app/(sitemaps)/` · `web/jobbliggaren-web/scripts/public-job-smoke.mjs`
- Tests: as listed under *Implementation*; the boundary test is `tests/Jobbliggaren.Architecture.Tests/ListedJobAdSnapshotBoundaryTests.cs`.
- `docs/reviews/2026-10-18-2090-s4-plan-cto.md` · `docs/reviews/2026-10-18-2090-s4-plan-security-auditor.md` · `docs/reviews/2026-10-18-2090-s4-plan-dotnet-architect.md` (all three
  transcribed verbatim by the driving session; local) · `docs/runbooks/recruiter-pii-erasure.md` §2.4a · `docs/runbooks/public-job-page-publication-count.md`
- ADR 0064 (rule (d); the amendments of 2026-07-13, 2026-07-28 (II) and 2026-10-10) · ADR 0106 (D9) · ADR 0113 · ADR 0120 · ADR 0122 · ADR 0154 (§3, §4) · ADR 0158 (Alternative B, r4) ·
  ADR 0159 (D2, D3) · ADR 0160 (D1–D6) · ADR 0165 (D2, D3, D4, D6) · ADR 0166 (D2, D4, D5, D7, D8, D10, D11 and its amendments) · ADR 0171 (D2) · ADR 0050 (Amendment 2026-08-04)
- AGENTS.md §2.1, §3, §3.6, §5, §8, §12 · CLAUDE.md §9.2, §9.6
- Martin, *Clean Architecture* (2017), ch. 11 (DIP), 13 (CCP) and 22 · Fowler, *Patterns of Enterprise Application Architecture* (2002), ch. 7 (the First Law of Distributed Object
  Design) · Fowler, *Refactoring*, 2nd ed. (2018), ch. 2 (the Rule of Three; preparatory refactoring) · Hunt/Thomas, *The Pragmatic Programmer* (1999), DRY · Nygard, *Release It!*, 2nd ed.
  (2018), ch. 5 · Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017), ch. 2 · Saltzer & Schroeder (1975), least common mechanism · The Twelve-Factor App, VI
- Google Search Central, read 2026-10-18 (the date on each page is its own last update): *Build and submit a sitemap* (2026-07-08) · *Reduce the Googlebot crawl rate* (2026-10-06). Read by
  the session on 2026-10-17: *Manage large sitemaps* (2025-12-10) and *Job posting* (2026-09-08). These pages change; the dates say what was read.
- Next 16.3.8: `docs/01-app/03-api-reference/03-file-conventions/dynamic-routes.md:18` · `docs/01-app/03-api-reference/04-functions/generate-sitemaps.md:20` ·
  `server/response-cache/index.js:301-316` · `server/lib/incremental-cache/file-system-cache.js:393-399`
- #2090 · #2167 · #2121 · #1477
