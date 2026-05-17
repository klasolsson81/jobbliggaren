# STOPP 3a — Migration: AddManualPostingToApplications

**Datum:** 2026-05-17
**Roll:** db-migration-writer (CLAUDE.md §9.2 obligatorisk gate för nya migrations)
**Auktoritativ spec:** `docs/reviews/2026-05-17-fas3-stopp3a-architect-design.md` §4 (+ §3 EF-mappning)
**Scope:** EN migration genererad. Inga commits (CC committar i 3a.5). Inga modelländringar.

---

## Genererade filer

- `src/JobbPilot.Infrastructure/Persistence/Migrations/20260517222003_AddManualPostingToApplications.cs`
- `src/JobbPilot.Infrastructure/Persistence/Migrations/20260517222003_AddManualPostingToApplications.Designer.cs`
- `src/JobbPilot.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs` (uppdaterad)

**Filnamn:** `20260517222003_AddManualPostingToApplications` — timestamp-prefix per EF-konvention,
PascalCase action-verb-namn enligt befintlig migrations-namnstandard (arkitektens
föreslagna namn `AddManualPostingToApplications` använt verbatim).

**Genererings-kommando** (etablerad standard, verifierad i `docs/runbooks/aws-rds-migration-apply.md`):

```
dotnet ef migrations add AddManualPostingToApplications \
    --project src/JobbPilot.Infrastructure \
    --startup-project src/JobbPilot.Api \
    --context AppDbContext \
    --output-dir Persistence/Migrations
```

Startup-projekt = `JobbPilot.Api` (verifierat mot runbook, ej gissat).

---

## Verbatim Up-metod

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "manual_company",
        table: "applications",
        type: "character varying(200)",
        maxLength: 200,
        nullable: true);

    migrationBuilder.AddColumn<DateTimeOffset>(
        name: "manual_expires_at",
        table: "applications",
        type: "timestamp with time zone",
        nullable: true);

    migrationBuilder.AddColumn<string>(
        name: "manual_title",
        table: "applications",
        type: "character varying(300)",
        maxLength: 300,
        nullable: true);

    migrationBuilder.AddColumn<string>(
        name: "manual_url",
        table: "applications",
        type: "character varying(2000)",
        maxLength: 2000,
        nullable: true);
}
```

## Verbatim Down-metod

```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(
        name: "manual_company",
        table: "applications");

    migrationBuilder.DropColumn(
        name: "manual_expires_at",
        table: "applications");

    migrationBuilder.DropColumn(
        name: "manual_title",
        table: "applications");

    migrationBuilder.DropColumn(
        name: "manual_url",
        table: "applications");
}
```

---

## Verifiering mot architect §4

| Krav (§4) | Status | Bevis |
|---|---|---|
| Up: `manual_title` varchar(300) NULL | ✓ | rad 27–32, `nullable: true`, ingen default |
| Up: `manual_company` varchar(200) NULL | ✓ | rad 14–19, `nullable: true`, ingen default |
| Up: `manual_url` varchar(2000) NULL | ✓ | rad 34–39, `nullable: true`, ingen default |
| Up: `manual_expires_at` timestamptz NULL | ✓ | rad 21–25, `timestamp with time zone`, `nullable: true` |
| Tabell = `applications` | ✓ | alla 4 `table: "applications"` |
| Ingen default / backfill / NOT NULL / index | ✓ | inga `defaultValue`/`Sql`/`CreateIndex`-anrop i migrationen |
| Down: exakt 4 DROP COLUMN | ✓ | rad 45–59, samma 4 kolumner, inget annat |
| **Exakt 4 kolumner — INGEN `manual_source`** | ✓ | Source struken (architect §4 + Flagga 2; plan §59 supersederar §65). Fyra ADD, fyra DROP. Ingen femte kolumn. |
| Ingen oavsiktlig drift | ✓ | Se snapshot-analys nedan |

EF sorterar `AddColumn`-anropen alfabetiskt (company, expires_at, title, url) —
ren EF-kosmetik, kolumn-mängden är exakt de fyra spec:ade. Ingen funktionell avvikelse.

### Snapshot-drift-kontroll

`git diff` på `AppDbContextModelSnapshot.cs` (42 insertions, 1 deletion):

- **Enda strukturella ändring:** nytt `ManualPosting` owned-entity-block på
  `Application`-entiteten — `Company` (200, IsRequired på CLR-typen),
  `ExpiresAt` (timestamptz), `Title` (300, IsRequired), `Url` (2000),
  mappade till `manual_*`-kolumner, table-sharing via `ApplicationId`-FK
  (kolumn `id`, `fk_applications_applications_id`). Detta är standard
  EF owned-entity table-sharing — **ingen ny tabell, ingen ny DDL-kolumn
  utöver de fyra**.
- **`ProductVersion` 10.0.7 → 10.0.8:** benign EF-tooling-metadata, matchar
  verifierad EF Core 10.0.8 (architect §14). Ingen DDL-påverkan, konsekvent
  med tidigare migrations-beteende.
- **Ingen annan entitet rörd.** Inga kolumn-renames, inga typ-ändringar,
  ingen kolumn borttagen. Migrationen är rent additiv.

`IsRequired()` på `Title`/`Company` i snapshot är CLR-typ-nullabiliteten
(VO-property non-null när VO finns) — kolumnerna är ändå `nullable: true`
i migrationen (architect §3b: optional owned-entity null-semantik via
`Navigation(...).IsRequired(false)`, redan i `ApplicationConfiguration.cs:54`).
Detta är exakt det avsedda mönstret, ingen avvikelse.

---

## Idempotens

EF-genererad migration är idempotent via `__EFMigrationsHistory`-mekanismen
(samma som alla befintliga JobbPilot-migrations). Verifierat via
`dotnet ef migrations script ... --idempotent` (ingen live Postgres i miljön
→ SQL-script-verifiering per uppdrag steg 4, fallback-vägen):

- Varje `ALTER TABLE applications ADD` wrappad i
  `IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260517222003_AddManualPostingToApplications')`.
- Exakt 4 `ADD`-satser (manual_company / manual_expires_at / manual_title / manual_url) — ingen `manual_source`.
- `INSERT INTO "__EFMigrationsHistory"` med samma guard. Allt i en `START TRANSACTION; ... COMMIT;`.
- Applicering mot färsk DB: ren — 4 nullable-kolumner adderas, befintliga rader
  får NULL i alla fyra (= semantiskt "ingen ManualPosting", korrekt; architect §4).

---

## Build-verifiering

`dotnet build src/JobbPilot.Api` med migrationen på plats: **Build succeeded,
0 Warning(s), 0 Error(s)** (före och efter migrations-generering).

---

## Slutsats

Migrationen matchar architect §4-spec **exakt**: 4 nullable kolumner på
`applications`, ingen default/backfill/NOT NULL/index, Down = 4 DROP COLUMN,
**ingen `manual_source`** (Source struken — fyra kolumner, ej fem), ingen
snapshot-drift utöver det avsedda ManualPosting owned-entity-blocket,
idempotent via `__EFMigrationsHistory`. Inga avvikelser → ingen STOPP-eskalering.

Migration + `.Designer.cs` + uppdaterad snapshot committas av CC i SAMMA
atomiska batch (3a.5). db-migration-writer committar ej.
