# Performance Audit — Jobbliggaren

> **Date:** 2026-07-10 · **Scope:** entire codebase (API, Worker, data layer, frontend,
> perceived performance, measurement readiness) · **Mode:** READ-ONLY — this report is
> the only file the audit produced.
>
> **Method:** plan-approved workflow fan-out — 8 dimension finders (Fable 5), one
> adversarial verifier per finding (default stance: the finding is WRONG until its
> evidence survives a code read), a completeness critic, and a second adversarial pass
> over the critic's gap candidates. 62 raw findings → **56 kept** (51 CONFIRMED,
> 5 WIP-DEPENDENT), 6 killed: 4 refuted (two because an accepted ADR already records
> the trade-off) + 2 duplicates of existing TDs. Every kept finding carries `file:line` evidence that a
> verifier re-read, plus the verifier's corrections. All finder/verifier agents
> self-reported `claude-fable-5` (schema-enforced).
>
> **Severity calibration (binding, from the approved audit plan):**
> **P0** = user-visible jank on core flows at CURRENT scale (spinner-rule violations on
> primary navigation are P0 by definition). **P1** = ADR 0045 budget breach on a hot
> path, or a systemic per-request amplifier. **P2** = measurable but within budget;
> measurement-readiness gaps. **P3** = scale-hypothetical/latent. Hard rules: job_ads
> won't 10x → corpus-growth-dependent ≤ P2; company_register read path = WIP + P3.
> Effort: S < 0.5 day, M 0.5–2 days, L > 2 days.
>
> **Measurement anchor:** ADR 0045 locked budgets — API p95 read/list 300 ms ·
> typeahead 150 ms · write 400 ms · ingestion ≥ 200 jobs/min · LCP < 2.5 s · CLS < 0.1 ·
> INP < 200 ms · Worker soft cap 512 MiB. All gates observe-only in Fas 1.

---

## 1. Sammanfattning (svenska)

**Helhetsbild:** Jobbliggaren har en ovanligt frisk prestandagrund. Auditens
verifierare kunde INTE hitta N+1-frågor, blockerande `.Result`/`.Wait()`, saknad
`AsNoTracking`-hygien, oindexerad typeahead eller HttpClient-slarv — de klassiska
felen är redan undvikna, och tidigare incidenter (ADR 0032 minne, ADR 0042 suggest,
ADR 0062 q-COUNT) är åtgärdade och icke-regresserade. Problemen som finns är i
stället strukturella och koncentrerade till **upplevd prestanda och mätbarhet**.

**Största problemet (P0):** *navigations-döda klick.* Ingen enda primär route i
appen har `loading.tsx` — varje klick i huvudnavigationen låter gamla sidan stå
frusen tills serverns fulla svar kommit (på /oversikt: åtta backend-anrop). Det är
exakt anti-mönstret "klick → ingenting händer → innehållet dyker upp". Eftersom
`(app)`-layouten dessutom väntar på session + statistik finns inget statiskt skal
som kan förhandsladdas. Fixen är mekanisk: skelett-`loading.tsx` per segment.

**Näst största (P1):** hela i18n-katalogen (~163 kB rå svenska, 21 namespaces)
serialiseras in i VARJE sidas payload, inklusive juridiktexter som bara två
marknadssidor använder — en systemisk per-request-förstärkare. Samt: /jobb:s
Suspense-gräns hjälper bara sökningar inne på sidan; första navigeringen blockerar
ändå på fyra hero-beroenden.

**Tredje temat (P2):** små serialiseringar på renderingsvägen — `/matchningar` och
/jobb-resultaten inväntar en "sett"-markering (en WRITE) innan de målar;
/cv och /oversikt kör en femte/nionde fetch seriellt efter sina parallella; ett
annonsöppnings-flöde kör 3–4 seriella steg fast stegen bara behöver annons-id:t.
Allt detta är S-fixar (`after()`, flytta in i `Promise.all`).

**Mätbarhet:** ADR 0045 låser fyra budgetklasser men bara klass (a) har något
instrument alls — typeahead (150 ms), writes (400 ms), ingestion (200 jobb/min) och
Worker-minnet (512 MiB) är helt omätta, och Lighthouse-CI mäter bara startsidan.
Auditens mätplan (avsnitt 5) bygger uteslutande på instrument som redan finns i repot.

**Snabba vinster:** 41 av 56 fynd är S-effort, varav ~10 ger direkt användarupplevd
effekt (loading-skeletons, `after()`-flyttar, waterfall-parallellisering,
browse-sort-index). **Databasen är INTE flaskhalsen i dag** — korpusen är ~54k rader
och växer inte 10x; de flesta DB-fynden är P2/P3 av just det skälet.

**Budgetstatus mot ADR 0045:** ingen bekräftad budgetöverträdelse i befintliga
mätningar — men fyra av sju budgetklasser saknar instrument, så "inom budget" är
delvis ett omätt påstående. Det är auditens viktigaste icke-kod-slutsats.

---

## 2. Snabba vinster (quick wins — hög effekt, låg insats)

Ranked by user-perceived impact. All are S-effort unless noted.

| # | Finding | Fix in one line |
|---|---------|-----------------|
| 1 | `p1-no-loading-tsx-any-primary-route` (P0, M total — S per segment) | Skeleton `loading.tsx` per primary segment; start with /jobb, /oversikt, /ansokningar, /cv |
| 2 | `p3-jobb-initial-nav-hero-deps-block` (P1) | `loading.tsx` for /jobb reproducing hero plate + list skeleton |
| 3 | `p2-matchningar-markseen-blocks-render` (P2) | `after(() => markMatchesSeen())` instead of awaiting on the render path |
| 4 | `p2-jobb-results-markjobsseen-await` (P2) | Same `after()` pattern for `markJobsSeen()` |
| 5 | `p2-cv-serial-skill-resolve-waterfall` (P2) | Chain `resolveSkillLabels` off `getMyProfile` inside the `Promise.all` |
| 6 | `p2-oversikt-9th-serial-taxonomy-fetch` (P2) | Start `getTaxonomyTree()` unawaited alongside the 8-way fan-out |
| 7 | `d1-jobads-browse-sort-no-index` (P2) | Partial index `(published_at DESC, id) WHERE status='Active' AND deleted_at IS NULL` |
| 8 | `d2-bitmap-count-always-transacted` (P2) | Gate the `SET LOCAL` count transaction on `Q` being non-blank |
| 9 | `g2-ef-command-logging` (P2) | `"Microsoft.EntityFrameworkCore.Database.Command": "Warning"` in both hosts |
| 10 | `b5-nine-static-font-files-vs-variable` (P2) | `weight: "variable"` in both `next/font` calls → 2 files instead of 9 |
| 11 | `b6-lighthouse-asserts-only-root` / `m6` (P2) | Add public + guest URLs to `lighthouserc.json` collect list |
| 12 | `d1-table-row-rerender` + `d2-pipeline-keystroke-fanout` (P2) | `React.memo` rows/queue/rail + `useCallback` + memoized rows array |
| 13 | `m4`/`w6` ingestion rate (P2) | One-line `jobsPerMinute` field in both sync jobs' completion logs |
| 14 | `m5`/`w2` worker memory (P2) | Periodic `WorkingSet`/`GC.GetTotalMemory` trend log vs 512 MiB |
| 15 | `m7-no-seq-p95-query-artifact` (P2) | Commit the Seq p95-per-handler query snippet to a runbook |

---

## 3. Findings table

56 findings (51 CONFIRMED + 5 WIP-DEPENDENT), plus one extra row: the residual
kernel of the refuted g3 claim (marked "residual kernel" — not counted among the 56).
"WIP" = flagged WIP-dependent (feature-dark or dormant code path — not a defect in
shipped behavior). Full details in section 4.

