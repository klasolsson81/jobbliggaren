# Arkitektur-design — STOPP 3a backend-vertikal (FAS 3 /ansokningar manuella ansökningar)

**Datum:** 2026-05-17
**Roll:** dotnet-architect som implementations-design-auktoritet (STOPP 3a.1, plan §1.4-gate INNAN kod)
**Scope:** EN atomisk batch (J3) — Domain `ManualPosting` + invariant / EF-mappning + migration / `CreateApplicationCommand` / 3 read-handlers ManualPosting-fallback / tester
**Status:** Read-only-pass — INGEN kod skriven. Besluts-bärande design nedan; test-writer skriver RÖD svit mot denna, implementation följer exakt.

---

## Sammanfattning

Designen är **entydig** mot principer och tidigare beslut (architect a4c1483aeaee7fcea Variant A, CTO rev2 J1–J3, ADR 0048 Beslut d). **Ingen ny Klas-STOPP krävs för designvalen** — alla är härledda ur redan godkända beslut och kod-verifierade invarianter. Source-strykningen bryter **inget** i designen; den förenklar VO:t (ingen dead axis) och read-projektionen (literal i stället för lagrat fält) — konsekvent med CLAUDE.md §5.1 (ingen YAGNI-axel). **En** punkt kräver Klas-medvetenhet men inte STOPP: EF optional owned-entity null-semantik (§3 nedan) har en konkret fallgrop som måste lösas explicit i mappningen — designen specificerar lösningen, så det är en implementations-instruktion, inte ett öppet beslut.

Kodgrund verifierad mot HEAD (ej gissad): `Application.cs:8-66`, `JobAd.cs:155-189` (TD-80 i `ValidateCore`), `Company.cs:11-21`, `JobSource.cs:11`, `ApplicationConfiguration.cs:1-70`, `JobAdConfiguration.cs:31-65` (owned-type-precedens), `ExternalReference.cs` (optional-VO-precedens), 3 read-handlers, `CreateApplicationCommand/Handler/Validator.cs`, `applications.ts`, ADR 0048, EF Core 10.0.8 + Npgsql 10.0.1 + global `UseSnakeCaseNamingConvention()` (`DependencyInjection.cs:262`).

---

## 1. `ManualPosting` value object — exakt form

**Fil:** `src/JobbPilot.Domain/Applications/ManualPosting.cs` (Applications-aggregatets mapp — VO:t hör till Application, ej JobAds; det är ett Application-ägt value object, ADR 0048 Beslut d).

**Typ:** `public sealed record ManualPosting` (referens-record, ej `record struct`). Motiv: konsekvent med `Company` (`Company.cs:5`) och `ExternalReference` (`ExternalReference.cs:8`) — JobbPilots VO-precedens är `sealed record` class, ej struct. EF owned-entity-mappning (§3) kräver referenstyp; `record struct` owned-types är problematiska i EF Core 10 (value-comparer + nullability). `sealed record` ger value-equality gratis (default record-equality på de fyra propertyna är **korrekt** här — två ManualPosting med samma Title/Company/Url/ExpiresAt ÄR samma värde; ingen custom `Equals` behövs).

**Form (skiss — implementation följer exakt):**

```
namespace JobbPilot.Domain.Applications;

public sealed record ManualPosting
{
    public string Title { get; }
    public string Company { get; }      // non-empty (se validering) — INTE nullable
    public string? Url { get; }         // nullable: manuell ansökan kan sakna URL
    public DateTimeOffset? ExpiresAt { get; }

    private ManualPosting(string title, string company, string? url, DateTimeOffset? expiresAt)
    {
        Title = title;
        Company = company;
        Url = url;
        ExpiresAt = expiresAt;
    }

    public static Result<ManualPosting> Create(
        string? title, string? company, string? url, DateTimeOffset? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(title))
            return Result.Failure<ManualPosting>(
                DomainError.Validation("ManualPosting.TitleRequired", "Jobbtitel är obligatorisk."));
        if (title.Length > 300)
            return Result.Failure<ManualPosting>(
                DomainError.Validation("ManualPosting.TitleTooLong", "Jobbtitel får vara max 300 tecken."));

        if (string.IsNullOrWhiteSpace(company))
            return Result.Failure<ManualPosting>(
                DomainError.Validation("ManualPosting.CompanyRequired", "Företag är obligatoriskt."));
        if (company.Length > 200)
            return Result.Failure<ManualPosting>(
                DomainError.Validation("ManualPosting.CompanyTooLong", "Företag får vara max 200 tecken."));

        string? normalizedUrl = null;
        if (!string.IsNullOrWhiteSpace(url))
        {
            // TD-80 — IDENTISK scheme-whitelist som JobAd.ValidateCore (JobAd.cs:177-183).
            // Återanvänd regeln verbatim — duplicera EJ slarvigt (samma OWASP A01-yta).
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri)
                || (parsedUri.Scheme != Uri.UriSchemeHttp
                    && parsedUri.Scheme != Uri.UriSchemeHttps))
                return Result.Failure<ManualPosting>(
                    DomainError.Validation("ManualPosting.UrlInvalid",
                        "Annonslänk måste vara en giltig http(s)-URL."));
            if (url.Length > 2000)
                return Result.Failure<ManualPosting>(
                    DomainError.Validation("ManualPosting.UrlTooLong",
                        "Annonslänk får vara max 2000 tecken."));
            normalizedUrl = url.Trim();
        }

        return Result.Success(new ManualPosting(title.Trim(), company.Trim(), normalizedUrl, expiresAt));
    }
}
```

**Designbeslut, motiverade:**

