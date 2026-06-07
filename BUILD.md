# JobbPilot — Build Specification

> **Version:** 1.0 · draft
> **Status:** approved by product owner (Klas Olsson)
> **Last updated:** 2026-04-18
> **Companion docs:** [`CLAUDE.md`](./CLAUDE.md), [`DESIGN.md`](./DESIGN.md)

---

## 1. Översikt

### 1.1 Vision

JobbPilot är en komplett jobbsök- och ansökningshanterare för den svenska arbetsmarknaden. Appen kombinerar JobTech/Platsbanken-integration med modern AI-assistans men positioneras medvetet som en *civic utility* — ett verktyg som signalerar tillit, pålitlighet och professionalism snarare än hajp. Målet är att stressade jobbsökare får ett verktyg som känns som en förlängning av svensk offentlig digital service (1177, Försäkringskassan, Digg) snarare än ett av hundra AI-produkter som alla ser likadana ut.

### 1.2 Positionering

**JobbPilot är:**
- Svensk-först (Platsbanken, SCB, svenska rekryteringskultur)
- Kvalitet över volym — inga auto-apply-funktioner
- AI-assisterad där det ger tydligt värde, aldrig "AI-genererat för syns skull"
- GDPR-säker med äkta EU-datalokalisering
- Öppen för att låta användaren koppla egna AI-nycklar (BYOK)

**JobbPilot är inte:**
- Ännu en ChatGPT-wrapper
- Ett mass-apply-verktyg som LoopCV eller Sonara
- En ATS-keyword-stuffer
- En jobbmarknad / rekryteringstjänst

### 1.3 Målgrupp

**Primär (v1):** Aktiva jobbsökare i Sverige. Initial kohort: Klas + ~20 klasskamrater på NBI/Handelsakademin (.NET/fullstack-utvecklare).

**Sekundär (v2+):** Bredare svensk arbetsmarknad — tjänstemän, utvecklare, generella kunskapsarbetare. Betalande användare via freemium.

**Tertiär (framtid):** Internationella användare när `IJobSource`-adapters finns för NAV (Norge), Arbejdsformidlingen (Danmark), EURES (EU).

### 1.4 Success criteria

| Mätpunkt | Mål v1 |
|----------|--------|
| Klas använder appen dagligen i 14 dagar utan Excel-fallback | Ja/nej |
| 2–3 Team 7 Debuggers-klasskamrater testar i internal beta | Ja/nej |
| Alla hela klassens 20 testare får onboardade konton | Ja/nej |
| Time-to-first-application (från inloggning till skickad ansökan) | < 15 min |
| AI-genererat personligt brev behöver < 30% manuell redigering | Subjektivt mått |
| Ingen GDPR-hink — EU-inferens fungerar end-to-end | Verifierat |

---

## 2. Feature scope

### 2.1 In scope för v1

**Discovery**
- Hämta platsannonser från JobTech JobSearch API (Platsbanken)
- Full-text-sökning + facetterad filtrering (region, yrke, SSYK-kod, anställningsform, distans-ok, publiceringsdatum)
- Sparade sökningar med namn och notifieringsinställning per sökning
- Taxonomi-baserad matchningsscore (Fast mode) — gratis, beräknas för alla synliga annonser
- LLM-baserad matchningsscore (Deep mode) — endast på begäran eller topp-N, kostar credits
- Smarta rekommendationer: "Du har inte tittat på matchningar över 70% — 3 nya sen igår"
- Lönestatistik-overlay per annons: hämta medianlön för SSYK-koden från SCB och visa inline

**Application management**
- Full pipeline-tracker med status state machine (Draft → Submitted → Acknowledged → InterviewScheduled → Interviewing → OfferReceived → Accepted/Rejected/Withdrawn/Ghosted)
- FollowUp-loggning per ansökan (kanal: Email, LinkedIn, Telefon, Annat; datum; anteckning; utfall)
- Kalenderintegration: intervjuer som events, bidirektionell sync mot Google Calendar + iCal-export
- Påminnelser: "Du kontaktade inte Acme efter 10 dagar — dags att följa upp?"
- Automatisk "Ghosted"-transition efter X dagar utan svar (konfigurerbart, default 21)
- Notes per ansökan (fritext, datumstämplade journalinlägg)
- Avslags-analys: dashboard-vy som visar trender över tid (vilka branscher, vilka roller, konverteringsgrader)

**AI-assistans**
- CV-parsing från PDF/DOCX → strukturerad `ResumeContent` (hybrid: PdfPig/OpenXml text-extraktion + Haiku LLM för strukturering)
- Skräddarsytt CV per annons (Sonnet): behåller orginal-CV intakt, skapar ny versioned `ResumeVersion` med annons-anpassad ton och nyckelord
- AI-genererat personligt brev (Sonnet): på svenska, följer användarens skriv-DNA från tidigare brev
- Anti-klyscha-detektor (Haiku): markerar "brinner för", "driven team-player", "passion" och föreslår konkreta alternativ
- Företagsresearch-brief (Sonnet + web_search): 1-pager per företag med senaste nyheter, teknik-stack (om tech-roll), kulturell signal

**Integrationer (v1)**
- Gmail-sync: OAuth-anslutning, appen skannar inkorgen efter ansökningssvar och auto-loggar som FollowUps
- Google Calendar: OAuth, skapa events för intervjuer, mappa till Application
- SCB lönestatistik: periodisk import av medianlöner per SSYK
- iCal-export: alla intervjuer som `.ics`-fil

**Admin (v1)**
- Användarhantering: lista alla konton, sök, filter, detaljvy, suspendera, mjukradera, återställ lösenord
- Impersonation: "logga in som Klas" via temporär JWT med `impersonating_by` claim, alla handlingar dubbel-taggade i audit log
- Token-användning per användare: dashboard med kostnad per användare över tid (input/output tokens, per modell, per operation)
- Audit-sökning: filtrera på tid, användare, action, aggregate
- Jobbkälla-status: senaste sync per källa, antal nya annonser per dag, error rate, circuit breaker state

### 2.2 Explicitly out of scope för v1

Dessa är medvetet uppskjutna men arkitekturen ska inte blockera dem:

- Kanban-vy (drag & drop-kolumner som Huntr) — v1 kör tabell-vy enligt civic-design
- Chrome extension — stor egen kodbas, browser-store-godkännande, fokusbov
- Rekryterar-enrichment (Hunter.io/Apollo) — GDPR-komplex, ToS-gråzon
- Intervjuträning (AI-genererade frågor) — bra feature men inte kritisk för v1
- Mobilappar (iOS/Android) — desktop-first
- Electron desktop-app
- LinkedIn-integration (deras API är nästan värdelös)
- Bemanningsföretagsscraping (Manpower, Academic Work, etc.)
- Monster/Jobbland-integration
- Stripe / betalningar
- Team-funktioner / delade sökningar
- PWA offline-mode

### 2.3 Feature prioritering för roadmap

