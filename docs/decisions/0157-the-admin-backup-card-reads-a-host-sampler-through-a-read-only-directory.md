# ADR 0157 — The admin Backup card reads a host sampler through a read-only directory

**Date:** 2026-10-10
**Status:** Accepted
**Deciders:** The driving session of #1982, under the scope Klas Olsson set for the admin surface (epic #1972; ADR 0150 D4
gives #1982 "services, host and backup"). Klas reviews the diff post-merge (CLAUDE.md §9.2), and D6's one-off writes still
need his GO. Inputs: the design sanity review of 2026-10-10 by `security-auditor` and `dotnet-architect`; their reports are
not tracked files, so D1, D2 and D6 describe the outcome and do not link it.
**Amends:** ADR 0150 (a dated amendment: the Backup card reads an observation; D4's #1982 row is flipped for Backup only)
**Related:** ADR 0150 (D2 honest unavailability, D4 capability map) · ADR 0154 (§4, the reading of A2) · ADR 0143 (read-only
mounts per service) · ADR 0125 (the backup this reports on) ·
[#1982](https://github.com/klasolsson81/jobbliggaren/issues/1982) (the issue this ADR ships under) · #1972 (the epic) ·
#2064 (the overview's pre-authorisation fan-out) · #197 (the backup) · runbook
[`host-observations.md`](../runbooks/host-observations.md), the operational contract and the single home of the field tables
**Measured against:** `origin/main` at `ad4bccbdd`, 2026-10-10, the base of the #1982 branch.

> **Provenance.** Written by `adr-keeper` for the driving session, from its brief (CLAUDE.md §9.2, §13). The decisions are
> the driving session's design under Klas's scope. `security-auditor`'s review of 2026-10-10 raised two Majors, both closed
> in the design (D1: an unprivileged sampler account instead of root; D6: the collector exits 0 on every path, so the unit
> stays off `systemctl --failed`). `dotnet-architect`'s points shaped the reader (D2: asynchronous I/O, an explicit token
> switch, golden files, no catch-all, a registration file of its own). The mandatory agents review the PR (CLAUDE.md §9.2).

---

## Context

**What the card could not say.** ADR 0150 D4 gives #1982 "services, host and backup", and D2 forbids a backup claim without
an observation behind it. Until now the Backup card showed an en-dash in every row under one "Kommer snart" line, because
nothing in `src/` observed the host or the backups (ADR 0150, Context, measured at `7c21f8117`).

**The two facts are on the host, not in the container.** Whether the last nightly run succeeded is the stamp
`/var/lib/jobbliggaren/last-successful-backup`, which `jobbliggaren-backup.sh` writes last, on its success path. When the
next run is due is `NextElapseUSecRealtime` of `jobbliggaren-backup.timer`, which only systemd holds. The `api` container
mounts neither the stamp's directory nor the bus.

**The constraints.** The one box is production (ADR 0154). `/run/jobbliggaren/host-secrets` holds the backup's upload
credential and is mounted into no container (`DeployComposeHostBridgeTests` pins that). The process that serves admin
requests must not gain a host permission to answer a metadata card; the house practice is a read-only mount per service
(ADR 0143). A host that has not reported yet is neither an error nor an unbuilt feature (ADR 0150 D2). And every merge goes
live (ADR 0154), so anything that touches compose, a unit or an account has a delivery cost (D6).

## Decision

Seven decisions govern the Backup card's data. The field-by-field contract is in the runbook and is not restated here.

### D1 — Transport: a host sampler publishes a file, and `api` reads it through a read-only mount

A systemd timer runs `jobbliggaren-observe.service` every minute: `Type=oneshot`, as the system account `jbl-observe` (no
shell, no home, no groups), sandboxed with `ProtectSystem=strict`, `PrivateNetwork=yes`, `RestrictAddressFamilies=AF_UNIX`,
an empty `CapabilityBoundingSet`, and `InaccessiblePaths` over `/run/jobbliggaren/host-secrets`,
`/run/jobbliggaren/secrets` and `/etc/jobbliggaren`. It publishes one small JSON file per source into
`/run/jobbliggaren/observations` (tmpfs, `0755 jbl-observe`) by writing a temporary file in the same directory and renaming
it. The `api` service, and only it, mounts that directory read-only at `/run/observations`, and `HostBridge__Directory` names
the mount. The browser sends nothing that reaches the host, and no SSH key, Docker socket or host permission enters the
application.

The mount carries `create_host_path: true`, a deliberate deviation from the two Redis credential mounts beside it (ADR 0143;
`false`, so that a missing credential directory fails loudly). The directory holds non-secret, world-readable metadata, so
Docker's root-owned default is harmless, and a recreate of `api` that runs before the directory exists must not stop the
application from starting over a metadata card. The tmpfiles entry then hands the directory to `jbl-observe`.

*Why.* A one-way file drop is the narrowest channel that carries a fact across the container boundary: the application gains
the right to read one directory of timestamps and tokens, and nothing it could use to act on the host. The writer's identity
is the control, because it bounds what a collector bug could ever publish into a mount an API compromise can read. The
unprivileged account cannot read the credential directories, and the unit makes them inaccessible so that a later change of
user cannot undo that quietly. `ProtectSystem=strict` alone would not do it (Alternatives).

### D2 — Contract v1: one envelope, a strict reader, and golden files both sides are tested against

Each file is `{"schema":1,"source":…,"sampledAt":…}` plus exactly one of `data` and `error` (a closed token). The API's reader
is strict, and every rule is a typed refusal:

- the directory is never listed: each source has one fixed file name;
- the file is regular, not a symlink, at most 16 KiB, and the cap is enforced on the bytes read from the open handle;
- valid UTF-8 and JSON, depth-capped, with no comment, no trailing comma and no repeated name;
- exactly the contract's keys, and every instant exactly `yyyy-MM-ddTHH:mm:ssZ`;
- wire tokens mapped by an explicit switch, never `Enum.TryParse`;
- nothing of the file, and no exception message, reaches a log or a response.

**Content rule.** The directory carries only timestamps and closed tokens, fit for any admin to read. A source that needs
more is a new contract version, reviewed by `security-auditor` before it is built. The golden files under
`deploy/systemd/fixtures/observations` are produced by the sampler's own shell suite (compared byte for byte) and parsed by
the API's tests, so the two sides cannot drift.

*Why.* A file written by one process is trusted by another only after it has been validated, and a shell script and a C#
parser that agree by luck will one day not. The golden files make the contract one artefact with two readers. The content
rule is what makes a read-only mount of this directory safe to hand to the API at all.

### D3 — Time semantics: the host says when it looked, the API judges against its own clock

The stamp's content is the run's **start** (UTC, compact) and its mtime is the run's **end**: the instant the backup script's
own `--check` measures its 26 h threshold from. The browser receives `completedAt` (the mtime) and never the start, which the
API uses only to refuse a stamp whose two times are not a run. `sampledAt` is the host's clock, taken after the facts are
read, and the API never replaces it with the time of the call. The response header `X-Admin-Sampled-At` is the moment of the
response; the BFF takes the body's `observedAt` as the observation time. The API judges:

- **overdue** = API now − `completedAt` > 26 h, against *now* and not against the sample, so a stopped sampler cannot leave
  "not overdue" standing;
- **stale** = the observation is more than 5 min old against the same clock; a sample dated more than 60 s ahead of now is
  refused and its time is not shown;
- **`nextRunAt`** = `NextElapseUSecRealtime`, which includes `RandomizedDelaySec`. Measured on the box on 2026-10-10 against
  the reconcile timer: `OnCalendar` at :47 with `RandomizedDelaySec=3min` showed 15:49:41.

Tests tie 26 h to `MAX_STAMP_AGE_SECONDS` and 5 min to `OVERVIEW_STALE_MS`, and keep the evaluator's 2 h run span above the
backup unit's `TimeoutStartSec`.

The box's zone is Europe/Berlin (measured 2026-10-10), so `OnCalendar=*-*-* 02:15:00` is local time: 00:15 or 01:15 UTC. The
earlier text said "UTC". It is corrected in the live artefacts (the timer's comment, `backup-restore.md`); ADR 0125 keeps
"02:15 UTC" as historical text, since an ADR is not edited. The wire and the API carry UTC only.

*Why.* "Overdue" must mean what the box's own check means, or two alarms disagree about one stamp. The sample's age and the
run's age are two facts: measuring the run against the sample would leave "not overdue" standing after the sampler died.
`NextElapseUSecRealtime` is the instant systemd has actually armed, jitter included, which a re-derived `OnCalendar` is not.

### D4 — Cadence, cost, and how the overview reads it

The sampler runs every 60 s (`OnBootSec=30s`, `OnCalendar=minutely`, `AccuracySec=10s`, not `Persistent`: a sample describes
now, and a catch-up run would publish one taken later than the interval it stands for). The overview's existing 60 s
visibility-aware poll and its 5-min staleness are reused. The Backup read is one request, made **after** the three existing
reads have passed their 401/403 check, so the work an unauthenticated caller can start stays at three requests (#2064). An
ordinary refresh failure keeps the last value and marks it at once; a 401 or 403 from the new source clears everything.
Per sample the host runs one `systemctl show`, two file reads and one rename; per poll the API reads one file of at most
16 KiB. The runbook (§3) gives the commands that regenerate the cost; this ADR holds no live figure.

*Why.* Reuse beats a second mechanism (ADR 0150's amendment of 2026-10-08, AGENTS.md §4): five missed samples are what an
admin already sees as stale, and the order of the reads keeps the pre-authorisation fan-out of #2064 from growing.

### D5 — States, rows, and what the card does not claim

The card distinguishes available, loading, **awaiting**, failed and old. Awaiting is a host that has not reported (no
directory configured, or no file in it yet): a quiet status line, not an alarm and not "Kommer snart", because the source
is built. The rows are *Senaste lyckade körning*, *Extern kopia*, *Nästa planerade körning* and *Behålls*. *Extern kopia*
and *Behålls* keep saying "Saknar verifierad datakälla" in every state. A recorded run is never shown as proof of a working
restore: the card says it shows when the run last finished, and the restore drill owns the other claim
(`backup-restore.md` §6).

*Why.* ADR 0150 D2 says "Kommer snart" only of a capability that is not built. A source whose host has not reported is
built, and until the sampler is enabled that is the expected state, not an error. The box holds no private key by design,
so nothing on it can show that an artefact decrypts or restores.

### D6 — Operations and authority

The sampler is not one of the heartbeat's floor timers: its only reader is the card, which shows a stopped sampler as an old
observation, and the collector exits 0 on every path so that a cosmetic unit never lights `systemctl --failed`, the alarm
surface the heartbeat pages from. The install is the runbook's §5 block. Under ADR 0154 §4 (A2) the merging session may
advance the box's checkout and refresh a changed unit file (`install`, `daemon-reload`); **creating the account, the
tmpfiles entry and enabling the new timer are one-off writes that need Klas's GO for exactly those lines.** Compose is
release-bound, so this release's compose change freezes delivery until the checkout is advanced: every reconcile refuses
and prints the command. An agent may read `backup.json` on the box (timestamps and tokens, CLAUDE.md §9.2 (a)); a
signed-in look at the card is Klas's (§9.2 (d)).

*Why.* ADR 0154 §4 draws A2 around what a merged release contains and leaves one-off box writes to Klas. A new account is
the clearest case of the second kind.

### D7 — Scope boundary: this bridge is for what only the host can see

Facts the container can observe for itself (the host's CPU, memory and disk through procfs and `statfs`) do not use this
bridge. The Server card samples them in the API process, under its own record (ADR 0158, in preparation at the time of
writing). The bridge carries what only the host can see: systemd and host files. A further source follows the runbook's §7
and, if it exceeds the content rule, a contract version.

*Why.* Every source added widens what the mount can carry. A fact the API can read itself gains nothing from a writer
on the host.

## Alternatives considered

- **Mount `/var/lib/jobbliggaren` into `api`.** Rejected: it gives the stamp and never the timer's next run.
- **The Docker socket or the system bus in the application.** Rejected: a broad host permission in the process that handles
  requests, which an API compromise would inherit.
- **The stamp in Postgres.** Rejected: it changes the backup algorithm.
- **The sampler as root, with the sandbox only.** Rejected on `security-auditor`'s design review of 2026-10-10:
  `ProtectSystem` limits writes, not reads, so a buggy collector could publish `host-secrets` into a mount an API compromise
  reads. Replaced by an unprivileged account and `InaccessiblePaths` (D1).
- **`DynamicUser` with `RuntimeDirectory`.** Rejected: the directory is removed and recreated when a oneshot stops, which
  leaves the container's bind mount on a dead inode.
- **An API endpoint per metric.** Rejected: the card needs one request.
- **Letting compose alone create the directory.** Rejected: the order of two creators is a hazard, and the directory would be
  root-owned, so the unprivileged sampler could not write it. The opposite choice (`create_host_path: false`, as for the
  credentials) turns a missing directory into an outage of the application over a metadata card.

## Consequences

### Positive

- The Backup card states a fact with an observation behind it, in the closed union ADR 0150 D2 asks for, and says plainly
  what it does not know.
- The application gained no host permission: one read-only mount of non-secret metadata, and no write path into it.
- The sampler and the reader cannot drift: golden files and cross-language pins stand where agreement would otherwise
  rest on luck.
- A next source is a file name, a collector, a port and fixtures (runbook §7), not a new channel.

### Negative and risks

- **The first live card reads "no run recorded, no run scheduled".** The backup timer is disabled and the stamp is absent
  while the backup is not switched on (#197). That is true and not a fault in the sampler (runbook §5).
- One more host timer, one more system account and one more tmpfiles entry to keep in step with the repository.
- This release's compose change freezes delivery until the box's checkout is advanced (D6, ADR 0154 Consequences).
- `/run` is tmpfs, so a reboot empties the directory and the card reads "awaiting" until the first sample (`OnBootSec=30s`).
- **Residuals, stated and not closed.** A file swapped between the reader's check and its open can be swapped only by the
  sampler or root, and a FIFO at the file's name would block the read. Both need a writer the model already trusts.
- **What is not claimed.** No offsite copy, no retention and no restore. A scheduled timer says when it fires, not that the
  credential its upload needs is present.

## Implementation

Ships under #1982; this ADR, the ADR 0150 amendment, the index row and the runbook travel in the same PR as the scope
(CLAUDE.md §1.5):

- `deploy/systemd/jobbliggaren-observe{.service,.timer,-backup.sh,-lib.sh,-sysusers.conf,-tmpfiles.conf,.test.sh}` and the
  golden files in `deploy/systemd/fixtures/observations/`;
- the `api` service's mount and `HostBridge__Directory` in `deploy/docker-compose.yml`; the `worker` gets neither;
- `Application/Admin/Backup` (the port, the evaluator, the closed DTO), `Infrastructure/Admin/HostBridge` (the reader, the
  source, its own registration) and `GET /api/v1/admin/overview/backup`;
- the BFF read and the card in `web/jobbliggaren-web`;
- the pins: `ObserveUnitFilePinTests` and `DeployComposeHostBridgeTests`, plus the sampler's shell suite.

## References

- [`docs/runbooks/host-observations.md`](../runbooks/host-observations.md) — contract, field meanings, install, rollback ·
  [`docs/runbooks/backup-restore.md`](../runbooks/backup-restore.md) §6 (the drill)
- ADR 0150 (D2, D4 and its Amendment 2026-10-08, #1978) · ADR 0154 · ADR 0143 · ADR 0125
- CLAUDE.md §9.2 ("Reading the production box") · AGENTS.md §2.1, §4, §5 · #1982 · #1972 · #2064 · #197
