# Runbook — the production deploy stack on the Netcup box

**Scope:** the container half of #196 — bringing the Compose stack up on the Netcup
RS 1000 G12, and the proofs that close ADR 0050's gates. The base host (SSH, nftables,
edge firewall, zram, unattended-upgrades) is delivered and documented separately in
[`vps-base-hardening.md`](./vps-base-hardening.md); nothing here repeats it.

**Authority:** ADR 0050 and its `Amendment 2026-08-04` (host, capacity conditions, gates
M-5a/M-5b/M-6/M-7, decisions K1–K4) and the `Amendment 2026-07-18` (Option B, six
invariants). Where this runbook and an ADR disagree, the ADR wins and this file is wrong.

---

## 1. What the stack is

Seven services in one Compose project (`deploy/docker-compose.yml`), all images pulled
from GHCR — nothing is built on the box, which is capacity condition 1.

| Service | Role | Publishes |
|---|---|---|
| `caddy` | TLS termination, K2 basic-auth gate, reverse proxy to `web` | **80, 443 — wide** |
| `web` | Next.js. The only thing Caddy proxies to (Option B) | nothing |
| `api` | ASP.NET API. Reached only over the internal network | nothing |
| `worker` | Hangfire jobs | nothing |
| `migrate` | Oneshot; gates `api`/`worker` via `service_completed_successfully` | nothing |
| `postgres` | Data | nothing |
| `redis` | Sessions, cooldown gates, landing-stats cache, and the company-register cache | nothing |

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
  .env             # root:root 0600 — DB + edge credentials. NEVER COMPOSE_FILE, and since
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
described in ADR 0049 `Amendment 2026-08-09` and its acceptance lives in ADR 0123, which is
still `Proposed`.

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
> - **Install and enable `jobbliggaren-secrets-present.timer` in the same step.** A
>   crash-looping container never appears in `systemctl --failed`; that timer is what puts the
>   condition on the box's only alarm surface.
>
> Both are the install block in [`master-key-ops.md`](master-key-ops.md) §2. Run it **before**
> the first `docker compose up`, then inject (§3 of that runbook) and confirm
> `--check` passes. Nothing mechanical enforces this ordering — that is what these lines are.

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