Se §18 Utvecklingsfaser.

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
| Mapping | Mapster | 10.x | Snabbare än AutoMapper, kodgenerering |
| Background jobs | Hangfire | 1.8.x | Postgres-storage |
| Smart enum | Ardalis.SmartEnum | 8.x | State machines i domänen |
| Logging | Serilog | 4.x | Seq (lokal dev); produktions-sink TBD (ADR 0050) |
| Observability | OpenTelemetry | 1.15+ | Traces + metrics |
| PDF parsing | PdfPig | 0.1.14+ | Text extraction |
| DOCX parsing | DocumentFormat.OpenXml | 3.x | Microsoft-underhåll |
| PDF generation | QuestPDF | 2026.2.x | Community MIT free under USD 1M revenue; `QuestPDF.Settings.License = LicenseType.Community` i startup |
| DOCX generation | DocumentFormat.OpenXml | 3.x | Template-baserad |
| AI SDK | Anthropic (officiell NuGet) | 12.x | MIT, Anthropic Direct API för **både** systemnyckel och BYOK (Bedrock utgår, ADR 0051) |
| HTTP | HttpClientFactory + Refit | 10.x | JobTech-klient |
| Database | PostgreSQL | 18.3 | lokal Docker Compose nu; managed host TBD (ADR 0050) |
| Cache | Redis | 8.6 | lokal Docker Compose nu; managed host TBD (ADR 0050) |
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
| Typografi | Hanken Grotesk | Google Fonts | Primär; Inter som fallback |

### 3.2 Infrastruktur

> **Statusbanner (2026-06-06):** AWS-dev-stacken är avvecklad (ADR 0066) och
> AWS lämnas permanent (Klas-direktiv 2026-06-06). All utveckling kör nu lokalt
> på laptop (Docker Compose: postgres + redis + seq). Permanent deploy-mål
> (plan: Hetzner CX-serie BE + Vercel FE + Cloudflare) specificeras i **ADR 0050
> (Proposed)** och fastställs när den flippas till Accepted. Tabellen nedan
> beskriver **nuläge (lokalt)** + **TBD-pekare** — inga molnkonfig-värden fylls
> i förrän Hetzner-deploy-ADR:n är beslutad. AWS-kolumnerna i ADR/sessions/
> research bevaras som historik.

| Tjänst | Nuläge (lokal dev) | Permanent mål |
|--------|--------------------|---------------|
| Compute (backend/worker) | `dotnet run` lokalt | TBD — Hetzner (ADR 0050 Proposed) |
| Database | PostgreSQL 18.3 (Docker Compose) | TBD (ADR 0050) |
| Cache | Redis 8.6 (Docker Compose) | TBD (ADR 0050) |
| Object storage | lokal disk / ej aktiverat | TBD — S3-kompatibel (ADR 0050: Cloudflare R2) |
| AI inferens | Anthropic Direct API (system + BYOK), opt-in per ADR 0051; AI-lager Fas 4 (ej byggt) | Anthropic Direct API (ADR 0051) |
| Email | `ConsoleEmailSender` → Seq (ADR 0066) | TBD — transaktionell mejlväg (TD-101) |
| Secrets | `appsettings.Local.json` (gitignored) | TBD (ADR 0050) |
| Encryption keys | `LocalDataKeyProvider` AES-256-GCM (ADR 0066) | TBD — self-managed nyckelmodell (TD-102) |
| Frontend | `pnpm dev` (localhost:3000) | TBD — Vercel (ADR 0050 Proposed) |
| DNS / CDN | — | TBD — Cloudflare (ADR 0050) |
| Logging / monitoring | Seq (lokal) | TBD (ADR 0050) |
| Errors | — | Sentry (EU) planerat |
| CI | GitHub Actions (build + test + coverage, inga moln-anrop) | oförändrat |
| IaC | `infra/terraform/` bevarad som reversibilitets-mekanik (ADR 0066 Beslut 1) | retireras via egen ADR vid Hetzner-cutover |

### 3.3 Miljöer

> **Status (2026-06-06):** dev/staging/production-AWS-miljöerna är avvecklade
> (ADR 0066). `local` är enda aktiva miljön tills permanent deploy-mål är
> beslutat (ADR 0050). Tag-baserad deploy (`v*-dev`/`v*-rc*`/`v*`) är pausad —
> deploy-workflowsen (`deploy-dev.yml` m.fl.) bevarade men inaktiva tills
> Hetzner-deploy-ADR:n definierar ny pipeline.

| Miljö | Syfte | Deployment | Status |
|-------|-------|-----------|--------|
| local | Utveckling | Docker Compose | **Aktiv** |
| dev / staging / production | Integration / pre-prod / live | TBD (ADR 0050) | Avvecklad (ADR 0066) |

PR-flöde mot `main` per ADR 0065 (CI-gate). Permanent deploy-strategi och
miljö-topologi fastställs i ADR 0050 när den flippas till Accepted.

---

## 4. Systemarkitektur

### 4.1 Lager (Clean Architecture)

```
┌─────────────────────────────────────────────────────┐
│  Presentation / Interfaces                          │
│  ├─ JobbPilot.Api          (REST endpoints)         │
│  ├─ JobbPilot.Worker       (Hangfire host)          │
│  └─ JobbPilot.Web          (Next.js, extern)        │
├─────────────────────────────────────────────────────┤
│  JobbPilot.Infrastructure                           │
│  ├─ Persistence (EF Core, migrations)               │
│  ├─ Identity                                        │
│  ├─ JobSources.Platsbanken                          │
│  ├─ AiProviders.Anthropic   (Fas 4, ej byggt)       │
│  ├─ Email (ConsoleEmailSender; SES borttagen)       │
│  ├─ Security (Local/Kms DEK-provider, ADR 0066)     │
│  ├─ CalendarIntegration.Google                      │
│  ├─ GmailSync                                       │
│  ├─ Salary.Scb                                      │
│  └─ BackgroundJobs (Hangfire setup)                 │
├─────────────────────────────────────────────────────┤
│  JobbPilot.Application                              │
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
│  ├─ AiAssist                                        │
│  └─ Admin                                           │
├─────────────────────────────────────────────────────┤
│  JobbPilot.Domain  (PURE, no external deps)         │
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
/JobbPilot.sln
/src
  /JobbPilot.Domain
  /JobbPilot.Application
  /JobbPilot.Infrastructure
  /JobbPilot.Api
  /JobbPilot.Worker
/web
  /jobbpilot-web             (Next.js)
/tests
  /JobbPilot.Domain.UnitTests
  /JobbPilot.Application.UnitTests
  /JobbPilot.Api.IntegrationTests    (Testcontainers + WebApplicationFactory)
  /JobbPilot.Architecture.Tests
  /jobbpilot-web-tests       (Playwright e2e)
/infra
  /terraform
    /modules
    /environments
/prompts                      (AI prompts som .prompt.md filer)
/docs
  /ADR                        (Architecture Decision Records)
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
| `Company` | AR | — | — |
| `Contact` | AR | — | `CompanyId` (opt.) |
| `Application` | AR | `FollowUp` (entity), `ApplicationNote` (entity) | `JobSeekerId`, `JobAdId`, `ResumeVersionId`, `CoverLetterId`, `ContactId` |
| `CoverLetter` | AR | — | `JobSeekerId`, `JobAdId`, `ApplicationId` |

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
- `MatchBreakdown` (taxonomyOverlap: 0-100, skillMatch: 0-100, locationFit: 0-100, salaryFit: 0-100, aiReasoning: string?)
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

    public void TransitionTo(ApplicationStatus next, DateTimeOffset occurredAt)
    {
        if (!Status.CanTransitionTo(next))
            throw new DomainException($"Invalid transition {Status.Name} → {next.Name}.");
        var previous = Status;
        Status = next;
        LastStatusChangeAt = occurredAt;
        RaiseDomainEvent(new ApplicationStatusChangedEvent(Id, previous, next, occurredAt));
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

### 5.4 ApplicationStatus (state machine)

```csharp
public sealed class ApplicationStatus : SmartEnum<ApplicationStatus>
{
    public static readonly ApplicationStatus Draft = new(nameof(Draft), 0);
    public static readonly ApplicationStatus Submitted = new(nameof(Submitted), 1);
    public static readonly ApplicationStatus Acknowledged = new(nameof(Acknowledged), 2);
    public static readonly ApplicationStatus InterviewScheduled = new(nameof(InterviewScheduled), 3);
    public static readonly ApplicationStatus Interviewing = new(nameof(Interviewing), 4);
    public static readonly ApplicationStatus OfferReceived = new(nameof(OfferReceived), 5);
    public static readonly ApplicationStatus Accepted = new(nameof(Accepted), 6);
    public static readonly ApplicationStatus Rejected = new(nameof(Rejected), 7);
    public static readonly ApplicationStatus Withdrawn = new(nameof(Withdrawn), 8);
    public static readonly ApplicationStatus Ghosted = new(nameof(Ghosted), 9);

