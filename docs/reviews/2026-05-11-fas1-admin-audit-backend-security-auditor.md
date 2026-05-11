# Security-audit: Fas 1-stängningen — admin-audit-vy + roll-claim-flow + admin-seeder

**Status:** Approved med 0 Blockers, 0 Sec-Major, 5 Sec-Minor (defense-in-depth / hardening / dokumentation)
**Granskat:** 2026-05-11
**Auktoritet:** CLAUDE.md §GDPR + §5.4, GDPR Art. 5/6/17/32, ADR 0008/0012/0013/0017/0018/0022/0024

## Verdict

Backend-ändringarna håller säkerhetsmässig nivå för Fas 1-stängning. Inga GDPR-brott. Inga auth-bypass-vektorer. Roll-claim-flow A1 (per-request fetch) implementerad korrekt — roll-revoke verkar omedelbart, ingen claim-cache i Redis-session. Defense-in-depth via `AdminAuthorizationBehavior` är på plats och Worker-isolation via `WorkerSystemUser.IsInRole => false` blockerar Admin-commands från system-jobb. Audit-skrivningen lägger inga nya PII-fält till den ytan, och admin-läs-vyn returnerar samma /24-anonymiserade IP + 256-char-trunc UA som redan skrivs i `audit_log` per ADR 0024 D7.

---

## Svar på frågorna i delegations-noten

**1. PII-exponering i admin-vyn — ingen ny GDPR-yta.**

`AuditLogEntryDto` returnerar redan minimerade fält:
- `IpAddress` är /24-maskad pseudonym per ADR 0024 D7
- `UserAgent` trunkerad till 256 chars
- `UserId` (Guid) är pseudonymt — inte rå PII
- `ImpersonatedBy` är null i Fas 1

Per Breyer (C-582/14) är /24-maskad IP en pseudonym, inte ren personuppgift. Admin-rollen i Fas 1 = bara Klas → minimal access-yta, GDPR Art. 32 uppfylld.

**2. Roll-claim-flow A1 — implementation OK.**

- **Timing-side-channel:** ingen exploaterbar yta. Samma DB-path för Admin/icke-Admin.
- **Exception under role-fetch:** bubblar idag → 500. Renare semantik: `AuthenticateResult.Fail` → 401. **Sec-Minor-2.**
- **Roll-string-injection:** ej möjligt. Roll-strängar är hårdkodade konstanter.

**3. Admin-seeding-säkerhet — acceptabel risk för Fas 1.**

- Default `""` i dev/test. Prod-vägen "AWS Secrets Manager → ECS task-def" är intention, inte verifierad i kod. **Sec-Minor-1.**
- Privilege-eskalering via env-var-injection kräver compound-attack (IAM + befintligt konto) — mitigerat av KMS/IAM i prod.
- Race-condition korrekt hanterad via `RoleExistsAsync`-recheck efter `CreateAsync`-fel.

**4. 403 vs 401-disclosure — semantik-korrekt.**

RFC 7235/9110-korrekt. Att returnera 404 för admin-yta är security-through-obscurity (OWASP rekommenderar inte). Exception-middleware-ordning verifierad: Validation → 400, Unauthorized → 401, **Forbidden → 403**, NotFound → 404, SessionStoreUnavailable → 503.

**5. AdminAuthorizationBehavior defense-in-depth — korrekt isolation.**

`WorkerSystemUser.IsInRole => false` är hårdkodad. `IAdminRequest : IAuthenticatedRequest`-ärvning ger 401-före-403-ordning. Pipeline-ordning per ADR 0008+0022 verifierad.

**6. Audit-skrivning för admin-läsning — korrekt val per ADR 0022.**

GET är inte mutation. **Sec-Minor-3:** Fas 6 kan utöka audit till admin-läs-yta (GDPR Art. 30 — record of processing).

**7. Rate-limiting på admin-endpoint — Sec-Minor-4.**

Ingen specifik policy. För Fas 1 (en admin = Klas) ingen praktisk DoS. För Fas 6 bör `AdminLooseRateLimit` införas.

**8. HSTS / forwarded-headers — ingen påverkan.**

---

## Sec-Minors

### Sec-Minor-1: Prod-konfig-källa för `AdminBootstrap__InitialAdminEmail` ska dokumenteras

**Fil:** `IdempotentAdminRoleSeeder.cs` + `AdminBootstrapOptions.cs`

Risk: future-Klas eller framtida medarbetare sätter värdet i `appsettings.json` och commit:ar email till git.

**Åtgärd:** Kommentar i `AdminBootstrapOptions.cs` + dokumentera i `docs/runbooks/aws-setup.md`. Delegera till docs-keeper.

### Sec-Minor-2: Role-fetch-exception bör fail:a som AuthenticateResult.Fail

**Fil:** `SessionAuthenticationHandler.cs:82-84`

Idag: `GetRolesAsync`-exception bubblar till 500. Renare: try/catch som returnerar `AuthenticateResult.Fail("Role resolution failed")` → 401.

**Åtgärd:** Inte STEG-blocker. Hardening-pass eller in-block om scope ≤30 min.

### Sec-Minor-3: Admin-läs-aktioner bör audit-loggas i Fas 6

**Auktoritet:** GDPR Art. 30. När impersonation/admin-suspend införs (Fas 6) bör `IAuditableQuery`-mönster införas för admin-läs-yta.

**Åtgärd:** TD vid Fas 6 ADR-extension.

### Sec-Minor-4: Admin-endpoint saknar dedikerad rate-limit-policy

**Fil:** `AdminEndpoints.cs`

**Åtgärd:** TD vid Fas 6 admin-yta-utbyggnad.

### Sec-Minor-5: `AdminPolicy`-konstant överlappar `Roles.Admin`

**Fil:** `AdminEndpoints.cs:9` — `AdminPolicy = Roles.Admin` (samma strängvärde).

**Åtgärd:** Cosmetic. Stilfråga.

---

## Praise

- **Per-request roll-fetch (A1):** roll-revoke verkar omedelbart, verifierat av `GetAuditLog_AfterRoleRevoke_Returns403OnNextRequest`-test.
- **Defense-in-depth:** HTTP-policy + pipeline-behavior + `WorkerSystemUser.IsInRole => false`.
- **`IAdminRequest : IAuthenticatedRequest`:** 401-före-403-ordning korrekt.
- **DTO speglar tabellen 1:1:** ingen ny PII-yta.
- **Validator:** EventType/AggregateType max 100 chars, PageSize max 200. DOS via LIKE-fråga eliminerad.
- **`AsNoTracking()` + projection:** minimal EF-yta.
- **Bootstrap-seeder fail-loud** förutom 42P01: korrekt säkerhets-default.
- **`Roles.Admin`-konstant:** bryter magic-string-anti-pattern.
- **7 integration-tester** inkl roll-revoke-immediacy.

---

## Sammanfattning

**Backend Fas 1-stängningen är godkänd ur säkerhets- och GDPR-perspektiv.** Inga blockers, inga sec-majors. Fem sec-minors är defense-in-depth / TD / dokumentation och blockerar inte merge.

**Delegera vid Fas 1-stängningen:**
- docs-keeper: Sec-Minor-1, -3, -4 (TD-poster + runbook-update)
- dotnet-architect: Sec-Minor-2 (hardening-pass, inte STEG-blocker)

**Re-review krävs inte.** Direct-push till `main` per ADR 0019 efter Klas:s GO.
