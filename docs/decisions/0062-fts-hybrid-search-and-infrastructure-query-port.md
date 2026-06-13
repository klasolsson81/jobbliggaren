# ADR 0062 — FTS-hybrid-sök + Infrastructure-query-port för sök-kompositionen

**Datum:** 2026-05-21
**Status:** Accepted
**Kontext:** JobbPilot F6 Prompt 4 FTS-skifte (2026-05-21). ADR 0061:s GIN trigram-index löste q-search för specifika söktermer (`systemutvecklare` 40s → 1.6s) men en hel klass av korta vanliga svenska termer förblev budget-brytande (`lärare` 18.7s). EXPLAIN ANALYZE 2026-05-21 bevisade att roten är fundamental trigram-selektivitet, inte index- eller `work_mem`-konfiguration. Frågan: vilken sök-strategi avlöser trigram för q-search, och i vilket lager bor sök-kompositionen när den nya strategin kräver provider-specifik LINQ?
**Beslutsfattare:** Klas Olsson (produktägare; explicit Accepted-direktiv i F6 P4 FTS-skifte-startprompten 2026-05-21 — Klas granskar prosan post-hoc)
**Relaterad:** [ADR 0001](./0001-clean-architecture.md) (Dependency Rule), [ADR 0039](./0039-savedsearch-aggregate-and-query-run-semantics.md) Beslut 1 (`JobAdSearch` SPOT — amendas, se Leverans 3/amendment 2026-05-21), [ADR 0042](./0042-search-surface-information-architecture.md) Beslut D2 (ILIKE-3-2-1-relevans — ersätts av `ts_rank`), [ADR 0042](./0042-search-surface-information-architecture.md) Beslut E (`IsNew`/`Since`), [ADR 0043](./0043-taxonomy-acl-for-search-surface.md) (`ITaxonomyReadModel`-port-precedens), [ADR 0045](./0045-performance-budget-and-fitness-functions.md) (perf-budgetar + fitness functions), [ADR 0048](./0048-cross-aggregate-read-join-in-application-query-path.md) (cross-aggregat join vs port — ADR 0062 Beslut 4 är ett komplementärt tredje ben, EJ supersession), [ADR 0050](./0050-deployment-migration-aws-exit-hetzner.md) (Hetzner CX32-constraint — Elasticsearch utesluten), [ADR 0060](./0060-recent-job-searches-auto-capture.md) Beslut 4 (RecentJobSearches N+1 — `CountAsync`-konsument), [ADR 0061](./0061-job-ad-search-perf-strategy.md) (GIN trigram-strategi — amendas, se Leverans 2/amendment 2026-05-21).

> **Livscykel-/proveniens-not:** Skriven 2026-05-21 av Claude Code (adr-keeper-disciplin) på explicit Klas-direktiv i F6 P4 FTS-skifte-startprompten — medveten override av CLAUDE.md §9.4 webb-Claude-verbatim-konventionen (memory `feedback_klas_can_override_adr_verbatim_source`). Besluts-substansen är grundad i agent-domar 2026-05-21: senior-cto-advisor (3 ronder — FTS-hybrid + Variant b Infrastructure-query-port; samt Variant B port-kontrakt — två metoder, splittad filter-record), dotnet-architect-rond (port-design + verifiering av att FTS-LINQ fysiskt ligger i `Npgsql.EntityFrameworkCore.PostgreSQL` + test-topologi), code-reviewer (0 Block / 2 Major / 4 Minor) och security-auditor (0 Block / 0 Critical / 0 High / 0 Medium / 1 Low). Status **Accepted** per Klas explicit-direktiv; Klas granskar prosan post-hoc. Samma proveniens-mönster som [ADR 0061](./0061-job-ad-search-perf-strategy.md) rad 9.

---

## Kontext

[ADR 0061](./0061-job-ad-search-perf-strategy.md) introducerade GIN trigram-index (`ix_job_ads_title_lower_trgm`, `ix_job_ads_description_lower_trgm`) som svar på att `GET /api/v1/job-ads?q=systemutvecklare` tog 40s p95 mot dev (~51k job_ads-rader). Trigram löste den mätta termen — `systemutvecklare` föll 40s → 1.6s. Men en hel klass av q-termer förblev budget-brytande efter trigram-deploy: `lärare` mätte 18.7s, väl över ADR 0045:s < 2s p95-budget.

`EXPLAIN ANALYZE` 2026-05-21 bevisade att detta inte är en index- eller konfigurationsbrist utan **fundamental trigram-selektivitet för korta vanliga svenska termer**:

- `lärare` COUNT-vägen: Bitmap Heap Scan 4881ms.
- `Heap Blocks: exact=4635, lossy=0` — `lossy=0` utesluter `work_mem`-hypotesen (bitmappen rymdes i minnet; ingen lossy-degradering skedde).
- `ix_job_ads_description_lower_trgm` returnerade 12 980 kandidat-rader varav 7 581 falska positiva.
- Kostnaden = de-TOAST + LIKE-recheck av ~13k stora `description`-texter.

Rotorsaken: korta, vanliga svenska trigram har låg selektivitet. Trigrammet för `are`-ändelsen återkommer i `lärare`, `snickare`, `bagare`, `ingenjör`-närliggande former — trigram-indexet pekar ut tiotusentals kandidatrader som sedan måste de-TOAST:as och LIKE-rechecka:s mot den stora `description`-kolumnen. Trigram fungerar utmärkt för långa specifika termer (`systemutvecklare`) och fungerar dåligt för korta vanliga termer (`lärare`). Det är en egenskap hos trigram-modellen, inte en bugg i ADR 0061:s implementation.

Den underliggande frågan: vilken sök-strategi avlöser trigram för q-search, och — eftersom den nya strategin (PostgreSQL FTS) kräver provider-specifik LINQ — i vilket lager ska sök-kompositionen bo utan att bryta Clean Arch-gränsen som ADR 0001 etablerade?

## Beslut

### Beslut 1 — PostgreSQL FTS-hybrid avlöser trigram för q-search

Migration `F6P4FtsSearchVector` (`20260521090234`) introducerar en STORED generated column och ett dedikerat GIN-index:

```sql
ALTER TABLE job_ads
  ADD COLUMN search_vector tsvector
  GENERATED ALWAYS AS (
    to_tsvector('swedish',
      coalesce(title, '') || ' ' || coalesce(description, ''))
  ) STORED;

CREATE INDEX ix_job_ads_search_vector
  ON job_ads USING gin (search_vector)
  WHERE deleted_at IS NULL;
```

q-sökningen blir en **hybrid** av två grenar:

```
search_vector @@ websearch_to_tsquery('swedish', q)
  OR lower(title) LIKE '%q%'
```

- **FTS-grenen** är den snabba primärvägen: GIN-tsvector-scan med svensk stemming. `lärare`/`läraren`/`lärarens` reduceras till samma lexem → en kort vanlig term blir en exakt lexem-matchning i stället för en låg-selektiv trigram-spridning.
- **title-LIKE-grenen** är en billig mitt-i-ord-substring-fallback: titlar är korta, ingen TOAST-kostnad, och grenen träffar `ix_job_ads_title_lower_trgm` från ADR 0061.
- **description-LIKE körs ALDRIG i q-grenen** — det var perf-rotorsaken (de-TOAST + LIKE-recheck av ~13k stora description-texter). Full description-*ord* matchas ändå via FTS eftersom `search_vector` spänner både `title` och `description`.

`websearch_to_tsquery` valdes för robusthet mot användar-input — den kastar aldrig på dålig syntax (till skillnad från `to_tsquery`), vilket gör den säker att exponera direkt mot q-parametern.

`JobAdSortBy.Relevance` blir:

```sql
ORDER BY ts_rank(search_vector, websearch_to_tsquery('swedish', q)) DESC,
         published_at DESC,
         id
```

Detta **ersätter** [ADR 0042](./0042-search-surface-information-architecture.md) Beslut D2:s ILIKE-3-2-1-heuristik — `ts_rank` är en riktig relevansrankning över FTS-lexem i stället för en handviktad ILIKE-poäng.

**Avvisade alternativ** (se även Alternativ-sektionen nedan): Elasticsearch (managed $25–130/mån är opraktiskt dyrt för en så liten korpus; self-hosted kräver ~8GB RAM och ryms inte på Hetzner CX32 per [ADR 0050](./0050-deployment-migration-aws-exit-hetzner.md) — Klas-beslut: stanna i PostgreSQL); FTS-only utan trigram (saknar mitt-i-ord-substring-semantik som UI-kontraktet förutsätter); acceptera trigram-svagheten (ADR 0045-budgetbrott kvarstår för hela `are`-klassen).

### Beslut 2 — Variant (b): Infrastructure-query-port `IJobAdSearchQuery`

