#!/usr/bin/env bash
#
# Fixture tests for select-deletable-branches.sh.
#
# Run:  bash .github/scripts/select-deletable-branches.test.sh
#
# WHY THIS FILE EXISTS. The script under test authorises an IRREVERSIBLE remote
# branch deletion. Untested logic inside a destructive sweep is an assertion
# that cannot fail, which is the defect class CLAUDE.md §5 `Tests:` legislates
# against -- and `is-pure-base-merge.sh` set the precedent in this same
# directory: a bash predicate that a merge control depends on gets fixtures and
# a blocking `ci` job.
#
# WHY PURE DATA AND NOT A LIVE REPOSITORY. The script deliberately takes API
# output as FILES rather than calling an API, so the decision is separable from
# the fetch. That is what lets these cases run with no network, no repository
# and no credentials, and it is why the workflow keeps the fetch (fail-loud) and
# the deletion (state-verified) outside the script.
#
# Each case names the SHAPE it builds. The shapes are the argument: they are the
# ways a branch can look on a repository that merges through an app, stacks pull
# requests, and takes contributions from forks.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/select-deletable-branches.sh"
[ -f "$SUT" ] || { echo "missing script under test: $SUT" >&2; exit 1; }

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

readonly OWNER=acme
readonly DEFAULT=main

failures=0
cases=0

# ---------------------------------------------------------------------------
# Harness
# ---------------------------------------------------------------------------

# run <name> <merged.json body> <open.json body> <branches.tsv body>
#   -> sets REPLY_OUT / REPLY_RC
run() {
  local name=$1 merged=$2 open=$3 branches=$4
  local dir="$TMPROOT/$name"
  mkdir -p "$dir"
  printf '%s' "$merged" >"$dir/merged.json"
  printf '%s' "$open" >"$dir/open.json"
  printf '%s' "$branches" >"$dir/branches.tsv"
  set +e
  REPLY_OUT=$(bash "$SUT" "$dir/merged.json" "$dir/open.json" "$dir/branches.tsv" "$DEFAULT" "$OWNER" 2>/dev/null)
  REPLY_RC=$?
  set -e
}

expect() { # expect <case name> <expected stdout> <expected rc>
  local name=$1 want_out=$2 want_rc=$3
  cases=$((cases + 1))
  if [ "$REPLY_RC" != "$want_rc" ]; then
    echo "FAIL [$name]: exit $REPLY_RC, want $want_rc" >&2
    failures=$((failures + 1))
    return
  fi
  if [ "$REPLY_OUT" != "$want_out" ]; then
    echo "FAIL [$name]: stdout mismatch" >&2
    echo "  want: $(printf '%q' "$want_out")" >&2
    echo "  got : $(printf '%q' "$REPLY_OUT")" >&2
    failures=$((failures + 1))
    return
  fi
  echo "ok   [$name]"
}

# A merged PR by this repo's owner.
mine() { printf '{"number":%s,"headRefName":"%s","headRepositoryOwner":{"login":"%s"}}' "$1" "$2" "$OWNER"; }
# A merged PR whose head lives in a fork.
theirs() { printf '{"number":%s,"headRefName":"%s","headRepositoryOwner":{"login":"outsider"}}' "$1" "$2"; }
# An open PR.
openpr() { printf '{"number":%s,"headRefName":"%s","baseRefName":"%s"}' "$1" "$2" "$3"; }

# ---------------------------------------------------------------------------
# 1. The happy path: a branch whose PR merged, nothing else claims it.
# ---------------------------------------------------------------------------
run happy "[$(mine 10 'fix/done')]" '[]' $'fix/done\tfalse\n'
expect "merged head is deleted" $'delete\tfix/done\tmerged-pr-#10' 0

# ---------------------------------------------------------------------------
# 2. Work in progress. No PR has ever been opened from this branch. This is the
#    case #725's own text protects: a branch is not garbage for lacking a PR.
# ---------------------------------------------------------------------------
run wip '[]' '[]' $'feat/in-progress\tfalse\n'
expect "branch with no merged PR survives" $'skip\tfeat/in-progress\tno-merged-pr' 0

# ---------------------------------------------------------------------------
# 3. The default branch. Guarded by name even though `main` is also protected --
#    two independent reasons, because either one alone can be misconfigured.
# ---------------------------------------------------------------------------
run trunk "[$(mine 11 'main')]" '[]' $'main\tfalse\n'
expect "default branch is never deleted" $'skip\tmain\tdefault-branch' 0

