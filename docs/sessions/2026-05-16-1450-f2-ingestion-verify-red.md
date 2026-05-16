---
session: F2 ingestion-cron-verifiering — RÖD, Fas 2-stängning pausad
datum: 2026-05-16
slug: f2-ingestion-verify-red
status: STOPP — Klas-beslut krävs (ingestion ej grön)
commits:
  - docs(infra)-runbook-log-group-drift (denna session)
  - docs(F2)-ingestion-verify-red-session-end (denna session)
---

# F2 ingestion-cron-verifiering — RÖD

## Mål

Verifiera snapshot-cron live i CloudWatch + `job_ads`-korpus, och **formellt
stänga Fas 2-milstolpen om grönt**. Utfall: **rött på båda verifieringsstegen
→ Fas 2 kan ej stängas.**

## Förkrav (alla gröna)

HEAD `24f9dad` · git clean · `/api/ready` 200 · AWS SSO aktiv · deployad
`v0.2.8-dev` (v0.2.6-dev:s child-scope-per-item-fix ÄR inne).

## Discovery: log-group-namn (runbook-drift bekräftad + fixad in-block)

`aws logs describe-log-groups` → faktiska namn `/aws/ecs/jobbpilot-dev/{worker,migrate,api}`.
Runbook `aws-rds-migration-apply.md` rad 120 sa `/ecs/jobbpilot-dev-migrate`
(fel) → rättad till `/aws/ecs/jobbpilot-dev/migrate`. ECS task-def-family
`jobbpilot-dev-migrate` (rad 59/65) verifierad mot
`aws ecs list-task-definition-families` → **korrekt, orörd** (family ≠
log-group; CTO-direktiv: rör ej family-rader). Mandat fanns explicit i
sessionsprompten ("ev. fixa runbook in-block").

## Steg 1 — SnapshotJob completar INTE (regressionssymptom tillbaka, NY rotorsak)

Log-group `/aws/ecs/jobbpilot-dev/worker`:

| Mätning | Värde |
|---|---|
| `SyncPlatsbankenSnapshotJob: startad [5401]` — 7 dygn | **60** |
| `SyncPlatsbankenSnapshotJob: klart [5402]` — 7 dygn | **0** |
| `JsonException` — 24h | 11 |
| `Npgsql 23505 duplicate key` — 24h | 46 760 |

Numeriskt EXAKT samma symptom ("60 starts / 0 completes") som rotorsaken
**före** v0.2.6-dev. Hangfire `AutomaticRetry` retry:ar var ~1–8 min, aldrig
`[5402]`.

**Fatal ofångad exception (INNAN upsert, under deserialisering) — verbatim:**

```
warn: Hangfire.AutomaticRetryAttribute[0]
System.Text.Json.JsonException: Expected depth to be zero at the end of the JSON
payload. There is an open JSON object or array that should be closed.
Path: $[2570] | LineNumber: 0 | BytePositionInLine: 26442355.
 ---> System.Text.Json.JsonReaderException: Expected start of a property name or
 value, but instead reached end of data. LineNumber: 0 | BytePositionInLine: 47901751.
```

Avbrott vid bytepositioner **26 MB / 41 MB / 47 MB** → Platsbanken-snapshotens
JSON-svar kapas mitt i strömmen → `System.Text.Json` kastar "reached end of
data" → ofångat → hela `RunAsync` dör före `LogCompleted` → Hangfire retry →
oändlig loop.

Sekundärt (icke-fatalt — v0.2.6-dev child-scope fångar per item men i enorm
volym): `Npgsql 23505 duplicate key ix_job_ads_external_source_external_id`
46 760/24h (≈ hela ~47k-korpusen — snapshoten hämtar full lista, allt finns
redan, dör sedan på trunkerad svans-payload). Plus
`Polly.RateLimiting.RateLimiterRejectedException` (retry efter 1 min).

**Rotorsaken är NY** — payload-trunkering vid deserialisering, distinkt från
den 23505-ackumulering v0.2.6-dev fixade. v0.2.6-dev:s child-scope-per-item
adresserade inte payload-trunkering → defekten är oadresserad. Detta är ett
andra "falskt fixad"-mönster i samma pipe.

## Steg 2 — job_ads-korpus långt under mål

Autentiserad API (dev-test-konto, bearer-session-id):

