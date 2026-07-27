#!/usr/bin/env bash
#
# Fixture tests for is-pure-base-merge.sh.
#
# Run:  bash .github/scripts/is-pure-base-merge.test.sh
#
# WHY THIS FILE EXISTS. The predicate it tests decides whether a push may keep a
# PR's review gate armed. Untested YAML inside a merge control is an assertion
# that cannot fail -- the exact defect class #843 legislates against, and #836
# is that class living inside the organ meant to enforce it. If you liked it,
# you should have put a test on it (Winters/Manshreck/Wright 2020, ch. 11).
#
# WHY FIXTURES AND NOT PINNED REAL SHAs. The interesting commits live on feature
# branches, and branch deletion garbage-collects them -- a corpus of real SHAs
# would rot within the week. Every repository below is built from nothing, so
# the tests answer the same way in five years as they do today.
#
# Each case names the SHAPE it builds, because the shapes are the argument: they
# are the ways a `synchronize` event can look. Only a base merge whose tree IS
# the automatic merge's tree may keep the gate -- which is a rule about trees,
# not a position in this list, and cases 1 and 1b are both members of it.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/is-pure-base-merge.sh"
[ -f "$SUT" ] || { echo "missing script under test: $SUT" >&2; exit 1; }

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

# Fixture repositories must not inherit the developer's git config: a global
# `core.autocrlf`, a signing key, or a merge driver would make the fixtures mean
# something different on one machine than another.
export GIT_CONFIG_GLOBAL=/dev/null
export GIT_CONFIG_SYSTEM=/dev/null

# Nor the environment. `git -C <dir>` does NOT override `GIT_DIR`, so running
# this suite from a git hook or any `git` subprocess context would point every
# fixture commit at the HOST repository -- the fixtures would still pass, while
# having tested nothing they claim to.
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_OBJECT_DIRECTORY GIT_ALTERNATE_OBJECT_DIRECTORIES

failures=0
cases=0

# ---------------------------------------------------------------------------
# Harness
# ---------------------------------------------------------------------------

new_repo() { # new_repo <name> -> prints the repo path
  local dir="$TMPROOT/$1"
  mkdir -p "$dir"
  git init -q -b main "$dir"
  git -C "$dir" config user.email fixture@example.invalid
  git -C "$dir" config user.name Fixture
  git -C "$dir" config commit.gpgsign false
  printf 'base line 1\nbase line 2\nbase line 3\n' >"$dir/base.txt"
  git -C "$dir" add -A
  git -C "$dir" commit -qm 'A: root'
  printf '%s' "$dir"
}

commit_in() { # commit_in <repo> <file> <content> <message>
  printf '%s' "$3" >"$1/$2"
  git -C "$1" add -A
  git -C "$1" commit -qm "$4"
}

# Runs the predicate and captures BOTH channels of its contract: the reason
# token on stdout and the exit status. Asserting only one of them would let a
# script that always exits 1 pass every negative case.
run_predicate() { # run_predicate <cwd> [args...] -> sets REASON, STATUS
  local cwd=$1
  shift
  set +e
  REASON=$(cd "$cwd" && bash "$SUT" "$@" 2>/dev/null)
  STATUS=$?
  set -e
}

# A `git` earlier on PATH that hijacks ONE subcommand and delegates the rest.
# This is how the error class gets tested honestly: the failures below cannot be
# provoked from the outside any other way, and a fail-closed default nobody has
# ever seen fail is exactly the assertion-that-cannot-fail this gate exists to
# eliminate.
real_git=$(command -v git)
readonly real_git

new_git_shim() { # new_git_shim <name> <subcommand> <shell action> -> prints dir
  local dir="$TMPROOT/shim-$1"
  mkdir -p "$dir"
  {
    printf '#!/usr/bin/env bash\n'
    printf 'if [ "${1:-}" = %q ]; then %s; fi\n' "$2" "$3"
    printf 'exec %q "$@"\n' "$real_git"
  } >"$dir/git"
  chmod +x "$dir/git"
  printf '%s' "$dir"
}

