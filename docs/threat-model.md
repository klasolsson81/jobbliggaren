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

Account erasure crosses DeleteAccountCommandHandler, HardDeleteAccountsJob and
`src/Jobbliggaren.Infrastructure/Auth/AccountHardDeleter.cs`. New storage must
include originals, derived artifacts, identities, audit handling and DEKs in its
erasure design. Live DEK deletion does not prove backups cannot restore old keys.

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
