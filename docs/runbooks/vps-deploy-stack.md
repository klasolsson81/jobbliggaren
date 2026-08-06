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
  .env             # root:root 0600 — every secret the stack needs. NEVER COMPOSE_FILE.
  staging/         # transient only (corpus dump); emptied and shredded after use
```

`deploy/.env.example` is the template and the required-key list. Every key is a hard `:?`
requirement: compose refuses to start rather than starting on a default nobody chose.

**The master key lives in `.env` on disk, and gate B-1 is therefore NOT closed.** B-1
requires the key never be plaintext on disk — a TPM-bound systemd credential, or sops+age
into tmpfs. It is owed by #198, and the `<NAME>_FILE` seam alone does not discharge it:
a plain file plus an env var pointing at it is still plaintext on disk. Measured
2026-08-05, there is a **second** copy nobody had registered — `docker inspect` returns
the value after the container has exited, so Docker persists it in its own state.

**B-1 is a Blocker-graded pre-beta-data gate, and the recruiter corpus IS beta data.**
The stack may be deployed with the key in `.env`; the corpus may not land until B-1 is
closed. That sequencing is a Klas decision and is recorded in this runbook so it is not
carried in anyone memory.

What `memswap_limit == mem_limit` delivers is a **stronger B-1 posture**, which ADR 0050
`Amendment 2026-08-04` §2 condition 4 attributes to B-1 in as many words — the key cannot
reach zram either, only anonymous RAM. It strengthens the gate; it does not close it:
`memswap_limit == mem_limit` on `api` and `worker` keeps their memory out of swap, and the
host swaps to zram only (gate B-1). The `<NAME>_FILE` seam exists in `Jobbliggaren.Migrate`
only — the API and Worker read plain environment through `IConfiguration` — so moving the
key to a file is a code change, and it belongs to #198 (master-key protection and
rotation) rather than here.

---

## 3. First boot, in order

Prerequisite: Docker installed, `/etc/docker/daemon.json` written, and the nftables
`forward` delta applied — all in §4 below, all before anything here.

```bash
cd /opt/jobbliggaren
C="docker compose -f deploy/docker-compose.yml"

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
sed -i "s|^#*ACME_CHALLENGE_MODE=.*|ACME_CHALLENGE_MODE=http01|" deploy/.env
$C up -d --force-recreate caddy
$C exec caddy caddy adapt --config /etc/caddy/Caddyfile | grep -o '"challenges":{[^}]*}'
$C logs caddy | grep -iE "http-01|tls-alpn|obtained|certificate"

# 2. TLS-ALPN-01 only (row 6). Step 1 left a VALID staging certificate, so Caddy would issue
#    nothing and row 6 would be ticked on row 5's work. Discard the STAGING tree only.
sed -i "s|^#*ACME_CHALLENGE_MODE=.*|ACME_CHALLENGE_MODE=alpn01|" deploy/.env
$C exec caddy rm -rf /data/caddy/certificates/acme-staging-v02.api.letsencrypt.org-directory
$C up -d --force-recreate caddy
$C exec caddy caddy adapt --config /etc/caddy/Caddyfile | grep -o '"challenges":{[^}]*}'
$C logs caddy | grep -iE "http-01|tls-alpn|obtained|certificate"