run_predicate_with_shim() { # run_predicate_with_shim <shim dir> <cwd> [args...]
  local shim=$1 cwd=$2
  shift 2
  set +e
  REASON=$(cd "$cwd" && PATH="$shim:$PATH" bash "$SUT" "$@" 2>/dev/null)
  STATUS=$?
  set -e
}

expect() { # expect <case name> <want reason> <want status>
  cases=$((cases + 1))
  if [ "$REASON" = "$2" ] && [ "$STATUS" -eq "$3" ]; then
    printf 'ok   %s\n' "$1"
  else
    printf 'FAIL %s\n       want: reason=%-25s status=%s\n       got:  reason=%-25s status=%s\n' \
      "$1" "$2" "$3" "${REASON:-<empty>}" "$STATUS"
    failures=$((failures + 1))
  fi
}

# ---------------------------------------------------------------------------
# 1. The only shape that may keep the gate: a clean base merge.
#    main advances; `gh pr update-branch` merges it in; no line of the author's
#    code changes.
# ---------------------------------------------------------------------------
repo=$(new_repo clean-base-merge)
git -C "$repo" checkout -qb feature
commit_in "$repo" feat.txt 'feature work' 'F: feature commit'
before=$(git -C "$repo" rev-parse HEAD)
git -C "$repo" checkout -q main
commit_in "$repo" base.txt 'base line 1
base line 2 moved
base line 3
' 'B: base advances'
git -C "$repo" checkout -q feature
git -C "$repo" merge -q --no-ff -m "Merge branch 'main' into feature" main
after=$(git -C "$repo" rev-parse HEAD)
run_predicate "$repo" "$before" "$after" main
expect 'clean base merge keeps the gate' pure-base-merge 0

# Kept for section 9: the only fixture that reaches the later conditions, so it
# is the one the git-failure shims must run against.
clean_repo=$repo
clean_before=$before
clean_after=$after

# ---------------------------------------------------------------------------
# 1b. The same verdict, on the shape the ruling's own measurement turned on.
#     Case 1 edits DISJOINT files, so merge-ort never performs a line-level
#     automerge. The two real commits that made tree equality win over patch
#     comparison were both OVERLAPPING edits to one file (CLAUDE.md and
#     globals.css) -- and two sessions editing different lines of CLAUDE.md is
#     the most common base merge in this repo. If `git merge-tree` and GitHub's
#     server-side merge ever disagreed on that shape, the predicate would disarm
#     on every base merge and the gate would stop converging, which V16 makes
#     part of the definition of done. Untested, that is a hope.
# ---------------------------------------------------------------------------
repo=$(new_repo overlapping-base-merge)
git -C "$repo" checkout -qb feature
commit_in "$repo" base.txt 'base line 1
base line 2
base line 3 edited by the feature
' 'F: feature edits the last line'
before=$(git -C "$repo" rev-parse HEAD)
git -C "$repo" checkout -q main
commit_in "$repo" base.txt 'base line 1 edited by main
base line 2
base line 3
' 'B: base edits the first line of the same file'
git -C "$repo" checkout -q feature
git -C "$repo" merge -q --no-ff -m "Merge branch 'main' into feature" main
after=$(git -C "$repo" rev-parse HEAD)
# Verify the fixture built the shape it is named after, the way case 2 does.
# Without this, simplifying it back to disjoint files would make it a silent
# duplicate of case 1 and the suite would keep passing -- the merged blob must
# differ from BOTH sides, which is what "a real line-level automerge happened"
# means and is exactly the shape that defeated the patch-comparison predicate.
merged_blob=$(git -C "$repo" rev-parse "$after:base.txt")
if [ "$merged_blob" = "$(git -C "$repo" rev-parse "$before:base.txt")" ] ||
  [ "$merged_blob" = "$(git -C "$repo" rev-parse "main:base.txt")" ]; then
  echo 'fixture broken: case 1b did not produce a line-level merge of base.txt' >&2
  exit 1
