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

> **ESCROW IS A HARD PREREQUISITE TO CUTOVER, AND IT IS UNDECIDED AS OF 2026-08-09.** With no
> at-rest copy, an off-box escrow is the *only* recovery path: an operator who loses these
> values destroys every encrypted field and every pseudonymised lookup irreversibly. The
> senior-cto-advisor escalated the decision to Klas and bound it as a hard prerequisite — it is
> a risk acceptance, which CLAUDE.md §9.6 makes Klas's to grant and never a session's to claim.
> **Do not cut over until it is decided.** An earlier draft of this runbook stated the escrow as
> delivered fact; it was not, and stating it that way would have let the cutover proceed past an
> open gate. When it is decided, record the date here and fill in the escrow row in
> `vps-deploy-stack.md` §5.

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

---

## 2. Files and units

| Path | What |
|---|---|
| `/run/jobbliggaren/secrets/` | tmpfs staging dir. **`0700 root:root` at boot** (`/etc/tmpfiles.d/jobbliggaren.conf`), **raised to `0710 root:<container-gid>` by the injection script**. Two actors, two states — and with the `:` prefix tmpfiles can never produce the second one, so a `WRONG MODE` from `--check` means the injection has not run |
| `…/FieldEncryption__LocalMasterKeyBase64` | the master key, `0400` |
| `…/FieldEncryption__LocalMasterKeyId` | key identity, not a secret — the rotation marker |
| `…/AuditPseudonymization__PepperBase64` | pepper |
| `…/CompanyWatchPseudonymization__PepperBase64` | pepper |
| `…/CvReviewFingerprintPseudonymization__PepperBase64` | pepper |
| `/run/app-secrets` | the same directory as api and worker see it (read-only bind mount) |
| `jobbliggaren-inject-secrets.sh` | injection (interactive) and `--check` (the absence detector) |
| `jobbliggaren-secrets-present.{service,timer}` | runs `--check` at boot + every 10 min |

**File names are .NET configuration keys** with `__` as the section delimiter. That is the
contract, not an implementation detail: it is what `AddKeyPerFile` expects, so the reader can
be swapped for the first-party package later without touching this box.

### Install (once)

```bash
sudo install -m 0644 /opt/jobbliggaren/deploy/systemd/jobbliggaren-tmpfiles.conf \
  /etc/tmpfiles.d/jobbliggaren.conf
sudo systemd-tmpfiles --create /etc/tmpfiles.d/jobbliggaren.conf
sudo install -m 0644 /opt/jobbliggaren/deploy/systemd/jobbliggaren-secrets-present.service \
  /opt/jobbliggaren/deploy/systemd/jobbliggaren-secrets-present.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now jobbliggaren-secrets-present.timer
```

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
4. Prompts for each secret with `read -rs`, validates that the master key decodes to 32 bytes,
   and creates each file `0400` owned by the measured uid.

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
sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh --check
docker inspect -f '{{.State.Health.Status}}' jobbliggaren-api    # expect: healthy
```

api and worker recover on their own restart backoff (`restart: unless-stopped`). **No
`compose up` and no reconcile run is needed**, and neither should be used: a hand-typed
`docker compose up -d` takes no lock and runs no attestation
(`jobbliggaren-reconcile.sh` header).

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
docker compose -f /opt/jobbliggaren/deploy/docker-compose.yml run --rm   -e REWRAP_RETIRING_MASTER_KEY_FILE=/run/app-secrets/OLD_KEY   -e REWRAP_INCOMING_MASTER_KEY_FILE=/run/app-secrets/FieldEncryption__LocalMasterKeyBase64   -e REWRAP_RETIRING_KEY_ID=local-v1   -e REWRAP_INCOMING_KEY_ID=local-v2   migrate rewrap-master-key
```

Every value above is a **path**, never a secret — `MigrateEnv` resolves the `_FILE` suffix, the
same convention api and worker use. The `migrate` service carries the secrets mount for exactly
this run; `schema` mode needs no crypto material and receives none.

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
3. Remove the old pair and inject the new one. The identity and the bytes are written together
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
4. Rewrap old → new (the command above), using the retiring key written to a temporary file on
   the same tmpfs. **Skip entirely when `user_data_keys` is empty** — there is nothing to
   re-wrap and the new bytes are already in force; the tool would report a no-op anyway.
   Remove the temporary retiring-key file afterwards.
5. Start the containers; confirm `healthy`.
6. `sudo systemctl start jobbliggaren-secrets-present.timer` if it was stopped, and re-arm the
   reconcile timer.

---

## 5. Recovery, and the one way to lose everything

**Losing a value destroys what it protects, irreversibly** — and that is true of all four, not
only the master key. The master key: every encrypted field. The company-watch pepper: every
stored organisation-number token, because the backfill destroyed the plaintext in place. The
CV-fingerprint pepper: every Ignored/Resolved finding decision reverts to Open. (The audit
pepper is the exception — nothing reads back against it.)

With no at-rest copy, an **off-box escrow is the only recovery path**, and per §1 it is a
decision Klas has not yet made. Crypto-erasure is the design (ADR 0049 Beslut 2) — the same
property that makes an account deletion final makes a lost key final.

If an escrow copy exists: inject it (§3). If it does not, there is nothing to recover and no
procedure here will help.

---

## 6. What this runbook does not own

- **Key-access detection.** `--check` detects **absence**, not access, and the two must not be
  read as one. Access detection needs auditd (absent on this box, measured 2026-08-09), and
  under this model every illegitimate read of the tmpfs file is by construction a root action
  — a subset of host root-activity detection, which ADR 0050:574 assigns to #196/#1201. The
  disposition is recorded on [#1201](https://github.com/klasolsson81/jobbliggaren/issues/1201).
- **Backup encryption.** [#197](https://github.com/klasolsson81/jobbliggaren/issues/197) — its
  acceptance criterion says the backup key is "handled like the master key", so it consumes
  this model rather than rebuilding one. Its age identity must be a **different** identity
  that never sits on the box.
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
  but "reboots are manual today".
- **Whether any Netcup snapshot has been taken since 2026-08-05.** If one exists it contains
  the old plaintext key — which is one of the reasons the cutover rotates rather than relocates.
- **Whether Netcup's snapshot facility captures guest RAM.** If it does, no in-guest mechanism
  closes it; that is a hypervisor-level residual and applies to every branch equally.
- **Compose behaviour on v5.4.0.** The compose file's load-bearing behavioural notes were
  measured on 2.40.3; the box now runs v5.4.0.
