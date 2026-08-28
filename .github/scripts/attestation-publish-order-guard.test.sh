#!/usr/bin/env bash
#
# Fixture tests for attestation-publish-order-guard.sh.
#
# Run:  bash .github/scripts/attestation-publish-order-guard.test.sh
#
# NEEDS NO DOCKER, NO REGISTRY AND NO NETWORK. Every case is a workflow file this suite writes
# itself, so what is measured is the guard's PREDICATE — which shapes it refuses, which it
# passes, and which it declines to judge.
#
# THE NEGATIVE FIXTURES CARRY THE FILE. A guard whose cases all pass has shown it does not
# crash, not that it refuses anything. Each case below is a way the #1314 repair has been undone
# or could be undone by an ordinary edit, and each one was verified to turn this suite red
# before it was written down.
#
# AND THE LAST CASE IS THE ONE THAT PROTECTS THE REPO. Everything above it measures the guard
# against synthetic files; the coupling case runs it against the REAL
# `.github/workflows/release-images.yml`. A guard that is correct about fixtures and never
# pointed at production is decoration — the same floor `nocache-stage-guard.test.sh` applies.
#
# THREE OUTCOMES, NEVER COLLAPSED (the house rule):
#   exit 0 — the publish order holds.
#   exit 1 — it does not.
#   exit 2 — could not answer. A shape the reader does not model must never read as "holds".
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/attestation-publish-order-guard.sh"
readonly REAL="$script_dir/../workflows/release-images.yml"
[ -f "$SUT" ] || {
  echo "missing script under test: $SUT" >&2
  exit 1
}

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

pass=0
fail=0
out="$TMPROOT/out"

# Emits a workflow shaped like release-images.yml's publish tail. Knobs, all defaulting to the
# correct shape, so each case names ONLY what it breaks:
#   PUSH_LATEST=1        the push step publishes :latest itself (the 2026-08-11 regression)
#   PUBLISH=0            no :latest publisher at all
#   PUBLISH_TWICE=1      a second, unguarded publisher
#   PUBLISH_IF=<expr>    the publisher's guard expression; "OMIT" writes no if: at all
#   ATTEST_ID=0          the attest step carries no id:
#   ORDER=reversed       the publisher is emitted BEFORE the attest step
#   INDENT=<n>           step marker indentation; the reader models 6
emit() {
  local path="$1"
  local push_latest="${PUSH_LATEST:-0}"
  local publish="${PUBLISH:-1}"
  local publish_twice="${PUBLISH_TWICE:-0}"
  # A sentinel rather than an inline default: the correct expression contains single quotes,
  # and there is no way to write them inside ${VAR:-...} that does not emit literal
  # backslashes. The first version of this file did exactly that, and it cost two findings at
  # once: the shape it called correct was refused, and the near-miss case below then PASSED
  # FOR THE WRONG REASON — rejected for its backslashes rather than for naming the push step.
  local publish_if="${PUBLISH_IF:-DEFAULT}"
  if [ "$publish_if" = "DEFAULT" ]; then
    publish_if="steps.attest.outcome == 'success'"
  fi
  local attest_id="${ATTEST_ID:-1}"
  local order="${ORDER:-normal}"
  local ind
  ind=$(printf '%*s' "${INDENT:-6}" '')

  {
    echo "name: release-images"
    echo "jobs:"
    echo "  release:"
    echo "    steps:"
    echo "${ind}- name: Push api"
    echo "        id: push"
    echo "        run: |"
    echo '          docker push "${{ steps.tag.outputs.image }}:sha-${{ steps.tag.outputs.sha }}"'
    [ "$push_latest" = "1" ] && echo '          docker push "${{ steps.tag.outputs.image }}:latest"'
    if [ "$order" = "reversed" ] && [ "$publish" = "1" ]; then emit_publisher "$publish_if" "$publish_twice"; fi
    echo "${ind}- name: Attest api"
    [ "$attest_id" = "1" ] && echo "        id: attest"
    echo "        uses: actions/attest-build-provenance@v4"
    if [ "$order" != "reversed" ] && [ "$publish" = "1" ]; then emit_publisher "$publish_if" "$publish_twice"; fi
  } >"$path"
}

emit_publisher() {
  local guard="$1" twice="$2" ind
  ind=$(printf '%*s' "${INDENT:-6}" '')
  echo "${ind}- name: Publish latest api"
  [ "$guard" != "OMIT" ] && echo "        if: $guard"
  echo "        run: |"
  echo '          docker tag "${{ steps.tag.outputs.image }}:sha-${{ steps.tag.outputs.sha }}" "${{ steps.tag.outputs.image }}:latest"'
  echo '          docker push "${{ steps.tag.outputs.image }}:latest"'
  [ "$twice" = "1" ] && echo '          docker push "${{ steps.tag.outputs.image }}:latest"'
  return 0
}