| ID | Area | File/location | Sev | User impact | Effort | WIP |
|----|------|---------------|-----|-------------|--------|-----|
| p1-no-loading-tsx-any-primary-route | Perceived/navigation | `(app)/oversikt/page.tsx:27,47-81` (+ all 12 primary segments) | **P0** | Every primary navigation is a dead click until full RSC render | M | No |
| b1-full-i18n-catalog-hydrated | Bundle/hydration | `src/i18n/request.ts:29-37` | **P1** | ~163 kB extra Flight payload on every document load | M | No |
| p3-jobb-initial-nav-hero-deps-block | Perceived/Suspense placement | `(app)/jobb/page.tsx:140-146,302-305` | **P1** | First nav to /jobb blocks on 4-way fan-out with zero affordance | S | No |
| b2-cv-hub-eager-taxonomy-modal | Bundle/hydration | `(app)/cv/page.tsx:67-74,241-263` | P2 | ~300 kB taxonomy serialized into every /cv visit | M | No |
| b3-no-dynamic-imports-modals | Bundle/hydration | `match-setup-rail-modal.tsx` + static imports | P2 | Wizard/dialog JS shipped to routes that rarely open them | S | No |
| b4-globals-css-291kb-monolith | Bundle/hydration | `src/app/globals.css` (291 kB) | P2 | ~172 kB uncompressed CSS parsed on every route | L | No |
| b5-nine-static-font-files-vs-variable | Bundle/hydration | `src/app/layout.tsx:17-29` | P2 | 9 font fetches contending with first paint vs 2 | S | No |
| b6-lighthouse-asserts-only-root | Bundle/CWV coverage | `lighthouserc.json:10` | P2 | Regressions on heavy routes ship unmeasured | S | No |
| d1-jobads-browse-sort-no-index | DB/indexes | `JobAdSearchComposition.cs:65,217-227` | P2 | Default browse sort seq-scans + top-N-sorts ~43k rows per view | S | No |
| d1-list-dto-ships-full-description | API per-request | `JobAdSearchComposition.cs:262-273` | P2 | Full ad text serialized per list row; list UI never renders it | M | No |
| d1-table-row-rerender | Re-renders | `applications-table.tsx:77-80,98-105` | P2 | All 50 rows re-render per checkbox/keystroke | S | No |
| d2-auth-hot-path-7-roundtrips | API per-request | `RedisSessionStore.cs:35,59,126-132` | P2 | 5 Redis ops + 2 PG queries per authed request; ×8 on /oversikt | M | No |
| d2-bitmap-count-always-transacted | DB/query plan | `JobAdSearchQuery.cs:45,67,98,118-128` | P2 | 2-3 extra DB roundtrips per count/facet even without free-text | S | No |
| d2-pipeline-keystroke-fanout | Re-renders | `applications-pipeline.tsx:86,146-231` | P2 | Whole pipeline tree re-renders per search keystroke | S | No |
| d4-grade-rank-case-not-index-servable | DB/query plan | `PerUserJobAdSearchQuery.cs:148-190,422-461` | P2 | Match-sort evaluates CASE per row; q+grade count skips bitmap hygiene | M | No |
| d4-rate-limiter-after-full-auth | API per-request | `Api/Program.cs:277-281` | P2 | 429'd authed floods still pay full session+role cost | M | No |
| g1-jobad-detail-open-serial-stages | Perceived/waterfall | `(app)/jobb/[id]/page.tsx:36-75` + modal | P2 | Ad open = 3-4 serial stages (8-9 authed requests) | S | No |
| g2-ef-command-logging | Logging volume | `Api/appsettings.json:2-7`, `Worker/appsettings.json:2-7` | P2 | 100k+ log events per Platsbanken sync; unbudgeted signal | S | No |
| m1-typeahead-budget-unmeasured | Measurement | `perf/.../Program.cs:14` | P2 | Strictest budget (150 ms) has zero instrument | S | No |
| m3-write-budget-unmeasured | Measurement | `perf/.../Program.cs:15` | P2 | No write endpoint is fitness-checked vs 400 ms | M | No |
| m4-ingestion-throughput-no-verdict | Measurement | `SyncPlatsbankenStreamJob.cs:124-149` | P2 | 200 jobs/min floor never computed | S | No |
| m5-worker-memory-zero-telemetry | Measurement | ADR 0045:61-63 vs `src/Jobbliggaren.Worker` | P2 | 512 MiB cap unobservable until OOM | S | No |
| m6-lighthouse-only-root-url | Measurement | `lighthouserc.json:7-23` | P2 | CWV budgets unverified beyond "/" | M | No |
| m7-no-seq-p95-query-artifact | Measurement | `LoggingBehavior.cs:37-38` | P2 | p95 extraction from live logs is ad hoc | S | No |
| p2-cv-serial-skill-resolve-waterfall | Perceived/waterfall | `(app)/cv/page.tsx:67-73,110-111` | P2 | CV hub paint waits for a 5th serial fetch | S | No |
| p2-jobb-results-markjobsseen-await | Perceived/critical-path write | `jobb-results.tsx:373-375` | P2 | Every search waits one extra write RTT before content | S | No |
| p2-matchningar-markseen-blocks-render | Perceived/critical-path write | `(app)/matchningar/page.tsx:31,39-41` | P2 | Nav pays read + write serially before paint | S | No |
| p2-oversikt-9th-serial-taxonomy-fetch | Perceived/waterfall | `(app)/oversikt/page.tsx:47-81,139-141` | P2 | First-run users get the slowest /oversikt paint | S | No |
| w1-bgmatch-shared-context | Worker/jobs | `BackgroundMatchingJob.cs:51,81-102,188-211` | P2 | One user's failure can poison every later user's scan | M | No |
| w2-worker-memory-unobserved | Worker/jobs | `Worker/Program.cs:240-271` | P2 | ADR 0032-class regression invisible until OOM | S | No |
| w6-ingestion-throughput-not-derived | Worker/jobs | `SyncPlatsbankenSnapshotJob.cs:161-193` | P2 | Throughput regressions found only by manual log math | S | No |
| b7-radix-barrel-no-optimize-imports | Bundle/hydration | `next.config.ts:9-45` | P3 | Compile-time cost; residual chunk risk | S | No |
| b8-dormant-dark-mode-client-shell | Bundle/hydration | `theme-provider.tsx:37,63-138` | P3 | Dead listeners for a disabled feature | S | **Yes** |
| d1-caddy-api-prefix-shadows-bff | BFF/proxy topology | ADR 0050:139-141 + TD-106 | P3 | At Hetzner cutover `/api/*` rule 404s all 10 BFF handlers | S | **Yes** |
| d2-compression-placement-undocumented | BFF/proxy topology | `next.config.ts` (no `compress` key) | P3 | Node gzip CPU; placement decision untracked | S | Yes* |
| d3-proxy-refresh-serial-nav-hop | BFF/proxy topology | `proxy.ts:29,41,104-116` | P3 | One serial refresh RTT per 15 min; 2.5 s cap on degraded backend | S | No |
| d3-taxonomy-etag-recompute-per-request | API per-request | `JobAdsEndpoints.cs:201-216,287-292` | P3 | Re-serialize + SHA-256 of invariant 300 kB tree per hit | S | No |
| d4-jobb-page-serial-me-before-parallel-fetches | BFF/proxy topology | `(app)/jobb/page.tsx:78-79,140-146` | P3 | One avoidable serial RTT before hero | S | No |
| d4-pendingids-context-fanout | Re-renders | `application-actions.tsx:98-129,145-148` | P3 | Two full-list renders per status change | S | No |
| d5-dek-prefetch-2-pg-queries-per-request | API per-request | `FieldEncryptionKeyPrefetchBehavior.cs:36-58` | P3 | 2 index-served point reads per encrypted request | M | No |
| d5-employer-ilike-no-trigram | DB/indexes | `EmployerDisambiguationQuery.cs:36-52` | P3 | Per-lookup corpus scan; endpoint has no FE consumer yet | S | No |
| d5-hero-filters-taxonomy-rebuild | Re-renders | `jobb-hero-filters.tsx:212-229` | P3 | Allocation churn only; memoized siblings exist | S | No |
| d5-taxonomy-datacache-per-session-copies | BFF/proxy topology | `lib/api/taxonomy.ts:36,42-53` | P3 | One 300 kB cache copy per session per hour | M | No |
| d6-no-stj-source-generation | API per-request | `Api/Program.cs:180-217` | P3 | Cold-start/first-hit serialization cost | M | No |
| d6-npgsql-pools-no-budget-no-autoprepare | DB/connections | `DependencyInjection.cs:837-844,1058-1065,1295-1302` | P3 | Pool exhaustion risk at prod concurrency; SQL re-parsed | S | No |
| d6-suggest-double-hop-no-cache-header | BFF/proxy topology | `api/jobb/suggest/route.ts:17-31` | P3 | Repeat prefixes always refetch | S | No |
| d6-theme-context-value | Re-renders | `theme-provider.tsx:37,116-138` | P3 | None today (zero mounted consumers) | S | **Yes** |
| d7-applications-updatedat-sort | DB/indexes | `GetApplicationsQueryHandler.cs:53-56` | P3 | Per-seeker sort, small cardinality | S | No |
| d7-loggingbehavior-2-info-events-per-send | API per-request | `LoggingBehavior.cs:17,23,34-38` | P3 | 2× log volume per mediator send | S | No |
| d8-hangfire-poll-default | DB/background | `HangfireStorageOptionsFactory.cs:34-58` | P3 | Unpinned 15 s poll vs the factory's no-float doctrine | S | No |
| g3-landing-fetch-no-timeout (residual kernel) | BFF/proxy topology | `lib/api/landing.ts:33-35` | P3 | Hung backend could stall "/" TTFB (no `AbortSignal.timeout`) | S | No |
| m8-corpus-comment-seed-discarded | Measurement/recon | `JobAdSearchComposition.cs:51` | P3 | None — seed claim disproven, recorded to stop re-chasing | S | No |
| p3-company-lookup-ariabusy-only | Perceived/feedback | `company-lookup.tsx:64-66,81,145-152` | P3 | "Sök" gives no visible pending state | S | **Yes** |
| p3-typeahead-loading-sr-only | Perceived/feedback | `job-ad-typeahead.tsx:180-181,278-284` | P3 | No visible cue while suggestions load; prior list flashes out | S | No |
| w4-snapshot-retry-daytime-drift | Worker/jobs | `SyncPlatsbankenSnapshotWorker.cs:23-27` | P3 | Residual uncaught failures retry into daytime | S | No |
| w7-hangfire-pollinterval-floats | Worker/jobs | `HangfireStorageOptionsFactory.cs:34-58` | P3 | ~30k idle queries/day, negligible but unpinned | S | No |
| w8-scb-saturday-daytime-window | Worker/jobs | `ScbCompanyRegisterStore.cs:34-35,116-147` | P3 | Post-launch Saturday co-tenancy to re-verify | M | **Yes** |

\* `d2-compression-placement-undocumented`: the *decision gap* is current; the fix's
natural home (Caddy) is the unbuilt TD-106 stack, hence WIP-flagged.

---

## 4. Detailed findings

