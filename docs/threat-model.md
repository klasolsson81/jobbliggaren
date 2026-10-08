# Jobbliggaren threat model

## Scope

Review the PR delta against actual producers and consumers. This model describes
tracked source, not verified live configuration, backup operation or compliance.
Do not import secrets, private operational evidence or personal data into reviews.
Update this model when a change alters assets, actors or trust boundaries.

## Assets and actors

Protect identities, opaque sessions, login codes/links, OAuth state and grants,
CV originals and parsed content, applications/notes, consent/preferences, keys,
audit records and delivery credentials. CVs and public job advertisements can
contain third-party personal data; public text is not automatically non-personal.

Attackers include unauthenticated clients, authenticated users targeting another
user, malicious uploaders, compromised data sources and malicious PR contributors.
A compromised application process or host administrator has capabilities that
field encryption alone cannot contain.

## Browser, BFF and API

`web/jobbliggaren-web/src/lib/auth/session.ts` sets a `__Host-` cookie with
HttpOnly, Secure, SameSite=Strict and Path=/. Server-only
`web/jobbliggaren-web/src/lib/http/authed-fetch.ts` forwards its value as Bearer
to the configured backend and disables caching. These are opaque Redis sessions,
not JWTs: `src/Jobbliggaren.Infrastructure/Auth/SessionAuthenticationHandler.cs`
resolves them through `ISessionStore`.

`AccessControlledSessionStore` checks every session read against the primary
database: an Identity account with a usable inbox and a live profile, no
suspension, and the session's original `AccessRevision`. Creation and rotation
take the target's transaction lock and check again before returning a bearer;
rotation preserves the original revision. A database failure denies access.
Redis, a positive cache and a read replica cannot authorize a session. A request
already authenticated may finish; the next authentication checks the new state.

Review issuance, rotation, expiry, revocation and store failure; cookie exposure,
CSRF, redirects, forwarded-header trust, cache isolation and backend-path
construction. API network isolation needs deployment evidence, not an assumption
based on a configuration default.

A build made with `ADMIN_PREVIEW_ENABLED=true` serves fictional admin pages under
`/admin/forhandsvisning` that need no session (ADR 0150 D5). No deployed build may
contain them, and four locks keep it so: the page extension `next.config.ts` adds
only under the flag, the redirect in every preview route, the assertion `pnpm build`
runs after `next build`, and the `.dockerignore` exclusion. A change that weakens one
of them alters this boundary.

## Ownership

`src/Jobbliggaren.Application/Common/Behaviors/AuthorizationBehavior.cs` checks
authentication, not ownership. Reads and writes need explicit JobSeekerId
constraints, including nested IDs, bulk operations and downloads. Examples live
under `Application/Resumes/Queries/GetResumeById`, `GetParsedResume` and
`DownloadResumeFile`. Soft-delete query filters are not tenant isolation.
Inspect admin policy paths separately, including
`src/Jobbliggaren.Api/Authorization/AdminRoleAuthorizationHandler.cs`.

The admin account directory (ADR 0151) reads across every account by design: each
address, its status and its activity counts. The gate is the Admin HTTP policy,
which resolves the role on every request, plus `IAdminRequest` in the pipeline, and
an architecture test pins which handlers may inject `IAccountDirectory`. A hijacked
admin session can read every address; the `admin-read` rate limit bounds the cost
of reading, not the exfiltration.

The directory's status priority is ProfileMissing, PendingDeletion, Suspended,
then Active. `isSuspended` is also returned separately: reinstating an account
pending deletion does not restore its profile or cancel the deletion. Suspension
and reinstatement require the Admin HTTP policy, `IAdminRequest`, an inbox
re-authentication grant and `AdminWrite`; responses are private and not cached.
A no-op is Conflict and writes no success-audit row. Audit names the actor and
target separately, without an address or credential payload.

