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
| **P4** | every watch RULE the file defines is loaded in the kernel, compared as (path, key) | the detection configuration's own integrity — a missing rules file, a missing `auditctl` and an unloaded rule are all failures, never passes |
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
- **It does not lift ADR 0123's scope limit.** Both mitigations the acceptance named are void as
  written — measured 2026-08-17, derivation in `vps-base-hardening.md` §11 — so the exit at the
  pre-real-data boundary is **re-grant**, not **close**. ⚠ **That does not release this gate.**
  `security-auditor`'s ruling of 2026-08-17 makes three requirements **cumulative**, and both M-7
  legs verified on §7's rows is requirement (3). The exit therefore rests on this capability too —
  it simply does not rest on it **alone**. Re-reading that ruling is hers (§9.6).
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
| `jbl-units` | operator installs, and the operator's own `git pull` in `/opt/jobbliggaren` | **nothing pulls this clone automatically** — measured, zero `git` invocations across `deploy/systemd/*.sh`, the reconcile unit included; it reconciles GHCR images against the compose file already on disk. So a unit file changing means an operator ran §5's pull, and a firing that no operator can account for is a finding rather than a deploy |
| `jbl-cron` | none | no user crontab exists on this box (measured 2026-08-10), so this family is close to pure signal |
| `jbl-deploy` | the operator's own `git pull`, when it brings a compose change; an operator editing `.env` by hand | **not the reconcile timer** — reconcile never pulls, so this family is silent on the hourly cadence and fires only on an attended visit. This watch covers `deploy/docker-compose.yml` and `deploy/.env` and nothing else, so it is `registration-gate.md`'s visit that fires it (it edits both) and **not** `master-key-ops.md`'s, which writes only `/run/jobbliggaren` and `/etc/systemd/system` — a firing during a key visit is a finding, not that runbook. Treat any unattributable firing the same way |
| `jbl-detection` | **two reads per heartbeat run**: systemd reads `EnvironmentFile=` and the script sources it | the timer runs every 15 min, so expect ~192 reads/day. Anything that is not those two is the finding this watch exists for |
| `jbl-auditconf` | `unattended-upgrades` when the auditd package is upgraded; §5's own `install` and `sed` under `/etc/audit` on any RE-RUN; an operator editing by hand | **The watch fires — measured 2026-08-10 with a counterfactual** (`touch -a /etc/audit/auditd.conf` → 1 record), which is why this row is a measurement and not an argument. It produced zero during the census for a narrower reason than an earlier version of this row claimed: nothing touched `/etc/audit` while the rule was live in that window. **On a FIRST load the family also cannot record §5 itself** — `augenrules` writes `/etc/audit/audit.rules` before the rule is in the kernel — but on a re-run the rule IS live and §5's own writes fire it, which is expected rather than a finding |

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

# TWO PATHS MUST EXIST BEFORE THE RULES LOAD, and rule 1 is the dangerous one.
# `augenrules --load` ABORTS THE ENTIRE LOAD at the first rule it cannot apply — measured
# 2026-08-10 twice. With /etc/jobbliggaren/detection.env absent (rule 18) the load stopped at
# line 23, `audit-rules.service` failed, `auditd.service` never started because it depends on
# it, and 17 of 19 rules were in the kernel with NOTHING being collected. A counterfactual with
# a bad rule 1 gave 0 of 2. So the order below is not tidiness.
#
# `-w /run/jobbliggaren` IS RULE 1 AND LIVES ON tmpfs, so it is destroyed by every reboot. The
# only thing that recreates it is jobbliggaren-tmpfiles.conf — and `audit-rules.service` is
# `After=systemd-tmpfiles-setup.service` (measured), so tmpfiles runs FIRST and the ordering
# works. Without that file installed, a reboot leaves rule 1 unloadable and the box gets ZERO
# audit rules and no auditd at all. Measured 2026-08-10: it was not installed on this box.
# THE COMMAND IS NOT COPIED HERE ON PURPOSE. jobbliggaren-tmpfiles.conf is #198's artefact and
# its install lives in master-key-ops.md §2 — which already has a second, drifted copy in
# backup-restore.md — and the drift is real but not the one an earlier version of this comment
# claimed: BOTH carry `systemd-tmpfiles --create`, but master-key-ops.md scopes it to the file
# while backup-restore.md applies every tmpfiles.d file on the box. A third home
# is how the next divergence starts, and this PR's own rules file says a gate with two owners
# has none. What M-7 needs is the DEPENDENCY named, not the command duplicated:
#   · rule 1 does not load unless /run/jobbliggaren exists at load time
#   · so master-key-ops.md §2's install must have run before the block below
# Verify rather than re-run. If either line prints nothing, go run that runbook's §2 first.
# Not an && chain: a missing conf file must not stop the second check from reporting.
ls -l /etc/tmpfiles.d/jobbliggaren.conf; ls -ld /run/jobbliggaren

