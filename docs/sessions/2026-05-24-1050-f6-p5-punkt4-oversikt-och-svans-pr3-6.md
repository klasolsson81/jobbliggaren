---
session: F6 P5 Punkt 4 — `/oversikt` + svans-PR3-6 + TD-94 eskalerad
datum: 2026-05-24
slug: f6-p5-punkt4-oversikt-och-svans-pr3-6
status: levererad (med Klas-feedback om scope-disciplin)
commits:
  - 0e2bd57 feat(oversikt): F6 P5 Punkt 4 — /oversikt-route + 3 sektioner per HANDOVER-spec
  - c6a018c docs: F6 P5 Punkt 4 — TD-82 stängd + TD-92 lyft + reviews-rapporter + current-work-synk
  - 1222f3d fix(oversikt): F6 P5 P4 svans-PR2 — Klas-feedback adresserad (5 fixar + perf)
  - 9cbff1c docs: F6 P5 P4 svans-PR2 — TD-93/94/95 + CTO-perf-rapport + current-work-synk
  - 29c2026 fix(auth): F6 P5 P4 svans-PR3 — login-redirect ignorera next=/jobb och next=/
  - a9ccc87 fix(recent-searches): F6 P5 P4 svans-PR4 — IncludeCount-parameter skippar slow COUNT för /oversikt (TD-95 stängd)
  - 3c881d4 fix(ux): F6 P5 P4 svans-PR5 — delta-pill auto-clear 8s + per-call timeout för recent-searches
  - e2580ae fix(recent-searches): F6 P5 P4 svans-PR6 — default includeCount=false (TD-94 eskalerad till blocker)
tags:
  - v0.2.62-dev (på `c6a018c`)
  - v0.2.63-dev (på `a9ccc87`)
  - v0.2.64-dev (på `3c881d4`)
  - v0.2.65-dev (på `e2580ae`)
---

# F6 P5 Punkt 4 — `/oversikt` + svans-PR3-6 + TD-94 eskalerad

## Sammanfattning

Sessionen levererade F6 P5 Punkt 4 (Översiktssidan `/oversikt`) per HANDOVER-spec
plus fyra svans-PRs som adresserade Klas post-leverans-feedback. **Klas-direktiv
vid session-slut: scope-disciplin har varit otillräcklig.** För mycket fram-och-
tillbaka, kvickfix-på-kvickfix, inga riktiga agenter inkopplade (dotnet-architect,
perf-test-writer, db-migration-writer), ingen ordentlig discovery innan kod-fixar,
inga web-searches vid osäkerhet. TD-94 + TD-95 ska lösas från grunden i separat
session, inte mer symptom-fixar.

## Vad levererades

### PR1+PR2 — Översiktssidan grundleverans

Per CTO-dom Variant A (agentId `ac1dbfa14aa599e65`): direkt RSC `Promise.all`
mot 6 befintliga endpoints, INGEN ny composer-endpoint, INGEN Worker-cache
(per-user auth-gated → ADR 0064-mönstret EJ tillämpligt; ADR 0048 Beslut (b)
täcker regeln).

**Tre sektioner per HANDOVER-oversikt.md (Klas-godkänd 2026-05-23):**
Title+I dag-kort / Notiser (Kräver åtgärd + Information) / Sammanfattning
(Ansökningar + Bevakning + Underlag).

**Filer (10 nya + 3 ändrade i PR1):**
- `app/(app)/oversikt/page.tsx` (RSC entry, force-dynamic)
- `components/oversikt/{oversikt-page,today-card,notice-list,notice-row,summary,summary-row}.tsx`
- `lib/oversikt/{mock-data,aggregations,aggregations.test}.ts` (HANDOVER §3.7 centraliserad mock)
- `globals.css` (+420 LoC verbatim-klonad CSS från v3-källan rad 1462-1900)
- `app-shell.tsx` (Översikt FÖRSTA nav-item additivt)
- `middleware.ts` (`/oversikt` + `/jobb`/`/sokningar`/`/sparade` i `PROTECTED_PREFIXES`)

**TDs i PR1+PR2:**
- TD-82 STÄNGD — flyttad till arkivet med stängningsnotat
- TD-92 LYFT (Major × F6 P5-fas-stängning) — rate-limit på 5 endpoints

### Klas post-leverans-feedback (6 punkter)

1. **Default-route-byte** — vill login → `/oversikt`
2. **Notice-persistens** — dismissad notis kom aldrig tillbaka
3. **"Senaste sökning" tom på /oversikt** trots att text-search "systemutvecklare" gjordes
4. **HeaderStats borta + 28 vs 9 mismatch** (mock vs riktig)
5. **"Sökstart" oklart** — vad menas?
6. **/oversikt 10s+ loadtime** — INTE cold

### svans-PR2 (commit `1222f3d` + `9cbff1c`)