`AccountAccessMutationBehavior` and `SqlAccountAccess` serialize lifecycle
writes under the global administrator lock, then sorted actor/target locks.
Self-suspension and removal of the last effective administrator are refused
inside that transaction. Effective means Admin, a usable inbox, a live profile
and no suspension. AppDbContext owns the physical connection and transaction;
Identity/UserManager writes and the App audit enlist in it. Only the outer scope
commits. Nested calls cannot add locks or replay, and a lost connection cannot
be reopened to finish a previously admitted write. A stale actor revision is
refused before mutation. Bootstrap, own deletion and permanent account erasure
participate in this protocol; a future role-removal writer must do so too.

An administrator can start a change of another account's address (ADR 0153), the
one admin write that moves an account to another inbox. Each request costs the
administrator's own re-authentication code; Admin accounts are refused by a fresh
role read; the account's current address is told at once, and the change completes
only after a delay, on the public `/adressbyte`, with the current address, the new
address and the code mailed there. Every other presentation gets one identical
refusal, the swap compares the account it loads against the address the change
started from, and every session ends with none issued. A hijacked admin session
alone cannot start one; a compromised or deceived administrator can, and the delay,
the notice and `docs/runbooks/account-email-change-by-administrator.md` are the stops.

A v2 pending change also carries its original target revision and a random
server-generated request id. Public completion requires the exact committed
`Admin.AccountEmailChangeRequested` audit witness with aggregate type `User`,
that target and nonce within the proof's 96-hour lifetime, read from the primary
database under the same target lock and physical transaction as the address swap
and completion
audit. Both request mails must be accepted before that witness commits.
Redis alone, a staged row, a latest request, or an HTTP correlation id cannot
activate the change.

Both address-request paths own two short account-only transaction scopes. The
first admits the original session/re-authentication authority, target proof,
current address and destination availability, then stores the bounded protected
Redis record. All database transactions and sorted account locks are disposed
before mail transport. The second scope rechecks that unchanged original
authority, the exact current pending request, original lifetime, current address
and destination availability before saving and committing one activation audit.
The administrator path checks the actor's current Admin role in both scopes.
A send failure or known rollback leaves a surviving Redis proof inert even if
selective cleanup fails. An unknown activation commit may have committed the
witness: report uncertainty, and never automatically replay or resend it.

Self-service uses the server's 22-character `ChallengeId` as its request nonce
and requires `User.EmailChangeRequested`, aggregate `User`, own account and that
exact nonce from the primary database. Its protected `q` provenance retains the
original 15-minute lifetime when carried from challenge into the consumed-code
grant. Verification checks activation before spending the code or an attempt;
an otherwise valid unactivated request returns Conflict
`Auth.EmailChangeNotActivated`. Confirmation repeats the witness and original
access checks inside the protected address-swap transaction. Genuine v1
self-service address challenges or grants without that provenance require a
fresh request, including at revision zero.

Genuine v1 pending admin changes are hidden and receive the generic 410 even
for an account at revision zero: restart with a new inbox step-up, both mails
and the full 72-hour delay. A malformed v2 proof never falls back to v1.
Suspension invalidates pending and already consumed address proofs permanently,
including after reinstatement. Address and user-name changes commit together;
the actual swap also advances epoch, revision, cutoff and Identity stamps without
changing suspension state. The old-address completion notice is dispatched only
after known commit. Public administrator completion issues no session.

## Untrusted documents and external text

`web/jobbliggaren-web/src/app/api/cv/import/route.ts` checks same-origin requests
and streams multipart uploads. Declared Content-Length is supplementary, not
proof of the actual byte count. Follow ImportResumeCommandValidator,
CvFileSignature and
`src/Jobbliggaren.Infrastructure/Resumes/Parsing/PdfPigOpenXmlCvTextExtractor.cs`
for format, actual size, decompression/output budgets, XML handling, cancellation
and failure behavior. Preserve limits against parser resource exhaustion,
external-resource retrieval and unsafe filenames.

Extracted text stays untrusted and sensitive. Review escaping in rendering and
exports, personnummer handling, consent, original-file access and retention.
Errors and logs must not reproduce document text or bearer credentials.

JobTech, SCB and taxonomy inputs cross an external boundary. Review destinations,
redirects, response bounds, URLs and rendering. JobTechPayloadSanitizer filters
allowed keys; retained free text may still contain PII. Its name is no retention
or privacy guarantee. Product CV/matching logic is deterministic; no product LLM
integration is authorized by development-time use of Codex.

