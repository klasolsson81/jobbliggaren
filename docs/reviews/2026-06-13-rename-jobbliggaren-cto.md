# CTO verdict — product rename JobbPilot → Jobbliggaren

- **Date:** 2026-06-13
- **Agent:** senior-cto-advisor (decision-maker, CLAUDE.md §9.2)
- **Trigger:** APPROACH-VERDICT requested by Claude Code; CC gave no own recommendation by design.
- **Scope:** Execution shape of an already-locked product rename. PRE-LAUNCH, localhost-only, no production, no issued auth tokens in the wild.
- **Out of scope (locked by Klas, not re-litigated):** the name choice itself; timing (rename executes after the parallel session's "nästa steg" PR merges, on a quiet tree).

## Discovery corrections to the supplied inventory

Read-only verification this session refined three premises the decision points rested on:

1. **There is no `JobbPilot`-named DbContext class.** The contexts are `AppDbContext` and `AppIdentityDbContext` (`src/JobbPilot.Infrastructure/Persistence/AppDbContext.cs`, `.../Identity/AppIdentityDbContext.cs`). D2's fallback ("only rename the C# DbContext class") therefore has **no target** — it is already brand-neutral. Good design; nothing to do there.
2. **Zero EF schema carries the brand.** Schemas are `identity` (+ default `public`); tables are `refresh_tokens`, columns `created_at`, etc. (`AppIdentityDbContextModelSnapshot.cs`). Confirms D2 assessment: **no migration, db-migration-writer NOT pulled in.**
3. **Live CI couples to the namespace.** `.github/workflows/build.yml` references `JobbPilot.sln` and per-assembly coverage gates (`check JobbPilot.Domain coverage 93`, `…Application 95`, `…Infrastructure 82`, `…Api 91`, branch-coverage gates, and the `JobbPilot.Worker` observe-only line). A full namespace rename (D1-A) **must** carry these gate identifiers or CI breaks. This is the single highest-value reason to keep the rename atomic (see D7).
4. **`deploy-dev.yml` is armed at a dead target.** It still triggers on `v*-dev` and references `dev.jobbpilot.se` + ECR, but its target (AWS) is retired (ADR 0066). It is a loaded trigger pointing at infrastructure that no longer exists — it would fail at OIDC-assume if fired. This is material to D3.

---

## D1 — Namespace root: **A (full rename `JobbPilot.*` → `Jobbliggaren.*`).**

**This overrides nothing — it confirms Klas's stated instinct ("update all names in code too"). I considered overriding it toward B and decided against. Motivation, because the override-of-the-override must be conscious:**

The textbook-economical answer is B (keep `JobbPilot.*` as an internal codename, rename only user-facing surface). The argument for B is real: the namespace is not user-facing, the internal-rename delivers zero user benefit, and ~836 C# files is the largest-churn option. Under YAGNI/KISS that looks like the disciplined call.

I reject B here for four reasons that outrank the churn argument **in this specific context**:

- **Ubiquitous Language (Evans 2003, "Ubiquitous Language").** DDD's first discipline is that the name in the code, the conversation, and the product are one word. A permanent split — product "Jobbliggaren", codebase "JobbPilot" — institutionalises a translation layer in every developer's head and every future ADR. Evans treats that gap as a defect, not a convenience. A decoupled codename is a legitimate pattern, but it must be *chosen as an asset* (e.g. a deliberate, neutral internal name), not be the **fossil of the old product name you are actively burying**. Keeping `JobbPilot` as the codename means the dead brand lives forever at the root of every namespace.
- **The Mastercard test (CLAUDE.md §1).** An outside senior reviewer opening this repo post-rename and finding every namespace, the `.sln`, and 13 `.csproj` still saying `JobbPilot` will read it as an *unfinished* rename — exactly the "we'll fix it later" signal JobbPilot's quality bar exists to forbid. B does not look like restraint to a stranger; it looks like an abandoned migration.
- **The cost asymmetry only exists now.** This is pre-launch, localhost-only, no production, no consumers, and — decisively — Klas has already arranged a **quiet tree** with the only parallel work merged first. The blast radius of a namespace rename is a monotonic function of project age and contributor count. It is the smallest it will ever be today. B "saves" the churn by deferring it to a moment when it is strictly more expensive and more likely to be declared permanent. That is the opposite of YAGNI — it is paying interest to avoid principal.
- **Churn here is mechanical, not architectural.** ~836 files is a large *number* but a *shallow* change: `JobbPilot.` → `Jobbliggaren.` in namespaces, `using`s, `.csproj`, `.sln`, project folders. It is find-replace-grade, fully covered by `dotnet build` + the architecture tests + the existing coverage gates. File **count** is not a design axis to be minimised (this is the same reasoning as the per-domain-files precedent). High churn ≠ high risk when the change is uniform and the verification is total.

**Trade-off accepted:** the largest diff of the three options, and a one-time conflict surface for the parallel session. Both are bounded by the quiet-tree timing Klas already secured, and the conflict is mechanical (same token, same replacement) — see D7.

> Note for the rename ADR's "Alternatives considered": option C (a short neutral codename) is rejected for the same Ubiquitous-Language reason as B, *plus* it introduces a **third** name into the system (product, domain, codename) — strictly worse cohesion than either A or B.

---

## D2 — Physical DB name / postgres user / roles: **Rename to `jobbliggaren*`.**

Rename `Database=jobbpilot` / `POSTGRES_DB` / `jobbpilot_test` / the `jobbpilot_app|_worker|_migrations` roles / container + volume names to the `jobbliggaren` equivalents. Touches `docker-compose.yml`, `appsettings.Development.json`, and the provisioning comments in `JobbPilot.Migrate/Program.cs` + `Worker/appsettings.Production.json`. A local DB recreate is required — trivial pre-launch (no data of value).

**Why rename rather than keep physical (the conservative option):** these identifiers are operational ubiquitous language. Once D1 commits to brand coherence top-to-bottom, leaving the physical database, roles, and containers named after the dead brand reintroduces exactly the translation gap D1 rejected — an operator on Hetzner reading `psql -d jobbpilot` against product "Jobbliggaren" hits the same defect Evans warns against. Consistency with D1 is the deciding force; a half-coherent rename is its own anti-pattern.

**db-migration-writer: NOT triggered — confirmed.** No EF schema, table, column, or `HasDefaultSchema` carries the brand (discovery #2/#3). Renaming the *physical database container/login* is connection-string + infra config, not a schema migration. EF emits no migration for any of this. CC's assessment is correct.

**One STOPP-adjacent flag (not a blocker): JWT `Audience = "jobbpilot-api"`** (`appsettings.Development.json`, validated in `JwtTokenGenerator`/`JwtSettings`). This is a runtime token-validation contract, not a namespace. Pre-launch with no tokens issued, renaming it to `jobbliggaren-api` is free and belongs in the same sweep for coherence. If there were *any* live tokens it would be a breaking change requiring a rollover window — there are none, so include it now. Logged here so the decision is conscious, not accidental.

---

## D3 — Retired AWS Terraform / ECS / `deploy-dev.yml` identifiers: **EXCLUDE from the rename. Delete the dead AWS stack as a SEPARATE, later cleanup PR — do NOT fold it into this rename, and do NOT delete it inside this PR.**

Three sub-rulings:

- **Do not rename the AWS identifiers.** Spending review/architect effort to rename `jobbpilot-dev-cluster`, ECR repos, `jobbpilot/dev/*` secret paths, and `dev.jobbpilot.se` inside infra that is **retired (ADR 0066)** and will be **replaced** (Hetzner, TD-104) is polishing a corpse. YAGNI is decisive here: you do not invest in the ubiquitous language of code that is scheduled for deletion. This is the one place in the whole rename where the churn-avoidance argument wins, *because the asset has no future*.
- **Do not delete it inside this PR.** Deletion of 85 `.tf` files + a deploy workflow is an **architectural** change (removes a deployment path, ADR-0036 territory), not a rename. Folding it into the rename PR pollutes an already-large mechanical diff with a semantically different decision, and makes the parallel session's rebase reason about deletions on top of renames. Keep the rename PR *purely* a rename.
- **`deploy-dev.yml` is an armed trigger at a dead target — flag, defer the fix.** It still fires on `v*-dev`. Post-rename it references a brand and an AWS account that no longer exist; it would fail at OIDC-assume. This is pre-existing debt, not introduced by the rename, so it does not block this PR — but it should be the **first item of the separate AWS-teardown PR** (disable/delete the workflow). I am not raising a new TD for it: ADR 0066 already owns "AWS dev-stack teardown"; this is its unfinished tail. CC should reference TD-104 / ADR 0066 when scheduling it.

**ADR 0036 gate:** because the *exclude* decision means the rename PR **does not touch IaC**, the mandatory `dotnet-architect`-for-IaC gate (ADR 0036) **does not fire for the rename PR.** It **will** be mandatory for the separate AWS-teardown PR. This is a positive side effect of excluding: it keeps the rename PR's gate set smaller and cleaner.

---

## D4 — GitHub repo rename + in-code/doc URL refs: **YES — rename the repo; update LIVING in-code URLs in the rename PR; leave historical-doc URLs to D6.**

- Klas renames `klasolsson81/jobbpilot` → `…/jobbliggaren` (GitHub 301-redirects the old slug, so nothing breaks in the interim — the existing references keep resolving).
- The six **source** `UrlFormat` assembly attributes (`JwtSettings.cs`, `IJwtTokenGenerator.cs`, `IAccessTokenRevocationStore.cs`, `RedisAccessTokenRevocationStore.cs`, `JwtTokenGenerator.cs`, `JwtSettings.cs` in Infrastructure) point at `…/klasolsson81/jobbpilot/blob/main/…`. These are *living code* and travel with D1's sweep — update them to `…/jobbliggaren/…`.
- README + runbook URLs (living docs) update here too. Historical session-log/old-ADR URLs are governed by D6 (left as-is; redirect covers them).

GitHub's auto-redirect makes the sequencing forgiving, so the repo rename does not have to be perfectly atomic with the code PR.

---

## D5 — Frontend dir + package: **Rename `web/jobbpilot-web` → `web/jobbliggaren-web` and `package.json name` → `jobbliggaren-web`.**

Same Ubiquitous-Language consistency that drives D1/D2; doing the backend in full but leaving the frontend folder and package named after the dead brand is the half-rename the Mastercard test forbids. The coupling is contained and known: `build.yml` already hardcodes `web/jobbpilot-web` paths (`working-directory`, `cache-dependency-path`) — those move in the same PR, which is again an argument **for** D7 atomicity (the CI path-rename and the dir-rename must land together or CI's frontend job breaks).

**Trade-off accepted:** git sees a directory move (history follows via `--follow`; not a real loss pre-launch).

---

## D6 — Historical docs policy: **Update only LIVING/spec docs + write the rename ADR. Do NOT rewrite history.**

Update: `BUILD.md`, `CLAUDE.md`, `DESIGN.md`, `docs/current-work.md`, `docs/steg-tracker.md`, the ADR `README.md` index, runbooks, and any other doc that describes the system *as it is now*. Add **ADR 0069** (the rename decision + the collision/trademark/SEO rationale Klas gathered).

**Do not** rewrite "JobbPilot" across historical `docs/sessions/` logs and already-decided ADRs (0001–0068).

- **Software Engineering at Google (Winters/Manshreck/Wright 2020), "Documentation":** living docs must track the system; *records* must remain truthful to their moment. A session log dated 2026-05-24 said "JobbPilot" because that is what the product was called then. Rewriting it manufactures a false history — the repo would claim the product was always "Jobbliggaren", which is simply untrue and destroys the audit value of a time-stamped log.
- **The rename ADR is the bridge.** ADR 0069 records *when and why* the name changed; every historical "JobbPilot" reference is then correctly interpretable as pre-0069. That is exactly what an ADR trail is for — you don't retro-edit the past, you record the transition.
- **Cost/risk:** mass-editing 376 markdown files is the highest-effort, lowest-value, highest-noise slice of the whole job, and it buries the *meaningful* doc changes (spec + ADR) under hundreds of cosmetic ones in the diff. CLAUDE.md §1's own existing-Swedish-docs-not-mass-translated stance is the same instinct: don't churn historical records for cosmetic consistency.

**Trade-off accepted:** historical docs say "JobbPilot" forever. Correct and intended — they are dated records, disambiguated by ADR 0069.

---

## D7 — Execution shape: **Big-bang single atomic PR** (carrying D1, D2, D4-code-URLs, D5, D6-living-docs together). D3 is explicitly NOT in it.

- **Minimises the half-renamed window.** A batched rename guarantees intermediate states where namespace says one thing and CI/config say another — every such window is a chance for a broken build to merge and for the parallel session to rebase onto an inconsistent tree. One atomic PR has no inconsistent intermediate state.
- **Makes the parallel session's rebase one-shot.** Klas already sequenced this PR *after* that session's merge onto a quiet tree. A single atomic rename is one mechanical conflict resolution (one token, one replacement, uniform); batches multiply the rebase events.
- **CI couplings demand atomicity.** `build.yml`'s `JobbPilot.sln` reference, the per-assembly coverage gates, and the `web/jobbpilot-web` paths must change *in lockstep* with the namespaces and the frontend dir. Splitting them across PRs means at least one PR has a red `ci` by construction. Atomic is the only shape where every commit can be green.
- **This is the textbook case for big-bang.** Building Evolutionary Architectures (Ford/Parsons/Kua 2017) reserves incremental migration for changes that *can* be safely partial under live traffic. A pre-launch, no-consumer, uniform, fully-test-covered rename is the precise opposite: nothing is gained by incrementality and inconsistency is the only risk it adds.

**Trade-off accepted:** one large diff to review. Mitigated because (a) the diff is mechanical and uniform — reviewers verify *the pattern*, not 836 individual changes; (b) `dotnet build` + architecture tests + the coverage gates are a total correctness oracle for a rename; (c) the alternative (batches) trades a big-but-boring review for multiple risky inconsistent states. Boring-and-safe beats small-and-fragile.

---

## Downstream gates for the rename PR (confirmed / adjusted)

| Gate | Fires? | Why |
|---|---|---|
| `code-reviewer` + `dotnet-architect` | **Yes** | >5 files and a structural change (CLAUDE.md §9.2). Architect confirms the namespace move preserves Clean-Architecture layering and the `.sln`/`.csproj` graph. |
| `dotnet-architect` MANDATORY-for-IaC (ADR 0036) | **No (for this PR)** | D3 *excludes* IaC. The rename PR touches no Terraform/workflow infra. This gate **moves to** the separate AWS-teardown PR. |
| `db-migration-writer` | **No** | No EF schema change (discovery #2/#3). Physical-DB rename is connection-string/infra config only. |
| `security-auditor` | **Light/optional** | Strictly a rename, but it grazes auth identifiers: JWT `Audience`, the DB role names, and secret-path strings. Pre-launch with no issued tokens there is no live-credential exposure, so this is not a hard gate — but since the diff touches auth-adjacent config, a quick security-auditor pass is cheap insurance. Recommend running it; not a blocker. |
| approval-hook (BUILD/CLAUDE/DESIGN edits) | **Yes** | D6 edits all three top-level spec files. Klas is instructing the rename, but the approval hook still clicks per CLAUDE.md §1.6 — expected, not friction. |

---

## ADR count: **ONE ADR — 0069.**

A single rename ADR. It records: the name change + Klas's collision/trademark/SEO/PRV/WIPO rationale (Decision/Context), the D1-A namespace verdict and the explicit rejection of B and C (Alternatives considered — so a future instance does not reopen the codename question), the D3 exclude-and-defer stance with a forward-reference to the AWS-teardown work under ADR 0066 / TD-104, and the D6 do-not-rewrite-history policy. It does **not** supersede an existing ADR — it is a new product/architecture decision. The separate AWS-teardown is *not* a second ADR here; it is execution under existing ADR 0066.

---

## STOPP concerns

**No hard STOPP.** The rename is a green-light with the shape above. Two items raised to conscious-decision level (handled, not blocking):

1. **JWT `Audience = "jobbpilot-api"`** is a runtime validation contract. Free to rename pre-launch (no tokens issued); included in D2's sweep. Would be a breaking, rollover-requiring change post-launch — it is being done at the only safe time.
2. **`deploy-dev.yml` is an armed `v*-dev` trigger pointing at retired AWS.** Pre-existing debt, not introduced by this rename, so it does not block the rename PR. It is the first task of the separate AWS-teardown PR (disable/delete the workflow) under ADR 0066 / TD-104. No new TD needed — it is the unfinished tail of an existing decision.

---

## One-line summary for `current-work.md`

> Rename JobbPilot→Jobbliggaren: full namespace rename (D1-A) + physical DB/roles (D2, no EF migration) + frontend dir/package (D5) + living-docs & ADR 0069 (D6) as ONE atomic PR on the quiet tree (D7); AWS IaC EXCLUDED and deferred to a separate teardown PR under ADR 0066/TD-104 (D3); repo rename via GitHub redirect (D4). Gates: code-reviewer + dotnet-architect + approval-hook; db-migration-writer NOT triggered; ADR-0036 IaC gate moves to the teardown PR; optional security-auditor pass.

## References

- Eric Evans, *Domain-Driven Design* (Addison-Wesley, 2003) — "Ubiquitous Language"
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — DRY as one authoritative place per knowledge piece (ubiquitous-language consistency)
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (O'Reilly, 2017) — incremental-vs-atomic migration criteria
- Winters/Manshreck/Wright, *Software Engineering at Google* (O'Reilly, 2020) — "Documentation" (living docs vs dated records)
- Robert C. Martin, *Clean Architecture* (2017) — file count is not a design axis; YAGNI/KISS scoping
- CLAUDE.md §1 (Mastercard-level bar), §1.6 (docs map + approval hook), §9.2 (agent gates), §9.6/9.7 (TD discipline)
- ADR 0066 (AWS dev-stack teardown), ADR 0051 (Bedrock retired / Anthropic Direct), ADR 0036 (dotnet-architect mandatory for IaC), ADR 0065 (PR flow + CI gate)