Adresserade 5 av 6 feedback-punkterna:

1. Default-route-byte: `safeRedirectPath` → `/oversikt`, brand-link → `/oversikt`
2. Notice-IDs med datum-suffix (Variant A — Klas-val)
3. HeaderStats route-villkorlig render (dolde på /oversikt — fel beslut, reverterat i PR3)
4. HeaderStats kvar ja/nej + 28-vs-9 — flyttades till PR3
5. "Sökstart" → "Aktiv sedan" copy
6. PERF: bytt `getJobAds()` → `fetchLandingStats()` för "Aktiva annonser totalt"
   (samma värde, Worker-precomputed 0-1ms vs ~1.2s ListJobAdsQuery)

**TDs lyfta i svans-PR2:** TD-93 (matching mot CV/kriterier — Klas-direktiv
"behåll mock så länge"), TD-94 (`ListJobAdsQuery` perf — Major × F6 P5-fas-
stängning), TD-95 ("Senaste sökning" tom — discovery + verify behövs).

**CTO-rond svans-PR2** (agentId `ad37955db80099f19`) — Variant (c) discovery-
first, behåll cache-strategi, perf blockerar EJ tag-push.

### svans-PR3 (commit `29c2026`)

Klas-feedback: "loggade in, kom direkt till jobb". Rotorsak: middleware-redirect
flöde `unauth user på /jobb → /logga-in?next=/jobb` → safeRedirectPath
respekterade `next=/jobb` → login landade på /jobb.

**Fix:** `HOME_REDIRECT_PATHS = {"/", "/jobb"}` hoppas över i safeRedirectPath
och defaultar till `/oversikt`. Andra deep links respekteras.

### svans-PR4 (commit `a9ccc87`) — **CloudWatch-discovery äntligen**

Klas-feedback: "/oversikt fortfarande 7-10s, INTE COLD. Senaste sökning tom."

**ROTORSAK via CloudWatch-discovery:**
```
System.OperationCanceledException: Query was cancelled
  ---> Npgsql.PostgresException 57014: canceling statement due to user request
       at ListRecentSearchesQueryHandler.Handle:line 60
```

`ListRecentSearchesQueryHandler:60` har avsiktlig N+1 (CTO 2026-05-20 Variant A,
cap=20) över `IJobAdSearchQuery.CountAsync` — samma slow COUNT som TD-94 rot.
Cap=20 × ~1.5s = 7.5s totalt sekventiellt → FE-timeout 8s → Postgres 57014 →
handler kastar → FE faller till tom array.

**Fix:** `ListRecentSearchesQuery(bool IncludeCount = true)` parameter +
handler-condition + endpoint `?includeCount=false` + FE `getRecentSearches(false)`
på `/oversikt`. `/jobb`-hero-chip behåller default `true` för "(N nya)".

**Försök 1 reverterat innan commit:** Task.WhenAll-parallellisering skulle krasch
EF Core (JobAdSearchQuery använder samma IAppDbContext). Verifierat via
JobAdSearchQuery.cs:29 läsning.

**TD-95 STÄNGD** — flyttad till tech-debt-archive.md med full rot-analys.

### svans-PR5 (commit `3c881d4`)

Klas-feedback: (1) delta-pill stannar permanent, (2) /sokningar + hero-chip
fortfarande "Inga senaste sökningar".

**Fix 1:** Delta-pill auto-clear via `setTimeout(8000)` + ref-baserad timer +
cleanup vid unmount.

**Fix 2:** Per-call timeout baserat på includeCount: 8s (compact) / 25s
(med count). /sokningar + /jobb hero-chip använder 25s.

### svans-PR6 (commit `e2580ae`) — **TD-94 eskalerad till blocker**

Klas-feedback: "/sokningar fungerar inte heller + lista tom igen efter klick".

**CloudWatch-bevis (ListRecentSearchesQueryHandler last 15min):**
- 409ms PASS (includeCount=false, /oversikt)
- 24896ms FAIL
- 17185ms PASS
- 22291ms PASS
- 25510ms FAIL

p50 15-22s med includeCount=true, max >25s med low-selectivity Q ("AI" → 1925,
"lärare" → 11293). Min PR5 25s-timeout räcker inte. Cascade-fel: /sokningar
drar ECS busy → /jobb's `getRecentSearches(true)` också timeout → hero-chip tom.

**Fix:** Default `getRecentSearches(includeCount = false)` för alla konsumenter.
Hero-chip + /sokningar visar bara namn UTAN "(N nya)"-affordance.

**TD-94 eskalerad: Fas-stängning → Fas Nu.** Blocker för "(N nya)"-restoration.

## Klas-direktiv vid session-slut (kritiskt)

> "Det har varit väldigt mycket fram och tillbaka i denna sessionen, du har
> fixat, det har blivit fel, inga agenter har kopplats in, ingen riktig
> discovery har gjorts, inga webbsökningar har gjorts ifall du varit osäker."

