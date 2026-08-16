# Runbook — SCB company-register live population (first run)

> **Audience:** Klas (the only operator who can run this — the SCB client
> certificate lives only on his machine). **Scope:** issue #560 / ADR 0091 —
> the first full population of the local `company_register` replica from SCB's
> certificate-authenticated `sokpavar` API (~1.17M legal-entity rows).
> **Related:** `docs/runbooks/local-dev-setup.md` (base stack launch),
> `docs/runbooks/hangfire-schema.md` (Hangfire storage/dashboard posture),
> `docs/decisions/0091-*` (the SCB register decision), the rate-limit hardening
> shipped alongside this runbook (senior-cto-advisor 2026-07-05).

This run is **cert-gated and deliberate**: `ScbRegister:Enabled` ships `false`
so CI and every autonomous session stay dark. Turning it on and hitting SCB's
real, metered, ban-risk API is an operator action, never automation.

---

## 0. What "safe" means here (read once)

SCB caps each API-Id at **10 calls / 10 s**; a breach risks an **API-Id ban**
(a §12 STOPP condition). Everything below is built so a healthy run cannot
approach that ceiling and an unhealthy one fails loud instead of hammering:

- **Outbound throttle: 6 calls / 10 s** — a sliding-window limiter (not fixed,
  so it cannot burst across a window boundary). That is 60 % of SCB's cap, a
  deliberate 4-call margin above any clock-skew edge. The population client is
  **sequential** (one SCB call in flight at a time), so this ceiling holds for
  new calls; retries stay safe via exponential backoff + 429 fail-fast (below),
  not by re-throttling each attempt (the limiter is Polly-outermost, so a permit
  is taken once per call, not per retry).
- **429 = fail fast.** A `429 Too Many Requests` is **never retried**
  (`ScbRetryPolicy`); it trips the circuit breaker instead (persistent 429 →
  5-min open). One 429 at 6/10 s means something is wrong upstream — **stop and
  inspect, do not run harder** (see §6).
- **No false deletes.** Any partial/errored run marks the outcome *truncated*,
  which **skips the deregister sweep** — a half-fetched run can never flip the
  untouched majority to `Deregistered`.
- **No personnummer.** The SCB query already excludes sole traders (`Juridisk
  form ≠ 10`); an independent `IsPersonnummerShaped` guard drops any pnr-shaped
  org.nr before it is persisted (defense-in-depth). Verified in §5.
- **Progress is visible.** The Worker logs a heartbeat (~every 60 s) with
  batches / rows-so-far, so a healthy ~11 h run is never silent.
