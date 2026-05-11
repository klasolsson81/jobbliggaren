# ADR 0030: Frontend API result kind-union convention

- **Status:** Accepted
- **Date:** 2026-05-11
- **Deciders:** Klas Olsson (slutgodkännande), senior-cto-advisor (multi-approach-triage)
- **Related:** ADR 0017 (frontend-auth), ADR 0020 (Zod DTO-validering), TD-53 (split TD-53a + TD-53b), TD-7 (TD-7 etablerade ACL-helpern som ADR 0030 utökar)

## Kontext

Frontend-API-funktioner i `web/jobbpilot-web/src/lib/api/*.ts` returnerar
data från backend via `fetch()` + Zod-validerad `parseResponse` (ADR 0020).
Returvärdets *shape* är validerad. Returvärdets *outcome-semantik* är inte —
fram till denna ADR.

Befintliga `T | null`-mönster i `getMyProfile`, `getApplicationById`,
`getResumeById` (och list-endpoints i TD-53b) komprimerar fyra olika
betydelser till en bit:

- **Unauthorized (401)** — användaren har ingen aktiv session
- **Forbidden (403)** — användaren är inloggad men saknar behörighet (Admin-only)
- **Not Found (404)** — resursen finns inte (eller tillhör inte användaren)
- **Network/parse-error (5xx / DtoParseError)** — backend nere eller shape-skew

UI:t kan inte distingera mellan dessa via `null`. Konkret konsekvens:
"Du saknar behörighet" och "Backend nere" renderar samma generic-tomt-state.
Det är fel UX och dålig observability.

Nya `lib/api/admin.ts` etablerade ett bättre mönster vid TD-7-leveransen
(`AuditLogResponse` som diskriminerat union). TD-53-arbetet generaliserar
det mönstret till hela frontend-API-ytan.

## Beslut

### 1. Generic `ApiResult<T>`-union är standard för data-endpoints

`lib/dto/_helpers.ts` exporterar:

```ts
export type ApiResult<T> =
  | { kind: "ok"; data: T }
  | { kind: "unauthorized" }
  | { kind: "forbidden" }
  | { kind: "notFound" }
  | { kind: "error" };
```

Varje variant motsvarar en distinkt UI-state och en distinkt user-action:

| Kind | HTTP-trigger | UI-action |
|---|---|---|
| `ok` | 2xx + valid shape | Rendera data |
| `unauthorized` | 401 / no session | Redirect till `/logga-in` |
| `forbidden` | 403 | Visa "Saknar behörighet"-state |
| `notFound` | 404 | Visa "Resursen hittades inte"-state |
| `error` | 5xx / network / DtoParseError | Visa "Tekniskt fel"-state |

### 2. `responseToResult<T>` är gemensam helper

```ts
export async function responseToResult<T>(
  res: Response,
  schema: z.ZodType<T>,
  context: string,
  options?: { includeNotFound?: boolean }
): Promise<ApiResult<T>>
```

Helpern mappar HTTP-status till `kind` enligt tabellen ovan. `includeNotFound`
är default `false` — list-endpoints distingerar inte 404 (vilket bryter
404-semantik annars), endast detail-endpoints sätter den till `true`.

Vid `parseResponse`-success: `{ kind: "ok", data }`. Vid Zod-mismatch eller
JSON-parse-fel: `{ kind: "error" }` (logging behålls via `parseResponse`).

### 3. `getServerSession()` är undantag — behåller `CurrentUser | null`

`null` är där **legitim domän-semantik** ("ingen aktiv session"), inte
fel-komprimering. Auth-pipeline-design (ADR 0017) använder cookie-existens
som första gate och `null` som "no session"-signal. Att kind-union:isera
detta skulle förvirra layer-ansvar mellan auth och data.

Konsumenter som vill skilja "no session" från "session-fetch failed" kan
kontrollera cookie-existens via `getSessionId()` innan call.

### 4. Konsumenter använder `switch (result.kind)` med exhaustiveness-check

```tsx
const result = await getApplicationById(id);
switch (result.kind) {
  case "ok":
    return <ApplicationDetail data={result.data} />;
  case "unauthorized":
    redirect("/logga-in");
  case "notFound":
    notFound();  // Next.js notFound-helper
  case "forbidden":
    return <ForbiddenState />;
  case "error":
    return <ErrorState />;
  default:
    return assertNever(result);
}
```

`assertNever(value: never): never` exporteras från `lib/dto/_helpers.ts`
för exhaustiveness-checking. Glömd `case` blir TypeScript-fel, inte
runtime-skyltning.

### 5. List-endpoints distingerar inte `notFound` — endast detail-endpoints

List-endpoints har inte "id finns inte"-semantik (en tom array är giltigt
svar). De använder samma `ApiResult<T>` men `notFound` är aldrig värdet
(eller saknas via `Exclude<ApiResult<T>, { kind: "notFound" }>` om
TypeScript-precision behövs).

