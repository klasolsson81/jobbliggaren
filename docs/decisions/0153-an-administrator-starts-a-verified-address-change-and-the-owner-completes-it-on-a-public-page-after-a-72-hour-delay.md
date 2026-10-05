# ADR 0153 — An administrator starts a verified address change and the owner completes it on a public page after a 72-hour delay

**Date:** 2026-10-05
**Status:** Accepted
**Deciders:** Klas Olsson (controller: the delay, answered 2026-10-04 on #1975, comment 5984508392; the issue's scope
note of the same day; epic #1972) · `senior-cto-advisor` (the routing of the #1975 form round: every verdict but the
delay unambiguous under CLAUDE.md §9.2; `docs/reviews/2026-10-04-1975-form-cto.md`, local) · inputs: `security-auditor`,
`dotnet-architect` and `design-reviewer`
(`docs/reviews/2026-10-04-1975-form-{security-auditor,dotnet-architect,design-reviewer}.md`, local)
**Amends:** ADR 0142 (five dated pointers: D1's key families, D5's swap caller, the Page form, "Attempt budget" with
lapse trigger 5's re-run, and the class line of Amendment 2026-09-19 (2)) · ADR 0150 (D8: one row rewritten, one added) ·
ADR 0151 (D6's radius pin gains one handler)
**Related:** ADR 0008 (pipeline order) · ADR 0022 (audit rows come from commands) · ADR 0023 (the Worker has no
`ISessionStore`) · ADR 0024 (erasure) · ADR 0028 (admin authorization; its update of 2026-10-04) · ADR 0142 (D1, D5, D10
and Amendments 2026-09-21 (4), 2026-09-23 (6) and 2026-09-25 (14)) · ADR 0143 (Redis boundaries: the real-adapter rule) ·
ADR 0150 (the admin surface: D2, D4, D8) · ADR 0151 (the directory: D6, D7) ·
[#1972](https://github.com/klasolsson81/jobbliggaren/issues/1972) (the epic) ·
[#1975](https://github.com/klasolsson81/jobbliggaren/issues/1975) (the issue this ADR ships under) · #1976 (suspension) ·
#1977 (scheduled deletion) · #2003 (the ACL-only PR) · #2001 and #2006 (the bootstrap repair) · #1879 (the box's
recovery path)
**Measured against:** the form round read `origin/main` at `4cedf5e60`, 2026-10-04. The delivered code is the #1975
branch on `origin/main` at `6566dceed`, read 2026-10-05. The arithmetic in ADR 0142's pointer was re-taken 2026-10-05
and is not an observation.

> **Provenance.** Written by `adr-keeper` for the driving session, from its brief and the form-round record (CLAUDE.md
> §9.2, §13). The delay is Klas's decision (D2). Every other decision is `senior-cto-advisor`'s routing of the form round
> over the three agent memos, except D4's deviation, which was decided while building. The mandatory agents review the
> ADR in the PR (CLAUDE.md §9.2).

---

## Context

**An owner without the inbox has no way in.** Self-service (Mina sidor, "Byt e-postadress") proves both inboxes with two
codes (ADR 0142 D5), so an owner who can no longer read mail at the account's address cannot use it. ADR 0150 D4 gives
#1975 "changing an account's email address", and the issue's scope note of 2026-10-04 fixes what that means: accounts
store no name (ADR 0142 D7), so the issue is a verified email change, and its acceptance criteria forbid an
administrator's approval from bypassing inbox proof or silently relinking an external provider. The one other route was a
manual change in the database, which `security-auditor` ruled never acceptable: it bypasses every guard.

**The address is the recovery vector.** An address is what enters an account (ADR 0142 D10), so whoever holds an
account's address holds the account, CVs included, and an administrator can already read every account's address (ADR
0151). A change an administrator makes alone, or one that completes the moment it is asked for, would turn the admin role
into a takeover capability. The form round weighed five shapes: A1 and A2 (an administrator starts the change and the owner
completes it, without or with the account's current address), B (the change completes at the owner's next login), C
(read-only, with a runbook) and E (a delay before the swap). `security-auditor` signs A2 plus E, with the must-holds this
ADR records, and rejects A1.

**What the delivered code could not give this flow.** Measured 2026-10-04 by the form round, on `4cedf5e60`:

- `SwapConfirmedAddressAsync` compared nothing about the account's old address, so a swap could move an account that had
  changed its address in the meantime, or had since been granted Admin.
- On an anonymous route `AuditBehavior` stamps the user as null, and `AuditTrailEraser` clears an audit row's IP address
  and user agent only `WHERE user_id = {id}`. A completion row written that way would keep the completer's IP address
  and user agent after the account is erased, which makes the privacy policy's statement that log data linkable to the
  account is anonymised when it is erased false (Art. 13, Art. 17).
- The volatile ACL granted no family a read of an index or of an expiry (no `GET`, `TTL` or `PTTL`), and each existing
  family is shaped for one flow.
- The architect's ceiling of 24 hours for anything on the volatile instance does not hold. Nothing in the instance
  imposes it: Redis enforces any TTL, the ACL grants `+expire` without a bound, and the instance runs `noeviction` with no
  persistence. The one 24-hour figure that matters is how long an anonymous flood can keep the instance full (ADR 0142
  Amendment 2026-09-19 (2)).

**The delay.** Without one, the notice to the account's current address only reports a change already made.
`security-auditor` graded that Major (M-2): ADR 0142 D5 rejected that shape for self-service as "detection sold as
prevention". She put one controller question, the length of the delay between the start and the earliest completion
(Art. 24(1)); the architect's and design's open questions on the code's life and on live support were answered by the same
choice and by measurement (the contact page offers e-mail only).

**A neighbouring defect, repaired first.** The admin bootstrap granted the Admin role at every start to whoever held the
configured address, so an administrator who changed address left a second Admin holder reachable (`security-auditor`
M-1, measured 2026-10-04). With #1975 that would have been a takeover capability, so it was repaired in its own PR before
this one arms (#2001, #2006; ADR 0028's update of 2026-10-04).

## Decision

Thirteen decisions govern the flow, each in one place. The form round called the delay "D" and the usable window "W".
Here they are named by their constants, so that D1 to D13 mean only decisions.

### D1 — The administrator starts, the owner completes, and neither can finish the change alone

An administrator re-authenticates with a code to their own address and names the new address for an account that is
Active, holds an address and does not hold Admin (D5). The account's current address is told at once, and the new
address gets a code that works only once the delay has run, and then for the window (D2). The owner completes the change
on the public page `/adressbyte` by presenting the account's current address, the new address and the code. The server
compares all three, moves the account to the new address, ends every session of the account and issues none (D7, D8).

Nothing else changes an address. An administrator cannot complete a change, because the code reaches only the new inbox,
and an owner cannot complete one without knowing the account's current address.

*Why.* Each requirement closes a different failure.

- The code proves the new inbox.
- The current address ties the completion to someone who knows the account, which is what A1 lacks: one typo by the
  operator would hand the account to the holder of the mistyped inbox, with no attacker effort, which is
  `security-auditor`'s Blocker class (PII exposure).
- The delay turns the notice to the current address from a report into a chance to object, for every owner who still
  reads that inbox (D2).

The threat table (`security-auditor` §3) reads accordingly. A stranger at a mistyped new address has the code and lacks
the current address, and the record is found by the new address, so every guess at the current address spends that
change's three attempts (D3). A requester who deceives the administrator, and a malicious or compromised administrator,
are bounded by one step-up per request, Admin targets refused, the notice, the delay and an audit row naming the
administrator. The step-up also bounds an administrator at ten starts a day (`reauth-codes`, ADR 0142 Amendment
2026-09-21 (4)). A stolen administrator session alone cannot start a change, because the step-up code goes to the
administrator's own inbox. Today there is one Admin holder, the controller (ADR 0151 D7, read 2026-10-04 16:44:38 UTC).

### D2 — The delay is 72 hours: Klas's decision, with a 24-hour window and a 96-hour record

The form round put one controller question to Klas, `security-auditor`'s escalation merged with the architect's Q1 and
design's Q1: how long the delay between the start and the earliest completion should be, during which the account's
current address has been told and an objection stops the change. The three options:

- **(a) 72 hours.** Objections arrive by mail at kontakt@ and an administrator cancels in the panel, which needs no new
  interface. `security-auditor` and `senior-cto-advisor` recommended it.
- **(b) 24 hours,** with the owner able to stop the change on Mina sidor after logging in with the current address.
- **(c) No delay.**

**Klas chose (a) on 2026-10-04** (#1975, comment 5984508392), verbatim: "kör enligt REK, att jag byter epost kommer nog
ske högst sällan, troligen aldrig." ("REK" is the recommendation.) There is no override.

`AccountEmailChangePolicy` holds three constants: `Delay` 72 hours, `UsableWindow` 24 hours and `Ttl`, their sum, 96 hours,
in whole seconds. They are constants and never configuration, for the reason `LoginChallengePolicy` gives: the code's life
is one of lapse trigger 5's quantities, and every mail and the privacy policy state these values.
`AccountEmailChangePolicyTests` spells the literals out. The code's length and its attempts are the login challenge's own
(6 digits, 3 attempts), so trigger 5 keeps one home.

The not-before is a fact inside the protected record, enforced at the completion by the server's clock (D3). A full match
before it is the "not yet" answer (D7), which neither consumes the change nor spends an attempt.

The code exists for the delay plus the window and is usable only during the window. The window is 24 hours because the
closest published rule, NIST SP 800-63B-4 §4.2.1.2 (read 2026-10-04), caps an e-mailed recovery code at 24 hours: the
window meets it as a usable window, and a strict reading counted from issuance does not. `security-auditor` owns that
exposure and confirms it at the PR round.

*What it costs, as the question stated it to Klas.* An owner who has lost the inbox waits three days. The protection
assumes that Klas reads kontakt@ while a change waits. There is no new interface. The new address and the code are stored
protected for at most four days. Klas starts every such change himself while he is the only administrator, so he knows
when a waiting period is open. Option (a) forecloses nothing: (b)'s owner-side stop would be additive on the same store
and index.

### D3 — A port of its own, and two key families on the volatile instance

`IAccountEmailChangeStore` (`Application/Auth/AccountEmailChanges`) is implemented by `RedisAccountEmailChangeStore` on
`VolatileRedisConnection` and nothing else. Two key families hold a pending change:

| Key | Holds | Found by |
|---|---|---|
| `auth/account-email-change/v1/{hex}` | the record, one hash: `p`, the protected payload; `a`, the attempt counter; `o`, the owner marker; `n` and `x`, plain copies of the two instants, for the administrator's read, which never unprotects | the fingerprint of the NEW address: the completing POST's one lookup |
| `auth/account-email-change-by-user/v1/{hex}` | the index: the account's one pointer to its record | the fingerprint of the user id: the administrator's handle for cancel and read |

Both `{hex}` values come from `SubjectFingerprint`, the one normaliser home (ADR 0142 D1). The payload is protected under a
DataProtector purpose of its own and holds the new address as the administrator typed it, the code, the account's id, the
**fingerprint** of the account's current address, and both instants. The not-before is enforced from inside the payload;
the plain copy in `n` is for display only, because a Redis reader is in scope (ADR 0142 D1's threat model).

**The current address is never stored** (Art. 5(1)(c)). The completer's input is compared by its fingerprint, which is the
fold login uses (`SubjectFingerprint`: trimmed, NFC, then upper-invariant), so a phone's capitalised address or a pasted
trailing space is not a refusal the page cannot explain.

**The record is found by the new address, never by the current one.** Were it found by the current address, a stranger who
holds the code could try every plausible current address, each guess landing on another record or none, and none would
spend this change's attempts.

Both keys carry the record's whole life, delay plus window. The store sets every expiry itself, in whole seconds, because
the ACL cannot constrain the arguments of `SET` or `EXPIRE`.

**The wire stays inside the grant.** #2003 grants the two families ahead of the store, to the `api-volatile` identity: the
record family `HMSET HGET HINCRBY EXISTS EXPIRE UNLINK EVAL EVALSHA`, the index `SET GET`. The adapter keeps to it, as
#1975's comment of 2026-10-04 binds (5984415080), because each departure would fail with a `NOPERM` that looks like a
missing grant:

- The expiry is a whole-second `EXPIRE`: `KeyExpireAsync(key, DateTime)` sends `EXPIREAT` or `PEXPIREAT`, and a fractional
  `TimeSpan` sends `PEXPIRE`.
- No StackExchange.Redis `Condition` is used, because it sends `WATCH`. The one-live-record check and every guarded delete
  are one-key scripts, and a script deletes with `UNLINK`, never `DEL`.
- Reads are `HGET`, or a one-key script that also returns the expiry field (`HMGET` and `PTTL` are not granted). Writes are
  `HMSET` or `HINCRBY` (`HSET` is not granted). The index is written with `SET … EX … GET` and never `SETEX`, which is what
  `StringSetAsync` sends for a whole-second expiry.
- Every script stays within one family.
- The anonymous consume keeps `EXISTS` before `HINCRBY`: as `api-volatile`, a bare `HINCRBY` on a missing record key creates
  a hash with no TTL on a `noeviction` instance. The "not yet" refund runs inside the same guard.
- Every granted verb has a call site in the adapter. A verb that lost its call site would be narrowed out of the selector.

**ADR 0143's real-adapter rule.** `RedisAclContractTests` carries two facts. One shows the granted verbs work and the
denials hold. The other runs the store's own put, consume, cancel and read as `api-volatile` through the adapter's key
builders, after a `SCRIPT FLUSH` so that `EVALSHA` runs through its reload, asserts a TTL on both keys, and then asserts
the refusal of the commands the adapter never sends. If this PR is withdrawn, #2003's two selectors are reverted.

*Why a port of its own.* `ILoginChallengeStore` says "a third kind is the signal to split this port", and this is a third
kind: an anonymous holder finds it by address, an administrator finds it by account, and it carries a second factor.
Packing it into `challenge-bound` would break ADR 0142 D1's rule that a record-shape change costs a new segment, ignore
that signal (Martin 2017, ch. 10) and blur least privilege per key family. What the stores genuinely share is shared, not
copied: `ChallengeCodeArm` holds the mint, the attempt-before-compare script and the dummy code, and both stores call it,
so a security mechanic has one home (Hunt/Thomas 1999).

*Why the volatile instance.* An artefact that lives at most four days earns no migration on the single-owner hotspot, and
Postgres would make durable the personal data ADR 0142 keeps volatile. No scheduler, Worker or ADR 0023 amendment is
needed either, because the not-before is checked by the completing POST. A restart of the instance drops the record, so it
can cancel a pending change and can never shorten the delay: it fails closed.

### D4 — What each operation guarantees

- **Put.** One change per account: a put displaces the account's earlier change. (The panel never offers a second request
  while one is pending, so only a race reaches the displacement.) One change per new address: a put for an address
  another account's change holds is refused and displaces nothing. A put is three steps. One script writes the record
  unless another account's owner marker holds the key, with the TTL in the same script. The index is then overwritten with
  the record's segment, returning what it held. The record it displaced, when its segment differs from the new one (a
  same-address re-request never deletes its own fresh record), is removed by a script that deletes it only while its owner
  marker is still this account's.
- **Revoke.** Removes the record a put wrote and never a newer one to the same address: the script is guarded on the
  payload that put wrote. The request uses it when a mail is refused (D5).
- **Consume.** The record is found by the new address. One script counts the attempt before anything is compared, and does
  nothing when the record is absent. The code and the current address's fingerprint are then compared in fixed time within
  that one counted attempt, neither short-circuiting the other, and a missing or burned record pays the same dummy work.
  The third miss burns the change: it stays until its TTL, answers like a miss, and the pending read shows `CodeBurned`. A
  full match before the not-before answers "not yet" and gives the attempt back. A full match after it consumes the change
  once, by a delete that returns 1, and only while the account's index still names the record and the window has not run
  out. Past the window, or for a record the index no longer names, a match consumes the change and is refused.
- **Cancel.** Reads the index, then deletes the record only while its owner marker is the account's. It answers true when
  something was removed. Against a completion racing it, exactly one of the two gets through.
- **Read.** Reads the index, then one script that returns the attempt counter and the two plain instants, only while the
  owner marker matches. No unprotect and no address.
- **The index is never deleted**, so a stale entry outlives a completed or cancelled change. Every operation that reaches a
  record through it therefore re-checks the record's owner marker in the same script as its read or delete: a stale
  pointer reaches neither the read nor the cancel of another account's change at the same address.

**Deviation from the plan, decided while building.** The plan guarded the displaced record's delete by the owner marker and
also by an older not-before. The delivered store guards it by the owner marker only. With the extra guard, one
interleaving of two puts leaves the newest record orphaned: when the two puts' index swaps cross, the index names the
older record, and the older put, which swapped last, declines to delete the newer one because the newer one's not-before
is not older than its own. Without the extra guard, every interleaving ends with exactly one live record, the one the
index names, and the consume-side index check keeps any orphan, left by a Redis fault between the index swap and the
displaced delete, from completing.

### D5 — The request: gates cheapest first, two budgets keyed by the new address, and both mails awaited

`POST /api/v1/admin/accounts/{id}/email-change` with `{newEmail, reauthGrant}` is `RequestAccountEmailChangeCommand`: an
`IAdminRequest` (ADR 0028) and an `IReauthenticatingRequest`. The admin gate runs before the grant is redeemed, so a caller
without the role never spends one; the grant is the administrator's own, redeemed against `ICurrentUser`, and each request
spends one. Custody follows ADR 0142 Amendment 2026-09-23 (6): the web verifies the code and performs the request in one
Server Action, so no grant reaches the browser. A request with no grant is a 400, and one with another user's or a spent
grant is the shared byte-identical 401, both before the handler runs. The route takes the Admin policy and the
`AdminWrite` bucket, sets `Cache-Control: private, no-store` before the command runs, and answers 202 with
`{completableFrom, expiresAt}`. A sender that cannot deliver is a 503, which the administrator's route maps itself through
the shared `AuthProblem` helper, because the central mapper would turn that Validation error into a 400.

The handler's gates run cheapest first. Every refusal comes before any mail is sent or any budget is spent, except the one
only the store can answer (gate 6):

1. The sender's capability, else 503 (`Auth.EmailDeliveryUnavailable`): two mails are the change.
2. The administrator's own account, else 409 (`Auth.AccountEmailChangeAdministratorTarget`): an administrator's address
   changes on Mina sidor, which proves both inboxes.
3. The account, read fresh from the directory in one statement and never inferred from the address: none, 404; an Admin,
   409 with the code of gate 2; not Active or without a stored address, 409 (`Auth.AccountEmailChangeInactiveTarget`).
   #1976's Suspended is refused by default, because the gate is Active. The read goes through `IAccountDirectory`, not a
   summary read plus a profile read, which would write the profile rule a fourth time, after `LoginSubjectResolver`,
   `ReauthenticationService` and the directory's `CASE`; that widens ADR 0151 D6's consumer pin by this one handler.
4. The new address storable and free, as an address and as a user name: 400 `Auth.EmailNotStorable`, 409
   `Auth.EmailTaken`. The account's own address in another case is taken by the account.
5. The two budgets keyed by the NEW address, `change-email-target` and `change-email-per-target-daily`, else 409
   (`Auth.ChangeEmailCooldown`). They are shared with self-service, so that whoever asks, an address gets the same number
   of codes a day.
6. The store's put, else 409 (`Auth.AccountEmailChangePendingForAnotherAccount`): another account's change holds the
   address.

**Never the account's own per-user budget scopes.** ADR 0142 Amendment 2026-09-21 (4) keyed `change-email-user` and
`change-email-targets-daily` by the user id, so that only someone holding that account's session can spend them and nobody
else can block the owner's Art. 16 or 17 operations. Keyed by the target, five administrator starts would block the
owner's own self-service change for 24 hours. Lapse trigger 5 fires, because this is a new minting path, and is re-run in
ADR 0142's "Attempt budget": the per-address bound is unchanged.

**The notice to the current address is sent first and the code to the new address second, both awaited.** A mail the
provider does not accept removes exactly the record this request wrote, with `CancellationToken.None`, and the failure
propagates as a 500, so nothing becomes completable and no request row is written. NIST SP 800-63B-4 §4.6 makes notifying a
recovery event a SHALL, with contact details for repudiating it, so a change whose notice was not accepted must not be able
to complete. This supersedes design item 7's "(it is best-effort)".

*Why this order* (`security-auditor` C-1 over the architect's R5). The grant is redeemed before the handler runs, so a
refusal at any gate costs the administrator a code. The cheapest and most certain refusals therefore come first, and the
panel does not offer the action where gates 2 and 3 would refuse it. `RequestAccountEmailChangeCommandHandlerTests` pins
the order. The receipt may say a code was sent, because ADR 0142's three facts for stating a send hold here: the 2xx
follows both awaited sends, every branch that sends nothing ends in a visible refusal, and the address shown is the
recipient's.

### D6 — Cancel needs no step-up, the pending read is its own route, and a write that changes nothing answers a DomainError

`DELETE /api/v1/admin/accounts/{id}/email-change` (`AdminWrite`) removes the account's pending change and answers 204. It
needs no re-authentication, because cancelling only removes exposure and fixing a typo should not wait for a code. A cancel
that finds nothing to remove (expired, completed or already cancelled) is `Auth.AccountEmailChangeNothingPending`, Gone,
410: as a failure it writes no `Admin.AccountEmailChangeCancelled` row, so no row claims a change that did not happen.

`GET /api/v1/admin/accounts/{id}/email-change` (`AdminRead`) is the pending read. It answers 200 with
`{state, completableFrom, expiresAt}`, where `state` is `Pending` or `CodeBurned` and nothing names an address, or 204 when
nothing is pending, never 404, because the account may well exist. It is its own route so that a fault on the volatile
instance (503) costs the pending line only and never the account's details (ADR 0150 D2). A pending change is a fact of the
account: the panel shows it from this answer or from the request's 2xx, never from form state, so that a reload, a second
tab and a second administrator agree.

**The rule for the whole admin write surface, decided once.** A write that changes nothing answers a `DomainError`: Gone
when there is nothing left to act on, Conflict when the state already holds. `AuditBehavior` writes no row for a failure,
which meets the issue's rule that a no-op claims no change, with no new member on `IAuditableCommand` (no `RecordsChange`)
and no ADR 0022 amendment. #1976 (suspending an already suspended account) and #1977 inherit it.

### D7 — The completion: one refusal, a compare-and-swap, and nothing thrown after the swap

`POST /api/v1/auth/account-email-change/complete` is public: no session is read or issued, `AuthWrite` (20 per minute per
IP) limits it, and it takes `{currentEmail, newEmail, code}`. The command is not an `IAuthenticatedRequest`, sits outside
`Application.Admin` (every message there carries the admin gate, ADR 0151 D6) and is not an `IAuditableCommand` (D9). The
page posts through a Server Action that relays the client's forwarding headers (`forwardedHeaders()`, #1202), so the
limiter and the audit row see the client and not the web container. The order is fixed:

1. The pipeline's validator refuses a malformed presentation first, 400, and spends no attempt: the code is exactly six
   digits, so the store's fixed-time compare always sees two strings of one length.
2. The handler checks the sender's capability before it asks the store anything, 503, so that the 503 cannot vary with what
   was presented. A store out of reach is a 503 as well.
3. The store judges before anything about any account is read. Every failure before a full match reads no account.
4. **One byte-identical 410** (`Auth.AccountEmailChangeUnusable`) for everything but a full match: no change, an expired,
   cancelled, burned or completed one, a wrong code, a wrong current address. The page cannot say which input was wrong,
   and neither may the wire.
5. A full match before the not-before is **409 `Auth.AccountEmailChangeNotYet`** with the earliest instant as a
   `completableFrom` extension of the problem. Only a full match reaches it, so it tells nobody anything they did not
   already hold, and nothing is spent.
6. After a full match, every refusal is the same 410 and consumes the change: an account without a live profile (a missing
   or soft-deleted `JobSeeker`, #1349's predicate, now one helper, `ProfileLiveness`, shared with the re-authentication
   gate), or a swap that refuses.
7. **The swap is a compare-and-swap.** `SwapPrecondition.AdminInitiated(ExpectedCurrentAddress)` is checked on the instance
   `SwapConfirmedAddressAsync` loads, under the concurrency stamp that guards its first write: the account still holds the
   address the change started from, compared by `SubjectFingerprint`, the one normaliser every key uses, and does not hold
   Admin. This is Fowler's Optimistic Offline Lock (2002). A separate read before the swap would leave a window between the
   check and the write (the architect's MH-4); here, an owner's own change or a role granted after the load fails the
   write. The swap's own refusals, a taken address and an incomplete write, join the 410.
8. The account moves to the record's spelling of the address, never the request's, in #1790's order (the user name first).
   `ConfirmedAddressSwap` then sends the existing changed-address notice to the address it replaced, best effort: no code,
   no link, never the new address. It is the one caller of `SwapConfirmedAddressAsync`, shared with self-service's confirm,
   so the write order and the notice cannot drift apart between the two flows.
9. The endpoint sends the command with `CancellationToken.None`, invalidates every session of the account and answers 204
   (RFC 9110 §15.3.5): there is no body and no session. Anything that fails after the swap, the teardown or the audit row,
   answers 500 with an Error log line carrying the account's id, never a 503 that would invite a retry the refusal then
   meets.

*Why one refusal.* A public page that answers differently for a missing change, a wrong code and a wrong address becomes an
oracle for which accounts and addresses exist. The capability check sits first, as it does on the login request (ADR 0142
D2), so that the 503 cannot vary with what was presented. The store judges before any account is read, and the handler
branches only after proof (ADR 0142 D3).

### D8 — Sessions and external logins

Initiation and cancel touch no session. The completion invalidates every session of the account after the swap commits,
through `InvalidateAllForUserAsync`, which plants its revocation tombstone first, and creates none, not even for the device
that completed: the owner logs in afterwards with a code sent to the new address. A teardown that fails answers non-2xx
over a committed change, never a 2xx claiming every device was logged out (the self-service rule).

External logins are neither relinked nor erased, and none is created. A provider that still asserts the old address is
refused by the address-first rule (ADR 0142 Amendment 2026-09-25 (14), `security-auditor` M-1) with no session, so a stale
link is inert until someone can make that provider assert the new address, which means the holder of the new inbox.
In-flight self-service records and grants all need a session, so the teardown leaves them unusable, and a login challenge
for the old address that is proven after the swap resolves to no account.

### D9 — Three audit rows, and the completion's names the account

| Event | Written by | `user_id` | Aggregate | Payload | Written |
|---|---|---|---|---|---|
| `Admin.AccountEmailChangeRequested` | `AuditBehavior` | the administrator | `User`, the account | none | only once the record exists and the provider has accepted both mails |
| `Admin.AccountEmailChangeCancelled` | `AuditBehavior` | the administrator | `User`, the account | none | only when a live change was actually removed |
| `User.EmailChangedViaAdministrator` | the completion handler itself | **the account** | `User`, the account | none | exactly one per committed swap |

No row is written for a refusal, a retry, a burn, an expiry or a cancel that removed nothing. The event types tell these
rows apart from self-service's `User.EmailChangeRequested` and `User.EmailChanged`.

The completion row names the account and not the anonymous completer. `AuditBehavior` would stamp null on the anonymous
route, and `AuditTrailEraser` anonymises an account's rows only `WHERE user_id = {id}`: the completer's IP address and user
agent would survive the account's erasure and make the privacy policy's sentence false. The handler writes the row itself,
as `AccountRegistrar` and `PasswordlessSessionGrant` do on anonymous paths, and no architecture test forbids a handler
writing a row (`ImportResumeCommandHandler` does). A row that cannot be written after the swap is logged (Error, the
account's id) and left out of the unit of work, never thrown: the teardown must still run, and the endpoint then answers
500.

No row carries a payload, so none can carry an address in any form, a fingerprint, a code, a challenge id, a grant or a mail
body. One live change per account means the latest request row on the same `aggregate_id` is the completion's pair, so a
correlation id in a payload would be a second source for the same knowledge.

### D10 — Three mails, and the re-authentication mail's opening

1. **The code, to the new address,** as a variant of its own, `LoginChallengeEmail.AccountEmailChangeCode`, because
   `AddressChangeCode`'s Art. 14 notice gives its source as "en användare", which would be false here. It carries the
   code and both instants in Swedish time; a parameter-free link to `/adressbyte`, because the recipient otherwise holds a
   code with nowhere to use it, and a bare route holds neither a credential nor an address (`security-auditor` C-3; design
   item 15); the sentence that says what to enter (the account's current address, this address and the code); and the
   Art. 14 notice with the source "en administratör hos oss", Art. 6.1 f, and a retention read from the TTL ("högst 96
   timmar"). It names nothing about the account, least of all its current address.
2. **The notice, to the current address, at the start,** through a new member of `IEmailSender`: no code, no site link and
   never the new address, stating the earliest completion and the end of the code's life. It is delivery-dependent (D5).
3. **The existing changed-address notice, at the completion,** to the address the account moved from (D7).

The re-authentication mail's opening now names its operations by example ("till exempel radera kontot eller byta en
e-postadress"), because an administrator's code also starts a change of another account's address.

Every instant in the mails, and in the panel and the page, is the pending change's own, read from the record and shown as an
absolute Swedish time, since a mail may be read hours later and a relative lifespan would be false. The new
`IEmailSender` member changes the port's arity: `ConsoleEmailSenderReservedRecipientTests` gains a case, and every
implementation and every fake gains the member. The signed blocks (the Art. 14 notice and the retention sentence) are
graded by `security-auditor` and `design-reviewer` at the PR panel.

### D11 — The public page, and what the panel does with the answers

`/adressbyte` is a route in `(auth)`: a Swedish noun that exists in house copy and can be typed from a mail, never
`/bekrafta-epost`, which was retired and whose old links can still be opened (ADR 0142 Amendment 2026-09-21 (4)). It answers
200 without a session, is not indexed and is not in the sitemap, and carries no address in its `h1` or `<title>`.
`design-reviewer`'s form round binds the page and the panel (items 1 to 18 of her memo, each at its own grade, graded as
built at the PR). Five of its points are decisions of this ADR, because they reach the backend:

- The form has three fields in DOM order: the account's current address, the new address, the code. The comparison
  tolerates what login tolerates: the server folds both addresses with `SubjectFingerprint` on a trimmed value, and the
  page never folds. A test submits a case variant.
- The refusal is one form-level message, identical for every cause, with no field marked invalid, because the backend's one
  410 cannot say which input was wrong. The addresses are kept and the code is cleared, and the sentence about three wrong
  attempts is pinned to the enforced attempt budget.
- The "not yet" answer reads `completableFrom` and tells the owner when to come back. A 429 or 503 keeps the input. An
  unknown outcome never tells the owner to try again, because after a lost success a retry gets the one refusal.
- Success replaces the form with a status panel that says every device is logged out and links to `/logga-in` with no
  address in the URL.
- The panel does not offer the action where gates 2 and 3 of D5 would refuse it, and shows a pending change from D6's read.
  ADR 0150 D8 carries the two rows in which the delivered panel departs from the handoff.

### D12 — Pins

- **`AccountEmailChangePolicyTests`** spells out the three literals (D2).
- **`AccountEmailChangeChainTests`** (architecture): the store's consumers are exactly the four handlers. The request is
  pinned to `IAdminRequest`, `IReauthenticatingRequest` and `IAuditableCommand` by name, because
  `ReauthenticationTripwireTests`' pattern reads "ChangeEmail" and not "EmailChange", and widening it would catch the
  self-service steps that stand outside the marker by design. Cancel and read carry the admin gate and no
  re-authentication. The completion is neither an `IAuthenticatedRequest` nor an `IAuditableCommand`, and is not in
  `Application.Admin`, and it cannot reach a session, a login or the account directory. Every record that carries an
  address, a code or a grant prints none of them.
- **`AddressSwapCallerTests`**: the files that name `SwapConfirmedAddressAsync` are the port, the adapter and
  `ConfirmedAddressSwap`, and the handlers that take `ConfirmedAddressSwap` are exactly self-service's confirm and the
  completion (D7).
- **`ReauthenticationChainTests`**: the four handlers are roots of the "cannot reach a session" walk.
- **`AdminAccountDirectoryTests`**: the request handler is a fourth consumer of `IAccountDirectory` (ADR 0151 D6, widened by
  D5).
- **`VolatileRedisIsolationTests` and `VolatileRedisPlacementTests`**: the adapter reaches only the volatile connection, and
  its keys land on the volatile instance and never the durable one.
- **`RedisAclContractTests`**: ADR 0143's real-adapter fact (D3).
- **`RedisAccountEmailChangeStoreTests`, `UserAccountServiceAddressSwapTests` and
  `CompleteAccountEmailChangeCommandHandlerTests`** pin the store's contract on a real Redis, the swap's precondition and
  the completion's order (D4, D7).
- **The endpoint-graph policy pins** (`AdminAccountsRateLimitWiringTests`): the two writes carry `AdminWrite` and the read
  `AdminRead`, each with the Admin policy. The completion's `AuthWrite` is pinned in `AccountEmailChangeCompletionTests`.
  **`BuildMdAuthRoutesTests`** holds BUILD.md's route list to the endpoints.
- **The mails** (`EmailTemplatesLoginChallengeTests`, `EmailTemplatesAccountEmailChangeRequestedNotificationTests`,
  `ConsoleEmailSenderReservedRecipientTests`): each variant's blocks, and the port's arity (D10).

### D13 — Declared residuals: none is an accepted risk

Each is a residual the design leaves, declared so that it is not discovered later. None is accepted under CLAUDE.md §9.6
(3), and none is signed away.

- **A Redis reader learns that an address is about to belong to an account.** The record's key is the new address's
  fingerprint, so a reader of the instance who can test an address sees that a change toward it is pending. ADR 0142 D1's
  threat-model paragraph, which keeps the address and the code protected and every key a fingerprint, does not cover
  what a key's existence says.
- **An owner who does not read the current inbox within the delay loses the account to a successful deception.** The
  notice is prevention only for an owner who reads that inbox.
- **Anyone who knows the new address can burn a pending change,** with three wrong presentations: the record is found by
  that address, and a burn needs no knowledge of the code or the account. The administrator starts again.
- **The notice tells the holder of the current inbox that the account exists.** That holder can learn it anyway by trying
  to log in.
- **A restart of the volatile instance cancels every pending change.** The panel then shows none, the administrator starts
  again, the current address is told again and the delay starts over. It fails closed.
- **Sessions outlive the swap** for as long as the changed-address notice takes to send (up to its send timeout), because
  `ConfirmedAddressSwap` awaits it before the endpoint's teardown runs. After a process crash between the commit and the
  teardown no teardown runs at all, and the sessions live until they expire. Self-service's confirm has the same shape.
- **A live change can have no request row.** If the pipeline's own save of the request row fails after both mails were
  accepted, or if the removal of the record after a refused mail itself fails (logged at Error, event 4004; the mail's
  failure is the answer), a change the administrator may not know of stays live until it expires or is cancelled. The
  runbook's remedy is to cancel it and start again.
- **An orphan record holds its address.** A Redis fault between a put's index swap and its removal of the record it
  displaced leaves a record the index no longer names. It cannot complete, because the consume checks the index, but it
  holds its address against other accounts until its TTL, and cancel does not reach it.
- **A hostile holder of a recycled current inbox is not beatable here.** The holder can change the address first, in which
  case the compare-and-swap refuses the administrator's change, or object during the delay, and an address is what enters
  an account (ADR 0142 D10). Contested accounts go to the controller, never decided by who is more persuasive.

## Alternatives considered

- **D1, A1: no current-address input.** The owner completes with the new address and the code alone. The architecture is the
  same minus one compare and one payload field. Rejected: one typo by the operator hands the account, CVs included, to
  whoever holds the mistyped inbox, with no attacker effort, which is `security-auditor`'s Blocker class.
- **D1, B: the change completes at the owner's next login with the old address.** Safe, but it serves no case self-service
  does not already serve: an owner who still has the old inbox can use Mina sidor, and one who has lost it cannot log in
  with it. It is also ruled out by structure: the swap would have to run inside the login proof chain, which
  `LoginProofChainTests` keeps from reaching the account service.
- **D1, C: read-only, with a runbook.** Rejected: it leaves Art. 16 unserved for exactly the person who has lost the inbox,
  unless the runbook's remedy is deletion and a new account. A runbook that edited Identity directly would bypass
  `StorableAddress`, #1790's order, the notice and the teardown, so that runbook is not written. "Ändra e-postadress" would
  also have no honest state in the panel, since ADR 0150 D2 keeps "Kommer snart" for capabilities that are coming: C means
  removing the action and amending ADR 0150 D4 and D8, which reverses Klas's scope note of 2026-10-04.
- **D2, (b) 24 hours with an owner-side stop.** A new interface on Mina sidor, built, reviewed and tested in the same PR. In
  return the wait is shorter, the protection does not hang on Klas reading kontakt@ in time, and the data is stored for at
  most two days. Not chosen.
- **D2, (c) no delay.** The notice to the old address is then only a discovery after the fact. It could be accepted only as a
  CLAUDE.md §9.6 (3) exception that `security-auditor` signs and that lapses when the first test user registers, so it would
  have to be redone before the MVP. The data would be stored for at most a day. Not chosen.
- **D2, the architect's record of at most 24 hours.** A change can complete only while the delay has run and the record has
  not expired, so with a delay of 24 hours and a record of 24 hours or less that window is empty, and a 15-minute record
  survives no delay. `security-auditor` signs no delay under 24 hours except (c) as an acceptance. The ceiling itself does
  not hold (Context).
- **D3, a durable pending swap on Postgres, run by a scheduler at the end of the delay** (the architect's reading of E).
  Rejected: it needs a migration on the single-owner hotspot for an artefact that lives at most four days, it makes durable
  the personal data ADR 0142 keeps volatile, and it needs a runner. The Worker has no `ISessionStore` (ADR 0023) and
  `worker-persistent` has no session selector, so it would take an ADR 0023 amendment and a persistent-ACL change, or a
  scheduler hosted in the Api.
- **D3, packing the record into `challenge-bound`,** the architect's fallback if the operator step could not happen.
  Rejected (D3's *Why*): it breaks ADR 0142 D1's segment rule, ignores the port's own "third kind" signal and blurs least
  privilege per family.
- **D5, the account's own per-user budget scopes** (the architect's R4). Rejected: it contradicts ADR 0142 Amendment
  2026-09-21 (4), and five administrator starts would block the owner's own change for a day.
- **D5, a best-effort notice to the current address** (design item 7). Rejected for NIST SP 800-63B-4 §4.6 (D5): a change
  whose notice was not accepted must not be able to complete.
- **D6, `RecordsChange` on `IAuditableCommand`.** `security-auditor`'s §8 wrote it as if it existed; it does not, and her
  substance, no row for a no-op, already holds through `AuditBehavior`'s skip of a failure. The form for a 2xx no-op would be
  a default member read after the failure gate, with an ADR 0022 amendment and `AuditBehavior` tests. Rejected: D6's rule
  needs neither.
- **D7, a separate read for the staleness check** (the architect's MH-4). Rejected for the compare-and-swap inside the swap
  (D7, point 7), which leaves no window between the check and the write.
- **D9, the completion row through `AuditBehavior`, with a correlation-id payload** (the architect's R7). Rejected: it would
  stamp a null user and leave the completer's IP address and user agent beyond erasure, and the payload would be a second
  source for what the latest request row already names (D9).
- **An amendment to ADR 0142 instead of a new ADR.** Rejected (the architect's R10): this ADR introduces a trust model ADR
  0142 never had, a third party starting a change of someone else's recovery address and the owner completing it without a
  session; ADR 0142 is already over 3,750 lines; and ADR 0151 decides a read, not a write flow. So one new ADR, with dated
  pointers into 0142, 0150 and 0151.

## Consequences

### Positive

- An owner who has lost the inbox gets a path that proves both a new inbox and knowledge of the account, with no manual
  write to the database.
- The notice to the current address becomes prevention and not only detection, for every owner who reads that inbox within
  the delay.
- No table, migration, scheduler or Worker coupling: the single-owner migration hotspot is untouched.
- The public page gives an enumerator one answer for everything but a full match, and the store reads no account before one.
- A swap cannot move an account past a newer fact: an owner's own change or an Admin grant refuses it, whether it landed
  before the swap loaded the account or after.
- Every erasure path reaches the completion's audit row, because its user is the account.
- #1976 and #1977 inherit one rule for a write that changes nothing.

### Negative and risks

Accepted as trade-offs by `senior-cto-advisor` (§8):

- A pending change lives the delay plus the window on an instance that forgets on restart. A restart cancels it, and the
  administrator starts it again.
- The code sits in the new inbox for the delay plus the window, though it is usable only during the window.
- The volatile instance's longest-lived key becomes an administrator-minted one: at most 96 hours, where the budgets' longest
  was 24 hours.
- A legitimate owner who is locked out waits the delay.
- The bootstrap's own residual, recorded in ADR 0028's update of 2026-10-04: while the Admin role has no holder, the
  configured inbox is the bootstrap credential.
- The protection under option (a) assumes Klas reads kontakt@ while a change waits (D2).
- The page cannot tell the owner what was wrong (D7, D11).
- The request costs the administrator a code even when a gate refuses it (D5).
- The `IEmailSender` port grows by one member, and every implementation and fake with it. The directory has a fourth consumer.
- One more operator step on the box before the code can merge (Implementation).
- D13's residuals stand, each declared and none accepted.

## Implementation

Implemented under #1975, in the PR that carries this ADR, which stays a draft until the rollout conditions below hold:

- The refactors the flow needs, each without a change of behaviour for the flows that already used them: `ConfirmedAddressSwap`
  with `SwapPrecondition`, `ProfileLiveness` and `ChallengeCodeArm` (D3, D7).
- The store, its policy and its port (D2 to D4); the request, cancel and pending read (D5, D6); the completion (D7); the two
  mails and the new `IEmailSender` member (D10); the pins (D12).
- The web: the panel's step-up, pending row and cancel; the `/adressbyte` page with its Server Action; the Swedish and English
  messages in the same PR (D11).
- The documents that follow the data, in the same PR: the privacy policy names the notice an administrator's start sends
  (`privacy.sections.3.list.1`) and says how an owner without the inbox asks (`sections.10.list.1`, Rättelse), with
  `privacy.updated` and `TermsAcceptance.CurrentPrivacyPolicyVersion` moving together; the processing register gets the
  pending change's storage and TTL; `docs/runbooks/account-email-change-by-administrator.md` holds the one statement of the
  no-name verification rule, which `docs/runbooks/account-deletion.md` points to instead of its false premise
  (`security-auditor` m-1); the Redis service-boundary runbook names the adapter and its calls in a paragraph beside the
two rows #2003 added; the threat model and BUILD.md
  §6.2 carry the flow and its four routes; and the dated pointers in ADR 0142, 0150 and 0151 and the index row.

**DoD 8 (GDPR).** The new personal data is the pending change's protected new address and code, and the fingerprints of the
new address and of the user id, held for at most 96 hours on the instance that cannot reach a disk; the current address is
never stored (D3). No new category, no profiling, Art. 35(3)(a) to (c) all negative, so no DPIA (`security-auditor`). The
completion's one row is retained as audit rows are, and every retention sentence in the mails is read from the constant that
enforces it.

**Rollout, in the CTO's order** (`senior-cto-advisor` §3). #1999 (the directory) and #2006 (the bootstrap repair) have
merged. As of 2026-10-05, #2003 grants the two key families ahead of the store and is open: it merges only on Klas's GO
("när jag har tid", #1975, comment 5984508392), after which, in one sitting with Klas, the session advances the box's
checkout, Klas re-publishes the volatile ACL from escrow along #1879's recovery path, and the session measures it read-only (the effective `ACL LIST`,
`ACL DRYRUN` positive on both families, and a control `GET` refused), recording the reading with its timestamp. This PR is a
draft with `automerge` until three things hold: #2003 has merged and that reading holds, #2006 has merged (it has), and HEAD
is unchanged since the panel. Only then is it marked ready and `agents-done` set again. The hold exists because every
merge goes live on the one box (ADR 0154), no checkout advance may pass a Redis ACL change before Klas's escrow sitting
(CLAUDE.md §9.2), and the new routes answer 503 when the ACL is missing (`docs/runbooks/redis-service-boundaries.md`).

## References

- AGENTS.md §2.1, §5, §12 · CLAUDE.md §6.5, §9.1, §9.2, §9.6 · BUILD.md §6.2
- ADR 0008, 0022, 0023, 0024, 0028, 0142, 0143, 0150, 0151, 0154
- `docs/reviews/2026-10-04-1975-form-cto.md` and the three memos beside it (local)
- `docs/runbooks/account-email-change-by-administrator.md` · `docs/runbooks/redis-service-boundaries.md` ·
  `docs/threat-model.md`
- #1975's comments of 2026-10-04: the scope note (5977248872), the constraints from #2003's review (5984415080) and Klas's
  answers (5984508392)
- The CTO record's citations: Martin 2017, ch. 10 · Hunt/Thomas 1999 (DRY) · Fowler 2002, *Patterns of Enterprise Application
  Architecture*, "Optimistic Offline Lock" · RFC 9110 §15.3.5 · NIST SP 800-63B-4 §4.2.1.2 and §4.6,
  `pages.nist.gov/800-63-4/sp800-63b/events/` (read 2026-10-04) · GDPR Art. 5(1)(c), 13, 14, 16, 17 and 24(1)
- `src/Jobbliggaren.Application/Auth/AccountEmailChanges/` (the port and the policy) ·
  `src/Jobbliggaren.Infrastructure/Auth/AccountEmailChanges/RedisAccountEmailChangeStore.cs` ·
  `src/Jobbliggaren.Application/Admin/Accounts/Commands/RequestAccountEmailChange/` ·
  `src/Jobbliggaren.Application/Auth/Commands/CompleteAccountEmailChange/` ·
  `src/Jobbliggaren.Application/Auth/ConfirmedAddressSwap.cs` ·
  `src/Jobbliggaren.Application/Common/Abstractions/SwapPrecondition.cs` ·
  `src/Jobbliggaren.Infrastructure/Auth/UserAccountService.cs` (`SwapConfirmedAddressAsync`) ·
  `src/Jobbliggaren.Api/Endpoints/AdminAccountsEndpoints.cs` and `AuthEndpoints.cs` ·
  `tests/Jobbliggaren.Architecture.Tests/AccountEmailChangeChainTests.cs` (D12's first pin)
