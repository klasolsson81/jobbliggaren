# CTO-beslut — /sokningar civic-utility-läcka (concept-id i sparade-sökningar-listan)

**Datum:** 2026-05-17
**Decision-maker:** senior-cto-advisor (read-only — persisteras av CC)
**Status:** Entydiga beslut. Implementation kör non-stop direkt på dessa.
**Kontext:** Fortsättning på ADR 0043-temat (civic-utility: JobTech concept-id ut ur användarytan). ADR 0043 löste `/jobb` sök-ytan men designade aldrig `/sokningar`-listan. Klas-GO "enligt rek" = GO att ta batchen given; flagga endast om delbeslut överraskande kräver separat GO.
**Källor lästa:** ADR 0043 (Beslut C/D/E), `2026-05-17-fynd2-taxonomi-acl-cto.md` (MAP-1/2/3 + Scope-fork), `2026-05-17-fynd2-security-auditor.md` (GO, cap-faktum), `saved-search-list.tsx`, `ListSavedSearchesQuery/Handler.cs`, `SavedSearchDto.cs`, `saved-searches.ts` (FE-DTO), `sokningar/page.tsx`, `lib/api/saved-searches.ts` + `lib/api/taxonomy.ts`, `ITaxonomyReadModel.cs`, `ResolveTaxonomyLabelsQueryValidator/Handler.cs`, `TaxonomyReadModel.cs`, `save-search-button.tsx`, `scripts/visual-verify.ts`.

---

## Faktakorrigering innan beslut (on-disk verifierat)

Frågans premiss innehåller två avvikelser mot kod på disk. Beslutet vilar på koden, inte premissen:

1. **Cap-värdet:** Frågan säger `ResolveTaxonomyLabelsQueryValidator` cap = `MaxConceptIds*2 = 20`. Detta är **korrekt mot koden** (`ResolveTaxonomyLabelsQueryValidator.cs:17` — `MaxConceptIdsPerCall = SearchCriteria.MaxConceptIds * 2`), verifierad och GO:ad av security-auditor (`2026-05-17-fynd2-security-auditor.md` rad 9/23). ADR 0043 brödtext och MAP-3-rapporten säger `=10`; den faktiska implementationen landade på `*2=20` (Ssyk-lista + Region-lista i *samma* sparade sökning). On-disk = 20. Detta påverkar approach-valet nedan.
2. **"Spara-hjälptexten i job-ad-filters":** Strängen `"SSYK-kod"` finns **inte** i `job-ad-filters.tsx` utan i `save-search-button.tsx:70` (`Lägg till minst ett filter (sökord, SSYK-kod eller region)…`). Det är samma copy-läcka, korrekt fil för in-block-fix angiven nedan.

Ingen av dessa ändrar problembilden — bekräftar den med exakt fil-rad.

---

## Beslut: Approach A — server-side namn-berikning i `ListSavedSearchesQuery` via `ITaxonomyReadModel.ResolveLabelsAsync`

`ListSavedSearchesQueryHandler` injicerar `ITaxonomyReadModel`, resolverar Ssyk+Region concept-id → namn per sparad sökning **i samma handler**, och `SavedSearchDto` utökas med två namn-fält (label-projektioner). `saved-search-list.tsx` `criteriaSummary` renderar namnen istället för rå `s.ssyk.join(", ")`. Ingen ny endpoint, ingen klient-fan-out, ingen ny round-trip.

**Approach B (ny bulk-endpoint) och C (denormaliserad label-lagring) avvisas.** Motivering och avvisning nedan.

---

## Motivering mot principer

### Fan-out-DoS-oron är felställd — den gäller en annan yta

ADR 0043 Beslut D / MAP-3 dimensionerade DoS-skyddet (`TaxonomyReadPolicy` 20/60s, reverse-lookup-cap, ETag, `private`-cache) för **`GET /api/v1/job-ads/taxonomy/labels`** — en auth-gated HTTP-endpoint som klienten anropar. Capen (`MaxConceptIdsPerCall=20`) är en **validator på query-objektet bakom den endpointen** (`ResolveTaxonomyLabelsQueryValidator`), inte på `ITaxonomyReadModel.ResolveLabelsAsync` självt. Porten har ingen cap.