    public bool IsTerminal => this == Accepted || this == Rejected || this == Withdrawn;

    public bool CanTransitionTo(ApplicationStatus next)
    {
        if (IsTerminal) return false;
        if (next == Withdrawn) return true;

        return (this.Name, next.Name) switch
        {
            (nameof(Draft), nameof(Submitted)) => true,
            (nameof(Submitted), nameof(Acknowledged)) => true,
            (nameof(Submitted), nameof(Rejected)) => true,
            (nameof(Submitted), nameof(Ghosted)) => true,
            (nameof(Acknowledged), nameof(InterviewScheduled)) => true,
            (nameof(Acknowledged), nameof(Rejected)) => true,
            (nameof(Acknowledged), nameof(Ghosted)) => true,
            (nameof(InterviewScheduled), nameof(Interviewing)) => true,
            (nameof(InterviewScheduled), nameof(Rejected)) => true,
            (nameof(Interviewing), nameof(OfferReceived)) => true,
            (nameof(Interviewing), nameof(InterviewScheduled)) => true,
            (nameof(Interviewing), nameof(Rejected)) => true,
            (nameof(OfferReceived), nameof(Accepted)) => true,
            (nameof(OfferReceived), nameof(Rejected)) => true,
            _ => false,
        };
    }

    private ApplicationStatus(string name, int value) : base(name, value) { }
}
```

### 5.5 Domain events

Alla domain events implementerar `IDomainEvent` och dispatchas av `SaveChangesInterceptor` efter `SaveChangesAsync`. Events hanteras av Mediator.SourceGenerator `INotificationHandler<>` i Application-lagret.

**Händelser som ska finnas:**

- `JobSeekerRegisteredEvent`
- `ResumeCreatedEvent`, `ResumeVersionCreatedEvent`, `ResumeTailoredWithAiEvent`
- `SavedSearchCreatedEvent`, `SavedSearchTriggeredEvent`
- `JobAdIngestedEvent`, `JobAdDismissedEvent`
- `ApplicationSubmittedEvent`, `ApplicationStatusChangedEvent`, `ApplicationNoteAddedEvent`
- `FollowUpLoggedEvent`
- `CoverLetterGeneratedEvent`
- `MatchScoreComputedEvent`
- `AiOperationCompletedEvent` (för token-tracking)
- `UserImpersonationStartedEvent`, `UserImpersonationEndedEvent`

Alla events loggas till `AuditLog`-tabellen via en gemensam `AuditLogHandler`.

### 5.6 Konsistensregler (invariants)

- Application.Status är alltid giltig enligt state machine
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
- `POST /api/v1/resumes/{id}/tailor` (AI-skräddarsytt, `jobAdId` i body)
- `GET /api/v1/resumes/{id}/versions/{versionId}/export?format=pdf|docx`
- `DELETE /api/v1/resumes/{id}`
- `DELETE /api/v1/resumes/{id}/versions/{versionId}`

**Job ads**
- `GET /api/v1/job-ads` (sök, filter, paginerad)
- `GET /api/v1/job-ads/{id}`
- `POST /api/v1/job-ads/{id}/dismiss` (dölj från sök)
- `POST /api/v1/job-ads/{id}/save` (bookmark)
- `POST /api/v1/job-ads/{id}/compute-match` (trigger Deep match)
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
- `DELETE /api/v1/applications/{id}` (soft delete)
- `POST /api/v1/applications/{id}/follow-ups`
- `GET /api/v1/applications/{id}/follow-ups`
- `POST /api/v1/applications/{id}/notes`
- `GET /api/v1/applications/pipeline` (board-style aggregation)
- `GET /api/v1/applications/stats` (avslags-analys, pipeline-konvertering)

**Cover letters**
- `POST /api/v1/cover-letters/generate` (AI, body: `{ applicationId, tone, extraContext }`)
- `GET /api/v1/cover-letters/{id}`
- `PATCH /api/v1/cover-letters/{id}`
- `POST /api/v1/cover-letters/{id}/detect-cliches`
- `GET /api/v1/cover-letters/{id}/export?format=pdf|docx`

**Contacts / companies**
- `GET /api/v1/companies`
- `GET /api/v1/companies/{id}`
- `POST /api/v1/companies/{id}/research-brief` (AI + web)
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

**AI settings / BYOK**
- `GET /api/v1/me/ai-settings`
- `POST /api/v1/me/ai-keys` (lägg till BYOK-nyckel, krypteras med KMS)
- `DELETE /api/v1/me/ai-keys/{provider}`
- `GET /api/v1/me/ai-usage` (token + kostnadsstatistik)
- `GET /api/v1/me/credits` (återstående gratis credits)

**Admin (role = Admin eller SuperAdmin)**
- `GET /api/v1/admin/users`
- `GET /api/v1/admin/users/{id}`
- `POST /api/v1/admin/users/{id}/suspend`
- `POST /api/v1/admin/users/{id}/unsuspend`
- `POST /api/v1/admin/users/{id}/reset-password`
- `POST /api/v1/admin/users/{id}/impersonate` (SuperAdmin only, returnerar temporär JWT)
- `GET /api/v1/admin/ai-usage` (global token-kostnad per användare)
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
  kind (text: 'master'|'tailored')
  tailored_for_job_ad_id (uuid FK null)
  content (jsonb)                -- ResumeContent VO
  ai_provider (text null)
  ai_model (text null)
  ai_tokens_input (int null)
  ai_tokens_output (int null)
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
  content (text)
  tone (text)
  ai_provider (text null)
  ai_model (text null)
  ai_tokens_input (int null)
  ai_tokens_output (int null)
  created_at, updated_at

-- Matching (read model)
match_scores
  id (uuid PK)
  job_seeker_id (uuid FK)
  job_ad_id (uuid FK)
  resume_version_id (uuid FK null)
  depth (text)                   -- 'fast' | 'deep'
  score (int)
  breakdown (jsonb)
  computed_at (timestamptz)
  UNIQUE(job_seeker_id, job_ad_id, resume_version_id, depth)

-- AI usage
ai_operations
  id (uuid PK)
  job_seeker_id (uuid FK)
  operation_type (text)          -- 'cv_parse', 'cv_tailor', 'cover_letter_generate', 'match_deep', 'research_brief', 'cliche_detect'
  tenancy (text)                 -- 'system' | 'byok' (båda via Anthropic Direct API, ADR 0051)
  model (text)
  tokens_input (int)
  tokens_output (int)
  cost_usd (numeric)             -- beräknat utifrån pricing-tabell
  credits_used (int)             -- internal credit units
  billed_to (text)               -- 'system' | 'byok'
  succeeded (boolean)
  error_message (text null)
  request_id (uuid)
  occurred_at (timestamptz)

byok_credentials
  id (uuid PK)
  job_seeker_id (uuid FK)
  provider (text)                -- 'anthropic' | 'openai' | 'gemini'
  ciphertext (bytea)             -- KMS envelope-encrypted
  key_id_used (text)             -- KMS key ARN
  fingerprint (text)             -- sha256 av plaintext för display
  created_at, last_used_at

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
  ses_message_id (text null)
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
- `ai_operations (job_seeker_id, occurred_at DESC)`
- `audit_log (occurred_at DESC)` + partitionering per dag

### 7.3 Soft delete-strategi

- Alla user-ägda aggregates har `deleted_at` (timestamptz null)
- Global EF Core query filter på alla soft-deletable entities
- Hard delete efter 30 dagar via schedulerad Hangfire-job
- `DELETE /me` sätter `deleted_at` på alla aggregat tillhörande användaren
- Restore-endpoint (`POST /api/v1/admin/users/{id}/restore`) återställer inom 30 dagar

### 7.4 Migrations

- EF Core migrations i `JobbPilot.Infrastructure/Persistence/Migrations/`
- Namn: `20260418_InitialSchema`, `20260420_AddImpersonationClaim`, etc.
- Aldrig redigera applied migration — skapa ny
- Migration körs automatiskt i Api-startup i dev/staging, manuellt i prod
- Seed-data för reference (SSYK) körs via separat `Seed`-kommando

---

## 8. AI-lager

### 8.1 Interface

```csharp
namespace JobbPilot.Application.Common.Interfaces;

