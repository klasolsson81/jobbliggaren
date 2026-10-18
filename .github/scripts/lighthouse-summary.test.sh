#!/usr/bin/env bash
# Fixtures for lighthouse-summary.sh.
#
# Run:  bash .github/scripts/lighthouse-summary.test.sh
#
# The renderer exits 0 on every handled input (the job is observe-only), so each case
# asserts on the TEXT it writes. A case that checked only the exit code would pass
# against a renderer that wrote nothing, which is the green-but-empty shape this job
# had until #1007, #1011 and #1048.
#
# Every sample is lhci 0.15.1's own output, written by its `assert` command over the
# 24 LHRs of build run 39140914222 (PR #2179, head c285f1f74, `lighthouse-results`):
# - lhci-assertion-results-mixed.json: `lhci assert --includePassedAssertions` under this
#   repo's lighthouserc.json. Its eight failing rows are identical to that run's own
#   assertion-results.json, the eight LCP failures.
# - lhci-assertion-results-all-pass.json: the mixed file with its failing rows deleted.
# - lhci-assertion-results-tbt-warn.json: lhci assert over the same LHRs with one
#   assertion, total-blocking-time at warn level and maxNumericValue 100 instead of 200.
#   The production budget produced no warn-level failure in the last 40 build runs
#   (scanned 2026-10-18), so lhci was given a lower bound to emit the shape it emits
#   whenever TBT exceeds its budget.
# Cases derive further inputs from these by deletion or truncation only, never by
# editing a value.
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
SUT="$HERE/lighthouse-summary.sh"
MIXED="$HERE/samples/lhci-assertion-results-mixed.json"
ALLPASS="$HERE/samples/lhci-assertion-results-all-pass.json"
WARN="$HERE/samples/lhci-assertion-results-tbt-warn.json"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
PASS=0; FAIL=0

# render <input> -> sets RC, STDOUT and SUMMARY
render() {
  local out="$TMP/summary.md"
  rm -f "$out"
  STDOUT=$(bash "$SUT" "$1" "$out" 2>&1); RC=$?
  SUMMARY=$(cat "$out" 2>/dev/null || true)
}
ok()  { PASS=$((PASS+1)); echo "  ok   — $1"; }
bad() { FAIL=$((FAIL+1)); echo "  FAIL — $1"; echo "----- summary -----"; echo "$SUMMARY"; echo "----- stdout -----"; echo "$STDOUT"; echo "------------------"; }
expect()       { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1 (got '$2', want '$3')"; fi; }
expect_has()   { case "$2" in *"$3"*) ok "$1" ;; *) bad "$1" ;; esac; }
expect_lacks() { case "$2" in *"$3"*) bad "$1" ;; *) ok "$1" ;; esac; }
rows()         { printf '%s\n' "$1" | grep -c "^| $2 |" || true; }
cells()        { printf '%s\n' "$1" | grep '^| ' || true; }

echo "mixed: the eight LCP failures and every passing row"
render "$MIXED"
expect      "exit 0"                         "$RC" 0
expect_has  "byte header carries lhci's budget"   "$SUMMARY" "| URL | document.size <= 30720 | font.size <= 122880 |"
expect_has  "a byte cell is lhci's aggregate"     "$SUMMARY" "| / | 12359 ✓ | 61250 ✓ |"
expect_has  "a timing cell shows the runs"        "$SUMMARY" "| / | 0 ✓ (0 · 0 · 0) | 2930.6055 ✗ (3006.9885 · 2930.6055 · 2925.5882) |"
expect_has  "timing points to §F, D0"             "$SUMMARY" "performance-measurement.md\` §F, D0."
expect      "every URL is a row in both tables"   "$(printf '%s\n' "$SUMMARY" | grep -c '^| /')" 16
expect_lacks "no warning"                         "$STDOUT" "::warning::"

echo "all-pass: the failing rows deleted"
render "$ALLPASS"
expect      "exit 0"                         "$RC" 0
expect_lacks "no failure mark in a cell"          "$(cells "$SUMMARY")" "✗"
expect_lacks "a deleted assertion leaves no column" "$SUMMARY" "largest-contentful-paint"
expect_has  "the timing table keeps its other rows" "$SUMMARY" "| / | 0 ✓ (0 · 0 · 0) | 166 ✓ (257 · 82.5 · 166) |"
expect_lacks "no warning"                         "$STDOUT" "::warning::"

echo "warn level: a failure at warn renders ⚠, never ✗"
render "$WARN"
expect      "exit 0"                         "$RC" 0
expect_has  "the warn failure"                    "$SUMMARY" "| / | 166 ⚠ (257 · 82.5 · 166) |"
expect_has  "a pass beside it"                    "$SUMMARY" "| /matchning | 92 ✓"
expect_lacks "no error mark in a cell"            "$(cells "$SUMMARY")" "✗"
expect_has  "no byte rows is said, not hidden"    "$SUMMARY" "No \`resource-summary\` assertion in the results."

echo "a row not evaluated renders –, and the columns keep their order"
jq 'del(.[0])' "$MIXED" > "$TMP/one-missing.json"
render "$TMP/one-missing.json"
expect_has  "the missing cell"                    "$SUMMARY" "| / | 0 ✓ (0 · 0 · 0) | – | 166 ✓"
expect_has  "the same timing header as the full file" "$SUMMARY" "| URL | cumulative-layout-shift <= 0.1 | largest-contentful-paint <= 2500 | total-blocking-time <= 200 |"

echo "missing file"
render "$TMP/does-not-exist.json"
expect      "exit 0"                         "$RC" 0
expect_has  "a warning"                           "$STDOUT" "::warning::Lighthouse summary: no assertion results"
expect_has  "a visible line in the summary"       "$SUMMARY" "**Lighthouse:** no assertion results"
expect      "no table"                            "$(rows "$SUMMARY" URL)" 0

echo "empty array"
printf '[]\n' > "$TMP/empty.json"
render "$TMP/empty.json"
expect      "exit 0"                         "$RC" 0
expect_has  "a warning"                           "$STDOUT" "::warning::"
expect_has  "a visible line in the summary"       "$SUMMARY" "holds no assertion results"
expect      "no table"                            "$(rows "$SUMMARY" URL)" 0

echo "malformed: the mixed file truncated"
head -c 2000 "$MIXED" > "$TMP/truncated.json"
render "$TMP/truncated.json"
expect      "exit 0"                         "$RC" 0
expect_has  "a warning"                           "$STDOUT" "::warning::"
expect_has  "a visible line in the summary"       "$SUMMARY" "is not a list of lhci assertion results"
expect      "no table"                            "$(rows "$SUMMARY" URL)" 0

echo "malformed: one row without its actual value"
jq 'del(.[0].actual)' "$MIXED" > "$TMP/no-actual.json"
render "$TMP/no-actual.json"
expect_has  "a warning"                           "$STDOUT" "::warning::"
expect      "no table"                            "$(rows "$SUMMARY" URL)" 0

echo "the summary is appended to, never replaced"
printf 'earlier step\n' > "$TMP/summary.md"
STDOUT=$(bash "$SUT" "$MIXED" "$TMP/summary.md" 2>&1); SUMMARY=$(cat "$TMP/summary.md")
expect      "the earlier line stays first"        "$(printf '%s\n' "$SUMMARY" | head -1)" "earlier step"

echo "usage"
bash "$SUT" "$MIXED" >/dev/null 2>&1; RC=$?
expect      "one argument exits 2"                "$RC" 2

echo
echo "lighthouse-summary: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ]