# 3. Back to default, then production issuance ONCE. Storage is CA-scoped, so switching the
#    issuer is itself what forces a fresh production certificate — no rm belongs here, and one
#    would throw away a VALID production cert on any re-run and spend a duplicate slot.
sed -i "s|^#*ACME_CHALLENGE_MODE=.*|#ACME_CHALLENGE_MODE=both|" deploy/.env
sed -i "s|^#*ACME_CA=.*|#ACME_CA=|" deploy/.env
$C up -d --force-recreate caddy

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
curl -sSI https://dev.jobbliggaren.se | head -1
```

**Rollback is an image tag.** Pin `IMAGE_TAG=sha-<short>` in `.env` and re-run the
reconcile unit — seconds. A Netcup snapshot is **not** deploy rollback: snapshots are
copy-on-write, need 50 % free disk, only *offline* ones are consistent, and one exportable
snapshot remains. Their role is **before a migration**, once real user data exists.

---

## 3b. The reconcile unit

`release-images` publishes on an hourly schedule rather than on merge, because automerge
merges as a GitHub App and app-triggered events start no workflow runs (measured
counterfactual: #1107 app-merged, zero runs; #1108 human-merged, a run after 5 s). Nothing
tells the box a new image exists, so the box asks. Install once:

```bash
sudo cp /opt/jobbliggaren/deploy/systemd/jobbliggaren-reconcile.{service,timer} /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now jobbliggaren-reconcile.timer
systemctl list-timers jobbliggaren-reconcile            # Förväntat: one entry, next at :47
sudo systemctl start jobbliggaren-reconcile.service     # prove it runs at all, not just that it is scheduled
journalctl -u jobbliggaren-reconcile -n 20 --no-pager
```

`enable --now` schedules it; it does not run it. `list-timers` showing an entry proves
scheduling and nothing else — the one-shot `start` above is what proves the unit works,
and it is safe because an unchanged pull is a no-op.

**The timer fires at :47, deliberately offset from the publish run's :17.** That run builds
and scans five images and takes tens of minutes, so pulling at :17 would race a
half-published tag — `latest` moved while `sha-<short>` has not, which is exactly the split
the release workflow's own idempotence predicate treats as unfinished.

**Rollback stays an image tag.** Pin `IMAGE_TAG=sha-<short>` in `deploy/.env` and run the
unit: the pull resolves the pinned tag and `up -d` recreates only what moved. Seconds, and
it is the primary rollback path — a Netcup snapshot is not deploy rollback.

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
that the import mechanism works, and needs no separate check:

```bash
cd /opt/jobbliggaren
C="docker compose -f deploy/docker-compose.yml"

# 1. Staging, HTTP-01 only (row 5).
sed -i "s|^#*ACME_CHALLENGE_MODE=.*|ACME_CHALLENGE_MODE=http01|" deploy/.env
$C up -d --force-recreate caddy
$C logs caddy | grep -iE "http-01|tls-alpn|obtained|certificate"

# 2. Staging, TLS-ALPN-01 only (row 6). Step 1 left a VALID certificate, so Caddy would issue
#    nothing and the row would be ticked on step 1 all over again. Discard it first.
sed -i "s|^ACME_CHALLENGE_MODE=.*|ACME_CHALLENGE_MODE=alpn01|" deploy/.env
$C exec caddy rm -rf /data/caddy/certificates
$C up -d --force-recreate caddy
$C logs caddy | grep -iE "http-01|tls-alpn|obtained|certificate"

