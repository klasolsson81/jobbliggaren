# ADR 0013 — Separat AppIdentityDbContext för Identity-tabeller

**Datum:** 2026-04-19
**Status:** Accepted
**Kontext:** STEG 3 — Auth-stack
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0001, ADR 0009, ADR 0012

## Kontext

ASP.NET Core Identity kräver ett DbContext som ärver `IdentityDbContext<TUser>`. Valet är att antingen utöka befintlig `AppDbContext` eller skapa ett separat kontext.

Befintlig `AppDbContext` (ADR 0009) körs med `UseSnakeCaseNamingConvention()` och äger domänentiteterna i Postgres-schemat `public`. Identity-tabellerna (AspNetUsers, AspNetRoles etc.) har egna EF Core-mappningskonventioner med PascalCase-kolumnnamn. De två konventionerna är oförenliga i samma DbContext utan manuell override av varje Identity-kolumnnamn.

## Beslut

Separat `AppIdentityDbContext` i `JobbPilot.Infrastructure/Identity/`. Äger Identity-tabellerna i Postgres-schemat `identity` (schemaseparation inom samma databas). `AppDbContext` (befintlig) kör i default-schemat (`public`) och äger domändata. Båda kontexterna pekar på samma Postgres-databas men har egna migrations-historiker i separata migrations-mappar.

Cross-context reference: `JobSeeker`-entiteten har en `user_id`-kolumn (Guid, FK-värde) som pekar på `identity.AspNetUsers`. Ingen EF Core navigation property, ingen FK-constraint i databasen. Referential integrity upprätthålls av Application-lagret (register-flödet skapar User + JobSeeker atomiskt via en saga/unit-of-work).

## Konsekvenser

**Positivt:**

- Ren ansvarsuppdelning: Identity-uppgraderingar (ny ASP.NET-version) påverkar inte domänmigrations-historiken
- Schemaseparation (`identity` vs `public`) ger visuell klarhet i psql/pgAdmin
- Isolering gör det enklare att byta auth-stack i framtiden — swap bara `identity`-schemat och `AppIdentityDbContext`
- `UseSnakeCaseNamingConvention()` i `AppDbContext` påverkar inte Identity-mappningarna

**Negativt:**

- Två `dotnet ef migrations add`-kommandon och två `DesignTimeDbContextFactory`-implementationer att hålla reda på
- Att skapa User + JobSeeker atomiskt kräver att båda kontexterna samordnas i Application-lagret (inget EF Core-stöd för cross-context-transaktioner)

**Mitigering:**

- `dotnet ef migrations add --context <AppDbContext|AppIdentityDbContext>` krävs för att undvika "More than one DbContext was found"-fel. Mönstret dokumenteras i `docs/current-work.md` efter Fas B.
- Register-flödet (User + JobSeeker) använder shared `DbConnection` + `UseTransactionAsync` mellan båda contexter — EF Core 10 stödjer detta när båda pekar på samma Postgres-databas. Ingen kompensationslogik behövs. Konkret implementation definieras i Fas D.

## Alternativ övervägda

**Alt 1 — Utöka AppDbContext med IdentityDbContext:** Avfärdat. Blandar PascalCase Identity-konventioner med `snake_case`-konventionen. Gör migrations-historiken svår att läsa (Identity-kolumner blandas med domänkolumner). Kopplar domändata tight till Identity-versionsuppdateringar.

**Alt 2 — Separat databas för Identity:** Avfärdat. Overkill för solo-dev-fas. Cross-DB-transaktioner (register = skapa User + JobSeeker atomiskt) kräver distribuerade transaktioner eller Saga-mönster som inte tillför värde i Fas 0–1.

## Implementationsstatus

**Beslutsdatum:** 2026-04-19 (session 8, inför Fas B — Auth-stack)

**Ej implementerat än:** Implementation sker i Fas B. Konkret: skapa `AppIdentityDbContext : IdentityDbContext<ApplicationUser>` i `Infrastructure/Identity/`, konfigurera `defaultSchema: "identity"`, generera initial Identity-migration, skapa `DesignTimeDbContextFactory` för båda kontexterna.

**Nästa steg:** ADR 0014 dokumenterar refresh token-strategi som bygger på `AppIdentityDbContext`.

---

## Update 2026-05-06 — Identity tabellnamn-konvention

**Session:** 9 (2026-05-06)

### Vad som ändrades

`UseSnakeCaseNamingConvention()` aktiverades på `AppIdentityDbContext` i session 9. Kolumnnamn, constraint-namn och indexnamn i identity-schemat är nu snake_case — exempelvis `user_name`, `token_hash`, `pk_asp_net_users`, `ix_refresh_tokens_token_hash`.

### Varför tabellnamnen förblir PascalCase

ASP.NET Core Identity anropar `ToTable("AspNetUsers")`, `ToTable("AspNetRoles")` etc. internt i sin `OnModelCreating`. Dessa explicita `ToTable()`-anrop åsidosätter EF Core:s namnkonvention för tabellnamnen specifikt. Resultatet är ett blandat läge:

- **Tabellnamn:** `AspNetUsers`, `AspNetRoles`, `AspNetRoleClaims`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserRoles`, `AspNetUserTokens` — PascalCase (Microsofts defaults, oförändrade)
- **Kolumnnamn, constraints, index:** snake_case (EF Core-konventionen gäller fullt ut för allt utom tabellnamnen)

### State för refresh_tokens

Vår egna `refresh_tokens`-tabell är fullt snake_case — tabell, kolumner, constraints och index — eftersom vi aldrig anropar `ToTable()` explicit och äger mappningen helt själva. EF Core:s namnkonvention gäller utan undantag.

### Bedömning — varför workaroundern inte är värd det

En workaround finns: manuell `ToTable("asp_net_users")` (och motsvarande) per Identity-entity i `AppIdentityDbContext.OnModelCreating`. Det skulle ge konsekvent snake_case även för tabellnamnen.

Varför vi väljer bort det:

1. **Inget praktiskt värde.** Vi accessar Identity-tabellerna uteslutande via `UserManager<ApplicationUser>` och `SignInManager` — vi skriver aldrig rå SQL mot Identity-tabeller.
2. **Underhållsbörda.** Varje Identity-uppgradering som lägger till nya entiteter eller byter tabellnamn kräver manuell synk av `ToTable()`-anropen.
3. **Migrationsrisk.** Döper vi om tabeller måste befintliga databaser migreras om — ett onödigt riskmoment.

Nuvarande state — PascalCase tabellnamn + snake_case kolumner — är godtagbart och stabilt.
