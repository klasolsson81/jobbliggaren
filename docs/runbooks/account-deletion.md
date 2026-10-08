# Account deletion

This runbook describes the implemented lifecycle under ADR 0024, ADR 0142,
ADR 0153 and ADR 0155. Source and production-produced tests are the operational
contract. Historical separate-context and raw SQL restore instructions are retired.

## 1. Scheduling

Scheduling immediately prevents access. It does not prove completed permanent
deletion. The product grace period is 30 days; this is not a statutory GDPR
30-day rule. Restore and immediate permanent deletion are unavailable in the
admin MVP. Do not promise support restoration or clear `deleted_at` with SQL.

| Entry | Proof | Success |
|---|---|---|
| `POST /api/v1/me/delete` | Owner's own inbox reauthentication grant | `204`, existing self-service contract |
| `POST /api/v1/admin/accounts/{id}/deletion` | Admin session and administrator's own inbox grant | `202`, actual target and scheduling instants |

The Server Action verifies the administrator's inbox code and passes its grant
internally. It neither impersonates the target nor obtains its data key. The
confirmation identifies the target, immediate access loss, permanent interruption
of pending address changes, grace period and projected worker run.
Every opening reads a fresh same-target live status and server-calculated preview
before enabling code request, verification or resend. The calculation timestamp
labels the frozen preview during inbox confirmation. The committed receipt uses
the actual scheduling instant and recalculates eligibility and the projected run;
crossing 04:00 UTC during confirmation can move that run to the next day.

The receipt contains `userId`, `deletedAt`, `eligibleAt` and `scheduledRunAt`.
New deletion stamps use one captured UTC instant at millisecond precision for the
whole cascade and its events. Historical persisted stamps remain unchanged.
`eligibleAt = deletedAt + 30 days`; `scheduledRunAt` is the first daily 04:00 UTC
run **strictly after** that instant. Eligibility exactly at 04:00 projects the
next day's run. The panel displays Swedish local time and the UTC cadence.
A projected run is not a completion guarantee.

Admin self-deletion and last effective administrator removal return `409`.
Already pending returns `409 Admin.AccountAlreadyPendingDeletion` without
changing dates, revisions or success audit. Missing Identity is `404`; missing
profile is `410`. Self-service retains its existing refusal/error contract.

## 2. Transaction and access authority

1. Take the global lifecycle lock, then sorted actor/target owner locks.
2. Re-read actor session revision, current Admin role and primary-database
   lifecycle state. Re-check target and last effective administrator.
3. Advance epoch, target access revision, credential cutoff and Identity stamps.
   Scheduling a suspended target preserves `IsSuspended` and still advances the
   deletion generation.
4. Soft-delete active profile, applications and their notes/follow-ups, and
   resumes/versions through aggregate methods. Preserve ciphertext without a
   target-DEK prefetch or decrypted-content read.
5. Erase every external-login link and save the audit in the same physical
   App/Identity PostgreSQL transaction. Owner event: `Account.Deleted`; admin
   event: `Admin.AccountDeletionScheduled`, distinct actor and target.
6. After known commit and lock disposal, mark deleted in the session store,
   invalidate older session revisions and cancel older pending address changes.
   Volatile-store cleanup is best effort.

The primary database denies old sessions, new login admission and old proofs even
when Redis cleanup fails. Pending and already consumed address-change proofs
remain permanently inadmissible after the generation advance. Reinstate never
restores a pending deletion or its old proofs.

Audit failure rolls back profile cascade, provider erasure and Identity transition.
A lost commit acknowledgement is an **unknown outcome**: never automatically
replay. Explicitly reload current state before deciding the next action. A later
fresh request against a pending target refuses without a second success audit.

## 3. Ordinary worker

`HardDeleteAccountsJob` runs daily at 04:00 UTC through ordinary Hangfire
registration. Every run performs its normal four steps:

1. Clean historical Identity-only orphans after the one-hour grace. Current
   registration is atomic; grace does not imply a current partial-commit window.
   Count and warn about reverse orphans without erasing them.
2. Select profiles with `deleted_at < now - 30 days`.
3. Acquire lifecycle/owner locks and re-check eligibility with the adapter's
   current clock. Delete owned graph, anonymize actor audit fields, delete DEK
   and Identity in one transaction. Identity failure rolls **everything** for
   that account back. Clear trackers so the next account proceeds independently.
4. Erase provider links retained by historical partially scheduled accounts.
   New scheduling already removes them atomically.

The cascade includes applications/children, resumes/versions, parsed CVs and
originals, matching/digest state, saved searches/job ads, recent searches,
company watches/hits/criteria and database-cascaded derived rows, and feedback
screenshots/submissions/notices/prompt suppressions. Screenshots are removed before
their submissions. The cascade map and worker integration tests pin the current
inventory, including screenshot preservation while scheduled, rollback on Identity
failure and target-only removal by the ordinary worker.

Feedback dispatch checks live reporter before claim and again after persisted
`Sending`, immediately before transport. Requeue holds sorted actor/reporter
locks through fresh administrator authority, the live-profile check, notice
change, success-only audit, save and commit. It refuses a deleted reporter,
rolls back concurrent-write conflicts and never replays an unknown commit.
Application timeline status changes receive the same scheduling stamp as their
aggregate; the scheduler explicitly loads that navigation before soft deletion.
Matching and watch scans exclude deleted profiles. Top-direct, match digest and
followed-company digest recheck the current live profile and corresponding
consent after their persisted `Queued` claim, immediately before transport.
A refused claimed row stays `Queued`; a job rerun never replays it. No database
transaction spans email transport; already handed-off mail cannot be recalled.

