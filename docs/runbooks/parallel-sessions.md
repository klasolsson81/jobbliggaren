# Runbook — Parallel Claude Code sessions (autonomous multi-session flow)

> Operational playbook for running 2–4 Claude Code sessions concurrently
> (Max x20, Opus 4.8) without collisions. Governance rules in
> [`CLAUDE.md` §6.5](../../CLAUDE.md); strategic map in
> [`steg-tracker.md`](../steg-tracker.md); backlog in GitHub Issues.
> Created 2026-06-25 (FAS 0 foundation). Locked decisions:
> backlog = GitHub Issues, runtime = one stack-owner, PR-babysitter = cloud
> `/schedule` on PR events.

---

## 1. The model in one paragraph

Each session works in its own **git worktree** branched from `origin/main`.
Exactly **one** session is the **stack-owner** (runs the local dev Postgres on
port 5435 + Api/Worker against it); every other session runs code + unit +
architecture tests + **Testcontainers** (ephemeral DB) and never touches the
shared dev DB. **EF migrations** are owned by one session at a time (the most
dangerous hotspot). Work is partitioned by **bounded context** (§4) so two
sessions rarely touch the same files; shared **hotspot files** (§5) are owned
by one session at a time, coordinated via issue assignment. Each merged PR
gets `automerge`; a cloud `/schedule` **PR-babysitter** reviews + merges.

---

## 2. Roles

| Role | Count | Runtime | Notes |
|---|---|---|---|
| **Stack-owner** | exactly 1 | Its OWN `c:/tmp` worktree; runs dev Postgres (5435) + Api + Worker + FE **from that worktree** | Owns runtime verification, rendered-verify, manual dev testing. Holds the bin-lock **on its own worktree only** (each worktree has its own `bin/` → no cross-worktree lock under Model 1). Injects secrets via env override (below). |
| **Worktree session** | 1–3 | Isolated worktree; code + `dotnet test` (unit/arch) + **Testcontainers** integration; FE `pnpm test`/`build` | Never runs Api/Worker against 5435; never the migration owner unless explicitly handed the token. |

**Model 1 (Klas, 2026-06-28) — EVERY session works in its own `c:/tmp`
worktree, including the stack-owner. NO session works in the shared main
working copy** (`C:/DOTNET-UTB/JobbPilot`): two sessions there share one
HEAD/index, so either's `git checkout` silently reverts the other's working
tree (incident 2026-06-28). The stack-owner runs the real stack from its
worktree by injecting **ALL `appsettings.Local.json`-borne secrets** at
runtime — the worktree never has that file, so everything it carries must
become an env override, not just the Postgres password (real incident
2026-07-02: a worktree launch with only the Postgres var silently fell back
to the retired AWS-KMS DEK provider — every PII flow 500:ed at first use
while all DEK-free routes looked healthy):

```bash
# Values come from the MAIN checkout's gitignored files — never echo them.
PW=$(grep -E '^POSTGRES_PASSWORD_DEV=' C:/DOTNET-UTB/JobbPilot/.env | head -1 | cut -d= -f2- | tr -d '\r')
MK=$(sed -n 's/.*"LocalMasterKeyBase64"\s*:\s*"\([^"]*\)".*/\1/p' C:/DOTNET-UTB/JobbPilot/src/Jobbliggaren.Api/appsettings.Local.json)
export ConnectionStrings__Postgres="Host=localhost;Port=5435;Database=jobbliggaren;Username=jobbliggaren;Password=$PW"
export FieldEncryption__Provider=Local
export FieldEncryption__LocalMasterKeyBase64="$MK"   # required for BOTH Api and Worker
```

The dev `appsettings` Postgres string uses a `${...}` placeholder the launch
must expand, else `28P01`. `FieldEncryption:Provider` code-defaults to `"Kms"`
(deliberate prod fail-loud, ADR 0066), so omitting the FieldEncryption vars
boots green but breaks every DEK-needing flow (CV import/read, crypto-erasure)
at first use — and the master key MUST be the main copy's: it wrapped all
existing per-user DEKs, a fresh key cannot unwrap them. Set the vars only in
the launching shell — never persist them to a dotfile or a committed wrapper
script (security-auditor 2026-07-02). The launch does NOT
copy `appsettings.Local.json` into the worktree. The main checkout is left on
`main` and untouched (a fallback/reference, not a workspace).