FTS-LINQ — `EF.Functions.WebSearchToTsQuery`, `NpgsqlTsVector.Matches`, `.Rank` — ligger fysiskt i `Npgsql.EntityFrameworkCore.PostgreSQL`-assemblyn (dotnet-architect web-verifierat 2026-05-21). Det finns **ingen provider-agnostisk väg** att uttrycka FTS-query i Application-lagret. Arch-testet `TaxonomyAclLayerTests.Application_should_not_depend_on_Npgsql_or_EF_relational` förbjuder Npgsql-beroende i Application.

Därför flyttas **hela sök-kompositionen** — `JobAdSearch.ApplyCriteria` + `ApplySort` + `ApplyRelevanceSort` — från Application till Infrastructure-impl:en `JobAdSearchQuery` (`internal sealed`), bakom Application-porten `IJobAdSearchQuery`. [ADR 0039](./0039-savedsearch-aggregate-and-query-run-semantics.md) Beslut 1:s SPOT bevaras — porten är den **enda** sök-vägen; det är fortfarande ett knowledge piece, bara i ett annat lager.

Mönstret speglar `IJobSource` / `ITaxonomyReadModel` ([ADR 0043](./0043-taxonomy-acl-for-search-surface.md)): Application-port, `internal` Infrastructure-impl, ren DTO över lager-gränsen — ingen EF-entity passerar Application-gränsen (CLAUDE.md §5.1).

senior-cto-advisor häver i rond 3 (2026-05-21) explicit sitt eget rond-2-beslut ("INTE port") på verifierad falsk premiss: rond 2 antog att FTS-LINQ kunde uttryckas provider-agnostiskt; dotnet-architects assembly-verifiering 2026-05-21 bevisade att `WebSearchToTsQuery`/`Matches`/`Rank` bor i Npgsql-assemblyn. När premissen föll föll slutsatsen — porten är nödvändig.

### Beslut 3 — Port-kontrakt: två metoder, splittad filter-record

Porten exponerar två metoder (senior-cto-advisor Variant B, 2026-05-21):

```
SearchAsync(JobAdSearchCriteria)  → PagedResult<JobAdDto>
CountAsync(JobAdFilterCriteria)   → int
```

- `SearchAsync` konsumeras av `ListJobAdsQueryHandler` och `RunSavedSearchQueryHandler` — full filtrering + `ts_rank`-relevans + sortering + paginering.
- `CountAsync` konsumeras av `ListRecentSearchesQueryHandler` — träffräkning per sparad sökning ([ADR 0060](./0060-recent-job-searches-auto-capture.md) Beslut 4:s capped N+1).

Filter-formen splittas i två records:

- `JobAdFilterCriteria(Ssyk, Region, Q)` — filter-SPOT (Fowler 2018, *Introduce Parameter Object*).
- `JobAdSearchCriteria` — komponerar `Filter` (en `JobAdFilterCriteria`) + presentations-fälten `SortBy` / `Page` / `PageSize` / `Since`.

**Tre konsumenter delar samma filter-SPOT** (`JobAdFilterCriteria`) — kompilator-garanti mot att list-/run-/count-vägarna divergerar i filter-semantik.

**Avvisat:** en ensam `SearchAsync` med `PageSize=1` för att räkna — bryter CLAUDE.md §2.3 (en count är ingen sök-resultatsida; det drar dessutom onödig `ts_rank`-ordering). En enda 7-fälts-record återanvänd för count — döda fält (`SortBy`/`Page`/`PageSize`/`Since` är meningslösa för en ren räkning, CLAUDE.md §5.1 primitive/struct-hygien).

### Beslut 4 — Additiv beslutsregel: ett tredje ben till ADR 0048

[ADR 0048](./0048-cross-aggregate-read-join-in-application-query-path.md) etablerade en beslutsregel längs **cross-aggregat-axeln**: "extern/översatt/context-korsande → port; intern/enkel/samma-DbContext → in-handler-join".

ADR 0062 lägger ett **ortogonalt tredje ben**:

> **Provider-specifik query-mekanik som fysiskt bor i en provider-assembly som arch-testet förbjuder i Application → port.**

`JobAdSearchQuery` läser **ett** aggregat (`JobAd`) — det är ingen cross-aggregat-läsning, och ADR 0048:s anti-corruption-/context-axel gäller inte. Drivkraften här är **assembly-/lager-fysik** (FTS-LINQ ⊂ Npgsql ⊂ förbjuden-i-Application), inte anti-corruption. En query kan alltså vara intern + samma-DbContext och **ändå** tvingas till en port av provider-assembly-skäl.

