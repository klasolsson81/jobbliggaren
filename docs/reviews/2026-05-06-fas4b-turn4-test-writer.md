# test-writer — Pre-impl rapport Turn 4
**Datum:** 2026-05-06
**Scope:** Test-impact-kvantifiering, helper-strategi, fixture-hantering inför refaktor av auth-pipeline från JWT-issuance till stateful Redis-sessioner via `ISessionStore`.

> **Notering om låsta designval:** Backend cookie-agnostiskt; raw session-id i response body. `ISessionStore` är primary; `JwtTokenGenerator` blir `[Obsolete]` men raderas INTE i Turn 4. `SessionId` value-object med SHA-256-hashade Redis-nycklar. 14d sliding TTL via `IDistributedCache.RefreshAsync`. Rapporten antar dessa premisser utan att omdebattera dem.

---

## Grep-resultat (kvantifiering)

- **Test-filer berörda:** 11 (5 unit + 5 integration + 1 architecture)
- **Test-metoder berörda:** ca 28 (alla auth-relaterade handlers + integration-flöden som loggar in)
- **Fixtures berörda:** 1 primär (`ApiFactory.cs`) + 1 helper (`MeTests.RegisterAndGetToken`)

### Detaljer (konkreta fynd)

**Ingen delad LoginAsync-helper finns.** Grep efter `LoginAsync`, `RegisterAsync`, `GetTokenAsync`, `AuthenticateAsync` returnerar inga träffar. Den enda login-helpern är privat i `MeTests`:

```
tests/JobbPilot.Api.IntegrationTests/Me/MeTests.cs:15
    private async Task<string> RegisterAndGetToken(CancellationToken ct)
```

Övriga integration-tester duplicerar register-call-flödet inline (`LoginTests`, `RegisterTests`, `RefreshReplayTests`).

**JWT-fixtures i integration-test-projektet:**

| Fil | Rad | Fynd |
|-----|-----|------|
| `tests/JobbPilot.Api.IntegrationTests/Infrastructure/ApiFactory.cs` | 29-34 | RSA-keypair genereras i constructor, skrivs till temp-PEM-filer |
| `tests/JobbPilot.Api.IntegrationTests/Infrastructure/ApiFactory.cs` | 80-84 | `Jwt__PrivateKeyPath` / `Jwt__PublicKeyPath` env-vars sätts före `Services` accessas |
| `tests/JobbPilot.Api.IntegrationTests/Infrastructure/ApiFactory.cs` | 93-97 | Cleanup raderar PEM-filer + nullar env-vars |

**`accessToken`-extraction i integration-tester:**

| Fil | Rad | Användning |
|-----|-----|------------|
| `tests/JobbPilot.Api.IntegrationTests/Auth/RegisterTests.cs` | 15, 29 | Test-name + assertion på `accessToken` i body |
| `tests/JobbPilot.Api.IntegrationTests/Auth/LoginTests.cs` | 15, 29 | Test-name + assertion på `accessToken` i body |
| `tests/JobbPilot.Api.IntegrationTests/Me/MeTests.cs` | 21, 39, 53 | Extraherar `accessToken`, sätter `Authorization: Bearer <token>` |

**JWT-beroenden i unit-tester:**

| Fil | Mock-typ | Antal Sub-skapanden |
|-----|----------|---------------------|
| `LoginCommandHandlerTests.cs` | `IJwtTokenGenerator`, `IRefreshTokenStore` | 4 metoder, 4 test |
| `RegisterCommandHandlerTests.cs` | `IJwtTokenGenerator`, `IRefreshTokenStore` | 4 metoder, 4 test |
| `RefreshCommandHandlerTests.cs` | `IJwtTokenGenerator`, `IRefreshTokenStore` | 7 metoder, 7 test |
| `LogoutCommandHandlerTests.cs` | `JwtSettings`, `IAccessTokenRevocationStore` | 2 metoder, 2 test |
| `GetCurrentUserQueryHandlerTests.cs` | (ingen JWT-direkt, använder `ICurrentUser`) | 2 test — påverkas indirekt |

**Architecture-test:**

```
tests/JobbPilot.Architecture.Tests/DomainLayerTests.cs:81
    "Microsoft.AspNetCore.Authentication.JwtBearer"
```
Verifierar att Domain inte beror på JwtBearer — denna test förblir oförändrad och fortsätter skydda Clean Arch-gränsen.

**RefreshReplayTests.cs** (Turn 3) hanterar redan cookies manuellt med `HandleCookies = false` och extraherar `jobbpilot-refresh`-cookie — denna test är tätt kopplad till nuvarande refresh-cookie-flöde och kommer behöva förändringar när refresh-flödet ersätts av session-rotation.

