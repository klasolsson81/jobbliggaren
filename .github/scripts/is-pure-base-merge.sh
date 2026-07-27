#!/usr/bin/env bash
#
# is-pure-base-merge.sh -- did this `synchronize` push contribute content of its
# own, or was it nothing but the branch being brought up to base?
#
# Usage:  is-pure-base-merge.sh <before-rev> <after-rev> <base-rev>
#
#   <before-rev>  the PR head before the push  (github.event.before)
#   <after-rev>   the PR head after the push   (github.event.after)
#   <base-rev>    the PR's base branch         (origin/<base.ref>)
#
#   Exit 0  the push is a pure base merge -- the review gate may stay armed.
#   Exit 1  everything else, including every error and every unhandled case.
#   Stdout  exactly one reason token (table at the bottom of this header).
#
# WHY IT EXISTS. `main` is protected with `strict: true`, so a PR must be
# up-to-base to merge at all, and every sibling that lands puts it BEHIND again.
# Measured 2026-07-27: 30 base merges across 12 branches in five days -- the
# dominant commit shape on feature branches here. Disarming the review gate on
# each of them means the gate never converges, and a gate that never merges does
# not get obeyed; it gets routed around. That is the failure this narrowing
# exists to prevent (#836, CTO ruling V11/V16, 2026-07-27).
#
# THE CONTRACT IS FAIL-CLOSED, AND THAT IS THE ENTIRE ARGUMENT FOR ALLOWING GIT
# PLUMBING INTO A MERGE CONTROL. `exit 0` is written at exactly ONE place in this
# file, after every condition has held. Everything else -- a git error, an unset
# variable, a signal, falling off the end of the script -- lands in the EXIT
# trap, which prints `predicate-error` and exits 1. So a bug in here degrades to
# "disarm always", which is exactly the behaviour that shipped before this script
# existed. The worst case of the narrowing is the baseline it narrows.
#
# WHY TREE EQUALITY AND NOT COMMIT SHAPE. The obvious predicate -- "two parents,
# parent 1 is the old head, parent 2 is already in the base" -- is fail-OPEN, and
# this repo holds the counterexample: `6ae078eb` (2026-07-25) satisfies all three
# conditions and its automatic merge CONFLICTS, so the committed tree carries a
# hand-written resolution no agent has ever seen. Resolving such a conflict on
# the branch is documented house procedure (parallel-sessions.md §8.1), so that
# is a normal path, not an accident. Conditions 1-3 below survive only because
# they make condition 4 meaningful; condition 4 carries the safety, because it
# asks the only question that matters: is this commit's tree exactly what an
# automatic merge would have produced -- which is exactly what `gh pr
# update-branch` would have produced, that being a server-side automatic merge
# which refuses on conflict.
#
# WHAT IT DELIBERATELY DOES NOT CATCH (stated, not hidden):
#   * Semantic drift. The PR's own content is byte-identical, but the base moved
#     under it. Required `ci` re-runs against the new head; agent JUDGEMENT is
#     not re-run. Accepted knowingly -- re-running every mandatory agent on 30
#     base merges a week is the cost this decision refuses.
#   * A textually clean but semantically wrong auto-merge. `ci` is the only net.
#   * A dishonest `agents-done`. Nothing here verifies the agents ran.
#   * Divergence between `git merge-tree` and GitHub's server-side merge (rename
#     detection limits, merge options). That produces a false DISARM, never a
#     false allow -- it costs a review round, not a silent merge.
#
# REASONS (stdout, one token, last line)
#   pure-base-merge            exit 0 -- the push changed nothing but the base
#   review-push                an ordinary commit (one parent)
#   unexpected-parent-count    zero parents, or an octopus merge
#   parent1-not-previous-head  force-push, or a commit landed before the merge
#   parent2-not-in-base        parent 2 carries content the base does not have
#   conflicted-base-merge      the automatic merge conflicts => hand-resolved
#   base-merge-with-content    the tree is not the automatic merge's tree
#   before-unreachable         the previous head is not in this clone
#   after-unreachable          the new head is not in this clone
#   base-unreachable           the base rev does not resolve
#   git-too-old                git < 2.38 has no `merge-tree --write-tree`
#   predicate-error            anything else -- the script could not decide
#
# The last four are the error class. If `git-too-old` or `predicate-error` ever
# fires, this script is wrong and the fail-closed default is silently paying for
# it -- that is the monitoring signal named in the ruling, so keep the tokens.

set -euo pipefail