Approach A anropar **aldrig endpointen**. Den anropar `ITaxonomyReadModel.ResolveLabelsAsync` direkt i en annan handler, inom samma process. `TaxonomyReadModel` är (`TaxonomyReadModel.cs:20-115`):

- Singleton med in-memory-cache (`Volatile`-publicerad `CacheState`), snapshot läst **en gång per process-livstid**.
- `ResolveLabelsAsync` = O(n) dictionary-`TryGetValue` mot `LabelByConceptId` (rad 44-51). Ingen DB-touch på varm väg, ingen extern hop, ingen HTTP-yta.

En sparad sökning bär per domän-invariant (`SearchCriteria.MaxConceptIds=10`) ≤10 Ssyk + ≤10 Region = ≤20 concept-id. N sparade sökningar = ≤20·N dictionary-`TryGetValue` mot in-process-minne under **en** redan auth-gated, redan rate-limitad (`ListReadPolicy` på `/saved-searches`) request. Det finns ingen "N round-trips", ingen "stor batch >cap", ingen fan-out. **Beslut D är uppfyllt, inte brutet — det gäller endpointen, A rör den inte** (samma resonemangsklass som ADR 0043 själv använder: "ADR 0042 rad 21-constraint uppfyllt, inte brutet — lokal snapshot är per definition inte på sök-vägen").

- **Evans 2003, *DDD* kap. 14 (Anticorruption Layer):** `ITaxonomyReadModel` ÄR ACL:n materialiserad. Dess uttalade syfte (`ITaxonomyReadModel.cs:5-17` + `ResolveLabelsAsync`-doc rad 28-34) är *exakt* "reverse-lookup för redan-sparade sökningar/valda chips". `/sokningar`-listan är den kanoniska andra-konsumenten porten redan designades för. Att använda porten där är att fullfölja ACL:n, inte att tänja den. Den läckande bounded context (JobTechs ubiquitous language i UI:t) tätas på den sista ytan där den fortfarande läcker.
- **DRY / SPOT (Hunt/Thomas 1999):** Namn↔concept-id-översättning (inkl. `"Okänd kod (<id>)"`-fallback) har redan en single point of truth: `TaxonomyReadModel`. Approach A återanvänder den. Approach C skapar en *andra* sanningskälla (snapshot-kopia i `saved_searches`) — exakt det DRY-brott ADR 0043 Beslut A avvisade frontend-konstanten på ("två sanningskällor, drift garanterad").
- **YAGNI / KISS (Beck; Fowler 2018, *Refactoring* kap. 3 Speculative Generality):** Problemet är "rendera N×≤20 namn på en JobSeeker-scopad lista som i praktiken har en handfull rader" (`ListSavedSearchesQuery.cs:7-11` dokumenterar explicit låg domänvolym, ej ens paginerad). Den enklaste mekanismen som löser det är att be den redan existerande porten om namnen i den redan existerande handlern. Ny endpoint (B) eller schema-migration (C) är lösningar på problem som inte finns.
- **Blast-radius (Ford/Parsons/Kua 2017, *Building Evolutionary Architectures* kap. 4):** A = 1 handler-rad (DI) + ~5 rader resolve-loop + 2 DTO-fält + 1 FE-render-rad + 1 copy-rad. B = ny endpoint + ny rate-limit-policy-övervägande + ny FE-fetcher + ny round-trip + klient-orkestrering + ADR-amendment. C = EF-migration på `saved_searches` + write-path-ändring (snapshot vid spar-tid) + stale-invalidering + ADR 0039-kontraktsbrott. A har ojämförligt minst blast-radius för full civic-vinst.
- **Clean Architecture lager (CLAUDE.md §2.1):** A håller sig inom befintliga lager: Application-handler → Application-port → Infrastructure-impl. Ingen ny lager-yta, ingen ny endpoint-yta, ingen domän-mutation. `JobAdSearch.ApplyCriteria` orörd (ADR 0043 Beslut E garanti bevarad — A rör bara presentations-projektionen, inte filter-vägen).
- **Mastercard-test (CLAUDE.md §1):** En utomstående arkitekt ser att porten designades med en `ResolveLabelsAsync` vars doc-kommentar uttryckligen säger "för redan-sparade sökningar" — och att den faktiskt används för sparade sökningar. Det är konsekvent design. B (ny endpoint för data redan tillgänglig in-process) eller C (denormaliserad kopia av referensdata in i ett aggregat) skulle båda noteras som onödig yta respektive lager-läcka.