---

## 1. LoginAsync-helper-strategi

**Rekommendation: Introducera ny helper `LoginWithSessionAsync` i en delad infrastructure-fil och behåll INTE den privata `RegisterAndGetToken`.**

Eftersom det idag inte finns någon delad `LoginAsync`-helper är detta en chans att etablera *en* kanonisk auth-helper för integration-tester istället för att fortsätta duplicera inline. Skapa:

```
tests/JobbPilot.Api.IntegrationTests/Infrastructure/AuthTestHelpers.cs
```

med signaturer typ:

```csharp
public static async Task<string> RegisterAndGetSessionIdAsync(
    this HttpClient client, string? email = null, CancellationToken ct = default);

public static async Task<string> LoginAndGetSessionIdAsync(
    this HttpClient client, string email, string password, CancellationToken ct = default);
```

### Trade-offs

| Alternativ | Pro | Con |
|------------|-----|-----|
| **A. Behåll signatur, byt impl** (`RegisterAndGetToken` returnerar session-id) | Minimalt diff, lokal förändring | Namnet ljuger — "Token" är kvar i namnet trots att vi inte längre returnerar JWT. Lurar nästa läsare. Helpern är dessutom privat i MeTests — andra test-klasser fortsätter duplicera. |
| **B. Ny helper `LoginWithSessionAsync` i delad fil** *(rekommenderas)* | Tydligt namn signalerar session-baserad auth; centraliserar duplicerad logik från LoginTests/RegisterTests/MeTests; gör det enkelt att Turn 5 (när JWT raderas) — bara helpern behöver justeras igen | Större diff i Turn 4 (3 testklasser uppdateras), men diffen är mekanisk |
| **C. Två helpers parallellt** (`RegisterAndGetToken` deprecated + ny `LoginWithSessionAsync`) | Ingen | Dubbelt kodflöde; deprecated-helper kommer ändå raderas i Turn 5 |

**Val:** B. Det är det enda alternativet som inte införs förvirrad terminologi i testkod. JobbPilots civic-utility-standard tål inte att namnet säger "Token" när värdet är ett session-id.

---

## 2. JWT-test-fixtures

**Rekommendation: Behåll JWT-fixtures (RSA-PEM-generering + env-var-injection) i `ApiFactory.cs` i Turn 4.**

Motivering:
- `JwtTokenGenerator` markeras `[Obsolete]` men raderas inte i Turn 4 — DI-container i `Program.cs` registrerar fortfarande tjänsten, vilket innebär att `Jwt__PrivateKeyPath` / `Jwt__PublicKeyPath` fortfarande läses vid service-registration. Tar vi bort PEM-genereringen kraschar host-creation.
- I Fas 1 när JWT raderas helt är detta en separat städ-PR (uppdatera `ApiFactory` + ta bort `JwtSettings` från `LogoutCommandHandlerTests`).
- Architecture-testet i `DomainLayerTests.cs:81` hindrar Domain från att läcka mot `JwtBearer` — den förblir grön under hela övergången och behöver inte röras.

**Konkret i Turn 4:**
- `ApiFactory.cs` rad 17-18: behåll både `_postgres` och `_redis` containrar (Redis behövs nu både för IDistributedCache *och* sessions; samma container räcker).
- `ApiFactory.cs` rad 29-34, 80-84, 93-97: behåll JWT-keypair-blocket oförändrat.
- Lägg till — om `SessionStoreOptions` kräver explicit konfiguration utöver default — en services-replacement för testbara TTL-värden i `ConfigureWebHost`.

---

## 3. Exakta ändringar per testklass

