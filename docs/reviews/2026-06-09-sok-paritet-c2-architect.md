# dotnet-architect — Platsbanken sök-paritet Fas C2 (design-detalj)

**Datum:** 2026-06-09
**Agent:** dotnet-architect (advisor — design inom CTO:s sex låsta beslut (a)–(f), `docs/reviews/2026-06-09-sok-paritet-c2-cto.md`)
**Scope:** SearchCriteria-VO-expansion + reverse-lookup-migration + jsonb-bakåtkompat + RecentJobSearch-expansion + Ssyk-borttagning.

### Sammanfattning
Behöver åtgärdas i designen — 1 villkorat Blocker (RecentJobSearchDto-kontraktet mot FE), 4 Viktiga design-constraints (jsonb-predikat på nyckel-existens, COLLATE "C"-sortering, deploy-ordering för fail-loud-konvertern, DI/commit-samhörighet), 2 fas-fynd (§9.6). Alla CTO-beslut är implementerbara utan spänning mot Clean Arch — F5-kontraktsrisken löses med additiv DTO-form som inte bryter (e)/(f).

---

## F1 — SearchCriteria-VO-form

**Create-signatur och parameterordning:**

```csharp
public static Result<SearchCriteria> Create(
    IEnumerable<string>? occupationGroup,
    IEnumerable<string>? municipality,
    IEnumerable<string>? region,
    string? q,
    JobAdSortBy sortBy)
```

Motivering av ordningen: exakt paritet med `JobAdFilterCriteria` (OccupationGroup, Municipality, Region, Q) — SPOT-ordningen är redan etablerad i `ListJobAdsQuery`, validatorn och endpoint-parametrarna sedan C1. En enda kanonisk dimensionsordning genom hela kedjan (endpoint → query → VO → filter-SPOT → canonical-hash-JSON) eliminerar den positionella tyst-fel-fällan som C1-disciplinen flaggade. `SortBy` sist (icke-list-svans, samma position som idag). **Named arguments obligatoriskt vid alla fyra call-sites** (tre likatypade listor i rad) — converter, capture-behavior, Create/Update-handlers.

**Properties (namnen ÄR jsonb-nycklarna — PascalCase, converter-kontrakt):**

```csharp
public IReadOnlyList<string> OccupationGroup { get; private init; } = [];
public IReadOnlyList<string> Municipality { get; private init; } = [];
public IReadOnlyList<string> Region { get; private init; } = [];
public string? Q { get; private init; }
public JobAdSortBy SortBy { get; private init; }
```

`IReadOnlyList<string>` består (memory-regeln `feedback_ef_strongly_typed_vo_contains_translation` — ingen strongly-typed VO-lista; Npgsql `Contains`-translation).

**ADR 0042 Beslut B:s fyra invarianter per ny dimension:**

1. **Normalisering** — `NormalizeList` återbrukas oförändrad (trim, droppa tom, distinct ordinal, sort ordinal) för båda nya listorna.
2. **Cap** — `MaxConceptIds` (400) per lista. Samma delade konstant: 400 täcker ssyk-level-4-universumet och 290 kommuner med marginal; per-dimension-konstanter vore speculative generality.
3. **Tom-invariant** — generaliseras till alla tre listor:
   ```csharp
   if (normOccupationGroup.Length == 0 && normMunicipality.Length == 0
       && normRegion.Length == 0 && normQ is null)
       return Result.Failure<SearchCriteria>(DomainError.Validation(
           "SearchCriteria.Empty",
           "Minst ett sökkriterium (yrkesgrupp, kommun, region eller fritext) krävs."));
   ```
4. **jsonb-bakåtkompat** — Infrastructure-converter (F2); VO:t förblir serialiserings-fritt.

**Felkoder + svensk copy** (speglar exakt de meddelanden som redan finns i `ListJobAdsQueryValidator` sedan C1 — ingen ny copy uppfinns):

