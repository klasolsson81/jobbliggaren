#!/usr/bin/env bash
#
# Fixture tests for assert-not-truncated.sh.
#
# Run:  bash .github/scripts/assert-not-truncated.test.sh
#
# WHY THIS FILE EXISTS. This guard was, before extraction, the only logic in the
# branch sweep with no fixtures -- and it is the guard standing between a
# truncated open-PR list and a deletable stacked base. `build.yml`'s own
# rationale for the blocking `scripts` job applies to it verbatim: untested logic
# inside a destructive control is an assertion that cannot fail.
#
# The boundary is the whole point: `n < limit` passes, `n == limit` refuses.
# Off-by-one in the safe direction costs a red job; off-by-one in the other
# direction is a silent fail-open, which is the case `at_ceiling` pins.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/assert-not-truncated.sh"
[ -f "$SUT" ] || { echo "missing script under test: $SUT" >&2; exit 1; }

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

failures=0
cases=0

# arr <n> -> a JSON array of n objects, the shape `gh pr list --json` returns
arr() { jq -nc --argjson n "$1" '[range($n) | {number: .}]'; }

check() { # check <name> <file> <limit> <want-rc>
  local name=$1 file=$2 limit=$3 want=$4 rc
  cases=$((cases + 1))
  set +e
  bash "$SUT" "$file" "$limit" >/dev/null 2>&1
  rc=$?
  set -e
  if [ "$rc" = "$want" ]; then
    echo "ok   [$name]"
  else
    echo "FAIL [$name]: exit $rc, want $want" >&2
    failures=$((failures + 1))
  fi
}

mk() { # mk <name> <content> -> prints path
  local p="$TMPROOT/$1.json"
  printf '%s' "$2" >"$p"
  printf '%s' "$p"
}

# --- the boundary ----------------------------------------------------------
check "well under the ceiling passes"      "$(mk under "$(arr 3)")"    10 0
check "one below the ceiling passes"       "$(mk justunder "$(arr 9)")" 10 0
check "EXACTLY at the ceiling refuses"     "$(mk at "$(arr 10)")"      10 1
check "above the ceiling refuses"          "$(mk over "$(arr 12)")"    10 1
check "empty array is not truncated"       "$(mk none '[]')"           10 0

# --- unreadable input is not a short list ----------------------------------
# `gh` writes errors to stderr and NOTHING to stdout, so a failed fetch leaves a
# zero-byte file. That must refuse, not read as "zero entries, fine".
check "an empty FILE refuses"              "$(mk emptyfile '')"        10 1
check "an API error object refuses"        "$(mk errobj '{"message":"Bad credentials"}')" 10 1
check "malformed JSON refuses"             "$(mk bad 'not json')"      10 1
check "a JSON object is not an array"      "$(mk obj '{"number":1}')"  10 1

# --- argument hygiene ------------------------------------------------------
check "missing file refuses"               "$TMPROOT/nope.json"        10 1
check "non-numeric limit refuses"          "$(mk lim "$(arr 1)")"      abc 1
check "zero limit refuses"                 "$(mk lim0 "$(arr 1)")"     0 1
check "negative limit refuses"             "$(mk limneg "$(arr 1)")"   -5 1

cases=$((cases + 1))
set +e
bash "$SUT" onlyone >/dev/null 2>&1
rc=$?
set -e
if [ "$rc" = 1 ]; then echo "ok   [wrong argument count refuses]"; else
  echo "FAIL [wrong argument count refuses]: exit $rc, want 1" >&2
  failures=$((failures + 1))
fi

echo
if [ "$failures" -eq 0 ]; then
  echo "assert-not-truncated: $cases/$cases cases passed"
else
  echo "assert-not-truncated: $failures of $cases cases FAILED" >&2
  exit 1
fi