public interface IAiProvider
{
    AiProviderKind Kind { get; }
    AiModelTier DefaultTier { get; }

    Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken ct);
}

public interface IAiProviderResolver
{
    // Väljer provider per användare: systemnyckel vs BYOK. Dispatch-axeln är
    // credential/tenancy (plattformens nyckel vs användarens egen nyckel), INTE
    // vendor — båda grenar talar Anthropic Direct API (Bedrock utgår, ADR 0051).
    Task<IAiProvider> ResolveForUserAsync(
        JobSeekerId userId,
        AiOperationType operationType,
        CancellationToken ct);
}

// Namngivning fastställs i Fas 4 (ADR 0051 — AiProviderKind-namngivning deferred).
// Dispatch-axeln är credential/tenancy, inte vendor.
public enum AiProviderKind { SystemKey, Byok }
public enum AiModelTier { Fast, Deep }     // Fast = Haiku, Deep = Sonnet
public enum AiOperationType { CvParse, CvTailor, CoverLetterGenerate, MatchDeep, ResearchBrief, ClicheDetect, RecommendationReasoning }
```

### 8.2 Modell-mappning

> **Anthropic Direct API för båda vägar (ADR 0051).** Bedrock utgår — ingen
> EU-inference-profil-kolumn. Systemnyckel-AI är **opt-in** (US-processing,
> ADR 0051 Beslut 2); icke-opt-in-användare får ingen systemnyckel-AI.

Mappningen nedan binder *use case → tier*, inte use case → exakt modell-version.
Tier-strategin är det stabila beslutet; de exakta modell-ID:na lever i config
(`appsettings`, source-of-truth) och verifieras mot `https://docs.claude.com`
(ADR 0002-amendment 2026-06-07 — versions-ID upprepas inte i prosa, de ruttnar).

| Use case | Tier |
|----------|------|
| CV-parsing (text → JSON) | Fast |
| Anti-klyscha-detektor | Fast |
| Matchningsscore | Deep |
| Skräddarsytt CV | Deep |
| Personligt brev | Deep |
| Företagsresearch-brief | Deep (+ web search tool) |
| Rekommendations-reasoning | Fast |
| Premium (optional) | Premium |

**Tier → modellfamilj:** Fast = Haiku (snabb/billig), Deep = Sonnet (balans),
Premium = Opus. Exakta ID:n per tier sätts i config (nedan).

Modellnamn hämtas från konfiguration, hårdkodas aldrig. Exempel `appsettings.json`
(ID:na är **exempel** — aktuella verifieras mot docs.claude.com per ADR 0002):

```jsonc
{
  "Ai": {
    // Provider-resolver dispatchar på credential/tenancy (systemnyckel vs BYOK),
    // inte vendor — båda talar Anthropic Direct API (ADR 0051).
    "Anthropic": {
      "ApiVersion": "2023-06-01",
      "Models": {
        // Pinnade exakta ID:n (ADR 0002) — exempelvärden, ej kanon:
        "Fast": "claude-haiku-4-5-20251001",
        "Deep": "claude-sonnet-4-6",
        "Premium": "claude-opus-4-8"
      }
    }
  }
}
```

### 8.3 Credit system

- Free tier: 50 AI-operationer per kalendermånad per användare
- Operationsvikt: `CvParse=1`, `ClicheDetect=1`, `CoverLetterGenerate=3`, `CvTailor=5`, `MatchDeep=2`, `ResearchBrief=4`
- Räknaren återställs 00:00 CET första varje månad
- Vid credit-slut visas meddelande: "Du har använt dina gratis AI-operationer för april. Lägg till egen API-nyckel för obegränsad användning, eller vänta till nästa månad."
- Admin-override: `POST /api/v1/admin/users/{id}/grant-credits`

### 8.4 BYOK-säkerhet

1. Användaren klistrar in nyckel i UI
2. Frontend POST:ar i klartext över HTTPS till backend (TLS enda skyddet på wire)
3. Backend omedelbart:
   - Verifierar nyckeln mot provider (1 billig test-call)
   - Genererar ny AES-256-nyckel (data key) via `IDataKeyProvider`
     (Local AES-256-GCM eller KMS, config-switchat per ADR 0066)
   - Krypterar API-nyckel med data key (AES-GCM)
   - Lagrar `ciphertext` + `encrypted_data_key` (wrappad av provider)
   - Plaintext-nyckeln scrubbas från minnet direkt
