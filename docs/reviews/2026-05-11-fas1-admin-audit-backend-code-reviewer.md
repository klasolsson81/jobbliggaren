# Code-review: Fas 1 — admin-audit + roll-claim + admin-seeder

**Status:** APPROVE-WITH-FIXES (alla fynd är Minor/Nit — gate öppen för push)
**Granskat:** 2026-05-11
**Auktoritet:** CLAUDE.md §2.1–2.4, §3.5, §3.6, §5.1, §5.4 + ADR 0008, ADR 0022
**Scope:** Backend — Application + Infrastructure + Api + tester

## Verdict-sammanfattning

| Severity | Antal |
|---|---|
| Blocker | 0 |
| Major | 0 |
| Minor | 4 |
| Nit | 3 |

## Svar på gate-frågor

1. **Clean Arch-disciplin:** OK. Domain inga nya beroenden. Application använder IAppDbContext direkt (ADR 0009).
2. **DDD/CQRS:** OK. PagedResult<T> är förbättring över GetApplicationsQuery:s shape.
3. **Pipeline-ordning:** OK. Auth→AdminAuth→UoW→Audit korrekt motiverat. ADR 0008 saknar uppdatering (M3).
4. **DRY/SRP:** OK. IsInRole är tunn wrapper för Application-abstraktion.
5. **CLAUDE.md anti-patterns:** OK med M2 (email-logging).
6. **Test-coverage:** M4 (saknade unit-tester).
7. **Roll-claim-säkerhet:** OK. Per-request fetch, ingen Redis-läckage.
8. **Seeder-resilience:** OK som kompromiss.

## Minors

### M1: CancellationToken-disciplin i IdempotentAdminRoleSeeder
**Fil:** `IdempotentAdminRoleSeeder.cs:67,92`
Identity-API saknar ct-stöd. Lägg `ct.ThrowIfCancellationRequested()` vid metod-start för cooperative cancellation under host-shutdown.
**Motivering:** CLAUDE.md §3.5.

### M2: LogAdminAssigned loggar email på Info — bör vara Debug eller UserId
**Fil:** `IdempotentAdminRoleSeeder.cs:123–125`
Email i klartext på Info-level. Föreslå: byt till UserId i meddelandet, eller sänk till Debug.
**Motivering:** CLAUDE.md §5.1 + §5.4 PII-disciplin.

### M3: ADR 0008 nämner inte AdminAuthorizationBehavior eller AuditBehavior
**Fil:** `docs/decisions/0008-pipeline-behavior-order.md`
ADR 0008 listar 4 behaviors, faktisk pipeline har 6. Delegera till adr-keeper. Separat docs-commit.
**Motivering:** CLAUDE.md §9 DoD punkt 9 + §1.6.

### M4: Saknade unit-tester
**Saknas:**
- `AdminAuthorizationBehaviorTests.cs` (3 fall: IsInRole=true → next; IsInRole=false → ForbiddenException; icke-IAdminRequest → next oavsett)
- `GetAuditLogEntriesQueryHandlerTests.cs` (filter-permutationer, paginering)

Delegera till test-writer. Skattning ~45 min — in-scope per 4h-regel.
**Motivering:** CLAUDE.md §2.4.

## Nits

### N1: Seeder 42P01-catch är test-specifik kompromiss
Inget att fixa nu. Flagga som potentiell rensning vid Fas 2 test-infrastruktur-revision.

### N2: PagedResult.TotalPages returnerar 0 vid PageSize=0
Defensivt; pageSize=0 ska aldrig nå handlern (validator). Behåll.

### N3: AdminEndpoints.AdminPolicy = Roles.Admin är mikro-alias
Kan tas bort om man vill — eller behåll för framtida policy-utveckling.

## Praise

1. `IAdminRequest : IAuthenticatedRequest`-arvet inkodar 401-vs-403 i typsystemet.
2. `Roles.Admin`-konstant refererad från 4 lager — magic-string brutet.
3. Architecture-test uppdaterat samtidigt med pipeline-tillägget.
4. Roll-revoke-immediacy-testet bevisar A1-invarianten end-to-end.
5. Validator DOS-skyddar EventType/AggregateType med MaxLength(100).
6. PagedResult<T> generisk + immutable, matchar CLAUDE.md §3.3 + §3.6.
7. Seeder race-condition-medveten (RoleExistsAsync-recheck efter CreateAsync).

## Sammanfattning

Push-gate: ÖPPEN.

**Rekommendation:**
- **In-block (≈75 min):** M1 (5 min), M2 (5 min), M4 (45 min)
- **Separat docs-commit:** M3 (adr-keeper)
- **Nits:** ignoreras eller väntelista
