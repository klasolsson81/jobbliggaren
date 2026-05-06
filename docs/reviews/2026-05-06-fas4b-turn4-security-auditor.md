# security-auditor — Pre-impl rapport Turn 4

**Datum:** 2026-05-06
**Scope:** Token-extraction, session fixation, logout-säkerhet, CSRF, race conditions
**Auktoritet:** ADR 0017, ADR 0018, GDPR Art. 32, CLAUDE.md §5.4, OWASP ASVS v4 (sessions)

---

## 1. Bearer-token-extraction

### 1.1 Case-insensitive prefix-matching

RFC 6750 §2.1: schemat "Bearer" är case-insensitive. Rekommendation: `StringComparison.OrdinalIgnoreCase` via `AuthenticationHeaderValue.TryParse` (samma mönster som `JwtBearerHandler`).

### 1.2 Malformed header-handling

Alla följande input-klasser ska ge `Fail` eller `NoResult`, aldrig exception:

| Input | Förväntat utfall |
|---|---|
| Header saknas | `NoResult()` |
| `Authorization:` (tom) | `NoResult()` |
| `Authorization: Bearer` (utan space + token) | `Fail("Malformed Authorization header")` |
| `Authorization: Bearer ` (space, ingen token) | `Fail` |
| `Authorization: Basic xxx` (annat schema) | `NoResult()` |
| `Authorization: Bearer foo bar` (space i token) | `Fail` |
| Token > 256 tecken | `Fail` — session-id är 43 tecken (32 bytes base64url) |
| Token med icke-base64url-tecken | `Fail` — validera `^[A-Za-z0-9_-]+$` |

**Konkret pattern:**
```csharp
if (!AuthenticationHeaderValue.TryParse(headerValue, out var auth))
    return AuthenticateResult.NoResult();
if (!"Bearer".Equals(auth.Scheme, StringComparison.OrdinalIgnoreCase))
    return AuthenticateResult.NoResult();
if (string.IsNullOrWhiteSpace(auth.Parameter))
    return AuthenticateResult.Fail("Empty bearer token");
if (auth.Parameter.Length is < 16 or > 256)
    return AuthenticateResult.Fail("Bearer token length out of bounds");
if (!Base64UrlRegex.IsMatch(auth.Parameter))
    return AuthenticateResult.Fail("Bearer token contains invalid characters");
```

### 1.3 Timing-attack-skydd

**Constant-time-jämförelse behövs INTE.** Vår modell: angriparen ger sträng → vi använder den som nyckel → inget byte-för-byte-jämförelsearbete att skydda. SessionId är 256 bits CSPRNG — enumerering är astronomiskt. Redis `GET` är O(1); tidsskillnad miss vs hit drunknar i nätverksjitter.

**Rekommendation:** Dokumentera timing-modellen i XML-doc på `AuthenticationHandler` så nästa-utvecklare inte "fixar" det på fel ställe.

---

## 2. Session fixation

### 2.1 Hotmodell och nuvarande skydd

Klassisk session-fixation skyddas redan: session-id genereras av backend vid login (`CreateAsync`). Ingen anonym session "uppgraderas".

### 2.2 Rekommendation: Val (a) — acceptera multipla parallella sessioner i Fas 0

**Rationale:**
1. Multi-device är legitim default (telefon + laptop ska kunna vara inloggade parallellt)
2. Tvångs-invalidering "alla andra sessioner vid login" är fel beteende — rätt är en "aktiva sessioner"-vy i `/installningar/sakerhet/sessioner` (Fas 1)
3. Sekundärt index krävs för sessioner-UI — leverera dem tillsammans i Fas 1
4. GDPR Art. 17-krav (`InvalidateAllForUserAsync` för account-deletion) är redan flaggat i ADR 0017 Out-of-Scope #1

**Dokumentationsåtgärd:** Uppdatera ADR 0017 Out-of-Scope #1 med: "Active-sessions UI för användare (Fas 1) — secondary-index-konsument".

### 2.3 Kompenserande kontroller (krävs i Turn 4)

- Login-händelser MÅSTE auditloggas: `userId` + `ip` + `userAgent` + `sessionId.ToString()` (prefix-form)
- Failed login-försök ska auditloggas separat (utan att avslöja om e-post finns)

---

## 3. Logout-flöde-säkerhet

### 3.1 Krävd sekvens

```
1. AuthenticationHandler kör (UseAuthentication).
   Extraherar bearer → ISessionStore.GetAsync() → sätter HttpContext.User.
   Ogiltig token → 401 challenge → endpoint körs inte.

2. Endpoint körs bara om autentiserad:
   a. Plocka SessionId från HttpContext.Items (satt av AuthenticationHandler)
      — INTE re-parsas från råa headern
   b. ISessionStore.InvalidateAsync(sessionId, ct) — bool-returvärde ignoreras
   c. 204 No Content
```

### 3.2 Idempotens

Returnera 204 ändå om `InvalidateAsync` returnerar `false` (race — annan enhet loggade ut simultant). Att returnera 404 vore fel UX och en informationsläcka.

### 3.3 Säkerhets-checklista logout

