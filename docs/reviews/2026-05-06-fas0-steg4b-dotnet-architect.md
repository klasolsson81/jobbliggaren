# Architect Advisory: AuthProvider Enum Design (STEG 4b)

**Date:** 2026-05-06
**Role:** Advisory only — no code changes
**Requested by:** Turn 2 of STEG 4b

---

## Q1 Recommendation: Enum storage format

**Rekommendation: lagra `AuthProvider` som `string` (max 20 tecken), inte som `int`.**

### Resonemang

1. **Konsistens med befintlig pattern.** Alla "enum-liknande" typer i denna kodbas
   (`JobAdStatus`, `JobSource`) lagras som `string` via `HasConversion`. Att
   införa en avvikande pattern (int) bara för att `AuthProvider` är en simpel
   C# `enum` skapar kognitiv dissonans i schemat — DBA:n / supportingenjören som
   öppnar `pg_dump` ska kunna se `provider = 'Google'`, inte `provider = 1`.

2. **Migrationssäkerhet.** ADR 0017 lägger grunden för OAuth som inte är
   implementerat ännu. Sannolikheten att enum-värden läggs till, omordnas eller
   byter namn under Fas 1–2 är hög (LinkedIn kräver kanske splittning till
   `LinkedInPersonal` / `LinkedInBusiness`, eller `Microsoft` läggs till). Med
   `int`-lagring binder du dig vid den ordinala ordningen — en oavsiktlig
   omordning av enum-medlemmar i C# blir ett tyst dataintegritetsfel. Med
   `string` är symbol-namnet sanningen och refactoring i C# är synligt i diff:en.

3. **Storlek är försumbar.** `provider` får max ~10 unika värden. Postgres
   `varchar(20)` med så låg kardinalitet komprimerar effektivt och är inte en
   reell perf-fråga. Indexeringen sker ändå på det partiella unika indexet
   `(provider, provider_user_id)`.

4. **Default-värdet blir läsbart.** `HasDefaultValue("Local")` i SQL är
   självdokumenterande på ett sätt `HasDefaultValue(0)` aldrig blir.

5. **CLAUDE.md §5.1 — "Magic strings" gäller user-facing strängar och
   business-logik, inte enum-serialisering.** En `string`-mappad enum bryter
   inte mot anti-patternet eftersom C#-enumen är källan till sanning;
   string-representationen är enbart en serialiserad form.

### Konkret EF Core-mappning

```csharp
builder.Property(u => u.Provider)
    .HasConversion<string>()       // EF Core inbyggd enum-till-string-converter
    .HasMaxLength(20)
    .HasDefaultValue(AuthProvider.Local)
    .IsRequired();
```

