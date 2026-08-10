# Runbook — host detection: what would make anyone aware, and within what time (gate M-7)

**Scope:** the obligation gate M-7 states, the mechanism bound against it, and the drills that
decide whether the mechanism works. **Hemvist:** [#1201](https://github.com/klasolsson81/jobbliggaren/issues/1201).

**Authority:** ADR 0050 `Amendment 2026-08-04` §6b and gate row M-7. **The legal basis lives in
that gate row and is deliberately not restated here** — it is security-auditor's under §9.6, and
a second formulation is a second place to drift apart. **Severity is restated**, because the gate
row points here and a pointer must lead to an answer: **`Major`, escalating to `Blocker` if
ADR 0123 is still ungranted or unmitigated at first real user data.**

This file is the tracked home of the written answer first recorded on #1201 on 2026-08-10 (that
comment is the ordering proof AC 1 asks for; this file is the demonstrable record Art. 5(2)
asks for).

---

## 1. What "aware" means here, and the claim discipline

Art. 33's clock runs from the **controller** becoming aware. A journal line nobody reads is not
awareness. Every bound in §3 therefore decomposes:

```
T_aware  ≤  T_signal  +  T_surface  +  T_read
            (event →     (record →      (that surface's
             record)      a surface)     read cadence)
```

Three rules this file holds itself to:

1. **No bound without an instrument.** A time bound may not appear below without a verification
   row in §7 that measures it. A bound without an instrument is an aspiration, and Art. 5(2)
   does not accept aspirations.
2. **"Unbounded" is a measured cell, not a confession.** Where no reader exists, unbounded is the
   honest value, and the surface enumeration in §2 is its instrument.
3. **Rows are referenced by property text, never by ordinal.** The log grows; an ordinal is true
   of its evidence and false of its subject by the next appended row.

## 2. The awareness surfaces, and who reads them

The state this gate was opened against — measured read-only over SSH on 2026-08-10, before any
mechanism existed. Regenerate with the commands in §7.

| Surface | What lands on it | Reader | Cadence |
|---|---|---|---|
| `systemctl --failed` | **one feeder of four** — `jobbliggaren-reconcile` was installed and succeeding, so the list was empty on its merits; the other three (`secrets-present`, `backup`, `backup-fresh`) were shipped and never installed | nobody | ∞ |
| systemd journal (persistent) | sshd VERBOSE, sudo, unit output | nobody | ∞ |
| Docker json-file logs | app and container logs — a **disjoint** stream from the host journal | nobody | ∞ |
| `audit_log` table | application auth/audit events | nobody | ∞ |
| reconcile stamp | "the box last applied at T" | nobody | ∞ |
| off-box: anything | **nothing left the box** — no `rsyslog`, `auditd`, `aide`, journal-upload, shipping agent or cron reader | — | — |
| weekly operator check (`backup-restore.md` §4) | prose procedure, not a scheduled job | Klas, when performed | asserted, never logged |

**The sharpest fact, and the reason the predicate in §3 carries non-vacuity arms:** an empty
failure list cannot distinguish "everything is well" from "almost nothing reports here" — three of
four feeders were absent, and nobody read the list either way. A green light whose green is true
of its evidence and false of its subject is worse than no light.

## 3. The mechanism

Bound by senior-cto-advisor 2026-08-10. **One script, one unit pair, one check, two ping verbs.**

- **Predicate false → `POST <url>/fail`** with an allowlisted diagnosis. Fast and diagnostic.
  Does **not** survive an attacker who controls the box.
- **No ping at all → the expecter fires after its grace period.** Slow, carries no diagnosis, and
  it is the only signal that does not depend on the box choosing to send it: **disarming the
  heartbeat is itself the alarm.**

**How far that second property actually reaches, stated precisely — the generous reading is
wrong, and security-auditor measured it.** It catches an attacker who *disarms* this box's
reporting: stops the timer, kills the unit, cuts the network, takes the machine. It does **not**
catch the attacker ADR 0123 names. That attacker is root, root reads the ping URL out of
`/etc/jobbliggaren/detection.env`, and replayed success pings mean silence never occurs — one
`cat` and one cron line. So the dead-man converts *disarmed* into *alerted* **only while the
credential beside it is not taken too**, which puts it in the same mistake-and-lower-privilege
class as everything else here. It is not a control against root.

Neither property contains the other, which is why both exist. Period 15 min, grace 45 min ⇒ the
dead-man fires no later than **one hour** after the last successful ping, and it takes four
consecutive missed pings to get there. **Those two numbers are one design**: change either
without the other and the bound §7 measured moves silently.

