# CTO-dom — Platsbanken sök-paritet Fas C2 (VO-expansion + reverse-lookup + jsonb-bakåtkompat)

**Datum:** 2026-06-09
**Agent:** senior-cto-advisor (decision-maker)
**Scope:** ADR 0067 Beslut 1 (reverse-lookup) + Beslut 6 (VO-expansion) + Beslut 7 C2-raden. Sex multi-approach-beslut (a)–(f). CC gav ingen egen rekommendation (CLAUDE.md §9.6).

Verifierad on-disk-läsning 2026-06-09: ADR 0067 Beslut 1/6/7, ADR 0042 Beslut B + amendment 2026-06-09 (MaxConceptIds=400, Yta A3), ADR 0039 Beslut 3, ADR 0043 + amendment + implementerings-notat (×4-multiplikator), C1-domarna (cto + architect), `SearchCriteria` (Domain), `SearchCriteriaJsonConverter`/`SearchCriteriaConversion` (Infrastructure, statisk), `RecentJobSearch` + `FilterHashCalculator` (Domain), `ICapturesRecentSearch` + `RecentJobSearchCaptureBehavior` (Application), `tools/taxonomy-snapshot/generate.mjs` (`fetchChildren` fail-loud-mekanik). CC:s live-verifiering (2179 occupation-names, alla exakt 1 ssyk-level-4-parent) tas som given.

**Sammanhängande linje i domen:** (a)+(d)+(e)+(f) bildar en helhet — C2 levererar ett *rent sluttillstånd* där VO:t bara bär dimensioner som faktiskt filtrerar, occupation-name-dimensionen avvecklas helt ur sök-identiteten (substratet i `job_ads` bevaras per Beslut 1), och ingen permanent translation-logik lämnas kvar. Besluten är medvetet ömsesidigt beroende; jag redovisar dem separat men de ska implementeras som en batch.

---

## Beslut (a) — VO-expansion-scope: **ENDAST OccupationGroup + Municipality**

EmploymentType/WorktimeExtent läggs INTE i `SearchCriteria` i C2. De levereras som VO-fält i **samma touch som deras query-wiring** (post re-ingest, D1-grannskapet per C1-CTO-dom (c)).

### Motivering mot principer

- **"Tysta noll-träffar"-argumentet från Beslut 1 slår tillbaka mot fyra-fälts-läsningen.** Ett VO-fält utan `ApplyCriteria`-gren, utan `ListJobAdsQuery`-param och med 100%-NULL-kolumner (re-ingest ej körd) skulle — om command-params adderades — låta en användare spara en anställningsform-bevakning som *tyst filtrerar ingenting* när den körs. Det är exakt den klass av tyst degradering ADR 0067 Beslut 1 förklarade oacceptabel (CLAUDE.md §1). Utan command-params är fälten i stället permanent osättbara — död kod i ett Domain-VO.
- **Speculative Generality (Fowler 2018, kap. 3) + YAGNI (Beck).** Ingen konsument, ingen producent, ingen data. Samma dom som C1-CTO (c): B2-dims hör ihop med sin data-tillgänglighet (CCP — Martin 2017, kap. 13: things that change together belong together). VO-fältet, ApplyCriteria-grenen, query-paramen och re-ingest-verifieringen är EN sammanhållen leverans.
- **Testbart först (CLAUDE.md §2.4).** Beslut 6 kräver test-writer FÖRST. Ett VO-fält utan väg in eller ut kan bara testas tautologiskt (Create normaliserar listan) — ingen meningsfull invariant-verifiering mot verklig användning.
- **Konverter-designen gör senare tillägg trivialt.** `SearchCriteriaJsonConverter` läser saknat fält → tom lista. Beslut 6:s "samma jsonb-bakåtkompat-yta"-argument för samlokalisering är därmed svagt: kostnaden för att addera två fält senare är ~en switch-case + Write-block + tester, inte en ny bakåtkompat-händelse.

### Förhållande till ADR 0067 Beslut 6:s ordalydelse

Beslut 6 listar alla fyra som VO-dimensioner. Min dom **omprövar inte klassificeringen** (de ÄR VO-dimensioner per ADR 0039 Beslut 3-testet — del av sökningens identitet) — den sekvenserar *leveransen* efter data-tillgänglighet, exakt som C1-CTO (c) gjorde för query-wiringen med Klas-synlighet. Detta är sekvensering inom Accepted-beslut, inte reversering → **additivt implementerings-notat i ADR 0067** ("C2 levererar OccupationGroup+Municipality; EmploymentType/WorktimeExtent-VO-fält följer B2-wiring-touchen"), ingen amendment.

