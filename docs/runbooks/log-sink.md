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

> **A SECOND PRECONDITION, AND THIS ONE IS ON THE SHIPPING AND NOT ON THE INSTALL (ADR 0050 G3,
> decided 2026-08-12).** The archive must write into **two** namespaces with different retention:
> `hostlogs/app/` (30 days) and `hostlogs/host/` (90 days). `jobbliggaren-logship.sh` writes
> **flat** today (`REMOTE_PREFIX=hostlogs`, basenames `app-`/`journal-`/`audit-`), so the two rules
> match nothing until the script splits the namespace. **The layout change and both lifecycle
> rules must land before the first object is shipped** — afterwards it is a migration of `age`
> objects nobody can read in order to sort them. The layout itself is a follow-up PR; this runbook
> records the constraint, not the mechanism.
>
> The window in which that is free is open **provided the prefix is still empty**, which follows
> from the timer not being installed but is not itself measured here — confirm with `rclone lsl`
> on `hostlogs/` (§4 names the same instrument) before relying on it. If objects already exist
> they are named `hostlogs/app-…`/`hostlogs/journal-…` and match **neither** new rule, i.e. no
> lifecycle at all on exactly the artefacts N-1 is about, while the register says 30/90.
>
> ⛔ **Which of the two windows is longer is a MEASUREMENT, not a standing fact — read it in
> `host-detection.md` §7 before relying on either, and do not restate it here.** An earlier form of
> this block asserted the local journal was longer by an order of magnitude and concluded that the
> off-box leg's 90 days was the constraint. **Measured 2026-08-17 that is inverted:** the local
> journal restarted at the 2026-08-16 vacuum, so 90 days is now the *longer* of the two — while
> the off-box leg transports nothing until its timer is installed, so today neither leg carries the
> backlog. The binding constraint was never journald's ceiling; it is `journalctl --vacuum`, which
> is exactly what fired.
> **Read this as raising the urgency of shipping, never as slack to defer it.**