4. Vid användning:
   - `IDataKeyProvider` unwrap för data key
   - AES decrypt för API-nyckel
   - Used i-memory, aldrig loggad
5. Admin kan **inte** se BYOK-nycklar — bara fingerprint
6. Nyckel-rotation: `DELETE` + `POST` av användaren; ingen auto-rotation

### 8.5 Prompts

Alla AI-prompts lagras som `.prompt.md`-filer i `/prompts/`-katalogen, inte i C#-kod:

```
/prompts
  /cv-parse.prompt.md
  /cv-tailor.prompt.md
  /cover-letter-generate.prompt.md
  /match-deep.prompt.md
  /research-brief.prompt.md
  /cliche-detect.prompt.md
```

Varje prompt-fil har front matter:

```markdown
---
id: cv-tailor
version: 1.3.0
tier: deep
output: structured-json
schema: CvTailorOutput
---
# System

Du är en svensk karriärrådgivare...

# User

Anpassa följande CV till jobbannonsen nedan:

<cv>
{{cv_content}}
</cv>

<job_ad>
{{job_ad_description}}
</job_ad>

Returnera JSON enligt schema CvTailorOutput.
```

Runtime laddar prompts via `IPromptLibrary`, gör token-substitution, och skickar till provider. Prompts versioneras — ändringar kräver ny version + PR-review.

### 8.6 Svensk språkbehandling

- Alla prompts skrivs på svenska (bättre output i svenska use cases)
- Prompts har explicit "Du är en svensk karriärrådgivare med djup förståelse för svensk arbetsmarknadskultur"
- Anti-klyscha-listan är på svenska: "brinner för", "driven team-player", "passion för", "ödmjuk men ambitiös", etc.
- Företagsresearch baseras på svenska källor först (allabolag.se, DI.se, Computer Sweden, Ny Teknik) innan internationella

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
- Retry med Polly: 3 försök med exponential backoff
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
- User-consent-skärm visar exakt vad vi gör: "JobbPilot läser inkomna mejl från adresser du märkt som rekryterare för att automatiskt logga uppföljningar"
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

### 9.4 SCB (Statistiska centralbyrån)

- Användar Pxweb-API för lönestatistik: https://api.scb.se/OV0104/v1/doris/sv/ssd
- Tabellen `AM/AM0110/AM0110A/LonArbsSNI2025` eller motsvarande löne-per-SSYK
- Månatlig import via Hangfire → `ssyk_salary_stats`-tabellen

### 9.5 Anthropic Direct API (för BYOK)

- HTTP-klient mot `https://api.anthropic.com/v1/messages`
- `anthropic-version: 2023-06-01`
- Streamar respons när UI kan använda det
- Token-usage returneras och loggas per operation

### 9.6 Systemnyckel via Anthropic Direct API (Bedrock utgår — ADR 0051)

- SDK: officiell `Anthropic` NuGet (HTTP mot `https://api.anthropic.com/v1/messages`)
  — **samma klient som BYOK-vägen**; dispatch sker på credential/tenancy, inte vendor
- `AWSSDK.BedrockRuntime` och en Bedrock-adapter byggs **aldrig** (ADR 0051 Beslut 1)
- **Ingen EU-residency-fallback:** Anthropic Direct self-serve är US-only.
  Systemnyckel-AI är därför **opt-in** (ADR 0051 Beslut 2, GDPR Art. 25.2);
  icke-opt-in-användare får ingen systemnyckel-AI (endast BYOK om egen nyckel finns)
- **Fas-4-grind (ADR 0051 Beslut 3, security-auditor GDPR-veto, icke-förhandlingsbar):**
  DPIA (Art. 35) + SCC modul 2 + Schrems II-TIA + Anthropic-DPA + DPF-status-verifiering
  + ADR 0049-cross-ref (decrypt-före-AI = klartext-PII över Atlanten) — alla uppfyllda
  **innan en Fas-4-kodrad skrivs**
- Token-usage returneras och loggas per operation; PII loggas aldrig i klartext

---

## 10. Frontend-arkitektur

### 10.1 Next.js 16 App Router-struktur

```
/web/jobbpilot-web
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
        /statistik/page.tsx   -- avslags-analys
      /cv
        /page.tsx
        /[id]/page.tsx
      /brev                   -- cover letters
      /foretag                -- companies + contacts
      /kalender               -- upcoming events
      /installningar
        /profil
        /ai-nycklar           -- BYOK
        /integrationer        -- Gmail, Calendar
        /aviseringar
        /fakturering          -- v2
    /(admin)                   -- role=Admin+
      /anvandare
      /token-anvandning
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
    /fonts                     -- Hanken Grotesk
    /logo.svg
```

### 10.2 Data fetching-mönster

- Server components för initial rendering (paginerade listor, detaljvyer)
- TanStack Query för mutation-heavy UI (statusändringar, notes)
- Form state: React Hook Form + Zod schema
- Optimistic updates för statustransitions
- Stream AI-responses med Server-Sent Events för cover letter generation

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
- "Räkna om Deep match"-knapp per rad (kostar 2 credits)

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

- Åtkomsttoken: 15 min, signerad med RS256, claims inkluderar `sub`, `roles`, `impersonating_by` (null vid icke-impersonation)
- Refresh token: 14 dagar, opaque, lagrad i httpOnly cookie
- Refresh-rotation: varje användning av refresh token ger ny refresh token, gammal invalideras
- Revokation-lista i Redis för refresh tokens

### 11.3 Impersonation-flöde

1. SuperAdmin klickar "Logga in som [user]" i admin-UI
2. Backend verifierar SuperAdmin-roll
3. Backend utfärdar ny JWT med `sub=targetUser.Id`, `impersonating_by=adminUser.Id`, TTL 30 min
4. `UserImpersonationStartedEvent` raisas → audit log
5. UI:t visar banner "Du ser appen som Klas Olsson. Avsluta impersonation."
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
- Myndighetsblå `#0B5CAD` som primär
- Inga emojis i UI, inga exklamationstecken, inga gradients
- Rak svensk copy: kvantifierad information först
- `border-radius: 4px` max (förutom badges/pills)

---

## 13. Säkerhet & GDPR

### 13.1 Dataklassificering

| Klass | Exempel | Hantering |
|-------|---------|-----------|
| Känsligt | CV-innehåll, cover letters, OAuth-tokens, BYOK-nycklar | Kryptera at rest, logga aldrig |
| Personligt | Namn, email, ansökningar | Standard GDPR, logga ej i klartext |
| Operationellt | JobAd-data | Offentligt, cacha fritt |

### 13.2 Encryption

**At rest:**
- Databas: managed encryption (AES-256) på permanent host (TBD, ADR 0050)
- Object storage: server-side-kryptering (TBD, ADR 0050)
- PII-fält (`cover_letter`, `resume_versions.content` m.fl.), OAuth-tokens och
  BYOK-nycklar: per-användar-DEK envelope encryption via `IDataKeyProvider`
  (Local AES-256-GCM eller KMS, config-switchat per ADR 0066/0049) — extra lager
  utöver databas-kryptering

