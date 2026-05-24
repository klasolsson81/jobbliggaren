# STEG 6 — Fas A Discovery: Search-recall för "systemutvecklare"

**Datum:** 2026-05-24
**Session-uppdrag:** Akut MVP-demo-fix måndag 25 maj — höj sök-recall för "systemutvecklare" från 162 hits till minst 600.
**Källa:** Klas-prompt STEG 6; SSM-Exec-baseline söndag 24 maj 2026.
**Discovery cap:** 45 min (Fas A1+A2+A3) — utfört.
**Föregående HEAD:** `758fc90` (v0.2.65-dev deployad).

---

## A1 — JobTech taxonomy-uppslag (utvecklar-occupations)

Web-verifierat 2026-05-24 mot `https://taxonomy.api.jobtechdev.se/v1/taxonomy/suggesters/autocomplete?query-string=…&type=occupation-name`.

### Utvecklar-occupations (concept_ids → preferred_label)

**Kärngrupp för "systemutvecklare"-expansion** (9 stycken):

| concept_id | preferred_label |
|---|---|
| `fg7B_yov_smw` | **Systemutvecklare/Programmerare** ★ exakt match |
| `rQds_YGd_quU` | Mjukvaruutvecklare |
| `7wdX_4rv_33z` | Backend-utvecklare |
| `GDHs_eoz_uKx` | Frontend-utvecklare |
| `71Ji_irM_rSJ` | Fullstack-utvecklare |
| `oSg4_2uU_wpN` | Devops utvecklare |
| `n2kJ_qFK_x2K` | Mobilutvecklare/Apputvecklare |
| `WoBk_fWz_nrt` | GIS-utvecklare |
| `U1YF_JRy_qoo` | Administrativ utvecklare |

**Specialiserad spel-programmering** (5 — kan inkluderas eller utelämnas; klassiska "systemutvecklare"-användare letar sannolikt inte efter spel-roller):

| concept_id | preferred_label |
|---|---|
| `TTF3_T1t_58U` | Programmerare, PC-spel |
| `CmFu_Dkt_fkX` | Programmerare, konsolspel |
| `kG4e_dXA_XQf` | Programmerare, mobilspel |
| `6n5q_5Dz_JuW` | Programmerare, webbspel |
| `io6z_ZVg_ZwT` | Programmerare, onlinespel |

### Korrigering av prompt-antaganden

Prompten antog att top-SSYK-coverage i dev-RDS (`eU1q_zvL_9Rf` 560 jobb, `bXNH_MNX_dUR` 380 jobb) var "utvecklarrelaterade SSYK 2512". Felaktigt:

- `eU1q_zvL_9Rf` = **Personlig assistent** (inte utvecklare)
- `bXNH_MNX_dUR` = **Sjuksköterska, grundutbildad** (inte utvecklare)

Top-SSYK-coverage reflekterar svensk arbetsmarknad i stort, inte utvecklar-domänen. Detta påverkar inte expansion-strategin (mappningen `"systemutvecklare" → 9 utvecklar-concept_ids` ovan är fortfarande rätt), men det betyder att vi inte kan baka in en "top-N SSYK"-heuristik som proxy.

### Senior Java AWS Developer-annonsernas SSYK (verifierat)

| Annons-id | ssyk_concept_id | Resolverad label |
|---|---|---|
| `ce2a1a11-179e-402d-be41-e8614c8cb46e` | `rQds_YGd_quU` | Mjukvaruutvecklare |
| `8360eb89-027f-49c4-a519-9223f09719da` | `7wdX_4rv_33z` | Backend-utvecklare |
| `a1574fde-ce10-4819-88ac-74589afa3a97` | `NULL` | — (källan tomt) |

De 2 första matchar **Approach B** (SSYK-expansion via fritext→concept_id). Den tredje matchar ENDAST om **Approach A/C** (sync-fix + backfill) körs och JobTech faktiskt har occupation för annonsen vid re-sync.

---

## A2 — Sync-kod + EF-mapping

### Sync-pipeline (filer)

- `src/JobbPilot.Infrastructure/JobSources/Platsbanken/JobTechSearchResponse.cs` — wire-format. `JobTechHit.Occupation` är `[JsonPropertyName("occupation")]` (sedan F6 P4-fix 2026-05-20). Innehåller `ConceptId`, `Label`, `LegacyAmsTaxonomyId`. **Korrekt.**
- `src/JobbPilot.Infrastructure/JobSources/Platsbanken/PlatsbankenJobSource.cs:199` — `var rawJson = JsonSerializer.Serialize(hit);` — defaults respekterar `[JsonPropertyName]` → output `"occupation": {…}` (lowercase). **Korrekt.**
- `src/JobbPilot.Infrastructure/JobSources/Platsbanken/JobTechPayloadSanitizer.cs:42-50` — allowlist innehåller `occupation`, `concept_id`. Sanitizern släpper igenom rekursivt. **Korrekt.**

### EF-mapping (kritisk)

