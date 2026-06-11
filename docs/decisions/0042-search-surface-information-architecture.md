# ADR 0042 — Sök-yta-informationsarkitektur: kollaps-filter, multi-värde-kriterier, typeahead och relevans-sort

**Datum:** 2026-05-16
**Status:** Accepted 2026-05-16 (Klas-GO STOPP 4) — *draft-flaggad: Accepted-flippen kräver Klas-GO på STOPP 4; ADR:n dokumenterar redan låsta beslut*
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0039 (SavedSearch-aggregat — **Beslut 3 partiellt superseras av denna ADR**; Beslut 1 delad `JobAdSearch` SPOT **hålls**), ADR 0040 (Smart CV-härlett filter — endast korsreferens, ingen design här), ADR 0032 (JobTech-integration — korpus/taxonomi-källan typeahead och filter speglar; senaste amendment 2026-05-16 hybrid), ADR 0008 (pipeline behavior order — validator-yta), ADR 0009 (ingen Repository — direkt `IAppDbContext`), BUILD.md §18 (Fas 2-milstolpe; **orörd — denna ADR är beslutskällan**), ADR 0049 (Accepted — TD-13 PII-fält-kryptering: Beslut 3:s `raw_payload`-exklusion bevarar generated columns/SPOT som denna ADR:s sök-yta konsumerar), CLAUDE.md §2.3 (CQRS), §5.3, §9.6 (in-block vs TD/fas-regeln), jobbpilot-design-principles regel 3/7 (civic-utility)

---

## Kontext

ADR 0039 låste `SavedSearch`-aggregatet och den delade `JobAdSearch`-modulen (Beslut 1: `ApplyCriteria`/`ApplySort` är ett SPOT-knowledge-piece som både `ListJobAdsQueryHandler` och `RunSavedSearchQueryHandler` återanvänder). Det som ADR 0039 inte täckte är **sök-ytans informationsarkitektur**: hur användaren faktiskt hittar och filtrerar annonser i UI:t, och vilken form filter-kriterierna måste ha för att stödja den ytan.

Fem icke-uppenbara, bestående designval uppstod i plan-design (senior-cto-advisor `a4318f13a645293cb` + dotnet-architect `a64f2ee9d89379046`, Klas §9.6 p.6-override 2026-05-16). De är **redan fattade** — denna ADR strukturerar låsta beslut, den introducerar inga nya multi-approach-val.

Krafter som spelar in:

- **Civic-utility-tonen** (jobbpilot-design-principles regel 3/7): JobbPilot speglar Platsbanken/1177, inte Linear/Vercel. En alltid-expanderad filterpanel signalerar "power-tool"; resultat-först med on-demand-filter signalerar "myndighetsverktyg".
- **`SearchCriteria` single-värde-form** (ADR 0039 Beslut 3): dagens `Ssyk`/`Region` är `string?`. En användare som vill bevaka "systemutvecklare ELLER frontendutvecklare i Stockholm ELLER Uppsala" kan inte uttrycka det. Multi-värde är ett genuint produktbehov i Fas 2, inte spekulativ generalisering (kontrast mot ADR 0040 där multi-occupation avvisades som forward-compat — där var slutformen okänd; här är behovet konkret och formen känd).
- **`SearchCriteria` är en `record` med värde-equality** som `SavedSearch` jsonb-dedupe vilar på. En naiv `string?` → `IReadOnlyList<string>` bryter record-collection-equality (referens-equality, inte strukturell) → identiska sparade sökningar deduperas inte längre i `saved_searches.criteria` jsonb.
- **JobTech-korpusen** (ADR 0032): `job_ads` är 5–15k rader i Fas 2. Typeahead och relevans-sort måste fungera på den volymen utan att introducera ett externt taxonomi-API-beroende på sök-vägen.
- **CV-matchning** ("bra match"/"bästa matchning") är ADR 0040 Fas 4+ — **hårt out**. Denna ADR korsrefererar bara; ingen design, ingen visuell placeholder.

## Beslut

### Beslut A — Kollaps-filteryta (Platsbanken-mönster)

Sök-ytan visar **annonser först**; filter exponeras on-demand via en disclosure-kontroll (expanderbar sektion), **inte** som alltid-expanderad filterpanel. Detta är civic-utility-styrt (jobbpilot-design-principles regel 3 — resultat är det primära; regel 7 — undvik power-tool-täthet). Frontend-leverans i **Batch 6**.

### Beslut B — `SearchCriteria` single → multi (SUPERSEDER ADR 0039 Beslut 3, delvis)