**In transit:**
- TLS 1.3 överallt
- HSTS + preload
- Certificate pinning i mobilklient (framtida)

**Secrets-hantering per miljö:**
- `local`: `appsettings.Local.json` (gitignored) + `.env` för frontend; committade defaults i `appsettings.Development.json`
- permanent miljö: managed secrets-store (TBD, ADR 0050)
- BYOK-nycklar: ALDRIG i klartext i config — alltid DEK-envelope via `IDataKeyProvider` (§8.4, ADR 0066/0049)
- `IConfiguration`-abstraktionen gör att koden är identisk oavsett källa; endast DI-registreringen skiljer

### 13.3 GDPR-flöden

**Registerutdrag (Art. 15):**
- `GET /api/v1/me/export` genererar ZIP med alla användardata som JSON + originalfiler
- Delivered via signerad nedladdnings-URL, giltig 24 h (lagrings-backend TBD, ADR 0050)
- Loggas som `DataExportRequestedEvent`

**Rätt till radering (Art. 17):**
- `DELETE /me` sätter `deleted_at` på alla aggregat
- 30-dagars restore-fönster
- Hard delete-job rensar efter 30 dagar
- AI-prompts/responses med PII raderas samtidigt
- Audit log behålls i 90 dagar (rättslig grund)

**Dataportabilitet (Art. 20):**
- Export i strukturerad JSON + DOCX för CVs

**Samtyckeslog:**
- Alla samtycken (TOS, privacy, BYOK-data-transfer) sparas i `user_consents` med version + timestamp

### 13.4 Subprocessors

Upprätthålls i publik lista på `/integritet#subprocessors` (publiceras när AI-lagret
och permanent infra aktiveras; listan nedan speglar **planerad** uppsättning):
- Infrastruktur (hosting/databas/storage): TBD — permanent provider beslutas i ADR 0050
- Anthropic (system- + BYOK-AI, **opt-in**, US) — Fas 4, ej aktiverad; gated av
  ADR 0051:s fem GDPR-villkor (DPIA/SCC/TIA/DPA innan flippen är live)
- Google (Gmail/Calendar, frivilligt, global)
- Vercel (frontend, EU) — TBD (ADR 0050)
- Sentry (errors, EU) — planerat
- PostHog self-hosted (analytics, EU — inte subprocessor)

> AWS (infrastruktur + SES) är avvecklat (ADR 0066) och utgår ur subprocessor-
> kedjan. Permanent infra-subprocessor läggs till när ADR 0050 är Accepted.

### 13.5 Säkerhetshygien

- `dotnet-outdated` + `npm audit` körs i CI, bryter build vid kritiska CVEs
- Secrets aldrig i kod — allt via managed secrets-store eller miljövariabler (lokalt: `appsettings.Local.json`, gitignored)
- `dotnet format` + ESLint/Prettier i pre-commit (Husky)
- Rate limiting per IP + per user på alla endpoints (AspNetCoreRateLimit eller custom middleware)
- CORS restriktivt: bara `jobbpilot.se`-domäner
- CSP: strict, script-src 'self' + Vercel CDN
- Weekly dependency update via Dependabot

---

## 14. Observability

### 14.1 Logging

- Serilog structured logging
- Sinks: Seq (lokal dev); produktions-sink TBD (ADR 0050)
- Log levels:
  - `Trace`/`Debug`: dev only
  - `Information`: normala request-flows (start/slut av handlers)
  - `Warning`: validation failures, rate limits, degraded dependencies
  - `Error`: exceptions, failed AI calls
  - `Critical`: crashing errors
- Alla logs har `CorrelationId`, `UserId`, `OperationType`
- Känslig data (CV-innehåll, AI-prompts) loggas **aldrig** i klartext

### 14.2 Traces

- OpenTelemetry (exporter/backend TBD, ADR 0050)
- Trace från frontend genom backend till DB/AI/external
- Sampling: 100% i dev, 10% i prod

### 14.3 Metrics

- `http.request.duration`, `.count`, `.error_rate`
- `ai.operation.duration`, `.tokens_used`, `.cost_usd`
- `jobtech.sync.duration`, `.new_ads`, `.errors`
- `application.status_change.count` per transition
- Exposeras på `/metrics` för Prometheus-format (om vi behöver senare)

### 14.4 Alerting

Alarms (plattform TBD, ADR 0050):
- Backend 5xx rate > 1% över 5 min → PagerDuty/email
- AI-providers error rate > 10% → email
- JobTech sync misslyckas 3 gånger i rad → email
- Databas CPU > 80% i 10 min → email

### 14.5 Product analytics (PostHog)

- Self-hosted PostHog i EU (host TBD, ADR 0050)
- Auto-capture off, explicit event-tracking
- Events: `job_searched`, `application_submitted`, `ai_cover_letter_generated`, `ai_cv_tailored`, `cliche_detected`, `byok_added`, etc.
- Session recording av för integritet (kan slås på per användare via admin-flag)
- Feature flags via PostHog

---

## 15. Infrastruktur & deployment

> **Status (2026-06-06):** Den AWS-baserade deploy-arkitekturen nedan är
> **avvecklad** (ADR 0066) och AWS lämnas permanent (Klas-direktiv 2026-06-06).
> Permanent deploy-mål — plan: **Hetzner** (BE) + **Vercel** (FE) + **Cloudflare**
> (DNS/CDN/R2) — specificeras i **ADR 0050 (Proposed)** och fastställs där.
> Den faktiska deploy-layouten, IaC-strukturen och CI/CD-deploy-pipelinen för
> Hetzner skrivs in här när ADR 0050 flippas till Accepted. **Inga Hetzner-
> konfig-värden fylls i förrän de är beslutade.**
>
> `infra/terraform/` (den tidigare AWS-stacken) + AWS-deploy-workflowsen
> (`deploy-dev.yml`, `rds-ca-bundle-check.yml`) är **bevarade men inaktiva** som
> reversibilitets-mekanik (ADR 0066 Beslut 1). De retireras via egen ADR vid
> Hetzner-cutover, inte i en städ-PR.

### 15.1 Deploy-layout (TBD — ADR 0050)

Permanent topologi (Hetzner BE + Vercel FE + Cloudflare) definieras i ADR 0050.
Den tidigare AWS-layouten (VPC/ECS/RDS/ElastiCache/S3/Bedrock/Route 53) finns
dokumenterad i ADR 0066 + sessions som historik.

### 15.2 IaC (TBD — ADR 0050)

Befintlig AWS-Terraform under `infra/terraform/` bevarad som reversibilitets-
mekanik (ADR 0066). Hetzner-IaC-strategi (Terraform-provider, modul-struktur)
beslutas i ADR 0050.

### 15.3 CI/CD

**Aktivt nu (PR-flöde per ADR 0065):**

`build.yml` (`ci`-aggregat):
- Trigger: PR mot `main`, push till `main`
- Jobs: backend build + test, frontend lint/typecheck/test, coverage-gate (ADR 0044)
- Inga moln-anrop, inga deploys

Observe-only-jobb (lighthouse / loadtest / audit per ADR 0045) blockerar ej merge.

