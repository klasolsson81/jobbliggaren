# Runbook — Playwright E2E in CI (#796)

Operational reference for the full-stack Playwright end-to-end suite and its CI
workflow. Companion to [`local-dev-setup.md`](local-dev-setup.md) and
[`frontend-visual-verification.md`](frontend-visual-verification.md).

## What runs where

- **Workflow:** [`.github/workflows/e2e.yml`](../../.github/workflows/e2e.yml),
  triggers on `push`/`pull_request` to `main` + `workflow_dispatch`.
- **Specs:** `web/jobbliggaren-web/tests/e2e/*.spec.ts`
  (`applications`, `cv`, `delete-account`, `jobb`, `security-headers`, `auth`),
  single chromium project, serial (`workers: 1`).
- **Stack the job assembles:** ephemeral `postgres:18` + `redis:8-alpine` service
  containers → migrations via `dotnet ef database update` → the .NET Api on
  `:5049` (background, health-waited on `/api/ready`) → the Next.js dev server on
  `:3000` (started by Playwright's own `webServer`).

## Observe-only — and the ratchet rule

This workflow is **observe-only** (ADR 0045 Beslut 6): `continue-on-error: true`,
a **separate** workflow from `build.yml`, and **not** part of `build.yml`'s required
`ci` aggregate. A red E2E run **cannot block a merge**. This is deliberate for Fas 1
so the run distribution (flake rate, duration) can be observed first.

**Ratcheting to blocking** — making the workflow a required check, or folding it into
`ci.needs` — is a later, **explicit Klas decision**. Never make it blocking silently.

## The flag conflict and the confirmed-login seam (the core design)

`Auth:RequireEmailConfirmation` is host-startup-bound (env
`Auth__RequireEmailConfirmation`), with no per-request toggle:

- The `loginAs` specs (`applications`/`cv`/`delete-account`/`jobb`) need a
  **confirmed** user (they wait for `/mig`).
- `auth.spec.ts` (#791/#733) needs the flag **ON** and an **unconfirmed** user
  (register-202 panel + login-403 gate + resend).

CI runs **one** backend with the flag **ON** (launch-representative). To let the
`loginAs` specs get a confirmed user without a real email round-trip, a **dev-only,
Mediator-routed confirmed-login seam** exists:

- `POST /api/v1/dev/confirm-email` → `ConfirmEmailDevCommand` →
  `IDevEmailConfirmer.ForceConfirmByEmailAsync` (sets `EmailConfirmed = true`, no
  token). Returns 204 / 400 (empty) / 404 (unknown).
- **Two independent structural gates, both keyed on `IsDevelopment()`:** the endpoint
  is mapped only in Development (`Program.cs`), and `IDevEmailConfirmer` is
  DI-registered only in Development
  (`DependencyInjection.AddDevOnlyTestingSupport`). In any deployed environment the
  route 404s and the command's dependency cannot resolve (fail-closed).
- Guardrail: `ProductionStartupSmokeTests` asserts the whole `/api/v1/dev/*` group
  404s in Production — a **hard merge gate** (CLAUDE.md §12). **REMOVE BEFORE LAUNCH**
  alongside `reset-my-data`.

Helper wiring (`tests/e2e/helpers/auth.ts`): the `loginAs` specs seed via
`ensureConfirmedTestUser` (register → `confirmTestUser` → login). `auth.spec.ts`
keeps `ensureTestUser` (register-only, stays unconfirmed) — do **not** fold confirm
into `ensureTestUser`.

## Frontend: `pnpm dev`, not a prod build

The job runs the frontend via `pnpm dev` (Playwright's `webServer`), matching the
default config. Reason: `security-headers.spec.ts` asserts **dev-mode** CSP
directives; a production build (`pnpm build && pnpm start`) flips the CSP mode and
would break that spec. The `__Host-` session cookie works over `http://localhost`
because Chromium treats localhost as a secure context. Trade-off: Next dev compiles
each route on-demand on first hit, so `playwright.config.ts` gives CI extra headroom
(`timeout: 60s`, `webServer.timeout: 180s`) and `retries: 2` absorbs cold-compile
flake. If flake proves stubborn, revisit a prod build + a CSP-mode-aware
`security-headers.spec`.

## Secrets in CI (env-only, never committed)

- Field-encryption master key: `FieldEncryption__Provider=Local` +
  `FieldEncryption__LocalMasterKeyBase64=$(openssl rand -base64 32)` into
  `$GITHUB_ENV`.
- Throwaway RSA JWT keys generated per-run (`Jwt__PrivateKeyPath`/`__PublicKeyPath`).
- `Email__Provider=Console` (no outbound mail). Never depend on the gitignored
  `appsettings.Local.json` (absent in CI).

## Run the suite locally

```bash
# 1. Stack up (Postgres 5435 / Redis 6379 / Seq) + Api + Worker per local-dev-setup.md
# 2. Frontend must see the backend:
cd web/jobbliggaren-web
BACKEND_URL=http://localhost:5049 pnpm test:e2e
# The loginAs specs need the confirmed-login seam, which requires the backend in
# Development with Auth:RequireEmailConfirmation=true (the dev default).
```

## Triage a red run

1. Download the `playwright-report` artifact (HTML report + `test-results/` traces).
2. If every spec failed at seeding/login, check the **Api log dump** step — the Api
   likely never reached `/api/ready` (migration or connection-string failure).
3. `dotnet ef database update` failure → usually `pg_trgm` missing before the
   AppDbContext trigram migration (the workflow creates it first) or a
   connection-string mismatch.
4. A single-route timeout on first navigation is typically Next dev cold-compile —
   `retries: 2` should recover it; persistent cases argue for the prod-build revisit
   above.