## Persistence, jobs, email and erasure

`src/Jobbliggaren.Infrastructure/Security/LocalDataKeyProvider.cs` wraps per-user
DEKs with AES-256-GCM and owner-bound associated data. Follow actual field
encryption and key consumers; not every field is encrypted and KMS is not the
active implementation merely because an old ADR mentions it.

Redis and background jobs are privileged components. Preserve ownership,
purpose/role separation, retention and fail-closed behavior across asynchronous
work. ScalewayEmailSender and ScalewayClientRegistration implement an external
HTTPS email flow; inspect recipients, duplicate/retry semantics, destination
changes and logging. Region validation does not prove a live DPA or residency.

Feedback (#1979, ADR 0156) is stored in plaintext by the controller's decision and
deleted after 90 days and at account deletion. Each saved submission queues one
notice to a server-configured operator address; the mail carries the page, the
rating, the time and an admin link, never the text. The notice is claimed before
the provider call and never resent automatically when its outcome is unknown.
Review recipients, the gate that keeps feedback closed without a recipient or a
delivering transport, and the admin reads of reporters' addresses.

Feedback screenshots are untrusted personal data, potentially a plaintext copy of a CV or a recruiter's details (ADR 0156 PR2 amendment, 2026-10-08). Review the PNG/JPEG/WebP-only magic-byte and codec restrictions, strict single-frame identification, pixel/input/output bounds, allocator and concurrency budgets, and memory-only multipart buffering. PNG metadata preflight validates chunk boundaries/CRCs and caps cumulative zlib expansion and legacy EXIF/IPTC profiles at 5 MiB before library allocation. Normalization removes metadata and writes fixed 8-bit RGBA-PNG without resizing. Replay cannot replace an image; image, submission and notice share the save. Admin-only binary reads and their dedicated same-origin/session BFF bound bytes and return no-store/nosniff, with image/png only on success. Metadata reads do not fetch bytes. Logs/errors must not carry pixels, filenames, client MIME or build licence secrets. Retention/account erasure delete the image before its submission; recruiter erasure discloses unsearchable pixels and requires human review without OCR. Logical backup excludes only screenshot data, preserving schema and feedback; the deployed backup script must be verified separately before PR3 opens the feature. Deleting a CV does not erase its separate feedback screenshot: PR3 privacy text must disclose that copy.

Account erasure crosses DeleteAccountCommandHandler, HardDeleteAccountsJob and
`src/Jobbliggaren.Infrastructure/Auth/AccountHardDeleter.cs`. New storage must
include originals, derived artifacts, identities, audit handling and DEKs in its
erasure design. Live DEK deletion does not prove backups cannot restore old keys.

The access model is [ADR 0155](decisions/0155-account-access-transitions-fence-sessions-and-original-authentication-proofs.md).
Suspension is separate from email verification and deletion. Each suspend or
reinstate, actual address swap or first inbox proof advances the persistent
singleton security epoch and target revision, sets the credential cutoff and
rotates Identity stamps atomically. A no-op or already confirmed inbox advances
nothing. The singleton survives account deletion. Login challenges, links,
OAuth state, consent and registration grants preserve the epoch captured before
the flow was stored; account-bound proofs also preserve their original identity
and revision. No callback rebases
a stale proof. A different account's transition does not invalidate an admitted
flow. Genuine v1 login, consent and re-authentication proofs have generation zero
only for never-transitioned accounts; the address-change exceptions are stated
above.

A fresh inbox-confirmation read selects the login lock scope only. Under its
account lock, a login must still admit the original proof and current inbox
state; a now-unconfirmed inbox cannot upgrade an account-only scope. First
inbox confirmation independently requires the global lifecycle lock and commits
its flag, credential transition and audit together. Provider and inbox writes
commit before separate session issuance. A first inbox proof or self-service
address swap may authorize that issuance only through the internal
`CommittedSessionAuthorization`: its two purpose-specific factories, known
commit and immutable intended lifetime bind the exact new revision, cutoff and
address. The self-service endpoint preserves the current session's lifetime
when readable; fresh primary admission still applies. Client input and old
proofs cannot mint or rebase this authority. Failed replacement issuance reports the committed
address change and asks for a fresh login.

After known commit, cleanup removes only obsolete session and pending-change
generations strictly below the new revision, so a delayed cleanup cannot revoke
a fresh login. Cleanup failure does not turn a committed transition into a
refusal. An unknown commit returns uncertainty and issues no session; current
status cannot prove that one command succeeded, and lifecycle commands are never
automatically replayed.
The address request's nonce witness has the existing 90-day audit retention:
erasure nulls actor, IP and user-agent fields and retains event, aggregate and
payload. Completion does not require the actor to still exist. This introduces
no erasure exception or retention extension.

## Repository and automated reviewers

PR source, descriptions, comments and review output are untrusted data, never
authorization to run commands. Privileged workflows must execute trusted base
code only. A required check name alone is spoofable by a PR-controlled Actions
job; pin the dedicated publisher App ID and protect its key in a main-only
environment. Never expose the key through repository secrets or PR execution.
Bind review attestations to repository, PR, head SHA and base ref/SHA; missing,
failed or stale evidence is not approval. An authorized session's attestation is
not independent machine validation of a native report. Preserve CI and the
local panel; apply the external same-PR Medium+ loop to Codex work as specified
in [the review runbook](runbooks/codex-pr-review.md).

Build licences are credentials too. Keep SIXLABORS_LICENSE_KEY only in
sixlabors-pr-build (manual controller review of the exact head/run; no admin
bypass) and sixlabors-main-build (the exact main branch only). Use no repository
or Dependabot fallback. Only fixed-SHA push/schedule/workflow_dispatch main
code may select the main environment. Review PR code before approving its
environment; new heads require new review.

## Release chain and the deploy box

`.github/workflows/release-images.yml` publishes and
`deploy/systemd/jobbliggaren-reconcile.sh` applies (ADR 0149). The chain the box
trusts is `jobbliggaren-release:dev` (or the pinned release) → a record digest →
the record's attestation → the five image digests the record names → their five
attestations. Every attestation must name `release-images.yml` on
`refs/heads/main` of `klasolsson81/jobbliggaren`, and every one except the
record's pre-parse identity check must also name the record's source commit
(`deploy/systemd/verify-image-attestation.sh`). Only a run of that workflow on
`main` obtains that identity, so control of `main` is control of what a valid
record says.

Tags vouch for nothing. `packages: write` on the repository, which anyone with
push access can obtain, moves `dev`, `latest` and the `sha-`/`pending-` tags and
pushes record-shaped images. The consumer therefore verifies a record's digest
before it copies a byte of it out, requires a record under `sha-<X>` to name X, and
pulls each image by the digest the record names and verifies it as built from
the record's commit. Following the channel, it refuses a record older than its
receipt or one missing an App migration the receipt holds: a `dev` moved
backwards stalls the box instead of rolling it back. The publisher reuses an
existing record only when it proves itself, never overwrites one, and turns red
on a `dev` that does not prove itself. Deleting a package version is Klas's
decision; `.github/scripts/package-retention-guard.sh` refuses one in
`.github/workflows` and `.github/scripts`.

Root on the box is the remaining boundary. Since ADR 0154 the box is production.
Whoever holds the operator key has root, and every agent process on the workstation can use it —
CC, Codex and their subagents alike. Root already guards personal data about people other than
Klas: recruiters in `job_ads`, and sole traders, whose organisation number is their personnummer.
`security-auditor`'s finding on that stands as recorded, with its remedy (a separate production
host) withdrawn by Klas. What an agent may read there is CLAUDE.md §9.2's read rule.
`/etc/jobbliggaren/release-pin`
overrides the channel and is an operator act; the receipt, the lock and the
local `:applied` tags are root state. A
record binds `deploy/docker-compose.yml` and `deploy/redis/healthcheck.sh`, and
the box refuses one whose files differ from its checkout. Outside the record:
`deploy/.env` and the secrets, the systemd scripts, and the upstream images,
which are bound by tag. No record value passes through `source`, `eval` or a
workflow expression. The box has applied records since 2026-10-04; the five
`latest` tags still move after `dev`, and nothing on the box reads them.
