# Security Audit — Turn 2 (layout.tsx + mig/page.tsx)

Datum: 2026-05-07
Granskare: security-auditor agent
Scope: `src/app/(app)/layout.tsx`, `src/app/(app)/mig/page.tsx`
Kontext: `src/lib/auth/session.ts`, `src/lib/auth/actions.ts`, `src/app/api/me/route.ts`, backend `CurrentUserDto.cs`

---

## Verdict

Fynd — 0 blockers, 2 major, 3 minor, flera praise.
Mergeklar med villkor: de 2 major-fynden bör adresseras.

---

## 1. PII-visning

**Filer:** `src/app/(app)/mig/page.tsx`, `src/app/(app)/layout.tsx`

`/mig` visar tre fält för inloggad användare: `userId`, `email`, `roles`.

### Rimlighetsbedömning per fält

| Fält | Visas för | Bedömning |
|------|-----------|-----------|
| `email` | Inloggad användare själv | OK — användaren känner till sin egen e-post |
| `userId` (Guid) | Inloggad användare själv | Se nedan |
| `roles` | Inloggad användare själv | OK med förbehåll — se nedan |

**`userId` (Guid) — Minor:**
Att exponera internt databas-ID i UI:t är inte en GDPR-violation i sig (det är inte mer känsligt än en e-post), men det fyller inget användarbehov och ökar attackytan om sidan i framtiden cachas, screenshotas eller screen-shares. En intern UUID är inte meningsfull för slutanvändaren. Rekommendation: ta bort `userId`-raden från `/mig`-sidan, eller begränsa visningen till admin-användare.

**`roles` — Minor:**
Rollnamn exponeras direkt (`roles.join(", ")`). Om rollnamn i framtiden speglar interna systembegrepp (t.ex. "SuperAdmin", "BetaFeatureX") kan detta läcka intern information om systemarkitektur. Inga akuta roller identifierade i nuläget, men fältet bör granskas på nytt när rollsystemet expanderas.

**Oavsiktlig exponering — Praise:**
`getServerSession()` körs server-side, data renderas i Server Component utan att exponeras i klient-JavaScript. Ingen risk för att PII hamnar i `window.__NEXT_DATA__` eller liknande klient-synliga strukturer.

---

## 2. Session-hantering

**Filer:** `src/lib/auth/session.ts`, `src/app/(app)/layout.tsx`, `src/app/(app)/mig/page.tsx`

### Null-check och redirect-pattern

Båda komponenterna (`layout.tsx` och `mig/page.tsx`) gör:

```typescript
const user = await getServerSession();
if (!user) redirect("/logga-in");
```

Redirect-target `/logga-in` är en hårdkodad sökväg. Ingen dynamisk komponent, ingen user-supplied input. Ingen open redirect-vektor.

**Dubbelskydd-analys:**
`layout.tsx` innehåller redan null-check + redirect. `mig/page.tsx` duplicerar samma check. Tekniskt sett är `layout.tsx`:s guard tillräcklig eftersom `mig/page.tsx` alltid renderas inuti layouten. Dupliceringen är defensiv och inte felaktig, men skapar en minor observation.

**Minor — Redundant session-check i mig/page.tsx:**
`mig/page.tsx` anropar `getServerSession()` en andra gång. React `cache()` innebär att backend-anropet inte dupliceras (dedupleras per request), men det tillför onödig komplexitet och kan vilseleda framtida utvecklare om vad som faktiskt skyddar routen. Rekommendation: ta bort null-check från `mig/page.tsx` och lita på att `layout.tsx` enforcar autentisering. Lägg en kommentar i `layout.tsx` som förklarar att det är gatekeepern.

**Open redirect — Praise:**
`safeRedirectPath()` i `actions.ts` validerar korrekt att redirect-mål börjar med `/` men inte `//` eller `/\`. Detta blockerar de vanligaste open redirect-vektorerna. Pattern är säkert.

### Cookie-konfiguration

`setSessionCookie()` i `session.ts` sätter:
- `httpOnly: true` — skyddar mot XSS-stöld
- `secure: true` — kräver HTTPS
- `sameSite: "strict"` — starkt CSRF-skydd
- `__Host-`-prefix — kräver secure + path=/ + ingen domain, förhindrar subdomain cookie injection

Detta är exemplarisk cookie-konfiguration.

---

## 3. DTO-falt-konsistens (KRITISK KONTROLL)

### Backend CurrentUserDto (verbatim)

Fil: `c:\DOTNET-UTB\JobbPilot\src\JobbPilot.Application\Auth\Queries\GetCurrentUser\CurrentUserDto.cs`

```csharp
namespace JobbPilot.Application.Auth.Queries.GetCurrentUser;

