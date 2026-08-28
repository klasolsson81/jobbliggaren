#!/usr/bin/env bash
#
# attestation-publish-order-guard.sh — `latest` may only be published over an attested digest.
#
# WHY THIS EXISTS (#1314). On 2026-08-11 the `migrate` leg of `release-images.yml` pushed its
# image and then FAILED at `Attest`. Both tags had already been moved, so `latest` named a
# publicly pullable image carrying no attestation. The box pulls `latest`, verifies it, and
# refuses the whole apply when it does not verify — so for 56 minutes the reconcile unit failed
# hourly on an image our own pipeline had published. The gate was right; the publish order was
# wrong. The repair is the order: push `sha-<short>`, attest that digest, and only then move
# `latest` onto it.
#
# AND THE ORDER IS THE ONLY THING HOLDING IT. It is three steps in one file whose correctness
# is entirely in their sequence and in one `if:` expression. Nothing in Actions, buildx or
# cosign checks it: a `docker push …:latest` moved back into the push step, or an `if:` relaxed
# to a bare `success()`, restores the 2026-08-11 shape and every job stays green. That is the
# same class `nocache-stage-guard.sh` was written for — a repair that lives as a string in one
# file and has no reader.
#
# WHAT IT CHECKS, and each of the four is a way the repair has been observed to be undone or
# could be undone by an ordinary edit:
#   1. Exactly ONE mutable publish exists in the workflow. Two means one of them is unguarded,
#      and a second one is how a "quick fix" for a skipped push arrives.
#   2. Its step is guarded on the ATTEST STEP's own outcome. A bare `if:` inherits the implicit
#      `success()`, which is TRUE when the push step was skipped (a dry-run dispatch) — so the
#      weaker guard publishes `latest` from a build that was deliberately not pushed.
#   3. The attest step appears BEFORE the publishing step. A publisher above it can never see
#      `success` in `steps.attest.outcome`, so it would silently never publish at all — green,
#      and the box goes stale forever. Order is checked, not assumed.
#   4. The push step does NOT itself publish the mutable tag. This is the exact line the repair
#      removed; check 1 would catch it only while the guarded publisher still exists.
#
# THE EXIT CONTRACT IS THE HOUSE'S, and 2 never collapses into 1 (cf. `nocache-stage-guard.sh`,
# `verify-image-attestation.sh`, `jobbliggaren-reconcile.sh`):
#   0 — the order holds.
#   1 — it does not: the declaration is wrong and the publish path is unsafe.
#   2 — could not answer: the workflow is missing, unreadable, or shaped in a way this reader
#       does not model. "I could not read it" must never read as "it holds".
#
# THE READER IS ANCHORED, AND THAT IS A LIMITATION RATHER THAN A DESIGN WIN — the same
# declaration `nocache-stage-guard.sh` makes about its own matrix reader. Steps are recognised
# by the exact six-space `      - ` indentation this workflow uses, and `if:` by an eight-space
# key on one line. A step written in another legal YAML shape (block scalars for `if:`, a
# different indentation, a flow mapping) is not read as a step; the count then disagrees or the
# required ids go missing, and the guard refuses rather than passing.
#
# WHAT DEFEATS THIS READER, stated because the first version of this paragraph claimed a
# blanket "fails closed" that was measured false twice in review. What is now closed: a trailing
# comment on the publish line, a publish split across a backslash continuation, and an untagged
# `docker push "$img"` whose tag defaults to `latest` — the first two passed with exit 0, and
# all three are pinned in the suite. What remains open, and is not modelled: a push invoked
# other than by the literal words `docker push` (an alias, a variable holding the binary, a
# `docker` wrapper action), and a publish performed by a marketplace action rather than a `run:`
# step. Neither occurs in this workflow today. A guard cannot prove the absence of a shape it
# cannot name, so the list is the honest claim rather than the blanket one.
#
# CRLF IS HANDLED EXPLICITLY. The repo default is `core.autocrlf=true`, so this workflow is
# CRLF in a Windows worktree and LF on a CI checkout. A trailing `\r` breaks every `case` and
# `[ = ]` comparison below, silently and in the passing direction, so it is stripped on read.
set -euo pipefail

WORKFLOW="${1:-.github/workflows/release-images.yml}"
readonly WORKFLOW

readonly ATTEST_ID="attest"
readonly PUSH_ID="push"
# The guard expression the publishing step must carry, byte for byte. A weaker one is the
# defect this file exists to prevent, so it is compared exactly rather than matched loosely.
readonly REQUIRED_GUARD="steps.${ATTEST_ID}.outcome == 'success'"

fail() {
  echo "::error::attestation-publish-order-guard: $1" >&2
  shift
  for line in "$@"; do echo "  $line" >&2; done
  exit 1
}

cannot_answer() {
  echo "::error::attestation-publish-order-guard: could not answer — $1" >&2
  exit 2
}

[ -f "$WORKFLOW" ] || cannot_answer "no such workflow: $WORKFLOW"
[ -r "$WORKFLOW" ] || cannot_answer "not readable: $WORKFLOW"

# One pass, recording for each line the step it belongs to. A step begins at `      - `; every
# line until the next one belongs to it. `if:` and `id:` are read at eight spaces.
cur_id=""
cur_if=""
cur_name=""
joined=""
step_index=0
attest_index=-1
push_index=-1
publish_index=-1
publish_guard=""
publish_name=""
publish_count=0
push_publishes_latest=0

