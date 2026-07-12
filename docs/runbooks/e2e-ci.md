# Runbook — Playwright E2E in CI (#796)

Operational reference for the full-stack Playwright end-to-end suite and its CI
workflow. Companion to [`local-dev-setup.md`](local-dev-setup.md) and
[`frontend-visual-verification.md`](frontend-visual-verification.md).

## What runs where

- **Workflow:** [`.github/workflows/e2e.yml`](../../.github/workflows/e2e.yml),
  triggers on `push`/`pull_request` to `main` + `workflow_dispatch`. (They were pulled
  back to dispatch-only in #814 while the suite was red; #813 restored them.)
- **Specs:** `web/jobbliggaren-web/tests/e2e/*.spec.ts`
  (`applications`, `cv`, `delete-account`, `jobb`, `security-headers`, `auth`),
  single chromium project, serial (`workers: 1`).
- **Stack the job assembles:** ephemeral `postgres:18` + `redis:8-alpine` service
  containers → migrations via `dotnet ef database update` → the .NET Api on
  `:5049` (background, health-waited on `/api/ready`) → a Next.js **production build**
  served on `:3000` (`pnpm build` as its own step; Playwright's `webServer` runs
  `pnpm start`).

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

## Frontend: a production build, not `pnpm dev`

CI serves a **production build** (`pnpm build` as an explicit step; the Playwright
`webServer` then runs `pnpm start`). Locally the default is still `pnpm dev`, and an
already-running server is reused.

This is the fix for the timeout that made the gate unusable (#813). `next dev` compiles
each route on demand on first hit; with `workers: 1` and `retries: 2` the suite could not
finish inside the job timeout. Against a production build every route is precompiled:
the full suite runs in **~2 minutes** locally, versus ~25 min (timeout) on `pnpm dev`.
`retries` is accordingly `1` in CI, not `2`.

`security-headers.spec.ts` is **mode-agnostic** and passes in both: it asserts the
branch-invariant CSP directives, plus the dev/prod branches as a mutual-exclusion
invariant (a policy carrying `'unsafe-eval'`/`ws:` must NOT also carry
`upgrade-insecure-requests`, and vice versa). So the prod build cannot silently weaken it.

The `__Host-` session cookie works over `http://localhost` because Chromium treats
localhost as a secure context.

## Secrets in CI (env-only, never committed)

- Field-encryption master key: `FieldEncryption__Provider=Local` +
  `FieldEncryption__LocalMasterKeyBase64=$(openssl rand -base64 32)` into
  `$GITHUB_ENV`.
- Throwaway RSA JWT keys generated per-run (`Jwt__PrivateKeyPath`/`__PublicKeyPath`).
- `Email__Provider=Console` (no outbound mail). Never depend on the gitignored
  `appsettings.Local.json` (absent in CI).

## Run the suite locally

Do **not** run the suite against the shared dev Postgres (:5435) — another session may own
the stack (CLAUDE.md §6.5). Stand up an isolated stack on alt ports:

```bash
# 1. Ephemeral Postgres + Redis (own ports, own containers)
docker run -d --name jbl-e2e-pg -e POSTGRES_DB=jobbliggaren -e POSTGRES_USER=postgres   -e POSTGRES_PASSWORD=postgres -p 5445:5432 postgres:18
docker run -d --name jbl-e2e-redis -p 6395:6379 redis:8-alpine
docker exec jbl-e2e-pg psql -U postgres -d jobbliggaren -c "CREATE EXTENSION IF NOT EXISTS pg_trgm;"

# 2. Migrate, then run the Api in Development with the flag ON + relaxed rate limits
#    (every spec registers from one IP in a burst -> AuthWrite 429 otherwise), a Local
#    DEK master key, and throwaway JWT keys. See the workflow's env block for the full set.
#    NB: `dotnet ef`'s -c is --context, NOT --configuration.
dotnet ef database update --configuration Release --project src/Jobbliggaren.Infrastructure   --startup-project src/Jobbliggaren.Api --context AppDbContext   # + AppIdentityDbContext

# 3. Frontend: build + serve, then point Playwright at it
cd web/jobbliggaren-web
BACKEND_URL=http://localhost:5079 pnpm build
BACKEND_URL=http://localhost:5079 PORT=3055 pnpm start &
PLAYWRIGHT_BASE_URL=http://localhost:3055 BACKEND_URL=http://localhost:5079 pnpm test:e2e
```

Iterate locally, not through CI: a CI cycle is minutes and can be cancelled by a parallel
merge. Verify a green run on main afterwards with `gh workflow run e2e.yml --ref main`.

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
