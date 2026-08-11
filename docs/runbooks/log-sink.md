# Log sink and off-box log archive — #1175

Sibling of [`host-detection.md`](host-detection.md) and [`backup-restore.md`](backup-restore.md).
The decision and its rationale are ADR 0128; this file is the operational half.

**Two mechanisms, and they are not one.** ADR 0128 split #1175 because its three obligations do
not share a change-reason:

| | **(A) `jobbliggaren-logship`** | **(B) Seq** |
|---|---|---|
| Purpose | durable, age-bounded, off-box | searchable, structured, correlated |
| Where | OVH `jobbliggaren-backups`, `hostlogs/` prefix | a compose service on this box |
| Encrypted to | an `age` recipient this box cannot decrypt | nothing — plaintext on the box's disk |
| Survives a root attacker | **intended, and NOT in force** — see §1 | no, and it is not meant to |
| Streams | journal + auditd + app containers | app containers only |

---

## 1. The property that is not in force, read this before quoting the archive as protection

Verification row **27d** (`vps-deploy-stack.md`) was measured **FALSE on 2026-08-09: the upload
credential CAN delete.** `delete-object` succeeded against the live container.

Until an OVH **user policy** with an explicit `Deny s3:DeleteObject` is applied — owner Klas, a
first-real-data gate — an attacker holding this box's credential deletes the off-box archive too.
The archive is therefore a durable corpus against **accident and a lower-privilege attacker**, and
a documented-but-not-in-force one against the ADR 0123 root attacker. `jobbliggaren-logship.sh`'s
header carries the same warning, deliberately, so the two cannot drift apart.

---

## 2. Install — (A), the off-box archive

Order matters. The archive's credential is #197's, so **(A) cannot run until #197's host secrets
are provisioned**; installing the units before then is fine and is the intended sequence, because
the service's `ConditionPathExists` skips the run rather than failing it.

```bash
# The clone. NOT `git pull` blind — on this box a pull is a DEPLOY that
# jobbliggaren-reconcile.timer applies within the hour, and one such pull cost a 13-minute
# outage on 2026-08-10. Read what it would bring FIRST, then pull; the fetch+log is what makes
# the pull deliberate, never a substitute for it. Read vps-deploy-stack.md §6 if
# deploy/docker-compose.yml is involved.
git -C /opt/jobbliggaren fetch origin
git -C /opt/jobbliggaren log --oneline HEAD..origin/main -- deploy/
sudo git -C /opt/jobbliggaren pull --ff-only

# FOUR unit files, two pairs. The shipping pair archives; the -fresh pair is the only thing that
# ever calls `--check`, and without it a stopped archive is on no surface at all: the service's
# ConditionPathExists SKIPS a credential-less run, and a skip is inactive, not failed.
# The script is already executable in the clone (git carries the mode, CI gates it).
sudo cp /opt/jobbliggaren/deploy/systemd/jobbliggaren-logship.{service,timer} /etc/systemd/system/
sudo cp /opt/jobbliggaren/deploy/systemd/jobbliggaren-logship-fresh.{service,timer} /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now jobbliggaren-logship.timer
sudo systemctl enable --now jobbliggaren-logship-fresh.timer

# Prove it RUNS, not merely that it is scheduled. Before #197's secrets exist this is EXPECTED to
# report a skipped condition rather than a failure — that is the designed state, not a fault, and
# the probe skips for the same reason rather than lighting a permanent alarm.
sudo systemctl start jobbliggaren-logship.service
sudo journalctl -u jobbliggaren-logship -n 30 --no-pager
systemctl show -p ConditionResult -p Result jobbliggaren-logship.service
```

**Add `jobbliggaren-logship.timer` to `FLOOR_TIMERS` in `jobbliggaren-heartbeat.sh` at this
point, and not before.** That list is the non-vacuity floor for M-7's P3 — a timer named there
must be enabled and active or the box pages. The file's own `KEEP IN SYNC AS UNITS LAND` note
binds at the moment of installation, so the obligation falls due here. The `-fresh` timer belongs
there too by the same argument; the two are installed together.

**The lifecycle rule on the new prefix is a separate, Klas-owned step**, and until it exists the
archive is append-only with no age bound at all — i.e. it discharges the off-box obligation and
**not** the Art. 5(1)(e) one. Create it against `hostlogs/` with its own retention number; do not
extend `main/`'s rule to cover it, because the retention question for logs is a different question
from the one K4 answered for database artefacts.

---

## 3. Install — (B), Seq

```bash
# 1. Two values in the box's .env (root-owned, chmod 600, never committed).
#    SEQ_ADMIN_PASSWORD is read ONLY on Seq's first run against an empty volume. After that the
#    password lives in Seq's own store and this value stops being the source of truth.
sudo nano /opt/jobbliggaren/deploy/.env      # SEQ_ADMIN_PASSWORD=..., leave SEQ_INGEST_API_KEY empty for now

# 2. Bring the service up. reconcile does this hourly on its own; doing it by hand takes no lock,
#    so prefer letting the timer apply, or stop the timer first.
sudo systemctl stop jobbliggaren-reconcile.timer
sudo docker compose -f /opt/jobbliggaren/deploy/docker-compose.yml up -d seq
sudo docker logs jobbliggaren-seq --tail 20

# 3. Reach the UI. THERE IS NO PUBLISHED PORT, and that is the control rather than an omission:
#    compose-edge-publish-guard allows exactly one publishing service and caddy is it. Tunnel:
#      ssh -L 8341:127.0.0.1:8341 jp-vps
#      # then on the box, forward the container's port into the tunnel's local end
#    or read it through a one-shot container on the stack network.

# 4. In Seq: change the admin password, create an INGEST-ONLY API key, and set retention.
#    Retention: one policy, "All events", 30 days. There is no environment variable for it.

# 5. Put the ingest key in .env and let reconcile apply, so api and worker pick it up.
sudo nano /opt/jobbliggaren/deploy/.env      # SEQ_INGEST_API_KEY=...
sudo systemctl start jobbliggaren-reconcile.timer
```

