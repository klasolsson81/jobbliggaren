# ADR 0158 — The Server card's host readings are sampled in the API process from procfs and statfs, not over the host bridge

**Date:** 2026-10-10
**Status:** Accepted
**Decider:** Klas Olsson, through his request of 2026-10-10 in issue #1982 ("Server-delen"). `senior-cto-advisor` ruled
the design (`docs/reviews/2026-10-10-1982-server-cto.md`: Q1 the mechanism, Q3 the window, Q4 the wire, Q5 the disk,
Q6 the composition, Q7 this ADR and the amendment, Q8 no GO; its conditions 1–10 and residuals r1–r5 are this ADR's) ·
a planning review (the Plan agent) tested the design before the CTO ruled.
**Amends:** ADR 0150, by its Amendment of 2026-10-10 and a dated pointer in its header (the surface contract of the
Server card only). Nothing is rewritten.
**Related:** [#1982](https://github.com/klasolsson81/jobbliggaren/issues/1982) (the issue this ADR ships under) ·
[#1972](https://github.com/klasolsson81/jobbliggaren/issues/1972) (the epic) · ADR 0150 (D2: honest unavailability;
Amendment 2026-10-08: the overview's observations) · ADR 0154 (§3: every merge is a deploy; §4: what needs Klas's GO) ·
ADR 0157 (the host-collector bridge under `/run/jobbliggaren/observations`, the Backup sub-track; cited by number, not
merged when this ADR was written) · ADR 0045 (the read budget) · ADR 0009 and AGENTS.md §2.1 (ports) · CLAUDE.md §9.2
(the read rule)
**Measured against:** `origin/main` at `ad4bccbdd`; the box (`jp-vps`), read-only, 2026-10-10.

> **Provenance.** Written by `adr-keeper` for the driving session, from its brief and the CTO's report (CLAUDE.md §9.2,
> §13). The mechanism, the guards, the residuals and the lapse trigger are the CTO's ruling; the mandatory agents
> review them in the PR. The runbook `docs/runbooks/admin-host-observations.md` is the one home for the definitions
> and for checking them against the box; this ADR does not repeat its numbers.

---

## Context

The Server card of the `/admin` overview shows three readings of the host the stack runs on: CPU, memory and disk.
ADR 0150's Amendment of 2026-10-08 left host and services unavailable. The Backup sub-track meanwhile builds a host
collector that writes observations under `/run/jobbliggaren/observations` for the API to read (ADR 0157), and the order
of work ("Backup first") assumed that Server would ride the same bridge.

Measured on the box on 2026-10-10, read-only, numbers only (CLAUDE.md §9.2 (a)):

- The API container runs under runc, on cgroup v2, with no `lxcfs`, as the user `app`, with a cgroup memory limit of
  1 073 741 824 B (1 GiB).
- Inside it, `/proc/meminfo` reports the host's `MemTotal` (identical), `/proc/stat` carries counters within seconds
  of the host's, and `statfs('/')` on the overlay root reports the host root filesystem, `/dev/vda4`. That filesystem
  also holds `/var/lib/docker`, `/var/lib/containerd` and `/var/lib/jobbliggaren`.
- `/run/jobbliggaren` has a recursive `auditd` read watch. `/var/lib/jobbliggaren` is mounted in no container.
- A compose change gives a `DEPLOY_FILES` hash mismatch, so reconcile refuses until the box's checkout is advanced
  (ADR 0154's accepted cost).

That a container reads the host's counters is a kernel property under runc, not an accident of this deployment: no
namespace (pid, mount, cgroup, user) virtualises `/proc/stat` or `/proc/meminfo`. Only something mounted over them (`lxcfs`) or a runtime other
than runc (gVisor, Kata, Sysbox) changes what a container reads. The question is therefore where to read the host from,
and whether a second channel to it is worth what it costs.

## Decision

**The API process samples the host itself, from procfs and `statfs`, and serves the newest sample from memory.** The
Server card does not use the host bridge.

### D1 — The mechanism

- `HostObservationService`, a `BackgroundService` in the Api (`PeriodicTimer`), takes the first sample at once and then
  one every `HostObservation:SampleIntervalSeconds` (default 30 s; validated 5–300).
- It calls `HostObservationSampler` (Application), which reads through the Application port `IHostObservationProbe` and
  keeps the newest result as an immutable in-memory snapshot. The adapter is `ProcFsHostObservationProbe`
  (Infrastructure). Paths (`/proc`, `/sys/fs/cgroup`, `/proc/self/mountinfo`, `/`) are code constants, never
  `IOptions`; the test seam is a constructor-injected root. Reads are bounded to 64 KiB and no file content reaches a
  log line or an exception.
- The port speaks in cumulative idle and busy CPU time, bytes and a verdict, never in procfs column names; the grouping
  lives in the adapter and is pinned by parser tests over fixtures captured on the box.
- `GET /api/v1/admin/overview/host` (`AdminHostObservationEndpoints`, its own file) sits behind the Admin policy and
  `IAdminRequest`, on the `admin-read` bucket, answers `private, no-store`, and reads the snapshot. It does no I/O.
- Composition is the Api's alone (`Api/Hosting`): a hosted service is a host-lifecycle detail, the Worker has no admin
  surface, and nothing goes into `AddPersistence` or `AddApplication`, which both hosts load. Infrastructure supplies the
  adapter only.
- The first sample and every tick run inside the nested guard of the Worker's `WorkerMemoryTrendService`, so the one
  risk of a hosted service, the default `StopHost` behaviour, cannot stop the API (a test shows a throwing probe never
  faults the host).

### D2 — What a reading means (the runbook §2 and §4 hold the definitions)

- **CPU** is `busy / (busy + idle)` over the difference of two consecutive samples, over all vCPUs. `steal` counts as
  busy, `iowait` as idle, and the two `guest` columns are left out (the kernel already counts guest time in `user` and
  `nice`). The window is the monotonic time between the two reads (`Stopwatch`), never the wall clock, which steps. A
  window under half the interval is skipped and the previous reading keeps its own `sampledAt`; one over three times the
  interval is `Failed` and re-baselines. `StaleAfterSeconds` (default 120) must be more than three times the interval,
  validated at start.
- **Memory** is `MemTotal − MemAvailable`, never `MemFree`.
- **Disk** is `df`'s arithmetic: `used = Size − TotalFree`, `free = Available`, `total = used + free`, so the total is
  not `df`'s Size and the meter and its two numbers share one denominator. The heartbeat's disk alarm reads `df`'s
  `Use%`, so the card and the alarm cannot disagree about the same disk.
- **States** are the closed union Available, Stale, Collecting, NotObservable and Failed. A value and `sampledAt` exist
  only in Available and Stale; otherwise they are `null`, never 0. A failed tick does not carry the previous value
  forward as new. NotObservable means built, but this deployment cannot supply it (no procfs on a developer's Windows
  machine, or a `/proc` that does not describe the host); it is never "Kommer snart".
- **The wire** carries states, instants and numbers only: no reason, path, device or free text. Why a reading is missing
  stays in the snapshot, in edge-triggered logs and in tests.
- **Percentages** are computed once, on the server. **Clocks:** `sampledAt` is the injected wall clock read once per
  tick after the reads, and `readAt` is the wall clock at the request. The header `X-Admin-Sampled-At` carries `readAt`,
  the API read instant; each reading's own `sampledAt` is in the body.

### D3 — The host-view guard

The adapter looks for the two ways its premise stops holding. If either finds something, CPU and memory are
NotObservable and disk is unaffected.

1. **Structural.** `/proc/self/mountinfo` shows a mount at `/proc/stat` or `/proc/meminfo`, or `/proc` is not of type
   `proc`.
2. **Heuristic.** `memory.max` is a finite number and `MemTotal` is not larger than it.

A missing or unreadable file, and `max`, cannot disprove host scope, so sampling proceeds. The heuristic alone would
catch `lxcfs` only while a memory cap exists; the structural check does not depend on one. A container started with
`--cgroupns=host` is not a correctness residual: under runc the values are still the host's, and the flag only blinds
the heuristic.

### D4 — Residuals and the lapse trigger

The premise is a deployment fact that is checked, not proved. The residuals are written here so they are not discovered
later:

- **r1.** A sandboxing runtime (gVisor, Kata, Sysbox) is not detected.
- **r2.** The heuristic fails safe in the other direction: a memory cap at or above the host's RAM withholds the CPU and
  memory meters.
- **r3.** Disk means the filesystem under the container's writable layer, which is Docker's storage. Today that is the
  host root.
- **r4.** About 30 s of Collecting for the CPU after every API recreate, which is every release.
- **r5.** One API replica: the snapshot is per process.

**One lapse trigger, one reader.** This decision must be re-examined, and the card treated as unverified, when any of
these changes on the box: the Docker daemon's runtime or default runtime; `lxcfs` is installed; Docker's data-root moves
off `/`; or the box runs cgroup v1 (the heuristic reads `memory.max`, a cgroup v2 file, and would fall silent). The
reader is Klas: a change of the daemon's runtime is a one-off write on the box, which needs his GO (ADR 0154 §4), and a
compose `runtime:` change arrives through a PR and shows in its diff. What to re-run is `docs/runbooks/admin-host-observations.md` §8 (a host reading,
a second `/proc/stat` and the boundary proof from inside the API container).

### D5 — Image-only

The release adds no compose change, host unit, mount, migration or dependency. Any diff under `deploy/` in the PR that
carries this ADR is a STOPP back to the CTO.

## Alternatives considered

- **A. The in-process sampler (chosen).** One writer and one reader in the same process, no host artifact, no channel
  to validate. Its costs are r1–r5 and the dependence on a runtime fact, which D3 and D4 bound.
- **B. A host collector, a systemd timer and an atomic JSON file in the Backup bridge, mounted read-only into the API.**
  Rejected. Its costs, as the CTO weighed them: a new unit and timer, installed by hand; a compose bind, which freezes
  delivery; a 30 s writer under the recursive `auditd` read watch on `/run/jobbliggaren`; CPU state carried between timer
  runs, either in a file or as a sub-second window inside one run; and a second trust boundary, a host-written file the
  API must schema-validate. B's independence from container semantics is itself bought with a bind mount, which is
  container semantics. What B buys, CPU readings that survive an API recreate (about 30 s of Collecting per release),
  does not pay for that (YAGNI, KISS). The CTO's ruling on "reuse the bridge": it is honoured wherever there is something
  to reuse (the loader, retention, the header contract and the state vocabulary), and the transport is not reused because
  Server does not need it.
- **C. An on-demand two-point `/proc/stat` read in the request handler.** Rejected: the window would be sub-second and
  noisy, the request would pay the latency, and the state would live per request instead of in one sampler.
- **D. A jiffy cross-check of the window.** Rejected: it assumes `USER_HZ` = 100 and a stable CPU count. What it served
  is whether a window is representative, not whether the value is correct; the percentage is a tick ratio and does not
  depend on the window's length, and the monotonic bound of D2 answers the representativeness question.
- **E. Stale as a boolean flag, and reason codes on the wire.** Rejected. In a closed union the state decides whether a
  value is present, so "Failed but stale" cannot be expressed; a flag allows it. No card copy needs a reason, and Klas's
  instruction of 2026-10-10 limits the browser to "de överenskomna numeriska värdena, observationstiderna och
  tillstånden".

## Consequences

### Positive

- An image-only release: no unit installed by hand, no compose bind to freeze delivery, no writer under the `auditd`
  watch, no host-written file to validate. The sampler reads files the container can already read and needs no extra
  capability.
- Each reading carries its own state, so one failing reading never blanks the other two (ADR 0150 D2).
- Cost per sample is four small, bounded file reads and one `statfs` every 30 s; the endpoint does no I/O. Measured
  numbers belong in the runbook §9, taken when measured.
- The runtime facts the design stands on are named, with one trigger that says when to look again.

### Negative

- Every release recreates the API container, so the CPU reads Collecting for about one interval each time (r4).
- One replica (r5): a second would answer from its own sampler.
- CPU and memory stand on a runc fact that is guarded, not proved (r1), and the guard can withhold the meters when it
  should not (r2).
- Disk is the filesystem under Docker's storage, not "the host's disks" (r3).
- Windows development reads NotObservable for all three readings. That is correct for that environment, and no warning
  is logged.
- A new hosted service runs inside the production API process (ADR 0154 §3: the merge is its approval). The browser
  cannot be told why a reading is missing; the operator reads the log.
- The sentence of ADR 0150's 2026-10-08 Amendment that host "remain[s] unavailable" is superseded for the Server card
  only (ADR 0150, Amendment 2026-10-10).

## Implementation

One vertical slice in one PR (CTO Q2): the backend, the card, the runbook, this ADR and the ADR 0150 Amendment. Tests
cover each guard outcome over fixtures, the non-Linux path, the hosted service not faulting the host, and a Linux-only
smoke test of the real adapter that asserts shape only. There is no migration, dependency or deploy file. Reading `/proc`
and `df` on the box is allowed under CLAUDE.md §9.2 (a); the signed-in card is Klas's to check (§9.2 (d)). Host metrics
are not personal data, and nothing is retained beyond the newest in-memory snapshot (DoD §8.8).

## References

- `docs/runbooks/admin-host-observations.md` · `docs/reviews/2026-10-10-1982-server-cto.md`
- Code: `src/Jobbliggaren.Application/Admin/HostObservations/` · `src/Jobbliggaren.Infrastructure/Diagnostics/`
  (`ProcFsHostObservationProbe`) · `src/Jobbliggaren.Api/Hosting/` (`HostObservationService`,
  `HostObservationRegistration`) · `src/Jobbliggaren.Api/Endpoints/AdminHostObservationEndpoints.cs`
- ADR 0150, 0154, 0157, 0045, 0009 · AGENTS.md §2.1 · CLAUDE.md §9.2
- Microsoft Learn, "Breaking changes in .NET 10", "BackgroundService runs all of ExecuteAsync as a Task" (read
  2026-10-10; the link may rot): https://learn.microsoft.com/en-us/dotnet/core/compatibility/10
