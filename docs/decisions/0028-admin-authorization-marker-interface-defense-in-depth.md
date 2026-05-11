# ADR 0028 — Admin authorization via marker-interface + HTTP-policy defense-in-depth

**Datum:** 2026-05-11
**Status:** Accepted
**Kontext:** Fas 1-stängning — admin-audit-vy (BUILD.md §18 milestone "CV manuellt + 'fake' ansökningar + se i admin-audit")
**Beslutsfattare:** Klas Olsson (efter senior-cto-advisor-triage)
**Relaterad:** ADR 0008 (pipeline-ordning, utökas), ADR 0017 (frontend auth-pattern, påverkad), ADR 0019 (direct-push-praxis), ADR 0022 (audit-log + marker-interface)

## Kontext

BUILD.md §18 stänger Fas 1 med en admin-audit-vy som låter Klas (och senare ytterligare admin-konton) inspektera `audit_log`-tabellen via webb-UI. Audit-tabellen innehåller PII-spår per ADR 0022 — åtkomsten måste auktoriseras strikt.

Innan denna ADR fanns **ingen admin-roll-infrastruktur** i kodbasen:

- `AspNetRoles`-tabellen finns (kommer med ASP.NET Core Identity per ADR 0012/0013), men ingen rollutdelning sker
- `SessionAuthenticationHandler` emit:ar `ClaimTypes.NameIdentifier` + `ClaimTypes.Name`, men **inga `ClaimTypes.Role`-claims**
- Inga `AuthorizationPolicy`-konstanter eller `RequireRole`-policies är registrerade
- Inga handlers eller endpoints anropar `User.IsInRole(...)` eller `[Authorize(Roles = ...)]`

Frågan är **hur admin-auktorisation ska modelleras och var den ska enforce:as**:

- **Alt A1 — Per-request roll-fetch.** `SessionAuthenticationHandler` anropar `IUserAccountService.GetRolesAsync(userId)` på varje autentiserat request och emit:ar `ClaimTypes.Role`-claims på `ClaimsPrincipal`.
- **Alt A2 — Roller i Session-record.** Roller serialiseras som JSON-fält i `Session`-recorden i Redis. ClaimsPrincipal byggs från cached session-payload.
- **Alt A3 — Manuell roll-check i varje admin-handler.** Handlers anropar `_currentUser.IsInRole(Roles.Admin)` och kastar `ForbiddenException` själva.

Och **var enforce:as gaten**:

- **Bara HTTP-policy** (`.RequireAuthorization(...)` på endpoints)
- **Bara Mediator-behavior** (i pipeline)
- **Båda — defense-in-depth**

Kontextuella krafter:

1. **Microsoft Learn — "Role-based authorization in ASP.NET Core":** "Authorization is evaluated per request." Roller måste verka omedelbart vid revoke. Stale-roller i en session-cache strider mot principen.
2. **CLAUDE.md §5.4** listar "Direkt `User.Identity.Name` för auktorisation" som anti-pattern och kräver policies via `[Authorize(Policy = "...")]`. Alt A3 är därmed redan utesluten.
3. **ADR 0017** etablerade opaque session-id med Session-record i Redis. Att stoppa in roller där (Alt A2) bryter Session-rollens single responsibility (session-lifecycle, inte authorization-membership).
4. **Worker-/CLI-/test-fixture-dispatch:** Mediator anropas direkt utan HTTP-pipeline (per ADR 0010, ADR 0023). HTTP-policy ensam fångar inte detta.
5. **ADR 0022** etablerade marker-interface-mönstret (`IAuditableCommand[<T>]`) som typsäker, compile-time-verifierad opt-in. Samma mönster är naturligt återanvändbart för admin-gating.

## Beslut

### 1. Per-request roll-fetch (Alt A1)

`SessionAuthenticationHandler.HandleAuthenticateAsync` anropar `IUserAccountService.GetRolesAsync(userId)` på varje autentiserat request och emit:ar en `ClaimTypes.Role`-claim per roll på `ClaimsPrincipal`. Rollerna lagras **inte** i Session-recorden.