| Kod | Copy |
|---|---|
| `SearchCriteria.TooManyOccupationGroup` | `$"Max {MaxConceptIds} yrkesgrupper per sökning."` |
| `SearchCriteria.InvalidOccupationGroup` | `"Yrkesgrupp måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-)."` |
| `SearchCriteria.TooManyMunicipality` | `$"Max {MaxConceptIds} kommuner per sökning."` |
| `SearchCriteria.InvalidMunicipality` | `"Kommun måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-)."` |
| `SearchCriteria.TooManyRegion` / `InvalidRegion` | oförändrade |
| `SearchCriteria.TooManySsyk` / `InvalidSsyk` | UTGÅR med fältet |

Med tre identiska valideringsblock rekommenderas en privat helper (DRY utan att tappa per-dimension-koder):

```csharp
private static DomainError? ValidateConceptList(
    string[] values, string tooManyCode, string tooManyMessage,
    string invalidCode, string invalidMessage)
```

**Equals/GetHashCode** — utöka sekvensjämförelsen i kanonisk ordning (OccupationGroup, Municipality, Region). jsonb-dedupe-garantin (ADR 0039) vilar på detta — test-writer täcker likhet/olikhet per ny dimension FÖRST (CTO standing gate).

**XML-doc:** uppdatera dimensionslistan, ADR 0067-referens ((a)-scope: B2-dims-VO följer wiring-touchen), och lägg till en explicit mening: *"Property-namnen är jsonb-nyckel-kontraktet (PascalCase) — rename utan converter+migration bryter persisterad data."* `RecentJobSearch`-XML-docens stale `MaxConceptIds=10` rättas i samma touch (in-block, §9.6).

---

## F2 — SearchCriteriaJsonConverter

**Read-switch:** nya cases `"OccupationGroup"`/`"Municipality"` (ReadStringOrStringArray-återbruk), `"Region"`/`"Q"`/`"SortBy"` oförändrade, samt:

```csharp
case "Ssyk":
    // CTO-dom 2026-06-09 (f): fail-loud, ALDRIG tyst Skip(). Migrationen
    // + stängd skrivväg är garantin som gör fallet ≈ omöjligt.
    throw new JsonException(
        "Lagrad SearchCriteria-jsonb bär legacy-nyckeln \"Ssyk\" (occupation-name) "
        + "som skulle ha transformerats till \"OccupationGroup\" av C2-reverse-lookup-"
        + "migrationen. Raden är omigrerad — applicera migrationen i stället för att "
        + "tyst droppa sökningens yrke-dimension (fail-safe default, ADR 0067).");
```

**Write-block-ordning** — spegla VO-/Create-ordningen: `OccupationGroup`, `Municipality`, `Region`, `Q`, `SortBy`. `"Ssyk"` skrivs ALDRIG mer → skrivvägen producerar per konstruktion aldrig en rad som triggar fail-loud-casen.

**Bakåtkompat-invariant 4, verifierad mot radklasserna:**

- *Post-migration-rad*: `Municipality`-nyckel saknas → tom lista → `Create` passerar. OK.
- *Rad som var Ssyk-only:* migrationen garanterar `OccupationGroup` icke-tom (mappningen är total — fail-loud annars; 2179/2179 exakt 1 parent live-verifierat) → tom-invarianten kan **inte** brytas av transformen.
- *Omigrerad rad med "Ssyk"-nyckel* (även `"Ssyk":[]`!): fail-loud `JsonException`. **Kedjan dokumenteras i converter-XML-doc:** (1) migrationen transformerar/strippar nyckeln på ALLA rader (predikat = nyckel-existens, F3), abortar vid omappbart id; (2) skrivvägen stängs i samma batch; (3) `saved_searches` = 0 rader idag. Fail-loud är sista skyddsnätet, inte en förväntad väg.

---

## F3 — Reverse-lookup-migration (design-ram för db-migration-writer)

**Embedded resource:**

- Placering: `src/JobbPilot.Infrastructure/Persistence/Migrations/Resources/occupation-name-to-ssyk-level-4.v30.json`
- Format: `{ "taxonomyVersion": "30", "fetchedAt": "2026-06-09", "note": "FRUSEN migration-ägd artefakt — regenereras ALDRIG (CTO-dom (c) 2026-06-09).", "mappings": { "<occupation-name-id>": "<ssyk-level-4-id>", ... } }` (kompakt objekt-map, 2179 poster)
- csproj-wiring med explicit `LogicalName` (paritet med taxonomy-snapshot-posten)
- `Up()` läser resursen via `Assembly.GetManifestResourceStream` och genererar INSERT-batchar (~500 rader/VALUES-sats) — fungerar identiskt för `database update` och `migrations script` (psql-apply-praxisen).

