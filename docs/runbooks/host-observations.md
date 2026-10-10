# Runbook — Host observations: the Backup card's data (#1982, ADR 0157)

> What the host publishes for the admin overview, what it means, how it is installed, verified and
> rolled back. The decision and its alternatives are in
> [ADR 0157](../decisions/0157-the-admin-backup-card-reads-a-host-sampler-through-a-read-only-directory.md);
> the card's page contract is in [BUILD.md §10](../../BUILD.md) and [ADR 0150](../decisions/0150-the-admin-surface-a-scoped-dashboard-honest-unavailability-and-a-preview-no-deployed-build-contains.md).
> The resource meters on the Server card are a different mechanism and a different document
> (`admin-host-observations.md`); this one covers only what the API container cannot see for itself.

## 1. What it is

A systemd timer runs `jobbliggaren-observe-backup.sh` once a minute as the unprivileged user
`jbl-observe`. The script writes one small JSON file, `/run/jobbliggaren/observations/backup.json`,
by writing a temporary file in the same directory and renaming it. The `api` container mounts that
directory **read-only** at `/run/observations` (and nothing else mounts it). The API reads the file on
each `GET /api/v1/admin/overview/backup`, checks it, judges it against its own clock and returns a closed
union to the browser.

```
timer (60 s) → observe-backup.sh → backup.json (tmpfs) ══ ro bind ══▶ api → /overview/backup → BFF → card
```

Nothing flows the other way. The browser sends nothing that reaches the host, the application holds no
credential for it, and the sampler runs fixed commands with no input.

## 2. The contract (version 1)

One file per source, one fixed name each. The reader never lists the directory.

```json
{"schema":1,"source":"backup","sampledAt":"2026-10-10T13:41:02Z","data":{"lastSuccess":{"state":"recorded","completedAt":"2026-10-10T02:19:07Z","startedAt":"2026-10-10T02:15:41Z"},"timer":{"state":"scheduled","nextRunAt":"2026-10-10T13:49:41Z"}}}
{"schema":1,"source":"backup","sampledAt":"2026-10-10T13:41:02Z","error":"collector-failed"}
```

* Exactly the keys above, each once; exactly one of `data` and `error`. `schema` is the integer `1`.
* Every instant is `yyyy-MM-ddTHH:mm:ssZ`: UTC, whole seconds, nothing else. No offset, no decimals.
* The file is at most 16 KiB, regular, not a symlink, valid UTF-8 with no byte-order mark.
* The golden files in [`deploy/systemd/fixtures/observations/`](../../deploy/systemd/fixtures/observations)
  are exactly what the sampler publishes in each scenario. The sampler's shell suite compares its output
  with them byte for byte, and the API's parser tests read the same files. After a deliberate change:
  `JBL_OBSERVE_WRITE_FIXTURES=1 bash deploy/systemd/jobbliggaren-observe.test.sh`, then read the diff — it
  is the contract change.

**Content rule.** The directory carries only what is fit for any admin to read: timestamps and closed
tokens. No personal data, no secret, no path, no free text from systemd or the filesystem. A source that
needs something else is a new contract version and needs `security-auditor`'s review first.
`ObserveUnitFilePinTests` pins the sampler's scripts against naming the backup credential, the secrets
directories, docker or rclone.

### What each field means

