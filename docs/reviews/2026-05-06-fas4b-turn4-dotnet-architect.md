# dotnet-architect — Pre-impl rapport Turn 4

**Datum:** 2026-05-06
**Scope:** AuthenticationHandler design, scheme-namn, claims, [Obsolete]-pattern, logout-scope

---

## 1. AuthenticationHandler vs middleware

### Rekommendation: `SessionAuthenticationHandler : AuthenticationHandler<SessionAuthenticationSchemeOptions>`

| Aspekt | Custom middleware | AuthenticationHandler |
|---|---|---|
| 401 vs 403 | Måste implementeras manuellt | Inbyggt: `HandleChallengeAsync` → 401, `HandleForbiddenAsync` → 403 |
| `[Authorize]`-integration | Måste skriva egen filter | Fungerar ut-ur-lådan med befintlig `RequireAuthorization()` (4 call-sites) |
| Multi-scheme (framtida OAuth) | Manuellt | `AddAuthentication().AddScheme(...).AddJwtBearer(...)` parallellt — krävs när OAuth läggs till per ADR 0017 |
| `AuthenticateResult.Fail("…")` | Saknar koncept | Strukturerad orsak propageras (utgången, ogiltig, store nere) |
| Test-isolering | Måste mocka hela request-pipeline | `WebApplicationFactory` + `TestAuthHandler` är standardmönster |

**Edge-case att lösa explicit:** Vid `SessionStoreUnavailableException` ska handler INTE returnera `Fail` (det blir 401 — fel signal). Den ska låta exception bubbla till middleware-pipelinen som redan översätter till 503.

---

## 2. Scheme-namn

### Rekommendation: Behåll `"Bearer"` i Turn 4 — migrera till `"Session"` i Fas 1

**Rationale:**
- Wire-format är oförändrat per ADR 0017 §6: backend tar emot `Authorization: Bearer <session-id>`
- Schemnamnet `"Bearer"` beskriver wire-format, inte token-typ — korrekt även för opaque session-id:n
- Alla 4 `RequireAuthorization()`-call-sites (`AuthEndpoints.cs:65`, `MeEndpoints.cs:18,24,33`) använder default scheme — noll ändringar i endpoints
- Inga `[Authorize(AuthenticationSchemes = "...")]`-attribut existerar i kodbasen (grep-verifierat)

**Lägg till XML-doc-kommentar på handler-registreringen:**
```csharp
// Scheme-namnet "Bearer" speglar wire-format (Authorization: Bearer <token>),
// inte token-typ. Backend lagrar opaque session-id i Redis sedan Turn 4 (ADR 0017).
// Schemnamnet byter till "Session" när JWT-klasserna raderas i Fas 1.
```

---

## 3. Claims-design

### Exakta claims att sätta vid session-validation

| Claim type | Värde | Källa | Varför |
|---|---|---|---|
| `ClaimTypes.NameIdentifier` | `Session.UserId.ToString()` | `ISessionStore.GetAsync` | Standard `User.Identity.Name`-mappning |
| `JwtRegisteredClaimNames.Sub` | `Session.UserId.ToString()` | Samma | Bakåtkompatibel med `CurrentUser.cs:17-18` (läser Sub OR NameIdentifier) |
| `"session_id_prefix"` (custom) | 6-tecken-prefix + `…` | `Session.Id` | Strukturerad logging utan att läcka raw token (ADR 0017 §Log and Audit Policy) |

### Claims att INTE sätta i Turn 4

- **Email** — kräver join mot `AppIdentityDbContext` per request; ADR 0017: session-store är primary lookup. Defer till Fas 1.
- **AuthProvider** — irrelevant för auktorisation i Turn 4. Local är enda provider. Läggs till när OAuth aktiveras.
- **Roles** — inga roller finns ännu.
- **Jti** — finns på `CurrentUser.cs:26` för revocation. Sessioner använder inte JTI — markera `[Obsolete]` parallellt med JWT-klasserna eller ta bort i Fas 1.

**CurrentUser.cs** lämnas oförändrad i Turn 4 (den fungerar för Sub-claim). Jti rensas i Fas 1 tillsammans med JWT-radering.

---

## 4. [Obsolete]-pattern

### Exakt attribut-syntax (icke-brytande, med DiagnosticId)

