# ADR 0001 — Clean Architecture med DDD för JobbPilot

**Datum:** 2026-04-18
**Status:** Accepted
**Kontext:** Session 2 (bootstrap), fylld session 4 STEG 9
**Beslutsfattare:** Klas Olsson
**Relaterad:** BUILD.md §2, CLAUDE.md §2.1, ADR 0002 (Mediator.SourceGenerator)

## Kontext

JobbPilot är en SaaS-applikation för jobbansökningshantering med flera reella komplexitetskällor:

- **Riktig domänlogik:** CV-tailoring, match-scoring, cover-letter-generering, ghost-detection, applikation-pipeline, BYOK-nyckelhantering. Dessa är inte CRUD — de har invarianter, state-maskiner, och regler som inte är triviala att hålla rätt i ad-hoc-kod.
- **AI-integration:** Prompts, token-budgetering, streaming, modell-routing (Opus/Sonnet/Haiku), EU-inferens via Bedrock. Detta lager måste vara utbytbart (vi vill kunna byta modeller utan att röra domän) och testbart (mockning av Bedrock-svar).
- **GDPR-krav:** Soft-delete, audit-logging, retention-policies, PII-hantering. Dessa invarianter får inte läcka eller bero på framework-lager.
- **Solo-dev som förvärvar team:** Klas är ensam utvecklare nu, men projektet ska kunna ta in fler utvecklare senare utan att bli oframgängligt.

Utan arkitekturell disciplin tenderar .NET-projekt av denna storlek att utvecklas mot "fat controllers + anemic services + EF-entiteter överallt" — en struktur som fungerar på kort sikt men bryter samman när domänen växer. Vi har sett detta mönster i flera referensprojekt och vill undvika det.

## Beslut

JobbPilot använder **Clean Architecture med Domain-Driven Design**, strukturerat som fyra projekt:

- `JobbPilot.Domain` — aggregate roots, entities, value objects, domain events, domain services, invariants. Ingen referens till något lager utanför `System.*`.
- `JobbPilot.Application` — use-cases via Mediator.SourceGenerator (commands + queries), DTOs, application services, pipeline behaviors (logging, validation, authorization, unit-of-work), interfaces för infrastructure. Referens endast till `Domain`.
- `JobbPilot.Infrastructure` — EF Core 10, AWS Bedrock, Redis, Hangfire, external API clients (JobTech, SCB), implementationer av `Application`-interfaces. Referens till `Domain` + `Application`.
- `JobbPilot.Api` — ASP.NET Core 10 HTTP-endpoints, composition root, authentication, CORS, OpenAPI. Referens till alla lager men exponerar inget utanför Application-layer.

**Fyra explicita regler:**

1. **Domain imports bara `System.*`.** Ingen EF, inget MediatR, ingen ASP.NET. Enforced via arkitekturtester (`NetArchTest.Rules`).
2. **Application definierar interfaces som Infrastructure implementerar** — inte tvärtom. Dependency inversion.
3. **Ingen Repository-pattern** (kommer dokumenteras separat i framtida ADR). Direkt `DbContext` i Application-handlers med `IUnitOfWork`-abstraktion.
4. **Aggregater äger sina invarianter** — ingen "service" sätter state på en entity utan att gå via aggregate roots metod.

## Konsekvenser

**Positivt:**

- Domänlogik testbar utan databas, utan AI-provider, utan HTTP
- Byte av persistens (t.ex. SQL Server för en kund) är ett Infrastructure-lager-refactor, inte en app-rewrite
- AI-provider-byte (t.ex. Bedrock → Azure OpenAI) isolerat till Infrastructure
- GDPR-invarianter kodifieras i Domain-lagret där de inte kan kringgås av framework-lager
- Framtida utvecklare har tydligt mentalt ramverk: *"vilket lager ändrar jag?"*

**Negativt:**

- Mer boilerplate än ad-hoc-lösningar (handlers, DTOs, mappningar)
- Inlärningskurva för utvecklare som inte sett mönstret
- Frestelse att "kortvägen" genom lager när man har bråttom — måste motstås konsekvent
- Arkitekturtester behöver underhåll när nya projekt tillkommer

**Mitigering av negativa:**

- Scaffolding-skills (`add-command`, `add-entity`, etc.) i `.claude/skills/` genererar korrekt struktur — minskar boilerplate-friktion
- `code-reviewer`-agenten enforcar lagergränser automatiskt (CLAUDE.md §2.1)
- Pre-commit hook (`.husky/pre-commit`) kör `dotnet test` inklusive architecture tests när scaffold finns

## Alternativ övervägda

**Alt 1 — Transaction Script / lager-lös.** Avvisat. Fungerar för CRUD, kollapsar under komplex domänlogik som CV-tailoring + ghost-detection + BYOK. Domänlogik skulle utspridas i controllers och services utan tydlig hemvist.

**Alt 2 — Onion/Hexagonal.** Likvärdigt alternativ, konceptuellt nära Clean Architecture. Clean valdes för att det är det mest dokumenterade mönstret i .NET-ekosystemet (Microsoft reference apps, Jason Taylor Clean Architecture template) — lägre inlärningsbarriär för nya utvecklare.

**Alt 3 — Vertical Slice Architecture.** Tilltalande för solo-dev-fas men sätter långsiktig pris när delade invarianter (t.ex. `Application.Status`-transitioner) behöver koordinera över features. Clean Architecture-lagergränserna är ett mer robust långtidsval.

**Alt 4 — Microservices från start.** Avvisat som over-engineering för fas 0-1. JobbPilot har en enda bounded context för v1 (applications + resumes är tätt kopplade). Microservices kan övervägas när vi separerar AI-layer eller integrations-layer som egna tjänster — tidigast fas 2-3.

## Implementationsstatus

**Aktiv sedan:** start av projektet (lager-strukturen definierad i BUILD.md §2).

**Ej scaffoldat än:** `src/`-projekt skapas i Fas 0/1 när .NET-scaffolding sker. Denna ADR dokumenterar den valda arkitekturen innan koden finns — det är avsiktligt. Beslutet ska vara förankrat innan första handler skrivs.

**Enforcement-mekanismer:**

- `NetArchTest.Rules` i `tests/JobbPilot.Architecture.Tests/` (aktiveras när test-projekt scaffoldas). NetArchTest är formellt abandoned sedan 2022 men fungerar för JobbPilots skala i v1; `TngTech.ArchUnitNET` övervägs vid v2-refactor.
- `code-reviewer`-agent (`.claude/agents/code-reviewer.md`) läser `.claude/rules/clean-arch.md` före varje review
- Pre-commit hook (`.husky/pre-commit`) kör `dotnet test` inklusive architecture tests när scaffold finns