| Field | Source | Meaning |
|---|---|---|
| `sampledAt` | the host's clock, taken **after** the facts were read | when the host looked. It is the observation's time; the API never replaces it with the time of a request. |
| `lastSuccess.completedAt` | the mtime of `/var/lib/jobbliggaren/last-successful-backup` | the **end** of the last successful run: the stamp is the last thing `jobbliggaren-backup.sh` writes, after upload and DEK promotion. The same instant the box's own `--check` measures its 26 h threshold from. |
| `lastSuccess.startedAt` | the first line of that file | the run's **start**, UTC, as `YYYYMMDDTHHMMSSZ`. Not shown to the browser; used to reject a stamp whose two times are not a run. |
| `lastSuccess.state` | | `recorded`, `missing` (no stamp file: no run has ever succeeded on this box), `unreadable`, or `invalid` (not a regular file, or the first line is not a run start). |
| `timer.nextRunAt` | `NextElapseUSecRealtime` of `jobbliggaren-backup.timer` | the instant systemd has armed the timer for. `RandomizedDelaySec` is included in it (measured on the box 2026-10-10: the reconcile timer, `OnCalendar` :47 with `RandomizedDelaySec=3min`, showed 15:49:41). It says when the **timer** next fires, not that the backup will succeed — the service is skipped, not failed, when its upload credential is absent. |
| `timer.state` | `LoadState`, `ActiveState` | `scheduled`, `inactive` (installed but disabled or stopped), `notInstalled`, `unknown`. |

**Time zones.** `OnCalendar=*-*-* 02:15:00` is read in the box's own zone, which is **Europe/Berlin**
(measured 2026-10-10), not UTC: 02:15 local, which is 00:15 or 01:15 UTC. Sweden and Germany share their
offsets and their clock changes, so the card shows 02:15 in `Europe/Stockholm`. The sampler forces
`TZ=UTC` on its one `systemctl show`, because `systemctl show` ignores `--timestamp` and prints in the
process's zone; the file and the API carry UTC only.

### Who judges what

The sampler reports; it never judges. The API judges, against `IDateTimeProvider`:

| Rule | Value | Where it lives |
|---|---|---|
| An observation is **stale** | older than 5 min | `BackupStatusEvaluator.ObservationStaleAfter`; equals `OVERVIEW_STALE_MS` in the web app |
| A run is **overdue** | now − `completedAt` > 26 h | `BackupStatusEvaluator.BackupOverdueAfter`; equals `MAX_STAMP_AGE_SECONDS` in `jobbliggaren-backup.sh`. Measured against *now*, not against `sampledAt`, so a stopped sampler cannot leave "not overdue" standing. |
| A sample dated after now | > 60 s ahead → `FutureSample` | refused; its time is not shown |
| A stamp is **invalid** | `completedAt` after `sampledAt` + 60 s, before 2020, `startedAt` after it, or a span over 2 h | the span is kept above the backup unit's `TimeoutStartSec` by a test |
| A next run is **unknown** | more than 120 s before the sample, or more than 366 days after it | |

Each pin between a shell constant and a C# constant is a test in `ObserveUnitFilePinTests`.

### What the card does and does not prove

A recorded run says **the nightly run last finished successfully**. It does not say an artefact exists
offsite, decrypts or restores: the box holds no private key by design, and that claim belongs to the
restore drill (`backup-restore.md` §6). *Extern kopia* and *Behålls* keep saying that they have no
verified data source. A scheduled timer says when it fires, not that the credential it needs is present.

### Local development

Nothing is mounted on a workstation, so `HostBridge:Directory` is unset and the card says that the host has not
reported yet. To see its other states, point the API at a directory holding one of the golden files under the
name `backup.json`:

```bash
mkdir -p /tmp/jbl-bridge && cp deploy/systemd/fixtures/observations/backup-recorded-scheduled.json /tmp/jbl-bridge/backup.json
HostBridge__Directory=/tmp/jbl-bridge dotnet run --project src/Jobbliggaren.Api
```

The golden files carry a fixed `sampledAt`, so the card shows them as old; that is the contract working.

## 3. Cadence and cost

One sample a minute (`OnBootSec=30s`, `OnCalendar=minutely`, `AccuracySec=10s`). The overview polls every
60 s while a tab is visible and marks any source old after five minutes, so five missed samples are what an
admin sees as stale. Per sample the host runs one `systemctl show`, two `stat`/`head` reads and one rename;
per poll the API reads one file of at most 16 KiB. Nothing here is a live number: regenerate both with