### Avvisade alternativ

**Alla fyra nu:** Speculative Generality; antingen tyst-noll-träff-bevakningar (med command-params) eller osättbara döda Domain-fält (utan); bryter "falsk klar"-disciplinen (ADR 0067 Beslut 2-analogt); otestbart meningsfullt.

### Klas-STOPP: **NEJ** (C1 (c)-precedens, §9.6-default, bygger mindre). Flaggas i PR-body + ADR-notat.

---

## Beslut (b) — Reverse-lookup-mappningskälla: **(a-källa) — taxonomins broader-relation via `generate.mjs`-tooling**

Mappningen occupation-name → ssyk-level-4 genereras från JobTech Taxonomy GraphQL `broader`-relationen med `fetchChildren`-mekaniken (fail-loud vid ≠1 parent), off-repo/off-runtime, och committas som **frusen artefakt**. (Var artefakten bor avgörs i (c).)

### Motivering mot principer

- **Matchar ADR 0067 Beslut 1:s ordalydelse exakt** ("via taxonomins `broader`-relation, deterministisk single-parent") — ingen amendment, ingen Klas-STOPP. Determinism-antagandet är live-verifierat (2179/2179 exakt 1 parent).
- **Committad artefakt = hermetisk + granskningsbar** (Winters/Manshreck/Wright 2020, kap. 18 — hermetic builds; samma dom som ADR 0043 Beslut A:s snapshot-disciplin). Mappningen är versionerad referensdata under git-granskning.
- **Befintligt mönster återanvänds** (CLAUDE.md §9.1): `fetchChildren(childType, parentType)` med fail-loud finns redan; tillägget är en tredje invocation (`occupation-name` → `ssyk-level-4`), ingen ny mekanik.

### Avvisade alternativ

**(b-källa) korpus-härledning ur `raw_payload`:** täcker bara yrken som råkar finnas i nuvarande ~44k-annonskorpus. En sparad sökning är en *framåtblickande bevakning* — dess occupation-name behöver inte ha en aktiv annons idag. Ofullständig mappning = tyst dataförlust för exakt de rader migrationen finns för att skydda. Dessutom indirekt källa (annons-payload) i stället för auktoritativ (taxonomin) — ACL-tänkets motsats (Evans 2003, kap. 14).

**(c-källa) live JobTech-query under migrationen:** extern hop i en migration = icke-hermetisk, icke-deterministisk (taxonomin kan ändras mellan körningar), icke-idempotent över tid, och fail-yta (nätverk) mitt i en schema-apply. Bryter ADR 0043:s hela disciplin ("Ingen runtime-extern-hop någonsin") och migrations-reproducerbarhet (Fowler/Sadalage, *Refactoring Databases* — migrationer är frusna, repeterbara steg).

### Klas-STOPP: **NEJ** (inom Beslut 1:s ordalydelse).

---

## Beslut (c) — Reverse-lookup-mekanism: **EF-data-migration (eager, set-baserad SQL) med FRUSEN migration-ägd mappnings-resurs; INTE lazy on-read; INTE runner-jobb**

Konkret konstruktion (detaljer till db-migration-writer, mina bindande constraints):

1. **Mappningen materialiseras som en frusen, migration-ägd embedded resource** (t.ex. `Migrations/Resources/occupation-name-to-ssyk4-snapshot-v30.json`, genererad av (b)-tooling, **regenereras aldrig**) — INTE en läsning av den levande `taxonomy-snapshot.json` och INTE av `taxonomy_concepts`-tabellen. Skälen: (i) seeder-ordering-fällan (seedern kör EFTER migrationer — tabellen kan inte litas på) elimineras strukturellt; (ii) migrations-immutabilitet — om migrationen läste den levande snapshotten skulle en framtida v31-regenerering tyst ändra vad migrationen gör vid replay på färsk DB (Fowler/Sadalage: en applicerad migration ändrar aldrig betydelse).
2. **Set-baserad transform:** temp-mappningstabell (INSERT-batchar ur resursen) + en UPDATE av `saved_searches.criteria` med jsonb-omskrivning: `Ssyk`-element som finns i mappningen ersätts av sina ssyk-level-4-parents, resultatet skrivs **sorterat + distinct** in i `OccupationGroup` (ADR 0042 Beslut B invariant 1 ska hålla även i lagrad form, inte bara via Create-on-read).
3. **Idempotens by construction:** mappningen är nycklad på occupation-name-ids; occupation-name- och ssyk-level-4-universerna är disjunkta concept-id-mängder → omkörning mappar ingenting nytt. Predikatet träffar bara rader med kvarvarande `Ssyk`-nyckel.
4. **Fail-loud-policy för omappbara ids:** om en rad bär ett `Ssyk`-id utanför mappningen → migrationen ABORTar med tydligt fel (Saltzer/Schroeder fail-safe default — samma filosofi som konverterns default-deny). Med 0 rader idag fyrar den aldrig; på färska DBs är den trivialt no-op. Tyst droppning som lämnar en rad i tom-invariant-brott är förbjuden.
5. **`Down()` = dokumenterat irreversibel** (lossy per (f)); motiveras i migrations-XML-doc med 0-rader-läget.

