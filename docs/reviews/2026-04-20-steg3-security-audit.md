---
date: 2026-04-20
commits: e7507f0..c17f83d
auditor: security-auditor (claude-sonnet-4-6)
verdict: BLOCKED
blockers: 1
majors: 3
minors: 4
info: 1
---

# Security-audit: STEG 3 — Auth + JobSeeker (commits e7507f0..c17f83d)

**Status:** BLOCKED — en (1) BLOCKER + tre (3) MAJORS som kräver åtgärd innan STEG 4 påbörjas  
**Granskad:** 2026-04-20  
**Auditor:** security-auditor  
**Commits:** e7507f0..c17f83d (JwtBearer-setup, Auth-endpoints, JobSeeker-aggregat, integration tests)  
**Auktoritet:** GDPR Art. 5/6/25/32, OWASP ASVS V2/V3, CLAUDE.md §5.2/§5.4, ADR 0014

---

## Executive Summary

STEG 3 etablerar en i grunden solid auth-stack: RS256-JWT, hashade refresh tokens i Postgres, httpOnly/Secure/SameSite=Strict-cookie, stateless access-tokens med Redis jti-revokation, soft-delete på `JobSeeker` med global query-filter, `IAuthenticatedRequest`-markör + `AuthorizationBehavior`, och tydlig PII-hygien i `LoggingBehavior` (bara typnamn, inga claims/emails). Allt detta är beröm värt.

Men två saker måste åtgärdas innan vidare arbete:

1. **BLOCKER:** ADR 0014 kräver explicit replay-detektering på refresh token rotation ("om en redan roterad token används → revokera hela token-kedjan"). `RefreshCommandHandler` implementerar **inte** detta — handlern returnerar bara `InvalidToken` och lämnar återstoden av kedjan aktiv. Detta är en avvikelse från egen ADR + OWASP ASVS V3 och undergräver hela rotations-designen.
2. **MAJOR × 3:** Identity-lösenordspolicy för svag (8 tecken + specialtecken, ingen krav på siffra/upper/lower), refresh-cookie saknar `Path`-begränsning och rensas inte vid logout, `refresh_tokens.token_hash` saknar index (prestanda + ingen unik constraint).

Ingen BLOCKER gäller PII-hantering, GDPR-residens eller secret-läckor. Privata nyckeln är korrekt gitignored (`.secrets/` i `.gitignore`) och inte spårad i git. Lösenord passerar aldrig genom loggen. Emails skickas inte till externa tjänster.

**Verdict:** BLOCKED — fixa Blocker + minst 2 av 3 Majors innan merge till `main` i Fas 1. För nuvarande MVP-läge på `main` (pre-produktion) är detta teknisk skuld som måste loggas och inte lämnas olöst inför första PII-produktionsdeploy.

---

## Findings

### BLOCKER-1 — Replay-detektering på refresh token rotation saknas

**Fil:** `src/JobbPilot.Application/Auth/Commands/Refresh/RefreshCommandHandler.cs` rad 24–27  
**Referens:** `src/JobbPilot.Application/Common/Abstractions/IRefreshTokenStore.cs`  
**Auktoritet:** ADR 0014 §"Beslut" + OWASP ASVS V3.3.3 (Token Reuse Detection)

**Nuvarande:**
```csharp
var stored = await refreshTokenStore.FindActiveByHashAsync(tokenHash, cancellationToken);
if (stored is null)
    return Result.Failure<AuthTokensDto>(InvalidToken);
```

Handlern slår bara upp *aktiva* (ej revokerade, ej utgångna) tokens. När en angripare återanvänder en redan roterad token (d.v.s. `RevokedAt != null`) returnerar `FindActiveByHashAsync` `null` → generisk "InvalidToken"-response. Resten av token-kedjan från samma ursprungs-login förblir aktiv och fortsätter utfärda nya access tokens.

**Risk:** Angripare som stulit en refresh token kan användas osynligt parallellt med legitim användare. Rotation utan replay-detection ger ingen nettosäkerhet. ADR 0014 §Beslut punkt 2 kräver explicit: "om en redan roterad token används → revokera hela token-kedjan och logga säkerhetshändelse" — ej implementerat.

**Krävs:**
1. Utöka `IRefreshTokenStore` med `FindByHashAsync(string tokenHash, CancellationToken ct)` som returnerar även revokerade/utgångna tokens.
2. I `RefreshCommandHandler`: om den hittade token är revokerad (`RevokedAt != null`) → anropa `RevokeAllForUserAsync(stored.UserId, ct)` och logga en säkerhetshändelse (`LogWarning("Refresh token replay detected for user {UserId}", userId)`).
3. Returnera `InvalidToken` som idag för att inte avslöja att en replay upptäcktes.
4. Lägg till integration test `Refresh_ReusedRevokedToken_RevokesEntireChain`.