Detta är **inte ett undantag** från ADR 0048 utan en **annan beslutsaxel**. ADR 0062 **superseder inte** ADR 0048 — relationen är komplementär (samma "EJ supersession"-språk som ADR 0048 självt använde mot ADR 0043). Framtida läsare har nu två ortogonala kriterier för "join vs port": (1) cross-aggregat-axeln (ADR 0048) och (2) provider-assembly-axeln (ADR 0062).

### Beslut 5 — Båda trigram-indexen behålls

- `ix_job_ads_title_lower_trgm` (ADR 0061) **behålls och är aktivt** — det betjänar title-LIKE-fallback-grenen i Beslut 1:s hybrid.
- `ix_job_ads_description_lower_trgm` (ADR 0061) blir **oanvänt i q-grenen** efter FTS-skiftet (description-LIKE körs aldrig längre i q-sökningen). Det **behålls medvetet** (Klas-GO) — det kan betjäna framtida admin-queries eller relevans-arbete. Det betjänar **inte** q-search.

Detta dokumenteras explicit så att en framtida architect inte drop:ar `ix_job_ads_description_lower_trgm` av misstag i tron att det är dött efter FTS-skiftet — det är medvetet bevarat, inte kvarglömt.

## Konsekvenser

### Positiva

- **`lärare` förväntas falla 18.7s → < 0.2s** (GIN-tsvector-scan på ett exakt lexem i stället för låg-selektiv trigram-spridning).
- **Alla q-termer förväntas < 2s** (ADR 0045-budget) — den korta-vanlig-term-klassen som ADR 0061 inte nådde är nu täckt.
- **Svensk stemming som bonus** — `lärare`/`läraren`/`lärarens` matchar samma lexem; det adresserar exakt den morfologi-lucka ADR 0061 medvetet sköt upp (se ADR 0061-amendment 2026-05-21).
- **`ts_rank`-relevans** är en riktig FTS-rankning, ett kvalitetslyft mot ADR 0042 Beslut D2:s handviktade ILIKE-3-2-1-heuristik.
- **SPOT bevarad och stärkt** — tre konsumenter delar `JobAdFilterCriteria`; kompilator-garanti mot filter-divergens.

### Negativa / accepterade trade-offs

- **description-mitt-i-ord-substring matchas inte längre.** En sökning på en delsträng *mitt i ett ord* som bara förekommer i `description` ger ingen träff. Medveten trade-off: hela description-*ord* matchas via FTS (`search_vector` spänner `description`), och title-substring matchas via title-LIKE-grenen. Det som faller bort är enbart infix-delsträngar i description-texten.
- **GIN-tsvector-index är skriv-tyngre** än ingen kolumn. Acceptabelt — JobAd-ingest (snapshot-cron) är inte hot-path (samma resonemang som ADR 0061:s GIN-trigram-trade-off).
- **STORED generated column** ökar radstorleken med `search_vector`. Acceptabelt mot 18.7s → 0.2s-vinsten.

### Verifikations-plan post-deploy

1. Re-mät `GET /api/v1/job-ads?q=lärare` mot dev — förvänta < 0.2s, ned från 18.7s.
2. Re-mät ett urval q-termer (`systemutvecklare`, `snickare`, `sjuksköterska`) — förvänta alla < 2s.
3. `EXPLAIN ANALYZE` på q-query: verifiera **Bitmap Index Scan on `ix_job_ads_search_vector`** (FTS-grenen tar primärvägen).
4. `explain-search-mode`-verktyget uppdateras till den nya FTS-queryn så framtida diagnos speglar produktionsvägen.
5. ADR 0045 fitness function (NBomber p95) ratchet:as till den nya nivån vid framtida observation (observe-only Fas 1).

## Alternativ som övervägdes

### Alt A — PostgreSQL FTS-hybrid (VALT)
**För:** löser hela korta-vanlig-term-klassen via lexem-matchning; svensk stemming på köpet; `ts_rank` ger riktig relevans; ingen ny infrastruktur — PostgreSQL nativt; ryms på Hetzner CX32.
**Emot:** kräver schema-ändring (generated column + index); FTS-LINQ tvingar lager-omflyttning av sök-kompositionen (Beslut 2); description-infix-substring faller bort.

### Alt B — Elasticsearch / extern sökmotor (AVVISAT)
**För:** purpose-built sök med rik relevans och facettering.
**Emot:** managed $25–130/mån är opraktiskt dyrt för en korpus på ~51k rader; self-hosted kräver ~8GB RAM och ryms inte på Hetzner CX32 ([ADR 0050](./0050-deployment-migration-aws-exit-hetzner.md)). Massiv ops-overhead för ett problem PostgreSQL löser nativt. Klas-beslut: stanna i PostgreSQL.