```bash
# host: the cost of one sample (CPU time and wall time of the last run)
systemctl show jobbliggaren-observe.service -p CPUUsageNSec -p ExecMainStartTimestamp -p ExecMainExitTimestamp
# API: the cost of one read (the handler's stopwatch — the overview perf probe in docs/runbooks/performance-measurement.md)
dotnet test --project tests/Jobbliggaren.Application.UnitTests -- --filter-class "*Admin.Backup.BackupSampleSourceTests"
```

## 4. Security model

* **Who writes.** `jbl-observe`, a system account with no shell, no home and no groups
  (`jobbliggaren-observe-sysusers.conf`). It cannot read `/run/jobbliggaren/host-secrets` or
  `/run/jobbliggaren/secrets` (both root-only), and the unit also makes them, and `/etc/jobbliggaren`,
  inaccessible. It writes only `/run/jobbliggaren/observations` (`jobbliggaren-observe-tmpfiles.conf`:
  `0755 jbl-observe jbl-observe`). Sandbox: `ProtectSystem=strict`, `ProtectHome`, `ProtectProc=invisible`,
  `PrivateTmp`, `PrivateNetwork`, `RestrictAddressFamilies=AF_UNIX`, an empty capability set,
  `NoNewPrivileges`.
* **Who reads.** The `api` container, read-only, and no other service. The compose source is the literal
  `/run/jobbliggaren/observations`; `DeployComposeHostBridgeTests` pins it, its target, `read_only` and the
  absence of any other mount, and that the source is never a parent of the secrets directories.
  `create_host_path` is `true` here, unlike the Redis mounts that protect secrets: for a non-secret,
  world-readable directory Docker's auto-created root-owned `0755` directory is harmless, and it means a
  recreate of `api` before the directory exists cannot stop the application from starting. The tmpfiles line
  then hands the directory to `jbl-observe`.
* **What the API refuses.** Anything the contract above does not allow, with a typed reason and no echo of
  the file's content or an exception message into a log or a response. Residual, stated rather than closed:
  a file replaced between the reader's check and its open can only be replaced by the sampler or root, and
  a FIFO at the file's name would block the read; both need a writer the model already trusts.
* **What the browser receives.** `{status, reason, observedAt, stale, lastSuccess{state, completedAt, overdue}, timer{state, nextRunAt}}`.
  No path, no systemd text, no run start, no error message. A test pins the key set.

## 5. Install on the box

The release path (ADR 0154) delivers the files by advancing the box's checkout, and under the standing GO
(A2) the session whose merge needs it may do that, copy a changed unit file and run `daemon-reload`.
`docker-compose.yml` changes in this release, so the checkout must be advanced before the next reconcile:
until it is, every reconcile refuses and prints the exact command. Everything below that creates an account,
a tmpfiles entry or enables a timer is a one-off write on the box and needs Klas's GO for exactly these lines.

