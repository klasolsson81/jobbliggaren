# ADR 0012 — Auth-stack: ASP.NET Core Identity + JWT (RS256)

**Datum:** 2026-04-19
**Status:** Accepted
**Kontext:** STEG 3 — Auth-stack
**Beslutsfattare:** Klas Olsson
**Relaterad:** BUILD.md §11, ADR 0001

## Kontext

BUILD.md §3.1 föreskriver ASP.NET Core Identity + JwtBearer för auth. Inför Fas B-implementation behöver det tekniska valet formaliseras mot konkreta alternativ och varje avvisning motiveras explicit.

Krav som styr valet:

- GDPR-datasuveränitet: användardata får inte processas utanför EU utan explicit samtycke
- Lokal testbarhet utan internet-anslutning
- Gratis för solo-dev-fas och skalbart till kommersiell fas utan licensbyte
- Välkänt mönster i .NET-ekosystemet — låg onboarding-kostnad för framtida medarbetare
- Stateless JWT-validering möjlig i separata services (Worker) utan att exponera signerings-hemlig nyckel

## Beslut

ASP.NET Core Identity 10 för user-management (password hashing, roller, claims). `Microsoft.AspNetCore.Authentication.JwtBearer` för JWT-validering. RS256-signering (asymmetrisk nyckel — verifiering kräver bara publik nyckel, utfärdning kräver privat). Access token livslängd 15 minuter, refresh token livslängd 14 dagar.

## Konsekvenser

**Positivt:**

- Microsoft-supportat, välbeprövat i .NET-ekosystemet, ingen extra licenskostnad
- Lokalt testbart utan internet-anslutning — `InMemoryUserStore` i unit tests
- RS256 tillåter stateless JWT-validering i separata services (t.ex. Worker) utan att exponera privat signeringsnyckel
- Full kontroll över datalagring — all användardata stannar i vår Postgres-instans (GDPR-kontroll)

**Negativt:**

- Identity-tabellernas kolumnnamn är föreskrivna (AspNetUsers, AspNetRoles etc.) med PascalCase i EF-mappningen, vilket inte matchar vår snake_case-konvention för domändata
- `UserManager<T>` API har async overhead och är inte alltid direkt testbart utan test-doubles

**Mitigering:**

- Identity-tabeller isoleras i separat DbContext och eget Postgres-schema (`identity`) — se ADR 0013
- `UserManager<T>` wrappas bakom ett application-interface (`IUserAccountService`) för testbarhet

## Alternativ övervägda

**Alt 1 — AWS Cognito:** Avfärdat. Vendor lock-in mot AWS. GDPR-komplexitet: data lagras i US-regioner som default. Kostnad per MAU vid tillväxt. Inte testbart lokalt utan internet-anslutning.

**Alt 2 — Duende IdentityServer:** Avfärdat. Kommersiell licens krävs för produktion med fler än 1 000 MAU. Overkill för solo-dev-fas. Drar med sig ett eget OAuth2/OIDC-server-skikt som inte tillför värde i Fas 0–1.

**Alt 3 — Egen implementation (PasswordHasher + egna tabeller):** Avfärdat. Säkerhetsrisk — PBKDF2-implementationsdetaljer (iterations, salt-längd) är lätta att göra fel. Hjul-återuppfinning av välbeprövad och auditad kod.

**Alt 4 — Auth0 / Clerk:** Avfärdat. Samma GDPR/datasuveränitets-problematik som Cognito. Månadsavgift som ökar med MAU. Extern SaaS-beroende för kärnfunktionalitet.

## Implementationsstatus

**Beslutsdatum:** 2026-04-19 (session 8, inför Fas B — Auth-stack)

**Ej implementerat än:** Implementation startar i Fas B. Konkret: `AddIdentity<ApplicationUser, IdentityRole>()`, `AddJwtBearer()`, RS256-nyckelpar genereras och lagras i AWS Secrets Manager (prod) / `appsettings.Development.json` (dev).

**Nästa steg:** ADR 0013 dokumenterar hur Identity-kontexten separeras från domänkontexten. ADR 0014 dokumenterar refresh token-strategi.
