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
