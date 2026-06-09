# CTO-dom — Platsbanken sök-paritet Fas C1 (query/filter-layer + yrke-nivåbyte)

**Datum:** 2026-06-09
**Agent:** senior-cto-advisor (decision-maker)
**Scope:** ADR 0067 Beslut 1 + Beslut 7 C1-raden. Fyra multi-approach-beslut. CC gav ingen egen rekommendation (CLAUDE.md §9.6 — CTO är decision-maker).

Verifierad on-disk-discovery 2026-06-09: filter-SPOT (`JobAdFilterCriteria`), `ApplyCriteria`, `SearchCriteria` (MaxConceptIds=10), validator, query, `ITaxonomyReadModel`/`TaxonomyTreeDto`/`TaxonomyReadModel.LoadAsync`, alla shadow-props, `TaxonomyConceptKind` (Municipality + OccupationGroup finns). ADR 0067 Beslut 1 + 7 C1-raden lästa verbatim.

---

## Beslut (a) — Nivåbyte-strategi: **Variant C**

Additivt `OccupationGroup`-fält + ta bort den explicita `Ssyk`-equality-grenen i `ApplyCriteria`, behåll `SsykConceptId`-kolumnen + synonym/q-vägen.

### Motivering
- **Beslut 1 är Accepted — C1 implementerar, omprövar inte.** ADR 0067 Beslut 1 avvisade *occupation-name som konkurrerande filter-dimension* (Option C i ADR:n). Variant C (denna dom) avvecklar occupation-name som explicit equality-filter och inför yrkesgrupp som enda explicit yrke-dimension — i linje med Beslut 1.
- **Variant A återinför exakt det Beslut 1 avvisade.** Två yrke-equality-grenar samtidigt i en SPOT delad av tre konsumenter = ADR 0067:s avvisade Option C på query-nivå. Speculative Generality (Fowler 2018 kap. 3).
- **Ubiquitous language (Evans 2003 kap. 2/14).** En yrke-equality-dimension (ssyk-level-4) — matchar paritet + framtida CV-matchning (TD-93). Två parallella är "semantiskt oklart" (ADR:ns ord).
- **occupation-name-substratet BEVARAS — C offrar inget recall.** `SsykConceptId`-kolumnen + `synonymExpander.Expand(q)`-grenen rörs inte. Endast den explicita `if (criteria.Ssyk.Count > 0)`-equality-grenen ersätts med `OccupationGroup`-equality mot `OccupationGroupConceptId`.
- **SRP / Move Function (Martin 2017 kap. 7; Fowler 2018).**

### Klas-STOPP: **JA — UX-konsekvens C1→E-fönstret**
FE skickar idag occupation-name-ID via `?ssyk=` (Fas E byter picker). Med Variant C blir `?ssyk=` en **no-op** → yrke-filtret i FE slutar fungera som explicit filter mellan C1-merge och Fas E.

**Mitigering:** occupation-name-termen lever kvar i q-FTS/synonym-vägen — fritext "systemutvecklare" ger fortfarande träffar. Bara explicit dropdown-yrke-equality tystnar tills E.

**CTO-rek till Klas:** acceptera C med vetskap om no-op-fönstret, ELLER förkorta C1→E-avståndet. Bygg INTE Variant A för att slippa fönstret — permanent design-skuld för temporär bekvämlighet.

### Avvisat
- **Variant A:** återinför avvisad Option C; Speculative Generality.
- **Variant B (repoint `Ssyk`→OccupationGroupConceptId):** funktionellt lik C i FE-effekt men sämre namngivning (fält heter `Ssyk` men targetar occupation_group = lögn i ubiquitous language, Evans kap. 2).

---

## Beslut (b) — MaxConceptIds-höjning: **100, per-dimension, enhetligt**

