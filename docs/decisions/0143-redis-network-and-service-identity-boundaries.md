# ADR 0143 — Redis network and service-identity boundaries

**Date:** 2026-09-19
**Status:** Accepted
**Decider:** Klas Olsson, approved implementation plan for #1759
**Related:** #1759 under #1758; #1735; #1757; ADR 0064; ADR 0142

## Context

Redis serves two different lifecycles: durable sessions and application caches,
and short-lived authentication challenges and budgets. The API and Worker also
have different responsibilities: the Worker publishes landing statistics, while
the API reads them and owns authentication. Network membership and Redis
credentials must express these distinctions independently.

The authentication work in #1735 owns the non-persisted Redis instance, its typed
connection holder and the boot contract. #1759 builds on that work and preserves
the durable-session decision in #1757. Application-owned ports and existing key
names remain stable; connection configuration belongs in Infrastructure and host
composition, following BUILD.md §4.

Klas approved two deliveries so that the contract and isolated verification can
proceed independently of the shared Compose, DI and secret-injection handoff.
This record describes the intended boundary. Its Accepted status confirms the
decision, not the installation of that boundary in an environment.

## Decision

Use pairwise internal Docker bridges and distinct Redis application identities.
Each identity receives only the command-and-key combinations its real adapters
need, with separate credentials for persistent and volatile Redis.

### Network membership

Create a separate internal bridge for each pair below. The left column describes
the intended caller, not a direction enforced by a bridge.

| Intended caller | Peer | Dependency |
|---|---|---|
| Caddy | web | Web traffic and health checks |
| web | API | BFF calls |
| API | PostgreSQL | Application data |
| Worker | PostgreSQL | Background jobs |
| migrate | PostgreSQL | Schema maintenance |
| migrate-rewrap | PostgreSQL | Key maintenance |
| API | persistent Redis | Sessions and application caches |
| Worker | persistent Redis | Landing-statistics publication |
| API | volatile Redis | Authentication challenges and budgets |
| API | Seq | Log ingestion |
| Worker | Seq | Log ingestion |

Caddy, API and Worker each receive their own egress bridge. No shared bridge may
reconnect otherwise separated peers. Web, Caddy, PostgreSQL, migration services
and Seq receive no direct Redis path; Worker receives no volatile Redis path.

A Docker bridge permits bidirectional access to listening ports between its
members. Pairwise membership does not promise direction or port filtering and
does not replace Redis authorization. Host forwarding rules and their rollback
order must be prepared for the new topology. API forwarded-header trust is
limited to the web–API network, coordinated with #1767.

### Redis identities and policy

| Application identity | Store | Permitted data responsibility |
|---|---|---|
| API/persistent | Persistent | Sessions, session indexes, revocation and deletion markers, existing cooldowns and company caches; read-only landing statistics |
| Worker/persistent | Persistent | Write-only access to exactly `jobbliggaren:landing:stats:v1` |
| API/volatile | Volatile | Challenge records, address indexes and explicitly enumerated budget namespaces |

Disable the default user and start every application policy from denied access.
Use separate ACL selectors for each command-and-key combination; do not combine
the API's authentication writes with its landing-statistics read permission.
Do not grant broad command categories, a general `jobbliggaren:*` namespace,
administration, keyspace scanning or unused pub/sub access.

The executable ACL templates and real-adapter tests define the detailed command
inventory in the [Redis service-boundary runbook](../runbooks/redis-service-boundaries.md).
Account for the pinned clients' wire commands, including `HMGET`, `HMSET` and
the required expiry operations. API/volatile additionally receives only the
verified transaction and Lua operations its adapters require. API/persistent and
Worker/persistent receive no scripting permission. Disable unused discovery,
tie-breaker and pub/sub behavior in the shared Infrastructure configuration;
allow only the connection commands that remain necessary.

### Credentials, startup and verification

Mount credentials read-only and separately for each service, reusing `_FILE`
configuration loading. Worker must not be able to read either API credential;
placing an API secret in a directory mounted by both hosts violates this
boundary. Redis ACL files and operator credentials are not application mounts.

After production integration, startup rejects missing, empty or malformed
configuration, an unexpected identity, failed authentication and the wrong
store. There is no silent fallback or general legacy-mode switch. Reuse #1735's
typed volatile connection and boot contract instead of introducing a competing
registration path.

Readiness uses the actual application connections: both stores for the API and
persistent Redis for Worker. `PING` establishes availability under that identity;
it does not establish least privilege. A separate mandatory operator preflight
verifies the effective ACL against the expected policy digest and `ACL DRYRUN`
allow/deny cases. Real-adapter tests cover command behavior, including Lua and
transactions.

