# Security-audit: Fas E2j sök-commit-modell (PII-capture-trigger)

**Status:** ✓ APPROVED
**Granskat:** 2026-06-12
**Auktoritet:** GDPR Art. 5(1)(c) (data-minimering), Art. 6(1)(f) (berättigat intresse), Art. 13 (informationsskyldighet), Art. 32 (säkerhet vid behandling) · CLAUDE.md §5.4, §9.2 · ADR 0060 + amendment 2026-06-12
**Scope:** Obligatorisk per CLAUDE.md §9.2 + CTO-dom VAL 7 — PII-insamlingsvägen ändras (NÄR söktermer/`q` persisteras till `recent_job_searches`).
**Underlag läst:** ADR 0060 (Beslut 3 + Mekanik-not 1/2/5/6 + amendment 2026-06-12), architect-dom + CTO-dom 2026-06-12, on-disk-diffen (BE behavior/markör/query/endpoint/capturer + FE search-params/strip-island/hero-search/hero-filters/results/page/api-helper).

**Sammanfattning:** Ändringen **stärker** GDPR-posturen (materiell data-minimering, Art. 5(1)(c)) utan att öppna någon ny attack-yta. Inga Blockers, inga Major, inga Minor. Ett par observationer noterade som icke-blockerande (befintlig Klas-pending privacy-policy-uppgift, oförändrad). Säkerhetsmässigt mergeklar.

---

## (a) Live `router.replace` fångar bevisligen INTE längre — VERIFIERAT

**Finding: Inga läckvägar. Live-förhandsvisning bär aldrig `commit=1`.**

Capture-gaten sitter i `RecentJobSearchCaptureBehavior` rad 48–49: `if (!capt.Commit) return response;` — additiv guard, placerad FÖRE response-markör-, anonym- och default-browse-guarderna. `ICapturesRecentSearch.Commit` (rad 51) defaultar via `ListJobAdsQuery` rad 36 till `bool Commit = false`. Behaviorn no-op:ar alltså om FE inte explicit sätter flaggan.

FE: enda vägen som tänder flaggan är `withCommitFlag(href)` (`search-params.ts` rad 45–49), och den anropas bara via `commit(next, announce, markCommit=true)` (`jobb-hero-search.tsx` rad 211–219). Jag spårade varje commit-anrops-site:

| Commit-site | fil:rad | `markCommit` | commit=1? |
|---|---|---|---|
| `runDelta` (live-delta från `onFieldChange`) | jobb-hero-search.tsx:230–236 | utelämnad ⇒ **false** | **NEJ** ✓ |
| `onFieldChange` (mellanslag/komma per ord) | jobb-hero-search.tsx:240–249 | (anropar runDelta) | **NEJ** ✓ |
| `onSubmitText` (Enter/Sök) | jobb-hero-search.tsx:308–315 | **true** | JA (avsiktlig) ✓ |
| `onSelectSuggestion` (förslags-val) | jobb-hero-search.tsx:295 | **true** | JA (avsiktlig) ✓ |
| `onClear` (×-clear, semantik ii) | jobb-hero-search.tsx:330 | **true** | JA (avsiktlig) ✓ |
| Toolbar `removeChip`/`clearAll`/`onSortChange` | jobb-results-toolbar.tsx:119–145 | `withCommitFlag(buildJobbHref(...))` | JA (avsiktlig, Klas-VAL 5) ✓ |

Det kritiska negativa beviset: `runDelta`s `commit(...)`-anrop (rad 230) skickar **ingen tredje param** ⇒ `markCommit` defaultar false ⇒ `router.replace(href, ...)` utan suffix (rad 216). Live-typing per ord kan därför aldrig nå capturen. Bekräftas av FE-test `jobb-hero-search.test.tsx:136` ("live-typing (mellanslag) committar UTAN commit-intent — ingen capture") + `search-params.test.ts:28` (commit ingår ALDRIG i `buildJobbHref`). Backend-sidan bekräftas av att `commit` exkluderas ur både `JobbUrlState` (`search-params.ts` rad 21–28) och `resultsKey`/Suspense-key (`page.tsx` rad 113–120, 212) — flaggan kan inte smyga in via state-serialisering.

