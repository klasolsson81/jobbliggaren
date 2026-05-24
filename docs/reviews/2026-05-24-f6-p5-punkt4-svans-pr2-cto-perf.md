# CTO-rond — F6 P5 P4 svans-PR2 PERF-incident `/oversikt` 10s+

**Datum:** 2026-05-24
**Agent:** senior-cto-advisor
**AgentId:** `ad37955db80099f19`
**Trigger:** Klas post-leverans-feedback visual-verify mot dev v0.2.61-dev — "Översiktssidan laddas väldigt långsamt, nästan 10 sekunder+. Även jobbsidan är seg."

## Beslut

| Fråga | Beslut |
|---|---|
| 1 — Scope för svans-PR | **(c) Discovery-first**, 30 min tidsbox |
| 2 — Cache-strategi-omvärdering | **Behåll `force-dynamic` + `no-store`** (ingen Next.js-cache-policy-byte) |
| 3 — Blockerar tag-push v0.2.62-dev? | **NEJ** — observe-only Fas 1 per ADR 0045 Beslut 5 |

CC kör discovery innan kod skrivs. Klas-STOPP behövs ej (Auto Mode + nonstop-disciplin).

## Motivering

### (c) Discovery-first

Klas rapport: "även /jobb är seg". `/jobb` är **single-endpoint-page** (`GET /api/v1/job-ads`). Om single-endpoint är "ungefär lika långsam" som 6-endpoint-fanout, är **fanout INTE dominerande variabeln**. Roten ligger i BE-query-latens.

Nygard 2018 kap. 17 + Hunt/Thomas 1999 "Don't Assume It — Prove It": symptom-fix mot fel hypotes är värre än 30 min discovery. `LoggingBehavior` har redan latency-mätning — CloudWatch-loggar finns. Discovery = läs dem, inte lägg till mätning.

### Behåll cache-strategi

`force-dynamic` + `no-store` är ADR 0045-koherent och GDPR-säkert. Premature switch till `revalidate: N` utan cache-key-audit = Saltzer/Schroeder fail-safe-default-brott + Knuth 1974 premature optimization mot okänd rot.

Per-user Redis-cache (Variant C från D1-ronden 2026-05-24) skulle bryta Variant A-domen — cache är sista utvägen, inte första.

### Tag-push ej blockerad

ADR 0045 Beslut 5 säger explicit Fas 1 observe-only. 10s-incident **upptäckt manuellt av Klas** är *exakt* signalen observe-only-modellen producerar. Klas-veto enligt CLAUDE.md §2.5 betyder "leverera motivering eller åtgärd" — inte "stoppa deploy". Deploy-block är annan beslutsaxel (rollback/freeze) som kräver explicit Klas-beslut.

## Discovery-resultat (CC post-CTO-rond)

### CloudWatch (senaste 1-6h, dev `/aws/ecs/jobbpilot-dev/api`)

| Handler | n | p50 | max | ADR 0045 budget |
|---|---|---|---|---|
| `ListJobAdsQuery` | 88+12 | ~1200ms | **6729ms** | 300ms |
| `GetResumesQuery` | 10 | 32ms | 741ms (cold) | OK |
| `GetLandingStatsQuery` | 129 | 1ms | 102ms | OK (cached) |
| `ListSavedJobAdsQuery` | — | 11ms | 143ms | OK |
| `ListRecentSearchesQuery` | — | 16ms | 125ms | OK |
| `GetJobAdStatusBatchQuery` | — | 11ms | — | OK |
| `GetCurrentUserQuery` | — | 2-5ms | — | OK |
| `LoginCommand` | — | — | 696ms | OK |

**Entydigt:** `ListJobAdsQuery` är 4-22x över ADR 0045-budget. Konstant regression — inte cold-start (många datapunkter).

### Rotorsak-hypoteser för `ListJobAdsQuery`

1. COUNT(*) över 46k+ rader utan dedikerad index
2. JsonbContains för ssyk/region pre-FTS (seq scan)
3. Sortering publishedAt utan composite index
4. TOAST-detoasting av raw_payload per row

Verifiering kräver dotnet-architect-rond + EXPLAIN ANALYZE → separat TD-94.

### `/oversikt`-specifika fixet (samma svans-PR)

`/oversikt` ringer `getJobAds({ page:1, pageSize:1 })` BARA för `totalCount`-fältet ("Aktiva annonser totalt"). Detta är **exakt samma värde** som `getLandingStats().activeCount` (Worker-precomputed Redis-cache, 0-1ms).

**Fix:** Byt `getJobAds()` → `fetchLandingStats()` i `/oversikt/page.tsx`. Eliminerar 1-2s från `Promise.all`-max + löser Klas:s 28-vs-9 mismatch-feedback (samma siffra som HeaderStats visar) i samma drag.

`/jobb`-perf-rotorsak (`ListJobAdsQuery` self) → **TD-94** Major × F6 P5-fas-stängning, separat dotnet-architect-rond.

## Referenser

- Nygard, *Release It!* 2nd (2018) kap. 5, 17
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — Don't Assume It — Prove It
- Knuth (1974) — premature optimization
- Saltzer/Schroeder (1975) — fail-safe defaults
- Fowler, *Refactoring* 2nd (2018) kap. 3 — speculative generality
- Poppendieck, *Lean Software Development* (2003) — Last Responsible Moment
- ADR 0045 Beslut 1 + 5 + 6
- ADR 0064 (landing-stats publik-anonym)
- CLAUDE.md §2.5

---

*Sparad per CLAUDE.md §9.2. Discovery genomförd inom 30 min-tidsbox. Fix i svans-PR2.*
