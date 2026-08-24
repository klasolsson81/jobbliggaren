# Security-audit: /foretag/sok live region (PR #1504)

**Agent:** `security-auditor` · **Datum:** 2026-08-24 · **PR:** [#1504](https://github.com/klasolsson81/jobbliggaren/pull/1504)
**Gren:** `fix/foretag-sok-live-region-1092` · **Delta:** `origin/main...HEAD`, merge-base `207e9bb4` · **Head:** `1b15966b`
**Status:** ✓ Approved
**Auktoritet:** ADR 0087 D8(c) · CLAUDE.md §9.2/§9.6 · AGENTS.md §5 (`Security:`, `Frontend:`) · GDPR Art. 5(1)(c), Art. 32 · audit-områden 1, 3, 6, 7, 8

> **Transkriberad av den anropande sessionen.** Agentens charter förbjuder `Write`/`Edit` (report-only,
> ingen repo-effekt), så hon kunde inte skriva den här filen själv. CLAUDE.md §9.2 lägger
> transkriptionsplikten på den anropande sessionen och binder den vid charterns Output format-cap.
> Innehållet nedan är hennes, ordagrant.

## Blockers

Inga.

## Major

Inga.

## Minor

Inga.

## Mätningar (det som faktiskt avgör verdiktet)

**1. Org.nr-/personnummer-grinden är byte-oförändrad — mätt, inte antaget.**
`git diff -U0 origin/main...HEAD -- page.tsx` ger exakt två hunkar: `@@ -9,0 +10 @@` (en tillagd
import-rad, noll borttagna) och `@@ -158,9 +159,16 @@` (Suspense-inlindningen). Grinden ligger i sin
helhet utanför båda. Starkare än så: `parseNamn` och `buildOrgNrRefusedHref` **definieras** i
`web/jobbliggaren-web/src/lib/company-search/search-params.ts:244` respektive `:270` — en fil som
`git diff --name-status origin/main...HEAD` inte listar alls. Redirect-målet
(`/foretag/sok?avvisat=orgnr`), refusal-copyn och `parseOrgNrRefused` är orörda.

**2. Ordningen auth → grind → announcer är intakt.**
`page.tsx:52-53` (`getServerSession` + `redirect("/logga-in")`) → `:83-86` (`parseNamn` →
`buildOrgNrRefusedHref`) → `:164` (`ForetagSokAnnouncer`). Announcern ligger strikt nedströms båda;
en orenderad request når den aldrig.

**3. Ingen ytmagnifiering över RSC→client-gränsen.**
`ForetagSokResults` är en Server Component (`export async function` på `:36`, noll `"use client"`,
importerar `next-intl/server` + `next/navigation` + server-side API-klienter). Dess props
serialiseras därför aldrig — bara dess renderade output passerar. Den enda raden i deltat som nämner
`namn` i produktionskod är `namn={namn}`, den omindenterade befintliga propen in i just den Server
Componenten. Vad som **nytt** korsar gränsen som client-prop är exakt en `message: string` per call
site, och det finns två i produktion: `foretag-sok-results-skeleton.tsx:24` (`t("loadingResults")`)
och `foretag-sok-results.tsx:109` (`announcement`). Baslinjen är dessutom redan bredare:
`foretag-sok-searchbar.tsx` är `"use client"` och tar emot `readonly namn: string` (`:135`) sedan
tidigare, och org.nr når DOM:en redan via `CompanyBrowseList`/follow-toggeln. Ingenting vidgas.

**4. Ingen av de tre annonserade meningarna kan bära ett användarlevererat eller PII-bärande värde.**
Mätt direkt mot katalogen: `emptyTitle` = `"Inga företag matchar sökningen"` (**ingen**
ICU-placeholder — säger "sökningen", aldrig söktermen), `announceResultsReady` =
`"Företagen har laddats."` (fast sträng), `resultsCountUnit` =
`{count, plural, one {träff} other {träffar}}` (selekterar på ett **tal** och emitterar bara
substantivet, inte ens `#`). Talet självt kommer från `formatMagnitude`, som enligt
`format-magnitude.ts` enbart locale-grupperar en `number` och lägger på `+` vid saturation. Ingen väg
interpolerar `namn`, ett org.nr eller ett företagsnamn. De placeholder-bärande nycklarna
`announceFilterAdded`/`announceFilterRemoved` binder `{namn}` till en **filteretikett** (`chip.name` /
SCB-referensnamn) i searchbarens *separata* region — orört av deltat, och medvetet inte delad region.

**5. XSS/logg/lagringsytan är oförändrad.**
Ingen tillagd rad i deltat innehåller `console.`, `logger`, `localStorage`, `sessionStorage`,
`dangerouslySetInnerHTML`, `innerHTML`, `eval(`, `fetch(`, `document.cookie` eller
`window.location`. Regionen renderar `{message}` som textbarn — React-escapat — så även ett
hypotetiskt användarvärde vore ingen injektionsvektor. Inga nya beroenden:
`package.json`/`pnpm-lock.yaml` har noll rader i deltat.

**6. Grindens egna pins håller under den ändrade `page.tsx`.**
`vitest run "src/app/(app)/foretag/sok/page.test.tsx" foretag-sok-searchbar.test.tsx
foretag-sok-announcer.test.tsx foretag-sok-results.test.tsx search-params.test.ts` →
**Test Files 5 passed (5), Tests 141 passed (141)**. Däri ingår tvätten av tiosiffrigt `?namn=`,
"refuses BEFORE the reference is fetched", 18xx-residualen och `/logga-in`-redirecten.

**7. Ingen ny PII-behandling.** Deltat är rent FE (två meddelandekataloger, fem `.tsx`, två
testfiler) — noll `.cs`, `Migrations/`, `appsettings*`, `.env`, compose, Caddyfile eller `.tf`. Ingen
ny kolumn, inget nytt ändamål, ingen ny mottagare, ingen ny sub-processor. Antalet träffar som
annonseras är dessutom **redan renderat synligt** i `.jp-results-count` för samma inloggade
användare — annonseringen är en omdirigering av en befintlig sträng, inte ett nytt utlämnande.
Område 2, 4 och 5 matchar inte deltat.

## Område 8 — NOT TAKEN (inte "rent")

Deltat rör **ingen** del av suppressionsytan. Mätt med
`git diff --name-only origin/main...HEAD | grep -Ei 'package\.json|pnpm-lock|pnpm-workspace|\.github/|Directory\.Packages\.props|\.csproj|nuget|dependabot'`
→ noll träffar. Alltså: ingen ändring av `pnpm.auditConfig.ignoreGhsas`, `pnpm.overrides`,
`--audit-level`, `NuGetAudit`/NU1901–1904, `ignoredBuiltDependencies` eller
`pnpm/action-setup`-majoren, i någon riktning. Området triggar på exponerings**riktning**, och ingen
riktning finns här.

Jag har därför **medvetet inte kört** `audit-suppression-guard.sh`. Läs detta som *området togs inte
upp*, aldrig som *mätt utan fynd*: en körning här hade producerat en repo-tillståndsavläsning som
denna PR inte orsakat och som per mitt charter ändå inte fått blockera den. Ingen
`OVER-BROAD SUPPRESSION`, `STALE SUPPRESSION` eller `DEAD OVERRIDE` är alltså varken påstådd eller
utesluten av denna granskning.

## Praise

- Grinden flyttades inte, kringgicks inte och fick ingen inline-predikat-tvilling —
  `search-params.ts` förblir enda regelkällan, precis som dess docblock kräver. ✓
- Announcern är medvetet **skild** från searchbarens region, vilket hindrar att en laddning skriver
  över en filterändring innan den lästs. ✓
- Wrapper-mönstret (children-as-props runt en Server Component) håller `namn`/`reference` kvar på
  servern i stället för att dra in dem i klientbundlen. ✓

## Sammanfattning

**0 Blocker / 0 Major / 0 Minor.** Ingen re-review krävs — inga fynd att stänga.

**Eskalering till Klas:** nej.

## Buret vidare, INTE ett fynd mot denna PR

`page.tsx:65-89` protokollför en **pre-existerande, mätt** `Referer`-läcka på backstop-vägen: en
avvisad URL som når sex subresource-requests när sidnivå-`redirect()` degraderar till en
meta-refresh. Deltat varken förvärrar eller rör den, och proxyn är fortsatt den primära grinden.
Nämnt enbart för att mätningen inte ska gå förlorad, inte som något att åtgärda här.
