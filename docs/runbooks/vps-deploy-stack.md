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
| `redis` | Sessions, cooldown gates, rate limiting, landing-stats cache | nothing |

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

**The master key lives in `.env`, and that is a deliberate v1 position, not an oversight.**
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
docker compose -f deploy/docker-compose.yml pull

# One-time role provisioning. Needs the master credentials, which is why it is an
# operator step and not part of `up`.
docker compose -f deploy/docker-compose.yml run --rm migrate init
docker compose -f deploy/docker-compose.yml run --rm migrate bootstrap
docker compose -f deploy/docker-compose.yml run --rm migrate ensure-extensions

# Hangfire's own schema — see hangfire-schema.md. Runs as jobbliggaren_migrations.
# Then the stack. `migrate schema` runs as a gated dependency of api/worker, so
# ordering holds on every `up`, not only the first.
docker compose -f deploy/docker-compose.yml up -d
docker compose -f deploy/docker-compose.yml ps
```

**Rollback is an image tag.** Pin `IMAGE_TAG=sha-<short>` in `.env` and re-run the
reconcile unit — seconds. A Netcup snapshot is **not** deploy rollback: snapshots are
copy-on-write, need 50 % free disk, only *offline* ones are consistent, and one exportable
snapshot remains. Their role is **before a migration**, once real user data exists.

---

## 4. Host-side prerequisites

**Docker daemon.** Write `/etc/docker/daemon.json` **before the first `up`**: `json-file`
logging capped (`max-size` + `max-file`), because no production log sink exists yet
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
must succeed. Then confirm the exemption leaks nothing else: an unknown path under
`/.well-known/acme-challenge/` returns 404 with no application content, and every other
path unauthenticated returns 401. Only then switch to production issuance — the production
limits are 5 duplicate certificates per week and 5 failed validations per hour, and a
mistake discovered there costs days. **Never configure TLS-ALPN-01 away without having
proven HTTP-01 for real.**

### Verification log

Fill in as each is measured. Property · measured value · instrument · date — the same
shape as `vps-base-hardening.md` §9.1. A row without a date is a claim that cannot be told
from one that has decayed.

| # | Property (gate) | Instrument | Measured | Date |
|---|---|---|---|---|
| 1 | Per-service `mem_limit`/`memswap_limit` match the ADR 0122 table | `docker inspect` | | |
| 2 | Redis runs `maxmemory` **and** `noeviction` | `redis-cli config get maxmemory*` | | |
| 3 | Postgres steady-state RSS against the 2 560 MiB cap | cgroup `memory.stat` anon/file during the 02:00 snapshot job | | |
| 4 | Postgres tuning is explicit, derived from the cap | `SHOW shared_buffers` etc. | | |
| 5 | Certificate issues over HTTP-01 with the K2 gate live (M-5a) | forced issuance, staging | | |
| 6 | Certificate issues over TLS-ALPN-01 (the fallback path) | forced issuance, staging | | |
| 7 | ACME exemption leaks nothing else | `curl` unknown challenge path → 404; other paths → 401 | | |
| 8 | HSTS on the **unauthenticated 401** (M-5a) | `curl -sI` | | |
| 9 | HSTS on a Next-served 200 (M-5a, complement) | `curl -sI -u` | | |
| 10 | Only 80/443 answer from outside (M-5b p6) | external TCP probe per container port | | |
| 11 | The 11 BFF `/api/*` handlers resolve to Next (Option B) | cutover curl matrix | | |
| 12 | `/api/v1/dev` and `/api/v1/admin/*` unreachable from outside | same matrix | | |
| 13 | `forward` keeps `policy drop` with targeted accepts (M-5b p4) | `nft list chain inet filter forward` | | |
| 14 | The edge's IPv6 behaviour | `nc -6 -vz <box-v6> 22` from mobile data | | |
| 15 | Every container runs non-root | `docker inspect -f '{{.Config.User}}'` | | |
| 16 | `DOTNET_gcServer=0` reaches api and worker | `docker exec ... env` | | |
| 17 | Swap is zram only, no disk swap (B-1) | `swapon --show` | | |
| 18 | Per-IP rate limiting partitions on the real client IP (#1202) | two known client IPs; one exhausts, the other still authenticates | | |
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
- **`infra/terraform/`** — a record of what once ran on AWS, not a starting point. Do not
  repair its names toward the current application: it injects options #802 removed, injects
  no master key (so a re-apply hard-fails at startup), and names Dockerfile paths that do
  not exist.