- **No silent restarts (#688).** The SCB job carries `AutomaticRetry(Attempts
  = 0)`: a failed run goes straight to the **Failed** state (visible via
  `GET /api/v1/admin/jobs/failed`) instead of Hangfire's default 10 from-zero
  retries — a from-zero retry of a ~2 h run re-spends ~8k metered SCB calls
  per attempt. Recovery is deliberate: the next weekly cron or a manual
  re-trigger (§4 Path B). The Worker's Hangfire storage runs with **sliding
  invisibility** (`UseSlidingInvisibilityTimeout = true`), so a healthy long
  run keeps its fetch lease instead of being re-fetched at the 30-min
  invisibility ceiling. The population SQL path has explicit command timeouts
  (120 s batch-upsert / 600 s sweep) instead of the Npgsql 30 s default.
- **No hidden EF retry.** `EnableRetryOnFailure` is deliberately NOT wired —
  `AppDbContext` uses no EF transient-retry execution strategy, and the
  population store issues raw `NpgsqlCommand`s an EF strategy would not wrap
  anyway. A transient DB blip is NOT auto-retried: resilience is
  command-timeout headroom + fail-fast-to-Failed + a clean idempotent re-run.

Expected duration at 6/10 s: **~11 h** (empirical — the first completed run,
2026-07-05→06, clocked 665 min for a full-register re-fetch of 1,107,940 rows;
the earlier
"~1.5–3 h" estimate was wrong for a from-zero population and holds for no run,
since every run — including the weekly steady-state refresh — is a full
re-fetch + upsert, not incremental). Longer than at 9/10 s by design — Klas
accepted the extra minutes in exchange for the wider margin. This
~11 h real hold time is why `DistributedLockTimeout` is pinned to 12 h (#693,
§6 "lock takeover").

---

## 1. Preconditions

1. **Certificate installed.** The A01489 client cert (`docs/scb/*.pfx`, gitignored)
   must be imported into the Windows cert-store `CurrentUser\My` **with its
   private key**. Verify (PowerShell):
   ```powershell
   Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like "*A01489*" -or $_.HasPrivateKey } |
     Select-Object Subject, Thumbprint, NotAfter, HasPrivateKey
   ```
   Note the **Thumbprint** (40 hex chars) and confirm `HasPrivateKey = True` and
   `NotAfter` is in the future. If it is not in the store, import the `.pfx`
   (double-click → Current User → default store) and re-check.
2. **DPIA clearance.** ADR 0091 covers this run; the register holds no
   personnummer by design (§0, verified §5). No new PII surface.
3. **Dev Postgres up** (Docker Compose, port 5435) and reachable — the same
   database backs both `company_register` and the Hangfire storage.
4. **Time / slot (#708 PR 2, senior-cto-advisor 2026-07-09):** budget ~11 h
   uninterrupted (empirical, §0). Prefer a **Saturday run started ≥ ~06:00 UTC**
   (after SCB's Saturday-morning update), finishing before Sunday 00:00 UTC:
   SCB updates its API **every night except Saturday→Sunday** (per its
   onboarding PDF, which documents no status code for rate/maintenance
   rejections), so a Saturday start between ~06:00 and ~13:00 UTC is the only
   slot where an ~11 h run fits without crossing a nightly update. The 2026-07-05 run's 40 transient 400s are
   attributed by elimination to the nightly update its tail crossed — a rate
   breach is code-excluded and would surface as 429 (§6). Still avoid
   02:00–05:00 UTC for the DB-contention reason in point 5.
5. **ISOLATED run (mandatory — #688).** The first live attempt failed on DB
   contention, not SCB: the full recurring-job fleet ran against the same dev
   Postgres and starved the population's writes. During the population window:
   - **Sole stack-owner (CLAUDE.md §6.5):** exactly ONE Worker (the population
     one) against the dev Postgres (port 5435) — no other CC session's
     Worker/Api, no parallel stack.
   - **No manual multi-job triggering** (admin backfills, snapshot re-runs,
     ad-hoc jobs) while the population runs.
   - **Avoid the heavy daily-backfill window 02:00–05:00 UTC** (snapshot +
     `Backfill*` cluster). The light frequent jobs registered in the same
     Worker (landing stats `*/5`, stream `*/10`) may co-run — the #688 command
     timeouts absorb their brief contention and sliding invisibility prevents
     the 30-min re-fetch: the code turns co-running light jobs from *fatal*
     into *survivable*; the isolation rule lowers the probability. If the
     procedural rule ever proves insufficient, the documented fallback is to
     temporarily env-gate the other `AddOrUpdate` calls in
     `RecurringJobRegistrar` — an operator option, not a shipped toggle
     (senior-cto-advisor 2026-07-05, #688 Q5).

---

## 2. Configure the Worker (gitignored `appsettings.Local.json`)

Create/merge `src/Jobbliggaren.Worker/appsettings.Local.json` (gitignored —
**never committed**; the thumbprint is not a secret but is kept out of the repo
per ADR 0091). Only the `ScbRegister` block is population-specific; the
connection strings are whatever your dev stack already uses (see
`local-dev-setup.md` — typically injected via the `ConnectionStrings__Postgres`
env override, not copied here).

```jsonc
{
  "ScbRegister": {
    "Enabled": true,
    "CertThumbprint": "<PASTE_YOUR_40_HEX_THUMBPRINT>",
    "CertStoreLocation": "CurrentUser"
    // BaseUrl + SyncCadenceCron inherit appsettings.json; do not duplicate.
  }
}
```

Env-override equivalent (if you prefer not to write a file):
`ScbRegister__Enabled=true` and `ScbRegister__CertThumbprint=<thumbprint>`
(ASP.NET wants the string `true`, not `1`).

If `Enabled=true` but `CertThumbprint` is missing, the Worker **fails loud on
start** (by design) — it never runs cert-less.

---

## 3. Start the stack

Launch the Worker (it registers the recurring job and runs the HangfireServer
that executes it) against the dev Postgres, exactly as in `local-dev-setup.md`.
The Worker console is your primary monitor.

- The recurring job id is **`sync-scb-company-register`**.
- It is `DisableConcurrentExecution(4h)`-guarded (a 4 h *acquisition* wait). With
  `DistributedLockTimeout` at 12 h (> the ~11 h runtime, #693) the lock cannot be
  taken over mid-run, so a duplicate trigger blocks then lands `Failed` rather than
  co-running — see §6 "lock takeover" for the pre-#693 failure mode.
- With `Enabled=true` + a valid thumbprint you should see the cert load on start
  and **no** `Enabled=false — no-op` line.

You only need the **Api** running too if you use the admin-endpoint trigger
(Path B, §4). The cron-nudge trigger (Path A) needs the Worker alone.

---

## 4. Trigger the run

Two paths — pick one. For a first controlled run, **Path A** is simplest
(Worker only, no auth).

**Pre-flight (both paths — #693 lesson):** Hangfire's own catch-up can start
the job **at Worker boot** if a cron occurrence passed while the Worker was
down. Before any manual trigger or cron-nudge, check for an already-running
execution:

```sql
SELECT id, statename, createdat FROM hangfire.job ORDER BY id DESC LIMIT 5;
```

If a run is already `Processing`, do **not** nudge — on 2026-07-05 the nudge
created the duplicate execution (§6 "lock takeover"). Post-#693 a duplicate can
no longer co-run: it blocks on its 4 h acquisition wait and lands `Failed` —
harmless, but a wasted slot.

### Path A — cron-nudge (Worker-only, simplest)

Set `ScbRegister:SyncCadenceCron` in `appsettings.Local.json` to a time **1–2
minutes in the future, in UTC** (the cron is UTC; Swedish local = UTC +1 winter
/ +2 summer). Example: if your clock says 23:47 local in summer, that is 21:47
UTC → set `"49 21 * * *"` to fire at 21:49 UTC. Start the Worker; Hangfire fires
the job within ~1 min of the matched minute. Watch the console (§5). After the
run, **revert the cron** (or set `Enabled=false`) so it does not re-fire.

### Path B — admin trigger endpoint (repeatable, needs Api + admin)

Requires the Api running (shares the Hangfire storage) and an **Admin**-role
account (grant locally by inserting into `AspNetUserRoles`; the role name is
`Admin`). Then, authenticated as admin:

```
POST /api/v1/admin/jobs/recurring/sync-scb-company-register/trigger
```

The id is on the closed allowlist (fan-out/RCE-safe); a non-allowlisted id is a
400. The call is audited and rate-limited (AdminWritePolicy). It enqueues an
ad-hoc run the Worker's HangfireServer picks up immediately.

### Canary discipline (both paths)

Do **not** walk away at trigger time. Watch the **first ~1–2 minutes / first
municipality**: confirm the cert authenticated live (no TLS/auth error), **zero
429s**, and that rows are being fetched (heartbeat advancing). Only then let it
run to completion. If anything looks wrong, abort (§6).

---

## 5. Monitor + verify

### During the run
- **Worker console** — `LogStarted` (5710), then a heartbeat (~60 s):
  `pågår — batchar=…, upserted=…, fetched=…, förfluten min=…`. Silence for
  minutes on end is a red flag (see §6).
- **Api (if running):** `GET /api/v1/admin/jobs/recurring` shows the job's
  state (`Processing` → `Succeeded`); `GET /api/v1/admin/jobs/failed` lists any
  failure (sanitized — no PII).

### On completion
- **Worker console** — `LogCompleted` (5712): `klart — upserted=…,
  deregistered=…, excludedPnr=…, excludedInvalid=…, fetched=…, sweepApplied=…,
  failedPartitions=…, durationMin=…`. This is the run summary; capture it.
  `failedPartitions` (#708) counts SCB-rejected partition requests
  (rakna/hamta non-success); each also latched the run truncated — see §6
  "400-rejected partitions".
- **Worker console** — `LogProtectedPartitionTails` (**5717**, #717): one WARN,
  emitted only when the run protected an over-cap 5-digit tail —
  `skyddade partitioner … antal=…, total otäckt svans≈… rader … Per partition
  (kommun×SNI): …:count=…,leaves=…,tail=…`. This is the **#641 facet-sizing
  evidence** the completion run now yields **for free** (zero extra SCB calls —
  the over-cap `raknaforetag` counts were already taken): the per-partition
  breakdown sizes each dense-metro tail (e.g. Sthlm×AB×`00000`), biggest first,
  and supersedes a metered round-3 tail probe. The total is an **upper bound**
  ("övre gräns" — a multi-SNI entity can be double-counted across cells, the #628
  caveat), so read it as "at most N rows short". **Capture this line into the
  session log.** It carries kommun + SNI + counts only, never an org.nr. A clean
  run with no over-cap tail is silent (guarded on a non-empty protected set).

### Verification queries (psql against the dev DB)
```sql
-- 1. Total rows — honest post-#708 expectation: ~1.07–1.11M distinct. SCB's own
--    register size is ~1.17M, but the 34 protected-partition tails (~105k rows,
--    e.g. Sthlm×AB×00000 alone counts 31,000 at SCB vs the 2000 fetch cap) are
--    structurally unfetchable until #641's 4th-rung facet — they are #641 scope,
--    not a run failure (senior-cto-advisor 2026-07-09).
SELECT count(*) FROM company_register;

