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
# WHAT THIS SUITE DOES NOT PIN, MEASURED BY MUTATION 2026-08-28. Eleven mutations were applied to
# the SUT, each verified to have LANDED (anchor present exactly once before, gone after, file
# bytes actually changed, mutant still parses) before its result was read. Ten were killed.
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
  one_field)
    printf '"%s"' "$D1"
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
#
# `env -u GITHUB_REPOSITORY` IS LOAD-BEARING, NOT TIDINESS. The SUT reads
# `${RESOLVE_DIGESTS_REPO:-${GITHUB_REPOSITORY:-}}`, and `:-` treats SET-BUT-EMPTY as unset — so
# the empty-slug case below fell straight through to `GITHUB_REPOSITORY`, which every Actions
# runner sets. Locally that variable is absent and the case passed; in CI it resolved five real
# digests and exited 0 where 2 was wanted (measured 2026-08-28: `passed: 26   failed: 1`). Same
# defect class as the docker-free case further down — a fixture ASSUMING an environment property
# instead of constructing it.
run_sut() {
  local wf="$1"
  set +e
  PATH="$BIN:/usr/bin:/bin" \
  RESOLVE_DIGESTS_WORKFLOW="$wf" \
  RESOLVE_DIGESTS_REPO="${SLUG_OVERRIDE-klasolsson81/jobbliggaren}" \
    env -u GITHUB_REPOSITORY bash "$SUT" >"$TMPROOT/out" 2>"$TMPROOT/err"
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

# DELETING THE STUB IS NOT ENOUGH, AND THAT IS MEASURED. An earlier version of this case did
# `rm -f "$BIN/docker"` and ran with `PATH="$BIN:/usr/bin:/bin"`. On a developer machine that
# removes docker from the search path; ON THE GITHUB RUNNER IT DOES NOT, because the runner ships
# a real docker at /usr/bin/docker. So the SUT found it, inspected the real GHCR images, resolved
# five real digests and exited 0 — the case failed in CI on run 33167528101, and it had been
# reaching the live registry rather than testing anything. This suite's header promises no
# network; that promise was false for exactly one case.
#
# So the absence is CONSTRUCTED instead of assumed: a PATH containing wrappers for the externals
# the SUT actually uses, and nothing else. That doubles as the only written record of what those
# externals are.
#
# The farm holds WRAPPERS, not symlinks, and that is not a stylistic choice. A symlink into
# /usr/bin works on the Linux runner and breaks under MSYS/Git Bash, where a relocated coreutil
# can no longer find msys-2.0.dll and dies with "error while loading shared libraries" — which
# is a non-zero exit that would have let this case pass for the wrong reason on the runner while
# failing noisily for a developer. A wrapper invokes the tool by ABSOLUTE path, so the loader
# still resolves, and it names its interpreter absolutely for the same reason.
readonly NODOCKER="$TMPROOT/nodocker"
mkdir -p "$NODOCKER"
bash_bin=$(command -v bash)
for t in dirname tr awk grep sort uniq head; do
  src=$(command -v "$t") || { echo "  FAIL cannot build the docker-free PATH: $t not found" >&2; exit 1; }
  printf '#!%s\nexec "%s" "$@"\n' "$bash_bin" "$src" >"$NODOCKER/$t"
  chmod +x "$NODOCKER/$t"
done
if PATH="$NODOCKER" command -v docker >/dev/null 2>&1; then
  fail=$((fail + 1))
  echo "  FAIL the docker-free PATH still resolves docker — this case would prove nothing" >&2
else
  pass=$((pass + 1))
  echo "  ok   the docker-free PATH really has no docker (the case is not vacuous)"
fi

# The INTERPRETER is resolved by absolute path. `env bash` would look `bash` up through the
# docker-free PATH above and exit 127 — a missing shell, not a missing docker. That is a non-zero
# exit that would have let this case pass while proving nothing about the SUT (measured: it did,
# on the first attempt at this repair).
# `unset` in a subshell rather than `env -u`: `env` is an external too, and it is deliberately not
# in the farm — reaching for it here would fail with 127 exactly like `env bash` did.
nodocker_rc=0
(
  unset GITHUB_REPOSITORY
  PATH="$NODOCKER" RESOLVE_DIGESTS_WORKFLOW="$WF5" RESOLVE_DIGESTS_REPO="klasolsson81/jobbliggaren" \
    "$bash_bin" "$SUT"
) >"$TMPROOT/out" 2>"$TMPROOT/err" || nodocker_rc=$?
if [ "$nodocker_rc" -eq 2 ]; then
  pass=$((pass + 1)); echo "  ok   docker missing is unanswerable, never 'nothing to scan' (exit 2)"
else
  fail=$((fail + 1)); echo "  FAIL docker missing — wanted exit 2, got $nodocker_rc" >&2; sed 's/^/       /' "$TMPROOT/err" >&2
fi
assert_stderr_names "docker is not on PATH" "and it names the missing tool rather than blaming the registry"

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
expect_exit 2 "a null creation timestamp is refused" "$WF5"

stub_docker one_field
expect_exit 2 "a one-field answer is refused (the timestamp is absent, not empty)" "$WF5"

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

# THE TRUNCATION CASE, and it is the one respelling the count check cannot see. The first version
# of the reader matched `name: [A-Za-z0-9_-]+`, so `api.v2` was read as `api`: the row still
# counted as matched, the duplicate check stayed quiet, and the resolver scanned a DIFFERENT image
# from the one the workflow publishes, exit 0 and silent. Found by `security-auditor` with a probe,
# 2026-08-28. A dot is legal in an OCI path component, so the right answer is to read it WHOLE.
readonly WFDOT="$TMPROOT/wf/dot.yml"
write_workflow "$WFDOT" api.v2 worker migrate web caddy
expect_exit 0 "a legal dotted name is read whole, never truncated to a prefix" "$WFDOT"
dotted=$(awk -F'\t' 'NR == 1 { print $1 }' "$TMPROOT/out")
if [ "$dotted" = "api.v2" ]; then
  pass=$((pass + 1)); echo "  ok   and it resolves as api.v2, not as api"
else
  fail=$((fail + 1)); echo "  FAIL the dotted name resolved as [$dotted] — a prefix of the declared image" >&2
fi

# And a name that is not a legal component must REFUSE rather than be silently cut down to one.
readonly WFBAD="$TMPROOT/wf/bad.yml"
write_workflow "$WFBAD" 'api$(id)' worker migrate web caddy
expect_exit 2 "a name that is not a legal OCI component is refused, not trimmed into one" "$WFBAD"

readonly WFEMPTY="$TMPROOT/wf/empty.yml"
mkdir -p "$(dirname "$WFEMPTY")"
printf 'jobs:\n  release:\n    steps:\n      - run: echo no matrix here\n' >"$WFEMPTY"
expect_exit 2 "a matrix that cannot be found at all is refused, never a zero-row pass" "$WFEMPTY"

expect_exit 2 "a missing workflow file is refused" "$TMPROOT/wf/does-not-exist.yml"
# THE DIAGNOSTIC, because the exit code alone does not reach the guard. Deleting
# `[ -f "$WORKFLOW" ] || refuse` left the suite green: awk fails to open the file and dies with
# its own fatal exit 2, which is the same number for a different reason (measured 2026-08-28).
assert_stderr_names "workflow not found" "and it is the guard that says so, not awk dying on its own"

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

echo "-- 7. the consumer, and the parity its whole claim rests on"
# THE ANTI-VACUITY DISCIPLINE DID NOT REACH PAST THE RESOLVER'S FILE BOUNDARY, and that was the
# gap (`dotnet-architect`, 2026-08-28). Two holes, both closed here. (1) Delete
# `rescan-images.yml` and every case above stays green: all this coverage would be measuring a
# script nothing calls. (2) `rescan-images.yml` asserts in its own header that its scan
# parameters are identical to the two build-time gates, and that identity IS its claim — "this
# artefact would not pass today the gate it passed when it was built". Change `severity` in
# `build.yml` and that sentence goes quietly false. A comment cannot hold an invariant; this can.
#
# WHAT IT DOES NOT PIN, so that "parity holds" stays an honest sentence: it is a PRESENCE check
# over four settings. It does not pin the action SHA the three steps share, it does not pin the
# ABSENCE of `continue-on-error` that `rescan-images.yml` calls load-bearing, and it would not
# notice a second, weaker scan step added beside a correct one (security-auditor, 2026-08-28).
RESCAN="$REPO_ROOT/.github/workflows/rescan-images.yml"
if [ -f "$RESCAN" ]; then
  pass=$((pass + 1)); echo "  ok   the consumer exists (this suite is not measuring a script nothing calls)"
else
  fail=$((fail + 1)); echo "  FAIL $RESCAN is missing — the resolver has no consumer" >&2
fi

if grep -qF 'resolve-published-digests.sh' "$RESCAN" 2>/dev/null; then
  pass=$((pass + 1)); echo "  ok   and it is the consumer OF THIS SCRIPT"
else
  fail=$((fail + 1)); echo "  FAIL $RESCAN does not call resolve-published-digests.sh" >&2
fi

for gate in rescan-images build release-images; do
  gf="$REPO_ROOT/.github/workflows/$gate.yml"
  missing=""
  # ANCHORED TO A SETTING LINE, NEVER A SUBSTRING. An unanchored `grep -F` was satisfied by these
  # same strings appearing as PROSE: two of the three files explain in a comment that
  # "format: table deliberately, sarif ignores exit-code silently", so flipping the real setting
  # to sarif left this guard green on the strength of the comment describing why it must not be.
  # And that is the one parameter whose drift is silent -- a sarif scan cannot go red at all --
  # which is precisely what this guard was added to make loud. Found independently by
  # dotnet-architect and code-reviewer, 2026-08-28. None of the four needles contains an ERE
  # metacharacter, so they need no escaping; the leading-whitespace anchor excludes comment lines
  # because a comment puts # before the payload.
  for needle in 'severity: HIGH,CRITICAL' 'ignore-unfixed: true' 'exit-code: "1"' 'format: table'; do
    # The trailing `[[:space:]]*` is for CR, not for tidiness: these files check out with CRLF on
    # Windows, where a bare `$` anchor then matches nothing and this guard failed for every needle
    # in two of three files while the same run passed under Git Bash. A guard whose verdict
    # depends on the checkout's line endings is not one (measured 2026-08-28, ubuntu container
    # over the Windows working tree).
    grep -qE -- "^[[:space:]]+${needle}[[:space:]]*$" "$gf" 2>/dev/null || missing="$missing [$needle]"
  done
  if [ -z "$missing" ]; then
    pass=$((pass + 1)); echo "  ok   $gate.yml carries the agreed scan parameters"
  else
    fail=$((fail + 1)); echo "  FAIL $gate.yml is missing:$missing — the parity claim in rescan-images.yml is now false" >&2
  fi
done

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ]
