# Architect-review: Fas 1 admin-audit + roll-claim + admin-seeder

**Status:** APPROVE-WITH-FIXES
**Granskat:** 2026-05-11
**Auktoritet:** CLAUDE.md §2.1–2.4, §3.5, §3.6, ADR 0008, ADR 0009, ADR 0017, ADR 0022

## Verdict-sammanfattning

| Severity | Antal |
|---|---|
| Blocker | 0 |
| Viktigt | 2 |
| Mindre | 7 |

## Svar på gate-frågor

1. **Clean Arch-gränser:** Korrekt. Inga ASP.NET-namespaces i Application/Domain.
2. **Pipeline-disciplin:** OK. ADR 0008 saknar uppdatering (M3).
3. **DDD `Admin/Queries/`-folder:** Acceptabel — Admin är konsument-perspektiv, inte bounded context. Domain-entiteter förblir i sina egna namespaces.
4. **Marker-interface-mönster:** Stå för det. Typsäkert, compile-time-verifierat, komponerbart med IAuditableCommand etc.
5. **PagedResult<T>:** Behåll. GetApplicationsQuery har en paginerings-bug (saknar totalCount) — separat TD (M4).
6. **SessionAuthenticationHandler → IUserAccountService:** OK. Infrastructure→Application-port är rätt riktning.
7. **IHostedService-seeder + 42P01-catch:** Acceptabel pragmatik. Dokumentera trade-off.
8. **Test-täckning:** Lägg till AdminAuthorizationBehavior-unit-test (krävs för defense-in-depth-invariant) + handler-test (önskvärt). Seeder-tests inte värt det.

## Viktigt-fynd

### Viktigt #1: AdminPolicy = Roles.Admin är false equivalence

**Fil:** `AdminEndpoints.cs:9`

Policy-namn ska vara separat konstant från roll-namn. Idag fungerar bara för att båda är "Admin".

**Föreslagen åtgärd:**
```csharp
// Application/Common/Authorization/AuthorizationPolicies.cs
public static class AuthorizationPolicies
{
    public const string Admin = "Admin";
}
// Program.cs: AddPolicy(AuthorizationPolicies.Admin, p => p.RequireRole(Roles.Admin))
// AdminEndpoints: .RequireAuthorization(AuthorizationPolicies.Admin)
```

Skattning: 5 min.

### Viktigt #2: Saknad ApplicationLayerTests architecture-test

**Fil:** Saknas: `tests/JobbPilot.Architecture.Tests/ApplicationLayerTests.cs`

Inget arkitekturtest verifierar att Application inte beror på ASP.NET-namespaces. Med växande `IAdminRequest`/`ICurrentUser.IsInRole`/`IRequestContextProvider`-abstraktioner är risken för läckage stigande.

**Föreslagen åtgärd:** Lägg till tester:
- `Application_should_not_depend_on_AspNetCore_or_Infrastructure`
- `Domain_should_not_depend_on_anything_except_BCL_and_SmartEnum`

Använder NetArchTest (`Types.InAssembly(...).ShouldNot().HaveDependencyOnAny(...)`).

Skattning: 15 min.

## Mindre-fynd

### M1: AdminAuthorizationBehavior-unit-test saknas
**Saknas:** `tests/JobbPilot.Application.UnitTests/Common/Behaviors/AdminAuthorizationBehaviorTests.cs`

3 fall: non-admin-request passes through, admin-request utan roll → ForbiddenException, admin-request med roll → passes through.

Bevisar Worker-bypass-invarianten som integration-tester inte når.

### M2: GetAuditLogEntriesQueryHandlerTests saknas
**Saknas:** `tests/JobbPilot.Application.UnitTests/Admin/Queries/GetAuditLogEntries/...Tests.cs`

Filter-permutationer, paginering, whitespace-edge-cases. Snabbare än integration.

### M3: ADR 0008 saknar amendment
ADR 0008 listar 4 behaviors, faktisk pipeline har 6. Skriv ny ADR 0027 eller utöka 0008.

### M4: GetApplicationsQuery exponerar inte totalCount
Separat TD. PagedResult<T> kan retro-fittas vid Fas 1.5-housekeeping.

### M5: Lokal-variabel-tilldelning i handler utan kommentar
**Fil:** `GetAuditLogEntriesQueryHandler.cs:32-34, 38-40`
Kommentar: `// EF Core 10: lokal kopia undviker nullable-closure-issue`.

### M6: Per-request roll-fetch saknar metrics-baseline
**Fil:** `SessionAuthenticationHandler.cs:82`
Lägg till `auth.role_fetch.duration_ms`-counter för Fas 2-volym-monitoring.

### M7: PagedResult.TotalPages returnerar 0 vid PageSize=0
**Fil:** `PagedResult.cs:13`
Defensivt — kasta `InvalidOperationException` om PageSize<=0.

## Verdict-detalj

**APPROVE-WITH-FIXES.** Release-kandidaten är arkitektoniskt sund.

**Två fixes innan merge:**
1. Viktigt #1 (Admin-policy-konstant) — 5 min
2. Viktigt #2 (ApplicationLayerTests) — 15 min

**Mindre (post-merge eller TD):**
- M1 (behavior-unit-test) — addera vid Fas 1-stängning
- M2, M4, M5, M6, M7 — TD eller housekeeping
- M3 (ADR amendment) — separat docs-commit