# Runs the guard on $1 and records the verdict against a wanted exit code AND a wanted message
# fragment. The fragment matters: several distinct defects all exit 1, and a case that checked
# only the code would pass while the guard blamed the wrong thing — measured, twice, while this
# guard was being written.
expect() {
  local want="$1" needle="$2" desc="$3" path="$4"
  local got=0
  bash "$SUT" "$path" >"$out" 2>&1 || got=$?
  if [ "$got" -eq "$want" ] && grep -qF -- "$needle" "$out"; then
    pass=$((pass + 1))
    echo "  ok   $desc (exit $got)"
  else
    fail=$((fail + 1))
    echo "  FAIL $desc — wanted exit $want naming [$needle], got exit $got" >&2
    sed 's/^/       /' "$out" >&2
  fi
}

echo "attestation-publish-order-guard.sh"

echo "-- the correct shape passes"
(emit "$TMPROOT/ok.yml")
expect 0 "order holds" "push, then attest, then latest is the shape that holds" "$TMPROOT/ok.yml"

echo "-- the regression this guard exists for"
# The exact 2026-08-11 shape: the push step moves :latest itself, so a failing Attest leaves an
# unattested image on the tag the box pulls. It also makes the count two — the guard checks this
# FIRST so the message names the regression rather than the count.
(PUSH_LATEST=1 emit "$TMPROOT/regress.yml")
expect 1 "publishes \`:latest\` itself" "the push step publishing :latest is refused BY NAME" "$TMPROOT/regress.yml"

echo "-- the guard expression"
(PUBLISH_IF="success()" emit "$TMPROOT/bare.yml")
expect 1 "not gated on the attestation" "a bare success() is refused — it is TRUE when push was SKIPPED" "$TMPROOT/bare.yml"

(PUBLISH_IF="OMIT" emit "$TMPROOT/noif.yml")
expect 1 "not gated on the attestation" "no if: at all is refused" "$TMPROOT/noif.yml"

# The near-miss: gating on the PUSH step looks right and is not. It is true when the attestation
# failed, which is precisely the case that must not publish.
(PUBLISH_IF="steps.push.outcome == 'success'" emit "$TMPROOT/onpush.yml")
expect 1 "not gated on the attestation" "gating on the PUSH step instead of the ATTEST step is refused" "$TMPROOT/onpush.yml"

echo "-- how many publishers"
(PUBLISH=0 emit "$TMPROOT/none.yml")
expect 1 "no step publishes" "a pipeline that never moves latest can never ship, and is refused" "$TMPROOT/none.yml"

(PUBLISH_TWICE=1 emit "$TMPROOT/two.yml")
expect 1 "steps publish a \`:latest\` tag" "a second publisher is refused" "$TMPROOT/two.yml"

echo "-- order, not merely presence"
# steps.attest.outcome read before that step has run is the empty string, so a publisher above
# the attest step never fires: green pipeline, box never updated. Presence is not order.
(ORDER=reversed emit "$TMPROOT/rev.yml")
expect 1 "does not follow" "a publisher placed BEFORE the attest step is refused" "$TMPROOT/rev.yml"

echo "-- what it declines to judge, rather than passing"
(ATTEST_ID=0 emit "$TMPROOT/noid.yml")
expect 2 "id: attest" "an attest step with no id: is UNANSWERABLE, never a pass" "$TMPROOT/noid.yml"

(INDENT=4 emit "$TMPROOT/indent.yml")
expect 2 "could not answer" "a shape the reader cannot parse is UNANSWERABLE, never a pass" "$TMPROOT/indent.yml"

expect 2 "no such workflow" "a missing workflow is UNANSWERABLE, never a pass" "$TMPROOT/does-not-exist.yml"

echo "-- line endings"
# The repo default is core.autocrlf=true, so this file is CRLF in a Windows worktree and LF on
# a CI checkout. A trailing \r breaks every comparison in the guard, silently and in the PASSING
# direction, so both are pinned rather than assumed.
(emit "$TMPROOT/lf.yml")
expect 0 "order holds" "an LF workflow (what CI checks out) passes" "$TMPROOT/lf.yml"
sed 's/$/\r/' "$TMPROOT/lf.yml" >"$TMPROOT/crlf.yml"
expect 0 "order holds" "a CRLF workflow (what a Windows worktree holds) passes identically" "$TMPROOT/crlf.yml"

(PUSH_LATEST=1 emit "$TMPROOT/regress-lf.yml")
sed 's/$/\r/' "$TMPROOT/regress-lf.yml" >"$TMPROOT/regress-crlf.yml"
expect 1 "publishes \`:latest\` itself" "and a CRLF regression is still caught, not passed" "$TMPROOT/regress-crlf.yml"

echo "-- the coupling: the guard against the REAL workflow"
# Everything above measures the guard. THIS measures the repo. Without it the guard could be
# correct about fixtures while release-images.yml drifted underneath it.
if [ -f "$REAL" ]; then
  expect 0 "order holds" "release-images.yml itself publishes latest only over an attested digest" "$REAL"
else
  fail=$((fail + 1))
  echo "  FAIL the real workflow is not where this suite expects it: $REAL" >&2
fi

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ]