### Motivering
- **Verifierat behov (ej spekulativt):** Klas "Välj alla Data/IT-yrken" (70 st) 2026-06-08 → ValidationException. ~12 yrkesgrupper i Data/IT efter nivåbytet > 10. 10 valdes 2026-05-16 innan paritet var mål.
- **Tal 100:** ssyk-level-4-universum ~400 fördelat på ~21 fält (≈19 snitt, största klart < 100); region ~21; kommun ~290 men "alla kommuner i ett län" max ~50. 100 täcker all realistisk paritet-kombination med marginal utan att vara obegränsat.
- **IN(...)-DoS:** Postgres klarar hundratals element mot B-tree-indexerad STORED-kolumn trivialt. Verklig DoS-vektor = *obegränsad* lista, inte 100. Ändligt tak består.
- **jsonb-dedupe + ADR 0045 300ms p95:** ≤100 sorterade+distinct ids = liten jsonb; IN mot B-tree långt under budget.
- **Samma tak alla dimensioner — DRY + KISS.** `MaxConceptIds` är EN konstant (Domain→validator single source). Per-dimension-differentierade tak = accidental complexity utan bärande skäl. Knowledge piece = "ingen filter-lista får vara obegränsad" → en konstant.

### Klas-STOPP: **JA — ADR 0042 Beslut B-amendment**
Ändrar invariant i Accepted ADR. Implementation trivial efter GO (konstant 10→100 + felmeddelande-copy + validator-trösklar + tester). `MaxConceptIds`-single-source-mönstret består.

---

## Beslut (c) — B2-dims (employment_type/worktime_extent): **DEFER till re-ingest klar**

### Motivering
- **"Falsk klar"-disciplin (ADR 0067 Beslut 2; CLAUDE.md §9.6).** Kolumnerna NULL för 100% av ~44k rader tills nattcron-re-ingest. Filter mot 100%-NULL-kolumn returnerar alltid noll i prod = död feature som ser klar ut. Bryter DoD §8.4 (manuellt testad i dev).
- **YAGNI / inkrementell leverans (Beck; Martin kap. 22).** "Samma touch sparar PR" är bekvämlighet, inte princip — och håller inte: B2-dims behöver ändå egen verifierings-touch när data finns. Wire-nu splittrar arbetet ("wire nu, verifiera sen") = §9.6 anti-pattern.
- **Component cohesion (Martin kap. 13, CCP).** B2-filter hör ihop med sin data-tillgänglighet (re-ingest-fasen), inte C1:s read-väg för populerade Klass 1-kolumner.
- **§9.6-kriterium uppfyllt:** annan fas + saknad funktion-dependency (re-ingest). Legitimt defer, ej TD-bloat.

### Klas-STOPP: NEJ (defer = §9.6-default, bygger mindre)
Hör logiskt till D1-grannskapet (där data-täckning ändå verifieras). Verifiera mot ADR 0067-fasplanen om redan fångad; annars 1 TD (Minor, Fas 2/Trigger="re-ingest-täckning > tröskel"). Architect/Klas avgör exakt fas-etikett.

---

## Beslut (d) — DTO-exponering (kommun + ssyk-level-4): **additiv kaskad, occupation-name behålls**

### (d.1) Trädform: kaskad — kommun barn under Region, yrkesgrupp barn under OccupationField
- Spegla domänens/Platsbankens struktur (Evans kap. 2 model-driven). Pickers = Län→Kommun, Yrkesområde→Yrkesgrupp. `ParentConceptId` finns; Municipality/OccupationGroup 1:1-barn.
- `LoadAsync` grupperar via samma `GroupBy(ParentConceptId)`-mönster som `occupationsByField` redan använder — etablerat mönster, ingen ny mekanik.

### (d.2) Befintliga `TaxonomyOccupationFieldDto.Occupations` (occupation-name): BEHÅLLS
- Beslut 1: occupation-name-substratet bevaras (recall + CV TD-93). Open-Closed (Martin kap. 8) — ta inte bort befintligt publikt DTO-fält.