### 6. Symmetri med backend-`Result<T, E>` är önskvärd men inte krav

Backend använder `Result<TSuccess, TError>` (CLAUDE.md §3.4, BUILD.md §2.3).
Frontend-pattern är konceptuellt likvärdig men `kind`-diskriminator är
mer idiomatic TypeScript än `{ ok, value, error }`-tuple. Bekräftar
"identisk anda, olika idiom"-pragmatism.

## Avvisade alternativ

### Variant B — `{ ok: true; data: T } | { ok: false; error: ApiError }`

Funktionellt elegant men `kind`-diskriminator ger bättre TypeScript-
exhaustiveness via `never` i `default`. Backend-`Result<T, E>`-symmetri är
svag motivering eftersom språken har olika idiom. **Avvisad.**

### Variant C — Hybrid: kind-union endast på detail-endpoints, behåll `T | null` på list

Bryter Common Closure Principle (Martin 2017 kap. 14): codebase ska ha
**ett** sätt att representera API-resultat. Konsumenter tvingas lära sig
två mönster + välja rätt per call site. Hybrid är inte pragmatism — det
är uppskjuten refactor-kostnad. **Avvisad.**

### Variant D — Behåll `T | null`, lyft TD som "wait-and-see"

Mastercard-test (CLAUDE.md §1): utomstående granskare ser admin.ts:s
kind-union bredvid `me.ts`:s `T | null` och frågar varför. Vi har inget
bra svar. **Avvisad.**

## Konsekvenser

### Positiva

- **Konsekvent API-yta** — samma pattern överallt, lägre cognitive load
- **Exhaustiveness-check** via `assertNever` fångar glömda UI-states vid kompilering
- **UX-precision** — användare ser rätt feedback (forbidden vs nere vs notFound)
- **Observability-grund** — distinkt logging per outcome-kind möjlig (Fas 2+)
- **OCP-compliance** — nya outcome-kinds (t.ex. `rateLimited`) kan adderas utan att bryta call-sites

### Negativa

- **Boilerplate ökar marginellt** — `switch` istället för `if (data === null)`. Acceptabelt — explicit > implicit vid felhantering
- **8+ konsumenter måste uppdateras vid migration** — fördelat över TD-53a + TD-53b. Engångskostnad

### Neutrala

- `assertNever`-helper är ny pattern i kodbasen. Etableras av denna ADR.
- TanStack Query (framtida) kan adaptera `ApiResult<T>` direkt eftersom den är JSON-serialiserbar och Server/Client-symmetrisk.

## Migration

### TD-53a (denna leverans)

- `responseToResult<T>` + `assertNever` i `lib/dto/_helpers.ts`
- Refactor: `getMyProfile`, `getApplicationById`, `getResumeById`
- Konsumenter: `app/(app)/mig/page.tsx`, `app/(app)/ansokningar/[id]/page.tsx`, `app/(app)/cv/[id]/page.tsx`

### TD-53b (separat batch)

- Refactor: `getPipeline`, `getApplications`, `getResumes`
- Konsumenter: `app/(app)/ansokningar/page.tsx`, `app/(app)/cv/page.tsx`
- `admin.ts` migreras till `ApiResult<T>` (ersätter dess unika `AuditLogResponse`)

### Acceptanskriterier (TD-53a)

- `responseToResult<T>` exporterad och testad
- 3 detail/profile-endpoints returnerar `ApiResult<T>`
- 3 konsumenter använder exhaustive switch
- Inga `T | null`-returer kvar i refactorade endpoints
- `pnpm typecheck` + `pnpm test` grönt utan nya regressioner

## Referenser

- Robert C. Martin, *Clean Architecture* (2017), kap. 8 (OCP), kap. 14 (CCP)
- Eric Evans, *Domain-Driven Design* (2003), kap. 5 (Value Objects), kap. 14 (ACL)
- Martin Fowler, *Refactoring* 2nd ed (2018), "Replace Data Value with Object"
- ADR 0020 — Zod-DTO-validering vid HTTP-gränsen (ACL-shape; ADR 0030 är ACL-outcome)
- ADR 0017 — Frontend authentication pattern (motivering för `getServerSession` undantag)
- TD-53 (ersatt av TD-53a + TD-53b)

## Validation

- Unit-tests för `responseToResult` per status-kod (200/401/403/404/500/network/parse-fel)
- Konsument-tests verifierar exhaustiveness via `assertNever`
- code-reviewer + design-reviewer (UI-states) invokeras vid TD-53a-leverans

## Out of scope

- Tillstånds-helpers per `kind` (komponenter som `<ApiStateRenderer>`) — UI-detalj utanför ACL
- Globala error-toast vid `kind: "error"` — observability/UX-policy separat
- Retry-policy per `kind` — TanStack Query-domän (Fas 2+)
