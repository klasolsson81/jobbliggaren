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

**THERE IS NO SSH TUNNEL, AND NO ADDRESS FORM FIXES THAT.** An earlier draft of this section, of
`deploy/docker-compose.yml`'s seq comment and of ADR 0128 all said operator access is an `ssh -L`
tunnel. Measured 2026-08-11: this box runs `AllowTcpForwarding no` — the drop-in `vps-base-hardening.md`
§4.2 itself prescribes, confirmed effective with `sudo sshd -T | grep allowtcpforwarding`. The
counterfactual was run rather than assumed: `ssh -f -N -L 18342:<container-ip>:8080 jp-vps` is
accepted by the client and then refused by the server with
`channel 1: open failed: administratively prohibited`. So Seq's **browser UI is not reachable at
all** from an operator workstation today, and reaching it would mean lowering a hardening control —
which is Klas's decision and an ADR, never a step in this file.

**Everything below is therefore headless, and every command was run against `datalust/seq:2026.1`
(`sha256:91e93ff2…`) on 2026-08-11 before it was written here.** What talks to Seq is the box's own
`curl` against the container IP; that path is measured (host → container over the docker bridge,
HTTP 404 from a live container endpoint, i.e. reachable). `jq` is **not** installed on this box —
`python3` and `curl` are, so JSON is built and read with `python3`.

**1.** Put `SEQ_ADMIN_PASSWORD` in the box's `.env` (root-owned, chmod 600, never committed). Leave
`SEQ_INGEST_API_KEY` empty and `SEQ_SERVER_URL` **absent** for now — the order in step 8 is
load-bearing. This is an interactive editor and is not something you can paste from here:
`sudo nano /opt/jobbliggaren/deploy/.env`.

> `SEQ_ADMIN_PASSWORD` is read ONLY on Seq's first run against an empty volume. After that the
> password lives in Seq's own store and **this value stops being the source of truth.** Do not
> spend it on an experiment: a `docker volume rm seq_data` is the only way back.

**2.** Bring the service up. reconcile does this hourly on its own; doing it by hand takes no lock,
so stop the timer first.

```bash
sudo systemctl stop jobbliggaren-reconcile.timer
sudo docker compose -f /opt/jobbliggaren/deploy/docker-compose.yml up -d seq
sudo docker logs jobbliggaren-seq --tail 20
```

**3.** Find the container's address. It changes whenever the container is recreated, so it is looked
up at use time and never written down.

```bash
SEQ_IP=$(sudo docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' jobbliggaren-seq)
curl -s -o /dev/null -w '%{http_code}\n' "http://$SEQ_IP/"     # expect: 200
```

**4.** Sign in — and note that the **first** sign-in fails by design. Measured: with a first-run
admin password set, `POST /api/users/login` answers `401 {"Error":"A password change is
required.","MustChangePassword":true}`. Supplying `NewPassword` in the same request both changes it
and signs in. Everything after this needs two things: the `Seq-Session` cookie and the `CsrfToken`
from the response, sent back as `X-Seq-CsrfToken` on every non-GET.

```bash
read -rsp 'Current SEQ_ADMIN_PASSWORD: ' OLD_PW; echo
read -rsp 'New admin password: ' NEW_PW; echo
LOGIN=$(python3 -c 'import json,sys; print(json.dumps({"Username":"admin","Password":sys.argv[1],"NewPassword":sys.argv[2]}))' "$OLD_PW" "$NEW_PW" \
  | curl -s -c /tmp/seq.jar -H 'Content-Type: application/json' --data-binary @- "http://$SEQ_IP/api/users/login")
CSRF=$(printf '%s' "$LOGIN" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("CsrfToken",""))')
[ -n "$CSRF" ] && echo "signed in" || { echo "LOGIN FAILED: $LOGIN"; }
```

**5.** Turn on the ingestion gate **before** creating the key, because until it is on the key bounds
nothing. Measured on a stock 2026.1 with authentication enabled: `RequireApiKeyForWritingEvents`
defaults to **`false`**, and with it false a `POST /api/events/raw?clef` is accepted with a valid
key, an **empty** key, a **wrong** key and **no key at all** — 201 in all four cases. With it true:
201 for the valid key, **401** for the other three. **No environment variable sets this** — both
`SEQ_API_REQUIREAPIKEYFORWRITINGEVENTS` and `SEQ_REQUIREAPIKEYFORWRITINGEVENTS` were measured
silently ignored — so it cannot be shipped fail-closed in compose and has to be a step here.

```bash
curl -s -b /tmp/seq.jar -H 'Content-Type: application/json' -H "X-Seq-CsrfToken: $CSRF" -X PUT \
  -d '{"Name":"requireapikeyforwritingevents","Value":true,"Id":"setting-requireapikeyforwritingevents"}' \
  "http://$SEQ_IP/api/settings/setting-requireapikeyforwritingevents"
# expect: "Value":true — a PUT carrying only Id+Value answers 500, the full object answers 200
```

**6.** Create the INGEST-ONLY key and keep the token: it is shown once.

```bash
curl -s -b /tmp/seq.jar -H 'Content-Type: application/json' -H "X-Seq-CsrfToken: $CSRF" \
  -d '{"Title":"jobbliggaren-app-ingest","AssignedPermissions":["Ingest"],"InputSettings":{"AppliedProperties":[],"Filter":{"DescriptionIsExcluded":false},"UseServerTimestamps":false}}' \
  "http://$SEQ_IP/api/apikeys" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d["Token"], d["AssignedPermissions"])'
# expect: <token> ['Ingest']
```

**7.** Set retention: one policy, all events, 30 days.

```bash
curl -s -b /tmp/seq.jar -H 'Content-Type: application/json' -H "X-Seq-CsrfToken: $CSRF" \
  -d '{"RetentionTime":"30.00:00:00","RemovedSignalExpression":null}' \
  "http://$SEQ_IP/api/retentionpolicies"
# expect: 201 with "RetentionTime":"30.00:00:00"
```

**8.** Only now put the two remaining values in `.env`, **in this order**, and re-arm reconcile.
`SEQ_SERVER_URL` is what ATTACHES the provider, so setting it before the key exists points both
hosts at a sink that answers 401 to every event — which step 5 measured is exactly what an empty
key gets. Interactive editor again, deliberately outside this block:
`sudo nano /opt/jobbliggaren/deploy/.env` — `SEQ_INGEST_API_KEY=<token>` first, then
`SEQ_SERVER_URL=http://seq:5341`.

```bash
rm -f /tmp/seq.jar
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
| **The one-time setup completes with NO change to sshd** | the §3 command sequence, end to end | **Measured against `datalust/seq:2026.1` (`sha256:91e93ff2…`), not against this box:** login-with-`NewPassword` 200, gate PUT 200, `POST /api/apikeys` 201 with `['Ingest']`, `POST /api/retentionpolicies` 201. The box-side run is what this row still owes; the mechanism is no longer an assumption | 2026-08-11 (image only) |
| **Ingestion REFUSES an unkeyed write at this box** | `curl -X POST "http://$SEQ_IP/api/events/raw?clef"` with no `X-Seq-ApiKey` | **This is the row that says whether the ingest key bounds anything.** Measured on a stock 2026.1: with `RequireApiKeyForWritingEvents=false` — the DEFAULT — no key, an empty key and a wrong key are all accepted (201). The gate is §3 step 5 and has no environment variable, so it is a step someone can skip; expect **401** here | |
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