### (d.3) DTO-form: additiv, ej brytande
- Nya nästlade listor (`Regions[].Municipalities`, `OccupationFields[].OccupationGroups`); inga befintliga fält ändras/tas bort. Open-Closed + ISP.
- Trade-off: större payload — acceptabelt (bounded statisk referensdata, cachad singleton en gång/process, ingen per-request-kostnad).

### Klas-STOPP: NEJ (inom C1-scope per Beslut 7 + B1-CTO Beslut 2; ren implementation efter architect-detaljering)

---

## Sammanfattning av Klas-STOPP-flaggor

| Beslut | Vald variant | Klas-STOPP? | Skäl |
|--------|-------------|-------------|------|
| (a) Nivåbyte | **Variant C** | **JA** | FE `?ssyk=` blir no-op i C1→E-fönstret (UX-regression, mitigerad av q-vägen) |
| (b) MaxConceptIds | **100, per-dimension, enhetligt** | **JA** | ADR 0042 Beslut B-amendment |
| (c) B2-dims | **DEFER** | NEJ | §9.6-default; bygger mindre |
| (d) DTO | **Additiv kaskad, occupation-name behålls** | NEJ | Inom C1-scope, ren implementation |

## In-block-fixar (samma C1-touch)
- (a) `JobAdFilterCriteria`: `Ssyk`→`OccupationGroup`; `ApplyCriteria`: equality-gren → `OccupationGroupConceptId`, q/synonym-gren orörd; `ListJobAdsQuery`/Validator/endpoint motsvarande; integration-tester mot populerad `OccupationGroupConceptId`.
- (b) efter Klas-GO: `MaxConceptIds` 10→100 + felmeddelande-copy + validator-trösklar + tester.
- (d) `TaxonomyTreeDto` additiva nästlade listor + `LoadAsync` GroupBy + integration-test.

## Genuina TDs
- Endast (c) B2-query-wiring om ej redan i ADR 0067-fasplanen → 1 TD, Minor, Fas 2/Trigger="re-ingest-täckning".

---

## CTO-uppföljning (b) — cap-tal efter Klas-fråga "200 eller 400?"

Klas-svar på (b): "jag vill att man ska välja flera grupper, så kanske 200 eller 400?" (vill kunna välja MÅNGA/ALLA yrkesgrupper).

### Dom: **MaxConceptIds = 400 per dimension** + "välj alla" = tomt-filter (FE/Application-translation, EJ Domain)

- **400 = ssyk-level-4-universumets storlek** → "Välj alla yrkesgrupper" (~400) träffar aldrig taket. Invarianten speglar domänens verklighet (Evans 2003 kap. 5) — självförklarande "du kan välja upp till alla som finns".
- **"Markera alla" → skicka tom lista** (= inget filter = alla yrken), inte materialisera ~400 ids. Redan systemets semantik (SearchCriteria tom lista = inget filter). YAGNI/KISS — "alla" och "inget filter" är samma resultatmängd. **OBS: detta är FE-kontrakt = Fas E, ej C1.**
- **DoS-analys (400 säkert):** IN(400) mot B-tree-indexerad STORED-kolumn trivialt för Postgres; jsonb ~5–15KB/sparad sökning (TOAST normalt, läses en-i-taget); FTS/ts_rank (q-grenen) är den dyra vägen, oberoende av ssyk-taket. Inom ADR 0045 300ms p95.
- **Avvisat:** 200 (godtycklig, bryter UX för manuellt 250-val under universumstorleken, noll DoS-vinst mot 400); asymmetriskt per-dimension-tak (bryter enhetlighet utan bärande skäl — region binds aldrig av sitt lägre universum).

### In-block C1-fixar för (b)
1. `SearchCriteria.MaxConceptIds` 10→400 + uppdaterad XML-doc ("= ssyk-universumstorlek; 'alla' = tom lista").
2. `ListJobAdsQueryValidator` — refererar konstanten (verifiera inga literal-10).
3. `CreateSavedSearchCommandValidator` / `UpdateSavedSearchCommandValidator` — refererar konstanten (verifiera).
4. **Verifiera ADR 0043 Beslut D reverse-lookup-cap** (`ResolveTaxonomyLabelsQueryValidator`) klarar 400 ids; höj i samma touch om lägre.
5. Tester: boundary 10/11 → 400/401 i `SearchCriteriaTests`, `ListJobAdsQueryValidatorTests`, saved-search-validator-tester + "tom ssyk-lista passerar"-regressionstest.

