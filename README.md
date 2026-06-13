# Jobbliggaren

> **Svensk jobbansökningshanterare byggd som civic utility — och ett portfolio-bevis på agent-orkestrerad ingenjörsdisciplin.**
> Platsbanken-integration, AI-assisterad CV/brev-skräddarsydning, end-to-end pipeline-tracker.
> Clean Architecture med maskinellt verifierade lager-gränser, EU-data-residens, GDPR-säker, Bring-Your-Own-Key för AI.

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-14-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Next.js](https://img.shields.io/badge/Next.js-16.2-000000?logo=next.js&logoColor=white)](https://nextjs.org/)
[![TypeScript](https://img.shields.io/badge/TypeScript-6.0-3178C6?logo=typescript&logoColor=white)](https://www.typescriptlang.org/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18.3-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![Dev](https://img.shields.io/badge/dev-lokal%20stack%20(Docker%20Compose)-2C3E50)](docs/decisions/0066-aws-dev-stack-teardown-semester-pause.md)
[![Arkitektur](https://img.shields.io/badge/arkitektur-Clean%20%2B%20DDD-2C3E50)](docs/decisions/0001-clean-architecture.md)
[![Tester](https://img.shields.io/badge/backend-1%20100%2B%20gröna-success)](docs/decisions/0044-test-coverage-policy.md)
[![Vitest](https://img.shields.io/badge/vitest-686%20gröna-success)](docs/decisions/0044-test-coverage-policy.md)
[![Coverage](https://img.shields.io/badge/first--party%20line-92,1%25-success)](docs/decisions/0044-test-coverage-policy.md)
[![ADR](https://img.shields.io/badge/ADR-66%20beslut-informational)](docs/decisions/README.md)
[![Status](https://img.shields.io/badge/fas-3%20klar%20·%204%20GDPR--gated-blue)](docs/steg-tracker.md)
[![License](https://img.shields.io/badge/license-proprietary-lightgrey)](#licens)

---

## Snabblänkar

- [Vad är Jobbliggaren](#vad-är-jobbliggaren)
- [Om utvecklingsmodellen](#om-utvecklingsmodellen)
- [Agent-orkestrering](#agent-orkestrering)
- [Ingenjörsprinciper i praktiken](#ingenjörsprinciper-i-praktiken)
- [Position och anti-position](#position-och-anti-position)
- [Funktioner](#funktioner)
- [Arkitektur](#arkitektur)
- [Kvalitet, test och coverage](#kvalitet-test-och-coverage)
- [Tech-stack](#tech-stack)
- [Komma igång lokalt](#komma-igång-lokalt)
- [Projekt-struktur](#projekt-struktur)
- [Vanliga kommandon](#vanliga-kommandon)
- [Miljöer](#miljöer)
- [Säkerhet och GDPR](#säkerhet-och-gdpr)
- [Status och roadmap](#status-och-roadmap)
- [Dokumentation](#dokumentation)
- [Författare](#författare)
- [Licens](#licens)

---

## Vad är Jobbliggaren

Jobbliggaren är en komplett jobbsök- och ansökningshanterare för den svenska arbetsmarknaden. Appen kombinerar JobTech/Platsbanken-integration med modern AI-assistans men positioneras medvetet som en *civic utility* — ett verktyg som signalerar tillit och pålitlighet snarare än hajp.

Målet är att stressade jobbsökare får ett verktyg som känns som en förlängning av svensk offentlig digital service (1177, Försäkringskassan, Digg) snarare än ett av hundra AI-produkter som alla ser likadana ut. Den medvetna icke-differentieringen är ett designval, inte en brist på ambition.

> [!NOTE]
> Detta repo är publikt för portfölj-syfte. Det är ett **pågående arbete** — Fas 0–3 är levererade, Fas 4 (AI-lager) är GDPR-gated bakom [ADR 0051](docs/decisions/0051-ai-provider-anthropic-direct-bedrock-retired.md) och fem icke-förhandlingsbara villkor. Pre-Fas-4-arbete pågår parallellt i `/oversikt`, `/sparade`, landing live-stats, closed-beta-väntelista m.m. (se [Status och roadmap](#status-och-roadmap)). README beskriver det faktiska tillståndet, inte ett mål-tillstånd.

### Målgrupp

| Tier | Användare |
|------|-----------|
| **v1 (primär)** | Aktiva jobbsökare i Sverige — initial kohort: produktägaren + ~20 klasskamrater på NBI/Handelsakademin |
| **v2** | Bredare svensk arbetsmarknad — tjänstemän, utvecklare, kunskapsarbetare. Freemium. |
| **framtid** | Internationella användare via `IJobSource`-adapters för NAV (Norge), Arbejdsformidlingen (Danmark), EURES (EU) |

---

## Om utvecklingsmodellen

Jag har byggt Jobbliggaren med **Claude Code som primär utvecklingspartner** i en agent-orkestrerad modell — inte "AI som autocompletear", utan en governance-struktur där specialiserade review-agenter har veto-rätt, en CTO-agent är decision-maker vid arkitektur-tradeoffs, och varje arkitekturbeslut historieförs som en immutable ADR. Modellen är dokumenterad i sin helhet i [`CLAUDE.md §9`](CLAUDE.md) och verifierbar mot katalogen [`.claude/`](.claude/).

Positionen jag tränar i detta projekt: **AI-Augmented Fullstack Engineer med fokus på agent-orkestrering** över .NET, React och TypeScript. Differentiatorn är inte att AI används — det gör många. Differentiatorn är att utvecklingsflödet har *granskningsspärrar, beslutsdisciplin och spårbarhet* som håller för en kodgranskning på Mastercard-nivå. Resten av denna README är evidensen för det påståendet.

---

## Agent-orkestrering

Jobbliggaren kör **direct-push till `main` utan PR-flöde** ([ADR 0019](docs/decisions/0019-solo-direct-push-to-main.md)). Granskningsvärdet ett PR-flöde ger ersätts inte av tillit — det ersätts av en orkestrerad agent-struktur med skrivna mandat. Roster verifierad i [`.claude/agents/`](.claude/agents/): **13 specialiserade agenter** med distinkta, icke-överlappande mandat.

```mermaid
flowchart TB
    Klas["Klas Olsson<br/>Agent-orkeströr · sista ordet"]

    CTO["senior-cto-advisor<br/>decision-maker (ej advisor)<br/>multi-approach-val · fynd-triage"]

    subgraph Veto["Review-agenter — veto-rätt före commit"]
        CR["code-reviewer<br/>Clean Arch / DDD / CQRS / coverage"]
        SA["security-auditor<br/>PII · auth · secrets · GDPR"]
        DR["design-reviewer<br/>civic-utility · WCAG 2.1 AA"]
    end

    subgraph Advisor["Arkitektur-rådgivning före kod"]
        DA["dotnet-architect<br/>aggregat · bounded contexts · EF Core"]
    end

    subgraph Builders["Builder-agenter"]
        TW["test-writer<br/>xUnit v3 · TDD-först"]
        TR["test-runner<br/>dotnet test · svensk summering"]
        DM["db-migration-writer<br/>EF Core-migrations · GDPR-schema"]
        UI["nextjs-ui-engineer<br/>RSC · shadcn · Tailwind 4"]
        AP["ai-prompt-engineer<br/>Anthropic-prompts · token-budget"]
        PT["perf-test-writer<br/>NBomber · Lighthouse-CI"]
    end

    subgraph Keepers["Dokumentations-keepers"]
        AK["adr-keeper<br/>ADR-livscykel · status-flips"]
        DK["docs-keeper<br/>kod↔docs-synk · cross-refs"]
    end

    Klas --> CTO
    CTO --> Veto
    CTO --> Advisor
    CTO --> Builders
    Klas --> Keepers
    Veto -.->|blockerar commit| Klas
```

### Modellen i sex steg

1. **Plan-design** — scope, sekvens, risker och alternativ designas i chat innan kod skrivs. Ingen kod utan plan.
2. **STOPP-disciplin** — Claude Code stannar vid varje övergång. Inga `str_replace`, inga commits, ingen analys mellan STOPP och GO ([CLAUDE.md §6.3](CLAUDE.md)).
3. **Agent-veto** — `code-reviewer`, `security-auditor` och `design-reviewer` har **blockerande** veto vid relevant scope. En review-agents auktoritet är skriven regel (CLAUDE.md), inte konsensus eller deadline.
4. **In-block-fix-disciplin** — fynd fixas i samma commit-batch som default. Teknisk skuld lyfts endast vid genuin fas- eller dependency-orsak ([CLAUDE.md §9.6](CLAUDE.md)) — TD-listan är inte ett dumpningsställe.
5. **ADR-historik** — alla arkitekturbeslut är immutable ADRs. En ändring skapar en ny ADR som *superseder* den gamla, aldrig en tyst redigering ([docs/decisions/](docs/decisions/)).
6. **Session-protokoll** — varje session börjar med `docs/current-work.md` + senaste session-logg + git-log-verifiering, och avslutas med synkroniserad docs-state ([CLAUDE.md §1.5](CLAUDE.md)).

Detta är governance-mognad — inte "jag använder AI". Chat-historiken (produktägare + webb-Claude) är den primära granskningstrailen; agent-rapporterna sparas i [`docs/reviews/`](docs/reviews/) och bifogas varje STOPP-rapport så att granskning sker parallellt.

---

## Ingenjörsprinciper i praktiken

Den här sektionen är portfolions kärna. Varje princip nedan är kopplad till en **verifierbar mekanism** — ett arkitekturtest som failar bygget, en ADR som låser beslutet, eller en namngiven kod-path. Inga påståenden utan referent.

### Clean Architecture — maskinellt enforced, inte beskrivet

De flesta kodbaser *beskriver* sin lager-separation. Jobbliggaren **failar bygget** om den bryts. [`Jobbliggaren.Architecture.Tests`](tests/Jobbliggaren.Architecture.Tests/) innehåller **78 NetArchTest-fakta över 14 filer** som körs i CI. Den hårdaste regeln, `DomainLayerTests.Domain_should_not_depend_on_any_other_project`, asserterar att domänlagret har noll beroende på EF Core, ASP.NET Core, Mediator, FluentValidation eller något högre lager:

```csharp
// tests/Jobbliggaren.Architecture.Tests/DomainLayerTests.cs
Types.InAssembly(typeof(Jobbliggaren.Domain.Common.Entity<>).Assembly)
    .ShouldNot()
    .HaveDependencyOnAny(
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore",
        "Mediator", "FluentValidation",
        "Jobbliggaren.Application", "Jobbliggaren.Infrastructure",
        "Jobbliggaren.Api", "Jobbliggaren.Worker")
    .GetResult();
```

Samma testklass enforcar att Application inte beror på Infrastructure, inte på ASP.NET, inte på konkreta EF Core-providers, och att inget aggregat exponerar en publik setter. Lager-strukturen är ett **kört kontrakt** (Martin, *Clean Architecture* 2017, kap. 22 — en gräns som inte enforcas är ingen gräns). Beslutet är låst i [ADR 0001](docs/decisions/0001-clean-architecture.md).

### SOLID — demonstrerat, inte deklarerat

| Princip | Mekanism | Var |
|---------|----------|-----|
| **DIP** — Application definierar portar, Infrastructure implementerar | `ICurrentUser`, `IJobSource`, `IAppDbContext` deklareras i Application; konkreta implementationer ligger i Infrastructure. Arch-testet `Application_should_not_depend_on_Infrastructure` failar om riktningen vänds. | `src/Jobbliggaren.Application/Common/Abstractions/` → `src/Jobbliggaren.Infrastructure/` |
| **OCP** — beteende läggs till utan att ändra handlers | Cross-cutting concerns är Mediator-pipeline-behaviors i låst ordning (`Logging → Validation → Authorization → AdminAuthorization → UnitOfWork → Audit`). Ny behavior = ny rad i `InOrder`, ingen handler rörs. Ordningen delas av Api + Worker så de inte kan drifta isär, verifierad av ett arch-test. | `MediatorPipelineBehaviors.InOrder` i `src/Jobbliggaren.Application/Common/`, låst av [ADR 0008](docs/decisions/0008-pipeline-behavior-order.md) |
| **SRP** — en behavior, ett ändringsskäl | Varje pipeline-behavior bär exakt ett cross-cutting concern (en anledning att ändras, Martin 2017 kap. 7). En command-handler komponerar inte flöden — komplexa flöden komponeras av flera commands, aldrig en fet handler ([CLAUDE.md §2.3](CLAUDE.md)). | `src/Jobbliggaren.Application/Common/Behaviors/` |

### DRY — delade SPOT-moduler, inga magiska primitiver

Primitive obsession motverkas av strongly-typed IDs som `readonly record struct` ([ADR 0011](docs/decisions/0011-strongly-typed-ids.md)) — ett `ApplicationId` kan aldrig av misstag skickas där ett `JobSeekerId` förväntas, kompilatorn fångar det:

```csharp
// src/Jobbliggaren.Domain/Applications/ApplicationId.cs
public readonly record struct ApplicationId(Guid Value)
{
    public static ApplicationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
```

Sökkriterier är inte lösa strängar utan ett `SearchCriteria`-value-object som normaliserar och validerar invarianter (concept-id-format, term-längd, sort-precondition) på konstruktion — en enda sanningspunkt för sök-semantik, återanvänd av samtliga SavedSearch-commands och -queries ([ADR 0039](docs/decisions/0039-savedsearch-aggregate-and-query-run-semantics.md)).

### SoC / DDD / CQRS — aggregat skyddar invarianter

Affärsregler bor i domänen, inte i handlers. `Application`-aggregatet är en state-maskin: en olaglig statusövergång kan inte ske, eftersom `TransitionTo` avvisar den och raisar inget event:

```csharp
// src/Jobbliggaren.Domain/Applications/Application.cs — TransitionTo
if (!Status.AllowedTransitions.Contains(target))
    return Result.Failure(DomainError.Validation(
        "Application.InvalidTransition",
        $"Övergång från {Status.Name} till {target.Name} är inte tillåten."));
...
RaiseDomainEvent(
    new ApplicationStatusTransitionedDomainEvent(Id, JobSeekerId, previous, target, clock.UtcNow));
```

- **CQRS** — commands returnerar `Result<T>`, queries returnerar DTOs direkt; inga domänobjekt passerar Application-gränsen ([CLAUDE.md §2.3](CLAUDE.md)). Pipeline-ordningen är låst av [ADR 0008](docs/decisions/0008-pipeline-behavior-order.md).
- **Domain events som sanning** — state-ändringar raisar events; handlers reagerar, de driver inte sanningen ([ADR 0022](docs/decisions/0022-audit-log-pipeline-behavior.md), audit via pipeline-behavior + marker-interface).
- **Anticorruption Layer** — JobTech-taxonomins instabila vokabulär läcker aldrig in i domänens ubiquitous language. Kommentaren i sök-query-vägen citerar källan explicit i koden:

```csharp
// src/Jobbliggaren.Application/JobAds/Queries/JobAdSearch.cs
// Shadow-properties refereras via EF.Property<string?>(...) eftersom de
// inte är top-level Domain-fält (Evans 2003 §14 ACL — JobTech-taxonomi
// är inte Jobbliggarens ubiquitous language).
```

ACL:n är formaliserad i [ADR 0043](docs/decisions/0043-taxonomy-acl-for-search-surface.md) (lokal taxonomi-snapshot bakom port — externt taxonomi-API aldrig på sök-vägen).

> [!IMPORTANT]
> Varje rad ovan pekar på en namngiven artefakt (testklass, ADR, fil + medlem) — aldrig ett radnummer som ruttnar vid nästa redigering. En granskare kan öppna referenten och verifiera påståendet. Det är skillnaden mellan att kunna vokabulären och att ha fattat besluten.

---

## Position och anti-position

**Jobbliggaren är:**
- Svensk-först (Platsbanken, SCB, svensk rekryteringskultur)
- Kvalitet över volym — inga auto-apply-funktioner
- AI-assisterad där det ger tydligt värde, aldrig "AI-genererat för syns skull"
- GDPR-säker med dataminimering och fält-kryptering; AI-residens via Anthropic Direct opt-in (ADR 0051), permanent infra-region TBD (ADR 0050)
- Öppen för Bring-Your-Own-Key (BYOK) för AI

**Jobbliggaren är inte:**
- Ännu en ChatGPT-wrapper
- Ett mass-apply-verktyg som LoopCV eller Sonara
- En ATS-keyword-stuffer
- En jobbmarknad eller rekryteringstjänst

---

## Funktioner

Detaljerad scope finns i [`BUILD.md §2`](BUILD.md). Sammanfattning:

### Discovery

- Hämta platsannonser från **JobTech JobSearch API** (Platsbanken)
- Full-text-sökning och facetterad filtrering (region, yrke, SSYK, anställningsform, distans, datum)
- Sparade sökningar med notifieringsinställning per sökning
- **Taxonomi-baserad matchningsscore** (Fast mode) — gratis, beräknas för alla synliga annonser
- **LLM-baserad matchningsscore** (Deep mode) — på begäran, kostar credits
- Lönestatistik-overlay per annons från **SCB**

### Application management

- Full pipeline-tracker med state machine: Draft → Submitted → Acknowledged → InterviewScheduled → Interviewing → OfferReceived → Accepted / Rejected / Withdrawn / Ghosted
- Follow-up-loggning per ansökan (kanal, datum, anteckning, utfall)
- Kalenderintegration: Google Calendar + iCal-export
- Automatisk Ghosted-transition efter X dagar utan svar
- Avslags-analys med trender över tid

### AI-assistans

- **CV-parsing** från PDF/DOCX till strukturerad `ResumeContent` (PdfPig + OpenXml + Haiku LLM)
- **Skräddarsytt CV** per annons (Sonnet) — original behålls, ny versioned `ResumeVersion` skapas
- **AI-genererat personligt brev** (Sonnet) — på svenska, följer användarens skriv-DNA
- **Anti-klyscha-detektor** (Haiku) — markerar "brinner för", "driven team-player" etc. med förslag
- **Företagsresearch-brief** (Sonnet + web_search) — 1-pager med nyheter, teknik-stack, kulturell signal

### Integrationer

- Gmail-sync (OAuth) — auto-loggar ansökningssvar som Follow-Ups
- Google Calendar (OAuth) — intervjuer som events
- SCB lönestatistik — periodisk import per SSYK
- iCal-export av intervjuer

### Admin

- Användarhantering, suspendering, mjukradering
- **Impersonation** med audit-trail (`impersonating_by` claim, dubbel-taggning av handlingar)
- Token-användning per användare med kostnad över tid
- Audit-sökning och jobbkälla-statushälsa

---

## Arkitektur

Jobbliggaren följer **Clean Architecture** med strikt lager-separation och **DDD** med aggregates som invariant-skydd. Lager-gränserna är inte en konvention — de är [maskinellt verifierade](#clean-architecture--maskinellt-enforced-inte-beskrivet).

```mermaid
flowchart TB
    subgraph Edge["Edge / Klient"]
        Browser["Browser<br/>Next.js 16 / React"]
        Mobile["Mobile<br/>(framtid)"]
    end

    subgraph Local["Lokal stack (Docker Compose) — permanent mål TBD (ADR 0050)"]
        Api["Jobbliggaren.Api<br/>ASP.NET Core 10 Minimal API"]
        Worker["Jobbliggaren.Worker<br/>Hangfire"]
        DB[("PostgreSQL 18.3")]
        Redis[("Redis 8")]
        Seq["Seq<br/>log-sink"]
        DEK["IDataKeyProvider<br/>Local AES-256-GCM (ADR 0066)"]
    end

    subgraph External["Externt"]
        JobTech["JobTech / Platsbanken"]
        SCB["SCB lönestatistik"]
        Gmail["Gmail / Calendar"]
        Anthropic["Anthropic Direct API<br/>(system + BYOK, Fas 4, ADR 0051)"]
    end

    Browser -->|HTTPS| Api
    Api -->|EF Core 10| DB
    Api -->|Sessions| Redis
    Api --> Seq
    Api --> DEK
    Api -.->|Fas 4, opt-in| Anthropic
    Worker --> DB
    Worker --> JobTech
    Worker --> SCB
    Worker --> Gmail
```

### Lager (.NET-backend)

```mermaid
flowchart LR
    Api["Jobbliggaren.Api<br/>(composition root)"]
    Worker["Jobbliggaren.Worker<br/>(composition root)"]
    Infra["Jobbliggaren.Infrastructure<br/>(EF Core, Anthropic, local crypto)"]
    App["Jobbliggaren.Application<br/>(CQRS handlers, behaviors)"]
    Domain["Jobbliggaren.Domain<br/>(aggregates, VOs, events)"]

    Api --> App
    Api --> Infra
    Worker --> App
    Worker --> Infra
    Infra --> App
    App --> Domain
```

**Regler (arch-test-enforced):**
- `Domain` beror på **ingenting** — inga ORM, inga frameworks
- `Application` definierar interfaces; `Infrastructure` implementerar
- `Api` och `Worker` är separata komposition-rots ([ADR 0010](docs/decisions/0010-worker-composition-root.md)) — de bygger DI-containern; pipeline-ordningen delas så de inte driftar isär

64 arkitekturbeslut är historieförda som ADRs under [`docs/decisions/`](docs/decisions/) — index i [`docs/decisions/README.md`](docs/decisions/README.md). Mer detaljerat: [`BUILD.md §4`](BUILD.md), [`CLAUDE.md §2`](CLAUDE.md).

---

## Kvalitet, test och coverage

Jobbliggaren byggs med en uttalad kvalitetsstandard: varje commit ska kunna försvaras i en kodgranskning på Mastercard-nivå. Det är inte en paroll utan en mätbar praxis.

### Test-disciplin

- **Clean Architecture-gränser verifieras maskinellt.** NetArchTest-regler i `Jobbliggaren.Architecture.Tests` failar bygget om Domain importerar EF Core, om Application känner till Infrastructure, eller om ett aggregat exponerar en publik setter.
- **Domänlogik testas utan databas.** Aggregat och value objects bär sina invarianter; handlers testas mot fake `IAppDbContext` med NSubstitute. Om något kräver en startad ASP.NET-host för att testas betraktas designen som fel ([CLAUDE.md §2.4](CLAUDE.md)).
- **TDD där det bär.** Nya domäntyper och handlers får tester först; produktionskod skrivs för att passera.
- **Integrationstester mot riktig Postgres.** Testcontainers startar PostgreSQL 18.3 och Valkey per integrations-svit — ingen in-memory-attrapp som döljer provider-skillnader.
- **Granskningsspärrar utan PR-flöde.** Direct-push till `main` ([ADR 0019](docs/decisions/0019-solo-direct-push-to-main.md)) kompenseras av plan-design, STOPP-disciplin, specialiserade review-agenter med veto-rätt (code-reviewer, security-auditor, design-reviewer), manuell diff-granskning och pre-push-hooks.

Backend-sviten omfattar **1 100+ tester gröna** över Domain (422), Application (591), Architecture (78), Api-integration, Worker-integration och Migrate. Frontend-sviten kör **686 Vitest-tester** plus Playwright E2E för kritiska flöden.

### Coverage — reproducerbar, ärlig, regressionsskyddad

Coverage mäts av en **versionerad in-repo-mekanism** ([ADR 0044](docs/decisions/0044-test-coverage-policy.md)), inte en maskin-lokal ad-hoc-körning:

- `Microsoft.Testing.Extensions.CodeCoverage` (Microsoft, MTP-native, central via Central Package Management) samlar rå Cobertura per testprojekt — ofiltrerad, audit-trail bevarad.
- `dotnet-reportgenerator-globaltool` via in-repo tool-manifest producerar den first-party-filtrerade rapporten report-time. Rådatan förstörs aldrig — filtreringen är deklarativ och reversibel.
- Genererad kod (Mediator source-gen, OpenAPI), entrypoints (`Program.cs`, `Jobbliggaren.Migrate`) och migrationer filtreras bort så siffran speglar verklig testbar kvalitet, inte nämnar-kosmetik.
- En kommandorad reproducerar allt: `bash scripts/coverage.sh` (Windows: `scripts/coverage.ps1`).

First-party-resultat per ADR 0044-baseline (samma mekanism):

| Lager | Line | Branch | Method |
|-------|------|--------|--------|
| Jobbliggaren.Domain | 95,3 % | 93,3 % | 91,9 % |
| Jobbliggaren.Application | 97,7 % | 91,1 % | 98,1 % |
| Jobbliggaren.Infrastructure | 84,0 % | 71,1 % | 80,3 % |
| Jobbliggaren.Api (efter filter) | 93,7 % | 82,9 % | 92,3 % |
| Jobbliggaren.Worker | 30,7 % | observe-only | 36,8 % |
| **Totalt first-party** | **92,1 %** | **84,5 %** | **90,2 %** |

Siffrorna är medvetet asymmetriska: Domain och Application bär affärsinvarianter och har hög grentäckning; Worker är en tunn Hangfire-bootstrap vars jobblogik testas i Application-lagret. En global tröskel skulle dölja den asymmetrin — därför gejtar CI per lager.

### Regressions-gate (icke-regression-ratchet)

CI-jobbet `coverage` blockerar `main` om något lager faller under sitt golv. Golvet är `floor(uppmätt baseline − 2,0 pp)` — en absorptionsmarginal mot icke-deterministisk grenmätning som gör gaten trovärdig i stället för falsklarmande. Den är ett regressionsskydd, inte en måltavla (Fowler, Goodharts lag): golvet höjs manuellt när coverage stabilt ligger högre, aldrig automatiskt. Branch gejtas endast för Domain och Application — lagren som bär invarianter. Modell och pinnade golv: [ADR 0044](docs/decisions/0044-test-coverage-policy.md).

---

## Tech-stack

Versioner är låsta. Full lista i [`BUILD.md §3`](BUILD.md).

### Backend

| Komponent | Val | Version |
|-----------|-----|---------|
| Runtime | .NET | 10 (LTS) |
| Språk | C# | 14 |
| Framework | ASP.NET Core (Minimal API) | 10 |
| ORM | EF Core (Npgsql) | 10 |
| Mediator | `Mediator` (martinothamar) | 3.x |
| Validering | FluentValidation | 12.x |
| Mapping | Mapster | 10.x |
| Background jobs | Hangfire (Postgres-storage) | 1.8.x |
| Logging | Serilog | 4.x |
| Observability | OpenTelemetry | 1.15+ |
| AI (system + BYOK) | Anthropic (officiell NuGet) — Anthropic Direct API (Bedrock utgår, ADR 0051) | 12.x |
| PDF | PdfPig (parse) + QuestPDF (gen) | 0.1.14 / 2026.2 |

### Frontend

| Komponent | Val | Version |
|-----------|-----|---------|
| Framework | Next.js (App Router) | 16.2 |
| Språk | TypeScript (strict) | 6.0 |
| UI-komponenter | shadcn/ui | CLI v4 |
| Styling | Tailwind CSS | 4.2 |
| Server state | TanStack Query | 5.x |
| Tabeller | TanStack Table (headless) | 8.x |
| Forms | React Hook Form + Zod | RHF 7.72 / Zod 4.x |
| Auth-klient | NextAuth.js (Auth.js) | 5 |
| Datum | date-fns (svensk locale) | 4.x |
| Typografi | Hanken Grotesk (fallback Inter) | — |

### Datalager och infra

> AWS-dev-stacken avvecklad (ADR 0066); permanent mål (Hetzner BE + Vercel FE + Cloudflare) i ADR 0050 (Proposed). Tabellen visar **nuläge (lokalt)** + **permanent mål**.

| Tjänst | Nuläge (lokal dev) | Permanent mål |
|--------|--------------------|---------------|
| Databas | PostgreSQL 18.3 (Docker Compose) | TBD (ADR 0050) |
| Cache | Redis 8 (Docker Compose) | TBD (ADR 0050) |
| Compute | `dotnet run` lokalt | TBD — Hetzner (ADR 0050) |
| AI inference | Anthropic Direct API (Fas 4, opt-in, ADR 0051) | Anthropic Direct API |
| Object storage | lokal disk / ej aktiverat | TBD — S3-kompatibel (ADR 0050) |
| Encryption | `LocalDataKeyProvider` AES-256-GCM (ADR 0066) | TBD — self-managed (TD-102) |
| Frontend hosting | `pnpm dev` (localhost) | TBD — Vercel (ADR 0050) |
| DNS / CDN | — | TBD — Cloudflare (ADR 0050) |
| Email | `ConsoleEmailSender` → Seq (ADR 0066) | TBD — transaktionell väg (TD-101) |
| Logs / metrics | Seq (lokalt) | TBD (ADR 0050) |
| Errors | — | Sentry (EU) planerat |
| IaC | `infra/terraform/` bevarad (reversibilitet, ADR 0066) | Hetzner-IaC TBD (ADR 0050) |
| CI | GitHub Actions (build + test + coverage) | oförändrat |

### Tester

| Verktyg | Användning |
|---------|------------|
| xUnit v3 | Test-runner |
| Shouldly | Assertions |
| NSubstitute | Mocks |
| Testcontainers | Postgres + Redis i integration-tests |
| NetArchTest.Rules | Architecture-tests |
| Playwright | E2E-frontend |
| Vitest | Unit-tests frontend |

---

## Komma igång lokalt

> Full setup-guide: [`docs/runbooks/local-dev-setup.md`](docs/runbooks/local-dev-setup.md)

### Förkrav

| Verktyg | Version | Installation (Windows) |
|---------|---------|------------------------|
| .NET SDK | 10.x | `winget install Microsoft.DotNet.SDK.10` |
| Node.js | 22 LTS | `winget install OpenJS.NodeJS.LTS` |
| pnpm | 10.x | `npm install -g pnpm` |
| Docker Desktop | Engine 28+ | `winget install Docker.DockerDesktop` |
| Git | senaste | `winget install Git.Git` |
| openssl | (lösenord-gen) | bundlat med Git for Windows |

### Första start

```bash
# 1. Klona
git clone https://github.com/klasolsson81/jobbliggaren.git
cd jobbliggaren

# 2. Generera lokala lösenord (PowerShell)
@"
POSTGRES_PASSWORD_DEV=$(-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 32 | ForEach-Object {[char]$_}))
POSTGRES_PASSWORD_TEST=$(-join ((48..57)+(65..90)+(97..122) | Get-Random -Count 32 | ForEach-Object {[char]$_}))
REDIS_PASSWORD_DEV=
"@ | Out-File -Encoding utf8 .env

# 3. Starta Docker-stacken (Postgres, Valkey, Seq)
docker compose up -d

# 4. Verifiera
docker compose ps
docker exec jobbliggaren-postgres-dev psql -U jobbliggaren -d jobbliggaren -tAc "SELECT version();"
docker exec jobbliggaren-redis-dev redis-cli ping
```

### Backend

```bash
# Restore + build
dotnet restore
dotnet build

# Migrations (när du har en DbContext-ändring)
dotnet ef database update --project src/Jobbliggaren.Infrastructure --startup-project src/Jobbliggaren.Api

# Kör Api lokalt (port 5000/5001)
dotnet run --project src/Jobbliggaren.Api

# Kör Worker lokalt
dotnet run --project src/Jobbliggaren.Worker
```

### Frontend

```bash
cd web/jobbliggaren-web

# Installera deps
pnpm install

# Kopiera env-mall
cp .env.example .env.local

# Dev-server (port 3000)
pnpm dev
```

### Verifierings-URL:er

| Tjänst | URL |
|--------|-----|
| Frontend | http://localhost:3000 |
| Api (HTTP) | http://localhost:5000 |
| Api (HTTPS, dev-cert) | https://localhost:5001 |
| Health-check | http://localhost:5000/api/ready |
| Seq (logs) | http://localhost:5341 |

---

## Projekt-struktur

```
jobbliggaren/
├── src/
│   ├── Jobbliggaren.Domain/             # Aggregates, value objects, domain events
│   ├── Jobbliggaren.Application/        # CQRS handlers, pipeline behaviors, abstractions
│   ├── Jobbliggaren.Infrastructure/     # EF Core, Anthropic-klient, local/KMS crypto-providers
│   ├── Jobbliggaren.Api/                # ASP.NET Core Minimal API, composition root
│   └── Jobbliggaren.Worker/             # Hangfire-server, schedulerade jobb
│
├── web/
│   └── jobbliggaren-web/                # Next.js 16 App Router, shadcn/ui, Tailwind 4
│
├── tests/
│   ├── Jobbliggaren.Domain.UnitTests/
│   ├── Jobbliggaren.Application.UnitTests/
│   ├── Jobbliggaren.Architecture.Tests/        # NetArchTest-regler för lager-gränser
│   ├── Jobbliggaren.Api.IntegrationTests/      # Testcontainers + WebApplicationFactory
│   ├── Jobbliggaren.Worker.IntegrationTests/   # Hangfire-job-orkestrering, recurring-jobs
│   └── Jobbliggaren.Migrate.UnitTests/         # Migrate-CLI + connection-string-fabriker
│
├── infra/
│   └── terraform/                    # AWS-stack bevarad men INAKTIV (ADR 0066); retireras via egen ADR vid Hetzner-cutover
│       ├── modules/                  # network, rds, redis, alb, ecs, route53, acm, ...
│       └── environments/
│           ├── prod/                 # Baseline (historisk referens)
│           └── dev/                  # avvecklad (ADR 0066)
│
├── prompts/                          # AI-prompts som .prompt.md-filer
│
├── docs/
│   ├── current-work.md               # Single source of truth för session-state
│   ├── steg-tracker.md               # Långsiktig fas/STEG-progression
│   ├── tech-debt.md                  # TD-register
│   ├── decisions/                    # ADR (Architecture Decision Records)
│   ├── reviews/                      # Auto-genererade agent-reviews
│   ├── runbooks/                     # Operativa procedurer
│   ├── sessions/                     # Per-session retrospektiv
│   └── research/                     # Investigationer, planer
│
├── .claude/                          # Claude Code agent-configs + skills + hooks
├── BUILD.md                          # Huvudspec — feature-scope, datamodell, integrationer
├── CLAUDE.md                         # Coding conventions för AI-assisterad utveckling
├── DESIGN.md                         # Design-system-index (specs i .claude/skills/)
└── docker-compose.yml                # Lokal Postgres + Valkey + Seq
```

---

## Vanliga kommandon

### Backend

```bash
# Bygg hela solutionen
dotnet build

# Kör alla tester (kan vara fragilt på solution-nivå — kör test-projekt direkt vid behov)
dotnet test

# Specifika test-suiter
dotnet test tests/Jobbliggaren.Domain.UnitTests
dotnet test tests/Jobbliggaren.Application.UnitTests
dotnet test tests/Jobbliggaren.Api.IntegrationTests
dotnet test --filter "Category=Architecture"

# Coverage (reproducerbar in-repo-mekanism, ADR 0044)
bash scripts/coverage.sh          # Windows: scripts/coverage.ps1

# Format-check (pre-commit hook kör detta automatiskt)
dotnet format --verify-no-changes

# Skapa migration
dotnet ef migrations add <Name> --project src/Jobbliggaren.Infrastructure --startup-project src/Jobbliggaren.Api

# Applicera migrations
dotnet ef database update --project src/Jobbliggaren.Infrastructure --startup-project src/Jobbliggaren.Api
```

### Frontend

```bash
cd web/jobbliggaren-web

pnpm dev              # Dev-server med HMR
pnpm build            # Produktion-build
pnpm lint             # ESLint
pnpm test             # Vitest unit-tests
pnpm playwright test  # E2E-tests
```

### Infrastruktur (lokal dev)

AWS-dev-stacken är avvecklad (ADR 0066). All utveckling kör lokalt på laptop:

```bash
docker compose up -d         # postgres + redis + seq
dotnet run --project src/Jobbliggaren.Api
dotnet run --project src/Jobbliggaren.Worker
```

Permanent deploy-infra (Hetzner/Vercel/Cloudflare) definieras i ADR 0050
(Proposed). `infra/terraform/` är bevarad men inaktiv som reversibilitets-mekanik.

---

## Miljöer

| Miljö | Syfte | Deployment | Status |
|-------|-------|------------|--------|
| `local` | Utveckling | Docker Compose | **Aktiv** |
| `dev` / `staging` / `prod` | Integration / pre-prod / live | TBD (ADR 0050) | Avvecklad (ADR 0066) |

Branch-strategi: **PR-flöde mot `main`** med Conventional Commits per [ADR 0065](docs/decisions/0065-pr-flow-restoration-with-ci-gate.md) (superseder ADR 0019). `ci`-aggregatet (backend + frontend + coverage) måste vara grönt innan squash-merge; agent-reviews + manuell diff-review + pre-commit/pre-push-hooks kompletterar.

---

## Säkerhet och GDPR

Jobbliggaren är byggd för svensk arbetsmarknad och är därför **GDPR-säker by default**. Nyckel-höjdpunkter:

- **Datalokalisering:** PII och fält-data minimeras och krypteras lokalt; AI-prompter med användardata skickas till Anthropic Direct API (US) **endast vid opt-in** (ADR 0051, Bedrock/EU-routing utgår). Permanent infra-region TBD (ADR 0050)
- **Encryption at rest:** PII-fält + OAuth-tokens + BYOK-nycklar via per-användar-DEK envelope (`IDataKeyProvider`: Local AES-256-GCM eller KMS, ADR 0066/0049); managed databas-/storage-kryptering på permanent host (TBD, ADR 0050)
- **Encryption in transit:** TLS 1.3 ([ADR 0027](docs/decisions/0027-https-aktiverat-supersession.md)); HSTS 365 dagar + includeSubDomains
- **BYOK:** användare kan koppla egen Anthropic-API-nyckel; den envelope-krypteras med separat DEK och syns aldrig i klartext utanför inference-anrop
- **Audit-trail:** alla state-transitioner i `Application`-aggregatet raisar domain events som lagras i `audit_log`. Impersonation dubbel-taggas
- **Art. 17 cascade:** soft-delete på primära aggregates triggar 30-dagars anonymisering ([ADR 0024](docs/decisions/0024-audit-retention-and-art17-cascade.md))
- **IP-anonymisering:** IPv4 /24 + IPv6 /48 i alla loggar
- **Loggretention:** 30 dagar standard
- **Rate-limiting:** auth-write 20/min/IP, auth-loose 30/min/IP, account-deletion 1/60s/UserId
- **Subprocessor-kedja (planerad):** infra-host TBD (ADR 0050), Anthropic (Anthropic Direct, opt-in, US — Fas 4, ADR 0051), Sentry (EU), PostHog self-hosted, Vercel (EU). AWS utgår (ADR 0066)

Detaljer: [`BUILD.md §13`](BUILD.md), [`docs/decisions/0024-*`](docs/decisions/), [`docs/decisions/0031-*`](docs/decisions/).

---

## Status och roadmap

Jobbliggaren är ett **pågående arbete**. Faserna nedan följer den auktoritativa progressionen i [`docs/steg-tracker.md`](docs/steg-tracker.md); aktuell session-state alltid i [`docs/current-work.md`](docs/current-work.md).

| Fas | Innehåll | Milstolpe | Status |
|-----|----------|-----------|--------|
| **Fas 0** | Foundation — infra, container-pipeline, DNS + TLS, CI/CD (ursprungligen AWS; avvecklat ADR 0066) | Registrera + logga in på dev.jobbliggaren.se | **Klar 2026-05-10** |
| **Fas 1** | Core Domain — auth, kärn-CRUD, aggregat, audit | CV manuellt + "fake" ansökningar i admin-audit | **Klar 2026-05-11** |
| **Fas 2** | JobTech Integration — Platsbanken-sök, sparade sökningar, taxonomi-ACL | Söka jobb på Platsbanken via appen | **Klar 2026-05-17** |
| **Fas 3** | Application Management — fullständig ansökningshantering (utan AI) | Pipeline-tracker end-to-end | **Klar 2026-05-18** |
| **Pre-Fas-4** | Discovery- och UX-vertikaler — landing live-stats, översiktssida, jobbkort spara/har-ansökt, closed-beta-väntelista, sökningsperformance | Avskild från Fas 4 (AI) — körs medan AI-grinden är stängd | Pågående 2026-05 |
| **Fas 4** | AI Layer — alla AI-features end-to-end + dogfood | CV/brev-skräddarsydning live | **GDPR-gated** — kräver 5 villkor per [ADR 0051](docs/decisions/0051-ai-provider-anthropic-direct-bedrock-retired.md) |
| **Fas 5** | Integrationer — Gmail auto-logg, Google Calendar | Intervjuer i kalendern | Planerad |
| **Fas 6** | Admin & Analytics — admin-panel komplett | Impersonation + token-statistik | Planerad |
| **Fas 7** | Internal Beta — 3 användare aktivt 14 dagar | Dogfood-validering | Planerad |
| **Fas 8** | Klass-launch — 20 klasskamrater onboardade | v1 klar | Planerad |

**Pre-Fas-4-disciplin.** Fas 4 (AI) är låst bakom fem icke-förhandlingsbara GDPR-villkor i [ADR 0051](docs/decisions/0051-ai-provider-anthropic-direct-bedrock-retired.md): DPIA Art. 35, SCC + Schrems II-TIA + Anthropic-DPA + DPF-verifikation, versionerad privacy-policy, Art. 25-opt-in även för systemnyckel, och ADR 0049-decrypt-interaktion. Tills villkoren är gröna körs leveransen i avskilda pre-Fas-4-vertikaler: landing live-stats ([ADR 0064](docs/decisions/0064-public-aggregate-read-via-worker-precomputed-redis-cache.md)), översiktssida `/oversikt`, jobbkort Spara/Har-ansökt ([ADR 0063](docs/decisions/0063-per-user-overlay-status-batch-port.md)), FTS-hybridsök ([ADR 0062](docs/decisions/0062-fts-hybrid-search-and-infrastructure-query-port.md)), recent-job-searches auto-capture ([ADR 0060](docs/decisions/0060-recent-job-searches-auto-capture.md)) och closed-beta-väntelista per EDPB-tolkning ([ADR 0005 amendment](docs/decisions/0005-go-to-market-strategy.md)). Auktoritativ status: [`docs/current-work.md`](docs/current-work.md).

Dev-miljön (`dev.jobbliggaren.se`) är avvecklad under semester-pausen (ADR 0066) — all utveckling kör lokalt. Permanent miljö återupprättas vid Hetzner-cutover (ADR 0050). Projektet är pre-MVP; inga publika användare ännu.

---

## Dokumentation

| Fil | Syfte |
|-----|-------|
| [`BUILD.md`](BUILD.md) | Huvudspec — feature-scope, datamodell, API-design, integrationer, deployment |
| [`CLAUDE.md`](CLAUDE.md) | Coding conventions, anti-patterns, agent-orkestrerings-workflow |
| [`DESIGN.md`](DESIGN.md) | Design-system-index — civic-utility-tone, design tokens, komponenter |
| [`docs/current-work.md`](docs/current-work.md) | Session-state, senaste commits, aktiv fas |
| [`docs/steg-tracker.md`](docs/steg-tracker.md) | Långsiktig fas/STEG-progression |
| [`docs/tech-debt.md`](docs/tech-debt.md) | TD-register med prioriteringar |
| [`docs/decisions/`](docs/decisions/) | 66 Architecture Decision Records (ADRs) |
| [`docs/reviews/`](docs/reviews/) | Auto-genererade agent-reviews |
| [`docs/runbooks/`](docs/runbooks/) | Operativa procedurer (lokal-dev, TLS, etc.) |
| [`docs/sessions/`](docs/sessions/) | Per-session retrospektiv-loggar |
| [`.claude/`](.claude/) | Agent-definitioner, skills, hooks, slash-kommandon |
| [`prompts/`](prompts/) | AI-prompts som versionerade `.prompt.md`-filer |

---

## Författare

**Klas Olsson** — AI-Augmented Fullstack Engineer · agent-orkestrering · .NET / React / TypeScript
.NET / fullstack-student, NBI/Handelsakademin Göteborg

- GitHub: [@klasolsson81](https://github.com/klasolsson81)
- Email: klasolsson81@gmail.com

Jobbliggaren drivs av en solo-utvecklare i pre-MVP-fas. Externa bidrag accepteras inte i nuvarande fas; vid framtida öppning byts flödet från direct-push till PR-baserat (trigger dokumenterad i [ADR 0019](docs/decisions/0019-solo-direct-push-to-main.md)). Vill du diskutera kod, arkitektur eller designval — hör av dig direkt.

---

## Licens

**Proprietär** — all rights reserved tills annat anges.

Detta repo är publikt synligt för portfölj-syfte men innehållet är inte fri programvara. Återanvändning, fork, eller derivat-arbete kräver explicit skriftligt godkännande från Klas Olsson.

Under v1 kommer projektet sannolikt att förbli proprietärt. När produkten lanserats publikt kommer en formell licens-policy att antas (övervägs: AGPL-3.0 för server-koden, MIT för shadcn-komponenter, separat ToS för hostad tjänst).

---

> _"Skriv som om varje commit ska kunna försvaras i en kodgranskning på Mastercard-nivå."_ — utdrag ur [`CLAUDE.md`](CLAUDE.md)