fi
run_predicate "$repo" "$before" "$after" main
expect 'line-level automerge of one file keeps the gate' pure-base-merge 0

# ---------------------------------------------------------------------------
# 2. The measured counterexample to the commit-SHAPE predicate (#836 M4, real
#    commit 6ae078eb): two parents, parent 1 is the old head, parent 2 is in the
#    base -- and the automatic merge conflicts, so the tree carries a
#    hand-written resolution no reviewer has seen. Must disarm.
# ---------------------------------------------------------------------------
repo=$(new_repo conflicted-base-merge)
git -C "$repo" checkout -qb feature
commit_in "$repo" base.txt 'base line 1
feature edit
base line 3
' 'F: feature edits the contested line'
before=$(git -C "$repo" rev-parse HEAD)
git -C "$repo" checkout -q main
commit_in "$repo" base.txt 'base line 1
main edit
base line 3
' 'B: base edits the same line'
git -C "$repo" checkout -q feature
if git -C "$repo" merge -q -m "Merge branch 'main' into feature" main; then
  echo 'fixture broken: case 2 expected a merge conflict and did not get one' >&2
  exit 1
fi
commit_in "$repo" base.txt 'base line 1
hand resolved
base line 3
' "Merge branch 'main' into feature"
after=$(git -C "$repo" rev-parse HEAD)
run_predicate "$repo" "$before" "$after" main
expect 'hand-resolved conflict merge disarms' conflicted-base-merge 1

# ---------------------------------------------------------------------------
# 3. An ordinary review-round push. One parent -- the case the gate exists for.
# ---------------------------------------------------------------------------
repo=$(new_repo review-push)
git -C "$repo" checkout -qb feature
commit_in "$repo" feat.txt 'round 1' 'F1'
before=$(git -C "$repo" rev-parse HEAD)
commit_in "$repo" feat.txt 'round 2' 'F2: addresses a review finding'
after=$(git -C "$repo" rev-parse HEAD)
run_predicate "$repo" "$before" "$after" main
expect 'review-round push disarms' review-push 1

# ---------------------------------------------------------------------------
# 4. A merge whose second parent is NOT in the base: someone merged a sibling
#    feature branch in. That is content, not a base update.
# ---------------------------------------------------------------------------
repo=$(new_repo parent2-not-in-base)
git -C "$repo" branch side
git -C "$repo" checkout -qb feature
commit_in "$repo" feat.txt 'feature work' 'F'
before=$(git -C "$repo" rev-parse HEAD)
git -C "$repo" checkout -q side
commit_in "$repo" side.txt 'side work' 'S: never landed on main'
git -C "$repo" checkout -q feature
git -C "$repo" merge -q --no-ff -m 'Merge side into feature' side
after=$(git -C "$repo" rev-parse HEAD)
run_predicate "$repo" "$before" "$after" main
expect 'merge of an unlanded branch disarms' parent2-not-in-base 1

# ---------------------------------------------------------------------------
# 5. History rewritten before the merge: parent 1 is not the head the reviewers
#    answered against. Covers force-push, and also "a commit landed, then the
#    base was merged" -- both push content past the gate.
# ---------------------------------------------------------------------------
repo=$(new_repo parent1-not-previous-head)
git -C "$repo" checkout -qb feature
commit_in "$repo" feat.txt 'feature work' 'F'
before=$(git -C "$repo" rev-parse HEAD)
printf 'feature work, amended' >"$repo/feat.txt"
git -C "$repo" add -A
git -C "$repo" commit -q --amend -m "F': rewritten"
git -C "$repo" checkout -q main
commit_in "$repo" base.txt 'base line 1
base line 2 moved
base line 3
' 'B: base advances'
git -C "$repo" checkout -q feature
git -C "$repo" merge -q --no-ff -m "Merge branch 'main' into feature" main
after=$(git -C "$repo" rev-parse HEAD)
run_predicate "$repo" "$before" "$after" main
expect 'rewritten history before the merge disarms' parent1-not-previous-head 1

