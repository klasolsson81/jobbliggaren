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
> §6.5, like every ADR from 0074; if it is absent from your checkout the decision and its ground
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
| `/run/jobbliggaren/secrets/` | tmpfs staging dir. **`0700 root:root` at boot** (`/etc/tmpfiles.d/jobbliggaren.conf`), **raised to `0710 root:<container-gid>` by the injection script**. Two actors, two states — and with the `:` prefix tmpfiles can never produce the second one, so a `WRONG MODE` from `--check` means the injection has not run. **A third actor reads it since #1295, and only reads:** `jobbliggaren-reconcile.service` re-asserts the ownership against the image it is about to apply, and refuses the apply rather than repairing anything |
| `…/FieldEncryption__LocalMasterKeyBase64` | the master key, `0400` |
| `…/FieldEncryption__LocalMasterKeyId` | key identity, not a secret — the rotation marker |
| `…/AuditPseudonymization__PepperBase64` | pepper |
| `…/CompanyWatchPseudonymization__PepperBase64` | pepper |
| `…/CvReviewFingerprintPseudonymization__PepperBase64` | pepper |
| `/run/app-secrets` | the same directory as api and worker see it (read-only bind mount) |
| `/run/jobbliggaren/host-secrets/Backup__RcloneConfigBase64` | **#197, and mounted into no container.** `0400 root:root` in a `0700 root:root` directory. Injected by the same script, in the same run — but demanded by `--check-host` and by no other predicate, so its absence gates its **own** timer and nothing else (#1329) |
| `jobbliggaren-inject-secrets.sh` | injection (interactive), `--check` (crypto absence) and `--check-host` (host-only absence) — **two detectors, one per set, one per owner** |
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
`--check` demands the crypto secrets, their directory mode, and `deploy/.env`'s mail configuration
(an invalid `EMAIL_PROVIDER` and, under `Ses`, the SES credentials). `--check-host` demands #197's
`Backup__RcloneConfigBase64`, and nothing else — which is why an absent backup credential no longer
holds the crypto alarm down.

**And a permanently failed unit is not merely an unread list.** Where `jobbliggaren-heartbeat.timer`
is enabled and reaching its expecter, P1 in `jobbliggaren-heartbeat.sh` is `systemctl --failed`
being *clean*, so a permanently failed unit fail-pages on every heartbeat run and makes M-7's P1
vacuous for as long as it stands. The other failed units are still *listed* — `check_failed_units`
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
   32 bytes, and creates each file `0400` owned by the measured uid. The SES credentials are
   prompted for only under the condition the script states (provider `Ses`, a `_FILE` pointer,
   or `JBL_INJECT_SES=1`).
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
> An empty result means no DEK exists yet and the default is correct. Otherwise pass the value
> it prints: `sudo JBL_MASTER_KEY_ID=<value> …inject-secrets.sh`. The identity is also part of
> what escrow must hold — **the bytes AND the identity**, not just the bytes.

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

