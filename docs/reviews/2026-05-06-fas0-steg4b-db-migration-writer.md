# DB Migration Report: AddAuthProviderToUser (STEG 4b)

**Date:** 2026-05-06
**Migration:** AddAuthProviderToUser
**Context:** AppIdentityDbContext

## Files changed

Created:
- `src/JobbPilot.Infrastructure/Identity/AuthProvider.cs`
- `src/JobbPilot.Infrastructure/Identity/Configurations/ApplicationUserConfiguration.cs`
- `src/JobbPilot.Infrastructure/Identity/Migrations/20260506160036_AddAuthProviderToUser.cs`
- `src/JobbPilot.Infrastructure/Identity/Migrations/20260506160036_AddAuthProviderToUser.Designer.cs`
- `tests/JobbPilot.Api.IntegrationTests/Auth/AuthProviderDefaultsTests.cs`

Edited:
- `src/JobbPilot.Infrastructure/Identity/ApplicationUser.cs` — added Provider + ProviderUserId properties with comment
- `src/JobbPilot.Infrastructure/Identity/Migrations/AppIdentityDbContextModelSnapshot.cs` — auto-updated by dotnet ef
- `src/JobbPilot.Infrastructure/Identity/Migrations/20260506160036_AddAuthProviderToUser.cs` — CA1861 fix (see STOPP events)

## Migration Up() (verbatim)

```csharp
private static readonly string[] ProviderIndexColumns = ["provider", "provider_user_id"];

protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "provider",
        schema: "identity",
        table: "AspNetUsers",
        type: "character varying(20)",
        maxLength: 20,
        nullable: false,
        defaultValue: "Local");

    migrationBuilder.AddColumn<string>(
        name: "provider_user_id",
        schema: "identity",
        table: "AspNetUsers",
        type: "character varying(255)",
        maxLength: 255,
        nullable: true);

    migrationBuilder.CreateIndex(
        name: "ix_asp_net_users_provider_provider_user_id",
        schema: "identity",
        table: "AspNetUsers",
        columns: ProviderIndexColumns,
        unique: true,
        filter: "\"provider_user_id\" IS NOT NULL");
}
```

## Migration Down() (verbatim)

```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(
        name: "ix_asp_net_users_provider_provider_user_id",
        schema: "identity",
        table: "AspNetUsers");

    migrationBuilder.DropColumn(
        name: "provider",
        schema: "identity",
        table: "AspNetUsers");

    migrationBuilder.DropColumn(
        name: "provider_user_id",
        schema: "identity",
        table: "AspNetUsers");
}
```

## Test results

- Before: 79 passing
- After: 80 passing
- New test: `POST_register_new_user_has_Provider_Local_and_null_ProviderUserId`

## Deviations from implementation plan

None regarding the schema design or EF Core configuration. The CA1861 fix (see STOPP events) is a minor
modification to the auto-generated migration file, not a deviation from the architect advisory.

## STOPP events

**CA1861 build error in generated migration file.**

After generating the migration, `dotnet test` triggered a build that treated CA1861
("Prefer 'static readonly' fields over constant array arguments") as an error on the
auto-generated `columns: new[] { "provider", "provider_user_id" }` expression in `CreateIndex`.

Resolution: the `new[]` literal was replaced with a `private static readonly string[]` field
(`ProviderIndexColumns`) in the migration class. The schema content of Up() and Down() is
unchanged — only the array allocation pattern changed to satisfy the analyzer. This is a
mechanical fix consistent with the project's analyzer settings and does not constitute a
deviation from the implementation plan.