### Alt C — FTS-only utan trigram-fallback (AVVISAT)
**För:** enklast — en enda gren, inget LIKE.
**Emot:** saknar mitt-i-ord-substring-semantik. UI-kontraktet förutsätter att `systemut` matchar `systemutvecklare` mitt i ord (samma kontrakt ADR 0061 Beslut 2 / Variant D1 redan identifierade). title-LIKE-grenen behövs.

### Alt D — Acceptera trigram-svagheten (AVVISAT)
**För:** noll ytterligare arbete efter ADR 0061.
**Emot:** ADR 0045-budgetbrott (< 2s p95) kvarstår för hela `are`-klassen av korta vanliga svenska termer. EXPLAIN ANALYZE 2026-05-21 bevisade att det är en strukturell trigram-egenskap som inte går att konfigurera bort.

## Implementation

- **Migration** `F6P4FtsSearchVector` (`20260521090234`) — STORED generated `search_vector tsvector` + partial GIN-index `ix_job_ads_search_vector ... WHERE deleted_at IS NULL`.
- **Application-port** `IJobAdSearchQuery` (`src/JobbPilot.Application/JobAds/Abstractions/IJobAdSearchQuery.cs`) — `SearchAsync` + `CountAsync`.
- **Filter-records** `JobAdFilterCriteria(Ssyk, Region, Q)` + `JobAdSearchCriteria` (komponerar `Filter` + `SortBy`/`Page`/`PageSize`/`Since`).
- **Infrastructure-impl** `JobAdSearchQuery` (`internal sealed`) — bär `ApplyCriteria` + `ApplySort` + `ApplyRelevanceSort`; DI-registrerad i Infrastructure composition (CLAUDE.md — DI i samma commit som impl).
- **`JobAdSearch.cs` borttagen** ur Application — sök-kompositionen lever nu i Infrastructure (se ADR 0039-amendment 2026-05-21).
- **Konsument-handlers** `ListJobAdsQueryHandler` + `RunSavedSearchQueryHandler` (via `SearchAsync`) + `ListRecentSearchesQueryHandler` (via `CountAsync`) — blir tunna adaptrar mot porten.
- **Arch-test** — `Application_should_not_depend_on_Npgsql_or_EF_relational` förblir grön (Npgsql-beroendet ligger nu uteslutande i Infrastructure-impl:en).
- **Gates:** code-reviewer 0 Block / 2 Major (M1 = denna ADR; M2 = arch-test `NpgsqlTypes` — åtgärdat) / 4 Minor. security-auditor 0 Block / 0 Critical / 0 High / 0 Medium / 1 Low (LIKE-metatecken i title-LIKE-grenen — pre-existing, ej regression). dotnet-architect-rond 2026-05-21 (port-design + FTS-LINQ-API-verifiering + test-topologi). senior-cto-advisor 3 ronder 2026-05-21 (FTS-hybrid + Variant b) + Variant B port-kontrakt 2026-05-21.

## Referenser

