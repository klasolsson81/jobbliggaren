# ADR 0020: Frontend-DTO-validering vid HTTP-gränsen med Zod

- **Status:** Accepted
- **Date:** 2026-05-11
- **Deciders:** Klas Olsson (slutgodkännande), senior-cto-advisor (multi-approach-triage)
- **Related:** ADR 0015 (frontend-stack), ADR 0017 (frontend-auth), TD-7 (stänger), TD-55 (ersätter hand-rullad `isPagedResult`)

## Kontext

Next.js-frontenden konsumerar JSON-DTO:er från .NET-backenden via `fetch()` +
`res.json()`. `res.json()` returnerar `unknown` i runtime — TypeScript-typen
som påförs via cast är ett påstående, inte ett bevis.

Manuellt deklarerade `interface`-typer i `lib/types/*.ts` drev en bekräftad
Major-bugg i security-auditor-review Turn 2 (2026-05-07): `roles?: string[]`
i frontend matchade inte `IReadOnlyList<string>` i backend, vilket tyst gav
tom roles-array i UI när nyckeln saknades. `tsc` kan inte fånga sådan
shape-skew — den uppstår vid runtime-gränsen.

Före denna ADR fanns tre olika valideringsmönster i koden samtidigt:

1. **Blind cast:** `(await res.json()) as Dto` — 6 call-sites (TD-7-fynd).
2. **Hand-rullad type-guard** för paginerade wrappers: `isPagedResult<T>` med
   opt-in per-item-guard (TD-55-leverans i `lib/types/paged.ts`).
3. **Zod på input-sidan** för Server Actions: `lib/actions/*-schemas.ts`
   (3 filer, Zod 4.4.3 redan i `package.json`).

Detta är inkonsekvent. Backend och frontend är separata *bounded contexts*
kopplade via HTTP — gränsen behöver en *Anti-Corruption Layer* (Evans 2003
kap. 14) som översätter och validerar, inte litar på.

## Beslut

JobbPilot validerar varje backend-DTO i runtime vid `res.json()`-gränsen med
**Zod-schemas**. Sex konkreta beslut:

### 1. Zod är ACL-verktyget för HTTP-gränsen

Varje DTO som tas emot från backend har ett Zod-schema i `lib/dto/<domän>.ts`.
Schemat är *single source of truth* för shape:n — TypeScript-typen härleds
via `z.infer<typeof schema>`. Inga parallella `interface`-deklarationer.

### 2. `parseResponse<T>` är gemensam helper

```ts
// lib/dto/_helpers.ts
export async function parseResponse<T>(
  res: Response,
  schema: z.ZodType<T>,
  context: string  // t.ex. "GET /api/v1/me"
): Promise<T>
```

Helpern:

- läser `res.json()` (catchar JSON-parse-fel som strukturerat `DtoParseError`)
- kör `schema.safeParse()`
- vid fail: loggar via `console.error` med `context`, Zod-`issues` (path + message)
  och kastar `DtoParseError`
- vid pass: returnerar parsed `T`

Konsumenter wrappar `parseResponse` inom befintliga try-block och behandlar
fel som "backend nere"-state (motsvarar nuvarande `null`-returvärden).

### 3. Schemas co-lokaliseras nära API-klienter

Filstruktur:

```
lib/dto/
├── _helpers.ts         # parseResponse + DtoParseError
├── me.ts               # CurrentUser + JobSeekerProfile
├── applications.ts     # ApplicationDto, PipelineGroupDto, paged wrapper
├── resumes.ts          # ResumeDetailDto + nested content
└── admin.ts            # AuditLogPagedResult
```

`lib/api/*.ts` importerar från `lib/dto/`. `lib/types/*.ts` blir tunna
re-exports under en migrationsperiod (eller raderas direkt — se §
*Migration*).

### 4. Strikta schemas (`z.object({...}).strict()` är ej default)

Schemas är **icke-strikta som default** — extra fält från backend ignoreras.
Detta tillåter additiva backend-ändringar utan att frontenden bryter.