`src/JobbPilot.Infrastructure/Persistence/Configurations/JobAdConfiguration.cs:75-77`:

```csharp
builder.Property<string?>("SsykConceptId")
    .HasColumnName("ssyk_concept_id")
    .HasComputedColumnSql("raw_payload->'occupation'->>'concept_id'", stored: true);
```

`SsykConceptId` är **shadow-property** (ej CLR-property på `JobAd`), **STORED generated column** av Postgres. Värdet härleds vid INSERT/UPDATE från `raw_payload`-jsonb. B-tree-index `ix_job_ads_ssyk_concept_id` partial WHERE `ssyk_concept_id IS NOT NULL`.

LINQ-referens: `EF.Property<string?>(j, "SsykConceptId")`.

### Hypotes för 76% NULL ssyk_concept_id

Givet att sync-koden är strukturellt korrekt 2026-05-24, är 35 384 NULL-rader rotsymptom av en av:

1. **JobTech-källan saknar `occupation.concept_id` för dessa annonser.** Vissa annonser i Platsbanken klassificeras inte med SSYK av arbetsgivaren — fältet kommer tom från JobTech. Re-sync hjälper inte; raw_payload re-skapas men `occupation.concept_id = null` → STORED column förblir NULL.
2. **Legacy-rader (pre-2026-05-20-fix).** Innan F6 P4-fixen 2026-05-20 där `JobTechHit.Occupation` lades till deserialisering, blev `raw_payload` lagrad utan `occupation`-key. STORED column = NULL. Efter fixen får nyare imports occupation. Snapshot-cron upsert:ar via `UpdateFromSource(..., rawPayload, ...)`; raw_payload re-skrivs och STORED column re-evaluteras. Men om en annons aldrig får en stream-update OCH inte landar i snapshot (sällsynt) → NULL kvar.

Verifierings-test för Hypotes 1 (kan göras post-implementation): plocka 5 NULL-annons-id, anropa JobTech jobsearch-API mot specifik id, kolla om `occupation.concept_id` finns vid källan. Om JA → bug i sync-trigger; om NEJ → data saknas vid källan, ingen kod-fix möjlig.

### EF Core 10 + Npgsql translation-risk för `ANY(@arr)`

Memory `feedback_ef_strongly_typed_vo_contains_translation` gäller strongly-typed value objects (e.g. `JobAdId`). `SsykConceptId` shadow är `string?` — `IEnumerable<string>.Contains(EF.Property<string?>(j, "SsykConceptId"))` översätts av Npgsql 10 till `ssyk_concept_id IN (@p0, @p1, …)` eller `ssyk_concept_id = ANY(@arr)` beroende på cardinality. **Mönstret finns redan i kodbasen** (`JobAdSearchQuery.cs:110` Ssyk-filter), så det är verifierat fungerande.

---

## A3 — Baseline mot dev-RDS

Baseline från Klas-prompt (verifierat via SSM-Exec söndag 24 maj 2026):

```
46 328 aktiva job_ads (status='Active', deleted_at IS NULL)
35 384 (76%) har ssyk_concept_id = NULL
10 944 (24%) har ssyk_concept_id satt
```

Sökning för "systemutvecklare" mot UI dev-environment: **162 hits**.
Platsbankens egen sökning: **803 hits**.
Recall-gap: **641 hits saknas (80%)**.

CC försökte ytterligare SSM-Exec idag 2026-05-24 för stickprov mot utvecklar-SSYK-counters i RDS — `TargetNotConnected`-fel mot ECS-task `fb61a5bba361414a9dcfba0fc9f5ef83` (även om `enableExecuteCommand=true`). Antaglig orsak: SSM-agent-lokal endast på Klas workstation, inte CC-VM. Prompt-baseline är tillräcklig grund för CTO-rond.

**Förväntad effekt av Approach B (SSYK-expansion i query-grenen):**

Approach B lägger till `OR ssyk_concept_id = ANY(@expansion-arr)` i Q-grenen. Träffar exakt de annonser där:
- (a) SSYK är satt (24% av korpus = ~10 944 jobb), OCH
- (b) SSYK matchar en av 9 utvecklar-concept_ids

Av 803 platsbanken-hits för "systemutvecklare" är troligen ~60-70% klassificerade med utvecklar-SSYK (rest ostagged). Effekt-estimat:

- Befintlig FTS+title-LIKE för "systemutvecklare": ~162 (verifierat)
- Approach B-tillägg: 9 utvecklar-SSYK × andel utvecklar-jobb i 24% NULL-fria del av korpus → uppskattning 400-600 ytterligare träffar
- **Totalt Approach B: ~600-750 hits** (cap = NULL-rader kan inte fångas)

Approach A/C (backfill 35 384 NULL-rader): kan nå 750-800+ MEN endast om JobTech har occupation för dem. Per Hypotes 1 ovan är detta sannolikt INTE fallet — backfill kan ge mindre lyft än man hoppas.

---

## A4 — Web-search-verifierat (2026-05-24)

Per CLAUDE.md §9.5:

