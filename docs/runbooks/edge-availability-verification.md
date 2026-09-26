# Edge abuse and availability verification

Owner: [#1767](https://github.com/klasolsson81/jobbliggaren/issues/1767), under
[#1758](https://github.com/klasolsson81/jobbliggaren/issues/1758).
This runbook defines the evidence required to assess availability controls. It does
not authorize deployment, external stress tests, or removal of development Basic Auth.
Keep operational inventories, account identifiers, raw logs and live findings private.

## Boundaries and ownership

The Option B request path is client → Caddy → Next → API. Caddy owns the
forwarded client address; Next relays it without appending; API middleware accepts
it only from configured trusted peers and runs before authentication and rate limiting.
Network trust is distinct from authentication of a particular service.

[#1202](https://github.com/klasolsson81/jobbliggaren/issues/1202) closed on
2026-08-08 with two-client budget separation and spoof rejection. Preserve this as
dated historical evidence. A topology or image change needs new evidence, not an
assumption that the earlier result measured the replacement.

| Owner | Boundary |
|---|---|
| #1735 / #1739 | Challenge, mail, code and reauthentication budgets; uniform outcomes and cooldown semantics |
| #1759 / #1760 and current stack owner | Network trust, container isolation and shared Compose changes |
| #1201 | Off-host evidence, human alert receipt, failure and recovery measurements |
| #1768 | Launch domain and host routing |
| #1767 | Cross-layer limits, bounded refusal tests and provider coverage evidence |

The protected development hostname is intentional. An unauthenticated 401 there
is an admission-control result. Assess the launch hostname separately through #1768.

## Control inventory

Read the reviewed revision and effective configuration together. Values below
identify source configuration; they are not an assertion about any live deployment.

| Control / purpose | Source and configured contract | Required evidence |
|---|---|---|
| Client attribution | `deploy/caddy/Caddyfile`, `forwarded-headers.ts`, `ForwardedHeadersConfig`: one replaced XFF value, XFP relay, default forward limit 1 | Trusted/untrusted peers, independent client budgets, missing headers; repeat the chain test after topology changes |
| Admission / renewal | Caddy Basic Auth before proxy; unknown ACME challenge path returns 404 locally | Normal admitted request, unauthenticated refusal and local ACME refusal |
| Body consumption | Caddy 20MB (20,000,000 bytes); API global 16 MiB | At/over each applicable boundary, declared length and chunked transport |
| Upload handling | `ResumesEndpoints`: 11 MiB transport limit; validator 10 MiB file limit; BFF declared-length fast refusal | Valid multipart at supported file size, validation refusal and real-socket transport refusal; include multipart overhead |
| Server Action parsing | Next framework default 1MB; no application override in `next.config.ts` | Measure against the built Next version; this does not govern the streaming upload route |
| Header parsing | Caddy and Kestrel server limits; Node HTTP parser | Record effective bytes/count/line limits for each built runtime and refused oversized headers |
| Slow clients | Caddy header 5 s, body 10 s, write 30 s, idle 2 min | Actual incomplete-header/body termination and normal traffic afterward; distinguish HTTP versions |
| Upstream availability | Caddy health interval 5 s, health timeout 3 s, retry window 10 s | Bounded failure and recovery, with safe handling of non-idempotent requests |
| Request frequency | `RateLimitingOptions` + `RateLimitingExtensions`: per-policy partitions, zero queues, 429 with Retry-After | Legitimate bursts, exact refusal, independent partitions, window recovery and client retry presentation |
| Auth abuse budgets | `LoginChallengePolicy` / `AuthEmailCooldownOptions`, owned by #1735/#1739 | Preserve outer IP refusal versus uniform challenge outcomes; code-budget exhaustion must retain the approved link path |
| Work and resources | Effective process/container limits and downstream cancellation | Bound simultaneous work, queues and memory; prove that timed-out/disconnected work terminates, not merely that its response disappears |

Rate limits describe different mechanisms. `AuthWrite` defaults to 20/60 s per IP,
`AuthLoose` to 30/60 s; health checks have their own 120/60 s IP policy. Authenticated
list, suggest, preview and mutation policies have separate purposes and must be read
from the two rate-limiting source files. Token-bucket capacity and replenishment are
not a fixed-window request count. Shared-NAT usability is a separate acceptance test.
Per-process limits must be reassessed if replica count changes.

Do not introduce a blanket API deadline without evaluating the longer internal
`RecruiterErasureMatchQuery` operation. Measure cancellation through Next and API
separately from the edge write timeout. Container memory limits are not concurrency
limits, and provider filtering is not application rate limiting.

## Bounded local verification

From the repository root, with Node and a local Docker engine:

```sh
docker build -t jobbliggaren-edge-probe deploy/caddy
node scripts/edge-probe.mjs jobbliggaren-edge-probe
node --test tests/edge-probe-guards.test.mjs
dotnet test --project tests/Jobbliggaren.Api.IntegrationTests -- --filter-class '*ForwardedClientBudgetTests'
```

The edge probe also requires a locally available `node:22-alpine` image. Supply a
locally pulled Caddy digest to repeat a specific image measurement. It prints the
resolved image IDs and source revision. It mounts the checkout's Caddyfile and
challenge snippets read-only, uses synthetic Basic Auth and an HTTP-only test site,
and substitutes a small upstream that reads the request body. It creates a dedicated
Docker network and containers, publishes only a dynamic loopback port, and removes
only its own resources. It has no remote target argument and rejects remote Docker
contexts. Do not run it on the VPS or point it at an existing service.

Safety bounds: at most 100 client requests, sequential probes, less than 26 MB per
body request (four body probes), a 2 MiB synthetic header, bounded socket waits,
and explicit container CPU/memory/PID limits. Normal runs use far fewer requests.
An interrupted process may need cleanup of the exact emitted container/network IDs;
never use an unscoped Docker prune command.

The probe covers admission, forwarding-header replacement, at/over body caps using
both transfer forms, oversized headers, incomplete reads and subsequent recovery.
Its synthetic upstream is not Next or the API. It does not measure TLS, HTTP/2,
HTTP/3, IPv6 reachability, API cancellation, provider mitigation or real user journeys.
`ForwardedClientBudgetTests` uses the real middleware and limiter registration in
a small TestServer host. Its synthetic socket peers exercise IPv4/IPv6 partitioning
and untrusted-header rejection, not production pipeline wiring or network isolation.

Keep full outputs privately with UTC time, source commit, image digest, test count,
transport, target, ceilings and stop conditions. Public summaries carry only sanitized
results. A passing options test is not a real-socket body-limit test.

## Provider verification

Official sources checked 2026-09-19:

- [Server documentation](https://www.netcup.com/en/helpcenter/documentation/server)
  advertises DDoS protection at 2 Tbps and annual minimum availability of 99.6% for
  VPS and 99.9% for root servers. This is offered coverage, not a per-server capacity
  reservation or proof that a particular attack would be mitigated.
- [Firewall documentation](https://www.netcup.com/en/helpcenter/documentation/server/firewall)
  describes separate configurable policies. Product availability does not prove a
  policy is assigned; with no assigned policy, traffic is allowed.
- [Terms §3](https://www.netcup.com/en/terms-and-conditions) define general availability
  and exclusions, including third-party faults outside provider control, force majeure
  and temporary interruptions to reduce exploit risk. Verify the actual contracted
  product and applicable terms rather than selecting an advertised percentage.
- [Support](https://www.netcup.com/en/helpcenter/support) links the provider status
  service and ticket route, and lists regular and emergency telephone hours.
  [Abuse notices](https://www.netcup.com/en/helpcenter/documentation/security/abuse-notices)
  separately describe abuse handling and chargeable emergency assistance. Confirm
  the route and applicable charges before treating it as an incident-response SLA.

The private provider record must establish product/contract version, protected
addresses and protocols, enabled mitigation, L3/L4 versus application-layer coverage,
activation/detection time, saturation/null-routing conditions, exclusions, support
route and response commitment. Record who receives alerts, who contacts the provider,
who may authorize recovery and the fallback when that person is unavailable. Obtain
this from account evidence or a provider response; SSH and marketing cannot prove it.
Owner-confirmed account 2FA is recorded independently and is not a DDoS control.

## Closure

#1767 stays open until its acceptance criteria are measured, including legitimate
navigation/upload/challenge/reauthentication journeys, #1201 alarm/recovery evidence,
private provider verification and the security-auditor's current M-5b assessment.
Record historical dispositions separately. An instrument PR can be reviewed and
merged without asserting that these operational closure conditions are satisfied.