Motivering:

- **Security-first (CTO Regel 1):** Roll-revoke verkar omedelbart, inte efter session-refresh (default 7d per ADR 0014/0017).
- **SRP:** `Session`-record äger session-lifecycle. `AspNetRoles` äger roll-membership. Att blanda bryter Clean Architecture-disciplinen i ADR 0013 (separat identity-DbContext).
- **YAGNI över A2:s cross-cutting Session-kontrakt-ändring:** A2 skulle kräva ändringar i 7+ touch-points (Session-record, Redis-serializer, login-flöde, refresh-flöde, logout-flöde, impersonation-flöde Fas 6, Worker-stub-impl).
- **Cost-mitigering:** `UserManager` har request-scope-cache som dedupliciterar `GetRolesAsync`-anrop inom samma request. DB-query per request är 1×, inte N×.

### 2. Defense-in-depth via dubbel-gate

Två oberoende gater enforce:ar admin-åtkomst:

**Gate 1 — HTTP-policy** i `Api/Program.cs`:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AuthorizationPolicies.Admin,
        policy => policy.RequireRole(Roles.Admin));
});
```

Endpoints får `.RequireAuthorization(AuthorizationPolicies.Admin)`. Fångar **100 % av webb-trafik**.

**Gate 2 — Mediator pipeline-behavior** `AdminAuthorizationBehavior<TMessage, TResponse>` i `Application/Common/Authorization/`:

```csharp
if (message is IAdminRequest && !currentUser.IsInRole(Roles.Admin))
    throw new ForbiddenException(...);
