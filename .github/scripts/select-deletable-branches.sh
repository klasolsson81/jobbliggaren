#!/usr/bin/env bash
#
# select-deletable-branches.sh -- which remote branches are safe to delete
# because the pull request that owned them has already merged?
#
# Usage:  bash select-deletable-branches.sh <merged.json> <open.json> \
#                                           <branches.tsv> <default-branch> <owner>
#         (invoked through `bash`; the file is mode 100644, so the shebang is
#          documentation, not an entry point -- same convention as
#          is-pure-base-merge.sh)
#
#   <merged.json>     `gh pr list --state merged --json number,headRefName,headRepositoryOwner`
#   <open.json>       `gh pr list --state open   --json number,headRefName,baseRefName`
#   <branches.tsv>    `name<TAB>protected` per line, one per branch that EXISTS
#                     on the remote (`gh api repos/{o}/{r}/branches --paginate`)
#   <default-branch>  the repository default branch, e.g. `main`
#   <owner>           the repository owner login, for the fork check
#
#   Exit 0  every branch was decided. Verdicts on stdout.
#   Exit 1  the input could not be trusted. NOTHING on stdout.
#   Stdout  one TSV line per branch: `<verdict><TAB><branch><TAB><reason>`
#           where <verdict> is `delete` or `skip`.
#
# WHY THIS EXISTS -- AND WHY IT IS NOT AN EVENT HANDLER. `delete_branch_on_merge`
# is true on this repository and still does not fire, because the automatic
# deletion follows the identity that merged and `label-automerge.yml` merges as
# the `github-actions` app via GITHUB_TOKEN (#725). The obvious repair -- a
# workflow on `pull_request: closed` -- cannot work: events triggered by
# GITHUB_TOKEN do not start workflow runs, so the population that has the bug is
# exactly the population whose events are suppressed. Both halves were measured
# on 2026-07-27, the second with a counterfactual; the numbers live in the
# workflow header, which is the one place that owns them.
#
# THE CONTRACT IS FAIL-CLOSED, IN THE STRONG SENSE: all-or-nothing. Verdicts are
# buffered and printed only after every branch has been decided, so a failure
# half-way through cannot leave the caller acting on a partial list. `exit 0` is
# written at exactly ONE place. Everything else -- a jq error, an unreadable
# file, an unset variable, a signal -- lands in the EXIT trap, which prints
# `selector-error` to STDERR, prints nothing to stdout, and exits 1. A bug in
# here therefore degrades to "delete nothing", which is the behaviour that
# preceded this script.
#
# THE BUFFERING IS DECLARED UNEXERCISED, NOT DEMONSTRATED. Every input is
# validated BEFORE the loop, so there is today no reachable failure inside it --
# which means no fixture can distinguish buffering from streaming, and mutation
# testing confirmed it: replacing the buffer with a direct `printf` leaves the
# whole suite green. It is kept anyway, as structural insurance against a future
# edit that introduces a mid-loop failure, and it is recorded here as untested
# rather than left to read as proven.
#
# WHY THE PREDICATE IS "HEAD OF A MERGED PR" AND NEVER AGE. A branch with no
# merged PR is somebody's work in progress -- possibly in a worktree on another
# machine, possibly not yet pushed anywhere else. #725's own text names two
# CLOSED-not-merged heads that are restorable from the PR page and must survive.
# Age, "looks stale", and "already contained in main" are all proxies for
# liveness, and ADR 0094 rejected liveness proxies outright for exactly this
# class of destructive sweep: doubt resolves to skip, never to "probably fine".
#
# THE FIVE SKIPS, AND WHY EACH ONE IS LOAD-BEARING
#
#   default-branch     never delete the trunk, whatever the API says about it.
#   protected          branch protection is somebody's stated intent; a hygiene
#                      job does not overrule it.
#   base-of-open-pr    THE ONE THAT IS EASY TO GET WRONG. Deleting the base of
#                      an open PR does not close it -- since Pull Request
#                      Retargeting (2020-05-19) GitHub silently RETARGETS it to
#                      the merged PR's base. That is worse than a close for this
#                      house: the stacked PR's merge base moves, so the diff its
#                      reviewers granted `agents-done` against is no longer the
#                      diff that merges -- and retargeting fires `edited`, not
#                      `synchronize`, so `label-automerge.yml`'s disarm and
#                      `is-pure-base-merge.sh` never see it. That is #836's
#                      defect class re-entering through a hygiene job.
#   head-of-open-pr    a branch can own a merged PR AND a newer open one (reuse,
#                      or a reopened line of work). The open PR wins.
#   fork-head-only     a fork's head branch is not in this repository, so name
#                      equality with one of ours is a COLLISION, not a match.
#                      Without this check a fork PR from a branch called
#                      `main`, or `fix/x`, would authorise deleting ours.
#
# WHAT IT DELIBERATELY DOES NOT DO (stated, not hidden):
#   * It does not delete anything. It decides; the workflow deletes and then
#     verifies the state afterwards. Keeping the decision pure is what makes it
#     fixture-testable without a network or a repository.
#   * It does not paginate or call an API. Truncated input is the caller's
#     failure mode; a short <merged.json> makes branches look like `no-merged-pr`
#     and skips them, which is the safe direction.
#   * It does not rank or cap. Every branch that exists gets a verdict.
#
# REASONS (third column)
#   merged-pr-#N        delete -- head of merged PR N, and nothing else objected
#   no-merged-pr        skip -- no merged PR in the input has this head
#   fork-head-only      skip -- the only merged PRs with this head are forks'
#   default-branch      skip -- this is the repository default branch
#   protected           skip -- branch protection is on
#   base-of-open-pr-#N  skip -- open PR N would be silently retargeted
#   head-of-open-pr-#N  skip -- open PR N still owns this branch

