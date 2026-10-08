# ADR 0155 — Account access transitions fence sessions and original authentication proofs

**Date:** 2026-10-07
**Status:** Accepted
**Deciders:** Klas Olsson (the approved implementation plan for #1976) · `senior-cto-advisor`
(the plan approval and the bounded witness, credential-transition and split-phase request
decisions of 2026-10-07, in the external conversation “Granska två Codex-issues”) · inputs:
`security-auditor` and `dotnet-architect`
**Amends in part:** ADR 0013 (cross-context write boundary) · ADR 0142 (authentication proofs,
session admission and address writes) · ADR 0146 (account lifecycle commands do not replay) ·
ADR 0151 (account states) · ADR 0153 (pending address-change generations, request activation
and atomic completion)
**Related:** ADR 0017, 0018, 0022, 0024, 0028, 0143, 0149, 0154 ·
[#1976](https://github.com/klasolsson81/jobbliggaren/issues/1976) ·
[#1972](https://github.com/klasolsson81/jobbliggaren/issues/1972) ·
[#1994](https://github.com/klasolsson81/jobbliggaren/issues/1994)

Accepted records the approved architecture. It does not attest implementation tests, a final
review verdict, permission to arm the migration-bearing PR, or a completed production release.

## Context

The admin directory distinguishes a live account, a pending deletion and a missing profile
(ADR 0151). It has no independent way to stop a live account's access. Email verification,
deletion and the retired password lockout each describe another fact and cannot carry
suspension. #1976 adds suspension and reinstatement across passwordless code and link login,
Google, GitHub and LinkedIn, consent and registration completion, address confirmation and
existing sessions.

A flag checked only when resolving a login subject is insufficient. A challenge, OAuth state,
consent grant or already consumed address proof can outlive both suspension and reinstatement.
Refreshing that proof at callback would turn an old credential into a new one. Redis deletion
alone also fails when cleanup is unavailable or a delayed cleanup meets a newer generation.

The last administrator invariant crosses Identity roles, the stored address, the App profile
and access state. Concurrent suspensions, the owner's deletion and bootstrap must therefore
participate in one server protocol. Separate commits, or locks held on another connection,
leave a write able to escape the decision it was admitted under.

ADR 0153 keeps pending address changes in volatile Redis. Its D13 explicitly records that a
live record can remain after request-audit save or revocation fails. A protected Redis record
cannot, on its own, prove that the administrator's request committed. The approved witness
decision closes that boundary without making the protected address and code durable.

## Decision

Keep account access state and credential generations in the primary Identity schema. Admit
every protected write and session against its original proof, under one physical PostgreSQL
transaction, and use selective Redis cleanup after a confirmed commit. Address requests
release every database transaction and lock before mail transport and activate only after
fresh checks of their unchanged original authority.

### D1 — Access state is independent; directory precedence is explicit

`ApplicationUser` gains `IsSuspended = false`, `AccessRevision = 0` and
`CredentialCutoff = 0`. The state does not change email verification, the deletion timestamp,
or the retired password lockout. Reinstatement changes access state only: it never cancels a
pending deletion or restores a profile.

Directory status precedence is `ProfileMissing > PendingDeletion > Suspended > Active`.
Filtering and counts use that classification. Details also expose `isSuspended`, so a pending
deletion can truthfully show that access is suspended. The existing account panel remains the
interface for the actions (ADR 0150).

### D2 — A durable epoch fences flows that started without an account

Identity also owns the singleton `account_security_epoch`, with `id = 1` and initial
`value = 0`. Every successful suspension and reinstatement atomically advances it, increments
the target's `AccessRevision`, sets the target's `CredentialCutoff` to that epoch, and rotates
its Identity security and concurrency stamps. An actual address swap and a first inbox proof
perform the same credential transition without changing `IsSuspended`. Their original proof
is admitted before that mutation; the mutation does not rebase the proof. Arithmetic is
checked. A no-op or an already confirmed inbox advances nothing.

The singleton has no dependency on an account row and survives account erasure. An unbound
challenge or OAuth state captures the current **committed** epoch before it is stored, even
when no account exists for the address. A fresh primary connection reads that value; an
enclosing transaction's uncommitted epoch must never be published into a flow.

When its target becomes known, the proof requires `FlowEpoch >= CredentialCutoff`. This
compares the target's cutoff, not the current global epoch: another account's transition does
not invalidate an otherwise valid flow.

### D3 — Proofs preserve their origin through every handoff

Application owns `AccountAccessProof`: the original `FlowEpoch` and, for a known or bound
account, the original user id and `AccessRevision`. The same origin passes through code or
link consumption, OAuth callback, consent grants, registration completion, re-authentication
and address grants. Binding an originally unknown account adds its identity only after
admission; it never refreshes the flow epoch. A callback cannot upgrade an obsolete proof to
the current revision. Address-dependent handoffs also preserve their expected address.

Before provider linking, inbox-proof stamps, address mutations or session issuance, the server
reads fresh Identity and profile state from the primary database. Admission requires a live
profile, usable inbox address, no suspension, the target cutoff, and the original identity and
revision when bound. Missing state and database errors deny access. Pending deletion retains
its existing login outcome and never becomes a login restoration path.

A fresh server read of `EmailConfirmed` selects an account-only login scope or a lifecycle
scope for a possible first proof. It is a lock-selection hint, never authority to confirm the
inbox. Inside the selected scope, the server rechecks the unchanged original proof and the
fresh confirmation state. If an account-only scope discovers that first-proof mutation is
needed, it refuses rather than upgrading its lock set. The inbox recorder independently
requires the lifecycle lock before performing a first confirmation.

### D4 — Sessions carry a revision; the database is their authority

Each session records the account revision at issuance. Reads, creation and rotation check
fresh Identity, a live profile and that same revision against the primary database. A positive
cache or a read replica cannot authorize access. Rotation retains the session's revision; it
cannot manufacture a new generation.

Session issuance follows a confirmed database commit for provider links, inbox proof and its
audit, registration, or an address mutation. A flow without a credential transition uses its
unchanged original proof in its own short account scope. A first inbox proof or self-service
address swap instead produces `CommittedSessionAuthorization`, a purpose-limited internal
authorization for that exact committed transition. Its constructor is private; internal
factories admit only a first inbox proof or a self-service address change, and only a known
successful owner commit can confirm it. It authorizes the exact new user id, revision,
cutoff and address with an immutable intended session lifetime, refusing a different lifetime
before reaching the session adapter; it is neither client input nor a
replacement generation for the original proof. Fresh primary admission still applies.

The self-service endpoint may issue its replacement session after that confirmed commit.
Anonymous completion of an administrator's address request issues none. If replacement
issuance fails after a known swap commit, the result truthfully reports that the address
changed and requires a fresh login. Issuance and rotation refuse an enclosing mutation scope
rather than returning a session before an outer commit. An unknown commit outcome creates no
confirmed authorization, stops issuance and permits no automatic replay.

Suspension makes old sessions inadmissible at the next authentication even when Redis cleanup
fails. Reinstatement increments the revision again, so it admits only a fresh login and cannot
revive a session from before or during suspension. An ordinary request already authenticated
before suspension may finish; its next authentication must be refused.

### D5 — Changed records have a strict v2 format

Changed protected payloads, Data Protection purposes and key families use v2. Parsing checks
the complete generation shape, nonnegative values and a consistent identity/revision pair.
Malformed v2 records are unusable; their presence never triggers a v1 fallback. Existing
padding and dummy-comparison work remain part of the adapter contract.

Genuine v1 authentication proofs and sessions represent generation zero. They can be admitted
only for an account that has never crossed a credential transition. A legacy record cannot
claim a later generation. D9 records narrower address-change exceptions: genuine v1 pending
administrator changes are unusable, including at revision zero, and genuine v1 self-service
address challenges or grants without their activation witness require a fresh request.
Unrelated legacy login, consent and re-authentication retain generation-zero compatibility.

### D6 — One physical transaction owns locks, checks and both contexts

Application declares the reader, coordinator, scope, writer and cleanup ports; Infrastructure
owns provider SQL, connection binding, advisory locks and commit. `AppDbContext` owns the
physical connection and transaction. `AppIdentityDbContext` joins that connection and
transaction before any `UserManager` or other Identity write. The two contexts and raw provider
commands cannot write on separate transactions while claiming the same protected scope.

Lifecycle and credential-transition mutations acquire the global administrator advisory lock
before distinct account locks in sorted user-id order, including actor and target where
applicable. Ordinary login and the two short address-request phases use the same physical
transaction model with account-only locks. These are transaction-scoped locks; no
session-scoped lock is substituted.

Only the outer scope commits. A nested scope can borrow already held locks but cannot acquire
additional locks, reconnect a context, replay, or commit independently. Its incomplete or
failed operation poisons the outer scope. An EF interceptor checks the protected physical
connection and transaction before commands and refuses reopening while the scope is active.
A dead lock backend therefore cannot be replaced by a new connection carrying an old decision.
Abort clears staged tracking; cleanup failure must not replace the original commit outcome.

### D7 — Last-administrator protection is a database invariant

An effective administrator has the Admin role, a usable login address, a live profile and no
suspension. `EmailAddressRules.IsUsableInboxAddress` is the shared address predicate; the
last-administrator check must not substitute a different SQL approximation.

Self-suspension is refused. Removing the last effective administrator's access is refused
under the global lock and fresh actor/target checks, including concurrent requests. Own
account deletion, bootstrap and full Identity writes participate in this protocol. Future
role withdrawal and administrative deletion inherit it rather than introducing another lock
or a check-before-write window.

`AccountAccessMutationBehavior` surrounds UnitOfWork and audit: admission, change, audit,
save, then the outer commit. A lifecycle command cannot opt into
`IReplayOnConcurrencyConflict` (ADR 0146); the original code and decision are not replayable.
The administrator and self-service address-request handlers, and self-service address-code
verification, own their short scopes under `IOwnsAccountTransaction`. Generic audit and
UnitOfWork skip those commands; a surrounding transaction must not hold locks across their
transport or introduce another success-audit row.

### D8 — Admin writes report actual outcomes and preserve audit meaning

`POST /api/v1/admin/accounts/{id}/suspend` and `/reinstate` require the Admin policy,
`IAdminRequest`, `IReauthenticatingRequest`, the administrator's existing single-use inbox
re-authentication, and `AdminWrite`. Responses are `private, no-store`.

An unchanged state is `DomainError.Conflict`; missing Identity is NotFound; a disappeared
actionable profile is Gone. Refusal changes no account state, pending address change or
success audit row. The re-authentication credential and rate budget may already have been
spent. This is ADR 0153 D6's no-op rule, not a new success-shaped no-op convention.

Successful changes write `Admin.AccountSuspended` or `Admin.AccountReinstated`, with the
actor separate from the target aggregate and without addresses or credential contents.
State and success audit commit together. A confirmed commit yields a success receipt even
if subsequent Redis cleanup fails. An uncertain commit yields an unknown outcome, no
automatic replay, and no claim that a later status read proves this command succeeded.

### D9 — Both address-request paths activate only through an exact committed witness

Each new v2 address request preserves one immutable server-created request identifier. The
protected record and the one activation audit payload carry the same identifier:

| Flow | Identifier and protected provenance | Exact request event and aggregate | Proof lifetime |
|---|---|---|---|
| Administrator request | Random server `Guid` `RequestId`, original target access proof and original instants | `Admin.AccountEmailChangeRequested`, `User`, target account | 96 hours |
| Self-service request | The server's 22-character `ChallengeId` reused as the nonce; `EmailChangeRequestProof` holds it and the original issued/expiry instants | `User.EmailChangeRequested`, `User`, own account | 15 minutes |

These identifiers are not supplied over HTTP, derived from client correlation ids or minted
on read. The payload contains the request identifier, not an address, code, grant or mail
body. Self-service carries its protected request provenance unchanged from challenge into
the address grant; consuming a challenge never extends its original 15-minute lifetime.
A missing or malformed v2 provenance record fails closed without a legacy fallback.

`IAccountEmailChangeRequests` owns both witness contracts. Infrastructure owns bounded,
parameterized primary-database queries for the exact event type, aggregate type `User`,
target user id and request identifier. Each lookup is bounded to that proof's original
lifetime and does not materialize account audit history. Staged tracking, cached rows, the
latest request for the target, time proximity or a row for another request are not witnesses.
No SQL schema or durable outbox is added for request activation.

**Two short account scopes, with no database lock held during transport.** Both request
handlers require ownership of their scopes and refuse an ambient mutation transaction:

1. Under sorted account-only locks, admit the original authority and target, their revisions,
   cutoffs and current address; validate the destination and write the purpose-bound Redis
   record with its original identity, nonce and lifetime. The administrator path locks actor
   and target and requires the actor's current Admin authority. Dispose the entire database
   transaction and release every lock before calling the sender.
2. Send outside database transactions and locks. An administrator request awaits the warning
   to the current address first and the code to the new address second. Self-service awaits
   its new-address code. The previously completed re-authentication is not repeated or
   silently upgraded while transport waits.
3. In a second short account-only scope with the same sorted participants, recheck the
   **original** authority and target proof, current address, lifetime, destination availability
   and the exact still-current Redis request. Do not substitute current generations for the
   original ones. Only then write one request audit row, save and commit its activation.

The generic audit and UnitOfWork behaviors skip `IOwnsAccountTransaction` (D7); they cannot
create a witness before transport or leave an outer transaction held throughout the sends.
Both administrator request mails must be accepted before its activation commit. A transport
exception does not prove non-delivery. A known rollback, a send failure or failed selective
revocation leaves any remaining Redis record inert because its exact witness is missing.
An **unknown activation commit** may have committed the witness: the outcome must say that
it is unknown, not assert that the record is inert or automatically replay or resend it.

**Self-service verification checks activation before spending the code.** It reads the
protected request, freshly admits the original access proof and requires the exact committed
self-service witness before `ConsumeBoundCodeAsync`. A valid request whose activation has
not committed yet returns the activation Conflict and can be verified again; this branch
spends neither a code attempt nor the code itself. Verification preserves the original
request proof in the grant. Confirmation requires that exact committed primary witness again
inside the protected credential-transition scope, along with the original access and
current-account checks, before the atomic swap and its completion audit.

Public administrator completion checks its exact committed witness in the same protected
credential-transition transaction as original revision/cutoff and account-state admission,
current-address and non-Admin preconditions, the address mutation and its completion audit.
A failure rolls back the address and audit. An already consumed proof stays consumed;
suspension permanently fences it even after reinstatement. A completion's changed-address
notice is queued and sent only after its confirmed commit, and public completion issues no
session (D4).

**Legacy rollout exceptions, approved by the CTO.** Genuine v1 pending administrator changes
have no exact request nonce, so they are hidden from the pending view and answer the same
generic 410 at completion, even at revision zero and even with an old request audit row.
An administrator restarts with a fresh step-up, both mails and the full 72-hour delay.
Genuine v1 self-service address challenges or grants without protected witness provenance
likewise fail closed and require a fresh request. No v1 address record is upgraded or paired
by guess. Unrelated genuine v1 login, consent and re-authentication retain D5's bounded
generation-zero compatibility.

### D10 — Cleanup removes older generations without granting access

After a confirmed credential transition, cleanup removes sessions and pending address changes
with a revision strictly less than the new committed revision. This includes actual address
swaps and first inbox proofs as well as suspension and reinstatement. Guarded record deletion
compares the actual payload where replacement is possible. A delayed cleanup must not delete a newer
session or pending change. Cleanup is not the authority for access admission.

Suspension cancels the pending address change permanently through this fencing and cleanup.
When the panel knows a change is pending, its confirmation says
**“Det väntande adressbytet avbryts.”** Reinstatement during pending deletion reports that
the deletion continues. Server Actions perform code verification and mutation in one handoff;
no re-authentication grant reaches the browser. Direct command refusal is shown and focused
in Åtgärder without a success receipt (#1994).

### D11 — Registration and address writes replace split commit boundaries

Registration creates Identity, the profile, the terms acceptance and its account-created
audit in one transaction. It no longer relies on deleting an already committed Identity row
as compensation when the profile save fails. The former orphan shape is historical; the
current writer must be tested not to produce it.

User name and email change atomically in the protected transaction, with the unique user-name
write first. The successful swap advances epoch, revision, cutoff and stamps in that same
transaction without changing suspension state, then records its completion audit. First
inbox confirmation similarly commits its flag, credential transition and audit together.
Provider linking and inbox writes commit before separate session issuance; a transition can
authorize that session only through D4's internally confirmed authorization. A fresh server
hint never permits a mutation inside a scope that lacks the required lifecycle lock.

ADR 0142's and ADR 0153's former partial-write diagnostics, success after a missing completion
audit, notice-before-commit arrangement and authority of unactivated Redis address records
are replaced by these boundaries. Address requests use D9's two short phases rather than
holding a database transaction through transport. Own deletion participates in the same
lifecycle protocol; a no-op refusal records no change.

### D12 — Retention and erasure do not expand for the witness

The pending address/code record remains volatile for at most 96 hours. Its delay remains
72 hours and usable window 24 hours (ADR 0153 D2). Self-service address provenance retains
its original 15-minute challenge lifetime, including when carried into a grant; activation
and verification never restart that clock. Audit retention remains 90 days (ADR 0024).
The request payload's opaque identifier links the exact protected request to its activation
without adding an address, code or mail body to the audit. Self-service reuses its existing
server-created challenge id instead of creating another client identifier.

The existing eraser nulls actor identity, IP address and user agent while retaining event,
aggregate and payload. Witness admission does not require an existing actor, so legitimate
actor anonymization does not break it. Original current-account admission still applies;
retained evidence cannot restore an erased target. Missing evidence fails closed. Neither
nonce introduces a retention extension, a new storage category for address/code content,
an erasure exemption, a SQL schema addition or a durable outbox.

### D13 — Migration and release compatibility fail closed

`20261007074622_AddAccountAccessSuspension` is an Identity-context migration. It introduces
D1's columns and D2's constrained, seeded singleton. Its Down takes an exclusive lock on the
epoch table and refuses unless the singleton still exists at zero. Once the global epoch
has been used, deleting every affected account does not make rollback safe.

Ordinary and pinned reconcile compare actual Identity migration history with the verified
candidate manifest before changing services, applied tags or the receipt. Schema or policy
incompatibility refuses application. A narrow manual `--prepare-identity` path performs the
approved prepare/drain/ACL/bootstrap/apply procedure under one reconcile lock, for one
verified candidate. Resume admits only the complete predecessor policy on first preparation,
or that same candidate's complete policy after an interruption; partial or unknown policy,
credential drift, a different release or incompatible live processes refuse continuation.
Any preparation checkpoint also blocks ordinary/channel/pinned/stage mutations under that
lock. Bootstrap receives the bound predecessor and exact approved additions and compares
actual primary history and the compiled manifest before any schema/grant mutation; it admits
only exact predecessor or complete candidate history. Missing/invalid binding or read failure
refuses. The separately explicit initial-bootstrap contract requires empty history.

Installed files, mounted policy and effective Redis ACL must all agree. PING and file
presence cannot establish this. Positive and negative probes include the real v1/v2 adapter
contracts and script-cache loss. No automatic schema or ACL rollback is permitted. The
operational procedure lives in [the VPS runbook](../runbooks/vps-deploy-stack.md), including
Identity bootstrap and its readback; this ADR does not duplicate the command sequence.

For this migration-bearing PR, Klas's concrete GO before arming binds the final reviewed
HEAD/tree, exact migration set and preparation procedure. Permission for the subsequently
published release is explicit; its actual digest and provenance must be verified against
that approval. Klas performs escrow publication with the existing seven secrets and the
signed-in live checks. ADR 0154's data-reading restriction remains in force. No plan verdict
substitutes for final-head panel, native code/security review, CTO approval or Klas's GO.

## Alternatives and replaced arrangements

The following are the choices expressed in the approved plan and the recorded predecessor
boundaries; no additional design alternatives are inferred from the implementation.

- **Selected: durable access generations with protected database transactions.** This
  fences old proofs and sessions independently of Redis availability and serializes the
  effective-administrator invariant. Its costs are primary-database work on authentication,
  coordinated connection ownership, a migration and a coordinated ACL rollout. Address
  request transport sits between two short scopes with exact post-send activation.
- **The previous split writes and Redis teardown as the decisive session boundary**
  (ADR 0142 D5; ADR 0153 D7–D9 and D13). It keeps simple local operations, but the documented
  gaps permit partial address writes, a change without its success audit, or sessions
  surviving failed cleanup. The approved plan replaces those write boundaries and makes
  database access state authoritative for suspension.
- **The latest target request row as the pending change's pair** (ADR 0153 D9). It avoids a
  payload field but cannot prove which request committed when failed or replaced records
  remain in Redis. The CTO's exact request-id witness replaces that pairing. Committing a
  witness before best-effort request mails is also refused by the same decision; both mails
  precede its commit.

## Consequences

### Positive

- Suspension and reinstatement cover all login and session paths without reviving credentials.
- Concurrent lifecycle operations cannot remove the final effective administrator by racing
  separate reads and writes.
- Failed cleanup does not undo a committed transition or make old generations admissible.
- Account and success-audit writes share their outcome, and either address-change path
  requires its exact committed request activation.
- Provider and relational mechanisms remain behind Application-owned ports.

### Negative

- Authentication now depends on a fresh primary-database read; database failure denies access.
  The latency cost requires the scoped measurement under ADR 0045 before final acceptance.
- Global lifecycle serialization and account locks constrain concurrent security writes.
  Releasing them during request transport requires fresh admission of unchanged original
  authority and the exact Redis request before activation can commit.
- Genuine v1 pending administrator address changes require a restart and another full delay;
  old self-service address proofs without an activation witness also require a fresh request.
- Unknown commit outcomes cannot safely offer automatic retry or infer command success from
  the account's current status.
- A coordinated Identity and Redis-policy rollout is required. Once used, the epoch prevents
  migration Down even if the transitioned accounts have since been erased.

## Implementation and acceptance

#1976 implements this decision in one PR. The relevant owners are
`Application/Auth/Access`, `SqlAccountAccess` and its protected-transaction interceptor,
the session decorator, authentication adapters, account lifecycle behavior and commands,
the directory and account panel, and the existing release reconciler. The migration writer
alone owns the Identity migration.

Acceptance uses migrated PostgreSQL and real Redis with production-reachable fixtures. It
covers all login paths, pre-existing sessions and rotation, before/during-transition proofs,
fresh reinstatement login, policies and direct Mediator refusal, self/last-admin races,
pending deletion and address changes, no-op audit behavior, audit rollback, unknown commit,
cleanup failure and delay, killed lock/write backends, legacy/v2 parsing, exact witness
admission and actor anonymization, request transport without held database locks, changed
original authority or replaced request during transport, verification before activation
without spending the code, internally confirmed transition authorization, Redis restart,
ACL boundaries and SCRIPT FLUSH. Tests must pin that current registration and atomic address
writers no longer produce the retired partial-write premises.

Changed web outcomes must be rendered before the design verdict, with keyboard/axe and
scoped Lighthouse checks. Authenticated HTTP latency must be measured. Test and CI results,
final-head review evidence, concrete GO and release readback belong in the PR and session
evidence; none is claimed by this ADR's Accepted status.

## Amendment 2026-10-08 — Explicit administrator deletion (#1977)

The approved #1977 plan reuses this lifecycle through an explicit-target
`AccountDeletionScheduler`, called by separate owner and administrator handlers.
The administrator retains its own identity, session and inbox proof; the target
never becomes an impersonated actor or a data-key owner in that scope.

Scheduling advances epoch, revision, cutoff and stamps even for a suspended
target, without changing suspension. Profile/application/resume soft-delete,
provider-link erasure and the separate actor/target audit commit together.
The application cascade loads notes, follow-ups and status changes; each child
receives the same deletion stamp while an unrelated account's timeline remains live.
After known commit, selective session and pending-address cleanup remains best
effort; primary-database access denial remains authoritative. Unknown commit is
never replayed automatically. Admin self-deletion, last-effective-admin removal
and already pending deletion refuse; the no-op changes no dates, revision or
success audit. Existing self-service HTTP/refusal contracts remain intact.

New scheduling stamps capture one clock instant at millisecond precision for the
whole cascade and events, so returned and persisted receipt timestamps agree.
Historical timestamps keep their stored precision. Eligibility is the actual
stamp plus the 30-day product grace; the first daily 04:00 UTC run strictly after
eligibility is a projected run, not proof of completed erasure. Restore remains
unavailable. This amendment adds no migration or legal retention rule.

The existing worker shares one physical App/Identity transaction for owned
graph/DEK erasure and audit anonymization. A real Identity-delete failure retains
the whole account, isolates subsequent accounts and permits a later ordinary
retry. Feedback dispatch/requeue use live-profile checks. Top-direct matching,
match digests and followed-company digests repeat the live-profile/consent check
after the persisted claim and immediately before transport, retaining `Queued`
without automatic replay when access is no longer admitted. No transaction spans
mail transport; mail already handed off cannot be recalled.
Implementation, final-head review and live acceptance evidence remain separate
from this approved plan and amendment.

## References

- AGENTS.md §§2.1, 5, 6, 7, 8, 12 · CLAUDE.md §§6.5, 9.2, 9.6 · BUILD.md §§4, 6.2, 11, 13
- ADR 0013, 0017, 0018, 0022, 0024, 0028, 0045, 0142, 0143, 0146, 0149, 0150, 0151, 0153, 0154
- [#1976](https://github.com/klasolsson81/jobbliggaren/issues/1976), including
  [pending-address cancellation](https://github.com/klasolsson81/jobbliggaren/issues/1976#issuecomment-5994465782)
  and [direct-command refusal debt](https://github.com/klasolsson81/jobbliggaren/issues/1976#issuecomment-5980290675)
- The approved #1976 plan; the CTO's bounded exact-witness, address/first-inbox credential
  transition and administrator/self-service split-phase request decisions of 2026-10-07,
  in the external conversation “Granska två Codex-issues”. These are architecture approvals,
  not a final implementation verdict or migration/release GO.
- [PostgreSQL 18 — Advisory Locks](https://www.postgresql.org/docs/18/explicit-locking.html#ADVISORY-LOCKS),
  read in the approved plan review on 2026-10-07
- [VPS deployment](../runbooks/vps-deploy-stack.md) ·
  [Redis service boundaries](../runbooks/redis-service-boundaries.md) ·
  [Administrator-initiated address changes](../runbooks/account-email-change-by-administrator.md) ·
  [Threat model](../threat-model.md)
