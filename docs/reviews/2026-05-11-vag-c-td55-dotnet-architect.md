# dotnet-architect review — Väg C / TD-55 Block A

**Datum:** 2026-05-11
**Reviewer:** dotnet-architect (Claude subagent)
**Scope:** Block A — TD-55 PagedResult retro-fit, två commits `c2f539e` (backend) + `0b0886d` (frontend) på `main` lokalt
**Verdict:** **APPROVE-WITH-FIXES** — 0 blockers, 3 Minor, 2 Nit

---

## Sammanfattning

Refaktoreringen är väl utförd och låser ett verkligt typ-skew-problem mellan
backend och frontend. Designvalen (separat count-query, immutable PagedResult,
type-guard på frontend, hard cap på ListJobAds) ligger i linje med CLAUDE.md
§2.1, §2.3, §3.6 och §4.1. Tre minor + två nit-fynd noterade, varav
`PagedResultContractTests`-heuristiken är svag men inte felaktig.

## Fynd

### [Minor 1] PagedResultContractTests.HasPagedSemantics missar audit-query

**Var:** `tests/JobbPilot.Architecture.Tests/PagedResultContractTests.cs:62-68`

`HasPagedSemantics` letar endast efter `PageNumber` + `PageSize`. Det finns en
avvikare i kodbasen: `GetAuditLogEntriesQuery` använder property-namnet `Page`
(inte `PageNumber`). Heuristiken missar audit-querien helt.

**Funktionellt:** benign idag — `GetAuditLogEntriesQuery` returnerar redan
`PagedResult<AuditLogEntryDto>`. Men kontraktet skyddar inte mot regression där.

**Varför:** Architecture-tests ska vara robusta och false-negative-fria.
CLAUDE.md §7 säger explicit "Architecture tests gröna" som DoD-krav.

**Föreslagen åtgärd:** Utöka heuristiken till `"PageNumber" or "Page"`. Eller
normalisera audit-query till `PageNumber`. Min rek: utöka heuristiken så
befintliga wire-shapes bevaras.

**Status:** Fixad in-block. Heuristiken accepterar nu både `Page` och `PageNumber`.

### [Minor 2] Frontend type-guard-duplikering

**Var:** `web/jobbpilot-web/src/lib/api/applications.ts:17-26` och
`web/jobbpilot-web/src/lib/api/resumes.ts:16-25`

Två i princip identiska type-guards (`isPagedApplications`, `isPagedResumes`)
som skiljer sig endast på TypeScript-genericen i returtypen. Mild kod-duplikering
som reproduceras varje gång en ny paginerad endpoint läggs till.

**Varför:** TD-55 stänger ett typ-skew-problem som inträffade just för att det
inte fanns en standardiserad paged-konsumtions-pattern. Två handskrivna
implementationer på rad är embryot till samma problem.

**Föreslagen åtgärd:** Extrahera generisk `isPagedResult<T>` i
`lib/types/paged.ts` med opt-in `isItem`-validering.

**Status:** Fixad in-block. Ny fil `lib/types/paged.ts` med generisk
`isPagedResult<T>`. Båda api-moduler använder den.

### [Minor 3] MaxItems-konstant hårdkodad

**Var:** `src/JobbPilot.Application/JobAds/Queries/ListJobAds/ListJobAdsQueryHandler.cs:13`

`private const int MaxItems = 500;` ligger i handlern. Värdet är operationellt
knob — beror på DB-storlek, latency-budget och frontend-renderingskapacitet.

**Varför:** CLAUDE.md §5.1: "Konfiguration hårdkodad — allt via `IOptions<T>`".
Hard cap mot DoS-vektor är konfigurations-typ, inte domän-invariant.

**Föreslagen åtgärd:** Pragmatiskt (idag): lämna const + TODO-kommentar.
Värdet försvinner när Fas 2 introducerar full paginering ändå. Inte
blockerande fynd.

**Status:** Fixad in-block. TODO-kommentar pekar mot TD-56 (Fas 2-paginering)
+ IOptions-pattern.

### [Nit 1] PagedResult-record primary-ctor-stil