- Robert C. Martin, *Clean Architecture* (Prentice Hall, 2017), kap. 22 (Dependency Rule — Beslut 2:s lager-grund)
- Martin Fowler, *Refactoring* 2nd ed (Addison-Wesley, 2018) — *Introduce Parameter Object* (Beslut 3:s `JobAdFilterCriteria`-SPOT)
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — DRY/SPOT (filter-väg som ett knowledge piece, Beslut 3)
- Kent Beck, *Extreme Programming Explained* (1999) — YAGNI (Alt B/C/D-avvisningsgrund)
- Nygard, *Documenting Architecture Decisions* (2011) — additiv beslutsregel utan supersession (Beslut 4)
- [PostgreSQL docs — Full Text Search](https://www.postgresql.org/docs/current/textsearch.html) — `to_tsvector` / `websearch_to_tsquery` / `ts_rank` / GIN-tsvector-index
- [Npgsql EFCore docs — Full Text Search](https://www.npgsql.org/efcore/mapping/full-text-search.html) — `EF.Functions.WebSearchToTsQuery` / `NpgsqlTsVector.Matches` / `.Rank` (assembly-placering verifierad 2026-05-21)
- Mätningar och EXPLAIN-diagnos: `docs/sessions/2026-05-20-2340-f6-p4-sok-infrastruktur-fix.md` ("Forts. 2026-05-21"-sektionen — EXPLAIN ANALYZE + 3 CTO-ronder)
- Reviews: `docs/reviews/2026-05-21-f6-p4-fts-code-review.md`, `docs/reviews/2026-05-21-f6-p4-fts-security-audit.md`
- Kod-källor: `src/JobbPilot.Application/JobAds/Abstractions/IJobAdSearchQuery.cs`, migration `20260521090234_F6P4FtsSearchVector`
- Agent-domar: senior-cto-advisor 3 ronder 2026-05-21 (FTS-hybrid + Variant b + Variant B port-kontrakt), dotnet-architect 2026-05-21 (port-design + FTS-LINQ-assembly-verifiering + test-topologi), code-reviewer 2026-05-21, security-auditor 2026-05-21
- Relaterade ADR: 0001, 0039, 0042, 0043, 0045, 0048, 0050, 0060, 0061

---

*ADR-index underhålls av docs-keeper. ADR 0062 fastställer PostgreSQL FTS-hybrid som q-search-strategin efter att trigram-tröskeln passerades, och `IJobAdSearchQuery` som Infrastructure-query-port — komplementärt tredje ben till ADR 0048:s join-vs-port-regel (provider-assembly-axeln), EJ supersession.*

---

## Amendment 2026-05-23 — `ApplyCriteria` får `Status = Active`-SPOT-filter

**Datum:** 2026-05-23
**Källa:** F6 P5 Punkt 1 snapshot-retention-batch (CTO-dom 2026-05-23 Q4=(W), `a8e277380b446bb02`).
**Trigger:** Korpus-retention-arbete avtäckte att tre konsumenter (`ListJobAdsQueryHandler`, `RunSavedSearchQueryHandler`, `ListRecentSearchesQueryHandler`) saknade `Status=Active`-filter — UX-räkning inkluderade arkiverade JobAds.
**Beslutsfattare:** senior-cto-advisor 2026-05-23 + Klas Olsson (godkänd 2026-05-23, Variant 1: filter + retention samma release).
**Status:** Accepted 2026-05-23 (Klas-GO).

### Beslut

`JobAdSearchQuery.ApplyCriteria` (Infrastructure/JobAds/`JobAdSearchQuery.cs`) får `source.Where(j => j.Status == JobAdStatus.Active)` som **första** filter-steg, före `ApplyQ`/`ApplyFilters`. SPOT-mekanism — alla tre konsumenter av `IJobAdSearchQuery` (`SearchAsync` + `CountAsync`) ärver filtret automatiskt.

**Avvisat:** filter per konsument-handler (bryter SPOT, tre divergens-risker), global query-filter på `JobAd` (admin-ytor måste `IgnoreQueryFilters()`, ADR 0048 förbjuder), filter i `JobAdSearchCriteria`-record (gör presentations-laget ansvarigt för domain-invariant).

### Motivering

Full motivering, beslutsmatris, defense-in-depth-mekanism och cron-schema dokumenterade i [**ADR 0032-amendment 2026-05-23**](./0032-jobtech-integration.md#amendment-2026-05-23--snapshot-retention-defense-in-depth-miss-cleanup--expiresat-cron--applycriteria-statusactive-spot) (snapshot-retention: miss-cleanup + ExpiresAt-cron + ApplyCriteria-filter).

Detta amendment dokumenterar enbart filter-tillägget i `IJobAdSearchQuery`-portens impl — query-mekaniken (FTS-hybrid, `IJobAdSearchQuery`-port, splittad filter-record per Beslut 1–3) är oförändrad. ADR 0062 Beslut 1–5 består.

### Implementations-trail

- `src/JobbPilot.Infrastructure/JobAds/JobAdSearchQuery.cs` (ÄNDRAD — `Status=Active`-filter som första steg i `ApplyCriteria`)

### Konsekvenser

- **UX-räkne-konsistens** — `/jobb`-totalCount, `RunSavedSearchQueryHandler`-resultat och `ListRecentSearchesQueryHandler`-träffräkning reflekterar samtliga aktiv-marknads-korpusen.
- **Klas-STOPP-flaggad UX-drop** (~56k → ~40k vid deploy) — Klas valde Variant 1 (filter + retention samma release) för konsistent state över alla läs-ytor från deploy-tillfället. Detaljer i ADR 0032-amendment 2026-05-23.
- **SPOT bevarad och stärkt** — `IJobAdSearchQuery`-portens monopol på sök-läsning gör att en enda kod-rad i `ApplyCriteria` täcker alla tre konsumenter. Kompilator-garanti mot framtida divergens.

### Referenser

- [ADR 0032-amendment 2026-05-23](./0032-jobtech-integration.md#amendment-2026-05-23--snapshot-retention-defense-in-depth-miss-cleanup--expiresat-cron--applycriteria-statusactive-spot) — full beslutsmatris (Q1/Q2/Q3/Q4/Q5/Q6) + defense-in-depth-mekanism + cron-schema
- senior-cto-advisor 2026-05-23 (`a8e277380b446bb02`) — Q4=(W) ApplyCriteria-SPOT-val
- ADR 0062 Beslut 1–3 (FTS-hybrid + port-kontrakt — oförändrade)
- ADR 0048 (cross-aggregat join vs port — global query-filter avvisad här på samma grund)

---

## Amendment 2026-06-13 — title-LIKE branch gated on `q.Length >= 3` (TD-94 perf-ratchet)

**Datum:** 2026-06-13
**Källa:** TD-94 root fix — free-text q-COUNT violated ADR 0045 Klass (a) 300 ms p95 warm.
**Trigger:** `CountAsync` (and by extension `FacetCountsAsync` and `SearchAsync`'s internal `totalCount`) measured 777 ms warm / 9 310 ms OS-cold for q = "ai" (2-char), far exceeding the 300 ms p95 budget. EXPLAIN ANALYZE isolated the root cause to the planner choosing a Seq Scan that de-TOASTs the wide STORED `search_vector` column (521 MB TOAST, ~3 198 B/row) for every row when the title-LIKE branch forces a full-corpus evaluation for queries too short to use the GIN trigram index.
**Beslutsfattare:** senior-cto-advisor 2026-06-13 (agentId `a0472fa5783cdf9ea`) + dotnet-architect 2026-06-13 (root-cause isolation, agentId `a1ce174943247863e`); Klas Olsson (Accepted-direktiv, TD-94 Klas-direktiv session 2026-06-13 — Klas reviews prose post-hoc).
**Status:** Accepted 2026-06-13.

> **Livscykel-/proveniens-not:** Written 2026-06-13 by Claude Code (adr-keeper) on explicit Klas-direktiv for TD-94 root fix, following the same proveniens-mönster established at ADR 0062 rad 9 (memory `feedback_klas_can_override_adr_verbatim_source`). Decision substance grounded in agent verdicts: dotnet-architect root-cause isolation (agentId `a1ce174943247863e`, 2026-06-13) and senior-cto-advisor ruling that the filter-semantic change must be documented as an ADR 0062 amendment (agentId `a0472fa5783cdf9ea`, 2026-06-13). Klas reviews prose post-hoc.

### Beslut

**Beslut 1 Amendment** — The title-LIKE branch in Beslut 1's q-hybrid is now gated on `q.Length >= 3`:

```
-- q >= 3 chars (unchanged):
search_vector @@ websearch_to_tsquery('swedish', q)  OR  lower(title) LIKE '%q%'

-- q < 3 chars (new — FTS-only, no title-LIKE):
search_vector @@ websearch_to_tsquery('swedish', q)
```

This gate lives in `ApplyCriteria` inside `Jobbliggaren.Infrastructure/JobAds/JobAdSearchQuery.cs` — the ADR 0039 Beslut 1 SPOT. Because all three consumers (`SearchAsync`, `CountAsync`, `FacetCountsAsync`) pass through the same shared `ApplyCriteria`, the gate applies identically to list, count, and facets. There is no list↔count divergence.

### Root cause (dotnet-architect isolation, 2026-06-13)

The planner chose a Seq Scan when the q-length was below 3. The isolation proof:

- Forced seq scan + `lower(title) LIKE '%utvecklare%'` → 44 ms / 7 274 buffers.
- Forced seq scan + `search_vector @@ 'utvecklare'` → 531 ms / 223 335 buffers → **487 ms delta = pure TOAST-detoast** of the `search_vector` STORED column.

A GIN trigram index physically cannot serve a `LIKE '%q%'` predicate for a query shorter than 3 characters (trigrams are 3-grams). For a 2-char q such as "ai" the title-LIKE branch therefore forced a btree-prefix/seq scan over the full corpus (42 873 rows, 346 ms warm) even though FTS alone was highly selective (1 907 rows, < 20 ms). The planner's cost model does not account for TOAST-detoast cost, so it mis-selected Seq Scan over the GIN bitmap when the title-LIKE predicate was present.

Gating title-LIKE to `q.Length >= 3` lets short queries use the GIN `search_vector` bitmap directly, avoiding full-corpus TOAST-detoast.

### Companion mechanism: `SET LOCAL enable_seqscan = off` for count paths

`CountAsync`, `FacetCountsAsync`, and `SearchAsync`'s internal `totalCount` now wrap the pure-count query in a transaction with `SET LOCAL enable_seqscan = off` (implemented as `CountWithBitmapPlanAsync` in `JobAdSearchQuery.cs`). This coaxes the planner to the GIN Bitmap(Or) plan even when it would otherwise mis-cost it due to the TOAST-detoast gap in its cost model.

This is an **execution-budget concern, not a filter-semantic change** — it does not alter which rows match. ADR 0039 Beslut 1's SPOT on filter-semantics is intact. The list's `ts_rank`-ordered items query is deliberately left on the default planner (its ordered scan benefits from planner freedom). This mechanism does not require a separate Beslut; it is documented here so a future reader understands why count and list paths have different execution hints.

### Trade-off accepted

Mid-word substring matching on the `title` for queries shorter than 3 characters is dropped (from list, count, and facets uniformly). The UI contract established in Beslut 1 (`systemut` → `systemutvecklare`) requires at least 3 characters and is unaffected. A 2-char title-substring match is marginal recall in practice: short FTS lexemes cover the same tokens, and the prior behavior for < 3-char queries was carrying a full-corpus Seq Scan (346 ms warm) regardless of result selectivity.

senior-cto-advisor (2026-06-13, agentId `a0472fa5783cdf9ea`) ruled this a net improvement with no functional regression against the documented UI contract, and mandated it be documented as an ADR 0062-amendment because it changes the filter-semantics of the shared SPOT.

### Verified results (warm / shared-cold, local dev corpus 42 711 active job_ads)

| Query | Before | After | ADR 0045 Klass (a) budget |
|---|---|---|---|
| ai (2-char, now FTS-only) | 777 ms warm / 9 310 ms OS-cold | 15 ms / 16 ms | 300 ms p95 |
| utvecklare (≥3, BitmapOr) | 294–413 ms | 96 ms / 157 ms | 300 ms p95 |
| lärare (≥3, BitmapOr) | 332 ms | 116 ms | 300 ms p95 |

All results within ADR 0045 Klass (a) 300 ms p95 warm. 169 integration tests green (JobAds + RecentSearches + SavedSearches). OS-cold cliff structurally reduced (bitmap reads ~1 700–42 000 buffers vs 177 000 for seq scan); full OS-cold mitigation (pg_prewarm) is a separate fas-tillhörig TD per CTO ruling.

### Implementations-trail

- `src/Jobbliggaren.Infrastructure/JobAds/JobAdSearchQuery.cs` — `ApplyCriteria` q-predicate gated on `q.Length >= 3`; `CountWithBitmapPlanAsync` wraps count queries with `SET LOCAL enable_seqscan = off`.

### Konsekvenser

- **ADR 0045 Klass (a) 300 ms p95 warm restored** — all measured q-terms now within budget.
- **SPOT preserved** — the `q.Length >= 3` gate sits in the single shared `ApplyCriteria`; list, count, and facets cannot diverge.
- **Beslut 1–5 of ADR 0062 are otherwise unchanged.** The FTS-hybrid architecture, `IJobAdSearchQuery` port, `JobAdFilterCriteria` SPOT, and cross-references to ADR 0039/0061 remain in force.
- **Trade-off accepted** — < 3-char title-LIKE substring dropped; marginal recall loss against a structural perf win. Documented explicitly so a future reader does not remove the gate assuming it is an oversight.

### Referenser

- [ADR 0045](./0045-performance-budget-and-fitness-functions.md) Beslut 1 Klass (a) — 300 ms p95 warm budget (violated before this fix)
- [ADR 0039](./0039-savedsearch-aggregate-and-query-run-semantics.md) Beslut 1 — `ApplyCriteria` SPOT (gate lives here; SPOT integrity preserved)
- [ADR 0061](./0061-job-ad-search-perf-strategy.md) — GIN trigram-index precedent (trigrams cannot serve < 3-char LIKE predicates)
- TD-94 — free-text COUNT perf-ratchet (re-opened after initial FTS-hybrid deploy; closed by this amendment)
- dotnet-architect 2026-06-13 (agentId `a1ce174943247863e`) — root-cause isolation, TOAST-detoast delta proof
- senior-cto-advisor 2026-06-13 (agentId `a0472fa5783cdf9ea`) — net-improvement ruling + mandate to document as ADR 0062-amendment
- `src/Jobbliggaren.Infrastructure/JobAds/JobAdSearchQuery.cs` — q-predicate gate + `CountWithBitmapPlanAsync`