The predicate, in `deploy/systemd/jobbliggaren-heartbeat.sh`:

| | Property | Why it is in the predicate |
|---|---|---|
| **P1** | `systemctl --failed` is empty | the existing signal surface |
| **P2** | every **enabled** `jobbliggaren-*.timer` is also **active** | derived, never a list — catches a `stop` or `mask` on any current or future unit, which a failure list structurally cannot show |
| **P3** | the floor set of timers is enabled **and** active | non-vacuity: without it, an empty failure list on a box where nothing runs reports perfect health |
| **P4** | every `jbl-*` key the rules file defines is loaded in the kernel | the detection configuration's own integrity — a missing rules file, a missing `auditctl` and an unloaded rule are all failures, never passes |
| **P5** | free space on `/` and the Docker filesystem is above the floor | absorbs the **detection** half of the disk-usage finding (below) |

**The script always exits 0.** It must never land on the failure list it reads: P1 would then be
permanently false and the alarm permanently lit, which trains an operator to stop reading it. A
bash-level crash is still covered twice — the unit fails *and* the ping is not sent.

**Detection primitive:** `auditd` with **watch rules only** — no syscall rules, whose volume
would compete directly with the journal's evidence window and ADR 0122's capacity conditions on a box where
the answer to memory pressure (disk swap) is forbidden because it breaks gate B-1. Rules and the
reasoning for the `zz-` filename, the `-p` flags and `-e 1` are in
`deploy/systemd/zz-jobbliggaren-audit.rules`; the ratchet to `-e 2` carries its condition and
instrument at the rule itself.