```csharp
[Obsolete(
    "JWT-issuance ersätts av ISessionStore i Fas 0 STEG 4b (ADR 0017). " +
    "Klassen bevaras tillfälligt för bakåtkompatibilitet under Turn 4–5. " +
    "Raderas i Fas 1.",
    error: false,
    DiagnosticId = "JOBBPILOT0001",
    UrlFormat = "https://github.com/klasolsson81/jobbpilot/blob/main/docs/decisions/0017-frontend-auth-pattern.md")]
```

### Klasser att markera

| Fil | Markering |
|---|---|
| `JwtTokenGenerator.cs` | `[Obsolete]` på klass |
| `IJwtTokenGenerator` (interface) | `[Obsolete]` på interface |
| `RedisAccessTokenRevocationStore.cs` | `[Obsolete]` på klass |
| `IAccessTokenRevocationStore` | `[Obsolete]` på interface |
| `JwtSettings` (Infrastructure + Application) | `[Obsolete]` på klass |
| `RefreshToken` (entity) | **INTE ännu** — defer tills refresh-endpoint raderas |
| `RefreshTokenStore` + `IRefreshTokenStore` | **INTE ännu** — samma |

**Varför `error: false`:** Bygget ska gå igenom. `error: true` skulle bryta `Program.cs`, `LoginCommandHandler`, `LogoutCommandHandler` som fortfarande använder gamla klasser — de raderas inte i Turn 4.

**Varför DiagnosticId:** Tillåter selektiv `#pragma warning disable JOBBPILOT0001` i filer som medvetet fortsätter använda gamla API:t. Filtrerbar i CI-output.

**Notera:** `IAccessTokenRevocationStore`-`[Obsolete]` i `LogoutCommandHandler.cs:11` är **önskat** — det är en TODO-flagga för Turn 5/6 logout-refaktor. Ingen `#pragma`-suppression där.

---

## 5. Logout-scope

### Rekommendation: Refaktorera `/auth/logout` i Turn 4 — minimalt

**Riskmatris om defer:**

| Risk | Sannolikhet | Konsekvens |
|---|---|---|
| Sessioner som aldrig invalideras vid logout | Hög (handler träffar revocation-store som inte har effekt längre) | Säkerhets- och GDPR-fel |
| Entwicklare i Fas 1 litar på fungerande logout | Medel | Tech-debt rippling |
| Frontend förlitar sig på `POST /auth/logout` som "definitiv ut" | Hög (ADR 0018 §Logout beskriver detta) | Bryter ADR-kontraktet |

**Minimal refaktor i Turn 4:**
- `LogoutCommandHandler` anropar `ISessionStore.InvalidateAsync(sessionId, ct)` istället för JTI-revocation
- Cookie-radering på `AuthEndpoints.cs:58-63` tas bort (cookie sätts/raderas av Next.js per ADR 0018 §Logout)
- Endpoint returnerar bara `204 No Content`

---

## Sammanfattning av rekommendationer

| Fråga | Rekommendation |
|-------|---------------|
| Handler vs middleware | `SessionAuthenticationHandler : AuthenticationHandler<TOptions>` |
| Scheme-namn | Behåll `"Bearer"` i Turn 4, byt till `"Session"` i Fas 1 |
| Claims | Endast `NameIdentifier` + `Sub` + `session_id_prefix` |
| `[Obsolete]`-syntax | `error: false`, DiagnosticId `JOBBPILOT0001`, UrlFormat → ADR 0017 |
| Logout-scope | Refaktorera i Turn 4 — minimal handler med `InvalidateAsync` |

## Granskade filer

- `src/JobbPilot.Api/Program.cs`
- `src/JobbPilot.Api/Endpoints/AuthEndpoints.cs`
- `src/JobbPilot.Api/Endpoints/MeEndpoints.cs`
- `src/JobbPilot.Application/Common/Abstractions/ISessionStore.cs`
- `src/JobbPilot.Application/Auth/Commands/Login/LoginCommandHandler.cs`
- `src/JobbPilot.Application/Auth/Commands/Logout/LogoutCommandHandler.cs`
- `src/JobbPilot.Infrastructure/Auth/JwtTokenGenerator.cs`
- `src/JobbPilot.Infrastructure/Auth/CurrentUser.cs`
- `src/JobbPilot.Infrastructure/Auth/Sessions/RedisSessionStore.cs`
- `src/JobbPilot.Infrastructure/Auth/Sessions/InMemorySessionStore.cs`
- `docs/decisions/0017-frontend-auth-pattern.md`
- `docs/decisions/0018-cookie-and-csrf-strategy.md`