---

### MAJOR-1 — Lösenordspolicy för svag jämfört med OWASP

**Fil:** `src/JobbPilot.Infrastructure/DependencyInjection.cs` rad 44–49  
**Auktoritet:** OWASP ASVS V2.1.1, NIST SP 800-63B §5.1.1.2

**Nuvarande:**
```csharp
opts.Password.RequiredLength = 8;
opts.Password.RequireNonAlphanumeric = true;
opts.User.RequireUniqueEmail = true;
```

`RequireDigit`, `RequireUppercase`, `RequireLowercase` är implicit `false`. `aaaaaaa!` accepteras.

**Risk:** Credential stuffing och brute-force. Lätta lösenord som `password!` och `letmein!` accepteras. 

**Krävs (välj en av):**
- **Alt A (NIST-linje, rekommenderas):** `RequiredLength = 12`, låg komplexitet, breach-kontroll via PwnedPasswords.Client i Fas 1.
- **Alt B (klassisk Identity):** `RequireDigit = true`, `RequireUppercase = true`, `RequireLowercase = true`, `RequiredLength = 10`.

---

### MAJOR-2 — Refresh-cookie saknar Path-restriktion och rensas inte vid logout

**Fil:** `src/JobbPilot.Api/Endpoints/AuthEndpoints.cs` rad 55–77  
**Auktoritet:** OWASP ASVS V3.4.3, CLAUDE.md §5.2

**Problem 1:** Utan `Path = "/api/v1/auth"` skickas refresh-cookien på varje request till domänen — onödig exponering i loggar och reverse-proxy.

**Problem 2:** Logout-endpointen tar inte bort cookien i response. Browsern behåller `jobbpilot-refresh` tills `Expires` nås, trots revokerad serversidestoken.

**Krävs:**
1. Lägg till `Path = "/api/v1/auth"` i `CookieOptions`.
2. I logout-endpointen: `ctx.Response.Cookies.Delete("jobbpilot-refresh", new CookieOptions { Path = "/api/v1/auth", Secure = true, SameSite = SameSiteMode.Strict });`

---

### MAJOR-3 — `refresh_tokens.token_hash` saknar index och unik constraint

**Fil:** `src/JobbPilot.Infrastructure/Identity/Migrations/20260419193738_InitialIdentity.cs`  
**Auktoritet:** OWASP ASVS V3.2 + prestanda

`TokenHash` är `text not null` utan index, utan `UNIQUE`. `FindActiveByHashAsync` gör seq-scan — kostnaden växer linjärt. Ingen unique constraint → möjlig duplikat (defensivt svagt).

**Krävs:** Ny migration med `CREATE UNIQUE INDEX ix_refresh_tokens_token_hash ON identity.refresh_tokens (token_hash);` + `IEntityTypeConfiguration<RefreshToken>` med `.HasMaxLength(64)` och index.

---

### MINOR-1 — Access-token TTL i logout hårdkodat till 20 min

**Fil:** `src/JobbPilot.Application/Auth/Commands/Logout/LogoutCommandHandler.cs` rad 19

```csharp
await revocationStore.RevokeAsync(currentUser.Jti, TimeSpan.FromMinutes(20), cancellationToken);
```

Om `JwtSettings.AccessTokenLifetimeMinutes` ändras glöms TTL:n bort. Redis-entryn utgår för tidigt → token blir icke-revokerat i praktiken.

**Krävs:** Läs `exp`-claim från `currentUser` eller injicera `IOptions<JwtSettings>`.

---

### MINOR-2 — Refresh-flödet utfärdar ny access-token med tom email-claim

**Fil:** `src/JobbPilot.Application/Auth/Commands/Refresh/RefreshCommandHandler.cs` rad 30

```csharp
var newTokens = tokenGenerator.GenerateTokens(stored.UserId, string.Empty, roles);
```

Alla tokens efter första refresh saknar `email`-claim — inkonsekvent med login/register-flödena.

**GDPR-notering:** Övervägning om email-claim alls behövs i JWT — om nej, ta bort från alla flöden och låt FE läsa via `/api/v1/me`.

---

### MINOR-3 — RSA-nyckelobjekt i `JwtTokenGenerator.LoadPrivateKey` disposas aldrig

**Fil:** `src/JobbPilot.Infrastructure/Auth/JwtTokenGenerator.cs` rad 70–76

Varje token-generering skapar ny `RSA`-instans som disposas aldrig → GC-tryck + potentiell CNG-handle-läcka vid höga volymer.