**Pausat (deploy — bevarat men inaktivt):**
Tag-baserad AWS-deploy (`deploy-dev.yml` m.fl.) är pausad efter ADR 0066.
Ny deploy-pipeline mot Hetzner/Vercel definieras i ADR 0050.

### 15.4 Deployment-strategi (TBD — ADR 0050)

Permanent deploy-strategi (rolling/canary, migrations-ordning, rollback)
fastställs i ADR 0050 utifrån vald Hetzner/Vercel-topologi. Health-check-kravet
`/api/ready` → 200 inom 30 s består oavsett plattform.

---

## 16. Background jobs

### 16.1 Hangfire-setup

- Postgres-storage (`Hangfire.PostgreSql`)
- Dashboard på `/hangfire` skyddad med Admin-roll
- Dedicerad worker-process (separat från Api — egen container/service på permanent host, TBD ADR 0050)

### 16.2 Schedulerade jobb

| Jobb | Schema | Beskrivning |
|------|--------|-------------|
| `SyncPlatsbankenJob` | Var 10:e min | Pull JobStream, upsert annonser |
| `SyncPlatsbankenFullBackfillJob` | Daglig 02:00 | Full sync mot JobSearch för robusthet |
| `RunSavedSearchesJob` | Var 30:e min | Kolla nya matchningar mot aktiva searches, skicka notifieringar |
| `DetectGhostedApplicationsJob` | Daglig 03:00 | Transition Submitted/Acknowledged till Ghosted efter threshold |
| `SendFollowUpRemindersJob` | Daglig 09:00 | Email + in-app: "Det var 10 dagar sen du kontaktade X" |
| `SyncGmailJob` | Per-user, var 15:e min | För användare med Gmail-connection |
| `ImportScbSalaryStatsJob` | Månatlig | Uppdatera ssyk_salary_stats |
| `GdprHardDeleteJob` | Daglig 04:00 | Permanent radera soft-deleted efter 30 dagar |
| `AuditLogPartitionMaintenanceJob` | Daglig | Skapa nya partitions, rulla 90 dagar |
| `AiUsageReconciliationJob` | Daglig | Matcha logged ai_operations mot provider-fakturor |

### 16.3 Fire-and-forget jobb

Triggas av handlers för:
- Skicka välkomst-email
- Generera exportfil efter export-request
- Skicka invite-email
- Uppdatera SCB-data när SSYK-kod ändras

---

## 17. Testing

### 17.1 Test-pyramiden

**Domain unit tests** (JobbPilot.Domain.UnitTests, ~70% av antalet tester)
- Aggregate-invariants, state machines, value objects
- Ingen databas, ingen I/O
- Använder xUnit + Shouldly (ersätter FluentAssertions efter dess kommersialisering 2025)
- Target coverage på Domain: **>90%**

**Application unit tests** (JobbPilot.Application.UnitTests, ~20%)
- Handlers mot in-memory fakes/mocks (NSubstitute)
- xUnit + Shouldly + NSubstitute
- Testar use case-logik utan Infrastructure

**Integration tests** (JobbPilot.Api.IntegrationTests, ~10%)
- Testcontainers för Postgres + Redis (ephemeral per test-klass)
- WebApplicationFactory
- Shouldly för assertions
- Happy-path + nyckel-felscenarion per endpoint
- Kör i CI med `dotnet test --filter Category=Integration`

**Architecture tests** (JobbPilot.Architecture.Tests)
- NetArchTest-regler:
  - Domain beror inte på Infrastructure/Application/Api
  - Application beror inte på Infrastructure
  - Alla endpoints har auth-attribute (eller explicit `[AllowAnonymous]`)
  - Alla aggregates ärver `AggregateRoot<>`

**E2E tests** (jobbpilot-web-tests, Playwright)
- Kritiska användarflöden: registrera → skapa sökning → söka jobb → submit ansökan → logga follow-up
- Kör i CI mot staging-miljö, nattligt
- Max 15-20 tester (dyra att underhålla, håll tight)

### 17.2 Testdata

- `TestDataBuilder`-klasser per aggregate (fluent builder-pattern)
- Inga `.sql` seed-filer i tests — bygg data via builders för tydlighet

### 17.3 AI-tester

- `IAiProvider` mockas i alla vanliga tester
- Separat `/tests/ai-evaluations`-folder med scriptade evals:
  - 10 CV-parsings mot known good outputs
  - 20 cover letters genererade + mätning av cliche-score
  - Deep match mot 30 kända "bra fit"/"dåligt fit"-par
- Körs manuellt före release, inte i CI

---

## 18. Utvecklingsfaser (roadmap)

Ingen hård deadline, men mjuka milstolpar för att driva framåt:

### Fas 0 — Foundation (~2 veckor)

> **Status:** Fas 0 genomfördes ursprungligen på AWS (Klar 2026-05-10). AWS är
> sedan avvecklat (ADR 0066) — foundation-stegen nedan beskriver det ursprungliga
> AWS-uppsättet och är historiska. Permanent infra-foundation görs om mot
> Hetzner/Vercel när ADR 0050 är Accepted.

- (Historiskt) AWS-konto + SSO, IAM-roller, OIDC-federation, Terraform bootstrap + prod-baseline
- Clean Arch-solution setup + NetArchTest
- ASP.NET Core Identity + första JWT-endpoint
- Next.js 16-projekt + design system-baseline (tokens, Hanken Grotesk, Button, Card, Input)
- Första deploy till dev (hello world backend + login/register-flöde)
- GitHub Actions CI/CD fungerar (tag-baserad deploy per §15.3)
- CLAUDE.md + DESIGN.md committade
- Bootstrap-IAM-user raderas som sista steg när SSO-profilen fungerar för Terraform

**Milstolpe:** Du kan registrera dig + logga in på dev.jobbpilot.se.

### Fas 1 — Core Domain (~3 veckor)
- Domain-projekt: alla aggregates, entities, VOs med >80% test coverage
- EF Core-konfiguration för alla
- Migrations, seed-data för SSYK
- Mediator.SourceGenerator-pipeline med alla behaviors
- Application-lagret: grundläggande queries/commands för JobSeeker, Resume (utan AI), Application (utan integrations)
- API-endpoints för ovan
- Audit log-infrastruktur

**Milstolpe:** Du kan skapa CV manuellt, submit:a "fake" ansökningar, se dem i admin-audit.

### Fas 2 — JobTech Integration (~2 veckor)

> **Förkrav innan Fas 2 påbörjas:** ADR 0005 (go-to-market) måste vara beslutad
> och samtliga kostnadsskydd implementerade enligt ADR:s obligatoriska sektion
> (Budget Actions, feature flag `registrations_open`, rate limiting per user,
> runbook `docs/runbooks/aws-cost-recovery.md`).

- `IJobSource` + `PlatsbankenJobSource`
- Hangfire-setup + första syncjob
- JobAd-domän + CRUD
- API-endpoints för sök/filter
- Frontend: `/jobb`-sida med lista + filter + detaljvy
- Saved searches

**Milstolpe:** Du kan söka jobb på Platsbanken genom appen, spara sökningar.

