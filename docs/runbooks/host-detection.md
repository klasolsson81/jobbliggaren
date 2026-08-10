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
| `systemctl --failed` | **nothing** — the only jobbliggaren unit installed was `jobbliggaren-reconcile`; the three units that feed this surface (`secrets-present`, `backup`, `backup-fresh`) were shipped but not installed | nobody | ∞ |
| systemd journal (persistent) | sshd VERBOSE, sudo, unit output | nobody | ∞ |
| Docker json-file logs | app and container logs — a **disjoint** stream from the host journal | nobody | ∞ |
| `audit_log` table | application auth/audit events | nobody | ∞ |
| reconcile stamp | "the box last applied at T" | nobody | ∞ |
| off-box: anything | **nothing left the box** — no `rsyslog`, `auditd`, `aide`, journal-upload, shipping agent or cron reader | — | — |
| weekly operator check (`backup-restore.md` §4) | prose procedure, not a scheduled job | Klas, when performed | asserted, never logged |

**The sharpest fact, and the reason the predicate in §3 carries non-vacuity arms:** the box's
stated "only alarm surface" was fed by nothing at all. A green light whose green is true of its
evidence and false of its subject is worse than no light.

## 3. The mechanism

Bound by senior-cto-advisor 2026-08-10. **One script, one unit pair, one check, two ping verbs.**

- **Predicate false → `POST <url>/fail`** with an allowlisted diagnosis. Fast and diagnostic.
  Does **not** survive an attacker who controls the box.
- **No ping at all → the expecter fires after its grace period.** Slow, carries no diagnosis, and
  it is the **one signal a root attacker cannot erase**: disarming the heartbeat *is* the alarm.

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
would compete directly with the journal floor and ADR 0122's capacity conditions on a box where
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

| Actor | Expected shape | Notes |
|---|---|---|
| `jobbliggaren-inject-secrets.sh` | writes, at injection | one burst per operator injection |
| api / worker container start | one read per secret file per start | **containers restart on every applied reconcile**, so this is not "rare" — it is as frequent as deploys |
| *(expected silent)* | `--check` runs `stat()`, which triggers none of `r`/`w`/`a` | a firing here is a real finding, not noise to be baselined away |

**Any actor not in this table is a finding.** The table is the discriminator; without it the rule
produces events nobody can triage, which is the same failure as no rule at all.

## 5. Install and drill

**Order matters and nothing enforces it — these lines are the enforcement.** The expecter is
armed **last**, so arming does not itself page.

```bash
# 1. Journal floor first: it is what gives every later step an evidence window.
sudo install -m 0644 -o root -g root \
  /opt/jobbliggaren/deploy/systemd/journald-jobbliggaren-retention.conf \
  /etc/systemd/journald.conf.d/10-jobbliggaren-retention.conf
sudo systemctl restart systemd-journald

# 2. auditd, then its configuration, then the rules. Installing the package starts it with
#    Debian's defaults, one of which can SUSPEND logging on a full disk — so the config lands
#    before the rules that raise the write rate.
sudo apt-get update && sudo apt-get install -y auditd
sudo install -d -m 0755 /etc/audit/auditd.conf.d
sudo install -m 0640 -o root -g root \
  /opt/jobbliggaren/deploy/systemd/auditd-jobbliggaren.conf \
  /etc/audit/auditd.conf.d/10-jobbliggaren.conf
sudo install -m 0640 -o root -g root \
  /opt/jobbliggaren/deploy/systemd/zz-jobbliggaren-audit.rules \
  /etc/audit/rules.d/zz-jobbliggaren.rules
sudo augenrules --load
sudo systemctl restart auditd

# JUDGE THE KERNEL, NOT THE FILE. `auditctl -s` says "enabled" whether or not our rules loaded,
# and the two ways they silently do not are a sort-order collision and a watch on a path that
# did not exist at load time.
sudo auditctl -l | grep -c 'jbl-'        # expect the number of -k keys in the rules file

# 3. The capability URL. Create the check at the expecter first, then paste its ping URL here.
sudo install -d -m 0700 -o root -g root /etc/jobbliggaren
sudo install -m 0600 -o root -g root \
  /opt/jobbliggaren/deploy/detection/detection.env.example /etc/jobbliggaren/detection.env
sudo nano /etc/jobbliggaren/detection.env      # paste the real URL; never echo it

# 4. The heartbeat.
sudo install -m 0755 /opt/jobbliggaren/deploy/systemd/jobbliggaren-heartbeat.sh \
  /opt/jobbliggaren/deploy/systemd/jobbliggaren-heartbeat.sh
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
| **auditd cannot suspend or halt the box on a full disk** | effective `disk_full_action` / `admin_space_left_action` / `max_log_file_action` / `num_logs`. **The drill is deliberately NOT run** — filling the production disk is a self-inflicted incident — so this row measures configuration and says so | | |
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
- **Availability monitoring.** An external HTTP probe is a different obligation. The certificate
  expiry roughly 60 days after cutover is a real silent-death vector and is **named here as
  uncovered** rather than absorbed into a detection-duty gate.
- **The recurring re-drill cadence.** Art. 32(1)(d) grounds "tested, not asserted"; the cadence
  row is owed once the mechanism has run through one full drill set.
