# code-reviewer — Post-impl rapport Turn 4

**Datum:** 2026-05-06
**Status:** GO efter Phase 3.1-fixar
**Scope:** Backend — Application + Infrastructure + Api + tests för session-baserad auth (Phase 2 + P2.1)
**Auktoritet:** CLAUDE.md §2.1 / §2.3 / §2.4 / §5, ADR 0017, ADR 0018, advisor-rapporter Turn 4

---

## Executive summary

Implementationen följer de låsta designvalen i hög grad: AuthenticationHandler-mönstret är rent, `[Obsolete]`-pattern är konsekvent med `error: false` + DiagnosticId `JOBBPILOT0001`, claims-design matchar dotnet-architects spec, och `LogoutCommandHandler` läser `SessionId` från `ICurrentUser` istället för att re-parsa headern. Inga Critical-fynd. Tre Major-fynd och nio Minor-anmärkningar ska adresseras i Phase 3.1 eller markeras som follow-ups innan commit.

---

## Findings-sammanfattning

- Critical: 0
- Major: 3
- Minor: 9
- Total: 12

---

## Critical findings

Inga.

---

## Major findings

### M1 — Saknad integration-test för `/api/v1/auth/logout`

**Kategori:** Testing
**Fil:** `tests/JobbPilot.Api.IntegrationTests/Auth/` (saknar `LogoutTests.cs`)
**Beskrivning:** test-writer §4 rekommenderade explicit tre nya tester i Turn 4: `POST_logout_without_session_returns_401`, `POST_logout_with_valid_session_returns_204`, och `GET_me_with_invalidated_session_returns_401`. Ingen av dessa finns. Ingen test verifierar att `RequireAuthorization()` på logout avvisar oautentiserade kall, och ingen test verifierar att `InvalidateAsync` körs end-to-end vid lyckad logout.
**CLAUDE.md-referens:** §2.4 — "Integration-test för varje ny endpoint"
**Rekommenderad åtgärd:** Skapa `tests/JobbPilot.Api.IntegrationTests/Auth/LogoutTests.cs` med tre fakta:
1. `POST_logout_without_authorization_returns_401`
2. `POST_logout_with_valid_session_returns_204_and_invalidates_session` (registrera → logout → `GET /me` → 401)
3. `POST_logout_called_twice_is_idempotent_returns_204_204`

### M2 — `BearerTokenValidationTests` täcker 6 av 8 input-klasser från security-auditor §1.2

**Kategori:** Testing / Security
**Fil:** `tests/JobbPilot.Api.IntegrationTests/Auth/BearerTokenValidationTests.cs`
**Beskrivning:** Två input-klasser från security-auditor §1.2 är inte explicit testade:
- `Authorization:` (header-värdet tomt) → ska ge `NoResult` → 401
- `Authorization: Bearer foo bar` (space INNE I tokenet) → ska ge `Fail` → 401
**Rekommenderad åtgärd:** Lägg till två rader i `[Theory]`-fallet:
```csharp
[InlineData("", "Tom Authorization-header")]
[InlineData("Bearer foo bar", "Token med space inuti")]
```

### M3 — ADR 0018 saknar "Backend trust-model"-sektion

**Kategori:** Documentation
**Fil:** `docs/decisions/0018-cookie-and-csrf-strategy.md`
**Beskrivning:** Security-auditor §4.1 markerade detta som Major: ADR 0018 dokumenterar CSRF-strategin från frontend-perspektiv men beskriver inte att backend medvetet förlitar sig på Next.js-proxyn som CSRF-isolation. Arkitektur-varningskommentar finns i `Program.cs` men bör backas upp av text i ADR:n.
**Rekommenderad åtgärd:** Lägg till sektion under `## Decision`:
> **Backend trust-model.** Backend förutsätter att klienter är non-browser eller att browser-klienter går via Next.js-proxyn på samma origin. Backend gör ingen CSRF-validering, ingen Origin/Referer-kontroll, och sätter inga cookies. Bearer-token-modellen är immun mot CSRF: bearer-headers skickas inte automatiskt cross-origin av browsers. Backend får INTE i framtiden registrera `AddCookie()`-scheme — det bryter trust-modellen.

---

## Minor findings

### m1 — `LogoutCommandHandler` har dubbel `SessionId`-check

**Fil:** `src/JobbPilot.Application/Auth/Commands/Logout/LogoutCommandHandler.cs`
**Beskrivning:** Handler kontrollerar `currentUser.SessionId is { } sessionId` i två separata if-block med olika variabelnamn. Konsolidera till ett block.

### m2 — `IAuthAuditLogger` har döda `ip`/`userAgent`-parametrar

**Fil:** `src/JobbPilot.Application/Common/Abstractions/IAuthAuditLogger.cs` + `src/JobbPilot.Infrastructure/Auth/Auditing/AuthAuditLogger.cs`
**Beskrivning:** Interface tar `ip` och `userAgent` som parametrar, men implementationen ignorerar dem och läser från `IHttpContextAccessor`. Alla call-sites skickar `string.Empty`. Antingen ta bort parametrarna från interfacet (rekommenderat — Application-lagret behöver inte känna till IP/UserAgent), eller låt implementationen prefera parametrarna framför HttpContext.

### m3 — `JwtRegisteredClaimNames.Sub` i session-handler utan TODO-kommentar

**Fil:** `src/JobbPilot.Infrastructure/Auth/SessionAuthenticationHandler.cs`
**Beskrivning:** Användning av JWT-konstant i en icke-JWT-session är medveten övergångslösning (bakåtkompatibilitet med `CurrentUser.cs` Sub-OR-NameIdentifier-läsning) men bör flaggas explicit:
```csharp
// TODO Fas 1: byt JwtRegisteredClaimNames.Sub till NameIdentifier
//   när JWT-klasser raderas. Behålls nu för bakåtkompatibilitet med CurrentUser.cs.
```