Container health uses a separate identity with only `PING`. The probe succeeds
only when the process exits successfully and its complete combined output is
exactly `PONG`. Credential revocation includes terminating existing connections
and verifying that the revoked credentials cannot reconnect; changing a password
alone is not the revocation procedure.

### Session integrity evaluation

This decision introduces no session cryptography. The architecture and security
reviews identified a separate future evaluation of Data Protection or another
integrity mechanism for session records. That evaluation must address binding
the protected value to the session key, replay, sliding and absolute expiry,
rotation, revocation and deletion markers, legacy records and key recovery.
It must not be presented as a property supplied by network separation or ACLs.

## Alternatives considered

### Pairwise bridges with service-specific egress and ACLs — selected

**For:** expresses the required service pairs and independently restricts data
operations when a service can reach Redis.

**Against:** more network declarations, credentials and verification cases, with
coordinated host forwarding changes. Shared pairs still have bilateral reach.

### Tier bridges with a shared backend network — rejected

**For:** fewer network declarations and a simpler initial Compose change.

**Against:** placing API, Worker, PostgreSQL, persistent Redis and Seq together
creates sibling paths that their responsibilities do not require.

### One held PR for preparation and production integration — rejected

**For:** one combined review and delivery unit.

**Against:** unnecessarily binds independent ACL design and isolated tests to
the #1735 handoff. The selected two-PR sequence makes the preparatory contract
reviewable without introducing new runtime boot requirements.

### Compatibility through fallback credentials or a legacy-mode flag — rejected

**For:** an older deployment configuration could continue to boot.

**Against:** absent service identity becomes hidden and the required startup
refusal loses its meaning. Compatibility is instead measured and handled through
deployment sequencing. A session-integrity rollout is likewise deferred to its
own lifecycle design rather than bundled into this boundary change.

## Consequences

### Positive

- Network paths, readable secret files and Redis operations become separately
  testable service boundaries.
- The Worker publication/API read split preserves ADR 0064 without giving the
  Worker authentication access.
- The persistent/volatile split preserves their separate retention contracts.
- Shared templates allow isolated tests and later deployment to use one ACL
  definition, with explicit registration of new consumers.

### Negative

- Client upgrades or new Redis consumers may require an explicit policy change
  and another adapter verification run.
- Network, forwarding and credential changes require coordinated deployment and
  rollback preparation; a code image alone cannot install the boundary.
- Revocation must account for already-authenticated connections.
- Pairwise bridges and ACLs do not isolate a service from its permitted peer or
  provide session-value integrity.

## Implementation and acceptance

**PR1 — preparation:** the design contract, shared ACL templates and isolated
contract tests. It introduces no new production boot requirements and changes no
live configuration. Tests use synthetic data and their own Testcontainers
instances and networks, never a shared development store.

**PR2 — integration:** after explicit #1735 handoff, wire networks, service
credentials, client configuration, startup and readiness. Coordinate secret
injection with #1760 and forwarded headers with #1767. Preserve #1757's durable
session storage. #1737, #1739 and #1744 must register new consumers in the
contract before release.

Acceptance includes real session create/read/sliding/rotation/logout/mass-revoke
and deletion-marker paths; Worker-write/API-read statistics; challenge code/link
flows and concurrent consumption; budgets; cold/warm Lua and script-cache loss;
denied anonymous, wrong-identity, wrong-store, cross-service and administrative
operations; denied operations inside Lua and transactions; DNS and direct-address
network probes with positive controls; per-service secret visibility; credential
revocation with established connections; startup, outage, reconnect and log
redaction. Run the relevant architecture, API, Worker and script checks and retain
their actual test totals. These are acceptance requirements, not results claimed
by this ADR.

Before integration can merge, verify a new image with the previously deployed
Compose configuration. Account for scheduled image publication and reconciliation:
if that combination cannot start, hold the integration merge until a separately
authorized deployment can establish verified version pinning and prerequisites.
Prepare the operator preflight, rollback order and dated verification record.

This work authorizes no live configuration change, restart, rotation or session
forgery. #1759 remains open until its acceptance criteria and required operational
verification are complete. Public documentation contains intended requirements
and sanitized results; operational evidence and secrets remain private.

## References

- [Redis service-boundary runbook](../runbooks/redis-service-boundaries.md)
- [ADR 0064 — Public aggregate reads through Worker-precomputed Redis cache](0064-public-aggregate-read-via-worker-precomputed-redis-cache.md)
- [ADR 0142 — Passwordless authentication](0142-passwordless-auth-one-page-code-or-link-oauth-ready.md)
- [BUILD.md](../../BUILD.md), §§4, 11, 15 and 17
- [#1759 — Redis network and service-identity boundaries](https://github.com/klasolsson81/jobbliggaren/issues/1759)