# ---------------------------------------------------------------------------
# 4. Branch protection is somebody's stated intent.
# ---------------------------------------------------------------------------
run protected "[$(mine 12 'release/v1')]" '[]' $'release/v1\ttrue\n'
expect "protected branch is never deleted" $'skip\trelease/v1\tprotected' 0

# ---------------------------------------------------------------------------
# 4b. Unknown protection state. A missing column must read as protected, not as
#     unprotected: "the API did not say" may not authorise a delete.
# ---------------------------------------------------------------------------
run unknownprot "[$(mine 13 'fix/done')]" '[]' $'fix/done\n'
expect "missing protected column reads as protected" $'skip\tfix/done\tprotected' 0

# ---------------------------------------------------------------------------
# 5. THE STACKED PR. `fix/parent` merged, but `fix/child` is still open against
#    it. Deleting the parent retargets the child silently (GitHub, 2020-05-19),
#    moving the merge base under a review that already happened.
# ---------------------------------------------------------------------------
run stacked "[$(mine 14 'fix/parent')]" "[$(openpr 15 'fix/child' 'fix/parent')]" \
  $'fix/parent\tfalse\n'
expect "base of an open PR survives" $'skip\tfix/parent\tbase-of-open-pr-#15' 0

# ---------------------------------------------------------------------------
# 6. Branch reuse: the same branch owns a merged PR AND a newer open one. The
#    open PR wins -- deleting the branch would destroy unmerged work.
# ---------------------------------------------------------------------------
run reused "[$(mine 16 'fix/recycled')]" "[$(openpr 17 'fix/recycled' 'main')]" \
  $'fix/recycled\tfalse\n'
expect "head of an open PR survives" $'skip\tfix/recycled\thead-of-open-pr-#17' 0

# ---------------------------------------------------------------------------
# 7. NAME COLLISION WITH A FORK. A contributor's merged PR came from their own
#    `fix/done`. Ours is a different branch that merely shares the name, so
#    their PR must not authorise deleting it.
# ---------------------------------------------------------------------------
run forkonly "[$(theirs 18 'fix/done')]" '[]' $'fix/done\tfalse\n'
expect "a fork's merged head does not authorise deleting ours" \
  $'skip\tfix/done\tfork-head-only' 0

# ---------------------------------------------------------------------------
# 7b. Deleted head repository. `headRepositoryOwner` is null; it is not this
#     owner, so it authorises nothing.
# ---------------------------------------------------------------------------
run nullowner '[{"number":19,"headRefName":"fix/done","headRepositoryOwner":null}]' '[]' \
  $'fix/done\tfalse\n'
expect "null head-repository owner authorises nothing" \
  $'skip\tfix/done\tfork-head-only' 0

# ---------------------------------------------------------------------------
# 8. Mixed input. The partition must be exact and the order must follow the
#    branches file, so the log reads in a stable order.
# ---------------------------------------------------------------------------
run mixed \
  "[$(mine 20 'fix/a'),$(mine 21 'fix/b'),$(mine 22 'fix/parent')]" \
  "[$(openpr 23 'feat/live' 'main'),$(openpr 24 'feat/child' 'fix/parent')]" \
  $'fix/a\tfalse\nfeat/live\tfalse\nfix/parent\tfalse\nmain\tfalse\nfix/b\tfalse\nfeat/orphan\tfalse\n'
expect "mixed input partitions exactly, in file order" \
  "$(printf 'delete\tfix/a\tmerged-pr-#20\nskip\tfeat/live\thead-of-open-pr-#23\nskip\tfix/parent\tbase-of-open-pr-#24\nskip\tmain\tdefault-branch\ndelete\tfix/b\tmerged-pr-#21\nskip\tfeat/orphan\tno-merged-pr')" 0

# ---------------------------------------------------------------------------
# 9. Branch names containing slashes and dots -- the house's actual convention
#    (`<type>/<short-slug>`, CLAUDE.md §6) -- must not word-split.
# ---------------------------------------------------------------------------
run slashes "[$(mine 25 'chore/v1.2.x/re-sync')]" '[]' $'chore/v1.2.x/re-sync\tfalse\n'
expect "slashes and dots survive" $'delete\tchore/v1.2.x/re-sync\tmerged-pr-#25' 0

# ---------------------------------------------------------------------------
# 10. No trailing newline on the last line. Without the read-loop guard the last
#     branch is dropped silently -- a safe verdict reached by a wrong mechanism,
#     and the log would not say it happened.
# ---------------------------------------------------------------------------
run notrailing "[$(mine 26 'fix/last')]" '[]' $'fix/first\tfalse\nfix/last\tfalse'
expect "final line without newline is still decided" \
  "$(printf 'skip\tfix/first\tno-merged-pr\ndelete\tfix/last\tmerged-pr-#26')" 0