**EN migration för hela batchen** (CCP — (c)+(d)+(e) är odelbara). Namn: `C2SearchParityReverseLookupAndRecentExpansion`. Ordning i `Up()`:

1. `DELETE FROM recent_job_searches;` — FÖRE DDL (annars failar NOT NULL-AddColumn på befintliga rader).
2. Recent-DDL (EF-scaffoldad ur modell-diffen).
3. Temp-mappningstabell + fail-loud-check + jsonb-transform.

**jsonb-transform-SQL-skiss:**

```sql
CREATE TEMP TABLE _occname_to_ssyk4 (
    occupation_name_id text PRIMARY KEY,
    ssyk4_id text NOT NULL);
-- INSERT-batchar genererade i Up() ur embedded resource

-- Fail-loud: omappbart id → ABORT (Saltzer/Schroeder fail-safe default)
DO $$
DECLARE bad record;
BEGIN
    SELECT s.id, e.elem INTO bad
    FROM saved_searches s
    CROSS JOIN LATERAL jsonb_array_elements_text(s.criteria->'Ssyk') AS e(elem)
    WHERE s.criteria ? 'Ssyk'
      AND NOT EXISTS (SELECT 1 FROM _occname_to_ssyk4 m
                      WHERE m.occupation_name_id = e.elem)
    LIMIT 1;
    IF FOUND THEN
        RAISE EXCEPTION
          'C2 reverse-lookup: saved_search % bär omappbart Ssyk-id "%". Migrationen abortar — komplettera mappnings-resursen, droppa inte tyst.',
          bad.id, bad.elem;
    END IF;
END $$;

-- Set-baserad transform: nyckel-EXISTENS-predikat (inkl. "Ssyk":[] —
-- Write skrev ALLTID nyckeln), sorterad+distinct i lagrad form.
UPDATE saved_searches s
SET criteria = (s.criteria - 'Ssyk')
    || jsonb_build_object('OccupationGroup', COALESCE(
        (SELECT to_jsonb(array_agg(DISTINCT m.ssyk4_id
                         ORDER BY m.ssyk4_id COLLATE "C"))
         FROM jsonb_array_elements_text(s.criteria->'Ssyk') AS e(elem)
         JOIN _occname_to_ssyk4 m ON m.occupation_name_id = e.elem),
        '[]'::jsonb))
WHERE s.criteria ? 'Ssyk';

DROP TABLE _occname_to_ssyk4;
```

Tre bindande detaljer:

- **Predikatet är `criteria ? 'Ssyk'` (nyckel-existens), inte icke-tom-array.** Befintlig `Write` emitterar alltid `"Ssyk"` — även `[]` på Region/Q-only-rader. Missas de raderna kastar fail-loud-konvertern på helt giltiga sökningar. Rader med `"Ssyk":[]` får `OccupationGroup:[]`.
- **`COLLATE "C"`** i `ORDER BY` — default-collation ordnar inte `[A-Za-z0-9_-]` garanterat ordinalt. CTO-constraint 2 kräver invariant 1 *i lagrad form*; `"C"` = byte-ordning = `StringComparer.Ordinal`-paritet.
- **Idempotens:** nyckeln tas bort i transformen → omkörning träffar 0 rader. occupation-name- och ssyk-level-4-id-universerna är disjunkta → dubbelmappning omöjlig by construction.

**recent_job_searches DDL — DROP+ADD, inte RENAME.** (i) raderna är raderade — rename bevarar ingenting; (ii) ETT ssyk_list-kolumn kan inte rename:as till TVÅ kolumner; (iii) EF scaffoldar exakt detta naturligt: `DropColumn("ssyk_list")` + `AddColumn("occupation_group_list")` + `AddColumn("municipality_list")` (text[], not null). RENAME skulle ljuga i migrations-historiken (kolumnen byter semantik, inte namn).

