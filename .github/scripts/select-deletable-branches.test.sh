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
# THE FIXTURES MUST BE SHAPES PRODUCTION CAN EMIT (CLAUDE.md §5 `Tests:`). The
# inputs here are hand-built JSON/TSV, which is legitimate ONLY because each one
# is a shape the real producer does emit -- `gh pr list --json ...` and
# `jq '[.name,(.protected|tostring),.commit.sha]|@tsv'`. Where a fixture uses a
# shape production CANNOT emit, it says so and names the actor; see
# `unknownprot_unreachable`.
#
# Each case names the SHAPE it builds. The shapes are the argument: they are the
# ways a branch can look on a repository that merges through an app, stacks pull
# requests, reuses branches, and takes contributions from forks.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/select-deletable-branches.sh"
[ -f "$SUT" ] || { echo "missing script under test: $SUT" >&2; exit 1; }

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

readonly OWNER=acme
readonly REPO=widget
readonly DEFAULT=main
# A stable stand-in for a commit id. The script compares for equality only, so
# the value is opaque -- but it must LOOK like what the API returns, so nobody
# reads these fixtures as testing a parser that does not exist.
readonly SHA1=1111111111111111111111111111111111111111
readonly SHA2=2222222222222222222222222222222222222222

failures=0
cases=0

# ---------------------------------------------------------------------------
# Harness
# ---------------------------------------------------------------------------