set -euo pipefail

fail() { # fail <message>
  # Clear the trap FIRST: `exit` below re-enters it otherwise, and every error
  # prints twice -- with the second copy saying "unexpected exit", which is a
  # worse description of the failure than the real one it follows.
  trap - EXIT
  echo "select-deletable-branches: $1" >&2
  echo "selector-error" >&2
  exit 1
}

# Anything that is not the single blessed exit at the bottom lands here. Note it
# prints to stderr only: stdout must stay empty on every failure path, because
# an empty stdout is what makes "delete nothing" the default.
trap 'fail "undecided (unexpected exit)"' EXIT

[ "$#" -eq 5 ] || fail "expected 5 arguments, got $#"

readonly MERGED_JSON=$1
readonly OPEN_JSON=$2
readonly BRANCHES_TSV=$3
readonly DEFAULT_BRANCH=$4
readonly OWNER=$5

for f in "$MERGED_JSON" "$OPEN_JSON" "$BRANCHES_TSV"; do
  [ -f "$f" ] || fail "input file not found: $f"
  [ -r "$f" ] || fail "input file not readable: $f"
done
[ -n "$DEFAULT_BRANCH" ] || fail "default branch must not be empty"
[ -n "$OWNER" ] || fail "owner must not be empty"

command -v jq >/dev/null 2>&1 || fail "jq is required"

# An empty ARRAY is legitimate (a repo can have no open PRs). Anything that is
# not an array at all -- an API error object, a truncated file, HTML from a
# proxy -- is not, and must not read as "nothing to worry about". This is the
# difference between "no open PRs" and "we could not find out", and collapsing
# the two is precisely how a fail-closed guard turns fail-open.
jq -e 'type == "array"' "$MERGED_JSON" >/dev/null 2>&1 || fail "not a JSON array: $MERGED_JSON"
jq -e 'type == "array"' "$OPEN_JSON" >/dev/null 2>&1 || fail "not a JSON array: $OPEN_JSON"

