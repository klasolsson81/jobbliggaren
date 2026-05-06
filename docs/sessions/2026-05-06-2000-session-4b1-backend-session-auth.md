---
session: "4b.1"
datum: 2026-05-06
slug: session-4b1-backend-session-auth
status: KOMPLETT
steg: "4b Turn 1–4 — Backend session-auth (ISessionStore + SessionAuthenticationHandler + IAuthAuditLogger)"
commits: |
  06e7b4e docs(adr): accept ADR 0017 frontend-auth-pattern
  2d3caf3 docs(adr): accept ADR 0018 cookie-and-csrf-strategy
  f321153 feat(auth): add AuthProvider enum and User entity migration
  be67183 chore(ci): suppress gitleaks false positive in AuthProviderDefaultsTests
  3e9d2e6 feat(auth): add ISessionStore with InMemory and Redis implementations
  292fb3f feat(auth): refactor to stateful session-based authentication
  e21f887 chore(ci): suppress gitleaks false positives in AuthTestHelpers + BearerTokenValidationTests
---

# Session 4b.1 — STEG 4b Turn 1–4: Backend session-auth

## Mål

Ersätta JWT-utgivning med stateful Redis-sessioner. Fas 0-backend-milstolpen klar:
register + login + logout via `ISessionStore` med Redis-backend.

Detaljplan i STOPP-format per turn:
- Turn 1: ADR 0017–0018 godkända
- Turn 2: AuthProvider-enum + User-migration
- Turn 3: ISessionStore + InMemory/Redis-implementationer
- Turn 4: SessionAuthenticationHandler + IAuthAuditLogger + tester + ADR-uppdateringar

---

## Genomfört per turn

### Turn 1 — ADR 0017 + 0018

ADR 0017 (frontend-auth-pattern) och ADR 0018 (cookie-och-csrf-strategi) skrivna och
godkända av Klas. Beslutar:
- HTTP-only `__Host-jobbpilot_session`-cookie (SameSite=Strict)
- Stateful Redis-sessioner, ingen JWT i browser
- Next.js Route Handler som proxy — backend cookie-agnostisk
- `SessionId` som `readonly record struct` med `ToString()` → 6-char prefix + `…`
- SHA-256-hashade Redis-nycklar (raw session-id aldrig i Redis-dump)

### Turn 2 — AuthProvider + migration

`AuthProvider` enum (`Local | Google | LinkedIn | Facebook`) och unik constraint
`(Provider, ProviderUserId)` på `User`-entiteten. Migration: `AddAuthProviderColumns`.
OAuth-readiness utan OAuth-implementation — stub `(auth)/oauth/[provider]/callback/route.ts`
reserverat för Fas 1.

Gitleaks-falskt positivt för `AuthProviderDefaultsTests` — supprimerat i `.gitleaksignore`.

### Turn 3 — ISessionStore + implementationer

`ISessionStore`-interface definierat i Application-lagret:
- `CreateAsync(userId)` → `Session`
- `GetAsync(sessionId)` → `Session?`  
- `InvalidateAsync(sessionId)` → `bool`
- `InvalidateAllForUserAsync(userId)` → SCAN-baserad fallback (sekundärindex Fas 1)

Två implementationer:
- `InMemorySessionStore` — för tester utan Redis
- `RedisSessionStore` — SHA-256(session-id) som nyckel, 14 dagars TTL (sliding)

Performancetest: p50 1,88 ms · p99 2,42 ms mot Docker Redis. CI-guard: p99 < 50 ms
(10× budget för Windows/Testcontainers-overhead).

### Turn 4 — SessionAuthenticationHandler + IAuthAuditLogger + review-fixes

**SessionAuthenticationHandler** (RFC 6750): ersätter JWT-validering med Redis-session-lookup.
Inte middleware — `AuthenticationHandler<SessionAuthenticationSchemeOptions>` per ASP.NET
Core-konvention. Constants för min/max session-id-längd (16–256 chars).

**IAuthAuditLogger** med [LoggerMessage]-källgenerator-pattern (zero-allocation):
- EventId 1001 `LoginSucceeded` — Information
- EventId 1002 `LoginFailed` — Warning (SHA-256(email), aldrig råe-post)
- EventId 1003 `LogoutSucceeded` — Information

**`/auth/refresh` → 410 Gone:** RefreshCommandHandler bevarad under `[Obsolete]`
(`DiagnosticId = "JOBBPILOT0001"`) men helt unwired. Endpoint returnerar 410 direkt.

