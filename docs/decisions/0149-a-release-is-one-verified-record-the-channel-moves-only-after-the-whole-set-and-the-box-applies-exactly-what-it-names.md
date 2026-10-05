# ADR 0149 — A release is one verified record: the channel moves only after the whole set, and the box applies exactly what it names

**Date:** 2026-10-04
**Status:** Accepted
**Deciders:** Klas Olsson (the #1238 contract of 2026-10-03, MVP epic #1960) · `senior-cto-advisor` (the
routing of the form round, C1–C9 and the binding text R1–R10, `docs/reviews/2026-10-03-1238-form-cto.md`,
local) · `dotnet-architect` (V1–V6, N1–N8, `docs/reviews/2026-10-03-1238-form-dotnet-architect.md`, local) ·
`security-auditor` (Majors 1–2 and Minors 3–10, `docs/reviews/2026-10-03-1238-form-security-auditor.md`,
local)
**Related:** [#1238](https://github.com/klasolsson81/jobbliggaren/issues/1238) (this ADR ships in its PR) ·
[#1960](https://github.com/klasolsson81/jobbliggaren/issues/1960) (the epic) ·
[#1961](https://github.com/klasolsson81/jobbliggaren/issues/1961) · [#1901](https://github.com/klasolsson81/jobbliggaren/issues/1901) (the
deploy specification) · #1236 (the schema-ahead gate) · #1314 (the attest-before-latest order) · #196 (the
attestation chain) · ADR 0050 (the deploy stack)
**Measured against:** `main` at `0a100f134`, 2026-10-03/04.

---

## Context

`release-images.yml` built five images in five independent matrix cells, and each cell moved its own
`latest` when it finished. The box's reconcile unit pulled the five `latest` tags hourly. Between the
first cell's move and the last one's, a pull could install a **mixed set** — a `web` from one commit
against an `api` from another — and every gate passed it: the attestation the box checks names **who**
built an image (our workflow, on `main`), never **which tree**. The timer's `:47` offset narrowed that
window and nothing closed it (#1238, found 2026-08-08). A mixed set is also the state in which a migration
meets code older than it (comment 5821638453, measured 2026-09-24 with Compose v5.5.1).

On 2026-10-03 Klas made coherent releases the first step of #1960: automatic delivery to dev, and later an
explicit promotion of the exact tested release to production. The contract (#1238, "Selected implementation
contract"): one immutable record per release, published only after the whole set is built, gated and
attested; the dev pointer moves only after the record; consumers snapshot one record and verify it before
any apply; approved releases are never silently rewritten; partial sets, wrong provenance and changed
digest or configuration bindings are refused.

## Decision

### 1. The record (C1: a GHCR package holding a scratch image)

A release is `ghcr.io/klasolsson81/jobbliggaren-release:sha-<40-hex commit>`: a `FROM scratch` image holding
one file, `/release.env`, attested by `actions/attest-build-provenance` under the same identity as the
images. The file is a closed, ordered, line-oriented format — each key once, nothing else, each value in an
anchored character class — and is never `source`d or `eval`d:

```
JBL_RELEASE_FORMAT=1
JBL_RELEASE_REPOSITORY=klasolsson81/jobbliggaren
JBL_RELEASE_SOURCE_REF=refs/heads/main
JBL_RELEASE_SOURCE_SHA=<40 hex>
JBL_RELEASE_SEQUENCE=<commit count of the source commit's history>
JBL_RELEASE_IMAGE_{API,WORKER,MIGRATE,WEB,CADDY}=sha256:<64 hex>
JBL_RELEASE_DEPLOY_SHA256=<hash of the bound deployment files>
JBL_RELEASE_MIGRATIONS_APP=<AppDbContext migration ids, ordinal order>
JBL_RELEASE_MIGRATIONS_IDENTITY=<AppIdentityDbContext migration ids>
```

Repository names are not in it — the consumer derives `ghcr.io/klasolsson81/jobbliggaren-<name>` — and
neither are timestamps or run ids, so "the same release" is byte equality. One script,
`deploy/systemd/jobbliggaren-release-record.sh`, owns the format and is called by the publisher and the box
alike.

### 2. The publisher

- Every job builds `github.sha` — the commit the signing certificate names — and asserts at run time that its
  tree is `$GITHUB_SHA`. Publishing requires a run from `refs/heads/main`.
- The five cells push and attest only `sha-<short>`. A commit that already has a record is **frozen**: its
  cells build nothing.
- The fan-in job `publish` runs only when all five cells succeeded, in the order `record` → `attest` →
  `verified` → `advance`: it verifies each image as built from the commit (blocking), writes the record,
  pushes it under a `pending-<commit>` tag, attests it, verifies it (blocking, bounded retry —
  0 unreadable attestations in 334 publishes over 120 runs, 2026-09-21 to 2026-10-03), and only then seals
  `sha-<commit>`, moves `dev` forward by git ancestry, and converges the five `latest` tags. Every move is a
  carbon copy (`imagetools create --prefer-index=false`) followed by a read-back of the landed digest.
- `.github/scripts/attestation-publish-order-guard.sh` holds that order; `package-retention-guard.sh` holds
  R3.

### 3. The consumer (C2: a local `:applied` tag; C6: a separate pin file)

`deploy/systemd/jobbliggaren-reconcile.sh` reads one record — the `dev` channel, or the release named in
`/etc/jobbliggaren/release-pin` (one line, `sha-<commit>` or `sha256:<record digest>`) — and applies exactly
what it names. Compose names our six services `jobbliggaren-<x>:applied` with `pull_policy: never`: a local
tag the reconcile moves only after everything is proven, which a manual compose command recreates from.
`--stage` verifies and tags a release on a box that has applied nothing (first boot);
`--status` reports the selection and compares the receipt with `:applied`, the running containers and the
checkout's deployment files.

### 4. Binding text (senior-cto-advisor, 2026-10-03; R1 and R7 amended by its author 2026-10-04)

- **R1, verify before parse.** No byte of a record is copied out of its image, on the box or in the publisher, before
  the record digest has verified against the release identity: the workflow SAN, ref `refs/heads/main`, and
  repository `klasolsson81/jobbliggaren`. The order is: identity verification → `docker create --pull never`,
  never started → copy, with a size cap and exactly one regular member named `release.env` → `validate` →
  verification against the validated `SOURCE_SHA` → under a tag pin, a check that `SOURCE_SHA` equals the
  pinned SHA. Record values never pass through `source`, `eval` or `${{ }}`.
- **R2, content, not tag.** A record is identified by its digest. A tag (`dev`, `sha-<40>`) selects a record
  and vouches for nothing. A record under `sha-<X>` is accepted only if `SOURCE_SHA` = X and it verifies
  against X. The publisher never rewrites a record, and any promotion a second environment
  would bring (deferred, ADR 0154), names the digest.
- **R3, rollback window.** Every record published since this ADR merged is retained. Nothing deletes package
  versions automatically, and CI guards that; deleting one is Klas's decision. Within the window, the #1236
  schema gate decides what is compatible. There is no record-based rollback to an earlier release.
- **R4, configuration binding.** The record binds the deployment configuration it was released with:
  `deploy/docker-compose.yml` without full-line comments and blank lines (semantic equivalence proven in CI on
  every commit), and `deploy/redis/healthcheck.sh` as bytes. Deliberately not bound: `deploy/.env`
  (environment and secrets); the systemd scripts (they are the consumer); the Redis ACL templates (rendered by
  a script); the Caddyfile (inside the caddy image); and upstream images, which are bound by tag through
  compose rather than by digest.
- **R5, checkout advance.** The box checkout advances to the `SOURCE_SHA` of the release it should apply,
  never to main's tip. A rollback across a configuration change pins the release and checks out its bound
  files at its `SOURCE_SHA`.
- **R6, three states.** `:applied` names what the last successful reconcile applied; before the first apply,
  it names what `--stage` verified. A failed run restores what it found. The receipt records the applied
  release once its postcondition holds. `status` compares both with the running containers, and the manual
  exceptions read `status`.
- **R7, channel acceptance.** Following the channel, the box refuses a record whose SEQUENCE is below the
  receipt's, or equal to it with a different SHA, and a record that lacks an AppDbContext id the receipt
  holds. A pin is an explicit operator act: neither check applies, and the #1236 gate decides. With no
  receipt, the first apply is a bootstrap and may move backwards once. The receipt's App ids are a subset
  of the database's history: a receipt is written only after `up` exited 0 and the postcondition held,
  and `migrate`'s `schema` mode exits 0 only on Proceed or OverriddenNoOp — subject to the residual that
  the ids are read from source.
- **R8, format evolution.** A format change ships consumer first: the box must accept FORMAT N+1, activated,
  before the publisher emits it.
- **R9.** A record is not environment-neutral: its web digest inlines the build-time `NEXT_PUBLIC_SITE_URL`.
  With one box (ADR 0154) nothing is promoted, so the value is a fixed build input set to that box's domain.
  Making the artifact environment-neutral is deferred to a second box.
- **R10 (history).** Until activation on 2026-10-04 the box ran the pre-#1238 consumer on `latest`, and the
  mixed window was the fan-in's five sequential tag moves. It closed when the box began applying records.

### 5. What C3–C9 settled

- **C3 (a):** the box refuses a release whose deployment files differ from its checkout's, and prints the
  exact command that advances the checkout to the release's commit. Klas answered A2 on 2026-10-04 (#1238,
  comment 5982420396): "A2 för dev, med fortsatt stopp vid inkompatibilitet. Ansvarig session får stående GO
  att uppdatera dev enligt runbooken och verifiera resultatet." On 2026-10-05 he extended it to the one box,
  now production: "Det gäller direkt och tillsvidare." ADR 0154 §4 reads its scope.
- **C4 (a):** App migration ids gate the channel (R7); Identity ids are recorded, and a journal line names
  any the incoming release adds (the unit never applies them; `vps-deploy-stack.md` §3c).
- **C5 (a):** `latest` stays, moved after `dev`, until a later change retires it and re-points
  `rescan-images.yml` (parked on #1961; not in scope, not verified).
- **C7:** frozen commits, with R2's validity predicate. **C8:** the record's readability gate is blocking.
  **C9:** activation is a separate, GO'd deploy.

## Consequences

**Positive.** A consumer can no longer apply images from two commits: the record names one commit and every
image is verified as built from it. A delayed or failed cell publishes nothing a consumer follows. A
published release is never rewritten, and an old run never moves the channel. The manual commands of the
runbooks recreate from the last verified release instead of whatever a refused pull left behind. The box
records which release it runs (the receipt).

**Accepted costs.** `jobbliggaren-release` becomes public, irreversibly (it holds public metadata). Delivery
pauses on a configuration-changing merge until the merging session advances the checkout (A2, §5 C3); 21 of
27 compose commits between 2026-08-01 and 2026-10-03 changed configuration (that session's measurement). There is no record-based
rollback to releases before the first record. Every fan-in run does a full clone, and every read of a record or
an image is a cosign verification. `latest` survives as a transitional tag.

**Residuals, named.**
- The commit, ref and repository pins read Fulcio's deprecated GitHub workflow extensions
  (1.3.6.1.4.1.57264.1.3, .1.5 and .1.6; sigstore/fulcio `docs/oid-info.md`, read 2026-10-03). Trigger: if Fulcio stops issuing them, the fan-in's
  blocking `verified` turns red first, before the box notices. Fallback: the predicate's
  `resolvedDependencies[0].digest.gitCommit`, which carries the same commit.
- The migration ids are read from the source tree at the commit, not from the migrate image. The #1236 gate
  in the image stays authoritative on the box; a cross-check against `GetMigrations()` is #1961's.
- Upstream images (postgres, redis, seq) are bound by tag through compose, not by digest.
- `:applied`, the receipt, the lock and the pin file are one per Docker daemon and host; with one box
  (ADR 0154) that is one per environment.

## Alternatives considered

- **Five tags moved together in the fan-in, no record.** Rejected by the contract: five registry writes are
  not one, and the consumer still reads five answers.
- **A GitHub Release asset as the record (C1 b).** Needs `contents: write` — branches and tags — and a new
  fetch-and-verify path on the box.
- **Tags or labels inside an existing image repository (C1 c).** Degenerates into five tags again, or into a
  referrers tool the box does not have.
- **Digest references in compose (C2 D).** The stronger form for two environments; here it would break about
  twenty operator commands, `inject-secrets.sh:789` and the old reconcile's image parsing.
- **Recording the configuration without enforcing it (C3 b).** Fails the contract's points 3 and 5.
- **The exact-value override for a configuration mismatch.** Would apply images with a configuration they
  were never released with; R5 covers rollbacks instead.
- **Re-attesting an existing, unattested record.** Would need its bytes read before they verify (R1); the
  pending tag makes the case unreachable instead.
- **Ordering releases with the compare API.** A network failure path; the fan-in's full clone answers the
  same question with `git merge-base --is-ancestor`.

## Implementation status

Delivered in #1238's PR: the record tool, the verifier's commit/ref/repository pins, the publisher fan-in and
`publish-release.sh`, the reworked order guard, the retention guard, the consumer with `--stage` and
`--status`, compose on `:applied` with `pull_policy: never`, runtime-ids `--pull never`, the threat model's
release section and the runbooks. Fixture suites cover every class of the contract's point 5, and the publisher and the consumer were rehearsed against a real local
registry and Compose v2.40.3.

**Done; #1238 closed 2026-10-04:** (i) the new publisher's first run on `main`, read from GHCR; (ii) Klas's
C3 answer, A2 (§5); (iii) activation in `vps-deploy-stack.md` §3b's order, 2026-10-04 20:30–20:36 UTC,
sequence 1563, after PR #2000 read each service's own image on the box's Compose v5.4.0; (iv) the first
automatic channel advance, sequence 1564.

**Handed to #1961, re-scoped by ADR 0154 to hardening the unattended delivery on the one box:** digests for
upstream images and the migration-id cross-check. Parked there (not in scope, not verified): retiring
`latest` and re-pointing the rescan. Deferred to a second box: an environment-neutral web artifact (R9) and
per-environment `:applied`, receipt, lock and pin (or C2-D).

## Amendment 2026-10-05 — host image retention (#2016)

Digest pulls followed by `:applied` tag movement leave release images in the
daemon. The dated inventory and disk evidence are in #2016 and its session log.

Use a separate fail-closed helper under the reconcile lock. Derive the current
receipt's six protection references through the existing record validator and
keys, without changing the record format. Protect all container states and
projects, all actual tags, the complete Compose profile model and receipt
references. Digest aliases in `RepoTags` are classified separately from tags;
unknown references are preserved. Validate the complete inventory before
deletion and refresh candidate tags and container references before removing
only full IDs with `--no-prune`, without force.

Run bounded passes before pulls and after the receipt, restoration phase and
apply stamp commit. Before-pass errors must not block a possible repair.
A complete after pass resolves them; an incomplete after pass exits 2 without
rolling back the committed apply. Bootstrap runs only the after pass.
Each automatic pass gets 60 seconds; reviewed one-off passes get 1800 seconds.
The monotonic deadline includes subprocess termination/reaping. An interrupted
daemon operation has an unknown outcome, so the next pass inventories afresh.

All host Docker writers must share the lock. Tagged history remains; rollback
fetches removed digests from GHCR and verifies them through the existing flow.
The reviewed one-off package uses both committed tools from one HEAD with
SHA-256 verification in a unique root-owned /run directory. No new unit, timer,
Compose/Redis binding, ACL or release format is introduced.