**Erkända brister:**
- `dotnet-architect` INVOKERADES ALDRIG för perf-discovery/index-strategi/EF-query-optimering
- `db-migration-writer` INVOKERADES ALDRIG för potentiella composite-index
- `perf-test-writer` INVOKERADES ALDRIG för NBomber-scenarier
- Inga `WebFetch`/`WebSearch` vid osäkerhet om EF Core 10 COUNT-optimering, Postgres FTS-tunings, EF DbContext parallelism
- CTO-rond invocerades 2 ggr (Variant A för Punkt 4, perf-incident-rond) men jag följde inte "discovery-first"-direktivet ordentligt — gick direkt till kod-fix istället för att verifiera rotorsaken via CloudWatch INNAN PR4
- Quickfixes byggdes på rad (PR4 → PR5 → PR6) eftersom rotorsaken (TD-94) inte adresserades
- Klas behövde själv visual-verifiera 3 gånger för att hitta att fix:arna inte räckte

## Reviews (sparade per CLAUDE.md §9.2)

- `docs/reviews/2026-05-24-f6-p5-punkt4-oversikt-cto.md` (agentId `ac1dbfa14aa599e65`)
- `docs/reviews/2026-05-24-f6-p5-punkt4-code-reviewer.md` (agentId `af16f6c15c1b431ca`)
- `docs/reviews/2026-05-24-f6-p5-punkt4-security-auditor.md` (agentId `a11074672eb69e526`)
- `docs/reviews/2026-05-24-f6-p5-punkt4-design-reviewer.md` (agentId `a5927ef8836d080f0`)
- `docs/reviews/2026-05-24-f6-p5-punkt4-svans-pr2-cto-perf.md` (agentId `ad37955db80099f19`)

## Tester

- vitest 70 filer / 676→683 PASS (+7: aggregations + filterFutureDeadlines + formatDaysAgo + findRecentInterviews-edge)
- .NET Domain 404 + Application 578 + Architecture 78 = 1060 PASS (genom alla 6 svans-commits)
- pnpm build PASS (`/oversikt` listad som `ƒ Dynamic`)
- gitleaks: no leaks alla pushar

## TDs efter sessionen

**Stängda:**
- TD-82 (Översikt/Dashboard-sida) — arkiverad
- TD-95 ("Senaste sökning" tom) — arkiverad med rot-analys

**Lyfta:**
- TD-92 (rate-limit på 5 auth-gated GET-endpoints) — Major × F6 P5-fas-stängning
- TD-93 (matching mot CV/kriterier) — Minor × Trigger
- **TD-94 (ListJobAdsQuery perf) — Major × Fas Nu** — eskalerad från fas-stängning till blocker. Blocker för "(N nya)"-restoration på hero-chip + /sokningar.

## Kvar att lösa (nästa session)

**TD-94 + TD-95-relaterade buggar som svans-PR6 endast symptom-mitigerar:**

1. **"(N nya)"-affordance** borta från hero-chip + /sokningar (förlorad i PR6)
2. **Stale recent-searches på /jobb hero-chip** — sökning syns inte direkt efter
   sök-action (Promise.all-race i parent vs child Suspense)
3. **ListJobAdsQuery perf-rot** — p50 1.2s, max 6.7s (ADR 0045 4-22x över budget)
4. **ListRecentSearchesQuery perf-rot** — samma cascade-symptom
5. **`/jobb`-listsidan seg** — direkt konsekvens av ListJobAdsQuery

**Rotorsak (CloudWatch-discovery):** COUNT(*) över 46k+ JobAds-rader (FTS-tsvector
+ JsonbContains-filter) tar 1-3s baseline. Behöver:
- dotnet-architect-rond: EXPLAIN ANALYZE mot dev-korpus, index-strategi
- db-migration-writer: composite-index för (status, deleted_at, published_at) + ev. materialized COUNT-vy
- perf-test-writer: NBomber-scenarier `list_job_ads_p95` per ADR 0045 fitness function
- web-searches: EF Core 10 + Npgsql 10 COUNT-optimering best practices, Postgres FTS-prestanda

## Nästa session-direktiv från Klas

> "Skapa en ordentlig startprompt där vi löser både TD-94 och TD-95 från grunden.
> Inga quickfix som blir fel osv."

TD-95 kvarstår implicit — även om symptom-bug är "stängd" så är "(N nya)"-
affordance förlorat. Återställs när TD-94 löst rotorsaken.

## Operativa events under sessionen

| Tag | Commit | Deploy-run |
|---|---|---|
| v0.2.62-dev | c6a018c | success |
| v0.2.63-dev | a9ccc87 | success |
| v0.2.64-dev | 3c881d4 | success |
| v0.2.65-dev | e2580ae | in_progress (vid session-slut) |