`HasConversion<string>()` är en built-in EF Core converter som använder
`Enum.ToString()` / `Enum.Parse` — ingen lambda behövs. Detta är **enklare** än
det `JobAdStatus`-mönstret (som behöver lambda eftersom `JobAdStatus` är en
SmartEnum-record, inte en C# enum).

### Etablera pattern

Detta är första standard C# `enum` som mappas mot DB i kodbasen. Eftersom du
etablerar pattern: lägg en kort kommentar i `ApplicationUserConfiguration.cs`:

```csharp
// JobbPilot-konvention: standard C# enums lagras som string i DB.
// Ger läsbar data i pg_dump och migrationssäkerhet vid enum-refactoring.
// SmartEnum-records (JobAdStatus, JobSource) använder lambda-conversion separat.
```

---

## Q2 Recommendation: Layer placement

**Rekommendation: `JobbPilot.Infrastructure.Identity` (samma assembly som
`ApplicationUser`).**

### Resonemang

1. **`AuthProvider` är en Identity/Infrastructure-koncept, inte ett
   domänkoncept.** Det är ett discriminator-värde för den lokala `IdentityUser`
   — inte en del av JobbPilot:s affärsdomän. Bounded context här är "auth",
   inte "applications" eller "job seekers".

2. **Domain-projektet ska vara fritt från Infrastructure-leakage.** Att lägga
   `AuthProvider` i `JobbPilot.Domain.Auth` skulle kräva att Domain-projektet
   exponerar en typ vars **enda syfte** är att discriminatera en
   Infrastructure-entity (`ApplicationUser` lever i Infrastructure per ADR
   0013). Det är en riktnings-violation: Domain ska inte ha begrepp som finns
   till för Infrastructure.

3. **Application-handlers behöver det inte.** JobbPilot:s Domain-aggregates
   (Application, JobAd, JobSeeker, Resume) bryr sig **inte** om hur en användare
   autentiserade sig. Auth-flödet (login, OAuth-callback, password-policy) är
   helt och hållet Infrastructure + Api-composition. Om en framtida Application
   command behöver veta provider (osannolikt — auth-policies hör hemma i Api/
   middleware) går det att exponera via en auth-specifik abstraktion utan att
   dra in enumen.

4. **YAGNI på cross-layer access.** Frågan ställer "om handlers ever need to
   check the provider type". Om/när det behovet uppstår är rätt åtgärd att
   skapa en Application-interface (t.ex. `ICurrentUserAuthInfo`) som
   exponerar **booleska egenskaper** (`IsLocalAuth`, `IsExternalProvider`) —
   inte att läcka enumen uppåt. Detta håller auth-detaljen i Infrastructure
   och ger Application en stabil, syntetisk view.

5. **Praktisk konsekvens.** EF Core-konfigurationen
   (`ApplicationUserConfiguration`) ligger i samma mapp. Att ha
   `AuthProvider`-enumen i `JobbPilot.Infrastructure.Identity` (eller
   `JobbPilot.Infrastructure.Identity.Auth` om du vill ha sub-namespace)
   håller alla auth-relaterade typer co-lokaliserade — `ApplicationUser`,
   `RefreshToken`, `AuthProvider`, konfigurationer.

### Föreslagen filplacering

```
src/JobbPilot.Infrastructure/
  Identity/
    ApplicationUser.cs           (utökad med Provider, ProviderUserId)
    AppIdentityDbContext.cs
    AuthProvider.cs              (NY — public enum, JobbPilot.Infrastructure.Identity-namespace)
    Configurations/
      ApplicationUserConfiguration.cs   (NY)
      RefreshTokenConfiguration.cs
```

### Avvikande scenario som skulle ändra rekommendationen

Om Klas senare bestämmer att en användares **provider** ska raisa en domain
event (t.ex. `UserAuthenticatedViaExternalProvider` som triggar något i
Application-lagret) — då blir Application-placering motiverad. Idag finns
inget sådant flöde. Håll det enkelt: Infrastructure.

---

## Q3 Recommendation: Partial unique index idiom

**Rekommendation: `.HasFilter("\"provider_user_id\" IS NOT NULL")` är rätt EF
Core 10-API. Använd kolumnnamnet `provider_user_id` (snake_case) eftersom
`UseSnakeCaseNamingConvention()` är aktiv på `AppIdentityDbContext` (commit
`286ff64`, session 9).**

### Verifierade fakta

1. **EF Core 10 / Npgsql-stöd:** `.HasFilter(string sql)` är rätt fluent API
   för partial index på Postgres. Det är en passthrough — strängen blir
   `WHERE`-klausulen på `CREATE UNIQUE INDEX`. Ingen typad fluent-variant
   finns; så har det varit sedan EF Core 2.x och det är fortfarande idiomet i
   EF Core 10.

2. **Kolumnnamn med snake_case:** Verifierat genom existerande migration
   `20260506091354_InitialIdentity.cs` — alla `ApplicationUser`-kolumner är
   redan snake_case (`user_name`, `email_confirmed`, `password_hash` etc.).
   `ProviderUserId` blir alltså `provider_user_id` automatiskt.

3. **Citationstecken i filter-uttrycket:** Postgres är case-folding (lowercase
   som default för icke-citerade identifierare). Eftersom `provider_user_id`
   redan är all lowercase fungerar det **även utan** citationstecken. Men:
   citera ändå för robusthet — skulle EF Core eller en framtida konvention
   ändra kolumnnamnet till mixed case bryts filtret tyst utan citat.

### Konkret rekommenderad fluent-kod

```csharp
// src/JobbPilot.Infrastructure/Identity/Configurations/ApplicationUserConfiguration.cs
internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.Provider)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(AuthProvider.Local)
            .IsRequired();

        builder.Property(u => u.ProviderUserId)
            .HasMaxLength(255);   // OAuth subject-claim kan vara långt; 255 är säkert tak

        builder.HasIndex(u => new { u.Provider, u.ProviderUserId })
            .IsUnique()
            .HasFilter("\"provider_user_id\" IS NOT NULL")
            .HasDatabaseName("ix_asp_net_users_provider_provider_user_id");
    }
}
```

### Övervägda detaljer

- **Explicit `.HasDatabaseName(...)`** — det auto-genererade namnet blir
  rimligt med snake_case-konvention, men EF Core:s default kan bli långt och
  trunkera. Var explicit. Konventionsformat: `ix_<table>_<col1>_<col2>` enligt
  existerande `ix_refresh_tokens_token_hash`.

- **`ProviderUserId` är nullable** — det är **förutsättningen** för ett partial
  index. Lokala användare har `Provider = 'Local'` och `ProviderUserId = NULL`,
  och kan vara hur många som helst utan att kollidera. Endast externa
  provider-rader ingår i unique-constraint-kontrollen.

- **Schema och tabellnamn** — `ApplicationUser`-tabellen heter `AspNetUsers`
  i schemat `identity` (Identity:s default, inte snake_case-konverterat —
  verifierat i existerande migration). EF Core hanterar schema-prefix i
  filter-uttrycket automatiskt; du behöver inte skriva
  `"identity"."AspNetUsers"."provider_user_id"` i filtret. Bara kolumnnamnet.

- **Ingen `.HasFilter(u => u.ProviderUserId != null)`-overload** — det finns
  ingen Linq-baserad partial-index-API i EF Core 10. Strängen är enda vägen.

### Concerns / fallgropar

1. **Default-värde-backfill:** `HasDefaultValue(AuthProvider.Local)` sätter
   default på kolumnen i Postgres (`DEFAULT 'Local'`). Existerande rader får
   detta värde när migrationen körs (Postgres `ALTER TABLE ADD COLUMN ... NOT
   NULL DEFAULT ...` är en rewrite-fri operation i Postgres 11+ när default är
   konstant). Detta är acceptabelt för dev/staging där tabellen är tom eller
   liten. För prod (Fas 1+) ska Klas validera mot rad-räkning innan migrationen
   körs — se db-migration-writer:s checklista.

2. **Fast värde-lås på enum-namnet:** Eftersom default är strängen `'Local'` i
   Postgres, kommer ett rename av `AuthProvider.Local` → `AuthProvider.Internal`
   i C# bryta default-värdet. Hantera det som en explicit migration-pair
   (rename + UPDATE). Detta är ett medvetet val från Q1 — string-lagring
   gör problem **synliga** istället för **tysta**.

3. **Index-namnslängd:** `ix_asp_net_users_provider_provider_user_id` är 45
   tecken — väl under Postgres 63-tecken-gränsen.

4. **Kolumn-ordning i compound-indexet:** `(Provider, ProviderUserId)` är rätt
   ordning eftersom: (a) lookups görs typiskt med Provider känd ("hitta
   Google-user med subject X"), (b) ger även en användbar prefix-index på
   Provider ensam för analytics ("hur många Local-users finns?").

---

## Deviations from proposal

Tre justeringar mot det ursprungliga förslaget:

1. **Storage-format ändras från int (default) till string.** Förslaget
   specificerade explicit int-värden (0–3) men sa inget om DB-kolumntyp.
   Default EF Core-mappning för `enum` är `int`. Rekommendation: explicit
   `HasConversion<string>()`. Behåll de explicita int-värdena i C#-enumen ändå
   — de ger framtida flexibilitet om någon vill växla till int senare och de
   gör enumen explicit (anti-pattern att förlita sig på implicit ordinal).

2. **`ProviderUserId` behöver explicit `HasMaxLength(255)`.** Förslaget
   specificerade inte längd. EF Core:s default för `string` på Postgres är
   `text` (obegränsat). OAuth-subjects är typiskt <100 tecken; sätt 255 som
   säkerhetsmarginal och få en `varchar(255)`-kolumn istället för `text`.
   Detta är konsistent med Identity:s egna kolumner (256 för username/email).

3. **Index-namnet ska vara explicit.** Förslaget sa `.HasIndex(...).IsUnique()
   .HasFilter(...)` utan `.HasDatabaseName(...)`. Lägg till för förutsägbart
   namn i migrationen.

Inget i förslaget är **fel** — dessa är förfining, inte riktningsändring.

---

## Implementation notes for db-migration-writer

Sammanfattning av nedanstående för db-migration-writer:s checklista. Detta är
inte instruktioner — det är fakta agenten bör verifiera innan migrationen
genereras.

### Bekräftade fakta att lita på

- `AppIdentityDbContext` har `UseSnakeCaseNamingConvention()` aktiv (commit
  `286ff64`). Migrationen kommer generera `provider` och `provider_user_id`
  som kolumnnamn — verifiera detta i den genererade `Up()`-metoden.
- Migrationsmappen är `src/JobbPilot.Infrastructure/Identity/Migrations/`
  (samma som `20260506091354_InitialIdentity.cs`). Inte
  `src/JobbPilot.Infrastructure/Migrations/` (som tillhör `AppDbContext`).
- Migration-namnet enligt task: `AddAuthProviderToUser`.
- Migration-history-tabell: `__EFMigrationsHistory` i schemat `identity`
  (separat från domän-history-tabellen).

### Kommando för design-time-faktoriering

```bash
dotnet ef migrations add AddAuthProviderToUser \
  --project src/JobbPilot.Infrastructure \
  --startup-project src/JobbPilot.Api \
  --context AppIdentityDbContext \
  --output-dir Identity/Migrations
```

(Verifiera exakta argument mot `DesignTimeIdentityDbContextFactory.cs` —
agenten kan ha en mer precis pattern från session 9.)

### Vad migrationen ska innehålla (förväntat)

`Up()`:
1. `AddColumn provider` — `varchar(20)`, `NOT NULL`, default `'Local'`
2. `AddColumn provider_user_id` — `varchar(255)`, nullable
3. `CreateIndex ix_asp_net_users_provider_provider_user_id` — unique, filter
   `"provider_user_id" IS NOT NULL`

`Down()`:
1. Drop index
2. Drop kolumn `provider_user_id`
3. Drop kolumn `provider`

### Smoke-test efter migration

- `dotnet ef migrations script` mot dev-DB — granska SQL för:
  - `ALTER TABLE identity."AspNetUsers" ADD COLUMN provider varchar(20) NOT NULL DEFAULT 'Local'`
  - `ALTER TABLE identity."AspNetUsers" ADD COLUMN provider_user_id varchar(255)`
  - `CREATE UNIQUE INDEX ix_asp_net_users_provider_provider_user_id ON identity."AspNetUsers" (provider, provider_user_id) WHERE "provider_user_id" IS NOT NULL`
- Kör migration mot lokal Postgres (Docker-compose).
- Verifiera att existerande rader (om några) får `provider = 'Local'`,
  `provider_user_id = NULL` automatiskt via DEFAULT.
- Insert-test: två rader med `provider = 'Local'` och `provider_user_id = NULL`
  ska samexistera (filter excluderar dem).
- Insert-test: två rader med `provider = 'Google'` och samma `provider_user_id`
  ska violera unique-constraint.

### Filer som ska skapas/ändras

| Fil | Action |
|---|---|
| `src/JobbPilot.Infrastructure/Identity/AuthProvider.cs` | NY — public enum |
| `src/JobbPilot.Infrastructure/Identity/ApplicationUser.cs` | EDIT — lägg till `Provider` och `ProviderUserId` properties |
| `src/JobbPilot.Infrastructure/Identity/Configurations/ApplicationUserConfiguration.cs` | NY — IEntityTypeConfiguration |
| `src/JobbPilot.Infrastructure/Identity/Migrations/<timestamp>_AddAuthProviderToUser.cs` | NY — auto-genererad |
| `src/JobbPilot.Infrastructure/Identity/Migrations/<timestamp>_AddAuthProviderToUser.Designer.cs` | NY — auto-genererad |
| `src/JobbPilot.Infrastructure/Identity/Migrations/AppIdentityDbContextModelSnapshot.cs` | EDIT — auto-uppdaterad |

### En öppen fråga som inte är min att avgöra

`ApplicationUser`-properties — `public AuthProvider Provider { get; set; }`
har **public setter**, vilket bryter mot CLAUDE.md §2.2 ("Ingen public setter
på entity-properties utom där EF Core *tvingar* det"). Identity:s egna
kolumner i `IdentityUser<Guid>` har dock public setters (det är ramverkets
val). Två alternativ för db-migration-writer / Klas:

- (a) Följ Identity:s pattern: `public AuthProvider Provider { get; set; }` —
  konsistent med `IdentityUser`, men formellt anti-pattern.
- (b) Skydda med private setter: `public AuthProvider Provider { get;
  private set; } = AuthProvider.Local;` plus en metod
  `SetProvider(AuthProvider, string?)`. Mer ortodox DDD, men bryter konvention
  med övriga `IdentityUser`-properties.

Min preferens: **(a)** — `ApplicationUser` är inte ett aggregate root i
JobbPilot:s domän, det är en Identity-framework-entity. CLAUDE.md §2.2 gäller
domain-aggregates. Public setter är acceptabelt här. Men Klas bör
explicit-godkänna så det dokumenteras varför vi avviker.

---

**End of advisory.** Inga kodändringar gjorda. db-migration-writer-agenten kan
nu köras med dessa rekommendationer som inputs.