DEK deletion makes copies encrypted under that key unreadable. It does not prove
physical erasure of backup media or Redis AOF history. #1757 retains Redis disk
retention scope. Audit retention follows [audit-retention.md](audit-retention.md).
Scheduling introduces no new legal basis or audit retention policy.

## 4. Operators and verification

Follow [vps-deploy-stack.md](vps-deploy-stack.md) and CLAUDE.md §9.2. Agent
production reads emit only permitted aggregates, schema, health and provenance.
Never print account/audit/Hangfire rows, personal identifiers, Redis keys/values
or secrets. Agents make no authenticated production requests; Klas performs
signed-in live checks. Keep selected identifiers inside commands/private records.

Useful aggregates include pending/due accounts, historical Identity-only and
reverse orphans, provider links on pending accounts, selected owned-row counts
and anonymized-audit counts. Do not infer physical storage erasure from a zero
logical-row count.

### 4.1 Restore

Restore is unavailable. SQL restore bypasses lifecycle generation, last-admin
protection and provenance of independently deleted children. Do not clear
`deleted_at`, revive old sessions or remove a marker to reactivate a profile.
A future supported restore needs its own reviewed design.

### 4.2 Reverse orphans

An Identity-less profile is historical/invariant-break state, not current
registration output. Event 2503 is count-only evidence for investigation; #1409
owns remediation. Admin scheduling cannot repair it and returns `404`.
Do not decrypt CVs to identify owners, erase an unverified profile or backdate a
row merely because its id appeared in a query. Corrective writes require a
separate concrete Klas GO bound to verified target, effect, locks/generation,
audit evidence and rollback limits in a private operation record. This runbook
authorizes no raw SQL erasure or maturity adjustment.

### 4.3 Email deletion requests

Follow [requester verification](account-email-change-by-administrator.md#verifying-a-requester).
A sender line alone is insufficient. After Klas verifies the requester and
selects the account, use normal admin scheduling and the administrator's own
inbox code. Keep the request in the controller's private case record. Communicate
**scheduled** deletion, access block and actual receipt timeline; never report
completed erasure or offer unavailable restoration. If already pending, report
existing dates without restarting the clock. If supported verification/write is
unavailable, arrange a separately reviewed operator procedure; do not substitute
direct profile or Identity SQL for the lifecycle protocol.

### 4.4 Redis cleanup repair

Cleanup warnings after known commit do not undo scheduling. Verify primary
database outcome and old-session refusal first. Repair needs separate approval
and the existing host credentials through:

`sudo bash /opt/jobbliggaren/deploy/systemd/jobbliggaren-redis-account.sh`

The helper authenticates as `operator-persistent` in DB 0 in the persistent
container's network namespace. Password stays on the host and stdin. Do not
print it, use anonymous commands or mount it into another service.

Before `mark-deleted`, establish positive whole-second deployed
`SessionStoreOptions.DeletionTombstoneTtl`, including overrides; bind its revision
and `TTL_SECONDS` in private operation evidence. Source default is not deployment
proof. The helper has no default; require its verified-presence receipt.
`USER_ID` is the verified private UUID, normalized lowercase by the helper.
`SESSION_KEY` for `delete-known-session` must be an exact hashed key whose owner
is already established privately. Shape validation does not establish ownership.
Never pass raw tokens, scan keys or use wildcards. Removing an index alone does
not prove every session record was physically erased.

### 4.5 Bounded production worker drill

Release approval does not authorize maturity adjustment. After Klas's signed-in
live checks and a normal scheduling receipt for his selected own test account,
request separate concrete GO bound to account, release, receipt, adjustment and
ordinary worker invocation. That invocation includes normal due/orphan work;
inspect its permitted aggregate effects before GO. Verify cleanup with permitted
aggregates only; no personal data or secrets enter chat/issues/PR evidence.

## 5. Failures and retry

| Failure | Outcome and action |
|---|---|
| Invalid proof, self/last-admin refusal, pending no-op | No success mutation/audit; correct input or read state. A spent inbox code requires a new code. |
| Scheduling save/audit failure | Shared rollback; investigate typed error without claiming a receipt. |
| Unknown commit | May have committed; explicit reload, no automatic replay or retrospective success receipt. |
| Redis failure after known commit | Scheduling persists; primary database denies access. Separately approved cleanup repair. |
| Per-account worker database/Identity/audit failure | Whole account retained; other accounts continue. Event 2502/final failed count records it. |
| Orphan sweep, cancellation or final provider-backstop failure | May fail overall run; investigate and use ordinary retries, not manual graph deletion. |

A caught per-account failure need not make Hangfire's run `Failed`. A later daily
run retries retained eligible accounts. Manual rerun before eligibility does not
shorten grace. Test anchors: `AdminAccountDeletionTests`, `DeleteMeTests`, account
login/address fences, `HardDeleteAccountsJobIntegrationTests`. Historical scopes:
#1909 Identity failure isolation; #533 grace-period processing; #1409 reverse
orphans; #1757 Redis physical retention.