---

## Avvisade alternativ

### Approach B — ny bulk-endpoint med egen högre cap + rate-limit (utöka Beslut D-yta)

**Avvisad.** Bygger en HTTP-endpoint för data som redan är tillgänglig som ett O(1)-portanrop i den handler som redan kör. Detta *skapar* den fan-out-/round-trip-yta frågan vill undvika i stället för att eliminera den: en separat klient-anrop från `/sokningar`-rendering, en ny rate-limit-budget att kalibrera, en ny endpoint att säkerhetsgranska. Det utökar Beslut D:s DoS-yta för noll vinst — A behöver ingen endpoint alls. Speculative generality (Fowler 2018 kap. 3): "vi kanske vill ha bulk-labels via HTTP nån gång" är inte ett nuvarande behov. Bryter YAGNI/KISS och maximerar blast-radius mot ett icke-existerande problem.

### Approach C — denormaliserad label-lagring i `saved_searches` (snapshot vid spar-tid)

**Avvisad — bryter ADR 0039-kontrakt och DRY.** ADR 0043 "Relation till andra ADR" (rad 133) fastslår: `saved_searches.criteria` jsonb-converter/comparer/shape/dedupe-invarianter (ADR 0039 Beslut B.1) är **orörda**; "namn ingår aldrig i VO:t". Att lagra labels i `saved_searches` skulle (a) kräva EF-migration på `saved_searches` som ADR 0043 uttryckligen garanterar inte sker, (b) introducera stale-label-problemet (snapshot regenereras → sparad label divergerar från taxonomin — den exakta drift ADR 0043 löste via *runtime* reverse-lookup med `"Okänd kod"`-fallback), (c) skapa en andra sanningskälla för namn-översättning (DRY-brott, Hunt/Thomas 1999). Reverse-lookup-vid-läsning (A) är redan ADR 0043:s designade svar på taxonomi-drift (`ITaxonomyReadModel.cs:31-33`); C återinför precis det problem den mekanismen finns för att lösa.

### Approach D-kandidat — klient-side `resolveTaxonomyLabels`-anrop från `/sokningar` (avvisad innan den föreslås)

`lib/api/taxonomy.ts:75` `resolveTaxonomyLabels` finns och *skulle* kunna anropas från `sokningar/page.tsx`. **Avvisad** — det ÄR fan-out-vägen frågan varnar för: en eller flera HTTP-anrop mot `/taxonomy/labels` per `/sokningar`-render, mot endpoint-capen `MaxConceptIdsPerCall=20` (en lista med N sökningar × upp till 20 ≫ 20 → måste chunkas till N round-trips eller överskrida cap). A eliminerar detta genom att aldrig lämna processen.

---

## Trade-offs accepterade

- **`SavedSearchDto` växer med 2 fält** (`SsykLabels`/`RegionLabels` eller motsv. — `IReadOnlyList<TaxonomyLabelDto>` eller parallell `IReadOnlyList<string>`). Fil-/fält-antal är ingen design-axel (samma resonemang som MAP-2:s port-fil-trade-off). Se ADR 0039-kontraktsnot nedan — detta är **additivt**, inte ett kontraktsbrott.
- **`ListSavedSearches` blir beroende av taxonomi-snapshot vid läsning.** Acceptabelt: porten är singleton in-memory-cache (`TaxonomyReadModel.cs:35-70`), första anrop laddar ~2 300 rader en gång per process, därefter dictionary-lookup. Graceful degradation inbyggd (`"Okänd kod (<id>)"`, aldrig throw — `TaxonomyReadModel.cs:48`). En sparad sökning vars concept-id försvunnit ur taxonomin renderar fallback-label, exakt som `/jobb`-chips redan gör. Konsekvent UX, ingen ny felväg.
- **Namn-resolve sker N gånger (en per sparad sökning).** Mot in-process-dictionary, för en icke-paginerad lista med dokumenterat låg volym. Noll mätbar kostnad. Om volymen någonsin motbevisar antagandet är det en `ListSavedSearches`-paginerings-fråga, inte en namn-resolve-fråga.