# ---------------------------------------------------------------------------
# 11. Nothing to do. Empty is a legitimate answer, not an error.
# ---------------------------------------------------------------------------
run empty '[]' '[]' ''
expect "no branches is a clean no-op" '' 0

# ---------------------------------------------------------------------------
# 12. THE FAIL-CLOSED CASES. Every one must exit 1 with EMPTY stdout: an empty
#     stdout is what makes "delete nothing" the default, so a failure that still
#     printed verdicts would be the whole design defeated.
# ---------------------------------------------------------------------------
run malformed_merged 'not json at all' '[]' $'fix/done\tfalse\n'
expect "malformed merged.json decides nothing" '' 1

run malformed_open "[$(mine 27 'fix/done')]" '{"message":"Bad credentials"}' $'fix/done\tfalse\n'
expect "an API error object is not an empty open-PR list" '' 1

run object_not_array '{"number":28}' '[]' $'fix/done\tfalse\n'
expect "a JSON object is not a JSON array" '' 1

# AN EMPTY FILE IS THE SHAPE A FAILED FETCH ACTUALLY LEAVES. `gh` writes its
# errors to stderr and NOTHING to stdout, so `gh pr list ... >open.json` on a
# rate limit, an expired token or a network fault leaves a zero-byte file --
# not an error object. That is the realistic input, and it is the dangerous
# one: an empty open-PR list reads as "no open PRs", which silently dissolves
# BOTH open-PR guards at once. The shape below is built so the difference is
# visible rather than incidental -- `fix/parent` is a merged head, so without
# the array check it would be DELETED while an open PR is stacked on it.
#
# These two cases were added because mutation testing found the gap: deleting
# the array validation left the whole suite green, since every other failure
# fixture happened to be caught by jq erroring on a non-iterable value instead.
run empty_open "[$(mine 29 'fix/parent')]" '' $'fix/parent\tfalse\n'
expect "an empty open-PR file is not an empty open-PR list" '' 1

run empty_merged '' '[]' $'fix/parent\tfalse\n'
expect "an empty merged-PR file decides nothing" '' 1

# A truncated merged.json is NOT an error -- it cannot be detected from the
# content, and it degrades to `no-merged-pr`, which skips. Pinned so the safe
# direction is a decision this file made rather than an accident.
run truncated '[]' '[]' $'fix/done\tfalse\n'
expect "truncated merged input degrades to skip, not to delete" \
  $'skip\tfix/done\tno-merged-pr' 0

# Missing files, and the argument count itself.
cases=$((cases + 1))
set +e
out=$(bash "$SUT" "$TMPROOT/nope.json" "$TMPROOT/nope2.json" "$TMPROOT/nope3.tsv" "$DEFAULT" "$OWNER" 2>/dev/null)
rc=$?
set -e
if [ "$rc" = 1 ] && [ -z "$out" ]; then echo "ok   [missing input files decide nothing]"; else
  echo "FAIL [missing input files decide nothing]: rc=$rc out=$(printf '%q' "$out")" >&2
  failures=$((failures + 1))
fi

cases=$((cases + 1))
set +e
out=$(bash "$SUT" one two 2>/dev/null)
rc=$?
set -e
if [ "$rc" = 1 ] && [ -z "$out" ]; then echo "ok   [wrong argument count decides nothing]"; else
  echo "FAIL [wrong argument count decides nothing]: rc=$rc out=$(printf '%q' "$out")" >&2
  failures=$((failures + 1))
fi

# An empty default-branch argument would make the `default-branch` guard match
# nothing, so it is rejected rather than tolerated.
cases=$((cases + 1))
dir="$TMPROOT/emptydefault"; mkdir -p "$dir"
printf '[]' >"$dir/merged.json"; printf '[]' >"$dir/open.json"; printf 'main\tfalse\n' >"$dir/branches.tsv"
set +e
out=$(bash "$SUT" "$dir/merged.json" "$dir/open.json" "$dir/branches.tsv" "" "$OWNER" 2>/dev/null)
rc=$?
set -e
if [ "$rc" = 1 ] && [ -z "$out" ]; then echo "ok   [empty default branch decides nothing]"; else
  echo "FAIL [empty default branch decides nothing]: rc=$rc out=$(printf '%q' "$out")" >&2
  failures=$((failures + 1))
fi

# ---------------------------------------------------------------------------
echo
if [ "$failures" -eq 0 ]; then
  echo "select-deletable-branches: $cases/$cases cases passed"
else
  echo "select-deletable-branches: $failures of $cases cases FAILED" >&2
  exit 1
fi