`SearchCriteria.Ssyk` och `.Region` ändras `string?` → `IReadOnlyList<string>`. `Q` och `SortBy` är **oförändrade**. ADR 0039 Beslut 3:s kärnresonemang — att `SortBy` ingår i VO:t som del av användarens avsikt — **hålls oförändrat**; endast Ssyk/Region single→multi-aspekten superseras.

Fyra DDD-invarianter som domänen MÅSTE upprätthålla (dotnet-architect-låst — implementeras i `SearchCriteria.Create` och speglas i `ListJobAdsQueryValidator`):

1. **Normalisering för record-equality.** Listorna normaliseras **sorterad + distinct** i `Create` innan de sätts. `record`-collection-equality är referens-baserad, inte strukturell — utan deterministisk normalisering (samma element i annan ordning, eller dubletter) bryts `SavedSearch` jsonb-dedupe (identiska sökningar deduperas inte). Sortering + distinct gör två logiskt lika kriterie-uppsättningar strukturellt lika.
2. **Maxantal-invariant (cap).** Nytt cap (riktvärde 10 per lista) — utan tak kan en `IN (...)`-blowup eller stuffad jsonb-rad ge query-DoS. Speglas i `ListJobAdsQueryValidator` (defense-in-depth, samma yta som dagens concept-id-regex).
3. **Tom-invariant.** Dagens regel "minst ett av Ssyk/Region/Q non-null" generaliseras till "minst en icke-tom lista **eller** `Q` non-null". Tomma listor behandlas som "inget filter" (analogt med dagens whitespace→null-normalisering).
4. **Gammal-rad jsonb-datakompat.** Befintliga single-värde-`saved_searches.criteria`-jsonb-rader måste fortfarande deserialiseras korrekt mot den nya list-formen (skalär → ett-element-lista, eller migrationssäker jsonb-läsning). Detta är en konkret bakåtkompat-yta, inte enbart en kodändring.

`ApplyCriteria`-signaturen i den delade `JobAdSearch`-modulen utvidgas från `(string? ssyk, string? region, string? q)` → list-form. ADR 0039 Beslut 1 (SPOT) hålls — `ListJobAdsQueryHandler` och `RunSavedSearchQueryHandler` fortsätter dela samma modul. Domän-implementation i **Batch 4** (Validator-yta samma batch).

### Beslut C — Typeahead = C1 (lokal `job_ads` ILIKE-prefix mot Title)

Sökfältets typeahead-förslag genereras från en **lokal query mot `job_ads.Title`** (ILIKE-prefix). C2 (anrop mot JobTech taxonomy-API per tangenttryck) är **avvisat** (CTO): externt API på den interaktiva sök-vägen ger latens- och resilience-yta som ADR 0032:s resilience-stack inte är dimensionerad för per-keystroke.

DoS-skydd (speglar dagens concept-id/`q`-yta-disciplin): rate-limit-policy på endpointen, minsta prefixlängd ≥ 2 tecken, `Take(n)`-cap, parametriserad EF-query (ingen string-concat). Ny `SuggestJobAdTermsQuery` levereras i **Batch 5**. Indexvalet (`text_pattern_ops` btree vs `pg_trgm` GIN) är ett **in-session CTO+architect-beslut i Batch 5** — avsiktligt **inte** låst här (det är en implementationsdetalj utan arkitekturell tvåvägs-låsning).

### Beslut D — Relevans-sort = D2 (ILIKE-heuristik)

`JobAdSortBy.Relevance` implementeras som en **ILIKE-baserad heuristik** (D2) på Fas 2-volymen. D1 (Postgres `tsvector`/full-text-search) är **avvisat som nuvarande lösning** — den är överdimensionerad för 5–15k rader och bär index-/migration-vikt utan motsvarande nytta nu. D1 dokumenteras som **framtida skala-trigger** (se Alternativ) — **inte** som TD: per CLAUDE.md §9.6/§9.7 är ett övervägt-men-ej-valt alternativ som adresseras vid skala-signal en "övervägt alternativ"-sektion, inte en dumpad tech-debt-post.

Ny invariant: `JobAdSortBy.Relevance` kräver `q` non-null (en relevans-ordning utan söktext är odefinierad — fail-fast i validator + `SearchCriteria.Create`). `ApplySort`-signaturen i `JobAdSearch` utvidgas: `(IQueryable<JobAd> source, JobAdSortBy sortBy, string? q)` — `q` behövs för relevans-rankningen. Leverans i **Batch 4**.