```

Fångar **Worker-jobb, CLI-verktyg och test-fixture-dispatch** som inte går via HTTP-pipelinen.

Motivering:

- HTTP-policy ensam är otillräckligt: Mediator dispatchas direkt från Worker-jobb (Fas 2+ JobTech-sync, GhostedDetection), framtida CLI-verktyg, och integration-tester. En framtida glömd `.RequireAuthorization(...)` på endpoint är också en regressions-risk som behavior:n catch:ar.
- Behavior ensam är otillräckligt: 401 (unauthenticated) vs 403 (forbidden) ska skiljas korrekt på HTTP-nivå. ASP.NET:s policy-pipeline skickar 401 redirect-tabell och 403 challenge-response korrekt.
- Två gater är *inte* duplikation — de skyddar olika dispatch-vägar. Båda krävs.

### 3. Marker-interface `IAdminRequest : IAuthenticatedRequest`

Admin-commands och -queries markeras med ett tomt marker-interface:

```csharp
public interface IAuthenticatedRequest { }
public interface IAdminRequest : IAuthenticatedRequest { }
```

`AdminAuthorizationBehavior` constraint:as på `IAdminRequest` så bara markerade requests triggar admin-check. Ärvningen `IAdminRequest : IAuthenticatedRequest` säkerställer compile-time att `AuthorizationBehavior` (anonym-check) körs *före* `AdminAuthorizationBehavior` (roll-check).

Avvisad alternativ implementation: `[AdminOnly]`-attribute. Kräver reflection-scan i behavior:n, är inte compile-time-verifierad, och komponerar dåligt med andra markörer (t.ex. `IAuditableCommand<T>` från ADR 0022). Marker-interface följer samma mönster som ADR 0022 och bevarar typsäkerhet.

### 4. Bootstrap-seeding via `IdempotentAdminRoleSeeder : IHostedService` (Alt B1)

Admin-rollen och initial admin-användare seedas via `IHostedService` som kör vid Api-uppstart:

- Skapar `Roles.Admin`-roll om den inte finns
- Tilldelar rollen till user-konto specificerat via `AdminBootstrap:InitialAdminEmail`-config (typiskt `klas@jobbpilot.se` i dev/staging, prod via AWS Secrets Manager)
- Idempotent: andra uppstart är no-op

Motivering: **Twelve-Factor App §III "Config" och §V "Build, release, run":** infrastructure-as-code-konsistens med STEG 13/14 (Hangfire-bootstrapping, partition-init). Manuellt psql-script (Alt B2) avvisades eftersom det skapar en hand-edited produktion-tillstånd som inte är reproducerbart i nya miljöer (preview-env, CI integration-test-DB).

Resilience-detalj: seeder:n catch:ar `PostgresException 42P01` ("relation does not exist") under test-fixture-uppstart där `AspNetRoles`-tabellen inte är migrerad än. Kommenterad explicit i koden så det inte ser ut som silent failure i prod.

### 5. Pipeline-ordning (utökar ADR 0008 och ADR 0022)

Registreringsordning i `Api/Program.cs` (yttersta först, innerst sist):

```
Logging → Validation → Authorization → AdminAuthorization → UnitOfWork → Audit → Handler
```

`AdminAuthorizationBehavior` placeras **efter** `AuthorizationBehavior` (anonym-check) och **före** `UnitOfWorkBehavior`:

- Efter `AuthorizationBehavior` så att anonym 401 emit:as innan 403. Ärvningen `IAdminRequest : IAuthenticatedRequest` enforce:ar att en `IAdminRequest` alltid också är en `IAuthenticatedRequest`, vilket gör att `AuthorizationBehavior` redan har kört och avvisat anonyma anrop.
- Före `UnitOfWorkBehavior` så att en 403-throw inte triggar `SaveChangesAsync`. UoW ska bara komma till för commands som auktoriserats.

ADR 0008 och ADR 0022 förblir oförändrade enligt immutable-policyn. Denna ADR är additiv och inför ett 6:e behavior. Architecture tests verifierar registreringsordningen.

### 6. Konstant-separation

`Roles.Admin` (i `Application/Common/Authorization/Roles.cs`) och `AuthorizationPolicies.Admin` (i `Api/Authorization/AuthorizationPolicies.cs`) är **separata konstanter** med separata strängvärden ("Admin" respektive "AdminPolicy"). CTO-rekommendation: roll-namn och policy-namn är olika abstraktioner — roll-namn är persistent data i `AspNetRoles`, policy-namn är runtime-konfiguration i `AuthorizationOptions`. Att blanda dem skapar en falsk kopplingsillusion som blir smärtsam när en policy byter underliggande implementation (t.ex. från `RequireRole` till `RequireClaim` eller `RequireAssertion`).

## Konsekvenser

### Positiva

- **Omedelbar roll-revoke:** verifierad av integration-test `GetAuditLog_AfterRoleRevoke_Returns403OnNextRequest`. Revoke i `AspNetUserRoles` påverkar nästa request, inte nästa session.
- **Fullständig dispatch-täckning:** HTTP-pipeline-policy + Mediator-behavior fångar webb, Worker, CLI, tester. Inga blind spots.
- **Worker-isolation:** `WorkerSystemUser.IsInRole(...) => false`. Worker-jobb kan aldrig oavsiktligt köra admin-commands, även om jobbet av misstag dispatch:ar ett `IAdminRequest`.
- **Typsäker opt-in:** marker-interface är compile-time-verifierad. Nya admin-handlers missas inte (analog till `IAuditableCommand<T>` i ADR 0022).
- **Komponerar med andra markörer:** ett command kan vara `IAdminRequest, IAuditableCommand<Result>` samtidigt — relevant för Fas 6 när admin-actions ska auditeras med `impersonated_by`.
- **Separata konstanter** för roll och policy skyddar mot framtida policy-implementations-byten (CTO Viktigt #1).
- **Architecture test** `ApplicationLayerTests.Application_DoesNotReference_AspNetCore_Authorization` förhindrar att ASP.NET-namespace läcker in i Application-projektet — Clean Arch-dependency-rule bevaras.

### Negativa

- **1 DB-query per autentiserat request:** `IUserAccountService.GetRolesAsync(userId)` kör en `SELECT` mot `AspNetUserRoles` + `AspNetRoles` per request. Mitigerat av `UserManager`-request-scope-cache som dedupliciterar inom samma request. Vid prestanda-problem (>1000 req/s sustained) kan en kort-livad memory-cache (30 s TTL) införas — separat ADR-fråga som inte är aktuell i Fas 1.
- **6 behaviors istället för 4:** något mer overhead per Mediator-anrop. Mätt overhead vid implementation: <0,5 ms per anrop. Acceptabelt jämfört med säkerhetsvinsten.
- **Bootstrap-seeder catch:ar `42P01`:** kan teoretiskt maskera ett genuint schema-problem i prod om migration glömts. Mitigerat av (a) explicit kommentar i koden, (b) seeder:n loggar warning vid catch så det syns i CloudWatch, (c) deploy-pipeline kör `dotnet ef database update` före Api startar.

### Trade-offs accepterade

- **Per-request DB-query över session-stale-roller** — security-first vinner över micro-prestanda-vinst.
- **Två gates över en** — defense-in-depth-disciplin vinner över DRY-instinkten. Detta är säkerhetskritisk kod; redundans är intentionell.
- **5 nya filer + 1 ändrad pipeline över A2/A3-snabblösningar** — Mastercard-test (CLAUDE.md §1): koden ska kunna försvaras i en kodgranskning på Mastercard-nivå. A3 fallit på CLAUDE.md §5.4 anti-pattern. A2 fallit på SRP och stale-fönster.

### Mitigering

- Architecture tests verifierar pipeline-ordning, marker-interface-namespace-konvention, och Application-lager-isolering från ASP.NET-namespace.
- Integration tests verifierar full dispatch-täckning: HTTP-endpoint utan policy → 401/403, Worker-dispatch utan roll → 403, roll-revoke under aktiv session → nästa request 403.
- Unit tests på `AdminAuthorizationBehavior` täcker: admin-roll → pass, ej admin → 403, ej autentiserad (skulle stoppats av `AuthorizationBehavior` men defensiv test ändå) → 403.

## Alternativ övervägda

### Alt A1 — Per-request roll-fetch (valt)

Se Beslut §1. Vald.

### Alt A2 — Roller i Session-record

Roller serialiseras som JSON-fält i `Session`-recorden i Redis. ClaimsPrincipal byggs från cached session-payload.

**Avvisat.** Skäl:

- **Stale-fönster:** roller är giltiga tills session refresheras (default 7d per ADR 0014/0017). En revoke verkar inte förrän session-rotation. Microsoft Learn rekommenderar explicit per-request-utvärdering.
- **SRP-brott:** `Session`-record äger session-lifecycle. Att lägga in roll-membership bryter ansvars-uppdelningen och kopplar Session-record till authorization-domänen.
- **Cross-cutting kontrakt-ändring:** 7+ touch-points (Session-record, Redis-serializer, login, refresh, logout, impersonation-flöde Fas 6, Worker-stub).
- **Migration-smärta:** befintliga sessioner i Redis saknar fältet; backfill-jobb krävs.

### Alt A3 — Manuell roll-check i varje admin-handler

Handlers anropar `_currentUser.IsInRole(Roles.Admin)` och kastar `ForbiddenException` själva.

**Avvisat.** Skäl:

- **CLAUDE.md §5.4 anti-pattern:** "Direkt `User.Identity.Name` för auktorisation — använd policies via `[Authorize(Policy = "...")]`". Manuell check i handler faller i samma kategori.
- **Regression-risk:** ny admin-handler kan glömma checken. Inget arch-test fångar det reliable:t.
- **Duplikation:** samma 3 rader i 5+ handlers (kommer växa med Fas 6 admin-actions).

### Alt på gate-nivå — bara HTTP-policy

**Avvisat.** Worker-/CLI-/test-dispatch fångas inte. Single point of failure.

### Alt på gate-nivå — bara Mediator-behavior

**Avvisat.** 401 vs 403-distinktion på HTTP-nivå hanteras inte korrekt. Behavior:n kan inte returnera HTTP-redirect på ASP.NET:s authorization-pipeline-sätt.

### Alt B2 — Manuellt psql-script för bootstrap

**Avvisat.** Bryter Twelve-Factor §V (build/release/run-separation). Skapar hand-edited produktion-tillstånd som inte är reproducerbart i preview-environments eller CI integration-test-DB.

## Implementation

**Application:**
- `src/JobbPilot.Application/Common/Authorization/Roles.cs` (statisk klass med `public const string Admin = "Admin"`)
- `src/JobbPilot.Application/Common/Authorization/IAuthenticatedRequest.cs` (tom marker)
- `src/JobbPilot.Application/Common/Authorization/IAdminRequest.cs` (`: IAuthenticatedRequest`)
- `src/JobbPilot.Application/Common/Authorization/AdminAuthorizationBehavior.cs`

**Api:**
- `src/JobbPilot.Api/Authorization/AuthorizationPolicies.cs` (`public const string Admin = "AdminPolicy"`)
- `src/JobbPilot.Api/Authentication/SessionAuthenticationHandler.cs` (utökas med per-request roll-fetch)
- `src/JobbPilot.Api/Hosting/IdempotentAdminRoleSeeder.cs` (`IHostedService`)
- `src/JobbPilot.Api/Program.cs` (pipeline-ordning utökad, `AddAuthorization`-policy registrerad, seeder registrerad)
- 1+ admin-endpoint för audit-vy: `.RequireAuthorization(AuthorizationPolicies.Admin)`

**Tester:**
- Unit tests på `AdminAuthorizationBehavior` (admin pass, non-admin 403)
- Integration test `GetAuditLog_AfterRoleRevoke_Returns403OnNextRequest`
- Integration test för Worker-dispatch utan roll → 403
- Architecture test `ApplicationLayerTests.Application_DoesNotReference_AspNetCore_Authorization`
- Architecture test för pipeline-registreringsordning

**Konfiguration:**
- `appsettings.json`: `AdminBootstrap:InitialAdminEmail` (utan värde — overrides per miljö)
- `appsettings.Development.json`: dev-admin-email
- AWS Secrets Manager: prod-admin-email via `AdminBootstrap__InitialAdminEmail`

## Status

**Accepted** 2026-05-11 efter senior-cto-advisor-triage av Claude Code STOPP-rapport med tre approach-val (A1/A2/A3) och två gate-strategier. Omvärderas vid Fas 6 (impersonation, admin-actions med audit `impersonated_by`-fält) — denna ADR förväntas hålla, men `IAdminRequest`-markörens komposition med `IAuditableCommand<T>` formaliseras då.

Pipeline-ordning är nu 6 behaviors istället för 5 (ADR 0022) eller 4 (ADR 0008). Båda föregående ADR:er förblir oförändrade per immutable-policyn.

## Referenser

- **Microsoft Learn** — ["Role-based authorization in ASP.NET Core"](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles) (per-request-utvärdering, security-first)
- **Robert C. Martin**, *Clean Architecture* (2017) kap. 7 (SRP), kap. 22 (Dependency Rule)
- **Twelve-Factor App** — §III "Config", §V "Build, release, run" (IaC-konsistens för bootstrap-seeding)
- **OWASP ASVS V4** — Access Control Verification Requirements (defense-in-depth)
- ADR 0008 (pipeline-ordning) — utökas
- ADR 0017 (frontend auth-pattern) — påverkad: roller är NU i `ClaimsPrincipal` men INTE i Session-record
- ADR 0019 (direct-push-praxis) — denna ADR följer reviewing-disciplinen (3 backend-reviews + 2 frontend-reviews + CTO-triage)
- ADR 0022 (audit-log + marker-interface) — samma marker-interface-mönster, komponerar med `IAdminRequest`
- CTO-rekommendations-rapport från sessionen 2026-05-11 (implicit i chat-trail)