-- 2. Lifecycle breakdown — Active dominates; Deregistered only if the sweep ran.
SELECT status, count(*) FROM company_register GROUP BY status ORDER BY 2 DESC;

-- 3. Personnummer spot-check — MUST be 0. Mirrors IsPersonnummerShaped in ALL
--    THREE branches: not 10 chars, not all ASCII digits, or 3rd digit < '2'.
--    The middle branch is the guard's fail-safe and is easy to drop: Arabic-Indic
--    and fullwidth digits are 10 CHARACTERS and pass a naive length test. Use
--    [0-9] because it is the NARROWEST class: any deviation can then only flag
--    more rows, never fewer. (The repo's ban on \d is a .NET rule — #865, \p{Nd}
--    folds fullwidth. Asserting it of Postgres too did NOT reproduce: measured
--    2026-08-16 on PG 18.3/en_US.utf8, neither \d nor [[:digit:]] matched
--    Arabic-Indic or fullwidth. The choice stands on fail-safe direction.)
--    A subset of the guard cannot detect the guard's own failure, which is what
--    this checks.
SELECT count(*) AS pnr_shaped
FROM company_register
WHERE organization_number !~ '^[0-9]{10}$' OR substring(organization_number, 3, 1) < '2';

-- 4. Durable audit row (payload carries fetched/upserted/deregistered/
--    excludedPnr/sweepApplied). audit_log is day-partitioned (ADR 0024).
SELECT occurred_at, event_type, payload
FROM audit_log
WHERE event_type = 'System.CompanyRegisterSynced'
ORDER BY occurred_at DESC
LIMIT 1;
```

**Pass criteria:** count in the **~1.07–1.11M distinct** band (the honest
post-#708 expectation, **Klas-confirmed 2026-07-09** — ~1.17M requires #641),
query 3 returns **0**, the audit row shows
`SweepApplied=true` with `FailedPartitionCount=0` — **those two fields are the
real #708 completion deliverable**; the row count is a secondary indicator.

**Reading the audit row's `FailedPartitionCount` (#708):**
`SweepApplied=false` **with** `FailedPartitionCount > 0` = SCB rejected that many
partition queries this run — the run is diagnosable from the log's WARN 5702
lines, which now carry the full partition descriptor (`Kategori=[kod,…]` pairs)
plus SCB's validator reason. `FailedPartitionCount = 0` with a skipped sweep =
the truncation came from another latch (over-cap leaf that could not be bounded,
reconciliation gap, envelope drift) — check `SweepSkipReason` and events
5701/5703/5713/5714. A WARN **5716** (2-digit division coverage gap, #708) is
OBSERVE-ONLY this run: diagnostic evidence, latches nothing, never a truncation
cause by itself.

### Sweep floors (why the sweep may skip — this is correct)
The deregister sweep runs only if the run completed cleanly AND fetched at least
`FloorAbsolute` (500 000) AND at least `FloorRelativeRatio` (0.80) of the max
previously-observed fetch. A first full run clears both; a truncated or
short run **skips the sweep** and logs `deregister-sweep SKIPPAD (<reason>)`.
A skip is a safety feature, not an error.

---

## 6. Abort + escalation

### Clean abort (there is no Hangfire dashboard — TD-17)
Stop the Worker process (**Ctrl-C**). Its HangfireServer signals the job's
CancellationToken; the refresh observes it at the next batch boundary (seconds)
and unwinds. Because the deregister sweep only runs *after* the full stream
completes, an aborted run **never sweeps** — no false deregistration. The job
lands in an aborted/failed state; the next run starts fresh (the upsert is
idempotent).

### 429 escalation
A single `429` at 6/10 s should not happen. If you see one (Worker WARN
`SCB <endpoint> svarade 429` or the breaker opening):
1. **Stop** (Ctrl-C). Do not re-trigger immediately.
2. Inspect: is another process using the same API-Id? Did SCB change the cap?
   Is the system clock skewed?
3. Only after understanding it, consider lowering the margin further (change
   `PermitLimit` to 5 in `DependencyInjection.cs` — a reviewed one-line change,
   not a config knob) and re-run off-peak.

### Known signature — 400-rejected partitions (#708, first completed run 2026-07-05→06)
The first completed population latched truncated on **40 SCB HTTP 400s** (20
`raknaforetag` + 20 `hamtaforetag`) against ~40 distinct deep-split query
instances; the sweep was correctly skipped. (The register's row gap is a
separate matter: ~105k distinct rows short of SCB's ~1.17M, dominated by the
34 protected-partition TAILS —
#641 scope — not by the 400'd cells, whose row cost was small.) Signature: WARN
`5702` lines mid-run; run ends `sweepApplied=False` with `SweepSkipReason:
truncated-or-errored` and (post-#708) `failedPartitions > 0` in the 5712
summary + `FailedPartitionCount > 0` in the audit row. Post-#708 each 5702
carries the partition descriptor + SCB's validator reason — **capture those
lines**. NB: a `rakna`-rejected cell is never fetched (the planner skips
zero-count partitions), so rakna-400s and hamta-400s are *different* cells. A
kodtabell rejection logs as its own event `5704` (dimension failure, not a
partition).

**Probe resolution (2026-07-09, #708 PR 2 — senior-cto-advisor bind):** a
9-call Klas-delegated live probe EXCLUDED every structural cause: the suspected
shapes (`Bransch` niva 3 `["00000"]` and `"2-siffrig bransch 1"` `["00"]`,
Sthlm×AB, BOTH endpoints) all return HTTP 200; an over-cap `hamtaforetag`
cleanly returns the first 2000 rows; the prefix-derived 2-digit set equals
SCB's own kodtabell (88/88, set-diff 0 both ways); an empty-`Kod` query is
structurally impossible in the planner. The 40 400s were **transient**,
attributed **by elimination** to SCB's nightly update window (the run's tail
crossed 02:00–02:50). A rate breach is ruled out: the process-wide static
6/10 s limiter caps combined outbound no matter how many executions co-run
(§0), and a genuine breach would surface as 429 — zero 429s were seen across
~35k calls. The #693 co-run was therefore a confounder only: it doubled
metered spend and ~halved throughput, and #693's fix de-risks the rerun on
those axes (plus lock hygiene) — NOT on rate, which the limiter already
guaranteed. Honest caveat: the 400 timestamps are lost, so the nightly-window
attribution is by elimination, not observed timing; if the 400s in fact fell
in the co-run window (15:55–17:04, outside any nightly window) the cause is an
unknown transient — still with no structural shape. Both legs are covered
cause-agnostically by PR-1's per-failure observability and the evidence-gated
end-of-run retry (**#712**). If 400s recur: read the 5702 descriptors, check
the run's clock window against §1 point 4, and re-run in the Saturday slot —
do **NOT** re-open a query-shape hunt without a descriptor showing a genuinely
rejected shape, and build #712 on completion-run `FailedPartitionCount`
evidence, not speculation (ADR 0091 amendment #6).

**Second data point (2026-07-11 completion run) — new evidence, theory not yet
reconciled.** The isolated Saturday run (06:30→16:53 UTC, entirely inside
daytime, crossing neither a nightly-update window nor 02:00–05:00) landed
**177** failed partitions (vs 40 on 2026-07-05) — `sweepApplied=False`, audit
row `FailedPartitionCount: 177, ProtectedPartitionCount: 35`. All 177 `5702`
descriptors carry the **identical** SCB reason `{"Message":"Ett oväntat fel
har uppstått. Försök igen eller kontakta SCB om felet kvarstår."}` (a generic
server-side error, not a validation-specific rejection). **82% (146/177) fall
in Stockholms län** (kommun-prefix `01`; kommun `0180` alone = 29) — exactly
the kommuner requiring the deepest #628 split (most simultaneous filter
dimensions, since Stockholm has the most companies). ~94% of the failures are
clustered in the first ~40% of the run's elapsed time. Since a run avoiding
the nightly window entirely still saw *more* failures than the first run,
this **weakens** (does not yet refute) the nightly-window attribution above —
kommun-order plausibly equals both iteration order and elapsed-time order
here, so "time-of-day" and "Stockholm's deep-split cells are
disproportionately fragile" are confounded in a single run, not
disentangled. Evidence only — posted to `#708` (`issuecomment-4948287799`),
no query-shape hunt opened, no code changed; the choice between building
#712 now (the uniform "try again" message is suggestive a retry would
recover most), probing the Stockholm/deep-split-complexity angle, or
re-running to see if the clustering repeats is Klas/senior-cto-advisor's to
make, not resolved here. Full descriptor breakdown in
`docs/sessions/2026-07-11-708-completion-run-dirty.md` (local).

