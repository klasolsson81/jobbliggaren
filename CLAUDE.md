# CLAUDE.md — JobbPilot coding conventions

> **Detta är instruktionsfilen för Claude Code.** Läs den på varje invocation innan du skriver kod eller föreslår ändringar i detta repo. Den kodifierar kvalitetsstandarden för JobbPilot.
>
> **Huvudspec:** [`BUILD.md`](./BUILD.md)
> **Design:** [`DESIGN.md`](./DESIGN.md)

---

## 1. Projektets identitet

JobbPilot är en svensk jobbansökningshanterare byggd som en **civic utility** — tänk 1177 eller Digg i tonen, inte Linear eller Vercel. All kod, copy och design ska bära den identiteten. När du är osäker: välj alternativet som känns *seriöst och pålitligt* framför *roligt eller trendigt*.

**Produktägare:** Klas Olsson, .NET/fullstack-student på NBI/Handelsakademin. Han har hög kvalitetsstandard, gillar rak svenska, och ogillar AI-klyschor. Skriv som om varje commit ska kunna försvaras i en kodgranskning på Mastercard-nivå.

---

## 2. Kärnprinciper

### 2.1 Clean Architecture är icke-förhandlingsbart

- **Domain** beror på **ingenting** — inte ens MediatR, inte EF Core, inte IDomainEvent bas-klass utanför projektet
- **Application** beror på Domain, definierar alla interfaces som Infrastructure implementerar
- **Infrastructure** implementerar Application-interfaces, innehåller EF Core, externa API-klienter
- **Api / Worker** beror på Application + Infrastructure, komponerar DI-container

Om du någonsin ser dig importera `Microsoft.EntityFrameworkCore` i Domain eller Application — **stoppa och tänk om**.

### 2.2 Domain-driven design

- Aggregates skyddar sina invarianter i konstruktorer och metoder, inte i handlers
- Ingen public setter på entity-properties utom där EF Core *tvingar* det (private set + mappings)
- Ändringar raisar domain events — events är sanningen, handlers reagerar
- Aggregates refererar varandra **endast via strongly-typed IDs**, aldrig direkta objekt
- State-övergångar går genom explicita metoder med preconditions (`Application.TransitionTo(status)`)

### 2.3 CQRS via MediatR

- Commands returnerar `Result<T>` där `T` är det som ändrats
- Queries returnerar DTOs direkt, inga domänobjekt ut genom Application-gränsen
- Pipeline-behaviors: Logging → Validation → Authorization → UnitOfWork (i den ordningen)
- En handler gör en sak. Komplexa flöden komponeras av flera commands, inte en fet handler.

### 2.4 Testbart först, snyggt sedan

- Alla aggregates testbara utan databas
- Handlers testbara med fake DbContext + NSubstitute
- Om du inte kan testa det utan att starta ASP.NET → designen är fel

---

## 3. C# / .NET-standarder

### 3.1 Stil

- C# 13-syntax där det hjälper (primary constructors, collection expressions, `field` keyword)
- Nullable reference types **på** för hela lösningen
- `file`-scoped namespaces
- `global using` i varje projekt för vanliga imports (List, Task, etc.)
- `dotnet format` körs pre-commit (Husky) och verifieras i CI

### 3.2 Namngivning

- Aggregates: substantiv, single form (`Application`, `JobSeeker`, inte `Applications`)
- Handlers: `<Verb><Noun>CommandHandler` / `<Verb><Noun>QueryHandler`
- Commands: `SubmitApplicationCommand`, inte `SubmitApplication` eller `ApplicationSubmitCommand`
- Interfaces: `I`-prefix (.NET-konvention)
- Private fields: `_camelCase`
- Async-metoder: `Async`-suffix alltid
- Tests: `<ClassUnderTest>_<Scenario>_<Expected>`: `Application_TransitionTo_ThrowsWhenInvalid`

### 3.3 Immutability och records

- Value Objects = `record struct` eller `readonly record class`
- DTOs = `record class`
- Entities = `class` med private setters
- Collections som exponeras = `IReadOnlyList<T>` / `IReadOnlyCollection<T>`, aldrig `List<T>`

### 3.4 Error handling

- Förväntade fel → `Result<TSuccess, TError>`-pattern (använd `FluentResults` eller egen)
- Oväntade fel → exceptions
- `DomainException` för invariant-brott — fångas i Api-middleware och returnerar 400
- `NotFoundException` → 404
- Aldrig `throw new Exception(...)` — alltid specifik subclass