**Krävs:** Läs PEM en gång i DI-registrering, cacha som singleton `RsaSecurityKey`, injicera i generator.

---

### MINOR-4 — Default `WeeklySummary = true` (GDPR opt-in)

**Fil:** `src/JobbPilot.Domain/JobSeekers/Preferences.cs` rad 3–6

```csharp
public sealed record Preferences(
    string Language = "sv",
    bool EmailNotifications = true,
    bool WeeklySummary = true);
```

`WeeklySummary` är marketing-adjacent digest — kräver explicit samtycke (GDPR Art. 6.1.a + ePrivacy). Inget blocker nu (ingen mailer), men **måste** åtgärdas innan mail-sändande kod merges.

**Krävs:** Ändra default till `WeeklySummary = false`.

---

### INFO-1 — `${POSTGRES_PASSWORD_DEV}` i `appsettings.Development.json` expanderas inte av .NET

`IConfiguration` expanderar inte `${VAR}`-syntax. Verifiera att `appsettings.Local.json` overridar korrekt. Inget säkerhetsproblem — bara felriskbärande.

---

## Approved items

1. ✓ RSA privat nyckel gitignored (`.secrets/`) — inte i git history.
2. ✓ Refresh tokens hashas (SHA-256) innan persistens — inte lagrade i klartext.
3. ✓ Refresh-cookie HttpOnly + Secure + SameSite=Strict — XSS- och CSRF-mitigering.
4. ✓ Access-tokens stateless JWT med kort TTL (15 min) + Redis jti-blacklist.
5. ✓ `LoggingBehavior` loggar endast typnamn — inga emails/lösenord i loggar.
6. ✓ `ClockSkew = TimeSpan.Zero` — ingen gratis expiry-förlängning.
7. ✓ Alla fyra JWT-validationskrav aktiva: Issuer, Audience, Lifetime, SigningKey.
8. ✓ `OnTokenValidated`-event konsulterar revocation store — logout faktiskt håller.
9. ✓ `AuthorizationBehavior` + `IAuthenticatedRequest`-markör — opt-in-mönster.
10. ✓ `RequireAuthorization()` på alla /me-endpoints.
11. ✓ `JobSeeker` har `DeletedAt` + global query filter — soft-delete korrekt.
12. ✓ Generiskt felmeddelande vid inloggning — avslöjar inte om emailen finns.
13. ✓ Integrationstesterna genererar temporära RSA-nycklar per körning.
14. ✓ `UseHttpsRedirection()` aktivt.

---

## GDPR-notes

- **PII-kategorier:** Email (Identity), lösenordshash, `DisplayName` (JobSeeker), `CreatedByIp` (RefreshToken — potentiellt PII per Art. 4), UserId (pseudonym).
- **Storage region:** Ingen AWS-kod i detta diff. Säkerställ EU-region (eu-north-1) vid RDS/Redis-deploy.
- **Audit trail:** `RefreshToken` sparar `CreatedAt`, `CreatedByIp`, `RevokedAt`, `ReplacedByTokenId`. Login/logout-händelser loggas inte explicit — rekommenderas som Fas 1-ärende.
- **Right to deletion:** `JobSeeker.SoftDelete()` finns men kaskaderar inte till refresh tokens. Fullständigt "delete my account"-flöde krävs innan produktions-release.
- **Cross-region data flows:** Inga externa anrop i STEG 3 — ingen CrossRegion-exposure.

---

## Öppna ärenden (Fas 1)

- [ ] Account deletion endpoint med kaskad till JobSeeker + refresh tokens + audit log (GDPR Art. 17)
- [ ] Separat säkerhets-audit-log-kanal för login/logout/refresh-events
- [ ] PwnedPasswords-integration (MAJOR-1 Alt A)
- [ ] RSA-nyckel till AWS KMS/Secrets Manager (EU-region)

---

## Åtgärdsplan

| Prioritet | Finding | Var | Estimat |
|-----------|---------|-----|---------|
| 1 (BLOCKER) | Replay-detektering | `IRefreshTokenStore` + `RefreshCommandHandler` | ~2h |
| 2 (MAJOR) | Lösenordspolicy | `DependencyInjection.cs` | ~15 min |
| 3 (MAJOR) | Cookie Path + logout-cleanup | `AuthEndpoints.cs` | ~30 min |
| 4 (MAJOR) | token_hash index | Ny EF migration | ~30 min |
| 5 (MINOR) | WeeklySummary default | `Preferences.cs` | ~5 min |
| 6 (MINOR) | Logout TTL | `LogoutCommandHandler.cs` | ~30 min |
