#!/usr/bin/env bash
#
# Fixture tests for resolve-published-digests.sh.
#
# Run:  bash .github/scripts/resolve-published-digests.test.sh
#
# NEEDS NO DAEMON, NO REGISTRY AND NO NETWORK. `docker` is replaced on PATH by a stub whose
# output each case dictates, so the suite tests the RESOLVER — the matrix reader, the shape
# checks, the exit-code split, the refusal to emit a short list — rather than GHCR. That
# separation is the reason the logic lives in its own file at all: the workflow around it needs
# a registry and credentials, so anything left inside the workflow is untestable in CI.
#
# THE NEGATIVE FIXTURES CARRY THE FILE. A resolver whose cases all pass has shown that it does
# not crash, not that it refuses anything — the house rule, stated in
# compose-loopback-guard.test.sh and measured on this repo more than once.
#
# THREE OUTCOMES, ASSERTED SEPARATELY AND NEVER COLLAPSED:
#   exit 0 — every declared leg resolved. Rows on stdout.
#   exit 1 — the declaration is wrong: fewer legs than the floor.
#   exit 2 — could not answer. Every refusal below asserts 2, never 1, because "the resolver
#            could not run" must never be indistinguishable from "the resolver passed".
#
# WHY SO MANY CASES ARE ABOUT THE SHAPE OF ONE STRING. If a reference falls through empty or
# truncated, `trivy image ghcr.io/...@` scans nothing and can exit 0. The whole value of this
# script over an inline `run:` block is that such a thing is a red run instead of a green one,
# and that value is exactly as real as these fixtures are.
#
# WHAT THIS SUITE DOES NOT PIN, MEASURED BY MUTATION 2026-08-28. Eight mutations were applied to
# the SUT, each verified to have LANDED (anchor present exactly once before, gone after, file
# bytes actually changed, mutant still parses) before its result was read. Seven were killed.
# The survivor is the closing `[ "$emitted" = "$declared" ]` check, and its survival is correct
# rather than a gap: it can only fire when the matrix reader's own count check upstream has
# already been defeated, so no fixture can reach it while the file is intact. It is not
# decoration either — the `matrix reader shortfall` mutation exited 2 THROUGH THIS LINE, which is
# why that case is pinned on its diagnostic rather than on its exit code. Two guards, one
# reachable at a time; a reader should not mistake the green suite for coverage of both.
#
# The same run deleted a `case "$digest" in sha256:*)` guard from the SUT and the suite stayed
# green, because the anchored pattern beside it strictly dominated it. That guard was removed
# rather than explained — an assertion that cannot fall is the defect class this file is for.
#
# SECTION 6 ASKS THE OTHER QUESTION ENTIRELY: not "does the resolver judge a fixture right" but
# "is the artefact it reads the one that ships". `real_repo` pins the delivery itself — if
# `release-images.yml`'s matrix is respelled so the anchor stops matching, that case goes red
# here rather than silently shortening a scan matrix at 05:05 UTC.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/resolve-published-digests.sh"
[ -f "$SUT" ] || { echo "missing script under test: $SUT" >&2; exit 1; }
REPO_ROOT=$(cd -- "$script_dir/../.." && pwd)
readonly REPO_ROOT

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

readonly BIN="$TMPROOT/bin"
mkdir -p "$BIN"

pass=0
fail=0

readonly D1="sha256:1111111111111111111111111111111111111111111111111111111111111111"

# Writes a `docker` stub. $1 selects the behaviour; the stub answers the LAST argument, which is
# the image reference, so a case can make one leg behave differently from the rest.
stub_docker() {
  local mode="$1"
  cat >"$BIN/docker" <<STUB
#!/usr/bin/env bash
ref="\${@: -1}"
name="\${ref##*jobbliggaren-}"
name="\${name%%:*}"
case "$mode" in
  ok)
    printf '"%s" "2026-08-28T05:26:14.579547216Z"' "sha256:\$(printf '%s' "\$name" | md5sum | cut -c1-32)\$(printf '%s' "\$name" | md5sum | cut -c1-32)"
    ;;
  fail_caddy)
    [ "\$name" = "caddy" ] && exit 1
    printf '"%s" "2026-08-28T05:26:14.579547216Z"' "$D1"
    ;;
  pretty)
    printf 'Name:      %s\nMediaType: application/vnd.docker.distribution.manifest.v2+json\nDigest:    %s\n' "\$ref" "$D1"
    ;;
  short_digest)
    printf '"sha256:abc" "2026-08-28T05:26:14.579547216Z"'
    ;;
  not_a_digest)
    printf '"latest" "2026-08-28T05:26:14.579547216Z"'
    ;;
  uppercase_digest)
    printf '"%s" "2026-08-28T05:26:14.579547216Z"' "sha256:AAAA111111111111111111111111111111111111111111111111111111111111"
    ;;
  null_created)
    printf '"%s" null' "$D1"
    ;;