### Known failure signature — DB contention (#688, first live run 2026-07-05)
The chain, for pattern-matching a future log: `System.TimeoutException: Timeout
during reading attempt` (Npgsql — the then-default 30 s command timeout under
fleet contention; the three raw population commands throw this bare and
unwrapped) → if the timeout instead hits the run-end **EF audit write**, EF
wraps it in `InvalidOperationException: "…likely due to a transient failure"`
(that is the NON-retrying strategy's advisory text, not an actual retry —
`EnableRetryOnFailure` is not wired, see §0) → the job fails
→ Hangfire's then-default `AutomaticRetry` restarted the ~2 h run from zero
("Retry attempt N of 10") → attempts also died at ~29.5 min elapsed = the
30-min non-sliding invisibility ceiling re-fetching a still-running job.
Result: 8 starts / 0 completions; register safe (truncated → sweep skipped).

Each leg is now closed in code: 120 s / 600 s command timeouts on the
population path, `AutomaticRetry(Attempts = 0)`, sliding invisibility. **If
this signature recurs post-#688, something NEW is wrong — capture the log and
investigate; do not just re-run.** First checks: is the run isolated (§1
point 5)? Is the dev Postgres healthy (disk, connections)? Did a heavy job
co-run anyway?

### Known signature — DisableConcurrentExecution lock takeover (#693, first live run 2026-07-05)
`[DisableConcurrentExecution]` is a distributed lock whose row carries a single `acquired`
timestamp; Hangfire.PostgreSql has **no heartbeat renewal** for it (verified against pinned
Hangfire.PostgreSql 1.21.1 `PostgreSqlDistributedLock` — the expiry SQL is `DELETE … WHERE
acquired < now - DistributedLockTimeout`), so a held lock is stealable once `now > acquired +
DistributedLockTimeout` regardless of whether the holder is alive. On the 2026-07-05 run the default
10-min `DistributedLockTimeout` let a SECOND, operator-triggered SCB execution (job 5977, a
cron-nudge on top of the boot catch-up 5906) acquire the SAME
`hangfire:ScbCompanyRegisterSyncWorker.RunAsync` lock at exactly +10:00 and co-run with the
in-flight ~11 h population. Signature:
- a SECOND `LogStarted` (5710) `startad (population/refresh)` line mid-run while the first run keeps
  heartbeating — both jobs in `Processing`;
