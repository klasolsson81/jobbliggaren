# Session 2026-06-13 — Product rename JobbPilot → Jobbliggaren (ADR 0069)

**Branch:** `refactor/rename-jobbliggaren` · **Base:** `main` (`3f33474`) · **Rename commit:** `e6b97ed`

## Goal

Execute ADR 0069 D1–D7 (excluding D3) as ONE atomic rename PR on a quiet tree — rename the
product from JobbPilot to Jobbliggaren across all living code, configuration and spec docs.

## Sequence

1. **Pre-flight:** `main` at `a84afd4` (#65), tree clean. Confirmed.
2. **Step 0:** merged draft ADR-PR #62 (`claude/zen-curie-ebqvg5`) first — ADR 0069 + index +
   CTO review, no code — so ADR 0069 (Accepted) is in `main` before the rename code. Required
   an `update-branch` (head was behind base) + green `ci` before squash-merge.
3. **Branch + execution** off updated `main`.

## What was renamed (ADR 0069 D1/D2/D4/D5/D6)

- **D1:** namespace `JobbPilot.*` → `Jobbliggaren.*` — ~836 C# files, `Jobbliggaren.sln`, 13
  `.csproj`, project folders (`src/`/`tests/`/`perf/Jobbliggaren.*`).
- **D2:** `docker-compose.yml` DB name / Postgres roles / container + volume names →
  `jobbliggaren*`; `appsettings` connection strings; JWT `Audience` `jobbpilot-api` →
  `jobbliggaren-api`; Worker/Migrate provisioning comments. No EF migration (DbContexts already
  brand-neutral `AppDbContext` / `AppIdentityDbContext`).
- **D4:** 6 in-code `UrlFormat` assembly attributes + README/runbook URLs → `.../jobbliggaren/...`.
- **D5:** `web/jobbpilot-web` → `web/jobbliggaren-web` + `package.json` `name`.
- **D6:** `build.yml` (`.sln` ref, per-assembly coverage-gate ids `check Jobbliggaren.X`,
  frontend `working-directory`/`cache-dependency-path`) + `codeql.yml` + `dependabot.yml`;
  living spec docs BUILD.md/CLAUDE.md/DESIGN.md (via the `approve-spec-edit.sh` token —
  Klas delegated "du kör hooken" — with the `jobbpilot-design-*`/`jobbpilot-td-lifecycle`
  skill-name tokens PRESERVED), root README, ADR index (`docs/decisions/README.md`), issue
  templates, `tools/taxonomy-snapshot/README.md`, current-work.md, steg-tracker.md.

## Scope boundaries (deliberate — documented, reversible)

Per ADR 0069 D3/D6 + the CTO review, EXCLUDED from this PR:
- AWS Terraform (`infra/terraform/**`) + `deploy-dev.yml` → separate teardown PR (ADR 0066 /
  TD-104); the ADR-0036 IaC `dotnet-architect` gate moves there.
- Historical `docs/sessions/` logs + ADRs 0001–0068 (dated records; ADR 0069 is the bridge).
- AWS-resource-entangled docs: `aws-*` runbooks, the steg-tracker / tech-debt HISTORICAL rows,
  and `docs/reviews/` + `docs/research/` + handoff bundles — left as-is so retired-AWS resource
  identifiers (`jobbpilot-prod-cluster`, `/aws/ecs/jobbpilot-dev`, secret paths) do NOT become
  false references to renamed-but-nonexistent resources.
- `.claude/` agents + skills (skill-dir names) — Claude Code tooling, not the product; renaming
  skill dirs has its own invocation-name ripple. Living-doc titles + current entries WERE
  updated (current-work.md, steg-tracker.md); their historical rows were left.

## Notable execution facts (two non-obvious traps)

1. **VS Code C# Dev Kit BuildHost corrupted the project graph** mid-rename: when project
   files moved, it rewrote `.sln` (pruned project entries) and stripped `<ProjectReference>`
   ItemGroups from several `.csproj`. Symptom: a false-green build on an empty sln, then 1680
   `CS0234`/`CS0246` despite correct source. Fix: reconstruct `.sln` + all 13 `.csproj` from
   pristine `git show HEAD:` + sed-rename (authoritative), `git add`, and `taskkill //F //PID`
   the BuildHost. (Memory: `feedback_rename_devkit_corruption_and_binary_grep`.)
2. **`git grep -I` silently skips binary-classified files** — `SearchQueryParserTests.cs`
   contains literal control chars (it tests Unicode Cc/Cf stripping) so the rename's `git grep -lI`
   file-list skipped it; the lone stragglers were caught with `git grep -la` + `dotnet build` as
   the oracle.

## Test-isolation finding → senior-cto-advisor (Path A)

The rename reordered xunit's name-based execution order ("Jobbliggaren" sorts differently than
"JobbPilot"), which surfaced a pre-existing shared-fixture fragility: `C2ReverseLookupMigrationTests`
replays a WHOLE-table `saved_searches` migration (guard `RAISE EXCEPTION` on any scalar/unmappable
`Ssyk`) against the shared `[Collection("Api")]` Testcontainers DB, and `SearchCriteriaJsonbBackcompatTests`
leaves legacy `Ssyk` rows behind. 4 C2 tests passed in class-isolation but failed deterministically in
the full project (476 / 4), incl. under `-parallel none` → within-collection ordering, not a race.

