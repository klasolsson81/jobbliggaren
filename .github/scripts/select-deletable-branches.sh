#!/usr/bin/env bash
#
# select-deletable-branches.sh -- which remote branches are safe to delete
# because the pull request that owned them has already merged?
#
# Usage:  bash select-deletable-branches.sh <merged.json> <open.json> \
#                                           <branches.tsv> <default-branch> <owner> <repo>
#         (invoked through `bash`; the file is mode 100644, so the shebang is
#          documentation, not an entry point -- same convention as
#          is-pure-base-merge.sh)
#
#   <merged.json>     `gh pr list --state merged
#                        --json number,headRefName,headRefOid,headRepositoryOwner,headRepository`
#   <open.json>       `gh pr list --state open --json number,headRefName,baseRefName`
#   <branches.tsv>    `name<TAB>protected<TAB>tip-sha` per line, one per branch
#                     that EXISTS on the remote
#                     (`gh api repos/{o}/{r}/branches --paginate`)
#   <default-branch>  the repository default branch, e.g. `main`
#   <owner>           the repository owner login, for the fork check
#   <repo>            the repository NAME, for the fork check
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
# WHY THE PREDICATE IS "HEAD OF A MERGED PR, AT THE COMMIT THAT MERGED" AND
# NEVER AGE. A branch with no merged PR is somebody's work in progress --
# possibly in a worktree on another machine, possibly not yet pushed anywhere
# else. #725's own text names two CLOSED-not-merged heads that are restorable
# from the PR page and must survive. Age, "looks stale", and "already contained
# in main" are all proxies for liveness, and ADR 0094 rejected liveness proxies
# outright for exactly this class of destructive sweep: doubt resolves to skip,
# never to "probably fine".
#
# THE SHA HALF OF THAT PREDICATE IS THE ONE THAT IS EASY TO LEAVE OUT, and
# leaving it out is the only way this script can destroy work that is not
# recoverable from the PR page. `delete_branch_on_merge`, which this replaces,
# acts at the instant the tip IS the merge tip by construction. A deferred sweep
# re-decides a day later against a tip that may have moved: a branch reused
# after its PR merged -- commits pushed, next PR not opened yet -- is still
# "head of a merged PR" by name. GitHub's "Restore branch" would then restore
# the MERGED commit, not the work that was on the branch. `head-of-open-pr`
# catches reuse WITH a PR open; nothing but the SHA check catches reuse before
# the PR exists. Measured 2026-07-27: EVERY candidate had tip == merge tip, so
# the guard is a no-op on today's population -- which prices it at zero, and says
# nothing about whether it is needed (Saltzer & Schroeder 1975 §3, the same
# citation `label-automerge.yml` leans on: fail-safe defaults are not weighed
# against probability). The claim is deliberately the RATIO and not an integer:
# three readings the same afternoon gave 36, 37 and 38 candidates as PRs merged
# underneath, and an integer here would have been stale within the hour -- the
# same discipline the workflow header states for its own commit count.
#
# THE SEVEN GUARDS THAT WITHHOLD A DELETION, AND WHY EACH ONE IS LOAD-BEARING.
# (There are EIGHT skip reasons; `no-merged-pr` is the eighth and is not a guard
# but the absence of any authorisation in the first place. The full set is in the
# REASONS table at the bottom of this header.)
#
# LISTED BY ROLE, NOT BY EVALUATION ORDER -- the order lives in the code and is
# deliberate, because the FIRST guard that matches owns the reason column. So
# `unsupported-name` runs first (nothing may be decided about a name we cannot
# carry), and `tip-moved-since-merge` runs last because the guards above it carry
# the more ACTIONABLE reason when several apply -- a stacked PR that would be
# silently retargeted matters more to a reader than a moved tip -- and because
# its reason embeds the merged PR number and therefore cannot be formed before
# the merged lookup has happened.
#
#   default-branch     never delete the trunk, whatever the API says about it.
#                      Read from the API rather than hardcoded, so it is
#                      name-independent.
#   protected          branch protection is somebody's stated intent; a hygiene
#                      job does not overrule it. NOTE: this flag reflects branch
#                      protection; a ruleset-based "Restrict deletions" may not
#                      appear in it. That gap is deliberately NOT patched with a
#                      name list -- a ruleset is enforced server-side, so the
#                      DELETE fails, the branch survives, and the workflow's
#                      verify step goes RED on it. An invisible ruleset is a
#                      loud signal, not a silent deletion.
#   tip-moved-since-merge  the branch was reused after its PR merged. See above.
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
#   unsupported-name   a ref name this script cannot vouch for downstream. Git
#                      permits `#` and `%` in ref names; the caller interpolates
#                      the name into a REST path, where `#` starts a fragment --
#                      so `feat/x#main` would send DELETE to `.../heads/feat/x`
#                      and destroy a branch that has no merged PR and therefore
#                      no Restore button. Refused here rather than escaped
#                      there, so the guard sits in the fixture-tested layer.
#
# WHAT IT DELIBERATELY DOES NOT DO (stated, not hidden):
#   * It does not check that its input is a SINGLE JSON document. `jq -e` takes
#     its exit code from the last document, so a stream would pass the type and
#     field checks here and then be read in full. That case is refused UPSTREAM
#     by `assert-not-truncated.sh`, which the workflow runs on both files before
#     this script sees them. The strictness is therefore a property of the call
#     ORDER, not of this file -- anyone invoking it without that assertion
#     inherits the stream case.
#   * It does not delete anything. It decides; the workflow deletes and then
#     verifies the state afterwards. Keeping the decision pure is what makes it
#     fixture-testable without a network or a repository.
#   * It does not paginate or call an API, and it CANNOT detect truncation --
#     a short list is well-formed. The two directions are NOT symmetric, and the
#     asymmetry is the point: a truncated <merged.json> makes branches look like
#     `no-merged-pr` and skips them (safe), while a truncated <open.json>
#     dissolves BOTH open-PR guards for the PRs that fell off the end (FAIL-OPEN
#     -- a stacked base becomes deletable). The caller must therefore assert it
#     did not hit its page ceiling; this script cannot do it for them.
#   * It does not rank or cap. Every branch that exists gets a verdict.
#
# REASONS (third column)
#   merged-pr-#N            delete -- head of merged PR N at the merged commit
#   no-merged-pr            skip -- no merged PR in the input has this head
#   fork-head-only          skip -- the only merged PRs with this head are forks'
#   default-branch          skip -- this is the repository default branch
#   protected               skip -- branch protection is on
#   tip-moved-since-merge-#N  skip -- reused after PR N merged
#   base-of-open-pr-#N      skip -- open PR N would be silently retargeted
#   head-of-open-pr-#N      skip -- open PR N still owns this branch
#   unsupported-name        skip -- cannot be expressed safely in a REST path

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