### 3.5 Async / threading

- `CancellationToken` propageras **genom hela kedjan**, från endpoint till DbContext/HttpClient
- `await` utan `.ConfigureAwait(false)` inne i ASP.NET Core-kontext (inte nödvändigt där)
- Ingen `.Result` eller `.Wait()` någonsin — bryter upp builden om det upptäcks
- `Task.Run` bara för CPU-bundet arbete, aldrig för I/O

### 3.6 LINQ och queries

- Använd `IAppDbContext` direkt i handlers — ingen Repository-abstraktion
- Specifikationer (`ISpecification<T>`) bara om samma filtrering används på 3+ ställen
- `.AsNoTracking()` som default för queries, explicit tracking endast för updates
- `Include()` bara när den behövs, inte "för säkerhets skull"
- Paginering via `.Skip().Take()` med total count i separate query

---

## 4. TypeScript / Next.js-standarder

### 4.1 Stil

- `strict: true` i `tsconfig.json`, inga undantag
- ESLint + Prettier, pre-commit via Husky
- `any` är **förbjudet** — använd `unknown` och type guards
- Funktionella komponenter + hooks, inga class components

### 4.2 Fil- och komponent-organisation

- Komponenter i `PascalCase.tsx`, en export per fil
- Hooks i `useCamelCase.ts`, en export per fil
- Types i `types.ts` per mapp eller `[domain].types.ts`
- Tests co-lokaliserade: `Button.test.tsx` bredvid `Button.tsx`

### 4.3 Data fetching

- Server components som default
- `"use client"` endast där interaktivitet krävs
- TanStack Query för klient-side mutations och pollar
- Form state: React Hook Form + Zod, aldrig oberoende `useState` för stora formulär

### 4.4 Namngivning

- Rutter: svenska substantiv (`/ansokningar`, `/jobb`, `/installningar`)
- Komponenter: engelska PascalCase (`JobAdCard`, `ApplicationPipelineTable`)
- Klass- och event-namn på svenska i UI-copy, engelska i kod

---

## 5. Anti-patterns (aldrig gör så här)

### 5.1 Backend

- ❌ Repository pattern ovanpå EF Core — använd `IAppDbContext` direkt
- ❌ AutoMapper över gränsen till Domain — mappningar skrivs explicit för tydlighet
- ❌ `DateTime.Now` / `DateTime.UtcNow` — alltid `IDateTimeProvider` injicerat
- ❌ Magic strings — alltid konstanter eller enums (eller SmartEnums)
- ❌ Generiska "Service"-suffix (`UserService`, `OrderService`) — namnge efter vad klassen *gör* (`SendWelcomeEmail`, `ComputeMatchScore`)
- ❌ Primitive obsession — skapa value object istället för att skicka runt `string` som email
- ❌ Statiska helpers som håller state
- ❌ `dynamic` i C# — förbjudet
- ❌ Catch-all `try/catch` utan action — låt exceptions bubbla till middleware
- ❌ Loggning av känslig data i klartext (CV-innehåll, AI-prompts med PII, OAuth-tokens)
- ❌ Konfiguration hårdkodad — allt via `IOptions<T>` bundet mot `appsettings.json` + AWS Secrets Manager
- ❌ Synkron I/O i request-pipeline
- ❌ Hämta hela listor utan paginering
- ❌ `SELECT *` via EF Core (använd projections till DTOs för read-models)

### 5.2 Frontend

- ❌ `any`-typ
- ❌ Global state där server state räcker (TanStack Query löser 80%)
- ❌ `useEffect` för datahämtning (använd server components eller TanStack Query)
- ❌ `console.log` i produktion — använd strukturerad logger
- ❌ Emoji i UI-copy
- ❌ "!"-utropstecken i texter (civic-utility-ton)
- ❌ Gradients, drop shadows > `shadow-sm`, glow-effekter, glas-morphism
- ❌ Radius större än 6px utom för pills/badges
- ❌ `localStorage` för känslig data (OAuth-tokens, session-tokens)
- ❌ Hårdkodade strängar i komponenter — använd `next-intl` med `messages/sv.json`
- ❌ `document.getElementById` eller DOM-manipulation — React är sanningen

### 5.3 AI-layer