Strikt-mode (`.strict()`) övervägs per fält där okänd nyckel skulle vara
säkerhetsrelevant (auth/admin-shapes). I praktiken: defaultet räcker.

### 5. Pagineringsschema ersätter `isPagedResult`

`lib/dto/_helpers.ts` exporterar en generic `pagedResult(itemSchema)` som
producerar Zod-schema motsvarande backend `PagedResult<T>`. Detta ersätter
hand-rullade `isPagedResult` (TD-55) — full item-validering blir default
istället för opt-in.

```ts
export function pagedResult<T>(item: z.ZodType<T>) {
  return z.object({
    items: z.array(item),
    totalCount: z.number().int().nonnegative(),
    page: z.number().int().positive(),
    pageSize: z.number().int().positive(),
  });
}
```

### 6. Datum är `string` på wire, parsas inte till `Date` här

DTO-schemas validerar `z.string()` för ISO 8601 / `yyyy-MM-dd` på wire-nivå.
Konvertering till `Date` är UI-formateringsansvar (se DESIGN.md locale-regler)
— inte ACL-ansvar. Detta håller schemas symmetriska mot backend JSON-output.

## Avvisade alternativ

### Variant B — OpenAPI-codegen från backend-spec

`.NET 10` har inbyggd OpenAPI-export. Tool som `openapi-zod-client` kan
generera `lib/dto/generated/*.ts` automatiskt. Single source of truth blir
backend-koden.

**Avvisad nu, accepterad som framtida supersession.** Grund:

- Backend `/openapi/v1.json` är inte etablerad som versionerad artefakt
- CI-pipeline för codegen saknas
- BUILD.md placerar `docs/api/openapi.yaml` "post-Fas 0"
- 6 DTOs nu motiverar inte att bygga pipelinen — spekulativ generalisering
  (YAGNI per Hunt/Thomas 1999)

Path forward: när backend OpenAPI-export etableras (Fas 2+) supersedas denna
ADR av en uppföljare som migrerar `lib/dto/*.ts` till generated.
Manuella schemas är *kompatibelt mellansteg* — formen `z.infer` ↔ generated
Zod är samma yttre API ut mot konsumenter.

### Variant C — Hand-rullade boolean-type-guards (`isCurrentUser`, etc.)

Utöka befintligt `isPagedResult`-mönster manuellt per DTO.

**Avvisad.** Grund:

- Bryter mot *Parse, don't validate* (Alexis King 2019): boolean-guards
  laundrar typer utan att eliminera `unknown` — `if (isFoo(x))` ger
  TypeScript-bevis men ingen runtime-bevis utan rekursiv field-by-field-check
- Hand-skriven rekursiv guard för `ResumeDetailDto`
  (versions → content → experiences/educations/skills + DateOnly +
  DateTimeOffset) är mer underhållsbörda än motsvarande Zod-schema
- Fel-feedback ("shape fel") är otillräcklig vid debugging — Zod ger
  `path: ["versions", 0, "content", "experiences", 2, "startDate"]`
- Skulle inte fångat original-buggen (`roles` saknad nyckel → tom array) utan
  uttrycklig kontroll, vilket Zod ger automatiskt med `z.array(z.string())`

### Variant D — `io-ts` eller `runtypes`

**Avvisad utan djup-analys.** Zod är redan i stacken (Server Actions), har
störst ekosystem, och `z.infer` ger arbets-flödet single-source-of-truth.
Ingen anledning att lägga till parallell bibliotek-yta för samma jobb.

## Konsekvenser

### Positiva

- **Anti-corruption layer aktiv vid varje DTO-gräns** — runtime-bevis ersätter
  cast-baserade påståenden (defense in depth ortogonalt mot backend-validering)
- **Single source of truth för shape** — `z.infer` eliminerar TS-interface +
  Zod-schema-duplikation, en plats att uppdatera vid backend-ändring
- **Strukturerad fel-feedback vid mismatch** — Zod `issues` ger path + expected
  + received, mycket bättre debug-yta än "shape fel"