### Beslut E — "Ny"-badge via runtime-kontext

"Ny annons"-markeringen drivs av ett nytt `ListJobAdsQuery.Since` (`DateTimeOffset?`) + ett DTO-fält `IsNew` på `JobAdDto`. `Since` ingår **inte** i `SearchCriteria` — det är runtime-presentationskontext, analogt med `Page`/`PageSize` (samma resonemang som ADR 0039 Beslut 3 använder för att exkludera pagination ur VO:t: det är inte del av sökningens identitet). Leverans i **Batch 4**.

### Beslut F — CV-matchning hårt out (korsreferens)

"Bra match"/"bästa matchning" (CV-härledd relevans) är **inte** del av denna ADR. Det är ADR 0040 Fas 4+ (Smart CV-härlett filter). Denna ADR ger ingen design och **ingen visuell placeholder** för CV-matchning — endast denna korsreferens.

## Konsekvenser

**Positiva:**
- Sök-ytan följer civic-utility-tonen (resultat-först) → konsistent med Platsbanken/1177-referensen och jobbpilot-design-principles.
- Multi-värde-kriterier möter ett genuint Fas 2-produktbehov (OR-bevakning över yrken/regioner) utan att vänta på Fas 4.
- ADR 0039 Beslut 1 SPOT hålls intakt — `list` och `run` kan aldrig divergera trots utvidgad signatur.
- Typeahead och relevans-sort introducerar **inget** externt beroende på den interaktiva sök-vägen (ADR 0032:s resilience-stack belastas inte per-keystroke).
- D1 (tsvector) är dokumenterad som ren skala-trigger, inte skuld — ingen TD-bloat (CLAUDE.md §9.7).
- BUILD.md §18 förblir orörd; denna ADR är den auktoritativa beslutskällan för sök-ytan.

**Negativa + mitigering:**
- `SearchCriteria` single→multi bryter naiv record-equality. *Mitigering:* sorterad+distinct-normalisering i `Create` (Beslut B.1) — låst invariant, ej valfri.
- Multi-värde öppnar query-blowup-yta. *Mitigering:* maxantal-cap (Beslut B.2) speglad i validator.
- Befintliga single-värde-jsonb-rader i `saved_searches.criteria` riskerar deserialiseringsfel. *Mitigering:* explicit bakåtkompat-yta (Beslut B.4) — skalär→lista-läsning verifieras innan Batch 4-stängning.
- ILIKE-relevans (D2) är en heuristik, inte rankad full-text. *Mitigering:* medveten Fas 2-scope-gräns; D1-skala-trigger dokumenterad nedan.
- `ApplySort`/`ApplyCriteria`-signaturändring rör grön, integrationstestad kod. *Mitigering:* ADR 0039 Beslut 1:s behaviour-preserving-disciplin gäller; befintliga `ListJobAds`-tester är regressions-grind.
- `Since`/`IsNew` exponeras i API men är presentationskontext. *Mitigering:* dokumenterat här som medvetet analog till `Page`/`PageSize` (ej VO-fält).

## Alternativ som övervägdes

### Beslut A: alltid-expanderad filterpanel (avvisat)
**Emot:** signalerar power-tool-täthet (jobbpilot-design-principles regel 7); skjuter resultatet under fold. Civic-utility prioriterar resultat-först.

### Beslut B: behåll single-värde `string?` (avvisat)
**Emot:** kan inte uttrycka OR-bevakning över yrken/regioner — ett konkret Fas 2-produktbehov. Till skillnad från ADR 0040:s avvisade forward-compat-multi (slutform okänd, spekulativ) är behovet här konkret och formen känd; YAGNI gäller inte när behovet är aktuellt.

### Beslut C: C2 — JobTech taxonomy-API per keystroke (avvisat — CTO)
**Emot:** externt API på interaktiv sök-väg → latens + resilience-yta som ADR 0032:s stack inte är dimensionerad för per-keystroke; bryter civic-utility-responsivitetsförväntan. C1 (lokal `job_ads`-prefix) ger relevanta förslag från faktisk korpus utan extern hop.

### Beslut D: D1 — Postgres `tsvector` full-text-search (avvisat som nuvarande — framtida skala-trigger)
**Emot nu:** överdimensionerad för Fas 2-volym (5–15k rader); bär GIN-index- och migration-vikt utan motsvarande relevansvinst på den volymen. **Skala-trigger (ej TD):** när `job_ads`-volymen eller relevanskvalitets-signal motiverar det, omprövas D1 i en supersession-ADR. Per CLAUDE.md §9.6/§9.7 är detta en "övervägt alternativ"-post, inte en tech-debt-post — tech-debt-matrisen är inte en dumpningsplats för medvetet uppskjutna skala-beslut.