**Code-reviewer (post-impl):** 3 Major + 5 Minor funna, alla fixade i samma session:
- M1: `SessionAuthenticationHandler` döpt om från felaktigt `SessionMiddleware`
- M2: `IAuthAuditLogger.ip`/`userAgent`-parametrar borttagna från interface (läses internt
  via `IHttpContextAccessor` i infrastruktur-implementationen)
- M3: ADR 0018 `### Backend trust model`-sektion tillagd verbatim (paste-pattern-bug:
  innehållet var godkänt i STOPP-rapport men aldrig skrivet till fil)
- m1: `BearerTokenValidationTests` utökad med tom header + "Bearer foo bar" InlineData
- m2: `LogoutTests` (ny fil) med 3 tester
- m3: `LogoutCommandHandler` dubbelkoll-konsolidering
- m4: `SessionAuthenticationHandler` min/max-constants

**ADR-keeper round 1 + 2:** Godkänd efter att 4 sektioner i ADR 0017 lagts till verbatim
(paste-pattern-bug instans 2):
- `## Auth Audit Logging` med EventId-tabell
- Uppdaterad `## Out of Scope` (Active-sessions UI)
- `## Deprecated Endpoints` med `### POST /auth/refresh — 410 Gone`
- `## Performance Budget` med mätdata

**Testresultat:** 153 tester gröna (21 domain, 46 application, 6 arch, 80 integration).
Upp från 75 tester i STEG 3.

---

## Beslut och avvikelser

**paste-pattern-bug identifierad:** CC beskriver innehåll verbalt i STOPP-rapport men skriver
aldrig till fil. Tre instanser under sessionen. Kräver CLAUDE.md-uppdatering som open
follow-up (ej blockande för 4b.2).

**LogoutTests tredje test:** Ursprungligen planerat som `idempotent_204_204`. Korrekt beteende
är att andra sequential logout-anrop returnerar 401 (session redan borta i Redis → AuthHandler
blockerar) — döpt om till `_with_already_invalidated_session_returns_401`.

**Gitleaks (push):** 4 nya falskt positiva för `T3stlosen123456` i `AuthTestHelpers.cs`
(rader 15, 39) och `BearerTokenValidationTests.cs` (rader 110, 130). Supprimerade i
`.gitleaksignore` som separat `chore(ci)`-commit.

---

## Open follow-ups

| # | Beskrivning | Ursprung |
|---|-------------|----------|
| m5 | Höj warmup-iterationer i RedisSessionStoreTests till 32 om test flakar | code-reviewer Minor |
| m7 | Reflection mot ApiFactory i SessionStoreUnavailableTests — städ-PR Fas 0.x | code-reviewer Minor |
| m8 | AuthAuditLoggerTests fel projekt (Application.UnitTests testar Infrastructure) | code-reviewer Minor |
| m9 | HashEmail-duplikation i LoginCommandHandler — löses Fas 1 JWT-radering | code-reviewer Minor |
| NB-1/2/3 | ADR 0017+0018 format/språk-divergens (English vs Swedish metadata) | adr-keeper |
| paste-pattern | CC beskriver men skriver aldrig — CLAUDE.md-disciplin-gap | P3+P4 pattern |

---

## Commits

| Hash | Meddelande |
|------|------------|
| 06e7b4e | docs(adr): accept ADR 0017 frontend-auth-pattern |
| 2d3caf3 | docs(adr): accept ADR 0018 cookie-and-csrf-strategy |
| f321153 | feat(auth): add AuthProvider enum and User entity migration |
| be67183 | chore(ci): suppress gitleaks false positive in AuthProviderDefaultsTests |
| 3e9d2e6 | feat(auth): add ISessionStore with InMemory and Redis implementations |
| 292fb3f | feat(auth): refactor to stateful session-based authentication |
| e21f887 | chore(ci): suppress gitleaks false positives in AuthTestHelpers + BearerTokenValidationTests |

---

## Nästa session (4b.2 — frontend auth)

1. Verifiera HEAD = `e21f887` och `dotnet test` = 153 gröna
2. Starta frontend: `cd web/jobbpilot-web && pnpm dev`
3. Implementera Next.js Route Handler proxy → .NET backend
4. Sätt `__Host-jobbpilot_session`-cookie per ADR 0018
5. Bygg `lib/auth/session.ts` + SessionProvider + `useSession()`
6. Bygg login/register/me-sidor (civic design, inga emoji, ingen utropstecken)