# `git merge-tree --write-tree` was added in git 2.38. Older git accepts
# `merge-tree` with a completely different (and useless here) output format, so
# the version must be asserted rather than inferred from an exit code.
readonly MIN_GIT_MAJOR=2
readonly MIN_GIT_MINOR=38

# The single exit path that reports a decision. `trap - EXIT` first, so the
# fail-closed trap below cannot also fire and print a second token.
decide() { # decide <reason> <status>
  trap - EXIT
  printf '%s\n' "$1"
  exit "$2"
}
disarm() { decide "$1" 1; }

# Reached only when the script did NOT decide: any `set -e` abort, any unset
# variable, any signal, or execution falling off the end. All of those are
# "unknown", and unknown disarms.
on_unplanned_exit() {
  trap - EXIT
  printf 'predicate-error\n'
  exit 1
}
trap on_unplanned_exit EXIT

[ "$#" -eq 3 ] || disarm predicate-error
before_arg=$1
after_arg=$2
base_arg=$3
[ -n "$before_arg" ] && [ -n "$after_arg" ] && [ -n "$base_arg" ] || disarm predicate-error

# --- git version ------------------------------------------------------------
version_line=$(git --version 2>/dev/null) || disarm predicate-error
version=${version_line#git version }
git_major=${version%%.*}
version_rest=${version#*.}
git_minor=${version_rest%%.*}
case "$git_major" in '' | *[!0-9]*) disarm predicate-error ;; esac
case "$git_minor" in '' | *[!0-9]*) disarm predicate-error ;; esac
if [ "$git_major" -lt "$MIN_GIT_MAJOR" ] ||
  { [ "$git_major" -eq "$MIN_GIT_MAJOR" ] && [ "$git_minor" -lt "$MIN_GIT_MINOR" ]; }; then
  disarm git-too-old
fi

# --- resolve the three revs -------------------------------------------------
# `--verify --quiet` exits 1 and prints nothing when the object is missing, so
# an unreachable rev is a decision here rather than noise on stderr. A shallow
# clone lands here too: fetch-depth must be 0 for this script to decide anything
# but "unreachable".
resolve_commit() { git rev-parse --verify --quiet "$1^{commit}"; }

before=$(resolve_commit "$before_arg") || disarm before-unreachable
after=$(resolve_commit "$after_arg") || disarm after-unreachable
base=$(resolve_commit "$base_arg") || disarm base-unreachable

# --- condition 1: exactly two parents ---------------------------------------
parent_line=$(git rev-list --parents -n 1 "$after") || disarm predicate-error
# The line is "<commit> <parent>...". Word-splitting is intended.
# shellcheck disable=SC2086
set -- $parent_line
shift # drop the commit itself; what remains is the parent list
case "$#" in
  1) disarm review-push ;;
  2) ;;
  *) disarm unexpected-parent-count ;;
esac
parent1=$1
parent2=$2

# --- condition 2: parent 1 is the head the reviewers answered against --------
[ "$parent1" = "$before" ] || disarm parent1-not-previous-head

# --- condition 3: parent 2 contributes only what the base already has --------
# `--is-ancestor` uses exit 1 for "no" and other non-zero codes for real errors.
# Collapsing those would report a git failure as a clean negative answer, so
# they are separated -- both disarm, but only one of them means "this script is
# broken", and the monitoring signal depends on telling them apart.
if git merge-base --is-ancestor "$parent2" "$base"; then
  :
else
  ancestor_status=$?
  [ "$ancestor_status" -eq 1 ] && disarm parent2-not-in-base
  disarm predicate-error
fi

# --- condition 4: the tree IS the automatic merge's tree --------------------
# This is the condition that carries the safety; 1-3 only make it meaningful.
# Exit 0 = clean merge, 1 = conflict, anything else = error.
set +e
merge_output=$(git merge-tree --write-tree "$before" "$parent2" 2>/dev/null)
merge_status=$?
set -e
case "$merge_status" in
  0) ;;
  1) disarm conflicted-base-merge ;;
  *) disarm predicate-error ;;
esac

# With `--write-tree` the first line is the resulting tree OID; anything after it
# is informational. Take the first line only.
merged_tree=${merge_output%%$'\n'*}
case "$merged_tree" in '' | *[!0-9a-f]*) disarm predicate-error ;; esac

after_tree=$(git rev-parse --verify --quiet "$after^{tree}") || disarm predicate-error
[ "$merged_tree" = "$after_tree" ] || disarm base-merge-with-content

# The one and only success path.
decide pure-base-merge 0