### Beslut B/forward-compat: spekulativ multi-form i fler fält (avvisat)
**Emot:** `Q`/`SortBy` har inget aktuellt multi-behov; att generalisera dem nu vore spekulativ generalisering (Beck/Fowler YAGNI) — samma resonemang som ADR 0040 använde mot forward-compat-multi-occupation. Multi tillämpas endast på Ssyk/Region där behovet är konkret.

### Beslut E: `Since` i `SearchCriteria` (avvisat)
**Emot:** "ny sedan"-fönstret är presentationskontext, inte del av sökningens identitet — två sparade sökningar med olika `Since` är inte två olika sökningar. Speglar ADR 0039 Beslut 3:s exklusion av `Page`/`PageSize`.

## Implementationsstatus

Ej påbörjad (LÅST session, Batch 2 — noll kod). Besluten är fattade (senior-cto-advisor `a4318f13a645293cb` + dotnet-architect `a64f2ee9d89379046` plan-design + Klas §9.6 p.6-override 2026-05-16). Leveranssekvens:

- **Batch 3:** Beslut B (domän `SearchCriteria` multi + fyra invarianter + `ListJobAdsQueryValidator`-yta; test-writer FÖRST/TDD; security-auditor BLOCKING för maxantal-cap; db-migration-writer om jsonb-shape ändras)
- **Batch 4:** Beslut E (`ListJobAdsQuery.Since` + DTO `IsNew`) sedan Beslut D (`ApplySort`-signatur + `Relevance`-invariant, D2 ILIKE)
- **Batch 5:** Beslut C (`SuggestJobAdTermsQuery` + DoS-skydd; index-val in-session CTO+architect; security-auditor BLOCKING)
- **Batch 6:** Beslut A (kollaps-filteryta frontend; design-reviewer VETO + Klas visuell verifiering)

Status-flip till Accepted bekräftas av Klas-GO på **STOPP 4** (ADR-accept = Klas-STOPP, samma konvention som ADR 0039 header-tabell).

## Krav på Klas-GO

| Punkt | Kräver Klas-GO? |
|---|---|
| Beslut A–F (dokumentation av låsta beslut) | Nej — redan fattade (CTO + architect + §9.6 p.6) |
| ADR 0042 Accepted-flip + ADR 0039 supersession-notat | **JA** — STOPP 4 (ADR-accept + partiell supersession) |
| Index-val Beslut C (btree vs GIN) | Nej här — in-session CTO+architect Batch 5 |

---

## Implementerings-notat 2026-05-17 — in-session-beslut Batch 4–5 (additivt, beslut-brödtext orörd)

**Datum:** 2026-05-17
**Källa:** in-session CTO/architect-beslut + Klas-bekräftelse 2026-05-17 (Fas 2-stängningssession). Notatet är additivt och dokumenterar de implementations-beslut som Beslut C/B/E explicit lämnade till in-session-avgörande — det **ändrar inte** Beslut A–F:s brödtext (ADR-immutabilitet; samma mönster som ADR 0032-amendments + ADR 0032-amendment 2026-05-16 hybrid).
**Beslutsfattare:** senior-cto-advisor + dotnet-architect (agentId-refererade nedan) + Klas Olsson (bekräftat 2026-05-17)
**Status:** Oförändrad **Accepted** — notatet dokumenterar implementation av redan låsta beslut, det fattar inga nya arkitekturval.

### Beslut C — index = senior-cto-advisor Variant A (btree functional partial-index)

Beslut C lämnade indexvalet (`text_pattern_ops` btree vs `pg_trgm` GIN) explicit till in-session CTO+architect-avgörande i Batch 5 (ADR-brödtext §47 + tabellrad "Index-val Beslut C"). Avgjort:

- **Valt (senior-cto-advisor agentId `afba3c7659c086817`, Variant A):** btree functional partial-index `lower(title) text_pattern_ops WHERE status = 'Active' AND deleted_at IS NULL`, levererat via migration `F2SuggestTitlePrefixIndex`. Left-anchored prefix-sök (Beslut C-scope) betjänas av `text_pattern_ops`-opclass; partial-predikatet håller indexet litet och linjerat mot den faktiska sök-ytan (aktiva, ej soft-deletade annonser).
- **Avvisat:** `pg_trgm` GIN. Kräver `CREATE EXTENSION pg_trgm` (DB-yta + migration-vikt), bär write-overhead som står i konflikt med den kontinuerliga stream-cron-skrivlasten (ADR 0032 sync-flöde), och dess infix-/similarity-styrka ligger **utanför** Beslut C:s left-anchored prefix-scope — spekulativ kapacitet (YAGNI, Beck/Fowler). **Ingen extension introduceras.**