- **`Company` är `string` (non-empty), INTE nullable.** Architect-skissen i datamodell-rapporten (`...datamodell-architect.md:69-76`) hade `string? Company`, men plan §1.5 + §7 + CTO rev2 fastställer **Företag obligatoriskt** för manuell ansökan ("Jobbtitel + Företag obligatoriska", plan §166). Non-null + non-empty är rätt invariant. Detta är en skärpning mot skiss-rapportens lösare form, motiverad av plan §7 (bindande).
- **Ingen `Source`-property** (Klas STOPP 3a-villkor, plan §59). En `ManualPosting` ÄR per definition Source=Manual — en property som bara kan ha ett värde är en dead axis (YAGNI; CLAUDE.md §5.1). Read-vägen projicerar literalen `"Manual"` (§5 nedan). **Source-strykningen bryter inget** — den tar bort en redundant axel; equality, mappning och invariant är oberoende av Source.
- **Längd-caps speglar JobAd:** Title ≤300 (`JobAd.cs:165`), Company ≤200 (`Company.cs:16`), Url ≤2000 (`JobAdConfiguration.cs:18`). Konsekvent med befintliga annons-metadata-gränser — ingen ny godtycklig gräns.
- **Ingen `ExpiresAt`-framtidsvalidering.** `JobAd.ValidateCore` validerar `ExpiresAt > PublishedAt` (`JobAd.cs:184`), men ManualPosting har **ingen PublishedAt** (J1 — manuell = ingen publicering). Det finns ingen referenspunkt att validera mot, och "sista ansökningsdag i det förflutna" är ett legitimt tillstånd (användaren registrerar en redan-sökt ansökan retroaktivt). Ingen framtidsvalidering — medvetet, motiverat av J1-semantiken. (Om Klas vill ha "ExpiresAt ≥ idag"-varning är det en frontend-UX-hint, ej domän-invariant — egen framtida touch.)
- **Equality:** record-default (strukturell på alla fyra props) är **korrekt**. Ingen custom `Equals`/`GetHashCode`. `Company` (JobAd-VO) har private ctor + ingen custom equality och fungerar som precedens.
- **Privat ctor + statisk `Create→Result`** — identiskt mönster med `Company` och `ExternalReference`. Validering i factory, aldrig i handler (CLAUDE.md §2.2).

**Flagga:** ingen. Entydigt mot Variant A-beslutet + plan §7 + VO-precedens.

---

## 2. Aggregat-invariant `JobAdId ⊕ ManualPosting` — exakt placering i Application

**Property på `Application` (`Application.cs`, efter rad 12):**

```
public ManualPosting? ManualPosting { get; private set; }
```

`private set` — konsekvent med övriga Application-props (`Application.cs:10-18`); EF sätter via owned-entity-mappning (§3), ej via setter.

**Konstruktor-utökning (`Application.cs:29-44`):** lägg `ManualPosting? manualPosting` som parameter, tilldela `ManualPosting = manualPosting;`. Behåll EF-ctorn `private Application() { }` orörd.

**`Create`-signatur — utökas, EJ ny overload.** Befintlig:

```
public static Result<Application> Create(
    JobSeekerId jobSeekerId, JobAdId? jobAdId, string? coverLetter, IDateTimeProvider clock)
```

→ utökas till (ny parameter **sist före `clock`** så call-site-migrering är minimal och positionell läsbarhet bevaras):

```
public static Result<Application> Create(
    JobSeekerId jobSeekerId,
    JobAdId? jobAdId,
    string? coverLetter,
    ManualPosting? manualPosting,
    IDateTimeProvider clock)
```

**Motiv mot overload:** en overload (`Create(..., clock)` + `Create(..., manualPosting, clock)`) skulle ge **två** vägar in i aggregatet där invarianten måste upprätthållas på båda — invariant-duplikation (DRY/SPOT-brott, samma resonemang som ADR 0048 Beslut c query-filter). En signatur, en invariant-kontroll, en sanningspunkt. Det finns exakt **en** call-site (`CreateApplicationCommandHandler.cs:37`) → migrering är trivial och bryter inget.

**Invariant-check — placeras i `Create` FÖRE `new Application(...)` (efter cover-letter-längdkollen, `Application.cs:56-58`):**

```
if (jobAdId is not null && manualPosting is not null)
    return Result.Failure<Application>(
        DomainError.Validation(
            "Application.JobAdAndManualMutuallyExclusive",
            "En ansökan kan inte vara både kopplad till en annons och manuellt angiven."));
```

**Invariant-semantik (kod-exakt, så test-writer skriver rätt RÖD svit):**

| `jobAdId` | `manualPosting` | Resultat | Tillstånd (plan §7) |
|---|---|---|---|
| satt | null | Success | 1 — JobAd-kopplad |
| null | satt | Success | 2 — manuell |
| null | null | **Success** | 3 — degenererad (befintligt cover-letter-only-beteende, bevaras) |
| satt | satt | **Failure** `Application.JobAdAndManualMutuallyExclusive` | förbjudet motstridigt |

**Kritiskt:** `(null, null)` MÅSTE förbli Success — det är dagens beteende (cover-letter-only-rader) och får inte regressera. Invarianten är **endast** "icke-båda", inte "exakt-en". Detta är direkt ur datamodell-rapporten A3 (`...datamodell-architect.md:97`) och plan §61.

**Call-site-migrering (`CreateApplicationCommandHandler.cs:37`)** — utan att bryta builden: se §4 (handler) + §7 (batch-ordning). Den enda existerande anroparen får `manualPosting`-argument i samma batch.