**`Down()`:** mekaniskt reversibel för DDL, **dokumenterat lossy för data**: raderade recent-rader är borta (cache-data, självåterbyggande per (d)); jsonb-transformen är irreversibel (grupp→yrken är 1-till-många). XML-doc motiverar med 0-rader-läget + ADR 0067 Beslut 7.

**Seeder-ordering-fällan — strukturellt eliminerad:** migrationen läser ENDAST embedded resource → temp-tabell. Rör inte `taxonomy_concepts`, läser inte levande `taxonomy-snapshot.json`. Replay på färsk DB deterministisk oavsett seeder-state.

---

## F4 — RecentJobSearch + FilterHashCalculator

**Entity:** `_occupationGroup` + `_municipality` backing-fields (List<string>, AsReadOnly-wrappers), `_region` oförändrad; Capture-projektion `AddRange(criteria.OccupationGroup/Municipality/Region)`. XML-doc invariant 1: "Q/OccupationGroup/Municipality/Region/SortBy är derivat av hash" + stale `MaxConceptIds=10` → 400.

**FilterHashCalculator — canonical-JSON (deterministisk fältordning, dokumenterad):**

```
{"q":string|null,"occupationGroup":[...],"municipality":[...],"region":[...],"sortBy":int}
```

Överlagringar:

```csharp
public static string Compute(SearchCriteria criteria)
    => Compute(criteria.Q, criteria.OccupationGroup, criteria.Municipality,
               criteria.Region, criteria.SortBy);

public static string Compute(
    string? q,
    IReadOnlyList<string> occupationGroup,
    IReadOnlyList<string> municipality,
    IReadOnlyList<string> region,
    JobAdSortBy sortBy)
```

`"ssyk"`-nyckeln utgår. Ingen hash-versionering (CTO (d): raderna raderas).

**EF-config:** ersätt `_ssyk`-blocket med två block — `"_occupationGroup"` → `occupation_group_list` och `"_municipality"` → `municipality_list` (text[], IsRequired, samma stringListComparer, PropertyAccessMode.Field); `Ignore(r => r.OccupationGroup)` + `.Ignore(r => r.Municipality)` ersätter `Ignore(r => r.Ssyk)`. Index/övriga kolumner orörda.

---

## F5 — KONTRAKTS-RISK (dom)

**Kartläggning av faktisk FE-konsumtion** (`web/jobbpilot-web`):

| Yta | Läser |
|---|---|
| `src/lib/dto/recent-searches.ts` (zod) | **`ssykList` är REQUIRED** (`z.array(z.string())`, ingen `.default([])`); `ssykLabels` har `.default([])`; `regionList`, `q`, `sortBy`, `label`, `currentCount`, `newCount`, `lastViewedAt`, `id` |
| `recent-search-row.tsx` + `recent-searches-hero-chip.tsx` | `q`, **`ssykList`**, `regionList`, `sortBy` → `buildJobbHref` (skriver `?ssyk=`-params); `label`, `currentCount`, `newCount`, `id` |
| `oversikt` (Sammanfattning, `includeCount=false`) | `label`, `lastViewedAt` |
| SavedSearch-API | **Konsumeras INTE** — verifierat: ingen `src/lib/api/saved-searches.ts` existerar; `src/lib/dto/saved-searches.ts` importeras endast för `SAVED_SEARCH_SORT_ORDER`-konstanten + egen testfil. ADR 0039-amendment 2026-05-20 bekräftad on-disk. |

**Dom:**