### Motivering mot principer

- **Lazy on-read avvisas på en konkret strukturell grund:** `SearchCriteriaConversion` är **statisk** (`static readonly ValueConverter`) utan DI. En runtime-translation kräver att en 2179-posts-mappning lazy-laddas statiskt i Infrastructure — en andra, parallell parser av snapshot-artefakten utanför `TaxonomyReadModel`/seedern (DRY-brott på knowledge-piece-nivå, Hunt/Thomas 1999) — och lägger permanent semantisk translation i en hot conversion-path som körs vid varje saved-search-läsning. F2-precedenten (`F2SearchCriteriaMultiValue` no-op + lazy converter) var **form-translation** (skalär→lista, identitetsbevarande, noll extern kunskap); detta är **semantisk nivå-translation som kräver en extern kunskapskälla** — inte samma klass. Att åberopa F2-precedenten här vore cargo-cult-mönsteråteranvändning.
- **Lazy lämnar dessutom persisterad data permanent i gammal form** → konverterns "Ssyk"-hantering, VO:ts Ssyk-fält och translation-logiken kan aldrig avvecklas — i direkt konflikt med (e)/(f) och med ADR 0067 Beslut 1:s uttryckliga val av *migration* över degradering.
- **Runner-jobb (B2-mönstret) avvisas:** `JobAdRefetchBackfillRunner` finns för ~44k-rader-refetch mot extern källa (volym + extern I/O motiverade Hangfire). Här: 0 rader, ingen extern I/O (frusen resurs), en engångs-transform. Hangfire-jobb + runner + trigger-yta för det = accidental complexity (KISS); fel verktyg för en deterministisk engångs-DDL/DML-händelse.
- **Eager-migration ger konvergerat sluttillstånd:** efter apply finns ingen legacy-form i DB → konvertern behöver ingen mappning, VO:t behöver inget Ssyk-fält, och (e)/(f) kan fullbordas. "Migration är kod" (Microsoft Learn — EF Core migrations; custom migration operations) — att läsa en embedded resource i `Up()` är etablerat och hermetiskt.

### C2→E-fönstret täpps i samma batch

Skrivvägen för occupation-name in i saved_searches **stängs samtidigt** ((e): `Create/UpdateSavedSearchCommand` droppar Ssyk-param) → inga nya legacy-rader kan uppstå efter migrationen. Utan den samtidiga stängningen vore eager-migration otät — det är därför (c) och (e) är en odelbar batch (CCP, Martin 2017 kap. 13).

### Klas-STOPP: **NEJ som designbeslut** (mekanism inom Beslut 1:s "reverse-lookup-migration"-ordalydelse). **Operativ standing-STOPP består:** CC applicerar migrationen via psql först efter Klas-GO (etablerad C2-praxis) — Klas ser migrations-SQL:en där.

---

## Beslut (d) — RecentJobSearch-expansion: **I C2 — det är inte ens ett val; plus: recent-raderna NOLLSTÄLLS i migrationen i stället för hash-akrobatik**

### Motivering mot principer