**`ApplicationCreatedDomainEvent`** (`Application.cs:63-64`): **oförändrad signatur** — eventet bär `(id, jobSeekerId, jobAdId, now)`. ManualPosting läggs **inte** i eventet i denna batch (ingen lyssnare behöver det; YAGNI — om en framtida projektor behöver manuell metadata är det en egen touch med egen event-version-bedömning). Ingen event-konsument finns idag som skulle behöva ManualPosting → orörd.

**Flagga:** ingen. Invariant-placering i `Create` är exakt CLAUDE.md §2.2 + datamodell-rapport A3.

---

## 3. EF-mappning — ManualPosting som optional owned entity

**Fil:** `ApplicationConfiguration.cs` (`Configure`-metoden, lägg efter CoverLetter-mappningen rad 32, före Status rad 34).

**Mappning — `OwnsOne` med explicit `HasColumnName`:**

```
builder.OwnsOne(a => a.ManualPosting, manual =>
{
    manual.Property(m => m.Title)
        .HasColumnName("manual_title")
        .HasMaxLength(300);
    manual.Property(m => m.Company)
        .HasColumnName("manual_company")
        .HasMaxLength(200);
    manual.Property(m => m.Url)
        .HasColumnName("manual_url")
        .HasMaxLength(2000);
    manual.Property(m => m.ExpiresAt)
        .HasColumnName("manual_expires_at");
});

builder.Navigation(a => a.ManualPosting).IsRequired(false);
```

**Kritiska EF-beslut (load-bearing — fallgropen uppdraget pekar på):**

### 3a. Explicit `HasColumnName` krävs trots global snake_case-konvention

Lösningen kör `UseSnakeCaseNamingConvention()` globalt (`DependencyInjection.cs:262`). **Utan** explicit `HasColumnName` skulle owned-entity-kolumnerna heta `manual_posting_title`, `manual_posting_company`, etc. (prefix från navigation-namnet `ManualPosting`). Plan §65 + §1.5 specificerar `manual_title`/`manual_company`/`manual_url`/`manual_expires_at`. Explicit `HasColumnName` på varje property ger exakt de namnen. Detta är **samma mönster** som `External` owned-type (`JobAdConfiguration.cs:52-64` — `external_source`/`external_id` med explicit `HasColumnName` "för konsekvens med övriga kolumner (init-migration)"). Konsekvent precedens.

### 3b. Optional owned-entity null-semantik — `IsRequired(false)` är OBLIGATORISK

**Detta är den enda icke-triviala EF-punkten.** EF Core 10:s default för en owned-entity-referens är **required** (motsvarar `IsRequired()`). Med default-required och alla kolumner nullable uppstår EF:s **optional-dependent-with-all-nullable-properties-varning/fel**: EF kan inte avgöra om en rad "har en ManualPosting med alla null-värden" eller "har ingen ManualPosting" eftersom det inte finns någon non-nullable required property som diskriminator.

`ExternalReference`-precedensen (`JobAdConfiguration.cs:52`) "kommer undan" med detta utan explicit `IsRequired(false)` därför att `ExternalReference` i praktiken alltid har non-null `Source`+`ExternalId` när den finns OCH koden aldrig materialiserar en all-null External (Import sätter alltid båda). ManualPosting har samma struktur men vi vill **explicit** och defensivt deklarera optionaliteten.

**Lösning (specificerad, ej öppen):** `builder.Navigation(a => a.ManualPosting).IsRequired(false);` efter `OwnsOne`. Detta säger EF: navigeringen är optional. EF Core 10:s null-semantik för optional owned-entity: **om alla mappade kolumner är NULL → navigeringen materialiseras som `null`** (EF behandlar "alla dependent-kolumner NULL" som "ingen dependent"). Eftersom `Title` och `Company` är non-null på CLR-typen men kolumnerna görs nullable av migrationen (§4), och `ManualPosting.Create` garanterar att Title/Company aldrig är tomma **när VO:t finns**, blir semantiken entydig:

- **JobAd-kopplad eller degenererad rad:** alla fyra `manual_*`-kolumner NULL → `app.ManualPosting == null`. Korrekt.
- **Manuell rad:** `manual_title` + `manual_company` NOT NULL (Create-garanti) → `app.ManualPosting` materialiseras. Korrekt.

**Verifierings-krav för STOPP 3a-rapporten:** ett test (test-writer) som persisterar en `Application` UTAN ManualPosting, läser tillbaka via `db.Applications`, och asserterar `app.ManualPosting is null` (ej en all-null ManualPosting-instans). Detta bevisar att optional-owned-semantiken faller ut rätt och är regressionsskydd mot EF-version-drift. **Detta test är BLOCKING i RÖD-sviten** — det är den enda punkten där EF-beteende är subtilt.

### 3c. Query-filter — ingen ändring

`ApplicationConfiguration.cs:66` har `HasQueryFilter(a => a.DeletedAt == null)`. Owned-entity ärver host-entityns query-filter automatiskt (EF Core — owned types har ingen egen query-filter-yta). Ingen ändring. ManualPosting följer Application:s soft-delete (korrekt — manuell metadata ska försvinna när ansökan soft-deletas).

**Flagga:** §3b är en implementations-instruktion (ej öppet beslut) — designen specificerar `IsRequired(false)` + BLOCKING round-trip-test. Klas-medvetenhet räcker; ingen STOPP. Om round-trip-testet RÖTT inte kan göras GRÖNT med `IsRequired(false)` (osannolikt, men EF-version-risk) → DÅ STOPP till Klas/architect, ej tyst workaround.

---