# The `\r` strip is the first thing done to every line, before any comparison. See the header.
while IFS= read -r raw; do
  line=${raw%$'\r'}

  case "$line" in
  "      - "*)
    step_index=$((step_index + 1))
    cur_id=""
    cur_if=""
    cur_name=""
    case "$line" in
    "      - name: "*) cur_name=${line#      - name: } ;;
    esac
    ;;
  "        id: "*)
    cur_id=${line#        id: }
    if [ "$cur_id" = "$ATTEST_ID" ]; then attest_index=$step_index; fi
    if [ "$cur_id" = "$PUSH_ID" ]; then push_index=$step_index; fi
    ;;
  "        if: "*)
    cur_if=${line#        if: }
    ;;
  esac

  # A command may be split across lines with a trailing backslash, and this file's own house
  # style splits exactly that shape three lines below (`docker tag "…:sha-…" \`). Joining them
  # is not cosmetic: the first version of this reader matched single lines only, so re-adding
  # the publish with a line break passed the guard with exit 0.
  case "$line" in
  *\\)
    joined="$joined${line%\\} "
    continue
    ;;
  esac
  cmd="$joined$line"
  joined=""

  # A comment is a line whose FIRST non-blank character is `#` — never a line that merely
  # CONTAINS one. The first version excluded any line with a `#` anywhere, so a publish
  # carrying a trailing comment was invisible to every check below and the guard reported
  # `order holds` over the exact 2026-08-11 regression.
  trimmed=${cmd#"${cmd%%[![:space:]]*}"}
  case "$trimmed" in
  "#"*) continue ;;
  esac

  # WHAT COUNTS AS A MUTABLE PUBLISH, and why the test is not `:latest`. Docker tags an
  # untagged reference `latest` implicitly, so `docker push "$img"` publishes the mutable tag
  # while containing neither the word nor a colon. Matching the literal missed it. The rule is
  # inverted instead: the ONLY push this workflow may make outside the guarded step is the
  # immutable, digest-bearing `:sha-<short>` tag. Every other `docker push` — `:latest`, an
  # untagged reference, or some future third tag — is a mutable publish and must sit in the
  # step gated on the attestation. Unknown shapes therefore fail CLOSED.
  case "$cmd" in
  *"docker push"*)
    case "$cmd" in
    *":sha-"*) ;;
    *)
      publish_count=$((publish_count + 1))
      publish_index=$step_index
      publish_guard=$cur_if
      publish_name=$cur_name
      if [ "$step_index" = "$push_index" ]; then push_publishes_latest=1; fi
      ;;
    esac
    ;;
  esac
done <"$WORKFLOW"

[ "$step_index" -gt 0 ] || cannot_answer "read no steps at all from $WORKFLOW — the reader's anchoring no longer matches the file"
[ "$attest_index" -ge 0 ] || cannot_answer "no step carries \`id: $ATTEST_ID\`; without it the publish guard has nothing to name"
[ "$push_index" -ge 0 ] || cannot_answer "no step carries \`id: $PUSH_ID\`"

# 4 FIRST, and the order of these two checks is itself a decision. Re-adding the
#   `docker push …:latest` line to the push step — the exact 2026-08-11 regression — ALSO
#   makes the publisher count two, so a count-first guard refuses with "two publishers" and
#   leaves the reader to work out which one is wrong. Measured against this guard's own
#   mutation harness. Both orders refuse; only this one names the regression.
if [ "$push_publishes_latest" -eq 1 ]; then
  fail "the \`$PUSH_ID\` step publishes \`:latest\` itself" \
    "That is the 2026-08-11 shape (#1314): a failing \`Attest\` then leaves \`latest\` on an" \
    "unattested digest. Publish \`sha-<short>\` here and move \`latest\` after \`Attest\`."
fi

# 1. Exactly one publisher.
if [ "$publish_count" -eq 0 ]; then
  fail "no step publishes a \`:latest\` tag" \
    "The box pulls \`latest\`; a pipeline that never moves it can never ship."
fi
if [ "$publish_count" -gt 1 ]; then
  fail "$publish_count mutable publishes found; exactly one may exist" \
    "Two publishers means one of them is not gated on the attestation."
fi

# 3. Order.
if [ "$publish_index" -le "$attest_index" ]; then
  fail "the \`:latest\` publisher (step $publish_index) does not follow the \`$ATTEST_ID\` step (step $attest_index)" \
    "\`steps.$ATTEST_ID.outcome\` read before that step runs is the empty string, so the" \
    "publisher would never fire and \`latest\` would never move."
fi

# 2. The guard expression, exactly.
if [ "$publish_guard" != "$REQUIRED_GUARD" ]; then
  fail "the \`:latest\` publisher is not gated on the attestation" \
    "step:     ${publish_name:-<unnamed>}" \
    "expected: if: $REQUIRED_GUARD" \
    "found:    if: ${publish_guard:-<none>}" \
    "A bare or absent \`if:\` inherits the implicit \`success()\`, which is TRUE when the" \
    "push step was SKIPPED — so \`latest\` would be published from a build that was" \
    "deliberately not pushed."
fi

echo "attestation-publish-order-guard: order holds in $WORKFLOW"
echo "  step $push_index publishes sha-<short>, step $attest_index attests it,"
echo "  step $publish_index moves latest, gated on \`$REQUIRED_GUARD\`."
