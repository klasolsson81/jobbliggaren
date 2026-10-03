#!/usr/bin/env bash
#
# Fixture tests for attestation-publish-order-guard.sh.
#
# Run:  bash .github/scripts/attestation-publish-order-guard.test.sh
#
# NEEDS NO DOCKER, NO REGISTRY AND NO NETWORK. Every case starts from the REAL
# `.github/workflows/release-images.yml` and `.github/scripts/publish-release.sh`, applies ONE edit an
# ordinary change could make, and runs the guard on the result. A case whose edit no longer applies
# (the real file moved on) fails as FIXTURE BROKEN rather than passing over an unmutated copy.
#
# THE FIRST CASE PROTECTS THE REPO: the guard must pass the real files as they are. A guard that is
# correct about mutants and never pointed at production is decoration.
#
# THREE OUTCOMES, NEVER COLLAPSED (the house rule):
#   exit 0 — the publish order holds.
#   exit 1 — it does not.
#   exit 2 — could not answer. A shape the reader does not model must never read as "holds".
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/attestation-publish-order-guard.sh"
readonly REAL_WF="$script_dir/../workflows/release-images.yml"
readonly REAL_PUB="$script_dir/publish-release.sh"
for f in "$SUT" "$REAL_WF" "$REAL_PUB"; do
  [ -f "$f" ] || {
    echo "missing: $f" >&2
    exit 1
  }
done

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

pass=0
fail=0

run_guard() {
  local got=0
  bash "$SUT" "$1" "$2" >"$TMPROOT/out" 2>&1 || got=$?
  echo "$got"
}

