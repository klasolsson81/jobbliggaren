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
  batches / rows-so-far, so a healthy ~1–3 h run is never silent.
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

Expected duration at 6/10 s: **~1.5–3 h**. Longer than at 9/10 s by design —
Klas accepted the extra night-time minutes in exchange for the wider margin.

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
4. **Time:** budget ~1–3 h uninterrupted; run it at night per the cron
   default rationale (lowest DB contention) — but **avoid 02:00–05:00 UTC**
   (see point 5).
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
- It is `DisableConcurrentExecution(4h)`-guarded — it can never overlap itself.
- With `Enabled=true` + a valid thumbprint you should see the cert load on start
  and **no** `Enabled=false — no-op` line.

You only need the **Api** running too if you use the admin-endpoint trigger
(Path B, §4). The cron-nudge trigger (Path A) needs the Worker alone.

---

## 4. Trigger the run

Two paths — pick one. For a first controlled run, **Path A** is simplest
(Worker only, no auth).

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
  durationMin=…`. This is the run summary; capture it.

### Verification queries (psql against the dev DB)
```sql
-- 1. Total rows — expect ~1,173,778 (±, register drifts week to week).
SELECT count(*) FROM company_register;

-- 2. Lifecycle breakdown — Active dominates; Deregistered only if the sweep ran.
SELECT status, count(*) FROM company_register GROUP BY status ORDER BY 2 DESC;

-- 3. Personnummer spot-check — MUST be 0. Mirrors IsPersonnummerShaped
--    (a legal-entity org.nr is 10 digits with its 3rd digit >= '2').
SELECT count(*) AS pnr_shaped
FROM company_register
WHERE length(organization_number) <> 10 OR substring(organization_number, 3, 1) < '2';

-- 4. Durable audit row (payload carries fetched/upserted/deregistered/
--    excludedPnr/sweepApplied). audit_log is day-partitioned (ADR 0024).
SELECT occurred_at, event_type, payload
FROM audit_log
WHERE event_type = 'CompanyRegisterSynced'
ORDER BY occurred_at DESC
LIMIT 1;
```

**Pass criteria:** count near ~1.17M, query 3 returns **0**, the audit row shows
`TotalRowsFetched` ≈ the row count and `SweepApplied` = the expected value
(false on the very first run if no prior baseline cleared the floor; true on
steady-state re-runs).

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
- **Ongoing refresh:** leave `Enabled=true` with the default weekly cron
  (`0 3 * * 1`, Monday 03:00 UTC — matches SCB's own weekly register update).
  The sweep then keeps the replica in step week to week.
- Record the `LogCompleted` summary + query results in the session log.