Each entry: what/why → evidence (verifier-corrected) → exact fix → how verification
tried to kill it. File paths are repo-relative; frontend paths omit
`web/jobbliggaren-web/src/` where obvious from context.

### P0

#### p1-no-loading-tsx-any-primary-route — every primary navigation is a dead click `[P0/M]`

No primary `(app)` route segment has a `loading.tsx`; every navigation freezes the old
page until the dynamic RSC render completes server-side. This is exactly the
anti-pattern the audit was told to hunt: click → nothing → content pops in.

**Evidence:** only 6 `loading.tsx` exist (4 modal intercepts + `cv/granska` +
`cv/slutfor`). Missing on all 12 primary segments: `oversikt` (8-way `Promise.all`
at `page.tsx:47-81`, `force-dynamic` at `:27`), `ansokningar`, `jobb` (4-way fan-out
`:140-146` above the Suspense at `:302`), `cv`, `matchningar`, `foretag`,
`installningar`, `sokningar`, `sparade`, `statistik`, `aktivitetsrapport`,
`jobb/[id]`, `ansokningar/[id]`. Aggravator: `(app)/layout.tsx:20,29` awaits
`getServerSession` + `fetchLandingStats`, so the whole group is dynamic — there is no
static shell to prefetch, and Next only prefetches the layout-to-boundary shell when a
`loading.js` exists.

**Fix:** per-segment `loading.tsx` skeletons matching page shape for
/jobb, /ansokningar, /oversikt, /cv (highest traffic) + a group-level
`(app)/loading.tsx` pagehero skeleton as the fallback net. Skeleton (not spinner) per
the spinner doctrine. This doubles as a prefetch enabler.

**Verification:** verifier hunted mitigations — no `template.tsx`, no
nav-progress/`useLinkStatus`/`useTransition` anywhere; confirmed the (app) layout
awaits make a static shell impossible today. ADR 0045's own class-(a) budget
(300 ms p95) exceeds the 200 ms zero-feedback rule on its own, so this is P0 at
current scale by the binding calibration. Related to #596 but a different angle.

### P1

#### b1-full-i18n-catalog-hydrated — whole i18n catalog in every document payload `[P1/M]`

The entire 21-namespace Swedish catalog (163,319 B raw) is serialized into the RSC
Flight payload of every document load and hydrated on every route — including
`content-legal.json` (43,680 B) used only by two server-rendered marketing pages, and
`content-*`/`metadata`/`errors` namespaces (~59.9 kB combined) that no client
component consumes at all.

**Evidence:** `i18n/request.ts:31` returns the full `MESSAGES[resolved]` barrel
(`messages/sv/index.ts:28-50` = 21 namespaces); `app/layout.tsx:65,76` passes
`getMessages()` untrimmed to `NextIntlClientProvider`. Grep-verified: zero
`useTranslations` calls on any `content-*` namespace; no `useMessages()` anywhere.
`budget.json` document budget is 30 kB.

**Fix:** keep server-side `getTranslations` on the full catalog, but pass a pruned
pick to `NextIntlClientProvider`: strip `content-*`, `metadata`, `errors` globally now
(and `admin` outside the `(admin)` group — `admin-nav.tsx` does consume it); then
per-route-group layouts pass only the namespaces their client components use
(next-intl supports partial client messages).

**Verification:** verifier confirmed the only production `NextIntlClientProvider` is
the root layout and no pruning mechanism exists. Correction absorbed: payload is
per *document* load — soft client navigations do not re-send it. Still a systemic
per-request amplifier → P1.

#### p3-jobb-initial-nav-hero-deps-block — /jobb Suspense only helps in-page searches `[P1/S]`

First navigation to Lediga jobb dead-clicks through `getServerSession` (serial,
`page.tsx:78`) plus a 4-way `Promise.all` (`:140-146`) that all sit *above* the sole
Suspense boundary (`:302-305`) — with no `loading.tsx` to paint anything.

**Evidence:** 3 of the 4 hero deps go through `authedFetch` which forces
`cache: "no-store"` (`authed-fetch.ts:48`); taxonomy is data-cached (1 h) but
Zod-parses ~300 kB per request. In-page filter changes re-render within the mounted
page (skeleton via key) — only cross-route navigation has zero affordance.

**Fix:** segment `loading.tsx` reproducing the jp-hero plate + `JobAdListSkeleton`
(both exist as stable shapes). Hero deps can stay above the boundary once the shell
paints instantly.

**Verification:** verifier confirmed no loading/template/`useLinkStatus` exists on
the nav path and that `authedFetch` forces fresh round-trips. Kept at P1 as a
systemic amplifier on the app's most-visited page.

### P2 — user-visible or systemic, within budget at current scale

#### g1-jobad-detail-open-serial-stages — ad open runs 3-4 serial backend stages `[P2/S]`

The hottest interaction (open an ad → intercepting modal or full page) chains
`getServerSession` → `getJobAd` → 6-way `Promise.all` (`isJobAdSaved`,
`hasAppliedJobAd`, `getCompanyWatchStatus`, `getJobAdMatchDetail`,
`getEmployerApplicationCounts`, **plus `markFollowedCompanyAdSeen` — a WRITE whose
comment claims "no hot-path latency" but which is awaited inside the fan-out**) →
serial `getTaxonomyTree` when a match exists. 8-9 authenticated requests, though
stages 2-3 share no data dependency (all six fan-out calls key on `id` only).

**Evidence:** `(app)/jobb/[id]/page.tsx:36,45,53-66,75` and the intercepting modal
`(app)/@modal/(.)jobb/[id]/page.tsx:41,50,56-69,77` (identical chain);
`company-follows.ts:138-158` awaited POST vs the fire-and-forget comment at `:63-64`.
The modal HAS `loading.tsx` (spinner appears instantly) — the issue is open-to-content
latency, not a dead click; the full page has no `loading.tsx`.

**Fix:** merge `getJobAd` into the fan-out `Promise.all`; include the (1 h-cached)
`getTaxonomyTree` there or keep it gated; move `markFollowedCompanyAdSeen` off the
render path via `after()` and fix the comment; add `loading.tsx` for the full page.
Natural landing spot: inside #596's shared `loadJobDetailData` helper (related issue,
not a duplicate — #596 is a pure DRY refactor with no perf angle).

**Verification:** surfaced by the completeness critic (this flow had zero findings in
the 8-dimension pass), then adversarially verified: all cited lines re-read, #596
overlap explicitly tested, modal loading.tsx + warm taxonomy cache rule out P0/P1.

#### g2-ef-command-logging — every SQL statement logs at Information in both hosts `[P2/S]`

`Logging:LogLevel:Default=Information` with **no `Microsoft.EntityFrameworkCore`
override** in any appsettings layer (base/Development/Production, Api and Worker) and
zero `AddFilter`/`ConfigureWarnings`/`LogTo` in src — so every `Executed DbCommand
(Xms)` event is emitted, including per-item statements from the ~47k-row Platsbanken
sync (per-item `mediator.Send` + `SaveChangesAsync`): a 100k+-event flood per run
into console + Seq. Flip side: this per-query duration signal is the audit
measurement plan's "EF query logging" instrument, and nobody has documented how to
use or budget it.

**Evidence:** `Api/appsettings.json:2-7`, `Worker/appsettings.json:2-7` (+ overlays);
`LoggingBuilderExtensions.cs:37-42` (Seq attaches when `Seq:ServerUrl` set, no level
overrides); `SyncPlatsbankenStreamJob.cs:71-98`;
`UpsertExternalJobAdCommandHandler.cs:67`. No `EnableSensitiveDataLogging` → volume
only, no PII angle.

**Fix:** add `"Microsoft.EntityFrameworkCore.Database.Command": "Warning"` to both
hosts' `Logging:LogLevel` (or Production overlay only, keeping Information in dev as
the measurement instrument); document in the perf runbook how the signal is
enabled/read against ADR 0045 budgets.

**Verification:** critic gap → adversarial verify re-read all appsettings layers in
both hosts, hunted for `AddFilter`/`LogTo` mitigations (none), confirmed the per-item
upsert pattern, and separated it from TD-104 (log *destination*, not volume).

#### b2-cv-hub-eager-taxonomy-modal — /cv pays taxonomy + wizard for every visitor `[P2/M]`

/cv fetches the full taxonomy tree unconditionally and renders `CvMatchSetup` (which
always mounts the closed `MatchSetupRailModal`) for every visitor with a CV — the
~300 kB tree serializes into every /cv Flight payload as client props, and the
2300+-line wizard tree ships in the route JS, though most sessions never open it.

**Evidence:** `(app)/cv/page.tsx:67-73` (unconditional `Promise.all` incl.
`getTaxonomyTree()`), `:241-263` (render); `cv-match-setup.tsx:155-183`. Contrast:
`oversikt/page.tsx:132-141` gates the same fetch+mount behind `shouldMountSetup`.
Softeners (verifier): server fetch amortized by `revalidate: 3600`; closed Radix
Dialog renders no DOM — the cost is RSC payload + route JS, not hydrated DOM.

**Fix:** mirror the /oversikt gate: `next/dynamic` the modal on first open and fetch
taxonomy lazily then (route handler or server action), instead of eager fetch +
always-mounted closed dialog.

**Verification:** verifier hunted mitigations (found the fetch cache + Radix
lazy-mount, folded into severity), confirmed the gate pattern already exists in-repo.

#### b3-no-dynamic-imports-modals — zero `next/dynamic`/`React.lazy` in the app `[P2/S]`

