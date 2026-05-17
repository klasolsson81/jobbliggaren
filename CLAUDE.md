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

## 1.5 Session Protocol (mandatory)

**At session start**, before any other work:

1. Read `docs/current-work.md` — captures status from previous session
2. Read the latest file in `docs/sessions/` for context on recent work
3. Run `git log --oneline -8` to verify HEAD matches expected state from current-work.md
4. If hooks should be active: verify `bash .claude/hooks/session-start.sh` produces output

**During the session**:

- Track multi-step work with TodoWrite
- Mark todos as completed only when verified working (post-todo-review hook
  triggers code-reviewer on completed code-related todos)
- Pause and ask Klas before deviating from the planned step

**After each STEG completion** (and at session end before pause):

1. Update `docs/current-work.md`:
   - Status header (current step + next step)
   - "Active now" section (what was completed, what's pending)
   - Commit table (append new commits)
   - "Done last session" list
2. Update `docs/steg-tracker.md` om STEG flyttat status (Klar/Pågående/Planerad)
3. Create session log in `docs/sessions/YYYY-MM-DD-HHMM-<slug>.md`
   - YAML frontmatter (session, datum, slug, status, commits)
   - Body covers: goals, what was completed per step, decisions, commits, next session
4. Commit docs-uppdateringar separat från feature-commits (inte bundlade) och pusha
5. **Endast vid session-end:** Generera startprompt för nästa session enligt
   strukturen i [`docs/runbooks/session-start-template.md`](./docs/runbooks/session-start-template.md).
   - **Levereras alltid som copy-paste-block i chatten — aldrig som ny fil i repot** (håller repot rent från engångs-prompter)
   - Self-contained: antar ny `/clear`-session utan tidigare kontext
   - Måste innehålla alla 12 obligatoriska sektioner från templaten:
     hälsning + förkrav + mandatory reads + memory + uppdrag + **discovery/web-search-targets** + Klas-STOPP-flaggor + disciplin (CTO/architect/reviewers INLINE) + förbud + pending operativt + förväntat sluttillstånd + avslutning
   - Faktiska värden, inte placeholders: verifierad HEAD-SHA, datum, versions-nummer, fil-paths
   - Innan leverans: kör templatens CC-checklist
   - **Trigger för uppdatering av template:** om CC eller Klas upptäcker att en startprompt glömt en kritisk regel (t.ex. agent-invocation, memory-läsning), uppdatera templaten i samma session

**Trigger:** STEG-completion (även när sessionen fortsätter med nästa STEG). Att synka docs först vid session-end gör att pushed state ljuger om verkligheten under sessionens gång — om context tappas eller ny session startas innan session-end-rutinen körs vet nästa Claude inte vad som faktiskt är klart.

**Format for session log files**:

- Filename: `YYYY-MM-DD-HHMM-<slug>.md` (e.g., `2026-04-19-1000-session-4-hooks-github-docs.md`)
- Hybrid format: YAML frontmatter + freeform markdown body
- Medium detail: focus on decisions and detours, not what's already in commit messages
- See `docs/sessions/` for examples (sessions 3 and 4 are reference templates)

---

## 1.6 Docs structure (where things live)

The `docs/` directory is organized by purpose. When generating new
documentation, place it according to this map:

| Directory | Purpose | Examples |
|-----------|---------|----------|
| `docs/current-work.md` | Single source of truth for session state | (one file) |
| `docs/sessions/` | Per-session retrospective logs | `2026-04-19-1000-session-4-*.md` |
| `docs/decisions/` | Architecture Decision Records (ADRs) | `0001-clean-architecture.md` |
| `docs/decisions/README.md` | ADR index — auto-maintained by docs-keeper | (one file) |
| `docs/runbooks/` | Operational procedures | `aws-setup.md`, `local-dev-setup.md` |
| `docs/research/` | Investigative findings, planning docs | `SESSION-1-FINDINGS.md` |
| `docs/research/issues/` | Open research questions awaiting decision | `tailwind-config-approach.md` |
| `docs/reviews/` | Code review reports (auto-generated) | `pre-commit-YYYY-MM-DD-*.md` |
| `docs/test-reports/` | Test coverage outputs | `coverage-YYYY-MM-DD.xml` |
| `docs/api/` | OpenAPI exports (post-Fas 0) | `openapi.yaml` |
| `docs/tech-debt.md` | Aktiva TDs i Severity × Fas-matris | (en fil — se §9.7) |
| `docs/tech-debt-archive.md` | Stängda TDs i kronologisk ordning (full kropp) | (en fil — se §9.7) |

**Top-level files** (repo root, not under docs/):

- `BUILD.md` — main spec, edit only on explicit Klas instruction
- `CLAUDE.md` — this file, edit only on explicit Klas instruction
- `DESIGN.md` — design system index (real specs in `.claude/skills/jobbpilot-design-*/`)
- `README.md` — project overview for outside readers

**Convention**: when an agent or skill creates a new doc, it should pick the
correct directory automatically. If unsure, ask Klas before placing it.
The docs-keeper agent (`.claude/agents/docs-keeper.md`) verifies cross-references
and structure at session-end.

**For new ADRs**: use the `/new-adr <slug>` command which triggers the
adr-keeper agent. Numbering is sequential (next free number = look at
`docs/decisions/README.md` index).

---

## 2. Kärnprinciper

### 2.1 Clean Architecture är icke-förhandlingsbart

- **Domain** beror på **ingenting** — inte ens Mediator.SourceGenerator, inte EF Core, inte IDomainEvent bas-klass utanför projektet
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

### 2.3 CQRS via Mediator.SourceGenerator

- Commands returnerar `Result<T>` där `T` är det som ändrats
- Queries returnerar DTOs direkt, inga domänobjekt ut genom Application-gränsen
- Pipeline-behaviors: Logging → Validation → Authorization → UnitOfWork (i den ordningen)
- En handler gör en sak. Komplexa flöden komponeras av flera commands, inte en fet handler.

### 2.4 Testbart först, snyggt sedan

- Alla aggregates testbara utan databas
- Handlers testbara med fake DbContext + NSubstitute
- Om du inte kan testa det utan att starta ASP.NET → designen är fel

### 2.5 Performance har en skriven dom, inte bara en mätning

- Performance är en granskningsbar konvention, inte ett engångs-launch-item.
  Statisk query-hygien (§3.6) är golvet; ADR 0045 ger den körtidsmätta domen.
- Hot-path-latens, Core Web Vitals och Worker-minne har budgetar (ADR 0045
  Beslut 1–3). En ändring som regresserar mot budget ska motiveras i
  STOPP-rapport eller åtgärdas — samma disciplin som sänkt test-coverage (§7).
- Fitness functions är observe-only Fas 1 (ADR 0045 Beslut 5–6); flip till
  blockerande gate är en medveten ratchet vid Klas-GO, aldrig en tyst default.
- `LoggingBehavior` mäter redan latens — perf-regression utan motivering när
  signalen finns är en disciplinmiss, inte en okänd.

---

## 3. C# / .NET-standarder

### 3.1 Stil

- C# 14-syntax där det hjälper (primary constructors, collection expressions, `field` keyword)
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

- `main` = enda branch (direct-push-praxis per ADR 0019, superseder ADR 0004)
- Inga feature-branches, inga PRs — granskningsspärrar listas i §6.3
- Conventional Commits-format består (§6.2)
- Deploy via taggar på `main`: `v*-dev` → dev-miljö, `v*-rc*` → staging, `v*` → prod (manuell approval)
- Staging är *miljö*, inte *branch*

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

### 6.3 Granskningsspärrar (PR-fri praxis)

JobbPilot kör direct-push till `main` per ADR 0019. PR-flödet finns inte. Granskningsvärdet ersätts av fem mekanismer:

1. **Plan-design** — webb-Claude och Klas designar scope, sekvens, risker och alternativ i chat innan kod skrivs
2. **STOPP-disciplin** — Claude Code halt vid varje övergång; inga `str_replace`, inga commits, ingen analys mellan STOPP och GO
3. **Agent-invocation** — security-auditor / code-reviewer / dotnet-architect invokeras vid relevant scope och rapporter granskas innan commit (§9.2)
4. **Manuell diff-granskning** — Klas läser `git diff` innan varje push
5. **Pre-push hooks** — gitleaks, dotnet format, lint-staged

Chat-history (Klas + webb-Claude) är primär granskningstrail. GitHub-side review-record finns inte. Vid bidragsgivar-tillkomst eller disciplin-regression: trigger för återgång till PR-flöde finns dokumenterad i ADR 0019.

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
8. Bifoga relevanta agent-rapporter (security-auditor / code-reviewer / dotnet-architect) till STOPP-rapport så Klas kan granska parallellt — direct-push till `main` efter Klas:s GO (per ADR 0019)

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
- Påbörja ny session-fas baserat på "logiskt nästa steg från ADR-läsning" — sessionsbyten är strategiska transitioner och kräver explicit GO från Klas

**Du ska invocera (vid relevant scope, innan STOPP-rapport till Klas):**
- **senior-cto-advisor** — multi-approach-val (Variant A/B/C), agent-review-fynd
  som ska bli in-block-fix vs TD, TD-skapande-validering. Decision-maker, inte
  advisor. Klas-STOPP behövs inte om CTO:s val är entydigt motiverat mot
  principer (CC går direkt till implementation efter CTO-beslut). Se §9.6.
- **security-auditor** — kod som rör PII, auth, secrets eller external integrations
- **code-reviewer + dotnet-architect** — större kodändringar (>5 filer eller arkitekturella val)
- **dotnet-architect (obligatorisk)** — all Terraform-/IaC-scope (modul-ändring,
  ny resurs, tfvars som rör prod). Kodifierar ADR 0036-precedensen: CTO+architect-
  tandem bär infra-granskning utan dedikerad infra-agent (medvetet anti-bloat,
  roster-gap-CTO 2026-05-17 §1.2).
- **db-migration-writer** — nya migrations
- **test-writer** — nya domain-typer eller handlers

Agent-rapporter sparas i `docs/reviews/<datum>-<fas>-<agent>.md` och bifogas STOPP-rapporten så Klas kan granska parallellt. Att hoppa över relevant agent-invocation räknas som disciplinmiss.

### 9.3 När du är osäker

- **Läs först** — sök i repo, läs BUILD.md, kolla befintliga mönster
- **Fråga sedan** — ställ konkreta frågor till Klas, inte generiska "hur vill du göra"
- **Gissa aldrig** — om du inte vet om en feature ska finnas, fråga innan du bygger den

---

### 9.4 Discovery-rapporter och verifiering

Strukturella spärrar mot sammanfattnings-glidning och otillämpade ändringar.

**När använda discovery-rapport:** vid osäkerhet om fil-state, on-disk-config, befintlig kodstruktur eller existerande patterns. Format:

> "Discovery: läs/kartlägg X. Rapportera Y. Inga ändringar."

Discovery är gratis. Använd liberalt — kostnaden är minimal jämfört med att agera på fel antagande.

**Rå output-krav:** discovery-rapport innehåller hela filer i kodblock. Inga `...`-trunkeringar, inga sammanfattningar, ingen TL;DR. Om filen är >500 rader: rapportera hela filen ändå — webb-Claude behöver verbatim text för att designa `str_replace`.

**Paste-verifiering:** efter `str_replace` eller paste skall STOPP-rapporten innehålla `grep`- eller `git diff`-output som bevisar fil-state. Påståenden om "verbatim paste:at" utan verifierings-evidens behandlas som otillämpat tills bevisat.

**Pre-flight-check för `str_replace`:** vid långa paste:ar (>20 rader eller flera sektioner samtidigt) — visa target-sträng + nytt innehåll i kodblock innan apply, vänta på GO. Pre-flight-check är inte overhead när det fungerar — det är försäkring mot omformulerings-glidning.

**Verbatim-text-källa:** när text ska appliceras verbatim till fil (ADR-sektion, dokumentation, kod-snippet) producerar webb-Claude källtexten. CC:s roll är att applicera, inte konstruera. Om CC saknar källtext i kontext (t.ex. efter kompaktering): STOPP, be webb-Claude om verbatim text.

### 9.5 Web-search vid osäkerhet om externa fakta

Externa fakta uppdateras konstant. Training data är out-of-date i veckor till månader. Vid present-tense-frågor om externa system: web-search > gissning från minnet.

**Triggers för web-search:**

- AWS — feature, pris, region, IAM-policy-format, Bedrock-modell-tillgänglighet
- .NET / Next.js / TypeScript — library-version, breaking changes, deprecation-status
- AI-modeller — modell-namn, kontextfönster, prissättning, EU-inferens-tillgänglighet
- Claude-features — Claude Code-flaggor, SDK-versioner, agent-konfiguration
- NuGet / npm — paket-status, senaste version, compat-matriser

**Regel:** vid feature/version/pris-frågor i presens — sök innan du svarar. Gissa inte från training data.

**Källprioritering:** officiella docs och release-notes > paketregistry > tredje-parts-blogg. Verifiera datum på källan.

**Rapportering:** vid web-search-baserade beslut — bifoga URL + datum i STOPP-rapporten så Klas kan följa upp källan.

### 9.6 In-scope-fix vs TD-skapande (fas-regeln)

När agent-review eller egen analys identifierar ett fynd: lyft **inte** som TD
som default. Default = **fixa in-block**.

TD lyfts ENDAST om ett av två kriterier uppfyllt:

1. **Annan fas:** fyndet hör till fas där feature/dependency ännu inte finns
   (t.ex. "BYOK-onboarding fas 3" innan BYOK-domän skapad). TDs som faktiskt
   tillhör nuvarande fas ska fixas innan fas-stängning, inte skjutas vidare.
2. **Saknad funktion-dependency:** scope kräver kod/projekt som inte existerar
   (t.ex. "JobbPilot.Api.UnitTests-projekt finns inte" — TD-49)

**Ingen tidsbegränsning per touch.** Tidigare 4h-regel borttagen 2026-05-11
efter Klas-direktiv: TD-bloat skapas av tidströskel-utlyftningar som sedan
återkommer i nästa session. Scope per touch begränsas av fas-tillhörighet,
inte CC-tid. Stora fynd inom rätt fas fixas i samma batch eller i naturlig
split-batch — inte som TD.

Vid tveksamhet: in-scope-fix vinner. JobbPilots policy: kvalitet > tempo.

**Anti-pattern:** "spara TD så scope inte växer" — vi måste ändå fixa det förr
eller senare. TD-listan är inte ett dumpning-ställe — det är ett verktyg för
att skjuta upp arbete som genuint inte hör till nu.

**Beslutsflöde:**

1. CC eller annan agent identifierar ett fynd
2. Default = fixa in-block (samma commit-batch som originaluppdraget)
3. Vid multi-approach-val, fynd-triage (in-block-fix vs TD), eller annan
   beslutspunkt: invokera `senior-cto-advisor` för avgörande. CC ger **inte**
   egen rekommendation — CTO är decision-maker.
4. CTO citerar branschens källor (Robert Martin, Eric Evans, GoF, Fowler,
   Beck, Microsoft Learn) vid kvalitets-tradeoffs
5. **CC följer CTO-beslutet automatiskt utan extra Klas-GO** när motiveringen
   är entydig mot principer. Klas-STOPP triggas endast vid större strategiska
   frågor (t.ex. fas-skifte, ADR-amendment, deploy-beslut). CTO flaggar i sitt
   svar om beslutet är sådant som Klas behöver godkänna.
6. Klas har alltid sista ordet — CTO argumenterar tydligt så Klas-override är
   medveten, inte gissning

### 9.7 TD-livscykel — var och hur TDs skrivs

Tech debt-listan splittades 2026-05-11 i två filer per senior-cto-advisor-
triage (Severity × Fas-matris för aktiva, kronologiskt arkiv för stängda).
**Kommentar:** TD-IDs är monotoniskt växande och **återanvänds aldrig**, även
om hopp finns i numreringen (t.ex. TD-32 till TD-36 i ADR 0027-luckorna).

**Filer:**

- `docs/tech-debt.md` — aktiva TDs i **Severity × Fas-matris**
- `docs/tech-debt-archive.md` — stängda TDs i **kronologisk stäng-datum-ordning** (full kropp bevarad)

**När du lyfter en ny TD:**

1. **Verifiera först att den faktiskt ska lyftas** (CLAUDE.md §9.6 fas-regel).
   Default = fixa in-block. TD lyfts ENDAST om annan fas eller saknad
   funktion-dependency.
2. **Allokera nästa TD-ID** = max(befintliga TD-IDs i båda filer) + 1. Greppa
   `TD-[0-9]+` i `docs/tech-debt.md` + `docs/tech-debt-archive.md`.
3. **Skriv TD-blocket i `tech-debt.md`** under rätt Severity × Fas-sektion.
   Använd `## TD-N: <titel>` (h2 med kolon, inte h3). Fält som ska finnas:
   `**Kategori:**`, `**Severity:**`, `**Fas:**`, `**Källa:**`, beskrivning,
   `**Föreslagen åtgärd:**`, ev. `**Beroenden:**` och `**Trigger:**`.
4. **Uppdatera översiktstabellen** överst i `tech-debt.md` med ny rad
   (ID | Titel | Severity | Fas | Kategori). Numerisk ID-ordning bevaras
   ej i översiktstabellen — sortering är Severity → Fas → ID.
5. **Cross-refs:** om TD refereras från ADR eller session-log, säkerställ
   att referensen finns i någon av de två filerna.

**När du stänger en TD:**

1. **Flytta hela TD-blocket från `tech-debt.md` till `tech-debt-archive.md`.**
   Bevara full kropp + lägg till stängningsnotat (datum, commit/STEG-referens,
   leverans-anteckningar, reviews). Strippa INTE till "bara namn + titel" —
   stängda TDs är granskningstrail (Fowler 2018, Ford/Parsons/Kua 2017).
2. **Placera i kronologisk ordning** i arkivet (äldsta först — nya stängningar
   appende sist).
3. **Ta bort från `tech-debt.md`:s Severity × Fas-sektion** + från
   översiktstabellen.
4. **Lägg till rad i "Stängda TDs"-tabellen** i slutet av `tech-debt.md`:
   `| TD-N | Titel | Stängd YYYY-MM-DD | commit/STEG |`.

**När du ersätter en TD (split/merge):**

- Originalt TD-block markeras `~~Titel~~ — ERSATT YYYY-MM-DD av TD-Na + TD-Nb`
  och flyttas till arkivet med kort förklaring av split-skälet.
- Nya TD-poster (t.ex. TD-Na, TD-Nb) får egna fullständiga block i
  `tech-debt.md` med `**Källa:** TD-N split per senior-cto-advisor-triage YYYY-MM-DD`.

**Severity-klassificering:**

- **Major:** säkerhetsblocker, tidsbundet, eller kritiskt för fas-stängning
- **Minor:** allt annat (a11y/UX-polish, code-hygiene, defensive refactors,
  framtida feature-arbete)
- Vid tveksamhet: invokera senior-cto-advisor.

**Fas-klassificering:**

- **Fas Nu** = tidsbundet eller akut (bör vara tom när möjligt)
- **Fas 1** = nuvarande fas, ska fixas innan fas-stängning
- **Fas 2 / 3+** = framtida fas där feature/dependency saknas idag
- **Efter MVP / Trigger** = adresseras vid faktisk användarsignal,
  skala-tröskel eller opportunistisk touch

**Ej kategorisera som Minor — Fas Nu.** Om en TD passar där: fixa in-block
istället, lyft inte.

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

- Docker Compose i repo-root: `postgres`, `redis`, `seq` (local Serilog sink). Riktig AWS används direkt — ingen LocalStack i dev (medvetet val per SESSION-2-PLAN).
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