| Krav | Status |
|---|---|
| `.RequireAuthorization()` | ✓ finns redan |
| SessionId från authenticated context, inte re-parsad header | Måste implementeras |
| `InvalidateAsync` idempotent (ignorera bool) | Måste implementeras |
| 204 även vid `InvalidateAsync = false` | Måste implementeras |
| Logout-händelse audit-loggas (userId, sessionPrefix, ip) | **Major — saknas idag** |

---

## 4. CSRF-skydd-verifiering

### 4.1 Trust-modell (dokumentationsgap — Major)

ADR 0018 dokumenterar CSRF-strategin från frontend-perspektiv men beskriver **inte** att backend medvetet förlitar sig på Next.js-proxyn som CSRF-isolation. Backend är inte browser-reachable (ADR 0018 §Architecture: "internal host"), vilket eliminerar cross-origin attack-yta från browser-håll.

**Bearer-token-modellen är immun mot CSRF** (CSRF utnyttjar att browsers automatiskt skickar cookies; bearer-headers skickas inte automatiskt cross-origin).

**Åtgärd Turn 4:** Lägg till "Backend trust-model"-sektion i ADR 0018:
> *Backend förutsätter att klienter är non-browser eller att browser-klienter går via Next.js-proxyn på samma origin. Backend gör ingen CSRF-validering.*

Lägg till arkitektur-kommentar som varnar mot att lägga till `AddCookie()` på backend.

### 4.2 Backend-invarianter att säkerställa

| Invariant | Status |
|---|---|
| Inga `UseCookieAuthentication`-schemes på backend | ✓ Verifierat |
| Inga state-changes på GET | ✓ ser bra ut |
| Backend förlitar sig inte på Origin/Referer för auktorisation | ✓ inte i nuvarande kod |
| CORS med `credentials: true` mot externa origin finns inte | **Verifiera när CORS läggs till** |
| `[Authorize]` på alla state-changing endpoints | ✓ (Login/Register är `AllowAnonymous` av nödvändighet) |

### 4.3 CORS-flagga (Fas 1)

Ingen CORS-konfiguration finns nu (OK i Fas 0). När CORS behövs: explicit allowlist, aldrig `*`, aldrig `AllowCredentials()` utan specifik motivering.

---

## 5. Race condition vid concurrent login

### 5.1 Scenario och utfall

Samma user, två parallella login-requests → Redis får två oberoende keys med olika session-ids → ingen overwrite (olika keys).

### 5.2 Säkerhets-implikationer

**Session-id-prediction:** Nej. `RandomNumberGenerator.GetBytes(32)` (CSPRNG) — parallella anrop ger oberoende 256-bit-värden utan kausal koppling.

**Session-fixation via race:** Nej. Backend genererar alltid — angriparen kan inte injicera ett ID.

**Resource exhaustion (DoS):** Minor. Utan rate-limiting kan angripare med stulna credentials skapa många sessioner och fylla Redis. Rate-limiting krävs i Fas 1 (redan flaggat i ADR 0017 Out-of-Scope).

### 5.3 Slutsats

Concurrent login är säkert i Fas 0. Inga Turn 4-åtgärder behövs för race-scenariot.

---

## Sammanfattning av säkerhetsrekommendationer

| # | Fråga | Rekommendation | Severity |
|---|---|---|---|
| 1.1 | Bearer-prefix-matching | `StringComparison.OrdinalIgnoreCase` via `AuthenticationHeaderValue.TryParse` | Major |
| 1.2 | Malformed header | Pre-validera längd + base64url-charset INNAN Redis-uppslagning | Major |
| 1.3 | Timing-attack | Constant-time-jämförelse ej motiverad — dokumentera modellen i XML-doc | Minor |
| 2 | Session fixation | Val (a): acceptera parallella sessioner. Audit-log på alla login-händelser i Turn 4 | Major (audit-log) |
| 3.2 | Logout-sekvens | Hämta SessionId från `HttpContext`, inte re-parsa header | Major |
| 3.3 | Logout idempotens | 204 även om `InvalidateAsync` returnerar false | Minor |
| 3.4 | Logout audit-log | Strukturerad logg `userId` + `sessionPrefix` + `ip` i `LogoutCommandHandler` | Major |
| 4.1 | CSRF trust-model | Dokumentera backend-isolering i ADR 0018 | Major (dokumentation) |
| 4.7 | CORS | Ingen Turn 4-åtgärd. Flagga: explicit allowlist när CORS läggs till | Blocker om felkonfigurerat |
| 5 | Concurrent login | Ingen Turn 4-åtgärd — CSPRNG eliminerar prediction-vektorn | n/a |

## Granskade filer

- `docs/decisions/0017-frontend-auth-pattern.md`
- `docs/decisions/0018-cookie-and-csrf-strategy.md`
- `src/JobbPilot.Api/Program.cs`
- `src/JobbPilot.Api/Endpoints/AuthEndpoints.cs`
- `src/JobbPilot.Api/Endpoints/MeEndpoints.cs`
- `src/JobbPilot.Application/Common/Abstractions/ISessionStore.cs`
- `src/JobbPilot.Infrastructure/Auth/Sessions/InMemorySessionStore.cs`
- `src/JobbPilot.Infrastructure/Auth/Sessions/RedisSessionStore.cs`
- `src/JobbPilot.Infrastructure/Auth/Sessions/SessionStoreOptions.cs`