### Klas-flagga (uppföljning)
- "Markera alla"-UX-kontraktet (FE skickar tom lista) = Fas E. Noteras som pending för Klas. Domain-taket 400 räcker oavsett FE-val.
- Om ssyk-universum växer förbi 400 i framtida snapshot → taket följer med (kommenterad rationale).

**Klas-GO 2026-06-09:** (a) Variant C ✓, (b) Klas erbjöd 400 som alternativ → CTO valde 400 med motivering ✓.

---

## CTO-uppföljning — reverse-lookup-cap-multiplikator (architect-fynd)

`ResolveTaxonomyLabelsQueryValidator.cs:17`: `MaxConceptIdsPerCall = SearchCriteria.MaxConceptIds * 2` antar 2 dims (Ssyk+Region). C1 inför OccupationGroup + Municipality → upp till 4 dims. Med bas 400 underdimensionerat.

### Dom: **fix NU i C1, multiplikator ×2 → ×4 (=1600), behåll multiplikator-modellen** (ej fast tak)

- **Fix nu (ej defer Fas E):** samma cap-yta rörs av (b); `MaxConceptIdsPerCall` är deriverad konstant av MaxConceptIds-basen som ändras denna touch. §9.6 "annan fas" gäller saknad funktion-dependency, ej saknad trigger-väg — validatorn/konstanten existerar och ändras NU. Complete mediation (Saltzer/Schroeder): cappa inte baserat på vad nuvarande FE råkar skicka. Anti-pattern att skjuta ("vi måste ändå fixa").
- **Behåll multiplikator (ej fast 1600):** DRY/single-source — query-cap får aldrig divergera från domän-cap. Fast tal = magic number (§5.1) + drift-risk om universum/MaxConceptIds växer. Knowledge piece = `MaxConceptIds × antal dimensioner`.
- **×4 (ej ×3):** platt ConceptIds-lista cappar summan av alla dims. Gamla sparade sökningar bär legacy-Ssyk-ids i jsonb → måste label-resolvas i listvyn (annars "Okänd kod"-regression i orörd FE-väg). ×4 = OccupationGroup+Municipality+Region+legacy-Ssyk-bakåtkompat.
- **1600 säkert:** O(n) in-memory dict-lookup (ingen DB-hop/id), auth+rate-limited, per-element MaximumLength(32). Verklig DoS = obegränsad lista; 1600 = ändligt domän-härlett tak. FTS/ts_rank storleksordningar dyrare + oberoende.

### In-block-fix (C1)
`ResolveTaxonomyLabelsQueryValidator.cs:17` `*2`→`*4` + uppdatera kommentar rad 16 + XML-doc rad 8-11 (fyra dims). Felmeddelande auto-följer. Test: boundary 1600 pass / 1601 fail (referera konstanten, ej literal), behåll DEFEKT #2-regression (null→400 via Cascade.Stop). ADR 0043 implementerings-NOTAT (ej amendment): "multiplikator 2→4 (C1 2026-06-09) — speglar 4 dims efter expansion".

### Klas-STOPP: NEJ (mekanisk konkretisering av in-block-fix vars förutsättning (b)=Klas-GO redan givet; ADR 0043-mekaniken består).

## Referenser
Evans 2003 (kap. 2/5/14); Fowler 2018 (kap. 3, Move Function); Martin 2017 (kap. 7/8/10/13/22); Beck XP (YAGNI); Hunt/Thomas 1999 (DRY); ADR 0067 Beslut 1/2/7; ADR 0042 Beslut B; ADR 0043 + amendment; ADR 0045 Beslut 5–6; CLAUDE.md §9.6/§9.2.
