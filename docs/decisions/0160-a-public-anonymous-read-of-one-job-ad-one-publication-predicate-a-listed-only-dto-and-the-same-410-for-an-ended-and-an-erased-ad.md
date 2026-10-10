# ADR 0160 — A public anonymous read of one job ad: one publication predicate, a listed-only DTO, and the same 410 for an ended and an erased ad

**Date:** 2026-10-10
**Status:** Accepted
**Deciders:** Klas Olsson (product owner; the epic directive of 2026-10-10, and his acceptance of the veto in D3, "Ja,
samma 410 (Rek.)") · `senior-cto-advisor`, "the CTO" below (decision-maker under CLAUDE.md §9.2; ruling on forks F1–F12,
report-only; `docs/reviews/2026-10-10-2091-s1-forks-cto.md`, local) · `security-auditor` (pre-implementation read,
report-only, with one GDPR Blocker veto against the CTO's F4;
`docs/reviews/2026-10-10-2091-s1-plan-security-auditor.md`, local)
**Amends:** ADR 0064 (Beslut (d): a fifth complementary leg) and ADR 0106 (the read-path table of D9: one row), each by
a dated amendment at the end of that file. Nothing is rewritten.
**Related:** ADR 0064 (the only anonymous read before this one: the landing aggregate) · ADR 0106 (recruiter erasure,
the neutral 410) · ADR 0087 D8(c) (the company name is shown, the organization number never) · ADR 0043 (taxonomy
behind a port) · ADR 0042 Beslut C (a dedicated rate-limit policy per surface) · ADR 0053 (the job route; S2 will amend
it) · ADR 0154 (every merge goes live) ·
[#2090](https://github.com/klasolsson81/jobbliggaren/issues/2090) (the epic) ·
[#2091](https://github.com/klasolsson81/jobbliggaren/issues/2091) (S1, the issue this ADR ships under) · #2092 (S0, the
brand search result, in parallel) · [#2098](https://github.com/klasolsson81/jobbliggaren/issues/2098) · #841 · #734
**Measured against:** `origin/main` at `6a0cc4a31`, 2026-10-10. D1–D8 describe what the branch `feat/public-job-ad-read`
builds. **D9 is decided and not built.**

> **Provenance.** Written by `adr-keeper` for the driving session, from its brief (CLAUDE.md §9.2, §13). The decision is
> the CTO's ruling as changed by `security-auditor`'s veto, which Klas accepted; the mandatory agents review the
> implementation in the PR. The test and mutation facts in the Implementation section are dated and describe the branch
> before merge.

---

## Context

Epic #2090 (Klas directive, 2026-10-10; a parallel lane, not `mvp`) makes job ads indexable. S1 (#2091) is backend only:
the rule for what is public, a structurally anonymous DTO and one anonymous endpoint. It has no FE, route, robots,
sitemap, migration or list endpoint.

At `6a0cc4a31`, `/api/v1/job-ads` is `.RequireAuthorization()` and no `AllowAnonymous` exists in the Api. The one
anonymous read is ADR 0064's aggregate, `GET /api/v1/landing/stats`. A public read of **one record** is a new class: it
returns text that can name a person (a sole trader as employer, a recruiter in free text), and an erased ad must stay
erased on it.

The endpoint has no external surface until S2: Caddy proxies only `web:3000`, `api` publishes no ports and Next has no
catch-all proxy (`security-auditor` measured). But every merge goes live (ADR 0154), so the decisions below hold from
the first merge, and D9 lists what must hold before S2, whose merge is the go-live of the public page.

Two facts shape the predicate.

- **`JobAd.Source` is user-settable.** `POST /api/v1/job-ads` accepts `Source = "Platsbanken"` and stores an ownerless,
  unscrubbed row with `External == null`. Only `JobAd.Import` writes `External`.
- **The bulk archivers raise no per-ad events** (`ExpireJobAdsJob`, the snapshot-miss tracker), so a deadline has to be
  evaluated when the ad is read.

## Decision

**`GET /api/v1/public/job-ads/{id}` answers one ad from one publication predicate.** A Listed ad is a 200 with a narrow
`PublicJobAdDto`. An ended ad and an erased ad get the identical 410. An ad the public surface never held and an unknown
id get the identical 404.

### D1 — The class and the route

`PublicJobAdsEndpoints` (`Api/Endpoints/`) has its own `MapGroup`, outside the authenticated group. It is anonymous by
omitting `RequireAuthorization`; no fallback policy exists, so the omission is what makes it so. `GetPublicJobAdQuery`
is deliberately not `IAuthenticatedRequest`. The handler projects a narrow row and never materialises `JobAd`, because
`RawPayload` carries the organization number and the text from before the scrub. It is registered `AnyStatus`, with its
reason, in `JobAdLifecycleReadRegistry`.

### D2 — One publication predicate, and its anchor

`JobAdPublication.Classify(JobAdStatus status, DateTimeOffset? expiresAt, JobSource? importedFrom, DateTimeOffset now)`
(`Application/JobAds/Publication/`) returns a `JobAdPublicationState`. It takes primitives rather than a `JobAd`, so the
handler can pass what a narrow projection read. The checks run in this order:

| Condition | State | Public answer |
|---|---|---|
| The ad was not imported from the published source (`importedFrom != JobAdPublication.PublishedSource`), whatever its status | `NotPublic` | 404 |
| `Status` is Erased | `Gone` | 410 |
| `Status` is not Active, or `ExpiresAt < now` | `Ended` | 410 |
| otherwise (Active, `ExpiresAt` null or `>= now`) | `Listed` | 200 |

- **The anchor is the import provenance, never `JobAd.Source`.** An ad is publishable only if
  `External?.Source == JobSource.Platsbanken`: one equality, held as `JobAdPublication.PublishedSource`. Each source
  needs its own licence reading (D8), so widening it is an amendment to this ADR. A manual row has no owner, no Tier A
  scrub (ADR 0106) and no CC0 basis; publishing it would be hosting user content, which is a different kind of
  processing.
- **The boundary is the archiver's.** `ExpireJobAdsJob` archives `ExpiresAt < now`, so an ad is Ended iff
  `ExpiresAt < now` and still Listed at the instant `ExpiresAt == now`. The text of the issue and the epic said
  `> now`; it was wrong by one instant and was corrected by comment on #2091 and #2090.
- **Shape: the classifier only, for now.** The SQL form, `IsListed(now)`, is added **in the same type** by the first
  list consumer (the S4 sitemap, the S5 catalogue or S6's related ads), with a test that it agrees with `Classify` in
  every state and a Testcontainers test that it translates. Rejected: a `Specification<JobAd>`, whose base class is
  sealed and would recompile for every `now`, and a SQL expression now, which has no consumer.
- **Other readers keep their own selects.** Search, the landing stats and `ExpireJobAdsJob` change for the signed-in
  product's reasons. `ExpireJobAdsJob` is a write path with its own resurrection pin, and sharing a fragment would need
  a combinator (LinqKit is off BUILD.md §3.1). The agreement between the archiver and the classifier is pinned by a
  test instead (Implementation).
- **A recorded window.** An ad can be Active in signed-in search and Ended publicly until the next `ExpireJobAdsJob` run
  (03:45 daily). S2's overlay fork owns that gap, so no issue is filed.

### D3 — The answers, and why an ended ad and an erased ad are one 410

- **Listed:** 200, `PublicJobAdDto`.
- **Ended and Gone: the identical 410.** `JobAdErrors.Gone` (`JobAd.Gone`, "Annonsen är inte längre tillgänglig."), the
  same headers, and no identity of the ad.
- **NotPublic and an unknown id: the identical 404** (`JobAdErrors.NotFound`), Erased or not. The public surface never
  held such a row, a 410 would claim it once existed there, and one answer for both misses gives no existence oracle.
- **A backend failure is a 5xx**, never a 404 or a 410.
- **`Application/JobAds/JobAdErrors.cs` is the one source of both answers.** `GetJobAdQueryHandler`,
  `GetJobAdMatchDetailQueryHandler`, `CreateApplicationFromJobAdCommandHandler` and the new handler read from it, so one
  ad gets one answer on every route. An integration test compares the public 410 with the signed-in detail's 410 for the
  same erased ad.

*Why the same 410 (the veto).* The CTO's first ruling (F4) was a 200 for an ended ad, with its title, company and dates.
`security-auditor` vetoed it as a GDPR Blocker (Art. 5(1)(a), (c) and (e); Art. 12(2); Art. 17), for two reasons.

1. **Erased is reached only through `EraseRecruiterAdsCommand`, which is an Art. 17 request.** A public 410 beside a
   public 200-Ended would tell any reader that someone asked for this ad to be erased. The listed DTO carries `Url`, and
   so Arbetsförmedlingen's ad id; a crawler that saw the ad while it was Listed keeps the join between our uuid and that
   id. Against Arbetsförmedlingen's open Historiska annonser dataset, at the scale search engines work at, that becomes a
   passive and permanent list of erased ads. It is the very signal ADR 0106 kept out of the 410 body: the erasure would
   broadcast the fact it exists to protect. The status code carries the same bit as a body would.
2. **A tombstone would keep a name public for as long as the row lives.** Archived rows are never hard-deleted
   (`RetainPlatsbankenJobAdsJob` and `ExpireJobAdsJob` only archive), so a tombstone with `Title` and `Company.Name`
   publishes a sole trader's personal name indefinitely.

On the signed-in surface the difference was accepted by ADR 0106, because only signed-in users can see it (and
registration is closed, #734). A public, crawlable surface changes who the audience is. **Klas accepted
the veto on 2026-10-10:** "Ja, samma 410 (Rek.)". The product consequence is his: a shared link to an ended ad shows
only the neutral 410 text in public, with no title and no company. Signed-in users still see the ended ad, and ADR
0106's acceptance for the signed-in surface is not reopened.

*A measured limitation, not a claim that the two are indistinguishable.* During an erased ad's remaining time to its
`ExpiresAt`, an observer who actively compares with Arbetsförmedlingens live API can see 410 here and the ad live there.
That follows from holding a copy and erasing it on request. This ADR records the limitation; it is not an acceptance
under CLAUDE.md §9.6 (3) and withdraws no finding.

### D4 — The PII boundary

`PublicJobAdDto(Id, Title, CompanyName, Description, Url, PublishedAt, ExpiresAt, MunicipalityConceptId, RegionConceptId,
OccupationGroupConceptId, EmploymentTypeConceptId, WorktimeExtentConceptId)`. `PublicJobAdDtoContractTests` pins the exact
member set, and an integration test pins the member set of the JSON on the wire.

- **Never carried:** user state, match data, the `Contacts` block, `OrganizationNumber`, `RawPayload`, `ExtractedTerms`.
- **Left out on purpose:** `Remote` (its fail-safe `false` is not a fact, and no TELECOMMUTE may be invented),
  `SsykConceptId` (no phase-1 consumer), `Source` (a constant under D2) and `CreatedAt` (it would read as a `lastmod`,
  and there is no trustworthy one).
- **The company name is shown, the organization number never** (ADR 0087 D8(c)). `IsPersonnummerShaped` is a declared
  non-primary heuristic, and ADR 0087 D8 refused to reshape or exclude a value on it; this ADR does not reopen that.
- **`Description` is the stored, Tier A-scrubbed plain text as it is.** A public-only redaction at read time would be a
  second normaliser on one read path, and ADR 0106 D4 puts the scrub in the aggregate.
- **`Url` is public:** http(s) only, the Arbetsförmedlingen ad page. It is what makes the scrub marker's "se annonsen hos
  Arbetsförmedlingen" honest.
- **No log line on the new path carries `Title`, `Company` or `Description`.**

Two residuals, stated and not closed here.

- **Recruiter names in free text.** ADR 0106 (Amendment 2026-07-13, A1) measured that about 22 % of ads carry the contact
  only in the free text. D5 of that ADR refused name NER and ADR 0071 forbids an LLM, so the remedy is whole-ad Tier B
  erasure.
- **A personnummer in the title or the description.** A sole trader's organization number is their personnummer (#841)
  and can be written in the text. The Tier A phone pattern anchors only on `0` or `+46`, and no personnummer pass runs at
  import. The signed-in detail shows the text as stored, so the gap is in delivered code; how many stored ads
  carry one is not measured. This is defect #2098, and an S2 precondition (D9).

### D5 — The rate limit

`PublicJobAdReadPolicy` (`"public-job-ad-read"`) is a TokenBucket partitioned **per client IP**, derived the same way as
`LandingPublicRead`'s partition: 60 per 60 s in 6 segments (`RateLimitingOptions.PublicJobAdRead`, `IOptions`),
`QueueLimit` 0, and a 429 with `Retry-After`. The partition is never copied from a per-user policy: those give an
anonymous caller `NoLimiter`, which would leave the endpoint unlimited. `ListReadPolicy` (anonymous means unlimited)
and `LandingPublicReadPolicy` (another surface's budget, ADR 0064 Beslut (b)) are not reused. `security-auditor`
approved the numbers; they are raised later from measured crawl rates, not guessed.

- **It protects availability, not confidentiality.** Ids are random GUIDs, but the corpus becomes enumerable through
  S4's sitemap, by design.
- **The client IP is real only when the caller relays Caddy's single trusted `X-Forwarded-For`** (Next's
  `forwardedHeaders()`; `KnownNetworks` is the internal web-api subnet). A call without it shares the web container's one
  bucket. Accepted for S1, which has no external surface.
- **If an AAAA record is ever added for the apex** (none was measured on 2026-10-10), IPv6 must be partitioned per /64.
- **The test factory raises the policy to 10 000**, like the other IP policies. The production numbers are pinned by a
  behavioural 429 test in the `StrictRateLimit` collection, and an endpoint-graph test pins that the policy sits on this
  one route.

### D6 — Cache: `no-store` on everything under `/api/v1/public`

Every answer under the prefix (200, 400, 404, 410, 429, 5xx) carries `Cache-Control: no-store`. It is set in the existing
path-prefix `OnStarting` block of `Program.cs`, so refusals the framework writes carry it too. The erasure rule is
absolute: any `max-age` is a window in which a stale answer can still say Listed. The API is the authoritative answer to
"now". Whether the HTML page is cached is S2's decision, under the same rule. The prefix does not reach
`/api/v1/landing`, so ADR 0064's `public, max-age=30` stands.

### D7 — Labels are concept ids

The five dimensions leave as concept ids (ADR 0043), and S1 injects no `ITaxonomyReadModel`. S3 decides per dimension
whether a value is coded or named (#1537).

### D8 — The licence, read from primary sources on 2026-10-10

- **Sveriges dataportal**, the dataset "Platsannonser" (Arbetsförmedlingen),
  https://www.dataportal.se/datasets/180_11042. Its DCAT metadata,
  https://admin.dataportal.se/store/180/metadata/11042, carries
  `dcterms:license <http://creativecommons.org/publicdomain/zero/1.0/>` (CC0 1.0) and access rights PUBLIC, and the Job
  Search API (https://jobsearch.api.jobtechdev.se/#distributionJobsearch) is one of the dataset's distributions.
- **Arbetsförmedlingen's own catalogue page**, https://data.arbetsformedlingen.se/dataset/job-ads/, states "Licens:
  Creative Commons CC0" on each of its distributions, Job Search API included.
- **Its advertiser terms** (valid from 2025-12-01),
  https://arbetsformedlingen.se/for-arbetsgivare/rekrytera/annonsera-i-platsbanken/for-over-annonser-till-platsbanken/connect-your-recruitment-tool-to-platsbanken/advertising-conditions,
  tell advertisers that personal data in a published ad may appear with other actors that fetch and show ads through the
  agency's open interface ("hämta och visa upp annonser via Arbetsförmedlingens öppna standardiserade gränssnitt"), and
  require the publisher to have the contact persons' consent.

CC0 waives copyright and database rights. It does not discharge GDPR: the personal data in an ad stays personal data,
and D4 and D9 are what this ADR does about it. `jobtechdev.se` itself did not resolve from the session; the dataportal
metadata is the primary record.

### D9 — What must hold before S2 merges

S2 is the public job page, and its merge is its go-live (ADR 0154). Each of these is a precondition of that merge.

1. **#2098 is merged, its re-scrub backfill has been run on the box, and the result is measured:** a count of
   personnummer-shaped tokens in the title and the description of listed ads, counts only (CLAUDE.md §9.2 (a)). A merge
   is not a run. A sole trader's personnummer on a public, indexed page does not meet dataskyddslagen 3 kap. 10 § and is
   Blocker-class for S2.
2. **The disclosure is amended**, in the existing texts and with no new notice: the recruiter notice's stated purpose,
   the processing-register row ("visning … i Jobbliggaren-katalog"), the legitimate-interest assessment, which must weigh
   that recruiter names in free text become findable through a search engine, and the Art. 6(4) compatibility reasoning.
   Until then the notice says the purpose is the user's own follow-up, and S2's go-live would make it false (Art.
   5(1)(a), 12(1)).
3. **The recruiter-erasure runbook gains an Art. 17(2) step:** reasonable steps towards search engines for the erased ad's
   public URL, since the data has then been made public by us.
4. **S2 renders the public read per request with `forwardedHeaders()`,** never ISR or static rendering for this fetch,
   with one API call per render, and tests both.
5. **An API 429 becomes a page 503 with `Retry-After`, and an API failure a 5xx.** Never a 404 or a 410.
6. **The description is rendered as plain text, never as HTML.** Rendering it as HTML would be an XSS Blocker.
7. **An ended ad gets the same 410 page as an erased ad** (D3). The epic's wording for S2 (a truthful `noindex` page for
   an ended ad) predates the veto.

## Alternatives considered

- **A 200 tombstone with title and company for an ended ad (the CTO's F4).** Rejected by the veto (D3).
- **Reuse `GetJobAdQuery` and `JobAdDetailDto` anonymously.** Rejected: they carry the recruiter contact block and user
  state.
- **Anchor on `Source == Platsbanken`.** Rejected: `Source` is user-settable (D2).
- **Publish manual ads as well.** Rejected: no owner, no scrub, no CC0 basis (D2).
- **Reuse `ListReadPolicy` or `LandingPublicReadPolicy`.** Rejected: the first is unlimited for anonymous callers, the
  second is another surface's budget (D5).
- **A public `max-age`.** Rejected: the erasure window (D6).
- **Resolve taxonomy labels now.** Rejected: no consumer in S1 or S2 (D7).
- **Ship the SQL predicate now.** Rejected: no consumer (D2).
- **A `Specification<JobAd>`.** Rejected: the base class is sealed and would recompile per `now` (D2).

## Consequences

### Positive

- One definition of "public" for the endpoint and every later surface (the page, `JobPosting`, the sitemap, the
  catalogue), which is the epic's condition on F1.
- The public body cannot carry what it must not: the DTO is a pinned member set, and `Contacts`, the organization number
  and `RawPayload` never leave the handler's projection.
- An erasure is not broadcast by a status code or a body. An ended ad and an erased ad are one answer.
- One ad gets one answer on every route.
- No migration, dependency or new recipient of personal data in S1.

### Negative and risks

- **The recorded window** between signed-in search and the public read stays open until S2's overlay fork resolves it
  (D2).
- **A shared link to an ended ad loses its title in public** (D3, Klas's acceptance).
- **The 410-versus-live comparison** during an erased ad's remaining deadline window (D3).
- **Two residuals reach the public page in S2:** recruiter names in free text, with whole-ad erasure as the only remedy,
  and a personnummer in the text until #2098 is run (D4, D9).
- **The predicate has other copies that stay.** Search, the landing stats and `ExpireJobAdsJob` keep their own selects
  (D2). The drift is bounded by the deadline-agreement test and by the first SQL consumer's duty to add `IsListed(now)`
  here with its agreement test.
- **Without a relayed `X-Forwarded-For` every caller shares one bucket** (D5).
- **The licence reading is dated.** `jobtechdev.se` did not resolve from the session, so the dataportal metadata is the
  primary record (D8).

## Implementation

Ships under #2091 in one PR, with this ADR and the two amendments (CLAUDE.md §1.5). It adds no migration, dependency, FE,
route, robots or sitemap change.

- `Application/JobAds/Publication/{JobAdPublication,JobAdPublicationState}.cs` ·
  `Application/JobAds/JobAdErrors.cs` ·
  `Application/JobAds/Queries/GetPublicJobAd/{GetPublicJobAdQuery,GetPublicJobAdQueryHandler,GetPublicJobAdQueryValidator,PublicJobAdDto}.cs`
- `Api/Endpoints/PublicJobAdsEndpoints.cs` · `Api/Program.cs` (the map and the `no-store` prefix) ·
  `Api/RateLimiting/{RateLimitingExtensions,RateLimitingOptions}.cs`
- `JobAdLifecycleReadRegistry` (`AnyStatus`, with its reason). The three existing handlers read their 404 and 410 from
  `JobAdErrors`.

*Validation, 2026-10-10, before merge.* Unit tests for the classifier (a frozen clock; the deadline at `now`, one tick
before and one after; every status; a never-imported row in every status), the handler, the validator and the DTO member
pin. Testcontainers tests: a 200 with the exact wire members; an archived, a past-deadline and an erased ad answering one
identical 410; an API-created `Source = "Platsbanken"` row and an unknown id answering one identical 404; `no-store` on
200, 400, 404, 410 and 429; the signed-in group still 401 anonymously; the public and the signed-in 410 agreeing; the
deadline agreeing with `ExpireJobAdsJob` at the instant and one microsecond after (whole-microsecond values, because
`timestamptz` resolves to the microsecond); the rate-limit wiring and a burst to 429.
*Mutation-checked, 2026-10-10:* `<` to `<=` kills the at-instant test; dropping the provenance anchor kills six NotPublic
tests; removing the `/api/v1/public` prefix kills four `no-store` tests.

## References

- Code: `src/Jobbliggaren.Application/JobAds/` (`Publication/`, `JobAdErrors.cs`, `Queries/GetPublicJobAd/`) ·
  `src/Jobbliggaren.Api/Endpoints/PublicJobAdsEndpoints.cs` · `src/Jobbliggaren.Api/RateLimiting/` ·
  `tests/Jobbliggaren.Architecture.Tests/JobAdLifecycleReadRegistry.cs`
- `docs/reviews/2026-10-10-2091-s1-forks-cto.md` · `docs/reviews/2026-10-10-2091-s1-plan-security-auditor.md` (both
  local) · `docs/runbooks/recruiter-pii-erasure.md`
- ADR 0064 (Amendment 2026-10-10), 0106 (Amendment 2026-10-10), 0087, 0071, 0053, 0043, 0042, 0154
- Epic #2090 · #2091 · #2092 · #2098 · #841 · #734 · #1537
- The licence sources are listed in D8.