### m4 — `RefreshCommandHandler` är dead code utan kommentar

**Fil:** `src/JobbPilot.Application/Auth/Commands/Refresh/RefreshCommandHandler.cs`
**Beskrivning:** Endpoint `/auth/refresh` returnerar 410 Gone direkt och anropar aldrig handlern. Handler bevaras med tester men är inte wired. Lägg till en kommentar som förklarar detta är avsiktligt.

### m5 — Performance-test: 10 warmup-iterationer är i gränsen

**Fil:** `tests/JobbPilot.Api.IntegrationTests/Sessions/RedisSessionStoreTests.cs`
**Beskrivning:** Test-writer rekommenderade 10+ warmup. 10 är precis på gränsen. OK för nu givet att p99-budget är generös (50 ms). Om testet börjar flaka, höj till 32.

### m6 — Magic numbers `16` och `256` i bearer-token-validering

**Fil:** `src/JobbPilot.Infrastructure/Auth/SessionAuthenticationHandler.cs`
**Beskrivning:** `auth.Parameter.Length is < 16 or > 256` är magic numbers.
**CLAUDE.md-referens:** §5.1 — "Magic strings — alltid konstanter eller enums"
**Rekommenderad åtgärd:**
```csharp
private const int MinSessionIdLength = 16;
private const int MaxSessionIdLength = 256;
```

### m7 — `BrokenSessionStoreFactory` använder reflection mot `ApiFactory`

**Fil:** `tests/JobbPilot.Api.IntegrationTests/Sessions/SessionStoreUnavailableTests.cs`
**Beskrivning:** Reflection mot `ApiFactory.GetType().GetMethod("ConfigureWebHost", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(...)` är fragilt vid rename/signaturändring. Städ-PR i Fas 0.x — lyft till delad helper eller `protected internal`.

### m8 — `AuthAuditLoggerTests` är i Application.UnitTests men testar Infrastructure-klass

**Fil:** `tests/JobbPilot.Application.UnitTests/Auth/AuthAuditLoggerTests.cs`
**Beskrivning:** Filen testar `JobbPilot.Infrastructure.Auth.Auditing.AuthAuditLogger` men ligger i `Application.UnitTests`. Infrastructure-implementationer bör testas i Infrastructure-testprojekt.
**CLAUDE.md-referens:** §2.4 "Test-isolering"

### m9 — `HashEmail` i `LoginCommandHandler` duplicerar hash-logik

**Fil:** `src/JobbPilot.Application/Auth/Commands/Login/LoginCommandHandler.cs`
**Beskrivning:** Privat `HashEmail`-helper duplicerar SHA-256-hashning som finns i `JwtTokenGenerator`. Extrahera till domain-utility eller flagga som TODO för Fas 1-städning.

---

## Bra gjort (beröm)

- **AuthenticationHandler-design är ren** — korrekt subklass av `AuthenticationHandler<TOptions>`, 6 av 8 input-klasser hanteras korrekt, `SessionStoreUnavailableException` bubblar okontrollerat.
- **Claims-design exakt enligt spec** — `NameIdentifier` + `Sub` + `session_id_prefix` med korrekt prefix-form från `SessionId.ToString()`.
- **`ICurrentUser.SessionId`** läses från `HttpContext.Items["SessionId"]` — ingen re-parsning av Authorization-header.
- **`LogoutCommandHandler` är minimal och clean** — `SessionId` från `ICurrentUser`, bool-returvärde från `InvalidateAsync` ignorerat (idempotent), inga JWT-imports kvar.
- **`[Obsolete]`-pattern konsekvent** — alla 6 förväntade klasser/interfaces markerade med `error: false` + DiagnosticId `JOBBPILOT0001` + UrlFormat → ADR 0017. `RefreshToken`-entity och `IRefreshTokenStore` korrekt INTE markerade.
- **AuthAuditLogger använder LoggerMessage-pattern** — allokeringsfri strukturerad logging. Raw email loggas aldrig — test verifierar detta explicit via property-bag-sniff.
- **DI-livstid för `IAuthAuditLogger` är `Scoped`** — korrekt eftersom `IHttpContextAccessor` är per-request.
- **Architecture-comment i `Program.cs`** varnar mot framtida `AddCookie()` per security-auditor §4.1.
- **SHA-256-hash på Redis-nyckel** — raw session-id syns aldrig i Redis-dump.
- **`SessionId` value object** med `Reveal()` + prefix-`ToString()` implementerat korrekt.
- **AuthTestHelpers används konsekvent** i alla berörda testklasser. Ingen `RegisterAndGetToken`-rest finns kvar.
- **CancellationToken** propageras genom alla async-anrop, inga `.Result`/`.Wait()`, inget direkt `DateTime.UtcNow` i `src/`.
- **410 Gone på `/auth/refresh`** läcker inga interna detaljer.

---

## Reviewer-omdöme

**GO efter Phase 3.1-fixar.**

M1 (saknad LogoutTests) är en testtäckningslucka som ska tätas i samma session. M2 (två missing input-klasser i BearerTokenValidationTests) är ~10 raders patch. M3 (ADR 0018 trust-model-sektion) är dokumentationsåtgärd.

Minor-fynden kan adresseras i Phase 3.1 (m1, m3, m6 rekommenderas starkt) eller markeras som follow-ups i backlogen. Re-review behövs inte — punktåtgärd på M1+M2+M3 räcker. Klas kan ge GO för commit när dessa tre är åtgärdade.