expect() {
  local want="$1" desc="$2" wf="$3" pub="$4" got
  got=$(run_guard "$wf" "$pub")
  if [ "$got" -eq "$want" ]; then
    pass=$((pass + 1))
    echo "  ok   $desc (exit $got)"
  else
    fail=$((fail + 1))
    echo "  FAIL $desc — wanted exit $want, got $got" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

# Copies <real> to a case file, applies the sed script, and refuses to continue if nothing changed.
mutant() {
  local real="$1" script="$2" out="$3"
  sed -E "$script" "$real" | tr -d '\r' >"$out"
  if cmp -s <(tr -d '\r' <"$real") "$out"; then
    fail=$((fail + 1))
    echo "  FAIL FIXTURE BROKEN — the edit no longer applies to $(basename "$real"): $script" >&2
    return 1
  fi
}

# A workflow case: mutate the workflow, keep the real publisher.
wf_case() {
  local want="$1" desc="$2" script="$3"
  mutant "$REAL_WF" "$script" "$TMPROOT/wf.yml" || return 0
  expect "$want" "$desc" "$TMPROOT/wf.yml" "$REAL_PUB"
}
# A publisher case: keep the real workflow, mutate the publisher.
pub_case() {
  local want="$1" desc="$2" script="$3"
  mutant "$REAL_PUB" "$script" "$TMPROOT/pub.sh" || return 0
  expect "$want" "$desc" "$REAL_WF" "$TMPROOT/pub.sh"
}

echo "attestation-publish-order-guard.sh"

echo "-- the real files"
expect 0 "the real workflow and publisher hold the order" "$REAL_WF" "$REAL_PUB"

echo "-- rule 1: no mutable publish in the workflow"
wf_case 1 "a cell that also pushes latest (the 2026-08-11 shape)" \
  's#^(          docker push "\$IMAGE:sha-\$SHORT")$#\1\n          docker push "$IMAGE:latest"#'
wf_case 1 "a push whose trailing comment merely MENTIONS :sha-" \
  's#^(          docker push "\$IMAGE:sha-\$SHORT")$#\1\n          docker push "$IMAGE:latest"  \# mirrors the :sha- push#'
wf_case 1 "an untagged push (docker tags it latest implicitly)" \
  's#^(          docker push "\$IMAGE:sha-\$SHORT")$#\1\n          docker push "$IMAGE"#'
wf_case 1 "a tag move in a workflow step" \
  's#^(          docker push "\$IMAGE:sha-\$SHORT")$#\1\n          docker buildx imagetools create --prefer-index=false -t "$IMAGE:latest" "$IMAGE@$digest"#'
wf_case 1 "a registry tool the guard does not model" \
  's#^(          docker push "\$IMAGE:sha-\$SHORT")$#\1\n          crane tag "$IMAGE:sha-$SHORT" latest#'
wf_case 1 "an immutable and a mutable push chained on one line" \
  's#^          docker push "\$IMAGE:sha-\$SHORT"$#          docker push "$IMAGE:sha-$SHORT" \&\& docker push "$IMAGE:latest"#'

echo "-- the cells: attest follows push"
wf_case 1 "attest swapped ahead of push" \
  's#^        id: push$#        id: tmp-swap#; s#^        id: attest$#        id: push#; s#^        id: tmp-swap$#        id: attest#'

echo "-- rule 2: the fan-in"
wf_case 1 "the fan-in does not wait for the cells" 's#^    needs: \[prepare, release\]$#    needs: [prepare]#'
wf_case 1 "the fan-in runs on a status function (over a failed cell)" \
  "s#^    if: needs.prepare.outputs.publish == 'true'\$#    if: always()#"
wf_case 1 "advance gated on the implicit success() alone" \
  "s#^        if: steps.verified.outcome == 'success'\$#        if: success()#"
wf_case 1 "the verified step removed" 's#^        id: verified$#        id: checked#'
wf_case 1 "advance moved ahead of verified" \
  's#^        id: verified$#        id: tmp-swap#; s#^        id: advance$#        id: verified#; s#^        id: tmp-swap$#        id: advance#'
wf_case 1 "the record step also calls advance" \
  's#^(        run: bash .github/scripts/publish-release.sh record "\$SOURCE_SHA" >>"\$GITHUB_OUTPUT")$#        run: bash .github/scripts/publish-release.sh record "$SOURCE_SHA" >>"$GITHUB_OUTPUT" \&\& bash .github/scripts/publish-release.sh advance x y#'
wf_case 1 "the record attested by something other than attest-build-provenance" \
  '/^  publish:$/,$ s#^        uses: actions/attest-build-provenance@.*#        uses: someone/attest@v1#'

echo "-- rule 3: the publisher script"
pub_case 1 "move_tag called from cmd_record" \
  's#^(  log "pushed the record for \$sha as \$digest .*)$#  move_tag "$RELEASE_REPO" "sha-$sha" "$digest"\n\1#'
pub_case 1 "imagetools create outside move_tag" \
  's#^(  emit_record "\$sha" >"\$work/release.env")$#\1\n  docker buildx imagetools create --prefer-index=false -t "$RELEASE_REPO:dev" "$RELEASE_REPO@$digest"#'
pub_case 1 "a push of something other than a :pending- tag" \
  's#docker push --quiet "\$RELEASE_REPO:pending-\$sha"#docker push --quiet "$RELEASE_REPO:sha-$sha"#'
pub_case 1 "a registry tool the guard does not model" \
  's#^(  emit_record "\$sha" >"\$work/release.env")$#\1\n  regctl image copy "$RELEASE_REPO:pending-$sha" "$RELEASE_REPO:dev"#'
pub_case 2 "a publisher without move_tag cannot be judged" 's#^move_tag\(\) \{#move_tags() {#'

echo "-- rule 4: one tree"
wf_case 1 "a cell that checks out main instead of the prepared commit" \
  '0,/^          ref: \$\{\{ needs.prepare.outputs.sha \}\}$/s##          ref: main#'
wf_case 1 "prepare no longer builds github.sha when publishing" \
  's#checkout="\$GITHUB_SHA"#checkout=main#'
wf_case 1 "the fan-in no longer asserts its tree" \
  '/^  publish:$/,$ s#\[ "\$\(git rev-parse HEAD\)" = "\$GITHUB_SHA" \] \&\& ##'

echo "-- rule 5: the local-only tag"
pub_case 1 "the publisher moves a tag named applied" \
  's#^(  log "latest names the record.s image for all .*)$#  move_tag "$PREFIX-api" applied "$image"\n\1#'

echo "-- rule 6: one run at a time"
wf_case 1 "runs allowed to overlap" 's#^  cancel-in-progress: false$#  cancel-in-progress: true#'
wf_case 1 "a computed concurrency group" 's#^  group: release-images$#  group: release-images-${{ github.run_id }}#'

echo "-- could not answer"
expect 2 "a missing workflow" "$TMPROOT/nope.yml" "$REAL_PUB"
expect 2 "a missing publisher" "$REAL_WF" "$TMPROOT/nope.sh"
printf 'name: x\njobs:\n  prepare:\n    runs-on: ubuntu-latest\n' >"$TMPROOT/empty.yml"
expect 2 "a workflow whose steps the reader cannot see" "$TMPROOT/empty.yml" "$REAL_PUB"

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ]