**Popover-filtren (`jobb-hero-filters.tsx`):** Klas lämnade dem medvetet UTAN `commit=1` — `commit()` rad 150–164 kör bara `buildJobbHref(...)` rått. **Bedömning: detta är en defensibel data-minimerings-hållning, ingen defekt.** Popover-klick är inkrementell filterkomposition (välj yrkesgrupp → välj kommun → ...) på samma sätt som man bygger en sökning genom att skriva — varje mellansteg är inte "en sökning användaren körde". Att inte fånga dem är konsistent med commit-gatens hela syfte (fånga avsiktliga slut-tillstånd, inte komposition) och drar insamlingen åt det minimerande hållet (Art. 5(1)(c)). Konsekvens: en ren popover-only-sökning som aldrig passerar en commit-punkt (Enter/Sök/förslags-val/toolbar) capture:as inte. Det är en under-capture av bekvämlighets-historik, inte ett säkerhets- eller compliance-problem — och commit-punkterna (inkl. toolbar) plockar i praktiken upp de flesta avsiktliga slut-tillstånd. Ingen åtgärd krävs; värt en rad i amendmentet om Klas vill att popover-only ska bli återfinnbar senare (produktval, ej säkerhet).

## (b) `commit=1` kan inte forgeras till skadlig capture — VERIFIERAT

**Finding: Worst case är benignt — en klient som fångar sin EGEN historik. Ingen cross-tenant-yta, ingen injection, ingen auth-bypass.**

- **JobSeeker-lookup är server-side och icke-klient-styrd.** `RecentJobSearchCapturer.CaptureAsync` (rad 39–47) slår upp `jobSeekerId` via `js.UserId == userId`, där `userId` kommer från behaviorn rad 54 (`currentUser.UserId`). `CurrentUser.UserId` (`CurrentUser.cs` rad 13–21) härleds ur den validerade JWT:ns `sub`-claim — inte ur någon query-param. En angripare kan inte rikta capture mot annan seeker oavsett vad de skickar i `?commit=`.
- **Ingen injection-yta.** `commit` är en `bool` (endpoint `JobAdsEndpoints.cs` rad 53 binder `bool commit = false`; query rad 36). Den binära parametern kan inte bära `IsInEnum`-stil-abuse (jfr `?dimension=7` på facet-endpointen) — ASP.NET binder icke-parsbara värden till default `false`. Den ingår inte i `FilterHash`/`SearchCriteria` och påverkar inte persistens-shapen — endast no-op-gaten. Bekräftat i `ListJobAdsQuery` rad 30–36 + `RecentJobSearchCaptureBehavior` rad 48–49.
- **Auth + rate-limit intakt.** Endpoint-gruppen har `.RequireAuthorization()` (`JobAdsEndpoints.cs` rad 25) + `ListReadPolicy` (rad 67). Behaviorns anonym-guard (rad 54) är defense-in-depth ovanpå det. Volym-abuse (spam-capture av egen historik) är cap-bunden till `MaxPerSeeker = 20` (`RecentJobSearchCapturer.EnforceCapAsync` rad 94–118) + rate-limit — ingen DoS-vektor utöver befintlig list-query.

Worst case för en uppsåtlig klient: den fångar fler/färre poster i sin EGEN `recent_job_searches`. Lägsta tänkbara känslighet, ingen påverkan på andra tenants.

## (c) Delad/bokmärkt `?commit=1`-länk-edge + strip-mitigering — VERIFIERAT, residual benign

**Finding: Mottagaren capture:ar till SIN EGEN historik vid första server-render; `StripCommitParam` förhindrar RE-capture. Residualrisken är benign och mitigeringen är sund.**

Korrekt förstådd sekvens: `JobbResults` (RSC) kör `getJobAds({..., commit})` (`jobb-results.tsx` rad 88–99) på servern under första render → capturen fyrar för mottagaren. `StripCommitParam` (`strip-commit-param.tsx`) körs i en client-`useEffect` EFTER mount och tar bort `?commit=1` via `router.replace`. Mitigeringen eliminerar alltså inte den allra första capturen vid öppnandet av länken — den förhindrar att flaggan blir kvar och re-capture:ar vid varje efterföljande navigering/reload på samma länk.

