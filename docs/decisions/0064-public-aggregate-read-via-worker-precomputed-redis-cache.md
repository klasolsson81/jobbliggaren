# ADR 0064 — Publik anonym aggregat-read via Worker-precomputed Redis-cache

**Datum:** 2026-05-23
**Status:** Accepted
**Kontext:** F6 P5 Punkt 3 — publik landing-stats (`GET /api/v1/landing/stats`) som första publika anonyma rekurrerande read-endpoint i JobbPilots kodbas. Mönstervalet blir därför en arkitekturprecedens, inte en lokal fix — `docs/current-work.md` rad 18 anger explicit att samma mönster ska återanvändas av F6 P5 Punkt 4 `/oversikt`.
**Beslutsfattare:** senior-cto-advisor (agentId `a1da26dc2029a5def` — multi-approach-triage 2026-05-23, Variant B vald över A/C/D); Klas Olsson (Accepted-flip-GO 2026-05-23, eventuella prosa-justeringar applicerar adr-keeper); Claude Code (ADR-leverans denna session, explicit Klas-override av CLAUDE.md §9.4 webb-Claude-verbatim per memory `feedback_klas_can_override_adr_verbatim_source` — substansen grundad i CTO-dom + implementationen redan committed HEAD `e6b08fa`).
**Relaterad:** ADR 0023 (Worker-isolation från ASP.NET HTTP-bagage), ADR 0042 Beslut C (SuggestPolicy least-common-mechanism-precedens för dedikerad rate-limit), ADR 0043 (taxonomi-singleton-cache auth-gated — skiljelinje), ADR 0044 (gate-mönster: observe-only Fas 1 + ratchet-väg), ADR 0045 Beslut 1 klass (a) (300 ms p95 hot-path-budget), ADR 0048 Beslut (b) (port-mönster vs in-handler-join — denna ADR är komplementär axel), ADR 0056 Beslut 4 (utbytespunkt `getLandingStats()` lyft via [Amendment 2026-05-23](./0056-landing-v3-shell-and-live-stats-placeholder.md#amendment-2026-05-23--live-stats-beslut-4-lyft-implementation-byts-utbytespunkt-bevarad)), ADR 0063 (per-user-overlay batch-port — privat read-skiljelinje på annan axel). Relaterade: CLAUDE.md §2.3 (CQRS — read-DTO ut, ingen Domain-objekt över gränsen), §3.6 (`.AsNoTracking()` default + projektion), §3.3 (DTO = `record class`), §5.4 (säkerhet — anonym DoS-yta).

> **Livscykel-/proveniens-not:** Skriven 2026-05-23 av Claude Code (adr-keeper) på explicit Klas-begäran — medveten override av CLAUDE.md §9.4 webb-Claude-verbatim-konventionen (memory `feedback_klas_can_override_adr_verbatim_source`). Besluts-substansen är transkriberad från senior-cto-advisor-dom 2026-05-23 (agentId `a1da26dc2029a5def`, Variant B-val över A/C/D) + verifierad mot redan committed implementation (HEAD `e6b08fa`). Inga nya beslut konstruerade. Status **Accepted** per (a) CTO-dom låst substans, (b) implementation redan i `main`, (c) memory ger explicit Klas-override.

---

## Kontext

F6 P5 Punkt 3 levererar publik landing-stats — `GET /api/v1/landing/stats` returnerar aggregaten `{ activeCount, newToday, isStale }` som renderas i `<LandingTopbar />` (jfr ADR 0056 Beslut 4). Detta är **första gången** en publik anonym rekurrerande read-endpoint möter kodbasen. Mönstervalet blir därför en arkitekturprecedens — `docs/current-work.md` rad 18 anger explicit att F6 P5 Punkt 4 `/oversikt` återanvänder samma mönster.

Den publika anonyma read-ytan introducerar en **ny axel** mot de två existerande overlay-axlarna:

- **ADR 0063** etablerade *privat per-user-overlay* via batch-port (`POST /me/job-ad-status` — auth-tolerant men per-user-räknad).
- **ADR 0043** etablerade *auth-gated singleton-cache* för taxonomi-läsning (Application-lager singleton, snapshot-versionerad).
- **ADR 0064** (denna) etablerar *publik anonym aggregat-read* — separat klass från båda ovan. Vi har därmed tre komplementära avgränsningar till ADR 0048 Beslut (b) port-vs-join-regeln på tre ortogonala axlar (bounded-context, provider-assembly, publik↔privat-domän) plus den nya: **publik anonym hot-path med pre-compute-disciplin**.

Krafter som spelar in:

- **Hot-path-budget (ADR 0045 Beslut 1, klass (a) 300 ms p95):** landing-routen är topp-trafik (anonym + inloggad), endpointen måste klara p95 < 50 ms cache-read för att inte regressera mot perf-budget-vakten. En cache-aside med "first miss räknar live" (Variant A) ger spikes på cold-start och cache-evict — oacceptabelt.
- **Stampede-risk (Nygard 2018 kap. 5):** vid cache-expiry på hot anonym endpoint genererar varje konkurrerande request en räkne-query mot PostgreSQL. Stampede-control via lock/single-flight finns inte i `IDistributedCache`-yta — pre-compute eliminerar problemet vid roten (queries körs av en enskild Worker-process, alltid på schemalagd tidpunkt).
- **DDD bounded-context-isolation (Evans 2003 kap. 14):** landing är en marknadsförings-/akquisitions-yta. Att låta dess endpoint utlösa join över MV (`JobAds`)-tabellen i hot-path drar marknadsförings-trafik genom MV-aggregatet — fel sida av context-gränsen. Worker-läget renderar aggregaten en gång, levererar via dedikerad read-modell.
- **DIP (Martin 2017 kap. 11):** cache-policy hör hemma där datat genereras, inte i frontend-RSC. Att låta Next.js `revalidate`-fetcha Platsbankens MV (Variant D) inverterar dependency-riktningen — frontend skulle bli ansvarig för cache-semantik som Domain/Application ska kontrollera. Avvisat på principnivå även innan operational visibility räknas in.
- **Operational visibility (12-Factor §XI):** Hangfire dashboard ger redan synlighet i `RefreshLandingStatsJob`-jobbets last-success, failure-count, duration. Cache-aside i handler döljer den signalen i log-aggregat — sämre obsability för en hot-path-mekanik som ska kunna inspekteras vid incident.
- **DoS-yta för anonym trafik (Saltzer & Schroeder 1975 — least common mechanism):** en publik anonym endpoint får inte återanvända en rate-limit-policy som är designad för UserId-partitionerad inloggad trafik. `ListReadPolicy` (UserId-partitionerad fixed-window) faller till `NoLimiter` för anonyma — inget skydd. Precedens från ADR 0042 Beslut C (`SuggestPolicy` dedikerad för typeahead-yta) och de existerande publika ytorna `WaitlistSignupPolicy` + `InvitationRedeemPolicy` (IP-partitionerade).
- **Worker-isolation (ADR 0023):** Worker körs som separat composition root utan ASP.NET HTTP-bagage. Schemalagda jobb skriver till `IDistributedCache` via Application-port — handlern läser samma port. Ingen direkt Worker→Api-koppling, ren cache-rendezvous.

## Beslut

> Beslut fattat av senior-cto-advisor (agentId `a1da26dc2029a5def` 2026-05-23 multi-approach-triage, Variant B vald över A/C/D). Status **Accepted** per CTO-låst substans + Klas explicit Accepted-flip-GO.

### Beslut (a) — Variant B = godkänt mönster: Worker-precomputed Redis-cache + Floor-fallback

> **⚠ AMENDAD 2026-07-13 (CTO-bind, verdict A′) — se [Amendment 2026-07-13 — Golvet var fel från dag ett: cache-miss svarar okänt, aldrig ett påhittat tal](#amendment-2026-07-13--golvet-var-fel-från-dag-ett-cache-miss-svarar-okänt-aldrig-ett-påhittat-tal) nedan. Originaltexten nedan bevaras oförändrad; amendment-lagret gäller punkt 2:s Floor-fallback-semantik (`LandingStatsFloor`/`Floor`-konstanten, `ActiveCount: 40_000, NewToday: 0`) — RADERAD. Cache-miss returnerar numera `LandingStatsDto.Unknown` (`null`-tal, `IsStale: true`). Variant B:s arkitektur i punkt 1 och 3 (Worker-precompute, stampede-frihet) är OBERÖRD.**

> **⚠ AMENDAD 2026-07-28 (Klas-direktiv) — se [Amendment 2026-07-28 - The Swedish day boundary, not UTC; and `created_at` was never implemented](#amendment-2026-07-28---the-swedish-day-boundary-not-utc-and-created_at-was-never-implemented) nedan. Originaltexten nedan bevaras oförändrad; amendment-lagret gäller punkt 1:s `newToday`-klausul: (a) dygnsgränsen är `Europe/Stockholm`, inte UTC, och (b) kolumnen var och är `published_at` — `created_at` i originaltexten var en transkriptionsmiss i ADR:n, aldrig implementerad kod. Variant B:s arkitektur i punkt 1:s Worker-precompute-mekanik och punkt 3 (stampede-frihet) är OBERÖRD.**

Publik anonym aggregat-read levereras via följande tre-deladhet:

1. **Worker-jobb (`RefreshLandingStatsJob`)** registrerat som Hangfire `RecurringJob` med cron `*/5 * * * *` UTC. Jobbet beräknar aggregaten (`activeCount = SELECT COUNT(*) FROM job_ads WHERE status='Active' AND deleted_at IS NULL`, `newToday = ... AND created_at >= date_trunc('day', now() AT TIME ZONE 'UTC')`) och skriver resultatet till Redis via Application-port `ILandingStatsCache` (`SetAsync(LandingStatsSnapshot, TimeSpan ttl)`).
2. **Api-endpoint (`GET /api/v1/landing/stats`)** är ren cache-read via samma `ILandingStatsCache.GetAsync()`. Vid cache-miss eller cache-fel returneras `LandingStatsFloor`-konstant (hardcoded konservativa värden, `IsStale = true`-flagga). Endpointen får **aldrig** köra COUNT-query in-line — pre-compute-disciplinen är load-bearing.
3. **Cache-key versionerad** (`landing:stats:v1`). TTL = **12× refresh-fönstret** (60 min vid 5 min cron) — TTL > refresh-intervall är defensivt mot enskild Worker-jobb-miss; vid längre Worker-stillestånd levererar Api ändå senaste snapshot tills TTL löper ut, sedan Floor-fallback med `IsStale = true`.

Två separata read-vägar (Worker writes, Api reads) håller varje pipeline ren mot sitt ansvar. CQRS-segregeringen (Martin 2017 kap. 23) är applikatorisk på en högre nivå än ADR 0063 — där handler räknar två queries i samma request, här räknar Worker en gång och levererar via cache.

### Beslut (b) — Dedikerad rate-limit-policy `LandingPublicReadPolicy` (IP-partitionerad fixed-window)

Publik anonym yta får INTE återanvända `ListReadPolicy` (UserId-partitionerad fixed-window som faller till `NoLimiter` för anonyma — inget skydd för publik DoS-yta). Precedens från ADR 0042 Beslut C (`SuggestPolicy`) och existerande publika ytor (`WaitlistSignupPolicy` + `InvitationRedeemPolicy`).

- **Partition:** request-IP (via `HttpContext.Connection.RemoteIpAddress`, normaliserad mot X-Forwarded-For när vi sitter bakom ALB/Cloudflare per ADR 0050 deferred + befintlig `ForwardedHeadersOptions`).
- **Fönster:** fixed-window 1 minut.
- **Limit:** 60 req/min/IP. Klas-låsbart värde — initialt headroom för normal browser-trafik (page-refresh + revalidate) utan att tappa DoS-spärr.

Saltzer & Schroeder (1975) "least common mechanism" är direkt tillämplig: anonym IP-trafik och inloggad UserId-trafik delar inte rate-limit-bucket, även om de bägge är "read-only". Sammanblandning gör att en anonym DoS-burst sänker inloggade användares quota — fel inkapsling.

### Beslut (c) — Cache-Control: public, max-age strikt mindre än Worker-refresh-fönstret

Api-response sätter `Cache-Control: public, max-age=30` (eller motsvarande tidsfönster < refresh-fönstret) så CDN/proxy/BFF (Vercel edge, framtida Cloudflare-cache per ADR 0050) får absorbera repeat-trafik utan att träffa origin. **Strikt mindre än Worker-refresh-intervallet** (5 min) — annars riskerar frontend att rendra data som Worker redan invaliderat.

`max-age=30` ger frontend ett 30 sek-fönster av delad cache, vilket täcker normal navigations-burst utan att data upplevs som inaktuell.

### Beslut (d) — Avgränsning mot ADR 0063 och ADR 0043 (publik↔privat axel som ny komplementär klass)

ADR 0048 Beslut (b) säger: *"extern/översatt/context-korsande → port; intern/enkel/samma-DbContext → in-handler-join"*. Vi har sedan utvidgat den i tre komplementära avgränsningar:

- **ADR 0043** — bounded-context-gräns (taxonomi-ACL): port + auth-gated singleton-cache.
- **ADR 0062 Beslut 4** — provider-assembly-axel (Npgsql-FTS): port + Infrastructure-implementation.
- **ADR 0063** — publik↔privat-domän över public-cacheable list-projektion: dedikerad batch-port per request.

ADR 0064 lägger ett **fjärde komplementärt ben:** *publik anonym hot-path med rekurrerande aggregat-read*. Mönstret är annorlunda från ADR 0063 (där varje request räknar per-user) — här räknar Worker en gång för alla läsare. Den gemensamma principen är att read-vägen flyttas ut ur hot-path-handlern.

ADR 0064 **superseder inte** någon ADR. Den lägger ett fjärde exempel på "när port-mönstret gäller även när båda aggregaten delar `IAppDbContext`" till de tre som redan finns. Beslutsregeln framåt:

1. Bounded-context-gräns med anti-corruption (ADR 0043).
2. Provider-assembly-axel (ADR 0062 Beslut 4).
3. Publik↔privat domän över public-cacheable list-projektion (ADR 0063).
4. Publik anonym hot-path med rekurrerande aggregat-read (denna ADR — Worker-precompute + dedikerad rate-limit + Floor-fallback).

In-handler-join (ADR 0048 Beslut b) gäller fortsatt för **enkla samma-DbContext 1:0..1-aggregatlänkar utan någon av ovanstående axlar**.

## Alternativ som övervägdes

### Variant A — Cache-aside i handler (read-through, in-handler COUNT vid miss) (AVVISAT)

**För:**
- En komponent (handler) sköter cache-write + read.
- Enklare diagram (ingen separat Worker-mekanik).

**Emot:**
- **Stampede-risk vid cache-expiry (Nygard 2018 kap. 5):** varje konkurrerande request vid cache-miss kör COUNT mot MV. Hot anonym endpoint = stampede-amplifikation. `IDistributedCache` saknar single-flight-primitiv.
- **Cold-start-spike:** första request efter deploy/cache-evict tar full COUNT-latens — bryter ADR 0045 Beslut 1 klass (a) 300 ms p95.
- **Operational visibility:** failure-cases gömda i log-aggregat. Ingen Hangfire dashboard-row att inspektera.
- **DDD-context-läckage (Evans 2003):** landing-handlern blir indirekt ansvarig för MV-aggregatets read-yta. Worker-läget håller marknadsförings-trafik och MV-aggregat skilda.

### Variant B — Worker-precomputed Redis-cache + Floor-fallback (VALT)

**För:**
- Inga stampede-spikes — Worker räknar en gång per fönster, oavsett trafik.
- Hot-path är ren cache-read (~ms-nivå), välbeskaffad mot ADR 0045 p95-budget.
- Operational visibility via Hangfire dashboard (last-success, duration, failure-count).
- Floor-fallback ger graceful degradation vid Worker-stillestånd (`IsStale = true` är synlig flagga, inte tystnad).
- Mönster-precedens för F6 P5 Punkt 4 `/oversikt` och framtida publika aggregat-ytor.

**Emot:**
- Två rörliga delar (Worker-jobb + Api-endpoint) istället för en. Mitigering: `ILandingStatsCache`-porten är trivial, Worker-jobb är ~20 rader Hangfire-recurring.
- Schemalagd 5 min-fördröjning innebär att `newToday` inte är realtid. Mitigering: acceptabelt för landing-stats (marknadsföring, inte transaktionellt); framtida finkornighet är konfig-byte (cron-uttrycket).

### Variant C — PostgreSQL materialiserad vy med `REFRESH MATERIALIZED VIEW CONCURRENTLY` (AVVISAT)

**För:**
- DB-native, ingen Worker-process.
- `CONCURRENTLY`-läget undviker lock på reads.

**Emot:**
- **Operational visibility svagare** än Hangfire — refresh-failure syns i `pg_stat_activity` men inte i en dashboard-rad.
- **CONCURRENTLY kräver unique index** på MV — overhead för en simpel `{ activeCount, newToday }`-aggregation.
- **Hot-path träffar fortfarande PG** vid varje request — vi byter live-COUNT mot live-MV-SELECT, vinsten är endast index-stöd. Redis-read är fundamentalt billigare än PG-roundtrip för en frontend hot-path.
- **TTL-/staleness-semantik måste handcrafted** mot `pg_stat_user_tables`-tidsstämpel. `IsStale`-flagga blir DB-side logik istället för Application-side, sämre testbart.

### Variant D — Next.js `fetch(..., { next: { revalidate: 300 } })` direkt mot Platsbanken-källan (AVVISAT)

**För:**
- Inget backend-arbete. Frontend revalidate-poll mot extern källa.

**Emot:**
- **Bryter ADR 0030 frontend↔backend-API-konvention** — frontend skulle bypassa JobbPilots Api och anropa Platsbanken JobTech-API direkt. Hela datakontrakt-disciplin (DTO-zod-schemas, ADR 0020) går förlorad för denna yta.
- **DIP-invertering (Martin 2017 kap. 11):** Frontend skulle vara ansvarig för cache-policy som Domain/Application ska kontrollera.
- **Saknar Floor-fallback-semantik** — vid Platsbanken-incident faller frontend till fetch-error utan IsStale-graceful-degradation.
- **Ingen rate-limit-skydd** — vi delar ut vår frontend-IP-pool mot Platsbankens API-quota. Klassisk SSRF-/quota-exhaustion-yta.

## Konsekvenser

### Positiva

- Hot-path `GET /api/v1/landing/stats` läser ren Redis-cache — välbeskaffad mot ADR 0045 Beslut 1 klass (a) 300 ms p95-budget med marginal.
- Stampede-risk eliminerad vid roten (Worker räknar en gång, ej per request).
- Operational visibility via Hangfire dashboard — last-success, duration, failure-count.
- Floor-fallback ger synlig graceful degradation (`IsStale = true`) — inte tystnad vid Worker-stillestånd.
- Dedikerad `LandingPublicReadPolicy` skyddar publik anonym DoS-yta utan att läcka rate-limit-bucket till UserId-policies (Saltzer & Schroeder 1975).
- `Cache-Control: public, max-age=30` ger CDN/proxy-absorption utan att överskrida Worker-refresh-fönstret.
- Mönster-precedens dokumenterad: F6 P5 Punkt 4 `/oversikt` och framtida publika aggregat-ytor har klar väg framåt.
- ADR 0048 Beslut (b)-regeln utvidgad explicit till publik anonym hot-path-axel — beslutsregeln framåt är inte godtycklig nästa gång.
- ADR 0056 Beslut 4 utbytespunkt (`getLandingStats()`) realiserad utan kontrakt-brott — sync→async signatur-byte är frontend-isolerad.

### Negativa

- **Två rörliga delar** (Worker-jobb + Api-endpoint) istället för en. Mitigering: `ILandingStatsCache`-porten är trivial (`GetAsync` / `SetAsync`), Worker-jobb är `RecurringJob` med två SQL-queries.
- **5 min-fördröjning på `newToday`-värdet.** Mitigering: acceptabel för landing-stats (marknadsföring, inte transaktionellt). Cron-uttrycket är konfig-byte om finkornighet behövs.
- **Ny rate-limit-policy att underhålla.** Mitigering: triviall partition-key-byte (UserId→IP) jämfört med existerande policies; pattern repeteras för framtida publika ytor.
- **POST-på-läs är HTTP-mässigt mindre cacheable** — irrelevant här, endpointen är GET. `Cache-Control: public, max-age=30` levererar CDN-absorption.

> **⚠ AMENDAD 2026-07-28 — se [Amendment 2026-07-28](#amendment-2026-07-28---the-swedish-day-boundary-not-utc-and-created_at-was-never-implemented) nedan.** Ovanstående punkts 5 min-cron-fördröjning på `newToday` gäller oförändrad i mekanism, men syns nu vid ett nytt och mer märkbart ögonblick: mellan svensk midnatt och Workerns nästa körning visar landningssidan i upp till 5 minuter gårdagens "nya idag"-tal. Ingen ny fördröjning introduceras — samma fördröjning som redan accepterades ovan blir bara synlig vid en annan tidpunkt.

### Mitigering

> **⚠ AMENDAD 2026-07-13 — se Amendment 2026-07-13 nedan.** Nästa bullet-punkt (om `LandingStatsFloor`-värden och "Klas-låsbara konstanter") beskriver en mekanism som är BORTTAGEN (CTO-bind, verdict A′) — det finns inga floor-konstanter kvar att låsa eller granska. Originaltexten nedan bevaras oförändrad för historiskt spår; Worker warm-start (Amendment punkt 6) är den nya mitigeringen mot ett långvarigt omätt läge.

- Worker-failure-detektion lyfts som operativ punkt — Hangfire dashboard ska larma vid `RefreshLandingStatsJob` failure > 3 i rad. Lyfts som TD om inte redan täckt av befintlig Hangfire-alarm-policy (ADR 0023-relaterad).
- `LandingStatsFloor`-värden måste vara realistiska men konservativa — om Worker har varit nere i timmar och Floor visar 45 580 / 312 (= målbild) ger det falsk visshet. Klas-låsbara konstanter, granskas vid landing-content-uppdatering.
- `LandingPublicReadPolicy` 60/min/IP är initialt headroom. Vid observed regression (legitima browser-bursts blockas) justeras värdet — ratchet-mönster per ADR 0044 (observe-only Fas 1, blocking gate vid Klas-GO).

## Implementation

Implementerad och committed i HEAD `e6b08fa` (F6 P5 Punkt 3 PR1, 2026-05-23):

- **Backend (Application-lager):** `ILandingStatsCache`-port (`GetAsync` / `SetAsync` / `LandingStatsSnapshot`-DTO). `GetLandingStatsQuery` + `GetLandingStatsQueryHandler` (ren cache-read med Floor-fallback). FluentValidation ej applicerbar (parametrelös query). Pipeline-behaviors per ADR 0008-ordningen (Logging→Validation→Authorization→UoW; Authorization-behavior no-op för anonym).
- **Backend (Infrastructure-lager):** `RedisLandingStatsCache` implementerar `ILandingStatsCache` mot `IDistributedCache` (Microsoft.Extensions.Caching.StackExchangeRedis-providern). Cache-key `landing:stats:v1`, TTL = 1h (12× refresh-fönstret).
- **Backend (Worker-lager):** `RefreshLandingStatsJob` registrerat som Hangfire `RecurringJob` med cron `*/5 * * * *` UTC i Worker composition root (ADR 0023). Jobbet kör två `.AsNoTracking()`-queries (`activeCount`, `newToday`) och skriver `LandingStatsSnapshot` via `ILandingStatsCache.SetAsync`.
- **Backend (Api-lager):** `LandingEndpoints.cs` — `MapGet("/api/v1/landing/stats")` utan `.RequireAuthorization()`, med `[EnableRateLimiting("LandingPublicReadPolicy")]`. Response sätter `Cache-Control: public, max-age=30`.
- **Backend (rate-limit):** `LandingPublicReadPolicy` i `RateLimitingExtensions.cs` — IP-partitionerad fixed-window, 60 req/min/IP. Partition-key tar `HttpContext.Connection.RemoteIpAddress` med X-Forwarded-For-respekt via befintlig `ForwardedHeadersOptions`.
- **Frontend:** `getLandingStats()` (ADR 0056 Beslut 4 utbytespunkt) byts från sync hårdkodad konstant till async `fetch('/api/v1/landing/stats')` i RSC-context. Yta bevarad (prop-driven konsumtion i `<LandingTopbar />`); enbart implementation byts. `IsStale = true` renderar samma värden visuellt men med subtil indikator (dokumenteras i UI-skill om/när Klas låser ut formen).
- **⚠ AMENDAD 2026-07-13:** se Amendment 2026-07-13 nedan. Indikatorn i bulleten ovan byggdes aldrig — `landing-stats.ts` motiverade i stället tystnaden med att "HANDOVER §6.4 nämner ingen sådan affordans", ett FAS-DEFERRAL-handover-dokuments tystnad citerad som skäl att inte bygga vad denna Accepted ADR redan specificerat. Amendmentet gör den specifika frågan moot (det finns inget tal kvar att flagga vid cache-miss — hela stat-gruppen utelämnas eller visas som streck), men lärdomen protokollförs: en ADR:s Implementation-sektion är inte ett förslag, och ett handover-dokuments tystnad övertrumfar inte en Accepted ADR.
- **Gates:** code-reviewer + dotnet-architect på handler + Worker-jobb (ADR-precedens-respekt: AsNoTracking, ingen Repository, port-mönster, Worker composition root). security-auditor på rate-limit-policy + anonym DoS-yta. design-reviewer på `<LandingTopbar />`-rendering med `IsStale`-flagga (Area 5 flödesbegriplighet per ADR 0047).
- **ADR-index** (`docs/decisions/README.md`) uppdateras additivt med ADR 0064-raden (docs-keeper-uppgift efter denna ADR-leverans). Amendment till ADR 0056 läggs in i samma operation av adr-keeper denna session.

## Amendment 2026-07-13 — Golvet var fel från dag ett: cache-miss svarar okänt, aldrig ett påhittat tal

> **Amendment 2026-07-13 (Klas spottade det live; senior-cto-advisor CTO-bind samma dag, verdict A′):** Landningssidan renderade "40 000 AKTIVA ANNONSER" som ett faktum för varje anonym besökare. Talet var **aldrig mätt** — det var Beslut (a) punkt 2:s Floor-fallback-konstant, returnerad rakt av vid cache-miss. Vid granskningstillfället (2026-07-12) var den verkliga korpusen 41 475; dagen efter (2026-07-13, live-payload i `docs/reviews/2026-07-13-landing-stats-floor-design-review.md`) hade den fallit till **40 281** — en marginal på **0,70 %** mot golvets 40 000, med negativ trend (~1 200 annonser tappade på ett dygn). Golvets eget försvar, "vi ljuger inte uppåt om vi inte vet", höll bara så länge korpusen råkade överstiga 40 000. Det var aldrig en egenskap hos mekanismen — det var ett sammanträffande med krympande marginal.
>
> **Explicit: golvet var fel FRÅN DAG ETT, inte "rätt då, fel nu".** En hårdkodad konstant kan inte vara konservativ om en storhet den inte mäter. Att korpusen sjönk till 41 475 och vidare till 40 281 gjorde inte golvet fel — det gjorde felet SYNLIGT. Hade korpusen legat på 60 000 vid F6 P5 Punkt 3:s leverans 2026-05-23 hade golvet varit precis lika fel; ingen hade bara märkt det ännu.
>
> **Vad ändras (Beslut (a) punkt 2, ersätter Floor-fallback):**
>
> 1. `LandingStatsDto.ActiveCount` / `NewToday` är nu `int?`. Vid cache-miss returnerar handlern `LandingStatsDto.Unknown` — båda talen `null`, `IsStale: true`. Den tidigare privata `Floor`-konstanten (`ActiveCount: 40_000, NewToday: 0`) i `GetLandingStatsQueryHandler` är RADERAD, inte bara maskerad.
> 2. En MÄTT nolla är fortfarande `0` — bara "vi vet inte" är `null`. Invarianten bärs av typsystemet, inte av en granskares vaksamhet: under NRT/strict är det ett kompileringsfel att rendera ett omätt tal. Det testades omedelbart — nullable-bytet fick kompilatorn att peka ut en tredje yta (`header-stats.tsx`, den inloggade shellen) som varken Klas eller Claude Code hade upptäckt för hand.
> 3. **Landningsheader** (anonym, RSC, server-fetchad): hela stat-gruppen utelämnas när omätt. Ingen CLS är möjlig — värdena kommer in som prop i samma HTML, ingen klient-swap. Verifierat via renderad box-mätning (design-review 2026-07-13): `space-between` med ett barn = flex-start, höjden bärs av `min-height: 88px`, brand-boxen pixel-identisk före/efter.
> 4. **App-shell + /oversikt** (inloggad, klient-polling): en-dash (U+2013, `common.header.valueDash`), samma affordans /oversikt redan hade sedan design-reviewer M2 2026-05-24 — men M2:s uttryckliga undantag för golvet ("vi använder floor-värdet hellre än '—' för att undvika svart fält på sidan") är nu upphävt. En pollande komponent kan reflowa; en RSC kan inte — därav olika affordans för samma princip.
> 5. **`GET /api/v1/landing/stats`** sätter `Cache-Control: no-store` när `IsStale`, i stället för `public, max-age=30`. Ett omätt svar får aldrig pinnas i en delad CDN-cache — det skulle sträcka ut det okända fönstret för alla besökare och underminera punkt 6.
> 6. **Worker warm-start:** `RefreshLandingStats` triggas en gång vid Worker-boot (`RecurringJobRegistrar.StartAsync`) i stället för att vänta upp till 5 min på nästa cron-tick. Best-effort — `try/catch` runt `manager.Trigger`, loggas som varning, kastar aldrig: en kosmetisk uppvärmning får inte fälla hela Worker-hosten (ingest, retention, digests). Botemedlet mot ett kallt-cache-gap är att KRYMPA det, inte att fylla det med fiktion.
>
> **Vad som INTE ändras:** Variant B:s arkitektur (Beslut (a) punkt 1 och 3, Beslut (b), Beslut (c), Beslut (d)) står OBERÖRD. Handlern träffar fortfarande aldrig databasen synkront; stampede-friheten var aldrig problemet, och testet `Handle_NeverWritesToCache` står verbatim. Detta amendment ändrar endast fallback-SEMANTIKEN — vad som returneras vid cache-miss — inte vem som beräknar eller var. Ingen supersession.
>
> **Konsekvenser/Mitigering:** bullet-punkten om `LandingStatsFloor`-värden som "Klas-låsbara konstanter, granskas vid landing-content-uppdatering" är moot — det finns ingen konstant kvar att låsa eller granska. Kvarvarande mitigering mot ett långvarigt omätt läge är Worker-failure-detektionen (befintlig Mitigering-punkt, Hangfire-larm > 3 i rad), nu förstärkt av warm-start (punkt 6 ovan) som krymper det normala gap-fönstret från upp till 5 min till sekunder.
>
> **Implementation-lärdomen:** Beslut (a):s ursprungliga Implementation-sektion lovade att `IsStale = true` skulle rendera "samma värden visuellt men med subtil indikator (dokumenteras i UI-skill om/när Klas låser ut formen)". Den indikatorn byggdes aldrig. `landing-stats.ts` motiverade tystnaden med att "HANDOVER §6.4 nämner ingen sådan affordans" — ett FAS-DEFERRAL-handover-dokuments TYSTNAD citerades som skäl att inte bygga vad en Accepted ADR redan hade specificerat. Amendmentet gör den specifika frågan moot (det finns inget tal kvar att flagga vid cache-miss), men lärdomen protokollförs eftersom den är generell: **en ADR:s Implementation-sektion är inte ett förslag, och ett handover-dokuments tystnad övertrumfar inte en Accepted ADR.** Nästa gång en Implementation-rad specificerar en UI-affordans är den bindande tills en amendment säger något annat — inte tills någon råkar leta efter den i fel dokument.
>
> **Principen detta etablerar, bortom denna ADR:** en renderad siffra är ett påstående. Om systemet inte har mätt den renderar systemet den inte — och FRÅNVARON av en brasklapp är i sig ett påstående. Detta gäller varje framtida yta som visar ett aggregat, en räknare eller en status till en användare (inloggad eller anonym): väljer koden mellan "visa ett gissat/länge-sedan-cachead/hårdkodat värde" och "visa att vi inte vet", är det senare alltid rätt val — oavsett hur liten sannolikheten för fel känns i stunden. 0,70 %-marginalen i detta fall bevisar varför: marginalen är aldrig en egenskap hos designen, den är en ögonblicksbild av data som rör sig.
>
> **Verifierat:** commit `141d9d6e` ("fix(landing): stop rendering a number nobody measured") + arbetsträds-uppföljning (`Cache-Control: no-store` vid `IsStale`, Worker-warm-start med best-effort `try/catch`) på branch `fix/landing-stats-floor`. Application 17024, full vitest 241 filer / 2544 tester, `tsc` 0, ESLint 0 fel, `pnpm build` grön, `dotnet format` rent. Mutation-verifierat: golvets återinförande i landningshuvudet fäller exakt det test som ska fälla. Design-review `docs/reviews/2026-07-13-landing-stats-floor-design-review.md` (design-reviewer, VERDICT: APPROVE_WITH_MINOR, 0 Blocker / 0 Major / 3 Minor — samtliga textuella/kommentar-fynd, ingen rör semantiken denna amendment beskriver).
>
> **Amendment-proveniens:** substansen är Klas + senior-cto-advisor (CTO-bind 2026-07-13, verdict A′) — strukturerad, inte konstruerad, av adr-keeper (Claude Code). ADR 0064 förblir **Accepted** — additivt tilläggslager, EJ supersession. Beslut (a) punkt 1 och 3, Beslut (b), (c), (d) bevaras helt oförändrade; endast punkt 2:s fallback-semantik, den relaterade Mitigering-bulleten, och Implementation-sektionens `IsStale`-indikator-löfte amenderas.

## Amendment 2026-07-28 - The Swedish day boundary, not UTC; and `created_at` was never implemented

> **Amendment 2026-07-28 (Klas-direktiv):** *"Nya idag - eftersom vi bor i Sverige, det är en svensk app i första hand, så ska det inte baseras på UTC, utan på svensk tid. Dvs Efter midnatt svensk tid, så nollställs räknaren."* Beslut (a) point 1 originally specified `newToday = ... AND created_at >= date_trunc('day', now() AT TIME ZONE 'UTC')`. Two separate defects sat in that one clause, and they are of different kinds: a time-zone rule that was genuinely wrong, and an ADR transcription that never matched the code.
>
> *(Section language: English per CLAUDE.md §1, which requires new ADR prose in English; the original Swedish body and its banners are left untranslated. Precedent: ADR 0053 Amendment 2026-06-19, written 2026-07-27, is English inside a Swedish file.)*
>
> **The defect was symmetric, not one-directional.** The UTC day boundary falls 1-2 hours after Swedish midnight depending on daylight saving. For those 1-2 hours the counter claimed "idag" about a window that was mostly YESTERDAY - and ads published between 00:00 and 02:00 Swedish time were excluded from "nya idag" for the remaining ~22 hours of that Swedish day. One clock produced both errors; one shows at the start of the day, the other across all of it.
>
> **What changes (Beslut (a) point 1, the `newToday` clause):**
>
> 1. **Time zone: `Europe/Stockholm`, not UTC.** New Application port `ISwedishCalendar` (`StartOfDay(DateTimeOffset)` / `StartOfMonth(int, int)`, both returning a `DateTimeOffset` at the UTC instant the Swedish boundary falls on) plus Infrastructure adapter `SwedishCalendar` (zone id `Europe/Stockholm` as a `const`; IANA id with no Windows fallback, since .NET 6+ converts IANA to Windows ids through ICU). Registered in `AddPersistence`, shared by Api and Worker. `RefreshLandingStatsJob` takes the port and computes `todayStart = calendar.StartOfDay(now)`; the query filters `Status == Active && PublishedAt >= todayStart`.
> 2. **The column is, and always was, `published_at` - an ADR transcription miss, not code drift.** Same shape as ADR 0052's `.jp-*` transcription (#1095): the code was never wrong, the ADR sentence was. `RefreshLandingStatsJob` has counted `PublishedAt` since the job's first commit; `created_at` was never implemented. `PublishedAt` is the employer's publication timestamp, taken from Platsbanken's `PublicationDate` (`PlatsbankenJobSource.TryConvertToImportItem`), not Jobbliggaren's ingest time. **Whether `CreatedAt` would be the better semantics for "nya idag" is an OPEN Klas question** (raised 2026-07-28) and is deliberately not settled here. Switching would also cost an EF migration for index support: `published_at` is covered by `ix_job_ads_status_published_at_id` (composite `status, published_at, id`), while `job_ads.created_at` has no index at all.
> 3. **A newly visible consequence, not a new mechanism.** The Worker cron is still `*/5 * * * *`. With the boundary moved to Swedish midnight, the landing page shows up to 5 minutes of YESTERDAY's "nya idag" figure between Swedish midnight and the next run. This is the same cron lag already accepted under Konsekvenser/Negativa, but it now surfaces at the one moment a user actually expects the counter to reset, so it is recorded rather than inherited silently.
>
> **Ratified by the same directive, NOT changed - recorded as confirmation of existing behaviour:**
>
> - **Source-unfiltered population.** *"Det behöver inte enbart vara från platsbanken, utan alla jobb / nya jobb som finns i våra db 'Jobbliggaren'"* (Klas, same directive). This was already the implementation: `RefreshLandingStatsJob` filters on `Status == Active` only - `JobAd` has no soft-delete axis (#821) and neither query carries a `JobSource` predicate. No code changes for this point.
> - **Public/authenticated parity by construction.** The landing header and the authenticated `HeaderStats` both resolve their value from `GET /api/v1/landing/stats`, hence the same Redis key `landing:stats:v1` - one number, one source, two rendering surfaces, with no separate computation available to diverge. They reach it through different server-side call sites (the landing page via the `getLandingStats` helper, the app shell via `fetchLandingStats` directly), which is a call-graph detail, not a second source.
>
> **What does NOT change:** Variant B's architecture (Worker precompute, cache rendezvous via `ILandingStatsCache`, stampede-freedom) is untouched, as are Beslut (b), (c) and (d). No supersession - only the `newToday` clause's time zone and column designation are corrected, plus the two ratifications above.
>
> **Three implementation choices worth being able to contest rather than inherit:**
>
> 1. **A separate port, not a widened `IDateTimeProvider`.** That port lives in Domain and is consumed across dozens of sites because aggregate factories need it. Three Application-layer sites need a civil day boundary and no aggregate does - a time zone is a locale concern, not an invariant. Widening a broadly consumed port for three consumers is the ISP violation this split avoids (CTO-bind 2026-07-28).
> 2. **The zone id is a `const`, a deliberate exception to CLAUDE.md §5's hardcoded-config rule.** §5 targets values that vary by environment; the product's home country does not. `IOptions` here would add a fail-open surface - a mistyped zone id would silently yield the wrong day rather than refuse to start - and would drag in §11's dev-boot contract for a value nobody will set. This is a ruling, not an oversight, and is flagged here so a reviewer can contest it rather than discover it.
> 3. **Two further civil-calendar sites** (`GetActivityReportQueryHandler` and `ApplicationStatsCalculator`, both still UTC-derived month boundaries) are DELIBERATELY out of this PR's scope and move together in a follow-up. Noted so the next reader does not conclude the port has a single consumer by design - `ISwedishCalendar.StartOfMonth` is in the contract for them.
>
> **Verified.** New tests pin both DST polarities (winter UTC+1 to 23:00Z, summer UTC+2 to 22:00Z), both transition dates (midnight is neither skipped nor repeated, because EU transitions occur at 01:00 UTC), idempotence on the boundary instant, and the discriminating case: an ad published 22:30Z on 22 May is 00:30 Swedish time on the 23rd (`RunAsync_AdPublishedJustAfterSwedishMidnight_CountsAsToday`), with a sibling at 21:30Z that must stay out - without it, a boundary moved a day too far would also pass. The tests use the real `SwedishCalendar`, not a stub (CLAUDE.md §5 `Tests:` - stubbing the boundary would let the test assert "nya idag" off a value no production adapter emits). **Mutation-verified:** reverting the job to the UTC boundary kills exactly the discriminating test, by name, leaving the other three green; replacing the DST-aware offset with a fixed +1h kills exactly the four summer-side calendar tests and leaves the winter ones green. Gates: Application unit 17945/17945, Architecture 416/416, Infrastructure build 0 warnings, `dotnet format --verify-no-changes` clean.
>
> **Amendment provenance:** the substance is Klas-direktiv 2026-07-28 (day boundary plus source-unfiltered ratification) and a senior-cto-advisor bind the same day (the separate-port choice) - structured, not constructed, by adr-keeper (Claude Code), with the section language and the parity wording corrected by the owning session. ADR 0064 remains **Accepted** - an additive layer, NOT a supersession.

## Amendment 2026-07-28 (II) - The two month-bucket sites moved, and `StartOfMonth` did not survive them

> **Amendment 2026-07-28 (II) (Klas-direktiv 2026-07-28 + senior-cto-advisor CTO-bind 2026-07-28-B):** The [amendment immediately above](#amendment-2026-07-28---the-swedish-day-boundary-not-utc-and-created_at-was-never-implemented) named `GetActivityReportQueryHandler` and `ApplicationStatsCalculator` as two further civil-calendar sites deliberately deferred to a follow-up, and said that `ISwedishCalendar.StartOfMonth` "is in the contract for them." Both sites moved together, the same day, under the same directive - point 3 of that amendment's "Three implementation choices" is fulfilled, not contradicted. But one sentence inside it is now stale in a specific way worth naming precisely, not left for a reader to trip over: **`StartOfMonth` was DELETED, not consumed.**
>
> *(Section language: English per CLAUDE.md §1, matching the amendment immediately above.)*
>
> 1. **What was stale, and why the deferred sentence did not survive contact with its consumers.** `StartOfMonth(int, int)` shipped in the amendment above ahead of any caller, and its entire contract was three prose prohibitions: do not read `.Year`/`.Month` off the returned instant, do not `AddMonths` it as a series, do not `AddMonths(1)` it for a window's exclusive end. It had zero production callers to test the prohibitions against. And the precision worth keeping is this: `GetActivityReportQueryHandler` and `ApplicationStatsCalculator` were ALREADY WRITING two of the three forms - `start.AddMonths(1)` for the exclusive end, and `monthStart.Year`/`.Month` for a bucket label - and against their UTC anchors both were CORRECT. `AddMonths(1)` off the 1st at 00:00:00Z never clamps, because every month has a 1st; `.Month` off the 1st of a month is that month. The forms were harmless where they stood and lethal the instant they consumed the port, whose anchor is the previous month's last day. So the prohibitions were not ignored by careless callers - they were aimed at code that already existed and read as correct, which is exactly the situation a doc comment cannot govern. A member whose safe use depends on a caller having read and remembered a doc comment is asking prose to do a type system's job, and it is CLAUDE.md §5's named anti-pattern read literally: a `DateTimeOffset` boundary was carrying two incompatible meanings, instant and label, in one primitive (CTO-bind 2026-07-28-B).
> 2. **Why the port changed shape, not just its callers - and the two measurements that decided it.** `StartOfMonth` is replaced by two members: `MonthOf(DateTimeOffset) -> CivilMonth`, the Swedish civil month an instant falls in, and `MonthWindow(CivilMonth) -> CivilMonthWindow`, a month's half-open `[Start, End)` with both ends handed over rather than derived. They sit over two new Application-layer value objects - `CivilMonth`, a label that cannot be an instant and owns the December-to-January rollover itself (`Next()`/`Previous()`, never `AddMonths`), and `CivilMonthWindow(Month, Start, End)`, which carries the label alongside the instants so a caller who wants a bucket's label never reaches past it into the boundary instant to get it wrong. Both types live in `Jobbliggaren.Application.Common.Abstractions` - not Domain. The amendment above gave the reason as "a time zone is a locale concern, not an invariant", which is the right conclusion on a weak ground: `CivilMonth` carries no zone knowledge at all, so that sentence says nothing about the type it is used to place. The load-bearing reasons, contested and confirmed by `dotnet-architect` against the CTO's pre-registered Domain flip, are three: no Domain consumer needs a civil month (CLAUDE.md §2.2 makes invariant-protection inside an aggregate the test for Domain membership); the pair is co-invariant and cannot be split, because `CivilMonthWindow` carries an Npgsql `timestamptz` contract Domain must not know about; and `RubricVersion` is the precedent - the same shape, the same layer, and cited by `CivilMonth.Of` itself. The port's member count moves from two (`StartOfDay`, `StartOfMonth`) to three (`StartOfDay`, `MonthOf`, `MonthWindow`). What that buys is that the dangerous derivation becomes UNNECESSARY, not impossible: `window.Start.AddMonths(1)` still compiles, and `StartOfDay` still hands back a bare boundary instant whose label trap is unguarded. The correct value now sits inside the same value as the dangerous one, on a shorter path, so no consumer needs the derivation. Two measurements made the danger concrete rather than theoretical. First, `AddMonths(1)` off a month's start is **silently correct in seven months of twelve, not two**, because `AddMonths` clamps the day-of-month it lands on: measured over 2026, exact when computing the end of January, February, April, June, August, September and November; short by **2 d 23 h** computing the end of March (the anchor is 28 February against a real 31 March, and the spring-forward accounts for only the missing hour); short by **1 d** computing the end of May, July and December; short by **1 d 1 h** computing the end of October; February is exact in every year, leap or not - so a hand check landing in any of the seven exact months returns green, and the defect is invisible 58 % of the year. Second, `StartOfDay(now).Month` is wrong on the **first of every month, all day** - not only in January: that value is the previous day's 22:00Z or 23:00Z, so on 1 June it reports May, and January is merely the case where the year is wrong too - twelve days a year, and for the activity report those are precisely the days a report gets filed.
> 3. **The user-visible behaviour change, stated rather than left to be discovered.** An application submitted in the last one to two hours of a UTC month (23:00-24:00Z in winter, 22:00-24:00Z in summer) now buckets into the LATER Swedish month. The activity report is the document a job seeker files with Arbetsförmedlingen, so a report already filed for the earlier month will no longer list that row - e.g. a submit at 2026-04-30T22:30Z is 2026-05-01T00:30 in Sweden, and used to render inside the April report. The FE has always rendered that row's "Datum sökt" column in Europe/Stockholm, so the row already showed a May date sitting inside an April report; window and display now agree instead of disagreeing by up to two hours. Nothing stored changes - the report is computed on read, never written - and no new data is exposed; only a different set of rows is selected. GDPR (CLAUDE.md §8 point 8): no new PII, no new flow, no new retention.
> 4. **`ApplicationStatsCalculator` now takes `ISwedishCalendar`, and that does not reopen the 2026-06-29 bind.** That bind forbade the calculator a database, an AI call and the clock. All three are instances of one principle - no ambient, environment-varying or nondeterministic input - and the calendar is none of them: it is a total, side-effect-free function over a fixed zone table, and every member takes its instant or its month as an argument. The prohibition was on SOURCING time, never on interpreting it; `now` is still supplied by the caller. The rejected alternative - the handler precomputing twelve windows itself - would move a metric definition out of the class that declares itself the SSOT for the funnel and the monthly series, and would make every test in that class hand-build a twelve-element window array. That second point is a maintenance and drift argument, NOT a CLAUDE.md §5 `Tests:` one - an earlier draft cited §5 here and `dotnet-architect` struck it, because §5 attaches the obligation to the ASSERTION, and a hand-built array matching the adapter's own output is a premise production does produce. The cohesion argument stands on its own. The same over-citation had already been corrected once in this repo, in a test file in this very PR, and it had not been carried here - where an ADR would have been quoted as the rule.
>
> **Verified.** New tests span both DST polarities at every boundary the two sites touch - the predecessor PR's seeds all sat inside one month, so a hardcoded fixed offset would have passed the lot, and that gap is closed here. A new handler-level integration test over real PostgreSQL (`GetActivityReportSwedishMonthBoundaryIntegrationTests`) seeds four rows around the Swedish July boundary and asserts the exact two-row set, not merely that nothing threw - each excluded row dies under a different named mutation. This is a gate the unit suite structurally cannot provide: the window's `End` is now a `timestamptz` query parameter for the first time, Npgsql throws on a non-zero-offset write, and EF InMemory evaluates the same predicate in LINQ-to-Objects, where the offset is invisible to it by construction - the predecessor PR shipped exactly that defect into final review behind a fully green unit suite. Gates: Application unit 18008/18008 (17956 before), Architecture 416/416, `dotnet format --verify-no-changes` clean.
>
> **Also ruled the same day - a separate clause of this ADR, closed rather than changed:** the amendment above left `PublishedAt` vs `CreatedAt` for "nya idag" as an OPEN Klas question. **Klas ruled 2026-07-28: `PublishedAt` is retained.** No code follows from this - the question is closed, not the mechanism.
>
> **What does NOT change:** Variant B's architecture, Beslut (b)/(c)/(d), and the day-boundary correction in the amendment immediately above are all untouched - this amendment does not reopen or rewrite that amendment's prose; `StartOfMonth` is recorded here as gone because the member it referenced no longer exists, not because the reasoning that named it was wrong. No supersession of ADR 0064, nor of the preceding amendment - this replaces one Application-layer port member with two, moves two deferred consumers into production, and closes one open question.
>
> **Amendment provenance:** the substance is Klas-direktiv 2026-07-28 (the same directive that moved the day boundary) and a senior-cto-advisor bind the same day (CTO-bind 2026-07-28-B - the port-shape choice and the calculator's argument), structured, not constructed, by adr-keeper (Claude Code) and written against the diff (`1073d00b`, `f7891bb2`, `97b86f31`). ADR 0064 remains **Accepted** - an additive layer, NOT a supersession.

## Referenser

- CLAUDE.md §2.3 (CQRS — read-DTO ut, ingen Domain-objekt över gränsen), §3.3 (DTO = `record class`), §3.6 (`.AsNoTracking()` default + projektion), §5.4 (säkerhet — anonym DoS-yta), §8 punkt 9 (ADR = DoD vid arkitekturbeslut)
- ADR 0023 (Worker-isolation från ASP.NET HTTP-bagage)
- ADR 0042 Beslut C (SuggestPolicy least-common-mechanism-precedens för dedikerad rate-limit)
- ADR 0043 (taxonomi-singleton-cache auth-gated — skiljelinje mot publik anonym)
- ADR 0044 (gate-mönster: observe-only Fas 1 + ratchet-väg)
- ADR 0045 Beslut 1 klass (a) (300 ms p95 hot-path-budget)
- ADR 0048 Beslut (b) (in-handler-join vs read-model-port — ADR 0064 lägger ett fjärde komplementärt ben på publik anonym hot-path-axel, **EJ supersession**)
- ADR 0056 Beslut 4 (utbytespunkt `getLandingStats()` lyft via amendment 2026-05-23)
- ADR 0063 (per-user-overlay batch-port — privat read-skiljelinje på annan axel)
- Robert C. Martin, *Clean Architecture* (2017) kap. 7 (SRP), 8 (OCP), 10 (ISP), 11 (DIP), 13 (REP/CCP), 23 (CQRS — separation av read- och write-modeller)
- Eric Evans, *Domain-Driven Design* (2003) kap. 14 (bounded context — marknadsförings-yta vs MV-aggregat)
- Martin Fowler, *Patterns of Enterprise Application Architecture* (2002) — Lazy Load / cache-aside (här explicit avvisat till förmån för pre-compute)
- Michael Nygard, *Release It!* 2nd ed. (2018) kap. 5 (stability patterns — cache stampede, graceful degradation)
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (2017) kap. 1 + 2 + 6 (fitness functions, observe-only-ratchet — ADR 0044-mönster)
- Kent Beck, *Extreme Programming Explained* 2nd ed. (2004) — YAGNI (avvisar Variant D fram-skjuten frontend-cache-policy)
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — DRY/SPOT (`ILandingStatsCache`-porten som single source för cache-rendezvous)
- Saltzer & Schroeder, "The Protection of Information in Computer Systems" (1975) — least common mechanism (rate-limit-bucket-separation publik vs inloggad)
- Dijkstra, "On the role of scientific thought" (1974) — separation of concerns (Worker write vs Api read)
- 12-Factor App §XI — observability (Hangfire dashboard som operativ visibility)
- Microsoft Learn — *Architect modern web apps with ASP.NET Core and Azure* (cache-mönster + Worker-isolation)
- Beslutsunderlag: senior-cto-advisor agentId `a1da26dc2029a5def` (multi-approach-triage 2026-05-23, Variant B vald över A/C/D)

---

*ADR-index underhålls av docs-keeper. Detta beslut fastställer Worker-precomputed Redis-cache + Floor-fallback + dedikerad IP-partitionerad rate-limit som godkänt mönster för publik anonym aggregat-read, komplementär avgränsning till ADR 0048 Beslut (b) på axeln publik anonym hot-path, EJ supersession. Cache-aside i handler (Variant A) är förbjudet för denna ytaklass på grund av stampede-risk och cold-start-spike mot ADR 0045 hot-path-budget.*