---

## ADR 0039-kontrakt: rör `SavedSearchDto`-ändringen det?

**Nej — additiv DTO-utökning, inget kontraktsbrott.** `SavedSearchDto` (`SavedSearchDto.cs:7-17`) är en **read-DTO ut ur Application-gränsen** (Query-svar), inte SavedSearch-aggregatet eller dess persisterade `SearchCriteria`-VO. ADR 0039 Beslut B.1-invarianterna gäller `saved_searches.criteria` jsonb-VO:t (form, dedupe, comparer) — **orört**: `s.Criteria.Ssyk/Region` läses fortfarande som concept-id, VO:t ändras inte, ingen migration. De nya fälten är en **presentation-projektion ovanpå** befintliga concept-id-fält, helt analogt med hur `/jobb`-chips reverse-lookupar utan att röra VO:t (ADR 0043 rad 133). FE-DTO-schemat (`saved-searches.ts:44-56`) speglar additivt (nya optional/array-fält). `sortByFromWire`-kontraktet och allt övrigt orört. **Ingen ADR 0039-amendment krävs.**

---

## ADR-konsekvens

**Ingen ny ADR. Ingen ADR 0043-amendment. Ingen ADR 0039-amendment.**

ADR 0043 Beslut C/E täcker redan denna mekanism konceptuellt: ACL:ns reverse-lookup-operation är designad "för redan-sparade sökningar" och ligger "utanför query-/filter-vägen". Att tillämpa den på `/sokningar`-listan är **implementation inom redan accepterad ADR 0043-arkitektur**, inte ett nytt arkitekturbeslut (CLAUDE.md §8 p9 — ADR krävs för arkitekturbeslut; detta är konsekvent applicering av ett befintligt). En kort **session-logg-/current-work-notering** räcker som spårning ("ADR 0043 reverse-lookup utvidgad till /sokningar-listan, samma port, ingen kontraktsändring").

Detta är **inget delbeslut som överraskande kräver separat Klas-GO** (per din flaggnings-instruktion): ingen ADR-flip, ingen fas-transition, ingen ny extern yta, ingen migration, inget domänkontraktsbrott. Ligger inom Klas-GO "enligt rek".

---

## Agent-sekvens

ADR 0043:s egen sekvens (test-writer FÖRST/TDD, security-auditor BLOCKING) är prejudikat. För denna mindre, lägre-risk-utvidgning:

1. **dotnet-architect — FÖRST (kort).** Verifiera DTO-utvidgnings-formen (`IReadOnlyList<TaxonomyLabelDto>` vs parallella namn-listor som speglar Ssyk/Region-indexering) och att `ITaxonomyReadModel`-injektion i `ListSavedSearchesQueryHandler` inte introducerar oönskat Application→Infrastructure-koppling (det gör den inte — porten är Application-ägd, samma mönster som handlern redan har mot `IAppDbContext`). Liten skiss, ingen multi-approach kvar (CTO har valt).
2. **test-writer — FÖRST/TDD (innan impl).** Handler-test: (a) sparad sökning med kända concept-id → namn i DTO; (b) okänt/stale concept-id → `"Okänd kod (<id>)"`-fallback, ej throw; (c) tom Ssyk/Region → tomma label-listor, inget portanrop nödvändigt på tom input; (d) ordning Ssyk-label[i] ↔ Ssyk[i] bevarad. FE: `saved-search-list.test.tsx` uppdateras — `criteriaSummary` visar namn, aldrig rå concept-id; befintlig fixtur (`saved-search-list.test.tsx:17` `ssyk: ["MVqp_eS8_kDZ"]`) ska assertas rendera namnet.
3. **Backend-impl test-grön:** handler-DI + resolve-loop; `SavedSearchDto` + FE-Zod-schema additivt; DI redan registrerad (`ITaxonomyReadModel` finns sedan ADR 0043 — verifiera, lägg ej till dubblett).
4. **nextjs-ui-engineer:** `saved-search-list.tsx` `criteriaSummary` (rad 17-18) renderar namn; **ta bort `font-mono`** på rad 32 (font-mono var till för concept-id — civic-utility: namn ska inte vara monospace, samma princip som ADR 0043 tog bort font-mono-chips på /jobb). Copy-fix `save-search-button.tsx:70` `"SSYK-kod"` → `"yrke"` (in-block, se nedan).
5. **security-auditor — input, ej full BLOCKING-rond.** A öppnar **ingen ny endpoint/HTTP-yta** (kärnskälet auditorns Beslut D-rond fanns). Bekräfta endast: ingen PII i ny logg-yta (concept-id loggas ej), ingen ny extern yta. Lättviktig — flagga bara om något oväntat.
6. **design-reviewer VETO + visual-verify:** `/sokningar` renderad UI ändras markant (concept-id → namn, font-mono bort) → screenshot-rond + Klas godkänner. Se visual-verify-not nedan.

---

## In-block-fixar (CLAUDE.md §9.6 — samma batch, ej TD)

1. **Copy-läcka `save-search-button.tsx:70`:** `"Lägg till minst ett filter (sökord, SSYK-kod eller region)…"` → ersätt `SSYK-kod` med `yrke` (civic-utility, CLAUDE.md §10.3; samma jargong-läcka ADR 0043 adresserar, samma fas, samma tema). Verifiera samtidigt `tests/e2e/jobb.spec.ts:31` (`getByLabel("SSYK-kod")`) — den asserterar en gammal label; uppdatera assertionen till den nya picker-labeln (`"Yrkesområde"`) i samma batch, annars är E2E:n redan stale/röd mot ADR 0043-leveransen (in-block: hör till samma jargong-utrensning, samma fas).
2. **`font-mono` på `saved-search-list.tsx:32`:** ta bort — monospace fanns för concept-id; namn ska inte vara monospace (civic-utility, jobbpilot-design-principles regel 3/7; konsekvent med ADR 0043:s font-mono-chip-borttagning på /jobb).

Dessa hör till samma fas (sök-yta/civic-utility = Fas 2-domän) och samma tema (concept-id ut ur användarytan). §9.6: default = fixa in-block, ingen saknad funktion-dependency, ingen annan fas. **Inga TD-lyft.**

---

## Visual-verify-skriptets stale `jobb-chip-filled` — fixa i samma batch: JA

`scripts/visual-verify.ts:243-250` gör `page.getByLabel("Yrkesområde")` → `.fill("MVqp_eS8_kDZ")` → `.press("Enter")` → väntar på `"Ta bort MVqp_eS8_kDZ"`. Efter ADR 0043 är "Yrkesområde"-fältet en **picker (combobox/select över namn)**, inte ett fritext-concept-id-fält (`occupation-picker.tsx`/`region-picker.tsx` emitterar concept-id från **namn-val**, användaren skriver aldrig `MVqp_eS8_kDZ`). `.fill()` med ett rått concept-id mot en namn-picker, och assertion på `"Ta bort MVqp_eS8_kDZ"` (chip visar nu **namn**, aldrig concept-id — `taxonomy-chip-list.tsx:11-12`), är **garanterat stale** — `try/catch` sväljer felet till en `console.warn` så regressionen är redan tyst (skriptet "lyckas" utan att fånga state 3).