- **JobTech Taxonomy API** — `/v1/taxonomy/suggesters/autocomplete?query-string=…&type=occupation-name` är fungerande endpoint för occupation-search. Response-shape `[{concept_id, preferred_label}]`. Verifierat genom 6 lyckade queries 2026-05-24.
- **EF Core 10 + Npgsql.EntityFrameworkCore.PostgreSQL** — `EF.Property<string?>` shadow-properties + `Contains` på `IReadOnlyCollection<string>` översätts korrekt till SQL IN/ANY. Mönstret är i produktion (`JobAdSearchQuery.cs:110`).
- **Postgres 18 GIN-index + B-tree på `ssyk_concept_id`** — `ssyk_concept_id = ANY(@arr)` med < 20 element använder befintligt `ix_job_ads_ssyk_concept_id` partial-index via planner-conversion till IN-clause (verifierat via PG-docs). Inget nytt index behövs för Approach B.

---

## Tre approacher — sammanfattning för CTO-rond

### Approach A — Sync-fix + backfill (rätt fix)

- **Effekt:** kan nå 750-800 hits OM JobTech-källan har occupation
- **Tid:** 4-6h (verifiera hypotes mot källan, ev. fixa sync-trigger, skriv idempotent backfill-Hangfire-job, kör mot dev, verifiera)
- **Risk:** om Hypotes 1 (källan saknar data) bekräftas → backfill ger 0 effekt → MVP-demo missar mål. Stort spelmoment.
- **Klas-direktiv-paritet:** "inga quickfixes" → ✓ (genuin rotsymptom-fix)
- **Time-pressure:** måndag MVP-demo riskerar bli sen

### Approach B — Pragmatisk fritext→SSYK-mapping i query

- **Effekt:** estimat 600-750 hits (cap = 24% av korpus med satt SSYK + 9 utvecklar-concept_ids)
- **Tid:** 1-2h (Application-port `IOccupationSynonymExpander` + Infrastructure-impl med hard-coded mapping ELLER `IOptions<SearchSynonymsOptions>` config + JobAdSearchQuery-ApplyCriteria-utvidgning + tester + DI)
- **Risk:** lämnar 76% NULL-SSYK som tech debt; expansion-mapping inte uttömmande (specifika fall som "ML-engineer" missas)
- **Klas-direktiv-paritet:** "inga quickfixes" → ✗ per definition; **kräver explicit motivering + ADR-dokumentation + TD-lyft**
- **Time-pressure:** klart söndag eftermiddag

### Approach C — Hybrid (B nu + A dokumenterat som TD)

- **Effekt:** ~600-750 hits omedelbart, väg till 750-800 efter framtida backfill
- **Tid:** 1-2h nu + dokumentation
- **Risk:** lågt akut; medel långt sikt (TD-skuld om A aldrig adresseras)
- **Klas-direktiv-paritet:** ✗ för B-delen; ✓ för dokumentationen att A är rätt fix
- **Time-pressure:** klart söndag eftermiddag; A bli ny session post-MVP

---

## Beslutsmatris (sammanfattning)

| Kriterium | A | B | C |
|---|---|---|---|
| MVP-mål ≥ 600 hits | Hög risk (beroende av källan) | Hög sannolikhet (~600-750) | Hög sannolikhet |
| Tid söndag 24 maj | 4-6h (kanske för sent) | 1-2h | 1-2h |
| Klas "inga quickfixes" | ✓ | ✗ | ✗ för B-delen |
| Permanent fix | ✓ | ✗ (TD kvar) | ✗ kort sikt, ✓ lång sikt |
| Tech-debt-skuld | 0 | hög | medel (dokumenterat) |
| ADR/dokumentations-krav | normal | hög (ADR-amendment) | hög |
| Regression-risk på andra söktermer | låg | låg-medel (ny clause i Q-grenen) | låg-medel |
| TD-94-perf-påverkan | låg (separat backfill-job) | medel (ny ANY-clause i hot-path) | medel |

---

## Beredskap för CTO-rond

CTO bör väga:

1. **Time-pressure söndag→måndag** vs Klas-direktiv 2026-05-24 "inga quickfixes". Approach B/C är quickfixes per definition — om vald, måste de vara välmotiverade.
2. **TD-94 (Major × Fas Nu) implikation:** ny `ANY(@arr)`-clause i `JobAdSearchQuery.ApplyCriteria` påverkar samma query-väg som redan är 4-22x över ADR 0045-budget. Approach B/C lägger på en clause utan att fixa rot.
3. **Hypotes 1-risk för Approach A:** om JobTech-källan saknar occupation för 76% av annonserna är A:s effekt 0. Discovery-verifiering (stickprov mot jobsearch-API för 5 NULL-id) krävs INNAN A-investering startar.
4. **ADR-amendment 0062 eller ny 0066** om B/C valdes.
5. **Klas-STOPP-flagga** — denna är större produkt-/strategi-fråga, inte rent teknisk. CTO motiverar tydligt; Klas väljer A/B/C.