- **DRY mot Server Actions** — samma bibliotek/mönster på input-sidan
  (`lib/actions/*-schemas.ts`) och respons-sidan (`lib/dto/*.ts`)
- **Migrerbar mot OpenAPI-codegen** — `lib/dto/*.ts`-API är stabilt mot
  generated-files-supersession

### Negativa

- **Manuell synk vid backend-shape-ändring.** Backend-DTO ändras → frontend-
  schema måste uppdateras manuellt. Mitigerat av:
  - Schema-tester (happy path + mismatch) som fångar driftsekvenser
  - Variant B-supersession när OpenAPI-pipeline etablerad
- **Bundle-storlek.** Zod redan i client-bundle (Server Actions) — marginalkostnad ≈ 0.
- **Runtime-overhead per response.** Mätbart för stora shapes (ResumeDetailDto
  med nested arrays). Acceptabelt för Fas 1-trafikvolym. Vid prestanda-problem
  i Fas 2+: överväg `.passthrough()` eller schema-caching.

### Neutrala

- `parseResponse` etablerar en konvention som måste användas konsekvent.
  ESLint-regel eller arch-test för att bevaka att `res.json()` aldrig
  castas direkt övervägs men inte beslutad här.

## Migration

### Steg 1 — `parseResponse` + första domän

ADR + helper + `lib/dto/me.ts` skrivs först. `lib/auth/session.ts` och
`lib/api/me.ts` refactoras som första call-sites. Detta bevisar mönstret.

### Steg 2 — Övriga domäner

`lib/dto/applications.ts`, `lib/dto/resumes.ts`, `lib/dto/admin.ts` skrivs
parallellt. Respektive `lib/api/*.ts` refactoras till `parseResponse`.

### Steg 3 — `lib/types/*.ts`

Existerande filer blir tunna re-exports från `lib/dto/*.ts` (för minsta
diff på konsumenter). Filer raderas helt om diff-stöd inte längre behövs.

### Steg 4 — `isPagedResult` retire

`lib/types/paged.ts` exporten `isPagedResult` raderas när inga call-sites
återstår. `pagedResult(itemSchema)` från `lib/dto/_helpers.ts` är ersättaren.

### Acceptanskriterier

- 6 unchecked casts i TD-7-inventering ersatta med `parseResponse`
- 0 förekomster av `as Dto`-mönstret i `lib/api/*.ts` och `lib/auth/`
- `lib/types/*.ts` antingen raderade eller re-exporterar från `lib/dto/*.ts`
- Unit-tests för varje schema (happy + minst en mismatch)
- `pnpm typecheck` grönt utan nya regressioner

## Referenser

- Alexis King, ["Parse, don't validate"](https://lexi-lambda.github.io/blog/2019/11/05/parse-don-t-validate/) (2019)
- Eric Evans, *Domain-Driven Design* (2003) kap. 14 (Anti-corruption Layer)
- Robert C. Martin, *Clean Architecture* (2017) kap. 7 (SRP), kap. 11 (DIP)
- Hunt/Thomas, *The Pragmatic Programmer* (1999) DRY + YAGNI
- NIST SP 800-160 vol. 1 (defense in depth)
- Zod docs — https://zod.dev (v4.x)
- TD-7 (closes), TD-55 (replaces `isPagedResult`)
- ADR 0017 (frontend auth — call-site i scope)

## Validation

- 6 enhetstest minimum (en happy + en mismatch per domän)
- code-reviewer + security-auditor invokeras vid TD-7-implementation
- Framtida regression-skydd: arch-test eller ESLint-regel för
  `res.json() as ...`-mönstret (beslutas separat)

## Out of scope

- Backend OpenAPI-export — beslutas separat när Variant B aktualiseras
- Migration av Zod input-schemas (Server Actions) — orörda; mönstret matchar
- Tema-stöd för fel-meddelanden (toaster, copy-strings) — UI-detalj utanför ACL
- Performance-optimering av schema-parse — Fas 2+ vid mätbar regress
