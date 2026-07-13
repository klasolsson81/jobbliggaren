# Runbook — measuring Jobbliggaren against the ADR 0045 budgets

> **Purpose:** answer the question *"is anything over budget?"* without hand-writing a query
> each time. This runbook is the operational face of
> [ADR 0045](../decisions/0045-performance-budget-and-fitness-functions.md) — the budgets
> themselves live there and are **not restated here** (two sources of truth is how budgets
> drift). This file tells you how to *read* them.
>
> Every mechanism here is **observe-only** (ADR 0045 Beslut 5). Nothing in this runbook
> blocks CI. Flipping any of it to blocking is a Klas ratchet (Beslut 6) — see §E.

**Sections**

- [Prerequisites](#prerequisites)
- [§D — Per-query EF measurement session](#d--per-query-ef-measurement-session)
- [§E — What to do when a budget is exceeded](#e--what-to-do-when-a-budget-is-exceeded)

---

## Prerequisites

Structured logs go to **Seq** (TD-104 / Pre-4 STEG 6). The sink attaches only when
`Seq:ServerUrl` is configured — without it, both hosts stay console-only and none of the
queries below have anything to run against.

```bash
docker compose up -d seq          # http://localhost:5341
```

Dev already sets `Seq:ServerUrl=http://localhost:5341` in both hosts'
`appsettings.Development.json`. Run the Api and the Worker, generate traffic (or let a sync
run), then query in the Seq UI.

> **Query on `@MessageTemplate`, never on `EventId`.** EventIds are **not unique** across this
> codebase — 5601/5602, 5701–5703 and 6001/6002 are each used by two or three unrelated
> classes. A signal keyed on an EventId will silently match events from a job you were not
> asking about. The message template is the stable key.

---

## §D — Per-query EF measurement session

### Why this is off by default

EF Core logs **one `Executed DbCommand` event per SQL statement** at Information. That is a
useful instrument and an unusable default:

- The Platsbanken snapshot upserts item by item, with a **child DI scope per item** (ADR 0032
  §5) — roughly 47 000 items per run. At Information, a single sync buries the log under
  **100 000+** statement events.
- The same child-scope shape emits ~47 000 **`ContextInitialized`** events, which live in a
  *different* category (`Microsoft.EntityFrameworkCore.Infrastructure`). Silencing only
  `Database.Command` halves a flood that has two sources.

So both categories ship at `Warning` in both hosts' base `appsettings.json` (#752, perf-audit
finding `g2`), and both survive into Development. Pinned by
`tests/Jobbliggaren.Architecture.Tests/EfCoreLoggingConfigurationTests.cs`, which runs the
shipped config through the real MEL filter engine.

**Failed SQL still reaches the log.** `CommandExecuted` is Information; `CommandError` is
**Error**. Warning silences the success chatter, not the failures.

### Turning it on for a session

Both hosts load a gitignored `appsettings.Local.json` last, so a measurement session needs no
committed change and cannot leak into anyone else's environment:

```jsonc
// src/Jobbliggaren.Worker/appsettings.Local.json   (or .../Jobbliggaren.Api/)
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

MEL resolves a category by **longest matching prefix**, so this re-enables exactly the
per-query duration signal and leaves the `ContextInitialized` flood silenced.

In a container, use the environment-variable form — and note the footgun:

```bash
Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information
```

Colons become `__`. **The dots stay dots.** The reflex to convert the dots to underscores as
well produces a key that binds to nothing and fails silently.

### What to query

```sql
-- Per-query duration, slowest first. Requires the override above.
select count(*) as n, percentile(elapsed, 95) as p95_ms, max(elapsed) as max_ms
from stream
where @MessageTemplate like 'Executed DbCommand%'
group by @Properties['commandText']
order by p95_ms desc
```

Read the result against ADR 0045 Beslut 1 — but read it as a *component* cost, not a verdict:
the budgets are stated per **handler** (server-side handler latency, which is what
`LoggingBehavior` measures), not per statement. A slow statement is a lead, not a breach.

### Turning it off

Delete the `appsettings.Local.json` entry (or unset the env var) and restart the host. Leaving
it on is not harmful in a short dev session, but it will drown the next sync you run.

### PII guard-rail — read before you widen the logging

> **Never enable `EnableSensitiveDataLogging` to obtain parameter values.**

EF redacts parameters by default (`@p0='?'`), and that redaction is load-bearing. These SQL
statements carry CV content, parsed CV text, e-mail addresses and tokens — all of which
CLAUDE.md §5 forbids logging in plaintext, and all of which would land in Seq. A measurement
session needs **durations**, not **values**. If you find yourself wanting the parameter values
to reproduce a query, reproduce it against seeded data instead.

---

## §E — What to do when a budget is exceeded

1. **Confirm it is real.** One slow sample is not a breach; the budgets are p95 (ADR 0045
   Beslut 1). Widen the window and re-run the query before concluding anything.
2. **Fix the regression, or write down why it is acceptable.** CLAUDE.md §2.5: regressing
   against budget requires a fix or a STOPP justification. It is the same discipline as
   lowered test coverage.
3. **Never silently raise the budget.** A cap change is a **dated in-file amendment to ADR
   0045**, reviewed like any other architectural decision. Bumping the number in config
   because the measurement exceeded it is the Goodhart move a fitness function exists to
   prevent — the instrument would then be measuring its own tolerance.
4. **Ratcheting observe-only → blocking** (Beslut 6) is a Klas decision and requires a stable
   distribution over several green runs on consistent hardware. `docs/runbooks/e2e-ci.md`
   documents the same ratchet lever for the E2E workflow.