esac
exit 0
STUB
  chmod +x "$BIN/docker"
}

# Writes a release-images.yml-shaped fixture whose matrix carries the given leg names.
write_workflow() {
  local path="$1"; shift
  mkdir -p "$(dirname "$path")"
  {
    printf 'jobs:\n  release:\n    strategy:\n      matrix:\n        include:\n'
    for n in "$@"; do
      printf '          - { name: %s, context: ".", file: "src/%s/Dockerfile" }\n' "$n" "$n"
    done
  } >"$path"
}

# Runs the SUT with ONLY the stub directory plus the real coreutils on PATH.
run_sut() {
  local wf="$1"
  set +e
  PATH="$BIN:/usr/bin:/bin" \
  RESOLVE_DIGESTS_WORKFLOW="$wf" \
  RESOLVE_DIGESTS_REPO="${SLUG_OVERRIDE-klasolsson81/jobbliggaren}" \
    bash "$SUT" >"$TMPROOT/out" 2>"$TMPROOT/err"
  local rc=$?
  set -e
  return $rc
}

expect_exit() {
  local want="$1" desc="$2" wf="$3"
  local got=0
  run_sut "$wf" || got=$?
  if [ "$got" -eq "$want" ]; then
    pass=$((pass + 1))
    echo "  ok   $desc (exit $got)"
  else
    fail=$((fail + 1))
    echo "  FAIL $desc — wanted exit $want, got $got" >&2
    echo "       --- stderr ---" >&2
    sed 's/^/       /' "$TMPROOT/err" >&2
    echo "       --- stdout ---" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

assert_stdout_empty() {
  local desc="$1"
  if [ ! -s "$TMPROOT/out" ]; then
    pass=$((pass + 1))
    echo "  ok   $desc"
  else
    fail=$((fail + 1))
    echo "  FAIL $desc — a refusal emitted rows, and a short list reads as a complete one" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

assert_stderr_names() {
  local needle="$1" desc="$2"
  if grep -qF -- "$needle" "$TMPROOT/err"; then
    pass=$((pass + 1))
    echo "  ok   $desc"
  else
    fail=$((fail + 1))
    echo "  FAIL $desc — '$needle' is not in the diagnostic" >&2
    sed 's/^/       /' "$TMPROOT/err" >&2
  fi
}

echo "resolve-published-digests.sh"
readonly WF5="$TMPROOT/wf/five.yml"
write_workflow "$WF5" api worker migrate web caddy

echo "-- 1. the happy path, and what it emits"
stub_docker ok
expect_exit 0 "five declared legs resolve" "$WF5"

rowcount=$(grep -c '' "$TMPROOT/out" || true)
if [ "$rowcount" = "5" ]; then
  pass=$((pass + 1)); echo "  ok   exactly five rows on stdout"
else
  fail=$((fail + 1)); echo "  FAIL expected five rows, got $rowcount" >&2; sed 's/^/       /' "$TMPROOT/out" >&2
fi

# THE FIELD COUNT IS NOT COSMETIC. The workflow splits these rows into a JSON matrix; a row with
# a shifted field would put a timestamp where a reference belongs, and `trivy image <timestamp>`
# is a failure whose message names nothing useful.
badfields=$(awk -F'\t' 'NF != 3 { n++ } END { print n + 0 }' "$TMPROOT/out")
if [ "$badfields" = "0" ]; then
  pass=$((pass + 1)); echo "  ok   every row is exactly three TAB-separated fields"
else
  fail=$((fail + 1)); echo "  FAIL $badfields row(s) are not three TAB-separated fields" >&2
fi

badrefs=$(awk -F'\t' '$2 !~ /^ghcr\.io\/klasolsson81\/jobbliggaren-[a-z]+@sha256:[0-9a-f]{64}$/ { n++ } END { print n + 0 }' "$TMPROOT/out")
if [ "$badrefs" = "0" ]; then
  pass=$((pass + 1)); echo "  ok   every ref is fully qualified and digest-pinned"
else
  fail=$((fail + 1)); echo "  FAIL $badrefs ref(s) are not a digest-pinned ghcr reference" >&2; sed 's/^/       /' "$TMPROOT/out" >&2
fi

# The names must be the matrix's, in the matrix's order — the workflow fans out over this and a
# reordering that dropped one would still be five rows.
emitted_names=$(awk -F'\t' '{ printf "%s ", $1 }' "$TMPROOT/out")
if [ "$emitted_names" = "api worker migrate web caddy " ]; then
  pass=$((pass + 1)); echo "  ok   the names are the matrix's, in order"
else
  fail=$((fail + 1)); echo "  FAIL names were [$emitted_names]" >&2
fi

echo "-- 2. the registry could not answer"
stub_docker fail_caddy
expect_exit 2 "a failed lookup is UNANSWERABLE, never 'clean'" "$WF5"
assert_stderr_names "jobbliggaren-caddy" "the failed lookup names which image"
assert_stdout_empty "a failed lookup emits no rows at all"

rm -f "$BIN/docker"
expect_exit 2 "docker missing is unanswerable, never 'nothing to scan'" "$WF5"

echo "-- 3. the output shape is checked, not trusted"
# Docker 29's bare-template fallback, reproduced from the real CLI 2026-08-28 (29.0.1 /
# buildx v0.29.1). `release-images.yml` records the same class at its push step. Without this
# case a paragraph of text would be parsed into something starting with the word Name.
stub_docker pretty
expect_exit 2 "the Docker 29 pretty-print fallback is refused, not parsed" "$WF5"
assert_stdout_empty "the pretty-print fallback emits no rows"
# THE DIAGNOSTIC, NOT ONLY THE EXIT CODE, and this assertion is load-bearing. Measured by
# mutation 2026-08-28: disabling the line-count check left the exit code at 2 anyway, because
# the digest-shape check below it also refuses a paragraph of text. Two guards catch this, but
# only one of them says WHY, and a run whose reason is "did not resolve to a well-formed digest:
# [Name: MediaType: Digest:]" sends the reader after the wrong cause. Without this line the
# line-count check is an assertion that cannot fall.
assert_stderr_names "pretty-print fallback" "and it is NAMED as the fallback, not as a bad digest"

stub_docker short_digest
expect_exit 2 "a truncated digest is refused" "$WF5"

stub_docker not_a_digest
expect_exit 2 "a tag where a digest belongs is refused" "$WF5"

# Registries emit lowercase hex; an uppercase digest would be a different string to every
# consumer downstream. The shape check is anchored and case-sensitive on purpose.
stub_docker uppercase_digest
expect_exit 2 "an upper-case digest is refused rather than normalised" "$WF5"

stub_docker null_created
expect_exit 2 "a null creation timestamp is refused (an empty field shifts every later one)" "$WF5"

echo "-- 4. the matrix reader"
stub_docker ok

# THE ANCHOR IS THE LIMITATION, so the count is what catches it. This fixture declares six
# sequence entries and spells one of them in a way the anchor cannot read; a reader that only
# refused on ALL rows failing would report five-of-six as coverage.
readonly WFHET="$TMPROOT/wf/het.yml"
write_workflow "$WFHET" api worker migrate web caddy
printf '          - name: seq\n            context: "."\n' >>"$WFHET"
expect_exit 2 "a row the anchor cannot read is a REFUSAL, not a silent four-of-five" "$WFHET"
assert_stderr_names "declares 6 entries but the reader matched 5" "the shortfall reports both counts"

readonly WF4="$TMPROOT/wf/four.yml"
write_workflow "$WF4" api worker migrate web
expect_exit 1 "a matrix below the floor is a VIOLATION, not a refusal" "$WF4"

readonly WFDUP="$TMPROOT/wf/dup.yml"
write_workflow "$WFDUP" api api migrate web caddy
expect_exit 2 "a duplicated leg name is refused" "$WFDUP"

readonly WFEMPTY="$TMPROOT/wf/empty.yml"
mkdir -p "$(dirname "$WFEMPTY")"
printf 'jobs:\n  release:\n    steps:\n      - run: echo no matrix here\n' >"$WFEMPTY"
expect_exit 2 "a matrix that cannot be found at all is refused, never a zero-row pass" "$WFEMPTY"

expect_exit 2 "a missing workflow file is refused" "$TMPROOT/wf/does-not-exist.yml"

echo "-- 5. the namespace"
SLUG_OVERRIDE="" expect_exit 2 "an empty repository slug is refused before it builds ghcr.io/-api" "$WF5"

echo "-- 6. the artefact that actually ships"
# Not "does the resolver judge a fixture right" but "is the file it reads still readable". If
# release-images.yml's matrix is respelled, this is where it turns red — at PR time, in the
# blocking `scripts` job, rather than by shortening a scan matrix at 05:05 UTC.
stub_docker ok
expect_exit 0 "the repo's real release-images.yml reads cleanly" "$REPO_ROOT/.github/workflows/release-images.yml"
real_names=$(awk -F'\t' '{ printf "%s ", $1 }' "$TMPROOT/out")
if [ "$real_names" = "api worker migrate web caddy " ]; then
  pass=$((pass + 1)); echo "  ok   it names exactly the five published images, in order"
else
  fail=$((fail + 1)); echo "  FAIL the real matrix read as [$real_names]" >&2
fi

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ]