## 4. Migration — db-migration-writer-spec

**Migration:** `AddManualPostingToApplications` (eller motsv. timestamp-prefix per EF-konvention; db-migration-writer väljer exakt namn enligt befintlig migrations-namnstandard i `src/JobbPilot.Infrastructure/Persistence/Migrations/`).

**Up — exakt fyra `ADD COLUMN`, alla nullable, ingen default, ingen backfill:**

| Kolumn | Typ (Npgsql) | Nullable | Default |
|---|---|---|---|
| `manual_title` | `character varying(300)` | YES | ingen |
| `manual_company` | `character varying(200)` | YES | ingen |
| `manual_url` | `character varying(2000)` | YES | ingen |
| `manual_expires_at` | `timestamp with time zone` | YES | ingen |

Tabell: `applications`. Alla befintliga rader får NULL i alla fyra kolumner = semantiskt "ingen ManualPosting" (de är JobAd-kopplade eller degenererade — korrekt, ingen data-lögn). **Ingen backfill, ingen NOT NULL, ingen data-migration, ingen index** (ManualPosting queryas aldrig som filter/join-nyckel — den projiceras bara ut; index vore spekulativt, plan §65 "ingen backfill").

**Down — exakt fyra `DROP COLUMN`** (`manual_title`, `manual_company`, `manual_url`, `manual_expires_at`). Down är förlustfri i den meningen att den bara tar bort tillagda kolumner; ingen befintlig kolumn rörs.

**Idempotent:** EF-genererad migration är idempotent via `__EFMigrationsHistory`-mekanismen (samma som alla befintliga JobbPilot-migrations). db-migration-writer verifierar att den genererade `Up`/`Down` matchar exakt fyra kolumner och inget annat (ingen oavsiktlig kolumn-rename/drift från owned-entity-konfig). Den genererade migrationen + `.Designer.cs` committas i SAMMA batch (§7).

**Gate:** `db-migration-writer` (CLAUDE.md §9.2 obligatorisk för nya migrations). security-auditor bekräftar att `manual_url` (användar-input) inte öppnar XSS-yta — mitigerad av `ManualPosting.Create` TD-80-whitelist (§1) FÖRE persistens (defense-in-depth: validator + VO-factory).

**Flagga:** ingen. Additiv, nullable, ingen backfill — låg risk (datamodell-rapport A5).

---

## 5. Tre read-handlers — left join + ManualPosting-fallback

### 5a. Ny DTO `JobAdSummaryDto`

**Fil:** `src/JobbPilot.Application/Applications/Queries/JobAdSummaryDto.cs`

```
namespace JobbPilot.Application.Applications.Queries;

public sealed record JobAdSummaryDto(
    Guid? JobAdId,             // null när källan är ManualPosting (ingen JobAd-rad)
    string Title,
    string Company,
    string? Url,
    string Source,             // "Platsbanken" | "LinkedIn" | "Manual" (literal)
    DateTimeOffset? PublishedAt,   // J1: null för manuell; ALDRIG Application.CreatedAt
    DateTimeOffset? ExpiresAt);
```

`sealed record` (CLAUDE.md §3.3 DTO = record class; ADR 0048 Implementation). `JobAdId` är `Guid?` — för JobAd-grenen satt, för ManualPosting-grenen `null` (det finns ingen JobAd-rad; plan §1.5 "JobAdId-fältet i DTO blir då null").

`ApplicationDto` och `ApplicationDetailDto` får additivt fält `JobAdSummaryDto? JobAd` **sist** (bakåtkompatibelt — rå `JobAdId Guid?` behålls, plan §33):

```
public sealed record ApplicationDto(
    Guid Id, Guid JobSeekerId, Guid? JobAdId, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    JobAdSummaryDto? JobAd);
```

Samma additiva tillägg på `ApplicationDetailDto` (sist, efter `Notes`). `PipelineGroupDto` oförändrad (den nästlar `ApplicationDto` som redan bär `JobAd`).

### 5b. LINQ-form — EN `LEFT JOIN job_ads`, projektion före materialisering

**Mönster (gäller alla tre handlers, anpassat per handler):** byt nuvarande `.ToListAsync()` av entiteter mot en `.Select(...)`-projektion som joinar `db.JobAds` via query-syntax `join ... into ... from ... DefaultIfEmpty()`, **före** `ToListAsync()`. Detta tvingar EF att generera **en** `LEFT JOIN job_ads` i samma SQL-query.

**`GetApplicationsQueryHandler` (`GetApplicationsQueryHandler.cs:41-53`) — kanonisk form:**

```
var page = await (
    from a in baseQuery
    join j in db.JobAds on a.JobAdId equals j.Id into ja
    from j in ja.DefaultIfEmpty()
    orderby a.UpdatedAt descending
    select new ApplicationDto(
        a.Id.Value,
        a.JobSeekerId.Value,
        a.JobAdId == null ? (Guid?)null : a.JobAdId.Value.Value,
        a.Status.Name,
        a.CreatedAt,
        a.UpdatedAt,
        j != null
            ? new JobAdSummaryDto(
                j.Id.Value, j.Title, j.Company.Name, j.Url,
                j.Source.Value, j.PublishedAt, j.ExpiresAt)
            : a.ManualPosting != null
                ? new JobAdSummaryDto(
                    null, a.ManualPosting.Title, a.ManualPosting.Company,
                    a.ManualPosting.Url, "Manual",
                    (DateTimeOffset?)null, a.ManualPosting.ExpiresAt)
                : null))
    .Skip((query.Page - 1) * query.PageSize)
    .Take(query.PageSize)
    .ToListAsync(cancellationToken);
```