# The env file is rule 18. Create it even when the URL is unknown: an empty file loads the
# rule, and the script refuses to post without a URL.
sudo install -d -m 0700 -o root -g root /etc/jobbliggaren
sudo touch /etc/jobbliggaren/detection.env && sudo chmod 0600 /etc/jobbliggaren/detection.env

sudo install -m 0640 -o root -g root \
  /opt/jobbliggaren/deploy/systemd/zz-jobbliggaren-audit.rules \
  /etc/audit/rules.d/zz-jobbliggaren.rules
sudo augenrules --load
sudo systemctl restart auditd
# `augenrules` is quiet on success and its failure is easy to skim past — judge the units.
systemctl is-active auditd; systemctl --failed --plain --no-legend --no-pager

# JUDGE THE KERNEL, NOT THE FILE. `auditctl -s` says "enabled" whether or not our rules loaded,
# and a watch on a path that did not exist at load time is not merely skipped — it aborts the
# load, as the block above records.
sudo auditctl -l | grep -c 'jbl-'
#    Expected: the number of WATCH RULES carrying a jbl- key — NOT the number of distinct keys.
#    Seven of the ten keys are carried by more than one path, so the two counts differ (19 vs 10
#    as this file stands), and comparing against the key count would show the operator a
#    discrepancy that does not exist. The heartbeat's P4 predicate compares (path, key) pairs for
#    the same reason: a single watch failing to load is invisible to a key-set comparison.
grep -v '^[[:space:]]*#' /opt/jobbliggaren/deploy/systemd/zz-jobbliggaren-audit.rules |
  grep -cE -- '^-w.*-k[[:space:]]+jbl-'

# 3. The capability URL. Create the check at the expecter first, then paste its ping URL into
#    the file step 2 created. THE LINE MUST BE `HEARTBEAT_PING_URL=<url>` — a bare URL would be
#    sourced as a command and the variable would stay unset, which produces a dead-man page with
#    no diagnosis. Format: deploy/detection/detection.env.example. Until then it stays empty and
#    the script refuses to post — the
#    correct state, and the dead-man reports the resulting silence.
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
# `-ts recent` is required: this box already carries jbl-key-tmpfs records from
# earlier drills, and without a time bound the drill passes on records it did not produce.
# stderr is NOT swallowed — a missing path must be visible, or a working mechanism reads as broken.
sudo dd if=/run/jobbliggaren/secrets/FieldEncryption__LocalMasterKeyBase64 of=/dev/null bs=1 count=1
sudo ausearch -if /var/log/audit/audit.log -ts recent -k jbl-key-tmpfs
# Record the FIELD NAMES plus uid/exe. Redact any addr=/hostname= before it reaches this file.