**Then start the archive by hand, once, if `jobbliggaren-logship.timer` is installed** (#1175):

```bash
sudo systemctl start jobbliggaren-logship.service
```

Its `ConditionPathExists` skipped every hourly run while the credential was absent, so the
freshness stamp is now as old as the reboot and `jobbliggaren-logship-fresh` will report the
archive stale — correctly, but for a condition you just cleared. `Persistent=true` does not cover
this: the catch-up firing happened at boot, when there was still no credential. Without this line
the alarm stays lit until the next `:17`, up to an hour, which is exactly the always-lit surface
these units are written against.

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
`0710 root:<gid>` denies an operator outside that group the read — so the pattern reaches the tool
unexpanded and it fails with `No such file or directory`. Measured 2026-08-15 during row 32b's
drill, on `jpadmin` (gids 1000, 27). `find` keeps the whole expansion inside the privileged
process, and `-mindepth 1` excludes the directory structurally rather than by warning.

```bash
# Both ids are in the refusal message; read them from there rather than re-deriving them.
sudo chown root:<gid> /run/jobbliggaren/secrets     # the directory: root keeps it
sudo find /run/jobbliggaren/secrets -mindepth 1 -maxdepth 1 \
  -exec chown <uid>:<gid> {} +                      # the files, and only the files
sudo systemctl start jobbliggaren-reconcile.service # apply now rather than waiting for :xx

# Prove the posture, because neither gate above reads the axis you just moved.
# NUMERIC %u:%g, never %U:%G: the container's uid/gid have no passwd/group entry on the host,
# so the name form prints UNKNOWN:UNKNOWN and the line cannot be read at all (measured same day).
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
   Escrow **the new bytes and the new identity** in the same step (§1 — Klas's decision, and
   a prerequisite). The identity is not a secret, but losing track of it costs the next
   rotation its marker.
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
  ADR 0123 carries that threat model and is still `Proposed` and ungranted. Klas grants,
  never a session (CLAUDE.md §9.6).

## 7. Unmeasured, and named

- **Unplanned-reboot frequency.** `last reboot` returned no readable history on 2026-08-09.
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
- **The ownership gate's real `docker run … 'id -u; id -g'` (#1295).** CI stubs docker, as it
  stubs cosign, so what the fixtures pin is the comparison and never the production ownership
  triple. Its proof is `vps-deploy-stack.md` row 32b, and that row runs after injection — so
  until the cutover, the gate is measured only against a stub.
- **A uid or gid divergence BETWEEN our three images (#1295).** The gate measures the api image,
  because that is the image injection measured when it set the ownership. All three Dockerfiles
  declare `USER app` — but **no gate anywhere measures that they still agree**, in CI or on the
  box (measured 2026-08-12: `release-images.yml` contains no `uid`/`gid` check). A worker or
  migrate image that drifted alone would be caught by nothing.
- **The directory's OWNER is read by nothing (#1295).** `--check` reads its mode, the reconcile
  gate reads its group and the files' owner and mode. `install -d -o root` sets it once at
  injection and `tmpfiles` is create-only, so a hand-`chown` — including the `chown -R` this
  runbook now warns against — leaves `0710 <container-uid>:<gid>` with every gate green. The
  cutover row (`vps-deploy-stack.md` 32b) is the only place that reads the axis, and it reads it
  once. Owned by [#1319](https://github.com/klasolsson81/jobbliggaren/issues/1319).
- **Injection still runs an unattested image.** `jobbliggaren-inject-secrets.sh` measures the ids
  from the operator's tag, resolved out of the compose file before anything has been verified.
  After #1295, reconcile is clean on that axis — it measures the digest attestation just cleared
  — and this is the only place left on the box that executes an unattested image. Blast radius is
  an unprivileged container with `--network none`, `--cap-drop ALL`, `no-new-privileges`, no
  mounts and no environment, with the operator at the keyboard.
- **The measurement needs `sh` and `id` inside the image (#1295).** A chiseled or distroless base
  — the same event class the gate exists to catch — would make the helper exit non-zero, and the
  gate would then refuse the apply hourly as "cannot answer" rather than as a bad base image.
- **Three of the gate's own guards are unreachable by construction, and so unpinned (#1295).**
  Reconcile's re-validation of the helper's numeric output, the
  `@sha256:` digest assertion, and the mode arithmetic's failure branch. Each is a guard on a
  SEAM — a separate executable, a directory another actor writes — rather than on a state any
  path in `src/` or on this box produces today. They are deliberately not fixtured: a fixture
  would have to manufacture a state nothing produces, which is the test-premise class CLAUDE.md
  §5 rejects. Named here so a later change that makes one of them reachable knows it arrives
  without a pin. **A fourth entry stood here and was struck as false:** the file loop past its
  first element IS pinned wherever the mode case runs — measured, the fixture's glob order puts
  the chmod'd file second, so the refusal happens on the second iteration and a `break` fails
  the case. It is unpinned only where that case is skipped, which is not a repo-wide property.
