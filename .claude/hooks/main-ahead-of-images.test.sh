#!/usr/bin/env bash
# Fixture suite for main-ahead-of-images.sh.
#
# The classifier's whole job is to be quiet when it cannot measure and loud when main really is
# ahead. Both halves fail silently if wrong — a permanent false alarm trains the reader to
# ignore the reaper's output, and a permanent silence is the green-silent-inert shape the
# detector was built to abolish. So each in-band failure shape gets a fixture; which shape comes
# from which caller is recorded at worktree-reaper.sh's Conjunct 4, and the labels below follow
# it.
#
# Read the summary line, not the exit code after a pipe.
set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
SUT="$HERE/main-ahead-of-images.sh"
PASS=0
FAIL=0

# Two distinct, well-formed 40-char lowercase hex SHAs.
SHA_A="ea769d32b751530ce6fec8fcb49cd73f1e431c44"
SHA_B="8c776da8a6b8d217192f03552090f29debfd3a7d"

check() { # label  expected  arg1...  (args may be absent, which is itself a case)
  local label="$1" expected="$2"
  shift 2
  local actual
  actual="$(bash "$SUT" "$@" 2>/dev/null)"
  if [ "$actual" = "$expected" ]; then
    PASS=$((PASS + 1))
    printf '  ok   %-46s -> %s\n' "$label" "$actual"
  else
    FAIL=$((FAIL + 1))
    printf '  FAIL %-46s -> got [%s], want [%s]\n' "$label" "$actual" "$expected"
  fi
}

echo "main-ahead-of-images.sh"

# --- the two verdicts that matter -------------------------------------------------------
check "identical SHAs"                 "in-sync"                        "$SHA_A" "$SHA_A"
check "main ahead of the last build"   "ahead 8c776da8 ea769d32"        "$SHA_A" "$SHA_B"
check "build ahead of main (also 'ahead')" "ahead ea769d32 8c776da8"    "$SHA_B" "$SHA_A"

# --- in-band failures, and there are two distinct shapes ---------------------------------
# Neither caller reports failure by exit code, and the two shapes are NOT interchangeable:
# gh's embedded `--jq` emits EMPTY for a null result, while a standalone `jq` over `[]` emits
# the literal string "null". worktree-reaper.sh's Conjunct 4 carries that distinction and is
# where it is recorded. `git ls-remote` emits nothing when offline or unauthenticated. Without
# the shape guard each compares unequal to any real SHA and the detector warns forever.
check "gh --jq emitted empty for the build"   "not-measurable last-build" "$SHA_A" ""
check "gh --jq emitted empty for main"        "not-measurable main-tip"   ""       "$SHA_A"
check "standalone jq emitted the string null" "not-measurable last-build" "$SHA_A" "null"
check "standalone jq null for the main tip"   "not-measurable main-tip"   "null"   "$SHA_A"

# --- malformed shapes --------------------------------------------------------------------
check "short sha (abbreviated)"        "not-measurable main-tip"        "ea769d32" "$SHA_A"
check "uppercase sha"                  "not-measurable main-tip"        "$(printf '%s' "$SHA_A" | tr 'a-f' 'A-F')" "$SHA_A"
check "41 hex chars"                   "not-measurable main-tip"        "${SHA_A}f" "$SHA_A"
check "non-hex character"              "not-measurable main-tip"        "ea769d32b751530ce6fec8fcb49cd73f1e431c4z" "$SHA_A"
check "an error message with spaces"   "not-measurable main-tip"        "could not resolve host" "$SHA_A"
check "no arguments at all"            "not-measurable main-tip"

# --- the classifier must never fail a session start --------------------------------------
bash "$SUT" >/dev/null 2>&1; rc_none=$?
bash "$SUT" "$SHA_A" "$SHA_B" >/dev/null 2>&1; rc_ahead=$?
if [ "$rc_none" -eq 0 ] && [ "$rc_ahead" -eq 0 ]; then
  PASS=$((PASS + 1)); printf '  ok   %-46s -> 0 and 0\n' "exit 0 on both no-args and ahead"
else
  FAIL=$((FAIL + 1)); printf '  FAIL %-46s -> %s and %s\n' "exit 0 on both no-args and ahead" "$rc_none" "$rc_ahead"
fi

echo ""
echo "total: $((PASS + FAIL)), passed: $PASS, failed: $FAIL"
[ "$FAIL" -eq 0 ]