run() { # run <name> <merged.json> <open.json> <branches.tsv>
  local name=$1 merged=$2 open=$3 branches=$4
  local dir="$TMPROOT/$name"
  mkdir -p "$dir"
  printf '%s' "$merged" >"$dir/merged.json"
  printf '%s' "$open" >"$dir/open.json"
  printf '%s' "$branches" >"$dir/branches.tsv"
  set +e
  REPLY_OUT=$(bash "$SUT" "$dir/merged.json" "$dir/open.json" "$dir/branches.tsv" \
    "$DEFAULT" "$OWNER" "$REPO" 2>/dev/null)
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

# A merged PR whose head is in THIS repository.
mine() { # mine <number> <headRef> <headOid>
  printf '{"number":%s,"headRefName":"%s","headRefOid":"%s","headRepositoryOwner":{"login":"%s"},"headRepository":{"name":"%s"}}' \
    "$1" "$2" "$3" "$OWNER" "$REPO"
}
# A merged PR whose head lives in a fork (different owner).
theirs() {
  printf '{"number":%s,"headRefName":"%s","headRefOid":"%s","headRepositoryOwner":{"login":"outsider"},"headRepository":{"name":"%s"}}' \
    "$1" "$2" "$3" "$REPO"
}
# A merged PR from a SIBLING repository under the SAME owner.
sibling() {
  printf '{"number":%s,"headRefName":"%s","headRefOid":"%s","headRepositoryOwner":{"login":"%s"},"headRepository":{"name":"other-repo"}}' \
    "$1" "$2" "$3" "$OWNER"
}
openpr() { printf '{"number":%s,"headRefName":"%s","baseRefName":"%s"}' "$1" "$2" "$3"; }

# ---------------------------------------------------------------------------
# 1. The happy path: PR merged, tip still the merged commit, nothing else claims it.
# ---------------------------------------------------------------------------
run happy "[$(mine 10 'fix/done' "$SHA1")]" '[]' "$(printf 'fix/done\tfalse\t%s\n' "$SHA1")"
expect "merged head at the merged commit is deleted" $'delete\tfix/done\tmerged-pr-#10' 0

# ---------------------------------------------------------------------------
# 2. Work in progress. No PR has ever been opened from this branch. This is the
#    case #725's own text protects: a branch is not garbage for lacking a PR.
# ---------------------------------------------------------------------------
run wip '[]' '[]' "$(printf 'feat/in-progress\tfalse\t%s\n' "$SHA1")"
expect "branch with no merged PR survives" $'skip\tfeat/in-progress\tno-merged-pr' 0

# ---------------------------------------------------------------------------
# 3. THE REUSED BRANCH. PR merged at SHA1; someone then pushed new work and has
#    not opened the next PR yet, so the tip is SHA2. Deleting it would destroy
#    commits that "Restore branch" cannot bring back -- GitHub would restore the
#    MERGED commit. This is the only path to unrecoverable loss in the design.
# ---------------------------------------------------------------------------
run reused_tip "[$(mine 11 'feat/reused' "$SHA1")]" '[]' \
  "$(printf 'feat/reused\tfalse\t%s\n' "$SHA2")"
expect "branch reused after its PR merged survives" \
  $'skip\tfeat/reused\ttip-moved-since-merge-#11' 0

# 3b. An unknown tip is not permission either.
run unknown_tip "[$(mine 12 'fix/done' "$SHA1")]" '[]' $'fix/done\tfalse\t\n'
expect "unknown tip is not permission" $'skip\tfix/done\ttip-moved-since-merge-#12' 0

# ---------------------------------------------------------------------------
# 4. The default branch, guarded by name-from-API even though it is also
#    protected -- two independent reasons, because either can be misconfigured.
# ---------------------------------------------------------------------------
run trunk "[$(mine 13 'main' "$SHA1")]" '[]' "$(printf 'main\tfalse\t%s\n' "$SHA1")"
expect "default branch is never deleted" $'skip\tmain\tdefault-branch' 0

# ---------------------------------------------------------------------------
# 5. Branch protection is somebody's stated intent.
# ---------------------------------------------------------------------------
run protected "[$(mine 14 'release/v1' "$SHA1")]" '[]' \
  "$(printf 'release/v1\ttrue\t%s\n' "$SHA1")"
expect "protected branch is never deleted" $'skip\trelease/v1\tprotected' 0

# 5b. THE SHAPE PRODUCTION ACTUALLY EMITS when the field is absent. `jq`'s
#     `(.protected|tostring)` renders a missing field as the STRING "null" --
#     never as a missing column. Measured 2026-07-27:
#     `[{"name":"b"}] | .[] | [.name,(.protected|tostring)] | @tsv` -> "b<TAB>null".
run nullprot "[$(mine 15 'fix/done' "$SHA1")]" '[]' "$(printf 'fix/done\tnull\t%s\n' "$SHA1")"
expect "a null protected flag reads as protected" $'skip\tfix/done\tprotected' 0

# 5c. DECLARED UNREACHABLE (CLAUDE.md §5 `Tests:`). A one-column row is a shape
#     NO producer in this repository emits -- the jq expression above always
#     writes every column. There is therefore no actor to name, so this case
#     asserts only that the read side DEGRADES SAFELY if the invariant were ever
#     broken by a future caller; it does not claim production can reach it. The
#     producible sibling that carries the real assertion is `nullprot` above.
run unknownprot_unreachable "[$(mine 16 'fix/done' "$SHA1")]" '[]' $'fix/done\n'
expect "unreachable: a truncated row degrades to protected" $'skip\tfix/done\tprotected' 0

# ---------------------------------------------------------------------------
# 6. THE STACKED PR. `fix/parent` merged, but `fix/child` is still open against
#    it. Deleting the parent retargets the child silently (GitHub, 2020-05-19),
#    moving the merge base under a review that already happened.
# ---------------------------------------------------------------------------
run stacked "[$(mine 17 'fix/parent' "$SHA1")]" "[$(openpr 18 'fix/child' 'fix/parent')]" \
  "$(printf 'fix/parent\tfalse\t%s\n' "$SHA1")"
expect "base of an open PR survives" $'skip\tfix/parent\tbase-of-open-pr-#18' 0

# ---------------------------------------------------------------------------
# 7. Branch reuse WITH a PR open. The open PR wins.
# ---------------------------------------------------------------------------
run reused_pr "[$(mine 19 'fix/recycled' "$SHA1")]" "[$(openpr 20 'fix/recycled' 'main')]" \
  "$(printf 'fix/recycled\tfalse\t%s\n' "$SHA1")"
expect "head of an open PR survives" $'skip\tfix/recycled\thead-of-open-pr-#20' 0

# ---------------------------------------------------------------------------
# 8. NAME COLLISION WITH A FORK, and with a SIBLING REPOSITORY. Neither may
#    authorise deleting ours.
# ---------------------------------------------------------------------------
run forkonly "[$(theirs 21 'fix/done' "$SHA1")]" '[]' "$(printf 'fix/done\tfalse\t%s\n' "$SHA1")"
expect "a fork's merged head does not authorise deleting ours" \
  $'skip\tfix/done\tfork-head-only' 0

run siblingrepo "[$(sibling 22 'fix/done' "$SHA1")]" '[]' \
  "$(printf 'fix/done\tfalse\t%s\n' "$SHA1")"
expect "a sibling repo's merged head does not authorise deleting ours" \
  $'skip\tfix/done\tfork-head-only' 0

run nullowner "[{\"number\":23,\"headRefName\":\"fix/done\",\"headRefOid\":\"$SHA1\",\"headRepositoryOwner\":null,\"headRepository\":null}]" '[]' \
  "$(printf 'fix/done\tfalse\t%s\n' "$SHA1")"
expect "null head-repository owner authorises nothing" \
  $'skip\tfix/done\tfork-head-only' 0

# ---------------------------------------------------------------------------
# 9. THE SUBSTRING TRAP -- the fixture that kills `$1 == key` -> `$1 ~ key`.
#    awk's `~` is a regex match, hence a SUBSTRING match: the live branch
#    `feat/x` would find the merged row for `feat/x-2` and be DELETED. Mutation
#    testing found this survived the entire suite; this case is why it no longer
#    does. Both verdicts matter -- the merged one must still go.
# ---------------------------------------------------------------------------
run substring "[$(mine 24 'feat/x-2' "$SHA1")]" '[]' \
  "$(printf 'feat/x-2\tfalse\t%s\nfeat/x\tfalse\t%s\n' "$SHA1" "$SHA2")"
expect "a live branch is not matched by a longer merged name" \
  "$(printf 'delete\tfeat/x-2\tmerged-pr-#24\nskip\tfeat/x\tno-merged-pr')" 0

# ---------------------------------------------------------------------------
# 10. TWO MERGED PRs ON ONE HEAD (branch reused and merged twice). Without the
#     `exit` in the lookup, awk emits both rows, the reason column swallows a
#     newline, and the branch drops out of BOTH downstream lists. First match
#     wins and the input is newest-first, so the newest PR is the referent.
# ---------------------------------------------------------------------------
run double_merge "[$(mine 25 'fix/twice' "$SHA2"),$(mine 26 'fix/twice' "$SHA1")]" '[]' \
  "$(printf 'fix/twice\tfalse\t%s\n' "$SHA2")"
expect "a head with two merged PRs yields exactly one verdict" \
  $'delete\tfix/twice\tmerged-pr-#25' 0

# ---------------------------------------------------------------------------
# 11. UNSUPPORTED NAMES. Git permits `#` and `%` in ref names; a REST path does
#     not survive them -- `feat/x#main` would send DELETE to `.../heads/feat/x`,
#     destroying a branch with no merged PR and therefore no Restore button.
#     Refused in the tested layer rather than escaped in YAML.
# ---------------------------------------------------------------------------
run hashname "[$(mine 27 'feat/x#main' "$SHA1")]" '[]' \
  "$(printf 'feat/x#main\tfalse\t%s\n' "$SHA1")"
expect "a ref name with # is refused, not escaped downstream" \
  $'skip\tfeat/x#main\tunsupported-name' 0

run percentname "[$(mine 28 'feat/a%2fb' "$SHA1")]" '[]' \
  "$(printf 'feat/a%%2fb\tfalse\t%s\n' "$SHA1")"
expect "a ref name with % is refused" $'skip\tfeat/a%2fb\tunsupported-name' 0

run dashname "[$(mine 29 '-dashed' "$SHA1")]" '[]' "$(printf -- '-dashed\tfalse\t%s\n' "$SHA1")"
expect "a leading dash is refused so it cannot read as an option" \
  $'skip\t-dashed\tunsupported-name' 0

# 11a. DOT-SEGMENTS. Every character here is in the allow-list, so only the
#      explicit `*..*` clause refuses it. The target shape is the dangerous one:
#      `git/refs/heads/feat/../../tags/v1` resolves to `git/refs/tags/v1`, which
#      would send a DELETE at a TAG. `git check-ref-format` also rejects `..`,
#      so this is unreachable today -- but that is git's rule, not this script's,
#      and a guard that depends on an unnamed third party is a guard that
#      disappears when somebody widens the character class.
run dotsegments "[$(mine 42 'feat/../../tags/v1' "$SHA1")]" '[]' \
  "$(printf 'feat/../../tags/v1\tfalse\t%s\n' "$SHA1")"
expect "a path-traversal ref name is refused by us, not only by git" \
  $'skip\tfeat/../../tags/v1\tunsupported-name' 0

# 11b. The house's real naming convention must survive all of the above.
run slashes "[$(mine 30 'chore/v1.2.x/re-sync' "$SHA1")]" '[]' \
  "$(printf 'chore/v1.2.x/re-sync\tfalse\t%s\n' "$SHA1")"
expect "slashes, dots and dashes survive" \
  $'delete\tchore/v1.2.x/re-sync\tmerged-pr-#30' 0

run dependabotname "[$(mine 31 'dependabot/npm_and_yarn/web/next-16.2.11' "$SHA1")]" '[]' \
  "$(printf 'dependabot/npm_and_yarn/web/next-16.2.11\tfalse\t%s\n' "$SHA1")"
expect "a real dependabot branch name survives" \
  $'delete\tdependabot/npm_and_yarn/web/next-16.2.11\tmerged-pr-#31' 0

# ---------------------------------------------------------------------------
# 12. Mixed input. The partition must be exact and in file order, so the log
#     reads stably.
# ---------------------------------------------------------------------------
run mixed \
  "[$(mine 32 'fix/a' "$SHA1"),$(mine 33 'fix/b' "$SHA1"),$(mine 34 'fix/parent' "$SHA1")]" \
  "[$(openpr 35 'feat/live' 'main'),$(openpr 36 'feat/child' 'fix/parent')]" \
  "$(printf 'fix/a\tfalse\t%s\nfeat/live\tfalse\t%s\nfix/parent\tfalse\t%s\nmain\tfalse\t%s\nfix/b\tfalse\t%s\nfeat/orphan\tfalse\t%s\n' \
     "$SHA1" "$SHA1" "$SHA1" "$SHA1" "$SHA1" "$SHA1")"
expect "mixed input partitions exactly, in file order" \
  "$(printf 'delete\tfix/a\tmerged-pr-#32\nskip\tfeat/live\thead-of-open-pr-#35\nskip\tfix/parent\tbase-of-open-pr-#36\nskip\tmain\tdefault-branch\ndelete\tfix/b\tmerged-pr-#33\nskip\tfeat/orphan\tno-merged-pr')" 0

# ---------------------------------------------------------------------------
# 13. No trailing newline on the last line. Without the read-loop guard the last
#     branch is dropped silently -- a safe verdict reached by a wrong mechanism.
# ---------------------------------------------------------------------------
run notrailing "[$(mine 37 'fix/last' "$SHA1")]" '[]' \
  "$(printf 'fix/first\tfalse\t%s\nfix/last\tfalse\t%s' "$SHA1" "$SHA1")"
expect "final line without newline is still decided" \
  "$(printf 'skip\tfix/first\tno-merged-pr\ndelete\tfix/last\tmerged-pr-#37')" 0

# ---------------------------------------------------------------------------
# 14. Nothing to do. Empty is a legitimate answer, not an error.
# ---------------------------------------------------------------------------
run empty '[]' '[]' ''
expect "no branches is a clean no-op" '' 0

# ---------------------------------------------------------------------------
# 15. THE FAIL-CLOSED CASES. Every one must exit 1 with EMPTY stdout: an empty
#     stdout is what makes "delete nothing" the default, so a failure that still
#     printed verdicts would be the whole design defeated.
# ---------------------------------------------------------------------------
run malformed_merged 'not json at all' '[]' "$(printf 'fix/done\tfalse\t%s\n' "$SHA1")"
expect "malformed merged.json decides nothing" '' 1

run malformed_open "[$(mine 38 'fix/done' "$SHA1")]" '{"message":"Bad credentials"}' \
  "$(printf 'fix/done\tfalse\t%s\n' "$SHA1")"
expect "an API error object is not an empty open-PR list" '' 1

run object_not_array '{"number":39}' '[]' "$(printf 'fix/done\tfalse\t%s\n' "$SHA1")"
expect "a JSON object is not a JSON array" '' 1

# AN EMPTY FILE IS THE SHAPE A FAILED FETCH ACTUALLY LEAVES. `gh` writes its
# errors to stderr and NOTHING to stdout, so `gh pr list ... >open.json` on a
# rate limit, an expired token or a network fault leaves a zero-byte file --
# not an error object. That is the realistic input, and it is the dangerous
# one: an empty open-PR list reads as "no open PRs", which silently dissolves
# BOTH open-PR guards at once. The shape below is built so the difference is
# visible rather than incidental -- `fix/parent` is a merged head at its merged
# commit, so without the array check it would be DELETED while an open PR is
# stacked on it.
#
# These two cases were added because mutation testing found the gap: deleting
# the array validation left the whole suite green, since every other failure
# fixture happened to be caught by jq erroring on a non-iterable value instead.
run empty_open "[$(mine 40 'fix/parent' "$SHA1")]" '' "$(printf 'fix/parent\tfalse\t%s\n' "$SHA1")"
expect "an empty open-PR file is not an empty open-PR list" '' 1

run empty_merged '' '[]' "$(printf 'fix/parent\tfalse\t%s\n' "$SHA1")"
expect "an empty merged-PR file decides nothing" '' 1

# TRUNCATION IS UNDETECTABLE FROM CONTENT and the two directions are NOT
# symmetric. A short merged.json degrades to `no-merged-pr` (safe); a short
# open.json would dissolve both open-PR guards (FAIL-OPEN). The script cannot
# tell either from a genuinely short list, so the CALLER must assert it did not
# hit its page ceiling. Both directions are pinned here so the asymmetry is a
# decision this file records rather than an accident.
run truncated_merged_safe '[]' '[]' "$(printf 'fix/done\tfalse\t%s\n' "$SHA1")"
expect "a truncated merged list degrades to skip, never to delete" \
  $'skip\tfix/done\tno-merged-pr' 0

run truncated_open_dangerous "[$(mine 41 'fix/parent' "$SHA1")]" '[]' \
  "$(printf 'fix/parent\tfalse\t%s\n' "$SHA1")"
expect "a truncated open list CANNOT be detected here -- the caller must assert it" \
  $'delete\tfix/parent\tmerged-pr-#41' 0

# Missing files, and the argument count itself.
cases=$((cases + 1))
set +e
out=$(bash "$SUT" "$TMPROOT/nope.json" "$TMPROOT/nope2.json" "$TMPROOT/nope3.tsv" \
  "$DEFAULT" "$OWNER" "$REPO" 2>/dev/null)
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

# Empty identity arguments would make the guards that depend on them match
# nothing, so they are rejected rather than tolerated.
for empty_arg in default owner repo; do
  cases=$((cases + 1))
  dir="$TMPROOT/empty-$empty_arg"; mkdir -p "$dir"
  printf '[]' >"$dir/merged.json"; printf '[]' >"$dir/open.json"
  printf 'main\tfalse\t%s\n' "$SHA1" >"$dir/branches.tsv"
  d=$DEFAULT; o=$OWNER; r=$REPO
  case "$empty_arg" in default) d="" ;; owner) o="" ;; repo) r="" ;; esac
  set +e
  out=$(bash "$SUT" "$dir/merged.json" "$dir/open.json" "$dir/branches.tsv" "$d" "$o" "$r" 2>/dev/null)
  rc=$?
  set -e
  if [ "$rc" = 1 ] && [ -z "$out" ]; then echo "ok   [empty $empty_arg decides nothing]"; else
    echo "FAIL [empty $empty_arg decides nothing]: rc=$rc out=$(printf '%q' "$out")" >&2
    failures=$((failures + 1))
  fi
done

# ---------------------------------------------------------------------------
echo
if [ "$failures" -eq 0 ]; then
  echo "select-deletable-branches: $cases/$cases cases passed"
else
  echo "select-deletable-branches: $failures of $cases cases FAILED" >&2
  exit 1
fi