### Fas 3 — Application Management (~2 veckor)
- Application-pipeline UI (tabell + status-grupperad vy)
- FollowUp-logging, notes, status transitions
- Påminnelser (Hangfire + notifikations-UI)
- Ghosted-detection-jobb

**Milstolpe:** Fullständig ansökningshantering fungerar (utan AI).

### Fas 4 — AI Layer (~3–4 veckor)

> **Förkrav (ADR 0051 Beslut 3, GDPR-veto):** DPIA + SCC/TIA + Anthropic-DPA +
> DPF-status-verifiering + ADR 0049-cross-ref uppfyllda **innan första AI-kodrad**.

- `IAiProvider` + Anthropic Direct API-implementation (Bedrock utgår, ADR 0051)
- BYOK-UI + DEK-envelope-säkerhet (`IDataKeyProvider`, ADR 0066/0049)
- Prompt library + versionshantering
- Credit system
- CV-parsing (PDF/DOCX)
- CV-tailoring
- Personligt brev-generation
- Anti-klyscha-detektor
- Matchningsscore (Fast + Deep)
- Företagsresearch-brief
- Smart CV-baserat filter — AI härleder JobTech-yrkesurval ur ett CV (med användarbekräftelse) och skapar en `SavedSearch`; ett smart filter per CV-spår. Bygger ovanpå Fas 2:s `SavedSearch`-aggregat. Se ADR 0040 (Proposed).

**Milstolpe:** Alla AI-features fungerar end-to-end med BYOK för power users. **Detta är den första riktiga dogfood-checkpointen: 14 dagar daglig användning av Klas.**

### Fas 5 — Integrationer (~2 veckor)
- Gmail OAuth + sync
- Google Calendar OAuth
- iCal-export
- SCB lönestatistik + overlay

**Milstolpe:** Gmail auto-loggar follow-ups, intervjuer hamnar i din Google-kalender.

### Fas 6 — Admin & Analytics (~2 veckor)
- Admin-vyer: användare, token-kostnad, audit, jobbkällor
- Impersonation-flöde med audit
- Avslags-analys-dashboard
- PostHog self-hosted uppe + event-tracking
- Polish på dashboard

**Milstolpe:** Admin-panel komplett, du kan se exakt vad som händer i systemet.

### Fas 7 — Internal Beta (~2 veckor)
- 2–3 Team 7 Debuggers-klasskamrater onboardas
- Buggfixar, UX-polish från feedback
- Accessibility-review
- Performance-pass (Lighthouse, profiling)
- GDPR-dokumentation komplett

**Milstolpe:** 3 användare har aktivt använt appen i 14 dagar.

### Fas 8 — Klass-launch (~1 vecka)
- 20 klasskamrater onboardas
- Support-rutin (Slack-kanal, email)
- Monitoring + alerting skarp

**Milstolpe:** Klass-launch. v1 klar.

### Fas 9+ — Efter klass-launch
- Mobilanpassning (responsive, inte native än)
- Kanban-vy
- Intervjuträning
- LinkedIn-integration (om möjlig)
- Betalplan via Stripe
- Chrome extension
- Bemanningsföretag-scrapers

**Totalt:** ~20 veckor till klass-launch. Ingen hård deadline, kan ta längre om Klas prioriterar klass + LIA2.

---

## 19. Monetisering (framtid)

### 19.1 Freemium-modell (v2+)

| Plan | Pris | Features |
|------|------|----------|
| Bas | Gratis | 50 AI-ops/mån, alla kärnfeatures |
| Premium | 99 kr/mån | 500 AI-ops/mån, Deep match på allt, prioriterad support |
| BYOK | 49 kr/mån | Obegränsade AI-ops (egen nyckel), alla features |
| Klass | Kostnadsfritt | För studenter, kräver verifiering mot NBI/Yrgo/andra skolor |

### 19.2 Pricing-rationale

- Bas = magneten, ska räcka för 3–4 ansökningar/månad
- Premium = aktiva sökare (2–3 per vecka)
- BYOK = tekniskt kunniga som vill ha obegränsat till egen kostnad
- Klass = viral tillväxt genom skolor, case-study-material

### 19.3 Betalintegration (v2)

- Stripe, samma setup som KalasKoll
- Månatliga + årliga (10% rabatt) prenumerationer
- Stripe Customer Portal för självbetjäning
- Webhooks till backend för prenumerationsstatus

---

## 20. Open questions / TODO

Saker som inte är beslutade och behöver stängas under fas 0–1:

- **Branding-färg exakt hex:** `#0B5CAD` är förslag, verifiera mot A11y-kontrastkrav på all körtext. Kan behöva justeras.
- **Logotyp:** ska designas (eventuellt ett litet projekt med AI-assistans à la KalasKoll-identitet, eller leja in designbyrå)
- **Domän:** `jobbpilot.se` — DNS-host TBD (ADR 0050: Cloudflare)
- **E-post-adress för support:** `hej@jobbpilot.se` förslag
- **Integritetspolicy + TOS:** behöver skrivas (svenska, med GDPR-explicit text)
- **Onboarding-video eller hjälpcenter:** skjuts till v1.1
- **Rate limits per plan:** rimligt förslag är 60 req/min Bas, 120 Premium
- **Bakgrundsjobb-observability:** Hangfire-dashboard räcker eller ska vi skicka till extern logg-sink? (sink TBD, ADR 0050)

Dessa ska alla lösas innan fas 8 (klass-launch).

---

## Bilaga A — Viktiga externa referenser

- JobTech Dev: https://jobtechdev.se / https://data.arbetsformedlingen.se
- JobSearch API docs: https://jobsearch.api.jobtechdev.se
- Taxonomy API: https://taxonomy.api.jobtechdev.se
- SCB Pxweb API: https://api.scb.se/OV0104/v1/doris/sv/ssd
- Anthropic API: https://docs.claude.com (Anthropic Direct för system + BYOK, ADR 0051)
- GOV.UK Design System: https://design-system.service.gov.uk
- Digg (svensk digital förvaltning): https://www.digg.se
- WCAG 2.1 AA: https://www.w3.org/TR/WCAG21/
- EU AI Act: https://artificialintelligenceact.eu

---

## Bilaga B — Arkitekturbeslut (ADRs)

ADR:er lagras i `/docs/ADR/` och namnges `NNNN-slug.md`. Initiala ADR:er som ska skrivas i fas 0:

1. `0001-clean-architecture.md` — Varför Clean Arch med DDD
2. `0002-postgresql-over-sqlserver.md`
3. `0003-aws-over-azure.md`
4. `0004-bedrock-eu-for-system-key.md`
5. `0005-byok-architecture.md`
6. `0006-no-repository-pattern.md`
7. `0007-strongly-typed-ids.md`
8. `0008-hangfire-background-jobs.md`
9. `0009-nextjs-app-router.md`
10. `0010-civic-design-language.md`

---

**Slut på BUILD.md.** Nästa läsning: [`CLAUDE.md`](./CLAUDE.md) för kodningsstandarder och [`DESIGN.md`](./DESIGN.md) för design-specifikation.
