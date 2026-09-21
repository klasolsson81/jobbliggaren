# Redis service boundaries

Status: preparatory contract for #1759, following [ADR 0143](../decisions/0143-redis-network-and-service-identity-boundaries.md).
The templates and tests are not mounted by either production or development
Compose. They add no application startup requirement. Integration follows the
explicit #1735 `1a-store` handoff; environment acceptance remains open.

## Contract and ownership

The executable sources are
[`service-boundaries.json`](../../deploy/redis/service-boundaries.json),
[`persistent.acl.template`](../../deploy/redis/persistent.acl.template) and
[`volatile.acl.template`](../../deploy/redis/volatile.acl.template).
Change the contract and its tests together when adding a consumer.

| Intended caller | Peer | Reason |
|---|---|---|
| Caddy | web | Web requests and health checks |
| web | API | BFF requests |
| API | PostgreSQL | Application data |
| Worker | PostgreSQL | Background jobs |
| migrate | PostgreSQL | Schema changes |
| migrate-rewrap | PostgreSQL | Key maintenance |
| API | persistent Redis | Sessions, cooldowns, company cache and statistics reads |
| Worker | persistent Redis | Statistics publication |
| API | volatile Redis | Login challenges and budgets |
| API | Seq | Application logs |
| Worker | Seq | Job logs |

Each row becomes its own internal bridge. Caddy, API and Worker each have a
separate egress bridge with no other application members. Web, Caddy,
PostgreSQL, both migration services and Seq have no direct Redis path. Worker
has no volatile path. Redis instances have no common bridge.

