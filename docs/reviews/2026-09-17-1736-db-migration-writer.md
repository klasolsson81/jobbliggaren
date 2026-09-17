# db-migration-writer — #1736 (epic #1732 part 1b): `AddTermsAcceptanceToJobSeeker`

**Date:** 2026-09-17 · **Agent:** db-migration-writer · **Worktree:** `C:/tmp/jbl-1736`, base
`origin/main` `88b6ea93`, nothing committed at scaffold time · **Input:** the session's brief — the
`TermsAcceptance` owned type (form bound by `dotnet-architect`, `docs/reviews/2026-09-17-1736-architect-form.md`)
mapped in `JobSeekerConfiguration.cs` as `OwnsOne` + `Navigation(...).IsRequired(false)`; scaffold on the
`AppDbContext` history, produce the idempotent SQL, run no database update. Report returned in the reply
and transcribed verbatim by the session.

---

## Migration skapad: AddTermsAcceptanceToJobSeeker

**Kommando kört:**
```
dotnet ef migrations add AddTermsAcceptanceToJobSeeker --project src/Jobbliggaren.Infrastructure --startup-project src/Jobbliggaren.Api --context AppDbContext -o Persistence/Migrations
```
`dotnet ef` fanns redan på PATH (version 11.0.0-preview.3.26207.106) — inget `dotnet tool restore` behövdes.

**Filer:**
- `C:/tmp/jbl-1736/src/Jobbliggaren.Infrastructure/Persistence/Migrations/20260917141433_AddTermsAcceptanceToJobSeeker.cs`
- `C:/tmp/jbl-1736/src/Jobbliggaren.Infrastructure/Persistence/Migrations/20260917141433_AddTermsAcceptanceToJobSeeker.Designer.cs`
- `C:/tmp/jbl-1736/src/Jobbliggaren.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs` (ändrad, +33/-0 rader)

**Typ:** Additive (endast `AddColumn`, alla nullable, ingen `DROP`/typändring/rename)

**Påverkade entiteter:** `JobSeeker` (tabell `job_seekers`) — inget annat table/kolumn rört. `git diff --stat` bekräftar att endast snapshot-filen ändrades utanför de två nya filerna, och `git status --porcelain` visar exakt de tre filerna ovan i migrations-katalogen.

### Schema-ändringar

**Up** — exakt tre `AddColumn` på `job_seekers`, alla `nullable: true`:
```csharp
migrationBuilder.AddColumn<string>(name: "privacy_policy_version", table: "job_seekers",
    type: "character varying(20)", maxLength: 20, nullable: true);
migrationBuilder.AddColumn<DateTimeOffset>(name: "terms_accepted_at", table: "job_seekers",
    type: "timestamp with time zone", nullable: true);
migrationBuilder.AddColumn<string>(name: "terms_version", table: "job_seekers",
    type: "character varying(20)", maxLength: 20, nullable: true);
```

**Down** — exakt tre `DropColumn` på samma tre kolumner (speglar Up).

**Snapshot-diffen** är uteslutande det nya `OwnsOne("...TermsAcceptance", "TermsAcceptance", ...)`-blocket under `JobSeeker`-entiteten: tre properties (`AcceptedAt`→`terms_accepted_at`, `PrivacyPolicyVersion`→`privacy_policy_version`, `TermsVersion`→`terms_version`, båda string-kolumnerna `IsRequired()` + `HasMaxLength(20)` **inom det owned-typen**, vilket är korrekt — det är kolumnernas nullability på DB-nivå som styrs av `Navigation(...).IsRequired(false)`, inte property-nivån inuti VO:n), plus `b.Navigation("TermsAcceptance");` helt utan `.IsRequired()`-anrop — dvs INTE required, till skillnad från grannen `Preferences` som har `.IsRequired()` explicit. Detta matchar den förväntade/obligatoriska formen exakt.

### SQL (idempotent, för granskning)

```
dotnet ef migrations script 20260914134635_AddOccupationDivisionProfile AddTermsAcceptanceToJobSeeker --project src/Jobbliggaren.Infrastructure --startup-project src/Jobbliggaren.Api --context AppDbContext --idempotent
```

```sql
START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917141433_AddTermsAcceptanceToJobSeeker') THEN
    ALTER TABLE job_seekers ADD privacy_policy_version character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917141433_AddTermsAcceptanceToJobSeeker') THEN
    ALTER TABLE job_seekers ADD terms_accepted_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917141433_AddTermsAcceptanceToJobSeeker') THEN
    ALTER TABLE job_seekers ADD terms_version character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260917141433_AddTermsAcceptanceToJobSeeker') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260917141433_AddTermsAcceptanceToJobSeeker', '10.0.10');
    END IF;
END $EF$;
COMMIT;
```