**Rollback is an image tag — for CODE. It is not a rollback for SCHEMA.** Pin
`IMAGE_TAG=sha-<short>` in `.env` and re-run the reconcile unit, and the four long-running
services are back on the old build in seconds. But `migrate` runs EF migrations before `api`
and `worker` start, and pinning an older tag re-runs an older `migrate` against a database the
newer one has already changed. EF has no down-migrations here, and this repo has measured
cases where a schema change destroys data irreversibly (a computed column reverted to an
ordinary one; a `DROP COLUMN` taking its indexes silently). So: **an image-tag rollback across
a migration boundary is not a rollback, and nothing in this stack currently stops one** —
[#1236](https://github.com/klasolsson81/jobbliggaren/issues/1236) owns that control. A Netcup
snapshot is **not** deploy rollback either: snapshots are copy-on-write, need 50 % free disk,
only *offline* ones are consistent, and one exportable snapshot remains. Their role is
**before a migration**, once real user data exists — which is precisely the boundary the tag
cannot cross.

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
sudo chmod 0755 deploy/systemd/jobbliggaren-reconcile.sh deploy/systemd/verify-image-attestation.sh
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

**The timer fires at :47, offset from the publish run's :17 — and the hazard is cross-image
skew, not a half-published single image.** The publish job is a five-cell matrix with no
fan-in, so between the first cell's push and the last one's, `latest` resolves to the new
build for some images and the previous one for others; a pull landing there installs a mixed
set. (It cannot be the other split: the workflow pushes `sha-<short>` **before** `latest`, out
of one locally built image.) **The offset narrows that window and nothing closes it** — each
cell allows 30 minutes, so a slow `:17` run can still be pushing at `:47`. Attestation does
not close it either: it binds who built an image, never which tree, so a mixed set verifies
end to end. Owed, not delivered — [#1238](https://github.com/klasolsson81/jobbliggaren/issues/1238).

**Rollback stays an image tag, with the schema caveat above.** Pin `IMAGE_TAG=sha-<short>` in
`deploy/.env` and run the unit: the pull resolves the pinned tag, every image is verified
against it, and `up -d` recreates only what moved. Seconds, and it is the primary rollback path
for the four long-running services. Across a **migration boundary** it is not a rollback at all
— see §3's paragraph and [#1236](https://github.com/klasolsson81/jobbliggaren/issues/1236).
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
| 3 | Postgres steady-state RSS against the 2 560 MiB cap | cgroup `memory.peak` (which retains the high-water mark across the whole uptime, so it captures any 02:00 run without watching one) + `memory.stat` anon/file | **Steady state measured, the deciding workload NOT.** Over 2 days 7 h of uptime: `memory.peak` **151.8 MiB**, `memory.current` 138.6 MiB against a `memory.max` of 2 560 MiB — **5.9 % of the cap**, and the split is 11.8 MiB anon against 117.1 MiB file, i.e. almost entirely page cache the cgroup charges to Postgres. **The nightly dump job did not exist when this was measured**, so the peak reflects idle operation plus one 80-migration boot, not the workload ADR 0122 called the least certain number. The headroom is large enough that this is reassurance rather than a result. #197 merged the job on 2026-08-09 with a timer set to 02:15 UTC — but **merged is not installed**: rows 28–31 below are empty, so nothing has yet measured that the units are on the box, let alone that a dump has run. [#1235](https://github.com/klasolsson81/jobbliggaren/issues/1235) owns closing this, and what unblocks it is row 29 carrying a date, not the merge. `pg_dump` runs inside this container's own cgroup, so its cost will land here when it does | 2026-08-08 (idle only) |
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


**These are UNMEASURED and are written as a checklist for the cutover, deliberately.** Until
#198 nothing in this log measured B-1's actual subject: row 17 measures swap hygiene, which
prepares the gate without discharging it. The rows are written *before* the cutover rather
than reconstructed after it, because `deploy/` gets no integration coverage from `ci` and the
cutover happens once.

Every command below captures the key into a shell variable from the tmpfs file and greps with
`-F "$K"`, so the value transits operator-shell RAM only and never scrollback. `unset K` at the
end. Procedure and rationale: [`master-key-ops.md`](master-key-ops.md).

| # | Property | Instrument | Measured | Date |
|---|---|---|---|---|
| 21 | `docker inspect` returns the key on **no** container, including exited ones | `K=$(sudo cat /run/jobbliggaren/secrets/FieldEncryption__LocalMasterKeyBase64)` then `sudo docker inspect $(sudo docker ps -aq) \| grep -cF "$K"` — expect `0`. `ps -aq`, **not** `ps -q`: the measured 2026-08-05 leak was state surviving container *exit*, so an inspect over running containers only would re-measure the wrong set. Structural half: `sudo docker inspect -f '{{.Name}}: {{range .Config.Env}}{{println .}}{{end}}' $(sudo docker ps -aq) \| grep -iE 'MasterKey\|Pepper'` — expect only `*_FILE=` **path** lines | | |
| 22 | No plaintext copy on persistent disk | `sudo grep -rIlF "$K" /opt /etc /root /home /var/lib/jobbliggaren /var/lib/docker/containers` — expect no output. `grep -c '^FIELD_ENCRYPTION_MASTER_KEY=' /opt/jobbliggaren/deploy/.env` — expect `0`, line **deleted** and not blanked. `sudo journalctl --grep "$K" -q \| head -1` — expect empty. **Named scope limits:** `/var/lib/docker/overlay2` is excluded (a box-generated key cannot be in an image layer — `.dockerignore:35-49`); and freed SSD blocks that once held the old `.env` line are unreachable by any grep, **which is why the cutover rotates the key rather than relocating it** — rotation makes those remnants worthless | | |
| 23 | The app boots and decrypts from the file-sourced key | `docker inspect -f '{{.State.Health.Status}}' jobbliggaren-api` → `healthy`. This **is** key evidence rather than a liveness check: `ValidateOnStart` plus the `LocalDataKeyProvider` constructor make an unreadable or invalid key a boot failure, so a healthy api has parsed and validated the file. Then read one encrypted field through the app (any page showing CV or profile data) — the DEK-level check alone cannot catch a re-wrap that generated a fresh DEK | | |
| 24 | Reboot survival is the DESIGNED failure, and self-heal works | `sudo systemctl reboot`. Expect: api `restarting` (crash-loop, fail-closed — no fallback key), `jobbliggaren-secrets-present.service` in `systemctl --failed` within ~2 min naming the missing files. Then inject and expect api `healthy` within one restart-backoff interval **with no `compose up` and no reconcile run**. Closes two unmeasured premises at once: the crash-loop-then-self-heal behaviour, and that the absence detector actually fires | | |
| 25 | The hourly reconcile is unaffected | after the cutover, one `systemctl start jobbliggaren-reconcile.service` → `Result=success`, stamp written, and the journal shows no interpolation error. The key is no longer referenced by the compose file, so there is nothing left to interpolate and **no `:?` guard for it remains anywhere** (measured: two references repo-wide, both removed by #198) | | |
| 26 | **Escrow exists off-box for all four secrets — and this row is a GATE, not a report** | Klas confirms the four values are in the password manager, and records the date here. With no at-rest copy this is the only recovery path, and losing a value is as final as rotating it after rows exist: the master key takes every encrypted field, the company-watch pepper every org.nr token (its plaintext was destroyed in place), the CV-fingerprint pepper every Ignored/Resolved decision. (The audit pepper is the fourth and the exception: nothing reads back against it, so losing it costs only the ability to link erasure-audit records to one another across the gap.) **Undecided as of 2026-08-09** — the CTO escalated it and bound it as a hard cutover prerequisite; §9.6 makes the acceptance Klas's to grant. **Do not cut over on an empty cell here** | | |
| 27 | The peppers were replaced, not carried forward | **Re-measure AFTER `docker stop jobbliggaren-api jobbliggaren-worker`** (`master-key-ops.md` §4 step 2), never merely "before cutover": while the old stack runs the write path is open, and a single `company_watches` row landing between the measurement and the injection locks that pepper permanently without the rotation noticing. Stopping the containers first makes measurement and rotation atomic with respect to new rows. Then, with raw SQL inside the postgres container (not through EF — soft-delete filters hide rows): `resume_finding_statuses`, `company_watches`, `user_data_keys` all 0. Measured 2026-08-09: all three were 0, `audit_log` 13 (its pepper is rotatable regardless — see row 26's note). Confirm the new values landed with `jobbliggaren-inject-secrets.sh --check` plus `sudo stat -c '%a %U:%G' /run/jobbliggaren/secrets/*` — the files are 0400, so do not try to read them back | | |


### Rows 27b–32 — the target's provisioning (27b–27d) and gate M-4 (28–32) (#197)

**Mixed on purpose, and the split is worth reading before the table.** **27b–27d are properties of
the TARGET**, measured against the live container: 27c is measured and closed; 27b and 27d are
**gates** and are open. **28–32 are the drill on the BOX** and are unmeasured, written as a
checklist deliberately — `deploy/` gets no integration coverage from `ci`, so the box is where they
are decided.

Rows 28–31 close **gate M-4**. Row 32 is a gate and Klas's, inheriting row 26's wording. A row
without a date is a claim that cannot be told from one that has decayed.

| # | Property (gate) | Instrument | Measured | Date |
|---|---|---|---|---|
| 27b | **The target's lifecycle actually removes objects, measured as an EFFECT and not as a rule** | Provisioning gate, and it is a gate: `get-bucket-versioning`, then the lifecycle configuration read back, then after two nights `list-object-versions --prefix deks/` returning **exactly one version per key**. On a versioned bucket an overwrite deletes nothing and an `Expiration` rule only writes a delete marker — so **both** prefixes need `NoncurrentVersionExpiration` (and `main/` additionally `ExpiredObjectDeleteMarker`), or the "exactly one DEK generation" property AND K4's 30 days are both unenforced while every rule reads back correct. A rule is a claim; the version listing is the measurement | | |
| 27c | **The chosen target is EU and unversioned, and BOTH prefixes carry a provider rule with `deks/` outliving `main/`** | `aws s3api get-bucket-location` / `get-bucket-versioning` / `get-object-lock-configuration` / `get-bucket-lifecycle-configuration` against the live container | **Measured, all four.** OVHcloud Object Storage, container `jobbliggaren-backups`. Location `eu-west-par` (Paris, EU). Versioning **not enabled** and Object Lock **not enabled** — Klas's decision, and the branch ADR 0125 names as strictly stronger for the one-generation property; the cost is that Object Lock is creation-time and is now permanently closed on this container. **Two** lifecycle rules, applied and read back: `k4-main-artefacts-30-days` (`main/`, 30 days) and `deks-outlive-main-90-days` (`deks/`, 90 days). **The ordering is the invariant, not the numbers:** both prefixes are written in the same run, so `deks/` outliving `main/` means a live main artefact implies its key generation still exists. Equal or shorter would expire the KEYS while younger ciphertext survives. `deks/` needs *some* bound because `user_data_keys` carries `JobSeekerId`+`CreatedAt` — pseudonymous personal data — so no-expiry becomes retention without purpose once the job stops for good (Art. 5(1)(e)) | 2026-08-09 |
| 27d | **The upload credential cannot DELETE — and it CAN, so this row is a GATE and it is OPEN** | `aws s3api delete-object` with the box's credential; then `get-bucket-policy` and `get-bucket-acl` to establish whether a policy could repair it | **Measured FALSE 2026-08-09, and the reason is structural.** `delete-object` **succeeded**. `get-bucket-policy` → `NotImplemented` on OVH, so the S3 instrument does not exist here; `get-bucket-acl` → the backup user **OWNS** the container with `FULL_CONTROL`, and the owner exemption defeats **implicit** deny. ADR 0125 Decision §3's credential-without-`DELETE` — the property that chose OVH over Hetzner Storage Box — is therefore **not in force**. ⚠ **An earlier draft of this row said no policy could repair it, and that is the expensive kind of wrong:** it reasoned from the absence of *bucket* policies to the absence of any instrument. OVH's **user policy** attaches to the S3 user, is invisible to `get-bucket-policy` by construction, and its documented evaluation checks an **explicit** `Deny` FIRST, before any ACL fallback — the owner exemption defeats only implicit deny. **Repair, and it is a measurement before a plan:** apply an explicit `Deny` on `s3:DeleteObject` as a user policy, then counter-measure that `put-object` still succeeds and `delete-object` returns `AccessDenied`. Compatible by construction — the script issues no delete verb, promotion is `PutObject`. Re-creating the container under a different owner is *a* repair and the expensive one. **Neither applied.** Klas's. Blocks neither backups nor the drill; **it is a first-real-data gate** | | |
| 28 | `age` and `rclone` are installable from apt on trixie, and the versions are recorded | `apt-cache policy age rclone`, then `age --version` / `rclone version`. **If either is absent this is a STOPP to security-auditor and Klas, not an improvised binary fetch** — `sops` was measured absent from trixie for #198, so this class of absence is live on this box rather than hypothetical | | |
| 29 | A real nightly run produces both artefacts, and the box holds no plaintext afterwards | `systemctl start jobbliggaren-backup.service`, then `journalctl -u jobbliggaren-backup.service`. Expect `Result=success`, a `main/jobbliggaren-<stamp>.dump.age` object, and a promoted DEK generation. Then `sudo find /run /var/lib/jobbliggaren /tmp -newermt '-1 hour' -type f` — expect no dump-shaped file. The working directory is on tmpfs and is removed by a trap on every exit path, so a survivor here is a real defect | | |
| 30 | The freshness probe fires AND clears — both halves, because a probe that cannot clear is worse than none | Move the stamp back 27 h (`touch -d '27 hours ago'`), run `jobbliggaren-backup-fresh.service`, expect it in `systemctl --failed` naming a stale backup. Then run a real backup and expect the next firing to take it **off** the list with no operator action. The clearing half is the one #198's timer got wrong (`OnUnitActiveSec` on a `Type=oneshot`, systemd#21600) | | |
| 31 | **The restore drill (M-4).** A real artefact, decrypted off-box, restored, and read through the application — and the crypto-erasure semantics measured rather than asserted | `backup-restore.md` §5 end to end against a real object from the real target, on a synthetic user manufactured through production paths and then hard-deleted. Record the counts from step 5, and record **(b2)** as the erasure result — *not* (b). DEK rows are created lazily, so (b) "restored users with no key" also counts users who never wrote an encrypted field and never had one; (b2) counts users who have ciphertext but no key, which is the erased-user signature. Reporting (b) would overstate the drill. **And record (b2) with its scope beside it**, because this row is where the number is protocolled and a later reader reads the row rather than the runbook: (b2)'s `EXISTS` inspects `applications.cover_letter` alone, while `EncryptedFieldRegistry` carries **six** encrypted columns — a user whose only ciphertext was a note, a follow-up, a resume version or a parsed CV is invisible to it. It is an **existential** proof (one confirmed case proves the mechanism; a zero is a prompt to investigate), not a census. Also record that a non-deleted user's encrypted field decrypts through the app — without that, "unreadable" is indistinguishable from "the restore broke" | | |
| 32 | **The age private key exists in escrow, off-box, and never beside the ciphertext — this row is a GATE, not a report** | Klas confirms the identity is in the password manager and records the date. The box holds only the public recipient by design, so escrow is the **only** path from ciphertext back to data, and a lost age key makes every retained artefact permanently unreadable — the same finality as row 26, with a wider blast radius. **Undecided as of 2026-08-09.** Do not treat the offsite artefacts as a recovery path on an empty cell here. Backups may be *taken* meanwhile; encryption needs only the recipient | | |

- **Backup and restore** — [`backup-restore.md`](backup-restore.md) (#197): the nightly job,
  the split-dump model, retention, and the restore procedure. **The target is CHOSEN and MEASURED
  (2026-08-09): OVHcloud Object Storage, `jobbliggaren-backups`, region `eu-west-par`** — rows
  27b–27d. **The Art. 28 DPA is NOT signed**; an account with credits is not a processing
  agreement. **Escrow of the age private key is the same gate as row 26 above and is equally
  undecided** — row 32.
- **Master-key operations** — [`master-key-ops.md`](master-key-ops.md) (#198): the injection
  procedure, the per-boot re-injection this model requires, rotation and its drill, and
  recovery. Key-**access** detection is not there either: it is dispositioned to #1201, and
  what #198 delivers is **absence** detection (`jobbliggaren-secrets-present.timer`).
- **Host detection and alerting (gate M-7)** — the obligation and its `Hemvist` are
  [#1201](https://github.com/klasolsson81/jobbliggaren/issues/1201); the host-level half
  (auditd or equivalent, file-integrity monitoring, log shipping off the box, something
  that pages a human) was re-homed there by Klas 2026-08-06 rather than being carried by
  #196's closure. Delivered separately from this file either way.
- **The production log sink** — #1175, unbuilt and unowned. Docker's log rotation above is
  a disk control, not a log sink.
- **Gate B-1's cutover — and the corpus still waits for it.** #198 shipped the repair (see §2
  and [`master-key-ops.md`](master-key-ops.md)), but **shipping a mechanism is not closing a
  gate**: the key is not moved on this box until the operator performs the cutover, and B-1 is
  discharged only when rows 21–25 in §5 carry measurements. **Klas confirmed the sequencing
  2026-08-05: the stack may be deployed and every cutover proof taken with the key as it was,
  because the box holds no user data — but the 51 347 recruiter contact records must not land
  until B-1 is closed.** Nothing mechanical enforces that; this paragraph is the reader. The
  corpus additionally waits on #1199 (the Netcup DPA), which is Klas's signature and not
  affected by any of this.
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
