# Master-key operations — injection, rotation, recovery

**Scope:** the field-encryption master key and the three pseudonymisation peppers on the
production box. Owned by [#198](https://github.com/klasolsson81/jobbliggaren/issues/198)
(ADR 0050 gates B-1, M-3; ADR 0049 `Amendment 2026-08-09`).
**Host:** Netcup RS 1000 G12, Debian 13 (trixie), Nuremberg.
**Related:** [`vps-deploy-stack.md`](vps-deploy-stack.md) (the stack itself) ·
[`vps-base-hardening.md`](vps-base-hardening.md) §7–§8 (the memory hygiene this depends on).

---

## 1. The model, in one paragraph

The four crypto values live **only in RAM**: as files on `/run/jobbliggaren/secrets`
(tmpfs) and, from there, in the api and worker process memory. There is **no encrypted copy
on disk anywhere on this box**, and that is the decision rather than an omission. Every
reboot destroys them and an operator must re-inject.

> **ESCROW IS A HARD PREREQUISITE TO CUTOVER. ~~UNDECIDED AS OF 2026-08-09~~ — DECIDED BY KLAS
> 2026-08-12, in a form this runbook did not anticipate.** With no
> at-rest copy, an off-box escrow is the *only* recovery path: an operator who loses these
> values destroys every encrypted field and every pseudonymised lookup irreversibly. The
> senior-cto-advisor escalated the decision to Klas and bound it as a hard prerequisite — it is
> a risk acceptance, which CLAUDE.md §9.6 makes Klas's to grant and never a session's to claim.
>
> **The form: two plaintext copies on Klas's own devices. No password manager, and there will
> not be one for this.** Recorded in `vps-deploy-stack.md` row 26 with its ground and its
> expiry, and the concession — including that the age private key may share the device holding
> `jobbpilot_vps_ed25519`, over `security-auditor`'s objection — in **ADR 0129** (gitignored per
> §6.5, like every ADR from 0071; if it is absent from your checkout the decision and its ground
> are summarised in `vps-deploy-stack.md` rows 26 and 32, which are tracked). An earlier
> draft of this runbook stated the escrow as delivered fact when it was not; this one states it
> as decided, which it now is, and points at where the reasoning lives rather than restating it.
>
> **What is still owed before cutover is not the decision but the act:** the four crypto values
> are held (row 26, dated), **the age private key is not** — row 32 is open, the identity was
> not found in four roots on 2026-08-07, and generating a fresh one is free until the first
> backup lands. **Do not cut over on an empty row 32.**
>
> **One measured input for that decision, because it is new:** `OLD_KEY` in step 3 lives on
> tmpfs like everything else here. A reboot between step 4 and step 9 therefore destroys BOTH
> the retiring key and the live one — the live one is escrowed at step 4, the retiring one is
> not. If rows are still wrapped under the retiring key at that moment (a rollback at step 5, or
> a partial rotation), the loss is total. So an escrow that merely *replaces* on rotation
> reproduces the failure this runbook just repaired, one layer outside the box: it has to cover
> the rotation window — both generations, bytes and identity.

**Why not a sealed blob on disk.** Gate B-1's own text names two mechanisms — a TPM-bound
`systemd-creds` credential, or sops+age into tmpfs — and measurement on 2026-08-09 exhausted
both: `systemd-analyze has-tpm2` reports `partial` with no `/dev/tpm0` and no libtss2, and
`sops` is absent from apt on trixie. Without a TPM, a `systemd-creds` blob and
`/var/lib/systemd/credential.secret` travel together in any disk snapshot, so that branch is
obfuscation rather than encryption; an on-disk age key is the same thing with one more
supply-chain dependency. The gate's *requirement* is "never plaintext on disk", and keeping
the key only in RAM satisfies it more strongly than either named option would have.

**The box was already built for this.** `vps-base-hardening.md` §8 removed disk swap,
configured zram with no writeback device and discarded core dumps; §7 already recorded
"every reboot also destroys the RAM-held key and requires re-injection" as the operating
model, with `Automatic-Reboot "false"`.

**The cost, stated plainly.** After an **unplanned** reboot, api and worker crash-loop until
someone injects. `jobbliggaren-secrets-present.timer` puts that on `systemctl --failed`
within two minutes — the box's only alarm surface, since no log sink exists (#1175).

**That last sentence holds only once the timer is ENABLED — and since #1329 nothing outside this
runbook gates that.** The timer used to share one predicate with #197's host-only backup
credential, so its absence failed the whole check and the crypto alarm could not be armed until an
unrelated ops half landed. `--check` now answers for the crypto set alone, so §2 enables the timer
in the same visit as the install. Install **without** enabling and there is no path at all from a
fail-closed outage to a human: a crash-looping container never reaches `systemctl --failed` on its
own, and `jobbliggaren-heartbeat.sh`'s P1 — the box's only *outbound* channel — is precisely "that
list is clean". The cost above is then the crash-loop **without** the alarm.

---

## 2. Files and units