**Beslut: JA, fixa i samma batch.** Skälet (CLAUDE.md §9.6 — samma fas, samma tema, ingen saknad dependency): ADR 0043-leveransen gjorde det stale; det är efterföljande städning av samma civic-utility-omdesign, inte separat scope. Korrekt mönster: `selectOption`/picker-interaktion mot namn (t.ex. öppna picker, välj "Systemutvecklare" som namn), assertera chip på **namn** (`"Ta bort Systemutvecklare"`-mönster eller motsv. dismiss-label) — speglar hur `occupation-picker.test.tsx`/`region-picker.test.tsx` redan testar (namn-val → namn-chip). nextjs-ui-engineer äger den konkreta selector-formen mot faktisk picker-markup; verifiera mot `occupation-picker.tsx` render. Lämna `try/catch`-resiliensen men säkerställ att happy path faktiskt fångar state 3 (annars är screenshot-täckningen en illusion — design-reviewer granskar tom-/fel-state utan att veta det).

---

## TL;DR

| Fråga | Beslut | Klas strategisk GO? |
|---|---|---|
| Hur lösa /sokningar-läckan utan fan-out-DoS | **Approach A** — server-side namn-berikning i `ListSavedSearchesQueryHandler` via `ITaxonomyReadModel`. Ingen endpoint, ingen klient-fan-out, in-process O(1)-lookup. Beslut D rör endpointen, A rör den inte. | NEJ |
| Approach B (ny bulk-endpoint) | Avvisad — skapar fan-out-ytan i stället för att eliminera den; speculative generality | — |
| Approach C (denormaliserad label-lagring) | Avvisad — bryter ADR 0039/0043 "saved_searches orört", DRY-brott, stale-label | — |
| Rör `SavedSearchDto`-ändring ADR 0039-kontrakt? | NEJ — additiv read-DTO-projektion; VO/jsonb/dedupe orört; ingen migration | NEJ |
| ADR-konsekvens | Ingen ny ADR, ingen amendment — implementation inom accepterad ADR 0043-arkitektur; session-logg-notering räcker | NEJ |
| Copy `save-search-button.tsx:70` "SSYK-kod"→"yrke" + e2e:31 label | In-block, samma batch (§9.6) | — |
| `font-mono` `saved-search-list.tsx:32` bort | In-block, samma batch (§9.6) | — |
| visual-verify `jobb-chip-filled` stale `.fill` → picker-interaktion | **JA — fixa i samma batch** (§9.6, samma fas/tema, ADR 0043 gjorde det stale) | — |

Inget delbeslut överraskar med separat Klas-GO-krav. Ligger inom Klas-GO "enligt rek". Implementation kör non-stop på dessa beslut. Klas har sista ordet — override medveten, ej gissning.

**Referenser:** Evans 2003 *DDD* kap. 14 (Anticorruption Layer / read-model); Hunt/Thomas 1999 (DRY/SPOT); Beck (XP, YAGNI); Fowler 2018 *Refactoring* kap. 3 (Speculative Generality); Ford/Parsons/Kua 2017 *Building Evolutionary Architectures* kap. 4 (blast-radius); Martin 2017 *Clean Architecture* kap. 22-24 (lager/dependency rule). CLAUDE.md §1 (Mastercard/civic-utility), §2.1 (lager), §8 p9 (ADR-krav), §9.6 (in-block vs TD), §10.3 (rak svenska); jobbpilot-design-principles regel 3/7. ADR 0039 (SavedSearch VO/jsonb-kontrakt — orört), ADR 0043 (Beslut C ITaxonomyReadModel / D DoS-cap / E shadow-prop orörd). Kod-faktum: `ResolveTaxonomyLabelsQueryValidator.cs:17` (cap=*2=20), `TaxonomyReadModel.cs:20-115` (singleton in-memory O(1)), `ITaxonomyReadModel.cs:28-37` (ResolveLabelsAsync designad för sparade sökningar), `SavedSearchDto.cs:7-17`, `ListSavedSearchesQueryHandler.cs:33-43`, `saved-search-list.tsx:13-34`, `save-search-button.tsx:70`, `scripts/visual-verify.ts:243-250`. Security-auditor `2026-05-17-fynd2-security-auditor.md` (GO, cap=20 verifierad).
