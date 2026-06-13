# Code-review: Product rename JobbPilot → Jobbliggaren (branch `refactor/rename-jobbliggaren`, HEAD `e6b97ed`)

**Status:** ⚠ Changes requested (1 Major — documentation-scope; oracle-neutral)
**Auktoritet:** CLAUDE.md §1.5 (living-doc sync in scope-PR), §1.6 (docs map), §2.1 (Clean Architecture), §6 (PR flow); ADR 0069 D1–D7 + CTO verdict 2026-06-13
**Scope:** Mechanical rename of ~1233 files (836 C# + FE + config + CI + spec docs). Reviewed: the 5 judgment areas + pattern-spot-checks. Mechanical correctness delegated to the stated oracle (dotnet build Release 0/0, .NET suite green incl. Api.IntegrationTests 476/0, Architecture.Tests green, FE build+lint+vitest green, dotnet format clean).

Diff shape: 1189 renames (R), 27 modifies (M), 14 add / 14 delete (git-classified moves where content churn exceeded rename-detection threshold — e.g. `JobbPilot.Application/GlobalUsings.cs` → `Jobbliggaren.Application/GlobalUsings.cs`; verified as same-file moves, not new logic).

---

## Sammanfattning upfront

The rename pattern held cleanly. **Zero `JobbPilot` tokens leak into any renamed `.cs` file** except two deliberate, accurate references inside the test-isolation comments (`JobbPilot→Jobbliggaren rename reordered it`). All 5 judgment areas are sound except one: two living docs that ADR 0069 D6 explicitly names (`docs/current-work.md`, `docs/steg-tracker.md`) were left entirely out of the PR, while the *same* D6 policy was correctly applied to `docs/decisions/README.md`. That inconsistency is the only finding above Minor.

**0 Blockers · 1 Major · 1 Minor.**

---

## Major

1. **Two D6-mandated living docs omitted from the rename PR** — Fil: `docs/current-work.md:1`, `docs/steg-tracker.md:1` (+ active status block in current-work.md)
   Nuvarande: Neither file is in `git diff main...HEAD`. `docs/current-work.md:1` still reads `# Current work — JobbPilot` and its active 2026-06-13 status header still says "JobbPilot"; `docs/steg-tracker.md:1,26` still read `# JobbPilot — STEG-tracker` / "JobbPilot:s utvecklingsbana".
   Krävs: Apply the **same** D6 distinction already used correctly in `docs/decisions/README.md` (this PR): update the *living-product surface* (file title + current-state/active-status prose) to "Jobbliggaren", while leaving historical STEG rows / AWS-entangled prose (steg-tracker lines 37+, `dev.jobbpilot.se`, `jobbpilot_app|_worker|_migrations`, `JobbPilot.Migrate` in dated rows) verbatim as dated records (identical to the ADRs-0001–0068 policy). At minimum the two titles + the current-work active header.
   Motivering: ADR 0069 D6 (line 43, 100) and CTO verdict D6 (line 83) **explicitly enumerate** both files as living docs the rename PR updates; CLAUDE.md §1.5 makes `current-work.md` + `steg-tracker.md` sync mandatory *in the same PR as the scope*. The PR proves it knows this policy — `docs/decisions/README.md` got exactly the right treatment (title → "Jobbliggaren", living rows 0015/0033 → "Jobbliggaren", only the 0069 bridge row retains "JobbPilot"). Leaving the other two D6 files wholly untouched is internally inconsistent with the PR's own handling.
   Caveat for Klas: the task's area-5 lists "steg-tracker historical *rows*" as an AWS-entangled exclusion. If the intent was to defer the **entire** file (not just historical rows) this Major collapses to a documented scope decision — but that intent is not what D6/§1.5 say, so it must be confirmed, not assumed. Oracle-neutral (no code/config/build impact); trackable as a follow-up issue/short docs-touch in this PR rather than oracle-blocking.
   Delegera till: Klas (scope confirmation vs D6); if confirmed in-scope, docs-keeper applies the title/header touch.

---

## Minor

1. **Pre-existing `AdminBootstrap:InitialAdminEmail` carried forward** — Fil: `src/Jobbliggaren.Api/appsettings.Development.json:21`
   Nuvarande: `"AdminBootstrap": { "InitialAdminEmail": "" }` survived the rename (empty).
   Krävs: Nothing in this PR — flagged only for visibility. MEMORY (`feedback_real_rbac_not_email_bootstrap`) records email-bootstrap as a rejected admin path (real RBAC = Fas 6). The key is **empty**, so no behavior; a mechanical rename is the wrong PR to remove config. Note so it is a conscious carry-forward, not an oversight.
   Delegera till: — (track for Fas 6 RBAC cleanup; not this PR).

---

## Judgment-area verdicts

**1. Test-isolation fix — SOUND.** Both `C2ReverseLookupMigrationTests.cs` and `SearchCriteriaJsonbBackcompatTests.cs` implement `IAsyncLifetime` correctly:
- `InitializeAsync` returns `ValueTask` (xUnit v3 surface), uses a scoped `AppDbContext`, propagates `TestContext.Current.CancellationToken`, and clears with `DELETE FROM saved_searches;` before every test.
- `DisposeAsync` calls `GC.SuppressFinalize(this)` (CA1816 satisfied) and returns `ValueTask.CompletedTask` — no resource leak (the scope in `InitializeAsync` is `using`-disposed; nothing held across the lifetime).
- No concurrency hazard: `[Collection("Api")]` serializes the classes that share the Testcontainers Postgres, so the table-wide DELETE cannot race a neighbour.
- Comment accuracy verified: the fix correctly identifies a **pre-existing** shared-fixture coupling (a whole-table migration replay that scans+mutates every `saved_searches` row cannot depend on `[Collection]` execution order) and resolves it the right way — by giving each test a clean precondition rather than encoding order. The rename's role (xUnit name-based ordering keys off the fully-qualified type name; `JobbPilot.*` → `Jobbliggaren.*` reordered it and surfaced the latent coupling) is technically accurate. This is the senior-cto-advisor-sanctioned rename-collateral, and the implementation matches the verdict.

**2. `.sln` + `.csproj` graph — INTACT.** `Jobbliggaren.sln` carries 11 buildable projects (Domain, Application, Infrastructure, Api, Worker, Migrate + 6 test projects) + 2 solution-folder nodes; all project GUIDs present in `ProjectConfigurationPlatforms` and `NestedProjects`. ProjectReference counts are Clean-Architecture-correct and every target resolves to an existing renamed path (verified against disk):
- Domain → 0 refs (depends on nothing — §2.1 floor holds)
- Application → Domain (1) · Infrastructure → Application + Domain (2) · Api → Application + Infrastructure + Domain (3) · Worker → Application + Infrastructure + Domain (3) · Migrate → Domain (1)
- Test projects reference via `..\..\src\Jobbliggaren.X\` — all resolve.
- Zero dangling refs; zero `JobbPilot` token in any `.csproj`/`.sln`. The 13th `.csproj` (`perf/Jobbliggaren.LoadTests`) is deliberately outside the `.sln` (NBomber perf scaffold, built separately in build.yml's observe-only `loadtest` job) — correct, not a missing entry. The Dev-Kit-corruption reconstruction produced a sound graph; build 0/0 confirms compilation.

**3. Config correctness — SOUND.**
- `docker-compose.yml`: complete rename — project `name`, all 5 `container_name`, `POSTGRES_DB`/`POSTGRES_USER` (dev + test), all 5 named volumes, both healthcheck `pg_isready -U jobbliggaren -d jobbliggaren[_test]`. Ports/passwords/secrets untouched (correct).
- `src/Jobbliggaren.Api/appsettings.Development.json`: `Database=jobbliggaren;Username=jobbliggaren`, `Jwt.Audience = "jobbliggaren-api"`. `Jwt.Issuer = https://localhost:5000` correctly left (not brand-bearing). JWT-Audience rename at the only safe (pre-launch, no issued tokens) window per D2 / CTO STOPP-flag.
- `.github/workflows/build.yml`: `.sln` ref in restore/build/test/vuln-scan, all 4 line-coverage gate ids (`check Jobbliggaren.{Domain,Application,Infrastructure,Api}`), both branch-coverage gates (Domain/Application), the `Jobbliggaren.Worker` observe-only `jq select`, all frontend paths (`working-directory` ×3, `cache-dependency-path` ×4, Lighthouse `configPath`), and the `perf/Jobbliggaren.LoadTests/...` paths in the loadtest job. Gate ids match assembly names (= renamed .csproj names) — coverage gate will resolve.
- `.github/dependabot.yml`: `directory: "/web/jobbliggaren-web"` matches the renamed FE dir; comments updated.
- `.github/workflows/codeql.yml` + `.github/codeql/codeql-config.yml`: `.sln` build refs flipped; config `name: jobbliggaren-codeql-config`; all `paths-ignore` FE paths flipped.
- D4 living-code URLs: all 6 `UrlFormat` assembly attributes → `…/klasolsson81/jobbliggaren/blob/main/…`; zero `/jobbpilot/` blob URLs remain in `src`. Root + web `package.json` `name` → `jobbliggaren` / `jobbliggaren-web`; `web/jobbliggaren-web/` dir move confirmed.

**4. Spec-doc skill-token preservation — CONFIRMED (surgical).** The `jobbpilot-td-lifecycle` and `jobbpilot-design-*` skill-name tokens are intact in `CLAUDE.md:45,239` and `DESIGN.md:4,52–56,64,78,95,136,160,175,202`, and the `.claude/skills/jobbpilot-*` directories are correctly NOT renamed (all 6 still present: `jobbpilot-design-{a11y,components,copy,principles,tokens}` + `jobbpilot-td-lifecycle`). The DESIGN.md `.claude/skills/jobbpilot-design-*/SKILL.md` pointers still resolve. This is the exact required distinction — product brand renamed, skill-infrastructure tokens preserved — with no broken skill references.

**5. Scope boundaries — CORRECTLY EXCLUDED (not defects).** AWS Terraform (`infra/terraform/environments/dev/*.tf`) and `.github/workflows/deploy-dev.yml` are absent from the diff and still carry old `jobbpilot` identifiers on disk — exactly per ADR 0069 D3 / CTO D3 (deferred to the separate AWS-teardown PR under ADR 0066 / TD-104; ADR-0036 IaC gate moves there). Historical session logs + ADRs 0001–0068 + tech-debt.md + AWS-entangled runbooks are untouched (D6 dated-records policy). `docs/decisions/README.md` correctly retains exactly one `JobbPilot` — the 0069 interpretive-bridge row text — confirming the dated-record/living-product split was applied deliberately, not missed.

---

## Bra gjort

- **The pattern held across 836 C# files with zero leakage** — the only `JobbPilot` tokens in renamed C# are two *accurate* rename-attribution comments. That is the cleanest possible outcome for a find-replace-grade rename of this size.
- **D6's living-vs-dated distinction applied correctly in `docs/decisions/README.md`** — title + living rows (0015, 0033) renamed, bridge row preserved. (The Major is that this same correct treatment was not extended to the other two named living docs.)
- **JWT `Audience` renamed at the only safe window** — pre-launch, no issued tokens, per the CTO STOPP-flag; avoids a post-launch rollover.
- **Clean-Architecture dependency directions survived the .sln/.csproj reconstruction intact** — Domain still depends on nothing; the layering is provably preserved by both the ProjectReference graph and the green Architecture.Tests.
- **AWS exclusion kept the rename PR's gate-set clean** — no IaC touched, so ADR-0036's mandatory dotnet-architect-for-IaC gate correctly does not fire here.

---

## Sammanfattning

0 Blockers, 1 Major (D6-mandated `current-work.md` + `steg-tracker.md` living-doc title/header update omitted, inconsistent with this PR's own correct handling of `docs/decisions/README.md`; oracle-neutral, documentation-scope — confirm against the area-5 "historical rows" exclusion before deciding in-PR fix vs tracked follow-up), 1 Minor (pre-existing empty `InitialAdminEmail` carried forward — visibility only). The mechanical rename is otherwise sound and the total correctness oracle (build 0/0, full suite green, format clean) covers everything the static review cannot. Delegations: Klas (D6 scope confirmation) → docs-keeper (apply title/header touch if in-scope). Re-review not required for the mechanical body; only the living-doc Major needs a decision.