| Path | What |
|---|---|
| `/run/jobbliggaren/secrets/` | tmpfs staging dir. **`0700 root:root` at boot** (`/etc/tmpfiles.d/jobbliggaren.conf`), **raised to `0710 root:<container-gid>` by the injection script**. Two actors, two states — and with the `:` prefix tmpfiles can never produce the second one, so a `WRONG MODE` from `--check` **naming this directory** means the injection has not run (since #1320 the same run can also emit one naming `deploy/.env`). **A third actor reads it since #1295, and only reads:** `jobbliggaren-reconcile.service` re-asserts the ownership against the image it is about to apply, and refuses the apply rather than repairing anything |
| `…/FieldEncryption__LocalMasterKeyBase64` | the master key, `0400` |
| `…/FieldEncryption__LocalMasterKeyId` | key identity, not a secret — the rotation marker |
| `…/AuditPseudonymization__PepperBase64` | pepper |
| `…/CompanyWatchPseudonymization__PepperBase64` | pepper |
| `…/CvReviewFingerprintPseudonymization__PepperBase64` | pepper |
| `…/Email__Scaleway__SecretKey` | **the transactional-mail key, and the only secret here that DIES ON A DATE.** Scaleway caps an API key at one year and has no instance-role equivalent. **Current key: expires `2027-08-16`** (issued 2026-08-16, access key `…P9DRX`, bearer = the IAM *application* `Jobbliggaren`; read in the console 2026-08-16, #183 E4). Rotation is **#198**'s. `EMAIL_SCALEWAY_KEY_EXPIRES_AT` in `deploy/.env` carries the same date and `--check` reads it: **expired** exits non-zero onto the fault surface, while the advance notice is a journal line at exit 0 — the split, and why a lead time may not latch that surface, is argued once in that script's `EXPIRY_NOTICE_DAYS` header. **So this row is a record and NOT a reminder with a reader:** nothing pages anyone before the key dies. That half is [#1267](https://github.com/klasolsson81/jobbliggaren/issues/1267)'s calendar-obligation class — this row satisfies its AC 1 (the date is registered); its AC 2, the reminder itself, is not built |
| `…/Email__Scaleway__ProjectId` | project selector, **not a secret and NOT on a rotation clock** — it changes only if the project does. Delivered through the same seam as the key above and therefore easy to sweep into one "rotate the Scaleway credentials" step; they are two lifecycles and the injection script's `SCALEWAY_SECRET_KEYS` comment is that distinction's home |
| `/run/app-secrets` | the same directory as api and worker see it (read-only bind mount) |
| `/run/jobbliggaren/host-secrets/Backup__RcloneConfigBase64` | **#197, and mounted into no container.** `0400 root:root` in a `0700 root:root` directory. Injected by the same script, in the same run — but demanded by `--check-host` and by no other predicate, so its absence gates its **own** timer and nothing else (#1329) |
| `jobbliggaren-inject-secrets.sh` | injection (interactive), `--check` (crypto absence + at-rest posture) and `--check-host` (host-only absence) — **two detectors, one per set, one per owner** |
| `jobbliggaren-secrets-present.{service,timer}` | runs `--check` at boot + every 10 min. Enabled in the same visit as the install below; it owes #197 nothing |
| `jobbliggaren-host-secrets-present.{service,timer}` | runs `--check-host` at boot + **hourly** — the resolution that matches "tonight's backup will not upload", not "the site is down". Installed with the pair above; **enabled only once the rclone config exists** |

**File names are .NET configuration keys** with `__` as the section delimiter. That is the
contract, not an implementation detail: it is what `AddKeyPerFile` expects, so the reader can
be swapped for the first-party package later without touching this box.

### Install (once)

```bash
sudo install -m 0644 /opt/jobbliggaren/deploy/systemd/jobbliggaren-tmpfiles.conf \
  /etc/tmpfiles.d/jobbliggaren.conf
sudo systemd-tmpfiles --create /etc/tmpfiles.d/jobbliggaren.conf
sudo install -m 0644 \
  /opt/jobbliggaren/deploy/systemd/jobbliggaren-secrets-present.service \
  /opt/jobbliggaren/deploy/systemd/jobbliggaren-secrets-present.timer \
  /opt/jobbliggaren/deploy/systemd/jobbliggaren-host-secrets-present.service \
  /opt/jobbliggaren/deploy/systemd/jobbliggaren-host-secrets-present.timer \
  /etc/systemd/system/
sudo systemctl daemon-reload
```

**Both pairs are installed here; the two are ENABLED at different moments, and which one waits is
the whole of #1329.** Each timer is armed by the set its own detector reads, and neither can be
enabled before that set exists: a timer enabled against an absent set fails on **every** fire.
`--check` demands the crypto secrets, their directory's mode **and owner**, `deploy/.env`'s **owner and mode** (both since #1320/#1319), and `deploy/.env`'s mail configuration
(an invalid `EMAIL_PROVIDER` — `Ses` and `Resend` among them, since both arms are gone and reach the
same `else throw`; `Resend`'s went in `3ee3d85c` (#1237) and `Ses`'s in `b71c14de` (#183 E1) — and,
under `Scaleway`, its two secrets **plus the region**, each named separately by the script). `--check-host` demands #197's
`Backup__RcloneConfigBase64`, and nothing else — which is why an absent backup credential no longer
holds the crypto alarm down.

**And a permanently failed unit is not merely an unread list.** Where `jobbliggaren-heartbeat.timer`
is enabled and reaching its expecter, P1 in `jobbliggaren-heartbeat.sh` is `systemctl --failed`
being *clean*, so a permanently failed unit holds that surface red and makes M-7's P1 vacuous for
as long as it stands — one page at the transition, then silence, never a page per run
(`jobbliggaren-heartbeat.sh` carries the grounding). The other failed units are still *listed* — `check_failed_units`
posts every name — but a predicate that is false continuously carries no information, which is the
same argument `jobbliggaren-heartbeat.sh` makes for its own exit contract. It records the same
sequencing for the same reason.

```bash
# After §3 has injected the CRYPTO secrets — which it always does, and first:
sudo systemctl enable --now jobbliggaren-secrets-present.timer

# After §3 has injected Backup__RcloneConfigBase64 — which an operator who does not hold the
# rclone config yet cannot do. Enable this one the day that file exists, and not before:
sudo systemctl enable --now jobbliggaren-host-secrets-present.timer
```

> **Only the second line can be deferred, and that is the point of the split.** §3 writes the
> crypto secrets before it prompts for anything host-only, so by the time you reach this block the
> crypto set is either complete or the run aborted — there is no state in which you have injected
> and the first line should wait. The unit files of a deferred pair are installed and inert (the
> service carries no `[Install]` section — only the timer does).
>
> **What deferring the HOST timer costs, in full, because a cost that is understated is not a
> decision:** an absent backup credential is on no surface at all. `jobbliggaren-backup.service`
> and `jobbliggaren-logship.service` both carry `ConditionPathExists` on that file and **skip**
> rather than fail — inactive, logged, not failed — deliberately, so they never latch. The skip is
> silent, so a box can go indefinitely without a working backup and nothing says so. That is what
> this timer exists to report, and it reports nothing until it is enabled.
>
> **What deferring it no longer costs:** the crypto alarm, `--check`'s other detections (wrong
> directory mode, invalid `EMAIL_PROVIDER`, SES boot refusal), and §7's unplanned-reboot series.
> All three ride the first line, which owes #197 nothing. Before #1329 they were held hostage to a
> credential whose absence means only that tonight's upload will not happen.
>
> **You are reminded by §3, not by a table.** The host enable step is repeated at the end of §3,
> where an operator stands when the credential finally is injected. `host-detection.md` §7's
> floor-set row is what must be **re-measured** once either timer is enabled — it is a dated
> measurement, not an instruction, and nothing in it will prompt you.

There is deliberately **no unit that starts the stack** and none that unseals anything — with
no at-rest copy there is nothing to unseal, so the `Before=docker.service` ordering problem
does not exist here.

---

## 3. After every reboot — inject

⛔ **BEFORE YOU RUN THAT COMMAND — since 2026-08-18 the injection IS the firing of the log archive,
and there is no second gate after it.** Shipping used to take TWO acts: inject the credential **and**
arm the archive timers, the second performed by someone reading `log-sink.md` §2's precondition. Both
timers are armed now, so it takes ONE. The injection creates `Backup__RcloneConfigBase64`, both
services' `ConditionPathExists` starts passing, and `jobbliggaren-logship.timer` (`OnCalendar=*:17`,
`Persistent=true`) fires within the hour with no cursor — the whole-journal run.
**Re-measure the journal here.** The discharge recorded later in this section is dated and is never
inherited.

If it cannot be re-measured in this visit, disarm **both** timers before injecting:

```bash
sudo systemctl disable --now jobbliggaren-logship.timer jobbliggaren-logship-fresh.timer
# … measure the journal, then — ONLY IF IT MEASURES CLEAN — re-arm BOTH. A vacuum produces that
# state; a further rotation does not. The re-arm is a duty, not a courtesy:
sudo systemctl enable --now jobbliggaren-logship.timer jobbliggaren-logship-fresh.timer
```

⚠ **The re-arm is owed precisely BECAUSE the disarm is invisible.** It takes down the archive and
`-fresh`, its only staleness probe, in one command — and until the box pulls the `FLOOR_TIMERS`
edit, no `floor-timer-down=` fires to remind anyone the archive is off. ADR 0126's "What this
decision does NOT do" names journal erasure among what local detection cannot see; the off-box
archive is that mitigation.

⚠ **Disarming the shipping timer alone is the trap, and it fails in the more dangerous direction.**
`-fresh` carries the same `ConditionPathExists`, so its shield lifts at the same injection; `--check`
then dies on the absent stamp (`shipping has never succeeded`) and lands in `systemctl --failed`,
i.e. M-7's **P1**, which the heartbeat puts into `/fail` every 15 minutes — the heartbeat's cadence,
not the probe's, since `-fresh` itself runs hourly. ⚠ A POST cadence, not a page cadence:
`systemctl --failed` **latches**, so the expecter notifies on the transition and a second genuine
fault inside the window changes only a body nobody reads. It stays latched because the hand-start that would clear it is not
available to you: mechanically it runs fine with the timer disabled, but it would ship the very
journal you disarmed for.

⚠ **The floor cost of a disarm is repo-side until the box pulls.** `FLOOR_TIMERS` names both timers
in the repo since 2026-08-18, but `/opt/jobbliggaren`'s copy — which is what
`jobbliggaren-heartbeat.service` actually runs — carries them only after the next `git pull --ff-only`.
Before that pull a disarm lights no `floor-timer-down=` at all.

```bash
sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh
```

What it does, in order:

1. **Refuses a non-terminal stdin.** Piping a value in would put it in argv and in shell
   history; `/proc` is world-readable, so a secret on a command line is published to every
   local process.
2. **Measures the container runtime uid AND gid out of the api image** rather than hardcoding
   or deriving them. Group traversal is the container's only way into a `0710` directory, so
   an image where gid differs from uid would make the mount unreadable — and the app would
   then report a *missing key* rather than a permission problem.
3. **Asks for the key identity first**, defaulting to `local-v1`, and writes it before the
   key bytes. Identity and bytes are one unit: a v2 key stamped `local-v1` makes the next
   rotation's compare-and-swap skip exactly the rows it must not skip.
4. Prompts for each **crypto** secret with `read -rs`, validates that the master key decodes to
   32 bytes, and creates each file `0400` owned by the measured uid. The Scaleway credentials are
   prompted for only under the condition the script states (provider `Scaleway`, a `_FILE`
   pointer, or `JBL_INJECT_SCALEWAY=1`), and they are **two** secrets with separate rotation
   lifecycles — `Email__Scaleway__SecretKey` and `Email__Scaleway__ProjectId` — not two halves of
   one credential (#183).
5. **Prompts for `Backup__RcloneConfigBase64` last, unconditionally, and to a different
   destination** — `/run/jobbliggaren/host-secrets/`, `0400 root:root`, mounted into no
   container (#197). It is validated for base64 *decodability*, not for a byte length: it is the
   base64 **of** an rclone config file, produced with `base64 -w0 < rclone.conf`.

> **IF YOU DO NOT HAVE THE RCLONE CONFIG YET, READ THIS BEFORE RUNNING §3.** That prompt is not
> gated on anything, and an empty answer aborts the run: `REFUSING: Backup__RcloneConfigBase64
> was empty or whitespace-only`, exit 1, and the closing `Injected.` line is never printed. **The
> crypto secrets are already written at that point** — they are written first — so this is the
> expected shape of a deferred injection and not a failed one. Verify it that way instead:
>
> ```bash
> sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh --check
> # expect exit 0 and `all secrets present in /run/jobbliggaren/secrets` — NO MISSING line at all
> sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh --check-host
> # expect exit 1 and EXACTLY ONE MISSING line, naming
> #   /run/jobbliggaren/host-secrets/Backup__RcloneConfigBase64
> docker inspect -f '{{.State.Health.Status}}' jobbliggaren-api    # expect: healthy
> ```
>
> **THIS RECIPE INVERTED AT #1329, AND AN OPERATOR WORKING FROM MEMORY WILL READ IT BACKWARDS.**
> Until then one `--check` answered for both sets, so this state produced exit **1** with one
> `MISSING:` line — and that non-zero exit was the thing you confirmed. It is now exit **0** with
> no `MISSING:` line at all, because `--check` reads nothing that is absent here. A green `--check`
> in this state is the correct result and not a detector that has stopped working: the line naming
> the file moved to `--check-host`, which is where the deferral now lives.
>
> **Run both, and read the `MISSING:` line rather than only an exit code.** `--check-host` alone
> cannot tell a deferred injection from a failed one — it exits 1 in both, and only `--check` says
> whether the crypto half landed. Exit 0 from `--check`, one `MISSING:` line from `--check-host`
> naming the host-only path, and a healthy api: that is the whole verification.
>
> **DO enable `jobbliggaren-secrets-present.timer` in this state.** That is precisely what the
> split bought, and §2's first enable line has no other condition. Do **not** enable
> `jobbliggaren-host-secrets-present.timer` — its `--check-host` is the exit 1 above, so it would
> fail on every fire.

> **NEVER ACCEPT THE IDENTITY DEFAULT BLINDLY ON A ROTATED BOX.** After a rotation the box
> holds no record of which identity is in force — tmpfs was cleared by the reboot and `.env`
> no longer carries it. Pressing Enter would stamp `local-v1` onto v2 bytes, and the next
> rotation's `cmk_key_id == 'local-v2'` predicate would then skip precisely those rows, which
> become unrecoverable when the v2 key is discarded. **The answer is recoverable, so look it
> up rather than guessing:**
>
> ```bash
> docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -tAc 'SELECT DISTINCT cmk_key_id FROM user_data_keys'
> ```
>
> Otherwise pass the value it prints: `sudo JBL_MASTER_KEY_ID=<value> …inject-secrets.sh`. The
> identity is also part of what escrow must hold — **the bytes AND the identity**, not just the
> bytes.
>
> ⚠ **AN EMPTY RESULT DOES NOT MEAN THE DEFAULT IS CORRECT, AND SINCE 2026-08-15 THIS BOX IS THE
> COUNTEREXAMPLE.** An earlier version of this line said it did. `user_data_keys` is empty
> whenever no DEK has been minted yet — which is true both before the first rotation *and* after
> a rotation performed while the table was empty. **This box has been rotated**, so the identity
> in force is **`local-v2`** while the query returns nothing. (Stated by name and not as "the
> second state": the table below has its own numbering, and its second row is the branch that
> destroys data.) Accepting the default would stamp
> `local-v1` onto v2 bytes — the path `jobbliggaren-inject-secrets.sh` calls, in the comment above
> its identity block, *"a data-loss path, not an inconvenience"*: the next rotation's `cmk_key_id`
> predicate would skip exactly the rows it must not.
>
> So read the query as answering one question only: *which identity do existing DEK rows name?*
> **Empty means it cannot answer**, not that the default is safe. Then:
>
> | State | Identity to pass |
> |---|---|
> | Query returns a value | that value |
> | Empty, and no rotation has ever been performed | the default (`local-v1`) |
> | **Empty, and a rotation has been performed** | **read it from escrow — never the default** |
>
> The third row is why row 26 requires escrow to hold the identity alongside the bytes: after a
> reboot the box keeps no record of which generation is in force, and the database cannot supply
> it while the table is empty. Escrow is the only source.

Then verify, and do not skip this — the whole point of the model is that a partial injection
looks like a healthy box from the outside:

```bash
sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh --check       # expect exit 0
sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh --check-host  # expect exit 0
docker inspect -f '{{.State.Health.Status}}' jobbliggaren-api    # expect: healthy
```

**Both, after a complete injection.** `--check` alone is a green light over an absent backup
credential — that is the whole shape of the deferred state above, and after a run that reached
`Injected.` it is no longer the state you are in. Each detector answers for its own set and neither
can speak for the other.

api and worker recover on their own restart backoff (`restart: unless-stopped`). **No
`compose up` and no reconcile run is needed**, and neither should be used: a hand-typed
`docker compose up -d` takes no lock and runs no attestation
(`jobbliggaren-reconcile.sh` header).

> ✅ **THE PRECONDITION THIS STOP DEMANDS WAS MET 2026-08-16 — read the rest for the mechanism,
> not as an outstanding blocker.** [#1343](https://github.com/klasolsson81/jobbliggaren/issues/1343)
> is discharged: the journal was vacuumed and then re-measured against **all four** secrets — the
> master key and each of the three peppers, every one with its own positive control — at **0**.
> That is exactly the *"demonstrably free of plaintext key material"* this block asks for, and it
> is the discharge rather than a promise of one. **The stop still binds in one respect and it is
> not the same respect:** the condition expires the moment anything writes key material to the
> journal again, so re-measure before shipping rather than inheriting this line.
> ⚠ `docs/runbooks/log-sink.md` §2 carried the same precondition and had drifted from this one for
> two days; **corrected 2026-08-18** — it now cites this discharge rather than asserting the plaintext
> key as a present fact.
>
> The re-measurement this discharge demands is owed **before** the injection, not here — it is
> stated at the top of this section, where the operator is standing when it can still be acted on.
>
> ~~⛔ **STOP — THIS IS THE FIRST RUN THAT SHIPS, AND THE JOURNAL IS NOT CLEAN (#1343).**~~ The
> injection you just performed created `Backup__RcloneConfigBase64`, which is the file
> `jobbliggaren-logship.service`'s `ConditionPathExists` waits for. Every earlier firing was a
> *skip*, so no cursor exists in `/var/lib/jobbliggaren` — and `jobbliggaren-logship.sh` reads the
> journal from the beginning when there is none. **This command therefore ships the whole journal**,
> which at the time carried the master key in plaintext (discharged 2026-08-16, see above), to an OVH object with no age bound at all
> (`log-sink.md` §2 carries the mechanism and the numbers). Do not run it until the journal is
> demonstrably free of plaintext key material. A **vacuum** produces that state; a **further
> rotation** does not — it retires the generation and leaves its bytes where they are.
>
> This is repeated here rather than left in `log-sink.md` because §3 is where the operator is
> standing when the condition actually comes due — the same reason the host-timer enable step is
> repeated below.

**First bring the clone forward, or the start below runs whatever revision the box is standing
on.** `jobbliggaren-logship.service`'s `ExecStart` points straight into `/opt/jobbliggaren`, and
nothing advances that clone on its own: `jobbliggaren-reconcile.sh` invokes `git` zero times
(measured 2026-08-29), so it moves images and never the working tree. A logship repair merged to
main therefore reaches this box only at a pull, and nothing schedules one — which is why the step
belongs at this visit rather than being left to be remembered. Take it in `log-sink.md` §2's
deliberate form, which carries the reason it is never blind and is not repeated here:

```bash
git -C /opt/jobbliggaren fetch origin
git -C /opt/jobbliggaren log --oneline HEAD..origin/main -- deploy/
sudo git -C /opt/jobbliggaren pull --ff-only
```

**Then start the archive by hand, once — MANDATORY, not tidiness, whenever
`jobbliggaren-logship.timer` is ENABLED** (#1175; it has been since 2026-08-18):

```bash
sudo systemctl start jobbliggaren-logship.service
```

Its `ConditionPathExists` skipped every hourly run while the credential was absent, so the
freshness stamp is now as old as the reboot and `jobbliggaren-logship-fresh` will report the
archive stale — correctly, but for a condition you just cleared. `Persistent=true` does not cover
this: the catch-up firing happened at boot, when there was still no credential. Without this line
the alarm stays lit until the next `:17`, up to an hour, which is exactly the always-lit surface
these units are written against.

**And since 2026-08-18 that alarm is a PAGE rather than a lit lamp**, because both logship timers are
now ENABLED — before that `-fresh` never fired at all. The surface is **P1** (failed units), not the
floor: `check_floor_timers` reads `is-enabled`/`is-active` on TIMERS and is unmoved by a failed
service. `--check` dies on the absent stamp (`shipping has never succeeded`), `-fresh` fires
at `:00` **before** the shipping timer's `*:17`, and a failed unit puts M-7's P1 into /fail every 15
minutes until the first successful ship. This line is what closes that window.

**And enable the HOST absence detector, which is the one §2 lets you defer** (#1329):

```bash
sudo systemctl enable --now jobbliggaren-host-secrets-present.timer
systemctl is-active jobbliggaren-host-secrets-present.timer    # expect: active
```

This is the reminder §2 promised, and it is the only one there is: a deferral leaves no failed
unit, no timer and no row that re-reads itself. **`jobbliggaren-secrets-present.timer` is not in
this block on purpose** — it was enabled at §2 in the same visit as the install, because nothing
about the crypto set was ever deferred once `--check` stopped answering for #197's credential. If
it is somehow still disabled, `systemctl is-enabled jobbliggaren-secrets-present.timer` says so and
§2's first enable line is the fix.

Re-measure `host-detection.md` §7's floor-set row afterwards — a timer belongs in
`jobbliggaren-heartbeat.sh`'s `FLOOR_TIMERS` from the moment it is **enabled**, not from the moment
its unit files were installed, and there are now two of them with two different enable moments.

### If reconcile refuses because the ids moved — repair by re-owning, NEVER by re-injecting

Since #1295, `jobbliggaren-reconcile.service` re-measures the api image's uid/gid before every
apply and refuses when the injected secrets are not readable by the image it is about to run. The
refusal names the axis and prints both ids. It means a base-image bump moved them; it does **not**
mean the secrets are wrong.

**Do not `rm` the files and re-inject.** Re-injection means re-entering the key from escrow, and
it walks straight back into the identity trap above: after a rotation the box holds no record of
which identity is in force, and pressing Enter at the prompt stamps `local-v1` onto v2 bytes. The
values on tmpfs are correct — only their owner is stale. Change the owner:

**`chown -R` from the directory is the wrong tool and it fails silently.** It chowns the operand
itself, so `chown -R <uid>:<gid> /run/jobbliggaren/secrets` leaves the directory
`0710 <uid>:<gid>` instead of `0710 root:<gid>` — the container's own uid becomes its **owner**
and gains `rwx` on the directory holding the master key, where the design gives it `--x`. Nothing
on this box would then say so: `--check` reads the directory's mode and never its owner, and the
reconcile gate reads the directory's group and the files' owner. Both go green on the broken
posture.

**A `sudo … /run/jobbliggaren/secrets/*` GLOB IS NOT RUNNABLE BY THE OPERATOR IT IS WRITTEN FOR,
and the earlier form of this block was.** Your shell expands the glob *before* `sudo` elevates, and
`0710` gives the group `--x` — traverse, not read — so **every non-root user** is denied the
listing the glob needs, group members included. The pattern reaches the tool unexpanded and fails
with `No such file or directory`. Measured 2026-08-15 during row 32b's drill. `find` keeps the
whole expansion inside the privileged process, and `-mindepth 1` excludes the directory
structurally rather than by warning.

**Two forms, and they differ on purpose — do not "correct" one toward the other.** The *repair*
is `-mindepth 1 -maxdepth 1`, because the directory is root's and must not be chowned. The
*posture proof* below is `-maxdepth 1` alone, because the directory is exactly the operand it
exists to show.

```bash
# Both ids are in the refusal message; read them from there rather than re-deriving them.
sudo chown root:<gid> /run/jobbliggaren/secrets     # the directory: root keeps it
sudo find /run/jobbliggaren/secrets -mindepth 1 -maxdepth 1 \
  -exec chown <uid>:<gid> {} +                      # the files, and only the files
sudo systemctl start jobbliggaren-reconcile.service # apply now rather than waiting for :xx

# Prove the posture, because neither gate above reads the axis you just moved.
# NUMERIC %u:%g, never %U:%G. The container's uid/gid have no passwd/group entry on the host, so
# the name form renders them UNKNOWN — `root:UNKNOWN` on the directory, `UNKNOWN:UNKNOWN` on the
# files. The owner axis does survive in the name form, but the group does not, and reading one
# line in two vocabularies is how the axis gets missed. Measured same day.
# -maxdepth 1 WITHOUT -mindepth: unlike the repair above, the directory is the point.
sudo find /run/jobbliggaren/secrets -maxdepth 1 -exec stat -c '%n %u:%g %a' {} +
# expect: the directory 0:<gid> 710 — owner ROOT — and every file <uid>:<gid> 400
```

The numbers are deliberately not written here. A live measured id in a tracked file decays within
a commit or two; the command that prints today's is what keeps.

---

## 4. Rotation (gate M-3)

**Cadence: at least annual, plus event-driven** — box compromise, offboarding of anyone with
box access, or any known exposure of the key.

A master-key rotation re-wraps the stored per-user DEKs under new key bytes. **Field data is
never touched, and `dek_version` never changes** (that is #501's separate axis;
`UserDataKeyStore.cs:44-45` draws the line). The operation is idempotent: it selects on
`cmk_key_id`, so a second run finds nothing and exits 0 — and that exit code is the
idempotence proof.

The mechanism is `migrate rewrap-master-key`. It selects rows by the retiring `cmk_key_id`,
unwraps each DEK with the retiring key, wraps the same bytes under the incoming key, and
compare-and-swaps the row. One transaction over all rows, then a post-commit pass that proves
every row unwraps under the new key.

```bash
docker compose -f /opt/jobbliggaren/deploy/docker-compose.yml --profile ops run --rm \
  -e REWRAP_RETIRING_MASTER_KEY_FILE=/run/app-secrets/OLD_KEY \
  -e REWRAP_INCOMING_MASTER_KEY_FILE=/run/app-secrets/FieldEncryption__LocalMasterKeyBase64 \
  -e REWRAP_RETIRING_KEY_ID=local-v1 \
  -e REWRAP_INCOMING_KEY_ID=local-v2 \
  migrate-rewrap
```

The two `*_FILE` values are **paths**, never secrets — `MigrateEnv` resolves the suffix, the same
convention api and worker use. The two `*_KEY_ID` values are literal identities, not paths, and
not secret either. The dedicated `migrate-rewrap` service carries the secrets mount and runs
only under `--profile ops`; the `migrate` service that runs on every `up` receives no crypto
material at all.

**To run it against a COPY, override the connection string — not `MIGRATE_DB_NAME`.** That
variable feeds only the master-credential path, so overriding it would leave the tool pointed at
the live database while the operator believed otherwise, and the drill's throwaway key is deleted
at the end. Add `-e MIGRATE_APP_CONNECTION_STRING="…;Database=jobbliggaren_drill;…"`.

> **The FIRST rotation — the one in #198's cutover — needs none of it.** Measured 2026-08-09 with
> raw SQL on the box: `user_data_keys` holds **0 rows**. There is nothing to re-wrap, so rotating
> the master key today is simply injecting different bytes. **The B-1 cutover is therefore not
> blocked on PR-2.** Re-measure before assuming it still holds — one registered user creates the
> first row.

### Drill against a copy — required before any real rotation

Run against a **copy**, never production, and steer the copy with
`MIGRATE_APP_CONNECTION_STRING`. `MIGRATE_DB_NAME` reaches only the master-credential path,
so overriding *that* would leave the tool pointed at the live database while the operator
believed otherwise — and the drill's throwaway key is deleted at the end, which would make
the damage unrecoverable.

1. Copy the database inside the postgres container (no dump file on disk).
2. Generate a throwaway key straight into tmpfs.
3. Run the rewrap against the copy's connection string.
4. **Run it a second time** — expect 0 rows rewrapped.
5. Verify: every DEK unwraps under the new key, **and** an encrypted field still decrypts.
   The DEK-level check alone is not sufficient: a bug that generated a *fresh* DEK instead of
   re-wrapping the existing one passes every DEK-level assertion and destroys all field data.
6. Drop the copy; remove the throwaway key.

### Real rotation — the order is load-bearing

1. `sudo systemctl stop jobbliggaren-reconcile.timer` — a `*:47` tick would otherwise start
   api/worker in the middle of the rewrap.
2. `docker stop jobbliggaren-api jobbliggaren-worker`.
3. **PRESERVE THE RETIRING KEY FIRST. It exists nowhere else.** Step 5 needs it, and step 4
   overwrites the only copy — with no at-rest copy on this box, and an escrow (§1) that holds
   whatever generation was last written to it and not this one,
   skipping this step means every DEK is permanently unopenable and every encrypted field
   permanently unreadable. An earlier draft of this runbook had steps 4 and 5 without this one;
   that ordering destroyed the input to its own next step.

   ```bash
   # uid/gid THROUGH the shared measurement, not a third hand-rolled copy of it. This block used
   # to inline its own `docker run`, described as "the same way the injection script does it" —
   # true when written, false from #1295, and without the argument guards, the containment flags
   # or the numeric validation the real one carries.
   ids=$(sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-runtime-ids.sh \
     "$(sudo docker compose -f /opt/jobbliggaren/deploy/docker-compose.yml config --images \
        | grep -m1 -F jobbliggaren-api)")
   uid=$(echo "$ids" | head -1); gid=$(echo "$ids" | tail -1)

   sudo install -m 0400 -o "$uid" -g "$gid" \
     /run/jobbliggaren/secrets/FieldEncryption__LocalMasterKeyBase64 \
     /run/jobbliggaren/secrets/OLD_KEY
   ```
   A `sudo tee`-created file would be `root:root` and the container could not read it — the tool
   would then fail with a configuration error rather than a permission one.

4. Remove the old pair and inject the new one. The identity and the bytes are written together
   by one run, deliberately — the script refuses a master key without a matching identity,
   because a v2 key stamped `local-v1` makes the next rotation's compare-and-swap skip exactly
   the rows it must not skip:

   ```bash
   sudo rm -f /run/jobbliggaren/secrets/FieldEncryption__LocalMasterKeyBase64 \
              /run/jobbliggaren/secrets/FieldEncryption__LocalMasterKeyId
   sudo JBL_MASTER_KEY_ID=local-v2 \
     /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh
   ```
   ⚠ **This run also prompts for #197's `Backup__RcloneConfigBase64`, and answering it may be
   forbidden at that moment.** The script walks its host-only set after the crypto set, and prompts
   for any file that is absent — so a rotation performed while the backup credential has never been
   injected ends at a prompt this step never mentions. **Ctrl-C there is safe and is usually the
   right answer:** the master key and identity are already written by the time that prompt appears
   (verify with `--check`), and everything after the host loop is log output naming the verification
   commands. The credential is #197's to place, and `current-work.md` has held it back behind
   [#1343](https://github.com/klasolsson81/jobbliggaren/issues/1343) — shipping the journal before
   that was resolved would have landed the master key offsite. Measured 2026-08-16, where exactly
   this prompt appeared mid-rotation and was declined.

   Escrow **the new bytes and the new identity** in the same step (§1 — Klas's decision, and
   a prerequisite). The identity is not a secret, but losing track of it costs the next
   rotation its marker. ⚠ **Escrow all FOUR values, not only the one that changed.** The script
   skips files that already exist, so a master-key rotation leaves the three peppers untouched;
   an escrow written from this run alone would drop them. Carry them across from the outgoing
   escrow, and keep that outgoing copy until step 7 succeeds — §1 requires the escrow to
   span the rotation window, both generations. **Destroy the outgoing copy once step 7 has
   succeeded**, not before and not never: an escrow of a retired generation is worse than none,
   because its holder believes they have the live one.

   ⚠ **Do not carry a pepper forward "verbatim" on the escrow's own authority — the box is the
   authority.** Row 26 records that the escrow↔box link is still operator-attested at both ends,
   so a value carried forward from an unverified escrow propagates the error through every future
   rotation, silently, and the peppers are irrecoverable: the company-watch pepper owns every
   org.nr token whose plaintext was destroyed in place, the CV-fingerprint pepper every
   Ignored/Resolved decision. Compare without exposing anything, and without breaking the argv
   discipline this section just established:

   ```bash
   sudo sha256sum /run/jobbliggaren/secrets/AuditPseudonymization__PepperBase64 \
                  /run/jobbliggaren/secrets/CompanyWatchPseudonymization__PepperBase64 \
                  /run/jobbliggaren/secrets/CvReviewFingerprintPseudonymization__PepperBase64
   ```

   Hash the escrowed values off-box **with `printf '%s' "$value" | sha256sum`, never
   `echo "$value" | …`**. The injection script writes with `printf '%s'` and says so — *"no
   trailing newline is written at all"* — so the file holds the bare base64 string. `echo` appends
   a `\n` and produces a different digest, which would report a **false mismatch on a correct
   escrow**. The failure direction is safe (it stops on a good escrow rather than passing a bad
   one), but the instruction below turns it into a halted rotation, so get the form right. A real
   mismatch means the escrow is not what the box runs, and that is a stop rather than a note.
5. Rewrap old → new, using `OLD_KEY` from step 3 (the command above). **Skip entirely when
   `user_data_keys` is empty** — there is nothing to re-wrap and the new bytes are already in
   force; the tool would report a no-op anyway.
6. Start the containers; confirm `healthy`.
7. **Read an encrypted field through the app.** A health probe decrypts nothing, and this is the
   box half of the gate ADR 0049 §5 names — the CI half is
   `Rewrap_FieldCiphertextStillDecrypts`.
8. **Take a fresh backup and verify it landed, BEFORE step 9 destroys `OLD_KEY` (#197).**

   ```bash
   sudo systemctl start jobbliggaren-backup.service
   journalctl -u jobbliggaren-backup.service -n 30    # expect a promoted DEK generation
   ```

   > **Every offsite DEK artefact taken before this rotation becomes unreadable the moment
   > `OLD_KEY` dies.** The re-wrap rewrites `wrapped_dek` in place, so the DEK artefacts already
   > offsite are wrapped under the retiring key and nothing else can open them. Skip this step
   > and the 30-day window collapses to whatever this rotation produces next — the retained main
   > artefacts survive, but there is no key generation that pairs with them until the next
   > nightly run, and if THAT run fails there is none at all.
   >
   > Main artefacts are unaffected: they carry no keys. That asymmetry is why ADR 0125 splits
   > the dump — a single full dump would have made every pre-rotation artefact unrestorable
   > here, i.e. the entire retention window, once a year, by design.
   >
   > `dek_version` is untouched by a rotation, so the new DEK artefact pairs with **any**
   > retained main artefact, not only ones taken after it.

9. **Only after steps 7 and 8 succeed:** `sudo rm -f /run/jobbliggaren/secrets/OLD_KEY`.

   > **If step 5, 7 or 8 fails, STOP and do NOT remove `OLD_KEY`.** It is the only way back:
   > step 4 already replaced the live key, so rows still wrapped under the retiring key can be
   > reached through nothing else. The re-wrap tool's own post-commit message says to re-run
   > rather than restore the old key — and re-running reads `OLD_KEY`.

10. `sudo systemctl start jobbliggaren-secrets-present.timer` if it was stopped, and re-arm the
    reconcile timer. **Use `enable --now` instead on any timer that was never enabled in the first
    place** — `start` on a never-enabled timer leaves it disabled, so it dies at the next reboot,
    silently, on the box's only alarm surface. Since #1329 the crypto timer is not the one that
    gets left behind: §2 enables it in the same visit as the install.
    `jobbliggaren-host-secrets-present.timer` is, because §2 lets that one wait for #197's
    credential. `systemctl is-enabled <timer>` tells the two states apart and `start` does not.

---

## 5. Recovery, and the one way to lose everything

**Losing a value destroys what it protects, irreversibly** — and that is true of all four, not
only the master key. The master key: every encrypted field. The company-watch pepper: every
stored organisation-number token, because the backfill destroyed the plaintext in place. The
CV-fingerprint pepper: every Ignored/Resolved finding decision reverts to Open. (The audit
pepper is the exception — nothing reads back against it.)

With no at-rest copy, an **off-box escrow is the only recovery path**, and per §1 it exists for
the four crypto values since 2026-08-12 — **but for the generation in force when it was written,
and not for the age private key, whose row is still open.** Crypto-erasure is the design
(ADR 0049 Beslut 2) — the same property that makes an account deletion final makes a lost key
final.

If an escrow copy exists: inject it (§3). If it does not, there is nothing to recover and no
procedure here will help.

---

## 6. What this runbook does not own

- **Key-access detection.** `--check` detects **absence**, not access, and the two must not be
  read as one. Access detection needs auditd (absent on this box, measured 2026-08-09), and
  under this model every illegitimate read of the tmpfs file is by construction a root action
  — a subset of host root-activity detection, which ADR 0050:574 assigns to #196/#1201. The
  disposition is recorded on [#1201](https://github.com/klasolsson81/jobbliggaren/issues/1201).
- **Backup encryption.** [`backup-restore.md`](backup-restore.md) (#197, ADR 0125). It consumed
  this model rather than rebuilding one, and the shape it took is worth knowing here: the
  **upload credential** rides the injection script, in a sibling directory
  (`/run/jobbliggaren/host-secrets`) that is mounted into no container — but the **age identity
  never reaches this box at all**. The box holds only the public recipient, so there is nothing
  to inject, nothing to escrow *here*, and nothing to steal that would read a backup.
  §4 step 8 is the one place the two runbooks are coupled, and it is a data-loss guard: a
  rotation makes every offsite DEK artefact unreadable, so a fresh verified backup must land
  before `OLD_KEY` is destroyed.
- **The database and edge credentials.** `POSTGRES_*` and `BASIC_AUTH_HASH` stay in
  `deploy/.env`. They are **not** B-1 subjects, and moving them is deliberately out of scope
  here — a named non-goal, not an oversight.
- **Granting the risk acceptances.** The key is plaintext in process memory and root reads it;
  ADR 0123 carries that threat model. ⚠ **Klas granted it 2026-08-16** — do not ask him again;
  read the status in the ADR, never here. Granting closes the acceptance, not the two mitigations
  the ADR names as unclosed — and it covers only the state **without** real user data, so it
  lapses by its own terms at the point #1201's M-7 is evaluated. `security-auditor` ruled
  2026-08-17 that **M-7 does convert**; see `release-checklist.md` §2.6 point 3.5 for what would
  actually discharge it.

## 7. Unmeasured, and named

- **Unplanned-reboot frequency. TRANSCRIBED 2026-08-15 BEFORE ANY JOURNAL VACUUM (#1343), because
  the instrument is the journal and a vacuum resets it.** `journalctl --list-boots` on that date:
  **2 boots** — `-1` spanning 2026-08-04 01:26 → 2026-08-15 21:02, and `0` from 2026-08-15 21:04.
  The single transition is the row-24 drill, so **0 unplanned reboots observed** over ~11.8 days.
  ⚠ Read that as the weaker measure it is: total reboots, planned and unplanned together — **not**
  this premise's own instrument, which §2's timer starts at `enable` — a date this session did not
  establish from the repo, so read that series as short rather than as any particular length. Journal at the same moment: 51.2 MB, oldest entry `2026-08-04T01:26:42`,
  `Storage=persistent`, `SystemMaxUse=4G`, no `MaxRetentionSec`. ⚠ **The journal's start predates
  nothing about the box** — the root filesystem was created **2026-07-30 14:22** and `/var/log/btmp`
  dates from 08-02, so the box is **four and a half days** older than its journal (4 d 11 h).
  A consequence worth stating rather than leaving to be re-derived: `--list-boots` cannot see
  any boot before that start, so the count above is a floor, not a census. Two explanations fit and this
  session could not separate them: the 2026-08-04 vacuum recorded in `vps-base-hardening.md`, or
  persistence being enabled that same day during hardening. Original note: `last reboot` returned no
  readable history on 2026-08-09.
  `jobbliggaren-secrets-present.timer` is the instrument that starts the series; until it has
  run for a while, the availability cost of the no-at-rest-copy model is bounded by nothing
  but "reboots are manual today". **The series does not begin at install but at `enable`** — §2
  now takes that step in the same visit as the install, so what this premise waits on is the
  cutover itself and no longer #197's ops half (#1329).
- **Whether any Netcup snapshot has been taken since 2026-08-05.** If one exists it contains
  the old plaintext key — which is one of the reasons the cutover rotates rather than relocates.
- **Whether Netcup's snapshot facility captures guest RAM.** If it does, no in-guest mechanism
  closes it; that is a hypervisor-level residual and applies to every branch equally.
- **Compose behaviour on v5.4.0.** The compose file's load-bearing behavioural notes were
  measured on 2.40.3; the box now runs v5.4.0.
- ~~**The ownership gate's real `docker run … 'id -u; id -g'` (#1295).**~~ **MEASURED 2026-08-15
  — this entry is discharged and kept struck rather than deleted, so a reader who remembers it as
  open can see what closed it.** CI stubs docker, as it stubs cosign, so the fixtures pin the
  comparison and never the production ownership triple. `vps-deploy-stack.md` row 32b ran the
  drill on the box after injection: the gate refused on a deliberately broken group, named the
  traversal axis and both ids, left the running containers up, and cleared on the repair. The
  production triple is recorded there. **One half is still owed and is named in that row rather
  than here:** the repair's *files* line was never load-bearing in the drill — only the
  directory's group was broken — so the `find` form that replaced the glob has not itself been
  run on the box by a non-root operator.
- **A uid or gid divergence BETWEEN our three images (#1295).** The gate measures the api image,
  because that is the image injection measured when it set the ownership. All three Dockerfiles
  declare `USER app` — but **no gate anywhere measures that they still agree**, in CI or on the
  box (measured 2026-08-12: `release-images.yml` contains no `uid`/`gid` check). A worker or
  migrate image that drifted alone would be caught by nothing.
- **Injection still runs an unattested image.** `jobbliggaren-inject-secrets.sh` measures the ids
  from the operator's tag, resolved out of the compose file before anything has been verified.
  After #1295, reconcile is clean on that axis — it measures the digest attestation just cleared
  — and this is the only place left on the box that executes an unattested image. Blast radius is
  an unprivileged container with `--network none`, `--cap-drop ALL`, `no-new-privileges`, no
  mounts and no environment, with the operator at the keyboard.
- **The measurement needs `sh` and `id` inside the image (#1295).** A chiseled or distroless base
  — the same event class the gate exists to catch — would make the helper exit non-zero, and the
  gate would then refuse the apply hourly as "cannot answer" rather than as a bad base image.
- **Five of the gate's own guards are unreachable by construction, and so unpinned (#1295;
  the last two #1319/#1320).** Reconcile's re-validation of the helper's numeric output, the
  `@sha256:` digest assertion, the mode arithmetic's failure branch, and — since the posture arms
  landed — `--check`'s two CANNOT ANSWER branches: a `stat` that fails on a directory `[[ -d ]]`
  has just accepted, and one that fails on a file `[[ -e ]]` has just accepted. Both need an I/O
  error or a race against a unit running as root. Each is a guard on a
  SEAM — a separate executable, a directory another actor writes — rather than on a state any
  path in `src/` or on this box produces today. They are deliberately not fixtured: a fixture
  would have to manufacture a state nothing produces, which is the test-premise class CLAUDE.md
  §5 rejects. Named here so a later change that makes one of them reachable knows it arrives
  without a pin. **A fourth entry stood here and was struck as false:** the file loop past its
  first element IS pinned wherever the mode case runs — measured, the fixture's glob order puts
  the chmod'd file second, so the refusal happens on the second iteration and a `break` fails
  the case. It is unpinned only where that case is skipped, which is not a repo-wide property.