> ⛔ **A THIRD PRECONDITION, AND IT IS NOT DISCHARGED BY ARMING — ADDED 2026-08-15 (#1343,
> `security-auditor` Major 2). THE FIRST RUN THAT ACTUALLY EXECUTES SHIPS THE WHOLE JOURNAL.**
> `jobbliggaren-logship.sh` narrows its window only when a cursor file already exists
> (`if [[ -f "$JOURNAL_CURSOR_FILE" ]]`); with no cursor, `journalctl` reads from the beginning.
>
> **The trigger is the first run with `Backup__RcloneConfigBase64` present on tmpfs — NOT the day
> this pair is armed.** Arming early ships nothing and is this section's intended order: the
> service's `ConditionPathExists` makes a credential-less run a *skip*, the script never executes,
> and `/var/lib/jobbliggaren`'s cursor is therefore never written. So an operator who has already
> armed the pair has **not** passed this precondition — they have merely not reached it. It comes
> due at `master-key-ops.md` §3's injection visit, which is where it is repeated.
>
> ⛔ **BUT ARMING IS NOT WITHOUT CONSEQUENCE, AND AN EARLIER WORDING HERE SAID IT WAS
> ("harmless"). It moves the gate rather than removing a risk, and after 2026-08-18 the gate is
> gone.** Before the pair was armed, shipping took TWO acts — inject the credential **and** arm —
> and the second was performed by someone reading this block. Armed, it takes ONE: the injection
> alone. `OnCalendar=*:17` with `Persistent=true` then fires within the hour, with no cursor, i.e.
> the whole-journal run this precondition governs — **without anyone having read this precondition
> at all.** `master-key-ops.md` §3 says *"up to an hour"* about the same firing, but says it about
> a stale freshness alarm rather than about this.
>
> **So the precondition is owed AT THE INJECTION, by the person performing it, and it is now the
> only thing standing between the credential and the archive.** If it cannot be measured in that
> same visit, disarm **both** timers before injecting — and re-arm both **only once the journal
> measures clean** (a vacuum produces that state; a further rotation does not):
>
> ```bash
> sudo systemctl disable --now jobbliggaren-logship.timer jobbliggaren-logship-fresh.timer
> # … measure the journal, then — ONLY IF IT MEASURES CLEAN — re-arm BOTH. A vacuum produces that
> # state; a further rotation does not:
> sudo systemctl enable --now jobbliggaren-logship.timer jobbliggaren-logship-fresh.timer
> ```
>
> ⚠ **Disarming the shipping timer alone is the trap.** `-fresh` carries the same
> `ConditionPathExists`, so its shield lifts at the same injection; `--check` then dies on the
> absent stamp and lands in `systemctl --failed`, i.e. M-7's **P1**, which the heartbeat puts into
> `/fail` every 15 minutes (the heartbeat's cadence, not the probe's — `-fresh` itself runs hourly).
> ⚠ That is a POST cadence, not a page cadence: `systemctl --failed` **latches**, so the expecter
> notifies on the transition and a second genuine fault inside the window changes only a body nobody
> reads. Read it as one alarm and then silence, never as a repeating siren.
> And the hand-start that would clear **that latched P1** is not available to you here:
> mechanically it runs fine with the timer disabled, but it would ship the very journal you
> disarmed for.
> ⚠ **That deafness is scale-invariant — the CORRECT full disarm carries it too.** Once the box has
> pulled the `FLOOR_TIMERS` edit, P3 lights `floor-timer-down=` for both timers, one notification
> goes, and the box is deaf to P1–P5 for the rest of the window. The path this section *instructs*
> has the same property as the trap it warns against, which is why the re-arm is a duty. **A
> hand-start does not clear P3 either** — that predicate wants the TIMER enabled and active, not a
> service run.
> ⚠ **The re-arm is a duty precisely BECAUSE the disarm is invisible.** It removes the archive and
> its only staleness probe together, and until the box pulls the `FLOOR_TIMERS` edit it lights no
> `floor-timer-down=` to remind anyone it is off.
>
> **Why it mattered, and what changed — the RULE survives, its GROUND does not.** #1343 put the
> master key in this box's persistent journal in plaintext (row 22's own instruments, through
> `sudo`'s argv logging). ✅ **That is discharged as of 2026-08-16:** the journal was vacuumed and
> re-measured against **all four** secrets — master key and each of the three peppers, each with
> its own positive control — at **0** (`master-key-ops.md` §3 carries the measurement and is the
> one home for it). **Do not read this paragraph as a live finding; it is why the rule exists.**
>
> **The rule still binds, on a condition rather than on a standing fact:** the discharge expires
> the moment anything writes key material to the journal again, so the state is **re-measured
> before shipping and never inherited from the line above.** That is the whole of what the two
> runbooks must keep saying together — an earlier revision of this block asserted the plaintext key
> as a present fact for two days after it stopped being one, while `master-key-ops.md` had already
> recorded the discharge and warned, in as many words, that this block must not drift from it.
>
> Concretely, what a first run before a fresh measurement writes: the field-encryption key into
> `hostlogs/journal-*.export.gz.age` at OVH — **with no age bound at all**, because `REMOTE_PREFIX` is flat `hostlogs/` and G3's two
> rules target `hostlogs/app/` and `hostlogs/host/`, so they match nothing; and §4 records that the
> rules are not applied in any case. The object is encrypted to an age recipient whose private key
> ADR 0129 places on the same device as `jobbpilot_vps_ed25519` — i.e. Klas's workstation,
> which row 26 already records as holding root on this box. (Reproduced rather than cited:
> ADR 0129 is gitignored per §6.5, and row 26 sets that convention for this same ADR.) One compromised workstation would
> then yield root, the upload credential, the age key **and** the master key out of a retained
> artefact, with the box not even running.
>
> **Bind the condition to the state, not to the issue:** wait until the journal demonstrably
> carries no plaintext key. #1343 offers two remedies and only one produces that — a **vacuum**
> does; a **further rotation** retires the exposed generation but leaves its bytes in the journal,
> so it does *not* discharge this precondition.

