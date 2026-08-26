#!/usr/bin/env bash
#
# Fixture tests for nocache-stage-guard.sh.
#
# Run:  bash .github/scripts/nocache-stage-guard.test.sh
#
# WHY THE NEGATIVE FIXTURES CARRY THE WHOLE FILE. A guard whose fixtures all pass has proven
# that it does not crash, not that it catches anything — the house rule, stated in
# compose-loopback-guard.test.sh and measured on this repo before. `renamed_stage` is the
# fixture that matters: it is the exact shape buildkit ignores silently (measured 2026-08-27,
# exit 0, zero warnings), and if the guard ever stops failing on it, the guard is decoration.
#
# THREE OUTCOMES, ASSERTED SEPARATELY AND NEVER COLLAPSED:
#   exit 0 — read, and the coupling holds.
#   exit 1 — read, and it does not. Names the leg.
#   exit 2 — could not answer. "The guard could not run" must never be indistinguishable
#            from "the guard passed", which is why every refusal below asserts 2, not 1.
#
# SECTION 4 PINS A BUG THIS GUARD ACTUALLY HAD. `empty_nocache_not_swallowed` failed against
# the first version: the row was emitted as name/file/nocache/has_nocache and read with
# `IFS=$'\t'`, but TAB IS IFS WHITESPACE, so the empty `nocache` collapsed and `has_nocache`
# shifted into it — every leg with `nocache: ""` refused as "has no nocache field". The
# possibly-empty field now goes last. The fixture stays because the failure mode is invisible
# by reading and reappears on any future field reorder.
#
# SECTION 5 ASKS THE OTHER QUESTION ENTIRELY: not "does the guard judge a fixture right" but
# "is the artefact it reads the one that ships". `real_repo` pins the delivery itself, so a
# future edit that renames a stage or drops a filter fails here and not only in review.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/nocache-stage-guard.sh"
[ -f "$SUT" ] || { echo "missing script under test: $SUT" >&2; exit 1; }
REPO_ROOT=$(cd -- "$script_dir/../.." && pwd)
readonly REPO_ROOT

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

pass=0
fail=0

# make_case <name> <nocache-for-web> <web-stage-name> [upgrade:yes|no]
# Builds a minimal repo root: a workflow carrying one api leg and one web leg, plus the two
# Dockerfiles those legs point at.
make_case() {
  local name="$1" web_nocache="$2" web_stage="$3" upgrade="${4:-yes}"
  local root="$TMPROOT/$name"
  mkdir -p "$root/.github/workflows" "$root/web" "$root/api"

  cat > "$root/.github/workflows/build.yml" <<YAML
jobs:
  images:
    strategy:
      matrix:
        include:
          - { name: api, context: ".", file: "api/Dockerfile", nocache: "" }
          - { name: web, context: "web", file: "web/Dockerfile", nocache: "$web_nocache" }
YAML

  printf 'FROM scratch AS base\nFROM scratch AS runtime\n' > "$root/api/Dockerfile"

  {
    printf 'FROM scratch AS deps\n'
    printf 'FROM scratch AS %s\n' "$web_stage"
    [ "$upgrade" = "yes" ] && printf 'RUN apk --no-cache upgrade\n'
  } > "$root/web/Dockerfile"

  echo "$root"
}

expect() {
  local label="$1" root="$2" want="$3"
  local got=0
  NOCACHE_GUARD_ROOT="$root" bash "$SUT" >/dev/null 2>&1 || got=$?
  if [ "$got" = "$want" ]; then
    printf '  ok    %-42s exit %s\n' "$label" "$got"; pass=$((pass + 1))
  else
    printf '  FAIL  %-42s want %s, got %s\n' "$label" "$want" "$got"; fail=$((fail + 1))
  fi
}

echo "== 1. the coupling holds =="
expect "filter names a stage that exists" "$(make_case ok runtime runtime yes)" 0
expect "no upgrade, no filter" "$(make_case noup "" runtime no)" 0

echo "== 2. violations — the reason this file exists =="
# THE fixture. Buildkit ignores this silently; nothing else in the system says a word.
expect "renamed_stage: filter names a dead stage" "$(make_case renamed runtime svelte yes)" 1
expect "upgrade with no filter at all" "$(make_case unfiltered "" runtime yes)" 1
expect "filter typo (one character)" "$(make_case typo runtme runtime yes)" 1

echo "== 3. refusals — 'could not run' is not 'passed' =="
missing_wf="$TMPROOT/no_workflow"; mkdir -p "$missing_wf"
expect "workflow absent" "$missing_wf" 2

reshaped="$TMPROOT/reshaped"; mkdir -p "$reshaped/.github/workflows"
printf 'jobs:\n  images:\n    strategy:\n      matrix:\n        include:\n          - name: api\n            file: "api/Dockerfile"\n' \
  > "$reshaped/.github/workflows/build.yml"
expect "matrix reshaped to block mappings" "$reshaped" 2

gone=$(make_case gone runtime runtime yes); rm "$gone/web/Dockerfile"
expect "leg points at a missing Dockerfile" "$gone" 2

nofield="$TMPROOT/nofield"; mkdir -p "$nofield/.github/workflows" "$nofield/api"
printf 'jobs:\n  images:\n    strategy:\n      matrix:\n        include:\n          - { name: api, context: ".", file: "api/Dockerfile" }\n' \
  > "$nofield/.github/workflows/build.yml"
printf 'FROM scratch AS runtime\n' > "$nofield/api/Dockerfile"
expect "leg omits the nocache field entirely" "$nofield" 2

echo "== 4. regression: the IFS bug this guard actually had =="
# Against v1 every one of these refused as "has no nocache field", because an empty middle
# field collapsed under IFS-whitespace and shifted has_nocache into nocache.
expect "empty_nocache_not_swallowed (api leg)" "$(make_case empty1 "" runtime no)" 0
expect "empty nocache beside a filled one" "$(make_case empty2 runtime runtime yes)" 0

echo "== 5. the delivered artefact, not a fixture of it =="
expect "real_repo: build.yml + real Dockerfiles" "$REPO_ROOT" 0

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ] || exit 1
