# Backup and restore — operations

**Scope:** the nightly encrypted offsite backup of the production database, its retention, and
the procedure for restoring from it.
**Owned by** [#197](https://github.com/klasolsson81/jobbliggaren/issues/197) (ADR 0050 gate M-4;
ADR 0050 `Amendment 2026-08-04` §7 is the binding requirement set).
**Host:** Netcup RS 1000 G12, Debian 13 (trixie), Nuremberg.
**Related:** [`vps-deploy-stack.md`](./vps-deploy-stack.md) ·
[`master-key-ops.md`](./master-key-ops.md) · [`account-deletion.md`](./account-deletion.md)

> **§5's COMMANDS ARE EXECUTED ON EVERY BUILD (2026-08-09). §5 AS AN OPERATIONAL PROCEDURE STILL
> IS NOT. Read the difference before relying on either.**
>
> **What is now observed rather than derived:** steps 3 to 7 run in CI against a real
> `postgres:18` pair, on the real migrated schema, over dumps the mechanism's own `pg_dump`
> invocations produce — `BackupRestoreDrillTests` (#197 PR-2). **Most** commands below are the
> strings that test executes; the four it does not type verbatim are named in its own docblock,
> and `RestoreDrillRunbookParityTests` holds a **fixed set of load-bearing properties** across the
> two files rather than every divergence — its own docblock says two files can satisfy every rule
> and still differ. Four semantics are measured: a user erased after a main artefact was taken
> restores with ciphertext and no key, the survivor decrypts, the staging table's necessity, and
> (b2)'s inability to distinguish erasure from a reversed pairing. **Step 7 is proven over a
> superuser connection only** — its privilege axis is unproven and is
> [#1286](https://github.com/klasolsson81/jobbliggaren/issues/1286).
>
> **What is still entirely underived:** every step's behaviour against a **real artefact from the
> real target** — the fetch, the `age` decryption, the private key, the object names, the pairing
> stamp, and the schema as it will actually be on the day. CI possesses no private key by design
> (§1), so it can prove none of that. **Gate M-4 is closed by the ops half, not by this note**, and
> it must complete **before first real data**. When it has run, record the date here and fill the
> verification rows in `vps-deploy-stack.md` §5.

---

## 1. The model, in one paragraph

Every night at 02:15 UTC a systemd timer runs `pg_dump` inside the running Postgres container,
pipes it through `age` on the host, and streams the ciphertext to an offsite target. **Plaintext
never touches a disk** — it exists only inside a pipe. **The box holds no private key**, only the
public recipient, so there is nothing here to steal that would read a backup; that is how ADR
0050 `Amendment 2026-08-04` §7 requirement (b) is satisfied structurally rather than by a rule
someone has to remember. The cost of that, stated plainly: **this box cannot verify that its own
backups decrypt.** Only the drill can.

**Two artefacts per night, and the split is the design.** The *main* artefact carries every table
except the **contents** of `user_data_keys`; the *DEK* artefact carries exactly those contents,
and exactly one verified generation of it is kept. A restore pairs **any main artefact inside the
retention window with the current DEK artefact**. A user hard-deleted since that main artefact was
taken therefore has no key anywhere in what we hold, and their field-encrypted columns are
unreadable by any combination of artefacts in our possession.

**Why that matters rather than being a nicety.** A single full dump would carry each user's
wrapped DEK *beside* the ciphertext it unwraps, and the master key survives on the box — so
restoring a dump taken before an erasure would make that user readable again. ADR 0049 Beslut 2
claims the opposite, and `content-legal.json` publishes the same claim to users
(*"…även från en eventuell säkerhetskopia"*). The claim was true only by an unwritten premise.
The split dump is what makes it true, and ADR 0049's amendment now records the premise so a later
refactor cannot quietly remove it (senior-cto-advisor bind 2026-08-09, D2).

> **ESCROW OF THE AGE PRIVATE KEY IS A HARD PREREQUISITE. ~~UNDECIDED AS OF 2026-08-09~~ — THE
> FORM WAS DECIDED 2026-08-12 AND THE ACT FOLLOWED THE SAME DAY — ROW 32 IS CLOSED.**
>
> There is no copy of the private key on this box by design, so an off-box escrow is the only
> path from ciphertext back to data. **A backup whose key is not escrowed is not a backup** — it
> is an offsite copy of noise.
>
> **The gate this used to point at is no longer the right one.** It said *"do not treat the
> offsite artefacts as a recovery path until **the escrow row** carries a measurement"*, and
> "the escrow row" meant the master key's — `vps-deploy-stack.md` §5 **row 26**, which has
> carried a measurement since 2026-08-12. Read literally, this callout now discharges itself
> while the age key is missing entirely and the artefacts are exactly as unreadable as before.
> **The condition hangs on row 32**, which is open — **but no longer on the identity's existence.**
> ~~The identity was not found in four roots on 2026-08-07, and generating a fresh one costs
> nothing until the first backup lands.~~ **A fresh one was generated 2026-08-12** and its
> recipient is what `deploy/backup/age.recipient` now carries. **Do not generate another** — that
> instruction has been discharged, and a reader acting on the struck sentence would produce a
> third identity and orphan the second.
>
> Klas owns it, and the *form* is settled — plaintext on his own devices, the same device as
> `jobbpilot_vps_ed25519` permitted, over `security-auditor`'s objection and recorded as an
> accepted risk in **ADR 0129** (gitignored per §6.5; if it is absent from your checkout, the
> decision is summarised in `vps-deploy-stack.md` rows 26 and 32). Her reservation in §5 below —
> that this choice is hers once real data exists — is **unspent**, and the rotation did not spend
> it: the new private half sits on the same device, which is the same accepted risk and not a new
> one. **Row 32 is closed and dated 2026-08-12: the identity exists and Klas confirmed the escrow.**
> Both escrow rows are now shut, which was the last thing blocking #198's cutover on this axis.
> The date lives there, not here; this callout never owned it.
>
> **Where it is, measured 2026-08-12: outside the repository, on Klas's own machine beside the
> four crypto values (ADR 0129).** No `.gitignore` rule guards it and none should — a rule for a
> path that cannot occur is decoration that reads as protection. The directory is deliberately
> not named here: this PR exists because key material accumulated somewhere it should not have,
> and writing the storage location into a tracked file that agents read and quote is the same
> class one step removed. Named this far so the next reviewer measures the question once.
>
> Backups may be *taken* meanwhile — encryption needs only the recipient — and taking them is
> strictly better than not. What may not happen is anyone relying on them.

**What this does not protect against, named rather than implied.** A restore from a main artefact
that predates a user's *deletion request* resurrects that user as live, with the request lost.
That is inherent to backups of any shape, it is bounded to 30 days by the retention window, and
neither the split dump nor any other design closes it. It is disclosed here because the
alternative is a runbook that reads as if it did.

---

## 2. Files, units, and install

| Path | What |
|---|---|
| `deploy/systemd/jobbliggaren-backup.sh` | the mechanism, and the `--check` freshness probe |
| `deploy/systemd/jobbliggaren-backup.{service,timer}` | nightly at 02:15 UTC, `Persistent=true` |
| `deploy/systemd/jobbliggaren-backup-fresh.{service,timer}` | hourly staleness probe |
| `/run/jobbliggaren/host-secrets/Backup__RcloneConfigBase64` | upload credential, tmpfs, `0400 root:root` |
| `/opt/jobbliggaren/deploy/backup/age.recipient` | the **public** recipient, `0444 root:root` |
| `/var/lib/jobbliggaren/last-successful-backup` | the stamp the freshness probe reads |

**`/run/jobbliggaren/host-secrets` is not `/run/jobbliggaren/secrets`, and the difference is the
control.** The second is bind-mounted read-only into `api` and `worker`; the first is mounted
**nowhere**. The upload credential is the box's write access to the backups, and an RCE in the
application must not reach it. Putting it in the mounted directory as a root-owned `0400` file
would also have worked — but then the separation rests on a mode bit and on nobody widening the
mount later, and a directory that is not mounted cannot be exposed by any edit to a mount.

> **PRECONDITION, AND IT IS NOT THE TOOLS: THE BOX'S CLONE MUST ALREADY CARRY THIS MECHANISM.**
> Step 3 below says the recipient "arrives with the `deploy/` clone". That is true of a clone new
> enough to contain it and false of every older one, and **nothing in this block checks which one
> you have** — the install would proceed to `chown` a path that is not there.
>
> **Measure two files in the clone and one directory on the host, because the clone's age and
> whether anyone acted on it are different facts:**
>
> ```bash
> ls /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh \
>    /opt/jobbliggaren/deploy/backup/age.recipient
> ls -d /run/jobbliggaren/secrets
> ```
>
> The two files **bracket** the clone. `jobbliggaren-inject-secrets.sh` arrived with #198
> (`66f2ac39`, which added it, `jobbliggaren-tmpfiles.conf` **and** the compose `_FILE` switch in
> one commit) and `age.recipient` arrived last, with #197 (`c1d293b4`), so between them they date
> the clone against both changes. `jobbliggaren-backup.sh` (`90db66e1`) lands between the two and
> adds no state of its own, which is why it is not part of the test. Verify the dating with
> `git log --diff-filter=A --oneline -- <path>` on either file.
>
> The directory is a **separate fact and is not inferable from the files**: `ls` dates the clone,
> while whether #198's install was ever *run* shows only on the host.
>
> - **Both files present** — continue into the install below. **If `/run/jobbliggaren/secrets` is
>   nonetheless absent, stop and run the five ordered steps below, skipping step 2** — your clone
>   is already current, so what you need is step 1 (close the window: timer **and** service), step
>   3 (`master-key-ops.md` §2 and §3), step 4 and step 5. The absence says #198's install has not
>   run on this host; it does **not** say the stack is broken yet, and the difference decides what
>   you do. An `up -d` from the `_FILE` compose would itself have created that directory — the
>   mount carries `create_host_path: true`, which you can re-measure with
>   `docker compose -f /opt/jobbliggaren/deploy/docker-compose.yml --env-file /dev/null config
>   --no-interpolate` (**both flags are load-bearing, they close different channels, and measured,
>   neither closes both.** `--env-file` closes the *file* channel — compose never opens the
>   root-only `deploy/.env`, which is why this needs no `sudo`. `--no-interpolate` closes the
>   *substitution* channel — nothing is expanded at all, which is what still protects you when the
>   values are reachable some other way, exported in your shell or `--env-file` pointed at the real
>   file. Drop **both** and compose prints all four database passwords, both edge basic-auth values
>   and the ACME address to your terminal. Those values live in `.env` by decision, not by
>   oversight: `master-key-ops.md` calls moving the database and edge credentials a named non-goal,
>   and the ACME address is a contact rather than a credential. That is why the file is root-only
>   `0600`. Measured against the current compose: the property survives both flags, and
>   `${POSTGRES_APP_PASSWORD:?}` comes back unexpanded, which is the visible control that
>   interpolation is off). So the directory's absence is
>   evidence that reconcile has **not yet applied** the new compose. Note *applied*, not *ticked*:
>   `jobbliggaren-reconcile.sh` fail-closes before `up -d` on several paths and then keeps serving
>   the old containers, so a tick is not an apply. The failure is therefore the next apply, not a
>   past one, which is why you are racing the timer and must close the window.
> - **`inject-secrets.sh` present, `age.recipient` missing** — the clone sits between #198 and
>   #197. Update the clone, then run this install. **And if `/run/jobbliggaren/secrets` is absent,
>   this is the same race as the bullet above** — a clone carrying #198 does not mean anyone
>   installed it — so run the five steps below in full, `master-key-ops.md` §2 and §3 included.
> - **Both files missing** — the clone predates #198, and **the clone update must come FIRST.** It
>   cannot be second: #198's own install block reads every file it installs out of the clone
>   (`master-key-ops.md` §2 installs `/opt/jobbliggaren/deploy/systemd/jobbliggaren-tmpfiles.conf`
>   and the two `-secrets-present` units; §3 runs
>   `/opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh`). "Run #198's install first"
>   is unexecutable on a clone that does not contain it.
>
> **Whichever branch applies, a clone update is not a free step and must not be taken as one.**
> `jobbliggaren-reconcile.timer` fires at `*:47:00` with `RandomizedDelaySec=180`, and
> `jobbliggaren-reconcile.sh` applies `docker compose up -d --remove-orphans --pull never` before
> writing its stamp. The unit runs no `git` command — it applies whatever is already in the clone
> — so **a pull is applied to the live stack by the next tick, up to ~63 minutes later, by a unit
> rather than by a decision, and not at a moment anyone chose.** Confirm the timer's state before
> you rely on any of this: `systemctl list-timers 'jobbliggaren-reconcile*'`.
>
> A clone predating #198 is the case that bites: it carries `.env`-sourced crypto values, while
> the compose file the pull brings reads them through `_FILE` pointers naming `/run/app-secrets`
> — the container side of the read-only bind mount whose host side is `/run/jobbliggaren/secrets`,
> a directory that does not exist until #198's install block has run.
> `vps-deploy-stack.md` §3 states the outcome: api and worker **crash-loop rather than refusing to
> start**. **That outcome is quoted, not re-measured here — the counterfactual is a live outage.**
>
> **So on any branch that reaches this point — both files missing, or either of the two above with
> `/run/jobbliggaren/secrets` absent — close the reconcile window rather than racing it. Five
> ordered steps, and steps 3 and 4 are procedures rather than commands —
> DO NOT PASTE THIS AS ONE BLOCK.** A paste would run stop, pull and start with nothing between
> them, and `Persistent=true` makes that `start` fire the missed elapse **immediately**, applying
> the pulled `_FILE` compose against a `/run/jobbliggaren/secrets` no install has created. That is
> the crash-loop this branch exists to avoid, arriving sooner than if you had done nothing.
>
> **1.** Stop the timer **and the service**, then measure that it took. Stopping the timer alone
> is not enough: nothing binds the two — no `PropagatesStopTo`, `BindsTo` or `PartOf` — so a
> reconcile already in flight keeps running and reads the compose file off disk at `up -d` time,
> which is after your pull. Its `flock` does not help either; that lock exists against a human
> running compose by hand, and `git pull` takes no lock.
>
> ```bash
> sudo systemctl stop jobbliggaren-reconcile.timer jobbliggaren-reconcile.service
> systemctl is-active jobbliggaren-reconcile.service   # expect: inactive
> ```
>
> **2.** Update the clone.
>
> ```bash
> sudo git -C /opt/jobbliggaren pull
> ```
>
> **3.** Run `master-key-ops.md` §2 (install), then §3 (inject). §3 prompts for the secret values
> and is not something you can paste from here.
>
> **4.** Run the install block below.
>
> **5.** Re-arm — and **measure** rather than trust that steps 3 and 4 happened, because
> `Persistent=true` fires the missed elapse at once and there is no second chance to notice.
>
> **The precondition is injected secrets, not a directory.** `master-key-ops.md` §2 creates
> `/run/jobbliggaren/secrets` **empty on purpose** — its tmpfiles unit says why, an empty
> directory makes api and worker fail loudly instead of silently — and §3 is what fills it, with
> many ways to stop before it does. So a `test -d` would pass the moment §2 had run, before §3
> had asked for a single value, and arm the timer against empty secrets: the crash-loop this
> branch exists to avoid, waved through by its own guard.
>
> Use the injector's own `--check`, which inspects the directory, its mode and each secret's
> contents. It is also the diagnostic `master-key-ops.md` §2 already relies on: with the `:` prefix
> in the tmpfiles unit, a `WRONG MODE` from `--check` **naming the secrets directory** means the injection has not run.
>
> Then confirm the timer — and note what is and is not silent here, because the reason is not the
> one that applied to a `test -d`. **`--check` is loud when it refuses**, a named line per missing
> item on stderr. What says nothing is `systemctl start` on success, and **neither command tells
> you whether the timer ended up armed.** Step 1 left it stopped, so a refusal you skim past leaves
> reconcile disarmed indefinitely, and a stopped timer appears on no alarm surface of its own: it
> is not in `systemctl --failed`, there is no freshness probe for reconcile the way there is for
> backup, and nothing reads its stamp on a cadence.
>
> **Since #1201 that specific gap has a mechanism** — `jobbliggaren-heartbeat` names
> `jobbliggaren-reconcile.timer` in its floor set and pages when an enabled timer is not active,
> which is exactly this failure. **But shipping a mechanism is not closing a gap:** it covers this
> only once it is installed on the box and the rows in
> [`host-detection.md`](./host-detection.md) §7 carry measurements. Until then, read the paragraph
> above as still true of this box.
>
> ```bash
> sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh --check \
>   && sudo systemctl start jobbliggaren-reconcile.timer
> systemctl is-active jobbliggaren-reconcile.timer   # expect: active
> ```
>
> (Written 2026-08-10, when the box was measured carrying neither file and no secrets directory,
> while the reconcile
> timer was live — the ordering had never been stated, and the install block below reads as though
> the clone is always current.)

### Install (once)

```bash
# 1. The tools. MEASURED 2026-08-10 and installed on this box — versions in
#    `vps-deploy-stack.md` §5 row 28. The STOPP below still stands for any other host or a
#    later trixie: it is the procedure, not a prediction. If either package is absent from trixie,
#    STOP and escalate rather than fetching a binary: `sops` was measured absent for #198, so
#    this class of absence is live on this box, not hypothetical.
sudo apt-get update && sudo apt-get install -y age rclone
age --version && rclone version

# 2. The tmpfiles line that creates the host-only directory at every boot.
sudo install -m 0644 /opt/jobbliggaren/deploy/systemd/jobbliggaren-tmpfiles.conf \
  /etc/tmpfiles.d/jobbliggaren.conf
sudo systemd-tmpfiles --create
stat -c '%a %U:%G' /run/jobbliggaren/host-secrets     # expect: 700 root:root

# 3. The recipient. It is TRACKED in the repo (it is public), so it arrives with the deploy/
#    clone. Klas generated the identity in force on his own machine 2026-08-12, and its private
#    half has not left it. Requirement (b) is structural rather than a rule because the box holds
#    only the public half - not because of any claim about where a private half has travelled.
#    THE 2026-08-09 IDENTITY - recipient `age1vrkz…` - IS REVOKED AND MUST NEVER BE REINSTALLED:
#    its private half was exposed in a chat transcript on 2026-08-12. It is still reachable from
#    git history and named in two gitignored documents, so "replaced" is not enough to say about
#    it. It is written truncated on purpose: BackupUnitFilePinTests matches age1 plus exactly 58
#    characters, so a full retired recipient here would read as a second current one and the
#    guard would demand the deletion of this provenance.
#    chown, not just chmod: the file arrives from a clone and is owned by whoever cloned it, and
#    0444 stops everyone EXCEPT the owner. Its integrity is the control - a swapped recipient
#    costs every subsequent night, silently, and only the drill notices.
#    THE DIRECTORY TOO, not only the file. Write access to a directory governs unlink and rename,
#    so whoever owns deploy/backup/ can REPLACE age.recipient however the file itself is moded --
#    and a stat that reads only the file cannot see that. The reason given above ("owned by
#    whoever cloned it") applies to the directory verbatim.
sudo chown root:root /opt/jobbliggaren/deploy/backup /opt/jobbliggaren/deploy/backup/age.recipient
sudo chmod 0755      /opt/jobbliggaren/deploy/backup
sudo chmod 0444      /opt/jobbliggaren/deploy/backup/age.recipient
stat -c '%a %U:%G' /opt/jobbliggaren/deploy/backup /opt/jobbliggaren/deploy/backup/age.recipient
#    expect: 755 root:root   then   444 root:root
# The expected value is written out here ON PURPOSE, and it is kept honest by CI rather than by
# anyone remembering to edit it: BackupUnitFilePinTests fails the build if this literal and
# deploy/backup/age.recipient ever disagree. A reference that lives inside the clone — including
# `git show HEAD:…` — is one the box itself controls, and the attacker this check exists for is
# the one who already has the box. It is also blind to the likelier failure: no script in
# deploy/systemd/ runs git at all (measured 2026-08-12), so the clone moves only on a manual
# pull, and a box that has not pulled holds a stale recipient AND a stale HEAD.
grep -qx age17xdg97ppkkpv5cl0qlsfctmkrdy7dt6ps0klt79evwcwsnz0j35sn3skut /opt/jobbliggaren/deploy/backup/age.recipient && echo RECIPIENT-OK || echo RECIPIENT-MISMATCH   # silence is not a result

# 3b. AFTER A ROTATION, THE BOX IS A HOME TOO — and this block is titled "install (once)", which
#     is exactly why the rotation of 2026-08-12 would otherwise have left the box holding the
#     REVOKED recipient with nothing saying so. Run these three, in order, after the rotation
#     commit has merged:
#     sudo on ALL THREE, matching :218 and vps-deploy-stack.md's own pull. Mixing privilege
#     mid-sequence makes which line fails depend on how the clone was created — root-owned and
#     line 1 dies on dubious ownership, user-owned and the sudo pull writes root objects into a
#     user .git/ — and an operator improvising mid-rotation is how the box stays on the revoked
#     recipient, which is the thing 3b exists to prevent.
sudo git -C /opt/jobbliggaren fetch origin            # read what the pull brings FIRST — on this
sudo git -C /opt/jobbliggaren log --oneline HEAD..origin/main -- deploy/   # box a pull is a DEPLOY
sudo git -C /opt/jobbliggaren pull --ff-only
#     The pull RECREATES age.recipient, and git carries only the exec bit — so step 3's
#     0444 root:root does not survive it. Re-apply the chown/chmod above, then re-run the
#     RECIPIENT-OK line. A rotation that stops at the merge leaves the box one manual pull away
#     from encrypting to a key nobody holds.
#
#     THE OTHER HOMES A ROTATION TOUCHES, because there is no separate rotation procedure and
#     the first rotation missed one: deploy/backup/age.recipient (the value), this runbook's
#     literal on the RECIPIENT-OK line (CI enforces the two agree — BackupUnitFilePinTests),
#     age.recipient.example's provenance note, ADR 0125 and the Art. 30 register (both
#     gitignored — mark the retired identity REVOKED there rather than merely replacing it,
#     since the old value stays reachable from git history), the box per 3b, and
#     vps-deploy-stack.md row 32.

# 4. The units.
sudo install -m 0644 /opt/jobbliggaren/deploy/systemd/jobbliggaren-backup*.{service,timer} \
  /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now jobbliggaren-backup.timer jobbliggaren-backup-fresh.timer
```

The upload credential is injected by the same script as the crypto secrets, so it is one prompt
in an existing procedure rather than a new one:

```bash
sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh
```

It wants **the base64 of a complete rclone config file**, not the config itself — one value that
carries the whole target, which is what keeps a vendor change to one constant and one secret:

```bash
base64 -w0 < rclone.conf        # produce it wherever you configured rclone, then paste
```

---

## 3. After every reboot

`/run` is tmpfs, so the upload credential dies with the box exactly as the master key does.
Re-inject with the command above; there is nothing backup-specific to remember.

> ⚠ **Injection is what ARMS hourly `rclone` execution, so it is the trigger for the advisory
> check — read `vps-deploy-stack.md` row 28a before injecting** ([#1289](https://github.com/klasolsson81/jobbliggaren/issues/1289)). Measured 2026-08-30: the logship
> timers were `enabled` and firing hourly, with execution held off only by this credential's
> absence. That row also names the one reading that is only available once the file exists —
> whether the injected `rclone.conf` sets `session_token` or `sse_customer_*`, which decides
> three otherwise-conditional CVEs. This is a pointer, not a re-measurement: if row 28a is
> current, the advisory axis costs nothing extra here.
>
> ⚠ **This callout covers the ADVISORY axis only.** It is not a clearance to inject: the Art. 28
> processor agreement with OVHcloud is unsigned (`security-auditor` escalation, 2026-08-30), and
> injection is the event that makes the processing operative. Klas owns that sequencing decision.

**What happens in the meantime, precisely, because the two cases differ.** A *scheduled* run with
no credential is **skipped**, not failed: `jobbliggaren-backup.service` carries
`ConditionPathExists=` on the credential, so systemd marks the unit inactive and logs the reason.
That is deliberate — the timer is `Persistent=true`, so a boot-time catch-up would otherwise
**latch** the unit in `systemctl --failed` until the next 02:15, even after an operator injected,
while the freshness probe simultaneously reported the backup fresh. An alarm that is lit for a
condition that no longer exists trains an operator to stop reading the only alarm surface there
is. A run started **by hand** with no credential still refuses loudly (exit 2), and a genuinely
missing backup is caught by the 26-hour freshness threshold rather than by the scheduled run.
Nothing is unreported **once `jobbliggaren-host-secrets-present.timer` is enabled** — it runs
`--check-host` hourly, and that predicate reads exactly this file.

**That caveat is load-bearing, and an earlier wording dropped it (#1329).** The sentence named
`jobbliggaren-secrets-present.service` until the split, and the objection to it was that on a box
without this credential nobody could enable that unit. **The same is true of the host unit, by
construction:** its enable is gated on exactly the file it alarms about (`master-key-ops.md` §2),
so it can only be armed in a state where it has nothing to report. What it therefore covers is
**loss after provisioning** — the credential existed, the box rebooted, nobody re-injected, which
is precisely this section's scenario — and never **absence before provisioning**, which is the
box's state today and which `log-sink.md` §2 owns as a named open window.

**So what #1329 bought here is not this cover but its price.** In the reboot case the pre-split
unit was already enabled and already alarming on the named file, so that half held before the
split too. What did not hold was having to buy it by keeping the crypto alarm down: that alarm now
arms independently of #197.

Verify — and **`--check-host` is the line that answers for this section**, since the split is
exactly what took the credential out of `--check`:

```bash
sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh --check-host  # the credential
sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh --check       # the box serves
sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-backup.sh --check               # the freshness
```

A green `--check` says nothing about the upload credential and never will again — that is the
whole of #1329, and reading it as this section's verification is the mistake the split makes
possible. `--check-host` is the only one of the three that reads the file you just injected.

---

## 4. Running it, and reading the alarm

```bash
sudo systemctl start jobbliggaren-backup.service     # a run by hand, e.g. before a rotation
journalctl -u jobbliggaren-backup.service -n 50
systemctl list-timers 'jobbliggaren-backup*'
```

**The alarm surface is `systemctl --failed`, and there are two units on it for two different
failures.** `jobbliggaren-backup.service` failing means last night's run broke.
`jobbliggaren-backup-fresh.service` failing means the backup stopped happening *at all* — a
masked timer, a stopped unit, a clock that never reached 02:15. The second is the one no failure
list would otherwise show, because a unit that is never triggered is not failed.

**Weekly operator check** (there is still no log sink — #1175. The *cadenced reader* half of this
parenthetical was answered by #1201: `jobbliggaren-heartbeat` reads `systemctl --failed` every
fifteen minutes and pages, **once installed** — see [`host-detection.md`](./host-detection.md).
The check below stays worth doing regardless, because it inspects the remote, which no predicate
on the box does):

```bash
systemctl --failed
sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-backup.sh --check
rclone --config <cfg> lsl jbl-backup:jobbliggaren-backups/main | tail -5   # newest artefacts
rclone --config <cfg> cat jbl-backup:jobbliggaren-backups/deks/verified.stamp
```

**Read those two together, not separately.** The DEK stamp must be at least as recent as the
newest main artefact. If it is behind, some run's DEK leg failed after its main artefact
uploaded, and the newest main artefact is unusable until the next successful run repairs the
pairing — `deks/` is the half a `main`-only listing cannot see.

### Retention

**30 days (K4, Klas 2026-08-04), enforced by the target and not by this box.** The box issues no
delete verb at all; it appends. Two reasons, and the second is the stronger: a provider-enforced
lifecycle policy is an artefact a third party issued and that we can export, which is what
Art. 5(2) accountability asks for, whereas a prune journal is a document we write about ourselves,
and its integrity depends on the very box that would be compromised in the scenario that matters.
The credential is *supposed* to carry no `DELETE` so that the worst a compromised box can do is
add; **measured 2026-08-09 it does carry it**, and the callout below is the current state rather
than this paragraph.

**`deks/` outlives `main/`, and that ordering is the invariant.** Two rules on the target:
`k4-main-artefacts-30-days` (`main/`, 30 days) and `deks-outlive-main-90-days` (`deks/`, 90 days),
both applied and read back 2026-08-09. **Never "no rule on `deks/`", and never an equal or shorter
one.** Both prefixes are written in the same run, so a longer key expiry means a main artefact that
is still alive implies its key generation still exists - by construction rather than by scheduling
luck. Precisely: the ordering holds while the DEK leg has not failed for 60 consecutive days
(90 - 30). Longer than that is an alarm nobody read, not a retention question. And `deks/` does need a bound: `user_data_keys` carries `JobSeekerId` and `CreatedAt`, which
is pseudonymous personal data (Art. 4(1), Recital 26), so an object with no expiry at all becomes
retention without purpose the moment the job stops running for good (Art. 5(1)(e)). While the job
runs, the objects are overwritten nightly and approach neither number.

The one object the box replaces is the DEK generation, and it does that by **overwrite after
verification**, never by deleting: it uploads to `deks/staged.dump.age`, reads it back, compares
the sha256 against what it sent, and only then writes the same verified bytes to
`deks/verified.dump.age`. If the comparison fails nothing is promoted and the previous verified
generation is untouched — which matters, because without a DEK generation **every** retained main
artefact is unreadable.

**The target is CHOSEN and MEASURED, 2026-08-09 — the Art. 28 DPA is NOT signed (§7).** OVHcloud Object Storage, container
`jobbliggaren-backups`, region **`eu-west-par`** (Paris), endpoint
`https://s3.eu-west-par.io.cloud.ovh.net`. Measured against the live container the same day, not
read off an order form: `get-bucket-location` -> `eu-west-par` (EU) - versioning **not enabled**
- Object Lock **not enabled**. The last two are Klas's decision, and they are the simpler and
strictly stronger branch for the one-generation property, since an unversioned overwrite genuinely
replaces. The stated cost: Object Lock is a creation-time property and is therefore closed on this
container permanently.

**K4 is now enforced by the provider rather than promised by us.** Lifecycle rule
`k4-main-artefacts-30-days` (`Expiration: 30 days`), applied and read back 2026-08-09.
**It is scoped to `main/` on purpose, and the scope is load-bearing:** a time expiry over `deks/`
would delete the key generation 30 days after its last write, so a pause in the nightly job would
expire the KEYS while 29-day-old main artefacts survived, leaving those permanently unreadable -
the silent-data-loss shape this whole design exists to avoid. The DEK artefacts need no time rule
at all: "exactly one generation" is achieved by overwrite.

> **THE UPLOAD CREDENTIAL CAN DELETE, AND NOTHING HAS YET TAKEN THAT AWAY.**
> Measured 2026-08-09: `delete-object` with the box's credential **succeeded**. ADR 0125 Decision
> 3 binds a credential *without* `DELETE` - that is the entire ransomware posture, and it is the
> property that chose OVH over Hetzner Storage Box in the first place. Two further measurements
> say why a policy cannot repair it here: `get-bucket-policy` returns **`NotImplemented`** on OVH,
> and `get-bucket-acl` shows the backup user **owns** the container with `FULL_CONTROL` - and
> OVH's documented evaluation authorises an owner even with no explicit allow.
>
> **THE REPAIR IS A USER POLICY, AND AN EARLIER DRAFT OF THIS CALLOUT GOT THAT WRONG.** It said
> the situation was irreparable by policy, reasoning from `NotImplemented` on *bucket* policies to
> "no instrument exists". OVH's other instrument is a **user policy**, attached to the S3 user and
> therefore invisible to `get-bucket-policy` by construction. Its documented evaluation checks for
> an **explicit** `Deny` first, before any ACL fallback; the owner exemption defeats only
> **implicit** deny. An explicit `Deny` on `s3:DeleteObject` is therefore honoured even for the
> owner.
>
> It is compatible with the mechanism, measured rather than assumed: this script issues **no delete
> verb at all** - the DEK promotion is an overwrite, i.e. `PutObject`. Nothing breaks.
>
> **Klas owns the next step, and it is a measurement before a plan:** apply the explicit `Deny`
> (OVH panel, Object Storage, Policy Users, import JSON), then counter-measure - `put-object` must
> still succeed and `delete-object` must return `AccessDenied`. Re-creating the container under a
> different owner is *a* repair and the expensive one; it is not needed if the policy holds.
> **Neither has been applied**, so D5's posture is documented and not in force. It blocks neither
> taking backups nor the drill.

---

## 5. Restore

> **Read the §0 note first: it says which half of this procedure is executed and which is not.**

**What you need, and where.** The two artefacts may be fetched anywhere — they are ciphertext.
The **decryption must happen on the machine holding the age private key, and that machine is
never this box.** For a drill before real data exists, that machine is the operator workstation;
once real data exists that choice is open and is security-auditor's to settle (§8).

> **STEP 0, AND IT IS NOT OPTIONAL: CHECK THE PAIRING BEFORE YOU FETCH ANYTHING.** The DEK
> artefact must never be older than the main artefact you pair it with. That is normally true by
> construction — the nightly run dumps main first and promotes the DEK generation second — but it
> is **false after any run whose DEK leg failed after the main artefact uploaded**: tonight's main
> then sits offsite beside last night's DEK generation, both objects present, both decrypting,
> and the pair looks fine. Every user created since that generation would restore with no key.
>
> The mechanism publishes the DEK generation's own run stamp for exactly this comparison, in
> plaintext so it can be read without the private key:
>
> ```bash
> rclone --config <cfg> cat jbl-backup:jobbliggaren-backups/deks/verified.stamp
> # -> e.g. 20260809T021500Z
> ```
>
> **The value it prints must be greater than or equal to the `<STAMP>` in the main artefact's own
> file name.** If it is not, do not use that main artefact: pick an older one whose stamp the DEK
> generation covers, or run `systemctl start jobbliggaren-backup.service` and use tonight's pair.
>
> **If the command prints NOTHING, the stamp does not exist — and that is a REFUSAL, not an
> unknown state.** No run has ever published a pairing stamp, or the one run that promoted a
> generation failed on the stamp upload afterwards. Either way the pairing cannot be checked, so
> it must not be assumed: run `systemctl start jobbliggaren-backup.service` and use the pair that
> run produces. This is the same rule the rest of this stack is built on — a tool's or a value's
> absence must never read as its verdict — and it is written here because it is the one branch of
> step 0 that would otherwise fail open.
>
> **What step 0 is and is not.** It is a consistency check against operational error, not a
> tamper control. The stamp is the only input to this step carrying no cryptographic integrity —
> everything else in §5 is age-framed — so anyone holding the upload credential could move it
> forward. That widens nothing (the same credential could upload a wrong generation outright),
> but do not read a plaintext, credential-writable object as an authority.

```bash
# 1. Fetch. Any main artefact whose stamp is <= the DEK generation's stamp (step 0), and the
#    CURRENT DEK artefact.
rclone --config <cfg> copy jbl-backup:jobbliggaren-backups/main/jobbliggaren-<STAMP>.dump.age .
rclone --config <cfg> copy jbl-backup:jobbliggaren-backups/deks/verified.dump.age .

# 2. Decrypt, on the key-holding machine.
age -d -i <identity-file> jobbliggaren-<STAMP>.dump.age > main.dump
age -d -i <identity-file> verified.dump.age            > deks.dump
```

> **NEVER PAIR AN OLDER DEK ARTEFACT WITH A NEWER MAIN ARTEFACT.** The invariant runs one way:
> the DEK artefact must be at least as new as the main one. Reversed, a user who registered
> between the two dumps lands in the restore with no key, and their fields are permanently
> unreadable — silent data loss wearing erasure's clothes. There is exactly one current DEK
> artefact for this reason. If `deks/verified.dump.age` fails to decrypt, use
> `deks/staged.dump.age`: it is the last upload that passed its round trip, and the two differ
> only when a promotion was interrupted.

```bash
# 3. Restore the main artefact into a FRESH database. Never into the live one.
createdb -U postgres jobbliggaren_restore
pg_restore -U postgres -d jobbliggaren_restore --no-owner --no-privileges main.dump

# 4. Load the DEKs through a staging table. THE STAGING TABLE IS NOT OPTIONAL.
#    user_data_keys carries fk_user_data_keys_job_seekers (ON DELETE CASCADE, added in raw SQL by
#    20260518145927_AddUserDataKeys). A DEK row whose owner is absent from THIS generation would
#    abort the whole COPY on that constraint — and that is not an edge case, it is precisely the
#    cross-generation restore this design exists to make possible.
psql -U postgres -d jobbliggaren_restore -v ON_ERROR_STOP=1 \
  -c 'CREATE TABLE _dek_restore (LIKE user_data_keys);'

#    Convert the custom-format dump to SQL and redirect the COPY at the staging table.
#
#    TWO THINGS HERE WERE MEASURED WRONG AND BOTH FAILED SILENTLY (code-reviewer, 2026-08-09,
#    reproduced in a throwaway postgres:18.3):
#
#    * THE TARGET MUST BE SCHEMA-QUALIFIED. pg_restore emits
#      `SELECT pg_catalog.set_config('search_path', '', false);` at the top of its output, so an
#      unqualified `_dek_restore` resolves to nothing: `ERROR: relation "_dek_restore" does not
#      exist`, zero rows loaded.
#    * psql MUST RUN WITH -v ON_ERROR_STOP=1. Without it psql prints that error and still exits
#      0, so the failure is invisible to anything checking the exit code.
#
#    Together they produced the worst possible outcome for this procedure: a restore that loaded
#    no keys at all, while evidence count (b) below reported EVERY user as having no key —
#    i.e. a broken restore presenting itself as a flawless crypto-erasure result, and that number
#    is what gate M-4 records. The grep checks below verify the SUBSTITUTION, not the load; they
#    passed throughout.
pg_restore -f - deks.dump | sed 's/^COPY public\.user_data_keys /COPY public._dek_restore /' > deks.sql
grep -c '^COPY public\._dek_restore ' deks.sql   # expect exactly 1
grep -c '^COPY public\.user_data_keys ' deks.sql # expect exactly 0
psql -U postgres -d jobbliggaren_restore -v ON_ERROR_STOP=1 -f deks.sql

#    AND VERIFY THE LOAD ITSELF, because the two checks above cannot. A staging table that is
#    empty here means the restore has loaded no keys, and every count below would then be
#    measuring that fact rather than an erasure.
psql -U postgres -d jobbliggaren_restore -tAc 'SELECT count(*) FROM _dek_restore'
#    -> must be > 0 on any generation that had users. Zero here means STOP.

# 5. Insert the rows that belong to a user this generation actually has.
#
#    -v ON_ERROR_STOP=1 IS LOAD-BEARING HERE AND IT WAS MISSING UNTIL 2026-08-09. This one
#    invocation carries the INSERT *and* all three evidence queries. Without the flag, an INSERT
#    that hits fk_user_data_keys_job_seekers prints its error, psql CONTINUES INTO THE EVIDENCE
#    QUERIES, and exits 0 — so (a), (b) and (b2) are computed against a user_data_keys the INSERT
#    never populated, i.e. every restored user reported keyless. That is step 4's callout one step
#    later, and it is the number gate M-4 records. Measured in a throwaway postgres:18: script-fed
#    without the flag -> error printed, SELECTs still run, exit 0; with it -> exit 3, SELECTs never
#    run. (Note that the flag changes nothing for a single-statement `psql -c`, which fails loudly
#    either way — the shape is what makes it matter.)
psql -U postgres -d jobbliggaren_restore -v ON_ERROR_STOP=1 <<'SQL'
INSERT INTO user_data_keys
SELECT * FROM _dek_restore
WHERE job_seeker_id IN (SELECT id FROM job_seekers);

-- THE TWO COUNTS BELOW ARE THE EVIDENCE, not diagnostics. Record both.
-- (a) DEK rows dropped as belonging to nobody in this generation: users who registered after
--     the main artefact was taken. Expected to be non-zero on any cross-generation restore.
SELECT count(*) AS deks_dropped_as_orphans
FROM _dek_restore d WHERE d.job_seeker_id NOT IN (SELECT id FROM job_seekers);

-- (b) Restored users with NO key. READ THIS CAREFULLY: it is NOT the crypto-erasure count on
--     its own, and calling it that would overstate the result. DEK rows are created LAZILY —
--     a user gets one on their first request carrying IRequiresFieldEncryptionKey, which is the
--     same trigger (b2) describes below and is NOT the same as writing: many carriers are
--     read-only queries, so merely opening an application or a CV mints one. A user who has
--     never made such a request has no key and never did. This number is therefore
--     (users erased since the main artefact) PLUS (users who never triggered a key), and only
--     the first group is what the drill is measuring.
--     (code-reviewer, 2026-08-09: RegisterCommand does not carry IRequiresFieldEncryptionKey,
--     so the prefetch that would create one eagerly does not run at registration.)
SELECT count(*) AS users_without_a_key_TOTAL
FROM job_seekers j WHERE j.id NOT IN (SELECT job_seeker_id FROM user_data_keys);

-- (b2) The erasure signature: restored users who have ciphertext but no key. Ciphertext without
--      a key is what an erased user looks like; no ciphertext and no key is simply a user who
--      never wrote any.
--      (b2) IS AN ERASURE COUNT ONLY IF STEP 0 PASSED, and that is a precondition rather than a
--      caveat. Under a REVERSED pairing — a DEK artefact older than the main one, which is what a
--      run whose DEK leg failed after its main artefact uploaded leaves behind — every
--      user whose FIRST KEY-CREATING REQUEST fell between the two generations, and who has
--      cover_letter ciphertext here, has ciphertext and no key too. That is byte-identical to
--      what this query counts. Two likelier readings of that trigger are both wrong. It is not
--      registration: no encrypted column sits on job_seekers, so a merely-registered user has no
--      ciphertext and is counted by (b) instead — the very distinction (b2) exists to draw. Nor
--      is it writing: the DEK row is minted by FieldEncryptionKeyPrefetchBehavior on the first
--      request carrying IRequiresFieldEncryptionKey, and many of those are READ-ONLY queries, so
--      merely opening an application or a CV creates one.
--      So a (b2) recorded without step 0 may be
--      measuring silent data loss and reporting it as a successful Art. 17 erasure. The drill
--      measures exactly this ambiguity (`BackupRestoreDrillTests`, the reversed-pairing
--      counterfactual); the operator's protection is step 0, not this query.
--      SCOPE, STATED RATHER THAN IMPLIED: the EXISTS below inspects `applications.cover_letter`
--      alone. A user whose only ciphertext was a note, a follow-up, `resume_versions.content_enc`
--      or `parsed_resumes` is invisible to it. That is safe for what the drill needs — one
--      confirmed case proves the mechanism, and a zero is a prompt to investigate rather than a
--      pass — but it is an EXISTENTIAL proof over one column, not a census. Widen the EXISTS if
--      you ever need the count itself to be complete. (security-auditor, 2026-08-09.)
SELECT count(*) AS users_with_ciphertext_but_no_key
FROM job_seekers j
WHERE j.id NOT IN (SELECT job_seeker_id FROM user_data_keys)
  AND EXISTS (
    SELECT 1 FROM applications a
    WHERE a.job_seeker_id = j.id AND a.cover_letter LIKE 'v1:%'
  );
--      `LIKE 'v1:%'` and not `<> ''`: the first tests for CIPHERTEXT, the second only for a
--      non-empty value, and this count's whole point is the presence of encrypted content. The
--      pattern is production's own — `FieldEncryptionSentinel.SqlLikePattern`
--      (`src/Jobbliggaren.Application/Common/Security/FieldEncryptionSentinel.cs:43`), the same
--      constant `FieldEncryptionBackfiller` uses as its SSOT — restated here because this is a
--      runbook an operator types, not code that can reference it.
SQL

psql -U postgres -d jobbliggaren_restore -c 'DROP TABLE _dek_restore;'

# 6. ANALYZE. A pg_dump restore carries no optimizer statistics (omitted unless --statistics, and
#    this one does not pass it), so at t=0 the first queries plan against nothing. This is a step,
#    not a footnote — step 7 boots the application immediately, so there is no window to wait in.
#
#    ⚠ Autoanalyze DOES re-arm on the restore's own DML — measured ~60 s for one 1,07M-row table
#    (scb-live-population.md §8 reason 2). That is why this step survives HERE (a throwaway drill
#    database, booted at once, and a DATABASE-WIDE ANALYZE, which a one-table measurement cannot
#    stand in for) and is deliberately NOT reflexive on the box, where §8 reason 2 retired it.
#
#    ⚠ Do NOT attribute #560 to a restore. That zero-statistics state came from an ~11 h POPULATION
#    run, and the canonical home refuses the mechanism outright: ScbCompanyRegisterStore.AnalyzeAsync
#    records "why autoanalyze never fired through the ~11 h population is NOT established" and calls
#    the emptiness "an observation, not evidence". An earlier version of this comment said the
#    restore case "is how" #560 happened, which imported a causal claim nobody has established.
psql -U postgres -d jobbliggaren_restore -c 'ANALYZE;'

# 7. Boot the application against the restored database and READ AN ENCRYPTED FIELD through it.
#    A health probe decrypts nothing, so it cannot tell you the envelope survived.
#    ConnectionStrings__Postgres override, then open an application with a cover letter.
#
#    RUN THIS STEP AFTER STEP 5, NEVER BEFORE, AND NEVER RE-RUN THE COUNTS AFTERWARDS.
#    IUserDataKeyStore.GetOrCreateDataKeyAsync WRITES: reaching a keyless user through the
#    application MINTS a fresh key row in the restored database. Re-running (b)/(b2) after this
#    step therefore returns a lower, quieter number than the one that is the evidence, and an
#    operator could read that as "no erasure is visible". Record the counts from step 5 and treat
#    them as final.
#
#    THE RESTORED CLUSTER HAS NO APPLICATION ROLES, and this step is where that surfaces.
#    pg_restore ran with --no-privileges and the target is a cluster an operator just created, so
#    `jobbliggaren_app` does not exist and no grants arrived. Connect as `postgres`, or run
#    Jobbliggaren.Migrate's Phase A against jobbliggaren_restore first if the boot must use the
#    application role. Following this step literally with an app-role connection string fails with
#    `role "jobbliggaren_app" does not exist` or 42501 — the #1229/#1232 class, at the last step of
#    a real restore.
```

### 8. Reconciliation after a restore — mandatory, not optional

A restored generation is a snapshot of a moment, and deletions that happened after that moment
are not in it. Two different states, and only one of them self-heals:

- **Users soft-deleted before the artefact was taken** carry `deleted_at` into the restore, and
  `HardDeleteAccountsJob` erases them again on its next 04:00 UTC run. Nothing to do but let it
  run, and confirm it did.
- **Users whose deletion request came *after* the artefact was taken** are restored as live, and
  the request is gone. Nothing in the restored data records it. If a restore is ever performed
  on real data, the deletion requests received since the artefact's timestamp must be
  reconstructed from outside the database and re-applied — and if they cannot be, that is a
  personal-data incident to be assessed, not a footnote.

Do not promote a restored database to live until both have been addressed.

---

## 6. The drill (gate M-4)

**A backup is a hypothesis until a restore has run.** The drill is what closes M-4, and it has
two halves that prove different things:

- **The CI half — DELIVERED 2026-08-09 (#197 PR-2).**
  `tests/Jobbliggaren.Worker.IntegrationTests/Backup/BackupRestoreDrillTests.cs` proves the
  *semantics* against a real Postgres: seed users through production entry points, dump,
  hard-delete one through `IAccountHardDeleter`, restore into a **second cluster**, and assert
  that the erased user has no key while the other decrypts. It runs on every build and needs no
  box. Two containers rather than two databases because Postgres roles are cluster-global, so a
  single-cluster drill could never fail for a missing production role — measured: dropping
  `--no-owner --no-privileges` from **step 3's `pg_restore`** fails with
  `role "jobbliggaren_migrations" does not exist`, while dropping the same flags from the
  mechanism's `pg_dump` changes nothing, because step 3 strips ownership either way.
- **The ops half** is this runbook's §5, executed end to end against a **real artefact from the
  real target**, on the real schema. It is what proves the units, the credential, the recipient,
  the retention layout and the decryption path — none of which CI can see.

**Run the ops half before first real data**, and again after any change to the target, the
recipient, or the master key. Record each run in `vps-deploy-stack.md` §5 with the date and the
counts from step 5 — **(b2), not (b)**, is the one that carries the erasure claim; a row without
a date is a claim that cannot be told from one that has decayed.

---

## 7. What this runbook does not own

- **The master key and the peppers** — `master-key-ops.md`. One overlap, and it is load-bearing:
  a master-key rotation re-wraps every stored DEK, so **every DEK artefact taken before the
  rotation is unreadable the moment the retiring key is destroyed.** The rotation procedure
  therefore takes a fresh backup and verifies it offsite *before* that step. Main artefacts are
  unaffected — they carry no keys — which is why this design loses nothing at a rotation while a
  single-full-dump design would lose its entire 30-day window once a year.
- **The choice of target and the Art. 28 DPA with its operator** — Klas, per ADR 0050
  `Amendment 2026-08-04` §7. The requirement profile is bound (EU, S3-compatible with server-side
  lifecycle, a credential that can exclude `DELETE`, a provider and account distinct from
  Netcup); the contract is not.
- **Escrow of the age private key** — Klas, see §1.
- **The deploy stack, the box's hardening, and TLS** — `vps-deploy-stack.md`,
  `vps-base-hardening.md`, #196.
- **Reading `systemctl --failed` on a cadence** — **#1201 owns this since 2026-08-10**, not #1175.
  `jobbliggaren-heartbeat` reads the list every fifteen minutes and pages, and its silence is
  itself an alarm ([`host-detection.md`](./host-detection.md)). Discharged when that runbook's §7
  rows carry measurements against this box, not when the mechanism merged. **#1175 still owns the
  log sink**, which is a different thing and is still unbuilt.

---

## 8. Unmeasured, and named

1. **CLOSED 2026-08-10 — whether `age` and `rclone` are in apt on Debian 13 (trixie).** Both are:
   `age` candidate `1.2.1-1+b5` and `rclone` candidate `1.60.1+dfsg-4`, both from `trixie/main`,
   installed on this box and recorded from the binaries in `vps-deploy-stack.md` §5 row 28. The
   STOPP branch this entry was written for — `sops` was measured absent from trixie for #198, so
   the class was live rather than hypothetical — did not fire. *(Kept as a closed entry rather
   than deleted, the same treatment items 2 and 6 got in this file and for the same reason.)*
2. **CLOSED 2026-08-09 — the target's lifecycle and immutability behaviour.** Object Lock is set
   at bucket creation and cannot be enabled afterwards, and enabling it enables versioning
   (OVHcloud's Object Lock guide, read 2026-08-09 and quoted in ADR 0125's amendment). So
   "permanently closed on this container" is a measurement with a source, and ADR 0125 Decision
   §3's Object Lock rider is superseded on this provider — the two riders were mutually exclusive
   here. The recommendation that premise supported is moot: the container exists. *(Kept as a
   closed entry rather than deleted, so a reader who came for this question finds the answer
   instead of its absence.)*
3. **Whether `rclone` can report a usable checksum for a streamed object on the chosen target.**
   The DEK promotion compares a full read-back rather than a remote hash precisely because this
   is unmeasured; if the read-back proves expensive at scale, that is when to measure it.
4. **The dump's cost on this box.** `pg_dump` runs inside the Postgres container's 2 560 MiB cap,
   and the peak recorded in `vps-deploy-stack.md` §5 row 3 reflects idle operation because this
   job did not exist. That row is unblocked by this change and is #1235's to close.
5. **Whether the target's lifecycle rules take EFFECT, not merely exist.** The *rule* half is
   measured (row 27c): versioning is off, so an overwrite genuinely replaces, and both prefixes
   carry a rule with `deks/` outliving `main/`. **The effect half is not**: nothing has yet
   confirmed that objects actually disappear on schedule. Row 27b owns it, and **only the `main/`
   half is measurable**: a plain `main/` listing after 31 days showing the older artefacts gone.
   Main objects carry a per-run stamp in their names, so they accumulate and only the 30-day rule
   removes them — that listing can fail. **Not `list-object-versions`:** with versioning off
   (item 6) it returns one `null`-id version per key whatever the lifecycle does. **And not a
   `deks/` listing either** ([#1292](https://github.com/klasolsson81/jobbliggaren/issues/1292)):
   three constant names overwritten nightly hold one generation by construction, and the nightly
   overwrite resets `LastModified`, so the 90-day rule never fires while the job runs. That rule
   is dormant by design — row 27c wants it for the state after the job stops for good — but no
   instrument observes it on a running system.
   *(Split from a single premise that read as wholly unmeasured once its first half was closed;
   a discharged premise loitering in an unmeasured list makes the whole list less credible.)*
6. **CLOSED 2026-08-09 — whether the target versions the `deks/` prefix.** It does not:
   `get-bucket-versioning` returns empty, so an overwrite genuinely replaces and the whole
   noncurrent-version class does not arise (row 27c). *(Kept as a closed entry rather than
   deleted and its number reused — the same treatment item 2 got in this file, and for the same
   reason: a reader who came for this question should meet the answer, not its absence. The
   "is a stopped job ever noticed" question that briefly occupied this slot is not an unmeasured
   premise at all — it is measured and owned by #1175, and §7 already says so.)*
7. **A failed run can leave a truncated object offsite, and the box cannot remove it.** If `age`
   or `pg_dump` dies mid-stream, `rclone` has already opened the destination and writes what it
   received. The run fails loudly, and age is an authenticated format so a restore from that
   object fails at decrypt rather than yielding partial data — but the object sits under a
   legitimate-looking run-stamped name until the lifecycle expires it. **Do not read a main
   artefact's existence as evidence it is complete;** the journal for that run is the evidence.
8. **Where decryption happens once real data exists.** The workstation is inside the trust
   boundary (ADR 0123). Acceptable for a drill on an empty box; open beyond that, and
   security-auditor's to settle.