```bash
# The clone. NOT `git pull` blind — on this box a pull is a DEPLOY that
# jobbliggaren-reconcile.timer applies within the hour, and one such pull cost a 13-minute
# outage on 2026-08-10. Read what it would bring FIRST, then pull; the fetch+log is what makes
# the pull deliberate, never a substitute for it. If deploy/docker-compose.yml is involved, read
# vps-deploy-stack.md §3b — the apply goes through the reconcile unit, never a hand-typed
# `up -d` — and §3a as well when the pull brings migrations or the box runs a pinned IMAGE_TAG,
# which is what the schema gate's exits 3 and 4 answer for.
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

**Both timers join `FLOOR_TIMERS` in `jobbliggaren-heartbeat.sh` at this point, and not before** —
that list is the non-vacuity floor for M-7's P3, a timer named there must be enabled and active or
the box pages, and the file's own `KEEP IN SYNC AS UNITS LAND` note binds at the moment of
**`enable`, never of install** — `check_floor_timers` measures `is-enabled` AND `is-active`, so a
named-but-disabled timer fails every fire, i.e. holds that surface red — one page at the
transition, then silence, not a repeating one. (An earlier wording here said
*installation*, alone among the four homes that carry this rule.) That edit landed 2026-08-18; until it did, P3 was vacuous for these two, because a disabled
`logship.timer` is on no surface at all, which is the hole the `-fresh` pair exists to close, one level up.

**Make that edit IN THE REPO, as its own PR, and pull it down here — never in the clone.**
`jobbliggaren-heartbeat.service` runs the script straight out of `/opt/jobbliggaren`, and the file
is git-tracked, so editing it on the box makes every later `git pull --ff-only` fail — the pull
that is this box's whole deploy path, three lines above. The handover row lives in
[`host-detection.md`](host-detection.md) §7, which is where the heartbeat script says to look.

> **DONE 2026-08-18 (#1175). Both timers are armed on the box and both are named in the floor.**
> The install half had been half-done since 2026-08-15 and nothing said so: all four unit files
> sat in `/etc/systemd/system` bit-identical to the clone's, `daemon-reload`/`enable` had never
> run, and `systemctl list-units` — which lists LOADED units — reported nothing at all, so the
> state read as "not installed" on the axis most people measure. **Read `list-unit-files` when the
> question is the disk and `is-enabled` when the question is the floor; `list-units` answers
> neither.**
>
> **No `git pull` was performed and none was needed**, which is why this visit was not a deploy:
> all four units were verified `sha256`-identical across the repo, `/opt/jobbliggaren/deploy/systemd/`
> and `/etc/systemd/system/` before arming, so the step above reduces to `daemon-reload` +
> `enable --now`. The `FLOOR_TIMERS` edit is the one thing here that does travel through the clone,
> and it rides the normal PR path as this section requires.
>
> Verified at the arming, not inferred: both timers `enabled`/`active`; `systemctl --failed` empty;
> and — **the primary evidence that nothing shipped** — both services ran and **skipped**:
> `Result=success`, `ConditionResult=no`, journal `unmet condition check
> ConditionPathExists=…/host-secrets/Backup__RcloneConfigBase64`. The condition is what proves it,
> because a skip means the script never executed at all.
> `/var/lib/jobbliggaren/` also carries **no cursor file**, and that corroborates rather than
> proves: the cursor is promoted only after a *successful upload*, so once the credential exists a
> run can fail mid-upload and leave no cursor while bytes have already left the box. Absence of a
> cursor stops meaning "nothing shipped" on the day the condition starts passing.

**The cross-cover the `-fresh` unit names is not installed yet, and the sequence has to say so.**
Installing before #197's host secrets exist is fine and intended — both units then skip on the same
`ConditionPathExists`, which is the designed state, not a fault. But `-fresh.service`'s residual
paragraph leans on `jobbliggaren-host-secrets-present.service` alarming on a missing credential, and
that unit is **#198's and is not on the box** (`host-detection.md` §7, measured 2026-08-10). Until
it is watching, a credential-less archive that has never once succeeded is watched by nothing.

**The window closes at `enable`, not at install, and an earlier wording here bounded it by the
wrong event (#1329).** It read "between installing these units and installing #198's" — true while
one predicate answered for both sets, because installing #198's units then came with a timer an
operator could arm. It does not survive the split: `jobbliggaren-host-secrets-present.timer` can be
installed and left disabled, and `check_floor_timers` measures `enable` precisely because an
installed-but-disabled timer fails nothing and therefore covers nothing (`host-detection.md` §7
carries that distinction). So the bound is the credential arriving, which is the same event the
next paragraph turns on.

**And the obvious first horn is not available inside that window, which is why it is spelled out
rather than offered.** Enabling `jobbliggaren-host-secrets-present.timer` here would close the gap,
but its `--check-host` demands `Backup__RcloneConfigBase64` — the very file whose absence *defines*
this window, and the same one both `logship` units skip on. Enabled here it fails every fire and
lights `systemctl --failed` permanently, trading a watched gap for an alarm surface nobody reads.
So: **enable it the moment that credential is injected** — `master-key-ops.md` §3 repeats the
command there — **and until then verify the credential by hand**, which is the whole instruction
while the window is open, not a fallback:

```bash
sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh --check-host
```

**The instrument on that line changed at #1329, and the previous one was a hand-rolled
`sudo test -s` on the path.** Until the split no predicate read this file and nothing else:
`--check` answered for both sets at once, so in this window it exited 1 whatever else was true and
could not tell you the file had arrived. `--check-host` reads exactly `HOST_SECRET_KEYS` — today
that one file — and it is the stricter test of the two, since `test -s` passes on a file holding a
single space that the reader treats as absent.

**#1329 does not close this window, but it does stop the window from reaching the crypto half.**
`--check-host` waits on the same credential the archive waits on, so the gap above is unchanged.
What changed is that `jobbliggaren-secrets-present.timer` is now enabled at `master-key-ops.md` §2
regardless of #197 — so what goes unwatched inside the window is the archive alone, and no longer
the box. `master-key-ops.md` §2 owns the ordering.

**The lifecycle rules on the new prefixes are a separate, Klas-owned step**, and until they exist
the archive is append-only with no age bound at all — i.e. it discharges the off-box obligation and
**not** the Art. 5(1)(e) one. ~~Create it against `hostlogs/` with its own retention number~~ —
**the number stopped being one on 2026-08-12** (ADR 0050 G3): create **two**,
`g3-hostlogs-app-30-days` on `hostlogs/app/` and `g3-hostlogs-host-90-days` on `hostlogs/host/`.
Do not extend `main/`'s rule to cover either, because the retention question for logs is a
different question from the one K4 answered for database artefacts — and, for the same reason one
layer in, the app stream's question is not the journal's.

**And that number is a legal parameter rather than a cost trade-off, which is easy to miss because
the backup prefix's number is not.** A backup's answer to an erasure request is crypto-erasure —
the DEK artefacts are per data subject, so one person can be struck out. **That mechanism cannot
apply here:** a `hostlogs/` artefact is one hour of logs for *every* user inside a single `age`
envelope this box cannot decrypt, so selective erasure is structurally impossible rather than
merely awkward. The time limit therefore **is** the whole Art. 17 answer for this leg. The register
carries the legal basis and this reasoning; ADR 0050 `Amendment 2026-08-11` gate **G3** carries the
obligation.

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

Neither password is typed at a prompt, and that is not convenience. **A `read` inside a pasteable
block consumes the following lines as keystrokes** — the defect this file's §3 was rebuilt to
remove — and a one-line fence containing only a `read` is worse, because the paste's own trailing
newline answers the prompt with an empty string. The current value is already in `.env`; the new
one is generated and printed once.

```bash
OLD_PW=$(sudo sed -n 's/^SEQ_ADMIN_PASSWORD=//p' /opt/jobbliggaren/deploy/.env | head -1)
NEW_PW=$(openssl rand -base64 24)
LOGIN=$(python3 -c 'import json,sys; print(json.dumps({"Username":"admin","Password":sys.argv[1],"NewPassword":sys.argv[2]}))' "$OLD_PW" "$NEW_PW" \
  | curl -s -c /tmp/seq.jar -H 'Content-Type: application/json' --data-binary @- "http://$SEQ_IP/api/users/login")