- ❌ Prompts som strängar i C#-kod — alltid `/prompts/<name>.prompt.md`
- ❌ Hårdkodade modellnamn — alltid via konfiguration
- ❌ AI-operationer utan token-tracking
- ❌ AI-output renderat direkt som HTML utan sanitization
- ❌ Systemnyckel använd för BYOK-användare (alltid rätt provider per user)
- ❌ BYOK-nycklar loggade eller synliga i admin-panel (bara fingerprint)
- ❌ AI-prompts med användardata skickas över **global** inference utan explicit samtycke — systemnyckel ska alltid EU-routas via Bedrock

### 5.4 Säkerhet

- ❌ Secrets i `appsettings.json` eller miljövariabler utan KMS
- ❌ JWT i localStorage
- ❌ CORS med `*` eller bredd credentials
- ❌ SQL via rå string concatenation (EF Core används, men om rå SQL: parametriserat)
- ❌ Impersonation utan audit-händelse
- ❌ Direkt `User.Identity.Name` för auktorisation — använd policies via `[Authorize(Policy = "...")]`

---

## 6. Commit och branch-strategi

### 6.1 Branches

- `main` = produktion, skyddad, kräver PR + godkänd review + grön CI
- `develop` = dev-miljö, ev. skyddad
- Feature branches: `feat/<kontext>-<beskrivning>` — `feat/applications-ghosted-detection`
- Fix branches: `fix/<beskrivning>`
- Chore: `chore/<beskrivning>`

### 6.2 Commits