- `SELECT resource, acquired FROM hangfire.lock WHERE resource =
  'hangfire:ScbCompanyRegisterSyncWorker.RunAsync';` shows `acquired` jumping forward to
  ~+`DistributedLockTimeout` after the first acquisition.

The co-run is SAFE (the static process-wide 6/10 s limiter caps outbound regardless of how many
executions co-run — no ban risk; idempotent upsert; per-run `synced_at` watermark; truncation latch)
but ~halves effective throughput (both runs walk the same municipalities minutes apart). On
2026-07-05 the operator deleted the duplicate via Hangfire's own Deleted-state mechanics mid-run and
the surviving run's rate doubled.

**Post-#693** `DistributedLockTimeout` is raised to 12 h (> the real ~11 h runtime), so a duplicate
can no longer steal the lock during a run — it blocks on its 4 h acquisition wait and then lands
`Failed` (`AutomaticRetry(0)`). **If you EVER see two concurrent `Processing` SCB runs after #693,
something NEW is wrong** — a second HangfireServer against the same storage, or the timeout
regressed. Capture the `hangfire.lock` row and investigate; do not delete-and-continue. The
operational rule still stands: NO manual multi-job triggering during the population window (§1
point 5).

### Envelope drift
If `fetched` is implausibly low or `excludedInvalid` is high, SCB may have
changed the `hamtaforetag` response shape. The client fails safe (marks
truncated → sweep skipped) rather than corrupting data; capture the run and
open an issue before re-running.

---

## 7. After the run

- **One-shot:** set `ScbRegister:Enabled=false` (and/or revert the Path-A cron)
  and restart the Worker so nothing re-fires. The register keeps the populated
  rows.