# ---------------------------------------------------------------------------
# 6. Octopus merge -- three parents. Not a shape `update-branch` can produce.
# ---------------------------------------------------------------------------
repo=$(new_repo octopus-merge)
git -C "$repo" branch s1
git -C "$repo" branch s2
git -C "$repo" checkout -qb feature
commit_in "$repo" feat.txt 'feature work' 'F'
before=$(git -C "$repo" rev-parse HEAD)
git -C "$repo" checkout -q s1
commit_in "$repo" s1.txt 'one' 'S1'
git -C "$repo" checkout -q s2
commit_in "$repo" s2.txt 'two' 'S2'
git -C "$repo" checkout -q feature
git -C "$repo" merge -q --no-ff -m 'Octopus' s1 s2
after=$(git -C "$repo" rev-parse HEAD)
run_predicate "$repo" "$before" "$after" main
expect 'octopus merge disarms' unexpected-parent-count 1

# ---------------------------------------------------------------------------
# 7. THE ONE THE SHAPE PREDICATE ALSO MISSES BY CONSTRUCTION: a real base merge
#    with an extra edit committed into the same merge commit. Every shape
#    condition holds; only tree equality catches it.
# ---------------------------------------------------------------------------
repo=$(new_repo base-merge-with-content)
git -C "$repo" checkout -qb feature
commit_in "$repo" feat.txt 'feature work' 'F'
before=$(git -C "$repo" rev-parse HEAD)
git -C "$repo" checkout -q main
commit_in "$repo" base.txt 'base line 1
base line 2 moved
base line 3
' 'B: base advances'
git -C "$repo" checkout -q feature
git -C "$repo" merge -q --no-commit --no-ff main
printf 'feature work, plus something nobody reviewed' >"$repo/feat.txt"
git -C "$repo" add -A
git -C "$repo" commit -qm "Merge branch 'main' into feature"
after=$(git -C "$repo" rev-parse HEAD)
run_predicate "$repo" "$before" "$after" main
expect 'base merge carrying an extra edit disarms' base-merge-with-content 1

# ---------------------------------------------------------------------------
# 8. The error class. Every one of these must disarm, because the whole design
#    rests on "if it did not decide, it disarms".
# ---------------------------------------------------------------------------
repo=$(new_repo error-class)
git -C "$repo" checkout -qb feature
commit_in "$repo" feat.txt 'feature work' 'F'
head=$(git -C "$repo" rev-parse HEAD)
missing=0123456789012345678901234567890123456789

run_predicate "$repo"
expect 'no arguments disarms' predicate-error 1

run_predicate "$repo" "$head" "$head"
expect 'too few arguments disarms' predicate-error 1

run_predicate "$repo" '' "$head" main
expect 'empty argument disarms' predicate-error 1

run_predicate "$repo" "$missing" "$head" main
expect 'unreachable before-head disarms' before-unreachable 1

run_predicate "$repo" "$head" "$missing" main
expect 'unreachable after-head disarms' after-unreachable 1

run_predicate "$repo" "$head" "$head" origin/does-not-exist
expect 'unresolvable base disarms' base-unreachable 1

# A failed `actions/checkout` leaves the runner without a repository. The
# predicate must decide, not crash into a green step -- and it must call that
# `predicate-error`, not `before-unreachable`: "there is no repo here" is the
# script failing, not an answer about the rev.
notrepo="$TMPROOT/not-a-repo"
mkdir -p "$notrepo"
run_predicate "$notrepo" "$head" "$head" main
expect 'outside a git repository disarms' predicate-error 1

# ---------------------------------------------------------------------------
# 9. git itself misbehaving. These shims are the reason the design can accept git
#    plumbing inside a merge control at all, so none of them may be left as an
#    assertion: each is provoked for real, from outside the script.
# ---------------------------------------------------------------------------

