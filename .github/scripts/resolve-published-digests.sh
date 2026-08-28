#!/usr/bin/env bash
#
# resolve-published-digests.sh — resolve every published image's `latest` tag to an immutable
# digest reference, so something can scan THE ARTEFACT THE BOX RUNS rather than a rebuild of it.
#
# WHY THIS EXISTS (#1519). `release-images.yml` skips Build, Trivy AND Push whenever the current
# `main` SHA is already published under both tags with a readable attestation. That skip is
# deliberate and well argued for trigger robustness, and it is not changed by this file. Its
# unnamed consequence is that A PUBLISHED IMAGE IS NEVER RESCANNED: no GitHub Actions event fires
# on a published advisory (measured 2026-08-28 — neither `security_advisory`,
# `repository_advisory` nor `repository_vulnerability_alert` is an `on:` event), Dependabot has
# nothing to propose for a floating base tag, and so the only thing that ever produces a new
# image is a merge for some unrelated reason. Patch latency had no ceiling.
#
# `rescan-images.yml` is the detector that gives it one, and this script is the half of it that
# can be tested without a registry.
#
# THE DECISION THIS ENCODES (senior-cto-advisor, 2026-08-28): DETECT, DO NOT REPAIR. The repair
# lever already exists and always did — a merge yields a new SHA, the release predicate goes
# false, and the box pulls within the hour. What was missing was never the repair, it was the
# knowledge. An age-term that rebuilt on a timer was refused because it re-points `sha-<short>`,
# which `release-images.yml` defines as the rollback handle the box pins in its `.env`; a handle
# that means two digests at two times is a rollback that does not roll back.
#
# WHY THE LIST IS DERIVED AND NOT DECLARED. The five image names already exist in `build.yml`,
# `release-images.yml`, `deploy/docker-compose.yml` and the reconcile allowlist. A sixth copy in
# the rescan workflow would be a sixth thing to keep in step, so the workflow declares nothing:
# it asks this script, and this script reads `release-images.yml`'s own matrix. Adding an image
# there is picked up here with no second edit.
#
# THREE OUTCOMES, NEVER COLLAPSED — the house rule (cf. nocache-stage-guard.sh,
# verify-image-attestation.sh, jobbliggaren-reconcile.sh):
#   exit 0 — every declared leg resolved to a well-formed digest. Rows on stdout.
#   exit 1 — read everything, and the DECLARATION is wrong: fewer legs than the floor.
#   exit 2 — could not answer. A missing workflow, a partially readable matrix, a missing
#            docker, a failed lookup, a malformed digest and Docker 29's pretty-print fallback
#            all land here rather than at 0, because "the resolver could not run" must never be
#            indistinguishable from "the resolver passed". THAT IS THE WHOLE POINT OF THE FILE:
#            if a reference fell through empty, `trivy image ghcr.io/...@` scans nothing and can
#            exit 0 — a green no-op that reads as coverage, which is the shape `build.yml` and
#            `nocache-stage-guard.sh` both already condemn in this subsystem.
#
# THE PRETTY-PRINT FALLBACK IS A MEASURED TRAP, NOT A HYPOTHETICAL. `release-images.yml` records
# it at its push step: on Docker 29 a BARE `{{.Manifest.Digest}}` template silently returns the
# whole human-readable block instead of the field. Reproduced again 2026-08-28 on Docker 29.0.1 /
# buildx v0.29.1 — three lines of `Name:/MediaType:/Digest:` where one digest was asked for. So
# the template here is `{{json ...}}`, and the shape of what comes back is checked rather than
# trusted.
#
# THE MATRIX READER IS ANCHORED, AND THAT IS A LIMITATION RATHER THAN A DESIGN WIN. Rows are
# matched by the exact one-line flow-mapping spelling used today. A row written any other legal
# way matches nothing — so the COUNT is checked first and any shortfall is a refusal, exactly as
# `nocache-stage-guard.sh` does after three reviewers independently measured that its first
# version read a heterogeneous matrix partially and reported coverage it did not have.

set -euo pipefail

readonly EXIT_OK=0
readonly EXIT_VIOLATION=1
readonly EXIT_REFUSE=2

# The floor is a FLOOR and not an equality, so adding a sixth image does not fail for the wrong
# reason — but a matrix that has lost legs is a real defect in repo state rather than something
# this script could not read, which is why it is a violation and not a refusal.
readonly MIN_LEGS=5

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT="${RESOLVE_DIGESTS_ROOT:-$(cd -- "$script_dir/../.." && pwd)}"
readonly REPO_ROOT
WORKFLOW="${RESOLVE_DIGESTS_WORKFLOW:-$REPO_ROOT/.github/workflows/release-images.yml}"
readonly WORKFLOW

refuse() { echo "REFUSED: $*" >&2; exit "$EXIT_REFUSE"; }

[ -f "$WORKFLOW" ] || refuse "workflow not found: $WORKFLOW"

# The registry namespace, lowercased exactly as `release-images.yml` does with
# `${GITHUB_REPOSITORY,,}`. An empty value would build `ghcr.io/-api`, which resolves to nothing
# and would look like an outage rather than a misconfiguration, so it refuses here where the
# cause is nameable.
slug="${RESOLVE_DIGESTS_REPO:-${GITHUB_REPOSITORY:-}}"
slug=$(printf '%s' "$slug" | tr '[:upper:]' '[:lower:]')
[ -n "$slug" ] || refuse "no repository slug — set GITHUB_REPOSITORY (or RESOLVE_DIGESTS_REPO). Without it every reference would be built against an empty namespace and a lookup failure would read as an outage."