CSRF=$(printf '%s' "$LOGIN" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("CsrfToken",""))')
[ -n "$CSRF" ] && printf 'signed in — STORE THIS ADMIN PASSWORD NOW: %s\n' "$NEW_PW" || printf 'LOGIN FAILED: %s\n' "$LOGIN"
```

> **Put that password in the password manager before you continue, and write it into `.env` in
> step 8's edit as well.** From this point Seq's own store is the source of truth and `.env`'s
> value is stale for the *running* instance — it is read only on a first run against an empty
> volume, so it will not let you back in. **But that is only half the fact:** lose `seq_data` and
> the next start IS a first run, which reads `.env` again. A stale value there is the password to
> a fresh Seq, and the one nobody will think to look for. `.env.example` carries the same warning
> at the key itself.

**5.** Turn on the ingestion gate **before** creating the key, because until it is on the key bounds
nothing. Measured on a stock 2026.1 with authentication enabled: `RequireApiKeyForWritingEvents`
defaults to **`false`**, and with it false a `POST /api/events/raw?clef` is accepted with a valid
key, an **empty** key, a **wrong** key and **no key at all** — 201 in all four cases. With it true:
201 for the valid key, **401** for the other three. **No environment variable sets this** — all three of
`SEQ_API_REQUIREAPIKEYFORWRITINGEVENTS`, `SEQ_REQUIREAPIKEYFORWRITINGEVENTS` and
`SEQ_FIRSTRUN_REQUIREAPIKEYFORWRITINGEVENTS` — the last using the prefix Seq actually does read —
were all measured silently ignored — so it cannot be shipped fail-closed in compose and has to be a step here.

```bash
curl -s -b /tmp/seq.jar -H 'Content-Type: application/json' -H "X-Seq-CsrfToken: $CSRF" -X PUT \
  -d '{"Name":"requireapikeyforwritingevents","Value":true,"Id":"setting-requireapikeyforwritingevents"}' \
  "http://$SEQ_IP/api/settings/setting-requireapikeyforwritingevents"