### Beslut C — rate-limit: dedikerad `SuggestPolicy`

Beslut C:s DoS-skydd ("rate-limit-policy på endpointen", §47) konkretiseras: en **dedikerad `SuggestPolicy`** per-user FixedWindow 30 förfrågningar / 10 s, IOptions-bunden. Ingen återanvändning av en befintlig `ListRead`-policy — least-common-mechanism-disciplin (separat skyddsyta för en separat, mer keystroke-intensiv endpoint; egen tröskel kan justeras utan att röra list-ytan). security-auditor PASS bekräftade 30/10 s-tröskeln.

### Beslut C — typeahead frontend-datahämtning: self-contained debounce-hook (ej TanStack Query)

Frontend-datahämtningen för typeahead implementeras som en **self-contained debounce-hook** (debounce ≥ 300 ms, minsta prefix 2 tecken, `AbortController` för in-flight-avbryt) — **inte** via TanStack Query.

- **Beslutskälla:** senior-cto-advisor agentId `a377901ce353b58e7`. Rationale: YAGNI + CLAUDE.md §9.1/§9.2 — CLAUDE.md §4.3 reglerar TanStack Query för *mutations och pollar*, inte för en kortlivad keystroke-driven read-suggest. Att lägga query-cache-infrastruktur på en debouncad, abortbar förslags-yta är spekulativ generalisering utan aktuellt behov.

### Beslut B — jsonb-persistens = CTO Yta A3 (property-level HasConversion + tolerant converter)

Beslut B.4 (gammal-rad jsonb-datakompat, ADR-brödtext §39) konkretiseras: multi-värde-`SearchCriteria` persisteras via **property-level `HasConversion`** + en `SearchCriteriaJsonConverter` (System.Text.Json) i Infrastructure med **tolerant default-deny**-beteende.

