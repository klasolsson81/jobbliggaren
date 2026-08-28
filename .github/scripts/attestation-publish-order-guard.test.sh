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
#   PUSH_LATEST=1        the push step publishes the mutable tag itself (the 2026-08-11
#                        regression). PUSH_LATEST_STYLE picks HOW it is written: plain,
#                        a trailing shell comment, a backslash continuation, an untagged
#                        reference, an && chain (one line or split), a `;` chain, a
#                        trailing comment that merely MENTIONS :sha-, or a comment line
#                        ending in a backslash. Every one of them was measured passing
#                        with exit 0 at some point in this PR's review.
#                        not a second step and not ungated. SECOND_STEP=1 is that one.
#   SECOND_STEP=1        a real second publisher step, after the guarded one, with no if:
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
    if [ "$push_latest" = "1" ]; then
      case "${PUSH_LATEST_STYLE:-plain}" in
      comment) echo '          docker push "${{ steps.tag.outputs.image }}:latest"  # keep latest moving' ;;
      continuation)
        printf '%s
' '          docker push \'
        printf '%s
' '            "${{ steps.tag.outputs.image }}:latest"'
        ;;
      and-oneline) echo '          docker push "${{ steps.tag.outputs.image }}:sha-${{ steps.tag.outputs.sha }}" && docker push "${{ steps.tag.outputs.image }}:latest"' ;;
      and-split)
        printf '%s
' '          docker push "${{ steps.tag.outputs.image }}:sha-${{ steps.tag.outputs.sha }}" && \'
        printf '%s
' '          docker push "${{ steps.tag.outputs.image }}:latest"'
        ;;
      semicolon) echo '          docker push "${{ steps.tag.outputs.image }}:sha-${{ steps.tag.outputs.sha }}"; docker push "${{ steps.tag.outputs.image }}:latest"' ;;
      comment-sha) echo '          docker push "${{ steps.tag.outputs.image }}:latest"  # mirrors the :sha- push' ;;
      comment-cont)
        printf '%s
' '          # quick fix \'
        printf '%s
' '          docker push "${{ steps.tag.outputs.image }}:latest"'
        ;;
      implicit) echo '          docker push "${{ steps.tag.outputs.image }}"' ;;
      *) echo '          docker push "${{ steps.tag.outputs.image }}:latest"' ;;
      esac
    fi
    if [ "$order" = "reversed" ] && [ "$publish" = "1" ]; then emit_publisher "$publish_if" "$publish_twice"; fi
    echo "${ind}- name: Attest api"
    [ "$attest_id" = "1" ] && echo "        id: attest"
    echo "        uses: actions/attest-build-provenance@v4"
    if [ "$order" != "reversed" ] && [ "$publish" = "1" ]; then emit_publisher "$publish_if" "$publish_twice"; fi
    if [ "${SECOND_STEP:-0}" = "1" ]; then emit_publisher "OMIT" "0"; fi
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

# THREE SPELLINGS THAT ALL PASSED WITH EXIT 0 BEFORE REVIEW, each measured. The detector
# excluded any line containing a `#` ANYWHERE, matched single lines only, and tested for the
# literal `:latest` — so a trailing comment, a line break, or an untagged reference each hid
# the regression completely. The rule is inverted now: every `docker push` that is not the
# immutable `:sha-<short>` tag is a mutable publish.
(PUSH_LATEST=1 PUSH_LATEST_STYLE=comment emit "$TMPROOT/regress-comment.yml")
expect 1 "publishes \`:latest\` itself" "a trailing shell comment does not hide the regression" "$TMPROOT/regress-comment.yml"

(PUSH_LATEST=1 PUSH_LATEST_STYLE=continuation emit "$TMPROOT/regress-cont.yml")
expect 1 "publishes \`:latest\` itself" "nor does a backslash continuation — this file's own house style" "$TMPROOT/regress-cont.yml"

(PUSH_LATEST=1 PUSH_LATEST_STYLE=implicit emit "$TMPROOT/regress-implicit.yml")
expect 1 "publishes \`:latest\` itself" "nor an untagged reference, whose tag DEFAULTS to latest" "$TMPROOT/regress-implicit.yml"

# FIVE MORE, ALL FOUND IN THE SCOPED RE-CHECK OF THE FIX ABOVE. Inverting the rule was right,
# but the exemption was tested against the whole LINE while it belongs to a COMMAND: one
# immutable push then vouched for a mutable one beside it. And the comment test ran AFTER the
# continuation accumulator, so a comment ending in a backslash swallowed the command below it.
# Each of these was measured exit 0 before the predicate was made per-segment.
(PUSH_LATEST=1 PUSH_LATEST_STYLE=and-oneline emit "$TMPROOT/regress-and1.yml")
expect 1 "publishes \`:latest\` itself" "an && chain does not let the sha- push vouch for the mutable one" "$TMPROOT/regress-and1.yml"

(PUSH_LATEST=1 PUSH_LATEST_STYLE=and-split emit "$TMPROOT/regress-and2.yml")
expect 1 "publishes \`:latest\` itself" "nor an && chain split across a real backslash continuation" "$TMPROOT/regress-and2.yml"

(PUSH_LATEST=1 PUSH_LATEST_STYLE=semicolon emit "$TMPROOT/regress-semi.yml")
expect 1 "publishes \`:latest\` itself" "nor a semicolon chain" "$TMPROOT/regress-semi.yml"

(PUSH_LATEST=1 PUSH_LATEST_STYLE=comment-sha emit "$TMPROOT/regress-csha.yml")
expect 1 "publishes \`:latest\` itself" "nor a trailing comment that merely MENTIONS :sha-" "$TMPROOT/regress-csha.yml"

(PUSH_LATEST=1 PUSH_LATEST_STYLE=comment-cont emit "$TMPROOT/regress-ccont.yml")
expect 1 "publishes \`:latest\` itself" "nor a comment line ending in a backslash, which used to swallow the next command" "$TMPROOT/regress-ccont.yml"

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
expect 1 "mutable publishes found" "a second publish command inside the guarded step is refused" "$TMPROOT/two.yml"

# The shape check 1's own rationale names — "two publishers means one of them is unguarded" —
# and which had no fixture at all until review: a real second step, after the guarded one,
# carrying no if: whatsoever.
(SECOND_STEP=1 emit "$TMPROOT/twostep.yml")
expect 1 "mutable publishes found" "a second, UNGATED publisher step is refused" "$TMPROOT/twostep.yml"

echo "-- order, not merely presence"
# steps.attest.outcome read before that step has run is the empty string, so a publisher above
# the attest step never fires: green pipeline, box never updated. Presence is not order.
(ORDER=reversed emit "$TMPROOT/rev.yml")
expect 1 "does not follow" "a publisher placed BEFORE the attest step is refused" "$TMPROOT/rev.yml"

echo "-- what it declines to judge, rather than passing"
(ATTEST_ID=0 emit "$TMPROOT/noid.yml")
expect 2 "id: attest" "an attest step with no id: is UNANSWERABLE, never a pass" "$TMPROOT/noid.yml"

(INDENT=4 emit "$TMPROOT/indent.yml")
expect 2 "read no steps at all" "a shape the reader cannot parse is UNANSWERABLE, never a pass" "$TMPROOT/indent.yml"

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