Detta matchar exakt vad architect/CTO band (architect Del 5 edge; CTO VAL 5 punkt 4). Bedömning:
- **Residualen är benign:** mottagaren fångar en sökning i sin egen historik. Ingen cross-tenant-läcka (det är mottagarens egen autentiserade kontext). Capturen är dessutom idempotent via `FilterHash` + UNIQUE-upsert (`RecentJobSearchCapturer` rad 51–61) — en redan-existerande post bumpas bara.
- **Mitigeringen är sund:** `StripCommitParam` rad 32 läser `window.location.href` i effekten (client-only, korrekt — `window` finns inte vid SSR), bygger ren URL och bevarar övriga params exakt (`url.searchParams.delete(COMMIT_PARAM)` rad 34, inte en blank URL). `active`-guarden (server-känt `params.commit === "1"`, `page.tsx` rad 75) hindrar onödiga replace:ar. Strip-replacen bär aldrig själv `commit=1` (den raderar parametern), så den kan inte trigga en ny capture-loop. Den går via `router.replace` på en icke-state-param, vilket hero-spegelns own-roundtrip-mekanik korrekt behandlar som en ren icke-state-ändring (kommentar rad 19–23, bekräftad mot `jobb-hero-search.tsx` rad 172–180).
- **Inget XSS/open-redirect i strippen:** `new URL(window.location.href)` + `router.replace(\`${url.pathname}${url.search}\`)` (rad 32–35) opererar på den faktiska adressen och navigerar samma-origin relativt — ingen användarstyrd redirect-destination, ingen `dangerouslySetInnerHTML`, ingen `eval`.

Att första-öppnings-capturen sker är acceptabelt och oundvikligt utan att blockera RSC-streamen på en client-effekt — och eftersom utfallet är "mottagarens egen historik" finns inget skyddsvärt som bryts. Godkänns.

## (d) GDPR — data-minimering STÄRKT, Art. 13 mer sanningsenlig — VERIFIERAT

**Finding: Ändringen är en materiell GDPR-förbättring (Art. 5(1)(c)). Art. 13-disclosuren blir mer korrekt, inte mindre. Inget görs sämre.**

- **Art. 5(1)(c) data-minimering stärkt:** Före E2j fångade behaviorn varje lyckad list-query, vilket efter E2i:s live-`router.replace` betydde att varje keystroke-mellansteg ("system", "systemut", "systemutvecklare"...) persisterades som PII (`q` är PII per ADR 0060 Mekanik-not 5). Detta var en over-collection — empiriskt bekräftat (dev-DB full vid cap=20 av mellanstegsspam). Commit-gaten gör att vi nu endast persisterar de söktermer användaren explicit committade. Detta är *insamlings*-minimering (rätt mekanism — Art. 5(1)(c) reglerar insamling), inte retention-städning efteråt. Materiell förbättring av posturen.
- **Art. 13 sanningshalt förbättrad:** Disclosurens utlovade ändamål ("vi sparar sökningar du kör") blir bokstavligt sant när mellanstegen inte längre fångas. Inget i diffen rör eller försämrar den befintliga Art. 13-ytan.
- **Art. 6(1)(f) (berättigat intresse) intakt:** Rättslig grund oförändrad; commit-gaten stärker proportionalitets-/balanstest-argumentet (mindre data, samma ändamål).
- **Art. 17-cascade intakt:** Inget i diffen rör `AccountHardDeleter`-cascaden (ADR 0060 Mekanik-not 5) eller `recent_job_searches`-schemat — radering-rätten är oförändrad.
- **Pending Klas-uppgift oförändrad (icke-blockerande för denna merge):** Privacy-policy-uppdatering + inline-disclosure (ADR 0060 Mekanik-not 6) är en känd, redan-pending Klas-uppgift som INTE introduceras eller försämras här. Per ADR 0060 är den blockerande för det FE-flöde som börjar *rendera/konsumera* RecentJobSearches, inte för denna trigger-precisering — och E2j gör disclosuren mer (inte mindre) sann. Jag bekräftar att den inte blir sämre. Den kvarstår som Klas-uppgift, ingen ny TD (CTO FAS-DISCIPLIN-not).

**Ingen ny PII-kategori introduceras** — `q`/sökhistorik samlas redan (ADR 0060). E2j *minskar* insamlingen av en redan-beslutad kategori. Därför ingen ny ADR/DPIA-trigger utöver den befintliga (amendmentet räcker, per CTO/architect).

## (e) Ny PII-loggning? Hemligheter? Ny input-validerings-yta? — VERIFIERAT, inga

