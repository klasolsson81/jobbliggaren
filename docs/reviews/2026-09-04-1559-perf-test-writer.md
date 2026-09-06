# perf-test-writer — PR #1653 (#1559)

**Datum:** 2026-09-04
**PR:** [#1653](https://github.com/klasolsson81/jobbliggaren/pull/1653) — `feat/watch-ad-counts-1559`
**Diff:** `76c747ab..7da45e7d`
**Läge:** report-only (inga filer skrivna, inga kodändringar)
**Kallad för:** ny Api-endpoint (ADR 0045 klass a/b/c) + ny FE-rutt (`lighthouserc.json`)

Transkriberad av den drivande sessionen — agenten skrev ingen rapportfil (CLAUDE.md §9.2).

---

## Trigger: PR #1653 utlöser mig — delvis

### 1. `GET /{id}/ads` och `GET /{id}/ad-count` → klass (a), inte (b). Utlöser mig.

Båda är auth-gated på `RateLimitingExtensions.CompanyBrowsePolicy` (TokenBucket, CTO-riktvärde
15/min — verifierat i `RateLimitingExtensions.cs:370-385`), inte `SuggestPolicy 30/10s`.
ADR 0045 Beslut 1 definierar klass (b) **uttryckligen** som "ADR 0042 SuggestPolicy 30/10s" — en
namngiven policy, inte en generisk "litet svar"-kategori. `/ad-count` är ett litet svar men fel
policy-familj och fel access-mönster (detaljsidans badge, inte tangentbordsdriven typeahead) för
att kvala som (b). Klass (a):s egen exempelkolumn — "`/jobb`-sök, list-endpoints" — täcker precis
den här formen: ett auth-gated read-query mot en tabell/join, paginerat eller aggregerat.

**Utslag:** klass (a), **p95 300 ms** (Klas-låst), **p99 600 ms** (observe-only), mätpunkt
server-side handler-latens.

### 2. FE-rutten `/foretag/smarta-bevakningar/[id]/annonser` → INGET skyldigt.

`lighthouserc.json`:s egen `//urls`-not är explicit: URL-mängden är **medvetet backend-fri**
(publika marketing-sidor + gäst-speglar) just för att det observe-only-jobbet ska klara sig utan
seedad stack, och **authed sidor är en namngiven följdfråga** gated på en seedad CI-stack med LHCI
`puppeteerScript`-inloggning — inte committad. Den nya rutten är auth-gated. Syskon-detaljsidan
`[id]/page.tsx` är **redan** frånvarande ur `collect.url` av samma skäl, så den nya rutten följer
etablerad precedent. Den drivande sessionens läsning bekräftas, inte motsägs.

---

## Är sessionens mätningar tillräckliga för ett ADR 0045-utslag? **Nej — rakt ut.**

ADR 0045 Beslut 1 är explicit: **p95 = primär dom, p99 = observe-only**. Siffrorna är varken:

- **Varm (105 ms / 252 ms):** enskilda observationer, inte en distribution. En enda varm sampling
  kan råka träffa bästa fallet (cache, planer, connection-pool-läge). Båda ligger under
  300 ms-budgeten, men *"under budget en gång"* är inte samma påstående som *"p95 under budget"*.
- **Kall (1,0 s / 1,37 s):** samma sak, plus att kallstart inte är den storhet som dömer i
  ADR 0045:s egen mätmetod (p95 under sustained last, inte first-hit-latens). De överskrider
  budgeten 3–4×, vilket är precis den signal ett NBomber-scenario måste skilja från brus innan
  någon fäller en dom — annars är det Beslut 5:s *"flaky perf-gate sämre än ingen"*.

Mätningarna är rätt sorts **kalibreringsunderlag** (verkliga laststorlekar: 167 vs 39 909 träffar,
verklig frågekostnad), inte en ersättning för scenariot.

---

## Skyldigt

```
## Perf-fitness-function skyldig: CompanyWatchCriteria ad-browse (#1559 / PR #1653)

**Fil att skriva (ej skriven här — report-only):**
  perf/Jobbliggaren.LoadTests/Scenarios/CriterionAdBrowseScenarios.cs

**ADR 0045-budget mätt mot:** klass (a) p95 300 ms / p99 600 ms (observe) —
verbatim, Klas-låst 2026-05-17. Ej klass (b): policyn är CompanyBrowsePolicy
(15/min TokenBucket), inte SuggestPolicy 30/10s.

**Mätpunkt:** server-side handler-latens (LoggingBehavior-konsekvent).

**Två endpoints, två handlers under samma policy-bucket:**
  GET /api/v1/me/company-watch-criteria/{id}/ads       (BrowseCriterionAdsQueryHandler)
  GET /api/v1/me/company-watch-criteria/{id}/ad-count  (GetCriterionAdMagnitudeQueryHandler)

**Last-form — kalibreringsvillkor:** 15/min per user-bucket. Måste sprida
simulerade users för att nå n>=100 utan 429-förorening; kalibrera storleken
(167 vs 39 909 träffar) mot PR-kroppens mätningar, ALDRIG mot deras enstaka
varm/kall-tal som budget-utslag — de är underlag för last-formen, inte p95.

**Observe-only Fas 1:** exit 0 ovillkorligt; p95-överskridande -> ::warning::.

**FE-rutt /foretag/smarta-bevakningar/[id]/annonser:** INGET skyldigt.

**Nästa steg:** Kör i CI:s observe-only loadtest-job efter scenariot är skrivet.
code-reviewer/CTO bedömer signalen mot CLAUDE.md §2.5 — perf-test-writer
fäller ingen dom.
```

Budget-konstanterna ska återanvändas ur SSOT (`LandingStatsScenarios.cs:43,48`), aldrig nya tal.
Mönstret är `FacetCountsScenarios.cs` (n≥100, Tukey-tröskel).

---

## Charter-avvikelse, orelaterad till denna PR

Agentens **egen charter** påstår att `web/jobbliggaren-web/budget.json` "existerar inte i trädet …
behandla som en fil att SKAPA". Det är fel: **ADR 0045 Amendment 2026-07-20 pensionerade
`budget.json` explicit** — page-weight-budgetarna konsoliderades in i `lighthouserc.json`:s
`assert.assertions` som `resource-summary:*`-poster (kommentarraden `"//fix-2026-07-20"`).
Amendmentet är daterat **före** charterns egen "Measured 2026-08-02"-notis, så notisen är själv
drift mot ADR 0045 — auktoriteten här.

Rör inte PR #1653 (ingen ny publik/gäst-rutt). Registrerat som en charter-fil att rätta, inte
agentens att göra i detta läge.

---

## Källor agenten utgick från

- `docs/decisions/0045-performance-budget-and-fitness-functions.md` (Beslut 1, Amendment 2026-07-20)
- `src/Jobbliggaren.Api/Endpoints/CompanyWatchCriteriaEndpoints.cs:111-143`
- `src/Jobbliggaren.Api/RateLimiting/RateLimitingExtensions.cs:370-385`
- `web/jobbliggaren-web/lighthouserc.json` (`//urls`-kommentaren)
- `perf/Jobbliggaren.LoadTests/Scenarios/FacetCountsScenarios.cs`, `LandingStatsScenarios.cs:43,48`

---

## Gradering

perf-test-writer är **byggare, inte granskare** — den skriver instrumentet, aldrig utslaget, och
graderar därför ingenting. Vart det skyldiga scenariot hör (in-block, följd-PR eller issue) är ett
§9.6-routningsbeslut och går till `senior-cto-advisor`.