# `git merge-tree --write-tree` needs git >= 2.38. An older git must be an
# explicit, named refusal -- never a skipped condition that reads as a pass.
shim=$(new_git_shim old-git --version 'echo "git version 2.30.2"; exit 0')
run_predicate_with_shim "$shim" "$clean_repo" "$clean_before" "$clean_after" main
expect 'git older than 2.38 disarms' git-too-old 1

# A version string the parser cannot read must disarm too, and this is not
# pedantry: `[ banana -lt 2 ]` returns status 2, which inside an `if` condition
# is exempt from `set -e`, so the comparison would silently evaluate false and
# the script would sail PAST the version assertion. That is the "assertion that
# skips instead of failing" the ruling forbids by name. Both arms of the parse
# are covered, because a guard on `major` proves nothing about `minor`.
shim=$(new_git_shim unparseable-version --version 'echo "git version banana"; exit 0')
run_predicate_with_shim "$shim" "$clean_repo" "$clean_before" "$clean_after" main
expect 'an unparseable git version disarms' predicate-error 1

shim=$(new_git_shim unparseable-minor --version 'echo "git version 2.x.1"; exit 0')
run_predicate_with_shim "$shim" "$clean_repo" "$clean_before" "$clean_after" main
expect 'an unparseable git minor version disarms' predicate-error 1

# THE FAIL-CLOSED BACKSTOP ITSELF. Every other case is decided by a condition
# the script wrote on purpose; this one is not. A `rev-list` that succeeds while
# printing nothing leaves no parents to `shift`, `set -e` aborts mid-script, and
# the EXIT trap is the only thing between that and a step which reports nothing
# and lets the gate stand. Without this case the trap is the single most
# load-bearing line in the file and never once observed to fire.
shim=$(new_git_shim empty-revlist rev-list 'exit 0')
run_predicate_with_shim "$shim" "$clean_repo" "$clean_before" "$clean_after" main
expect 'an unplanned abort hits the EXIT trap and disarms' predicate-error 1

# A broken `merge-base` must NOT be read as "parent 2 is not in the base". Both
# disarm, but only one of them means this script is broken, and the monitoring
# signal named in the ruling depends on telling them apart.
shim=$(new_git_shim broken-mergebase merge-base 'exit 128')
run_predicate_with_shim "$shim" "$clean_repo" "$clean_before" "$clean_after" main
expect 'a git error in merge-base is not a clean negative' predicate-error 1

# Same separation on the condition that carries the safety: an unusable
# `merge-tree` is an error, not a conflict and not a pass.
shim=$(new_git_shim broken-mergetree merge-tree 'exit 128')
run_predicate_with_shim "$shim" "$clean_repo" "$clean_before" "$clean_after" main
expect 'a git error in merge-tree is not a conflict' predicate-error 1

# A `merge-tree` that succeeds while printing something that is not a tree OID.
# Without the hex guard this reports `base-merge-with-content` -- which disarms,
# so it is safe, but it lands in the token the ruling's monitoring reads as "the
# predicate is too tight". Two error signals collapsed into one is a signal lost.
shim=$(new_git_shim garbage-mergetree merge-tree 'echo "not-a-tree-oid"; exit 0')
run_predicate_with_shim "$shim" "$clean_repo" "$clean_before" "$clean_after" main
expect 'merge-tree output that is not a tree OID disarms' predicate-error 1

# And a `rev-parse` that fails for a reason other than "no such object". The
# script keeps that separate on purpose; nothing observed it until now.
shim=$(new_git_shim broken-revparse rev-parse 'exit 3')
run_predicate_with_shim "$shim" "$clean_repo" "$clean_before" "$clean_after" main
expect 'a git error in rev-parse is not an unreachable rev' predicate-error 1

# ---------------------------------------------------------------------------
printf '\n%d cases, %d failures\n' "$cases" "$failures"
[ "$failures" -eq 0 ] || exit 1