senior-cto-advisor verdict: **Path A — in-scope, two-part test-isolation fix** (victim + polluter),
rejecting B (breaks the atomic quiet-tree design), C (`[Skip]` disables a fail-safe guard test) and D
(deterministic → would also red CI). Implemented as `IAsyncLifetime.InitializeAsync` → `DELETE FROM
saved_searches;` before every test in both fixtures, with an isolation-invariant comment. Result:
`Api.IntegrationTests` 476/0 (verified twice).

## Gates

- `dotnet build Jobbliggaren.sln -c Release`: 0 errors / 0 warnings.
- .NET suite (Release): Domain, Application, **Architecture** (78 facts, layering + namespace
  conventions under the new name), Migrate, Worker.Integration, Api.Integration (476/0) — all green.
- FE (`web/jobbliggaren-web`): `pnpm build` + `lint` + `vitest` — all green.
- `dotnet format --verify-no-changes`: clean (after re-wrapping lines that `Jobbliggaren` (+3 chars)
  pushed over the wrap width).

## Reviews (`docs/reviews/2026-06-13-rename-jobbliggaren-*.md`)

- **code-reviewer:** 0 Blocker / 1 Major / 1 Minor. Major = current-work.md + steg-tracker.md titles
  not yet renamed (resolved by the docs-sync commit — titles + current entries updated, historical
  rows left per the documented D6 scope boundary). Minor = pre-existing empty `InitialAdminEmail`
  (not this PR's concern).
- **dotnet-architect:** ✓ GODKÄND 0/0/0 — Clean Architecture intact, arch-tests guard non-vacuously,
  project graph correctly reconstructed, EF D2 honored (no migration, brand-neutral schema).
- **security-auditor:** ✓ PASS 0/0/0 — JWT audience consistent (and never bearer-validated since
  ADR 0017 session-auth; vestigial), DB roles consistent, no secrets leaked, `.gitleaksignore`
  untouched, no GDPR impact.

## Klas-STOPP / operational follow-ups

1. **GitHub repo rename** `klasolsson81/jobbpilot` → `…/jobbliggaren` (D4) — Klas's GitHub operation
   (GitHub 301-redirects the old slug; in-code/doc URLs already updated to the new slug).
2. **Logo PR — separate next step:** only the wordmark TEXT was renamed here; the logo MARK
   (compass) redesign is the separate downstream task (revisit ADR 0068's "compass stays
   navy+gold-dot" note).
3. **Local DB recreate** to `jobbliggaren` + stack restart (Api 5049 / Worker / FE 3000).
4. Deferred-doc follow-ups (AWS runbooks / tech-debt / `.claude/`) are reversible and non-build —
   fold in on request.