1. **RecentJobSearchDto kan INTE byta `Ssyk*` → `OccupationGroup*` rakt av.** zod-schemat failar parse när `ssykList` saknas → `/sokningar` + `/jobb`-hero-chip + `/oversikt`-Sammanfattningen går sönder. FE-ändring via bakdörren — förbjudet i C2.
2. **Minsta kontrakts-bevarande form = ADDITIV DTO:** behåll `SsykList` + `SsykLabels` som **deprecated, alltid-tomma** fält (matas med `[]`), addera `OccupationGroupList`, `MunicipalityList`, `OccupationGroupLabels`, `MunicipalityLabels`. zod stripper okända nycklar → nya fält osynliga för FE tills Fas E. `Label` server-härleds från nya labels → raden renderas korrekt även för OccupationGroup-captures.
3. **Att mata occupationGroup-ids IN i `ssykList`-fältet avvisas** (ubiquitous language ljuger; FE skulle skriva grupp-ids i `?ssyk=` som endpointen ignorerar — den tysta-degraderings-klass (f) förbjuder).
4. **Ingen funktionell spänning i fönstret C2→E:** `?ssyk=` är filter-no-op sedan C1 (Klas-GO:at). Efter C2 capture:as en `?ssyk=X`-sökning som Q/Region-only — *konsistent* med vad sökningen faktiskt gjorde. Tomma `ssykList` → `buildJobbHref` emitterar inga `?ssyk=`-params → "Kör igen" reproducerar samma resultat. OccupationGroup-bärande captures kan inte skapas via FE förrän Fas E — bara API-direkta anrop, och deras "Kör igen"-href tappar grupp-dimensionen; acceptabelt inom samma Klas-GO:ade fönster, men ska stå i PR-body.
5. **SavedSearchDto + Create/Update-commands kan renamsas fritt** (Ssyk→OccupationGroup, +Municipality) — ingen FE-konsument. En hypotetisk gammal klient som POST:ar `"ssyk"` får fältet tyst ignorerat (System.Text.Json default) → `SearchCriteria.Empty`-400 om inget annat kriterium — korrekt fail-säkert, ingen tyst halvspara.

Detta är ett **wire-kontrakt-shim, inte domän-skuld** — (e)/(f) listar query/interface/FilterCriteria/endpoint/commands/VO, inte read-DTO:n, och DTO-fälten är Fas E-bundna per definition. XML-doc-markera båda fälten: *"Deprecated — alltid tom sedan C2; tas bort i Fas E tillsammans med FE-zod-schemat."*

---

## F6 — Konsument-mappningar (exakta former)

**`JobAdFilterCriteria`** (−Ssyk, 3 listor + Q):

```csharp
public sealed record JobAdFilterCriteria(
    IReadOnlyList<string> OccupationGroup,
    IReadOnlyList<string> Municipality,
    IReadOnlyList<string> Region,
    string? Q);
```

XML-doc: C1-stycket om Ssyk-bevarande ersätts med C2-notat (fältet borttaget; occupation-name lever som synonym-substrat på q-vägen — `SsykConceptId`-kolumnen + `synonymExpander` i `JobAdSearchQuery` rörs INTE). Named-args-disciplinen består.

**`RunSavedSearchQueryHandler`:** `OccupationGroup: criteria.OccupationGroup, Municipality: criteria.Municipality, Region: criteria.Region, Q: criteria.Q` (täpper C1:s tomma listor).

**`ListRecentSearchesQueryHandler`:** `OccupationGroup: r.OccupationGroup, Municipality: r.Municipality, Region: r.Region, Q: r.Q`; `occupationGroupLabels`/`municipalityLabels` via `taxonomy.ResolveLabelsAsync`; `DeriveLabel(q, occupationGroupLabels, municipalityLabels, regionLabels)` fallback-ordning q → yrkesgrupp → kommun → region → "Alla annonser"; DTO per F5 (SsykList: [], SsykLabels: []).

**`ListJobAdsQueryHandler`:** raden `Ssyk: query.Ssyk ?? []` utgår.

**`ListJobAdsQuery`:** Ssyk-param bort (OccupationGroup/Municipality finns redan från C1 → ICapturesRecentSearch-shape matchas automatiskt). **Validator:** Ssyk-reglerna bort; övriga finns redan. **Endpoint:** `string[]? ssyk`-paramen bort; **in-block integrationstest: `GET /api/v1/job-ads?ssyk=X` → 200, samma resultat som utan param** (CTO (e)-krav).

**`ICapturesRecentSearch`:** `Q`, `OccupationGroup`, `Municipality`, `Region`, `SortBy`. Auth-invariant-doc orörd.

**`RecentJobSearchCaptureBehavior`** — guard räknar Q + OccupationGroup + Municipality + Region; `SearchCriteria.Create` med named args. Stänger även det LIVE capture-gapet från C1 (yrkesgrupp/kommun-sökningar capture:as inte idag).