| Klass | Ändringar |
|-------|-----------|
| `tests/JobbPilot.Api.IntegrationTests/Auth/LoginTests.cs` | (a) Byt namn på test `POST_login_with_valid_credentials_returns_access_token` → `..._returns_session_id`. (b) Byt assert rad 29 från `json.GetProperty("accessToken")` till `json.GetProperty("sessionId")` (eller vilken property body slutgiltigt får — bekräfta med dotnet-architect). (c) Wrong-password-testet är oförändrat (bara 401-status). |
| `tests/JobbPilot.Api.IntegrationTests/Auth/RegisterTests.cs` | (a) Byt namn `POST_register_with_valid_data_returns_access_token` → `..._returns_session_id`. (b) Byt assert rad 29 från `accessToken` → `sessionId`. (c) duplicate-email + blank-display-name oförändrade. |
| `tests/JobbPilot.Api.IntegrationTests/Me/MeTests.cs` | (a) Radera `RegisterAndGetToken` (rad 15-22). (b) Byt rad 38, 52 till `await _client.RegisterAndGetSessionIdAsync(ct)`. (c) Byt rad 39, 53 från `new AuthenticationHeaderValue("Bearer", token)` till lämplig session-header. **Konkret form måste avgöras av dotnet-architect** — sannolikt en custom request header (`X-Session-Id: <raw>`) eftersom backend är cookie-agnostiskt. Tills designvalet är låst, blockerar denna ändring. |
| `tests/JobbPilot.Api.IntegrationTests/Auth/RefreshReplayTests.cs` | **Hela testet behöver omtanke.** Refresh-token-rotation ersätts av session-sliding-TTL. Antingen (a) radera testet eftersom token-rotation/replay-protection inte längre är ett scenario, eller (b) omformulera som "session-invalidate-revokes-access" mot `ISessionStore.InvalidateAsync`. **Detta är ett designval för dotnet-architect — inte test-writer.** I Turn 4 rekommenderas att markera testet `[Fact(Skip = "Pending Turn 5: refresh-flöde ersätts av sessions")]` snarare än att skriva om det halvdant. |
| `tests/JobbPilot.Api.IntegrationTests/Auth/AuthProviderDefaultsTests.cs` | Ingen ändring. Verifierar bara `User.Provider` i DB efter register, oavhängig av token-typ i response. |
| `tests/JobbPilot.Api.IntegrationTests/Infrastructure/ApiFactory.cs` | (a) Behåll JWT-keypair-block. (b) Eventuellt: registrera `SessionStoreOptions` med kort TTL för deterministiska tester. (c) Lägg ev. till services-replacement om `ISessionStore` behöver injicera `FakeDateTimeProvider`. |
| `tests/JobbPilot.Api.IntegrationTests/Infrastructure/AuthTestHelpers.cs` *(NY FIL)* | Skapa `LoginAndGetSessionIdAsync` + `RegisterAndGetSessionIdAsync` som extension-methods på `HttpClient`. |
| `tests/JobbPilot.Application.UnitTests/Auth/LoginCommandHandlerTests.cs` | **Hela klassen ska skrivas om.** `LoginCommandHandler` ska injicera `ISessionStore` istället för `IJwtTokenGenerator` + `IRefreshTokenStore`. Tester verifierar: (a) `ISessionStore.CreateAsync(userId, ct)` anropas, (b) returnerad `Result.Value` innehåller raw session-id, (c) `IUserAccountService.ValidateCredentialsAsync` styr success/failure-grenar. **Behåll en deprecation-test som verifierar att `IJwtTokenGenerator` ej längre konsumeras** (frivilligt). |
| `tests/JobbPilot.Application.UnitTests/Auth/RegisterCommandHandlerTests.cs` | **Skrivs om analogt med LoginCommandHandler.** `ISessionStore.CreateAsync` ersätter `IJwtTokenGenerator.GenerateTokens` + `IRefreshTokenStore.StoreAsync`. JobSeeker-creation-testet är oförändrat. Compensating-delete-testet (rad 102-122) är oförändrat. |
| `tests/JobbPilot.Application.UnitTests/Auth/RefreshCommandHandlerTests.cs` | **Hela klassen blir antingen raderad eller markerad obsolete.** Refresh är inte en operation i session-modellen — sliding TTL hanteras transparent av `ISessionStore.GetAsync`. Markera testklassen `[Obsolete]` parallellt med `RefreshCommandHandler` så Roslyn-varning visas, eller flytta till `Auth/Deprecated/`-mapp. **Beslut: dotnet-architect.** |
| `tests/JobbPilot.Application.UnitTests/Auth/LogoutCommandHandlerTests.cs` | **Skrivs om.** `LogoutCommandHandler` ska injicera `ISessionStore` istället för `IRefreshTokenStore` + `IAccessTokenRevocationStore` + `JwtSettings`. Test verifierar `ISessionStore.InvalidateAsync(currentSessionId, ct)`. Skip-testet (`Handle_WhenJtiIsNull...`) blir irrelevant — ersätts med `Handle_WhenSessionIdMissing_ReturnsSuccess` (idempotent logout). |
| `tests/JobbPilot.Application.UnitTests/Auth/GetCurrentUserQueryHandlerTests.cs` | Sannolikt oförändrad om `ICurrentUser` fortsätter exponera `UserId` + `Email`. Om `ICurrentUser`-impl byter från JWT-claims-läsning till session-lookup ändras inte handler-testet — bara `ICurrentUser`-impl, vilket är Infrastructure och inte täcks av denna testklass. |
| `tests/JobbPilot.Architecture.Tests/DomainLayerTests.cs` | Ingen ändring. Domain-läckage-testet förblir grön under övergången. |

