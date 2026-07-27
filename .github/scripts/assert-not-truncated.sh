#!/usr/bin/env bash
#
# assert-not-truncated.sh -- did this API listing come back at its page ceiling,
# and might it therefore be missing entries?
#
# Usage:  bash assert-not-truncated.sh <json-file> <limit>
#
#   Exit 0  the list is shorter than the ceiling, so nothing was cut off.
#   Exit 1  the list is AT or above the ceiling, or could not be read at all.
#   Stderr  a reason, always.
#
# WHY IT EXISTS. `gh pr list --limit N` exits 0 on a truncated list, and a short
# list is indistinguishable from a complete one by content alone. The consumer
# (`select-deletable-branches.sh`) therefore cannot detect truncation, and says
# so in its own header -- which leaves the assertion to the caller, at the only
# place that knows a ceiling was asked for.
#
# WHY IT IS ITS OWN FILE AND NOT A FEW LINES OF YAML. It was seven lines of YAML,
# duplicated across two workflow steps, and the two copies had ALREADY DRIFTED in
# the commit that created them -- one carried the fail-open explanation and the
# remedy, the other had lost both. Duplicated prose in a guard rots in exactly
# the direction that makes the guard less useful when it finally fires. Extracted
# so there is one message, and so the logic lands in the blocking `scripts` job
# with fixtures instead of being the only untested guard in the sweep.
#
# THE ASYMMETRY IS THE REASON THE MESSAGE IS SHOUTY. Truncating the MERGED list
# is fail-safe: branches whose PR fell off the end look like `no-merged-pr` and
# are skipped. Truncating the OPEN list is fail-OPEN: both open-PR guards
# dissolve for the PRs that fell off, so a stacked base becomes deletable. Same
# ceiling, opposite consequences -- so the assertion refuses on either rather
# than trying to be clever about which list it was handed.

set -euo pipefail

fail() {
  echo "assert-not-truncated: $1" >&2
  exit 1
}

[ "$#" -eq 2 ] || fail "expected 2 arguments (<json-file> <limit>), got $#"

readonly FILE=$1
readonly LIMIT=$2

[ -f "$FILE" ] || fail "input file not found: $FILE"
[ -r "$FILE" ] || fail "input file not readable: $FILE"

case "$LIMIT" in
  '' | *[!0-9]*) fail "limit must be a positive integer, got: '$LIMIT'" ;;
esac
[ "$LIMIT" -gt 0 ] || fail "limit must be greater than zero"

command -v jq >/dev/null 2>&1 || fail "jq is required"

# A non-array is not a short list, it is an unreadable one -- and both must
# refuse. `gh` writes nothing to stdout when it fails, so an empty file is the
# realistic shape of a failed fetch and must not read as "zero entries, fine".
# ONE document, not a stream. `jq -e 'type == "array"'` evaluates PER document
# and takes its exit code from the LAST one, so the two-document stream
# `[...12 items...] []` passes the type check; `jq length` then prints two lines,
# `$n` becomes multi-line, and `[ "$n" -ge "$LIMIT" ]` returns 2 -- which `if`
# reads as FALSE. The guard would announce "not truncated" about a list at its
# ceiling. That is fail-OPEN through precisely the shape the sweep's verify step
# documents about `grep`'s exit 2, so it is refused here rather than relied upon
# to be unreachable.
docs=$(jq -s 'length' "$FILE" 2>/dev/null) || fail "cannot parse $FILE as JSON"
[ "$docs" = "1" ] || fail "$FILE is not a single JSON document (found $docs) -- refusing to decide"

jq -e 'type == "array"' "$FILE" >/dev/null 2>&1 || fail "not a JSON array: $FILE"

n=$(jq length "$FILE") || fail "cannot count entries in $FILE"

# Decide on the VALUE, never on `[`'s exit code: a non-numeric operand makes `[`
# return 2, and `if` cannot tell 2 from "false".
case "$n" in
  '' | *[!0-9]*) fail "cannot read a count from $FILE (got: $n)" ;;
esac

if [ "$n" -ge "$LIMIT" ]; then
  fail "$FILE returned $n entries against a ceiling of $LIMIT -- the list may be truncated. Refusing to decide: one of the two directions (the OPEN-PR list) is fail-OPEN, and this guard cannot tell which file it was handed apart from its name. See this file's header. Raise the ceiling."
fi

echo "assert-not-truncated: $FILE has $n entries, ceiling $LIMIT -- not truncated." >&2
exit 0
