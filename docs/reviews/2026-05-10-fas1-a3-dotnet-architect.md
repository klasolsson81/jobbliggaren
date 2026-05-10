# Fas 1 A3 — dotnet-architect-review: TD-31 UseHttpsRedirection env-gate-test

**Fil:** `tests/JobbPilot.Api.IntegrationTests/Configuration/UseHttpsRedirectionGateTests.cs`
**Datum:** 2026-05-10
**Granskare:** dotnet-architect

## Sammanfattning

Strukturen är kanonisk WAF-pattern (abstract base + sealed derived) och replikerar
`ProductionStartupSmokeTests` korrekt. `PostConfigure<HttpsRedirectionOptions>` är
rätt val för WAF-test-host. Implementationen är produktionsmässig men har två
mindre arkitektur-fynd (env-var-läckage risk + redundant `UseEnvironment`) och
ett mindre coverage-gap (HSTS-headern är inte täckt av A3 men hör tematiskt
ihop). **Approve** för in-block-fix av Mindre 1 + 2.

## Fynd

### Viktigt
Inga.

### Mindre 1 — `UseEnvironment(EnvironmentName)` redundant men ofarlig (rad 63)

**Vad:** Kommentaren på rad 60–62 säger korrekt att `UseEnvironment` är
otillräckligt och att riktig override sker via `Environment.SetEnvironmentVariable`
i `InitializeAsync`. Anropet finns ändå kvar.

**Varför:** WAF-host byggs lazy vid första `Services`-access (rad 118
`Services.CreateScope()`). `Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", ...)`
på rad 110 körs *innan* host-build → `WebApplication.CreateBuilder()` plockar
upp env-varen. `builder.UseEnvironment()` skriver bara till `IWebHostBuilder`-config-
pipeline som körs *efter* `WebApplication.CreateBuilder()` för minimal API.
Anropet är no-op men inte skadligt.

**Åtgärd:** Ta bort rad 63 + kommentar 60–62. Behåll bara den självförklarande
kommentaren på rad 107–109 i `InitializeAsync`. Reducerar kognitiv last
(TD-37-läxan blir tydligare när det otillräckliga anropet inte finns kvar).

### Mindre 2 — Env-var-läckage mellan collections vid parallell race (rad 110–116, 125–131)

**Vad:** `parallelizeTestCollections=false` garanterar att test-*körning* är
seriell, men `WebApplicationFactory`-fixtures lyfts som `[CollectionFixture]`
och xunit bygger fixtures eager vid första test-resolve. Om två fixtures hinner
allokeras innan första testet kör (vilket händer i v3 med shared host), kan
`InitializeAsync` på den ena skriva över env-varen som den andras host redan
har läst.

**Varför:** I praktiken minimal risk eftersom `parallelizeTestCollections=false`
också serialiserar fixture-initialization per collection. Men patternet är
fragilt — `Environment.SetEnvironmentVariable` är process-globalt, och TD-37
visade att envvar-races kan trigga subtila build-fail.

**Åtgärd (defensiv, in-block):** Lägg till en `lock`-baserad mutex i bas-klassen
runt env-var-set + host-trigger, eller dokumentera explicit beroendet på
`parallelizeTestCollections=false` i klass-kommentaren med en hänvisning till
`xunit.runner.json`. Alternativt accepterat som-is med kommentar — risken
realiseras inte med nuvarande config.

### Mindre 3 — `GC.SuppressFinalize(this)` i bas-DisposeAsync (rad 138)

**Vad:** Korrekt CA1816-fix för `IAsyncDisposable`-mönster.
`WebApplicationFactory<T>` har egen finalizer-pattern och `base.DisposeAsync()`
på rad 137 hanterar redan SuppressFinalize internt.

**Varför:** `GC.SuppressFinalize(this)` efter `base.DisposeAsync()` är dubbelt —
`WebApplicationFactory.DisposeAsync()` anropar redan `SuppressFinalize`. Inte
fel, men onödigt.

**Åtgärd:** Ta bort rad 138. Eller, om CA1816 ändå klagar, flytta
`SuppressFinalize` *före* `base.DisposeAsync()` så analyzern är nöjd och
dubbelanropet undviks.

### Mindre 4 — Coverage-gap: HSTS-header-verifiering (utanför A3-scope)

**Vad:** Test 2 (HttpsRedirectionEnabledProductionFactory) asserterar
`307 + Location.Scheme=https` men *inte* `Strict-Transport-Security`-header.
Program.cs:150–153 sätter HSTS via samma env-gate-mönster.

**Varför:** TD-31-spec listar 3 tester och de täcker `UseHttpsRedirection`-gaten
exakt. HSTS hör tematiskt ihop men är separat middleware. Acceptabelt att
lämna utanför A3 — men notera i `tech-debt.md` att HSTS-header-anti-regression
saknar test (potentiell TD-44 eller liknande).

**Åtgärd:** Ingen ändring i A3. Lämna till uppföljande TD om Klas vill ha det.

## Svar på specifika frågor

1. **WAF abstract base + sealed derived:** Kanonisk. Parameterized fixture via
   `TheoryData` fungerar inte för WAF eftersom varje variant kräver egen host
   + egen Testcontainers-instans (constructor-time state). Abstract base är
   rätt val.

2. **`PostConfigure<HttpsRedirectionOptions>`:** Rätt val. Påverkar inte andra
   middleware-läsare — `HttpsRedirectionOptions` är dedicerad till
   `UseHttpsRedirection`-middleware. Env-var `ASPNETCORE_HTTPS_PORTS` är
   ALB-port-config och hör inte hemma i test-host.

3. **Cost-trade-off 3 containers:** ~30 sek är acceptabelt för säkerhets-gate-test.
   Delad fixture med restart skulle kräva env-var-mutation mellan tester →
   bryter mot WAF-immutable-host-pattern.

4. **`UseEnvironment` redundans:** Se Mindre 1 — ta bort.

5. **`GC.SuppressFinalize`:** Se Mindre 3 — överflödig men inte skadlig.

6. **Test-isolation:** Säker givet `parallelizeTestCollections=false`. Se
   Mindre 2 för defensiv förbättring.

7. **Test-coverage:** 3 happy-path räcker för Sec-Major-2-anti-regression.
   Negative-test (middleware-fail) skulle testa ASP.NET Core-framework, inte
   JobbPilot-kod. HSTS-coverage = separat TD.

## Approve-status

**Approve** med två rekommenderade in-block-fixar:

- **Mindre 1:** Ta bort `builder.UseEnvironment(EnvironmentName)` + kommentar
  rad 60–63
- **Mindre 3:** Flytta `GC.SuppressFinalize(this)` FÖRE `await base.DisposeAsync()`
  (idempotent + analyzer-nöjd, undviker dubbel-anrop)

Mindre 2 + 4: ingen åtgärd i A3.

## Referenser

- CLAUDE.md §2.1 — Clean Architecture (test-host får trampa lager)
- CLAUDE.md §7 — Integration-test-krav (ny endpoint = integration-test)
- ADR 0026 — Alb:HttpsEnabled env-gate
- TD-31 i `docs/tech-debt.md:1167`
- TD-37-läxa (commit `92042cb`) — `ConnectionStrings__Redis` i fixtures