**File-integrity monitoring is the audit watches** (`jbl-authkeys`, `jbl-sshd`, `jbl-sudoers`,
`jbl-units`, `jbl-auditconf`) — change-as-event rather than state-diff, so no baseline churn under
`unattended-upgrades`. `aide` is not installed: it would be a second root component producing a
standing noise stream **with no reader**, and this gate's own thesis is that an unread signal is
not awareness. **The condition under which `aide` becomes owed** — not a date — is an off-box
corpus (#1175) that gives a FIM report a reader root cannot erase. Named limit: watch rules miss
**offline modification** (a boot from a rescue image).

### What the mechanism does not do

- **It does not make anyone aware of a competent root attacker.** Root can `auditctl -e 0`, erase
  the audit log and the journal, read the master key from process memory (`/proc/<pid>/mem`, no
  file touched) or read the disk raw (`dd` against `/dev/vda`, no watched path touched). What the
  watches give is **a tripwire for the mistake and lower-privilege classes plus a forensic
  trail — not an alarm.**
- **It does not lift ADR 0123's scope limit.** The acceptance's exit condition still rests on a
  capability that only partially exists, which is precisely the escalation condition in the gate
  row.
- **It ships no logs.** The forensic corpus stays local and root-erasable until #1175 lands.
- **An attacker who leaves the heartbeat running is not caught.** The dead-man converts
  *disarmed* into *alerted*; it never converts *compromised* into *alerted*.

## 4. Baseline noise — measured, never derived

**This section is AC 3's second half and it is a measurement, not a prediction.** A watch rule
whose ordinary traffic is not written down is alarm fatigue with extra steps.

Fill after a real observation window (§5 has the procedure). The expected actors on
`jbl-key-tmpfs`:

**Every watch family gets a row, not just the key one.** A family whose ordinary traffic is
undocumented is a family whose findings cannot be triaged.

| Key | Expected actors | Notes |
|---|---|---|
| `jbl-key-tmpfs` | `jobbliggaren-inject-secrets.sh` writes at injection; **one read per secret file per api/worker container start** | containers restart on **every applied reconcile**, so this is as frequent as deploys, not rare. *(Expected silent: `--check` uses `stat()`, which triggers none of `r`/`w`/`a` — a firing there is a real finding.)* |
| `jbl-authkeys` | none in normal operation | `/root/.ssh` and `/home/jpadmin/.ssh` only. **Deliberately not `/root`:** measured 2026-08-10, `/root` carries hourly cosign TUF-cache and docker buildx writes, which would drown this family |
| `jbl-sshd`, `jbl-sudoers`, `jbl-accounts` | none in normal operation | `unattended-upgrades` can touch these during a package upgrade — expected, and worth confirming against the upgrade log before treating one as a finding |
| `jbl-units` | operator installs, and `git pull` in `/opt/jobbliggaren/deploy/systemd` | the reconcile unit pulls the clone, so a deploy that changes a unit file fires here legitimately |
| `jbl-cron` | none | no user crontab exists on this box (measured 2026-08-10), so this family is close to pure signal |
| `jbl-deploy` | `git pull` during reconcile, when compose or `.env` actually changes | not every hour — only when the pull brings a change |
| `jbl-detection` | **two reads per heartbeat run**: systemd reads `EnvironmentFile=` and the script sources it | the timer runs every 15 min, so expect ~192 reads/day. Anything that is not those two is the finding this watch exists for |
| `jbl-auditconf` | `augenrules --load` writing the merged `/etc/audit/audit.rules`, and `unattended-upgrades` when the auditd package is upgraded | **this family is NOT quiet, and the install procedure itself triggers it:** §5 step 2 runs `augenrules --load`, and Debian's `auditd.service` runs it again via `ExecStartPost=` on every restart — including every reboot, and the reboot drill in §7 |

**Any actor not in this table is a finding.** The table is the discriminator; without it the rules
produce events nobody can triage, which is the same failure as no rules at all. **Fill the counts
after a real observation window** — the shapes above are derived from what runs; the numbers are
not measured until §7's baseline row carries a date.

## 5. Install and drill

**Order matters and nothing enforces it — these lines are the enforcement.** The expecter is
armed **last**, so arming does not itself page.

```bash
cd /opt/jobbliggaren && sudo git pull --ff-only    # the units and rules live in deploy/

# 1. Journal window first: it is what gives every later step an evidence window.
#    The 60- prefix is deliberate — drop-ins sort by filename across ALL directories and systemd
#    reserves 10–40 for vendor files under /usr, so a 10- name here can be overridden by the
#    distribution.
sudo install -d -m 0755 /etc/systemd/journald.conf.d
sudo install -m 0644 -o root -g root \
  /opt/jobbliggaren/deploy/systemd/journald-jobbliggaren-retention.conf \
  /etc/systemd/journald.conf.d/60-jobbliggaren-retention.conf
sudo systemctl restart systemd-journald
sudo systemd-analyze cat-config systemd/journald.conf | grep -E 'SystemMaxUse|SystemKeepFree|Storage'

# 2. auditd. INSTALLING THE PACKAGE STARTS IT WITH DEBIAN'S DEFAULTS, and three of those
#    (admin_space_left_action, disk_full_action, disk_error_action) are SUSPEND — a detective
#    control that stops the audit trail exactly when something is going wrong. Repair them
#    immediately after install, BEFORE loading rules that raise the write rate.
#
#    THERE IS NO auditd.conf.d. Measured 2026-08-10: auditd reads exactly one file, the package
#    ships only plugins.d, and the binary contains no such path. The values are edited in place.
sudo apt-get update && sudo apt-get install -y auditd
for kv in 'admin_space_left_action SYSLOG' 'disk_full_action ROTATE' 'disk_error_action SYSLOG'; do
  k=${kv% *}; v=${kv#* }
  sudo sed -i "s/^${k}[[:space:]]*=.*/${k} = ${v}/" /etc/audit/auditd.conf
done
sudo grep -E '^(admin_space_left_action|disk_full_action|disk_error_action) ' /etc/audit/auditd.conf
#    Expected: SYSLOG / ROTATE / SYSLOG — and no SUSPEND anywhere in that output.

sudo install -m 0640 -o root -g root \
  /opt/jobbliggaren/deploy/systemd/zz-jobbliggaren-audit.rules \
  /etc/audit/rules.d/zz-jobbliggaren.rules
sudo augenrules --load
sudo systemctl restart auditd

# JUDGE THE KERNEL, NOT THE FILE. `auditctl -s` says "enabled" whether or not our rules loaded,
# and a watch on a path that did not exist at load time is silently absent.
sudo auditctl -l | grep -c 'jbl-'
#    Expected: the number of WATCH RULES carrying a jbl- key — NOT the number of distinct keys.
#    Seven of the ten keys are carried by more than one path, so the two counts differ (19 vs 10
#    as this file stands), and comparing against the key count would show the operator a
#    discrepancy that does not exist. The heartbeat's P4 predicate compares (path, key) pairs for
#    the same reason: a single watch failing to load is invisible to a key-set comparison.
grep -v '^[[:space:]]*#' /opt/jobbliggaren/deploy/systemd/zz-jobbliggaren-audit.rules |
  grep -cE -- '^-w[[:space:]]+[^[:space:]]+[[:space:]]+-p[[:space:]]+[^[:space:]]+[[:space:]]+-k[[:space:]]+jbl-'

# 3. The capability URL. Create the check at the expecter first, then paste its ping URL here.
sudo install -d -m 0700 -o root -g root /etc/jobbliggaren
sudo install -m 0600 -o root -g root \
  /opt/jobbliggaren/deploy/detection/detection.env.example /etc/jobbliggaren/detection.env
sudo nano /etc/jobbliggaren/detection.env      # paste the real URL; never echo it

# 4. The heartbeat. The script is already executable in the clone (git carries the mode, and CI
#    gates it), so there is nothing to install — only the units are copied.
sudo cp /opt/jobbliggaren/deploy/systemd/jobbliggaren-heartbeat.{service,timer} \
  /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now jobbliggaren-heartbeat.timer
sudo systemctl start jobbliggaren-heartbeat.service   # prove it RUNS, not just that it is scheduled
sudo journalctl -u jobbliggaren-heartbeat -n 20 --no-pager

# 5. Arm the expecter's grace period LAST, at the expecter, once a success ping has arrived.
```

### The drills (AC 4 — "tested, not asserted")

Each drill fills a row in §7. **Measure on the expecter's side, never on the box's exit code**: a
`curl` that exits 0 proves the request left; only the service's own `last ping` proves it arrived.

```bash
# D1 — a key read produces a record naming uid and exe.
sudo dd if=/run/jobbliggaren/secrets/FieldEncryption__LocalMasterKeyBase64 \
  of=/dev/null bs=1 count=1 2>/dev/null
sudo ausearch -k jbl-key-tmpfs -ts recent
# Record the FIELD NAMES plus uid/exe. Redact any addr=/hostname= before it reaches this file.

# D2 — a write to a watched control produces a record.
sudo touch -a /etc/sudoers && sudo ausearch -k jbl-sudoers -ts recent   # expect a record

# D3 — active fail pages with a diagnosis. Use a TRANSIENT unit; never touch a delivered one.
sudo systemd-run --unit=jbl-m7-drill --service-type=oneshot /bin/false
sudo systemctl start jobbliggaren-heartbeat.service
# Expect: a /fail ping naming failed-units=. Record the wall-clock delta to the alert.

# D4 — the alarm self-clears with no operator action.
sudo systemctl reset-failed jbl-m7-drill.service
sudo systemctl start jobbliggaren-heartbeat.service    # expect a success ping; check goes up

# D5 — THE DEAD-MAN, which is the row the whole bound rests on.
sudo systemctl stop jobbliggaren-heartbeat.timer
# Wait past period + grace. Expect a page. Record the delta from the last successful ping.
sudo systemctl start jobbliggaren-heartbeat.timer      # re-arm and confirm the check clears
```

## 6. When a page arrives — the first fifteen minutes

1. **Read which kind it is.** A `/fail` body names the predicate. **Silence** names nothing —
   and that is the more serious of the two, because it means the box stopped reporting.
2. **On a `/fail`:** `systemctl --failed`, then `journalctl -u jobbliggaren-heartbeat -n 50`. The
   body's predicate name says where to look. Nothing here is an emergency by itself.
3. **On dead-man silence:** try SSH. If the box answers, the heartbeat or its config was
   disarmed — **treat that as a security event, not a maintenance one**, and check
   `ausearch -k jbl-units -k jbl-auditconf` before restarting anything, because a restart
   overwrites the state you would want to read. If the box does not answer, it is an availability
   incident until proven otherwise (the provider panel is the next instrument).
4. **If a compromise is suspected**, the Art. 33 assessment is the same one
   `failed-access-anomaly.md` §Steg 2.5–2.6 sets out (72 h from awareness, IMY): whether personal
   data was accessed, whose, and the reasoning — recorded whatever the conclusion. **Awareness
   starts when the page arrives, and this runbook is what makes that a datable moment.**

## 7. Verification log

Property · Instrument · Measured · Date — the same shape as `vps-deploy-stack.md` §5. **A row
without a date is a claim that cannot be told from one that has decayed.** Rows below are written
**before** measurement, deliberately: `deploy/` gets no integration coverage from `ci`, and the
install happens once.

| Property (gate) | Instrument | Measured | Date |
|---|---|---|---|
| **The baseline this gate was opened against** — no cadenced reader existed, and nothing left the box | `systemctl list-timers --all`, `crontab -l` + `/etc/cron.*`, `dpkg -l rsyslog auditd aide`, absence of journal-upload/remote config, `systemctl list-unit-files 'jobbliggaren*'` | Only `jobbliggaren-reconcile.{service,timer}` installed; no `rsyslog`/`auditd`/`aide`; no forwarding config; no cron reader; `systemctl --failed` empty **because nothing feeds it** | 2026-08-10 |
| **The SSH signal exists, names key and source, and had no reader** | `sudo sshd -T \| grep loglevel`, then `journalctl -u ssh -g "Accepted publickey"` | `loglevel VERBOSE`; accepted-publickey lines carry key fingerprint, source and timestamp — the signal is strong and was read by nobody. *(Values not reproduced here: the source address is personal data.)* | 2026-08-10 |
| **The journal window is computed, not declared** | `journalctl --disk-usage`; oldest entry timestamp; `systemd-analyze cat-config systemd/journald.conf` for the EFFECTIVE floor | | |
| **The audit rules are loaded in the kernel, not merely on disk** | `sudo auditctl -l \| grep -c 'jbl-'` against the `-k` keys in the rules file | | |
| **The rules survive a reboot** | same instrument, after `sudo systemctl reboot` | | |
| **A read of the key tmpfs produces a record naming uid and exe** | drill D1, then `ausearch -k jbl-key-tmpfs` | | |
| **A write to a watched control produces a record** | drill D2 | | |
| **The baseline-noise table is complete enough to discriminate** | §4's table against `ausearch -k jbl-key-tmpfs` over a window covering ≥1 injection and ≥1 applied reconcile | | |
| **The heartbeat reaches the expecter — measured at the expecter** | the service's own `last ping` timestamp, never the box's `curl` exit code | | |
| **Active fail pages, and the body names the predicate** | drill D3 | | |
| **The alarm self-clears with no operator action** | drill D4 | | |
| **The dead-man fires, and the delta is within the stated bound** | drill D5 — stop the timer, measure wall-clock from last successful ping to the page | | |
| **The payload carries no personal data** | the exact body the expecter stored, plus confirmation that no audit-record body appears in any ping | | |
| **auditd cannot suspend or stop logging when the disk fills** | `sudo grep -E '^(space_left_action\|admin_space_left_action\|disk_full_action\|disk_error_action\|max_log_file_action\|num_logs\|max_log_file) ' /etc/audit/auditd.conf` — read back from the file auditd actually reads, since there is no `auditd.conf.d` and no `cat-config` equivalent. **The drill is deliberately NOT run** — filling the production disk is a self-inflicted incident — so this row measures configuration and says so | | |
| **The RAM cost is measured, not asserted** | `systemctl show -p MemoryCurrent auditd`, `MemAvailable` before and after, against ADR 0122's honest free RAM | | |
| **The ping URL is single-purpose and absent from the repo** | `stat -c '%a %U:%G' /etc/jobbliggaren/detection.env`; `git log -S` finds no URL | | |
| **E-class's bound is conditional, and the condition is unmet today** | the floor set in the script against `systemctl list-unit-files 'jobbliggaren*'` | Floor set holds two timers. #197's and #198's units are not on the box, so this row **names the dependency** and does not claim their rows | 2026-08-10 |

## 8. What this runbook does not own

- **The production log sink** — [#1175](https://github.com/klasolsson81/jobbliggaren/issues/1175).
  M-7 delivers an **event channel**, not a sink. #1175 also inherits the upgrade that would make
  the forensic corpus survive a root attacker (an off-box copy) and the condition that makes
  `aide` owed.
- **Application-level alarms** (5xx rate, DB CPU) — [#1172](https://github.com/klasolsson81/jobbliggaren/issues/1172).
  M-7 covers the **host**; it neither delivers nor unblocks those.
- **The disk QUOTA** — a `df` threshold is detection, not a limit. The detection half is P5; the
  limit half is filed separately.
- **Granting the risk acceptance.** ADR 0123 is `Proposed` and Klas grants, never a session
  (§9.6). This mechanism narrows what that acceptance rests on; it does not close it.
- **Availability monitoring.** An external HTTP probe is a different obligation. Certificate
  RENEWAL is a real silent-death vector — Caddy attempts it with about a third of a 90-day
  certificate left, so a renewal that silently stops surfaces as an outage roughly a month later,
  and nothing here would notice. It is **named as uncovered** rather than absorbed into a
  detection-duty gate.
- **The recurring re-drill cadence.** Art. 32(1)(d) grounds "tested, not asserted"; the cadence
  row is owed once the mechanism has run through one full drill set.