Large dialog trees rendered closed are statically bundled into their routes: real
dead weight is /cv (closed wizard for every CV owner) and /installningar
(`MatchPreferencesDialog` rendered closed at `match-preferences-card.tsx:493`).
/oversikt self-mitigates (server-conditional mount + `autoOpen` means the chunk ships
only when it executes).

**Evidence:** grep over src: zero `dynamic(`/`next/dynamic`/`React.lazy`.
`MatchSetupRailModal` (1007 lines + OccupationSection 757 + SkillSection 512 + RHF/zod
chain) statically imported by `cv-match-setup.tsx:19`; `settings-form.tsx:161` →
closed dialog. The 1573-line `cv-complete-guide` is route-scoped already.

**Fix:** `next/dynamic({ ssr: false })` for dialog trees rendered only after user
click (MatchSetupRailModal behind launcher state, MatchPreferencesDialog); keep
trigger buttons static so no CLS.

**Verification:** verifier narrowed the claim (Radix unmounts closed content → cost
is download/parse, not hydration; /oversikt exempt) and confirmed no ADR covers
bundle splitting.

#### b4-globals-css-291kb-monolith — one global stylesheet for all routes `[P2/L]`

`globals.css` is 291,314 B imported once in `app/layout.tsx:6`; all route-scoped
`.jp-*` CSS (119 `.jp-wizard*`, 85 `.jp-land*`, 42 `.jp-hero*`, 23 `.jp-pagehero*`
selectors) ships and parses on every route. The *transfer* budget is NOT breached
(minified ~172 kB → ~24 kB gzip vs 75 kB budget) — the real cost is per-route parse
of ~172 kB uncompressed CSS.

**Evidence:** `budget.json:9` (75 kB stylesheet budget, observe-only Fas 1);
comment-stripped 184,308 B; rough-minified 171,607 B; gzip ~23,787 B (verifier
measurements).

**Fix:** measure compiled CSS size in build output first; then move clearly
page-scoped blocks (`.jp-land-*`, `.jp-wizard--rail`, marketing content styles) into
CSS imported by the owning route-group layout, keeping tokens/base in `globals.css`.

**Verification:** verifier REFUTED the budget-breach half with actual
compression measurements and kept the parse-cost half → P2.

#### b5-nine-static-font-files-vs-variable — 9 font files where 2 would do `[P2/S]`

`layout.tsx:17-29` requests 5 static Source Sans 3 weights + 4 JetBrains Mono weights.
Both families are variable Google fonts; `next/font` `weight: "variable"` gives one
latin file per family. All weights are genuinely used, so no weight can be dropped —
but the *file count* can. `display: "swap"` (`:21,28`) means bandwidth contention with
first paint rather than render-blocking.

**Fix:** switch both `next/font/google` calls to `weight: "variable"`; verify rendered
weights 400-800 map correctly; compare transferred bytes vs the 120 kB font budget
(`budget.json:11`).

**Verification:** verifier confirmed the in-file comment and ADRs 0015/0038 decided
the *weight set*, never static-vs-variable — no prior decision blocks this.

#### b6-lighthouse-asserts-only-root / m6-lighthouse-only-root-url — CWV gate covers one URL `[P2/S–M]`

`lighthouserc.json:10` collects only `http://localhost:3000/`; every budget assertion
(LCP error@2500ms, CLS error@0.1, TBT warn@200ms, `budget.json` page weight) is thus
unverified for all marketing-inner, guest, and authed surfaces — exactly where
findings b1–b4 concentrate. The CI job is observe-only (`build.yml:240-286`,
`continue-on-error`).

**Fix (S):** add public URLs to `collect.url`: `/matchning`, `/cv-granskning`,
`/hjalpcenter`, `/for-utvecklare`, plus guest mirrors `gast/jobb`, `gast/oversikt`,
`gast/cv` (mock-data pages, no backend needed in CI). **(M):** authed pages via LHCI
`puppeteerScript` form-login with the dev-test account once a seeded server exists in
CI (adjacent to TD-89).

**Verification:** verifier corrected the finder's "guest /jobb" (protected prefix —
`protected-routes.ts:25`) and confirmed ADR 0045 Beslut 2 locks thresholds but is
silent on URL scope, so root-only is a gap, not a decision.

#### d1-jobads-browse-sort-no-index — default browse sort has no serving index `[P2/S]`

The no-facet /jobb browse (the single most-hit query) always filters
`Status == Active` (`JobAdSearchComposition.cs:65`) and sorts by
`PublishedAt`/`ExpiresAt` (`:217-227`), but `job_ads` has **zero** index on
status/published_at/expires_at — the items query seq-scans + top-N-heapsorts ~42,873
rows per page view.

**Evidence:** `AppDbContextModelSnapshot.cs:384-501` has no `HasIndex` on JobAd;
existing indexes serve only concept-ids, trigram title/description, search_vector,
lexemes, org.nr. Verifier corrections: the COUNT half is already served (partial
title index + forced `enable_seqscan=off` per TD-94/ADR 0062; counts are O(active
rows) regardless) — the unmitigated cost is the items query (`JobAdSearchQuery.cs:49-55`).

**Fix:** migration: `CREATE INDEX ix_job_ads_active_published_at ON job_ads
(published_at DESC, id) WHERE status = 'Active' AND deleted_at IS NULL;` optional
sibling on `(expires_at, id)` with the same predicate. (Global query filter
`DeletedAt == null` at `JobAdConfiguration.cs:198` makes the predicate exact.)

**Verification:** verifier killed the count half with the existing partial index +
GUC evidence; corpus-growth hard rule caps severity at P2 (job_ads ≈ full Platsbanken
corpus, won't 10x).

#### d1-list-dto-ships-full-description — list responses carry the full ad text `[P2/M]`

`ToDto()` (`JobAdSearchComposition.cs:262-273`, Description at `:267`) projects the
full untruncated `Description` into every list row across all three list surfaces
(`JobAdSearchQuery.cs:54`, `PerUserJobAdSearchQuery.cs:198,340`) — but only
`job-ad-detail.tsx:188` renders it, and the detail fetches separately via `getJobAd`.
PageSize can be 100. Mitigating: `JobAdCard` is an RSC, so dead bytes stop at the BFF.

**Fix:** split a `JobAdListItemDto` without Description (or a ~200-char snippet) for
list projections; keep full Description in `GetJobAdQuery` (shared-DTO constraint:
ADR 0053 — detail modal/page share `JobAdDto`, so the split must preserve that).
Update FE Zod list schema (`lib/dto/job-ads.ts:72`).

**Verification:** verifier grep-confirmed zero list-side consumers of description and
verified the detail path fetches independently.

#### d1-table-row-rerender — tabell view re-renders all rows per interaction `[P2/S]`

Every checkbox toggle and search keystroke re-renders all ~50 visible table rows.
Zero `React.memo` exists in src; `toggleRow` (`applications-table.tsx:98`) is
recreated per render; the parent passes a fresh `sections.flatMap` array
(`applications-pipeline.tsx:197`) so the sort memo (`:77-80`) re-sorts per keystroke —
over the FULL fetched set (unbounded until TD-8 pagination).

**Fix:** `React.memo(ApplicationsTableRow)`, `useCallback(toggleRow)`, `useMemo` the
rows array. Verifier refinements: closed Radix menus lazy-mount (smaller per-row cost
than claimed); sort-click re-render is inherent — the waste is checkbox + keystroke;
the search input (`applications-controls.tsx:52-58`) also lacks debounce/
`useDeferredValue`.

**Verification:** mitigation hunt found none; scale-dependent (200-300 apps) → P2.

#### d2-auth-hot-path-7-roundtrips — 5 Redis ops + 2 PG queries per authed request `[P2/M]`

`RedisSessionStore.GetAsync` does GET (`:35`) + deleted-tombstone `KeyExists` (`:59`)
+ unthrottled sliding writes `SADD`/`KeyExpire`/`SetString` (`:126,130,132`), all
sequentially awaited; `SessionRoleClaimsTransformation.cs:65` →
`UserAccountService.cs:127-134` adds `FindByIdAsync` + `GetRolesAsync` (2 PG queries)
per request. /oversikt fires 8 authenticated backend calls in parallel → ~40 Redis
ops + ~16 identity queries per page view (verifier-corrected from 45/18;
`fetchLandingStats` is anonymous; `getServerSession` is React.cache-deduped).

**Fix:** throttle the sliding rewrite (skip SADD/EXPIRE/SET when < N % of SlidingTtl
consumed, e.g. slide at most once per minute per session); collapse the role fetch to
one joined query. **Any role-claim caching needs CTO sign-off** (immediate-revoke
decision A1, 2026-05-11). Note: TD-23 already earmarks IBatch pipelining as the
ADR 0045 mitigation, measurement-gated — this finding adds the *throttle* +
*role-query collapse* angles.

**Verification:** verifier confirmed every op, then narrowed with the sanctioned
mitigations (tombstone RTT is ADR-0045-sanctioned; SADD/EXPIRE is the #502 Art.17
fix) — cost amplifier, not a latency-series blocker → P2.

#### d2-bitmap-count-always-transacted — count hygiene applied where it can't help `[P2/S]`

`CountWithBitmapPlanAsync` (`JobAdSearchQuery.cs:118-128`) opens a transaction +
`SET LOCAL enable_seqscan = off` + commit for **every** count/facet call
(`:45,67,98`) — but the TOAST-detoast problem it works around exists only for the
tsvector q-predicate (`JobAdSearchComposition.cs:137` gates FTS on non-blank Q).
No-q counts (~31 ms on the default planner per TD-94 empirics) pay 2-3 extra
roundtrips for nothing.

**Fix:** gate the wrapper on `criteria.Q`: blank → bare `CountAsync`; only wrap when
the FTS predicate is present.

**Verification:** verifier confirmed unconditional routing and softened the "slow
forced plan" risk (`enable_seqscan=off` is disable_cost, not a ban; ADR 0067 partial
indexes serve filter-only predicates) — cost is the roundtrips. New angle on
TD-94/ADR 0062 (mechanism accepted; rationale is q-only), not a duplicate.