[ "$#" -eq 6 ] || fail "expected 6 arguments, got $#"

readonly MERGED_JSON=$1
readonly OPEN_JSON=$2
readonly BRANCHES_TSV=$3
readonly DEFAULT_BRANCH=$4
readonly OWNER=$5
readonly REPO=$6

for f in "$MERGED_JSON" "$OPEN_JSON" "$BRANCHES_TSV"; do
  [ -f "$f" ] || fail "input file not found: $f"
  [ -r "$f" ] || fail "input file not readable: $f"
done
[ -n "$DEFAULT_BRANCH" ] || fail "default branch must not be empty"
[ -n "$OWNER" ] || fail "owner must not be empty"
[ -n "$REPO" ] || fail "repo must not be empty"

command -v jq >/dev/null 2>&1 || fail "jq is required"

# An empty ARRAY is legitimate (a repo can have no open PRs). Anything that is
# not an array at all -- an API error object, a truncated file, HTML from a
# proxy -- is not, and must not read as "nothing to worry about". This is the
# difference between "no open PRs" and "we could not find out", and collapsing
# the two is precisely how a fail-closed guard turns fail-open. An EMPTY FILE is
# the realistic shape of that failure: `gh` writes errors to stderr and nothing
# to stdout, so a failed fetch leaves zero bytes, not an error object.
jq -e 'type == "array"' "$MERGED_JSON" >/dev/null 2>&1 || fail "not a JSON array: $MERGED_JSON"
jq -e 'type == "array"' "$OPEN_JSON" >/dev/null 2>&1 || fail "not a JSON array: $OPEN_JSON"