- **"Separat touch" är en illusion.** `FilterHashCalculator.Compute(criteria)` läser `criteria.Ssyk`; `RecentJobSearch`-ctorn projicerar `criteria.Ssyk`; `RecentJobSearchCaptureBehavior` anropar `SearchCriteria.Create(capt.Ssyk, ...)`. VO-ändringen i (a)/(f) **kompilator-bryter hela recent-kedjan** — C1-architecten listade redan "RecentJobSearch-entity + FilterHashCalculator" under "Inte rört i C1" *eftersom de hör till C2*. CCP (Martin 2017, kap. 13): klasser som ändras av samma skäl ändras tillsammans.
- **Funktionellt gap är LIVE sedan C1, inte framtida:** `ListJobAdsQuery` bär OccupationGroup/Municipality men `ICapturesRecentSearch` ser dem inte → en yrkesgrupp/kommun-sökning capture:as inte (default-browse-guarden räknar bara Q/Ssyk/Region). Det är en levande inkonsistens i en shippad feature → §9.6-default = fixa in-block, inte TD.
- **Hash-kompat-frågan löses genom att inte lösas:** jag dömer att C2-migrationen **raderar samtliga `recent_job_searches`-rader** (3 st dev; varav 1 med rå SSYK-kod `{5132}` som ändå är omappbar). Grund: entiteten deklarerar själv sin semantik — auto-capture-rader "har ingen audit-trail-värdighet", hard-delete är etablerat mönster, cap-20-eviction gör datat självåterbyggande cache-data. Att bygga versionerad hash eller per-rad-SHA-256-omräkning (kräver pgcrypto eller C#-loop i migration) för att bevara 3 efemära dev-rader är over-engineering i ren form (YAGNI, Beck; KISS). Efter nollställning utökas canonical-JSON fritt.

### In-block-yta (C2)

- `RecentJobSearch`: `_ssyk` ersätts av `_occupationGroup` + `_municipality` (text[]-kolumner), Capture-projektion uppdaterad; schema-migration för kolumnbytet + radering av befintliga rader.
- `FilterHashCalculator`: ny canonical-JSON `{"q","occupationGroup":[],"municipality":[],"region":[],"sortBy"}` (deterministisk fältordning, dokumenterad; "ssyk"-nyckeln utgår). XML-doc uppdaterad.
- `ICapturesRecentSearch`: −Ssyk, +OccupationGroup, +Municipality. Auth-invariant-doc orörd.
- `RecentJobSearchCaptureBehavior`: default-browse-guard räknar Q + OccupationGroup + Municipality + Region; `SearchCriteria.Create`-anropet följer nya signaturen.
- `RecentJobSearchConfiguration` + `ListRecentSearchesQueryHandler` (mappar nya fält in i `JobAdFilterCriteria` — täpper C1:s tomma listor) + DTO/labels (`labelByConceptId` täcker redan alla Kinds per C1-architect F3).

### Klas-STOPP: **NEJ** (kompilator-tvingad samhörighet + live-gap; radering av dev-rader syns i migrations-SQL:en vid psql-apply-GO:n).

---

## Beslut (e) — `ListJobAdsQuery.Ssyk` + `ICapturesRecentSearch.Ssyk` no-op-borttagning: **TA BORT NU (C2), inklusive `JobAdFilterCriteria.Ssyk` och endpoint-paramen**

### Motivering mot principer

- **C1-architecten har redan fas-bestämt detta:** "full borttagning = C2 (VO-expansion)" (§9.6 fas-fynd, `2026-06-09-sok-paritet-c1-architect.md` rad 40/114). C2 är nu. Att skjuta vidare = exakt det TD-bloat-mönster §9.6 förbjuder ("vi måste ändå fixa det").
- **Beroendena som motiverade behåll-i-C1 upplöses i denna batch:** efter (c)-migrationen finns ingen persisterad `SearchCriteria.Ssyk` att mata `RunSavedSearchQueryHandler`; efter (d) finns ingen `RecentJobSearch.Ssyk`-kolumn. De två persistens-bundna konsumenterna som tvingade fältets överlevnad är borta → fältet är en lögn i kontraktet (ubiquitous language, Evans 2003 kap. 2: ett fält som heter Ssyk och gör ingenting).
- **FE-fönstret är ofarligt och redan Klas-accepterat:** FE skickar `?ssyk=` tills Fas E. Minimal-API-binding ignorerar query-parametrar som inte binds → 200 OK, ingen 400. Funktionellt identiskt med C1:s no-op (Klas-GO:ade fönstret 2026-06-09); skillnaden är att kontraktet slutar låtsas. **In-block-verifiering:** integrationstest som bevisar att `?ssyk=X` ignoreras utan fel.
- `Create/UpdateSavedSearchCommand` droppar Ssyk-param (System.Text.Json ignorerar okända JSON-props default) → skrivvägen stängd, (c)-migrationens tätningskrav uppfyllt.
- `JobAdFilterCriteria` tappar Ssyk-listan (equality-grenen togs redan i C1) → ctor-arity-ändring → alla tre konsumenter + tester i samma commit, **named arguments** (C1-architect-disciplin).
- **×4-multiplikatorn i `ResolveTaxonomyLabelsQueryValidator`:** legacy-Ssyk-bakåtkompat-skälet (fjärde dimensionen) försvinner med migrationen, men B2-dims tillkommer senare som resolverbara dimensioner → **behåll ×4, uppdatera endast kommentaren** (dims = OccupationGroup+Municipality+Region+headroom/B2). Churn 4→3→5 vore poänglös; capen är ett tak, inte en exakt summa.

### Avvisade alternativ

**Behåll no-op till Fas E:** fältet skulle överleva sin sista konsument enbart som dokumentations-skuld; varje framtida läsare måste återupptäcka att det är dött (Fowler 2018 — Remove Dead Code är en refactoring, inte en lyx).

### Klas-STOPP: **NEJ** (fortsättning av redan Klas-GO:at C1-fönster; ingen funktionell förändring).

---

## Beslut (f) — Occupation-name-bevarande: **ERSÄTT — original-Ssyk transformeras till OccupationGroup och Ssyk-fältet utgår ur VO:t**

### Motivering mot principer

- **VO-fält ska vara aktiva sök-dimensioner.** Efter nivåbytet driver `SearchCriteria.Ssyk` ingenting: equality-grenen är borta (C1), synonym/recall-vägen drivs av `Q` (inte av Ssyk-listan), och occupation-name-*substratet* som Beslut 1 bevarar är `job_ads.ssyk_concept_id`-kolumnen + synonym-expandern — **inte** VO-fältet. Ett bevarat VO-Ssyk vore en permanent död dimension i sökningens identitet (Evans 2003, kap. 5: ett VO modellerar ett konceptuellt helt; kap. 2: ubiquitous language ljuger inte).
- **Audit/rollback-värdet är noll i sak:** `saved_searches` = 0 rader; ingen prod existerar (ADR 0066). Det enda "original" som kunde gå förlorat finns inte. Mappningen själv är dessutom committad ((b)/(c)) — *kunskapen* occupation-name→grupp är granskningsbar i git för all framtid, även om per-rad-originalen inte bevaras.
- **Behåll-varianten skapar följdskuld:** VO-Equals/GetHashCode, converter, dedupe-semantik och validators skulle alla behöva bära ett fält vars enda funktion är arkeologi — och en andra migration krävs ändå den dag det städas. Dubbel kostnad för noll nytta (YAGNI; Fowler 2018 — Speculative Generality igen, fast i data-form).
- **Konverter-policy för kvarvarande legacy-"Ssyk"-nyckel:** explicit case → **fail-loud `JsonException`** ("legacy Ssyk-form ej migrerad"), inte tyst `Skip()`. Tyst droppning kunde lämna en rad i tom-invariant-brott eller tyst amputera en bevaknings yrke-dimension — fail-safe default (Saltzer/Schroeder 1975), konsekvent med konverterns befintliga default-deny-filosofi. Migrationen + stängd skrivväg är garantin som gör fallet ≈ omöjligt; om det ändå inträffar ska det synas, inte döljas.

### Avvisade alternativ

**Behåll original-Ssyk parallellt:** död dimension i VO + dubbel dedupe-/equality-yta + garanterad framtida städmigration; bevarar "original" som inte existerar (0 rader). Avvisas.

### Klas-STOPP: **NEJ utöver standing C2-GO** (ADR 0067 Beslut 7 C2-raden "JA (sparad-sökning-migration)" är given; lossy-`Down()` + 0-rader-motiveringen syns i migrations-XML-doc och i psql-apply-STOPP:en).

---

## Sammanfattning

| Beslut | Dom | Klas-STOPP? |
|---|---|---|
| (a) VO-scope | OccupationGroup + Municipality ENDAST; B2-dims-VO följer wiring-touchen | NEJ (PR-flagga + ADR 0067-notat) |
| (b) Mappningskälla | Taxonomins broader-relation via generate.mjs-tooling, frusen committad artefakt | NEJ (inom Beslut 1-ordalydelsen) |
| (c) Mekanism | Eager EF-data-migration, frusen migration-ägd resurs, set-baserad SQL, idempotent, fail-loud vid omappbart | NEJ design; standing psql-apply-GO |
| (d) Recent-expansion | I C2 (kompilator-tvingat + live-gap); recent-rader RADERAS i migrationen (ingen hash-versionering) | NEJ (radering syns vid psql-GO) |
| (e) Ssyk-no-op-borttagning | Ta bort nu — query, interface, FilterCriteria, endpoint-param, commands | NEJ |
| (f) Occupation-name i VO | Ersätt; Ssyk utgår ur VO; konverter fail-loud på legacy-nyckel | NEJ (täcks av standing C2-GO) |

**Inga nya blockerande Klas-STOPP.** Standing gates som består: psql-apply av migrationen efter Klas-GO (där Klas ser reverse-lookup-SQL:en, recent-raderingen och lossy-Down-dokumentationen) + ADR 0067 Beslut 6-disciplinen: **test-writer FÖRST, security-auditor BLOCKING för cap-ytan på de nya dimensionerna**, db-migration-writer för migrationen.

### In-block-yta C2 (konsoliderad)

- **Domain:** `SearchCriteria` (−Ssyk, +OccupationGroup, +Municipality; Create-signatur, cap/regex/felkoder per dimension, tom-invariant-text, Equals/GetHashCode, XML-doc), `FilterHashCalculator` (ny canonical-JSON), `RecentJobSearch` (kolumn-/projektion-byte).
- **Application:** `ICapturesRecentSearch`, `RecentJobSearchCaptureBehavior` (guard), `JobAdFilterCriteria` (−Ssyk, +mappning i `RunSavedSearchQueryHandler` + `ListRecentSearchesQueryHandler` — täpper C1:s tomma listor), `Create/UpdateSavedSearchCommand` + validators, `ListJobAdsQuery` (−Ssyk), `ResolveTaxonomyLabelsQueryValidator` (endast kommentar).
- **Infrastructure:** `SearchCriteriaJsonConverter` (nya nycklar + fail-loud legacy-"Ssyk"), `RecentJobSearchConfiguration`, reverse-lookup-migration + frusen mappnings-resurs, recent-schema-migration.
- **Api:** `JobAdsEndpoints` (−ssyk-param) + integrationstest att `?ssyk=` ignoreras.
- **Tooling:** `generate.mjs`-utökning som emitterar den frusna mappnings-resursen.
- **Docs:** ADR 0067 additivt implementerings-notat ((a)-sekvensering + (c)/(d)/(f)-mekanik), ADR 0043-notat-kommentar (×4-dims-omformulering).

### Referenser

- Eric Evans, *Domain-Driven Design* (2003) — kap. 2 (ubiquitous language), 5 (Value Objects), 14 (ACL) — (a)/(b)/(e)/(f)
- Robert C. Martin, *Clean Architecture* (2017) — kap. 13 (CCP/component cohesion), 22 (lager) — (a)/(c)/(d)
- Martin Fowler, *Refactoring* 2nd ed (2018) — kap. 3 (Speculative Generality, Dead Code) — (a)/(e)/(f)
- Fowler/Sadalage, *Refactoring Databases* (2006) — migrations-immutabilitet/repeterbarhet — (b)/(c)
- Kent Beck (XP) — YAGNI — (a)/(d)/(f)
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — DRY som knowledge-piece — (c)/(e)
- Winters/Manshreck/Wright, *Software Engineering at Google* (2020) — kap. 18 (hermetic builds) — (b)/(c)
- Saltzer/Schroeder (1975) — fail-safe defaults — (c)/(f)
- Microsoft Learn — EF Core Migrations (custom operations/data i migrations) — (c)
- ADR 0067 Beslut 1/2/6/7; ADR 0042 Beslut B + amendment 2026-06-09; ADR 0043 + amendment + notat; ADR 0039 Beslut 3; ADR 0060; ADR 0066; CLAUDE.md §1/§2.4/§9.6; C1-domar `docs/reviews/2026-06-09-sok-paritet-c1-{cto,architect}.md`; memory `feedback_ef_strongly_typed_vo_contains_translation`, `feedback_td_lifting_discipline`

---

**CC-not (discovery-evidens för (b)-determinism):** live-verifiering 2026-06-09 mot `https://taxonomy.api.jobtechdev.se/v1/taxonomy/graphql` med query `{ concepts(type:"occupation-name") { id broader(type:"ssyk-level-4") { id } } }` → 2179 occupation-names; 0 med 0 parents; 2179 med exakt 1 parent; 0 med >1 parent. Dev-DB-inventering samma datum: `saved_searches` 0 rader; `recent_job_searches` 3 rader varav 1 med `ssyk_list={5132}` (rå SSYK-kod, ej concept-id — omappbar, raderas per (d)).