# 3. Back to default, then production issuance ONCE.
sed -i "s|^ACME_CHALLENGE_MODE=.*|#ACME_CHALLENGE_MODE=both|" deploy/.env
sed -i "s|^ACME_CA=.*|#ACME_CA=|" deploy/.env
$C exec caddy rm -rf /data/caddy/certificates
$C up -d --force-recreate caddy
curl -sSI https://dev.jobbliggaren.se | head -1
```

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

**The box runs Compose v5.4.0**, a major ahead of the 2.40.3 those numbers and this file's
behavioural comments were measured on. No divergence has been observed in the boot sequence,
the health-check semantics or `--force-recreate`, but the version belongs to every claim below
that came from the older one.
| # | Property (gate) | Instrument | Measured | Date |
|---|---|---|---|---|
| 1 | Per-service `mem_limit`/`memswap_limit` match the ADR 0122 table | `docker inspect` | caddy 128/256 · web 640/1280 · **api 1024/1024** · **worker 1024/1024** · postgres 2560/5120 · redis 512/1024 (MiB, mem/memswap). Matches ADR 0122, and `memswap_limit == mem_limit` holds on exactly the two services condition 4 names | 2026-08-06 |
| 2 | Redis runs `maxmemory` **and** `noeviction` | `redis-cli config get maxmemory*` | `maxmemory 419430400` (400 MiB) · `maxmemory-policy noeviction` — both set, which is the requirement: `noeviction` only engages when `maxmemory` is | 2026-08-06 |
| 2b | Redis has headroom under real traffic — `noeviction` means a full instance refuses writes, and the write that fails is the session store, so it surfaces as nobody being able to log in | `redis-cli info memory` — `used_memory` against `maxmemory` | | |
| 2c | The K2 gate's hash is bcrypt cost 11, not the tool's default 14 — the gate pays the full hash on every WRONG password, and nothing upstream filters | `docker exec jobbliggaren-caddy printenv BASIC_AUTH_HASH \| cut -c1-7` prints `$2a$11$` and nothing more: the hash itself is offline-crackable, so do not put it in the cutover scrollback. Then time a wrong-password request against a right one | `$2a$11$` — cost 11, not the tool default 14 | 2026-08-06 |
| 3 | Postgres steady-state RSS against the 2 560 MiB cap | cgroup `memory.stat` anon/file during the 02:00 snapshot job | | |
| 4 | Postgres tuning is explicit, derived from the cap | `SHOW shared_buffers` etc. | `shared_buffers 640MB` · `effective_cache_size 1536MB` · `work_mem 8MB` · `maintenance_work_mem 192MB` · `autovacuum_work_mem 64MB` · `max_connections 60` · `max_wal_size 4GB` — every value explicit, none a default | 2026-08-06 |
| 5 | Certificate issues over HTTP-01 with the K2 gate live (M-5a) | `ACME_CHALLENGE_MODE=http01` on staging, then **the issuance log line naming the challenge type** **and** the counterfactual that the other was off (`caddy adapt` shows `"tls-alpn":{"disabled":true}` **on the policy whose `subjects` contain `SITE_HOST`** — the adapted config carries two automation policies and a bare substring match would be satisfied by either). BOTH halves: a certificate alone can be ticked on one ALPN issued — the silent fallback this proof exists to catch — and the counterfactual alone does not survive an operator confusing `http01` with `alpn01` | | |
| 6 | Certificate issues over TLS-ALPN-01 (the fallback path) | `ACME_CHALLENGE_MODE=alpn01` on staging; same two halves as row 5, mirrored (`"http":{"disabled":true}`) | | |
| 6b | The box was LEFT with both challenges live | `caddy adapt` **inside the running container** shows no `"challenges"` key at all. Rows 5 and 6 measure the PROOF modes; this measures the END state, and nothing else does — a left-over mode or a glob value — plus a pre-seam image, which the gate detects separately by asserting the snippet exists, (`ACME_CHALLENGE_MODE=*` imports all three snippets and disables BOTH, exit 0, measured) all issue a valid certificate at cutover and kill the RENEWAL ~60 days later | | |
| 7 | The edge OWNS the ACME prefix (nothing under it proxies) | `curl -sI` unknown challenge path → 404 **and `Server: Caddy`**, never the upstream's own `Server`/`Via` | `404` **and `Server: Caddy`** on `/.well-known/acme-challenge/nonexistent`; no `Via`, no upstream `Server`. The edge answers, nothing proxies | 2026-08-06 |
| 8 | HSTS on the **unauthenticated 401** (M-5a) | `curl -sI` | `HTTP/1.1 401` + `Strict-Transport-Security: max-age=31536000; includeSubDomains` + `Server: Caddy`. The header is on the FIRST response a browser meets, before Next is reached | 2026-08-06 |
| 9 | HSTS on a Next-served 200 (M-5a, complement) | `curl -sI -u` | `HTTP/1.1 200` + the same header value. **Emitted TWICE** — Caddy's site header and `buildSecurityHeaders` both fire, so both response paths carry it (which is the gate) but the Next-served response carries it twice. Values byte-identical; noted rather than silently accepted | 2026-08-06 |
| 10 | Only 80/443 answer from outside (M-5b p6) | external TCP probe per container port | 3000 / 8080 / 5432 / 6379 → **Connection timed out** from 3 external nodes each. **Control: 80 and 443 → CONNECTED from 3 nodes each** — without it a broken probe service reads as containment | 2026-08-06 |
| 11 | The 11 BFF `/api/*` handlers resolve to Next (Option B) | cutover curl matrix | 9 handlers probed, all answered by Next with application statuses (200 health, 200 landing-stats, 400 suggest/facet-counts on missing params, 401 recent-searches, 405 POST-only surfaces). No 404, no ASP.NET signature. **Count discrepancy: 12 `route.ts` files exist, not the 11 this row and ADR 0050 name** — measured, unexplained, not reconciled here | 2026-08-06 |
| 12 | `/api/v1/dev` and `/api/v1/admin/*` unreachable from outside | same matrix | `/api/v1/dev`, `/api/v1/dev/reset-my-data`, `/api/v1/admin/jobs/recurring` and `/api/v1/auth/login` all `307 → /logga-in`, absorbed by Next middleware. The ASP.NET API is not reached at all — stronger than a 404 | 2026-08-06 |
| 13 | `forward` keeps `policy drop` with targeted accepts (M-5b p4) | `nft list chain inet filter forward` | `policy drop` with targeted `iifname`/`oifname` accepts, IPv4-scoped. Dead-man discipline: backup with leading `flush ruleset` dry-run-verified loadable, 10-min revert unit armed, verified from a NEW connection, timer stopped and `journalctl` confirms it never fired | 2026-08-06 |
| 14 | The edge's IPv6 behaviour | `nc -6 -vz <box-v6> 22` from mobile data | | |
| 15 | api/worker/migrate/web run as a non-root uid (`app`, `app`, `app`, `node`); postgres and redis drop privileges in their own entrypoints (`gosu` / `setpriv`); **caddy runs as root and is a named exception** — it binds 80/443 and its image sets no `USER`; every service carries `no-new-privileges` | `docker inspect -f '{{.Config.User}}'` per container + `docker exec <c> id` for the two that drop + `docker inspect -f '{{.HostConfig.SecurityOpt}}'` | api 1654 · worker 1654 · web 1000 (`node`; `docker top` prints the host name for that uid) · **postgres 999 · redis 999** (cross-checked in `/proc/1/status`) · **caddy root, the named exception**. `no-new-privileges:true` on all six plus migrate. NOTE: `docker exec <c> id` is the WRONG instrument — it runs a new process as the container's configured user and reported root for postgres and redis | 2026-08-06 |
| 16 | `DOTNET_gcServer=0` reaches api and worker | `docker exec ... env` | `DOTNET_gcServer=0` present in api and in worker | 2026-08-06 |
| 17 | Swap is zram only, no disk swap (B-1) | `swapon --show` | `/dev/zram0`, 3.9 G, priority 100, 0 B used. No disk swap anywhere; `backing_dev` = `none` | 2026-08-06 |
| 18 | Per-IP rate limiting partitions on the real client IP | two known client IPs; one exhausts the login budget, the other still authenticates | **blocked on #1202** — measured 2026-08-04, no component in this stack sends `X-Forwarded-For`: Caddy sets it toward `web`, and Next's BFF fetches toward `api` do not forward it. This row fails until that chain is closed. | |
| 19 | Hot-path latency against the ADR 0045 budgets after `gcServer=0` | NBomber | | |
| 20 | Reconcile-pull runs and applies a digest change | `systemctl list-timers` + journal of a real run | | |

---

## 6. What this runbook does not own

- **Backup and restore** — #197. The target is still open; §7 of ADR 0050's amendment adds
  that it must be a failure domain independent of both the box and the operator's
  workstation, and that the age private key must never sit with the ciphertext.
- **Master-key protection and rotation, and key-access detection** — #198.
- **Host detection and alerting (gate M-7)** — owned by #196 per #1201's split, delivered
  separately from this file.
- **The production log sink** — #1175, unbuilt and unowned. Docker's log rotation above is
  a disk control, not a log sink.
- **Closing gate B-1 — and the corpus waits for it.** The field-encryption master key is
  plaintext on disk here, in `deploy/.env` and a second time in Docker's own container
  state (measured: `docker inspect` returns it after the container has exited). B-1
  requires it never be plaintext on disk, and #198 owns the repair. **Klas confirmed the
  sequencing 2026-08-05: the stack may be deployed and every cutover proof taken with the
  key as it is, because the box holds no user data — but the 51 347 recruiter contact
  records must not land until B-1 is closed.** Nothing mechanical enforces that; this
  paragraph is the reader.
- **The edge binary is ours, not upstream's, and the scanned image must be the published
  one.** `deploy/caddy/Dockerfile` compiles caddy rather than taking it from the published
  tag, so "we run stock caddy 2.11.4" is no longer true when reading an upstream bug report.
  It is also non-deterministic in three ways — a floating builder tag, `apk upgrade`, and
  live Go module resolution — which means **a rebuild is a different artefact than the one
  trivy approved**. Whatever publishes must promote the image it scanned (scan and push the
  same loaded image, or push by digest), never rebuild from the Dockerfile for the push.
- **Publishing the images this stack pulls** — #196's own deploy-workflow AC, and it is
  **not built yet**. Until it is, §3's `docker compose pull` has nothing to pull: the tags
  do not exist. Named here because the rest of this section reads as an enumeration, and a
  reader would otherwise conclude publishing is owned and delivered.
- **`infra/terraform/`** — a record of what once ran on AWS, not a starting point. Do not
  repair its names toward the current application: it injects options #802 removed, injects
  no master key (so a re-apply hard-fails at startup), and names Dockerfile paths that do
  not exist.