- **Conventional Commits** (https://www.conventionalcommits.org)
- Format: `<type>(<scope>): <beskrivning>`
- Types: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `perf`, `build`, `ci`
- Scope = kontext: `applications`, `resumes`, `ai`, `infra`, `web`
- Beskrivning på svenska eller engelska (var konsekvent per PR), imperativ form
- Exempel:
  - `feat(applications): lägg till ghosted-detection via Hangfire-jobb`
  - `fix(ai): honorera EU-inferens när systemnyckel används`
  - `refactor(resumes): extrahera ResumeContent som value object`

### 6.3 Pull requests

- PR-titel = sammanfattning
- PR-beskrivning: **Varför**, **Vad**, **Hur testat**
- Länka till issue eller ADR om det finns
- Self-review först — läs diff:en en gång till innan du ber om review
- Håll PR:er små, helst <500 rader diff
- Ingen merge utan grön CI + minst 1 approval (när vi är fler än Klas)

---

## 7. Testing-krav

- Alla nya domain-klasser har minst en test som verifierar invarianten
- Alla nya handlers har minst en test för happy path + en för validation failure
- Integration-test för varje ny endpoint
- PR med sänkt coverage på Domain: motiverat i PR eller avvisat
- Snapshot tests bara för stabila komponenter
- E2E tests uppdateras när kritiska flöden ändras

### Kör tester lokalt

```bash
# Backend
dotnet test

# Frontend
cd web/jobbpilot-web && pnpm test

# E2E
cd web/jobbpilot-web && pnpm playwright test

# Architecture tests
dotnet test --filter "Category=Architecture"
```

---

## 8. Definition of Done per feature

En feature är "klar" när:

1. ✅ Implementerad enligt acceptance criteria i BUILD.md §2
2. ✅ Unit tests + integration tests (coverage inte sänkt)
3. ✅ Architecture tests gröna
4. ✅ Manuellt testad i dev-miljön
5. ✅ Lokal Lighthouse-score > 90 på påverkade sidor (frontend)
6. ✅ Tillgängligheten verifierad: kan navigeras med tangentbord, screenreader läser meningsfullt
7. ✅ Domain events dokumenterade (vad som raisas, vem som lyssnar)
8. ✅ GDPR-konsekvenser bedömda (new PII? logging? retention?)
9. ✅ ADR skriven om det är arkitekturbeslut
10. ✅ Code-review genomförd (när vi är fler)

---

## 9. Arbetsflöde med Claude Code

### 9.1 När du (Claude Code) får en uppgift

1. Läs `BUILD.md` sektionen som är relevant för kontexten
2. Kolla existerande kod — använd befintliga mönster, inte nya
3. Identifiera vilken lager-del som ska ändras (Domain / Application / Infrastructure / Api / Web)
4. Skriv testet först om det är ny domänlogik
5. Implementera minimalt för att passera
6. Kör `dotnet test` + relevant lint lokalt
7. Commit med conventional commits
8. Beskriv i PR: Varför, Vad, Hur testat

### 9.2 Gränser för Claude Code

**Du ska (Claude Code):**
- Skriva kod, tester, migrations, CI/CD-konfiguration, dokumentation
- Föreslå refaktoriseringar när du ser code smells
- Läsa prompts från `/prompts/` och implementera dem — inte skriva om dem
- Skapa ADRs när du tar arkitekturbeslut

**Du ska inte (Claude Code):**
- Ändra `BUILD.md`, `CLAUDE.md` eller `DESIGN.md` utan explicit instruktion från Klas
- Deploya till staging eller prod utan Klas:s godkännande
- Lägga till nya top-level dependencies utan motivering
- Använda externa bibliotek som inte står i BUILD.md §3.1 utan diskussion
- Skriva kod som bryter mot anti-patterns i §5

### 9.3 När du är osäker

- **Läs först** — sök i repo, läs BUILD.md, kolla befintliga mönster
- **Fråga sedan** — ställ konkreta frågor till Klas, inte generiska "hur vill du göra"
- **Gissa aldrig** — om du inte vet om en feature ska finnas, fråga innan du bygger den

---

## 10. Svenska-relaterat

### 10.1 Kod vs copy

- Kod (klassnamn, metoder, variabler, filnamn): **engelska**
- UI-copy, fel-meddelanden visade för användare, commit-meddelanden (om konsekvent): **svenska**
- Kommentarer: svenska eller engelska, var konsekvent per fil

### 10.2 Svensk locale

- Datum: `YYYY-MM-DD` eller "14 apr 2026"
- Tid: 24-timmars, "14:32"
- Decimaler: komma (",") i UI, punkt (".") i kod
- Valuta: `1 234 kr` med non-breaking space
- Svenska tecken (åäö) får inte trasas sönder i API-serialisering — UTF-8 överallt

### 10.3 Texter till användaren

- Tilltalsform: "du" (informell), aldrig "Du" med stort D
- Rak, konkret svenska: "Du har 3 aktiva ansökningar" — inte "Du har hela 3 spännande ansökningar på gång! 🚀"
- Fel-meddelanden är informativa men inte skyllande: "Inloggningen misslyckades. Kontrollera e-post och lösenord."
- Aldrig emoji
- Aldrig utropstecken
- Aldrig "Whoops!", "Oj då!", "Hoppsan!" — det är skit-design

---

## 11. Konkret konfiguration och verktyg

### 11.1 Pre-commit hooks (Husky + lint-staged)

```json
{
  "*.cs": ["dotnet format"],
  "*.{ts,tsx,js,jsx}": ["eslint --fix", "prettier --write"],
  "*.{json,md,yaml,yml}": ["prettier --write"]
}
```

### 11.2 Editor

- `.editorconfig` i rot
- `.vscode/settings.json` committed (formatOnSave, relevanta extensions)
- Rekommenderade extensions i `.vscode/extensions.json`

### 11.3 Dev environment

- Docker Compose i repo-root: `postgres`, `redis`, `seq` (local Serilog sink), `localstack` (för AWS-mocking)
- `make dev` eller `pnpm dev:up` startar allt lokalt
- `.env.local` för frontend, `appsettings.Development.json` för backend (committade defaults + overrides i `appsettings.Local.json` som är gitignorad)

---

## 12. När något ser fel ut

Om Claude Code föreslår eller skriver något som:
- Bryter mot anti-patterns i §5
- Verkar kringgå Clean Arch-gränserna
- Lägger till bibliotek som inte är i BUILD.md
- Förändrar design-tokens utanför DESIGN.md-definitionen
- Ändrar security-kritisk kod (auth, BYOK, GDPR) utan tester

→ Stoppa. Flagga i PR-kommentar. Diskutera med Klas innan merge.

---

## 13. Uppdateringsprocess

Denna fil uppdateras när:
- Ny anti-pattern upptäcks och behöver dokumenteras
- Ny coding standard etableras
- Ny gräns för Claude Code behövs

Process: Klas föreslår ändring → PR med diskussion → merge när överens. Aldrig tyst förändring.

---

**Slut på CLAUDE.md.** Huvudspec i [`BUILD.md`](./BUILD.md). Design i [`DESIGN.md`](./DESIGN.md).