public sealed record CurrentUserDto(
    Guid UserId,
    string Email,
    IReadOnlyList<string> Roles);
```

### Frontend CurrentUser (verbatim)

Fil: `c:\DOTNET-UTB\JobbPilot\web\jobbpilot-web\src\lib\auth\session.ts`

```typescript
export type CurrentUser = {
  userId: string;
  email: string;
  roles?: string[];
};
```

### Falt-jamforelse

| Backend-falt | Backend-typ | Frontend-falt | Frontend-typ | Status |
|--------------|-------------|---------------|--------------|--------|
| `UserId` | `Guid` (serialiseras till `string` som UUID) | `userId` | `string` | OK — camelCase via JSON-serialisering, Guid serialiseras som string |
| `Email` | `string` (non-nullable) | `email` | `string` | OK |
| `Roles` | `IReadOnlyList<string>` (non-nullable) | `roles` | `string[]` (optional `?`) | **MISMATCH — se nedan** |

### Mismatch: Roles-nullability

**Major — DTO-typ-mismatch pa Roles/roles:**

Backend `CurrentUserDto.Roles` deklareras som `IReadOnlyList<string>` — aldrig null. `GetCurrentUserQueryHandler` populerar den via `userAccountService.GetRolesAsync(...)` och returnerar vad tjänsten levererar. Om en användare inte har några roller är det rimligt att backend returnerar en tom lista `[]`, inte `null`.

Frontend-typen `roles?: string[]` behandlar fältet som optional (kan vara `undefined`). I `mig/page.tsx` skyddas detta av:

```typescript
{user.roles && user.roles.length > 0
  ? user.roles.join(", ")
  : "Inga roller tilldelade"}
