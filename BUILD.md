# Jobbliggaren — Arkitekturspecifikation

> Arkitektur-inriktad del av byggspecifikationen för Jobbliggaren, en svensk
> jobbansökningshanterare byggd som civic utility. Täcker teknikstack,
> systemarkitektur, domänmodell, API, datamodell, deterministiska CV-/matchnings-
> motorer, säkerhet och infrastruktur. Systemdokument:
> [`CLAUDE.md`](./CLAUDE.md), [`DESIGN.md`](./DESIGN.md)

---

## 3. Teknikstack

### 3.1 Exakta versioner

| Komponent | Val | Version | Notis |
|-----------|-----|---------|-------|
| Backend runtime | .NET | 10 (LTS till 2028-11) | GA sedan 2025-11-11 |
| Språk (backend) | C# | 14 | Extension members, `field` keyword GA, null-conditional assignment |
| Backend framework | ASP.NET Core | 10 | Minimal API |
| ORM | EF Core | 10 | Npgsql-provider 10.x |
| Auth | ASP.NET Core Identity | 10 | Egen Identity-DB |
| Mediator | Mediator (martinothamar) | 3.x | Source-generated CQRS, MIT, Native AOT-kompatibelt (ersätter MediatR) |
| Validering | FluentValidation | 12.x | Via Mediator-pipeline |
| Mapping | — (manuell) | — | Ingen mapping-bibliotek; explicit DTO-mappning per CLAUDE.md §5 (AutoMapper/Mapster avvisade över domängränsen) |
| Background jobs | Hangfire | 1.8.x | Postgres-storage |
| Smart enum | Ardalis.SmartEnum | 8.x | State machines i domänen |
| Logging | Microsoft.Extensions.Logging | 10.x | `Microsoft.Extensions.Logging.Console` → stdout + persistent strukturerad sink via Seq (TD-104, STEG 6) |
| Log sink | Seq.Extensions.Logging | 9.0.0 | MEL-provider → Seq (datalust); config-gated på `Seq:ServerUrl`; net9-asset .NET 10-kompatibel (MEL `>= 9` unifieras uppåt); dev lokal Seq, prod Seq self-hosted EU (TD-104, ADR 0050) |
| Observability | OpenTelemetry | 1.15+ | Traces + metrics |
| PDF parsing | PdfPig | 0.1.14+ | Text extraction |
| DOCX parsing | DocumentFormat.OpenXml | 3.x | Microsoft-underhåll |
| PDF generation | QuestPDF | 2026.6.0 | Community (source-available, free under USD 1M revenue, non-copyleft — ADR 0050-safe; not OSI-MIT); `QuestPDF.Settings.License = LicenseType.Community` i startup |
| DOCX generation | DocumentFormat.OpenXml | 3.x | Template-baserad |
| NLP (svenska) | Catalyst (+ Catalyst.Models.Swedish) | 26.x (CalVer) | MIT; lokal svensk NLP — tokenisering, lemmatisering, POS, NER (deterministisk CV-/matchnings-motor, ADR 0071 Beslut 6); svensk modell = separat MIT-datapaket |
| Stemmer (svenska) | libstemmer.net | 2.2.x | MIT-wrapper; Snowball-kärna BSD-3-Clause; svensk Snowball-stemmer |
| Stavning | WeCantSpell.Hunspell | 7.x | Hunspell-port — tri-licens **MPL 1.1 / GPL 2.0 / LGPL 2.1**; licensval MPL 1.1 (LGPL 2.1 fallback), aldrig GPL; server-side + oförändrad binär → ingen copyleft på produkten (se §3.1-notis) |
| Svensk ordlista | sv_SE Hunspell-ordlista (DSSO) | datafil | **LGPL-3.0** — oförändrad separat datafil, ej statiskt länkad/inbäddad/modifierad → copyleft smittar ej produkten (se §3.1-notis) |
| HTTP | HttpClientFactory + Refit | 10.x | JobTech-klient |
| Transaktionell e-post | Resend | 0.5.x | MIT; officiella .NET-SDK:n, Infrastructure-confined (IResend/EmailMessage korsar aldrig IEmailSender-porten, paritet Refit/QuestPDF); bakgrundsmatchnings-notiser (ADR 0080 Vag 4 PR-4); wrappar IHttpClientFactory; dev = test-mode (`onboarding@resend.dev`); prod-utskick kräver DPA/SCC + security-auditor-sign-off |
| Database | PostgreSQL | 18.3 | lokal Docker Compose nu; co-tenant container på Hetzner CAX31 (ADR 0050, ingen separat managed-DB) |
| Cache | Redis | 8.6 | lokal Docker Compose nu; co-tenant container på Hetzner CAX31 (ADR 0050) |
| Test-assertions | Shouldly | 4.3.x | MIT, ersätter commercial FluentAssertions |
| Test-mocks | NSubstitute | 5.x | Mock-ramverk för Application-tester |
| Arch-tests | NetArchTest.Rules | 1.x | V1-val; abandoned sedan 2022 — överväg ArchUnitNET vid v2 |
| Load-test | NBomber | 6.x | MIT; .NET-native, xUnit/MTP-koherent; k6 avvisat (ADR 0045 Beslut 4) |
| Load-test HTTP | NBomber.Http | 6.x | HTTP-scenario-helpers för API-latens-mätning |
| Frontend framework | Next.js | 16.2 (App Router) | SSR + ISR |
| Språk (frontend) | TypeScript | 6.0 | Strict mode |
| UI-komponenter | shadcn/ui | senaste (CLI v4) | Tung customisering, se DESIGN.md |
| Styling | Tailwind CSS | 4.2 | v4 config i `tailwind.config.ts` |
| Data fetching | TanStack Query | 5.x | Server state |
| Tabeller | TanStack Table | 8.x | Headless |
| Form | React Hook Form + Zod | RHF ^7.72, Zod 4.x | Schema-baserad validering |
| Auth-klient | NextAuth.js (Auth.js) | 5 | Integrerar mot backend Identity-JWT (cookie-sessions) |
| Datum | date-fns | 4.x | Svensk locale |
| Ikoner | Lucide React | ^1.8 | Minimalistiskt, civic-vänligt |
| Typografi | Source Sans 3 | Google Fonts | Primär (next/font/google, byte från Hanken Grotesk — ADR 0091 / #549 WS4); systemfont-fallback. JetBrains Mono för kod |

> **AI SDK borttaget (ADR 0071):** `Anthropic`-NuGet och `AWSSDK.BedrockRuntime`
> ingår inte — produkten har ingen AI/LLM. PdfPig / DocumentFormat.OpenXml /
> QuestPDF (ovan) täcker PDF/DOCX/render-tiern.
>
> **Lokal NLP-tier (Fas 4, ADR 0071 Beslut 6) — INLÅST 2026-06-14.** Catalyst
> (+ Catalyst.Models.Swedish), libstemmer.net och WeCantSpell.Hunspell driver den
> deterministiska CV-/matchnings-motorns lokala NLP (tokenisering, svensk
> stemming/lemmatisering, POS-taggning, stavning — ~26 % av kunskapsbankens
> kriterier per ADR 0071). Ingen AI/LLM; all NLP körs lokalt på VPS:en.
> Stemming (libstemmer/Snowball) och lemmatisering (Catalyst) är komplementära;
> den slutliga stemming-vägen för title/keyword-overlap avgörs vid Fas 4-design
> (ADR 0071 Open question 1) — båda inlåsta som beroende-kandidater, ej bindande
> att båda används i v1.
>
> **AOT-/VPS-notis (Fas 4-design).** Catalyst laddar svenska modeller via
> `Register()` + `DiskStorage` + MessagePack binär-deserialisering (runtime
> typupplösning) → NLP-tiern är **inte verifierat Native-AOT/trimming-säker**; kör
> JIT i container (ADR 0050, default). Mediator-AOT-kompatibiliteten (ovan) gäller
> CQRS-pipelinen, inte NLP-tiern. Modellerna laddas dessutom residenta i
> Worker-processen → cold-start-latens + statiskt minnesfotavtryck på CAX31
> (16 GB co-tenant, ADR 0050); mät mot ADR 0045 Worker-minnesbudget vid Fas 4 och
> överväg lazy-init (ladda vid första CV-operation, ej vid boot).
>
> **Copyleft-separation (security-auditor-sign-off 2026-06-14).** Jobbliggaren
> distribueras **inte** som binär — produkten kör server-side på VPS:en (ADR 0050)
> och konsumenten interagerar enbart över HTTP. MPL 1.1-, LGPL 2.1- och
> LGPL-3.0-copyleft utlöses av *distribution* ("Distribute"/"convey"); ingen av
> licenserna är AGPL (ingen network-use-klausul). Eftersom ingen binär lämnar
> VPS:en utlöses ingen copyleft-förpliktelse på produktkoden. Som extra marginal
> konsumeras de två copyleft-artefakterna ändå som **oförändrade, separerbara**
> enheter:
> 1. **WeCantSpell.Hunspell** ärver Hunspells **tri-licens MPL 1.1 / GPL 2.0 /
>    LGPL 2.1** (ADR 0071 Beslut 6:s "MIT" var ett faktafel — korrigerat här efter
>    licensverifiering 2026-06-14). Vi väljer **MPL 1.1** (file-level weak
>    copyleft: förpliktelser fäster bara på de licensierade *källfilerna*, aldrig
>    på vår egen kod i våra egna filer); LGPL 2.1 som fallback. Vi väljer **aldrig
>    GPL 2.0**. Villkor: den publicerade NuGet-binären får ej modifieras och ingen
>    produktkod får läggas in i eller härledas från de licensierade filerna.
> 2. **sv_SE Hunspell-ordlista (DSSO)** är **LGPL-3.0**. Den konsumeras som en
>    **oförändrad, separat datafil** (ej kompilerad in, ej inbäddad som resurs, ej
>    modifierad) → LGPL-copyleft sträcker sig inte till applikationen.
>
> **Notice-förpliktelse vid distribution (Fas 4 build-time):** MIT (Catalyst,
> libstemmer.net-wrapper), BSD-3-Clause (Snowball-kärnan), MPL 1.1 och LGPL-texten
> kräver att respektive licens-/copyright-notis medföljer deploy-artefakten —
> samla i `THIRD-PARTY-NOTICES`. Permissiva licenser är inte notis-fria.

### 3.2 Infrastruktur

> **Statusbanner (2026-06-08):** AWS-dev-stacken är avvecklad (ADR 0066) och
> AWS lämnas permanent (Klas-direktiv 2026-06-06). All utveckling kör nu lokalt
> på laptop (Docker Compose: postgres + redis). Permanent deploy-mål — **Hetzner
> Cloud CAX31 (ARM, 16 GB) all-in-one Docker Compose **BE + FE** + Cloudflare
> DNS/CDN/proxy** — är beslutat i **ADR 0050 (Accepted 2026-06-08)**. Tabellen
> nedan beskriver **nuläge (lokalt)** + **beslutat permanent mål**. Faktisk
> provisionering är fortsatt framtida Klas-gatat arbete (ADR 0050 Sekvensering:
> Hetzner sist, vid MVP före beta-testare). AWS-kolumnerna i ADR/sessions/
> research bevaras som historik.

| Tjänst | Nuläge (lokal dev) | Permanent mål (ADR 0050, Accepted) |
|--------|--------------------|---------------|
| Compute (backend/worker) | `dotnet run` lokalt | Hetzner CAX31 (ARM, 16 GB), Docker Compose: API + Worker co-tenant |
| Database | PostgreSQL 18.3 (Docker Compose) | PostgreSQL co-tenant container på CAX31 (ingen managed-DB) |
| Cache | Redis 8.6 (Docker Compose) | Redis co-tenant container på CAX31 |
| Object storage | lokal disk / ej aktiverat | TBD — roll/behov ej fastställt |
| AI inferens | Ingen — produkten har ingen AI/LLM (ADR 0071) | Ingen (deterministiska motorer på BE/VPS) |
| Email | `ConsoleEmailSender` (ADR 0066) | Transaktionell mejlväg (TD-101) |
| Secrets | `appsettings.Local.json` (gitignored) | Self-managed på VPS (systemd-credentials / sops+age, TD-106) |
| Encryption keys | `LocalDataKeyProvider` AES-256-GCM (ADR 0066) | Self-managed master-nyckelmodell + rotation (TD-102) |
| Frontend | `pnpm dev` (localhost:3000) | Next.js `next start` co-tenant container på CAX31 (bakom Caddy) |
| DNS / CDN / proxy | — | Cloudflare gratis-tier "Full (strict)" framför Caddy-origin på CAX31 |
| Backup | — | Nattlig klient-side-krypterad `pg_dump` → Hetzner-EU Storage Box (TD-107) |
| Logging / monitoring | console (MEL) + Seq (`Seq.Extensions.Logging`) | Persistent strukturerad sink via Seq self-hosted EU — TD-104 |
| Errors | — | Sentry (EU) planerat |
| CI | GitHub Actions (build + test + coverage, inga moln-anrop) | oförändrat |
| IaC | `infra/terraform/` bevarad som reversibilitets-mekanik (ADR 0066 Beslut 1) | retireras via egen ADR vid Hetzner-cutover |

### 3.3 Miljöer

> **Status (2026-06-08):** dev/staging/production-AWS-miljöerna är avvecklade
> (ADR 0066). `local` är enda aktiva miljön. Permanent deploy-mål är **beslutat**
> (Hetzner CAX31 + Cloudflare, ADR 0050 Accepted 2026-06-08) men ännu
> ej provisionerat (ADR 0050 Sekvensering: Hetzner sist, vid MVP före
> beta-testare). Tag-baserad AWS-deploy (`v*-dev`/`v*-rc*`/`v*`) refererar
> **avvecklad infra** — den AWS-baserade stacken är **riven** (ADR 0066,
> 2026-05-26), inte pausad; deploy-workflowsen (`deploy-dev.yml` m.fl.) är
> bevarade som reversibilitets-/historik-mekanik (ADR 0066 Beslut 1 + ADR
> 0069 D3) och `deploy-dev.yml`:s auto-trigger är borttagen 2026-06-28. Ny
> Hetzner-pipeline byggs vid cutover.

| Miljö | Syfte | Deployment | Status |
|-------|-------|-----------|--------|
| local | Utveckling | Docker Compose | **Aktiv** |
| production (planerad) | Live | Hetzner CAX31 + Cloudflare (ADR 0050) | Beslutad, ej provisionerad |
| dev / staging (AWS) | f.d. integration / pre-prod | — | Avvecklad (ADR 0066) |

PR-flöde mot `main` per ADR 0065 (CI-gate). Permanent deploy-strategi och
miljö-topologi är fastställd i ADR 0050; pipelinen byggs vid Hetzner-cutover.

---

## 4. Systemarkitektur

### 4.1 Lager (Clean Architecture)

```
┌─────────────────────────────────────────────────────┐
│  Presentation / Interfaces                          │
│  ├─ Jobbliggaren.Api          (REST endpoints)         │
│  ├─ Jobbliggaren.Worker       (Hangfire host)          │
│  └─ Jobbliggaren.Web          (Next.js, extern)        │
├─────────────────────────────────────────────────────┤
│  Jobbliggaren.Infrastructure                           │
│  ├─ Persistence (EF Core, migrations)               │
│  ├─ Identity                                        │
│  ├─ JobSources.Platsbanken                          │
│  ├─ CvEngines (parsing, lokal NLP, render — Fas 4)  │
│  ├─ Email (ConsoleEmailSender; SES borttagen)       │
│  ├─ Security (Local/Kms DEK-provider, ADR 0066)     │
│  ├─ CalendarIntegration.Google                      │
│  ├─ GmailSync                                       │
│  ├─ Salary.Scb                                      │
│  └─ BackgroundJobs (Hangfire setup)                 │
├─────────────────────────────────────────────────────┤
│  Jobbliggaren.Application                              │
│  ├─ Common (interfaces, behaviors, exceptions)      │
│  ├─ JobSeekers                                      │
│  ├─ Resumes                                         │
│  ├─ JobAds                                          │
│  ├─ Applications                                    │
│  ├─ CoverLetters                                    │
│  ├─ SavedSearches                                   │
│  ├─ Companies                                       │
│  ├─ Contacts                                        │
│  ├─ Matching                                        │
│  ├─ CvAssist (deterministic review/improve)         │
│  └─ Admin                                           │
├─────────────────────────────────────────────────────┤
│  Jobbliggaren.Domain  (PURE, no external deps)         │
│  ├─ Common (AggregateRoot, Entity, ValueObject)     │
│  ├─ JobSeekers                                      │
│  ├─ Resumes                                         │
│  ├─ JobAds                                          │
│  ├─ Applications                                    │
│  ├─ CoverLetters                                    │
│  ├─ SavedSearches                                   │
│  ├─ Companies                                       │
│  ├─ Contacts                                        │
│  ├─ Matching                                        │
│  └─ Shared (Money, Location, EmailAddress, ...)     │
└─────────────────────────────────────────────────────┘
```

### 4.2 Dependency direction

Domain beror på ingenting (inte ens Mediator.SourceGenerator).
Application beror på Domain.
Infrastructure beror på Application (implementerar interfaces) och Domain (läser entities).
Api och Worker beror på Infrastructure och Application.

Verifieras via ArchUnit.NET eller NetArchTest-regler i Domain.ArchitectureTests-projektet.

### 4.3 Solution-struktur

```
/Jobbliggaren.sln
/src
  /Jobbliggaren.Domain
  /Jobbliggaren.Application
  /Jobbliggaren.Infrastructure
  /Jobbliggaren.Api
  /Jobbliggaren.Worker
  /Jobbliggaren.Migrate         (DDL-init console-app, ADR 0033)
/web
  /jobbliggaren-web             (Next.js)
/tests
  /Jobbliggaren.Domain.UnitTests
  /Jobbliggaren.Application.UnitTests
  /Jobbliggaren.Api.IntegrationTests    (Testcontainers + WebApplicationFactory)
  /Jobbliggaren.Architecture.Tests
  /jobbliggaren-web-tests       (Playwright e2e)
/infra
  /terraform
    /modules
    /environments
/docs
  /decisions                  (Architecture Decision Records — index: decisions/README.md)
  /api
  /runbooks
```

### 4.4 Cross-cutting concerns

**Alla genom Mediator.SourceGenerator-pipeline i Application-lagret:**

1. `LoggingBehavior` — loggar request-start, duration, success/fail
2. `ValidationBehavior` — kör FluentValidation, returnerar `Result<T>.Failure` vid fel
3. `AuthorizationBehavior` — kontrollerar att current user har rätt att köra handler
4. `CachingBehavior` — caches `ICacheable`-queries till Redis
5. `UnitOfWorkBehavior` — wrappar commands i DB-transaction, triggar domain event dispatch efter SaveChanges

---

## 5. Domänmodell

### 5.1 Aggregate roots (översikt)

| Aggregate | Typ | Äger | Refererar till |
|-----------|-----|------|---------------|
| `JobSeeker` | AR | `Preferences` (VO) | — |
| `Resume` | AR | `ResumeVersion` (entity) | `JobSeekerId` |
| `SavedSearch` | AR | `SearchCriteria` (VO) | `JobSeekerId` |
| `JobAd` | AR | — | `CompanyId` |
| `Company` | owned VO (se not) | — | — |
| `Contact` | AR | — | `CompanyId` (opt.) |
| `Application` | AR | `FollowUp` (entity), `ApplicationNote` (entity) | `JobSeekerId`, `JobAdId`, `ResumeVersionId`, `CoverLetterId`, `ContactId` |
| `CoverLetter` | AR | — | `JobSeekerId`, `JobAdId`, `ApplicationId` |

> **`Company` är inte ett persisterat aggregat i v1 (ADR 0087 D2, Accepterat
> 2026-06-30).** I domänen är `Company` en namn-only *owned VO* på `JobAd`
> (`src/Jobbliggaren.Domain/JobAds/Company.cs`), inte en Aggregate Root.
> Företagsidentitet för företagsbevakning löses via en read-model-projektion
> över `job_ads` (org.nr som naturlig nyckel + `company_name`); det byggda
> aggregatet för bevakning är `CompanyWatch` (med notisspåret
> `FollowedCompanyAdHit`), inte `Company`.
>
> Motsvarande obyggda rekryterar-/CRM-yta står kvar på flera ställen i specen och
> behålls som framtida scope, inte v1-verklighet: `Company` som surrogat-nycklat
> AR med `website`, `CompanyId`-referenserna (`JobAd` och `Contact`, §5.1;
> `CompanyId`-VO:t, §5.2), `companies`/`contacts`-tabellerna i datamodellen (§7.1)
> med deras `/api/v1/companies`- och `/contacts`-endpoints (§6.2), och
> `Company.website` i Gmail-synk-matchningen (§9.2). Allt detta tillhör en **separat, ännu obyggd
> framtida rekryterar-/CRM- + Gmail-synk-feature** (§9.2 är själv uppskjuten ur
> MVP per #321).

### 5.2 Value Objects

- `JobSeekerId`, `ResumeId`, `ResumeVersionId`, `JobAdId`, `CompanyId`, `ContactId`, `ApplicationId`, `CoverLetterId`, `SavedSearchId`, `FollowUpId` — strongly-typed IDs (record struct med Guid wrapped)
- `Money` (amount: decimal, currency: Currency)
- `SalaryRange` (min: Money, max: Money, type: SalaryType)
- `Location` (city, region, postalCode, countryCode, coordinates?)
- `SsykCode` (code: string) — svensk yrkeskod
- `SsykTaxonomyPath` — hierarkisk yrkeshiearki
- `EmploymentType` enum: Permanent, FixedTerm, Substitute, Hourly, Internship
- `WorkMode` enum: OnSite, Remote, Hybrid
- `EmailAddress`, `PhoneNumber`, `Url` — validerade wrappers
- `MatchScore` (value: int 0-100, breakdown: MatchBreakdown)
- `MatchBreakdown` (ssykOverlap: 0-100, titleSimilarity: 0-100, skillMatch: 0-100, requirementCoverage: 0-100, locationFit: 0-100, employmentTypeFit: 0-100, matchedKeywords: IReadOnlyList<string>, missingKeywords: IReadOnlyList<string>) — deterministisk "Fast mode", förklarbar by design (ADR 0071)
- `SourceReference` (source: string, externalId: string, originalUrl: string)
- `FollowUpChannel` enum: Email, LinkedIn, Phone, Meeting, Other
- `ResumeContent` — strukturerad data: PersonalInfo, List<Experience>, List<Education>, List<Skill>, etc.
- `DateTimeRange` — inclusive range för intervjuer

### 5.3 Application-aggregatet (central hub)

Se arkitekturdiagram i det inledande samtalet. Application är det viktigaste aggregatet.

```csharp
public sealed class Application : AggregateRoot<ApplicationId>
{
    private readonly List<FollowUp> _followUps = new();
    private readonly List<ApplicationNote> _notes = new();

    public JobSeekerId JobSeekerId { get; private set; }
    public JobAdId JobAdId { get; private set; }
    public ResumeVersionId? ResumeVersionId { get; private set; }
    public CoverLetterId? CoverLetterId { get; private set; }
    public ContactId? RecruiterContactId { get; private set; }

    public ApplicationStatus Status { get; private set; }
    public DateTimeOffset AppliedAt { get; private set; }
    public DateTimeOffset? LastStatusChangeAt { get; private set; }

    public IReadOnlyList<FollowUp> FollowUps => _followUps.AsReadOnly();
    public IReadOnlyList<ApplicationNote> Notes => _notes.AsReadOnly();

    public DateTimeOffset LastContactAt =>
        _followUps.Count == 0 ? AppliedAt : _followUps.Max(f => f.OccurredAt);

    private Application() { } // EF Core

    public static Application Submit(
        ApplicationId id,
        JobSeekerId jobSeekerId,
        JobAdId jobAdId,
        ResumeVersionId? resumeVersionId,
        CoverLetterId? coverLetterId,
        DateTimeOffset appliedAt)
    {
        var app = new Application
        {
            Id = id,
            JobSeekerId = jobSeekerId,
            JobAdId = jobAdId,
            ResumeVersionId = resumeVersionId,
            CoverLetterId = coverLetterId,
            Status = ApplicationStatus.Submitted,
            AppliedAt = appliedAt,
            LastStatusChangeAt = appliedAt,
        };
        app.RaiseDomainEvent(new ApplicationSubmittedEvent(id, jobSeekerId, jobAdId, appliedAt));
        return app;
    }

    public void LogFollowUp(FollowUp followUp)
    {
        if (Status.IsTerminal)
            throw new DomainException($"Cannot log follow-up on a {Status.Name} application.");
        _followUps.Add(followUp);
        RaiseDomainEvent(new FollowUpLoggedEvent(Id, followUp.Id, followUp.Channel, followUp.OccurredAt));
    }

    public void AddNote(ApplicationNote note)
    {
        _notes.Add(note);
        RaiseDomainEvent(new ApplicationNoteAddedEvent(Id, note.Id, note.CreatedAt));
    }

    // ADR 0092 D3: transitions are FREE — any status is a valid target. The old
    // CanTransitionTo grind is replaced by an undo-toast + audit + the append-only
    // StatusChange timeline. Only two hard guards remain (soft-delete, self no-op).
    public Result TransitionTo(ApplicationStatus next, IDateTimeProvider clock)
    {
        if (DeletedAt is not null) return Result.Failure(/* borttagen ansökan */);
        if (next == Status) return Result.Success();                 // self-transition no-op
        var previous = Status;
        Status = next;
        LastStatusChangeAt = clock.UtcNow;
        _statusChanges.Add(StatusChange.Create(previous, next, clock.UtcNow)); // timeline
        RaiseDomainEvent(new ApplicationStatusTransitionedDomainEvent(Id, JobSeekerId, previous, next, clock.UtcNow));
        return Result.Success();
    }

    public void AttachTailoredResume(ResumeVersionId versionId)
    {
        if (Status != ApplicationStatus.Draft)
            throw new DomainException("Can only attach a resume version while in draft.");
        ResumeVersionId = versionId;
    }

    public void AttachCoverLetter(CoverLetterId coverLetterId)
    {
        if (Status != ApplicationStatus.Draft)
            throw new DomainException("Can only attach a cover letter while in draft.");
        CoverLetterId = coverLetterId;
    }

    public void MarkGhostedIfStale(DateTimeOffset now, TimeSpan threshold)
    {
        if (Status != ApplicationStatus.Submitted && Status != ApplicationStatus.Acknowledged)
            return;
        if (now - LastContactAt < threshold)
            return;
        TransitionTo(ApplicationStatus.Ghosted, now);
    }
}
```

### 5.4 ApplicationStatus (statusar + rekommenderade övergångar)

Tio statusar. **ADR 0092 D3: övergångar är fria** — `Application.TransitionTo` tillåter
vilken status som helst som mål (framåt, bakåt eller Ghosted). Den tidigare
tillståndsmaskin-grinden (`CanTransitionTo` som kastade) är ersatt av ångra-toast +
full audit + den append-only `StatusChange`-tidslinjen, så varje byte är spårbart och
ångringsbart. Grafen nedan är nu **enbart rådgivande** (`RecommendedNextStatuses`) — den
driver UI-hinten ("Flytta till {nästa steg}" + avsluta/parkera-alternativen), aldrig en
grind. Detta löser #566 (saknade övergångar + drift blir irrelevant när alla byten tillåts).

```csharp
public sealed class ApplicationStatus : SmartEnum<ApplicationStatus>
{
    public static readonly ApplicationStatus Draft = new("Draft", 1);
    public static readonly ApplicationStatus Submitted = new("Submitted", 2);
    // ... Acknowledged(3), InterviewScheduled(4), Interviewing(5), OfferReceived(6),
    //     Accepted(7), Rejected(8), Withdrawn(9), Ghosted(10)

    private readonly HashSet<ApplicationStatus> _recommendedNext = [];

    // ADVISORY ONLY (ADR 0092 D3) — a UI hint, NOT a guard. TransitionTo permits
    // any target regardless of what is listed here. Do not reintroduce enforcement.
    public IReadOnlySet<ApplicationStatus> RecommendedNextStatuses => _recommendedNext;

    // static ctor seeds the conventional forward step + usual closing moves per status
    // (e.g. Submitted → {Acknowledged, Rejected, Withdrawn}); see ApplicationStatus.cs.
}
```

### 5.5 Domain events

Alla domain events implementerar `IDomainEvent` och dispatchas av `SaveChangesInterceptor` efter `SaveChangesAsync`. Events hanteras av Mediator.SourceGenerator `INotificationHandler<>` i Application-lagret.

**Händelser som ska finnas:**

- `JobSeekerRegisteredEvent`
- `ResumeCreatedEvent`, `ResumeVersionCreatedEvent`, `ResumeImprovedEvent` (deterministisk förbättring, ADR 0071)
- `SavedSearchCreatedEvent`, `SavedSearchTriggeredEvent`
- `JobAdIngestedEvent`, `JobAdDismissedEvent`
- `ApplicationSubmittedEvent`, `ApplicationStatusChangedEvent`, `ApplicationNoteAddedEvent`
- `FollowUpLoggedEvent`
- `CoverLetterCreatedEvent` (användarens egen text — ingen AI-generering, ADR 0071)
- `CvReviewedEvent`, `MatchScoreComputedEvent`
- `UserImpersonationStartedEvent`, `UserImpersonationEndedEvent`

Alla events loggas till `AuditLog`-tabellen via en gemensam `AuditLogHandler`.

### 5.6 Konsistensregler (invariants)

- Application.Status är en av de tio statusarna; övergångar är fria (ADR 0092 D3 — `RecommendedNextStatuses` är rådgivande, ej en grind). En borttagen ansökan kan inte byta status, och ett självbyte är en no-op.
- Varje statusbyte registreras append-only på `StatusChange`-tidslinjen, atomiskt med `Status` (samma UnitOfWork)
- FollowUp kan inte loggas på en Application i terminal state
- ResumeVersion kan inte raderas om den är refererad av en icke-terminal Application
- Resume måste ha exakt en `Master`-version
- SavedSearch.Criteria måste ha minst ett kriterium (ej tomt sökning)
- MatchScore.Value är alltid 0–100

---

## 6. API-design

### 6.1 Konventioner

- REST, JSON body
- `/api/v1/...` prefix
- Kebab-case i URL, camelCase i JSON
- `Authorization: Bearer <JWT>` för alla skyddade endpoints
- Pagination: `?page=1&pageSize=20`, response wrappar med `{ items, page, pageSize, totalCount }`
- Error response: Problem Details (RFC 7807), alltid `application/problem+json`
- ETag + If-Match för optimistic concurrency på aggregate-updates
- `X-Correlation-Id` header propageras genom alla lager

### 6.2 Endpoints (grupperade per kontext)

**Auth**
- `POST /api/v1/auth/register`
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/refresh`
- `POST /api/v1/auth/logout`
- `POST /api/v1/auth/forgot-password`
- `POST /api/v1/auth/reset-password`
- `POST /api/v1/auth/verify-email`
- `POST /api/v1/auth/oauth/google`
- `POST /api/v1/auth/oauth/microsoft`

**Me / profil**
- `GET /api/v1/me`
- `PATCH /api/v1/me`
- `GET /api/v1/me/preferences`
- `PATCH /api/v1/me/preferences`
- `DELETE /api/v1/me` (GDPR-radering, soft delete + 30-dagars restore)

**Resumes**
- `GET /api/v1/resumes` (paginerad lista)
- `GET /api/v1/resumes/{id}` (inkl. versions)
- `POST /api/v1/resumes` (ny master eller upload)
- `POST /api/v1/resumes/{id}/upload` (PDF/DOCX, triggar parsing)
- `POST /api/v1/resumes/{id}/versions` (ny version manuellt)
- `POST /api/v1/resumes/{id}/review` (deterministisk granskning → per-kriterium PASS/WARN/FAIL med citerad evidens)
- `POST /api/v1/resumes/{id}/improve` (deterministiska förbättringsförslag som propose-and-approve-diffar, valfritt `jobAdId` för krav/keyword-täckning; ingen prosasyntes)
- `GET /api/v1/resumes/{id}/versions/{versionId}/export?format=pdf|docx`
- `DELETE /api/v1/resumes/{id}`
- `DELETE /api/v1/resumes/{id}/versions/{versionId}`

**Job ads**
- `GET /api/v1/job-ads` (sök, filter, paginerad)
- `GET /api/v1/job-ads/{id}`
- `POST /api/v1/job-ads/{id}/dismiss` (dölj från sök)
- `POST /api/v1/job-ads/{id}/save` (bookmark)
- `POST /api/v1/job-ads/{id}/compute-match` (beräkna deterministisk "Fast mode"-match)
- `GET /api/v1/job-ads/{id}/salary-stats` (SCB-data för SSYK)

**Saved searches**
- `GET /api/v1/saved-searches`
- `POST /api/v1/saved-searches`
- `GET /api/v1/saved-searches/{id}`
- `PATCH /api/v1/saved-searches/{id}`
- `DELETE /api/v1/saved-searches/{id}`
- `POST /api/v1/saved-searches/{id}/run`

**Applications**
- `GET /api/v1/applications` (filter status, datum, company)
- `POST /api/v1/applications` (draft)
- `GET /api/v1/applications/{id}`
- `PATCH /api/v1/applications/{id}` (notes, attach resume version, cover letter)
- `POST /api/v1/applications/{id}/submit`
- `POST /api/v1/applications/{id}/transition` (body: `{ status, occurredAt }`)
- `DELETE /api/v1/applications/{id}` (användar-initierad **hard delete**, #782/ADR 0104 — se §7.3)
- `POST /api/v1/applications/{id}/follow-ups`
- `GET /api/v1/applications/{id}/follow-ups`
- `POST /api/v1/applications/{id}/notes`
- `GET /api/v1/applications/pipeline` (board-style aggregation)
- `GET /api/v1/applications/stats` (avslags-analys, pipeline-konvertering)

**Cover letters**
- `POST /api/v1/cover-letters` (skapa, body: `{ applicationId, tone }` — användarens egen text, ingen AI)
- `GET /api/v1/cover-letters/{id}`
- `PATCH /api/v1/cover-letters/{id}`
- `POST /api/v1/cover-letters/{id}/detect-cliches` (deterministisk klysch-flaggning mot kurerad lista)
- `GET /api/v1/cover-letters/{id}/export?format=pdf|docx`

**Contacts / companies**
- `GET /api/v1/companies`
- `GET /api/v1/companies/{id}`
- `GET /api/v1/contacts`
- `POST /api/v1/contacts`
- `PATCH /api/v1/contacts/{id}`

**Integrations**
- `POST /api/v1/integrations/gmail/connect` (OAuth-start)
- `POST /api/v1/integrations/gmail/callback` (OAuth-callback)
- `GET /api/v1/integrations/gmail/status`
- `DELETE /api/v1/integrations/gmail` (disconnect)
- `POST /api/v1/integrations/gmail/sync-now`
- Samma mönster för `google-calendar`

> **AI settings / BYOK-endpoints utgår (ADR 0071):** `me/ai-settings`,
> `me/ai-keys`, `me/ai-usage`, `me/credits` byggs aldrig — ingen AI/LLM, ingen
> BYOK, inga credits.

**Admin (role = Admin eller SuperAdmin)**
- `GET /api/v1/admin/users`
- `GET /api/v1/admin/users/{id}`
- `POST /api/v1/admin/users/{id}/suspend`
- `POST /api/v1/admin/users/{id}/unsuspend`
- `POST /api/v1/admin/users/{id}/reset-password`
- `POST /api/v1/admin/users/{id}/impersonate` (SuperAdmin only, returnerar temporär JWT)
- `GET /api/v1/admin/audit-log?from&to&userId&action&aggregateType`
- `GET /api/v1/admin/job-sources/status`
- `POST /api/v1/admin/job-sources/{source}/resync`

**Health & meta**
- `GET /api/health`
- `GET /api/ready`
- `GET /api/meta/version`

---

## 7. Datamodell

### 7.1 Primära tabeller (PostgreSQL, snake_case)

```sql
-- Identity (ASP.NET Core Identity-defaults utökas)
users                           -- IdentityUser
user_roles, roles, role_claims, user_claims, user_logins, user_tokens

-- Core domain
job_seekers
  id (uuid PK)
  user_id (uuid FK users)
  display_name (text)
  preferences (jsonb)            -- flexibel VO
  created_at, updated_at, deleted_at (soft delete)

resumes
  id (uuid PK)
  job_seeker_id (uuid FK)
  name (text)
  created_at, updated_at, deleted_at

resume_versions
  id (uuid PK)
  resume_id (uuid FK)
  kind (text: 'master'|'improved')   -- 'improved' = deterministisk förbättring (ADR 0071); ingen LLM-skräddarsöm
  tailored_for_job_ad_id (uuid FK null)  -- mål-annons för keyword/krav-täckning (deterministisk)
  content (jsonb)                -- ResumeContent VO
  created_at, updated_at

saved_searches
  id (uuid PK)
  job_seeker_id (uuid FK)
  name (text)
  criteria (jsonb)
  notification_enabled (boolean)
  last_run_at (timestamptz null)
  created_at, updated_at, deleted_at

companies
  id (uuid PK)
  name (text)
  org_number (text null)         -- svenskt organisationsnummer
  website (text null)
  industry (text null)
  size_bucket (text null)        -- '1-10','11-50','51-200',etc.
  research_brief (jsonb null)
  research_brief_updated_at (timestamptz null)
  created_at, updated_at

contacts
  id (uuid PK)
  company_id (uuid FK null)
  full_name (text)
  title (text null)
  email (text null)
  linkedin_url (text null)
  phone (text null)
  added_by_job_seeker_id (uuid FK)
  created_at, updated_at, deleted_at

job_ads
  id (uuid PK)                   -- vår egen id
  source (text)                  -- 'platsbanken', 'eures', ...
  external_id (text)
  source_url (text)
  company_id (uuid FK null)
  title (text)
  description (text)
  description_html (text)
  ssyk_code (text null)
  employment_type (text)
  work_mode (text)
  location (jsonb)
  salary (jsonb null)
  deadline_at (timestamptz null)
  published_at (timestamptz)
  ingested_at (timestamptz)
  raw_payload (jsonb)            -- komplett JobTech-JSON
  UNIQUE(source, external_id)

applications
  id (uuid PK)
  job_seeker_id (uuid FK)
  job_ad_id (uuid FK)
  resume_version_id (uuid FK null)
  cover_letter_id (uuid FK null)
  recruiter_contact_id (uuid FK null)
  status (text)                  -- enum name
  applied_at (timestamptz)
  last_status_change_at (timestamptz)
  notes_summary (text null)
  ghosted_threshold_days (int default 21)
  created_at, updated_at, deleted_at

follow_ups
  id (uuid PK)
  application_id (uuid FK, ON DELETE CASCADE)
  channel (text)
  occurred_at (timestamptz)
  note (text null)
  outcome (text null)
  created_at

application_notes
  id (uuid PK)
  application_id (uuid FK, ON DELETE CASCADE)
  content (text)
  created_at

cover_letters
  id (uuid PK)
  job_seeker_id (uuid FK)
  application_id (uuid FK null)
  content (text)                 -- användarens egen text (ingen AI-generering, ADR 0071)
  tone (text)
  created_at, updated_at

-- Matching (read model) — deterministisk "Fast mode" (ADR 0071); ingen Deep mode
match_scores
  id (uuid PK)
  job_seeker_id (uuid FK)
  job_ad_id (uuid FK)
  resume_version_id (uuid FK null)
  score (int)
  breakdown (jsonb)              -- matchade/saknade nyckelord m.m. (förklarbar by design)
  computed_at (timestamptz)
  UNIQUE(job_seeker_id, job_ad_id, resume_version_id)

-- Ingen ai_operations- eller byok_credentials-tabell (ADR 0071): ingen AI/LLM,
-- ingen BYOK, inga token-/credit-räknare.

-- Audit
audit_log
  id (uuid PK)
  occurred_at (timestamptz)
  correlation_id (uuid)
  user_id (uuid FK null)
  impersonated_by (uuid FK null)
  event_type (text)
  aggregate_type (text null)
  aggregate_id (uuid null)
  payload (jsonb)
  ip_address (inet null)
  user_agent (text null)
  -- retention: 90 dagar (hantera via partitionering per dag)

-- Notifications
notifications
  id (uuid PK)
  job_seeker_id (uuid FK)
  type (text)
  title (text)
  body (text)
  read_at (timestamptz null)
  action_url (text null)
  created_at

email_log
  id (uuid PK)
  to_address (text)
  subject (text)
  template (text)
  sent_at (timestamptz)
  provider_message_id (text null)   -- provider-neutralt (SES borttaget, ADR 0066; transaktionell väg = TD-101)
  status (text)

-- Integrations
oauth_connections
  id (uuid PK)
  job_seeker_id (uuid FK)
  provider (text)                -- 'gmail', 'google-calendar'
  encrypted_access_token (bytea)
  encrypted_refresh_token (bytea)
  scopes (text[])
  expires_at (timestamptz)
  created_at, updated_at, disconnected_at
  UNIQUE(job_seeker_id, provider)

-- Reference data
ssyk_salary_stats
  ssyk_code (text PK)
  median_sek (int)
  p10_sek (int)
  p90_sek (int)
  source (text)                  -- 'SCB'
  updated_at (timestamptz)
```

### 7.2 Indexeringsstrategi

Alla FK-kolumner har index. Utöver det:
- `job_ads (source, external_id)` UNIQUE
- `job_ads (published_at DESC)` — senaste först
- `job_ads (ssyk_code)` för filtrering
- `job_ads USING gin(to_tsvector('swedish', title || ' ' || description))` — full-text search
- `applications (job_seeker_id, status, last_status_change_at DESC)` — för pipeline-vy
- `match_scores (job_seeker_id, score DESC)` — för "topp-matchningar"
- `audit_log (occurred_at DESC)` + partitionering per dag

### 7.3 Soft delete-strategi

- Alla user-ägda aggregates har `deleted_at` (timestamptz null)
- Global EF Core query filter på alla soft-deletable entities
- Hard delete efter 30 dagar via schedulerad Hangfire-job
- `DELETE /me` sätter `deleted_at` på alla aggregat tillhörande användaren
- Restore-endpoint (`POST /api/v1/admin/users/{id}/restore`) återställer inom 30 dagar
- **Undantag — användar-initierad per-ansökan-radering** (#782/ADR 0104): `DELETE
  /api/v1/applications/{id}` ("Radera ansökan") är en **hard delete** — raden + barnen
  (follow_ups/application_notes/application_status_changes via FK-cascade) tas bort
  omedelbart, audit-raden (`Application.Deleted`) överlever (Art. 5(2)). INTE soft:
  copyn lovar "kan inte ångras", ingen per-ansökan-sweeper finns, och GDPR-minimering
  (Art. 5(1)(c)/(e)) gynnas av omedelbar utplåning. Konto-nivåns `SoftDelete`-cascade
  (ADR 0024) och aggregatets `Application.SoftDelete` är orörda.

### 7.4 Migrations

- EF Core migrations i `Jobbliggaren.Infrastructure/Persistence/Migrations/`
- Namn: `20260418_InitialSchema`, `20260420_AddImpersonationClaim`, etc.
- Aldrig redigera applied migration — skapa ny
- Migration körs automatiskt i Api-startup i dev/staging, manuellt i prod
- Seed-data för reference (SSYK) körs via separat `Seed`-kommando

---

## 8. Deterministiska CV- och matchnings-motorer

> **Princip (ADR 0071, ersätter ADR 0051).** Produkten innehåller **ingen
> AI/LLM** och ingen BYOK. `IAiProvider`/`IAiProviderResolver`, `CvTailor`,
> credit/BYOK-systemet och `AiProviderKind` byggs **aldrig**. Jobbliggaren är
> gratis utan abonnemang; kostnaden för även ett magert LLM-lager (API-spend,
> DPIA/SCC/TIA-compliance, opt-in-UX, credits) är oförenlig med
> gratis-produkt-taket. Inget tredjelands-PII-transfer kvarstår — ingen
> CV-PII skickas till någon AI-provider, så ADR 0051:s fem GDPR-villkor
> upplöses. Allt nedan är **deterministiskt**: regex, list-lookup,
> datum-aritmetik, taxonomi-lookup och lokal NLP på VPS:en. En intern kriterie-analys
> visar att ~70 % av
> rubric-kriterierna är ren determinism, ~26 % determinism + lokal NLP, och
> bara ~4 % (mening-för-mening-prosa, ton, profilsyntes, annons-skräddarsöm)
> är genuint LLM-gatade — dessa är **uteslutna ur scope**.

### 8.1 CV-granskningsmotor — per-kriterium PASS/WARN/FAIL

En regelbaserad motor producerar en verdict per kriterium (PASS / WARN / FAIL)
**med citerad textevidens** ur det uppladdade CV:t, mappad mot kunskapsbankens
rubric (den versionerade kunskapsbanken). Scoringen är **kategori-primär**
(viktade kategorisummor — Innehåll, Struktur, Språk, ATS-parsbarhet, Visuell
kvalitet) med separata profiler för ATS-optimerad respektive visuell rendering
där innehållskriterierna delas. Rubriken är **versionerad** (`rubric@major.minor.patch`);
`rubric_version` lagras med varje bedömning.

- **Kritiska auto-fails lyfts separat**, oavsett totalpoäng: personnummer (B4),
  stavfel/grammatik (C1), fel filformat (D1), inga mätbara resultat (A1).
- **Kategori-score är primär UX**, totalscore sekundär — motverkar
  Goodhart-effekten.
- **Personnummer-guard** (regex, GDPR + civic-utility) är **högst prioriterat**:
  ett CV som innehåller svenskt personnummer (helt eller fyra sista) flaggas
  och användaren uppmanas stryka det före submit. Motorn uppmanar **aldrig**
  användaren att lägga in personnummer eller andra känsliga uppgifter (IMY).
- **Reducerad precision dokumenteras, ej missrapporteras:** kriterier som utan
  LLM blir svårbedömda (t.ex. karriärprogression A5, genuin grammatik C1) märks
  "ej bedömt v1" i output i stället för att rapportera fel verdict.

### 8.2 Matchningsmotor — "Fast mode" (taxonomi + lexikalt)

Matchningsscoren byggs som en deterministisk **"Fast mode"** och beräknas
gratis för alla synliga annonser:

- **SSYK nivå-4-overlap** (annonsens taxonomi mot CV-härledd SSYK)
- **Titellikhet** (stammad strängjämförelse)
- **Keyword/skill-overlap** (stammad, mot JobTech-taxonomin)
- **Kravtäckning** (parsade annonskrav mot CV-innehåll)
- **Region- och anställningsform-match**

"Deep mode" (LLM-baserad semantisk matchning) är **inställd**. Den
deterministiska scoren är **förklarbar by design**: matchade och saknade
nyckelord visas för användaren, vilket är arkitektoniskt överlägset en
LLM-black-box för en civic-utility-produkt — inte bara billigare.

### 8.3 CV-bygg/förbättringsmotor — diagnostik och struktur

Bygg/förbättra-motorn täcker:

- **Mall-rendering**: ATS-plain och visuell från **samma JSON-källdata**
  (QuestPDF) så att innehållskriterierna är identiska och bara rendering skiljer
- **Custom färgpalett** (WCAG-validerad, kontrast ≥ 4,5:1)
- **Svensk och engelsk** output
- **ATS-sanering** (strip av icke-standardtecken, tabellstrukturer, textrutor)
- **Klysch-flaggning** mot en kurerad svensk lista
- **Action-verb-förslag** ur en kurerad lista
- **Strukturell/format-normalisering** (sektionsordning, datumstandardisering)
- **Personnummer- / foto- / GPA-strip** (deterministiskt, GDPR; foto-default = av
  för SE-marknad)

Alla operationer producerar **propose-and-approve-diffar** — inget appliceras
utan explicit användarbekräftelse. Det uppfyller no-hallucination-kravet by
construction: en regelmotor kan inte hitta på kvalifikationer användaren saknar.

**Uteslutet (LLM-gatat):** mening-för-mening-prosaomskrivning,
tonjustering, profiltext-syntes, annons-skräddarsöm (`CvTailor`). Gränsen:
*determinism kan DIAGNOSTISERA och STRUKTURERA prosa, men inte SYNTETISERA den.*

### 8.4 SSYK-härledning via taxonomi-lookup (ADR 0040 re-scopad)

Smart CV-baserat sparat-sök-filter (ADR 0040, Proposed) härleder SSYK
**deterministiskt**: yrkestitel → SSYK nivå 4 via JobTech-taxonomin, med
**obligatorisk användarbekräftelse** innan en `SavedSearch` skapas. ADR 0040:s
transparens- och bekräftelsekrav är fullt bevarade; bara härlednings­mekanismen
ändras (LLM-inferens → taxonomi-lookup). Titlar som saknas i taxonomin
auto-mappas inte (fallback: manuellt SSYK-val, samma UX som bekräftelsesteget).

### 8.5 Interfaces (illustrativa — namngivning fastställs i Fas 4-design)

Motorerna lever i Application/Infrastructure per Clean Architecture; de exakta
signaturerna binds av dotnet-architect vid Fas 4-design (Last Responsible
Moment). Illustrativ form:

```csharp
namespace Jobbliggaren.Application.Common.Interfaces;

// Granskar CV mot den versionerade rubriken; returnerar per-kriterium-verdict
// med citerad evidens. Inga externa anrop — ren determinism + lokal NLP.
public interface ICvReviewEngine
{
    Task<CvReviewResult> ReviewAsync(
        ParsedResume resume,
        RenderProfile profile,          // Ats | Visual
        CancellationToken ct);
}

// Deterministisk "Fast mode"-matchning; förklarbar (matchade/saknade nyckelord).
public interface IMatchScorer
{
    Task<MatchScore> ScoreAsync(JobAdId jobAdId, ResumeId resumeId, CancellationToken ct);
}

// Föreslår förbättringar som propose-and-approve-diffar; applicerar aldrig själv.
public interface ICvImprovementEngine
{
    Task<IReadOnlyList<ProposedChange>> SuggestAsync(ParsedResume resume, CancellationToken ct);
}

public enum RenderProfile { Ats, Visual }
public enum CriterionVerdict { Pass, Warn, Fail, NotAssessed }
```

### 8.6 Kurerade datakällor och lokal NLP

- **Rubric, klysch-lista och action-verb-lista** är **versionerad data/config**
  (versionerad kunskapsbank), aldrig hårdkodade C#-strängar.
- **Lokal NLP-tier** (~26 % av kriterierna) körs på VPS:en utan externa anrop:
  tokenisering, svensk stemming, POS-taggning. Biblioteken (Catalyst,
  libstemmer.net, WeCantSpell.Hunspell + sv_SE-ordlista) är **flaggade i ADR
  0071 Beslut 6 men ej inlåsta i §3.1** förrän dotnet-architect/CTO-GO + Klas
  spec-edit-approve (se §3.1-notis). PdfPig / DocumentFormat.OpenXml / QuestPDF
  är redan godkända och täcker PDF/DOCX/render-tiern.

---

## 9. Extern integration

### 9.1 JobTech (Arbetsförmedlingen)

**Huvud-APIer i v1:**
- `JobSearch` — sök annonser med filter. https://jobsearch.api.jobtechdev.se/
- `JobStream` — streaming av nya/uppdaterade annonser. https://jobstream.api.jobtechdev.se/
- `Taxonomy` — SSYK-kod-referens, kompetensbegrepp. https://taxonomy.api.jobtechdev.se/
- `JobAd Enrichments` — kompetens-extraktion (används för Fast match). https://jobad-enrichments-api.jobtechdev.se/

**Implementation:**
- `IJobTechClient` interface, implementation via Refit
- `PlatsbankenJobSource : IJobSource`
- Sync-strategi: JobStream-prenumeration för realtid + JobSearch för backfill
- Retry via `Microsoft.Extensions.Http.Resilience` (Polly v8 under huven): 3 försök med exponential backoff
- Circuit breaker efter 5 consecutive failures, cooldown 5 min
- Hangfire-job `SyncPlatsbankenJob` kör var 10:e minut (JobStream-subscription) + nattlig full backfill

**Dataflöde:**
1. JobStream pushar/polls nya annonser
2. Varje annons parsas → `JobAdSnapshot`
3. Upsert i `job_ads` (unique `source+external_id`)
4. Kompetensextraktionsanrop till Enrichments API, cache 7 dagar
5. `JobAdIngestedEvent` raisas → matchning mot alla aktiva SavedSearches triggas

### 9.2 Gmail-sync

- Google Workspace OAuth 2.0 flow
- Scopes: `gmail.readonly` (minimal)
- User-consent-skärm visar exakt vad vi gör: "Jobbliggaren läser inkomna mejl från adresser du märkt som rekryterare för att automatiskt logga uppföljningar"
- Implementation: `IGmailSyncService`
- Sync-strategi: Pub/Sub via Gmail API history (`users.history.list`), fallback till polling var 15:e min
- Hantering av tokens: refresh token lagras envelope-krypterat i `oauth_connections`
- Användaren kan disconnecta när som helst → raderar token + stoppar sync

**Matchningslogik:**
1. Hämta inkomna mejl sedan sista sync
2. För varje mejl: kolla om `from`-adressen matchar en `Contact.email` eller innehåller domän som matchar en `Company.website`
3. Om match: försök hitta öppen `Application` där `recruiter_contact_id` eller `company_id` matchar
4. Skapa `FollowUp` med channel=Email, occurred_at=mejlets datum, note=subject (första 200 tecken)
5. Notifiera användaren i app

### 9.3 Google Calendar

- OAuth 2.0
- Scopes: `calendar.events` (läsa + skriva egna events)
- När användaren sätter status till `InterviewScheduled`, skapar appen ett calendar event
- iCal-export via egen endpoint som genererar `.ics`-fil (inget OAuth krävs)

> **Gmail-sync (§9.2) + Google Calendar (§9.3) skjuts upp — utgår ur MVP (#321,
> Klas-beslut 2026-07-10).** GDPR-/drift-kostnaden är hög mot single-box- (~$16
> CAX31, ADR 0050) och gratis-constraintsen — känslig `gmail.readonly`-OAuth-
> appverifiering, per-användare-envelopekryptering av refresh-token och en
> synk-last var 15:e minut per användare — och inget annat blockeras av att
> skjuta upp den. Det användarnära behovet (aldrig missa en intervju/deadline)
> täcks i stället av påminnelse-notiser i appen på översikten (t.ex. "Du har en
> intervju bokad om 3 dagar"), spårat som **#726**. §9.2/§9.3-specarna ovan
> bevaras som framtida referens om en extern-integrations-fas återupptas efter
> beta.

### 9.4 SCB (Statistiska centralbyrån)

- Användar Pxweb-API för lönestatistik: https://api.scb.se/OV0104/v1/doris/sv/ssd
- Tabellen `AM/AM0110/AM0110A/LonArbsSNI2025` eller motsvarande löne-per-SSYK
- Månatlig import via Hangfire → `ssyk_salary_stats`-tabellen

> **AI-provider-integration utgår (ADR 0071).** Tidigare §9.5/§9.6 specade
> Anthropic Direct API för BYOK respektive systemnyckel. Produkten innehåller
> ingen AI/LLM — ingen `Anthropic`-NuGet, ingen `api.anthropic.com`-klient,
> ingen Bedrock-adapter, inget tredjelands-inferensanrop. CV- och
> matchnings-funktionerna är deterministiska (§8).

---

## 10. Frontend-arkitektur

### 10.1 Next.js 16 App Router-struktur

```
/web/jobbliggaren-web
  /app
    /(marketing)               -- publika sidor
      /page.tsx               -- landing
      /om
      /priser
      /integritet
    /(auth)
      /logga-in
      /registrera
      /glomt-losenord
    /(app)                     -- autentiserat
      /layout.tsx             -- app shell, navigation
      /instrumentpanel        -- dashboard
      /jobb                   -- discovery
        /page.tsx             -- lista + filter
        /[id]/page.tsx        -- detaljvy
      /sokningar              -- saved searches
      /ansokningar            -- pipeline
        /page.tsx             -- tabell
        /[id]/page.tsx        -- detalj
        /pipeline/page.tsx    -- status-grupperad vy
      /statistik              -- avslags-analys + pipeline-konvertering (#313).
        /page.tsx             -- Top-level, INTE /ansokningar-nästlad: en sub-route
                              -- fångas av @modal/(.)ansokningar/[id]-intercepten på
                              -- soft-nav (samma skäl som /aktivitetsrapport — #316/#332).
      /cv
        /page.tsx
        /[id]/page.tsx
      /brev                   -- cover letters
      /foretag                -- companies + contacts
      /kalender               -- upcoming events
      /installningar
        /profil
        /integrationer        -- Gmail, Calendar
        /aviseringar
    /(admin)                   -- role=Admin+
      /anvandare
      /audit
      /jobbkallor
  /components
    /ui                        -- shadcn komponenter (customiserade)
    /layout
    /job-ad
    /application
    /resume
    /admin
  /lib
    /api                       -- API-klient (auto-genererad från OpenAPI)
    /auth                      -- session, JWT
    /hooks
    /utils
  /styles
    /globals.css               -- Tailwind + custom tokens
  /public
    /fonts                     -- Source Sans 3
    /logo.svg
```

**Deployment:** Next.js-frontend körs som en `next start`-container i samma Docker Compose-stack på Hetzner CAX31 som backend (ADR 0050 Beslut 3, amenderad 2026-06-14). `next build` körs i CI; endast den färdiga imagen shippas till boxen.

### 10.2 Data fetching-mönster

- Server components för initial rendering (paginerade listor, detaljvyer)
- TanStack Query för mutation-heavy UI (statusändringar, notes)
- Form state: React Hook Form + Zod schema
- Optimistic updates för statustransitions
- Skeleton/progressiv rendering för CV-granskning och mall-rendering (deterministiskt, inget LLM-streaming)

### 10.3 State management

- Ingen global store — server state via TanStack Query, local UI state via useState/useReducer
- Auth state via Auth.js session context
- Command palette (⌘K) med custom hook, knappas via shadcn-kommandokomponent

### 10.4 Sökupplevelse (jobb)

- URL-driven state: alla filter i query params så URL:en är delbar
- Debounced text search (300 ms)
- Facet counts visade inline ("Stockholm (142), Göteborg (87)")
- Server-paginerad tabell
- Inline match-score med färgkodning (muted: grå/gul/grön)
- "Räkna om match"-knapp per rad (deterministisk "Fast mode", gratis)

### 10.5 Tillgänglighet

- WCAG 2.1 AA som golv
- Keyboard-first: alla flöden navigerbara utan mus
- `role`, `aria-*` korrekt satta
- Fokusring synlig, svensk ledsagartext
- Hög kontrast (lägsta ratio 4.5:1 för body, 3:1 för stora rubriker)
- Testat mot NVDA + VoiceOver

### 10.6 Språk

- UI på svenska
- Admin-UI på svenska
- Inga hårdkodade strängar — alla via `messages/sv.json` (next-intl)
- Engelska som fallback för teknikorienterade fel ("Internal server error") men primärt "Ett fel uppstod, försök igen"

---

## 11. Auth & Authorization

### 11.1 Roller

- `User` — default, får hantera egen data
- `Admin` — admin-funktioner utom impersonation
- `SuperAdmin` — Admin + impersonation + feature flags

Roles lagras i `user_roles` (Identity).

### 11.2 JWT-flöde

> (jfr ADR 0014 som förfinar refresh-mekaniken — refresh tokens lagras i DB,
> access-token-`jti` i Redis; ADR 0014 avviker medvetet från beskrivningen nedan.)

- Åtkomsttoken: 15 min, signerad med RS256, claims inkluderar `sub`, `roles`, `impersonating_by` (null vid icke-impersonation)
- Refresh token: 14 dagar, opaque, lagrad i httpOnly cookie
- Refresh-rotation: varje användning av refresh token ger ny refresh token, gammal invalideras
- Revokation-lista i Redis för refresh tokens

### 11.3 Impersonation-flöde

1. SuperAdmin klickar "Logga in som [user]" i admin-UI
2. Backend verifierar SuperAdmin-roll
3. Backend utfärdar ny JWT med `sub=targetUser.Id`, `impersonating_by=adminUser.Id`, TTL 30 min
4. `UserImpersonationStartedEvent` raisas → audit log
5. UI:t visar banner "Du ser appen som [användarnamn]. Avsluta impersonation."
6. Alla handlingar i impersonation-sessionen har båda user-IDs i audit
7. Banner-knapp "Avsluta" → `POST /api/v1/auth/end-impersonation` → återgår till admin-session

### 11.4 Authorization-policies

- `[Authorize]` på alla endpoints utom `/auth/*`, `/health`
- `[Authorize(Roles = "Admin,SuperAdmin")]` för admin-endpoints
- Resource-based authorization: user kan bara läsa/skriva egna resumes, applications, etc.
- Implementerat via `IAuthorizationRequirement`-handlers som injiceras i Mediator.SourceGenerator-pipelinen

---

## 12. Design system

Se [`DESIGN.md`](./DESIGN.md) för komplett specifikation: färgtokens, typografi, komponenter, copy-riktlinjer.

**Viktigaste principer att komma ihåg under utveckling:**
- Civic-utility-estetik: tabeller före kort, hierarki före dekoration
- Grön accent `#15603F` som enda interaktionsfärg (`--jp-accent-*`-ramp, ADR 0068 — ersätter tidigare myndighetsblå)
- Inga emojis i UI, inga exklamationstecken, inga gradients (enda undantag: hero-plattans scopade gröna gradient, ADR 0068)
- Rak svensk copy: kvantifierad information först
- `border-radius` 6px-golv för rader/kort/knappar, 12px endast hero (ADR 0052), pills/badges undantagna
- Exakta tokens (färg/typografi/spacing/radius) ägs av DESIGN.md + design-skills

---

## 13. Säkerhet & GDPR

### 13.1 Dataklassificering

| Klass | Exempel | Hantering |
|-------|---------|-----------|
| Känsligt | CV-innehåll, cover letters, OAuth-tokens | Kryptera at rest, logga aldrig |
| Personligt | Namn, email, ansökningar | Standard GDPR, logga ej i klartext |
| Operationellt | JobAd-data | Offentligt, cacha fritt |

### 13.2 Encryption

**At rest:**
- Databas: co-tenant PostgreSQL på Hetzner CAX31 (ADR 0050); disk-/volym-kryptering på VPS-nivå
- Backup: nattlig `pg_dump` klient-side-krypterad (age) → Hetzner-EU Storage Box (ADR 0050 Beslut 4, TD-107)
- PII-fält (`cover_letter`, `resume_versions.content` m.fl.) och OAuth-tokens:
  per-användar-DEK envelope encryption via `IDataKeyProvider`
  (Local AES-256-GCM eller KMS, config-switchat per ADR 0066/0049) — extra lager
  utöver databas-kryptering

**In transit:**
- TLS 1.3 överallt
- HSTS + preload
- Certificate pinning i mobilklient (framtida)

**Secrets-hantering per miljö:**
- `local`: `appsettings.Local.json` (gitignored) + `.env` för frontend; committade defaults i `appsettings.Development.json`
- permanent miljö (Hetzner): self-managed på VPS (systemd-credentials / sops+age, ADR 0050 + TD-106); master-nyckel aldrig plaintext-på-disk (TD-102)
- `IConfiguration`-abstraktionen gör att koden är identisk oavsett källa; endast DI-registreringen skiljer

### 13.3 GDPR-flöden

**Registerutdrag (Art. 15):**
- `GET /api/v1/me/export` genererar ZIP med alla användardata som JSON + originalfiler
- Delivered via signerad nedladdnings-URL, giltig 24 h (lagring på Hetzner-box / EU-storage, ADR 0050)
- Loggas som `DataExportRequestedEvent`

**Rätt till radering (Art. 17):**
- `DELETE /me` sätter `deleted_at` på alla aggregat
- 30-dagars restore-fönster
- Hard delete-job rensar efter 30 dagar
- Härledda CV-artefakter (parsad `ResumeContent`, granskningsresultat, match_scores) raderas samtidigt
- Audit log behålls i 90 dagar (rättslig grund)

**Dataportabilitet (Art. 20):**
- Export i strukturerad JSON + DOCX för CVs

**Samtyckeslog:**
- Alla samtycken (TOS, privacy) sparas i `user_consents` med version + timestamp

### 13.4 Subprocessors

Upprätthålls i publik lista på `/integritet#subprocessors` (publiceras när
permanent infra aktiveras; listan nedan speglar **beslutad** uppsättning, ADR 0050):
- Infrastruktur (hosting/databas/backup): Hetzner Cloud (EU — Falkenstein/Nuremberg/Helsinki) inkl. Hetzner-EU Storage Box för krypterad backup
- DNS / CDN / proxy: Cloudflare (gratis-tier, "Full (strict)")
- **Ingen AI-subprocessor** (ADR 0071): produkten har ingen AI/LLM, ingen
  CV-PII lämnar systemet, inget tredjelands-transfer. CV- och matchnings-motorerna
  är deterministiska och körs på egen infra.
- Google (Gmail/Calendar, frivilligt, global)
- Sentry (errors, EU) — planerat
- PostHog self-hosted (analytics, EU — inte subprocessor)

> AWS (infrastruktur + SES) är avvecklat (ADR 0066) och utgår ur subprocessor-
> kedjan. Hetzner/Cloudflare läggs till i den publika listan vid faktisk
> provisionering (ADR 0050 Sekvensering).

### 13.5 Säkerhetshygien

- `dotnet-outdated` + `npm audit` körs i CI, bryter build vid kritiska CVEs
- Secrets aldrig i kod — allt via managed secrets-store eller miljövariabler (lokalt: `appsettings.Local.json`, gitignored)
- `dotnet format` + ESLint/Prettier i pre-commit (Husky)
- Rate limiting per IP + per user på alla endpoints (AspNetCoreRateLimit eller custom middleware)
- CORS restriktivt: bara `jobbliggaren.se`-domäner
- CSP: strict, script-src 'self'
- Weekly dependency update via Dependabot

---

## 14. Observability

### 14.1 Logging

- `Microsoft.Extensions.Logging` — strukturerad loggning; console (stdout) + Seq-sink
- Sinks: console (stdout) + persistent strukturerad **Seq**-sink via `Seq.Extensions.Logging` (MEL-provider, config-gated på `Seq:ServerUrl`); dev lokal Seq (`localhost:5341`), prod Seq self-hosted EU (TD-104, ADR 0050)
- Log levels:
  - `Trace`/`Debug`: dev only
  - `Information`: normala request-flows (start/slut av handlers)
  - `Warning`: validation failures, rate limits, degraded dependencies
  - `Error`: exceptions, failed external calls (JobTech, Gmail, SCB)
  - `Critical`: crashing errors
- Alla logs har `CorrelationId`, `UserId`, `OperationType`
- Känslig data (CV-innehåll, parsad CV-text) loggas **aldrig** i klartext

### 14.2 Traces

- OpenTelemetry (exporter/backend definieras med observability-sinken, TD-104)
- Trace från frontend genom backend till DB/external (JobTech, Gmail, SCB)
- Sampling: 100% i dev, 10% i prod

### 14.3 Metrics

- `http.request.duration`, `.count`, `.error_rate`
- `cv.review.duration`, `match.compute.duration` (deterministiska motorer)
- `jobtech.sync.duration`, `.new_ads`, `.errors`
- `application.status_change.count` per transition
- Exposeras på `/metrics` för Prometheus-format (om vi behöver senare)

### 14.4 Alerting

Alarms (plattform med observability-sinken, TD-104; extern uptime-monitor
UptimeRobot/BetterStack free ersätter ALB/CloudWatch-health per ADR 0050):
- Backend 5xx rate > 1% över 5 min → email
- JobTech sync misslyckas 3 gånger i rad → email
- Databas CPU > 80% i 10 min → email

### 14.5 Product analytics (PostHog)

- Self-hosted PostHog i EU (placering på/bredvid Hetzner-infra, ADR 0050)
- Auto-capture off, explicit event-tracking
- Events: `job_searched`, `application_submitted`, `cv_reviewed`, `cv_improved`, `cliche_detected`, `match_computed`, etc.
- Session recording av för integritet (kan slås på per användare via admin-flag)
- Feature flags via PostHog

---

## 15. Infrastruktur & deployment

> **Status (2026-06-08):** Den AWS-baserade deploy-arkitekturen är **avvecklad**
> (ADR 0066) och AWS lämnas permanent. Permanent deploy-mål — **Hetzner Cloud
> CAX31 (ARM, 16 GB) all-in-one Docker Compose (**BE + FE**) + Cloudflare
> (DNS/CDN/proxy)** — är **beslutat i ADR 0050 (Accepted 2026-06-08)** och
> beskrivs nedan. Faktisk provisionering är framtida Klas-gatat arbete (ADR 0050
> Sekvensering: Hetzner sist, vid MVP före beta-testare, med samtliga
> Pre-beta-data-gates lösta + andra security-granskning först).
>
> `infra/terraform/` (den tidigare AWS-stacken) + AWS-deploy-workflowsen
> (`deploy-dev.yml`, `rds-ca-bundle-check.yml`) refererar **avvecklad infra** —
> den AWS-baserade dev-stacken (ECS/ECR/RDS/Redis) är **riven** 2026-05-26
> (ADR 0066, commit `a1d9abd`), inte pausad; bara prod-baseline (~$2/mån: Route 53,
> KMS, CloudTrail, IAM) kvarstår. Filerna är **bevarade som reversibilitets-/
> historik-mekanik** (ADR 0066 Beslut 1 + ADR 0069 D3) och retireras via egen
> teardown-ADR/PR vid Hetzner-cutover, inte i en städ-PR. `deploy-dev.yml`:s
> auto-trigger (`push: tags v*-dev`) är **borttagen** 2026-06-28 så den inte kan
> köra mot riven infra; endast manuell `workflow_dispatch` kvarstår.

### 15.1 Deploy-layout (ADR 0050, Accepted)

**Backend — en Hetzner Cloud CAX31** (8 vCPU shared ARM Ampere Altra / 16 GB RAM
/ 160 GB NVMe / 20 TB trafik, ~€16/mån, EU-DC Falkenstein/Nuremberg/Helsinki).
Hela backend-stacken kör i **Docker Compose** på boxen: .NET API + .NET Worker
+ PostgreSQL (co-tenant container, ingen managed-DB) + Redis + **Caddy**
(reverse proxy, auto-TLS via Let's Encrypt DNS-01 mot Cloudflare). `mem_limit`
sätts hybrid — hård cap på Worker + Redis (skydda Postgres mot
ingestion-OOM), generös/osatt på Postgres (data-durabilitet); Bulkhead-principen
(ADR 0050 mem_limit-not, TD-106).

**Frontend — Next.js co-tenant container på CAX31.** FE körs som en `next start`-container i samma Compose-stack bakom Caddy (ADR 0050 Beslut 3, amenderad 2026-06-14). `next build` körs i CI; endast den färdiga imagen shippas till boxen (build-toppen belastar aldrig RAM-feldomänen). FE-footprint (~0,5 GB under last) ryms i CAX31:s headroom.

**Edge — Cloudflare gratis-tier** framför boxen (TLS-edge / DNS / CDN / DDoS):
**"Full (strict)"** mot giltigt origin-cert på Caddy (aldrig "Flexible") +
origin-IP-lockdown (origin accepterar bara Cloudflare-IP:er på 443) + HSTS
(ADR 0050 Beslut 4, gate M-5 i TD-106). Caddy reverse-proxiar två upstreams (API
på port 5000 + `next start`-FE på localhost:3000 för icke-`/api`-vägar); "Full
(strict)" + origin-IP-lockdown + HSTS täcker hela ursprunget.

**Backup — Hetzner-EU Storage Box** (~€3/mån/1 TB): nattlig `pg_dump`
klient-side-krypterad (age) → Storage Box i samma EU-jurisdiktion (Cloudflare R2
avvisat pga CLOUD Act-tredjelandsöverföring av icke-krypterad pg_dump-PII).
Backups ligger INTE på boxens 160 GB. Retention/rotation + restore-drill = TD-107.

**Single-box blast-radius** (API/Worker/Postgres/Redis delar OS + RAM + feldomän)
är ett medvetet beta-skala-val (ADR 0050 Negativa konsekvenser); CAX31:s 16 GB +
per-service `mem_limit` ger headroom. NBomber-lasttest mot 46k-korpuset (ADR 0045)
körs före cutover för att validera sizing empiriskt.

Den tidigare AWS-layouten (VPC/ECS/RDS/ElastiCache/S3/Bedrock/Route 53) finns
dokumenterad i ADR 0066 + sessions som historik.

### 15.2 IaC (ADR 0050)

Befintlig AWS-Terraform under `infra/terraform/` bevarad som reversibilitets-
mekanik (ADR 0066 Beslut 1), retireras via egen ADR/PR vid Hetzner-cutover.
Hetzner-provisioneringen är compose-centrerad (en box, Docker Compose + Caddy);
VPS-härdnings-baseline (SSH-key-only, brandvägg, fail2ban, auto-patch, PG/Redis
ej publika, swap/core-dump-hygien) = gate M-6, hemvist TD-106.

### 15.3 CI/CD

**Aktivt nu (PR-flöde per ADR 0065):**

`build.yml` (`ci`-aggregat):
- Trigger: PR mot `main`, push till `main`
- Jobs: backend build + test, frontend lint/typecheck/test, coverage-gate (ADR 0044)
- Inga moln-anrop, inga deploys

Observe-only-jobb (lighthouse / loadtest / audit per ADR 0045) blockerar ej merge.

**Historiskt (deploy — refererar avvecklad AWS-infra):**
Tag-baserad AWS-deploy (`deploy-dev.yml` m.fl.) refererar den **rivna** AWS-dev-stacken
(ADR 0066, 2026-05-26) — avvecklad, inte pausad; auto-triggern är borttagen 2026-06-28.
Ny deploy-pipeline mot **Hetzner** byggs vid cutover (ADR 0050: Compose-push till CAX31 — **FE-image byggs i CI (`next build`) och shippas som container**, ingen Vercel-build).

### 15.4 Deployment-strategi (ADR 0050)

Topologin (Hetzner CAX31 single-box **BE + FE** + Cloudflare) är beslutad; den
exakta deploy-mekaniken (Compose-pull/re-up-ordning, migrations-ordning via
`Jobbliggaren.Migrate`, rollback) detaljeras i Hetzner-fas-arbetet (TD-106). FE-containern bör få sin egen healthcheck i Compose (TD-106).
**Rollback-modell (ADR 0050):** lokal Docker-Compose-stack är
paritets-baselinen (samma image-byggväg som Hetzner-prod) — misslyckad cutover =
återgå till lokal-dev + ej-cutad DNS (Cloudflare). DNS-cutover är den enda
reversibla/irreversibla flippen; tills den sker påverkas ingen live-trafik.
Health-check-kravet `/api/ready` → 200 inom 30 s består oavsett plattform.

---

## 16. Background jobs

### 16.1 Hangfire-setup

- Postgres-storage (`Hangfire.PostgreSql`)
- Dashboard på `/hangfire` skyddad med Admin-roll
- Dedicerad worker-process (separat från Api — egen container i Docker Compose-stacken på Hetzner CAX31, ADR 0050)

### 16.2 Schedulerade jobb

> Speglar `src/Jobbliggaren.Worker/Hosting/RecurringJobRegistrar.cs` (verifierat
> 2026-06-28). Cron-tider är UTC. 30-min-padding mellan natt-jobben ger tydliga
> recovery-fönster (se klassens XML-doc för rationalen).

| Jobb (Hangfire-id) | Schema (UTC) | Beskrivning |
|------|--------|-------------|
| `sync-platsbanken-stream` | `*/10 * * * *` | Pull JobStream (overlap-fönster 15 min), upsert/arkivera annonser |
| `sync-platsbanken-snapshot` | 02:00 daglig | Full snapshot-backfill mot stream-drift |
| `audit-log-retention` | 03:00 daglig | Atomisk partition-DDL, rullar 90-dagars audit-retention (ADR 0024) |
| `retain-platsbanken-job-ads` | 03:15 daglig | Snapshot-miss-retention (ADR 0032-amend) |
| `background-matching` | 03:20 daglig | Per-user matchnings-scan: JobAds → `UserJobAdMatch` (ADR 0080 Våg 4) |
| `expire-job-ads` | 03:45 daglig | `ExpiresAt`-cron, defense-in-depth (ADR 0032-amend) |
| `hard-delete-accounts` | 04:00 daglig | Permanent radera soft-deleted efter 30 dagar (GDPR Art. 17) |
| `purge-stale-raw-payloads` | 04:30 daglig | Rensa mognad `raw_payload`-jsonb (TD-73 p2) |
| `reap-stranded-matches` | 04:45 daglig | `UserJobAdMatch` fast i Queued → terminal Failed (TD-114) |
| `backfill-field-encryption` | 05:00 daglig | DEK-backfill av PII-fält (ADR 0049) |
| `parsed-resume-retention` | 05:15 daglig | GDPR-svep av mognad ParsedResume-staging (TD-111, ADR 0074) |
| `digest-dispatch-daily` | 06:00 daglig | Strong-match-digest, daglig kadens (ADR 0080 Våg 4 PR-4b) |
| `digest-dispatch-weekly` | måndag 06:00 | Strong-match-digest, veckovis kadens (civic-default) |
| `refresh-landing-stats` | `*/5 * * * *` | Publik landing-stats pre-compute (ADR 0064) |

**Planerat / ej registrerat** (speccat men inte byggt — finns inte i
`RecurringJobRegistrar`):

| Jobb | Avsedd funktion | Status |
|------|--------|-------------|
| `RunSavedSearchesJob` | Kör sparade sökningar → notiser | Ej byggt (#312) |
| `SendFollowUpRemindersJob` | Uppföljnings-påminnelser | Ej byggt (Fas 5) |
| `SyncGmailJob` | Per-user Gmail-sync | Ej byggt (Fas 5, #321) |
| `ImportScbSalaryStatsJob` | Månatlig SCB-lönestatistik | Ej byggt (#320, backlog) |

### 16.3 Fire-and-forget jobb

Triggas av handlers för:
- Skicka välkomst-email
- Generera exportfil efter export-request
- Skicka invite-email
- Uppdatera SCB-data när SSYK-kod ändras

---

## 17. Testing

### 17.1 Test-pyramiden

**Domain unit tests** (Jobbliggaren.Domain.UnitTests, ~70% av antalet tester)
- Aggregate-invariants, state machines, value objects
- Ingen databas, ingen I/O
- Använder xUnit + Shouldly (ersätter FluentAssertions efter dess kommersialisering 2025)
- Target coverage på Domain: **>90%**

**Application unit tests** (Jobbliggaren.Application.UnitTests, ~20%)
- Handlers mot in-memory fakes/mocks (NSubstitute)
- xUnit + Shouldly + NSubstitute
- Testar use case-logik utan Infrastructure

**Integration tests** (Jobbliggaren.Api.IntegrationTests, ~10%)
- Testcontainers för Postgres + Redis (ephemeral per test-klass)
- WebApplicationFactory
- Shouldly för assertions
- Happy-path + nyckel-felscenarion per endpoint
- Kör i CI med `dotnet test --filter Category=Integration`

**Architecture tests** (Jobbliggaren.Architecture.Tests)
- NetArchTest-regler:
  - Domain beror inte på Infrastructure/Application/Api
  - Application beror inte på Infrastructure
  - Alla endpoints har auth-attribute (eller explicit `[AllowAnonymous]`)
  - Alla aggregates ärver `AggregateRoot<>`

**E2E tests** (jobbliggaren-web-tests, Playwright)
- Kritiska användarflöden: registrera → skapa sökning → söka jobb → submit ansökan → logga follow-up
- Kör lokalt / i CI mot lokal stack (staging-miljön är avvecklad, ADR 0066)
- Max 15-20 tester (dyra att underhålla, håll tight)

### 17.2 Testdata

- `TestDataBuilder`-klasser per aggregate (fluent builder-pattern)
- Inga `.sql` seed-filer i tests — bygg data via builders för tydlighet

### 17.3 Motor-tester (deterministiska — ADR 0071)

Motorerna är deterministiska → testbara som vanlig kod (inga mockade
AI-provider-anrop, inga icke-deterministiska evals):

- **Granskningsmotor:** golden-set av svenska CV med förväntad per-kriterium-verdict
  (PASS/WARN/FAIL); samma input → samma output, assertas exakt
- **Personnummer-guard:** regex-tester (helt personnummer + fyra sista, positiva/negativa)
- **Matchningsmotor ("Fast mode"):** kända "bra fit"/"dålig fit"-par med förväntad
  score + att matchade/saknade nyckelord surfas korrekt (förklarbarhet)
- **Mall-rendering:** ATS-plain + visuell från samma JSON → snapshot/struktur-assertion
- **Rubric-versionering:** `rubric_version` lagras med varje bedömning; N-1-kompatibilitet
- Körs i CI (deterministiska → inga flakes, ingen separat manuell eval-körning)

---

## Bilaga A — Viktiga externa referenser

- JobTech Dev: https://jobtechdev.se / https://data.arbetsformedlingen.se
- JobSearch API docs: https://jobsearch.api.jobtechdev.se
- Taxonomy API: https://taxonomy.api.jobtechdev.se
- SCB Pxweb API: https://api.scb.se/OV0104/v1/doris/sv/ssd
- GOV.UK Design System: https://design-system.service.gov.uk
- Digg (svensk digital förvaltning): https://www.digg.se
- WCAG 2.1 AA: https://www.w3.org/TR/WCAG21/
- EU AI Act: https://artificialintelligenceact.eu

---

## Bilaga B — Arkitekturbeslut (ADRs)

ADR:er lagras i `docs/decisions/` och namnges `NNNN-slug.md`. Den **auktoritativa
listan (SSOT)** över alla registrerade och planerade ADRs — med status, datum och
korsreferenser — underhålls i **[`docs/decisions/README.md`](./docs/decisions/README.md)**
(av `docs-keeper`-agenten). Denna bilaga duplicerar inte indexet; se README:n för
aktuell uppsättning. Nya ADRs skapas via `/new-adr` (adr-keeper); nästa lediga
nummer hämtas ur indexet.

---

**Slut på BUILD.md.** Nästa läsning: [`CLAUDE.md`](./CLAUDE.md) för kodningsstandarder och [`DESIGN.md`](./DESIGN.md) för design-specifikation.