**Projektions-prioritet (exakt, J1-konform):**
1. `j != null` (JobAd-join träffade, ej soft-deletad) → JobAd-fält; `PublishedAt = j.PublishedAt` (satt).
2. annars `a.ManualPosting != null` → ManualPosting-fält; `Source = "Manual"` literal; **`PublishedAt = null`** (J1 — `Application.CreatedAt` projiceras ALDRIG som PublishedAt; CTO rev2, Martin 2017 kap. 7).
3. annars `null` → frontend renderar `"Ansökan #{kort-id}"`-fallback (plan §7 tillstånd 3).

**Kritiskt — query-filter-disciplin (ADR 0048 Beslut c, BLOCKING):** joinen mot `db.JobAds` ärver JobAd:s globala query-filter (`JobAdConfiguration.cs:82`, `DeletedAt == null`) **automatiskt och före joinen**. Soft-deletad JobAd → finns ej i join-källan → `DefaultIfEmpty()` ger `j == null` → gren 2/3-fallback. **FÖRBJUDET i alla tre handlers:** `IgnoreQueryFilters()` (exponerar soft-deletad metadata — regression ADR 0032/0048 c) och manuellt `j.DeletedAt == null`-predikat (dubblerar SPOT — DRY-brott, ADR 0048 c). Fallback för soft-deletad JobAd faller ut av `DefaultIfEmpty` + query-filter, inte av eget predikat.

**`Company.Name` i projektion:** `j.Company.Name` — `Company` är owned-type (`JobAdConfiguration.cs:31`), EF översätter `j.Company.Name` till `job_ads.company_name`-kolumn i SAMMA query (ingen extra join — owned types är samma tabell). Verifierat mot owned-type-mappningen.

**`GetApplicationByIdQueryHandler` (`GetApplicationByIdQueryHandler.cs:31-73`):** samma join-form, men single-row. Behåll `.Include(a => a.FollowUps).Include(a => a.Notes)` — dessa är Application-ägda collections (owned/HasMany, `ApplicationConfiguration.cs:54-64`), separata från JobAd-joinen. Projektionen blir en `select new ApplicationDetailDto(...)` med samma 3-grens-`JobAd`-logik + FollowUps/Notes-subprojektioner (oförändrade). **Behåll** den befintliga failed-access-logik-grenen (`GetApplicationByIdQueryHandler.cs:38-52`, ADR 0031/TD-67) orörd — den körs när `app is null` EFTER projektionen; projektionen får `FirstOrDefaultAsync` → om null, kör befintlig exists-check (cross-user-anomali-logg). Join-tillägget ändrar inte den vägen.

> **Designnot för test-writer/implementation:** med projektion till DTO via `Select` försvinner entiteten `app` som `null`-check-objekt. Lös genom att projicera till `ApplicationDetailDto?` och behålla mönstret: först `var dto = await query.Select(...).FirstOrDefaultAsync(ct);` `if (dto is null) { /* befintlig exists/cross-user-logg, GetApplicationByIdQueryHandler.cs:42-51 */ return null; }`. exists-checken (`db.Applications.AnyAsync(a => a.Id == applicationId, ct)`) är oförändrad — den frågar Application, ej JobAd, och berörs ej av joinen.

**`GetPipelineQueryHandler` (`GetPipelineQueryHandler.cs:25-44`) — N+1-fri form (BLOCKING):** den befintliga koden materialiserar `apps` med `ToListAsync()` och `GroupBy` **in-memory** (rad 32-44). Join MÅSTE ligga **före** `ToListAsync()`:

```
var rows = await db.Applications
    .AsNoTracking()
    .Where(a => a.JobSeekerId == jobSeekerId)
    .GroupJoin(db.JobAds, a => a.JobAdId, j => j.Id, (a, ja) => new { a, ja })
    .SelectMany(x => x.ja.DefaultIfEmpty(), (x, j) => new { x.a, j })
    .OrderByDescending(x => x.a.UpdatedAt)
    .Take(500)   // TD-8 skyddsventil — oförändrad
    .Select(x => new ApplicationDto(
        x.a.Id.Value, x.a.JobSeekerId.Value,
        x.a.JobAdId == null ? (Guid?)null : x.a.JobAdId.Value.Value,
        x.a.Status.Name, x.a.CreatedAt, x.a.UpdatedAt,
        x.j != null
            ? new JobAdSummaryDto(x.j.Id.Value, x.j.Title, x.j.Company.Name,
                x.j.Url, x.j.Source.Value, x.j.PublishedAt, x.j.ExpiresAt)
            : x.a.ManualPosting != null
                ? new JobAdSummaryDto(null, x.a.ManualPosting.Title,
                    x.a.ManualPosting.Company, x.a.ManualPosting.Url, "Manual",
                    (DateTimeOffset?)null, x.a.ManualPosting.ExpiresAt)
                : null))
    .ToListAsync(cancellationToken);

return rows
    .GroupBy(r => r.Status)
    .Select(g => new PipelineGroupDto(g.Key, g.Count(), g.ToList()))
    .ToList();
```

**Join FÖRE `ToListAsync()`** (en SQL-query med en LEFT JOIN), **GroupBy EFTER materialisering** (in-memory, oförändrat mönster — pipeline är kanban-vy, ej DB-aggregering; gruppering på materialiserade DTO:er är N+1-fri eftersom all data redan hämtats i den ENA queryn). `query`-syntax och `GroupJoin/SelectMany` ger identisk genererad SQL — välj den form implementation finner mest läsbar; designen är agnostisk mellan dem så länge join + projektion sker före `ToListAsync()`. `.AsNoTracking()` bevaras i alla tre (CLAUDE.md §3.6).