| Query | totalCount |
|---|---|
| Ofiltrerad `/api/v1/job-ads` | **5 380** |
| `q=utvecklare` (förra sessionens referens) | **137** (oförändrat) |

Förväntat ~40k+. Faktiskt 5 380 — endast `*/10 SyncPlatsbankenStreamJob`
(inkrementell, completar fint) har fyllt på. Full snapshot har aldrig lyckats.
`q=utvecklare` oförändrat 137 bekräftar utebliven full snapshot.

## CTO-beslut (senior-cto-advisor, inline per §9.6 — agentId a5c2b2ca57caee056)

1. **Fas 2-stängning FÖRBLIR PAUSAD** — mekanisk konsekvens av DoD (CLAUDE.md
   §8 punkt 4 "Manuellt testad i dev-miljön"); milstolpe-integritet (Ford et al. 2017,
   fitness functions). Pauseringen i sig kräver ingen Klas-GO (upprätthåller
   befintlig regel); Fas 2-*stängning* förblir Klas-GO-flaggad strategisk
   transition (irrelevant nu — gaten röd).
2. **Rotorsaks-fix = SEPARAT fix-session** med obligatorisk dotnet-architect-rond
   + Klas-GO. INTE denna session (sessionsförbud + Google SWE kap. 9
   review-scope-integritet — v0.2.6-dev:s falska "fixad" uppstod ur exakt denna
   scope-glidning). **Ingen TD skapas** — pressad mot §9.6: misslyckas båda
   kriterierna (ej annan fas, ej saknad dependency); är Major/Fas-Nu vilket
   §9.7 förbjuder som TD-kategori. Lever som STOPP-underlag + denna session-logg
   + kommande ADR 0032-amendment (skrivs i fix-sessionen när fix är känd).
3. **Runbook-drift-fix NU in-block** — ortogonalt scope (Martin 2017 kap. 17),
   explicit sessionsmandat. Gjord (rad 120).
4. **Hangfire retry-storm = Klas-eskalering NU** — 46 760 23505/24h +
   rate-limiter-rejections är aktiv RDS/CloudWatch-förbrukning som kan svälta
   den fungerande stream-jobbets rate-limit-budget. CTO **rekommenderar paus**
   av `sync-platsbanken-snapshot` recurring-jobbet på dev tills fix-sessionen.
   Verkställs EJ av CC — kräver Klas-GO + AWS-operatörsåtgärd (manuell trigger
   är 410 Gone per ADR 0032 Amendment 2026-05-16; paus = ECS exec /
   Hangfire-radinsert).

## Beslut & avvägningar

- Fas 2 öppen tills separat fix-session lyckats — tempo offras för
  milstolpe-integritet/sanning (CLAUDE.md: kvalitet > tempo).
- Payload-trunkeringen bär ingen TD-rad (CTO-pressad §9.6/§9.7) — STOPP-underlag
  + session-logg + kommande ADR-amendment är granskningstrailen.
- Ingen egen ingestion-debug/fix påbörjad (Klas-STOPP-flagga + förbud).

## Commits denna session

- `docs(infra)`: runbook log-group-drift rad 120 (`/ecs/jobbpilot-dev-migrate`
  → `/aws/ecs/jobbpilot-dev/migrate`)
- `docs(F2)`: ingestion-verify-RÖD session-end (current-work + denna logg)

## Nästa session (kräver Klas-GO för att starta)

**F2 ingestion payload-trunkerings-fix** — egen fix-session:
- dotnet-architect-rond obligatorisk INNAN kod
- Hypoteser att utreda: `HttpClient` timeout / `MaxResponseContentBufferSize`,
  gzip/chunked-ström kapad av Polly rate-limiter mitt i nedladdning, eller
  upstream Platsbanken-API som trunkerar stora svar (~26–47 MB)
- ADR 0032-amendment skrivs när fix verifierad
- Förkrav: ev. paus av snapshot-jobbet (Klas-beslut, se eskalering ovan)

## Klas-eskaleringar (väntar svar)

1. Bekräfta: Fas 2 förblir pausad (regel, ej beslut — men bekräfta medvetet).
2. Ingestion-fix = egen session m/ dotnet-architect + Klas-GO — när?
3. Pausa `sync-platsbanken-snapshot` på dev nu? (CTO rekommenderar; kräver
   Klas + AWS-operatörsåtgärd.)
