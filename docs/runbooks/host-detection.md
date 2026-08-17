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
| **The journal window is computed, not declared** | `journalctl --disk-usage`; oldest entry timestamp; `systemd-analyze cat-config systemd/journald.conf` for the EFFECTIVE limits | Effective after install: `Storage=persistent`, `SystemMaxUse=4G`, `SystemKeepFree=2G`. **The window is not limit-bound and therefore not yet computable from rotation:** the journal held ~24 MB spanning 2026-08-04 → 2026-08-10 against a 4 G ceiling — 0.6 %, so nothing has rotated and the 6.5 days is the journal's AGE, not its retention. At that write rate (~3.7 MB/day) the size limit implies a window of order a thousand days, i.e. the binding constraint is the box's age and `journalctl --vacuum`, not journald. Re-measure once auditd has been running for a week — the direction is not obvious and is unmeasured: audit records go to `/var/log/audit/audit.log`, not the journal, and before the install the kernel's audit messages reached the journal via printk, so a running auditd may well LOWER the journal rate. `/` is 251 G, so both journald defaults bind at their 4 G cap — see the config file's own note on which of the two settings actually widens the window. **RE-MEASURED 2026-08-15, transcribed before any vacuum (#1343):** 51.2 MB spanning 2026-08-04 01:26 → 2026-08-15, i.e. ~11.8 days, i.e. **4.3 MB/day cumulative**. ⚠ **That figure does NOT answer this row's auditd question and the caveat above stays open.** The cumulative mean spans both regimes; the marginal rate over the auditd period alone is `(51.2 − 24) / 5.3` ≈ **5.1 MB/day**, ~39 % up rather than "slightly". And auditd has run **5 days, not the week this row asked for** — with 2026-08-15's reboot drill inflating the last of them. Direction is *indicated* upward; it is not yet the measurement this row specified. Effective config read with this row's own instrument: `Storage=persistent`, `SystemMaxUse=4G`, `SystemKeepFree=2G`, and **no `MaxRetentionSec`** — so entries never age out and 4 G implies a window of order a thousand days. **If #1343 is remediated by vacuum the window restarts there**, and this figure is the last reading before it | 2026-08-10, re-measured 2026-08-15 |
| **The audit rules are loaded in the kernel, not merely on disk** | `sudo auditctl -l \| grep -c 'jbl-'` against the number of WATCH RULES in the rules file — **not** the number of distinct keys, which is a smaller number (seven keys are carried by several paths). Derive it with the same command §5 step 2 uses, so the two sections cannot drift apart: `grep -v '^[[:space:]]*#' <rules> \| grep -cE -- '^-w.*-k[[:space:]]+jbl-'` | **19 of 19**, after a repair the first attempt made necessary. The first load reached **17 of 19**: `/etc/jobbliggaren/detection.env` did not exist, `augenrules` aborted the whole load there, `audit-rules.service` failed, and **`auditd` did not start at all** — so the two missing rules were the unloadable one and the one after it in the file. Creating the file first loaded all 19. The install order in §5 now enforces this | 2026-08-10 |
| **The rules survive a reboot** | same instrument, after `sudo systemctl reboot` | | |
| **A read of the key tmpfs produces a record naming uid and exe** | drill D1, then `ausearch -if /var/log/audit/audit.log -k jbl-key-tmpfs` | **The watch fires and attributes correctly — but NOT against a real key.** `/run/jobbliggaren/secrets` exists (created by `master-key-ops.md` §2's tmpfiles unit, installed 2026-08-10) but holds **no key file** until #198's cutover, so the drill ran against a file created under the watched parent instead: 5 `jbl-key-tmpfs` records. A sibling drill on `/etc/systemd/system` gives the full attribution shape — `auid=1000 uid=0 exe="/usr/bin/rm" key="jbl-units"`, i.e. the login identity survives the sudo. ⚠ **RE-RUN AND DISCHARGED 2026-08-17, against the real key file** — #198's cutover has landed, so §5's post-cutover form applies: `sudo dd if=/run/jobbliggaren/secrets/FieldEncryption__LocalMasterKeyBase64 of=/dev/null bs=1 count=1`. The record names that path and carries `auid=1000 uid=0 … comm="dd" exe="/usr/bin/dd" key="jbl-key-tmpfs"`, so the login identity survives the sudo **against the asset ADR 0123's risk is measured on**, not merely against the parent. *(`addr=`/`hostname=` redacted before transcription, per §5's own instruction; no key material was read to a file — `of=/dev/null`, one byte.)* The cutover this row waited on is recorded in the session state, not here — it is gitignored, so a reader without local docs has this row's date and no adjudicator for that half | 2026-08-10 (parent watch only) · **discharged 2026-08-17** (real key) |
| **A write to a watched control produces a record** | drill D2 | **Nine of the ten families fired during the census, and the tenth was proven separately.** `jbl-auditconf` produced zero here because nothing touched `/etc/audit` while the rule was live in that window — **not** because the family is inert. Counterfactual, measured 2026-08-10: `sudo touch -a /etc/audit/auditd.conf` then `sudo ausearch -if /var/log/audit/audit.log -ts recent -k jbl-auditconf` → **1 record**. (A cold load additionally cannot record itself, since `augenrules` writes `/etc/audit/audit.rules` before the rule is in the kernel; on a re-run the rule is live and §5's own writes fire it.) The nine: `jbl-units` 6 · `jbl-key-tmpfs` 5 · `jbl-sudoers` 4 · `jbl-cron` 3 · `jbl-accounts` 3 · `jbl-sshd` 2 · `jbl-deploy` 2 · `jbl-authkeys` 2 · `jbl-detection` 1. Regenerate with `sudo grep -oE 'key="jbl-[a-z-]+"' /var/log/audit/audit.log \| sort \| uniq -c` — **and read it against the key list in the rules file, because a family with zero events is absent from that output entirely**, so the command alone can never establish "every family" | 2026-08-10 |
| **The baseline-noise table is complete enough to discriminate** | §4's table against `ausearch -if /var/log/audit/audit.log -k jbl-key-tmpfs` over a window covering ≥1 injection and ≥1 applied reconcile | | |
| **The heartbeat reaches the expecter — measured at the expecter** | the service's own `last ping` timestamp, never the box's `curl` exit code | | |
| **Active fail pages, and the body names the predicate** | drill D3 | | |
| **The alarm self-clears with no operator action** | drill D4 | | |
| **The dead-man fires, and the delta is within the stated bound** | drill D5 — stop the timer, measure wall-clock from last successful ping to the page | | |
| **The payload carries no personal data** | the exact body the expecter stored, plus confirmation that no audit-record body appears in any ping | | |
| **auditd cannot suspend or stop logging when the disk fills** | `sudo grep -E '^(space_left_action\|admin_space_left_action\|disk_full_action\|disk_error_action\|max_log_file_action\|num_logs\|max_log_file) ' /etc/audit/auditd.conf` — read back from the file auditd actually reads, since there is no `auditd.conf.d` and no `cat-config` equivalent. **The drill is deliberately NOT run** — filling the production disk is a self-inflicted incident — so this row measures configuration and says so | `admin_space_left_action = SYSLOG`, `disk_full_action = ROTATE`, `disk_error_action = SYSLOG` — the three Debian shipped as SUSPEND. The other four were already correct and are unchanged: `space_left_action = SYSLOG`, `max_log_file_action = ROTATE`, `num_logs = 5`, `max_log_file = 8`. **Zero occurrences of SUSPEND in the whole file.** `halt` and `single` are excluded by the enumeration above, not by that count — a SUSPEND sweep says nothing about them, and an operator who later re-runs only the sweep would wrongly conclude otherwise. Drill not run, by the reasoning in the instrument | 2026-08-10 (config only) |
| **The drill instrument itself works — `ausearch` must be pointed at the log** | `sudo ausearch -k <key>` compared against `sudo ausearch -if /var/log/audit/audit.log -k <key>`, with a raw `grep` on the log as the control | **`ausearch -k` returns `<no matches>` on this box while the events exist.** Measured: `ausearch -k jbl-units` → 0, `ausearch -if /var/log/audit/audit.log -k jbl-units` → 6, and `grep -c 'key="jbl-units"'` on the log → 6. Without `-if` the drill would report a working mechanism as broken, which is why every drill in §5 carries it | 2026-08-10 |
| **The RAM cost is measured, not asserted** | `systemctl show -p MemoryCurrent auditd`, `MemAvailable` before and after, against ADR 0122's honest free RAM | | |
| **The ping URL is single-purpose and absent from the repo** | `stat -c '%a %U:%G' /etc/jobbliggaren/detection.env`; `git log -S` finds no URL | | |
| **E-class's bound is conditional, and the condition is unmet today** | the floor set in the script against `systemctl list-unit-files 'jobbliggaren*'` | Floor set holds **three** timers since 2026-08-15. **Re-measured on the box that day with this row's own instrument**, and the census has moved in both directions since 2026-08-10, so read install and enable separately — they are different facts here and only one of them is the floor-set trigger. **16 unit files are installed**; of the eight timers, exactly three are `enabled`/`active` — `jobbliggaren-heartbeat.timer`, `jobbliggaren-reconcile.timer`, `jobbliggaren-secrets-present.timer` — and `systemctl list-unit-files 'jobbliggaren*' --state=enabled` returns those same three. `jobbliggaren-secrets-present.timer` was installed AND enabled during #198's cutover, which is the trigger this row names, so it joined the floor set in the same change. **The other five timers are installed and `disabled`/`inactive`** — `jobbliggaren-backup.timer`, `-backup-fresh.timer`, `-logship.timer`, `-logship-fresh.timer` and `jobbliggaren-host-secrets-present.timer` — so none of them is owed a floor-set row, and the fix that added the third is complete rather than partial. ⚠ **That installed-but-disabled state for #197's and #1175's units is NOT what their runbooks produce:** `backup-restore.md` §2 step 4 installs and `enable --now`s in one block. It exists because the #198 cutover session installed the files deliberately without arming them — `jobbliggaren-backup.sh --check` exits 1 on a box that has never backed up, so an armed `-fresh` timer would latch a permanent failure and make P1 vacuous. The **deferral of `enable`** still belongs to `jobbliggaren-host-secrets-present.timer` alone, exactly as the sentence below says; what generalised beyond it is only the *installation*. **#1175 adds two more to the same dependency** (`jobbliggaren-logship.timer` and `jobbliggaren-logship-fresh.timer`, delivered in the repo and installed by `log-sink.md` §2) — until the floor names them, a disabled logship timer is on no surface at all, since its service SKIPS rather than fails without the upload credential. **"Not on the box" is binary and #198's unit no longer is — and since #1329 it is no longer ONE unit either.** The absence detector split in two, and the two have different triggers, so a single row for "#198's timer" would now be false of one of them. `jobbliggaren-secrets-present.timer` (crypto, every 10 min) is enabled in the same visit as its install, because `--check` answers for the crypto set alone; nothing outside `master-key-ops.md` §2 holds it back. `jobbliggaren-host-secrets-present.timer` (host-only, hourly) is the one whose files may sit installed while it stays disabled, deliberately, until #197's `Backup__RcloneConfigBase64` exists — **the deferral moved to it, and to it only.** For both, the trigger for joining the floor set is `enable`, which is what `check_floor_timers` measures, and never install. `master-key-ops.md` §2-§3 owns that ordering and repeats each enable command where an operator will be standing | 2026-08-15 (crypto timer enabled on the box and added to the floor set the same day — the first **positive** box measurement this row has carried, where the 2026-08-10 census carried the negative one with the same instrument; #1175's two added 2026-08-11 and #1329's split recorded 2026-08-13 were the repo-side entries) |

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
  2026-08-17 that **M-7 does convert**, and that building the mitigations is not enough without a
  **new** grant covering that state plus **both M-7 legs delivered and verified on this file's
  verification rows** — the capability, never issue numbers (#196 closed 2026-08-08; both legs are
  homed at #1201). This
  mechanism narrows what the acceptance rests on; it does not close it.
- **Availability monitoring.** An external HTTP probe is a different obligation. Certificate
  RENEWAL is a real silent-death vector — Caddy attempts it with about a third of a 90-day
  certificate left, so a renewal that silently stops surfaces as an outage roughly a month later,
  and nothing here would notice. It is **named as uncovered** rather than absorbed into a
  detection-duty gate.
- **The recurring re-drill cadence.** Art. 32(1)(d) grounds "tested, not asserted"; the cadence
  row is owed once the mechanism has run through one full drill set.