#### d2-pipeline-keystroke-fanout — search keystroke re-renders the pipeline tree `[P2/S]`

`query` state lives at the island root (`applications-pipeline.tsx:86`); every
keystroke re-renders `AttentionQueue` (keystroke-stable props), `StepRail` (unstable
`onToggle`), and all open `StatusSection` rows — ~100 rows default-collapsed, more
with "Visa fler"/`forceOpen` during search.

**Fix:** `React.memo(AttentionQueue)` + `React.memo(StepRail)` +
`useCallback(toggleFilter)`; optionally memo `ApplicationRow`. Do NOT restructure
state ownership — the shared query is by design (ADR 0092 D1).

**Verification:** verifier confirmed zero memo/debounce in the tree and identified
which subtrees are strictly keystroke-invariant (queue + rail) vs necessarily
re-rendered (filtered sections).

#### d4-grade-rank-case-not-index-servable — match-sort CASE scans; graded count skips hygiene `[P2/M]`

`GradeRankExpression` (`PerUserJobAdSearchQuery.cs:422-461`, CASE over 4 shadow
columns + a 5th in the golden-rung ORDER BY term `:178-186`) drives WHERE (`:148`),
ORDER BY (`:190`) and COUNT — per-column partial indexes exist (migrations
20260608155047/20260608205054) but cannot serve `CASE=ANY(@ranks)`, and the base
`Status==Active` predicate has no index. Worse: the graded count (`:158`) is a plain
`CountAsync` — when free-text q is present it re-exposes the TOAST detoast seqscan
that TD-94/ADR 0062 fixed on the shared port (the in-file comment `:233-239`
justifies skipping bitmap hygiene only for the no-q branch).

**Fix:** route the graded/status count through the bitmap-plan wrapper when
`filter.Q` is present (reachable via `ListJobAdsQueryHandler.cs:64-73,127`); longer
term, EXPLAIN ANALYZE the match-sort worst case (broad SSYK, no facets) before launch
and accept or precompute a rank column.

**Verification:** verifier confirmed `CountWithBitmapPlanAsync` exists only in
`JobAdSearchQuery.cs` and traced the q+grade reachability. New angle on TD-94, not a
duplicate.

#### d4-rate-limiter-after-full-auth — 429s pay full auth cost first `[P2/M]`

`app.UseRateLimiter()` runs after `UseAuthentication`/`UseAuthorization`
(`Api/Program.cs:277-281`, deliberate for UserId-partitioned limits) and
`DefaultAuthenticateScheme="Bearer"` (`:63-67`) makes auth eager — so every 429'd
request with a valid session has already paid 5 Redis ops + 2 identity queries.
Verifier narrowing: anonymous 429s (login/register, IP-partitioned) pay ≤ 1 Redis GET
— the amplification is authenticated-flood-only.

**Fix:** keep session lookup pre-limiter (partition key needs it) but make role
resolution lazy: move `GetRolesAsync` out of `IClaimsTransformation` into the
authorization handlers/policies that need Role claims, so 429'd and anonymous-policy
requests skip the 2 identity queries.

**Verification:** pipeline order + eager-auth verified verbatim; no `GlobalLimiter`
exists (`RateLimitingExtensions.cs`); CTO A1 covers claim *freshness*, not pipeline
*ordering*.

#### p2-matchningar-markseen-blocks-render / p2-jobb-results-markjobsseen-await — awaited writes on the render path `[P2/S ×2]`

`/matchningar` awaits `markMatchesSeen()` (a real POST, `me-matches.ts:65-80`) after
`getMyMatches` and before returning JSX (`(app)/matchningar/page.tsx:31,39-41`) — with
no `loading.tsx` and `force-dynamic`, both round-trips happen inside the nav dead
click. The in-file comment claims "fire-and-forget" (`:16-18`) while `:33-38` admits
a deliberate await. Same pattern in `jobb-results.tsx:373-375`: `await markJobsSeen()`
sits between the fetches and `return (`, delaying every skeleton-to-content swap by
one write RTT (here inside Suspense, so feedback exists → polish).

**Fix:** `after(() => markMatchesSeen())` / `after(() => markJobsSeen())` from
`next/server` (stable in the pinned Next 16.2.9), preserving the fetch-then-mark
gate; update the comments. ADR 0042 W-amendment (line 228) says the mark runs "efter
rendering" — the blocking await is not ADR-mandated.

**Verification:** verifier confirmed the awaits are real POSTs, `after()` is
available, and anonymous users skip the write.

#### p2-cv-serial-skill-resolve-waterfall — /cv's fifth serial fetch `[P2/S]`

`(app)/cv/page.tsx:67-73` runs a 4-way `Promise.all`, then `:110-111` serially awaits
`resolveSkillLabels(profile.preferredSkills)` — the dependency is only on `profile`,
so first paint = max(4 fetches) + one avoidable RTT. Softener (verifier):
`skills.ts:129` short-circuits with no round-trip when `preferredSkills` is empty, so
the cost hits exactly the engaged users with saved skills.

**Fix:** fold into the fan-out: `getMyProfile().then(p => p.kind === "ok" ?
resolveSkillLabels(...) : ...)` inside the `Promise.all` (keep the kind-guard). Plus
segment `loading.tsx` per the P0 finding.

#### p2-oversikt-9th-serial-taxonomy-fetch — first-run users get the slowest paint `[P2/S]`

`(app)/oversikt/page.tsx:47-81` blocks on an 8-way `Promise.all`; `:139-141` then
serially awaits `getTaxonomyTree()` when `shouldMountSetup` — exactly the
first-run/onboarding users pay max(8) + taxonomy before any pixel. Softeners: Next
data cache (1 h) is keyed per-session (first load misses), backend serves from an
in-process singleton — the hop is cheap but serial.

**Fix:** `loading.tsx` (P0 finding); start `getTaxonomyTree()` unawaited alongside
the fan-out and await it only inside the `shouldMountSetup` branch, or move
`MatchSetupLauncher` below a Suspense boundary.

#### w1-bgmatch-shared-context — one DbContext across the per-user matching loop `[P2/M]`

`BackgroundMatchingJob` shares one `IAppDbContext` (`:51`) across the whole
`foreach` over opted-in users (`:81-102`). A mid-user exception leaves poisoned
tracked entities (Added at `:191`, Modified at `:210`, plus `:252/:263` inside
`DispatchTopDirectAsync`) that the NEXT user's `SaveChangesAsync` flushes or re-fails
— a persistent error cascades to every subsequent user, violating the job's own TD-25
isolation claim (`:90-95`). This exact pathology was a lived incident for the
snapshot job (23505 poison → Hangfire 60 starts/0 completes) and was fixed there with
child scopes (`SyncPlatsbankenSnapshotJob.cs:76`).

**Fix:** mirror the snapshot job — `IServiceScopeFactory` child scope per user (own
context); or minimally `db.ChangeTracker.Clear()` in the catch and after each user's
commit (`IAppDbContext.Detach` exists but is unused).

**Verification:** verifier found the in-repo precedent (both the incident and the
fix pattern); duplicate-notification harm is bounded by
`UNIQUE(UserId, JobAdId)` + `existingJobAdIds` backstop → the cascade is the real
harm → P2.

#### w2-worker-memory-unobserved + m5-worker-memory-zero-telemetry — 512 MiB cap has no eyes `[P2/S ×2]`

ADR 0045 Beslut 3 promises an observe-only Fas 1 "trend-logg" for the 512 MiB Worker
soft cap; nothing implements it. Grep across src/perf/CI: zero
EventCounters/Meter/OTel/`GC.GetTotalMemory`/`WorkingSet` in runtime code. The only
memory observation anywhere is a test-time NLP-tier check
(`NlpTierMemoryObservationTests.cs:37-71`) whose own comment defers the final ADR 0045
mechanism to F4-9 — which shipped without it. No compose service/mem_limit exists for
the Worker (dev compose has only postgres/redis/seq; Hetzner compose = TD-106).

**Fix:** periodic hosted-service trend log in Worker (`Environment.WorkingSet` +
`GC.GetTotalMemory` every N min, structured `WorkerMemoryTrend` event, warn > 512 MiB)
— cheap now, and TD-104's closed Seq sink gives it a structured destination; add
`mem_limit` when the deploy compose lands (TD-106).

**Verification:** both finders' claims verified by independent verifiers; the
NlpTier carve-out was surfaced and folded in.

#### w6-ingestion-throughput-not-derived + m4-ingestion-throughput-no-verdict — 200 jobs/min floor never computed `[P2/S ×2]`

ADR 0045 class (d) (≥ 200 jobs/min sustained, `0045-...md:45`) has raw signal —
`LogCompleted` emits fetched + durationSec in both sync jobs
(`SyncPlatsbankenSnapshotJob.cs:190-193` EventId 5402;
`SyncPlatsbankenStreamJob.cs:169-172` EventId 5302), and the `JobAdsSynced` audit row
persists Fetched/StartedAt/CompletedAt — but no code computes jobs/min or compares
against the floor; `perf/` has no ingestion scenario.