**Finding: Ingen ny PII-loggning, ingen hemlighets-exponering, ingen riskabel ny input-yta.**

- **PII-logghygien oförändrad och korrekt.** `RecentJobSearchCaptureBehavior` catch-grenen (rad 92–101) loggar endast `ex.GetType().FullName` + `typeof(TMessage).Name` via `LogCaptureFailed` (rad 106–110) — **inte** `q`, inte hela Exception-objektet, inte SQL-parametrar. Kommentaren rad 94–99 (PII-hygien, security-auditor F6 P4a High-1) är fortfarande respekterad — diffen rör inte loggraden. `RecentJobSearchCapturer.LogRaceFallback` (rad 120–124) loggar `jobSeekerId.Value` (en GUID, ej PII-fritext) på Debug-nivå. Inga söktermer i någon logg.
- **Inga hemligheter.** Inga API-nycklar, connection strings eller tokens i diffen. `commit` är en boolean-signal.
- **Input-validerings-yta:** `commit` är `bool` på endpoint + query (binär, default false). Ingen enum/range/string-yta att missbruka. ASP.NET binder ogiltiga värden till `false` (fail-safe — saknad commit ⇒ ingen capture, default mot minimering). `StripCommitParam` opererar på `window.location` samma-origin (ingen användarstyrd navigerings-destination). Ingen ny SQL/raw-query (capturen använder EF Core parametriserat, oförändrat).

---

## Praise

- **Commit-gaten är en GDPR-förbättring förklädd som UX-fix** — insamlings-minimering (rätt Art. 5(1)(c)-mekanism), inte retention-städning. Korrekt arkitekturval.
- **`commit` strikt utanför `JobbUrlState`/`sameUrlState`/`buildJobbHref`/`resultsKey`** (`search-params.ts` rad 33–49, `page.tsx` rad 113–120/212) — signal vs tillstånd-separationen är konsekvent genomförd och förhindrar att flaggan läcker in i delningsbara/cachade URL:er.
- **`markCommit` default false** (`jobb-hero-search.tsx` rad 211) — fail-safe default: om en framtida commit-site glöms blir utfallet under-capture (ingen PII-läcka), inte over-capture. Rätt riktning på defaulten ur minimerings-synpunkt.
- **JobSeeker-lookup förblir server-side via JWT-`sub`** (`CurrentUser.cs` rad 17–19, `RecentJobSearchCapturer` rad 39–43) — ingen trust flyttad till klient utöver "när min egen historik fångas". Capturer-invarianten orörd.
- **PII-logghygien bevarad genom hela diffen** — catch loggar exception-typ, aldrig `q`.
- **`StripCommitParam` mitigerar delad-länk-edgen** utan att blockera RSC-streamen och utan XSS/redirect-yta.
- **Behaviorns guard-ordning** (commit → response-markör → anonym → default-browse → SearchCriteria) gör att tom sökning fortfarande aldrig capture:as även med `commit=1` — additiv, inte ersättande.

## Icke-blockerande observationer (ingen åtgärd krävs för merge)

1. **Privacy-policy + inline-disclosure (ADR 0060 Mekanik-not 6)** — befintlig Klas-pending-uppgift, oförändrad. E2j gör den mer sanningsenlig. Ingen ny TD; ingen merge-blockering för denna trigger-precisering.
2. **Popover-only-sökningar fångas inte** (`jobb-hero-filters.tsx` saknar `withCommitFlag` medvetet) — defensibel under-capture, ren minimerings-hållning. Om Klas senare vill att rena popover-slut-tillstånd ska bli återfinnbara är det ett produktval (lägg commit på popover-`commit()`), inte en säkerhets-/compliance-fråga.

---

## Verdikt

**✓ APPROVED.** Inga Blockers, inga Major, inga Minor. Ändringen stärker GDPR-data-minimeringen (Art. 5(1)(c)) materiellt, gör Art. 13-disclosuren mer sanningsenlig, och öppnar ingen ny attack-yta — `commit` är en binär, server-validerad, transient signal som endast styr NÄR en klients EGEN historik fångas. Cross-tenant-capture är omöjlig (server-side JWT-`sub`-lookup). Delad-länk-edgen är benign och korrekt mitigerad. PII-logghygienen är bevarad. Säkerhetsmässigt mergeklar.

— security-auditor, 2026-06-12
