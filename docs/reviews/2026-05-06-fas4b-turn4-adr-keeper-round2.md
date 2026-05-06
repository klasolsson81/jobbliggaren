# adr-keeper — Round 2 rapport Turn 4

**Datum:** 2026-05-06

## Findings from Round 1 — resolution status

- NB-4 (audit EventIds): RESOLVED
- NB-5 (Active-sessions UI): RESOLVED
- NB-6a (Deprecated Endpoints): RESOLVED
- NB-6b (Performance Budget): RESOLVED

## Detail per finding

### NB-4 — Auth Audit Logging (RESOLVED)
ADR 0017 lines 92–128 contain `## Auth Audit Logging` with:
- EventId table listing 1001 (`LoginSucceeded`), 1002 (`LoginFailed`),
  1003 (`LogoutSucceeded`) with Level and Trigger columns.
- Structured event shape: `UserId`, `SessionIdPrefix`, `Ip`, `UserAgent`
  for success events; `EmailHash` (SHA-256) instead of `UserId` for
  `LoginFailed` (PII-safe).
- Fas 0 destination: `ILogger<AuthAuditLogger>` → Serilog → Seq/CloudWatch,
  with `[LoggerMessage]` zero-allocation pattern noted.
- Fas 1 upgrade path: Postgres `auth_audit_log` table (append-only, 90-day
  retention), GDPR subject-access request support.

### NB-5 — Active-sessions UI in Out of Scope (RESOLVED)
ADR 0017 lines 160–165: the "Secondary user-sessions index" bullet now
explicitly includes "Active-sessions UI (list all open sessions per user,
revoke individually — requires secondary index as read model)". The bullet
also notes the dependency on account-deletion flow and synchronous
invalidation requirement.

### NB-6a — Deprecated Endpoints section (RESOLVED)
ADR 0017 lines 171–187 contain `## Deprecated Endpoints` with subsection
`### POST /auth/refresh — 410 Gone (Turn 4)` and four bullet points:
1. `POST /api/v1/auth/refresh` returns 410 immediately.
2. Response body is a `ProblemDetails` with Swedish message referencing
   `/auth/login` and ADR 0017.
3. Obsolete infrastructure remains under `[Obsolete(DiagnosticId =
   "JOBBPILOT0001")]` until Fas 1.
4. Deletion of the endpoint and all JWT infrastructure will occur in Fas 1.

### NB-6b — Performance Budget section (RESOLVED)
ADR 0017 lines 189–204 contain `## Performance Budget` with a two-column
table (Target vs Measured) for `ISessionStore.GetAsync` (p99 < 5 ms target;
p50 1,88 ms · p99 2,42 ms measured on Docker Redis 2026-05-06). CI guard
noted: assertion is p99 < 50 ms with rationale for the 10× multiplier and
test method reference.

### ADR 0018 — Backend trust model (UNCHANGED, CONFIRMED)
`### Backend trust model` subsection is present at ADR 0018 lines 44–63,
unchanged from Round 1. Content intact: topology invariant, CSRF analysis,
Origin/Referer responsibility delegation to Next.js layer.

## New findings (if any)

Inga nya findings.

The only minor observation (not a new finding, not a block) is that
`ISessionStore.CreateAsync` row in the Performance Budget table shows
"not measured" for both Target and Measured. This is transparent and
intentional — the ADR does not claim to have measured it. Acceptable for
Fas 0; a follow-up measurement would be appropriate before Fas 1 ships.

## Section ordering

CORRECT. ADR 0017 sections appear in the required order:
1. `## Log and Audit Policy` (line 73)
2. `## Auth Audit Logging` (line 92)
3. `## Consequences` (line 129)
4. `## Out of Scope (Deferred)` (line 151)
5. `## Deprecated Endpoints` (line 171)
6. `## Performance Budget` (line 189)
7. `## References` (line 206)

## Verdict

APPROVE

All four Round 1 non-blocking findings (NB-4, NB-5, NB-6a, NB-6b) are
resolved. ADR 0018 is unchanged and correct. Section ordering is correct.
No new issues introduced. ADR 0017 and ADR 0018 are ready to remain as
Accepted.