**Fix:** one-line `itemsPerMinute` field in both completion log messages + a
structured warn `IngestionThroughputBelowFloor` when < 200 (observe-only per
ADR 0045); optionally a runbook audit-log query.

**Verification:** repo-wide grep by both verifiers (only Polly `MinimumThroughput`
false positives); distinct from TD-89 (worker runtime metric, not HTTP scenario).

#### m1-typeahead-budget-unmeasured — the strictest budget has zero instrument `[P2/S]`

ADR 0045 class (b) — typeahead p95 150 ms, Klas-locked (`0045-...md:43`) — has no
scenario anywhere: `perf/Jobbliggaren.LoadTests/Scenarios/` contains only
LandingStats/FacetCounts/FreeTextCount/MatchSort/MatchTagBatch, all budgeted Class_A
300 ms. The live endpoint is keystroke-hot (`JobAdsEndpoints.cs:129` `/suggest`;
`job-ad-typeahead.tsx:121`).

**Fix:** `SuggestScenarios.cs` mirroring FreeTextCountScenarios (rotating ≥3-char
prefixes, `LOADTEST_BEARER_TOKEN`), registered under a `suggest` selector with
`Class_B_P95_BudgetMs = 150`.

**Verification:** verifier confirmed ADR 0042 bounded suggest *cost* (index + rate
policy) but measures nothing; runner is observe-only Fas 1 (`Program.cs:197-200`).

#### m3-write-budget-unmeasured — no write endpoint is fitness-checked `[P2/M]`

ADR 0045 class (c) — command/write p95 400 ms — has no scenario; all 10
`scenarioBudgets` assignments are Class_A (`Program.cs:98-174`; MatchTagBatch POSTs
but is read-shaped). Classes (b) and (d) are equally unmeasured — (c) is not unique;
the real gap is zero warn/trend signal for writes and nothing to ratchet when
ADR 0045 Beslut 6 flips to blocking.

**Fix:** one idempotent write scenario (e.g. PUT toggle of a saved/followed flag that
the scenario reverts) with `Class_C_P95_BudgetMs = 400`; document cleanup so repeated
runs stay bounded.

#### m7-no-seq-p95-query-artifact — p95 extraction is ad hoc `[P2/S]`

`LoggingBehavior.cs:37-38` emits `Handled {MessageName} in {ElapsedMs}ms` — ADR 0045's
declared measuring point — but no saved Seq query, signal, or dashboard exists
anywhere in the repo, and CI's NBomber path runs baseline-only pending TD-89. Nobody
can answer "is any handler over budget?" without hand-writing the query.

**Fix:** commit a Seq query snippet to a runbook: filter on the message template,
`select percentile(ElapsedMs, 95), count(*) group by MessageName order by p95 desc`
over 1 h/24 h windows; optionally a seqcli signal export.

### P3 — latent, scale-hypothetical, or hygiene (compact entries)

Each P3 entry survived the same adversarial verification as P0–P2; severity is capped
by the binding calibration (current scale, feature-dark paths, pre-launch traffic).

**b7-radix-barrel-no-optimize-imports `[P3/S]`** — the unified `radix-ui` barrel (35
namespace imports in `dist/index.mjs`) is imported by 7 ui primitives but absent from
Next's default `optimizePackageImports` (verified `next/dist/server/config.js:985+`:
lucide-react is there, radix-ui is not); both dev and build are webpack-pinned.
`sideEffects: false` lets prod builds prune, so the residual cost is mostly compile
time. *Fix:* `experimental.optimizePackageImports: ["radix-ui"]` in `next.config.ts`,
then verify chunks.

**b8-dormant-dark-mode-client-shell `[P3/S, WIP]`** — with `DARK_MODE_ENABLED=false`,
`ThemeProvider` still registers matchMedia/storage/custom-event listeners
(`theme-provider.tsx:63-100,116-138`). Verifier: snapshot is constant `"light"` so
`useSyncExternalStore` never re-renders (listeners are dead weight, not amplifiers);
children stay RSC. Dormant flag = Klas decision 2026-06-24. *Fix (with dark-mode
re-enable):* short-circuit listener registration when disabled.

**d1-caddy-api-prefix-shadows-bff `[P3/S, WIP — but cutover-critical]`** —
ADR 0050:139-141 routes `/api/*` to the ASP.NET API at Hetzner; **TD-106
(tech-debt.md:304) codifies the same rule** — but ALL backend routes live under
`/api/v1/` only (16 MapGroups + 3 top-level maps), while 10 Next BFF handlers live at
`/api/jobb/*`, `/api/me/*`, `/api/cv/*`, `/api/foretag/lookup`, `/api/landing-stats`.
As written, cutover 404s typeahead, facet counts, CV preview/import, company lookup
and header stats. No Caddyfile exists yet, so zero current impact — but both planning
docs carry the error. *Fix:* amend ADR 0050 + TD-106 to route `/api/v1/*` → API (or
route everything through Next — no client calls the backend directly); add a cutover
checklist item asserting the BFF prefixes are served by Next.

**d2-compression-placement-undocumented `[P3/S]`** — no `compress` key in
`next.config.ts`, so Node gzips by default; no ADR documents compression placement.
Verifier: Cloudflare (ADR 0050 M-5) serves brotli at the edge regardless, softening
browser impact; Node CPU + the undocumented decision remain. *Fix:* `encode zstd
gzip` in the TD-106 Caddyfile + `compress: false` in Next; record in the TD-106 ADR.

**d3-proxy-refresh-serial-nav-hop `[P3/S]`** — when the 15-min throttle elapses,
`proxy.ts:104-116` serially awaits POST `/auth/refresh` inside the navigation
(2500 ms cap at `:41`; 30 s failure backoff at `:118-131`). Structural (cookie
rotation must block); mitigations (throttle, prefetch skip) verified present.
*Fix:* accept as designed; lower `REFRESH_TIMEOUT_MS` toward ~1 s on same-box deploy
and document the p95 nav cost.

**d3-taxonomy-etag-recompute-per-request `[P3/S]`** — `/job-ads/taxonomy` re-serializes
the invariant ~300 kB singleton and recomputes SHA-256 per request — before the
If-None-Match check, and twice on 200s (`JobAdsEndpoints.cs:201-216,287-292`).
Verifier correction: FE hits this roughly once per user-hour per Next process (data
cache), not per render. *Fix:* memoize UTF-8 bytes + ETag alongside `CacheState`;
`Results.Bytes` on 200.

**d4-jobb-page-serial-me-before-parallel-fetches `[P3/S]`** — `page.tsx:78` awaits
`getServerSession` (deduped-but-serialized `/me` RTT) before the 4-way fan-out that
only needs the cookie. Verifier cap: the layout's own serial `/me` →
`fetchLandingStats` chain means fixing the page alone gains nothing — both need
reordering. *Fix:* start the fan-out before awaiting session (fetchers already map
401), and reorder the layout chain.

**d4-pendingids-context-fanout `[P3/S]`** — one row's status transition replaces the
`pendingIds` Set in shared context (`application-actions.tsx:107,121-125,145-148`),
re-rendering every row/menu consumer twice per transition (also
`attention-queue.tsx:71`, `use-row-actions.ts:50`). Only worth fixing together with
the d1/d2 row memoization (memo-less rows re-render anyway). *Fix:* boolean `pending`
prop from a parent, or split contexts + memo rows.

**d5-dek-prefetch-2-pg-queries-per-request `[P3/M]`** — each
`IRequiresFieldEncryptionKey` request re-runs JobSeekerId lookup + wrapped-DEK read
(`FieldEncryptionKeyPrefetchBehavior.cs:36-58`; `UserDataKeyStore.cs:35-124`) because
the DEK cache is request-scoped. Both are index-served sub-ms point reads; unwrap is
local AES-GCM. *Fix (if ever needed):* short-TTL cache of the two *non-secret*
lookups only — and any cross-request cache must be invalidated by
`DeleteDataKeysAsync` to preserve crypto-erasure timing (C6); CTO/security sign-off
required.

**d5-employer-ilike-no-trigram `[P3/S]`** — employer disambiguation runs
`ILIKE '%term%'` on unindexed `company_name` + GROUP BY per lookup
(`EmployerDisambiguationQuery.cs:36-52`). No FE consumer exists yet (grep: zero
`/employers` hits in web/src); rate-limited 60/min. *Fix (when a consumer lands):*
`CREATE INDEX ... USING gin (company_name gin_trgm_ops)` — verifier corrected the
finder's index spec: NOT `lower()` (EF emits ILIKE on the raw column) and NOT
status-predicated (the query filters only org.nr + soft-delete).

**d5-hero-filters-taxonomy-rebuild `[P3/S]`** — `jobb-hero-filters.tsx:212-229`
rebuilds ~300+ popover-group objects inline per render while sibling maps are
`useMemo`'d (`:232-246`). Allocation-only (consumers are key-remounted). *Fix:* wrap
both group builds in `useMemo(..., [taxonomy])`.

**d5-taxonomy-datacache-per-session-copies `[P3/M]`** — `getTaxonomyTree` fetches with
per-session Bearer headers + `revalidate: 3600` (`lib/api/taxonomy.ts:27-52`); Next
hashes init.headers into the data-cache key, so every session stores its own hourly
~300 kB copy and revalidation sends no If-None-Match. Backend ETag/in-memory cache
absorbs the DB cost. *Fix:* fetch with constant identity (gate auth in the caller) or
module-level TTL memo.