### 5c. SQL-verifieringsapproach (för STOPP 3a-rapport till Klas)

ADR 0048 + plan §1.2/§1.4 kräver **explicit bevis** att genererad SQL = en query med EN LEFT JOIN (ej post-materialiserings-lookup per rad).

**Approach (specificerad så STOPP 3a-rapporten kan visa Klas SQL):** test-writer skriver **ett dedikerat SQL-assertions-test** per handler-query som anropar `.ToQueryString()` på den projicerade `IQueryable` (före `ToListAsync()`) och asserterar mot den genererade SQL-strängen:

- `Assert` att strängen innehåller **exakt en** `LEFT JOIN` mot `job_ads` (count av `"LEFT JOIN"` + `"job_ads"` = 1).
- `Assert` att den **inte** innehåller `IgnoreQueryFilters`-artefakt — dvs den genererade SQL:en innehåller JobAd:s soft-delete-predikat (`deleted_at IS NULL` eller motsv. för den joinade job_ads-aliasen) — bevisar query-filtret applicerades på join-grenen (ADR 0048 c).
- `.ToQueryString()` kräver relationell provider; kör mot Npgsql i ett integrationstest (test-writer specificerar — JobbPilot har redan integrationstest-infrastruktur; bekräfta i test-design vilken test-bas som har relationell context, ej InMemory som ej stödjer `ToQueryString` meningsfullt för join-SQL).

Den producerade SQL-strängen klistras **verbatim** i STOPP 3a-rapporten till Klas (CLAUDE.md §9.4 paste-verifiering). Detta är gate-beviset ADR 0048 Implementation + plan §1.2 kräver.

**Flagga:** ingen — approach specificerad. Om `ToQueryString` mot Npgsql-test-bas inte är tillgänglig i nuvarande test-infra → test-writer flaggar i test-design (då kan EF-loggning-capture vara fallback), men `ToQueryString` är förstahandsval (deterministiskt, ingen log-parsing).

---

## 6. JobAdSummaryDto + Zod

**C#:** se §5a (kanonisk form). `PublishedAt` = `DateTimeOffset?` (J1).

**Zod (`web/jobbpilot-web/src/lib/dto/applications.ts`) — additivt, deploy-säkert:**

```
export const jobAdSummaryDtoSchema = z.object({
  jobAdId: z.string().nullable(),
  title: z.string(),
  company: z.string(),
  url: z.string().nullable(),
  source: z.string(),
  publishedAt: z.string().nullable(),
  expiresAt: z.string().nullable(),
});
export type JobAdSummaryDto = z.infer<typeof jobAdSummaryDtoSchema>;
```

`applicationDtoSchema` + `applicationDetailDtoSchema` får `jobAd: jobAdSummaryDtoSchema.nullable()`:

```
export const applicationDtoSchema = z.object({
  id: z.string(),
  jobSeekerId: z.string(),
  jobAdId: z.string().nullable(),
  status: applicationStatusSchema,
  createdAt: z.string(),
  updatedAt: z.string(),
  jobAd: jobAdSummaryDtoSchema.nullable(),   // additivt
});
```

`applicationDetailDtoSchema` ärver via `.extend(...)` (oförändrad extend-kedja, `applications.ts:61`) — `jobAd` kommer automatiskt från base.

**Deploy-säkerhet (3a deployas FÖRE 3b) — verifierat:** Zod `z.object()` är **icke-strict by default** (`.strip()`-beteende) — `applications.ts` använder ingen `.strict()` någonstans (verifierat: greppa `applications.ts` — endast `z.object`/`z.enum`/`.extend`/`.nullable`). DEPLOYAD frontend (3a-backend live, 3b ej deployad) tar emot DTO:er som NU innehåller `jobAd`-fältet. Befintlig deployad Zod-schema **utan** `jobAd`-fält: `z.object` **strippar okända fält tyst** → parse kraschar INTE. Existerande pages (`/ansokningar`, `/ansokningar/[id]`) läser inte `jobAd` ännu → ingen runtime-effekt. **3a-deploy är frontend-säker.** Detta är exakt varför Zod-tillägget är additivt och varför ADR 0020-single-source-mönstret tål deploy-skew. **Krav:** Zod-ändringen committas i 3a-batchen (single source — backend-DTO och Zod-spegel får aldrig divergera mellan commits; men frontend-RENDER av `jobAd` är 3b). Zod-schema-tillägg + backend i 3a; rendering i 3b.

> **Not:** `jobAd`-tillägget i Zod i 3a innebär att 3a-batchen rör en frontend-fil (`applications.ts`). Det bryter INTE J3:s "3a backend atomisk / 3b frontend"-split — `applications.ts` är DTO-kontraktet (ADR 0020 single source), inte rendering. Kontraktet hör till backend-vertikalen; komponenter/pages hör till 3b. Detta är konsekvent med plan §9 ("Backend skrivväg ... `lib/dto/applications.ts`" listad under STOPP 3-backend-relaterat via single-source-principen). Om Klas vill ha `applications.ts` helt i 3b i stället: det är ett Klas-beslut (se Flaggor) — designen rekommenderar Zod i 3a för single-source-integritet (DTO och spegel i samma commit, memory `feedback_di_with_handlers_same_commit`-analogt: kontrakt och dess spegel får ej divergera mellan commits).

---

## 7. Atomisk batch-ordning (J3) — bygg-säker sekvens i EN commit