---

## 3. Creating and tearing down a worktree

Each parallel session works in its own worktree so the working tree, the `bin/`
build output, and uncommitted edits never collide. Two paths — pick by setup.
Either way: branch from `origin/main`, and the session must obtain the gitignored
session-state docs a fresh worktree lacks (§3.2).

### 3.1 Create

**Path A — VS Code tabs on one machine (recommended, zero manual setup).** When
sessions run as **tabs in the same VS Code window** (shared workspace
`C:/DOTNET-UTB/JobbPilot`), you only open a tab and paste a start prompt — the
session isolates itself. Its FIRST action: call the **`EnterWorktree`** tool
(e.g. `name: frontend-locale-cleanups`) → it creates a worktree under
`.claude/worktrees/<name>` from `origin/main` and switches the session into it,
with its own branch + its own `bin/` (so no bin-lock against the stack-owner).
The session verifies with `git worktree list` before touching files. No git
commands, no new window/folder. `EnterWorktree` must be instructed explicitly
(the start prompt does this; authorized by CLAUDE.md §6.5); `ExitWorktree`
leaves/cleans it at the end.

**Path B — raw `git worktree` (separate dir or machine).**

```powershell
git fetch origin
git worktree add -b feat/<context>-<slug> C:/tmp/jbl-<context> origin/main
```

Branch name encodes the context: `feat/matching-…`, `fix/jobads-cv-…`,
`docs/infra-…` (see §4 for context names).

### 3.2 Get the gitignored session-state docs

A fresh worktree has the **tracked** files only (incl. this playbook). The
session-state docs — `current-work.md`, `steg-tracker.md` (§2.1 = the roadmap
SSOT), `tech-debt.md`, `sessions/`, local `reviews/` and ADRs 0074+ — are
gitignored (ADR 0072) and absent.

- **Path A (same machine): no sync needed.** All tabs share one disk, so read
  them in place from the main checkout's absolute path —
  `C:/DOTNET-UTB/JobbPilot/docs/current-work.md`, `…/docs/steg-tracker.md`.
  Nothing to copy.
- **Path B (separate dir/machine): copy them in** (run from the MAIN checkout):
  ```powershell
  C:/DOTNET-UTB/JobbPilot/scripts/sync-worktree-docs.ps1 C:/tmp/jbl-<context>
  ```
  The list lives in [`.worktreeinclude`](../../.worktreeinclude); the script
  **refuses secret-like entries** (`appsettings.Local.json`, `.env.local` are
  NEVER synced into a worktree — under Model 1 the stack-owner injects secrets
  at runtime via a `ConnectionStrings__Postgres` env override built from `.env`,
  so its worktree runs the real stack without copying secret files).

**Do not fork these shared docs in a worktree.** `current-work.md` / `steg-tracker.md`
/ `tech-debt.md` are owned centrally and updated in the **main checkout's copy**
(the canonical gitignored baseline — editing a gitignored doc there is a single
`cd`-and-edit, NOT "working in" the copy: no `checkout`/`commit`/stack, so it does
not trip the Model-1 collision rule). One session (the stack-owner or a designated
docs-owner) writes them; a worktree session reads its `sync-worktree-docs`-synced
copy for context and writes only its own gitignored `docs/sessions/<log>.md`.

### 3.3 Push (rebase first)

```powershell
git fetch origin
git rebase origin/main          # linear history; resolve before push
git commit -- <explicit paths>  # pathspec-scoped — shared index across worktrees
git show --stat HEAD            # verify exactly the intended files landed
git push -u origin feat/<context>-<slug>
gh pr create --fill
gh pr edit <nr> --add-label automerge
```