**d6-no-stj-source-generation `[P3/M]`** — zero `JsonSerializerContext` in src; all
API responses use reflection STJ. Reflection metadata caches after first use → the
win is cold-start p99, not steady state. Verifier: the middleware
`WriteAsJsonAsync` calls (`Program.cs:180-217`) use anonymous types which source-gen
does NOT support — those need named DTOs first. *Fix (low prio, when touching
serialization):* context covering hot DTOs via `ConfigureHttpJsonOptions`
TypeInfoResolverChain with reflection fallback.

**d6-npgsql-pools-no-budget-no-autoprepare `[P3/S]`** — no `Maximum Pool Size` or
`Max Auto Prepare` anywhere; Api and Worker hold separate pools (different secrets →
different strings, so 2+ independent 100-max pools) vs Postgres default
max_connections 100. Worker realistically bounded by WorkerCount=4. Pool *budgeting*
is already tracked (TD-106 point 6, Hetzner) — the **MaxAutoPrepare/re-parse angle is
the new part**. *Fix:* explicit pool sizes per process + `Max Auto Prepare=20;Auto
Prepare Min Usages=2` in Hetzner connection strings; document the max_connections
budget in the deploy runbook.

> **⚠ STOP — do not enable `Max Auto Prepare` connection-string-wide without reading this.**
> *(Measured during #875, 2026-07-14. This warning was previously an open issue; it lives
> here because this is the line whose author needs it.)*
>
> `CompanyWatchBrowseQuery.ItemsSql` is a **constant**. A selective criterion (5 SNI ×
> 3 kommun) and a broad one (1000 × 290) send the **same statement text** and differ only
> in `@sni`/`@kommun` **values**. Today Npgsql sends **unnamed** statements, so Postgres
> **custom-plans** each execution with the actual values — and picks **two different plans**:
> `BitmapAnd(GIN, kommun) → Sort` for selective, `Index Scan using
> ix_company_register_company_name_organization_number` (stop at LIMIT 20) for broad.
>
> `Max Auto Prepare` makes the statement **named** → one plan-cache entry → a **generic
> plan**, built *without* parameter values. **A generic plan cannot be both.** Measured at
> 200k rows under `plan_cache_mode = force_generic_plan`: it picks BitmapAnd + Sort and
> **never touches the name index**. The broad case falls back to sorting the whole match
> set — **7 066 ms p95** against ADR 0045's **300 ms** budget (23× over) — and #875's
> **55 MB** index becomes dead weight. **Every test stays green**, because they all EXPLAIN
> unnamed statements and cannot see it by construction. That is the repo's signature class:
> a guarantee that stops holding *silently* (#805-3, #842).
>
> The plan choice is **scale-dependent** — on a 2 000-row table the generic plan *does* pick
> the index. Verifying on a small table will tell you the opposite of the truth.
>
> **Before flipping it:** re-measure *both* cases under `force_generic_plan` at production
> scale; check that a generic plan which *does* choose the index walk doesn't then apply it
> to selective criteria (the opposite catastrophe); and consider a dedicated pool with
> auto-prepare off — or `plan_cache_mode = force_custom_plan` per session — for the browse path.
>
> **The hard guard already exists:** `CompanyWatchBrowseQueryPlanTests.
> GenericPlan_DoesNotUseTheNameIndex_SoMaxAutoPrepareWouldKillIt` seeds 200k rows and pins
> the measured generic-plan choice. If it changes, that test goes red and its failure message
> says what must be re-measured. Full campaign: `docs/reviews/2026-07-14-875-index-campaign.md`.

**d6-suggest-double-hop-no-cache-header `[P3/S]`** — typeahead pays
browser→Next→API per committed keystroke and returns suggestions with no
Cache-Control (`api/jobb/suggest/route.ts:30-31`), so backspace-retype refetches
identical prefixes. Debounce ≥ 300 ms + min-prefix + SuggestPolicy bound the cost.
*Fix:* `Cache-Control: private, max-age=60` on the 200 branch — NOTE: sibling skills
suggest deliberately uses `private, no-store` ("varies per keystroke"), so this
divergence needs a conscious decision.

**d6-theme-context-value `[P3/S, WIP]`** — `value={{ theme, setTheme }}` inline object
(`theme-provider.tsx:134`). Zero mounted `useTheme` consumers today (`ThemeToggle`
rendered nowhere); provider sits in the RSC root layout. *Fix (with dark-mode
re-enable):* `useMemo` the context value.

**d7-applications-updatedat-sort `[P3/S]`** — list/pipeline sort on unindexed
`updated_at` (`GetApplicationsQueryHandler.cs:54-56`, `GetPipelineQueryHandler.cs:42-43`);
only `(JobSeekerId, Status)` exists (+ an unrelated stale-detection partial index).
Per-seeker cardinality is small. *Fix (only if a ratchet fires):* replace with
`(job_seeker_id, updated_at DESC)` or `(job_seeker_id, status, updated_at DESC)`.

**d7-loggingbehavior-2-info-events-per-send `[P3/S]`** — `LoggingBehavior` emits
Handling + Handled at Information per mediator send (`:17,23,34-38`), doubling log
volume; nested sends multiply. ADR 0045 contracts only the ElapsedMs signal. *Fix:*
drop the pre-event `LogHandling` to Debug; keep `Handled ... {ElapsedMs}ms` at
Information.

**d8-hangfire-poll-default + w7-hangfire-pollinterval-floats `[P3/S, same fix]`** —
`QueuePollInterval` is not pinned in `HangfireStorageOptionsFactory.Create`
(`:34-58`) although the factory's own comments (`:39-41`) demand load-bearing values
never float on package defaults. Pinned Hangfire.PostgreSql 1.21.1 defaults to 15 s
plain polling (verified via reflection); Worker runs 4 workers; Api is enqueue-only
(no polling). ~30k idle queries/day — negligible, but doctrine-inconsistent. *Fix:*
pin `QueuePollInterval` (15–30 s; nothing is enqueue-latency-sensitive) with the same
doc-comment treatment as InvisibilityTimeout.

**g3-landing-fetch-no-timeout (residual kernel) `[P3/S]`** — the critic's "landing
forced to dynamic SSR by `no-store`" claim was REFUTED (see section 6), but a small
kernel survived: `fetchLandingStats` (`lib/api/landing.ts:33-35`) has no
`AbortSignal.timeout`, so a hung backend could stall "/" TTFB despite the
fail-fast-to-floor design. *Fix (if pursued):* `AbortSignal.timeout(1000-2000)` on the
fetch. Any static/ISR ambition requires a locale rework (cookie-free default render
or PPR) + a new CTO verdict superseding the 2026-05-23 fetch-cache rejection.

**m8-corpus-comment-seed-discarded `[P3/S — reconciliation, no action]`** — the
exploration seed's "40–43k corpus comment" does not exist;
`JobAdSearchComposition.cs:51` says "~13k description-texter" (a de-TOAST note) and
~54k is consistent across 6 files (Worker backfill workers, `ExtractedTerms.cs:9`,
backfill jobs/options, migration 20260615002034). Recorded so no future audit
re-chases it. *Fix:* none.

**p3-company-lookup-ariabusy-only `[P3/S, WIP]`** — company registry lookup signals
pending via `aria-busy` only (`company-lookup.tsx:145-152`); no `[aria-busy]` CSS
exists anywhere, so sighted users see nothing until the fetch settles. Feature-dark
behind `COMPANY_REGISTRY_ENABLED` (SCB lane). *Fix (when the surface lights up):*
disable the button + "Söker…" label or a skeleton result card.

**p3-typeahead-loading-sr-only `[P3/S]`** — job-search typeahead loading state is
sr-only (`job-ad-typeahead.tsx:278-284`), and during loading `showList` goes false
(`:180-181`) so previously shown suggestions flash out. ADR 0045's 150 ms budget +
300 ms debounce keep the silent window sub-perceptible on healthy networks —
degraded-network polish. *Fix:* visible one-row "Hämtar förslag" skeleton item while
loading; keep the sr-only status.

**w4-snapshot-retry-daytime-drift `[P3/S]`** — the 02:00 snapshot wrapper carries only
`[DisableConcurrentExecution]` (`SyncPlatsbankenSnapshotWorker.cs:25`), so Hangfire's
default 10-attempt retry (backoff to ~7 h) could re-run the ~47k upsert loop into
daytime. Verifier narrowing: ADR 0032 routes dominant failures (truncation, per-item
errors) to graceful completion — only residual uncaught throws (missTracker/auditor
DB failure, circuit-breaker/timeout exceptions) trigger the drift. *Fix:*
`[AutomaticRetry]` with night-window `DelaysInSeconds` or `Attempts = 1` (nightly cron
is the natural retry — SCB precedent `ScbCompanyRegisterSyncWorker.cs:32`).

**w8-scb-saturday-daytime-window `[P3/M, WIP]`** — the weekly SCB population (~11 h
from Sat 06:00 UTC) + its single 600 s full-table sweep UPDATE over ~1.17M rows runs
through Saturday daytime on the shared Postgres (`ScbCompanyRegisterStore.cs:34-35,
116-147`). Write job gated by `ScbRegister:Enabled=false` (verifier correction: NOT
`COMPANY_REGISTRY_ENABLED`, which gates the separate read surface); table is
write-only in prod → no read contention today. *Fix (before enabling company search
in prod):* re-verify the Saturday window against real traffic; consider batching the
sweep UPDATE by `synced_at` ranges.

---

## 5. Mätplan (measurement plan)

Baseline BEFORE any fixes, so wins are provable. Anchored exclusively to instruments
that already exist in the repo; targets are ADR 0045 verbatim — never invented.
Ratchet (observe-only → blocking) only at an explicit Klas GO (ADR 0045 Beslut 6).

1. **NBomber load tests** (`perf/Jobbliggaren.LoadTests`): `LOADTEST_SCENARIOS=all`
   against the local stack vs `BudgetReporter`, diffed against the 2026-06-11
   baseline. **Run by the stack-owner session only** (shared-Postgres rule §6.5).
   Note the missing class (b)/(c)/(d) scenarios — findings m1/m3/m4 close that.
2. **Server-side p95** via `LoggingBehavior` → Seq during a scripted browse+load run:
   compare per-handler p95 against 300/150/400 ms. Use the m7 query snippet (to be
   committed) — until then, the query text is in finding m7.
3. **EXPLAIN ANALYZE** (read-only, dev DB port 5435) on: default browse sort
   (d1-jobads-browse-sort), q-COUNT with/without the GUC (d2-bitmap-count), facet
   counts, match-rank CASE worst case — broad SSYK, no facets (d4-grade-rank),
   employer ILIKE (d5-employer), pipeline query. Attach plans to the respective
   issues before/after.
4. **Lighthouse-CI** per `lighthouserc.json` + manual runs on `/villkor`,
   `/integritet`, `/matchning`, guest pages (until b6/m6 lands URL coverage).
   Targets: LCP < 2.5 s, CLS < 0.1, TBT < 200 ms, page-weight per `budget.json`.
5. **`pnpm build` route table** (First Load JS per route) as the bundle instrument;
   record the i18n share (b1) before/after pruning; re-check after b2/b3/b5/b7.
6. **`dotnet-counters` / `docker stats`** on the Worker during a full JobTech sync:
   working set vs 512 MiB (w2/m5) and jobs/min vs ≥ 200 (m4/w6) — until the trend-log
   findings land and make this log-derivable.
7. **Redis `INFO commandstats`** delta across a scripted browse session to quantify
   the session-store amplification (d2-auth-hot-path) before/after the sliding-write
   throttle.

**Stack safety:** all measurement runs that need the live stack belong to the
stack-owner session; other sessions use Testcontainers or static analysis only.

---

## 6. Föreslås EJ som issues (not proposed — with reasoning)

Killed by adversarial verification (evidence re-read, then refuted or deduped):

| ID | Verdict | Reasoning |
|----|---------|-----------|
| d3-recent-searches-count-fanout | REFUTED | ADR 0060 Beslut 4 explicitly *accepts* the capped-20 sequential count fan-out and rejects the cache/batch fixes; render-path consumers pass `includeCount=false`; the fan-out runs only via the lazy popover-gated counts proxy. |
| d3-pipeline-unbounded-td8 | DUPLICATE → TD-8 | "Unbounded" is false — `GetPipelineQueryHandler.cs:43` caps at `Take(500)` (TD-8's safety valve); TD-8 already documents the cap AND the same remedy (virtualization/lazy-load per status). |
| w3-hibp-write-budget | REFUTED | ADR 0099 already records the ~2 s HIBP worst case as an accepted latency cost explicitly cross-referenced to ADR 0045, with a re-litigation condition (D5) and EventId 5001 as the observation signal — the "fix" already exists. |
| w5-unwrapped-jobs-no-mutex | REFUTED | The claimed trigger (dashboard "Trigger now") does not exist — no `UseHangfireDashboard` anywhere; daily sub-minute idempotent crons cannot overlap; #688 sliding invisibility removes re-fetch duplication. |
| m2-ci-loadtest-runs-nothing | DUPLICATE → TD-89 | True (CI runs `baseline-only` against a non-existent API) but TD-89 documents this exact gap, the same compose-based fix, and the Beslut 6 ratchet dependency. |
| g3-landing-no-store-ssr | REFUTED (residual P3 kept) | "/" is dynamic because `i18n/request.ts:22` awaits `cookies()` (NEXT_LOCALE, ADR 0078) — NOT because of the stats fetch; HeaderStats polling never mounts on the landing (prop-fed `LandingHeader`); fetch-cache was explicitly CTO-rejected 2026-05-23 (`landing.ts:19-23`). Residual: the no-timeout kernel (section 4 P3). |

Kept in the report but NOT proposed as issues (report-only):

- **WIP-dependent findings** (b8, d6-theme, p3-company-lookup, w8): fixes ride their
  feature lanes (dark-mode re-enable, SCB/company-search enablement) — creating
  issues now would collide with lane ownership. Exception proposed: d1-caddy (below)
  because both planning docs carry an error that breaks cutover.
- **Accept-as-designed / trigger-gated P3s** (d3-proxy-refresh, d7-applications-
  updatedat-sort, d5-dek-prefetch, d5-employer-ilike until an FE consumer exists,
  d6-no-stj, g3-residual, m8): documented triggers; no action until the trigger
  fires. Re-audit picks them up via this report.
- **Dedup mapping honored** (from the approved plan): /jobb detail fan-out → **#596**
  (g1 lands as a *new* perf angle referencing it) · pipeline pagination/
  virtualization → **TD-8** · CI loadtest stack → **TD-89** · cold-cache q-COUNT →
  **TD-110** · session pipelining → **TD-23** · raw_payload GIN → **TD-76** · docs
  drift → **#486** · Refit → **#442** · company_register read path → #545/#540/#628/
  #641/#708/#712 · cascade paging → TD-24.

---

## 7. Appendix

### 7.1 Docs-drift corrections (→ #486)

- BUILD.md/docs describe TanStack Query for client mutations/polling; the app uses
  Server Actions + `useTransition`/`useOptimistic` throughout (verified — and it is
  the *better* pattern for perceived performance here). ~~Already tracked as #486;
  no new issue.~~ **Correction 2026-08-02: this finding was never carried by #486.**
  *Measured:* #486 is OPEN, `P3`, `blocked`, and its body enumerates five Low items
  — EF-in-Application, Seq, Resend, AWS-config, the language policy. No TanStack
  item, in the body or the title. *Not measured:* why nobody acted. Either the
  finding was filed somewhere else or it was filed nowhere; the measurement shows
  nowhere, and the reason is not recorded. Consumed by the truth-sync PR #1154,
  which replaced the BUILD.md §3.1 rows and the CLAUDE.md §4 instruction with the
  delivered mechanism.
- `(app)/matchningar/page.tsx:16-18` comment claims the seen-mark is
  "fire-and-forget" while the code deliberately awaits it (`:33-38`) — fixed by
  p2-matchningar. Same contradiction in `company-follows.ts:63-64` vs `:138-158`
  (g1).
- ADR 0050:139-141 + TD-106: the `/api/*` Caddy rule is wrong as written
  (d1-caddy) — amend both.

### 7.2 Corpus reconciliation

The corpus is **~54k active ads** (consistent across 6 files; see m8). The
exploration seed's "40–43k" figure came from a nonexistent comment and is discarded.
`job_ads` ≈ the full active Platsbanken corpus — bounded; it will not 10x. This
grounds the severity hard-rule that corpus-growth-dependent findings cap at P2.

### 7.3 Positives — verified healthy, do not "fix"

The verification pass explicitly confirmed these as non-problems; future sessions
should not spend time on them:

- **No N+1 anywhere checked**: `GetPipelineQueryHandler` is single-query with an
  EXPLAIN-verified EXISTS; typeahead prefix search is index-served
  (`F2SuggestTitlePrefixIndex` btree + trigram GINs).
- **No sync-over-async**: zero `.Result`/`.Wait()` in request paths
  (`TaxonomyReadModel.cs:110` is a completed-task read).
- **HTTP hygiene**: all external clients via `IHttpClientFactory` + resilience;
  `IConnectionMultiplexer` singleton; CancellationToken propagated end-to-end.
- **Session/read hygiene**: `getServerSession` React.cache-deduped; reads skip
  UnitOfWork/audit; `.AsNoTracking()` default; encryption never on filter paths.
- **Perceived-perf strengths**: mutations/forms/dialogs are overwhelmingly INSTANT
  (Server Actions + `useTransition`/`useOptimistic`); modal intercept routes DO have
  `loading.tsx`; /oversikt fetches 8-way parallel; Suspense streaming on /jobb
  in-page search; login form has a proper pending state.
- **Deliberate decisions honored**: field Web Vitals/RUM absence is a documented
  Fas 7 deferral (ADR 0045); API-side compression is subsumed by the Caddy decision
  (d2-compression); `public/` holds only 4 small SVGs (no image-optimization
  surface); prior perf incidents ADR 0032/0042/0043/0062 verified non-regressed.

### 7.4 Audit provenance

- Deep-audit workflow: 68 agents (8 finders → 59 findings; 59 adversarial verifiers →
  54 kept, 5 dropped; 1 completeness critic → 3 gap candidates), ~26 min,
  2026-07-10 morning session. Critic-gap verification: 3 agents, resumed after the
  usage-limit interruption, 2026-07-10 afternoon session.
- Raw structured results (all claims, evidence, verdicts, corrections):
  session workflow state `wf_48bcfcd8-b27` + `wf_76986eb2-752` (gitignored session
  artifacts). This report is the durable synthesis.
- All finder and verifier agents self-reported `claude-fable-5` (schema-enforced
  `model_line`); the main session ran Fable 5 (original session Opus 4.8
  post-fallback with fable subagents, Klas-approved).


