# Security-audit: product-rename JobbPilot → Jobbliggaren (PR `refactor/rename-jobbliggaren`)

**Datum:** 2026-06-13
**Scope:** Light/optional pass per ADR 0069 gate table — strictly a rename that
grazes auth identifiers (JWT `Audience`, DB roles). Pre-launch, localhost-only,
no issued tokens, not a hard gate.
**HEAD:** `e6b97ed` · **Base:** `main` = `3f33474`
**Auktoritet:** ADR 0069 (D2/D3), ADR 0017 (session-auth supersedes JWT bearer),
ADR 0066 (AWS retired), CLAUDE.md §5 (secrets), GDPR Art. 5/32 (no PII change).

**Status:** ✓ Approved (PASS) — no Blocker, no Major, no Minor.

---

## 1. JWT Audience consistency (issuance ↔ validation)

`appsettings.Development.json:14` renames `Audience` to `jobbliggaren-api`.
Issuance side (`JwtTokenGenerator.cs:51`) reads `_settings.Audience` from
`IOptions<JwtSettings>` bound to config section `"Jwt"` (`JwtSettings.cs:11`),
so issuance consumes the renamed value.

**Validation side: there is none to split against.** A full-codebase grep for
`ValidAudience` / `ValidateAudience` / `TokenValidationParameters` / `AddJwtBearer`
returns **zero hits in living code** — the only matches are in historical ADRs
(0012, 0029) and prior review docs. JWT *bearer* validation was replaced by an
opaque session model in STEG 4b (ADR 0017 / 0029): `Program.cs:51` registers
`AddAuthentication("Bearer", SessionAuthenticationHandler)`, whose handler does
`ISessionStore.GetAsync()` — it does not parse or validate a JWT audience.

`JwtSettings` / `JwtTokenGenerator` are both `[Obsolete]` (DiagnosticId
`JOBBLIGGAREN0001`, "Raderas i Fas 1"), retained only for `RefreshCommandHandler`.
The renamed audience is therefore a **vestigial claim** stamped into an
access-token whose audience is never validated as a bearer audience. No
issuance/validation split is structurally possible. Pre-launch, no tokens
issued → no rollover needed.

**Surviving `jobbpilot-api` literals** are all in correct locations:
`docs/decisions/0069-…md` (the ADR text), `docs/decisions/README.md`,
`docs/reviews/2026-06-13-rename-jobbliggaren-cto.md` (descriptive/historical),
and `infra/terraform/modules/ecr/variables.tf` (D3-excluded AWS Terraform).
No living config still expects the old audience.

**Verdict:** consistent, no split. ✓

## 2. DB roles / connection strings (no half-rename)

The actual load-bearing literals all agree on `jobbliggaren`:

- `Roles.cs:8-10` — SQL role constants `jobbliggaren_migrations` /
  `jobbliggaren_app` / `jobbliggaren_worker` (consumed by every `CREATE/ALTER
  ROLE`, `GRANT`, and connection-string build in `Migrate/Program.cs`).
- `appsettings.Development.json:9` — `Database=jobbliggaren;Username=jobbliggaren`.
- `docker-compose.yml` — `name: jobbliggaren`, `POSTGRES_DB`/`POSTGRES_USER:
  jobbliggaren`, healthcheck `pg_isready -U jobbliggaren -d jobbliggaren`,
  volumes/containers `jobbliggaren_*`; test profile `jobbliggaren_test`.
- `Worker/appsettings.Production.json:4` + `Migrate/Program.cs` comments —
  Secrets-Manager paths `jobbliggaren/prod/postgres-app|worker`,
  `jobbliggaren/dev/db/*`.

No mismatch between role literal, connection-string DB/user, and provisioning
DDL → no half-rename that could break role auth or DB connection.

**D3-excluded files correctly left untouched** (verified absent from the diff,
present in the tree): `.github/workflows/deploy-dev.yml` and all
`infra/terraform/**`. Their retired-AWS secret-path strings (`jobbpilot_admin`,
`Database=jobbpilot`, `jobbpilot_app|_worker|_migrations`) intentionally keep
the old brand — they die in the separate AWS-teardown PR (ADR 0066 / TD-104),
not here. Historical docs (`tech-debt.md`, `steg-tracker.md`, `hangfire-schema.md`,
session logs) likewise retain `jobbpilot_*`: correct, they are dated records of
the retired provisioning model.

*Context note (not a finding):* `Migrate/Program.cs` targets retired AWS
infra (`AmazonSecretsManagerClient`, RDS, ECS). ADR 0069 D2 explicitly scopes
its provisioning comments into the rename, so renaming the strings here is
correct per the ADR. Whether the Migrate project is deleted with the AWS
teardown is TD-104/D3 territory, not this PR and not a security matter.

**Verdict:** consistent. ✓

## 3. Secret leakage

- No plaintext secret introduced. Dev DB passwords stay `${POSTGRES_PASSWORD_DEV}`
  / `${POSTGRES_PASSWORD_TEST}` env-placeholders; prod connection strings remain
  injected via env-vars + Secrets Manager (`Worker/appsettings.Production.json:2-5`,
  `Api/appsettings.Production.json:2-3`).
- `.env.example` carries only `change-me` placeholders + empty `REDIS_PASSWORD_DEV`,
  with explicit "Committa aldrig verkliga lösenord" guidance. No real secret.
- `Migrate/Program.cs` secret-handling unchanged: generated role passwords use
  `RandomNumberGenerator` over `[A-Za-z0-9]`, logged only as SHA256-truncate
  fingerprints (`Fingerprint`, line 507-511) — 0 % plaintext bytes. Untouched
  by the rename beyond identifier strings.
- **`.gitleaksignore` NOT altered** by this PR (empty diff against base) —
  historical commit/path fingerprints correctly preserved. Klas runs gitleaks
  pre-push as the executable gate.

**Verdict:** no leakage. ✓

## 4. GDPR

Pure namespace/brand rename. No new or changed PII column, no PII in logs, no
retention/consent/sub-processor change, no AI-inference path touched, no
auth-flow behavioural change (audience claim is non-validated). No DPIA-relevant
surface. GDPR posture unchanged.

**Verdict:** no GDPR impact. ✓

---

## Praise

- Auth model is opaque session (`ISessionStore`), so the audience rename is
  inherently low-risk — there is no JWT validator to fall out of sync. ✓
- Role names centralised in a `Roles` constant class → the rename is a single
  source-of-truth edit, not 30 scattered string literals that could drift. ✓
- `.gitleaksignore` and the D3-excluded AWS/Terraform paths were left untouched
  exactly as the ADR prescribes — disciplined scope boundary. ✓
- Prod secrets remain env/Secrets-Manager-injected; no overlay regressed to a
  committed plaintext value during the sweep. ✓

## Sammanfattning

0 Blockers, 0 Major, 0 Minor. JWT audience issuance is consistent and has no
validation counterpart to split (session-auth per ADR 0017). DB role literals,
connection strings, container/volume names, and Secrets-Manager paths all agree
on `jobbliggaren` — no half-rename. D3-excluded files and `.gitleaksignore`
correctly untouched. No plaintext secret introduced. No GDPR impact.
**PASS — no re-review required.** Klas runs gitleaks pre-push as planned.
