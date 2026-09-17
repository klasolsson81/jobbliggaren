# db-migration-writer — #1736 (epic #1732 part 1b): migration review of PR #1751

**Date:** 2026-09-17 · **Agent:** db-migration-writer (`/migration-review`, fresh instance) · **PR:**
[#1751](https://github.com/klasolsson81/jobbliggaren/pull/1751) · **Head reviewed:** `402fe30a`
(commits `0c2adead` · `45901320` · `67325f30` · `402fe30a`, base `88b6ea93`) · **Mode:** report-only,
no database update, no integration suites run by the agent · **Verdict:** APPROVE, 0 blocking, 0
non-blocking. Report returned in the reply and transcribed verbatim by the session.

---

## Migration review: 20260917141433_AddTermsAcceptanceToJobSeeker (PR #1751)

**Verdikt: APPROVE som committat. 0 blocking findings.**

### Migrationsfilen (migration-review scope)

`src/Jobbliggaren.Infrastructure/Persistence/Migrations/20260917141433_AddTermsAcceptanceToJobSeeker.cs:14-49`
— exakt tre `AddColumn<...>` i `Up` (`privacy_policy_version` character varying(20) maxLength 20 nullable:true, `terms_accepted_at` timestamp with time zone nullable:true, `terms_version` character varying(20) maxLength 20 nullable:true), och exakt tre `DropColumn` i `Down`. Inget annat i filen. Additiv migration — inget DROP/RENAME/typbyte, inga destruktiva element, inget godkännande-krav.

`AppDbContextModelSnapshot.cs` diff (`git diff 88b6ea93..402fe30a`) — endast ett nytt `OwnsOne("...TermsAcceptance", "TermsAcceptance", ...)`-block plus `b.Navigation("TermsAcceptance")` (utan `.IsRequired()`, dvs optional). Inget annat ändrat i snapshotten. `.Designer.cs` (ny fil, 1932 rader) speglar exakt samma tre kolumner/typer/nullability vid rad 1782-1816 — konsistent med snapshotten. Allt tre filer (migration, Designer, snapshot) är i fas.

### Configuration (`JobSeekerConfiguration.cs:40-50`)

`OwnsOne(js => js.TermsAcceptance, ...)` med explicit `HasColumnName` på alla tre properties (undviker navigation-prefixet `terms_acceptance_*`), `HasMaxLength(20)` på de två strängkolumnerna, `builder.Navigation(js => js.TermsAcceptance).IsRequired(false)`. Ingen inre `IsRequired()`-kedja på properties (korrekt — de tre properties i VO:n är non-nullable *inuti typen*, men den yttre navigationen måste vara optional för att representera "ingen stämpel").

Mönstret har etablerad precedens: `ApplicationConfiguration.cs:70` (`ManualPosting`) och `:122` (`AdSnapshot`) använder samma `Navigation(...).IsRequired(false)`-form. Kommentarens sakpåståenden håller:
- "EF Core 10 defaulterar en owned reference till required" — konsistent med varför `ManualPosting`/`AdSnapshot` behöver samma flagga.
- "sentinel unambiguous by construction" — verifierat: `TermsAcceptance` är en `sealed record` med tre non-nullable properties och en enda privat konstruktor (`TermsAcceptance.cs:44-49`), så en instansierad stämpel kan aldrig skriva en all-null rad.
- snake_case-kolumnnamn matchar namnkonventionen (`terms_accepted_at`, `terms_version`, `privacy_policy_version`).

### Skrivvägen håller NOT-NULL-löftet efter 1b

`JobSeeker.Register(Guid, string?, TermsAcceptance acceptance, IDateTimeProvider)` (`JobSeeker.cs:142-174`) tar `TermsAcceptance` som icke-nullbar parameter och refuserar explicit `acceptance is null` (`:157-160`, `JobSeeker.TermsAcceptanceRequired`) — signaturen **ersatte** den gamla (inte overload), så den stämpel-lösa vägen går inte längre att anropa. Enda produktionsanropet, `RegisterCommandHandler.cs`, skickar `TermsAcceptance.AcceptCurrent(clock)` (diff verifierad). `RegisterCommandValidator.cs` kräver `AcceptTerms == true` (`Equal(true)`, inget default på command-membret → en JSON-body utan fältet binder till `false` och refuseras) **innan** handlern och därmed innan `CreateUserAsync`. Två oberoende spärrar (request-form + aggregat-invariant), vilket matchar ADR 0142 D6-texten ordagrant (`docs/decisions/0142-...md:278-279, 316-321`). NULL är alltså bara nåbart för rader skrivna före denna migration — påståendet i uppdraget stämmer.

### Testtäckning — up/down-bevis mot Testcontainers

**`AddTermsAcceptanceToJobSeekerMigrationTests.cs`** (Worker.IntegrationTests) kör som app-rollen (`TestDatabaseProvisioner`, samma provisioning som produktionens migreringsroll under Phase A-privilegier), journey head → previous (`20260914134635_AddOccupationDivisionProfile`) → head på egen container, läser kolumnformen ur `information_schema.columns` (inte inferrerat från migrationsfilen) vid varje stopp. Verifierar: (1) vid head — tre kolumner, rätt datatyp, nullable=true, maxlength 20 på de två varchar-kolumnerna; (2) efter rollback — kolumnerna borta (`ShouldBeEmpty()`); (3) efter ny forward — kolumnerna tillbaka och `GetPendingMigrationsAsync` tom. Detta är ett fullständigt och genuint bevis på att `Down` faktiskt kör (inte bara scaffoldad), vilket är den svaga länken protokollet pekar på.

**`TermsAcceptanceBackcompatTests.cs`** (Api.IntegrationTests) testar läsvägen: en seedad rad med alla tre kolumner satta till NULL via rå SQL (simulerar en förmigrations-rad) laddas som `TermsAcceptance == null` utan krasch, resten av aggregatet materialiseras fortfarande korrekt, och en stämplad rad round-trippar exakt (kontrollfallet mot en mappning som alltid returnerar null). Premisstexten (`:20-29`) namnger uttryckligen skådespelaren för den ej-producerbara all-NULL-raden ("rows written before migration ...") och pekar till pinningen i `JobSeekerTests` att den nuvarande skrivvägen inte längre kan producera den formen — följer §5 Tests-disciplinen korrekt (aktör namngiven, inte en hand-vinkad premiss utan ankare).

**Slutsats om tillräcklighet:** ja, tillsammans är detta ett fullständigt bevis för "rent mot Testcontainers" i båda riktningar (schema up/down som app-rollen, och läsvägens bakåtkompatibilitet mot befintliga rader). Jag körde inte själv om suiterna (instruerat att avstå, och den fulla Api-sviten kör redan i detta träd) — bedömningen bygger på kodgranskning av testerna, inte en egen körning.

### Deploy-path / Phase A-privilegier

Ingen påverkan. `ADD COLUMN ... NULL` (utan DEFAULT, utan extension, utan funktionellt index) är ren EF Core-uttryckbar DDL som ryms inom `jobbliggaren_app`-rollens befintliga grant (`GRANT ALL ON ALL TABLES` + `ALTER DEFAULT PRIVILEGES` från Phase A, ADR 0033/0034) — kräver inte `CREATE ON DATABASE`, vilket är den enda begränsningen TD-71/ADR 0033 sätter (det är därför `pg_trgm`-extensionen måste köras av superuser i `ensure-extensions`-läge, medan denna migration inte rör extensions alls). `Jobbliggaren.Migrate`s `schema`-läge (Phase E) kör som app-rollen och detta är väl inom dess behörighet.

### GDPR

- **Art. 6(1)(b)-kontraktsstämpel + Art. 13-notisversion** — plaintext by design, korrekt: detta är inte känslig PII i sig (två versionstokens + en tidsstämpel), utan en accountability-post; ingen kryptering krävs, ingen maxlängd-konflikt med krypterade kolumner (Form A/B/C-taxonomin gäller inte här).
- **Soft delete + audit trail**: `job_seekers` har redan `deleted_at`/`created_at`/`updated_at` + `HasQueryFilter(js => js.DeletedAt == null)` (`JobSeekerConfiguration.cs:93-97`, oförändrat av denna PR). `TermsAcceptance` är en owned type utan egen livscykel — den delar förälderns audit-kolumner, samma mönster som `ManualPosting`/`AdSnapshot`. Ingen egen soft-delete-kolumn behövs för en owned type.
- **Erasure-cascade**: `ErasureCascadeRegistry.cs:504-509, 789-794` klassificerar `job_seekers.terms_version` och `job_seekers.privacy_policy_version` som `NotRecruiterData` med motivering (enda skrivväg är Domain-konstanter via `AcceptCurrent`, ingen användartext). Precedensen som åberopas, `resume_files.pnr_consent_dialog_version`, existerar verkligen i registret (rad 587) — inget spöke-citat. `terms_accepted_at` är timestamptz och explicit noterad som utanför textsvepet. Vid en Art. 17-hårdradering av `job_seekers`-raden försvinner alla tre kolumnerna med resten av raden — ingen separat kaskad behövs eftersom de inte ligger i en egen tabell.
- **Optimistic concurrency**: ingen `xmin`-konfiguration i `JobSeekerConfiguration.cs` — men detta är befintligt, opåverkat av denna PR (inte introducerat eller borttaget av diffen), så jag flaggar det inte som ett fynd i denna granskning.

### Single-owner hotspot (migrations)

`git fetch origin` + `git log --oneline 88b6ea93..origin/main -- .../Migrations .../Identity/Migrations` → tomt resultat. Ingen annan migration har landat på `origin/main` sedan basen. Ingen kollisionsrisk.

### Övrigt granskat, ej migrationsspecifikt men konsistensverifierat

`TermsAcceptance.cs`, `TermsAcceptanceTests.cs`, `TermsAcceptanceVersionsMatchPublishedPolicyTests.cs`, `RegisterCommand(Validator/Handler).cs`, och ADR 0142 D6 + dess 2026-09-17-amendment (`docs/decisions/0142-...md:266-330`) — allt konsekvent med varandra och med det committade schemat. Inga motsägelser mellan ADR-text, kod och migration.

### Sammanfattning

| Kontroll | Status |
|---|---|
| Migration = 3 nullable AddColumn / 3 DropColumn, inget annat | ✓ |
| Snapshot-diff minimal (owned-block + optional navigation) | ✓ |
| Designer.cs speglar snapshot | ✓ |
| Configuration: HasColumnName, HasMaxLength(20), IsRequired(false) | ✓ |
| Skrivväg garanterar NOT NULL efter 1b | ✓ |
| Up/down bevisat mot Testcontainers (app-roll) | ✓ |
| Bakåtkompatibel läsning bevisad (all-NULL → null, ej krasch) | ✓ |
| Deploy-path / Phase A-privilegier opåverkade | ✓ |
| GDPR-tabell | ✓ (plaintext by design, ärver förälderns audit/soft-delete) |
| Erasure-cascade-registret uppdaterat och korrekt | ✓ |
| Single-owner hotspot (inga konkurrerande migrationer) | ✓ |

Inga blocking eller non-blocking findings. Migrationen är redo för `agents-done` ur db-migration-writer-perspektiv.

---

## Session's note (2026-09-17)

Reviewed head `402fe30a`; commit `41aa7def` (the Playwright register helper and the registration-gate
runbook probe gain `acceptTerms`) landed after this verdict and touches no migration, configuration or
Domain file. The agent's line references were spot-checked by the session against the worktree.

---

## Scoped re-check (2026-09-17) — fix delta `41aa7def..c2c355d2`, report-only, same agent

**Verdict:** APPROVE stands for head `c2c355d2`. 0 blocking, 0 non-blocking in the delta. Transcribed
verbatim from the agent's reply.

### Scoped re-check: c2c355d2 (re-scaffold after security-auditor Minor 2, PR #1751)

**Verdikt: APPROVE står kvar för head c2c355d2.** Denna omkontroll är report-only och skopad till fix-deltat (`41aa7def..c2c355d2`, endast `src/Jobbliggaren.Infrastructure` + de fyra berörda testfilerna) — ingen ny granskning av rader utanför deltat.

#### Re-scaffoldad migration

Gammal migration (`20260917141433_...`, .cs + .Designer.cs) borttagen — bekräftat, `find` hittar inget spår. Ny migration `20260917153605_AddTermsAcceptanceToJobSeeker.cs`:

- **Up:** samma tre nullable `AddColumn` (oförändrade typer/maxlength/nullability) + `AddCheckConstraint(name: "ck_job_seekers_terms_all_or_none", table: "job_seekers", sql: "num_nonnulls(terms_accepted_at, terms_version, privacy_policy_version) IN (0, 3)")` sist — korrekt ordning: kolumnerna måste finnas innan constraint:en refererar dem.
- **Down:** `DropCheckConstraint` **före** de tre `DropColumn` — rätt ordning (explicit, även om Postgres skulle cascade-droppa constraint:en automatiskt när en refererad kolumn droppas; den explicita ordningen är den konventionella EF-scaffoldade formen och gör Down självdokumenterande).

#### Constraint-granskning

- **Namn:** `ck_job_seekers_terms_all_or_none` — följer `ck_<table>_<rule>` exakt.
- **`num_nonnulls`** är core PostgreSQL (9.6+, finns i PG 18) — inget Npgsql-specifikt.
- **Uttryckt via Fluent API, inte rå SQL-migration:** `JobSeekerConfiguration.cs` använder `builder.ToTable("job_seekers", t => t.HasCheckConstraint(name, sql))`, samma form som `TaxonomySnapshotMetaConfiguration.cs:14` (`ck_taxonomy_snapshot_meta_singleton`). Snapshot-diffen (`b.ToTable("job_seekers", null, t => { t.HasCheckConstraint(...); })`) speglar exakt samma mönster som `taxonomy_snapshot_meta`s snapshot-block (rad 1543-1546) — verifierad parity, inte bara ett påstått mönster. Detta är rätt väg: ett CHECK-villkor är per definition ett SQL-booleskt uttryck, och `HasCheckConstraint` ÄR Fluent API-ytan för det — det är inte `migrationBuilder.Sql(...)`-genvägen mitt uppdrag varnar för.
- **`ADD CONSTRAINT` validerar befintliga rader:** eftersom kolumnerna läggs till NULL för alla befintliga rader (ingen backfill, inget `defaultValue`) blir `num_nonnulls(...) = 0` för varje förmigrationsrad, vilket uppfyller `IN (0, 3)` — valideringen kan aldrig fela mot en befintlig rad så länge kolumnerna är nullable och obackfyllda, vilket de är.

#### Testerna (läst, ej körda enligt instruktion)

**`AddTermsAcceptanceToJobSeekerMigrationTests.cs`** — journey head → previous → head på egen Testcontainer, som app-rollen. Nytt jämfört med förra rundan: (1) läser `pg_constraint` (`contype='c'`) vid varje stopp och verifierar constraint:en finns vid head, är borta efter rollback; (2) **sätter in en förmigrationsrad** (`INSERT INTO job_seekers (id, user_id, display_name, preferences, created_at) ...`) medan databasen står vid `PreviousMigration`, kör sedan forward-migrationen på den populerade tabellen, och läser sedan explicit att radens tre kolumner kom ut NULL (`ReadTermsColumnsAreNullAsync` → `(true, true, true)`). Kommentaren i testet (rad 213-217) namnger exakt vad det fångar: en `defaultValue` på `terms_version` skulle lämna `is_nullable = YES` (klarar alla andra kontroller) men denna EFFEKT-läsning skulle fånga den — rimligt konstruerad mutationsresistens, konsistent med det rapporterade 1/1.

**`TermsAcceptanceBackcompatTests.cs`** — ny `PartiallyNulledRow_IsRefusedByTheAllOrNothingConstraint`: seedar en stämplad rad, nollar bara `terms_version` via rå SQL, förväntar `PostgresException` med `SqlState == PostgresErrorCodes.CheckViolation` (23514) och `ConstraintName == "ck_job_seekers_terms_all_or_none"` — exakt målsökt mot constraint:en, inte en generisk exception-fångst.

**Värt att notera (ingen finding, en observation om disciplin):** klassens doc-kommentar korrigerades ärligt i denna commit — föregående runda påstod att `Navigation(...).IsRequired(false)` var det enda som gav `null`-läsningen ("and nothing else asserts it"); den nya texten (rad 16-19) erkänner att en mutation visade att EF läser en all-null owned-rad som `null` **med eller utan** den flaggan, och skriver om påståendet till vad som faktiskt bär vikten (de nullable kolumnerna + `is_nullable`-läsningarna i migrationstestet). Det är en självrättelse av ett tidigare överdrivet påstående, inte ett nytt fynd — men värt att lyfta som bevis på att mutationsclaimet i uppdraget är substantierat, inte bara transkriberat.

`MalformedJsonbSeedTestBase.cs` fick bara en kommentarsrad förklarande varför basklassen återanvänds för icke-jsonb-kolumner — ingen beteendeändring. `JobSeekerTests.cs` och `RegisterConfirmationTests.cs` ligger utanför migrationsskopet (aggregatets publika yta respektive #714-oraklet för `acceptTerms=false`) men motsäger inget av ovanstående.

#### Hotspot / bas

Ingen annan migration har landat på `origin/main` sedan `41aa7def` (fetch + `git log 41aa7def..origin/main -- .../Migrations .../Identity/Migrations` tomt). Ingen kollision.

#### Sammanfattning

| Kontroll | Status |
|---|---|
| Constraint-namn `ck_<table>_<rule>` | ✓ |
| `num_nonnulls` core PG, uttryckt via Fluent API (ej rå SQL-migration) | ✓ |
| ADD CONSTRAINT validerar befintliga (NULL×3) rader utan att kunna fela | ✓ |
| Down-ordning: constraint droppas före kolumnerna | ✓ |
| Snapshot-parity med `TaxonomySnapshotMetaConfiguration`-mönstret | ✓ (verifierad, inte antagen) |
| Migrationstest: pg_constraint-läsning + populerad-tabell-scenario + mutationsresistent effekt-läsning | ✓ |
| Backcompat-test: CheckViolation + rätt constraint-namn | ✓ |
| Ingen konkurrerande migration på main | ✓ |

Inga blocking eller non-blocking findings i detta deltat. APPROVE står kvar för c2c355d2.