# --- the matrix reader, and its own count check first --------------------------------------
declared=$(awk '
  /^      matrix:[[:space:]]*$/             { in_m = 1; next }
  in_m && /^        include:[[:space:]]*$/  { in_i = 1; next }
  in_i && /^[[:space:]]*$/                  { next }
  in_i && /^[[:space:]]*#/                  { next }
  in_i && /^          - /                   { n++; next }
  in_i && !/^          /                    { in_i = 0; in_m = 0 }
  END { print n + 0 }
' "$WORKFLOW")

names=$(awk '
  /^          - \{ name: / {
    if (match($0, /name: [A-Za-z0-9_-]+/)) { print substr($0, RSTART + 6, RLENGTH - 6) }
  }
' "$WORKFLOW")

matched=0
[ -n "$names" ] && matched=$(printf '%s\n' "$names" | grep -c '' || true)

[ "$declared" -gt 0 ] \
  || refuse "no matrix sequence entries found in $WORKFLOW — the shape changed, and a zero-row sweep passes vacuously"
[ "$matched" = "$declared" ] \
  || refuse "matrix declares $declared entries but the reader matched $matched — a row is spelled in a way this anchor does not read, and a partially-read matrix reports coverage it does not have"

# Duplicate names would emit two rows for one image and make the scan matrix disagree with
# itself; the count above cannot see it because both rows parse.
dupes=$(printf '%s\n' "$names" | sort | uniq -d || true)
[ -z "$dupes" ] || refuse "duplicate image name(s) in the matrix: $(printf '%s' "$dupes" | tr '\n' ' ')"

if [ "$declared" -lt "$MIN_LEGS" ]; then
  echo "VIOLATION: $WORKFLOW declares $declared image(s), expected at least $MIN_LEGS." >&2
  echo "If one was legitimately removed, lower MIN_LEGS in this script deliberately." >&2
  exit "$EXIT_VIOLATION"
fi

command -v docker >/dev/null 2>&1 \
  || refuse "docker is not on PATH — a missing tool must never read as 'nothing to scan'"

# --- resolve each leg ------------------------------------------------------------------------
# Rows are accumulated and printed only on FULL success. A partial list on stdout would be a
# shorter scan matrix that still looks like a complete one, which is the same vacuous-coverage
# failure the count check above exists for.
rows=""

while IFS= read -r name; do
  [ -n "$name" ] || refuse "a matrix row parsed with no name — cannot vouch for this shape"
  img="ghcr.io/${slug}-${name}"

  # ONE lookup per image, both fields in one template. `{{json ...}}` on BOTH — see the
  # pretty-print note in the header; a bare template is the measured trap.
  #
  # STDERR IS DISCARDED, and the reason is the same one `release-images.yml` gives for its own
  # predicate: under `2>&1` the error text lands in the variable, where it is one more string to
  # be mis-parsed. The failure is reported by the branch below instead, which names the image.
  out=$(docker buildx imagetools inspect --format '{{json .Manifest.Digest}} {{json .Image.Created}}' "$img:latest" 2>/dev/null) \
    || refuse "could not inspect $img:latest — the registry, credentials or the CLI could not answer. This is NOT 'the image is clean'."

  # The fallback returns a multi-line human-readable block, so anything but a single line is
  # refused before it can be parsed into something that looks like a digest.
  lines=$(printf '%s\n' "$out" | grep -c '' || true)
  [ "$lines" = "1" ] \
    || refuse "$img:latest returned $lines lines where one was asked for — this is the Docker 29 pretty-print fallback, not a digest. Output began: $(printf '%s' "$out" | head -c 120)"

  digest=$(printf '%s' "$out" | awk '{print $1}' | tr -d '"')
  created=$(printf '%s' "$out" | awk '{print $2}' | tr -d '"')

  # ONE check, not two. An earlier version carried a `case "$digest" in sha256:*)` guard ahead of
  # this line; mutation 2026-08-28 deleted that guard and the suite stayed 24/0, because the
  # anchored pattern below strictly dominates it. An assertion that cannot fall is the defect
  # class this file exists to close, so it went rather than being explained.
  printf '%s' "$digest" | grep -qE '^sha256:[0-9a-f]{64}$' \
    || refuse "$img:latest did not resolve to a well-formed digest: [$digest] — an empty, truncated, upper-case or non-hex digest would reach the scanner as a reference that resolves to nothing"

  # Reported beside the digest so a red run reads "this artefact was built <date> and now carries
  # CVE-X", which is the sentence that makes the notification actionable. IT IS NEVER A
  # PREDICATE: an image is not stale because it is old, it is stale because an advisory with a
  # published fix now matches it, and that is what the scanner measures directly.
  [ -n "$created" ] && [ "$created" != "null" ] \
    || refuse "$img@$digest carries no image-config creation timestamp — the config could not be read, and a row with an empty field would shift every later field left"

  rows="${rows}${name}	${img}@${digest}	${created}
"
done <<EOF
$names
EOF

emitted=$(printf '%s' "$rows" | grep -c '' || true)
[ "$emitted" = "$declared" ] \
  || refuse "resolved $emitted row(s) for $declared declared image(s) — refusing rather than emitting a short scan matrix"

printf '%s' "$rows"
exit "$EXIT_OK"