**Create/UpdateSavedSearch:** commands + endpoint-body-records byter Ssyk → OccupationGroup + Municipality; validators byter Ssyk-blocken mot OccupationGroup+Municipality-block (samma cap/regex, copy per F1-tabellen). Handlers anropar nya Create-signaturen med named args.

**`ResolveTaxonomyLabelsQueryValidator`:** endast kommentar (×4 består; dims = OccupationGroup+Municipality+Region+headroom/B2 — legacy-Ssyk-formuleringen utgår).

---

## F7 — generate.mjs-utökning (dom: separat one-shot-script, INTE in i snapshoten)

**Separat script, inte en utökning av `generate.mjs`:s snapshot-skrivning.** `generate.mjs` är den *levande* snapshot-regeneratorn. CTO-constraint (c).1 är att migrations-resursen **aldrig regenereras** — att baka emitteringen in i den levande regenerator-vägen skapar exakt den tysta-omskrivnings-risk konstruktionen finns för att eliminera. Förslag:

- `tools/taxonomy-snapshot/generate-occupation-group-mapping.mjs` — one-shot, dokumenterad i header som "körd EN gång 2026-06-09; output är frusen migration-ägd artefakt — kör ALDRIG om mot samma fil".
- Återbruk: extrahera `gql` + `fetchChildren` (fail-loud ≠1 parent) till `tools/taxonomy-snapshot/lib.mjs` som båda scripten importerar (DRY på knowledge-piece-nivå). Tredje invocationen = `fetchChildren('occupation-name', 'ssyk-level-4')`.
- Output: resurs-filen i F3-formatet, sorterad på occupation-name-id (diff-stabil).

**Ska snapshoten OCKSÅ bära relationen? Nej — speculative generality.** Ingen konsument finns. Uppstår framtida behov regenereras snapshoten då till v31 — den är levande och får ändras; migrations-resursen är den frusna. Två artefakter med två livscykler är poängen med (c), inte duplikat-skuld.

---

## F8 — Filer som ändras (komplett, per lager)

**Domain:** `SavedSearches/SearchCriteria.cs` (F1), `RecentJobSearches/RecentJobSearch.cs` (F4), `RecentJobSearches/FilterHashCalculator.cs` (F4).

**Application:** `JobAds/Abstractions/JobAdFilterCriteria.cs` (−Ssyk, ctor-arity!), `JobAds/Queries/ListJobAds/{ListJobAdsQuery,ListJobAdsQueryHandler,ListJobAdsQueryValidator}.cs` (−Ssyk), `JobAds/Queries/GetTaxonomyTree/ResolveTaxonomyLabelsQueryValidator.cs` (endast kommentar), `RecentJobSearches/Common/ICapturesRecentSearch.cs`, `RecentJobSearches/Behaviors/RecentJobSearchCaptureBehavior.cs`, `RecentJobSearches/Queries/RecentJobSearchDto.cs` (additiv F5 + deprecated-doc), `RecentJobSearches/Queries/ListRecentSearches/ListRecentSearchesQueryHandler.cs`, `SavedSearches/Commands/CreateSavedSearch/*`, `SavedSearches/Commands/UpdateSavedSearch/*`, `SavedSearches/Queries/SavedSearchDto.cs` + `ListSavedSearches`/`GetSavedSearch`/`RunSavedSearch`-handlers.

**Infrastructure:** `Persistence/Configurations/SearchCriteriaConverters.cs` (F2), `Persistence/Configurations/RecentJobSearchConfiguration.cs` (F4), `Persistence/Migrations/<ts>_C2SearchParityReverseLookupAndRecentExpansion.cs` (+Designer +ModelSnapshot) (F3), `Persistence/Migrations/Resources/occupation-name-to-ssyk-level-4.v30.json` (NY, frusen), `JobbPilot.Infrastructure.csproj` (EmbeddedResource-post).

**Api:** `Endpoints/JobAdsEndpoints.cs` (−ssyk-param), `Endpoints/SavedSearchesEndpoints.cs` (body-records + mappning).

**Tooling:** `tools/taxonomy-snapshot/generate-occupation-group-mapping.mjs` (ny) + `lib.mjs`-extraktion (då även `generate.mjs` touch).