# D2 — a write to a watched control produces a record.
sudo touch -a /etc/sudoers && sudo ausearch -if /var/log/audit/audit.log -ts recent -k jbl-sudoers   # expect a record

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
   `ausearch -if /var/log/audit/audit.log -k jbl-units` and the same for `jbl-auditconf` before
   restarting anything, because a restart
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
| **The journal window is computed, not declared** | `journalctl --disk-usage`; oldest entry timestamp; `systemd-analyze cat-config systemd/journald.conf` for the EFFECTIVE limits | Effective after install: `Storage=persistent`, `SystemMaxUse=4G`, `SystemKeepFree=2G`. **The window is not limit-bound and therefore not yet computable from rotation:** the journal held ~24 MB spanning 2026-08-04 → 2026-08-10 against a 4 G ceiling — 0.6 %, so nothing has rotated and the 6.5 days is the journal's AGE, not its retention. At that write rate (~3.7 MB/day) the size limit implies a window of order a thousand days, i.e. the binding constraint is the box's age and `journalctl --vacuum`, not journald. Re-measure once auditd has been running for a week — the direction is not obvious and is unmeasured: audit records go to `/var/log/audit/audit.log`, not the journal, and before the install the kernel's audit messages reached the journal via printk, so a running auditd may well LOWER the journal rate. `/` is 251 G, so both journald defaults bind at their 4 G cap — see the config file's own note on which of the two settings actually widens the window. **RE-MEASURED 2026-08-15, transcribed before any vacuum (#1343):** 51.2 MB spanning 2026-08-04 01:26 → 2026-08-15, i.e. ~11.8 days, i.e. **4.3 MB/day cumulative**. ⚠ **That figure does NOT answer this row's auditd question and the caveat above stays open.** The cumulative mean spans both regimes; the marginal rate over the auditd period alone is `(51.2 − 24) / 5.3` ≈ **5.1 MB/day**, ~39 % up rather than "slightly". And auditd has run **5 days, not the week this row asked for** — with 2026-08-15's reboot drill inflating the last of them. Direction is *indicated* upward; it is not yet the measurement this row specified. Effective config read with this row's own instrument: `Storage=persistent`, `SystemMaxUse=4G`, `SystemKeepFree=2G`, and **no `MaxRetentionSec`** — so entries never age out and the ceiling is a size, not a time. **If #1343 is remediated by vacuum the window restarts there**, and this figure is the last reading before it. ⛔ **THAT IS WHAT HAPPENED, and the row read as a window that no longer existed until this line was added.** Measured 2026-08-17: `journalctl --vacuum-time=1s` was run by `jpadmin` on **2026-08-16 13:13:02**, executed as the remediation **Klas** chose for #1343 on `security-auditor`'s Blocker (the master key had reached the journal). **The cost — this window — is recorded and weighed at `vps-deploy-stack.md` §5 row 22**, so this is a decision's cost and not an unaccounted operator action — the journal's own oldest surviving entry is `journald` reporting its size seconds after that command, and the command itself is in the log because it outlived its own sweep by one line. So the window restarted there, and *"of order a thousand days"* is false of the present: **read it as hours-to-days, and regenerate before relying on it** — `sudo journalctl --disk-usage`, then `sudo journalctl -o short-iso \| head -1` for the oldest entry, which is the number that matters and is not the same as the size. ⚠ **The audit corpus is a separate file and survived** (`/var/log/audit/audit.log` plus its rotation), which is why the two must be measured apart — and it is measured too: oldest surviving record **2026-08-15T19:04Z**, bounded by `num_logs × max_log_file` from this table's own auditd row — a **size** ceiling, never a time one, so read the age and not the arithmetic. Regenerate with `sudo sh -c 'head -1 "$(ls -tr /var/log/audit/audit.log* | head -1)"'` — the substitution must be **inside** the privilege boundary; outside it the glob is denied, the substitution is empty, and `sudo head -1` then reads stdin instead of failing. **The journal is currently the shorter of the two**. **The forensic consequence is the gate's own subject:** Art. 33 requires establishing *whether* a breach happened, and an incident older than the shorter of these two windows is not establishable **from the host evidence channels this gate owns** — §2's Docker `json-file` stream and the `audit_log` table survived the vacuum and answer different questions, so a reader must not stop here. ⛔ **And it was routine maintenance, not the attacker §3's *What the mechanism does not do* models** — no `jbl-` watch covers `/var/log/journal`, so nothing reacted and nothing would. **Treat any future vacuum or rotation change as collapsing this gate's own evidence window**, and take the reading before, not after. The three earlier alarm events the expecter recorded in August are no longer corroborable here at all — the expecter's mail is their only surviving record, which is the argument #1175 owns in one measurement | 2026-08-10, re-measured 2026-08-15, **vacuum recorded 2026-08-17** |
| **The audit rules are loaded in the kernel, not merely on disk** | `sudo auditctl -l \| grep -c 'jbl-'` against the number of WATCH RULES in the rules file — **not** the number of distinct keys, which is a smaller number (seven keys are carried by several paths). Derive it with the same command §5 step 2 uses, so the two sections cannot drift apart: `grep -v '^[[:space:]]*#' <rules> \| grep -cE -- '^-w.*-k[[:space:]]+jbl-'` | **19 of 19**, after a repair the first attempt made necessary. The first load reached **17 of 19**: `/etc/jobbliggaren/detection.env` did not exist, `augenrules` aborted the whole load there, `audit-rules.service` failed, and **`auditd` did not start at all** — so the two missing rules were the unloadable one and the one after it in the file. Creating the file first loaded all 19. The install order in §5 now enforces this | 2026-08-10 |
| **The rules survive a reboot** | same instrument, after `sudo systemctl reboot` | **Not taken: requires a reboot of the production box**, which is Klas's to authorise and not a session's | — |
| **A read of the key tmpfs produces a record naming uid and exe** | drill D1, then `ausearch -if /var/log/audit/audit.log -k jbl-key-tmpfs` | **The watch fires and attributes correctly — but NOT against a real key.** `/run/jobbliggaren/secrets` exists (created by `master-key-ops.md` §2's tmpfiles unit, installed 2026-08-10) but holds **no key file** until #198's cutover, so the drill ran against a file created under the watched parent instead: 5 `jbl-key-tmpfs` records. A sibling drill on `/etc/systemd/system` gives the full attribution shape — `auid=1000 uid=0 exe="/usr/bin/rm" key="jbl-units"`, i.e. the login identity survives the sudo. ⚠ **RE-RUN AND DISCHARGED 2026-08-17, against the real key file** — #198's cutover has landed, so §5's post-cutover form applies: `sudo dd if=/run/jobbliggaren/secrets/FieldEncryption__LocalMasterKeyBase64 of=/dev/null bs=1 count=1`. The record names that path and carries `auid=1000 uid=0 … comm="dd" exe="/usr/bin/dd" key="jbl-key-tmpfs"`, so the login identity survives the sudo **against the asset ADR 0123's risk is measured on**, not merely against the parent. *(`addr=`/`hostname=` redacted before transcription, per §5's own instruction; no key material was read to a file — `of=/dev/null`, one byte.)* The cutover this row waited on has a **tracked** adjudicator: `vps-deploy-stack.md` §5 rows 21-25, dated, reading the key at this same watched path | 2026-08-10 (parent watch only) · **discharged 2026-08-17** (real key) |
| **A write to a watched control produces a record** | drill D2 | **Nine of the ten families fired during the census, and the tenth was proven separately.** `jbl-auditconf` produced zero here because nothing touched `/etc/audit` while the rule was live in that window — **not** because the family is inert. Counterfactual, measured 2026-08-10: `sudo touch -a /etc/audit/auditd.conf` then `sudo ausearch -if /var/log/audit/audit.log -ts recent -k jbl-auditconf` → **1 record**. (A cold load additionally cannot record itself, since `augenrules` writes `/etc/audit/audit.rules` before the rule is in the kernel; on a re-run the rule is live and §5's own writes fire it.) The nine: `jbl-units` 6 · `jbl-key-tmpfs` 5 · `jbl-sudoers` 4 · `jbl-cron` 3 · `jbl-accounts` 3 · `jbl-sshd` 2 · `jbl-deploy` 2 · `jbl-authkeys` 2 · `jbl-detection` 1. Regenerate with `sudo grep -oE 'key="jbl-[a-z-]+"' /var/log/audit/audit.log \| sort \| uniq -c` — **and read it against the key list in the rules file, because a family with zero events is absent from that output entirely**, so the command alone can never establish "every family" | 2026-08-10 |
| **The baseline-noise table is complete enough to discriminate** | §4's table against `ausearch -if /var/log/audit/audit.log -k jbl-key-tmpfs` over a window covering ≥1 injection and ≥1 applied reconcile | **Not taken: requires a window covering ≥1 injection and ≥1 applied reconcile.** ⚠ The 2026-08-16 vacuum did not touch the audit corpus, but it did remove the journal side of any such window taken before it | — |
| **The heartbeat reaches the expecter — measured at the expecter** | the service's own `last ping` timestamp, never the box's `curl` exit code. The expecter is a Healthchecks.io check in Klas's account, so the reading is **his to take and his to date** — a session cannot reach it | **Discharged 2026-08-17, and the arrival is corroborated against the box to the second.** Klas read the check's own notification: its `Status Changed to Down` and `Status Changed to Up` stamps equal the box's own journal stamps for the two drill runs exactly, and its ping counter advanced across them. So the transport works and the expecter's clock agrees with the box's. ⚠ **Note for the ROPA (`docs/runbooks/gdpr-processing-register.md`, gitignored) rather than for this gate: the expecter records the box's source address and reproduces it in the notification** — ADR 0126's acceptance is written on the *payload*, so it is **silent** here rather than contradicted, and the register is where this belongs — outside the payload, but it leaves the box all the same, and it is an IPv6 address, so outbound v6 works even though `vps-base-hardening.md` §4.2 removed the v6 *listener*. ⚠ **This measures EGRESS; that file's open question is INGRESS** — do not read one as the other | 2026-08-17 (Klas, at the expecter) |
| **Active fail pages, and the body names the predicate** | drill D3, read **at the expecter** — the stored `Last Ping Body`, never the box's log line, since the row's claim is about what arrived | **Discharged 2026-08-17.** The stored body is `failed-units=1:jbl-m7-drill.service` — P1's name and the offending unit, i.e. the predicate is named and not merely signalled, which is the whole value of this arm over the dead-man. `Last Ping Type` reads `Failure`, so the `/fail` verb reached the right endpoint. The box's own line was identical (`heartbeat: FAILING — failed-units=1:jbl-m7-drill.service`), which is what makes this a corroboration and not two readings of one source | 2026-08-17 (Klas, at the expecter; box side same second) |
| **The alarm self-clears with no operator action** | drill D4, read at the expecter | **Discharged 2026-08-17.** `reset-failed` on the transient unit was the only action; the next heartbeat posted success and the check returned to `Up` on its own. Stored body `ok`, `Last Ping Type` `Success`, and the expecter reported the outage's own duration — so recovery is visible at the expecter and needs no operator step there either | 2026-08-17 (Klas, at the expecter) |
| **The dead-man fires, and the delta is within the stated bound** | drill D5 — stop the timer, measure wall-clock from last successful ping to the page. **Read the notification's own trigger sentence**, not merely that a page arrived: the expecter says which of the two verbs put the check down, and only one of them is this row's subject | ⛔ **Discharged 2026-08-17, and it is the first in the check's recorded history — three prior DOWN events, all failure-signal.** The timer was stopped after a success ping and the check went down of its own accord — the notification reads *"success signal did not arrive on time, grace time passed"*, and its `Last Ping Type` still reads `Success` with body `ok`, so no failure ping was involved at any point. **The control for that reading is D3, the same day:** it flipped the very same field to `Failure`, so `Success` here means the field tracks that transition and did not make it — not that the field is inert. **The delta matches the design exactly — but read which half is observed:** `Status Changed to Down` is the expecter's **computed** deadline (`last ping + period + grace`), so on its own it restates configuration rather than measuring a send. The **observed** leg is the mail, and it bounds the surfacing at under a minute. Together: last success `12:19:13 +0200`, `Status Changed to Down` `13:19:13 +0200` — sixty minutes to the second, which is §3's `period 15 + grace 45` behaving as one design rather than as two numbers. The mail reached Klas in the same minute, so the human leg adds no material latency. Re-armed immediately after, with the floor set measured `enabled` **and** `active` again and predicates holding. ⚠ **The distinction this row exists for, and it is what the earlier evidence could not give:** every prior alarm the expecter recorded was *"received a failure signal"*, i.e. the box choosing to report. **This is the arm that survives an attacker who disarms the reporting** — §3's own claim that the two properties contain neither the other was, until now, half untested | 2026-08-17 (Klas at the expecter; box side same second) |
| **The payload carries no personal data** | the exact body the expecter stored, plus confirmation that no audit-record body appears in any ping. **Box side: a counterfactual against `sanitize_token`, never a reading of it** — extract it *with* `UNIT_SHAPE_REJECTED` (the constant sits on the line above; extracting the function alone makes every rejection print an empty string, which reads like a pass) and feed it adversarial shapes | **The shape control holds against every adversarial form measured, and one residual is named rather than claimed away.** Measured 2026-08-17: `192.0.2.1.service` → `unit-shape-rejected` · `someone.else@example.com.service` → `unit-shape-rejected` · `someone@example.com.service` → **`someone@.service`** — the pair is the point: **a dot in the local part is what refuses it, not its being an address**, so an undotted local part survives as the name half · `sshd@10.0.0.1:22-8.8.8.8:443.service` → `sshd@.service`, i.e. the real systemd per-connection form loses both addresses · legitimate units pass verbatim. ⚠ **The residual is a CLASS, not this one case: the plain-name class is preserved verbatim BY DESIGN** (the template name is diagnostic value the tests pin), so any personal datum shaped like a plain unit name reaches the wire — an undotted mail local part survives as the name half of an `@` form, and a personnummer is simply one member — `19850101-1234.service` → verbatim, because digits and a hyphen are exactly the plain-name class. It is **not reachable from anything this repo creates**: measured the same day, every unit name this repo creates is a static literal, and there is no template unit in `deploy/systemd/`. What would produce it is an operator naming a transient unit that way. **So the guarantee to rely on is "no domain and no instance data", not "no address" and not "no personal data of any shape"** — and D3's observed body on the wire was `failed-units=1:jbl-m7-drill.service`. The expecter-stored half is Klas's to read | 2026-08-17 (box side; expecter side outstanding) |
| **auditd cannot suspend or stop logging when the disk fills** | `sudo grep -E '^(space_left_action\|admin_space_left_action\|disk_full_action\|disk_error_action\|max_log_file_action\|num_logs\|max_log_file) ' /etc/audit/auditd.conf` — read back from the file auditd actually reads, since there is no `auditd.conf.d` and no `cat-config` equivalent. **The drill is deliberately NOT run** — filling the production disk is a self-inflicted incident — so this row measures configuration and says so | `admin_space_left_action = SYSLOG`, `disk_full_action = ROTATE`, `disk_error_action = SYSLOG` — the three Debian shipped as SUSPEND. The other four were already correct and are unchanged: `space_left_action = SYSLOG`, `max_log_file_action = ROTATE`, `num_logs = 5`, `max_log_file = 8`. **Zero occurrences of SUSPEND in the whole file.** `halt` and `single` are excluded by the enumeration above, not by that count — a SUSPEND sweep says nothing about them, and an operator who later re-runs only the sweep would wrongly conclude otherwise. Drill not run, by the reasoning in the instrument | 2026-08-10 (config only) |
| **The drill instrument itself works — `ausearch` must be pointed at the log** | `sudo ausearch -k <key>` compared against `sudo ausearch -if /var/log/audit/audit.log -k <key>`, with a raw `grep` on the log as the control | **`ausearch -k` returns `<no matches>` on this box while the events exist.** Measured: `ausearch -k jbl-units` → 0, `ausearch -if /var/log/audit/audit.log -k jbl-units` → 6, and `grep -c 'key="jbl-units"'` on the log → 6. Without `-if` the drill would report a working mechanism as broken, which is why every drill in §5 carries it | 2026-08-10 |
| **The RAM cost is measured, not asserted** | `systemctl show -p MemoryCurrent auditd`, against ADR 0122's honest free RAM. ⚠ **The "before and after" this row originally specified is no longer takeable** — auditd has run since its install, so there is no *before* left to read, and a `MemAvailable` delta across a running box measures every other actor too. `MemoryCurrent` on the unit is the narrower and better instrument: it attributes the cost to auditd rather than to the box | **Measured 2026-08-17 and the figure is single-digit megabytes** — regenerate with the command opposite rather than trusting this sentence, since a cgroup reading moves. Read against the box's own headroom the same day (`free -m`, `MemAvailable`), it is far below the noise on ADR 0122's capacity conditions: the watch-rules-only choice in §3 — no syscall rules — is what keeps it there, so **the mechanism is volume, not resident memory, and this instrument would understate a reversal** — the kernel audit backlog sits outside the unit's cgroup, and syscall rules' first-order cost here is the audit-corpus window in the row above. Treat the coupling as §3's reasoning, not as a measured counterfactual | 2026-08-17 |
| **The ping URL is single-purpose and absent from the repo** | `stat -c '%a %U:%G' /etc/jobbliggaren/detection.env`; then **classify** every match rather than count them — the host name is *supposed* to appear, in `deploy/detection/detection.env.example`, so a bare `git grep hc-ping` reads non-zero on a clean repo and a bare `git log -S` reads non-zero on a clean history. Split placeholder from capability without printing either: `git grep -ohE "hc-ping\.com/[0-9a-fA-F-]{8,}"` and the same over `git log --all -p -S`, then test each id against `^[0-]+$` | **Absent, measured by classification and not by absence.** File is `600 root:root`. Every match in tracked files **and across the whole history** classifies as the example's all-zero placeholder; the capability URL appears in neither. Single-purpose is Klas's reading at the expecter: one check, named for this box's heartbeat and tagged for this project | 2026-08-17 |
| **E-class's bound is conditional, and the condition is still unmet — but for three timers, not six** | the floor set in the script against `systemctl list-unit-files 'jobbliggaren*'` | Floor set holds **six** timers **in the repo** since 2026-09-03, held five from 2026-08-18, and three from 2026-08-15. ✅ **The box's copy followed at the pull on 2026-08-18 and now carries the same five** — measured there after the pull: `grep FLOOR_TIMERS /opt/jobbliggaren/deploy/systemd/jobbliggaren-heartbeat.sh` returns the five-name constant, and `journalctl -u jobbliggaren-heartbeat` after a hand run reads **`heartbeat: all predicates hold`**, i.e. the enlarged floor is satisfied rather than merely installed. **That journal line is the instrument, and neither the exit status nor an empty `systemctl --failed` may stand in for it** — the script always exits 0 by contract, and a disabled timer is not a failed unit, so both are blind to P3. Earlier the same day the clone still carried the three-timer constant, because `jobbliggaren-heartbeat.service` runs the script straight out of it. That order is deliberate and is the safe one: the window between merge and pull runs the OLD constant, so it cannot page. The reverse order would have paged — once, and then held the surface red. **Install and enable are two moments; merge and pull are two more.** **Two readings with this row's own instrument, and they must not be collapsed: the INSTALL census is 2026-08-15, the ENABLE census below is 2026-08-18** — on 2026-08-15 the instrument returned **three**, and the five it returns now did not exist until #1175's pair was armed, and the census has moved in both directions since 2026-08-10, so read install and enable separately — they are different facts here and only one of them is the floor-set trigger. **18 unit files are installed**; of the nine timers, exactly six are `enabled`/`active` — `jobbliggaren-heartbeat.timer`, `jobbliggaren-reconcile.timer`, `jobbliggaren-secrets-present.timer`, since 2026-08-18 `jobbliggaren-logship.timer` and `jobbliggaren-logship-fresh.timer`, and since 2026-09-03 `jobbliggaren-logprune.timer` — and `systemctl list-unit-files 'jobbliggaren*' --state=enabled` returns those same six. ⚠ **The prune timer is enabled on the box but not yet on the P3 floor THERE:** the repo constant reached six on 2026-09-03, and `jobbliggaren-heartbeat.service` runs the script straight out of the clone, so the box keeps measuring the five-name constant until its next `git pull --ff-only`. That lag is the safe direction — the window runs the OLD constant, which cannot page — and it is the same one the logship pair carried on 2026-08-18. `jobbliggaren-secrets-present.timer` was installed AND enabled during #198's cutover, which is the trigger this row names, so it joined the floor set in the same change. **The condition this row's heading calls unmet is exactly these: the other three timers are installed and `disabled`/`inactive`** — `jobbliggaren-backup.timer`, `-backup-fresh.timer` and `jobbliggaren-host-secrets-present.timer` — so none of them is owed a floor-set row. ⚠ **That installed-but-disabled state for #197's units is NOT what their runbook produces:** `backup-restore.md` §2 step 4 installs and `enable --now`s in one block. It exists because the #198 cutover session installed the files deliberately without arming them — `jobbliggaren-backup.sh --check` exits 1 on a box that has never backed up, so an armed `-fresh` timer would latch a permanent failure and make P1 vacuous. The **deferral of `enable`** still belongs to `jobbliggaren-host-secrets-present.timer` alone, exactly as the sentence below says; what generalised beyond it is only the *installation*. **#1175's pair discharged its own floor-set dependency 2026-08-18**, and it is the worked example of this row's own rule: the units had been installed since 2026-08-15 and were still owed nothing, because install is not the trigger; `enable --now` on both is what created the obligation and satisfied it in the same change. The reason the row was owed at all survives the discharge and is why it must not be undone lightly — a disabled logship timer is on no surface at all, since its service SKIPS rather than fails without the upload credential. **"Not on the box" is binary and #198's unit no longer is — and since #1329 it is no longer ONE unit either.** The absence detector split in two, and the two have different triggers, so a single row for "#198's timer" would now be false of one of them. `jobbliggaren-secrets-present.timer` (crypto, every 10 min) is enabled in the same visit as its install, because `--check` answers for the crypto set alone; nothing outside `master-key-ops.md` §2 holds it back. `jobbliggaren-host-secrets-present.timer` (host-only, hourly) is the one whose files may sit installed while it stays disabled, deliberately, until #197's `Backup__RcloneConfigBase64` exists — **the deferral moved to it, and to it only.** For both, the trigger for joining the floor set is `enable`, which is what `check_floor_timers` measures, and never install. `master-key-ops.md` §2-§3 owns that ordering and repeats each enable command where an operator will be standing | 2026-09-03 (prune timer enabled on the box and added to the repo floor set the same day; its box-side half is owed until the next pull) · 2026-08-18 (logship pair enabled on the box and added to the floor set the same day; 2026-08-15 for the crypto timer, likewise same-day — the first **positive** box measurement this row has carried, where the 2026-08-10 census carried the negative one with the same instrument; #1175's two added 2026-08-11 and #1329's split recorded 2026-08-13 were the repo-side entries) |

## 8. What this runbook does not own

- **The production log sink** — [#1175](https://github.com/klasolsson81/jobbliggaren/issues/1175).
  M-7 delivers an **event channel**, not a sink. #1175 also inherits the upgrade that would make
  the forensic corpus survive a root attacker (an off-box copy) and the condition that makes
  `aide` owed.
- **Application-level alarms** (5xx rate, DB CPU) — [#1172](https://github.com/klasolsson81/jobbliggaren/issues/1172).
  M-7 covers the **host**; it neither delivers nor unblocks those.
- **The disk QUOTA** — a `df` threshold is detection, not a limit. The detection half is P5; the
  limit half is filed separately.
- **The tmpfs directories the key watch depends on.** `/run/jobbliggaren` is created by #198's
  `jobbliggaren-tmpfiles.conf`, installed by [`master-key-ops.md`](master-key-ops.md) §2. M-7
  depends on it — watch rule 1 does not load without it, and an unloadable rule 1 takes the whole
  rule set and `auditd` with it — but M-7 does not own it, and §5 verifies rather than re-runs it.
- **Granting the risk acceptance.** ⚠ **Klas granted ADR 0123 on 2026-08-16** — do not ask him
  again; read the status in the ADR, never here. That closes the escalation's **`ungranted`** arm
  **literally and only** — its condition is *ungranted **or** unmitigated*, the mitigations are
  open, and ⚠ **the grant covers only the state WITHOUT real user data while M-7 is evaluated AT
  it**, so arm 1 gives no coverage where the condition is read. `security-auditor` ruled
  2026-08-17 that **M-7 does convert**; see `release-checklist.md` §2.6 point 3.5 for what would
  actually discharge it. ⚠ **Do not enumerate it here** — she restated requirement (1) the same day,
  and the earlier enumeration named as necessary work the two mechanisms she now expressly excludes.
  The capability is what the condition rests on, never issue numbers (#196 closed 2026-08-08; both
  legs are homed at #1201). This
  mechanism narrows what the acceptance rests on; it does not close it.
- **Availability monitoring.** An external HTTP probe is a different obligation. Certificate
  RENEWAL is a real silent-death vector — Caddy attempts it with about a third of a 90-day
  certificate left, so a renewal that silently stops surfaces as an outage roughly a month later,
  and nothing here would notice. It is **named as uncovered** rather than absorbed into a
  detection-duty gate.
- **The recurring re-drill cadence.** Art. 32(1)(d) grounds "tested, not asserted"; the cadence
  row is owed once the mechanism has run through one full drill set.