# TYPE IS NOT ENOUGH -- THE FIELDS MUST BE THERE TOO, and the asymmetry is the
# same one the truncation note describes, one level up. A well-formed array of
# objects MISSING the keys (a changed `--json` list, a partial fetch) passes the
# type check, and then `select(.baseRefName != null �)` quietly filters every
# element away: `open_bases` comes out empty and a stacked base becomes
# deletable. That is fail-OPEN. The merged side degrades safely under the same
# drift (missing keys read as null -> `no-merged-pr` or `tip-moved`), but it is
# checked too, because a guard that only covers the dangerous direction invites
# the reader to assume the other one was considered and found safe -- which it
# was, and saying so is cheaper than leaving it to be re-derived.
jq -e 'all(.[]; has("number") and has("baseRefName") and has("headRefName"))' "$OPEN_JSON" >/dev/null 2>&1 \
  || fail "$OPEN_JSON lacks number/baseRefName/headRefName -- the open-PR guards cannot be built"

jq -e 'all(.[]; has("number") and has("headRefName") and has("headRefOid") and has("headRepository") and has("headRepositoryOwner"))' \
  "$MERGED_JSON" >/dev/null 2>&1 \
  || fail "$MERGED_JSON is missing fields the predicate and the reason column need"

# `<branch>\t<pr-number>\t<head-oid>` for merged PRs whose head repo is THIS
# repository. `headRepositoryOwner` is null on a PR whose head repository has
# been deleted; null is neither this owner nor this repo, so such a PR
# contributes nothing and its branch falls through to `no-merged-pr` -- the safe
# direction, and the reason the comparison is written as an equality against the
# owner rather than as `!= fork`.
#
# BOTH halves of the identity are compared, not just the owner: two repositories
# under the same owner can carry the same branch name, so owner equality alone
# would let a merged PR in a SIBLING repository authorise deleting a branch
# here. Unreachable on a user account (GitHub will not fork your repo to
# yourself) but reachable under an org, and the header promised the repository
# property -- so the code now delivers the property the comment claims.
merged_heads=$(jq -r --arg owner "$OWNER" --arg repo "$REPO" '
  .[]
  | select(.headRepositoryOwner.login == $owner and .headRepository.name == $repo)
  | select(.headRefName != null and .headRefName != "")
  | [.headRefName, (.number|tostring), (.headRefOid // "")] | @tsv' "$MERGED_JSON") \
  || fail "cannot read $MERGED_JSON"

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

# EXACT match on field 1, and the `exit` is load-bearing twice over.
#
# `==` not `~`: awk's `~` is a REGEX match, and a regex match is a SUBSTRING
# match. With `~`, the live branch `feat/x` finds the merged row for `feat/x-2`
# and gets deleted -- a skip silently turned into a delete on a branch nobody
# had merged. Mutation testing found that this survived the whole suite; the
# fixture that kills it is named in the test file.
#
# `exit`: a branch can head TWO merged PRs (reused, merged twice). Without the
# early exit awk prints both rows, the caller's `$(...)` becomes multi-line, and
# the reason column silently swallows a newline -- which drops the branch out of
# BOTH the delete and skip lists downstream. First match wins, and the input is
# newest-first, so the newest merged PR is the one the SHA is compared against.
# That ordering is `gh`'s documented default (createdAt descending) and is NOT
# verified here; if it is ever wrong, the SHA comparison simply fails against an
# older PR's oid and the branch SKIPS, so the failure direction is safe.
lookup() { # lookup <table> <key> -> prints first matching row's remaining fields
  awk -F'\t' -v key="$2" '$1 == key { print $2 "\t" $3; exit }' <<<"$1"
}

# Buffered, never streamed -- see the all-or-nothing note in the header.
verdicts=""
emit() { verdicts+="$1"$'\n'; }

# `|| [ -n "${branch:-}" ]` so a final line without a trailing newline is still
# processed. Without it the last branch in the file is silently dropped, which
# would be a skip -- safe, but silently wrong, and the log would not say so.
while IFS=$'\t' read -r branch protected tip_sha _rest || [ -n "${branch:-}" ]; do
  # `git ls-remote` and the branches API both round-trip refs verbatim, so a
  # blank line means a malformed input file rather than a branch, and a branch
  # is never legitimately empty.
  [ -n "${branch:-}" ] || continue

  # FIRST, before any verdict that could authorise a deletion: can this name be
  # carried safely by the caller? Git's own rules already forbid space, `~`,
  # `^`, `:`, `?`, `*`, `[`, `\` and control characters, so this allow-list
  # mainly has to exclude what git permits and REST paths mangle -- `#` and `%`.
  # A leading `-` is refused too, so the name can never be read as an option.
  # It excludes considerably MORE than those two -- `+ @ , ( ) = & ! ; { '` and
  # every non-ASCII character are all git-legal and REST-harmless, and are
  # refused anyway. That is deliberate: an allow-list is cheaper to reason about
  # than an exhaustive deny-list, and EVERY branch name live in this repository
  # today passes it (44 at the time of writing -- a count this very sweep changes
  # within a day of merging, which is why the claim is the ratio). If a legitimate name is ever refused, the
  # verdict is a SKIP, which costs a branch that lingers -- never a wrong
  # deletion.
  #
  # `*..*` IS NOT REDUNDANT, and the reason is worth stating because it is the
  # one leg that would otherwise rest on somebody else's validator. A character
  # allow-list admits `feat/../../tags/v1` -- every character in it is legal --
  # and that path would redirect a DELETE from `git/refs/heads/` to
  # `git/refs/tags/`. Today it is unreachable only because `git check-ref-format`
  # rejects any ref containing `..`, which is a rule of GIT'S, not of ours, and
  # is nowhere else in this file. Refusing it here keeps the guarantee local and
  # fixture-tested. Anyone widening the character class must keep this clause.
  # The SINGLE-dot forms (`x/./y`, `./evil`) are refused for the same reason and
  # were the two thirds of the hole that survived the first fix -- git rejects a
  # component beginning with `.` too, so no legitimate name is affected.
  # Whether GitHub's gateway actually resolves these dot-segments is NOT verified
  # here, and deliberately not relied on: the clause refuses the name either way,
  # which is what makes the guarantee local rather than borrowed.
  case "$branch" in
    -* | .* | */.* | *..* | *[!A-Za-z0-9._/-]*)
      emit "$(printf 'skip\t%s\tunsupported-name' "$branch")"
      continue ;;
  esac

  if [ "$branch" = "$DEFAULT_BRANCH" ]; then
    emit "$(printf 'skip\t%s\tdefault-branch' "$branch")"
    continue
  fi

  # Only the exact string `false` counts as unprotected. A missing column, a
  # `null` (which is what the API emits for a branch whose `protected` field is
  # absent), or anything the API did not say is treated as protected, because
  # "we do not know" must not authorise a delete.
  if [ "${protected:-}" != "false" ]; then
    emit "$(printf 'skip\t%s\tprotected' "$branch")"
    continue
  fi

  base_pr=$(lookup "$open_bases" "$branch")
  base_pr=${base_pr%%$'\t'*}
  if [ -n "$base_pr" ]; then
    emit "$(printf 'skip\t%s\tbase-of-open-pr-#%s' "$branch" "$base_pr")"
    continue
  fi

  head_pr=$(lookup "$open_heads" "$branch")
  head_pr=${head_pr%%$'\t'*}
  if [ -n "$head_pr" ]; then
    emit "$(printf 'skip\t%s\thead-of-open-pr-#%s' "$branch" "$head_pr")"
    continue
  fi

  merged_row=$(lookup "$merged_heads" "$branch")
  merged_pr=${merged_row%%$'\t'*}
  merged_oid=${merged_row#*$'\t'}
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

  # The tip must still be the commit that merged. Empty on either side means we
  # could not establish it, which is not permission -- same rule as `protected`.
  if [ -z "${tip_sha:-}" ] || [ -z "$merged_oid" ] || [ "$tip_sha" != "$merged_oid" ]; then
    emit "$(printf 'skip\t%s\ttip-moved-since-merge-#%s' "$branch" "$merged_pr")"
    continue
  fi

  emit "$(printf 'delete\t%s\tmerged-pr-#%s' "$branch" "$merged_pr")"
done < "$BRANCHES_TSV"

printf '%s' "$verdicts"

trap - EXIT
exit 0
