# Runbook — the production deploy stack on the Netcup box

**Scope:** the container half of #196 — bringing the Compose stack up on the Netcup
RS 1000 G12, and the proofs that close ADR 0050's gates. The base host (SSH, nftables,
edge firewall, zram, unattended-upgrades) is delivered and documented separately in
[`vps-base-hardening.md`](./vps-base-hardening.md); nothing here repeats it. Opening
registration and creating the box's first accounts — the standing procedure that unblocks
row 23's second half — is [`registration-gate.md`](./registration-gate.md).

**Authority:** ADR 0050 and its `Amendment 2026-08-04` (host, capacity conditions, gates
M-5a/M-5b/M-6/M-7, decisions K1–K4) and the `Amendment 2026-07-18` (Option B, six
invariants). Where this runbook and an ADR disagree, the ADR wins and this file is wrong.

---

## 1. What the stack is

Eight services start with the Compose project (`deploy/docker-compose.yml`), plus a ninth,
`migrate-rewrap`, which carries `profiles: ["ops"]` and therefore never starts with `up` —
it is invoked by hand for the master-key re-wrap (M-3). All images are pulled from GHCR;
nothing is built on the box, which is capacity condition 1.

| Service | Role | Publishes |
|---|---|---|
| `caddy` | TLS termination, K2 basic-auth gate, reverse proxy to `web` | **80, 443 — wide** |
| `web` | Next.js. The only thing Caddy proxies to (Option B) | nothing |
| `api` | ASP.NET API. Reached only over the internal network | nothing |
| `worker` | Hangfire jobs | nothing |
| `migrate` | Oneshot; gates `api`/`worker` via `service_completed_successfully` | nothing |
| `postgres` | Data | nothing |
| `redis` | Sessions, cooldown gates, landing-stats cache, and the company-register cache | nothing |
| `seq` | The queryable log sink (#1175, ADR 0128). No host port and no SSH tunnel — `AllowTcpForwarding no` — so it is reachable only from inside the project network | nothing |
| `migrate-rewrap` | `profiles: ["ops"]`, so **not started by `up`**. Operator-invoked one-shot for the master-key re-wrap; shares `migrate`'s image and the app-secrets mount | nothing |

**Why the edge publishes wide and everything else publishes nothing.** ACME HTTP-01
arrives on 80 and TLS-ALPN-01 on 443, both from the public internet: a loopback-bound
reverse proxy is an unreachable site *and* a certificate that can never issue. Every other
service stays off the host's interfaces entirely. That shape is enforced in CI by
`.github/scripts/compose-edge-publish-guard.sh`, which is **not** the loopback guard — it
gives the opposite verdict on purpose.

**Select the compose file by path, never by `COMPOSE_FILE`.** A `COMPOSE_FILE` entry in
the box's `.env` would override file resolution, and the compose guards are structurally
blind to that channel (#1217) — a green guard would then vouch for a file the deploy does
not run.

```bash
docker compose -f /opt/jobbliggaren/deploy/docker-compose.yml <command>
```

---

## 2. Layout on the box

```
/opt/jobbliggaren/
  deploy/          # the tracked deploy/ directory, from a shallow clone of the repo
    .env           # root:root 0600 — DB + edge credentials. NEVER COMPOSE_FILE, and since
                   # #198 never the crypto values either.
  staging/         # transient only (corpus dump); emptied and shredded after use
/run/jobbliggaren/secrets/   # tmpfs — the four crypto values, RAM only, re-injected per boot
```

`deploy/.env.example` is the template and the required-key list for what remains in `.env`.
Every key there is a hard `:?` requirement: compose refuses to start rather than starting on
a default nobody chose.

**The crypto values are no longer in `.env`, and gate B-1's repair shipped in #198.** The
field-encryption master key and the three pseudonymisation peppers are files on
`/run/jobbliggaren/secrets` (tmpfs, RAM-backed), bind-mounted read-only into api and worker
as `/run/app-secrets`, and reaching configuration through the `<KEY>_FILE` seam. Both
measured plaintext-on-disk surfaces are addressed by that move: `deploy/.env` itself, and
Docker's own container state — `docker inspect` returned the value after the container had
exited (measured 2026-08-05).

**B-1's parenthetical is exhausted, and satisfying the gate by a mechanism it does not name
is not a deviation.** The gate text names "systemd-credentials TPM-bunden el. sops+age→tmpfs".
Measured 2026-08-09: this host has no TPM (`systemd-analyze has-tpm2` → `partial`, no
`/dev/tpm0`, no libtss2) and `sops` is absent from apt on trixie. Without a TPM a sealed blob
and its unsealing key travel in the same disk snapshot. The requirement is *never plaintext
on disk*; **no at-rest copy at all** meets it more strongly than either named option would.
Full rationale and the operator procedure: [`master-key-ops.md`](master-key-ops.md).

**B-1 is discharged when the verification rows exist, not when the PR merged.** The PR proves
the mechanism; the operator's injection proves the instance. Until the rows in §5 are filled
in against this box, treat the gate as owed.

What `memswap_limit == mem_limit` delivers is a **stronger B-1 posture**, which ADR 0050
`Amendment 2026-08-04` §2 condition 4 attributes to B-1 in as many words. It never closed the
gate and it is not what closed it now — but it stays load-bearing for the half that remains:
the key is plaintext in api/worker process memory for the process lifetime, and without
`memswap_limit` a container at its ceiling would page that memory into zram. That residual is
described in ADR 0049 `Amendment 2026-08-09` and its acceptance lives in ADR 0123, **granted by
Klas 2026-08-16**. Read its status there, not here.

---

## 3. First boot, in order

Prerequisite: Docker installed, `/etc/docker/daemon.json` written, and the nftables
`forward` delta applied — all in §4 below, all before anything here.

> **AND the crypto-secrets mechanism, before the first `up`.** Since #198 the four crypto
> values are not in `.env`, so compose no longer refuses to start when they are missing — it
> starts, and api/worker crash-loop instead. Two consequences the old `:?` guards used to make
> impossible:
>
> - **Install `/etc/tmpfiles.d/jobbliggaren.conf` first.** The secrets bind mount carries
>   `create_host_path: true` (measured), so without it Docker silently creates the directory
>   root-owned and un-traversable by the container — and the app then reports a *missing key*
>   rather than a permission problem.
> - **Install BOTH absence-timer pairs in the same step, and enable
>   `jobbliggaren-secrets-present.timer` as soon as the crypto secrets are injected.** A
>   crash-looping container never appears in `systemctl --failed`; that timer is what puts the
>   condition on the box's only alarm surface. Since #1329 its `--check` reads the crypto set
>   alone, so nothing about #197 holds it back. `jobbliggaren-host-secrets-present.timer` is the
>   one that waits: its `--check-host` demands `Backup__RcloneConfigBase64`, so enabling it before
>   that credential exists fails every fire and holds that surface red — one page, then a surface that
>   is deaf to the next fault. `jobbliggaren-heartbeat.sh` carries why.
>   `master-key-ops.md` §2 owns the ordering, the full grounding and what deferring costs; do not
>   restate them here.
>
> Both are the install block in [`master-key-ops.md`](master-key-ops.md) §2. Run it **before**
> the first `docker compose up`, then inject (§3 of that runbook), then confirm `--check` exits 0.
> On a box without the rclone config `--check-host` still exits 1 — that is the deferred state, and
> §3 says what it looks like — while the crypto timer is enabled **in** that state rather than
> after it. Nothing mechanical enforces this ordering; that is what these lines are.

```bash
cd /opt/jobbliggaren
C="docker compose -f deploy/docker-compose.yml"

# 0. RE-ENTRY IS NOT FIRST BOOT. This block assumes empty certificate storage. On a box that
#    has already issued anything, list BOTH trees before starting —
#    `$C exec caddy ls /data/caddy/certificates/` — and read them deliberately:
#      · a STAGING tree present: step 1 would issue nothing and row 5 would tick on old work.
#        Discard it (step 2's own command) BEFORE step 1, not only between 1 and 2.
#      · a PRODUCTION tree present: step 3's "switching the issuer forces a fresh certificate"
#        no longer holds — storage is CA-scoped, so an existing prod tree is simply reused and
#        the final issuance evidence would tick on whatever produced it. compose defaults
#        ACME_CA to PRODUCTION, so this is reachable by a single boot before step 0's sed ever
#        ran. Establish where it came from and decide, rather than discovering it at step 3.
#    Measured 2026-08-08 on this box: staging tree present (discarded per the above),
#    production tree ABSENT — so step 3 issues for real.
#
# 0. STAGING FIRST, AND PROVE IT FROM THE CONTAINER. The block below forces issuance three
#    times; against production that is 3 of 5 weekly duplicate-certificate slots, and a FAILED
#    validation — the expected outcome when a path really is broken, which is why you are here —
#    locks issuance for an hour mid-cutover. compose defaults ACME_CA to PRODUCTION and the
#    template ships the key commented out, so nothing about a fresh .env is safe by default.
#    The sed matches the commented form too; an anchored ^ACME_CA= would silently no-op on it.
sed -i "s|^#*ACME_CA=.*|ACME_CA=https://acme-staging-v02.api.letsencrypt.org/directory|" deploy/.env
$C pull caddy   # a pre-seam image ignores the variable and starts clean in EVERY mode
$C up -d --force-recreate caddy
$C exec caddy printenv ACME_CA | grep -q acme-staging || { echo "REFUSING: not staging"; exit 1; }

# 1. HTTP-01 only (row 5). Read the counterfactual out of the RUNNING container, not the file.
#
#    DISCARD THE ACME ACCOUNT, NOT ONLY THE CERTIFICATE. Measured 2026-08-08: with the
#    certificate tree deleted and http01 selected, a certificate arrived in twelve seconds
#    over NO challenge at all — the CA still held a valid authorization for this identifier
#    from the previous issuance, and authorizations are cached per ACME ACCOUNT. The row
#    would have been ticked on a certificate that proves nothing. Deleting the account
#    directory makes Caddy register a new one, which holds no authorizations. Free on
#    staging; never on production, where a new account and order spend real rate limit.
sed -i "s|^#*ACME_CHALLENGE_MODE=.*|ACME_CHALLENGE_MODE=http01|" deploy/.env
S=acme-staging-v02.api.letsencrypt.org-directory
$C exec -T caddy rm -rf /data/caddy/certificates/$S /data/caddy/acme/$S
$C up -d --force-recreate caddy
sleep 30

#    HALF A — the issuance line must NAME the challenge type. "certificate obtained" alone
#    is exactly the evidence the paragraph above shows can be produced without a challenge.
$C logs caddy --since 2m | grep -iE 'challenge_type|served key authentication|obtained successfully'

#    HALF B — the counterfactual, on the policy whose subjects contain SITE_HOST. The adapted
#    config carries TWO automation policies (measured: one for the site, one catch-all with no
#    subjects and no challenges key), so `grep -o '"challenges"...'` is satisfied by either and
#    cannot answer this. Capture, then parse.
adapted=$($C exec -T caddy caddy adapt --config /etc/caddy/Caddyfile) ||
  { echo "REFUSING: adapt failed — nothing was measured"; exit 1; }
printf '%s' "$adapted" | python3 -c 'import json,sys
for p in json.load(sys.stdin)["apps"]["tls"]["automation"]["policies"]:
    print(p.get("subjects","(none)"), [i.get("challenges","(none)") for i in p.get("issuers",[])])'
#    Expected on the SITE_HOST policy: {"tls-alpn": {"disabled": true}}

# 2. TLS-ALPN-01 only (row 6). Step 1 left a VALID staging certificate AND a warm account, so
#    Caddy would issue nothing — or issue without a challenge. Discard both again.
sed -i "s|^#*ACME_CHALLENGE_MODE=.*|ACME_CHALLENGE_MODE=alpn01|" deploy/.env
$C exec -T caddy rm -rf /data/caddy/certificates/$S /data/caddy/acme/$S
$C up -d --force-recreate caddy
sleep 30
$C logs caddy --since 2m | grep -iE 'challenge_type|served key authentication|obtained successfully'
#    Same parse as half B above; expected mirror: {"http": {"disabled": true}}

# 3. Back to default, then production issuance ONCE. Storage is CA-scoped, so switching the
#    issuer is itself what forces a fresh production certificate — no rm belongs here, and one
#    would throw away a VALID production cert on any re-run and spend a duplicate slot.
sed -i "s|^#*ACME_CHALLENGE_MODE=.*|#ACME_CHALLENGE_MODE=both|" deploy/.env
sed -i "s|^#*ACME_CA=.*|#ACME_CA=|" deploy/.env
$C up -d --force-recreate caddy

#    AND PROVE THE CA FROM THE RUNNING CONTAINER BEFORE READING ANY LOG. Commenting the key
#    out means compose's default applies; that the default IS production is a fact about the
#    compose file, not about this container. Judge the container.
ca=$($C exec -T caddy printenv ACME_CA) || { echo "REFUSING: printenv failed"; exit 1; }
[ "$ca" = "https://acme-v02.api.letsencrypt.org/directory" ] ||
  { echo "REFUSING: running CA is [$ca], not production"; exit 1; }

# 3b. THE END STATE IS ITS OWN MEASUREMENT (verification-log row 6b). Rows 5 and 6 prove the
#     PROOF modes; nothing above proves the box was LEFT with both challenges live. Two ways to
#     fail that no certificate reveals: a left-over mode, and a glob value —
#     ACME_CHALLENGE_MODE=* imports all three snippets and adapts exit 0 with BOTH challenges
#     disabled (measured). Every plain typo fail-closes; the glob does not. Both issue fine at
#     cutover and kill the RENEWAL about 60 days later.
#
#     CAPTURE, THEN JUDGE — never `adapt | grep && refuse || ok`. A pipeline's exit code is
#     grep's, not adapt's: with the container down or the config broken, stdout is empty, grep
#     returns 1, and the `||` arm prints OK on a run that measured NOTHING. That is the very
#     state the seam's own broken-default bug produced, reporting itself as a pass. `-T` is
#     deliberate — without it exec may allocate a TTY and put CR into the captured string.
adapted=$($C exec -T caddy caddy adapt --config /etc/caddy/Caddyfile) ||
  { echo "REFUSING: adapt failed — nothing was measured"; exit 1; }
$C exec -T caddy test -f /etc/caddy/challenge/both.caddy ||
  { echo "REFUSING: pre-seam image — rows 5 and 6 measured nothing"; exit 1; }
grep -q '"challenges"' <<<"$adapted" &&
  { echo "REFUSING: a challenge is disabled in the RUNNING config"; exit 1; }
echo "OK: both challenges live"

# 3c. THE CERTIFICATE IS ONLY PROVEN FROM OUTSIDE (verification-log row 6c). A curl on the box
#     shares the box's trust store and resolver, so it cannot speak for a browser. Run this from
#     the OPERATOR'S machine, and without -k — the whole point is that no flag is needed:
#       curl -sSI https://dev.jobbliggaren.se
#       echo | openssl s_client -connect dev.jobbliggaren.se:443 -servername dev.jobbliggaren.se
#     Expected: no TLS error, and `Verify return code: 0 (ok)` against a public root.
```

---

## 3a. Rollback and the schema gate (#1236)

**Rollback is an image tag — for CODE. It is not a rollback for SCHEMA, and since
[#1236](https://github.com/klasolsson81/jobbliggaren/issues/1236) `migrate` refuses to pretend
it is.** Pin `IMAGE_TAG=sha-<short>` in `.env` and re-run the reconcile unit, and the four
long-running services are back on the old build in seconds. But `migrate` runs EF migrations
before `api` and `worker` start, and EF applies only *pending* (assembly minus applied) —
history rows an older assembly cannot name would pass **silently**, and this repo has measured
cases where the schema direction destroys data irreversibly (a computed column reverted to an
ordinary one; a `DROP COLUMN` taking its indexes silently). So `schema` mode now reads
`__EFMigrationsHistory` against its own assembly first and refuses before `MigrateAsync`:
**exit 3** for a pure backwards pin (overridable, below), **exit 4** for a diverged history —
the squash/re-baseline shape, never overridable. **The refusal travels in the image you pin
TO**, so it protects pins back to the first tag published after #1236 merged and no further —
an older tag has no gate and applies silently, the same way the attestation window in §3b
starts at #196's attest step.

**What a refusal leaves behind** — measured 2026-08-13 with a minimal compose fixture on local
Docker Compose v2.40.3; **the box runs v5.4.0** (§5's verification log), so re-verify the exact
aftermath there before relying on it mid-incident. The mechanism, by construction: `api`,
`worker` **and `web`** all carry `IMAGE_TAG`, so a tag-changing `up -d` has already **stopped
and removed** all three when the migrate dependency fails, leaving their replacements `Created`
but never started; `caddy` gates on nothing and moves to the pinned tag, and its only upstream
is `web` — **the public site answers 502/503, not just an api path**. Services whose image did
not change (postgres/redis/seq) are untouched, the failed oneshot's `docker logs` survive, and
the unit fails before the success stamp. The journal carries compose's own line naming the
code — `service "migrate" didn't complete successfully: exit 3` — and the full diagnosis lives
in `docker logs jobbliggaren-migrate`: the unknown migration IDs and three exits. (1) Roll
`IMAGE_TAG` forward to a build that contains those migrations. (2) Treat it as a **restore**
problem — [`backup-restore.md`](./backup-restore.md) — never a deploy problem. (3) Deliberately
run the old code against the newer schema: set `MIGRATE_ALLOW_SCHEMA_AHEAD=<the exact refused
ID set>` in `deploy/.env` and re-run the unit — the refusal prints the exact line, the value is
never `1`, and it stops matching the moment a different pin produces different IDs
(`deploy/.env.example` documents the key). While the pin stands with no override, the unit
re-refuses every hour — that repetition is the alarm working, not a new fault. **Once the
override matches, the alarm goes quiet**: migrate exits 0 hourly and the box runs old code
against the newer schema indefinitely, with one Warning line per run as the only trace and
nothing in `systemctl --failed` — the ID set expires against the *next different* pin, not
against this one persisting, so removing the key after the incident is the operator's step.

**Exit 4 today means an unexplained fork.** No migration squash or re-baseline has ever been
performed in this repo, so a diverged history has no innocent explanation: stop, establish the
cause, and write **nothing** to `__EFMigrationsHistory` until it is established. A deliberate
squash ships its own history-reconciliation procedure *before* it merges (ADR 0130, local).

A Netcup snapshot is **not** deploy rollback either: snapshots are copy-on-write, need 50 %
free disk, only *offline* ones are consistent, and one exportable snapshot remains. Their role
is **before a migration**, once real user data exists — which is precisely the boundary the
tag cannot cross.

---

## 3b. The reconcile unit

`release-images` publishes on an hourly schedule rather than on merge, because automerge
merges as a GitHub App and app-triggered events start no workflow runs (measured
counterfactual: #1107 app-merged, zero runs; #1108 human-merged, a run after 5 s). Nothing
tells the box a new image exists, so the box asks. Install once:

**What it verifies, and why the box needs a tool for it.** The unit pulls five images as root
every hour. `latest` is mutable, so the Trivy gate in `release-images.yml` speaks about the
image the workflow *built*, not about the one the box pulls an hour later under the same tag —
which leaves the whole chain resting on nobody having taken over the GitHub account. Since
#196 the wrapper verifies each pulled **digest** against a provenance attestation naming our
workflow on `main`, and refuses the entire apply if any image fails. Refused means *nothing is
applied*: the containers already running keep running.

**It refuses on a second question too, and a deployer reading only the paragraph above would be
surprised by it (#1295).** After attestation and before the apply, the wrapper checks that the
incoming api image can actually **read the injected secrets**. `jobbliggaren-inject-secrets.sh`
owned `/run/jobbliggaren/secrets` to the uid and gid of the image current at injection time —
the directory `0710 root:<gid>` so group traversal is the container's only way in, the files
`0400 <uid>` so the owner is the only reader — and an hourly pull can bring an image whose base
moved either id. The failure that produces is a crash-loop reporting a *missing master key*
rather than a permission problem, after an injection that succeeded and a `--check` that
passed.

So the refusal names the axis (traversal or owner), prints both ids, and carries the repair.
**Repair by re-owning, never by re-injecting** — see `master-key-ops.md` §3. The ordering is
deliberate: measuring the ids runs the image, so it happens only after that image has verified,
and the box never executes something it just refused.

On a box with **nothing injected** the check stats first, finds no regular file, logs one line
and applies. That is not a gap — if no secret has been injected there is nothing an image bump
can make unreadable, and a later injection measures the image the box is already running. Do
not read an absent refusal here as an absent gate; read the log line.

**cosign, and pinned to what Debian ships.** `apt install cosign` on trixie gives 2.5.0, which
is exactly the release that introduced `--new-bundle-format`, and Debian security-maintains it.
CI pins the same version (`sigstore/cosign-installer` with `cosign-release: v2.5.0`) so a
bundle-format divergence between CI and the box surfaces as a red job rather than as an hourly
refusal nobody sees. The alternative, `gh attestation verify`, was rejected: it needs gh ≥
2.97.0 (that release fixes a verification *bypass*), Debian ships 2.46.0, and GitHub's apt repo
serves only "latest" — so unattended-upgrades would move the gate's own binary underneath it.

**Install once** (order matters — the tool before the unit that requires it):

```bash
sudo apt-get update && sudo apt-get install -y cosign
cosign version   # expect 2.5.0 on trixie

# Fetch the Sigstore trust root ONCE, as root, before the unit ever runs. Verification is
# root's, and cosign caches the root under the invoking user's home — so leaving the first
# fetch to happen inside the first reconcile puts a network round trip and a cache write into
# a systemd unit whose environment (HOME, XDG dirs) this runbook does not establish. Do it
# here, where a failure is a prompt rather than a refused deploy.
sudo cosign initialize
sudo ls -la /root/.sigstore/root/  # the cached root, proving it landed for the right user

cd /opt/jobbliggaren && sudo git pull --ff-only      # the wrapper and verifier live in deploy/
sudo cp deploy/systemd/jobbliggaren-reconcile.{service,timer} /etc/systemd/system/
# THREE scripts, not two: since #1295 the wrapper calls jobbliggaren-runtime-ids.sh, and a
# non-executable helper stops the apply with exit 2 rather than failing loudly at install time.
# (git carries 100755 and CI gates it, so this line is belt-and-braces on a clone that lost it.)
sudo chmod 0755 deploy/systemd/jobbliggaren-reconcile.sh deploy/systemd/verify-image-attestation.sh \
  deploy/systemd/jobbliggaren-runtime-ids.sh
sudo systemctl daemon-reload
sudo systemctl enable --now jobbliggaren-reconcile.timer
systemctl list-timers jobbliggaren-reconcile            # Expected: one entry, next at :47
sudo systemctl start jobbliggaren-reconcile.service     # prove it runs at all, not just that it is scheduled
journalctl -u jobbliggaren-reconcile -n 40 --no-pager
```

`enable --now` schedules it; it does not run it. `list-timers` showing an entry proves
scheduling and nothing else — the one-shot `start` above is what proves the unit works,
and it is safe because an unchanged pull is a no-op.

**JUDGE THE JOURNAL, NOT THE EXIT CODE.** `systemctl start` returning 0 does not mean the
reconcile ran: if the timer fires in the same window (:47 plus up to `RandomizedDelaySec`), the
wrapper takes the lock-held branch and exits 0 **deliberately** — a benign overlap must not
land the unit in `systemctl --failed`, which is this box's only alarm surface. So the proof is
the journal carrying `verified N image(s)` followed by `reconcile complete`. A run that logged
`another reconcile holds` proved nothing and should simply be repeated.

**Install only after a publish that carries attestations.** Images pushed before the attest
step existed have none, and the wrapper refuses them correctly — which would read as a broken
install rather than as a working gate.

**Refusal is readable, and absence is not.** A refused run writes to the journal, and a journal
line nobody reads is indistinguishable from silence, so a successful apply also stamps
`/var/lib/jobbliggaren/last-successful-reconcile`. "When did this box last apply anything"
is then one `stat` rather than an inference from missing output.

**Manual applies go through the unit.** `sudo systemctl start jobbliggaren-reconcile.service`,
never a hand-typed `docker compose up -d`. The wrapper guards the path that goes through it:
a manual apply takes no lock and runs no verification, and after a refused run the local
`latest` tag already points at the image that was just refused.

**The one exception is a re-create that applies a `deploy/.env` change to a service whose image
is not moving** — `registration-gate.md` step 3 (opening the gate), step 7 (blanking the admin
knob) and step 10 (closing it). A container's environment is fixed at creation, so `.env` is
re-read only by a re-create; and the reconcile unit is the wrong instrument for it, because
`compose pull` is its **first** action and is unconditional. That makes the blast radius of a
one-variable config change depend on what GHCR holds at that second — including the mixed set
below, which nothing closes — and its lock branch reports success having applied nothing. So
these steps run:

```bash
cd /opt/jobbliggaren/deploy && sudo docker compose -f docker-compose.yml up -d --pull never api
```

`--pull never` is not decoration. Compose's default is `missing`, which is an assumption about a
version rather than a guarantee — the wrapper states that same argument at its own `up`.

**What the exception costs, in full:**

- **It re-creates from whatever the local `latest` points at, and after a refused reconcile that
  is the refused image.** This is the real residual, which is why the exception carries a
  precondition rather than a warning. Before running it:

  ```bash
  stat -c %y /var/lib/jobbliggaren/last-successful-reconcile
  sudo journalctl -u jobbliggaren-reconcile --no-pager | grep -n 'REFUSING\|CANNOT ANSWER' | tail -5
  ```

  The stamp must be **more recent than the last refusal**. If it is not, repair the reconcile
  first: a gate is not closed by deploying an image the box just refused.
- **It takes no lock**, the case the wrapper's own header names. The timer fires at `:47` plus up
  to 180 s of jitter and may run for up to 900 s. Do not run this inside that window; if you must,
  re-read the gate's own log line afterwards, because a concurrent reconcile can re-create the
  container underneath you.
- **It does not run #1295's secrets-ownership gate — and here there is nothing for that gate to
  catch.** It compares the *incoming* image's uid and gid against the injected secrets' ownership,
  against a base-image bump that moves them. These steps re-create from the image already running
  and already reading those secrets, so the comparison holds by construction. Recorded rather than
  omitted, so the exception is not read as wider than it is.

**Anything that moves an image is not this exception** — a rollback pin, a new publish, a
`postgres`/`redis`/`seq` tag bump — and goes through the unit.

**The timer fires at :47, offset from the publish run's :17 — and the hazard is cross-image
skew, not a half-published single image.** The publish job is a five-cell matrix with no
fan-in, so between the first cell's push and the last one's, `latest` resolves to the new
build for some images and the previous one for others; a pull landing there installs a mixed
set. (It cannot be the other split: the workflow pushes `sha-<short>` **before** `latest`, out
of one locally built image.) **The offset narrows that window and nothing closes it** — each
cell allows 30 minutes, so a slow `:17` run can still be pushing at `:47`. Attestation does
not close it either: it binds who built an image, never which tree, so a mixed set verifies
end to end. Owed, not delivered — [#1238](https://github.com/klasolsson81/jobbliggaren/issues/1238).

**Rollback stays an image tag, with the schema gate above.** Pin `IMAGE_TAG=sha-<short>` in
`deploy/.env` and run the unit: the pull resolves the pinned tag, every image is verified
against it, and `up -d` recreates only what moved. Seconds, and it is the primary rollback path
for the four long-running services. Across a **migration boundary** it is not a rollback at all
— `migrate` refuses the apply (exit 3/4) instead of running an older assembly silently; §3a
carries the refusal anatomy and the exits
([#1236](https://github.com/klasolsson81/jobbliggaren/issues/1236)).
A pinned tag must also be one that was published *with* an attestation, or the wrapper refuses
it: images pushed before #196's attest step exist but cannot be verified, so the reachable
rollback window starts there.

## 4. Host-side prerequisites

**Docker daemon.** Write `/etc/docker/daemon.json` **before the first `up`**: `json-file`
logging capped at `max-size: 10m` and `max-file: 3` — the same values the compose file sets, written here so a divergence can be seen. Nothing compares them automatically, and nothing needs to: every service sets `logging: *logging`, so the daemon default never reaches this stack, because no production log sink exists yet
(#1175) and rotation is the only thing standing between the stack and a full disk;
`live-restore: true`; pinned `default-address-pools` so an ad-hoc network cannot collide
with the stack's subnet. Never set `"iptables": false` — publishing needs Docker's DNAT,
and containment comes from the `forward` chain below, which is final regardless.

**The nftables `forward` chain is the decision this stack forces.** A published port is
DNAT'd in `nat/PREROUTING` and then traverses `forward`, not `input` — so the host's
`tcp dport {80,443} accept` in `input` does **not** admit a containerised Caddy, and
Docker's own ACCEPT rules in `ip filter` do not override a DROP in `inet filter`. "No
firewall change needed" is wrong.

The naive fix is the dangerous one: `forward policy accept` plus the edge default-deny is
currently what keeps M-6's "PG/Redis not public" true even against an accidental
`0.0.0.0` publish. Use targeted accepts and keep the policy at `drop`:

```
chain forward {
    type filter hook forward priority filter; policy drop;
    ct state established,related accept
    ct state invalid drop
    iifname "br-jbl" oifname "br-jbl" accept                       # intra-stack
    iifname "eth0"   oifname "br-jbl" tcp dport { 80, 443 } accept # DNAT'd edge only
    iifname "br-jbl" oifname "eth0"   accept                       # egress; the edge filters
}
```

The bridge is named `br-jbl` by the compose file's `driver_opts`, so these rules match a
stable interface rather than a generated one. Apply with the full §5 dead-man discipline
from the base-hardening runbook: back up with a leading `flush ruleset`, arm a
`systemd-run --on-active=10min` revert unit, verify from a **new** connection, then cancel
the timer.

**Measure the edge's IPv6 half BEFORE replacing `forward policy drop`.** While that policy
stands it covers v6 too; the moment it is replaced with targeted accepts, the edge becomes
the control on that family — and it is unmeasured. From mobile data:
`nc -6 -vz <box-v6> 22`. Timeout means the edge does not pass v6; **RST means the packet
reached the host**, and the accepts must then be scoped `meta nfproto ipv4` explicitly.

---

## 5. Cutover proofs

Every gate closes on a **response or a measurement**, never on a configuration file. That
rule is not stylistic: the dev compose file bound five of six ports to `0.0.0.0` for months
while its own comment claimed the opposite, and no reader caught it (#1198).

**ACME first, and on staging.** Caddy runs HTTP-01 *and* TLS-ALPN-01 by default and
chooses between them adaptively, so a shadowed HTTP-01 **falls back silently** — a `curl`
against the challenge path proves nothing. Set `ACME_CA` to the Let's Encrypt staging
directory and force issuance **once per challenge type** (one disabled at a time). Both
must succeed. Then confirm the prefix leaks nothing: an unknown path under
`/.well-known/acme-challenge/` must be answered BY CADDY — `Server: Caddy`, not the
upstream's own header. A 404 alone proves nothing, because Next answers 404 too: measured
2026-08-05 against an earlier form of the Caddyfile that merely exempted the prefix from
the gate, `GET /.well-known/acme-challenge/probe.txt` returned application content with no
credentials. The edge now owns the prefix with a `handle` block. Every other
path unauthenticated returns 401. Only then switch to production issuance — the production
limits are 5 duplicate certificates per week and 5 failed validations per hour, and a
mistake discovered there costs days. **Never configure TLS-ALPN-01 away without having
proven HTTP-01 for real.**

The seam is `ACME_CHALLENGE_MODE` in `deploy/.env`, selecting one of three snippets baked into
the edge image. Run all three modes — that the container starts in each **is** the measurement
that the import mechanism works, and needs no separate check.

**The sequence itself lives in §3 and has exactly one home.** An earlier revision of this file
carried a second copy here, and the two had drifted into contradicting each other: this copy
discarded the ENTIRE certificate tree where §3 discards only the staging one, ran an `rm -rf`
before production issuance that §3's own comment forbids in as many words ("no `rm` belongs
here, and one would throw away a VALID production cert on any re-run and spend a duplicate
slot"), used anchored `^ACME_CHALLENGE_MODE=` seds that §3 warns "would silently no-op" on the
commented form the template ships, never set `ACME_CA` to staging at all, and carried none of
§3's four REFUSING gates. Every one of those divergences pushes a one-way mistake against
production limits of 5 duplicate certificates per week and 5 failed validations per hour.
A one-way sequence with two written forms has no authority; **run §3's block, and only it.**

**`both` is an EMPTY snippet, and that is load-bearing rather than tidy.** The invariant is that
the default branch produces the configuration the stack had BEFORE the seam existed, and it is
measured as exactly that — `caddy adapt` on the pre-seam Caddyfile against `adapt` in mode
`both`, identical environment: **1401 bytes vs 1401 bytes, byte-identical**. The proof modes do
change behaviour (`http01` adds `"challenges":{"tls-alpn":{"disabled":true}}`, `alpn01` the
mirror); the default provably does not.

*An earlier version of this paragraph claimed instead that mode `both` emits no explicit issuer
at all. That was measured with `ACME_CA` unset — a state compose never produces, since it always
injects the directory URL — and with it set the global block emits an issuer regardless of the
seam. The number was true of its evidence and false of its subject. The diff above is the claim
that survives measurement, and it is the stronger one anyway.*

**Discarding the certificate does NOT force a challenge — discard the ACME ACCOUNT too.**
Measured 2026-08-08, and it silently produced a certificate that proved nothing. With
`ACME_CHALLENGE_MODE=http01` set and the staging certificate tree deleted, Caddy obtained a
certificate in **12 seconds** whose log carried `authorization finalized, authz_status: valid`
one second in and **no challenge line at all**: the CA still held a *valid authorization* for
this identifier from the previous day's TLS-ALPN-01 issuance, and an authorization is cached
per ACME **account**, not per certificate. Row 5 would have been ticked on a certificate
issued over no challenge whatsoever — the same shape as ticking row 6 on row 5's work, one
layer further out than the step-2 comment anticipates. Deleting
`/data/caddy/acme/<ca-directory>/` alongside the certificate tree makes Caddy register a new
account, which holds no authorizations, and the real challenge then runs and logs its type.
Free on staging; **never do this on production**, where a new account and a fresh order spend
real rate limit.

**The Caddyfile directive for the CA URL is `dir`, not `ca`.** `ca` is only what it adapts to in
the JSON config; writing it in a Caddyfile fails with `unrecognized ACME issuer property`. Both
snippets were validated against caddy 2 before shipping, because a mistake here surfaces at
cutover — in the window where rate limit is being spent.

### Verification log

Fill in as each is measured. Property · measured value · instrument · date — the same
shape as `vps-base-hardening.md` §9.1. A row without a date is a claim that cannot be told
from one that has decayed.

Two CI-side measurements belong here rather than in a session log, and were carried over from
2026-08-05 (they had no tracked home until this PR — a runbook line is not a docs-only PR when
it rides real scope):

- `compose-edge-publish-guard.sh` against the real `deploy/` project: **exit 0** — *"caddy
  publishes 80 443 and nothing else publishes (Compose 2.40.3-desktop.1)"*. The predicate judges
  the landed compose file, not only fixtures.
- `compose-edge-publish-guard.test.sh`: **30 passed, 0 failed**, including
  `real_deploy_project_by_default`.

**The box runs Compose v5.4.0** — three majors ahead of the 2.40.3 those numbers and this file's
behavioural comments were measured on. No divergence has been observed in the boot sequence,
the health-check semantics or `--force-recreate`, but the version belongs to every claim below
that came from the older one.
| # | Property (gate) | Instrument | Measured | Date |
|---|---|---|---|---|
| 1 | Per-service `mem_limit`/`memswap_limit` match the ADR 0122 table | `docker inspect` | caddy 128/256 · web 640/1280 · **api 1024/1024** · **worker 1024/1024** · postgres 2560/5120 · redis 512/1024 (MiB, mem/memswap) — the six long-running services, matching ADR 0122's table. `migrate` is **512/1024 and deliberately absent from that table**: it is a one-shot that exits before the stack serves, so it is not part of the 5 888 MiB steady-state sum. `memswap_limit == mem_limit` holds on exactly two services, and the condition naming them is **ADR 0050 `Amendment 2026-08-04` §2 condition 4**, not ADR 0122 — 0122 carries the limit table, the amendment carries the normative condition | 2026-08-06 |
| 2 | Redis runs `maxmemory` **and** `noeviction` | `redis-cli config get maxmemory*` | `maxmemory 419430400` (400 MiB) · `maxmemory-policy noeviction` — both set, which is the requirement: `noeviction` only engages when `maxmemory` is | 2026-08-06 |
| 2b | Redis has headroom under real traffic — `noeviction` means a full instance refuses writes, and the write that fails is the session store, so it surfaces as nobody being able to log in | `redis-cli info memory` — `used_memory` against `maxmemory` | **Baseline only, and the row's own condition is unmet.** `used_memory 1.58 MiB` (and `used_memory_peak` identical) against `maxmemory 400 MiB` — 0.4 %. There is no real traffic and no session load on this box, so this measures an empty instance, not headroom. What would close the row is the same thing that closes row 19: users. Owned by [#1235](https://github.com/klasolsson81/jobbliggaren/issues/1235) | 2026-08-08 (empty) |
| 2c | The K2 gate's hash is bcrypt cost 11, not the tool's default 14 — the gate pays the full hash on every WRONG password, and nothing upstream filters | `docker exec jobbliggaren-caddy printenv BASIC_AUTH_HASH \| cut -c1-7` prints `$2a$11$` and nothing more: the hash itself is offline-crackable, so do not put it in the cutover scrollback. Then time a wrong-password request against a right one — **from the box**, so network latency does not swamp the signal, and with **distinct** wrong passwords | **Both halves measured, and the second one qualifies the first.** Prefix: `$2a$11$` — cost 11, not the tool default 14. Timing, from the box (12 runs each): no credentials **9.4 ms** · a *distinct* wrong password **121.4 ms**, every single time · a *repeated* wrong password 125 ms once and then **9.5 ms**. So bcrypt costs **~112 ms of server CPU per distinct guess**, and Caddy caches the verdict per credential — meaning a naive flood of one wrong password amplifies nothing, while a guessing attacker, who by definition sends distinct values, pays full price on every one. **The denominator is one core, not four:** `caddy` runs under `cpus: 1.0` (measured on the running container, `NanoCpus=1000000000`), and the gate is inside caddy — so roughly **9 guesses/s saturates the quota**, not 36. That quota is also the containment: an attacker exhausts caddy's own share and the other five services keep their CPU, which is why the exposure is acceptable rather than merely small. Cost 14 would be about 8x worse on the same denominator. Measured externally the same run reads 106 ms wrong vs 190 ms right, which inverts the apparent conclusion — the 84 ms gap there is Next rendering the page behind a *correct* password, not bcrypt. An external instrument cannot see this | 2026-08-08 |
| 3 | Postgres steady-state RSS against the 2 560 MiB cap | cgroup `memory.peak` (which retains the high-water mark across the whole uptime, so it captures any 02:00 run without watching one) + `memory.stat` anon/file | **Steady state measured, the deciding workload NOT.** Over 2 days 7 h of uptime: `memory.peak` **151.8 MiB**, `memory.current` 138.6 MiB against a `memory.max` of 2 560 MiB — **5.9 % of the cap**, and the split is 11.8 MiB anon against 117.1 MiB file, i.e. almost entirely page cache the cgroup charges to Postgres. **The nightly dump job did not exist when this was measured**, so the peak reflects idle operation plus one 80-migration boot, not the workload ADR 0122 called the least certain number. The headroom is large enough that this is reassurance rather than a result. #197 merged the job on 2026-08-09 with a timer set to 02:15 UTC — but **merged is not installed**: rows 29–31 below are empty, so nothing has yet measured that the units are on the box, let alone that a dump has run. (Row 28 is measured as of 2026-08-10, but it measures the apt packages, not the units — it moves nothing here.) [#1235](https://github.com/klasolsson81/jobbliggaren/issues/1235) owns closing this, and what unblocks it is row 29 carrying a date, not the merge. `pg_dump` runs inside this container's own cgroup, so its cost will land here when it does | 2026-08-08 (idle only) |
| 4 | Postgres tuning is explicit, derived from the cap | `SHOW shared_buffers` etc. | `shared_buffers 640MB` · `effective_cache_size 1536MB` · `work_mem 8MB` · `maintenance_work_mem 192MB` · `autovacuum_work_mem 64MB` · `max_connections 60` · `max_wal_size 4GB` — every value explicit, none a default | 2026-08-06 |
| 5 | Certificate issues over HTTP-01 with the K2 gate live (M-5a) | `ACME_CHALLENGE_MODE=http01` on staging, then **the issuance log line naming the challenge type** **and** the counterfactual that the other was off (`caddy adapt` shows `"tls-alpn":{"disabled":true}` **on the policy whose `subjects` contain `SITE_HOST`** — the adapted config carries two automation policies and a bare substring match would be satisfied by either). BOTH halves: a certificate alone can be ticked on one ALPN issued — the silent fallback this proof exists to catch — and the counterfactual alone does not survive an operator confusing `http01` with `alpn01` | **Half A:** `"msg":"trying to solve challenge","challenge_type":"http-01"`, then **five** `"served key authentication","challenge":"http-01"` lines from five Let's Encrypt validation nodes (66.133.109.36, 51.20.52.251, 3.19.55.58, 54.185.127.228, 13.229.69.141), then `certificate obtained successfully`. Those five lines are also the strongest form of the M-5a gate: **the K2 basic-auth gate does not shadow the challenge path**, proven by external validators fetching through it, not by reading the Caddyfile. **Half B:** the adapted config carries **two** automation policies; the one whose `subjects` are `["dev.jobbliggaren.se"]` carries `{"tls-alpn":{"disabled":true}}` and the other carries no `challenges` key at all — which is precisely why this instrument names the SITE_HOST policy instead of substring-matching the document | 2026-08-08 |
| 6 | Certificate issues over TLS-ALPN-01 (the fallback path) | `ACME_CHALLENGE_MODE=alpn01` on staging; same two halves as row 5, mirrored (`"http":{"disabled":true}`) | **Half A:** `"challenge_type":"tls-alpn-01"`, then five `"served key authentication certificate","challenge":"tls-alpn-01"` lines from five validation nodes, then `certificate obtained successfully`. **Half B:** the SITE_HOST policy carries `{"http":{"disabled":true}}` — the exact mirror of row 5 | 2026-08-08 |
| 6b | The box was LEFT with both challenges live | `caddy adapt` **inside the running container** shows no `"challenges"` key at all. Rows 5 and 6 measure the PROOF modes; this measures the END state, and nothing else does — a left-over mode or a glob value — plus a pre-seam image, which the gate detects separately by asserting the snippet exists, (`ACME_CHALLENGE_MODE=*` imports all three snippets and disables BOTH, exit 0, measured) all issue a valid certificate at cutover and kill the RENEWAL ~60 days later | All four gates passed on the end state: `adapt` captured (1 542 bytes, exit 0 — judged after capture, never through a pipe); `/etc/caddy/challenge/both.caddy` present, so the running image is post-seam; **no `"challenges"` key anywhere in the adapted config**, so neither challenge is disabled; `adapt` additionally warns `Import file is empty` for `both.caddy`, which is the empty-snippet invariant announcing itself. `.env` left with both keys commented (`#ACME_CHALLENGE_MODE=both`, `#ACME_CA=`) | 2026-08-08 |
| 6c | The PRODUCTION certificate is trusted by a client that was told nothing — the cutover's actual end goal, and the only row a browser would agree with | `curl -sSI https://dev.jobbliggaren.se` **without `-k`** plus `openssl s_client` **from the operator's machine, not the box** — a box-side curl shares the box's trust store and its own resolver, so it cannot speak for a browser | `HTTP/1.1 401` with no TLS error, and the chain verifies to a public root: `depth=0 CN=dev.jobbliggaren.se` ← `depth=1 C=US, O=Let's Encrypt, CN=YE2` ← `depth=2 O=ISRG, CN=Root YE` ← `depth=3 ISRG Root X2`, `Verification: OK`, **`Verify return code: 0 (ok)`**. Issuance was `challenge_type: tls-alpn-01` against `https://acme-v02.api.letsencrypt.org/directory` on a newly created production account, spending **one** of the five weekly duplicate slots. The staging tree remains beside it — storage is CA-scoped, so it is inert | 2026-08-08 |
| 7 | The edge OWNS the ACME prefix (nothing under it proxies) | `curl -sI` unknown challenge path → 404 **and `Server: Caddy`**, never the upstream's own `Server`/`Via` | `404` **and `Server: Caddy`** on `/.well-known/acme-challenge/nonexistent`; no `Via`, no upstream `Server`. The edge answers, nothing proxies | 2026-08-06 |
| 8 | HSTS on the **unauthenticated 401** (M-5a) | `curl -sI https://dev.jobbliggaren.se/` — over **HTTPS**, which is the only scheme the header is emitted on and the only one a browser would honour it from | `HTTP/1.1 401` + `Strict-Transport-Security: max-age=31536000; includeSubDomains` + `Server: Caddy`. The header is on the FIRST response a browser meets, before Next is reached | 2026-08-06 |
| 9 | HSTS on a Next-served 200 (M-5a, complement) | `curl -sI -u <K2-user>:<pw> https://dev.jobbliggaren.se/` — same scheme and host as row 8, past the gate this time | `HTTP/1.1 200` + the same header value. **Emitted TWICE** — Caddy's site header and `buildSecurityHeaders` both fire, so both response paths carry it (which is the gate) but the Next-served response carries it twice. Values byte-identical; noted rather than silently accepted | 2026-08-06 |
| 10 | The four container ports this stack could expose do not answer from outside, and 80/443 do (M-5b p6) | external TCP probe per container port, **IPv4** | 3000 / 8080 / 5432 / 6379 → **Connection timed out** from 3 external nodes each. **Control: 80 and 443 → CONNECTED from 3 nodes each** — without it a broken probe service reads as containment. **Two scope limits, both deliberate:** the claim covers the four ports probed, not every port on the box (a port sweep is a different instrument and was not run); and every probe was IPv4, so the v6 family is row 14's, not this row's | 2026-08-06 |
| 11 | The BFF `/api/*` handlers resolve to Next (Option B) | cutover curl matrix, **all 12 enumerated** by `find src/app/api -name route.ts` rather than counted from a document | **12 of 12.** Nine answered with application statuses (200 health, 200 landing-stats, 400 suggest/facet-counts on missing params, 401 recent-searches, 405 on the four POST-only surfaces). The **three parameterised CV routes** — `/api/cv/[id]/preview`, `/api/cv/[id]/ats-text`, `/api/cv/parsed/[parsedId]/preview` — each answered `401 {"error":"unauthorized"}` on a synthetic id: their own handler's JSON, not a framework page. **The counterfactual is what makes that evidence, and it was measured:** a route that does not exist (`/api/definitely-not-a-route`) returns `307` instead, because it falls through to the `(app)` layout's auth gate. Different shape, different producer. No 404 anywhere, no ASP.NET signature. **The 12-vs-11 discrepancy is resolved:** ADR 0050 counted 11 and was right when written; the twelfth is `/api/health`, added **2026-08-05** by this stack's own compose health check (`git log --diff-filter=A`) | 2026-08-08 |
| 12 | `/api/v1/dev` and `/api/v1/admin/*` unreachable from outside | the edge's own structure, read on the box — not a response code | **Three structural facts, each measured on the running box:** `reverse_proxy` occurrences in the Caddyfile = **1** (a single upstream, and it is `web`); services publishing a host port = **only caddy** (80, 443); `127.0.0.1:8080` from the host = **closed**. There is no path from outside to ASP.NET. **The 307 that an earlier version of this row cited proves nothing** and is withdrawn: `/api/v1/dev`, `/api/v1/admin/jobs/recurring` and `/api/v1/auth/login` do return `307 → /logga-in`, but so do `/zzz-nonexistent-path` and `/helt-pahittad` — the redirect comes from the auth gate in `src/app/(app)/layout.tsx:42` (`if (!user) redirect("/logga-in")`), which catches every unmatched route. It was a property of unauthenticated routing, not of API reachability | 2026-08-06 |
| 13 | `forward` keeps `policy drop` with targeted accepts (M-5b p4) | `nft list chain inet filter forward` | `policy drop` with targeted `iifname`/`oifname` accepts, each explicitly `meta nfproto ipv4`. **What that scoping does and does not buy** (measured 2026-08-08, and worth stating because the natural reading is wrong): it does **not** keep IPv6 away from the *published* ports. `userland-proxy` is unset in `/etc/docker/daemon.json`, so it defaults on, and `docker-proxy` accepts 80/443 as a host process through the `input` chain — `forward` is never traversed for them. The containment of everything else comes from those services having no host port mapping at all. Setting `userland-proxy: false` would move published traffic onto the DNAT+`forward` path and change what this row means; do not flip it without re-measuring. Dead-man discipline: backup with leading `flush ruleset` dry-run-verified loadable, 10-min revert unit armed, verified from a NEW connection, timer stopped and `journalctl` confirms it never fired | 2026-08-06 |
| 14 | The edge's IPv6 behaviour | an external `nc -6 -vz <box-v6> 22` is the direct instrument and still needs a v6-capable client (the operator's machine has **no v6 egress** — its "Network is unreachable" measures the prober, not the box, and reading it as containment is the failure row 10's control exists to prevent). Structural instruments meanwhile: `ip -6 addr`, `nft list chain inet filter input`, `ss -tlnp6`, `docker ps` port bindings | **The box is v6-reachable in principle and exposes exactly the v4 port set — no more.** It holds a global address (`2a0a:4cc0:c2:afe5:…:8fa4/64`) though DNS publishes **no AAAA**; the host `input` chain is **family-agnostic** (`tcp dport 22 accept`, `tcp dport { 80, 443 } accept` — no `meta nfproto` scoping), so v6 is admitted on those three ports; `docker-proxy` binds `[::]:80` and `[::]:443`, so the published ports do answer over v6; and **nothing else is published to the host at all** (web 3000, api 8080, redis 6379, postgres 5432 carry no host mapping), which closes them in both families for a reason that has nothing to do with address family. The remaining external probe would confirm the mechanism, not change the exposure, and is parked in [#1235](https://github.com/klasolsson81/jobbliggaren/issues/1235) | 2026-08-08 |
| 15 | api/worker/migrate/web run as a non-root uid (`app`, `app`, `app`, `node`); postgres and redis drop privileges in their own entrypoints (`gosu` / `setpriv`); **caddy runs as root and is a named exception** — it binds 80/443 and its image sets no `USER`; every service carries `no-new-privileges` | `docker inspect -f '{{.Config.User}}'` for the declared user + **`docker top` cross-checked against `/proc/1/status`** for the uid the main process actually runs as + `docker inspect -f '{{.HostConfig.SecurityOpt}}'`. The two instruments answer different questions and the row needs both: `Config.User` is what was *declared*, `/proc/1/status` is what is *running* | api 1654 · worker 1654 · web 1000 (`node`; `docker top` prints the host name for that uid) · **postgres 999 · redis 999** (`/proc/1/status`, after their entrypoints drop) · **caddy root, the named exception** · **migrate `Config.User=app`**, exited 0 after applying 80 migrations. `no-new-privileges:true` on all six plus migrate. NOTE: `docker exec <c> id` is the WRONG instrument and is not used here — it spawns a NEW process as the container's configured user and reported root for postgres and redis | 2026-08-06 |
| 16 | `DOTNET_gcServer=0` reaches api and worker | `docker exec ... env` | `DOTNET_gcServer=0` present in api and in worker | 2026-08-06 |
| 17 | Swap is zram only, no disk swap (B-1) | `swapon --show` | `/dev/zram0`, 3.9 G, priority 100, 0 B used. No disk swap anywhere; `backing_dev` = `none` | 2026-08-06 |
| 18 | Per-IP rate limiting partitions on the real client IP | two known client IPs; one exhausts the login budget, the other still authenticates; plus a spoof probe — a client-supplied `X-Forwarded-For` must not reach the partition. Driven through the **no-JS login form** (the `$ACTION_REF` fields the page already renders), because `/api/v1/auth/*` is not reachable from outside under Option B and the limiter only counts what arrives at the API | **Partitioned, and spoofing does not work.** The operator machine exhausted `AuthWrite` at attempt 20 of 20/60 s; **within the same window** the box's own public address still authenticated (three attempts, all reaching the API's 401). Reverse control run first: exhausting from the box and then probing from the operator machine. The spoof probe, sent from the already-exhausted address, stayed blocked while claiming `6.6.6.6`, `1.2.3.4, 5.6.7.8` and `127.0.0.1` — Caddy overwrites the inbound header with the TCP peer, so a forged chain never reaches the partition. **This measurement first FAILED, and the reason is the finding:** both clients shared one bucket until the box was updated. The running `web` image had been built 2026-08-05 **20:49** UTC and #1231's relay landed on main at **23:00** the same evening, so the box was serving a Next bundle from before the fix — for three days, while `release-images` published a corrected one hourly and nothing pulled it. The code was right; the box was stale. That is precisely the gap the reconcile unit closes, and it argues for installing it more strongly than any test could | 2026-08-08 |
| 19 | Hot-path latency against the ADR 0045 budgets after `gcServer=0` | NBomber | Not measured — needs load, and load needs users. Owned by [#1235](https://github.com/klasolsson81/jobbliggaren/issues/1235) together with rows 2b and 3, which resolve on the same trigger | |
| 20 | Reconcile-pull runs and applies a digest change | `systemctl list-timers` + journal of a real run | **Installed and proven on a run that actually moved every digest**, 2026-08-08 17:20 UTC. The journal — not the exit code, which is 0 on the lock branch by design — carries `verified 5 image(s), skipped 2 upstream; applying`, preceded by one `verified:` line per ghcr image naming its digest, and `skipping postgres:18.3 / redis:8.6-alpine (upstream, on the allowlist)`. Compose then **recreated all six services**, so this was a digest change applied end to end and not a no-op tick. `Result=success`, `ExecMainStatus=0`, stamp written to `/var/lib/jobbliggaren/last-successful-reconcile`. The site was re-checked afterwards: 401 with HSTS unauthenticated, 200 and `<title>Jobbliggaren</title>` past the gate, `/api/health` 200, certificate chain still `Verify return code: 0`. Timer armed for :47+jitter | 2026-08-08 |
| 20b | The attestation gate answers correctly on the box, with real cosign and a real bundle | `verify-image-attestation.sh` against published digests, run **on the box** — the fixture suite stubs cosign and so cannot speak for Debian's build, GHCR's referrer layout, or the bundle format | **Five polarities, all as designed.** **Positive: exit 0** — `cosign 2.5.0-2+b4` from Debian main read an `actions/attest-build-provenance@v4` bundle out of GHCR's referrer tags and verified it. That is the one load-bearing joint the fixtures cannot reach, and it holds. Negatives, each exit 1: a pre-#196 digest (`no valid bundles exist in registry`) · a wrong signer workflow · a wrong repository owner · and — the sharpest — **the same bundle against a different ref**, refused with `expected SAN … @refs/heads/main, got … @refs/heads/feat/…`. **Why the production identity could not be the positive case yet:** the OIDC token is minted from the ref the *run* executes on, not from `inputs.ref`, so a dispatch from this branch attests under the branch's ref even when it builds `main`'s tree. Only a run on `main` can produce `@refs/heads/main` — and that run has since happened. **Measured after the merge, with the unmodified verifier from `main`:** a freshly published image verifies `built by .github/workflows/release-images.yml on refs/heads/main in klasolsson81/jobbliggaren`, exit 0. The positive case against the PRODUCTION identity is therefore no longer owed | 2026-08-08 |

### Rows 21–27 — gate B-1's own subject (#198)

<!-- This heading said "21–25" from the day rows 26 and 27 were appended under it, which is the
     drift a range in a heading invites: the table grew, the label did not, and a reader looking
     for the escrow gate by heading would not have found it. Corrected 2026-08-09 while adding
     the backup rows, which live in their own section below rather than extending this one —
     they belong to a different gate (M-4) and a different issue. -->


**These were written UNMEASURED, as a checklist for the cutover, deliberately** — before it
rather than reconstructed after it, because `deploy/` gets no integration coverage from `ci`
and the cutover happens once. Until #198 nothing in this log measured B-1's actual subject:
row 17 measures swap hygiene, which prepares the gate without discharging it.

**The 2026-08-15 cutover moved all seven in its FIRST pass, into four different states, and collapsing them
into "closed" is the misreading this table is most prone to.** Every row carries its own
qualifier in its Date cell; this list indexes them, it does not substitute for them.

**A SECOND PASS THE SAME EVENING MOVED FIVE OF THEM AGAIN, and the caveats this list carried are
mostly gone.** Klas rotated at the row-24 reboot rather than deferring the question, which is what
turned three "vacuous" or "short" cells into attested ones.

**A THIRD PASS ON 2026-08-16 MOVED THE TWO THAT WERE HOLDING B-1** — the
[#734](https://github.com/klasolsson81/jobbliggaren/issues/734) registration-gate visit, which
folded in [#1343](https://github.com/klasolsson81/jobbliggaren/issues/1343)'s journal sanitation
because a master-key rotation is free only while `user_data_keys` is 0, and that visit was what
ended it. Order mattered in both directions: the vacuum ran **before** the rotation so its effect
could be measured against the live key, and the rotation ran **before** the first encrypted write
so §4's shortcut was still available. The states now:

- **Closed outright — 21 and 25.** Property, instrument and measurement agree. 21 was
  **re-measured** against the rotated generation, because a row about the retired key says
  nothing about the one injected after it.
- **22 is now a clean tick, as of 2026-08-16.** ~~Its Property was measured FALSE~~ — its own
  instrument set had written the key into the persistent journal via `sudo`'s command logging.
  [#1343](https://github.com/klasolsson81/jobbliggaren/issues/1343) is **discharged**: the journal
  was vacuumed **6 → 0** while `local-v2` was still on the box (so the removal is measured rather
  than inferred), and the generation was then rotated to **`local-v3`**, which retires anything a
  vacuum could not reach. All three instruments were re-run against the new generation, with the
  pattern file's non-emptiness and a positive control both checked first. Only the **first** of
  those is the fail-open mode the Instrument column names; the positive control is new, and the
  column still does not prescribe it.
- **27 is still operator-attested, not measured.** The identity binds to the master
  key alone, so it says nothing about the peppers, and "carry-forward was impossible by
  construction" was **withdrawn**: tmpfs rules out surviving *files*, not an operator re-entering
  retired values from escrow. ⚠ **The 2026-08-16 rotation did not re-open it and did not advance
  it either:** only the master key was replaced, because `jobbliggaren-inject-secrets.sh` skips
  files that already exist, so the three peppers on the box are **unchanged** from the `local-v2`
  generation. The escrow written for `local-v3` therefore carries three values forward by design —
  the opposite of the `0 of 4` this row measured for the previous rotation, and a difference to
  read before reusing that number.
- **Measured outright — 24, as of 2026-08-16.** ~~The crash being the *designed* refusal is
  inferred from correlation~~ — api's own log now carries the refusal by name
  (`InvalidOperationException` naming the absent master-key file and the remedy), obtained with a
  targeted stop-rename-start rather than a reboot, which had become the more expensive instrument
  once the box held an encrypted field. **`139` remains SIGSEGV and is now filed as a defect in its
  own right** ([#1355](https://github.com/klasolsson81/jobbliggaren/issues/1355)) — the row itself
  predicted that consequence if it proved reproducible, and it did.
- **Both halves measured — 23, with one property still owed**, as of 2026-08-16. ~~Second half
  needs data this box has none of~~ — the [#734](https://github.com/klasolsson81/jobbliggaren/issues/734)
  visit created the first accounts, a cover letter was written and read back through the app,
  `user_data_keys` went **0 → 1** under `cmk_key_id` `local-v3`, and the stored column was
  confirmed to hold no plaintext. That last check is what separates the measurement from an echo
  of the request. ⚠ **It is NOT the fresh-DEK re-wrap case the Instrument column names** — that
  needs a field written under generation N and read under N+1, and here nothing was re-wrapped
  (`user_data_keys` was 0 at the rotation) and the letter post-dates `local-v3`. Owed until the
  next rotation over a non-empty table.
- **Gate re-done twice — 26.** It expired at the 2026-08-12 injection and again at this rotation,
  by its own terms both times. The cell records the second re-doing and the escrow's structure.
- **Measured, with one half owed — 32b**, in its own section below: the gate fires and clears,
  but the `find` repair that replaced the glob has never run on the box.

~~**BOTH ROWS THAT HELD B-1 MOVED ON 2026-08-16 — AND B-1 IS NOT THEREBY CLOSED.**~~ **Superseded
the same day: B-1 IS closed — see the ✅ paragraph below, which is the current statement.** The
three paragraphs that follow are kept as the reasoning that led there and are struck where they
have gone false; read them as history, not as status. Its predicate is
§5's closing notes: B-1 is discharged when rows **21–25** carry measurements. Read that predicate
carefully, because **"carries a measurement" is satisfied by a NEGATIVE one and the gate is not** —
which is precisely why 22 held it while carrying one. That is resolved: 22's Property is now
measured **true** ([#1343](https://github.com/klasolsson81/jobbliggaren/issues/1343) discharged),
and 23 carries **both** halves. So 21, 22, 23 and 25 all stand.

~~**What remains is 24**~~ — **settled 2026-08-16.** Its Property is *"reboot survival is the
DESIGNED failure"*. Self-heal and the detector were measured at the drill; ~~that the crash-loop is
the designed refusal rather than an unrelated fault is inferred from correlation~~ — api's own log
now names the refusal, so it is measured. ~~The row owes one line at the next reboot to settle
it~~; that line was taken without a reboot. What survives from this paragraph is its last clause:
`139` **is** reproducible, and is filed as a defect in its own right
([#1355](https://github.com/klasolsson81/jobbliggaren/issues/1355)).

⚠ **THE PREDICATE STOPPED HOLDING THE GATE BEFORE THE GATE CLOSED, AND THAT ORDER IS WORTH
KEEPING.** *"Rows 21–25 carry measurements"* became **literally satisfied by all five** while the
objection to 24 was still live — because that objection was about the measurement's
**interpretation**, which the predicate does not reach. For a few hours the gate was held by
judgement alone rather than by anything readable off the text. ~~It no longer does.~~ That window
is what the ✅ paragraph below closed, by settling the interpretation rather than by re-reading the
predicate.

✅ **ROW 24'S INFERENCE WAS SETTLED 2026-08-16, AND KLAS CLOSED B-1 THE SAME DAY.** The imperative
this section carried — *"do not close B-1 until row 24's inference is settled or Klas accepts it"*,
itself replacing the older *"do not close B-1 on row 23 alone"* — is **discharged on its first
clause**: api's own log names the refusal, so nothing rests on correlation any more.

**All five of rows 21–25 now carry measurements, and the Properties hold.** B-1 is **CLOSED**
(Klas, 2026-08-16).

⛔ **CLOSING B-1 DISCHARGES B-1. IT DOES NOT RELEASE THE CORPUS, AND IT DOES NOT AUTHORISE
`JobTech__IngestEnabled=true`.**

**The gate that bites at the corpus load is Art. 28, and it is not one condition but a SET.**
⚠ **The count and the membership live at the home, not here** — this outline once carried "six" while
the home's own list was a different six (it counts `ACME_EMAIL`; this one counted the sign-off
separately). Two enumerations agreeing on a total by coincidence is worse than one, so the number is
gone from here per `release-checklist.md`'s own ETT HEM PER TAL rule.
`release-checklist.md`'s corpus gate is the home — read it there, it is not restated here — and it
names every leg with its adjudicator. ⚠ **Discharging its legs is not release, and neither is
discharging everything else.** What stands in front of `JobTech__IngestEnabled=true` is **Klas's
explicit written GO** — a decision, not a derivable state, whose home is that file's §2.6
**point 3.5**. #1201 and #1199 are context for an informed GO, **not a condition set that can be
exhausted**: enumerating them here as the things "still standing in front" is precisely the
state-shaped form that failed open four times on 2026-08-16. In outline, so a reader knows what they opened: **a signed data-processing
agreement with netcup**, which ⚠ **does NOT apply automatically** (measured first-hand
2026-08-09 — it is the exception among this stack's processors, so never generalise from Scaleway's
or the AWS era's); the generator's *"circle of affected persons"* naming **recruiter contact
persons**, since they are Art. 14 non-users and a narrower declaration makes the contract narrower
than the processing; the annex's **sub-processor list read before the load**, because netcup
publishes none and the annex is the only measurement of that chain; updated ROPA entries; the
`recruiterNotice` re-examined as the Art. 14 notice for the population the load creates; and a
**security-auditor sign-off — given 2026-08-16 (SIGNED), covering the ROPA leg and closing the
`recruiterNotice` leg**. ✅ **`ACME_EMAIL` is confirmed too** — `klasolsson81@gmail.com`, read off
the box by Klas 2026-08-16; it is the controller's own address, so no processor row is owed. ⚠ **Do
not re-derive that from this file:** the value cannot be measured from the repo, so a sweep finds
only an empty placeholder in `deploy/.env.example` and concludes wrongly. The record lives in
`release-checklist.md`'s point 3 leg list with its adjudicator and date. **This sentence claimed
the opposite until 2026-08-17, and it was made false by the very commit that created the record.**

Two further gates are open beside it: **#1199** and **#1201**, whose detection mechanism is
installed but whose **heartbeat is not** — it needs a Healthchecks check that is Klas's to create,
and that issue's AC 4 does not close before it exists.

`appsettings.Production.json` **said**, until the sentence was removed on 2026-08-16, that the flip
*"belongs to the same change that closes B-1"*. It named **which change would carry the flip**
while B-1 was the live gate; it never meant that discharging B-1 permits it, and once B-1 closed
it could only be read the wrong way — so it was deleted at its source rather than glossed here.
This paragraph keeps the quotation as history because the misreading is the thing worth
remembering, not the sentence. An earlier draft of
this paragraph read it the second way and wrote *"and therefore `JobTech__IngestEnabled=true` …
and nothing else"*, which inverted the only control this document provides over 51 347 recruiter
contact records. Corrected 2026-08-16; recorded because the failure was to turn a gate's discharge
into a licence, and this paragraph is where a reader would have believed it.

⚠ **Read what B-1 did NOT gate, because the temptation is to bundle it.** `company_register` has
**no** encrypted column and, per ADR 0091, holds registered legal entities' business data rather
than personal data — sole traders are excluded twice. B-1 never gated it in either direction, and
closing B-1 does not authorise anything there. Its own path is decided separately
(`senior-cto-advisor`, 2026-08-16: one-time load, no sync on the box).

**This paragraph is no longer an enforcement and should not be read as one.** It is the record of a
gate that closed and of what closing it did and did not permit. **§5's closing notes carry the same
correction and name this paragraph in turn; if the two ever disagree again, that disagreement is
itself the defect — and it has already happened once, in the direction of updating this paragraph
and leaving §5 stale.** (The heading spans 21–27 because 26 and
27 are the gate's *subject* while 21–25 are what discharge it — two ranges answering different
questions, not a typo.)

⚠ **THIS PARAGRAPH USED TO SAY "greps with `-F "$K"`, so the value transits operator-shell RAM
only and never scrollback". IT WAS MEASURED FALSE 2026-08-15 and the commands below are changed
accordingly.** `sudo` writes every command it runs — arguments included — to the journal, so a
key passed as an argument to a **`sudo`'d** grep is published to persistent disk by the act of
checking. The rule is therefore: **the key's VALUE may reach the argv of an unprivileged tool
only; a privileged tool receives its PATH.** In practice that is `grep -F -f <the tmpfs file>`,
which reads the pattern from the file. Row 21's commands are unaffected and are deliberately not
swept — `sudo cat` and `sudo docker inspect` never carry the key, and its `grep -cF "$K"` runs
unprivileged, so nothing logs it. **`unset K` when the check is done**: row 21 is now the only
instrument that still captures the value, and the variable outlives the check otherwise. Verified on the box: the corrected forms return the same
answers and add **zero new journal entries carrying the key**. They are still `sudo`
commands, so `sudo` still records each invocation — with the **path** as its argument, which
is the whole of the change. Measured: one new entry naming the path, zero matching the value.
Procedure and rationale:
[`master-key-ops.md`](master-key-ops.md).

| # | Property | Instrument | Measured | Date |
|---|---|---|---|---|
| 21 | `docker inspect` returns the key on **no** container, including exited ones | `K=$(sudo cat /run/jobbliggaren/secrets/FieldEncryption__LocalMasterKeyBase64)` then `sudo docker inspect $(sudo docker ps -aq) \| grep -cF "$K"` — expect `0`. `ps -aq`, **not** `ps -q`: the measured 2026-08-05 leak was state surviving container *exit*, so an inspect over running containers only would re-measure the wrong set. Structural half: `sudo docker inspect -f '{{.Name}}: {{range .Config.Env}}{{println .}}{{end}}' $(sudo docker ps -aq) \| grep -iE 'MasterKey\|Pepper'` — expect only `*_FILE=` **path** lines | **Both halves zero.** `docker inspect $(sudo docker ps -aq) \| grep -cF "$K"` over every container, running plus the exited `migrate` → **0**. Structural half: the count of `MasterKey`/`Pepper` env lines that are *not* a `_FILE=/run/app-secrets/…` **path** is **0** — api and worker each carry five path lines and no value. The key was captured into the box's own root shell and `unset` there; it reached no scrollback and no operator machine. ✅ **RE-MEASURED against the `local-v2` generation later the same day**, because a row about *this* key says nothing about a key injected after it: both halves are **0** again — the new master key appears on no container including the exited `migrate`, and the count of `MasterKey`/`Pepper` env lines that are not a `_FILE=/run/app-secrets/…` path is 0. Same capture discipline, same `unset`. ✅ **RE-MEASURED A THIRD TIME 2026-08-16 against `local-v3`** — the same sweep over every container including the exited `migrate` → **0**. That run is also transcribed inside row 22's cell, which is where a reader looking for "the rotated generation" will otherwise land on a moved referent: as of today that phrase means `local-v3`, not the `local-v2` this cell was first written against | 2026-08-16 (re-measured against `local-v3`; the 2026-08-15 measurement against `local-v2` is the historical one this cell describes) |
| 22 | No plaintext copy on persistent disk | `KF=/run/jobbliggaren/secrets/FieldEncryption__LocalMasterKeyBase64` then `sudo grep -rIlF -f "$KF" /opt /etc /root /home /var/lib/jobbliggaren /var/lib/docker/containers` — expect no output. **Confirm the pattern file is non-empty first — `sudo test -s "$KF"`** — because this form trades a loud failure for a quiet one: `grep -F ""` matched everything, while `grep -f` on a zero-byte file matches nothing and reads here as a clean pass. A missing file is still loud (exit 2); only the empty one is silent. **`-f "$KF"`, never `-F "$K"`:** under `sudo` the value would be logged to the journal by sudo itself, which is this row's own Property violated by its own instrument (measured 2026-08-15). `grep -c '^FIELD_ENCRYPTION_MASTER_KEY=' /opt/jobbliggaren/deploy/.env` — expect `0`, line **deleted** and not blanked. `sudo sh -c 'journalctl --no-pager \| grep -F -f /run/jobbliggaren/secrets/FieldEncryption__LocalMasterKeyBase64' \| head -1` — expect empty. ⚠ **RUN THE JOURNAL SWEEP FOR ALL FOUR SECRETS, NOT ONLY THE MASTER KEY.** This Property is unqualified, and the three peppers — `AuditPseudonymization__PepperBase64`, `CompanyWatchPseudonymization__PepperBase64`, `CvReviewFingerprintPseudonymization__PepperBase64` — are **not** covered by the master key's sweep. They are also the weaker case: a rotation replaces only the master key (the injection script skips files that already exist), so a pepper reaching the journal is live and un-retired, with no second remedy behind the vacuum. The 2026-08-16 pass narrowed to the master key silently before this line existed. ⚠ **And run a POSITIVE CONTROL before believing any zero** — the same pipeline against a string known to be present (`grep -cF pam_unix`), non-zero. The empty-pattern-file trap above is one fail-open mode; a dead pipe is the other, and it is invisible without this. **Not `journalctl --grep "$K"`**, which has no `-f` and would put the value in a `sudo`'d argv — the exact form that contaminated this row; the whole pipeline runs privileged instead, because the operator cannot read the 0400 file. **Named scope limits:** `/var/lib/docker/overlay2` is excluded (a box-generated key cannot be in an image layer — `.dockerignore:35-49`); and freed SSD blocks that once held the old `.env` line are unreachable by any grep, **which is why the cutover rotates the key rather than relocating it** — rotation makes those remnants worthless | **All three instruments zero — the Property holds. The row's RATIONALE does not, and the generation is decidable rather than unknowable.** `sudo grep -rIlF "$K" /opt /etc /root /home /var/lib/jobbliggaren /var/lib/docker/containers` → **0 files**. `grep -c '^FIELD_ENCRYPTION_MASTER_KEY=' deploy/.env` → **0**, deleted and not blanked. Name sweep for `MASTER\|PEPPER\|KEY` over the box's `.env`: the only hit is `POSTGRES_MASTER_PASSWORD`, so none of the four has a residual home there — note this diverges from `.env.example`, which also carries `SEQ_INGEST_API_KEY`; the box has no such line (`grep -c` → 0), consistent with Seq not yet being wired as a sink. `sudo journalctl --grep "$K"` → **empty**. ⚠ **The two commands in this paragraph are a RECORD OF WHAT WAS RUN and are no longer the prescribed forms — the Instrument column above is.** Both passed the key in a `sudo`'d argv, which is the contamination this row later measured, so do not copy them. **And that makes this `empty` a result to distrust rather than a clean measurement:** the same form returned hits when re-run in the evening, all of them `sudo`'s own records, so either this morning's invocation had not yet had its journald entry committed when it queried, or a durable entry was missed. Which of the two is **not measured** — the retired key is no longer on the box to re-test with. ⚠ **The rationale above turns on a ROTATION, and the evidence points AGAINST one having happened.** `FieldEncryption__LocalMasterKeyId` = **`local-v1`** (2026-08-15) — and `local-v1` is `jobbliggaren-inject-secrets.sh`'s own `DEFAULT_KEY_ID:94`. That is the discriminator this repo defines for the question (`deploy/docker-compose.yml`'s `x-app-secrets` block calls the identity "not a secret … the re-wrap operation's idempotency marker" — named by anchor, because a bare line number into a file is the self-invalidating pointer #1338 measured), and it is one `cat` away, not a comparison against the deleted `.env` lines. It is **evidence, not proof**: the identity is operator-asserted at the prompt, so an operator could have accepted the default while injecting fresh bytes. **Forward obligation, and it is the point of the rationale:** if the generation was never rotated, the freed SSD blocks that held the old `.env` line still hold live key material, and `master-key-ops.md` §4's first rotation is free only while `user_data_keys` = 0. Rotate before the first real user and before the recruiter corpus lands (§5's closing notes gate that corpus on B-1). ✅ **THE FORWARD OBLIGATION WAS DISCHARGED THE SAME DAY, and the rationale is no longer short.** Klas rotated at the row-24 reboot: four freshly generated values injected under `JBL_MASTER_KEY_ID=local-v2`, and `FieldEncryption__LocalMasterKeyId` now reads **`local-v2`** — a value the script's `DEFAULT_KEY_ID` cannot produce, which is exactly the discriminator `local-v1` failed to be. The freed `.env` blocks therefore hold a **retired** generation, which is what this row wanted and could not previously say. **Read the reach precisely:** the identity is still operator-asserted, so `local-v2` establishes that the operator declared a new generation and not that the bytes differ — but the bytes were generated in front of the injection from `RandomNumberGenerator`, escrowed, and are not the values `.env` held. Free at the time it was done: `user_data_keys` **0**, so §4 step 5's re-wrap was skipped by the runbook's own instruction rather than omitted. **RE-MEASURED against the rotated generation, because the instruments above ran against the retired one and a row about that key says nothing about this one** (the principle row 21 states and applies): instrument 1 → **no output** across `/opt /etc /root /home /var/lib/jobbliggaren /var/lib/docker/containers`, and the same sweep repeated for each of the three peppers → **0** files each; instrument 2 → **0**. ⚠ **INSTRUMENT 3 RETURNED A HIT, AND THE INSTRUMENT ITSELF PUT IT THERE.** `sudo journalctl --grep "$K"` passes the key as an argument to a `sudo`'d command, and **`sudo` logs every command it runs, arguments included, to the journal**. Three entries matched, `SYSLOG_IDENTIFIER=sudo` on every one — the audit records of the check's own invocations. Instrument 1 has the same defect for the same reason; it reported cleanly only because it greps files, not the journal, while still writing the key there on its way past. **So this row's Property is FALSE on this box right now, and its own instrument set is what falsified it:** `/var/log/journal` exists and `Storage=auto`, so the journal is persistent disk. The instruments are corrected above to `grep -F -f <the tmpfs file>`, which never puts the value in argv — verified, 0 hits. **The existing journal entries are not cleaned by this PR**: journald cannot delete individual records, so the options are a vacuum that discards journal history or a further rotation that retires the exposed generation, and both are Klas's ([#1343](https://github.com/klasolsson81/jobbliggaren/issues/1343)). ✅ **#1343 IS DISCHARGED AND THIS PROPERTY IS MEASURED TRUE, 2026-08-16.** Klas took **both** remedies rather than choosing between them. **The vacuum ran FIRST, and the order was the point:** while `local-v2` was still on the box its removal could be *measured* with this row's own instrument, whereas rotating first would have left only the inference "the journal looks empty now" — the value would no longer exist to grep for. `sudo journalctl --rotate` then `sudo journalctl --vacuum-time=1s`, with `sudo sh -c 'journalctl --no-pager \| grep -cF -f <the tmpfs file>'` reading **6 → 0** across it; and the oldest readable entry moved from 2026-08-04 to the same day. **Two size figures, two instruments, and they do not subtract** — `--vacuum-time`'s own report says **45.7 MB freed**, while `--disk-usage` read **51.2 MB before and 16 MB after**, because the second measures a journal that already contains a freshly created active file. Neither is wrong; comparing them is. **Two fail-open modes were closed before the zero was believed, and only ONE of them is the trap this row's Instrument column names.** (1) The column's own: an empty pattern file, which makes `grep -f` match nothing and read as a clean pass — checked with `sudo test -s` (a bare exit status; the **44 bytes** is `sudo stat -c %s`, a second command, because `test -s` prints nothing and cannot report a size). Base64 of 32 is exactly 44 with no trailing newline, so no empty second pattern could match every line. (2) **New here, and the stronger of the two:** the identical pipeline run against a string known to be present (`grep -cF pam_unix` → **23**), which is what separates a zero from a dead pipe. The column does not prescribe that control; it should. **Then the rotation:** `local-v2` → **`local-v3`**, injected under `JBL_MASTER_KEY_ID` with api and worker stopped and `user_data_keys` re-measured **0** at that moment. **§4 step 5 was skipped by the runbook's own instruction** (*"Skip entirely when `user_data_keys` is empty"*); **step 3 was skipped by operator inference** — it is written unconditionally (*"PRESERVE THE RETIRING KEY FIRST"*) and carries no skip clause. Safe with nothing wrapped under the retiring key, but it was a judgement, and an earlier draft of this cell widened a true claim about step 5 into a false one about both. **Steps 1 and 10 were both performed and are named here because omitting either is silent:** `jobbliggaren-reconcile.timer` was stopped before the rotation (a `*:47` tick would otherwise start api and worker mid-injection) and re-armed after, verified `active` **and** `enabled` — `start` on a never-enabled timer leaves it disabled and it dies at the next reboot, on the box's only alarm surface. **All three instruments re-run against `local-v3`**, because a row about the retired key says nothing about the one injected after it: disk sweep → **no output**; `grep -c '^FIELD_ENCRYPTION_MASTER_KEY=' deploy/.env` → **0**; journal → **0** with the positive control at 91. Row 21's instrument re-run over every container including exited → **0**. A survivor in freed SSD blocks therefore holds a generation that is now retired twice over. ⚠ **THE PEPPERS NEEDED THEIR OWN JOURNAL MEASUREMENT AND NEARLY DID NOT GET ONE.** The `local-v2` pass swept instrument 1 across all four values; the first `local-v3` pass narrowed silently back to the master key while still stamping this row's Property — which is unqualified and covers all four. The asymmetry is material rather than pedantic: the master key has defence in depth here, **vacuum and rotation**, while the peppers have **only the vacuum**, because `jobbliggaren-inject-secrets.sh` skips files that already exist and the rotation left all three untouched. Anything of theirs surviving in the journal would therefore be live and un-retired. **Measured 2026-08-16 rather than narrowed:** each pepper file confirmed non-empty (44 bytes each), each with its own positive control on the identical pipeline (`grep -cF pam_unix` → 534 / 543 / 552, growing because the checks add their own `sudo` records), and each → **0**. The Property holds for all four. ⚠ **What the vacuum cost, recorded so the next reader does not read it as a defect:** it collapsed the journal history ADR 0126 **Decision 4** deliberately widened (`SystemKeepFree=2G`), and that ADR's **"What this decision does NOT do"** names journal erasure among the actions its detection cannot see. (That ADR has no numbered sections; `Decision N` is the repo's own citation form for it.) Two things make it the right trade anyway — the erased window is what held the master key, and the journal carries sshd source addresses, so shortening it is an Art. 5(1)(e) gain. **The control that would have made it a Blocker holds:** the GDPR audit trail lives in the database and not the journal (`AuditBehavior` writes in the same transaction), so Art. 5(2)'s evidence was untouched | 2026-08-16 (**#1343 discharged** — journal vacuumed 6 → 0 against the live key, then rotated to `local-v3`; all three instruments re-measured against the new generation, **and the journal instrument additionally run against each of the three un-rotated peppers → 0**) |
| 23 | The app boots and decrypts from the file-sourced key | `docker inspect -f '{{.State.Health.Status}}' jobbliggaren-api` → `healthy`. This **is** key evidence rather than a liveness check: an unreadable or invalid key is a boot failure — by **three** gates, not one, and this cell named only the last of them until 2026-08-16: an **absent or unreadable** file is refused during *configuration building* (`EnvFileSecretsConfigurationProvider`, before DI exists), an **invalid** one by `FieldEncryptionOptionsValidator` under `.ValidateOnStart()`, and the `LocalDataKeyProvider` constructor is a documented **re-guard behind** that validator rather than the thing that fires in a hosted boot. A fourth case is deliberately *not* the configuration provider's: an **empty or whitespace** file passes it, because the options validator owns the "missing secret" verdict and the provider declines to duplicate it, so a healthy api has parsed and validated the file. Then read one encrypted field through the app (any page showing CV or profile data) — the DEK-level check alone cannot catch a re-wrap that generated a fresh DEK | **First half measured, second half BLOCKED on the absence of data rather than deferred by choice.** `docker inspect -f '{{.State.Health.Status}}' jobbliggaren-api` → **`healthy`** on 2026-08-15, ⚠ **superseded the same evening — the continuity this cell relied on was broken deliberately, and the replacement is stronger.** Row 24's reboot took api down to a crash-loop and a fresh injection brought it back to `healthy` under the rotated `local-v2` key. A container that *survives a cold boot and parses a key it has never seen before* evidences this row's first half better than one that had merely kept running, so read the measurement as **2026-08-15 post-reboot** rather than as uptime. Original wording, now historical: having run continuously since its 2026-08-13 start with the `_FILE` seam live and `.env` holding none of the four (regenerate the span with `docker inspect -f '{{.State.StartedAt}} {{.State.Health.Status}}' jobbliggaren-api` rather than reading an uptime written here, which decays) — which is the row's own argument that a healthy api has parsed and validated a file-sourced key, since an unreadable or invalid key is a boot failure — by **three** gates, not one, and this cell named only the last of them until 2026-08-16: an **absent or unreadable** file is refused during *configuration building* (`EnvFileSecretsConfigurationProvider`, before DI exists), an **invalid** one by `FieldEncryptionOptionsValidator` under `.ValidateOnStart()`, and the `LocalDataKeyProvider` constructor is a documented **re-guard behind** that validator rather than the thing that fires in a hosted boot. A fourth case is deliberately *not* the configuration provider's: an **empty or whitespace** file passes it, because the options validator owns the "missing secret" verdict and the provider declines to duplicate it. Worker likewise up on the same mount. **The second half cannot be run today:** it requires reading an encrypted field through the app, and `identity.AspNetUsers` = 0, `job_seekers` = 0, `user_data_keys` = 0 — there is no CV or profile data on this box to read, so the re-wrap-with-a-fresh-DEK case this half exists to catch has no subject. It resolves on the same trigger as rows 2b and 19: a real user. **And it is NOT locked behind B-1** — the trigger is the first registered test user, not the recruiter corpus that §5's closing notes gate on B-1, so there is no deadlock here even though B-1 and this row both wait on data. Do not tick this row on the health status alone. ✅ **SECOND HALF MEASURED 2026-08-16**, at the [#734](https://github.com/klasolsson81/jobbliggaren/issues/734) registration-gate visit that created the box's first accounts — the trigger this cell itself named. Written through the app: `POST /api/v1/applications/` carrying a `coverLetter`, which is the field `EncryptedFieldRegistry` lists; a profile field would have measured nothing, because `JobSeeker` has no encrypted column at all → **`HTTP 201`**, id `32c7079b-…` (truncated deliberately: a full object id is the hard half of an IDOR probe and the row's evidence does not need it — the same convention #1349 uses for its user id). Read back in a **separate** request: `GET /api/v1/applications/32c7079b-…` → **`HTTP 200`**, the letter byte-identical with `åäö ÅÄÖ –` intact, so UTF-8 survived the round trip as well. `user_data_keys` **0 → 1**, `cmk_key_id` = **`local-v3`** — the generation rotated earlier in the same visit. ⚠ **That performs `master-key-ops.md` §4 step 7 literally and does NOT discharge it substantively, and an earlier draft of this cell claimed it did.** Step 7 is the box half of the re-wrap gate ADR 0049 §5 names — its CI half is `Rewrap_FieldCiphertextStillDecrypts`, and §4's drill states the purpose outright: *"a bug that generated a fresh DEK instead of re-wrapping the existing one passes every DEK-level assertion and destroys all field data."* Same premise as below, same conclusion: **no re-wrap ran here at all**. So step 7 stands owed alongside the re-wrap case, on the same trigger. ⚠ **The control that makes this more than an echo:** the API could have handed back what it was given without encrypting anything, so the stored column was read directly — `select cover_letter like '%Rad 23-verifiering%'` → **false**, and length **251**, which is structurally right rather than merely large: the wire format is `"v1:" + base64(nonce(12) ‖ ct ‖ tag(16))`, so 251 = 3 + 248 and 248 is a legal base64 length. **The structural check is the load-bearing one; do not compare 251 against a character count** — the letter carries `åäö ÅÄÖ –`, so its character and byte lengths differ and the naive comparison misleads. ⚠ **WHAT THIS DOES *NOT* ESTABLISH, and an earlier draft of this cell claimed it did.** The Instrument column names the case as *"a re-wrap that generated a fresh DEK"*. Catching **that** needs a field encrypted under generation N and read under N+1. Neither held here: `user_data_keys` was **0** at the rotation, so §4 step 5 was skipped and **no re-wrap ran at all**, and the letter was written **after** `local-v3` was already in force. Write and read therefore sit under **one** DEK, created at the write. What is established — and it is real — is that encrypt-on-write and decrypt-on-read work end to end under the live generation, and that the column holds ciphertext rather than the input. **The re-wrap case remains owed** until a field written under generation N is read back under N+1, which the next rotation with a non-empty `user_data_keys` will finally make testable. **Named limit:** the calls ran against `http://api:8080` from inside the project network (curl in the caddy container), so this evidences the API half of the path and not Next's rendering of it. The browser half was walked at the same visit and is **operator-attested, not measured** — no instrument, no artefact, recorded here as the weaker thing it is | 2026-08-16 (both halves; **second half evidences encrypt/decrypt under the live key, NOT the fresh-DEK re-wrap case, which is still owed** — see the cell) |
| 24 | Reboot survival is the DESIGNED failure, and self-heal works | `sudo systemctl reboot`. Expect: api `restarting` (crash-loop, fail-closed — no fallback key), `jobbliggaren-secrets-present.service` in `systemctl --failed` within ~2 min naming the missing files. **This row measures two properties and since #1329 BOTH are measurable in one visit.** The detector half presupposes the timer is ENABLED, and that used to be conditional on #197's `Backup__RcloneConfigBase64` being injectable — one `--check` answered for both sets, so an absent backup credential held the crypto alarm down. It no longer does: `--check` reads the crypto set alone and `master-key-ops.md` §2 enables the timer in the same visit as the install, so **do not defer this row on a box that is merely waiting for the rclone config.** And read the right instrument: what proves the detector fired is `jobbliggaren-secrets-present.service` **appearing in** `systemctl --failed`, not the list being non-empty. If it is absent from that list, that is now a real negative and not a deferral — check `systemctl is-enabled jobbliggaren-secrets-present.timer` before concluding either way, and do not tick on the silence. Then inject and expect api `healthy` within one restart-backoff interval **with no `compose up` and no reconcile run**. Closes two unmeasured premises at once: the crash-loop-then-self-heal behaviour, and that the absence detector actually fires | **DRILL RUN — the first time this row has carried a measurement at all.** Reboot issued 21:04, box back at boot stamp `2026-08-15 21:04:16`, verified by `uptime -s` changing rather than by a reconnect. **Failure on absence:** `/run/jobbliggaren/secrets` **0 files** (`/run` is `tmpfs`, measured with `findmnt`, so the wipe is the filesystem's property and not an assumption); `jobbliggaren-api` and `jobbliggaren-worker` both `Restarting (139)`, api `unhealthy`, 16 restarts, while postgres, redis, web, caddy and seq came up normally. ⚠ **That the crash-loop is the DESIGNED refusal is an inference here, not a measurement, and the Property says "designed".** `139` is `128+11`, i.e. **SIGSEGV** — which is not the shape of a managed `ValidateOnStart` abort in `LocalDataKeyProvider`, and not the `137` an OOM against `mem_limit` would give. What is established is the *correlation*: only the two secret-consuming services fell, and both returned to `healthy` on injection with nothing else changed. What is missing is api's own log line naming the absent master key, and **it can no longer be recovered** — reconcile recreated the container after the injection (`RestartCount 0`, fresh `StartedAt`), so the crash-loop output is gone. **Owed next reboot, one line:** `docker logs jobbliggaren-api 2>&1 \| tail -5` during the loop. **If `139` proves reproducible it is a defect in its own right** — fail-closed should be a refusal, not a segfault — and this row is where that was first noticed. **Detector:** `jobbliggaren-secrets-present.service` entered `failed` at **21:06:28**, i.e. `OnBootSec=2min` after boot, and named all five files individually. It also emitted `WRONG MODE: … is 700, expected 710`, an unpredicted state its own message anticipates verbatim (*"After a reboot this is expected"*) — the directory is recreated `700` at boot and the injection sets `0710`. **Self-heal:** after Klas's injection api and worker returned to `healthy` with **no operator action beyond the injection**, and the detector cleared on re-run (exit 0). The alarm surface was therefore observed firing AND clearing in the same visit, unstaged: `heartbeat: FAILING — failed-units=1:jobbliggaren-secrets-present.service`, then `heartbeat: all predicates hold`. All three timers were `enabled` **and** `active` after the reboot, which is the `enable`-not-`start` distinction proving itself across a boot ✅ **THE OWED LINE WAS TAKEN 2026-08-16, AND THE INFERENCE IS NOW A MEASUREMENT.** The cell asked for `docker logs jobbliggaren-api \| tail -5` during the loop; it was obtained without a reboot, which had become the more expensive instrument once `user_data_keys` was no longer 0. Targeted equivalent: `docker stop`, the master-key file **renamed aside on the same tmpfs** (never deleted — the bytes stay recoverable throughout, and the escrow is the fallback), `docker start`, read, restore. `status=restarting exitcode=139 restarts=7`, and api's own log carries a purpose-written refusal naming the file and the remedy: `System.InvalidOperationException: Secret file for 'FieldEncryption__LocalMasterKeyBase64_FILE' could not be read: '/run/app-secrets/…' (FileNotFoundException). The value is a file path, not a secret; check that the file exists and is readable by the process user.` **So the crash-loop IS the designed refusal, and this row's Property is measured rather than correlated.** Restoration verified end to end: key back at 44 bytes `1654:1654 400`, api `healthy` with `restarts=0`, the reconcile timer **stopped before the operation** (a `*:47` tick runs `compose up` and would have raced it — the same step 1 row 22's rotation cell names) and re-armed after, verified `active` **and** `enabled`, and — the check that matters now that the box holds an encrypted field — the row-23 cover letter still decrypts through the app with `åäö ÅÄÖ` intact. ⚠ **The refusal fires EARLIER than row 23's Instrument column says.** The stack is `EnvFileSecretsConfiguration.cs:205` ← `Program.cs:46`, i.e. in **configuration building**, before DI is composed and before `ValidateOnStart` exists to run. Row 23 named *"`ValidateOnStart` plus the `LocalDataKeyProvider` constructor"*; the conclusion survives — a healthy api has read and validated the key — but the mechanism is **three** gates, and **row 23 has been corrected in place** rather than from here, because a correction written beside an error instead of on it is the defect this session repeated four times. ⚠ **`139` is reproducible, and this row's own prediction therefore fires:** *"if `139` proves reproducible it is a defect in its own right."* Filed as [#1355](https://github.com/klasolsson81/jobbliggaren/issues/1355). An unhandled managed exception on Linux normally terminates via `abort()` → 134; **why this is SIGSEGV is NOT measured**, and the diagnosis is printed in full before the process dies, so it is an exit-code defect and not a lost-diagnosis one | **2026-08-16** (both properties measured — detector and self-heal 2026-08-15, the *designed*-refusal half settled by api's own log 2026-08-16; exit-code shape filed as #1355) |
| 25 | The hourly reconcile is unaffected | after the cutover, one `systemctl start jobbliggaren-reconcile.service` → `Result=success`, stamp written, and the journal shows no interpolation error. The key is no longer referenced by the compose file, so there is nothing left to interpolate and **no `:?` guard for it remains anywhere** (measured: two references repo-wide, both removed by #198) | **Measured on two real runs the same day, the second being the cutover's own apply.** Journal: `verified 5 image(s), skipped 3 upstream; applying`, then `injected secrets are readable by the incoming image (uid 1654, gid 1654)` (#1295's ownership gate passing against the live tmpfs), then `Container jobbliggaren-migrate Recreated` — the compose change landing on exactly the one service it touches — then `reconcile complete; stamped /var/lib/jobbliggaren/last-successful-reconcile` and `Deactivated successfully`. **Zero** lines matching `error\|warn\|interpolat\|variable is not set` across the window, which is this row's actual predicate. Note `skipped 3` and not the `skipped 2` row 20 recorded: `seq` has joined `postgres` and `redis` on the upstream allowlist | 2026-08-15 |
| 26 | **Escrow exists off-box for all four secrets — a GATE, not a report, and it RE-OPENS AT CUTOVER** | Klas confirms the four values are held off-box **in whatever form he has accepted** — the instrument is mechanism-independent deliberately, because ~~"in the password manager"~~ was a spec nobody was going to meet and a cell that contradicts its own instrument makes it impossible to tell a gate that was *met* from one that was *waived* (corrected 2026-08-12; there is no password manager and there will not be one for this) — and records the date here. **Do not cut over on an empty cell here, and re-open it at injection.** With no at-rest copy this is the only recovery path, and losing a value is as final as rotating it after rows exist: the master key takes every encrypted field, the company-watch pepper every org.nr token (its plaintext was destroyed in place), the CV-fingerprint pepper every Ignored/Resolved decision. (The audit pepper is the fourth and the exception: nothing reads back against it, so losing it costs only the ability to link erasure-audit records to one another across the gap.) ~~**Undecided as of 2026-08-09**~~ — **DECIDED by Klas 2026-08-12, and the cell records what was actually done rather than this row's own wording.** There is **no password manager**: the four values are held as two plaintext copies on Klas's own devices (his workstation, plus a phone copy — the directory is deliberately not named in a tracked file, for the reason `backup-restore.md` §1 gives). **The ground for accepting that form:** escrow's job here is *durability*, not confidentiality, and the workstation already holds `~/.ssh/jobbpilot_vps_ed25519` — root on the box, therefore the four values. **Read the reach precisely, because it changes at cutover and an earlier draft of this cell got it wrong in both directions.** *Today (measured 2026-08-12, `grep -c` → 4):* they are in `deploy/.env`, because the box still runs the pre-#198 compose. *After cutover:* row 22 expects that count to be **0** and the values live on `/run/jobbliggaren/secrets/` (tmpfs, `0400`), which root still reads — **but only while the box is running post-injection**, and not at all after a reboot before injection. So the key's reach is permanent today and **transient and conditional** afterwards, while the escrow copy is permanent and at rest either way. The "opens no exposure class" argument is therefore strongest now and weakest after cutover, which is a second reason this cell expires there. What does not weaken: two independent devices is real durability, and durability is what the no-at-rest-copy model actually needs. **`security-auditor` reported the condition as narrower than the risk** — it covers the phone's *storage* but not the transport that put the values there, not ordinary device backup, and not the workstation copy at all. **Klas declined to widen it, 2026-08-12 — an accepted risk recorded in ADR 0129, which is gitignored per §6.5; her finding is reproduced immediately above, so a checkout without the ADR loses nothing of it.** **AND THIS CELL EXPIRES AT CUTOVER:** what is escrowed today is the PRE-cutover generation, and §4's first rotation replaces the bytes — an escrow of a retired key is worse than none, because the holder believes they have it. Re-do this row at injection | Klas, off-box, two devices — **re-done at injection 2026-08-15, which is what this row's own "RE-OPENS AT CUTOVER" required.** Klas confirms the four values now on `/run/jobbliggaren/secrets` are the ones he holds off-box, same two devices, same accepted form (ADR 0129). The generation's **identity** is `local-v1` (`FieldEncryption__LocalMasterKeyId`, measured 2026-08-15) — recorded here because `master-key-ops.md` §3 requires escrow to hold the bytes **and** the identity, and an escrow that cannot name its generation is the failure this row exists to prevent. ⚠ **What this re-confirmation does NOT settle, and it is deliberately left open rather than resolved by inference:** it establishes that the escrowed copy MATCHES tmpfs, not that tmpfs differs from what `deploy/.env` held before #198. `local-v1` is the injection script's default, so the evidence leans toward "no rotation" — in which case row 27's Property is *false* rather than merely unproven, and this cell still holds a current escrow of a never-rotated generation. **Klas owns that question**; it is not a docs-layer call. ✅ **KLAS ANSWERED IT THE SAME DAY, AND THIS CELL IS RE-DONE AGAINST THE ROTATED GENERATION.** He rotated at the row-24 reboot rather than deferring: four fresh values generated on his workstation from `RandomNumberGenerator` (a CSPRNG — `Get-Random` was explicitly ruled out), escrowed **before** the reboot, then injected under `JBL_MASTER_KEY_ID=local-v2`. Same accepted form as 2026-08-12 — two devices, workstation plus phone, ADR 0129 — and the escrow file carries the **identity** alongside the bytes, as this cell already requires above. Its structure was verified before the reboot without reading any value back: five `key = value` lines, all four secrets valid base64 decoding to exactly 32 bytes, all four **distinct** (no duplicate paste), identity `local-v2`. ⚠ **The retired `local-v1` copy may now be destroyed — but NOT on row 27's `0 of 4`, and the distinction is the point.** That number closes link C (the two escrow files differ) and this copy hedges link B (that the box actually runs what was escrowed), which row 27 records as still operator-attested. Had the old values been re-entered at the rotation, it is the *v1* bytes running, and destroying this file would destroy the escrow of the live generation — precisely what link C cannot rule out. **What does carry the decision:** nothing is encrypted under any generation. `user_data_keys`, `resume_finding_statuses`, `company_watches`, `job_seekers` and `identity.AspNetUsers` were all measured **0** the same day (row 27), so losing either escrow costs no data today. On that ground the copy should go, since this row's own warning is that an escrow of a retired key is worse than none. ⚠ **Destroying it does cost the ability to re-run row 27's comparison** — the method is written down, the input is not. Accepted for the same reason: the retired generation protects nothing | **2026-08-15** (re-done at the rotation; four new values + identity `local-v2`, two devices; **re-opens again if a further rotation is chosen as remediation — re-escrow BEFORE destroying the outgoing generation**) |
| 27 | The peppers were replaced, not carried forward | **Re-measure AFTER `docker stop jobbliggaren-api jobbliggaren-worker`** (`master-key-ops.md` §4 step 2), never merely "before cutover": while the old stack runs the write path is open, and a single `company_watches` row landing between the measurement and the injection locks that pepper permanently without the rotation noticing. Stopping the containers first makes measurement and rotation atomic with respect to new rows. Then, with raw SQL inside the postgres container (not through EF — soft-delete filters hide rows): `resume_finding_statuses`, `company_watches`, `user_data_keys` all 0. Measured 2026-08-09: all three were 0, `audit_log` 13 (its pepper is rotatable regardless — see row 26's note). Confirm the new values landed with `jobbliggaren-inject-secrets.sh --check` plus `sudo find /run/jobbliggaren/secrets -maxdepth 1 -exec stat -c '%n %u:%g %a' {} +` — the files are 0400, so do not try to read them back. **Not a `sudo … /*` glob and not `%U:%G`, and both halves were measured broken 2026-08-15:** the glob is expanded by the operator's shell before `sudo` elevates and `0710` denies them the read, and the name form renders the container's uid/gid as UNKNOWN — `root:UNKNOWN` on the directory, `UNKNOWN:UNKNOWN` on the files — because they have no host `passwd`/`group` entry; the owner axis survives it, the group does not | **The three pepper-locked tables are empty, so the property holds — but VACUOUSLY, and this is not the atomic measurement the instrument specifies.** `resume_finding_statuses` **0**, `company_watches` **0**, `user_data_keys` **0**; `audit_log` **40 as measured 2026-08-15** (13 on 2026-08-09 — its pepper is rotatable regardless, row 26's note). That one grows, so read it as a dated observation and not a current value; regenerate with `docker exec jobbliggaren-postgres psql -U postgres -d jobbliggaren -tAc 'select count(*) from audit_log;'`. Upstream of all of it: `identity.AspNetUsers` **0** and `job_seekers` **0**, so no user exists that could write a pepper-locked row. ⚠ **`docker stop jobbliggaren-api jobbliggaren-worker` was NOT run.** Measurement and rotation were therefore not made atomic with respect to new rows — but that requirement guards a rotation being performed *in the same visit*, and the injection this cell measures against happened **2026-08-12**, three days earlier, so there was no window left for this session to close. **THIS ROW'S PROPERTY IS THE ONE THING NOT MEASURED, which is what separates it from row 22.** Row 22's Property ("no plaintext copy on persistent disk") *is* discharged by its three instruments and only its rationale is short. Here the Property is *"the peppers were replaced, not carried forward"*, and replacement is exactly what no instrument on this box establishes: `FieldEncryption__LocalMasterKeyId` = `local-v1`, the injection script's own default (row 22), which is evidence **against** a rotation rather than for one. **So read this row as vacuously safe, never as a tick:** nothing is locked to any pepper because no row exists to lock, and that stays true whether the values were replaced or carried forward. Confirmation half, both instruments: `--check` → `all secrets present in /run/jobbliggaren/secrets`, exit 0, zero `MISSING:` lines; `stat` → directory `0710 root:1654`, every one of the five files `0400 1654:1654`. ✅ **THE PROPERTY IS NOW OPERATOR-ATTESTED — a real advance on vacuous, and NOT the same thing as measured.** Klas replaced **all four** values at the row-24 reboot: freshly generated on his workstation from `RandomNumberGenerator`, escrowed, then injected under `JBL_MASTER_KEY_ID=local-v2`. Post-injection: `--check` exit 0 with zero `MISSING:`, identity **`local-v2`**, ownership triple `0:1654 710` on the directory and all five files `1654:1654 400`, and the pepper-locked tables re-measured **0** (`resume_finding_statuses`, `company_watches`, `user_data_keys`) with `identity.AspNetUsers` **0** and `job_seekers` **0** upstream. ⚠ **READ THE REACH EXACTLY, because two earlier drafts of this cell overstated it in different directions.** (1) **`local-v2` is a MASTER-KEY discriminator and says nothing about the peppers.** `jobbliggaren-inject-secrets.sh` writes the identity only inside `if ! has_usable_content "$master_key_file"`; the three peppers go through the `SECRET_KEYS` loop with **no identity marker of any kind**. There is no pepper discriminator on this box, not even a weak one. (2) **"Impossible by construction" was too strong and is withdrawn.** tmpfs closes one carry-forward path — that old *files* survived — and cannot close the other: an operator re-entering the retired values from escrow, which row 26 says is deliberately still available. What actually rules that out is Klas's generation of four fresh values, i.e. the same operator assertion the identity rests on. **What IS measured**: the four escrowed values are pairwise distinct and each decodes to 32 bytes (verified before the reboot without reading any back), and no old file survived. ✅ **RUN 2026-08-15, and it closes the ONE link of three that was previously unmeasured — not the whole chain.** Read the chain before the number, because an earlier draft of this cell wrote the number as if it discharged all three. (A) what the box held before the rotation ≡ the retired escrow file — **operator-attested**, row 26. (B) what the box holds now ≡ the rotated escrow file — **operator-attested**, row 26; injection is `read -rs` at a prompt and no instrument compares tmpfs against escrow. (C) the two escrow files are disjoint — **MEASURED, here.** So the ⚠ note above stands rather than being superseded by this one: what rules out a deliberate re-entry of the old values is still Klas's own account of generating four fresh ones. What is no longer possible is that they were replaced *by accident with the same bytes*, or that the escrow itself carried the old generation forward. The two escrow generations were compared offline on the workstation by SHA-256 of each value — no box touched, no value printed, only truncated digests compared: retired (2026-08-12, `local-v1`) **4 values, 4 distinct**; rotated (2026-08-15, `local-v2`) **4 values, 4 distinct**; **values carried forward: 0 of 4.** Every escrowed value differs, the peppers included. **Truncation cannot have manufactured that result**: a shortened digest can only produce false *matches*, never false non-matches, so "0 of 4" is stronger than its 12-hex-character prefixes suggest. `no box touched` is a security property of the instrument **and** its limit, and both halves are meant. ⚠ **The comparison instrument was itself wrong three times before it measured anything** — base64 padding contains `=` and defeated a `label = value` regex, a non-ASCII path did not survive the script's encoding, and worst, the reader returned its own "MISSING" string as *data*, so an unreadable file counted as one value and the comparison reported a clean 0-overlap against a set of one. It now aborts unless both files yield exactly 4. Recorded because a fail-open verifier is the defect class this whole section is about, and it appeared here in the instrument written to close it. Separately, the instrument's `docker stop jobbliggaren-api jobbliggaren-worker` was not run *as such* — the reboot stopped them, which is strictly stronger for atomicity | **2026-08-15** (escrow generations MEASURED disjoint, 0 of 4; escrow↔tmpfs still operator-attested at both ends — identity `local-v2`) |


### Rows 27b–32 — the target's provisioning (27b–27d) and gate M-4 (28–32) (#197)

**Mixed on purpose, and the split is worth reading before the table.** **27b–27d are properties of
the TARGET**, measured against the live container: 27c is measured and closed; 27b and 27d are
**gates** and are open. **28–32 are the drill on the BOX**; 28 is measured and closed (2026-08-10),
29–32 are unmeasured, written as a
checklist deliberately — `deploy/` gets no integration coverage from `ci`, so the box is where they
are decided.

Rows 28–31 close **gate M-4**. Row 32 is a gate and Klas's, inheriting row 26's wording. A row
without a date is a claim that cannot be told from one that has decayed.

| # | Property (gate) | Instrument | Measured | Date |
|---|---|---|---|---|
| 27b | **The target's lifecycle actually removes objects, measured as an EFFECT and not as a rule** | Provisioning gate, and it is a gate: `get-bucket-versioning`, then the lifecycle configuration read back, then **a plain `main/` object listing after 31 days showing the older artefacts actually gone**. That listing is the whole live instrument, and it is the half that carries K4: main objects are named `main/jobbliggaren-<STAMP>.dump.age` per run, so they accumulate under distinct names and only `k4-main-artefacts-30-days` removes them — the listing can therefore fail. **A `deks/` listing cannot, and must not be ticked as though it could** ([#1292](https://github.com/klasolsson81/jobbliggaren/issues/1292)): that prefix carries three constant object names overwritten nightly, so it holds exactly one generation by naming construction whatever the lifecycle does, and each overwrite resets `LastModified` so `deks-outlive-main-90-days` cannot fire at all while the job runs. **And the retired instrument, named so it is not reached for again:** `list-object-versions --prefix deks/` returning exactly one version per key is what this row asked for first, and with versioning off it returns one `null`-id version per key regardless — true by construction, discriminating nothing. **Row 27c settled the branch this row was drafted against, and the conditional half below is inert — read it as history, not as work.** On a *versioned* bucket an overwrite deletes nothing and an `Expiration` rule only writes a delete marker, so both prefixes would need `NoncurrentVersionExpiration` (and `main/` additionally `ExpiredObjectDeleteMarker`). **Versioning was measured OFF on this container (27c, 2026-08-09)**, so no noncurrent version and no delete marker can exist, those two rule types would match nothing, and adding them is not the repair — `backup-restore.md` §8 item 6 records the same finding. A rule is a claim; the disappearance is the measurement | | |
| 27c | **The chosen target is EU and unversioned, and BOTH prefixes carry a provider rule with `deks/` outliving `main/`** | `aws s3api get-bucket-location` / `get-bucket-versioning` / `get-object-lock-configuration` / `get-bucket-lifecycle-configuration` against the live container | **Measured, all four.** OVHcloud Object Storage, container `jobbliggaren-backups`. Location `eu-west-par` (Paris, EU). Versioning **not enabled** and Object Lock **not enabled** — Klas's decision, and the branch ADR 0125 names as strictly stronger for the one-generation property; the cost is that Object Lock is creation-time and is now permanently closed on this container. **Two** lifecycle rules **as measured 2026-08-09, and the count is now a snapshot rather than the target** — ADR 0050 G3 adds `g3-hostlogs-app-30-days` and `g3-hostlogs-host-90-days` when the log archive ships, taking it to four with the same two numbers. Applied and read back at the time: `k4-main-artefacts-30-days` (`main/`, 30 days) and `deks-outlive-main-90-days` (`deks/`, 90 days). **The ordering is the invariant, not the numbers:** both prefixes are written in the same run, so `deks/` outliving `main/` means a live main artefact implies its key generation still exists. Equal or shorter would expire the KEYS while younger ciphertext survives. `deks/` needs *some* bound because `user_data_keys` carries `JobSeekerId`+`CreatedAt` — pseudonymous personal data — so no-expiry becomes retention without purpose once the job stops for good (Art. 5(1)(e)) | 2026-08-09 |
| 27d | **The upload credential cannot DELETE — and it CAN, so this row is a GATE and it is OPEN** | `aws s3api delete-object` with the box's credential; then `get-bucket-policy` and `get-bucket-acl` to establish whether a policy could repair it | **Measured FALSE 2026-08-09, and the reason is structural.** `delete-object` **succeeded**. `get-bucket-policy` → `NotImplemented` on OVH, so the S3 instrument does not exist here; `get-bucket-acl` → the backup user **OWNS** the container with `FULL_CONTROL`, and the owner exemption defeats **implicit** deny. ADR 0125 Decision §3's credential-without-`DELETE` — the property that chose OVH over Hetzner Storage Box — is therefore **not in force**. ⚠ **An earlier draft of this row said no policy could repair it, and that is the expensive kind of wrong:** it reasoned from the absence of *bucket* policies to the absence of any instrument. OVH's **user policy** attaches to the S3 user, is invisible to `get-bucket-policy` by construction, and its documented evaluation checks an **explicit** `Deny` FIRST, before any ACL fallback — the owner exemption defeats only implicit deny. **Repair, and it is a measurement before a plan:** apply an explicit `Deny` on `s3:DeleteObject` as a user policy, then counter-measure that `put-object` still succeeds and `delete-object` returns `AccessDenied`. Compatible by construction — the script issues no delete verb, promotion is `PutObject`. Re-creating the container under a different owner is *a* repair and the expensive one. **Neither applied** — but **Klas gave GO on the user-policy repair 2026-08-12**, so the remaining work is the apply and its counter-measurement, not the decision. **Do it together with G3's lifecycle rules, and IN THIS ORDER, because after the `Deny` there is no cleanup path from the box at all:** (1) the box measurement `security-auditor` had open is **TAKEN 2026-08-12 and arm (1) does not fire — but on zero registered users, not on the grep.** `AspNetUsers` = 0, `job_seekers` = 0, registration closed. The grep itself is structurally blind to the success path: `deploy/caddy/` carries no `log` directive, so the only capture path is `http.log.error` at 5xx (4xx sits below the default level), and that class has **0** lines in a buffer from a container started 16:49Z the same day. Re-check when `AspNetUsers > 0`. Re-run it before shipping if the box has served real users since — `sudo docker logs jobbliggaren-caddy 2>&1 \| grep -nE 'bekrafta-(epost\|konto)\|aterstall-losenord'`, **`-n` and not `-c`: the arm is worded over REAL token-bearing rows, and a count cannot separate those from our own test traffic, so the hits are read rather than tallied**; (2) if any of them is real, clear it *before* anything is shipped, because that flips N-1 to Blocker; (3) satisfy G2 (query scrubbing), since `http.log.error` writes the whole URI plus an unredacted `Referer` on every 5xx and logship ships caddy's stdout; (4) then apply the `Deny` and the two lifecycle rules. **After the `Deny`, an emergency purge needs Klas's own OVH console credential — not the box's.** Retention then comes ENTIRELY from the provider-side rules, which is what makes the Art. 17 answer for the app leg true; without the `Deny`, an attacker holding the box's credential deletes the archive and with it the ability to demonstrate anything (Art. 5(2)). The pair is a net improvement over today's unbounded, box-deletable state either way — this is the order, not whether. Klas's. Blocks neither backups nor the drill; **it is a first-real-data gate** | |
| 28 | `age` and `rclone` are installable from apt on trixie, and the versions are recorded | `apt-cache policy age rclone`, then `age --version` / `rclone version`. **If either is absent this is a STOPP to security-auditor and Klas, not an improvised binary fetch** — `sops` was measured absent from trixie for #198, so this class of absence is live on this box rather than hypothetical | **Both present in `trixie/main`, so this row's STOPP branch is closed and the `sops` precedent did not repeat.** `apt-cache policy` → `age` candidate `1.2.1-1+b5`, `rclone` candidate `1.60.1+dfsg-4`, both from `debian trixie/main amd64`. Installed, then self-reported: `age --version` → `1.2.1`, `rclone version` → `rclone v1.60.1-DEV`. **Record both spellings, because they disagree and only one of them is a version:** Debian builds rclone without upstream's version metadata, so the binary says `-DEV` while the package is an ordinary release build — a reader checking the tool's own output against an upstream advisory would otherwise read a release as a pre-release and dismiss it. *(Mechanism, not inference: upstream stamps the version at link time via `-ldflags -X github.com/rclone/rclone/fs.Version=…` in its own release workflow, and a build without that flag reports the source default, which carries the `-DEV` suffix — [rclone build.yml](https://github.com/rclone/rclone/blob/master/.github/workflows/build.yml) and [packages.debian.org/trixie/rclone](https://packages.debian.org/trixie/rclone), both read 2026-08-10. Nothing consumes this version against advisories — [#1289](https://github.com/klasolsson81/jobbliggaren/issues/1289) owns that gap.)* | 2026-08-10 |
| 29 | A real nightly run produces both artefacts, and the box holds no plaintext afterwards | `systemctl start jobbliggaren-backup.service`, then `journalctl -u jobbliggaren-backup.service`. Expect `Result=success`, a `main/jobbliggaren-<stamp>.dump.age` object, and a promoted DEK generation. Then `sudo find /run /var/lib/jobbliggaren /tmp -newermt '-1 hour' -type f` — expect no dump-shaped file. The working directory is on tmpfs and is removed by a trap on every exit path, so a survivor here is a real defect | | |
| 30 | The freshness probe fires AND clears — both halves, because a probe that cannot clear is worse than none | Move the stamp back 27 h (`touch -d '27 hours ago'`), run `jobbliggaren-backup-fresh.service`, expect it in `systemctl --failed` naming a stale backup. Then run a real backup and expect the next firing to take it **off** the list with no operator action. The clearing half is the one #198's timer got wrong (`OnUnitActiveSec` on a `Type=oneshot`, systemd#21600) | | |
| 31 | **The restore drill (M-4).** A real artefact, decrypted off-box, restored, and read through the application — and the crypto-erasure semantics measured rather than asserted | `backup-restore.md` §5 end to end against a real object from the real target, on a synthetic user manufactured through production paths and then hard-deleted. Record the counts from step 5, and record **(b2)** as the erasure result — *not* (b). DEK rows are created lazily — on a user's first request carrying `IRequiresFieldEncryptionKey`, which is the trigger spelled out at the end of this row and is not the same as writing — so (b) "restored users with no key" also counts users who never made such a request and never had a key; (b2) counts users who have ciphertext but no key, which is the erased-user signature. Reporting (b) would overstate the drill. **And record (b2) with its scope beside it**, because this row is where the number is protocolled and a later reader reads the row rather than the runbook: (b2)'s `EXISTS` inspects `applications.cover_letter` alone, while `EncryptedFieldRegistry` carries **six** encrypted columns — a user whose only ciphertext was a note, a follow-up, a resume version or a parsed CV is invisible to it. It is an **existential** proof (one confirmed case proves the mechanism; a zero is a prompt to investigate), not a census. Also record that a non-deleted user's encrypted field decrypts through the app — without that, "unreadable" is indistinguishable from "the restore broke". **AND RECORD THE TWO STAMPS BESIDE THE COUNTS, because without them this row cannot prove its own precondition ([#1287](https://github.com/klasolsson81/jobbliggaren/issues/1287)):** (b2) is an erasure count *only if step 0 passed*. Under a reversed pairing — a DEK generation older than the main artefact, which is exactly what a run whose DEK leg failed after its main upload leaves offsite — a user whose **first key-creating request** fell between the two generations, and who has cover-letter ciphertext in the main artefact, also has ciphertext and no key — byte-identical to what (b2) counts. **Read that trigger precisely, because two likelier readings are both wrong.** It is not *registration*: no encrypted column sits on `job_seekers`, so a merely-registered user has no ciphertext at all and falls in (b), which is the very distinction (b2) exists to draw. Nor is it *writing*: the DEK row is minted by `FieldEncryptionKeyPrefetchBehavior` on the first request carrying `IRequiresFieldEncryptionKey`, and many of those are **read-only queries** — opening an application or a CV is enough. So the false-positive class includes a user who registered long before the DEK generation and merely *read* something in the window, and any wording narrower than that under-counts it. So write down `deks/verified.stamp` and the `<STAMP>` in the main artefact's own file name, and a later reader can check `stamp(deks) >= stamp(main)` for themselves instead of trusting that someone did. With the stamps absent, **a one is as much a prompt to investigate as a zero is** — the number would otherwise carry a meaning that rests on something this row never recorded. **Two limits on the stamps themselves, so this row is not read as more than it is:** a stamp that does not exist offsite is §5 step 0's REFUSAL branch and not a licence to run the drill and record the number anyway — go take a fresh pair; and the stamp carries no cryptographic integrity, being plaintext and writable by the same credential the box holds, so it is a check against operational error and never a tamper control | | |
| 32 | **The age private key exists in escrow, off-box — a GATE, not a report, and THIS is the escrow row still to be executed** | Klas confirms the identity is held off-box **in whatever form he has accepted** — mechanism-independent for the reason row 26 gives, and ~~"in the password manager"~~ is struck here for the same reason it was struck there (corrected 2026-08-12; this row is the OPEN one, so a cell contradicting its own instrument would be run wrong rather than merely read wrong) — and records the date. The box holds only the public recipient by design, so escrow is the **only** path from ciphertext back to data, and a lost age key makes every retained artefact permanently unreadable — the same finality as row 26, with a wider blast radius. **Undecided as of 2026-08-09; still OPEN, and it is the one of the two escrow rows that is not closed by row 26's decision.** **Klas decided 2026-08-12 that the same form — including the SAME device as `jobbpilot_vps_ed25519` — is acceptable here, over `security-auditor`'s objection. Accepted risk, recorded in ADR 0129 (gitignored per §6.5, like every ADR from 0071; if it is absent from your checkout, her objection and his ground are reproduced in this cell).** Her objection in her own terms, because row 26's argument does **not** reach this key: the SSH key grants root, therefore the OVH credential, therefore *download* of every artefact — but **not decryption**, and the age key is the only thing that gives that. On one device a single compromise yields plaintext out of every retained artefact, including the leg `jobbliggaren-logship.sh` calls *"THE ONLY LEG CARRYING DATA-SUBJECT PERSONAL DATA"*; and `backup-restore.md:437-439` reserves this choice for her once real data exists. **That reservation is unspent — this decision is taken pre-data, and she may re-open it there.** What is independent of all of it: ~~the identity has not been located~~ — **IT EXISTS SINCE 2026-08-12, AND KLAS CONFIRMED THE ESCROW THE SAME DAY.** The 2026-08-09 identity was found, and its private half was exposed in a chat transcript the same day; it is **REVOKED and must never be reinstalled** (still reachable from git history, so "replaced" is not enough to say about it). A fresh identity was generated 2026-08-12 and its recipient is committed at `deploy/backup/age.recipient` — **do not generate another**, the earlier directive to do so is discharged and acting on it would orphan the second. The rotation was free because nothing was encrypted to the old one: measured 2026-08-12, the box carries only the heartbeat and reconcile units — **no backup unit is installed at all, so nothing could have run** — and rows 29/30/31 are all empty. (`/run/jobbliggaren/host-secrets` also holds **0** files, but read that as *no credential is injected now*, not as "none ever was": `/run` is tmpfs and a reboot empties it, so the count is consistent with a credential injected and lost. The absent unit is the load-bearing evidence; the empty directory is corroboration.) **The container listing that closes the premise from OVH's side was taken by Klas 2026-08-12: the container is EMPTY.** Both listings are PRESENT-TENSE and share a null hypothesis, so they corroborate rather than prove: neither can separate "never written" from "written and removed", and rad 27c measured versioning and Object Lock both OFF. What proves it is upstream of both — **zero registered users** (see row 27d and ADR 0050's N-1 row — not this row, which carries the count nowhere), so no personal data existed to be in any artefact. On that ground nothing was ever encrypted to the retired identity, the rotation cost nothing, and no Art. 4(12) personal-data breach arises (so no Art. 33 clock). The instrument was the whole container without a prefix argument, form before name, because a prefix listing only finds the prefixes someone remembered. After the first backup lands, a rotation stops being free and becomes a permanent loss of every retained artefact, which is what this gate protects. Do not treat the offsite artefacts as a recovery path on an empty cell here. Backups may be *taken* meanwhile; encryption needs only the recipient | Klas confirms the identity is held off-box, in the accepted form (ADR 0129): plaintext on his own devices, beside the four crypto values. Confirmed 2026-08-12 | **2026-08-12** (the 2026-08-12 identity; the 2026-08-09 one is REVOKED) |
| 32b | **The secrets-ownership gate fires AND clears (#1295)** — both halves, for row 30's reason: a gate that cannot clear itself is worse than none, because the operator learns to ignore the surface it lands on. This is also the ONLY place the real `docker run … 'id -u; id -g'` is exercised; CI stubs it, as it stubs cosign | Runs **after** injection, so it belongs to the cutover and not before it. Break the group deliberately — `sudo chgrp 0 /run/jobbliggaren/secrets` — then `sudo systemctl start jobbliggaren-reconcile.service`. Expect: the unit in `systemctl --failed`, its journal naming the **traversal** axis and printing both ids, and `sudo docker compose -f /opt/jobbliggaren/deploy/docker-compose.yml ps` **unchanged** (stale but serving). Then run the repair the message itself carries — re-own, never re-inject — start the unit again, and expect the apply to proceed and `systemctl --failed` to be empty with no further operator action. **Record both halves; a fired-but-never-cleared gate is a half-measured one.** **AND RECORD THE POSTURE, not only that the gate cleared** — `sudo find /run/jobbliggaren/secrets -maxdepth 1 -exec stat -c '%n %u:%g %a' {} +`, expecting the directory `0:<gid> 710` and every file `<uid>:<gid> 400`. **That form is a find and is numeric for two separately measured reasons (2026-08-15, this row's own drill):** a `sudo … /run/jobbliggaren/secrets/*` glob is expanded by the operator's shell before `sudo` elevates, and `0710` denies an operator outside the container group the read, so it fails with `No such file or directory`; and `%U:%G` renders the container's uid/gid as UNKNOWN — `root:UNKNOWN` on the directory, `UNKNOWN:UNKNOWN` on the files — because they have no `passwd`/`group` entry on the host, so the group axis is unreadable in the name form even where the owner axis is not. The earlier form of this cell had both, so the posture line this row calls load-bearing could be neither run nor read. The production ownership triple is measured HERE and nowhere else: the CI fixture runs unprivileged in a directory it owns, so it pins the comparison and never the ownership | **BOTH HALVES RUN, and the drill produced a defect of its own.** **Fires:** `sudo chgrp 0 /run/jobbliggaren/secrets` (posture `0:0 710`) → `jobbliggaren-reconcile.service` **`failed`**, journal `REFUSING: the incoming api image cannot TRAVERSE /run/jobbliggaren/secrets. directory group is 0; the image runs as gid 1654` — the traversal axis and **both** ids, as the row requires — followed by `Nothing is applied; the running containers stay up`. `docker compose ps` **unchanged** (api `Up 5 minutes (healthy)`, whole stack up) and the site still answered, i.e. stale but serving. **Clears:** → `reconcile complete; stamped`, `systemctl --failed` **empty**, `heartbeat: all predicates hold`, with no operator action beyond the repair. **Posture, the axis no gate reads:** directory `0:1654 710` — owner **root**, not 1654 — and all five files `1654:1654 400`. So the `chown -R` trap this row exists to catch was not sprung. ⚠ **SCOPE OF THE CLEAR, because "run the repair the message carries" is only half true here.** The drill broke the directory's **group** and nothing else, so the files never needed re-owning: the message's *directory* line alone was load-bearing, and it is the line that cleared the gate. **The files line was never exercised** — which matters, because that is the line the drill found defective. ⚠ **The refusal message's own repair was not runnable by the operator it addresses:** its files line was `sudo chown 1654:1654 /run/jobbliggaren/secrets/*`, and the glob is expanded by the operator's shell before `sudo` elevates — `0710` grants the group `--x`, traverse without read, so **every non-root user** is denied the listing the glob needs, and `ls` on that directory is `Permission denied` for the account that runs these drills. The pattern reaches `chown` unexpanded. It had six homes: both refusal branches, both copies in `master-key-ops.md`, and the instrument cells of rows 27 and 32b. **The two fixes are different forms and must not be collapsed** — the *repair* commands are now `find -mindepth 1 -maxdepth 1 -exec chown … {} +`, which excludes the directory structurally; the *posture proofs* are `find -maxdepth 1 -exec stat … {} +` **without** `-mindepth`, because there the directory is precisely the operand the line exists to show. ⚠ **Owed, and it is the reason this row is not a clean tick:** the `find` repair form has **never been run on the box** by a non-root operator. The glob form was measured broken; the replacement is measured only in the fixture suite, where it is now pinned in all three arms by `assert_no_secrets_glob` and `assert_bounded_find_repair` (both mutation-verified to fail when the defect returns). One no-op run next visit — `sudo find /run/jobbliggaren/secrets -mindepth 1 -maxdepth 1 -exec chown 1654:1654 {} +`, exit 0, posture unchanged — closes it | **2026-08-15** (fires and clears, posture recorded; the *files* repair line is unexercised) |

### Rows 33–38 — outbound email (#183)

⚠ **ROWS 33–34 AND 37–38 ARE RETIRED WITH THE PROVIDER THEY MEASURED (#183, 2026-08-15). ROWS 35 AND
36 ARE LIVE.** Row 35's property was never SES-specific and it now carries a runnable
Scaleway-console instrument; row 36 was never about SES at all. AWS refused production access
permanently
(2026-08-14), the SES arm was deleted rather than repointed (E1, `b71c14de`), and
`docs/runbooks/email-ses-setup.md` — which this block used to cite for procedure and rationale —
**was deleted in E2 rather than edited**: a stale operational runbook is more dangerous than none,
because it reads as current (Klas decision 2026-08-15, #183 decision 4). **Its replacement is E4's
and is written out of the steps Klas actually performs at the flip; nobody invents Scaleway console
steps here, and no row below has been repointed at Scaleway.**

**Rows 33 and 34 are kept as measured history, not as instruments.** They record that the domain was
DKIM-verified at AWS on 2026-08-10 — true when written, about a provider that is gone. Do not run
them, and do not read a green cell as coverage of anything shipping now. ⚠ **Row 35 left this set
on 2026-08-16 and is LIVE again** (#183 FU-2): its property was never SES-specific, and it now
carries a runnable Scaleway-console instrument instead of a dead `aws sesv2` call. *(This paragraph
said "rows 33–35" and named the configuration-set measurement as history until then. Read row 35's
own cells; this line is not its status.)* **Rows 37 and 38 were open and are now moot in that form**: row 37's
instrument is an `aws sesv2` call against a deleted arm, and row 38's DMARC `rua=` is a real and
unfinished piece of work whose owner is the flip, not this block. E4's replacement rows carry both.

**ROW 36 SURVIVES BECAUSE ITS SUBJECT WAS NEVER SES.** It is a **control** row: the risk in this
work was never to the new sender but to the old one — the domain publishes `p=reject`, and Klas's
ordinary STRATO mail survives it through DKIM alignment. That property is provider-independent, it
is owed **after every DNS change** and not once, and the 2026-08-15 DNS change made it sharper
rather than obsolete. ⚠ **Two of its five legs now have expectations that are measured FALSE, and
they are recorded here rather than repaired, because the repair is a DNS decision and DNS is E0 —
Klas's, deferred by him on 2026-08-15.** Measured against 8.8.8.8 on 2026-08-15: the apex MX is
`blackhole.tem.scaleway.com` and no longer `smtp.rzone.de`, so `kontakt@jobbliggaren.se` receives
no mail; and the apex TXT carried `v=spf1 include:_spf.tem.scaleway.com -all` where the row
expects none at all, which put Klas's own STRATO envelope in SPF **hard fail** under
`p=reject`, saved by `strato-dkim-0002`/`-0003` alone. ⚠ **THAT SECOND HALF IS SPENT: measured
2026-08-28 the apex reads `v=spf1 include:_spf.tem.scaleway.com include:_spf.strato.com -all`.**
Klas added STRATO's include that day, so his envelope now passes SPF **and** DKIM and no longer
hangs on the two selectors alone (`security-auditor` Minor 2, 2026-08-28, discharged). **The
leg itself stays open** — the row still expects no TXT at all, so the expectation is measured
false in a second way. **Read those two legs as open questions for E0, never as
deviations to "repair" toward the old expectation** — and read the remaining three legs (the two
STRATO DKIM selectors, and `_dmarc` for `p=reject` as **exactly one** record) as fully live, because
they are the mechanism carrying his mail today. The row was open for a second reason, and it is now
discharged: the 2026-08-10 measurement covered two of its five legs, and a control row whose
instrument under-reaches its own property reads as coverage it does not have. ✅ **All five
legs were read on 2026-08-28 in one pass** — see the row's own Measured cell. Read that as the
standard for this row rather than as a one-off: a partial reading here is the failure mode, so
re-run all five or record none.

| # | Property (gate) | Instrument | Measured | Date |
|---|---|---|---|---|
| 33 | **RETIRED (SES era — history, not an instrument), and the records are GONE, not merely unused.** The three Easy DKIM CNAMEs resolve **from outside**, with the token repeated on both sides | `nslookup -type=CNAME <token>._domainkey.jobbliggaren.se 8.8.8.8` for each of the three tokens. **The tokens themselves went with the deleted runbook and are not reproduced here, deliberately — they are recoverable from git history and nothing outstanding needs them: all three were measured NXDOMAIN on 2026-08-15 (dotnet-architect, PR #1341), so no DNS cleanup is owed.** Query a public resolver, never the registrar's own panel — what matters is what SES can see. The failure this catches is STRATO appending the domain to a prefix that already carried it, which yields `…jobbliggaren.se.jobbliggaren.se` and is invisible for up to 72 h | **All three resolve, each to `<same token>.dkim.amazonses.com.`** Read against public DNS (Google DoH), never out of STRATO's panel. **The token identity was checked, not assumed:** the three published tokens were compared against the ones in the API response *before* publication, so this row proves the records carry SES's tokens and not a plausible-looking transcription. The double-append failure mode did not occur | 2026-08-10 |
| 34 | **RETIRED (SES era — history, not an instrument).** SES itself considers the domain verified — the only authority on that question | `aws sesv2 get-email-identity --email-identity jobbliggaren.se --profile jobbpilot --region eu-north-1`, read for `DkimAttributes.{Status,SigningEnabled,SigningHostedZone}` and top-level `VerifiedForSendingStatus`. Expect `SUCCESS` / `true` / `dkim.amazonses.com`, and `true`. **Unprojected deliberately:** this row is the authority, and the same response carries row 35's `ConfigurationSetName` absence — one call, two rows, which is also why 2026-08-10's re-read of row 35 came free with this one. (§3 step 5 reads the same four fields through a projection; that is the convenience form.) The narrow two-field `--query` this row carried until 2026-08-10 could not return the two signing fields the Measured cell reports. The identity and its tokens are per-Region, so the region flag is part of the measurement, not decoration. Until this row is green the identity exists but cannot sign, and every send fails | **`DkimAttributes.Status SUCCESS`, `VerifiedForSendingStatus true`, `SigningEnabled true`** (`PENDING`/`false` before), `SigningHostedZone` unchanged. It turned in under half an hour against the 72 h AWS reserves — which is a fact about this publication, not a propagation rule to plan the next one on. **This row is the authority and row 33 is not:** records that resolve for us can still be records SES has not re-read. **What it does not establish:** the account remains in the sandbox (quotas and recipient count were `get-account`'s and `list-email-identities`' to report, not this row's), no message has been sent (row 37), and `Email:Provider` is unset — signing capability is not the flip. **The sandbox is why this row is retired rather than superseded: AWS refused to lift it permanently on 2026-08-14, which is what ended the SES lane** | 2026-08-10 |
| 35 | **RE-INSTRUMENTED 2026-08-16 (#183 FU-2) — the PROPERTY was always provider-independent and now has an instrument that can actually be run.** No open/click tracking arises on the sending identity — #183's ROPA acceptance criterion, and `security-auditor`'s own condition on a processor swap. `ConfigurationSetName` was the AWS *form* of this property and has no Scaleway analogue; the property is unchanged. `release-checklist.md` §2.5 point 1 precondition 4 is the gate and owns the status — read it there, not from this cell | ~~`aws sesv2 get-email-identity … --region eu-north-1`~~ names a deleted arm and an AWS-only attribute; it is kept **struck, as provenance**, exactly as row 37 keeps its own. **The live instrument is a console reading, and it is Klas's:** open the Scaleway console → *Transactional Email* → the `jobbliggaren.se` domain, and read the project's **provider-side state** — no tracking configuration on the sending identity, `Webhooks`, `Blocklist`, and `Settings → Activity report`. **The console is authoritative and the product changelog is not** — release notes are an inference about features, and the 2026-08-16 reading proves the point empirically: it found an enabled setting no changelog entry announced. **Never substitute the changelog for the console.** Two things this instrument cannot do, stated so nobody reads more into a green cell: it is **provider-side state that no test in the repo can pin** (the request-level absence was pinned by `SesEmailSenderTests`, deleted with the arm in E1, and `ScalewayEmailSenderTests` carries no equivalent because there is no request field left to assert), and it is **a reading of a day, not a redemption** — re-run it when leg (e) is signed, and after any identity or project edit | ✅ **Read 2026-08-16 (Klas).** **The property holds: no recipient-level tracking configuration exists** — TEM has no open/click tracking at all, no send-API field and no configuration-set analogue, and the capability sits as an open feature request with the provider. ⚠ **But the absence claim does NOT hold, and that is why this cell is worth its length:** `Settings → Activity report` is **ON, weekly**, delivered to the controller's own mailbox. **What it carries is aggregate per domain and never recipient-level**, so the property survives it — but *"no provider-side configuration exists"* would have been **false**. **The field enumeration and the Datakategori condition that governs a change to it live in the ROPA, which is that post's home; this cell records the verdict and not the contents.** Also read: `Webhooks` **0** (empty; TEM allows one per domain, so the surface is known and bounded), `Blocklist` **0** (empty), `Team access` **1** member (OWNER, the controller's own address), region **PAR** (`fr-par`), DNS **Verified**, and `Processed 4 / Delivered 4 / Rejected 0` — aggregate delivery statistics per domain, which nobody should read as tracking. **The webhook and blocklist counts are measured on the provider's side deliberately**: a `git grep` over our source proves only that *we* ship no webhook, never that none exists, since one is registered in the console without a line of code changing. ⚠ **Retention of the Activity report's own content is unmeasured and is NOT covered by ADR 0133** — that acceptance reaches leg (c)'s subject, *message content and recipient-bearing metadata*, and the report is neither (it is per-domain aggregate to the controller's own mailbox). An earlier form of this cell cited ADR 0133 over it, which widened an acceptance by citation; the ADR forbids that move in its own words (*"Neither ground survives a widening"*). Materially harmless while the report carries no recipient-level data — **and if its content ever changes to something recipient-bearing, that is a ROPA Datakategori change and is owed there before the setting is left on** | 2026-08-16 (console; SES-era cells 2026-08-09/2026-08-10 are history) |
| 36 | **CONTROL — the existing STRATO mail path is untouched by the transactional-email work. Provider-independent: its subject is Klas's own outbound business mail, which was never sent by SES and is not sent by Scaleway.** *(Two rows in this block are live, on different grounds: this one because its subject was never the provider, row 35 because its property never was.)* | `nslookup -type=TXT strato-dkim-0002._domainkey.jobbliggaren.se 8.8.8.8` and `-0003` (both still `v=DKIM1` — **these two now carry alignment ALONE, see the apex TXT leg below, so this is the leg whose failure silences his mail**), `nslookup -type=MX jobbliggaren.se 8.8.8.8` (⚠ **expectation measured FALSE 2026-08-15: `blackhole.tem.scaleway.com`, not `smtp.rzone.de`** — `kontakt@jobbliggaren.se` therefore receives no mail, and that address is `EmailTemplates.ContactAddress`, the Reply-To on every send and the published Art. 12 contact. Klas deferred this on 2026-08-15 pending the STRATO email package; the escalation schedule has ONE home — `release-checklist.md` §2.5 point 1 leg (e) precondition 5 — and this row cites it rather than restating it. ⚠ **The clause this row carried until 2026-08-16 ("BLOCKER at the first real user or at the flip, whichever comes first") is spent in its second half: the flip HAPPENED on 2026-08-16 and the ruling is NO Blocker.** Read the live schedule there, never here. Do NOT restore the old expectation as a "repair" — record what resolves. ⚠ **MOVING APEX-MX FALSIFIES THE PUBLISHED PRIVACY POLICY IN THE SAME SECOND, AND NOTHING ELSE IN THE REPO WILL SAY SO.** The policy names STRATO GmbH as the processor for incoming mail and marks that processing as planned and not yet in operation, in both locales. The move happens in STRATO's panel with **no PR, no CI and no deploy** — it is the first activation event in this house that no release gates — so the suite stays green through exactly the window in which the copy lies, which is ADR 0090 D3's transparency defect. **The copy flip and the tripwire's marker half land in the SAME change, and the tripwire's floor and path-parity are KEPT.** Regenerate §2.6 point 1's marker set from its own greps; never compute it from the old set. The second surface is the STRATO block in `web/jobbliggaren-web/src/lib/i18n/content-legal-parity.test.ts`, which carries this same conclusion for the reader who runs CI rather than for the one standing at the panel — **refine the conclusion and BOTH surfaces change; a fix on one of the two is no fix, in either direction**, and nothing links them. ⚠ **The same move voids ADR 0132's AND ADR 0133's risk acceptances**, since both rest on the same bound (`security-auditor` 2026-08-28) — that is part of what the move costs, not a side effect to discover afterwards. ⚠ **The order is not cosmetic, and the gate that enforces it already exists:** open the copy-flip PR with `automerge` set at creation and **`agents-done` withheld** until this leg reads STRATO. Intent is not permission (#836), and a PR that merges before the move asserts a processing that is not happening — the same defect mirrored. Then move MX, re-measure this leg, set `agents-done`, deploy, re-measure the other four legs, and only then row 38's `rua=`. **Klas moves MX; CC never does**), `nslookup -type=TXT _dmarc.jobbliggaren.se 8.8.8.8` (**still carries `p=reject`, and still EXACTLY ONE record**. Read it that way and not as byte-equality: row 38 adds `rua=` through this same control, and §4 states that is purely additive, so a `rua=` present here is row 38 working rather than a deviation. Getting that wrong is the expensive direction — an operator who has just read §4's warning that `_dmarc` is destructive to touch would "repair" the mismatch by deleting `rua=`, silently undoing row 38 with no row going red. This leg was added 2026-08-10 because the row's Property covers the policy governing Klas's mail path while its instrument did not read it, and the two ways to lose that policy are both silent: STRATO's DMARC control set to "Ingen" deletes it, and a second **DMARC** record makes DMARC not apply at all per RFC 7489 §6.6.3, which discards anything not starting with `v=DMARC1` and only then terminates on a surviving set of more than one — hence the two-part expectation. Reading for exactly one TXT record here is stricter than the RFC needs, and deliberately so: on a control row an over-strict test fires when nothing is wrong, never the reverse. The mechanism, kept here because the runbook that owned it is deleted: STRATO's DMARC control set to "Ingen" deletes the record outright, and both losses are silent), and `nslookup -type=TXT jobbliggaren.se 8.8.8.8` (⚠ **expectation measured FALSE 2026-08-15: the apex now carries `v=spf1 include:_spf.tem.scaleway.com -all`, where this row expects no TXT at all.** **SPF is evaluated against the ENVELOPE (MAIL FROM) domain, never the From header** (RFC 7208 §2.4) — that is the whole of why this leg exists, and it is the sentence the deleted runbook's §5 carried. Klas's ordinary STRATO mail has an envelope on `@jobbliggaren.se`, so it IS governed by this record, and a `-all` listing only Scaleway puts it in **hard fail** under `p=reject` — surviving on the two `strato-dkim-*` selectors alone, where before there were two independent mechanisms. STRATO's documented record is `v=spf1 redirect=_spf.strato.com`; adding an include for it beside Scaleway's restores the margin. **That is Klas's decision (E0) and is escalated, not repaired here** — and it affects his own business mail today, not test users) | **Five legs, all read against 8.8.8.8 in one pass — this row's own standard, never a subset.** `strato-dkim-0002` = `v=DKIM1; k=rsa` and `strato-dkim-0003` = `v=DKIM1; k=ed25519`, both **TXT and not CNAME**, one record each; the key material is public DNS data and is deliberately not reproduced here — record the property, per row 33's precedent. `MX` = **`10 blackhole.tem.scaleway.com`**, unchanged, so `kontakt@jobbliggaren.se` still receives nothing. **Klas confirmed the same day that sending FROM that mailbox works while receiving does not, and the two are not in tension:** outbound never consults MX and inbound is routed by nothing else, so a working send proves nothing about the leg this row measures. `_dmarc` = **`v=DMARC1; p=reject`, exactly one TXT record**, still no `rua=` (row 38). Apex `TXT` = **`v=spf1 include:_spf.tem.scaleway.com include:_spf.strato.com -all`**, changed that day by Klas; the preamble above is corrected to match rather than left to read as current. | 2026-08-28 (all five legs) |
| 37 | **RETIRED IN THIS FORM (SES era) — the PROPERTY survives and is E4's to re-instrument.** A real message is accepted with an **aligned** signature — DNS resolving is not delivery | ~~`aws sesv2 send-email`~~ names a deleted arm and an account AWS confined to sandbox permanently. The property is unchanged and E4 re-states it against Scaleway: send a real message, then read `Authentication-Results` in the received original. Required: `dkim=pass header.d=jobbliggaren.se` **and** `dmarc=pass`. Record `header.d`, not merely the word "pass" — a `dkim=pass` on some other domain is not alignment and would not survive `p=reject` | | |
| 38 | **RETIRED IN THIS FORM — the work is UNFINISHED and belongs to the flip, not to this block. IT CARRIES A GDPR OBLIGATION THAT IS NOT DISCHARGED, re-anchored here from the deleted runbook's §4 (`security-auditor` Major 2, PR #1341), because that was its only home and it comes due in about a week.** DMARC aggregate reports actually arrive, which the record's presence does not establish | ⚠ **Before the record is published, not after:** aggregate reports carry a `source_ip` for **every** sending source, spoofers included, and an IP address can be personal data (C-582/14 *Breyer*). Receiving them is ordinary network security — Art. 6(1)(f), recital 49 — so the basis exists, but it must be **named**, and the mailbox is a **new inbound flow to a processor that appears nowhere in the privacy policy's recipient list**: STRATO. So when this row is measured, record the legal basis, the mailbox's retention, and STRATO as a sub-processor wherever the flip's paperwork lands (E3). Then publish `rua=mailto:dmarc@jobbliggaren.se` and confirm a report is received within ~48 h. **Blocked on the same STRATO email package as the apex-MX leg of row 36**: the mailbox does not exist yet (Klas, ~1 week from 2026-08-15). The address **must** be on `jobbliggaren.se`: RFC 7489 §7.1 makes an off-domain destination conditional on a `<policy-domain>._report._dmarc.<destination>` record, and `jobbliggaren.se._report._dmarc.gmail.com` was measured NXDOMAIN 2026-08-09 — a Gmail `rua=` would look correct and collect nothing | | |

- **Backup and restore** — [`backup-restore.md`](backup-restore.md) (#197): the nightly job,
  the split-dump model, retention, and the restore procedure. **The target is CHOSEN and MEASURED
  (2026-08-09): OVHcloud Object Storage, `jobbliggaren-backups`, region `eu-west-par`** — rows
  27b–27d. **The Art. 28 DPA is NOT signed**; an account with credits is not a processing
  agreement. **Escrow of the age private key is ~~the same gate as row 26 above and equally
  undecided~~ — the two separated on 2026-08-12: row 26 is decided and dated, row 32's FORM is
  decided (ADR 0129, same device permitted) and the identity exists since 2026-08-12, so the
  row was open on nothing but Klas's escrow confirmation — which he gave 2026-08-12, so ROW 32 IS
  CLOSED AND DATED and both escrow rows are now shut. The 2026-08-09 identity is REVOKED.**
- **Master-key operations** — [`master-key-ops.md`](master-key-ops.md) (#198): the injection
  procedure, the per-boot re-injection this model requires, rotation and its drill, and
  recovery. Key-**access** detection is not there either: it is dispositioned to #1201, and
  what #198 delivers is **absence** detection — since #1329 in two units, one per set:
  `jobbliggaren-secrets-present.timer` (crypto) and `jobbliggaren-host-secrets-present.timer`
  (#197's host-only credential).
- **Host detection and alerting (gate M-7)** — the obligation and its `Hemvist` are
  [#1201](https://github.com/klasolsson81/jobbliggaren/issues/1201); the host-level half
  (auditd or equivalent, file-integrity monitoring, log shipping off the box, something
  that pages a human) was re-homed there by Klas 2026-08-06 rather than being carried by
  #196's closure. **The mechanism and its own verification log now live in
  [`host-detection.md`](./host-detection.md)** — auditd watch rules, a journal retention
  floor, and a heartbeat that reads `systemctl --failed` on a cadence and pings a dead-man
  expecter. **Log shipping is deliberately NOT part of it** and stays with #1175.
- **The production log sink** — #1175, unbuilt and unowned. Docker's log rotation above is
  a disk control, not a log sink. **What changed 2026-08-10:** M-7 took the *cadenced
  reader* half that this file and `backup-restore.md` had informally annexed to #1175 —
  a heartbeat **will read** the failure list every fifteen minutes **once it is installed on the
  box**, which has not happened yet — the rows in [`host-detection.md`](./host-detection.md) §7
  are what discharge it, not this merge. #1175 still owns the sink,
  the off-box corpus, and the retention that would survive a root attacker.
- **Gate B-1's cutover — and the corpus still waits for it.** #198 shipped the repair (see §2
  and [`master-key-ops.md`](master-key-ops.md)), but **shipping a mechanism is not closing a
  gate**: the key is not moved on this box until the operator performs the cutover, and B-1 is
  discharged only when rows 21–25 in §5 carry measurements. **The cutover HAS since been
  performed — 2026-08-12 injection, 2026-08-15 reboot drill and rotation to `local-v2` — so the
  first clause is history; the second still binds. ~~TWO rows hold it, not one: row 23's second
  half is unmeasured, and row 22 carries a measurement that says its Property is FALSE.~~
  **BOTH OF THOSE WERE DISCHARGED 2026-08-16** at the [#734](https://github.com/klasolsson81/jobbliggaren/issues/734)
  visit: row 22's journal is clean for all four secrets and the generation is rotated to
  `local-v3` ([#1343](https://github.com/klasolsson81/jobbliggaren/issues/1343) closed), and row
  23 carries both halves. **Read what that does and does not settle.** The predicate as written —
  *"rows 21–25 carry measurements"* — is now **literally satisfied by all five**, because row 24
  does carry one. ~~The reservation against 24 is about that measurement's interpretation~~ —
  **that reservation is gone as of 2026-08-16.** api's own log names the refusal
  (`InvalidOperationException` naming the absent master-key file and the remedy), obtained with a
  targeted stop-rename-start rather than a reboot, so the *designed*-refusal half rests on the
  application's own words instead of on correlation. `139` is still SIGSEGV and is filed as its own
  defect ([#1355](https://github.com/klasolsson81/jobbliggaren/issues/1355)) — a defect in the exit
  code, not in the refusal.

  **B-1 IS CLOSED (Klas, 2026-08-16).** All five rows carry measurements and the Properties hold.

  ⛔ **That discharges B-1 and releases nothing on its own**, and
  `JobTech__IngestEnabled=true` is **not** authorised by it. The gate that bites at the corpus load
  is **Art. 28** — `release-checklist.md`'s corpus gate is its single home; read the state there,
  never here. ⚠ **Discharging it releases nothing on its own** — the
  same misreading this paragraph exists to prevent applies to it exactly as it applied to B-1. **#1199** and **#1201** are open beside it. ⚠ **#1199 is where the Netcup DPA lives** — it is
  that issue's fifth acceptance criterion, the blocking Klas-owned one, and the DPA has **no
  separate issue**. #1199 is *broader* than the DPA (policy copy, ROPA, `BUILD.md`, the parity
  test), which is the trap: it can close on those and leave the blocking leg untracked if a reader
  assumes the DPA is filed somewhere of its own. An earlier draft of this paragraph said exactly
  that, and it was measured false.

  ⚠ **#1201 is not an equal third at this moment, and that is a grading, not an opinion.** It is
  gate **M-7**, graded `Major` by `security-auditor` 2026-08-04 with a conditional escalation
  written into the issue: *"M-7 becomes a `Blocker` if ADR 0123 is still ungranted **or
  unmitigated** at first real user data."* ⚠ **That is a disjunction of two arms, and only the
  first is discharged — and discharging it buys less than it looks.** Klas **granted ADR 0123 on
  2026-08-16**, which closes `ungranted` **literally**. ⚠ **Functionally it gives no coverage at
  the moment M-7 is evaluated:** the acceptance holds expressly only *"while the box carries no
  real user data"*, and M-7 is evaluated **at** first real user data — so it lapses by its own
  terms exactly where the condition is read, which is the act this paragraph governs.
  `unmitigated` is measured **OPEN** besides — neither the separate automation key nor the
  `Cmnd_Alias` narrowing is built, and that file's own provisioning step still sets
  `jpadmin ALL=(ALL) NOPASSWD:ALL`. ⚠ **And it cannot be closed by building them:** both are void
  as written, measured 2026-08-17. The derivation has one home —
  `vps-base-hardening.md` §11.
  ⛔ **`security-auditor`'s ruling 2026-08-17: M-7 DOES convert at first real user data**; see
  `release-checklist.md` §2.6 point 3.5 for what would actually discharge it. ⚠ **Do not enumerate
  it here** — she restated requirement (1) the same day, and the earlier enumeration named as
  necessary work the two mechanisms she now expressly excludes. The condition rests on the
  CAPABILITY, never issue numbers (#196 has been closed since 2026-08-08; both legs are homed at
  #1201). Hers to grade, not a later reader's to
  derive. Both of #1201's detection legs are still unverified on
  `host-detection.md`'s verification rows. ⚠ **Whether the second arm is discharged is `security-auditor`'s to say and
  never a later reader's to derive** — #1201 states that of this grading in as many words, and
  CLAUDE.md §9.6 puts severity with the reporting agent. Do not read the grant as closing M-7.

  This paragraph is the reader for all of that: **if it and the rows ever disagree, the
  disagreement is the defect.**

  ⚠ `company_register` is a separate question and this closure says nothing about it either: it has
  no encrypted column and holds registered legal entities' business data — ADR 0091 says *"not `personuppgift` **in the ordinary case**"*, and that hedge is its own, kept because a registered name can carry a natural person's — with sole traders excluded
  twice (SCB-side `Juridisk form ≠ 10`, plus an `IsPersonnummerShaped` guard at persist). The
  defensible claim is **"no personnummer by design"** rather than "not personal data" — the erasure
  cascade files its columns under `SeparateProcessing`, not `NotRecruiterData`. **The rows 21–27 index paragraph in §5 carries this
  same correction and names this point in turn; if the two ever disagree again, that disagreement
  is itself the defect — and it has already happened once, in the direction of updating the row
  paragraph and leaving this one stale.** **Klas confirmed the sequencing
  2026-08-05: the stack may be deployed and every cutover proof taken with the key as it was,
  because the box holds no user data — but the 51 347 recruiter contact records must not land
  ~~until B-1 is closed~~ ~~until `release-checklist.md`'s CORPUS GATE is ticked~~ ~~until **#1240**
  is closed~~ until **Klas gives an explicit written GO**.** ⚠ **Corrected FOUR times on 2026-08-16,
  each time by the previous correction's own
  success.** First: B-1 closed and the corpus gate had not, so *"until B-1"* read as satisfied and
  would have permitted the very load it was written to prevent. Then the Art. 28 conditions were
  discharged the same day, and *"until the CORPUS GATE is ticked"* inherited the identical defect
  within hours. Then *"until the item authorises it"* inherited it again, because the item has one
  binary and no `authorises` state to read. Then *"until #1240 is closed"* — better, because a
  superset cannot be satisfied by one leg discharging, but still a **state**: an issue closes as
  duplicate, superseded or by a sibling PR's squash without any legal gate moving.
  **The durable form is not a state at all.** Klas's GO is a **decision**
  (`release-checklist.md` §2.6 point 3.5, Klas 2026-08-16): no discharge, tick, closure or
  measurement can satisfy it by inference. Nothing mechanical enforces it; this
  paragraph is the reader. Beside Art. 28 the corpus also waits on **#1199** — the policy/host
  row — whose fifth acceptance criterion **is** the signed Netcup DPA (blocking, Klas-owned; it has no issue of its own),
  and on **#1201**, whose M-7 grading can still convert to Blocker at first real data: Klas
  granted ADR 0123 on 2026-08-16, but that escalation is **two-armed** and the mitigation arm is
  open (see the rows 21–27 paragraph, which carries that reasoning and is its single home).
- **The edge binary is ours, not upstream's, and the scanned image must be the published
  one.** `deploy/caddy/Dockerfile` compiles caddy rather than taking it from the published
  tag, so "we run stock caddy 2.11.4" is no longer true when reading an upstream bug report.
  It is also non-deterministic in three ways — a floating builder tag, `apk upgrade`, and
  live Go module resolution — which means **a rebuild is a different artefact than the one
  trivy approved**. Whatever publishes must promote the image it scanned (scan and push the
  same loaded image, or push by digest), never rebuild from the Dockerfile for the push.
- **Publishing the images this stack pulls** — `.github/workflows/release-images.yml`,
  delivered by #1225 and no longer owed. It builds, Trivy-gates and pushes the five images
  on an hourly reconciler (`sha-<short>` + `latest`), because automerge merges as a GitHub
  App and app-triggered events start no workflow runs. Named here because an earlier
  revision of this bullet said it was "not built yet" and that a `docker compose pull`
  would find no tags — false since #1225, and a reader acting on it would conclude §3
  cannot run.
- **`infra/terraform/`** — a record of what once ran on AWS, not a starting point. Do not
  repair its names toward the current application: it injects options #802 removed, injects
  no master key (so a re-apply hard-fails at startup), and names Dockerfile paths that do
  not exist.