# expect: "Value":true. A PUT carrying only Id+Value answers 500 — measured, and measured to be a
# no-op: the stored value is unchanged afterwards, so a 500 here means "not applied", never
# "half applied". Name+Value+Id is the form that was run.
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

**8.** One editor pass, **three** values, **in this order**, and then re-arm reconcile.
`SEQ_SERVER_URL` is what ATTACHES the provider, so setting it before the key exists points both
hosts at a sink that answers 401 to every event — which step 5 measured is exactly what an empty
key gets. Interactive editor again, deliberately outside this block:
`sudo nano /opt/jobbliggaren/deploy/.env` — `SEQ_INGEST_API_KEY=<token>` first, then
`SEQ_SERVER_URL=http://seq:5341`, and **`SEQ_ADMIN_PASSWORD` overwritten with the new password
from step 4** (it is stale for the running Seq and is the first-run password if the volume is
ever lost — step 4's note says why).

```bash
sudo systemctl start jobbliggaren-reconcile.timer
sudo docker compose -f /opt/jobbliggaren/deploy/docker-compose.yml up -d api worker
```

**9.** **Prove an event arrived, in the same session as the install.** Everything up to here talks
to port 80; `SEQ_SERVER_URL` points the app at **5341**, a listener nothing in steps 1–8 has
touched, and a transport failure there is silent — the MEL provider drops events without failing
its host. So the one setting that decides whether the sink works would otherwise be set blind, last,
and proven by nothing. This fills §4's `The MEL provider actually posts to 5341, not 80` row.

```bash
curl -s -b /tmp/seq.jar -G --data-urlencode 'count=5' "http://$SEQ_IP/api/events" \
  | python3 -c 'import json,sys; e=json.load(sys.stdin); print(len(e), "events"); [print(x["Timestamp"], x.get("Level","Information")) for x in e[:3]]'
rm -f /tmp/seq.jar
```

Zero events means the app leg is not connected — re-read step 8's ordering before anything else.
The loop itself is measured: against `datalust/seq:2026.1`, a CLEF `POST` to the **5341** listener
answers `201` and the event reads back through this query on 80, so a zero here is the app's
configuration and not the split.

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
| **The corpus survives erasure on the box** | `journalctl --vacuum-time=1s` and truncate the audit log, then list the prefix | **Still blocked by row 27d, but the decision is no longer what blocks it** — Klas gave GO on the user-policy `Deny` 2026-08-12; what remains is the apply and its counter-measurement (`put-object` OK, `delete-object` `AccessDenied`). Until then an attacker with the box's credential deletes the off-box copy too. **Apply it together with G3's TWO lifecycle rules — `hostlogs/app/` 30 days and `hostlogs/host/` 90 — and in this order**, which is the same order `vps-deploy-stack.md` row 27d carries and is repeated here because the person installing the log archive opens THIS file: (1) **TAKEN 2026-08-12 — arm (1) does not fire, on the ground that there are zero registered users (`AspNetUsers` = 0, registration closed), NOT on the grep:** Caddy carries no `log` directive, so a successful token click leaves no line at all and the grep can only see 5xx. Re-check when `AspNetUsers > 0`. Re-run before shipping if the box has served real users since: `sudo docker logs jobbliggaren-caddy 2>&1 \| grep -nE 'bekrafta-(epost\|konto)\|aterstall-losenord'` — **`-n`, not `-c`: the arm is worded over REAL token-bearing rows, and a count cannot tell those from your own test traffic, so the hits are read rather than tallied**; (2) if any of them is real, clear it before anything ships, because that flips ADR 0050's N-1 to Blocker; (3) satisfy G2; (4) then the `Deny` and both rules. After the `Deny` the box cannot remove its own old objects, so the provider-side rules become the only thing that does — which is exactly what makes the Art. 17 answer true, and why an emergency purge afterwards needs Klas's own OVH console credential rather than the box's | |
| **BOTH lifecycle rules remove objects, measured as an EFFECT** | plain prefix listing per prefix after N+1 days, older artefacts gone | **Not measured — the rules are not applied and nothing is shipped yet.** Two prefixes, two numbers (ADR 0050 gate G3; Klas set the number 2026-08-12, senior-cto-advisor set the scope the same day): **`hostlogs/app/` = 30 days** (`g3-hostlogs-app-30-days`) and **`hostlogs/host/` = 90 days** (`g3-hostlogs-host-90-days`). The split is not a complication — the container already carries `k4-main-artefacts-30-days` and `deks-outlive-main-90-days`, so the set of numbers is unchanged. **Why app is 30:** one object is an hour of logs for every user inside one `age` envelope this box holds no key to, so selective erasure is structurally impossible and **the time limit IS the whole Art. 17 answer for that leg**. **Why host is 90:** `journal-*`/`audit-*` are the root-surviving forensic corpus #1175 exists for, and 30 would cut the evidence window to 30 days — the defect `journald-jobbliggaren-retention.conf` already records making once. Row 27b's discipline: a rule is a claim; the disappearance is the measurement | |
| **The journal cursor neither drops nor duplicates across a restart** | stop the timer, reboot, run once, compare the last entry of run *n* against the first of run *n+1* | | |
| **The MEL provider actually posts to 5341, not 80** | `Seq:ServerUrl` set to the 5341 form, then confirm events arrive | **MEASURED ON THE BOX 2026-08-23, AND THE SPLIT HOLDS — no fallback was taken.** §3 step 8 set `SEQ_SERVER_URL=http://seq:5341` on both hosts and step 9 read events back through the query API on 80 within a minute of the apply, from **both** hosts — `Jobbliggaren.Api` alongside worker-side `Hangfire.*`, `WorkerMemoryTrendService` and `RefreshLandingStatsJob`. That end-to-end read is the whole point of the row: a transport failure on 5341 is **silent**, because the MEL provider drops events without failing its host, so the one setting that decides whether the sink works would otherwise be proven by nothing. **The counterfactual stands unchanged for any future move:** if ingestion is ever measured unavailable on 5341, fall back to `:80` **and record that the split was measured unreachable** — never switch silently. **The reason is not the one an earlier draft of this row gave:** the split is NOT what stops a compromised container reading the corpus back (see the 401 row below — that claim was measured false on 2026-08-11). What a fallback costs is that the query API moves into the app's own configuration, where an ingest-only key still cannot read but a second mistake no longer has to clear a second hurdle | 2026-08-23 |
| **An empty `.env` value counts as NOT SUPPLIED** | unset `SEQ_SERVER_URL`, confirm both hosts stay console-only | **Measured in BOTH directions on the box 2026-08-23, which is what makes it a measurement rather than a reading of the `:-` default.** *Not supplied:* before §3, `.env` carried neither `SEQ_SERVER_URL` nor `SEQ_INGEST_API_KEY`; `Seq__ServerUrl` rendered **empty** on api and worker, the provider was unattached, and the store held zero application events. *Supplied:* step 8 set it and events from both hosts arrived within a minute. `Email__Provider` in the same file is a measured case where empty ≠ unset (`??` does not catch `""`), so this could not be assumed from the `:-` default alone | 2026-08-23 |
| **The one-time setup completes with NO change to sshd** | the §3 command sequence, end to end | **§3 WAS RUN ON THIS BOX 2026-08-23, END TO END, WITH NO CHANGE TO sshd AND NO TUNNEL.** Step 1 needed no edit — `.env` already carried `SEQ_ADMIN_PASSWORD` and neither of the other two keys, which is exactly the state step 1 prescribes. Step 4's login answered the designed `401 MustChangePassword` and succeeded with `NewPassword`; step 5's gate PUT returned `"Value":true`; step 6 returned `['Ingest']`; step 7 returned **201** with id `retentionpolicy-36` and `RetentionTime 30.00:00:00`; step 8 set `SEQ_SERVER_URL` **after** step 7 and re-armed reconcile; step 9 read the events back. The 2026-08-11 image-side rehearsal (`datalust/seq:2026.1`, `sha256:91e93ff2…`) stands as the mechanism's provenance and is no longer the only evidence. ⚠ **ONE DEVIATION, RECORDED RATHER THAN IMPLIED:** steps 1 and 8 prescribe `nano`, which a non-interactive session cannot drive, so step 8's edit was made by an atomic rewrite preserving owner and mode. What bounds that deviation is a measurement and not care: the file's non-`SEQ_` lines hashed **identically** before and after (`fc7d8776…`), 45 lines → 47 with two keys appended and `600 root:root` unchanged — so the edit is known to have touched the three `SEQ_` values and nothing else | 2026-08-11 (image only) · 2026-08-23 (box: §3 run end to end) |
| **The query API refuses an unauthenticated read FROM ANOTHER CONTAINER** | from a SIBLING container — and the instrument has to exist there. Measured 2026-08-11: `aspnet:10.0-noble` (api, worker) and `postgres:18.3` carry **neither** `curl` nor `wget`; `redis:8.6-alpine` carries `wget`; `datalust/seq:2026.1` carries `curl` — **and seq is the one container that must not be the source**, since a run from inside seq against `http://seq` proves nothing about a sibling and still returns the expected 401. Use `sudo docker exec jobbliggaren-redis wget -S -O- 'http://seq/api/events?count=1'`, or an ephemeral `docker run --rm --network <stack> …` | **401 FROM A SIBLING, measured on this box 2026-08-23:** `sudo docker exec jobbliggaren-redis wget -S -O /dev/null 'http://seq/api/events?count=1'` → **401**, with both controls taken in the same run — `http://seq/` → **200** and `http://seq:5341/api/events` → **404**. **This is the read control, and the port split is not it.** The 200 is the point: measured 2026-08-11 on a bridge with no `ports:` and reproduced here, a sibling container reaches `seq:80` freely, because containers on a user-defined bridge reach each other by default and `stack` is unsegmented. What holds is authentication — `/api/events` answers 401 unauthenticated there — and that 5341 carries no query API at all. A 200 on `/api/events` would mean someone turned authentication off | 2026-08-23 |
| **Ingestion REFUSES an unkeyed write at this box** | `curl -X POST "http://$SEQ_IP/api/events/raw?clef"` with no `X-Seq-ApiKey` | **401, AND IT IS MEASURED AS AN EFFECT — the control was taken in both directions on this box 2026-08-23.** *Before* §3 step 5: an unkeyed `POST /api/events/raw?clef` answered **201**. *After* the gate PUT: the same unkeyed POST answered **401**, and a wrong key **401**. The control crosses the threshold, so the 401 measures the gate rather than the instrument — which is the difference between this cell and one that merely reads a setting back. **This is the row that says whether the ingest key bounds anything**, and the box now reproduces what a stock 2026.1 measured on 2026-08-11: with `RequireApiKeyForWritingEvents=false` — the DEFAULT — no key, an empty key and a wrong key are all accepted (201). The gate is §3 step 5, it has no environment variable, and it is therefore a step someone can skip | 2026-08-23 |
| **A retention policy exists on this box AT ALL — the question [#1170](https://github.com/klasolsson81/jobbliggaren/issues/1170) asks, and the one the row below silently presupposes** | **`GET /api/retentionpolicies`, authenticated.** The session is §3 step 4's — cookie jar plus `X-Seq-CsrfToken` — and with it `curl -s -b <jar> "http://$SEQ_IP/api/retentionpolicies"` returns the policy list. **An unauthenticated GET answers 401 and not an empty list, so a 401 is never evidence of absence** and must not be read as one. ⛔ **NOT the metastore entity-prefix grep this row named until 2026-08-23** — the Measured cell records why that instrument is fail-open for this property | **A POLICY EXISTS: one, all events, 30 days.** Created by §3 step 7 on 2026-08-23 — `POST /api/retentionpolicies` **201**, id `retentionpolicy-36`, `RetentionTime 30.00:00:00`, `DataSource Stream`, `RemovedSignalExpression null` — and read back through `GET /api/retentionpolicies` in the same authenticated session. That is what [#1170](https://github.com/klasolsson81/jobbliggaren/issues/1170) asks for, and §3 step 8 attached the provider only afterwards. ⛔ **THE INSTRUMENT THIS ROW NAMED UNTIL 2026-08-23 WAS FAIL-OPEN, AND THAT IS A SEPARATE FINDING FROM THE ANSWER IT GAVE.** With the policy demonstrably present, `grep -ac "retentionpolicy-"` over `metastore.collection.*.docc` still returns **0** — and `setting-` returns **0** while the ingestion gate it would count is measurably in force (401 on an unkeyed write). Dropping the trailing hyphen returns **1** and **39**. The store does not write these entity ids in the `<prefix>-` text form the grep assumed, so the row's controls proved only that the file is text-searchable — never that a `retentionpolicy-` document would match **if one existed**, which is the property a control has to cross. **The zero read on 2026-08-23 was therefore true but not measured by that instrument.** What actually carried that conclusion were the three independent corroborations taken the same day, and they stand as the record of the prior state: the container's environment carried only `ACCEPT_EULA` and `SEQ_FIRSTRUN_ADMINPASSWORD`; `/data/Seq.json` had **no retention section**; and a login on the `.env` password returned `{"Error":"A password change is required."}`, which showed §3 was unrun. **datalust document no retention configuration at all — there is no `SEQ_RETENTION_*` family and no config surface — so this can never be repaired in compose, only by §3 step 7** | 2026-08-23 |
| **Seq's retention policy removes events, and the DISK follows later** | query for an event older than the window; separately, `du` on the volume | **Not measured, and measurable for the first time only now:** the policy was created 2026-08-23, so the earliest date an event can be old enough to test removal is **2026-09-22**. Until then this cell is scheduling and not a claim about the box. Retention makes events inaccessible; space returns via compaction, which runs at **7 days of file age** — bytes can persist past the 30-day mark, and the register says so | |
| **The seq container has a healthcheck** | `docker inspect -f '{{.State.Health.Status}}'` | **Not shipped.** The compose file omits it deliberately rather than shipping an unverified probe that would paint a permanent "unhealthy"; whether this image carries a client to call Seq's health endpoint was not measured. This row closes that | |
| **`logship` runs, and its cost is bounded** | `systemd-analyze` on the unit; artefact size per run | **Half of this row is measured and the other half cannot be yet, and they are not the same claim.** *Runs:* both services were started by hand at the arming and both reached their condition and **skipped** — `Result=success`, `ExecMainStatus=0`, `ConditionResult=no`, journal `unmet condition check ConditionPathExists=…/host-secrets/Backup__RcloneConfigBase64`, `systemctl --failed` empty. *Cost:* **not measured and not measurable here** — a skipped run executes no script, ships no artefact and writes no cursor, so there is no size and no duration to bound. This cell is owed again at the first run with #197's credential present, which is also the run the third precondition in §2 governs | 2026-08-18 (runs only) |
| **The pair is armed AND on an alarm surface** | `systemctl is-enabled` + `is-active` on both timers; then that both names appear in `FLOOR_TIMERS` | **Two halves on two axes, and BOTH are now box measurements.** *Armed:* both `enabled`/`active` on the box 2026-08-18. *On the surface:* **live on the box since the pull later the same day.** `jobbliggaren-heartbeat.service` runs the script straight out of `/opt/jobbliggaren`, so the floor row reached the box only at that `git pull --ff-only` — which §2 owns and which is itself a deploy, and which is why this row was recorded as *owed* for the hours in between. `grep FLOOR_TIMERS /opt/jobbliggaren/deploy/systemd/jobbliggaren-heartbeat.sh` returns the five-name constant there. **And the floor is satisfied rather than merely enlarged** — `journalctl -u jobbliggaren-heartbeat` after a hand run on the box reads **`heartbeat: all predicates hold`**, which is the measurement that separates "P3 now names them" from "P3 now fails on them". ⚠ **Do not substitute the unit's exit status or an empty `systemctl --failed` for that line.** The script declares `THIS SCRIPT ALWAYS EXITS 0`, so `Result=success` is guaranteed whatever the verdict — `jobbliggaren-heartbeat.test.sh`'s P3 case asserts exactly that — and a disabled timer never becomes a failed unit, so `systemctl --failed` is blind to P3 by construction. An earlier revision of this row cited both of them. ⚠ **And arming was safe for a reason that EXPIRES.** `jobbliggaren-logship-fresh.service` carries the same `ConditionPathExists` as the shipping unit and therefore skips; `jobbliggaren-backup-fresh.service` carries none — measured 2026-08-18, and that difference is the whole asymmetry with the pair `host-detection.md` §7 declines to arm. The shield lifts the moment #197's credential is injected: `--check` then dies on the absent stamp (`shipping has never succeeded`) and latches until the first successful ship, with `-fresh` firing at `:00` **before** the shipping timer's `*:17`. `master-key-ops.md` §3's `systemctl start jobbliggaren-logship.service` is what bounds that window and is MANDATORY at injection, not tidiness. **Arming without the floor row would have been the weaker half of a pair, not a smaller version of the whole:** the shipping service SKIPS without the upload credential, and a skip is inactive rather than failed, so an armed-but-unnamed timer that later stopped would still have been on no surface. `check_floor_timers` measures `is-enabled` AND `is-active`, which is why the repo edit follows the `enable` and never the install | 2026-08-18 |

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