Allt i **en push** (J3 — write-utan-matchande-read får ALDRIG nå main; broken intermediate = exakt defekten Klas underkände 2 ggr). Inom commiten skrivs filerna i denna logiska TDD-ordning så att test-writer kan köra RÖD→GRÖN och builden är konsistent vid commit-tillfället (mellan-fil-state spelar ingen roll eftersom allt är EN commit — ordningen är för TDD-disciplin + reviewers läsbarhet, ej för bisect-säkerhet):

1. **`ManualPosting.cs`** (Domain VO) — ingen beroende, kompilerar isolerat.
2. **`Application.cs`** — `ManualPosting?`-property, ctor-param, `Create`-signatur + invariant. Beror på (1).
3. **test-writer RÖD svit** (Domain): `ManualPosting.Create`-invarianter (title/company required, url-whitelist, längd-caps); `Application.Create` invariant-matris (4 fall §2); `(null,null)`→Success-regressionsskydd. RÖD (implementation finns men test-asserterar beteendet — TDD per CLAUDE.md §7).
4. **`ApplicationConfiguration.cs`** — `OwnsOne` + `IsRequired(false)` (§3). Beror på (2).
5. **Migration** (`db-migration-writer`) — `AddManualPostingToApplications` + `.Designer.cs` (§4). Beror på (4) (EF genererar migration från model-snapshot).
6. **test-writer RÖD (Infrastructure):** owned-entity round-trip-test (§3b BLOCKING — persistera utan ManualPosting → `app.ManualPosting is null`; persistera med → materialiseras).
7. **`CreateApplicationCommand.cs`** — `ManualPostingInput? Manual` (Title/Company/Url?/ExpiresAt? — ingen Source) tillagd record-param.
8. **`CreateApplicationCommandValidator.cs`** — regler: `JobAdId == null` ⇒ `Manual.Title`/`Manual.Company` required; `JobAdId != null` ⇒ `Manual` måste vara null (motstridigt annars — speglar domän-invarianten §2 i validator-lagret, defense-in-depth).
9. **`CreateApplicationCommandHandler.cs`** — `command.Manual != null` ⇒ `ManualPosting.Create(...)` (Result; om Failure → returnera `Result.Failure<Guid>(result.Error)`); skicka `manualPosting` till `DomainApplication.Create(jobSeekerId, jobAdId, command.CoverLetter, manualPosting, clock)` (migrera den ENDA call-siten, `CreateApplicationCommandHandler.cs:37`). Beror på (2),(7).
10. **test-writer RÖD (Application):** create-command happy (manuell), validation-fail (jobAdId+Manual motstridigt), cross-user (orörd auth-väg bevarad), `ManualPosting.Create`-fel propageras som command-fel.
11. **`JobAdSummaryDto.cs`** + `ApplicationDto.cs`/`ApplicationDetailDto.cs` additiva fält (§5a).
12. **3 read-handlers** — join + 3-grens-projektion (§5b): `GetApplicationsQueryHandler`, `GetApplicationByIdQueryHandler`, `GetPipelineQueryHandler`. Beror på (11).
13. **test-writer RÖD (read):** per handler — med JobAd / jobAdId null+ManualPosting (Source="Manual", PublishedAt=null) / soft-deletad JobAd via default-join utan `IgnoreQueryFilters` → fallback / cross-user / tillstånd-3 (null,null)→`jobAd:null`. + **SQL-assertions-test** (§5c, `.ToQueryString()` EN LEFT JOIN + query-filter-predikat).
14. **`applications.ts`** Zod additivt (§6).
15. **GRÖN:** kör hela sviten; allt grönt → batch klar för STOPP 3a-rapport.

Gates som körs på batchen före STOPP 3a-rapport till Klas (plan §1.4 / CLAUDE.md §9.2): `db-migration-writer` (steg 5) · `test-writer` (RÖD-svit FÖRST, TDD) · `security-auditor` BLOCKING (ManualPosting URL-input TD-80-yta, cross-user, soft-deletad metadata ej läcker via join) · `code-reviewer`. SQL-strängen (§5c) verbatim i rapporten (ADR 0048-gate-bevis).

---

## Flaggor till Klas

1. **Inga designval kräver ny Klas-STOPP.** Samtliga är härledda ur godkända beslut (Variant A architect a4c1483aeaee7fcea, CTO rev2 J1–J3, ADR 0048 Beslut c/d) + kod-verifierade invarianter. CC kan gå STOPP 3a.1 → test-writer RÖD → implementation → gates → STOPP 3a-rapport utan mellanliggande Klas-GO (CLAUDE.md §9.6 p.5 — entydigt principmotiverat).

2. **Source-strykningen bryter inget.** Den eliminerar en dead axis i VO:t och förenklar read-projektionen till en literal. Konsekvent med CLAUDE.md §5.1 (ingen YAGNI-axel). Equality, EF-mappning och invariant är oberoende av Source. Ingen design-konsekvens utöver "en kolumn färre" (`manual_source` utgår ur migration §4 — fyra kolumner, ej fem; planens §65-exempel listade `manual_source` men §59 Source-strykning supersederar — **migrationen har FYRA kolumner**).

3. **EN punkt kräver Klas-medvetenhet (ej STOPP):** EF optional owned-entity null-semantik (§3b) löses med `Navigation(...).IsRequired(false)` + ett BLOCKING round-trip-test. Designen specificerar lösningen exakt; det är en implementations-instruktion. Endast om round-trip-testet inte kan göras GRÖNT med den angivna mappningen (EF-version-risk, osannolikt på EF 10.0.8) → DÅ STOPP, ej tyst workaround.