---

## 4. Nya [Authorize]-tester

### Vad finns idag

JobbPilot använder **minimal-API endpoints med `RequireAuthorization()`**, inte controller-`[Authorize]`. Skyddade routes (rad-referenser):

- `MeEndpoints.cs:18` — `GET /api/v1/me/`
- `MeEndpoints.cs:24` — `GET /api/v1/me/profile`
- `MeEndpoints.cs:33` — `PATCH /api/v1/me/profile`
- `AuthEndpoints.cs:65` — `POST /api/v1/auth/logout`

**Befintlig täckning:**
- `MeTests.GET_me_without_token_returns_401` — verifierar 401 utan token (rad 24-32) ✓
- `MeTests.GET_me_with_valid_token_returns_user_info` — verifierar 200 med token (rad 34-46) ✓
- `MeTests.GET_me_profile_with_valid_token_returns_profile` (rad 48-60) ✓
- **Ingen test för `POST /api/v1/auth/logout` med/utan auth.**
- **Ingen test för `PATCH /api/v1/me/profile` med/utan auth.**

### Behövs nya tester i Turn 4?

**Ja — minst tre nya tester behövs i Turn 4:**

1. `LogoutTests.POST_logout_without_session_returns_401` — säkerställer att `RequireAuthorization()` på logout faktiskt avvisar oautentiserade kall (saknas idag, bör skrivas oavsett refaktor men blir extra kritisk när auth-pipelinen byts ut).
2. `LogoutTests.POST_logout_with_valid_session_returns_204` + verifiering av `ISessionStore.InvalidateAsync` (genom efterföljande `/api/v1/me`-kall som ska 401:a).
3. `MeTests.GET_me_with_invalidated_session_returns_401` — sliding TTL och invalidation måste verifieras end-to-end. Detta är ny edge case som JWT inte hade (JWT var stateless, så "invalidated" innebar bara `IAccessTokenRevocationStore.Contains`).

**Befintliga tester räcker INTE** — de täcker happy path + missing-auth, men inte:
- session-id med fel format → 401
- expired session-id → 401
- invalidated session-id → 401 (ny scenario)
- session-sliding-TTL: `GetAsync` förlänger TTL (täcks i `RedisSessionStoreTests`/`InMemorySessionStoreTests` — bra. Men en e2e-test att samma flöde funkar genom `ICurrentUser`-pipelinen behövs).

**Rekommendation:** lägg till dessa som del av Turn 4-PR, inte som uppskjuten teknisk skuld. De är direkt orsakade av designändringen och förlorar värde om de skjuts till Turn 5.

---

## Sammanfattning

| Fråga | Rekommendation |
|-------|---------------|
| Helper-strategi | Skapa ny `AuthTestHelpers.cs` med `LoginAndGetSessionIdAsync` + `RegisterAndGetSessionIdAsync` extension-methods. Radera privata `RegisterAndGetToken` i `MeTests`. |
| JWT-fixtures | Behåll RSA-PEM-generering + env-vars i `ApiFactory.cs` under Turn 4 (deprecation only). Städa när `JwtTokenGenerator` raderas i Fas 1. |
| Nya `RequireAuthorization`-tester | Behövs i Turn 4: minst 3 nya (`logout`-utan-session 401, `logout`-med-session 204, `me`-med-invaliderad-session 401). |
| Test-skrivning blockerad på | (1) Exakt response-body-form efter login/register (`sessionId` property-namn). (2) Hur session-id skickas tillbaka från klient: header (`X-Session-Id`), `Authorization: Bearer <session-id>`, eller annat. (3) Beslut om `RefreshReplayTests` raderas, skips, eller skrivs om. **Alla tre är dotnet-architect-beslut.** |

### Advisory note till dotnet-architect

Tre öppna designval blockerar test-skrivning:

1. **Property-namn för session-id i response body** — `sessionId`, `session`, eller `accessToken` (för bakåtkompatibilitet under deprecation)?
2. **Klient-till-server transport av session-id** — eftersom backend är cookie-agnostiskt: ska klienten skicka `Authorization: Bearer <session-id>`, en custom header `X-Session-Id`, eller body parameter? Påverkar exakt 7-8 test-rader i `MeTests` + `LogoutTests`.
3. **`RefreshCommand`/`RefreshCommandHandler`-status i Turn 4** — `[Obsolete]` parallellt med `JwtTokenGenerator`, eller helt raderad? Påverkar `RefreshCommandHandlerTests.cs` + `RefreshReplayTests.cs`.

Dessa beslut behöver låsas innan Red-fasen i Turn 4 kan börja.