**`SEQ_ADMIN_PASSWORD` is deliberately a `:-` default and not `:?`.** A hard requirement the box's
`.env` does not yet carry makes compose refuse the **entire** apply, every service, every hour,
with `systemctl --failed` as the only signal. With `:-` only the seq container fails, and it fails
with Seq's own diagnostic.

---

## 4. Verification log

Property · Instrument · Measured · Date — the shape `host-detection.md` §7 and
`vps-deploy-stack.md` §5 both use. **A row without a date is a claim that cannot be told from one
that has decayed.** Rows are written **before** measurement, deliberately: `deploy/` gets no
integration coverage from `ci`, and the install happens once.

| Property (gate) | Instrument | Measured | Date |
|---|---|---|---|
| **Seq's memory cost is measured, not read off a spec sheet** | `docker run --memory=512m`, then cgroup `memory.current`/`memory.peak`, idle and after an ingest burst | **79 MiB idle, 111 MiB after 5 000 CLEF events, no OOM**, under a hard 512m cap on this box. Datalust's published "3.5 GB light workload" is a recommendation and is ~31× this. **The cap is part of the measurement:** Seq sizes its caches against the cgroup limit, so a different cap is a different configuration | 2026-08-11 |
| **The box has the headroom, measured as USAGE and not as the cap table** | `free -m`; per-container cgroup `memory.peak` | 1 165 MiB used of 7 945 (14.7 %); sum of container peaks 553 MiB against caps totalling 5 888 MiB. **Peak window was ~10 h and the corpus is EMPTY** — this row does not speak for a loaded box | 2026-08-11 |
| **Seq refuses to start unauthenticated** | run the image with neither `SEQ_FIRSTRUN_ADMINPASSWORD` nor the no-auth opt-out | Exit 1, `No default admin password was supplied`. This is the fail-closed default that makes #1198's shape hard to recreate | 2026-08-11 |
| **No service but caddy publishes a port** | `.github/scripts/compose-edge-publish-guard.sh` against the rendered model | `OK — caddy publishes 80 443 and nothing else publishes` | 2026-08-11 |
| **The archive leaves the box AND ARRIVES** | `rclone lsl` on the `hostlogs/` prefix, **never the script's exit code** — `post()`-style "it left" is not "it landed", the same rule ADR 0126 wrote for the heartbeat | | |
| **The archive is unreadable to the target** | download one artefact, confirm it is an `age` envelope, confirm the box holds no private key | | |
| **The corpus survives erasure on the box** | `journalctl --vacuum-time=1s` and truncate the audit log, then list the prefix | **Blocked by row 27d and must say so** — until the `Deny` policy is applied an attacker with the box's credential deletes the off-box copy too | |
| **The lifecycle rule removes objects, measured as an EFFECT** | plain prefix listing after N+1 days, older artefacts gone | Row 27b's discipline: a rule is a claim; the disappearance is the measurement | |
| **The journal cursor neither drops nor duplicates across a restart** | stop the timer, reboot, run once, compare the last entry of run *n* against the first of run *n+1* | | |
| **The MEL provider actually posts to 5341, not 80** | `Seq:ServerUrl` set to the 5341 form, then confirm events arrive | If ingestion is measured unavailable on 5341, fall back to `:80` **and record that the split was measured unreachable** — never switch silently, because the split is the control that stops a compromised app container reading the corpus back | |
| **An empty `.env` value counts as NOT SUPPLIED** | unset `SEQ_SERVER_URL`, confirm both hosts stay console-only | `Email__Provider` in the same file is a measured case where empty ≠ unset (`??` does not catch `""`), so this cannot be assumed from the `:-` default alone | |
| **Seq's retention policy removes events, and the DISK follows later** | query for an event older than the window; separately, `du` on the volume | Retention makes events inaccessible; space returns via compaction, which runs at **7 days of file age** — bytes can persist past the 30-day mark, and the register says so | |
| **The seq container has a healthcheck** | `docker inspect -f '{{.State.Health.Status}}'` | **Not shipped.** The compose file omits it deliberately rather than shipping an unverified probe that would paint a permanent "unhealthy"; whether this image carries a client to call Seq's health endpoint was not measured. This row closes that | |
| **`logship` runs, and its cost is bounded** | `systemd-analyze` on the unit; artefact size per run | | |

---

## 5. What this runbook does not own

- **The `json-file` layer's age bound** — [#1170](https://github.com/klasolsson81/jobbliggaren/issues/1170).
  (B) demotes `json-file` from *the* store to a last-resort buffer, which is what finally makes a
  smaller `max-size` defensible — but the number needs a write rate, and no write rate exists
  until there are users. **Do not use the archive's ~220 bytes/event as a proxy:** that is Seq's
  storage format, not console text.
- **Application-level alarms** (5xx rate, DB CPU) — [#1172](https://github.com/klasolsson81/jobbliggaren/issues/1172).
- **Row 27d's `Deny` policy**, the OVH Art. 28 agreement, and the retention numbers for journald
  and auditd. All Klas's, all open, all named in ADR 0128 §5.
- **Host detection and paging** — [`host-detection.md`](host-detection.md). M-7 sends *that*
  something happened; this file is about the corpus.
- **Whether `deploy/.env` is an acceptable home for the Seq credential.** ADR 0128 records that
  the bind expands one of the two plaintext-on-disk surfaces #198 was opened against, and that
  `security-auditor` owns the severity.