```bash
cd /opt/jobbliggaren
# 1. The release's files (A2).
sudo git -C /opt/jobbliggaren fetch origin main
sudo flock /run/jobbliggaren-reconcile.lock git -C /opt/jobbliggaren merge --ff-only <release commit>

# 2. The account, then the directory it owns. Order matters: the tmpfiles line names the account.
sudo install -m 0644 deploy/systemd/jobbliggaren-observe-sysusers.conf /etc/sysusers.d/jobbliggaren-observe.conf
sudo systemd-sysusers /etc/sysusers.d/jobbliggaren-observe.conf
sudo install -m 0644 deploy/systemd/jobbliggaren-observe-tmpfiles.conf /etc/tmpfiles.d/jobbliggaren-observe.conf
sudo systemd-tmpfiles --create /etc/tmpfiles.d/jobbliggaren-observe.conf
stat -c '%a %U:%G %n' /run/jobbliggaren/observations          # expect: 755 jbl-observe:jbl-observe

# 3. The units (A2).
sudo install -m 0644 deploy/systemd/jobbliggaren-observe.service deploy/systemd/jobbliggaren-observe.timer /etc/systemd/system/
sudo chmod 0755 deploy/systemd/jobbliggaren-observe-backup.sh deploy/systemd/jobbliggaren-observe-lib.sh
sudo systemctl daemon-reload

# 4. Measure the sandbox before arming anything: the same properties as the unit, as a transient
#    unit, run as the sampler account. It must write backup.json, and it must not reach the secrets.
sudo systemd-run --wait --collect --pipe --uid=jbl-observe --gid=jbl-observe \
  -p ReadWritePaths=-/run/jobbliggaren/observations \
  -p InaccessiblePaths="-/run/jobbliggaren/host-secrets -/run/jobbliggaren/secrets -/etc/jobbliggaren" \
  -p ProtectSystem=strict -p ProtectHome=yes -p ProtectProc=invisible -p PrivateTmp=yes -p PrivateNetwork=yes \
  -p RestrictAddressFamilies=AF_UNIX -p CapabilityBoundingSet= -p NoNewPrivileges=yes \
  /opt/jobbliggaren/deploy/systemd/jobbliggaren-observe-backup.sh
sudo systemd-run --wait --collect --pipe --uid=jbl-observe --gid=jbl-observe \
  -p InaccessiblePaths="-/run/jobbliggaren/host-secrets -/run/jobbliggaren/secrets -/etc/jobbliggaren" \
  /bin/sh -c 'for d in /run/jobbliggaren/host-secrets /run/jobbliggaren/secrets /etc/jobbliggaren; do test -r "$d" && echo "READABLE $d" || echo "denied $d"; done'
stat -c '%a %U:%G %s bytes %n' /run/jobbliggaren/observations/backup.json   # expect: 644 jbl-observe:jbl-observe

# 5. Reconcile applies the new compose (api is recreated with the mount); judge the journal, not the exit code.
sudo systemctl start jobbliggaren-reconcile.service && journalctl -u jobbliggaren-reconcile -n 40 --no-pager
sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-reconcile.sh --status            # expect: verdict: consistent

# 6. Arm it, and prove it runs rather than that it is scheduled.
sudo systemctl enable --now jobbliggaren-observe.timer
sudo systemctl start jobbliggaren-observe.service
journalctl -u jobbliggaren-observe.service -n 20 --no-pager
systemctl list-timers jobbliggaren-observe
```

The sampler is deliberately not in the heartbeat's floor timers (`jobbliggaren-heartbeat.sh`): its reader is
the card, and a stopped sampler shows there as an old observation.

### Verify (read-only, allowed under CLAUDE.md §9.2 (a))

`backup.json` holds only timestamps and tokens, so an agent may read it. Never sign in to the admin page on the
box to look at the card (§9.2 (d)); the signed-in check is Klas's.

```bash
sudo cat /run/jobbliggaren/observations/backup.json
stat -c '%y %s %a %U:%G' /run/jobbliggaren/observations/backup.json      # modified within the last minute
docker inspect --format '{{range .Mounts}}{{.Source}} -> {{.Destination}} rw={{.RW}}{{"\n"}}{{end}}' jobbliggaren-api | grep observations   # rw=false
```

Expected today, while the backup is not switched on (#197): `lastSuccess` `missing`, `timer` `inactive`. That
is the truth, and the card says so; it is not a fault in the sampler.

## 6. Rollback

```bash
sudo systemctl disable --now jobbliggaren-observe.timer
sudo rm -f /etc/systemd/system/jobbliggaren-observe.service /etc/systemd/system/jobbliggaren-observe.timer
sudo systemctl daemon-reload
```

The card then shows *the host has not reported yet* (no file) or, if a file remains, the old observation as
old after five minutes. The account, the tmpfiles entry and the mount are inert without the timer. A
`git revert` of the release removes the mount at the next reconcile.

## 7. Adding a source

A source is one fixed file name, one collector script that publishes through `observe_publish`, one
Application port with one consumer, and one golden file per scenario. It is a contract version bump when it
adds a key to the envelope or carries anything beyond the content rule above, and `security-auditor` reviews
that before it is built. Facts the container can observe for itself (CPU, memory and disk of the host through
procfs and `statfs`) do not belong here: they are sampled in the API process, and that is the Server card's
own design.