- **Beslutskälla:** senior-cto-advisor agentId `a3f867af2b57df564`, Yta A3. `OwnsOne().ToJson()` + converter avvisades — web-verifierat instabilt på Npgsql (Npgsql issue #3129). Konvertern läser gamla skalär-formade rader **lazy on-read** (skalär → ett-element-lista) — ingen data-migration. Migration `F2SearchCriteriaMultiValue` är en tom no-op (ingen schema- eller datamigrering krävs; jsonb-kolumnen bär den nya formen utan DDL-ändring).

### Beslut E — since-fönster: fast rullande 7 dygn, serverstyrt

Beslut E ("Ny"-badge via `ListJobAdsQuery.Since`) konkretiseras: fönstret är **fast rullande 7 dygn, serverstyrt, ingen UI-kontroll**. Klas-bekräftat 2026-05-17 (civic-enkelhet — ingen användarinställning, ett förutsägbart serverstyrt fönster i linje med jobbpilot-design-principles regel 3/7).

### Amendment 2026-06-09 — Beslut B maxantal-cap (MaxConceptIds) 10 → 400 (per ADR 0067 Fas C1)

**Källa:** Platsbanken-sök-paritets-initiativet ([ADR 0067](./0067-platsbanken-search-parity.md)) Fas C1. Additivt amendment-notat — ADR-immutabilitet: Beslut A–F-brödtext ovan är orörd. Detta amendment justerar **endast invariant-värdet** i Beslut B.2 (maxantal-cap), inte mekaniken.

**Status:** ADR 0042 förblir **Accepted**. Beslut B.2:s maxantal-cap-mekanik (`SearchCriteria.MaxConceptIds` som single source, speglad i `ListJobAdsQueryValidator`) **består oförändrad**. Endast talet ändras.

**Ändring:** `SearchCriteria.MaxConceptIds` **10 → 400** (per dimension, enhetligt över alla dimensioner: OccupationGroup, Municipality, Region, Ssyk).

**Motiv (senior-cto-advisor decision-maker 2026-06-09 + Klas-GO):**
- **Verifierat behov:** 10 valdes 2026-05-16 *innan* Platsbanken-paritet var mål. Klas testade "Välj alla Data/IT-yrken" (70 st) 2026-06-08 → ValidationException → FE "tekniskt fel". Redan ~12 yrkesgrupper i Data/IT (efter yrke-nivåbytet, ADR 0067 Beslut 1) överskrider 10.
- **400 = ssyk-level-4-universumets storlek** (~400 yrkesgrupper) → "Välj alla yrkesgrupper" träffar aldrig taket. Invarianten speglar domänens verklighet (Evans 2003 kap. 5).
- **"Markera alla" = tom lista** (= inget filter = alla), inte ~400 materialiserade ids (FE-kontrakt, Fas E). YAGNI/KISS — "alla" och "inget filter" är samma resultatmängd.
- **DoS-disciplin bevarad:** IN(400) mot B-tree-indexerad STORED-kolumn trivialt; jsonb-dedupe ≤~15KB/sparad sökning (TOAST normalt, läses en-i-taget); inom ADR 0045 read-query 300ms p95. Ändligt tak består. Avvisat: 200 (godtyckligt, bryter UX för manuellt 250-val under universumstorleken, noll DoS-vinst); asymmetriskt per-dimension-tak (bryter enhetlighet utan bärande skäl).

Full dom: `docs/reviews/2026-06-09-sok-paritet-c1-cto.md`. Konsekvens: ADR 0043 reverse-lookup-cap-multiplikator följde med 2→4 (se ADR 0043 implementerings-notat 2026-06-09).

### Klargörande-not 2026-06-11 — Beslut B och region×kommun-semantiken (per ADR 0067 Fas E2b)

Beslut B:s multi-värde-mekanik (list-form + fyra invarianter) **beslutade aldrig kombinationssemantik mellan region- och kommun-dimensionerna** — kommun existerade inte i ADR 0042 (tillkom via ADR 0043-amendment 2026-06-08/ADR 0067). "AND-mellan-dimensioner / OR-inom-dimension"-invarianten (ADR 0067 Beslut 5) avser **ortogonala** dimensioner (yrke × ort); län ⊃ kommun är EN dimension (Ort) i två granulariteter. Fas E2b implementerar därför region∪kommun som inkluderande union i `JobAdSearchQuery.ApplyCriteria` (Platsbanken/JobTech-verifierad semantik) — det är **inte** ett undantag från Beslut B utan ligger utanför dess beslutsyta. Full dom: senior-cto-advisor 2026-06-11 (`docs/reviews/2026-06-11-sok-paritet-e2b-cto.md`) + ADR 0067 implementerings-notat 2026-06-11 (Fas E2b). Beslut A–F + invarianterna består oförändrade.

### Korsreferenser

- ADR 0032-amendment 2026-05-16 (snapshot-trunkerings-resiliens/hybrid) — stream-cron-skrivlasten som motiverar att `pg_trgm` GIN-write-overhead avvisas.
- ADR 0039 Beslut 3 — partiellt supersederad av ADR 0042 Beslut B (oförändrat av detta notat).
- **ADR 0043 (Taxonomi-ACL för sök-ytan, 2026-05-17, Proposed):** korsref-notat (additivt, Beslut A–F-brödtext orörd — ADR-immutabilitet). ADR 0043 utvidgar Beslut C:s *datakälla för inmatningsytan* (svenska namn-väljare matas av en lokal taxonomi-snapshot/ACL) och adresserar concept-id-jargong i UI:t. **Beslut B-domänkontraktet (`SearchCriteria.Ssyk/Region` = `IReadOnlyList<string>` concept-id) ändras EJ.** Beslut C:s typeahead-arkitektur (C1, `SuggestPolicy`, debounce-hook) är oförändrad. Rad 21-constraintet ("inget externt taxonomi-API på sök-vägen") är **uppfyllt, inte brutet** av ADR 0043 (lokal snapshot är per definition inte på sök-vägen). Se [`0043-taxonomy-acl-for-search-surface.md`](./0043-taxonomy-acl-for-search-surface.md).

---

*Referenser: Eric Evans, DDD (2003) kap. 5, 14; Vaughn Vernon, IDDD (2013) kap. 6; Robert C. Martin, Clean Architecture (2017) kap. 7; Beck/Fowler — YAGNI; Ford/Parsons/Kua, Building Evolutionary Architectures (2017); Nygard, Documenting Architecture Decisions (2011). ADR 0008, 0009, 0032, 0039, 0040; jobbpilot-design-principles regel 3/7; CLAUDE.md §2.3, §4.3, §5.3, §9.1, §9.2, §9.6, §9.7.*