Pathspec-scoped commits (`git commit -- <paths>`) are required because parallel
worktrees can share the index state — never `git commit -a` (memory
`feedback_pathspec_commit_parallel_cc`).

### 3.4 Cleanup

**Automated (issue #673, ADR 0094 — observe-only until the Klas ratchet).** Two
hooks keep merged worktrees from piling up (the 2026-07-05 hygiene pass had to
remove 61 by hand):

- `SessionEnd` (`.claude/hooks/session-end.sh`) writes a **close-stamp** into the
  current worktree's per-worktree marker `.jbl-worktree.json` — but only on a
  genuinely-terminal reason (`logout` / `prompt_input_exit`), **never** on
  `clear`/`resume` (a `/clear` fires `SessionEnd(reason=clear)` and would else
  falsely retire a live worktree). The hook never deletes anything.
- `SessionStart` (`.claude/hooks/worktree-reaper.sh`) opens the marker for the
  current worktree and **reaps** every OTHER worktree that is, by *conjunction*:
  a linked worktree (not the main copy), not the current cwd, on a feature
  branch, **close-stamped**, **clean**, and whose **PR is MERGED**
  (`gh pr --state merged`, the squash-safe oracle). Any doubt → skip-and-report.

The reaper is **observe-only by default** — it logs `WOULD reap …` to
`docs/sessions/worktree-reaper-<date>.log` and does nothing. It only performs
the local, recoverable ops (`worktree remove` → `rm -rf` leftover → `branch -D`)
when `JBL_WORKTREE_REAP=live` is set (the Klas ratchet). The **shared remote is
never auto-touched**: merged branches are listed as `git push origin --delete`
candidates for the babysitter/manual sweep, never deleted by the hook. See ADR
0094 for the full safe-to-reap predicate and the must-not invariants.

**Manual (always available; the fallback for anything the reaper skips — crashed
sessions, pre-marker worktrees, remote branches):**

```powershell
git worktree remove C:/tmp/jbl-<context>   # after merge
git worktree prune                          # drop stale registrations
git branch -d feat/<context>-<slug>
git push origin --delete feat/<context>-<slug>   # remote (never automated)
```

---

## 4. Partition map (bounded contexts → files)

Assign each task to **one** context; two sessions on different contexts rarely
collide. Shared spine = the Infra-Docs context (§5 hotspots).

| Context | Backend | Frontend |
|---|---|---|
| **Matching** | `Domain/Matching/`, `Application/Matching/` (scorer, grading, profiles, notifications, jobs/BackgroundMatching+DigestDispatch, queries), `Infrastructure/Matching/`, `…/Configurations/UserJobAdMatch*` + `MatchPreferencesConverters`, `Api/Endpoints/MeJobAdMatchEndpoints`, `Worker/Hosting/BackgroundMatchingWorker`+`DigestDispatchWorker` | `app/(app)/matchningar/`, `components/matches/`, `components/ui/match-bar`, `lib/api/{job-ad-match,me-matches,match-count,skills}`, `lib/dto/{…,match-preferences}` |
| **Applications** | `Domain/Applications/`, `Application/Applications/`, `…/Configurations/{Application,ApplicationNote,FollowUp}`, `Api/Endpoints/ApplicationsEndpoints` | `app/(app)/ansokningar/`, `components/applications/`, `lib/applications/`, `lib/{api,dto}/applications` |
| **JobAds-CV** (largest) | `Domain/{JobAds,Resumes}/`, `Application/{JobAds,Resumes,SavedJobAds,SavedSearches,RecentJobSearches}/`, `Infrastructure/{JobAds,JobSources/Platsbanken,Resumes,Taxonomy,TextAnalysis,RecentJobSearches}/`, the JobAd/Resume/Taxonomy `Configurations/`, `Api/Endpoints/{JobAds,Resumes,SavedJobAds,SavedSearches,RecentSearches,MeJobAdStatus}`, the sync/backfill Workers | `app/(app)/{jobb,cv,sokningar,sparade}/`, `app/api/{cv,jobb}/*` (BFF), `components/{job-ads,resumes,saved-job-ads,recent-searches}/`, `lib/{job-ads,resumes}/`, related `lib/{api,dto}` |
| **Auth-Waitlist** | `Domain/{JobSeekers,Waitlist,Invitations,Privacy}/`, `Application/{Auth,JobSeekers,Waitlist,Invitations,UserStatus,Admin,Security,Dev}/`, `Infrastructure/{Identity,Auth,Invitations,Security,Auditing,FeatureFlags}/`, identity/auth `Configurations/`, `Api/Endpoints/{Auth,Me,Waitlist,Invitations,Admin*,Dev}`, **`Migrate/` (whole project)** | `app/(auth)/`, `app/(admin)/`, `app/(guest)/`, `app/(marketing-inner)/vantelista/`, `app/(app)/{installningar,oversikt}/`, `app/api/me/*`, `components/{me,settings,guest,dev,oversikt,onboarding}/`, `lib/{auth,guest,waitlist,onboarding,oversikt,me}/` |
| **Landing-KnowledgeBank** | `Application/{Landing,KnowledgeBank}/`, `Infrastructure/{Landing,KnowledgeBank}/`, `Api/Endpoints/LandingEndpoints`, `Worker/Hosting/RefreshLandingStatsWorker` | `app/(marketing)/`, `app/(marketing-inner)/{cookies,villkor}`, `app/api/landing-stats/`, `components/{landing,site,brand}/`, `lib/{api,dto}/landing` |
| **Frontend** (cross-cutting) | — | `app/layout` + route-group layouts + `(app)/@modal`, `components/ui/*` (shadcn), `components/{shell,modals,forms,i18n}/`, `lib/{api,forms,hooks,validation}` + `lib/{utils,env}`, `i18n/`, `messages/{sv,en}/*`, `app/globals.css` |
| **Infra-Docs** (cross-cutting) | `Jobbliggaren.sln`, `Directory.{Build,Packages}.props`, `global.json`, both DI roots, pipeline behaviors, `AppDbContext` + `Persistence/Migrations/`, `docker-compose.yml`, `appsettings*`, `infra/`, `.github/`, `docs/`, BUILD/CLAUDE/DESIGN | `next.config.ts`, `tsconfig.json`, `package.json`/lockfile, `components.json` |

Frontend paths above are relative to `web/jobbliggaren-web/src/`; backend paths
are full from repo root. **Context → `area:` label:** Matching→`area:matching`,
Applications→`area:applications`, JobAds-CV→`area:jobads-cv`,
Auth-Waitlist→`area:auth`, Landing-KnowledgeBank→`area:landing`,
Frontend→`area:frontend`, Infra-Docs→`area:infra` + `area:docs`.

Matching ⟷ JobAds-CV are the most coupled (Matching reads JobAd requirements +
confirmed CV skill chips). Coordinate shared-shape changes there.

---

## 5. Hotspot files (own one at a time)

These are touched by many contexts — **never edit a hotspot you do not own**;
take the issue, announce ownership, finish, hand off. EF migrations are the
single most dangerous one.

| Hotspot | Risk |
|---|---|
| EF migrations — **`AppDbContextModelSnapshot.cs`** (`Infrastructure/Persistence/Migrations/`) AND **`AppIdentityDbContextModelSnapshot.cs`** (`Infrastructure/Identity/Migrations/`, schema `identity`, ADR 0034) | **Most dangerous.** Each snapshot is one shared file rewritten by every `migrations add` against its context → parallel adds = unmergeable conflict + corrupt model. **Both contexts single-owner per session window** (§6). Local migrations are NOT auto-applied. |
| `Infrastructure/Persistence/AppDbContext.cs` | All 12 DbSets; new aggregate adds a DbSet line → contention. |
| `Infrastructure/DependencyInjection.cs` | Master Infra composition root (`AddInfrastructure` + all `AddX` feature modules). |
| `Api/Program.cs` | Api root + the contiguous `app.Map*Endpoints()` block (append-collision). |
| `Worker/Program.cs` + `Hosting/RecurringJobRegistrar.cs` | Worker root (HTTP-free, re-registers modules) + cron schedule. |
| `Application/Matching/Profiles/MatchProfileBuilder.cs` (+ `IMatchProfileBuilder`/`IMatchScorer`) | Shared builder; a shape change ripples to JobAds-CV + Worker. |
| `Application/Common/DependencyInjection.cs` + `Common/Behaviors/` | Mediator pipeline shared by every command/query. |
| `Directory.Packages.props` (+ `.sln`, `Directory.Build.props`, `global.json`) | Central package versions — parallel dep adds conflict. |
| `messages/{sv,en}/*.json` | i18n. Split per namespace (lowers collision), but `common`/`errors`/`validation` shared + sv/en parity must hold. |
| `app/globals.css` | Locked design tokens; DESIGN.md-gated. |
| `components/ui/*`, `components/shell/app-shell.tsx` + `header-stats.tsx`, `components/modals/route-modal-shell.tsx` | Shared chassis; `header-stats` renders the Matching new-match counter inside the global shell. |
| `lib/api/*` base + `lib/dto/{_helpers,common}.ts`, `lib/auth/session.ts` | Shared BFF/DTO plumbing. |
| `components/onboarding/welcome-setup-modal.tsx` | Spans Auth → CV-upload → Matching; server-action re-render can unmount the Radix modal (known pitfall). |

---

## 6. EF migration single-owner protocol

1. Only the session holding the **migration token** (announce in the issue /
   coordination channel) may run `dotnet ef migrations add` against
   `AppDbContext` or the Identity context.
2. Use the `db-migration-writer` agent (CLAUDE.md §9.2).
3. Land the migration PR (automerge), then **other sessions rebase** before any
   schema touch of their own. Serial, never parallel.
4. Local migrations are NOT auto-applied (Api/Worker do not migrate locally;
   `Migrate` is AWS-bound, TD-105). There are **two** DbContexts with separate
   snapshots + migration histories — `AppDbContext` (schema `public`) and
   `AppIdentityDbContext` (schema `identity`, ADR 0034; `Migrate` runs them
   separately, Identity with master creds per Npgsql #1770). After pulling a new
   migration the stack-owner applies **both** as needed:
   ```powershell
   dotnet ef database update --context AppDbContext
   dotnet ef database update --context AppIdentityDbContext   # identity schema
   ```
   A parallel session landing an Identity migration that the owner forgets to
   apply leaves auth silently broken — both contexts are single-owner.

---

## 7. DB / runtime / port rules

```
dev:   Postgres 5435 · Redis 6379 · Seq 5341     (container DB/user "jobbliggaren")
test:  Postgres 5433 · Redis 6380                (DB "jobbliggaren_test", profile "test")
```

- The dev connection string lives ONLY in `Api/appsettings.Development.json`
  (`Host=localhost;Port=5435;…;Password=${POSTGRES_PASSWORD_DEV}`). `.env`
  (gitignored) supplies `POSTGRES_PASSWORD_DEV/_TEST`. **The `${...}`
  non-expansion trap → 28P01 auth-fail** (memory
  `feedback_restart_stack_after_commit_stop`).
- The Worker has **no** connection string of its own — it inherits Postgres/
  Redis from env or `appsettings.Local.json`.
- **Field-encryption (DEK) config lives ONLY in the main copy's gitignored
  `appsettings.Local.json`** (`FieldEncryption: Provider=Local` + master key) —
  a worktree launch must inject `FieldEncryption__Provider` +
  `FieldEncryption__LocalMasterKeyBase64` as env for **both Api and Worker**
  (§2). Without them the code default `Kms` (retired AWS, ADR 0066) throws
  `AmazonClientException` on every DEK flow; the CV-import BFF then masks that
  500 as a misleading 400 "Filen kunde inte läsas…" (it never echoes backend
  bodies — PII discipline), so diagnose by curling the Api directly.
- **Single stack-owner:** Api/Worker/`dotnet ef`/Migrate all point at the same
  5435 DB → two live stacks collide on data + Hangfire state. Only the
  stack-owner runs against 5435; everyone else uses **Testcontainers**.
- **Bin-lock:** a running Api/Worker locks `bin/`; pre-commit `dotnet format`
  rebuilds and will fail/hang. **Stop both before committing any `.cs` change**;
  for ad-hoc verification build/test to a temp dir (`--output <temp>`).
- Per-user API rate-limiter (MeListRead, TokenBucket) can trip on request
  bursts during local verify — expected, recovers in ~10s.

---

## 8. PR-babysitter (cloud `/schedule`)

A cloud-scheduled agent watches PR events and runs review + automerge so the
local sessions stay heads-down. Set up once:

```
/schedule  → recurring cloud routine, e.g. every 15 min:
  "List open PRs with the automerge label on klasolsson81/jobbliggaren.
   For each with green required `ci` and no unresolved agent Blocker/Major,
   run /code-review; if clean, ensure automerge is set. Report a one-line
   status per PR. Do not merge a PR whose ci is red or that is BEHIND —
   leave a comment to rebase."
```

Notes: the babysitter is **billed** and **user-triggered** (you cannot launch
`/code-review ultra` yourself). Mythos is blocked in Claude Code → use **Fable 5
(1M)** for the babysitter / autonomous routines. Automerge via `GITHUB_TOKEN`
does not retrigger CodeQL on the main-push — run `gh workflow run codeql.yml
--ref main` after a batch if needed (memory
`project_automerge_suppresses_main_push_workflows`).

### 8.1 Self-babysitting (mandatory when no cloud babysitter is running)

If the cloud `/schedule` babysitter is NOT active, **every session babysits its OWN
pushed PRs** until they merge — do not push-and-forget. With 2–4 sessions, `origin/main`
advances constantly, so a freshly-pushed PR goes **BEHIND** within minutes and automerge
will NOT merge a BEHIND PR. Before ending a turn, re-check your open PRs:

```bash
gh pr list --state open --json number,headRefName,mergeStateStatus \
  --jq '.[] | "#\(.number) [\(.mergeStateStatus)] \(.headRefName)"'
```

- **`BEHIND`** → bring it up-to-base. **`git push --force[-with-lease]` is deny-listed**
  (destructive-command guardrail = Klas's hand). The CC-allowed path is GitHub's
  *Update branch* (merges base into the PR branch **remote-side**, no force-push;
  collapsed on squash-merge so `main` stays linear):
  ```bash
  gh api -X PUT repos/klasolsson81/jobbliggaren/pulls/<nr>/update-branch
  ```
  (A local `git rebase origin/main` + `gh api PATCH .../git/refs` does NOT work — the
  rebased objects aren't on the remote yet, so the ref-update 422s. Use `update-branch`.)
- **`BLOCKED`** → up-to-base, waiting on required `ci` / review — leave it; automerge takes
  it on green.
- **`DIRTY` / conflict** → the update-branch merge hit a conflict; resolve on a branch + push.

**Verify the issue actually closed on merge.** Automerge SQUASHES, and the squash commit
title often drops the PR body's `Closes #NNN` keyword → the issue stays OPEN after the PR
merges. After a PR merges, confirm its issue closed (`gh issue view <nr> --json state`);
close it manually with a comment referencing the merged PR if not.

---

## 9. Backlog = GitHub Issues

The strategic map is `steg-tracker.md`; the actionable queue is GitHub Issues.
Labels: `area:{matching,applications,jobads-cv,auth,landing,frontend,infra,docs}`,
`hotspot:{ef-migration,di,i18n}`, `P0`–`P3`, lane `{BE,FE,BE+FE}`, and the
coordination set `{wip, blocked, next-up}` (2026-06-28).

### Issue template

```markdown
## Context
<why this exists; link the source doc / ADR / TD>

## Acceptance criteria
- [ ] …

## Source
<current-work.md / steg-tracker.md / tech-debt.md TD-NN / ADR 00NN>
```

Pick an issue → claim its context → create the worktree → work → PR with
`automerge`. A `hotspot:*` label means the task touches a shared file: confirm
no other session owns it first.

### Lane affinity + claim-on-pickup (lighter coordination model, Klas 2026-06-28)

Three CCs run concurrently. The model is **soft affinity + a hard claim signal**,
NOT a hand-ranked per-CC sequence (that drifts every merge):

1. **Lane = soft affinity via the `BE`/`FE`/`BE+FE` labels.** CC1 leans `BE`/stack,
   CC3 leans `FE`, CC2 is flex/overflow (may take `BE` when its lane is dry).
   Labels mark *area*, never *ownership* — an idle CC takes the top item in any
   lane, **but lane pickup is still subject to §5/§6: a `hotspot:*`/`ef-migration`
   issue requires the hotspot/migration single-owner token FIRST** (claim-on-pickup
   is an additional anti-duplication signal, not a replacement for single-ownership;
   for migrations, serial order is harder than "first to `wip` wins").
2. **Priority = `P0`>`P1`>`P2`>`P3`** (`P0` = drop-everything hotfix, out-of-band)
   + a thin **`next-up`** label on the one obvious next pick per lane (so you don't
   re-rank the whole backlog each session).
3. **Claim-on-pickup (the anti-collision signal — this was the gap behind the
   #293/#306 duplicate-work collision):** the moment you start an issue,
   `gh issue edit <N> --add-assignee @me` **and add the `wip` label**. Another CC
   sees it's taken and skips it. Drop `wip` if you stand down.
4. **`blocked`** = blocked by another open issue/decision (e.g. #291 ⟵ #298) —
   skip until unblocked.
5. **Side-track PRs FIRST.** At session start, before new scope, shepherd your own
   open/red PRs to green (CI rerun on a known Docker-Hub flake; rebase a `BEHIND`
   PR via `git merge origin/main`). New work waits behind a stuck PR you own.
6. **`steg-tracker.md` §2.1 holds the strategic sequence** — ONE place, not three
   per-CC lists. It is updated by one session (stack-owner / a designated
   docs-owner) when Klas sets the order.

> Why issues sometimes look "merged but not done": see §8.1 — automerge squashes
> and the squash commit subject often drops the PR body's `Closes #N` keyword, so
> the issue stays OPEN (a Swedish close-keyword never closes it either). Remedy:
> the PR-babysitter `gh issue close`s the referenced issues on merge, or a periodic
> sweep does; PR bodies use English `Closes #N`. (2026-06-28 sweep closed 10 such
> stragglers: #204/#258/#261/#265/#266/#272/#273/#317/#318/#319.)

---

## 10. Common pitfalls (carried from memory)

- **Stale dev-server masks a Jest-worker crash** → `pnpm install` + clean
  restart; a prod `pnpm build` proves the code is OK.
- **`Results.Ok(null)` writes an empty body** (not `null`) → use
  `Results.Content("null", …)` for nullable-200 endpoints.
- **`dotnet test --filter` returns zero tests** (MTP/xUnit v3) → run the built
  `*.exe -class "<FQN>"` for one class; whole unit projects run fast unfiltered.
- **Sub-agent hook-bypass watch:** verify sub-agent commit content for
  `core.hooksPath=/dev/null`; flag a SECURITY WARNING if found.
- **Detached-diff false "deletions":** a worktree diffed against the wrong base
  can show phantom removals — diff against `origin/main`.
- **Post-merge local main sync:** `git fetch && git merge --ff-only origin/main`
  (not `pull`); don't `checkout main` over local-only docs.
- **Worktree stack-launch without the FieldEncryption env vars** → boots green
  and serves DEK-free routes fine, then the first PII flow (CV import) 500s on
  the retired AWS-KMS default, surfaced to the browser as a misleading 400
  "Filen kunde inte läsas". Inject both `FieldEncryption__*` vars for Api AND
  Worker (§2/§7); incident 2026-07-02.
