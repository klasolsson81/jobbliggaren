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

**8.** **The gate first, then one editor pass, three values, in this order, and then re-arm
reconcile.** `SEQ_SERVER_URL` is what ATTACHES the provider, so setting it before the key exists
points both hosts at a sink that answers 401 to every event — which step 5 measured is exactly what
an empty key gets.

**The gate is against step 7, and it exists because nothing else would notice.** This section is
re-run only after a `docker volume rm seq_data`, and a re-run that skips step 7 attaches both hosts
to a Seq with **no retention policy at all** — `UserId`, IP, `UserAgent` and `EmailHash` flowing
into an unbounded store, which is the state
[#1170](https://github.com/klasolsson81/jobbliggaren/issues/1170) is about, reached silently. Until
now the only guard was written order plus operator attention. Run this in the **same** session as
steps 4–7.

```bash
(
CODE=$(curl -s -o /tmp/seq.retention -w '%{http_code}' -b /tmp/seq.jar "http://$SEQ_IP/api/retentionpolicies")
if [ "$CODE" = 000 ]; then
  echo "GATE FAILED: no HTTP response from $SEQ_IP - the question was never asked."
  rm -f /tmp/seq.jar /tmp/seq.retention; exit 2
elif [ "$CODE" != 200 ]; then
  echo "GATE FAILED: GET answered $CODE - the question was never answered, which is NOT absence."
  rm -f /tmp/seq.jar /tmp/seq.retention; exit 2
fi
python3 -c 'import json,sys; d=json.load(open("/tmp/seq.retention")); print("GATE OK:", [(x.get("Id"), x.get("RetentionTime")) for x in d]) if d else sys.exit("GATE FAILED: retention list is EMPTY - step 7 has not run. Do NOT continue.")' || { rm -f /tmp/seq.jar /tmp/seq.retention; exit 1; }
)
echo "gate exit: $?"
# expect: GATE OK: [('retentionpolicy-NN', '30.00:00:00')]   then   gate exit: 0
# 1 = step 7 did not run. 2 = the question was never answered (401, or 000 = unreachable).
# The subshell is why a failing gate does not log you out of a pasted interactive shell.
```

Then the three values: `SEQ_INGEST_API_KEY=<token>` from step 6 first, then
`SEQ_SERVER_URL=http://seq:5341`, and **`SEQ_ADMIN_PASSWORD` overwritten with the new password from
step 4** (it is stale for the running Seq and is the first-run password if the volume is ever lost —
step 4's note says why). An interactive operator uses
`sudo nano /opt/jobbliggaren/deploy/.env`. **A session with no TTY uses the block below, which is
sanctioned rather than improvised** — §4 records the 2026-08-23 run deviating exactly here, because
this file prescribed only the editor.

```bash
(
KEEP=$(sudo grep -v -E '^SEQ_(INGEST_API_KEY|SERVER_URL|ADMIN_PASSWORD)=' /opt/jobbliggaren/deploy/.env) || [ $? = 1 ] || exit 1
{ [ -n "$KEEP" ] && printf '%s\n' "$KEEP"
  printf 'SEQ_INGEST_API_KEY=%s\nSEQ_SERVER_URL=http://seq:5341\nSEQ_ADMIN_PASSWORD=%s\n' "$TOKEN" "$NEW_PW"
} | sudo sh -c '
cd /opt/jobbliggaren/deploy || exit 1
T=$(mktemp .env.XXXXXX) || exit 1
trap "rm -f $T" EXIT INT TERM
cat > "$T" || exit 1
grep -q "^SEQ_INGEST_API_KEY=." "$T" || exit 1
grep -q "^SEQ_ADMIN_PASSWORD=." "$T" || exit 1
chown root:root "$T" && chmod 600 "$T" && mv "$T" .env'
)
echo "write exit: $?"
sudo stat -c 'mode=%a owner=%U:%G' /opt/jobbliggaren/deploy/.env
# expect: write exit: 0   then   mode=600 owner=root:root
```

**Neither secret is ever an argument.** `printf` is a shell builtin, so the values reach `sudo` down
the pipe instead of through argv, which `/proc/<pid>/cmdline` and sudo's own `COMMAND=` line both
expose. §4 measures both forms.
`NEW_PW` is already set by step 4; set `TOKEN` to what step 6 printed.

**And `mv`, not a redirect into `.env`.** reconcile reads this file hourly; a redirect rewrites the
target in place and a reader can catch it half-written, while a rename replaces it atomically. The
temp file is made in the **same directory** so the rename stays inside one filesystem.

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
rm -f /tmp/seq.jar /tmp/seq.retention
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
| **The MEL provider actually posts to 5341, not 80** | `Seq:ServerUrl` set to the 5341 form, then confirm events arrive | **MEASURED ON THE BOX 2026-08-23; no fallback was taken.** §3 step 8 set `SEQ_SERVER_URL=http://seq:5341` on both hosts and step 9 read events back through the query API on 80 within a minute of the apply, from **both** hosts — `Jobbliggaren.Api` alongside worker-side `Hangfire.*`, `WorkerMemoryTrendService` and `RefreshLandingStatsJob`. That end-to-end read is the whole point of the row: a transport failure on 5341 is **silent**, because the MEL provider drops events without failing its host, so the one setting that decides whether the sink works would otherwise be proven by nothing. **The counterfactual stands unchanged for any future move:** if ingestion is ever measured unavailable on 5341, fall back to `:80` **and record that the split was measured unreachable** — never switch silently. **The reason is not the one an earlier draft of this row gave:** the split is NOT what stops a compromised container reading the corpus back (see the 401 row below — that claim was measured false on 2026-08-11). What a fallback costs is that the query API moves into the app's own configuration, where an ingest-only key still cannot read but a second mistake no longer has to clear a second hurdle | 2026-08-23 |
| **An empty `.env` value counts as NOT SUPPLIED** | unset `SEQ_SERVER_URL`, confirm both hosts stay console-only | **Measured in BOTH directions on the box 2026-08-23, which is what makes it a measurement rather than a reading of the `:-` default.** *Not supplied:* before §3, `.env` carried neither `SEQ_SERVER_URL` nor `SEQ_INGEST_API_KEY`; `Seq__ServerUrl` rendered **empty** on api and worker, the provider was unattached, and the store held zero application events. *Supplied:* step 8 set it and events from both hosts arrived within a minute. `Email__Provider` in the same file is a measured case where empty ≠ unset (`??` does not catch `""`), so this could not be assumed from the `:-` default alone | 2026-08-23 |
| **The one-time setup completes with NO change to sshd** | the §3 command sequence, end to end | **§3 WAS RUN ON THIS BOX 2026-08-23, END TO END, WITH NO CHANGE TO sshd AND NO TUNNEL.** Step 1 needed no edit — `.env` already carried `SEQ_ADMIN_PASSWORD` and neither of the other two keys, which is exactly the state step 1 prescribes. Step 4's login answered the designed `401 MustChangePassword` and succeeded with `NewPassword`; step 5's gate PUT returned `"Value":true`; step 6 returned `['Ingest']`; step 7 returned **201** with id `retentionpolicy-36` and `RetentionTime 30.00:00:00`; step 8 set `SEQ_SERVER_URL` **after** step 7 and re-armed reconcile; step 9 read the events back. The 2026-08-11 image-side rehearsal (`datalust/seq:2026.1`, `sha256:91e93ff2…`) stands as the mechanism's provenance and is no longer the only evidence. ⚠ **ONE DEVIATION, RECORDED RATHER THAN IMPLIED:** steps 1 and 8 prescribe `nano`, which a non-interactive session cannot drive, so step 8's edit was made by an atomic rewrite preserving owner and mode. What bounds that deviation is a measurement and not care: the file's non-`SEQ_` lines hashed **identically** before and after (`fc7d8776…`), 45 lines → 47 with two keys appended and `600 root:root` unchanged — so the edit is known to have touched **nothing outside the `SEQ_` lines**. It says nothing about which of the three `SEQ_` values moved: the hash excludes those lines by construction, which is what makes it a bound on the blast radius rather than a proof of the write | 2026-08-11 (image only) · 2026-08-23 (box: §3 run end to end) |
| **The query API refuses an unauthenticated read FROM ANOTHER CONTAINER** | from a SIBLING container — and the instrument has to exist there. Measured 2026-08-11: `aspnet:10.0-noble` (api, worker) and `postgres:18.3` carry **neither** `curl` nor `wget`; `redis:8.6-alpine` carries `wget`; `datalust/seq:2026.1` carries `curl` — **and seq is the one container that must not be the source**, since a run from inside seq against `http://seq` proves nothing about a sibling and still returns the expected 401. Use `sudo docker exec jobbliggaren-redis wget -S -O- 'http://seq/api/events?count=1'`, or an ephemeral `docker run --rm --network <stack> …` | **401 FROM A SIBLING, measured on this box 2026-08-23:** `sudo docker exec jobbliggaren-redis wget -S -O /dev/null 'http://seq/api/events?count=1'` → **401**, with both controls taken in the same run — `http://seq/` → **200** and `http://seq:5341/api/events` → **404**. **This is the read control, and the port split is not it.** The 200 is the point: measured 2026-08-11 on a bridge with no `ports:` and reproduced here, a sibling container reaches `seq:80` freely, because containers on a user-defined bridge reach each other by default and `stack` is unsegmented. What holds is authentication — `/api/events` answers 401 unauthenticated there — and that 5341 carries no query API at all. A 200 on `/api/events` would mean someone turned authentication off | 2026-08-23 |
| **Ingestion REFUSES an unkeyed write at this box** | `curl -X POST "http://$SEQ_IP/api/events/raw?clef"` with no `X-Seq-ApiKey` | **401, AND IT IS MEASURED AS AN EFFECT — the control was taken in both directions on this box 2026-08-23.** *Before* §3 step 5: an unkeyed `POST /api/events/raw?clef` answered **201**. *After* the gate PUT: the same unkeyed POST answered **401**, and a wrong key **401**. The control crosses the threshold, so the 401 measures the gate rather than the instrument — which is the difference between this cell and one that merely reads a setting back. **This is the row that says whether the ingest key bounds anything**, and the box now reproduces what a stock 2026.1 measured on 2026-08-11: with `RequireApiKeyForWritingEvents=false` — the DEFAULT — no key, an empty key and a wrong key are all accepted (201). The gate is §3 step 5, it has no environment variable, and it is therefore a step someone can skip. ⚠ **The *before* half of that control left a residue, and it is named here rather than left for a later reader to find:** its 201 wrote one event, which is consequently the corpus's **oldest** and the only one predating the retention policy. It carries no personal data — a fixed CLEF line reading `jbl-1170 install probe: unkeyed write control` — and the policy covers it on event timestamp like everything else — whether removal actually follows is the row below's, and is not measurable before 2026-09-22 | 2026-08-23 |
| **A retention policy exists on this box AT ALL — the question [#1170](https://github.com/klasolsson81/jobbliggaren/issues/1170) asks, and the one the row below silently presupposes** | **`GET /api/retentionpolicies`, authenticated.** ⚠ **Do not reuse §3 step 4's block for this** — it sends `NewPassword` and therefore ROTATES the credential it authenticates with, so run verbatim after the install it destroys what it measures with. **The non-rotating form omits `NewPassword`**, and keeps step 4's stdin transport rather than putting the credential on curl's argv: `python3 -c 'import json,sys; print(json.dumps({"Username":"admin","Password":sys.argv[1]}))' "$PW" \| curl -s -c /tmp/seq.jar -H 'Content-Type: application/json' --data-binary @- "http://$SEQ_IP/api/users/login"` (`$PW` is the admin password step 4 printed and told you to store), then the read `curl -s -b /tmp/seq.jar "http://$SEQ_IP/api/retentionpolicies"`, then `rm -f /tmp/seq.jar`. **An unauthenticated GET answers 401 and not an empty list, so a 401 is never evidence of absence** and must not be read as one. ⛔ **NOT the metastore entity-prefix grep this row named until 2026-08-23** — the Measured cell records why that instrument is fail-open for this property | **A POLICY EXISTS: one, all events, 30 days.** Created by §3 step 7 on 2026-08-23 — `POST /api/retentionpolicies` **201**, id `retentionpolicy-36`, `RetentionTime 30.00:00:00`, `DataSource Stream`, `RemovedSignalExpression null` — and read back through `GET /api/retentionpolicies` in the same authenticated session. That is what [#1170](https://github.com/klasolsson81/jobbliggaren/issues/1170) asks for, and §3 step 8 attached the provider only afterwards. ⛔ **THE INSTRUMENT THIS ROW NAMED UNTIL 2026-08-23 WAS FAIL-OPEN, AND THAT IS A SEPARATE FINDING FROM THE ANSWER IT GAVE.** With the policy demonstrably present, `grep -ac "retentionpolicy-"` over `metastore.collection.*.docc` still returns **0** — and `setting-` returns **0** while the ingestion gate it would count is measurably in force (401 on an unkeyed write). Dropping the trailing hyphen returns **1** and **39**. The store does not write these entity ids in the `<prefix>-` text form the grep assumed, so the row's controls proved only that the file is text-searchable — never that a `retentionpolicy-` document would match **if one existed**, which is the property a control has to cross. **The zero read on 2026-08-23 was therefore true but not measured by that instrument.** What carried that conclusion was **one** corroboration, and calling it three repeated the same error one level down: the container's environment and `/data/Seq.json` read **identically whether or not a policy exists** — this cell says why three sentences on, since Seq exposes no configuration surface for retention — so neither of them discriminates, and neither is a control. What discriminated was the login returning `{"Error":"A password change is required."}`: step 4 was never completed, so step 7 could not have run. **datalust document no retention configuration at all — there is no `SEQ_RETENTION_*` family and no config surface — so this can never be repaired in compose, only by §3 step 7** | 2026-08-23 |
| **Seq's retention policy removes events, and the DISK follows later** | query for an event older than the window; separately, `du` on the volume | **Not measured, and measurable for the first time only now:** the policy was created 2026-08-23, so the earliest date an event can be old enough to test removal is **2026-09-22**. Until then this cell is scheduling and not a claim about the box. Retention makes events inaccessible; space returns via compaction, which runs at **7 days of file age** — bytes can persist past the 30-day mark, and the register says so | |
| **Seq's retention ENGINE runs against the policy — a different property from the row above, and deliberately its own** | the `seq` container's own log (`docker logs jobbliggaren-seq`), read for the scheduler's lines rather than for an effect | **The engine runs: measured 2026-09-03.** `Applying 1 retention policies` ×12, `Removing all data from Stream before <ts> under policy retentionpolicy-N` ×12, and `Reclaiming storage`. That rules out the failure mode the row above silently presupposes — *a policy exists but nothing ever evaluates it*. ⛔ **It says NOTHING about whether removal works, and must not be read as if it did.** No event on this box is yet 30 days old, so every one of those twelve passes removed **zero** rows, and a broken removal path would have logged identically. The property "an aged event is actually removed" is the row above's and is not measurable before 2026-09-22 | 2026-09-03 |
| **§3 step 8's precondition gate tells "no policy" apart from "not answered"** | run step 8's gate block verbatim against `datalust/seq:2026.1`, container to container, in every state it can meet | **Four states, four verdicts, image-side 2026-08-24, re-measured against the block now in this file.** *Policy present:* `200`, one id → `GATE OK` → **exit 0**. *No policy:* authenticated `GET` answers `200` with `[]` → `step 7 has not run` → **exit 1**. *Not signed in:* `401` → `the question was never answered` → **exit 2**. *Host unreachable:* curl writes `000` → `no HTTP response` → **exit 2**. Keeping "did not run" apart from "was never asked" is the property, and a stock 2026.1 ships **no** default policy. `POST` answers `201` with `DataSource Stream` and `RemovedSignalExpression null`; `DELETE /api/retentionpolicies/<id>` answers `200` and returns the list to `[]`, which is how the empty state was reached rather than assumed | 2026-08-24 (image) |
| **§3 step 8's `.env` write keeps both secrets off argv, replaces the file atomically, and fails loudly** | the step 8 block verbatim, against a `600 root:root` `.env` in a throwaway `ubuntu:24.04`: a stub `sudo` recording its own argv, the target's **inode** before and after, and `stat` | **Measured 2026-08-24, every arm, with the argv rig proven against the form it replaces.** *Argv:* the previous `sh -c '…' _ "$TOKEN" "$NEW_PW"` form put **both** secrets in the recorded argv (1 and 1) — the rig sees the defect; the block now in this file records **0** and **0**. *Atomic:* inode **changed** (rename), `mode=600 owner=root:root`, values byte-exact through `printf %s` including `+ / = $ % @` and a space, idempotent on a second run, no temp file left. *Not over-broad:* an unrelated `SEQ_MINIMUM_LEVEL` line **survives** — the three keys are named rather than matched by prefix. *Fails loudly:* empty `$TOKEN`, empty `$NEW_PW` and a missing target directory each give **exit 1** with the target untouched and no stray `.env` in the caller's cwd; an interrupted write leaves no temp file. The naive in-place redirect is the control: inode **unchanged**, i.e. reconcile's hourly read can catch it half-written | 2026-08-24 (image) |
| **The seq container has a healthcheck** | `docker inspect -f '{{.State.Health.Status}}'` | **Not shipped.** The compose file omits it deliberately rather than shipping an unverified probe that would paint a permanent "unhealthy"; whether this image carries a client to call Seq's health endpoint was not measured. This row closes that | |
| **`logship` runs, and its cost is bounded** | `systemd-analyze` on the unit; artefact size per run | **Half of this row is measured and the other half cannot be yet, and they are not the same claim.** *Runs:* both services were started by hand at the arming and both reached their condition and **skipped** — `Result=success`, `ExecMainStatus=0`, `ConditionResult=no`, journal `unmet condition check ConditionPathExists=…/host-secrets/Backup__RcloneConfigBase64`, `systemctl --failed` empty. *Cost:* **not measured and not measurable here** — a skipped run executes no script, ships no artefact and writes no cursor, so there is no size and no duration to bound. This cell is owed again at the first run with #197's credential present, which is also the run the third precondition in §2 governs | 2026-08-18 (runs only) |
| **The pair is armed AND on an alarm surface** | `systemctl is-enabled` + `is-active` on both timers; then that both names appear in `FLOOR_TIMERS` | **Two halves on two axes, and BOTH are now box measurements.** *Armed:* both `enabled`/`active` on the box 2026-08-18. *On the surface:* **live on the box since the pull later the same day.** `jobbliggaren-heartbeat.service` runs the script straight out of `/opt/jobbliggaren`, so the floor row reached the box only at that `git pull --ff-only` — which §2 owns and which is itself a deploy, and which is why this row was recorded as *owed* for the hours in between. `grep FLOOR_TIMERS /opt/jobbliggaren/deploy/systemd/jobbliggaren-heartbeat.sh` returned the five-name constant there. **And the floor is satisfied rather than merely enlarged** — `journalctl -u jobbliggaren-heartbeat` after a hand run on the box read **`heartbeat: all predicates hold`**, which is the measurement that separates "P3 now names them" from "P3 now fails on them". ⚠ **Do not substitute the unit's exit status or an empty `systemctl --failed` for that line.** The script declares `THIS SCRIPT ALWAYS EXITS 0`, so `Result=success` is guaranteed whatever the verdict — `jobbliggaren-heartbeat.test.sh`'s P3 case asserts exactly that — and a disabled timer never becomes a failed unit, so `systemctl --failed` is blind to P3 by construction. An earlier revision of this row cited both of them. ⚠ **And arming was safe for a reason that EXPIRES.** `jobbliggaren-logship-fresh.service` carries the same `ConditionPathExists` as the shipping unit and therefore skips; `jobbliggaren-backup-fresh.service` carries none — measured 2026-08-18, and that difference is the whole asymmetry with the pair `host-detection.md` §7 declines to arm. The shield lifts the moment #197's credential is injected: `--check` then dies on the absent stamp (`shipping has never succeeded`) and latches until the first successful ship, with `-fresh` firing at `:00` **before** the shipping timer's `*:17`. `master-key-ops.md` §3's `systemctl start jobbliggaren-logship.service` is what bounds that window and is MANDATORY at injection, not tidiness. **Arming without the floor row would have been the weaker half of a pair, not a smaller version of the whole:** the shipping service SKIPS without the upload credential, and a skip is inactive rather than failed, so an armed-but-unnamed timer that later stopped would still have been on no surface. `check_floor_timers` measures `is-enabled` AND `is-active`, which is why the repo edit follows the `enable` and never the install | 2026-08-18 |
| **The prune resolves the real container layout, and removes nothing it may not** | pipe `jobbliggaren-logprune.sh --dry-run` to the box over stdin (`ssh … 'sudo bash -s -- --dry-run' < …`) so no file is created and nothing is deleted; read the per-container lines. ⚠ **The install re-run took a different path** — §6 step 2 runs the script out of the clone, which by then carries it — so the two readings below are one script reached two ways, not one instrument run twice | **Measured 2026-08-28 21:25 UTC against the delivered nine-container set, and re-measured at the install 2026-09-03.** Eight of nine names resolved; `jobbliggaren-migrate-rewrap` answered **no such container** — measured absent, because it completes and is removed — and `jobbliggaren-migrate` resolved while **exited**, which is the state a running-only set never exercises. Both runs reported `pruned=0 kept=2 unreadable=0`, and both times the two kept segments were `postgres`'s, which is why it prints no line of its own: a container whose segments are all inside the window produces no per-container output. ⚠ **They are not the same two files.** The row below dates the segments present at the install to 2026-09-03 04:05 and 04:07, so the pair counted on 2026-08-28 has since been replaced — the count matched twice, the contents did not persist, and reading `kept=2` as one continuous fact is the mistake this sentence exists to prevent. ⚠ **An earlier revision of this cell reported `kept=0` against a four-container set and concluded the run could not prove the glob.** Against nine it does, and in both directions: `postgres` held **three** files and the glob matched exactly **two**, so it selected the rotated segments and **excluded the live one on a real host** — not only in a fixture. What the run still does not prove is a prune, because nothing on this box is yet old enough; that is the row below | 2026-08-28 · 2026-09-03 (install) |
| **The prune REMOVES an aged rotated segment, measured as an effect** | let a segment rotate, then list the directory after it passes the window | **Not measured. This cell is scheduling, not a claim** — it comes due at the first rotation plus the window, and a green unit run is not a substitute for it: the non-dry run of 2026-09-03 reported `pruned=0`, which measures that nothing was yet eligible and says nothing about whether removal works. The only rotated segments on the box are `postgres`'s two, written 2026-09-03 04:05 and 04:07, so on present contents the earliest date this becomes measurable is **2026-10-03**. ⚠ **That date is not a schedule, because a container replacement resets it:** rotated segments leave with the container they belong to, and `worker` demonstrated it inside one day — two full 10 MB segments at 04:05, none by 07:42, its replacement deployed at 04:49 | |
| **The LIVE segment survives a real (non-dry) run** | §6 step 6 (`sudo test -f …/<id>-json.log` per container), and in the same visit `stat -c %i` on every live segment **before and after** the run, plus `docker logs --timestamps --since` per container read for its exit status. ⚠ **The last two are what the verdict below rests on, and §6 step 6 alone does not produce them** — it stats for presence and nothing more | **Measured at the install 2026-09-03.** All eight existing containers reported `live=present` after the first `dry_run=0` run; every live segment's inode was **identical before and after** — `api` 2991163, `worker` 2991165, `web` 2991176, `caddy` 2991130, `postgres` 520207, `redis` 2991191, `seq` 2991142, `migrate` 2991150 — three of them (`worker`, `postgres`, `seq`) grew across the run, and `docker logs --timestamps --since` exited **0** for all eight, which is the input `jobbliggaren-logship.sh` dies on. ⚠ **That run reported `pruned=0`, so this reading does not discriminate.** An unchanged inode is also exactly what a run with nothing to delete produces, so the evidence is consistent with the design sparing the live segment AND with the run simply having had no work — it does not separate them. What separates them is the row above: `postgres` held three files and the glob matched exactly two, excluding the live one on a real host. **This cell comes due again at the first run that actually prunes**, which the row below dates. `jobbliggaren-logprune.test.sh` asserts the absence in every fixture case that prunes, but a fixture is not this box | 2026-09-03 |
| **The prune timer is on P3's floor** | `grep FLOOR_TIMERS /opt/jobbliggaren/deploy/systemd/jobbliggaren-heartbeat.sh` on the box, then `journalctl -u jobbliggaren-heartbeat` after a hand run, read for **`heartbeat: all predicates hold`** | **Half measured, and the two halves sit on different axes.** *Repo:* the sixth name landed 2026-09-03, after the arming and never before it — `check_floor_timers` measures `is-enabled` **and** `is-active`, so the reverse order fails the heartbeat on the next pull. *Box:* **owed until the box pulls that commit**, exactly as the logship pair's row above was owed for the hours between merge and pull on 2026-08-18. `jobbliggaren-heartbeat.service` runs the script straight out of the clone, so the constant reaches the box only at a `git pull --ff-only`, which is itself a deploy. **That window is the safe direction by construction:** it runs the OLD five-name constant, which cannot page; the reverse order would have paged once and then held the surface red. ⚠ **Neither the unit's exit status nor an empty `systemctl --failed` may stand in for that journal line** — the script declares it always exits 0, and a disabled timer never becomes a failed unit, so both are blind to P3. Until the box pulls, P2 covers the timer — its set comes from a `jobbliggaren-*.timer` wildcard, so an enabled timer that stops is caught — while P3 does not, so a `disable` is invisible | 2026-09-03 (repo) |
| **The prune unit's sandbox does not break the thing it sandboxes** | the four properties run as a transient unit: `systemd-run --wait --collect --pipe --property=ProtectSystem=strict --property="ReadWritePaths=/var/lib/docker/containers /run/docker.sock" --property=NoNewPrivileges=yes --property=ProtectHome=yes /bin/sh -c '…'`, with an UNSANDBOXED control in the same session | **Measured 2026-08-28: the daemon answered (server version returned) and `/var/lib/docker/containers` was readable under the sandbox; the control answered identically.** The question worth measuring was whether a read-only `/run` still permits a unix-socket `connect()` — `ProtectSystem=strict` covers `/run`, and the docker CLI reaches the daemon through it (`/var/run` is a symlink to `/run` here). It does, with the socket named in `ReadWritePaths`. A transient unit leaves nothing behind, so this is a reading rather than an install | 2026-08-28 |
| **A write rate exists on this box** | per-container `stat -c %s` on the live segment and a count of rotated segments, taken **inside a root shell** — `/var/lib/docker/containers/<id>/` is mode 700, so a glob expanded by an unprivileged shell silently yields zero and reads as "never rotated"; then a second `stat` after a fixed interval for the marginal rate | **A rate exists, and it is a BURST rather than a load — both halves measured 2026-09-03.** `jobbliggaren-worker` and `jobbliggaren-postgres` each held **two full 10 MB rotated segments**, all four written inside a ~2-minute window (04:05–04:07 box time); the other five app containers held **none**. Sampled over 60 s immediately afterwards the same two grew **+314 B** and **+522 B** — so a mean over uptime describes neither regime and must not be quoted as "the" rate. **Its source is the ingest job, not user traffic:** `postgres` logs `ERROR: duplicate key value violates unique constraint` plus the full `STATEMENT` for each duplicate an upsert absorbs, with the matching stack trace worker-side — ADR 0032 §5's deliberate `DbUpdateException` isolation in `SyncPlatsbankenStreamJob`, not a fault in it. `job_seekers` = **2** on the same date. ⚠ **This is NOT a dimensioning figure for `max-size`** — it is one scheduled job's burst on a box with no user base, which is the case §5's old premise excluded by construction (ADR 0024 Amendment 2026-09-03). Regenerate rather than quote: the numbers move with every sync run | 2026-09-03 |
| **The `json-file` residual reaches a real age on a real container** | oldest line in the **live** segment per container (`head -n1`, JSON `time` field) against the 30-day window; rotated-segment count in the same root shell | **`jobbliggaren-redis` carries a live segment whose oldest line is 2026-08-20 — 330 KB, never rotated — so on present behaviour it reaches 30 days on 2026-09-19.** `seq` is second at 2026-08-23. Both sit below `max-size`, so neither will rotate its way under a bound; `api`, `web` and `caddy` read 2026-09-02 only because the deploy replaced those containers. ⚠ **That a restart resets the clock is the defect, not an exception to it** (ADR 0024 Amendment 2026-08-28 §3): the age of retained data equals the container's own lifetime, and `redis` is simply the first container to demonstrate it with a real number. ⚠ **This cell records the AGE and nothing else.** Whether these particular segments carry personal data is an Art. 5(1)(e) question, it is `security-auditor`'s, and it is not settled here | 2026-09-03 |

---

## 5. What this runbook does not own

- **The `json-file` layer's `max-size`** — [#1170](https://github.com/klasolsson81/jobbliggaren/issues/1170).
  (B) demotes `json-file` from *the* store to a last-resort buffer, which is what finally makes a
  smaller `max-size` defensible — but the number needs a write rate (§4). **Do not use the
  archive's ~220 bytes/event as a proxy:** that is Seq's storage format, not console text.

  ⚠ **This bullet said "the layer's age bound" until 2026-08-28, and the split is the point rather
  than a rewording.** The write-rate premise binds a VOLUME number and only a volume number: a
  `max-size` has to be divided by a rate before it means an age, so it cannot be chosen without
  one. An AGE bound reads no rate at all — it compares a line's timestamp to a cutoff — so the
  premise never applied to it, and the age bound is **owned by §6 as of 2026-08-28** rather than
  deferred here. The `max-size` half is unchanged.

  **The residual that replaces it, named and bounded:** §6 prunes rotated segments only, so **one
  non-rotated live segment per app container reaches no age bound**, capped only by `max-size`.
  Binding that needs either a log-driver change (an ADR 0128 Streams-table decision) or truncation
  of a file the daemon holds open, which is unsupported and would fell logship's app leg. **That
  residual is what keeps #1170 open**, and it is not the same thing this bullet used to say.
- **Application-level alarms** (5xx rate, DB CPU) — [#1172](https://github.com/klasolsson81/jobbliggaren/issues/1172).
- **Row 27d's `Deny` policy**, the OVH Art. 28 agreement, and the retention numbers for journald
  and auditd. All Klas's, all open, all named in ADR 0128 §5.
- **Host detection and paging** — [`host-detection.md`](host-detection.md). M-7 sends *that*
  something happened; this file is about the corpus.
- **Whether `deploy/.env` is an acceptable home for the Seq credential.** ADR 0128 records that
  the bind expands one of the two plaintext-on-disk surfaces #198 was opened against, and that
  `security-auditor` owns the severity.

---

## 6. Install — (C), the json-file age bound (#1170)

**This section is owed because §5's first bullet no longer defers it.** (A) and (B) are age-bounded;
Docker's `json-file` layer is the third holder of the same events and its driver has **no time axis
at all** — the whole option set (`max-size`, `max-file`, `labels`, `labels-regex`, `env`,
`env-regex`, `compress`; docs.docker.com, read 2026-08-28) is volume-shaped, and removal happens by
file COUNT. Effective retention is therefore budget over write rate, i.e. **inversely proportional
to traffic**: the quieter a container, the longer it retains.

⛔ **READ THE BOUND BEFORE INSTALLING, BECAUSE THE UNIT NAME PROMISES MORE THAN THE UNIT DELIVERS.**
`jobbliggaren-logprune` removes **rotated** segments (`*-json.log.N`) whose newest line has aged
out. It never touches the **live** segment. The commitment it can honestly carry is:

> no app-log data older than 30 days, **except at most one non-rotated live segment per app
> container**, itself capped by `max-size` (10 MB).

**That residual is not academic — it is where the PROJECTED breach lives.** The word is exact: no container on this box has yet lived 30 days, so nothing has *exceeded* the window — what was measured is the absence of a bound. On 2026-08-28 `web` had
written five lines, all at container start, and `caddy` twenty-two within ten seconds of start;
neither had ever rotated, so neither owned a single segment this unit is permitted to touch. **#1170
stays open on the residual**, not on the mechanism.

**Why the live segment is left alone** (CTO 2026-08-28, on a coupling rather than on taste): Docker
owns that file's rotation, `jobbliggaren-logship.sh` reads the same stream through
`docker logs --timestamps --since`, and it **dies** when `docker logs` exits non-zero — withholding
its stamp and latching `logship-fresh`. A retention mechanism able to fell the off-box archive's
freshness signal is the cross-coupling ADR 0128 split #1175 to avoid.

⚠ **AND IT DELIBERATELY CARRIES NO `Condition*`, UNLIKE TWO OF ITS SIBLINGS** (CTO 2026-08-28). Theirs gate on a credential the script needs in order to run at all; this one needs none.

**The window is 30 days and is parity, not a new number** — ADR 0024 D7 policy 1 (tied to the
Art. 17 restore window, D5/D6) and Seq's `retentionpolicy-36`. A separate number here would give one
personal datum three retention numbers across three layers.

### Steps

```bash
# 1. Bring the clone forward. NOT a blind pull — on this box a pull is a DEPLOY that
#    jobbliggaren-reconcile.timer applies within the hour. Read what it brings first.
cd /opt/jobbliggaren && sudo git fetch origin && sudo git log --oneline HEAD..origin/main
sudo git pull --ff-only

# 2. DRY RUN FIRST, ALWAYS. It reports what it would remove and removes nothing.
#    On a box whose app containers have never rotated this correctly prints pruned=0.
sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-logprune.sh --dry-run
# expect: a "logprune: window=30d cutoff=… dry_run=1" line, then "logprune: pruned=N kept=N
#         unreadable=N". Any "WOULD PRUNE" line names a file.
#         ⚠ NOT one line per container: a container whose rotated segments are ALL inside the
#         window prints nothing of its own and is visible only in the `kept` total, and a
#         container that does not exist prints "no such container". Measured 2026-08-28: eight
#         of nine names resolved, `migrate-rewrap` absent, and `postgres` silent behind kept=2.

# 3. Install the unit pair.
sudo cp /opt/jobbliggaren/deploy/systemd/jobbliggaren-logprune.{service,timer} /etc/systemd/system/
sudo systemctl daemon-reload

# 4. Arm it.
sudo systemctl enable --now jobbliggaren-logprune.timer
systemctl is-enabled jobbliggaren-logprune.timer && systemctl is-active jobbliggaren-logprune.timer
# expect: enabled   then   active

# 5. Prove it RUNS, not merely that it is scheduled.
sudo systemctl start jobbliggaren-logprune.service
journalctl -u jobbliggaren-logprune -n 20 --no-pager
# expect: the same shape as step 2 with dry_run=0, and `systemctl --failed` still empty.

# 6. Prove the LIVE segments survived the run — the property the whole design is built around.
for c in jobbliggaren-api jobbliggaren-worker jobbliggaren-web jobbliggaren-caddy jobbliggaren-postgres jobbliggaren-redis jobbliggaren-seq jobbliggaren-migrate jobbliggaren-migrate-rewrap; do
  id=$(sudo docker inspect -f '{{.Id}}' "$c" 2>/dev/null) || continue
  printf '%-24s live=%s\n' "$c" \
    "$(sudo test -f /var/lib/docker/containers/$id/$id-json.log && echo present || echo MISSING)"
done
# expect: present for every container that exists. A MISSING here is a stop-everything result:
#         docker logs is the
#         input jobbliggaren-logship.sh dies on.
```

### Step 7 — and it is deliberately LAST, after the arming

⚠ **Add `jobbliggaren-logprune.timer` to `FLOOR_TIMERS` in
`deploy/systemd/jobbliggaren-heartbeat.sh` only once steps 4–5 have succeeded on the box.** That is
the same ordering the logship pair followed: it joined the floor on 2026-08-18, *the day it was
enabled*, and not on its install. `check_floor_timers` measures `is-enabled` **and** `is-active`, so
a name landing in the repo before the timer is armed makes the heartbeat fail on the next pull — an
alarm reporting the arming sequence rather than the box.