4. **Klas-beslut (mindre, ej blockerande):** `applications.ts` Zod-tillägg ligger i 3a-batchen (single-source-integritet — DTO och spegel i samma commit) trots att J3 säger "3a backend / 3b frontend". Designen motiverar detta (kontrakt ≠ rendering; rendering är 3b). Om Klas föredrar `applications.ts` helt i 3b accepteras det, men designen rekommenderar 3a för att DTO och Zod-spegel aldrig ska divergera mellan commits (ADR 0020). Lyfts för Klas-medvetenhet; default = Zod i 3a.

---

## Beslut (sammanfattat)

| Fråga | Svar |
|---|---|
| `ManualPosting`-form | `public sealed record` i `Domain/Applications/`; Title+Company non-empty obligatoriska, Url? (TD-80-whitelist), ExpiresAt? (ingen framtidsvalidering); record-default equality; ingen Source |
| `Application.Create`-ändring | Utökad signatur (ej overload): `ManualPosting?` sist före `clock`; invariant `jobAdId is not null && manualPosting is not null ⇒ Failure "Application.JobAdAndManualMutuallyExclusive"`; `(null,null)`→Success bevaras |
| EF-mappning | `OwnsOne` + explicit `HasColumnName` (`manual_title/company/url/expires_at`) + **`Navigation(...).IsRequired(false)`** (obligatorisk, §3b); BLOCKING round-trip-test |
| Migration | 4 nullable kolumner (ej 5 — Source struken), ingen default/backfill/index, Down=4 DROP, idempotent, db-migration-writer-gate |
| Read-handlers | EN `LEFT JOIN job_ads` via `DefaultIfEmpty()` FÖRE `ToListAsync()`; 3-grens-projektion (JobAd→ManualPosting[PublishedAt=null,Source="Manual"]→null); query-filter ärvs, `IgnoreQueryFilters`/manuellt DeletedAt-predikat FÖRBJUDET; Pipeline GroupBy in-memory EFTER materialisering |
| SQL-verifiering | `.ToQueryString()`-assertions-test per handler (1 LEFT JOIN + query-filter-predikat), verbatim i STOPP 3a-rapport |
| `JobAdSummaryDto` | `sealed record`; `JobAdId Guid?` (null för Manual), `PublishedAt DateTimeOffset?` (J1); additivt på Application(Detail)Dto |
| Zod | additivt `jobAdSummaryDtoSchema` + `jobAd: ...nullable()`; deploy-säkert (z.object strippar, ingen .strict); i 3a-batch (single source) |
| Klas-STOPP? | **Nej** — entydigt; 1 medvetenhets-flagga (§3b EF), 1 mindre Klas-beslut (Zod-batch-placering) |

---

## Referenser

- CLAUDE.md §2.2 (aggregat skyddar invarianter i Create, ej handler), §2.3 (CQRS — read-DTO ut), §3.3 (record VO/DTO), §3.6 (AsNoTracking/projektion/Include), §5.1 (primitive obsession; ingen YAGNI-axel — Source struken), §7 (TDD-test-krav), §9.2 (gates), §9.4 (paste-verifiering — SQL verbatim), §9.6 (in-block vs Klas-STOPP)
- ADR 0048 Beslut (a) in-handler join-mönster, (c) query-filter-disciplin (`IgnoreQueryFilters`/manuellt DeletedAt-predikat förbjudet), (d) write-side-avgränsning (ManualPosting inom Application-aggregatet)
- ADR 0032 §4 (`ExternalReference`-VO + owned-type-precedens), §8 (JobAd soft-delete-semantik — skyddas av query-filter-disciplin)
- ADR 0009 (`IAppDbContext` aggregate-per-DbSet — växer ej; join i handler)
- ADR 0020 (Zod single source — DTO och spegel i samma commit)
- `docs/reviews/2026-05-17-fas3-ansokningar-datamodell-architect.md` (Variant A-beslut a4c1483aeaee7fcea — A2/A3/A5 vidareutvecklade här med exakt EF-mappning + invariant-placering)
- `docs/reviews/2026-05-17-fas3-ansokningar-plan-cto-rev2.md` (J1 PublishedAt nullable, J3 atomisk batch)
- `docs/design/ansokningar-redesign-plan.md` §1.1–§1.5 (Source struken §59), §7 (tre identitets-tillstånd; Företag obligatoriskt), §9 (filer)
- Verifierad kod: `Application.cs:8-66`, `JobAd.cs:155-189` (TD-80), `Company.cs:11-21`, `JobSource.cs:11`, `ApplicationConfiguration.cs:1-70`, `JobAdConfiguration.cs:31-65` (owned-type-precedens), `ExternalReference.cs` (optional-VO-precedens), 3 read-handlers, `CreateApplicationCommand/Handler/Validator.cs`, `applications.ts`, `IAppDbContext.cs`, EF Core 10.0.8 / Npgsql 10.0.1 / `DependencyInjection.cs:262` (global snake_case)
- Eric Evans, *Domain-Driven Design* (2003) — Value Objects, Aggregates · Vaughn Vernon, *IDDD* (2013) — consistency boundary · Robert C. Martin, *Clean Architecture* / *Clean Code* kap. 7 (J1 — fält med 2 change-reasons) · Martin Fowler, *Refactoring* 2nd ed (2018) — Primitive Obsession / Speculative Generality · Hunt/Thomas, *Pragmatic Programmer* (1999) — DRY/SPOT (query-filter ej dubblerad) · OWASP A01:2021 / TD-80 (URL-scheme-whitelist återanvänd i `ManualPosting.Create`)