# `<branch>\t<pr-number>` for merged PRs whose head repo is THIS repo's owner.
# `headRepositoryOwner` is null on a PR whose head repository has been deleted;
# null is not this owner, so such a PR contributes nothing and its branch falls
# through to `no-merged-pr` -- the safe direction, and the reason the comparison
# is written as an equality against the owner rather than as `!= fork`.
merged_heads=$(jq -r --arg owner "$OWNER" '
  .[]
  | select(.headRepositoryOwner.login == $owner)
  | select(.headRefName != null and .headRefName != "")
  | [.headRefName, (.number|tostring)] | @tsv' "$MERGED_JSON") || fail "cannot read $MERGED_JSON"

open_bases=$(jq -r '
  .[] | select(.baseRefName != null and .baseRefName != "")
  | [.baseRefName, (.number|tostring)] | @tsv' "$OPEN_JSON") || fail "cannot read $OPEN_JSON"

# The head side of open PRs is NOT owner-filtered, deliberately and in the
# opposite direction from the merged side. There the fork check prevents a
# foreign branch from AUTHORISING a deletion; here any open PR at all, fork or
# not, is a reason to WITHHOLD one. Both choices resolve doubt to "do not
# delete" -- which is why they point opposite ways.
open_heads=$(jq -r '
  .[] | select(.headRefName != null and .headRefName != "")
  | [.headRefName, (.number|tostring)] | @tsv' "$OPEN_JSON") || fail "cannot read $OPEN_JSON"

lookup() { # lookup <table> <key> -> prints first matching value, or nothing
  awk -F'\t' -v key="$2" '$1 == key { print $2; exit }' <<<"$1"
}

# Buffered, never streamed -- see the all-or-nothing note in the header.
verdicts=""
emit() { verdicts+="$1"$'\n'; }

# `|| [ -n "${branch:-}" ]` so a final line without a trailing newline is still
# processed. Without it the last branch in the file is silently dropped, which
# would be a skip -- safe, but silently wrong, and the log would not say so.
while IFS=$'\t' read -r branch protected _rest || [ -n "${branch:-}" ]; do
  # `git ls-remote` and the branches API both round-trip refs verbatim, so a
  # blank line means a malformed input file rather than a branch, and a branch
  # is never legitimately empty.
  [ -n "${branch:-}" ] || continue

  if [ "$branch" = "$DEFAULT_BRANCH" ]; then
    emit "$(printf 'skip\t%s\tdefault-branch' "$branch")"
    continue
  fi

  # Only the exact string `false` counts as unprotected. A missing column, a
  # `null`, or anything the API did not say is treated as protected, because
  # "we do not know" must not authorise a delete.
  if [ "${protected:-}" != "false" ]; then
    emit "$(printf 'skip\t%s\tprotected' "$branch")"
    continue
  fi

  base_pr=$(lookup "$open_bases" "$branch")
  if [ -n "$base_pr" ]; then
    emit "$(printf 'skip\t%s\tbase-of-open-pr-#%s' "$branch" "$base_pr")"
    continue
  fi

  head_pr=$(lookup "$open_heads" "$branch")
  if [ -n "$head_pr" ]; then
    emit "$(printf 'skip\t%s\thead-of-open-pr-#%s' "$branch" "$head_pr")"
    continue
  fi

  merged_pr=$(lookup "$merged_heads" "$branch")
  if [ -z "$merged_pr" ]; then
    # Distinguish "no PR at all" from "only a fork's PR" so the log says which.
    # Both skip; only the wording differs, and the wording is what tells a
    # reader whether they are looking at live work or at a name collision.
    if jq -e --arg b "$branch" 'any(.[]; .headRefName == $b)' "$MERGED_JSON" >/dev/null 2>&1; then
      emit "$(printf 'skip\t%s\tfork-head-only' "$branch")"
    else
      emit "$(printf 'skip\t%s\tno-merged-pr' "$branch")"
    fi
    continue
  fi

  emit "$(printf 'delete\t%s\tmerged-pr-#%s' "$branch" "$merged_pr")"
done < "$BRANCHES_TSV"

printf '%s' "$verdicts"

trap - EXIT
exit 0