- **Ongoing refresh:** leave `Enabled=true` with the weekly cron. The shipped
  default is now **`0 6 * * 6`** (Saturday 06:00 UTC — **Klas-confirmed
  2026-07-09**, #708 PR 2): the only ~11 h slot consistent with §1 point 4 that
  crosses no nightly SCB update (every night except Sat→Sun) and clears the
  02:00–05:00 avoid-window. It supersedes the earlier `0 3 * * 1` (Monday 03:00),
  which was wrong on both counts — the carried #690/#693 cron question is now
  closed. The sweep then keeps the replica in step week to week.
- Record the `LogCompleted` summary + query results in the session log.

### Planner statistics — automatic after a sync, manual after a restore

A completed sync now runs `ANALYZE company_register` itself, as its last step
(#560, ADR 0119 — CLAUDE.md §3.6). It follows the 5712 run summary and is the
last line of a successful run:

```
ScbCompanyRegisterRefresher: planerarstatistik uppdaterad (ANALYZE company_register) — 871 ms.
```

**Failed job, 5712 present, no 5718** — the extract and the sweep completed and
only the statistics refresh failed (the exception names `ANALYZE
company_register`). Run the manual `ANALYZE` in step 3 below. Do **not**
re-trigger the sync: the upsert is idempotent, but a re-run costs ~11 h and the
full metered SCB call budget — which is why the worker carries
`AutomaticRetry(Attempts = 0)` (#688).

**Do not rely on autovacuum to cover the gaps.** Its analyze trigger is
change-driven, and its counters are discarded on an unclean shutdown and carried
by neither `pg_upgrade` nor `pg_dump`. The register is written by one periodic
job and read-only in between, so nothing re-arms that trigger until the next
sync. Full argument: `ScbCompanyRegisterStore.AnalyzeAsync` — it is the
canonical home and is not restated here.

**Know which statistics you are missing before you act** (verified against the
local PostgreSQL 18.3 binaries 2026-07-25 — an earlier draft of this section had
it wrong):

| | unclean shutdown | `pg_upgrade` (PG18) | `pg_dump` (PG18) |
|---|---|---|---|
| cumulative (`last_analyze`, autovacuum's trigger) | lost | not carried | not carried |
| optimizer (`pg_stats` — what the planner reads) | **survives** | **carried by default** | **omitted** unless `--statistics` |

So a PG18 major upgrade does **not** leave the table planning blind — it carries
the optimizer statistics over (`--no-statistics` opts out). What it does not
carry is extended statistics (`CREATE STATISTICS`), and
`vacuumdb --analyze-in-stages --missing-stats-only` is the right instrument
there. A `pg_dump` restore and a statistics reset are the cases that genuinely
leave the planner without statistics.

**Run this after a `pg_dump` restore or a statistics reset**, and any time the
register search is unexpectedly slow:

```sql
-- 1. Diagnose: zero rows here means the planner has NO statistics for the table.
SELECT count(*) FROM pg_stats
WHERE schemaname = 'public' AND tablename = 'company_register';

-- 2. Check when it was last refreshed (NULL on both = never, or counters reset).
SELECT last_analyze, last_autoanalyze, n_live_tup, n_mod_since_analyze
FROM pg_stat_user_tables
WHERE schemaname = 'public' AND relname = 'company_register';

-- 3. Fix. ~871 ms at 1 066 938 rows. Takes SHARE UPDATE EXCLUSIVE: it blocks
--    neither reads nor writes, but it DOES conflict with a concurrent
--    VACUUM/ANALYZE and with DDL — so do not run it against a sync in flight.
--    ANALYZE does NOT set the visibility map — only VACUUM does. Read the map
--    itself, never autovacuum_count (a resettable counter, and measured reset on
--    this database 2026-08-16): SELECT relpages, relallvisible FROM pg_class
--    WHERE relname = 'company_register'. If relallvisible is far below relpages,
--    run VACUUM too. After a restore into a database that will NEVER be synced
--    again, it is mandatory rather than conditional — §8 step 7 and its reason 2.
ANALYZE public.company_register;
```

A useful tell that the counters were reset rather than merely idle: **nearly every**
table in `pg_stat_user_tables` reads `n_live_tup = 0` with both timestamps NULL,
including ones you know hold rows. ⚠ **Not *every* — the earlier wording said so
and is falsified by the first measurement anyone took against it:** 2026-08-16 on
the dev database, **91 of 94** tables read zero while `company_register` alone
held 1 066 938 rows. Any table written *after* the reset re-arms its own counters,
so a handful reading non-zero is the normal shape of a reset, not evidence against
one. Read the pattern across tables, never one table's counter. `pg_stats`
survives a reset (it is a catalog), so step 1 and step 2 answer different
questions — run both.

---

## 8. Moving the register to the box (one-time copy, no sync there)

**Decision: option B** (`senior-cto-advisor`, 2026-08-16) — copy the populated
replica from the local dev database to the box **once**, and run **no sync on the
box**. This section is the procedure; §§0–7 remain the procedure for the local
run that produces the thing being copied.

### Why the box does not sync — measured, not preferred

Three independent facts, and each alone is sufficient:

- **`ScbRegister` appears nowhere under `deploy/`.** Regenerate:
  `grep -rn "ScbRegister" deploy/`. So the box takes the shipped configuration.
- **`Enabled` is `false` in both homes** — `src/Jobbliggaren.Worker/appsettings.json`
  and the C# property default on `ScbRegisterOptions`. Removing the key restores
  `false`. ⚠ **Do not generalise this polarity to the other ingest gate.**
  `JobSourceIngestOptions.IngestEnabled` defaults **`true`** and is held off only
  by the Worker's Production overlay — the two gates fail in opposite directions,
  and reasoning about one from the other is how a corpus lands unasked.
- **The client certificate is not on the box and nothing puts it there.**
  `ScbClientCertificateProvider` loads it from the OS certificate store
  (`X509Store(StoreName.My, …)`, `CurrentUser` by default) and it exists only on
  Klas's machine. The box could not authenticate to SCB with the flag on.

### The three things that are easy to get wrong

**1. Dump `--data-only`. The box's schema is EF's, and one index is invisible to
EF.**

`ix_company_register_company_name_lower` is a functional btree over
`lower(company_name) text_pattern_ops`, created by **raw SQL** in migration
`20260718191128_AddCompanyRegisterNameSearchIndex`. EF cannot model an expression
index, so **the model snapshot does not know it exists** — the migration says so
itself: *"no scaffolded migration will ever restore it."*

The consequence is specific. Once the migration is stamped in
`__EFMigrationsHistory` on the box, **a later `database update` will not rebuild
that index** — there is nothing left for it to replay. Confirm rather than assume
(the conclusion holds either way; this is the measurement, not a premise) — ⚠ **on
the box, in bash, never pasted into PowerShell**, because the `\"` escaping below
is a PowerShell parse error:

```bash
sudo docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -c "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '20260718191128%';"
```

Drop or replace
the table by any route other than replaying that exact migration and the index is
simply gone, while every migration reports applied and the app starts clean. The
register search then falls back to a sequential scan over the whole table.

`CompanyRegisterSearchQueryPlanTests` pins the plan **by index name** — but it
runs on an ephemeral Testcontainers database (`WorkerTestFixture`,
`[Collection("Worker")]`), so **it cannot see the box**. Nothing in CI will tell
you this happened.

`--data-only` sidesteps the whole class: the schema on the box is never dropped,
replaced or re-derived. It also keeps the ICU `swedish` collation on
`company_name` (#884) exactly as the box's own Postgres image produced it, which
is the other thing a schema-bearing restore can silently move.

**2. `VACUUM ANALYZE` afterwards — and it is two instruments, not one.**

- **`VACUUM`, because only `VACUUM` sets the visibility map, and on this box
  nothing else ever will.** The pagination count runs on **every** search
  (`CompanyRegisterSearchQuery.BuildCountCommand` → `SELECT count(*) FROM
  (SELECT 1 … LIMIT @count_cap) t`), and selecting no heap column is exactly the
  shape an index-only scan serves — but the planner skips the heap only for pages
  the visibility map marks all-visible, and a `COPY`-based restore leaves that map
  empty. Measured on the dev register **2026-08-01** (#1149), on the status-only
  count `SELECT count(*) WHERE status = 'Active'`: **438 ms with the map unset
  against 26 ms after a plain `VACUUM`**, 169 321 heap fetches to 0. That number
  belongs to that query; what generalises is the **mechanism** — every index-only
  path on the table pays the same toll.

  **Autovacuum will not close it on the box, and the reason is structural rather
  than statistical.** Its vacuum trigger is **dead-tuple** driven; the box writes
  the register exactly once, in the restore, and never again; a `COPY` into an
  empty table produces no dead tuples. So the trigger never arms and the map stays
  exactly as the restore left it — empty, permanently. Read-only forever cuts both
  ways: nothing repairs the map, so one `VACUUM` at load time is not a stopgap but
  the **permanent** fix. It also sets the per-page hint bits once under the
  operator's eye instead of charging them to whichever user query touches each page
  first — a smaller effect, and the one that would self-heal on its own.

  ⚠ **Do not reason about this from `autovacuum_count`, and do not carry the dev
  database's behaviour across.** Two measurements, both 2026-08-16 on the dev
  register: `autovacuum_count = 0` **but** 91 of 94 tables read `n_live_tup = 0`
  while holding rows, so the cumulative counters had simply been reset — the
  counter cannot tell "never vacuumed" from "counters reset" and is the wrong
  instrument. The right one is not resettable:

  ```sql
  SELECT relpages, relallvisible FROM pg_class WHERE relname = 'company_register';
  ```

  On dev that read **23830 of 23830 pages all-visible** — and the cause is
  recorded eleven lines above rather than inferred: the **manual `VACUUM` of
  2026-08-01** in #1149's own measurement, which found the map **unset** after
  four weeks of register life and set it by hand. Nothing automatic maintains it.
  No path in `src/` runs `VACUUM` at all — `ScbCompanyRegisterStore.AnalyzeAsync`
  says *"never `VACUUM ANALYZE`"* in as many words — and the sync is not a weekly
  automatic job on dev either: `ScbRegister:Enabled` is `false` in both its homes,
  the run is cert-gated, takes ~11 h, and §0 calls it *"an operator action, never
  automation"*. So dev is healthy today because a human vacuumed it a fortnight
  ago, and the box will have no such human unless this step is run.

  ⚠ **An earlier draft of this paragraph said the map was set "because the weekly
  sync upserts and arms autovacuum". That was false on three counts** — the
  measurement above, the absent `VACUUM` path, and the sync not running weekly on
  dev at all. It is recorded because it is the same error one layer on: the draft
  replaced an unsupported *statistical* claim with an unsupported *causal* one, in
  the very paragraph warning against inferring cause from an instrument that
  cannot separate two of them.
- **`ANALYZE`, because the restore carries no statistics and nothing on the box
  will ever produce them.** `pg_dump` **omits** optimizer statistics unless
  `--statistics` (the table in §7 above), and the box never syncs — so the one
  step that would otherwise refresh them (`ScbCompanyRegisterStore.AnalyzeAsync`,
  #560) never runs there. Without it the planner has no statistics for the table
  at all and the functional index above may not be chosen even though it exists.

Neither instrument is memory-hungry here. `VACUUM` sizes its dead-TID store to the
dead tuples it finds, and a freshly restored table has none, so the box's
`maintenance_work_mem` is a ceiling it never approaches; the scan reads through
VACUUM's own ring buffer rather than evicting `shared_buffers`. Regenerate:
`grep -n "maintenance_work_mem\|shared_buffers" deploy/docker-compose.yml`.

**3. Record the snapshot date. The copy's vintage is frozen from the moment it
lands.**

With no sync on the box the register never advances again — it is a point-in-time
extract, not a replica. Record, in the session log: **the dump date**, the row
count on both sides, and the `LogCompleted` summary of the local run the data came
from. An undated register cannot be told from a current one by looking at it, and
the next operator has no way to recover the vintage after the fact.

### Procedure

⚠ **The two databases do not run the same role, and getting this wrong fails at
the first command.** Local is `POSTGRES_USER: jobbliggaren`; the box is
`POSTGRES_USER: postgres` (both `POSTGRES_DB: jobbliggaren`). Regenerate:
`grep -n "POSTGRES_USER" docker-compose.yml deploy/docker-compose.yml`.

⚠ **Two shells, and the document says where the boundary is.** Steps **1-3** run in
**PowerShell on Klas's machine** — step 3 *is* the crossing. Steps **4-8a** run in
**bash on the box**, after `ssh`. **Step 8b crosses back** and is PowerShell again. That is not cosmetic — `sudo`, `<`, `\`
line-continuation and `\"` escaping are all PowerShell parse or shim failures, so
a box command pasted into PowerShell fails in a way that looks like the box being
broken. **Every box command in this section, including the
`__EFMigrationsHistory` probe under reason 1, belongs on the bash side of that
line.**

⚠ **Never redirect a `-Fc` archive through PowerShell's `>`.** Measured: native
stdout through `>` gets a UTF-16LE BOM and every byte widened
(`0x0A` → `0D 00 0A 00`), which destroys the archive. The failure is **silent at
dump time** — the file exists and looks plausibly sized — and only surfaces at
**step 5**, the restore, after it has been copied to the box. Write the file
**inside** the container with `-f`, then `docker cp` it out.

`tmp/` is the destination on purpose: this repo is **public**, and `tmp/` is
gitignored (`.gitignore:64`, "aldrig in i prod-bundle eller git-historik"). The
extract is held under Klas's signed SCB terms, so committing it would breach
those terms — a one-word slip that a history rewrite cannot fully undo.

**Steps 1-3 — PowerShell, on Klas's machine:**

```powershell
# 1. Dump data only, single table. -f writes INSIDE the container, so no
#    PowerShell redirection touches the archive. Then copy it out to tmp/.
docker exec jobbliggaren-postgres-dev pg_dump -U jobbliggaren -d jobbliggaren --data-only --table=public.company_register -Fc -f /tmp/company_register.dump
docker cp jobbliggaren-postgres-dev:/tmp/company_register.dump tmp/company_register.dump

# 2. Source row count, plus the invariant that makes this dataset safe to move.
#    Expect pnr_shaped = 0 — the legal-entities-only guard, re-measured on the
#    artefact being moved rather than inherited from the last run. The predicate
#    MIRRORS OrganizationNumber.IsPersonnummerShaped() in all three of its
#    branches: not 10 chars, not all ASCII digits (its fail-safe — Arabic-Indic
#    and fullwidth digits are 10 CHARACTERS and pass a naive length test), or
#    third character < '2'. [0-9] and not \d because it is the NARROWEST class,
#    so any deviation can only flag more rows, never fewer. (The \d ban is a .NET
#    rule — #865, \p{Nd} folds fullwidth. Asserting it of Postgres did NOT
#    reproduce: 2026-08-16 on PG 18.3/en_US.utf8 neither \d nor [[:digit:]]
#    matched Arabic-Indic or fullwidth.) A subset predicate could not detect the
#    ingest filter's own failure, which is the only scenario this exists for.
docker exec jobbliggaren-postgres-dev psql -U jobbliggaren -d jobbliggaren -c "SELECT count(*) AS rows, count(*) FILTER (WHERE organization_number !~ '^[0-9]{10}$' OR substring(organization_number from 3 for 1) < '2') AS pnr_shaped FROM public.company_register;"

# 3. Ship it, then cross to the box. Everything after this runs in bash.
scp tmp/company_register.dump jp-vps:/tmp/company_register.dump
ssh jp-vps
```

**Steps 4-8a — bash, on the box** (step 8b crosses back to PowerShell). Note the
different role (`postgres`, not `jobbliggaren`), and one command per block:

```bash
# 4. The table must already exist and be EMPTY. It is created by the migrate
#    container; confirm rather than assume, and never DROP it (reason 1 above).
sudo docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -c "SELECT count(*) FROM public.company_register;"
```

```bash
# 5. Restore. --single-transaction is kept for its --exit-on-error implication:
#    the default is to continue past errors and report them at the end, which is
#    the wrong posture when step 6 is a count. It is NOT here to prevent a
#    partial fill: --data-only --table on a non-partitioned table with a text PK
#    and no sequence yields ONE TABLE DATA entry = one COPY, and a COPY is
#    already atomic, so the partial branch cannot arise for this archive.
sudo docker exec -i jobbliggaren-postgres pg_restore -U postgres -d jobbliggaren --data-only --no-owner --single-transaction < /tmp/company_register.dump
```

```bash
# 6. Verify the count matches step 2 BEFORE step 7 — a short restore that is
#    then vacuumed and analyzed looks healthy.
sudo docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -c "SELECT count(*) FROM public.company_register;"
```

```bash
# 7. Both instruments, one statement — VACUUM cannot run inside a transaction
#    block, which is why this is psql -c and not part of step 5.
sudo docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -c "VACUUM ANALYZE public.company_register;"
```

```bash
# 8a. Remove the box's copy. Still bash, still on the box.
rm /tmp/company_register.dump
```

**Step 8b — back in PowerShell.** The procedure creates the artefact in **three**
places, and step 8a removes one. It is an SCB extract held under Klas's terms and
has no reason to persist once the rows are in, so clear the other two:

```powershell
docker exec jobbliggaren-postgres-dev rm -f /tmp/company_register.dump
Remove-Item tmp/company_register.dump
```

*The images differ in **minor** version — local `postgres:18.4`, box
`postgres:18.3` (regenerate: `grep -n "image: postgres" docker-compose.yml
deploy/docker-compose.yml`). For a `--data-only` archive that is a non-issue: the
payload is `COPY` data and the archive format does not change within a major
version. It would stop being a non-issue across a **major** divergence, where the
dump must be taken with the older server's client.*

### Verify, and verify the index specifically

The count matching is necessary and not sufficient — the index is the thing that
fails silently.

```sql
-- a. The functional index exists and is VALID. indisvalid = false is the
--    aborted-CONCURRENTLY signature the migration documents.
SELECT i.relname, x.indisvalid, pg_get_indexdef(x.indexrelid)
FROM pg_index x JOIN pg_class i ON i.oid = x.indexrelid
WHERE i.relname = 'ix_company_register_company_name_lower';

-- b. Statistics exist. Zero rows here means step 7 did not happen.
SELECT count(*) FROM pg_stats
WHERE schemaname = 'public' AND tablename = 'company_register';

-- c. ELIGIBILITY, not choice: can the index serve the predicate's shape at all?
--    Expect an index scan naming the index above rather than a Seq Scan.
EXPLAIN SELECT organization_number, company_name FROM public.company_register
WHERE lower(company_name) LIKE lower('volvo%') ESCAPE '\' LIMIT 20;
```

⚠ **Query (c) is a hand-typed lookalike and proves eligibility only — never read
it as proof that the plan the app runs is correct.** Production emits a different
shape: `CompanyRegisterSearchQuery.BuildItemsCommand` has **two branches** —
`ShouldMaterialize` decides, and browse-all/broad prefixes take the
unmaterialised one. On the materialised branch it wraps the predicate in a
`WITH … AS MATERIALIZED` CTE, and inside that CTE the index renders as
`Bitmap Index Scan on ix_…`, not `using ix_…`. ⚠ **The CTE carries no `ORDER BY`
and no `LIMIT`** — ordering and pagination live in the **outer** query only, and
an inner `LIMIT` would take an arbitrary subset and then order it, which
`CompanyRegisterSearchQueryCompositionTests` pins as the **broken** variant. That method is `internal` precisely so
`CompanyRegisterSearchQueryPlanTests` can EXPLAIN **the real command** rather
than a retyped one — a re-typed query is not an oracle. Plan **choice** is owned
by those tests and by `CompanyRegisterSearchPlanChoiceTests` (ADR 0119); this
runbook checks only that the box has an index the shape can use.

Query (a) is the one worth keeping: it is the only check that distinguishes "the
data arrived" from "the search still works", and those two are exactly what
`--data-only` decouples.
