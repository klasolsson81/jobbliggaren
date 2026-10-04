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

# <msg>, when given, is a piece of the rule's OWN message: a mutant that trips some other rule as well
# must not pass the case for the rule it was written for (measured: two rules survived their own
# deletion while their cases stayed green on a neighbour's message).
expect() {
  local want="$1" desc="$2" wf="$3" pub="$4" msg="${5:-}" got
  got=$(run_guard "$wf" "$pub")
  if [ "$got" -eq "$want" ] && { [ -z "$msg" ] || grep -qF -- "$msg" "$TMPROOT/out"; }; then
    pass=$((pass + 1))
    echo "  ok   $desc (exit $got)"
  else
    fail=$((fail + 1))
    echo "  FAIL $desc — wanted exit $want${msg:+ saying \"$msg\"}, got $got" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

# Copies <real> to a case file, applies the sed script, and refuses to continue if nothing changed.
mutant() {
  local real="$1" script="$2" out="$3"
  tr -d '\r' <"$real" | sed -E "$script" >"$out"
  if cmp -s <(tr -d '\r' <"$real") "$out"; then
    fail=$((fail + 1))
    echo "  FAIL FIXTURE BROKEN — the edit no longer applies to $(basename "$real"): $script" >&2
    return 1
  fi
}

# A workflow case: mutate the workflow, keep the real publisher.
wf_case() {
  local want="$1" desc="$2" script="$3" msg="${4:-}"
  mutant "$REAL_WF" "$script" "$TMPROOT/wf.yml" || return 0
  expect "$want" "$desc" "$TMPROOT/wf.yml" "$REAL_PUB" "$msg"
}
# A publisher case: keep the real workflow, mutate the publisher.
pub_case() {
  local want="$1" desc="$2" script="$3" msg="${4:-}"
  mutant "$REAL_PUB" "$script" "$TMPROOT/pub.sh" || return 0
  expect "$want" "$desc" "$REAL_WF" "$TMPROOT/pub.sh" "$msg"
}
# A workflow case no line-oriented sed can express: <awk> rewrites the whole file.
wf_case_awk() {
  local want="$1" desc="$2" prog="$3" msg="${4:-}"
  tr -d '\r' <"$REAL_WF" | awk "$prog" >"$TMPROOT/wf.yml"
  if cmp -s <(tr -d '\r' <"$REAL_WF") "$TMPROOT/wf.yml"; then
    fail=$((fail + 1))
    echo "  FAIL FIXTURE BROKEN — the edit no longer applies: $desc" >&2
    return 0
  fi
  expect "$want" "$desc" "$TMPROOT/wf.yml" "$REAL_PUB" "$msg"
}

echo "attestation-publish-order-guard.sh"

echo "-- the real files"
expect 0 "the real workflow and publisher hold the order" "$REAL_WF" "$REAL_PUB"

echo "-- rule 1: what the workflow may not run"
wf_case 1 "a cell that also pushes latest (the 2026-08-11 shape)" \
  's#^(          bash .github/scripts/publish-release.sh push "\$NAME" "\$SHORT" >>"\$GITHUB_OUTPUT")$#\1\n          docker push "$IMAGE:latest"#' \
  "pushes a mutable tag"
wf_case 1 "a push whose trailing comment merely MENTIONS :sha-" \
  's#^(          bash .github/scripts/publish-release.sh push "\$NAME" "\$SHORT" >>"\$GITHUB_OUTPUT")$#\1\n          docker push "$IMAGE:latest"  \# mirrors the :sha- push#' \
  "pushes a mutable tag"
wf_case 1 "an untagged push (docker tags it latest implicitly)" \
  's#^(          bash .github/scripts/publish-release.sh push "\$NAME" "\$SHORT" >>"\$GITHUB_OUTPUT")$#\1\n          docker push "$IMAGE"#' \
  "pushes a mutable tag"
wf_case 1 "the guarded push and a mutable push chained on one line" \
  's#^          bash .github/scripts/publish-release.sh push "\$NAME" "\$SHORT" >>"\$GITHUB_OUTPUT"$#          bash .github/scripts/publish-release.sh push "$NAME" "$SHORT" >>"$GITHUB_OUTPUT" \&\& docker push "$IMAGE:latest"#' \
  "pushes a mutable tag"
wf_case 1 "docker image push, the long form of docker push" \
  's#^(          bash .github/scripts/publish-release.sh push "\$NAME" "\$SHORT" >>"\$GITHUB_OUTPUT")$#\1\n          docker image push "$IMAGE:latest"#' \
  "pushes a mutable tag"
wf_case 1 "a build that pushes as it builds" \
  's#^(          bash .github/scripts/publish-release.sh push "\$NAME" "\$SHORT" >>"\$GITHUB_OUTPUT")$#\1\n          docker buildx build --push -t "$IMAGE:latest" .#' \
  "builds and pushes in one command"
wf_case 1 "a build that pushes, written across a continuation line" \
  's#^(          bash .github/scripts/publish-release.sh push "\$NAME" "\$SHORT" >>"\$GITHUB_OUTPUT")$#\1\n          docker buildx build \\\n            --push -t "$IMAGE:latest" .#' \
  "builds and pushes in one command"
wf_case 1 "a comment ending in a backslash does not swallow the push after it" \
  's#^(          bash .github/scripts/publish-release.sh push "\$NAME" "\$SHORT" >>"\$GITHUB_OUTPUT")$#\1\n          \# a note that ends in a backslash \\\n          docker push "$IMAGE:latest"#' \
  "pushes a mutable tag"
wf_case 1 "the cells' build-push-action told to push" \
  's#^          push: false$#          push: true#' \
  "publishes from docker/build-push-action"
wf_case 1 "a manifest list pushed by hand" \
  's#^(          bash .github/scripts/publish-release.sh push "\$NAME" "\$SHORT" >>"\$GITHUB_OUTPUT")$#\1\n          docker manifest push "$IMAGE:latest"#' \
  "runs docker manifest push"
wf_case 1 "a tag move in a workflow step" \
  's#^(          bash .github/scripts/publish-release.sh push "\$NAME" "\$SHORT" >>"\$GITHUB_OUTPUT")$#\1\n          docker buildx imagetools create --prefer-index=false -t "$IMAGE:latest" "$IMAGE@$digest"#' \
  "runs imagetools create"
wf_case 1 "a registry tool the guard does not model" \
  's#^(          bash .github/scripts/publish-release.sh push "\$NAME" "\$SHORT" >>"\$GITHUB_OUTPUT")$#\1\n          crane tag "$IMAGE:sha-$SHORT" latest#' \
  "runs a registry tool this guard does not model"

echo "-- the cells: attest follows push, and push goes through push_image"
wf_case 1 "attest swapped ahead of push, in the cells only" \
  '/^  release:$/,/^  publish:$/{s#^        id: push$#        id: tmp-swap#;s#^        id: attest$#        id: push#;s#^        id: tmp-swap$#        id: attest#}' \
  "does not follow 'push'"
wf_case 1 "a cell that pushes by hand and attests a re-read" \
  's#^          bash .github/scripts/publish-release.sh push "\$NAME" "\$SHORT" >>"\$GITHUB_OUTPUT"$#          docker push "$IMAGE:sha-$SHORT"#' \
  "does not push through publish-release.sh push"

echo "-- rule 2: the fan-in"
wf_case 1 "the fan-in does not wait for the cells" 's#^    needs: \[prepare, release\]$#    needs: [prepare]#' \
  "the publish job's needs is"
wf_case 1 "the fan-in runs on a status function (over a failed cell)" \
  "s#^    if: needs.prepare.outputs.publish == 'true'\$#    if: always()#" \
  "the publish job's if: is"
wf_case 1 "advance gated on the implicit success() alone" \
  "s#^        if: steps.verified.outcome == 'success'\$#        if: success()#" \
  "the advance step's if: is"
wf_case 1 "the verified step removed" 's#^        id: verified$#        id: checked#' \
  "lacks one of the step ids"
wf_case 1 "advance and verified swapping ids" \
  's#^        id: verified$#        id: tmp-swap#; s#^        id: advance$#        id: verified#; s#^        id: tmp-swap$#        id: advance#' \
  "are not in the order record"
wf_case_awk 1 "the advance step moved, whole, ahead of verified" \
  '/^      - name: Advance$/ { inadv = 1 } inadv { adv = adv $0 "\n"; next } { lines[++n] = $0 } END { for (i = 1; i <= n; i++) { if (lines[i] == "      - name: Verified") printf "%s", adv; print lines[i] } }' \
  "are not in the order record"
wf_case 1 "the record step also calls advance" \
  's#^(        run: bash .github/scripts/publish-release.sh record "\$SOURCE_SHA" >>"\$GITHUB_OUTPUT")$#        run: bash .github/scripts/publish-release.sh record "$SOURCE_SHA" >>"$GITHUB_OUTPUT" \&\& bash .github/scripts/publish-release.sh advance x y#' \
  "only the gated advance step may"
wf_case 1 "the record attested by something other than attest-build-provenance" \
  '/^  publish:$/,$ s#^        uses: actions/attest-build-provenance@.*#        uses: someone/attest@v1#' \
  "does not use actions/attest-build-provenance"

echo "-- rule 3: the publisher script"
pub_case 1 "move_tag called from cmd_record" \
  's#^(  log "pushed the record for \$sha as \$digest .*)$#  move_tag "$RELEASE_REPO" "sha-$sha" "$digest"\n\1#' \
  "calls move_tag outside cmd_advance"
pub_case 1 "imagetools create outside move_tag" \
  's#^(  emit_record "\$sha" >"\$work/release.env")$#\1\n  docker buildx imagetools create --prefer-index=false -t "$RELEASE_REPO:dev" "$RELEASE_REPO@$digest"#' \
  "runs imagetools create outside move_tag"
pub_case 1 "a docker push outside push_image" \
  's#^(  digest=\$\(push_image "\$RELEASE_REPO" "pending-\$sha"\))$#  docker push "$RELEASE_REPO:pending-$sha"\n\1#' \
  "runs docker push outside push_image"
pub_case 1 "push_image called from cmd_advance" \
  's#^(  \# Seal: the immutable name, created once.)$#  push_image "$RELEASE_REPO" "dev" >/dev/null\n\1#' \
  "calls push_image outside cmd_record and cmd_push"
pub_case 1 "cmd_record pushing the sealed name instead of the pending one" \
  's#push_image "\$RELEASE_REPO" "pending-\$sha"#push_image "$RELEASE_REPO" "sha-$sha"#' \
  "cmd_record pushes something other than a pending- tag"
pub_case 1 "cmd_push pushing a mutable tag" \
  's#push_image "\$PREFIX-\$name" "sha-\$short"#push_image "$PREFIX-$name" "latest"#' \
  "cmd_push pushes something other than a sha- tag"
pub_case 1 "a registry tool the guard does not model" \
  's#^(  emit_record "\$sha" >"\$work/release.env")$#\1\n  regctl image copy "$RELEASE_REPO:pending-$sha" "$RELEASE_REPO:dev"#' \
  "publish-release.sh runs a registry tool"
pub_case 2 "a publisher without move_tag cannot be judged" 's#^move_tag\(\) \{#move_tags() {#'
pub_case 2 "a publisher without push_image cannot be judged" 's#^push_image\(\) \{#push_images() {#'

echo "-- rule 4: one tree"
wf_case 1 "a cell that checks out main instead of the prepared commit" \
  '0,/^          ref: \$\{\{ needs.prepare.outputs.sha \}\}$/s##          ref: main#' \
  "checks out 'main', not"
wf_case 1 "prepare checks out the dispatch's ref input instead of github.sha" \
  '/^  prepare:$/,/^  release:$/ s#^          ref: \$\{\{ github.sha \}\}$#          ref: ${{ inputs.ref }}#' \
  "prepare checks out"
wf_case 1 "a cell's tree assertion gated by an if:" \
  '/^  release:$/,/^  publish:$/ s#^(      - name: The tree is the commit this run names)$#\1\n        if: success()#' \
  "job 'release' never asserts"
wf_case 1 "prepare's tree assertion removed" \
  '/^  prepare:$/,/^  release:$/ s#\[ "\$\(git rev-parse HEAD\)" = "\$GITHUB_SHA" \]#true#' \
  "job 'prepare' never asserts"
wf_case 1 "the fan-in no longer asserts its tree" \
  '/^  publish:$/,$ s#\[ "\$\(git rev-parse HEAD\)" = "\$GITHUB_SHA" \] \&\& ##' \
  "job 'publish' never asserts"

echo "-- rule 5: the local-only tag"
pub_case 1 "the publisher moves a tag named applied" \
  's#^(  log "latest names the record.s image for all .*)$#  move_tag "$PREFIX-api" applied "$image"\n\1#' \
  "names the local-only tag applied"

echo "-- rule 6: one run at a time"
wf_case 1 "runs allowed to overlap" 's#^  cancel-in-progress: false$#  cancel-in-progress: true#' "concurrency is group"
wf_case 1 "a computed concurrency group" 's#^  group: release-images$#  group: release-images-${{ github.run_id }}#' "concurrency is group"

echo "-- could not answer"
expect 2 "a missing workflow" "$TMPROOT/nope.yml" "$REAL_PUB"
expect 2 "a missing publisher" "$REAL_WF" "$TMPROOT/nope.sh"
printf 'name: x\njobs:\n  prepare:\n    runs-on: ubuntu-latest\n' >"$TMPROOT/empty.yml"
expect 2 "a workflow whose steps the reader cannot see" "$TMPROOT/empty.yml" "$REAL_PUB"

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ]