```

Det finns tre risker:

1. **Typ-lögn:** Om backend alltid skickar en lista (aldrig null) är `?` i frontend-typen en felaktig representation av kontraktet. Om backend i framtiden faktiskt returnerar `null` (t.ex. vid en bug eller refaktorering) ger TypeScript inga varningar eftersom typen tillåter det.

2. **Implicit `as Promise<CurrentUser>`-cast:** I `session.ts` rad 27:
   ```typescript
   return res.json() as Promise<CurrentUser>;
   ```
   Denna cast är unsafe — den accepterar vad som helst från backend utan runtime-validering (t.ex. Zod). Om backend introducerar ett nytt fält eller ändrar ett befintligt bryts tyst utan kompileringsfel.

3. **Ingen runtime-validering av backend-svaret:** Om backend returnerar oväntad struktur (extra fält, saknade fält, null istället för lista) kastas inga fel — komponenter kan krascha med svårfångade runtime-errors eller visa fel data.

**Rekommendation:** Inför Zod-schema för `CurrentUser` och validera backend-svaret i `getServerSession()`. Alternativt: gör `roles` non-optional (`roles: string[]`) om backend garanterar non-null lista, och dokumentera kontraktet med ett kommentar.

Delegera till: `nextjs-ui-engineer` (Zod-schema + typ-fix) + `dotnet-architect` (verifiera att backend aldrig returnerar null för Roles).

---

## 4. Logout-flow

**Fil:** `src/lib/auth/actions.ts` (`logoutAction`)

### Server Action CSRF-skydd

`logoutAction` är en Server Action (`"use server"`). Next.js 15+ (och Next.js 16 som projektet använder) inkluderar inbyggt CSRF-skydd för Server Actions via:

1. Origin-validering: Next.js kontrollerar att `Origin`-headern matchar server-hosten för form-submissions som triggar Server Actions.
2. `Content-Type: application/x-www-form-urlencoded` för form-actions, vilket browser skickar automatiskt.

Kombinerat med `sameSite: "strict"` på session-cookien är CSRF-risken låg. En angripare kan inte trigga `logoutAction` från en cross-origin form eftersom:
- `sameSite: "strict"` hindrar cookien från att skickas i cross-site requests
- Next.js origin-check blockerar requests med fel origin-header

**Praise:** Dubbelt skydd (SameSite + Next.js origin-validering) utan att manuellt implementera CSRF-tokens.

### Logout-robusthet

`logoutAction` raderar alltid den lokala cookien, även om backend-anropet misslyckas:

```typescript
} catch {
  // Always delete the local cookie even if backend is unreachable
}
await deleteSessionCookie();
redirect("/logga-in");
```

Detta är korrekt beteende — en backend-timeout får inte låsa ut användaren från att logga ut.

### Minor — Ingen audit-log for logout

`logoutAction` skickar `POST /api/v1/auth/logout` till backend, men om backend-anropet misslyckas (catch-blocket) loggas inget. Om backend inte loggar session-deletion för failed backend-call saknas spårbarhet för "session deleted locally but not on server". Detta är en minor observation — sessionen är ogiltig på backend ändå när den löper ut, men för forensisk spårbarhet vore det bättre.

---

## 5. Information leakage

### Felmeddelanden

`loginAction` returnerar:
- `"Inloggningen misslyckades. Kontrollera e-post och lösenord."` — för 401
- `"Ett oväntat fel uppstod. Försök igen."` — för andra HTTP-fel

**Praise:** Meddelandet avslöjar inte om e-postadressen finns eller inte. Enum-safe felmeddelanden som uppfyller GDPR Art. 5(1)(f)-principen om integritet och konfidentialitet.

### Cache-headers pa fetch-anrop

`getServerSession()` i `session.ts` använder `cache: "no-store"`. Route Handler `src/app/api/me/route.ts` använder också `cache: "no-store"`. Detta förhindrar att känslig session-data cachas i Next.js data-cache.

**Praise:** `cache: "no-store"` konsekvent på alla autentiserade backend-anrop.

### Middleware saknas i source

`src/middleware.ts` existerar inte i source-tree (men `.next/server/middleware.js` finns i build-output, vilket indikerar att filen kan ha raderats eller inte committats). Uppgiftsbeskrivningen refererar till middleware som "cookie presence check". Om middleware saknas i source:

**Major — Middleware-kalla saknas i source-kontroll:**

Om `src/middleware.ts` inte finns i version control (filen existerar inte ens som path) innebär det att:

1. Det är oklart exakt vilka routes som skyddas av middleware och på vilket sätt.
2. Framtida utvecklare kan inte läsa, granska eller modifiera route-skyddet.
3. Om `.next/`-katalogen rensas och projektet byggs om från scratch kan middleware-skyddet försvinna.

Build-output `.next/server/middleware.js` bekräftar att en middleware har byggts och existerat. Filen bör återfinnas eller återskapas och committas.

Eskaleras till Klas: "Är `src/middleware.ts` committad? Den finns i build-output men inte i source-tree. Om den tappats bort bör den återskapas och committas omedelbart."

Delegera till: `nextjs-ui-engineer` (hitta och återskapa/commita filen om den saknas).

### Source maps

`tsconfig.json` granskades inte i detta scope — source map-konfiguration är utanför de granskade filernas direkta scope men bör verifieras separat.

---

## Sammanfattning

| Nr | Fynd | Severity | Omrade | Delegera till |
|----|------|----------|--------|---------------|
| 1 | `roles?: string[]` mismatch mot backend non-nullable `IReadOnlyList<string>` + unsafe `as`-cast utan runtime-validering | Major | Area 1, Area 7 | nextjs-ui-engineer + dotnet-architect |
| 2 | `src/middleware.ts` saknas i source-tree (finns i build-output) | Major | Area 3 | nextjs-ui-engineer + eskalera till Klas |
| 3 | `userId` (intern Guid) visas i UI utan uppenbart användarbehov | Minor | Area 1 | nextjs-ui-engineer |
| 4 | Redundant `getServerSession()`-anrop i `mig/page.tsx` | Minor | Area 3 | nextjs-ui-engineer |
| 5 | Ingen lokal loggning om logout-backend-call misslyckas | Minor | Area 6 | nextjs-ui-engineer |

### Praise

- `__Host-`-cookie-prefix med `httpOnly`, `secure`, `sameSite: "strict"` — exemplarisk cookie-konfiguration
- `cache: "no-store"` konsekvent pa alla autentiserade backend-anrop
- `safeRedirectPath()` korrekt skyddar mot open redirect
- Server-side rendering av PII — inget PII i klient-JavaScript
- Loginfelmeddelanden avslöjar inte om e-post existerar
- Logout raderar lokal cookie oavsett backend-status

Inga GDPR-blockers identifierade. De 2 major-fynden bör adresseras innan merge.