**Docs:** ADR 0067 implementerings-notat ((a)/(c)/(d)/(f)), ADR 0043-notat-kommentar (×4-omformulering).

**Tester (test-writer FÖRST per CTO standing gate):** SearchCriteriaTests, FilterHashCalculatorTests, RecentJobSearchTests, SearchCriteriaJsonConverter-tester (inkl. fail-loud-"Ssyk"-case + saknade-nycklar-case), capture-behavior-tester, alla fyra handler-testsviter, validator-tester, `?ssyk=`-ignorerings-integrationstest, migrations-integrationstest via **Testcontainers** (seed legacy-jsonb-rad inkl. `"Ssyk":[]`-variant → apply → läs genom konvertern; InMemory fångar varken jsonb-transformen eller text[]-mappningen).

**Arkitektur-test-påverkan:** ingen förväntad. Domain förblir BCL-only, inga nya paket, inga nya lager-kanter; embedded resource i Infrastructure. **Clean Arch-verifiering:** Domain (VO + hash-kontrakt, serialiserings-fritt), Application (DTO/portar, inga EF-typer), Infrastructure (converter + migration + resurs bakom EF), Api (endpoint→Mediator). OK.

---

### Fynd

**[Blocker — villkorat, löst av F5-designen]** `RecentJobSearchDto` + `web/jobbpilot-web/src/lib/dto/recent-searches.ts:38` — `ssykList` REQUIRED i FE-zod-schemat; rakt rename → parse-fel → `/sokningar` + `/jobb`-hero-chip + `/oversikt` går sönder. **Åtgärd:** Additiv DTO per F5; borttagning = Fas E.

**[Viktigt]** Migrations-predikatet måste vara **nyckel-existens** (`criteria ? 'Ssyk'`), inte icke-tom-array — `Write` emitterade alltid nyckeln, även `[]`. Testcontainers-fixture med `"Ssyk":[]`-rad.

**[Viktigt]** jsonb-transformens sortering kräver **`COLLATE "C"`** — annars bryts ADR 0042 invariant 1 i lagrad form.

**[Viktigt]** Deploy-ordering: migration FÖRE ny binär — fail-loud-konvertern i ny kod mot omigrerad DB → 500. Idag riskfritt (0 rader); dokumentera i migrations-XML-doc + PR-body. Hela C2 = en odelbar commit-batch (ctor-arity kompilator-binder allt; jfr `feedback_di_with_handlers_same_commit`).

**[Nice-to-have]** `tools/taxonomy-snapshot/` — extrahera `gql`/`fetchChildren` till delad `lib.mjs` (fail-loud-regeln i EN kopia).

**[Fas-fynd §9.6 — Fas E]** Borttagning av deprecated `SsykList`/`SsykLabels` ur RecentJobSearchDto + FE-zod-schema + `buildJobbHref`/api-klientens `?ssyk=` → `?occupationGroup=`-byte. Hör till FE-picker-fasen.

**[Fas-fynd §9.6 — Fas E]** `web/jobbpilot-web/src/lib/dto/saved-searches.ts:87` — `MAX_CONCEPT_IDS = 10` stale mot Domain-konstanten 400. Får inte röras i C2 (FE-förbud); synkas i Fas E.

### Referenser
- `docs/reviews/2026-06-09-sok-paritet-c2-cto.md` (sex låsta beslut) + `2026-06-09-sok-paritet-c1-{cto,architect}.md`
- CLAUDE.md §2.1/§2.2/§2.4/§3.6/§5.1/§9.6
- ADR 0067 Beslut 1/6/7; ADR 0042 Beslut B + amendment 2026-06-09; ADR 0043 + amendment; ADR 0039 Beslut 3 + amendment 2026-05-20 (FE konsumerar ej SavedSearch — verifierad on-disk); ADR 0060; ADR 0062
- Memory: `feedback_ef_strongly_typed_vo_contains_translation`, `feedback_di_with_handlers_same_commit`
- Evans 2003 kap. 2/5; Fowler 2018 (Speculative Generality); Fowler/Sadalage 2006 (migrations-immutabilitet); Saltzer/Schroeder 1975 (fail-loud)