**Var:** `src/JobbPilot.Application/Common/PagedResult.cs:14-25`

Manuell ctor + explicita properties. Idiomatisk C# 14-stil per CLAUDE.md §3.1
vore primary-ctor med validation-body. Funktionellt likvärdigt.

**Status:** Lämnas oförändrad. Stilval, ingen åtgärd.

### [Nit 2] Empty(query)-helper design

**Var:** `GetApplicationsQueryHandler.cs:58-59` och `GetResumesQueryHandler.cs:48-49`

Empty-helper returnerar `PagedResult<T>` med `query.PageSize` bibehållen.
Bekräftar designval — bibehåller wire-shape-konsistens. Samma mönster som
Stripe/GitHub använder för tomma paginerade resultat.

**Status:** Inget att ändra. Design-val bekräftat.

## Nice-to-have

**Wire-shape integration-test.** En API-integrations-test som asserterar
JSON-shape (`page`, `pageSize`, `totalCount`, `items`) skulle fånga regression
om någon byter `PagedResult<T>` till annan shape. Delvis täckt av befintliga
integration-tester (`GET_applications_with_auth_returns_200_with_paged_result`
+ motsvarande för resumes). Inget separat test behövs idag.

## Verifierat OK

- **CQRS-disciplin (§2.3):** `PagedResult<DTO>` returnerar DTOs, inte aggregates
- **Clean Arch (§2.1):** `PagedResult.cs` har noll deps utåt, ingen Infrastructure-läckage
- **EF Core-pattern (§3.6):** `baseQuery` delas mellan `CountAsync` och materialisering
  → samma WHERE-villkor. `AsNoTracking()` aktivt
- **PagedResult-kontrakt:** sealed record, IReadOnlyList exposed (inte List),
  argument-validering komplett. §3.3 immutability uppfyllt
- **Empty(query)-helper:** korrekt val mot wire-shape-konsistens
- **Architecture-test sanity:** `pagedQueryTypes.ShouldNotBeEmpty(...)`
  förhindrar tyst grön-status vid trasig reflection
- **Frontend type-guard:** `payload: unknown` + `isPagedResult` följer §4.1
  (no-`any`) korrekt

## Granskade filer

- `src/JobbPilot.Application/Common/PagedResult.cs`
- `src/JobbPilot.Application/Applications/Queries/GetApplications/GetApplicationsQuery.cs` + handler
- `src/JobbPilot.Application/Resumes/Queries/GetResumes/GetResumesQuery.cs` + handler
- `src/JobbPilot.Application/JobAds/Queries/ListJobAds/ListJobAdsQuery.cs` + handler
- `src/JobbPilot.Application/Admin/Queries/GetAuditLogEntries/GetAuditLogEntriesQuery.cs`
- `tests/JobbPilot.Architecture.Tests/PagedResultContractTests.cs`
- `tests/JobbPilot.Application.UnitTests/Applications/Queries/GetApplicationsQueryHandlerTests.cs`
- `tests/JobbPilot.Application.UnitTests/Resumes/Queries/GetResumesQueryHandlerTests.cs`
- `tests/JobbPilot.Application.UnitTests/JobAds/Queries/ListJobAds/ListJobAdsQueryHandlerTests.cs`
- `web/jobbpilot-web/src/lib/api/resumes.ts`
- `web/jobbpilot-web/src/lib/api/applications.ts`
- `web/jobbpilot-web/src/lib/types/resumes.ts`
- `web/jobbpilot-web/src/app/(app)/cv/page.tsx`

## Referenser

- CLAUDE.md §2.1 (Clean Architecture-gränser)
- CLAUDE.md §2.3 (CQRS — DTOs ut genom Application-gränsen)
- CLAUDE.md §3.3 (Immutability — IReadOnlyList)
- CLAUDE.md §3.6 (LINQ — separat count-query, AsNoTracking)
- CLAUDE.md §4.1 (no-`any`, type guards)
- CLAUDE.md §5.1 (hårdkodad konfiguration, IOptions-pattern)
- CLAUDE.md §7 (Architecture-tests som DoD-krav)