These are intended calls. A bridge permits bilateral access to the listening
ports of its peers, and does not enforce direction or port-level policy.
Application proxying, host administration and a compromised permitted peer
remain outside that boundary. The isolated tests check DNS and every target
address, including positive listener controls.
[Docker bridge networking](https://docs.docker.com/engine/network/drivers/bridge/).

Network integration must also prepare host forwarding policy for each new bridge,
preserving required replies and egress without adding cross-bridge forwarding.
Do not assume a rule for an older bridge covers a newly created bridge. Limit
API forwarded-header trust to the web–API network and preserve the single-value
client-IP relay contract with #1767. Keep all actual subnets, interfaces,
firewall snapshots and operator evidence in the private deployment record.

## Consumer and command inventory

The baseline uses the repository-pinned StackExchange.Redis 3.1.13 and
Microsoft.Extensions.Caching.StackExchangeRedis 10.0.10. The contract fixtures
run Redis 8.6, matching the production manifest's minor line; a different image
or client version requires another contract run. PR2 must additionally verify
the development manifest's image. Versions here identify this contract baseline,
not a promise that dependencies never change.

Every full key below starts with `jobbliggaren:`. Cache adapters apply that
prefix via `RedisCacheOptions.InstanceName`; direct multiplexers apply it
themselves. All clients use database zero; numbered databases are not a security
boundary.

| Adapter / identity | Key suffix | Required wire operations |
|---|---|---|
| RedisSessionStore / API persistent | `session:*` | HMGET, HMSET, EXPIRE, UNLINK |
| Session rotation claim / API persistent | `session:*:rotating` | SET with NX and expiry |
| Session index / API persistent | `user:*:sessions` | SADD, SREM, SMEMBERS, EXPIRE, PEXPIRE, UNLINK |
| Revocation/deletion markers / API persistent | `user:*:revoked`, `user:*:deleted` | EXISTS, SETEX, PSETEX |
| RedisCooldownGate / API persistent | `cd/{scope}/v1/*` | HMGET, HMSET, EXPIRE |
| CachedCompanyRegistry / API persistent | `company-registry:v1:*` | HMGET, HMSET, EXPIRE |
| RedisLandingStatsCache / API persistent | exactly `landing:stats:v1` | HMGET |
| RedisLandingStatsCache / Worker persistent | exactly `landing:stats:v1` | HMSET, EXPIRE |
| RedisLoginChallengeStore / API volatile | `auth/challenge/v1/*` | HMSET, HGET, HINCRBY, EXISTS, EXPIRE, UNLINK, EVAL, EVALSHA |
| Challenge address index / API volatile | `auth/challenge-by-address/v1/*` | SET with GET and expiry |
| RedisGrantStore / API volatile | `auth/grant/v1/*` | SET with NX and expiry, GETDEL |
| RedisRegistrationClaim / API volatile | `auth/registration-claim/v1/*` | SET with NX and expiry |
| RedisRateBudget / API volatile | `budget/{scope}/v1/*` | INCR, EXPIRE with NX |

The five persistent cooldown scopes are `resend-confirm`, `account-exists`,
`change-email-target`, `change-email-user` and `password-reset`.
The four volatile budget scopes are `login-challenge-cooldown`,
`login-challenge-mails`, `login-challenge-codes` and
`login-challenge-unknown-address-mails`. Each is enumerated in the template;
unknown scopes are refused.

A missing selector fails closed and does not look like what it is: the API is
healthy and `/api/ready` is green, because both volatile identities may `PING`,
while every route that reaches the uncovered key family answers the uniform
503 and logs a throttled `store_unavailable`. Compare the rendered ACL with the
table above before suspecting the Redis instance.

The volatile adapter also needs MULTI, EXEC, DISCARD and SCRIPT LOAD. Its code
consumption script uses EXISTS, HINCRBY and HGET on one challenge record. Tests
exercise cold and warm execution and SCRIPT FLUSH recovery using the fixture
operator. Persistent API and Worker have no scripting permission.

The pinned client emits **HMSET**, not HSET, for hash arrays, and **UNLINK**
for asynchronous key deletion. Fractional expiry uses PEXPIRE or PSETEX where
the adapter supports configurable time spans. Sessions use explicit sliding
writes, without a RedisCache sliding expiry. Statistics use an absolute expiry,
so API reads do not need permission to refresh that key's TTL.

No broad command category or `jobbliggaren:*` grant is permitted. Each selector
combines its own key patterns and command list. This keeps authentication writes
from granting statistics writes. The default user is off. Redis evaluates
commands against the authenticated user's selectors.
[Redis ACL rules and selectors](https://redis.io/docs/latest/operate/oss_and_stack/management/security/acl/).

## Connections, credentials and health

PR2 must move the fixture-verified connection restrictions into shared
Infrastructure configuration: RESP2, explicit server-version baseline, empty
configuration channel and tie-breaker; disable unused CONFIG, INFO, CLUSTER,
SENTINEL, pub/sub, PUBLISH and SELECT discovery commands. Application roles allow
PING, ECHO, CLIENT SETNAME, CLIENT SETINFO and QUIT. Reuse #1735's typed volatile
holder and Application ports; do not introduce a competing DI contract.

Use separate read-only mounts for API persistent, API volatile and Worker
persistent connection secrets. The existing directory shared by API and Worker
must not contain API Redis credentials. Web and Caddy receive no Redis secrets.
Redis-only mounts contain the effective ACL and that store's health credential.
The fixture's administrative identity is appended only in test memory and never
appears in a deployment template. Operator credentials are independently managed.

Reuse whole-connection-string `_FILE` loading, with exact expected usernames
`api-persistent`, `worker-persistent` and `api-volatile`. Before integration,
verify missing, empty, unreadable and malformed values are refused, as are an
unexpected username, failed authentication and a connection to the other store.
An empty file must not uncover a lower-priority anonymous connection string.
No fallback to the persistent connection is allowed for volatile consumers.

The template placeholders accept lowercase 64-character SHA-256 password hashes,
with a literal `#` already in the template. Produce them from independent
high-entropy credentials in the approved secret tooling. Reject unresolved or
unexpected placeholders. Protect rendered ACL files as sensitive artifacts;
do not print their contents or include them in public CI artifacts.
An ACL file consists of `user ...` lines, without comment lines.

Container health uses `health-persistent` or `health-volatile`, with only PING.
Mount [`healthcheck.sh`](../../deploy/redis/healthcheck.sh) with its credential
file; invoke it with the identity and file path. The probe succeeds only with
exit zero and the complete combined output exactly `PONG` (after the shell's
trailing-newline removal). Redis CLI can otherwise produce an authentication
error followed by PONG when anonymous access is misconfigured. Never inspect
only the last line or trust the CLI exit code alone.

Application readiness uses the real API connections to both stores and the real
Worker persistent connection. PING proves authenticated availability; it does
not attest the ACL. Permission removal can leave PING green, which a test
demonstrates. Authorization is covered separately by adapter tests and the
mandatory effective-policy preflight below. PR2 must test the composed startup
and readiness endpoints, rather than treating these direct-connection fixtures
as host startup evidence.

## Isolated verification

Run with Docker available, without local application secrets or shared services:

```powershell
dotnet test --project tests/Jobbliggaren.Api.IntegrationTests -- --filter-class '*Security.Redis*'
dotnet test --project tests/Jobbliggaren.Architecture.Tests
```

A successful .NET run includes a nonzero `total:` and `failed: 0`.
The fixtures use randomly generated credentials, synthetic records, temporary
secret files, private test networks and ephemeral stores. Only the adapter
fixture publishes random ports for its test process; the topology fixture
publishes no ports. It uses synthetic Redis listeners for every service, so
pair reachability and mount behavior are independent of production boot files.

Coverage includes real session creation, reading, sliding, rotation, logout,
bulk revocation and deletion markers; real company/cooldown caches; real Worker
publication followed by API read; challenges, replacement, code/link consumption,
concurrent single use, cold/warm scripts and script-cache loss; and every existing
budget scope. Refusal tests cover Worker access to auth data, reversed statistics
permissions, unknown namespaces, administration, anonymous clients, wrong
credentials/identities/stores and cross-key attempts through Lua/transactions.

Failure tests cover volatile outage and reconnect, safe behavior and sanitized
logging after loss of a protection key, misleading health responses, and
credential revocation across existing connections and a fixture restart.
These are adapter and topology evidence, not evidence of a deployed boundary.
HTTP login/logout composition, production startup/readiness, actual service
mounts, the final Worker job path and full application log redaction remain
integration acceptance gates.

## Operator preflight and later change sequence

This section prepares a later, separately authorized operation; it is not a
deployment instruction for the current session.

1. Record the reviewed commit, resolved application/Redis image digests, rendered
   Compose digest, per-store rendered ACL digest and private secret version IDs.
   Record #1735's handoff and #1760/#1767 coordination. Confirm all new consumers
   from #1737/#1739/#1744 are registered. Preserve #1757's persistence decision.
2. In an isolated deployment rehearsal, verify the complete new configuration,
   including the exact service mounts and application startup/readiness. Also
   start the **new image with the previous Compose**. Image publication is
   scheduled; absence of a manual build dispatch does not preserve compatibility.
   If the old combination fails, hold PR2's merge until a later deployment GO
   permits verified image pinning and preparation.
3. Validate the generated ACL in isolated Redis using the same resolved image.
   Authenticate an operator through protected input, never a password argument.
   Compare the full effective ACL with the reviewed policy, accounting for
   canonical ordering and the separately approved operator identity. Inspect all
   users, selectors, password-version hashes and default-user state privately.
   The expected application identities must exist only on their intended store.
4. Use operator `ACL DRYRUN` for each row below on the intended instance. Require
   exact success (`OK`) for positive cases and permission refusal for negative
   cases. DRYRUN does not execute a write. Then run the real-adapter suite against
   isolated stores to cover transaction and script execution semantics.
5. Prepare bridge-specific forwarding additions and the rollback diff. Review
   effective routing and listener reachability using both DNS and direct
   addresses. Keep the prior rules needed for rollback until the authorized
   cutover is verified. Do not enable general cross-bridge forwarding.
6. Only during the separately authorized operation, install the staged policy,
   per-service secrets, networking and forwarding rules in the reviewed order;
   validate effective ACLs before admitting application traffic. Verify container
   health, both API stores, Worker publication, startup refusals and application
   flows. Retain dated private evidence and publish only the sanitized verdict.
7. If acceptance fails, stop the cutover. Restore the verified image/configuration
   pair and its required networking/forwarding before removing new paths. Keep
   operator access and persistence available. Never restore revoked credentials
   or an older policy that re-enables them as an automatic rollback step.

Example dry-run cases (all keys are synthetic names, and no writes execute):

| User / store | Command after `ACL DRYRUN <user>` | Expected |
|---|---|---|
| api-persistent / persistent | `HMGET jobbliggaren:session:probe data` | OK |
| api-persistent / persistent | `HMGET jobbliggaren:landing:stats:v1 data` | OK |
| api-persistent / persistent | `HMSET jobbliggaren:landing:stats:v1 data probe` | Refused |
| worker-persistent / persistent | `HMSET jobbliggaren:landing:stats:v1 data probe` | OK |
| worker-persistent / persistent | `HMGET jobbliggaren:landing:stats:v1 data` | Refused |
| worker-persistent / persistent | `UNLINK jobbliggaren:session:probe` | Refused |
| api-volatile / volatile | `HINCRBY jobbliggaren:auth/challenge/v1/probe a 1` | OK |
| api-volatile / volatile | `INCR jobbliggaren:budget/login-challenge-mails/v1/probe` | OK |
| api-volatile / volatile | `INCR jobbliggaren:budget/unregistered/v1/probe` | Refused |
| api-volatile / volatile | `GETDEL jobbliggaren:auth/grant/v1/probe` | OK |
| api-volatile / volatile | `GET jobbliggaren:auth/grant/v1/probe` | Refused |
| Each application user / its store | `ACL LIST`, `FLUSHALL`, `KEYS *` | Refused |
| Each health user / its store | `PING` | OK |
| Each health user / its store | `HMGET jobbliggaren:landing:stats:v1 data` | Refused |

DRYRUN samples complement the full effective-policy comparison; a few negative
samples cannot prove absence of every unintended grant.

## Credential rotation and revocation rehearsal

A password change alone does not terminate authenticated connections. Rehearse
with two synthetic credential versions and established clients before a live
operation. Stage the replacement hash and client secret version, reconnect using
it, remove the previous hash, and terminate every connection authenticated as
that user using the operator's `CLIENT KILL USER <user>`. Verify the old
established connection fails, the old credential cannot reconnect and the new
credential can reconnect. A full identity withdrawal uses DELUSER and has no
replacement. Persist the matching policy source and repeat the refusal after
restart so an old ACL file cannot resurrect access.
[Redis CLIENT KILL](https://redis.io/docs/latest/commands/client-kill/).

Keep health/operator credentials out of application files throughout rehearsal.
Record credential version identifiers and verdicts privately, never passwords,
connection strings, hashes, challenge payloads or session values in public output.

## Session integrity follow-up

The architect and security review require a separate design before any session
integrity scheme is introduced. Evaluate protection bound to the hashed Redis
session key (preventing value transplantation), valid-value replay, absolute and
sliding lifetime, rotation and grace, revocation/deletion markers, legacy records,
key rotation, key retention and recovery. Losing integrity keys must have an
explicit session outcome. Protecting only a session payload does not protect
independent markers or prevent replay of a valid old payload.

No session cryptography is implemented by this contract. #1759 stays open until
the composed implementation, application retesting (#1769) and the required
environment verification satisfy its acceptance criteria.