Tre plain `ALTER TABLE ... ADD` — inget `CONCURRENTLY` behövs (en `job_seekers`-rad-lås för en `ADD COLUMN` utan default/`NOT NULL` är momentant, ingen tabell-omskrivning i PG ≥11). En transaktion, historik-guardad, `--idempotent` gör den säker att köra mot valfri startpunkt.

### Build

`dotnet build src/Jobbliggaren.Infrastructure` → **Build succeeded, 0 Warning(s), 0 Error(s)**. Inga integrationssviter körda (sessionens ansvar).

### GDPR-kontroller

- **Soft delete:** N/A för denna migration specifikt — `job_seekers` har redan `deleted_at` + `HasQueryFilter(js => js.DeletedAt == null)` (rad 95/97 i `JobSeekerConfiguration.cs`), och de tre nya kolumnerna lever på samma rad, skyddas av samma filter. Ingen ny soft-delete-mekanik krävs.
- **Audit trail:** `job_seekers` har redan `created_at`/`updated_at` (rad 93–94). De tre nya kolumnerna (`terms_accepted_at`, `terms_version`, `privacy_policy_version`) ÄR själva audit-stämpeln för denna specifika händelse — enligt uppdraget: **`terms_accepted_at` är en Art. 6(1)(b)-avtalsstämpel** (ögonblicket villkoren accepterades, inte ett samtycke i Art. 7-mening — inget withdrawal-flöde finns eller ska finnas, ADR 0142 D6), och **`privacy_policy_version` är en Art. 13-notisversionsstämpel** för Art. 5(2)-ansvarsskyldighet, inte ett accept-faktum i sig (kryssrutan gäller villkoren; policyn är länkad som läst). `terms_version` stämplar samma sak för villkoren. Namngivningen är medvetet INTE `consent_*` av samma skäl. (Observation, inte ett fynd om denna migration: `job_seekers` saknar `created_by`/`updated_by` och en xmin-concurrency-konfiguration sedan tidigare — pre-existing gap utanför detta migrations diff och utanför min Domain/Application-behörighet; nämns för fullständighet, blockerar inte.)
- **Encryption: N/A.** De tre kolumnerna är **plaintext by design** — ingen DEK, inget `IDataKeyProvider`. Innehållet är två versionskonstanter (`TermsAcceptance.CurrentTermsVersion`/`CurrentPrivacyPolicyVersion`, ISO-datum-strängar ur domänen) plus en tidsstämpel — inget användarförfattat, inget känsligt. `HasMaxLength(20)` är därför korrekt här (headroom över ett 10-tecken ISO-datum) — encrypted-kolumn-regeln "aldrig `HasMaxLength`" gäller inte, för dessa kolumner krypteras aldrig.

**NULL-tillståndet:** enligt uppdragsbeskrivningen är NULL i de tre kolumnerna tillståndet för **endast de två redan existerande raderna** (skrivna före denna migration). Jag har inte kört någon query mot databasen (ingen `dotnet ef database update` och ingen annan DB-anslutning tillgänglig i detta uppdrag) — detta återges alltså som en premiss från uppdraget, inte som något jag själv mätt. Varje rad `JobSeeker.Register` skriver **efter** att denna migration är applicerad blir aldrig NULL i praktiken: write-porten vägrar en null-stämpel (domänens `Register`-metod kräver `TermsAcceptance` icke-null). Kolumnerna är alltså `nullable: true` på DB-nivå (obligatoriskt för bakåtkompatibilitet med de två gamla raderna) men effektivt `NOT NULL` för allt skrivet efter 1b, hållet av applikationslagret snarare än en DB-`CHECK`-constraint — konsekvent med att detta INTE är en NOT-NULL-utan-default-destruktiv ändring (de nya kolumnerna är just nullable, så inget §12/destructive-flagg utlöses).

**Inget destruktivt** i denna migration — ingen `⚠ DESTRUCTIVE`-rubrik behövs.

### Nästa steg

- Granska: `dotnet ef migrations script --idempotent` (ovan)
- Applicera dev: `dotnet ef database update` — **körs INTE av mig** (sessionens Testcontainers-sviter applicerar migrationen själva; jag har heller inte rört den delade dev-Postgres på 5435)
- SQL för prod: `dotnet ef migrations script 20260914134635_AddOccupationDivisionProfile AddTermsAcceptanceToJobSeeker -o deploy/AddTermsAcceptanceToJobSeeker.sql` när det blir aktuellt

Rapporten är inte skriven till `docs/reviews/2026-09-17-1736-db-migration-writer.md` av mig — sessionen transkriberar enligt uppdraget.

---

## Session's note (2026-09-17)

The "two pre-existing rows" premise is ADR 0142's and the Art. 30 register's dated reading
(`job_seekers = 2`, measured on the box 2026-08-23), carried here as a premise, not re-measured this
session; the migration's correctness does not depend on the count. The agent's observation that
`job_seekers` carries no `created_by`/`updated_by`/xmin is pre-existing state outside this diff and is
not a finding on it.
