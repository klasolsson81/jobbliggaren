#!/usr/bin/env bash
# SessionStart hook — worktree lifecycle (issue #673, CTO bind
# docs/reviews/2026-07-05-673-session-end-cleanup-cto.md, ADR 0094).
#
# Two jobs, both safe:
#   (1) Open a provenance marker (.jbl-worktree.json) for the CURRENT linked worktree
#       so a future session can reason about who owns it.
#   (2) Reap OTHER worktrees whose owning session declared itself finished
#       (close-stamped marker) AND whose PR is MERGED AND that are clean.
#
# Observe-only by default (logs "WOULD reap"); destructive LOCAL ops run only under
# JBL_WORKTREE_REAP=live (the Klas ratchet). The shared remote is NEVER auto-touched
# (git push --delete is report-only). The hook NEVER runs checkout/switch/reset.
#
# Must-not invariants (see the CTO report): never the main copy, never the current
# cwd, never an unmerged / dirty / open-marker worktree, never the shared remote,
# never a destructive op on a heuristic signal.
#
# Output uses scalar counters + string accumulation on purpose: this msys2 bash 5.2
# throws "unbound variable" on ${#arr[@]} / ${arr[@]} for an empty array under set -u.
set -u

MARKER=".jbl-worktree.json"
REAP_MODE="${JBL_WORKTREE_REAP:-dry}"          # dry (default) | live
STAMP="$(date +%Y-%m-%d 2>/dev/null || echo unknown)"
LOG="docs/sessions/worktree-reaper-${STAMP}.log"

# Best-effort session_id off the SessionStart stdin payload (JSON).
_stdin="$(cat 2>/dev/null || true)"
SESSION_ID=""
if command -v jq >/dev/null 2>&1 && [ -n "$_stdin" ]; then
  SESSION_ID="$(printf '%s' "$_stdin" | jq -r '.session_id // empty' 2>/dev/null || true)"
fi

# Not a git repo -> nothing to do.
git rev-parse --git-dir >/dev/null 2>&1 || exit 0

CUR_TOP="$(git rev-parse --show-toplevel 2>/dev/null || true)"
CUR_GITDIR="$(git rev-parse --git-dir 2>/dev/null || true)"
CUR_COMMON="$(git rev-parse --git-common-dir 2>/dev/null || true)"

# The MAIN copy's root. CUR_TOP is the CURRENT worktree, which is exactly the wrong
# place to rescue files TO when the session is itself running inside a worktree. The
# common git dir always points at the main copy's .git, so its parent is the main root.
MAIN_TOP="$(dirname -- "$(git rev-parse --path-format=absolute --git-common-dir 2>/dev/null || echo /nonexistent/.git)")"

# Only ever rm -rf inside a known worktree root. Bounds the blast radius of the
# leftover-dir cleanup so a malformed path can never escalate.
_reap_ok_path() {
  case "$1" in
    *..*) return 1 ;;                                  # reject any parent-dir traversal first
    */.claude/worktrees/*) return 0 ;;
    /c/tmp/jbl-*|c:/tmp/jbl-*|C:/tmp/jbl-*) return 0 ;;
    *) return 1 ;;
  esac
}

# --- (1) marker-open for the current linked worktree (never the main copy) ---------
# Main copy <=> git-dir == git-common-dir (the session-start.sh idiom). A linked
# worktree has them differ.
# Starting a session in a worktree PROVES it is live again, so the marker must read
# "open" whether it is absent (first start) or already close-stamped (a prior session
# ended terminally and someone re-entered the tree). Re-opening a close-stamped marker
# is what keeps a concurrent peer's reaper from removing a reused, live worktree — the
# clean-check alone does not protect a freshly-reopened session that has not edited yet.
if [ -n "$CUR_TOP" ] && [ -n "$CUR_GITDIR" ] && [ "$CUR_GITDIR" != "$CUR_COMMON" ] \
   && command -v jq >/dev/null 2>&1; then
  _opened="$(date -Iseconds 2>/dev/null || echo unknown)"
  if [ ! -f "$CUR_TOP/$MARKER" ]; then
    _branch="$(git -C "$CUR_TOP" rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
    jq -n --arg path "$CUR_TOP" --arg branch "$_branch" --arg sid "$SESSION_ID" --arg opened "$_opened" \
      '{path:$path, branch:$branch, session_id:$sid, pr_number:null, opened_at:$opened, closed_at:null}' \
      > "$CUR_TOP/$MARKER" 2>/dev/null || true
  else
    # Marker exists: reopen it if it was close-stamped (liveness must reflect reality).
    _was_closed="$(jq -r '.closed_at // empty' "$CUR_TOP/$MARKER" 2>/dev/null || true)"
    if [ -n "$_was_closed" ]; then
      _tmp="${CUR_TOP}/${MARKER}.tmp.$$"
      if jq --arg opened "$_opened" --arg sid "$SESSION_ID" '.closed_at = null | .opened_at = $opened | .session_id = $sid' \
            "$CUR_TOP/$MARKER" > "$_tmp" 2>/dev/null; then
        mv "$_tmp" "$CUR_TOP/$MARKER" 2>/dev/null || rm -f "$_tmp" 2>/dev/null || true
      else
        rm -f "$_tmp" 2>/dev/null || true
      fi
    fi
  fi
fi

# --- (2) reaper over every registered worktree -------------------------------------
# git worktree list always yields >=1 entry (the main copy), so the index loop below
# never touches an empty array.
declare -a WT_PATHS WT_BRANCHES
_p=""
while IFS= read -r line; do
  case "$line" in
    "worktree "*) _p="${line#worktree }" ;;
    "branch refs/heads/"*) WT_PATHS+=("$_p"); WT_BRANCHES+=("${line#branch refs/heads/}") ;;
    "detached")            WT_PATHS+=("$_p"); WT_BRANCHES+=("") ;;
  esac
done < <(git worktree list --porcelain 2>/dev/null)

REAPED_LIST=""; REAPED_N=0
RESCUED_LIST=""; RESCUED_N=0
REMOTE_LIST=""; REMOTE_N=0
SKIPPED_LIST=""; SKIPPED_N=0
_skip() { SKIPPED_LIST+="  skip: $1 :: $2"$'\n'; SKIPPED_N=$((SKIPPED_N + 1)); }

for i in "${!WT_PATHS[@]}"; do
  W="${WT_PATHS[$i]}"
  B="${WT_BRANCHES[$i]}"

  # Conjunct 1: a linked worktree, not the main copy.
  _wgd="$(git -C "$W" rev-parse --git-dir 2>/dev/null || true)"
  _wcd="$(git -C "$W" rev-parse --git-common-dir 2>/dev/null || true)"
  if [ -z "$_wgd" ] || [ "$_wgd" = "$_wcd" ]; then _skip "$W" "main-copy/unreadable"; continue; fi

  # Conjunct 2: never the current cwd (compare git-normalized toplevels).
  _wtop="$(git -C "$W" rev-parse --show-toplevel 2>/dev/null || true)"
  if [ -n "$CUR_TOP" ] && [ "$_wtop" = "$CUR_TOP" ]; then _skip "$W" "current-cwd"; continue; fi

  # Conjunct 3: a feature branch, not main, not detached.
  if [ -z "$B" ] || [ "$B" = "main" ]; then _skip "$W" "no-feature-branch"; continue; fi

  # Conjunct 5: marker exists AND is close-stamped (owner declared itself finished).
  # Cheap local checks before the network call.
  if [ ! -f "$W/$MARKER" ]; then _skip "$W" "no-marker (unknown provenance)"; continue; fi
  _closed="$(jq -r '.closed_at // empty' "$W/$MARKER" 2>/dev/null || true)"
  if [ -z "$_closed" ]; then _skip "$W" "marker-open (session may be live)"; continue; fi

  # Conjunct 6: clean modulo the marker file itself. Any tracked change, or any other
  # untracked path, means "skip on doubt". Exclude only the exact untracked-marker line
  # (-x -F), never a free substring, so a path merely CONTAINING the marker name still
  # counts as dirty.
  _dirty="$(git -C "$W" status --porcelain 2>/dev/null | grep -v -x -F -- "?? $MARKER" || true)"
  if [ -n "$_dirty" ]; then _skip "$W" "dirty (uncommitted changes)"; continue; fi

  # Conjunct 4: PR MERGED (the squash-safe oracle). Network-last. gh missing/unauth or
  # no merged PR -> skip (fail-safe). Require a NUMERIC PR id: gh's embedded --jq emits
  # empty for a null result today, but a standalone `jq '.[0].number'` over [] emits the
  # string "null" — the numeric guard keeps either shape from reaping an unmerged tree.
  _merged=""
  if command -v gh >/dev/null 2>&1; then
    _merged="$(gh pr list --head "$B" --state merged --json number --jq '.[0].number' 2>/dev/null || true)"
  fi
  case "$_merged" in
    ''|*[!0-9]*) _skip "$W" "PR not merged / gh unavailable"; continue ;;
  esac

  # All conjuncts pass. The branch is a remote-sweep candidate regardless of mode
  # (remote deletion is always report-only).
  REMOTE_LIST+="    - $B (PR #$_merged)"$'\n'; REMOTE_N=$((REMOTE_N + 1))

  # ── RESCUE BEFORE DESTROY (2026-07-14, #884 hygiene pass) ────────────────────────
  # Conjunct 6 above is BLIND to the files that matter most. docs/sessions/,
  # docs/reviews/ and ADRs 0074+ are GITIGNORED (.gitignore:98,123,124-125), so
  # `git status --porcelain` reports NOTHING for them, the tree looks CLEAN, and
  # `git worktree remove` — even without --force — removes it and takes them with it.
  #
  # MEASURED, not reasoned: a probe worktree holding one ignored docs/reviews/*.md was
  # removed successfully by the no-force path below, and the file was gone. The comment
  # further down knew half of this ("the marker is gitignored, so it does not block the
  # removal") and did not follow the implication: nothing else gitignored blocks it
  # either. Those files are the ONLY copy — agent reviews, CTO verdicts, session logs
  # and every ADR from 0074 on live nowhere else in the world. The 2026-07-14 pass had
  # to rescue 14 of them BY HAND (four CTO reports, six reviews, a session log) from
  # worktrees that were one `--force` away from gone.
  #
  # So: copy them out first, never overwriting a file the main copy already has. If the
  # rescue cannot complete, SKIP the reap — a destroy that proceeds past a failed
  # rescue is the same bug wearing a seatbelt.
  # DRY stays observe-only: it enumerates, it never copies. That the count is non-zero
  # IS the observation — it is the measure of what a live run would otherwise destroy.
  _resc_fail=""
  if [ -z "$MAIN_TOP" ] || [ ! -d "$MAIN_TOP" ]; then
    _resc_fail=1                                      # cannot locate the main copy → never destroy
  else
    for _rel in $(git -C "$W" ls-files --others -- docs/sessions docs/reviews docs/decisions 2>/dev/null || true); do
      [ -f "$W/$_rel" ] || continue
      [ -f "$MAIN_TOP/$_rel" ] && continue            # already safe in the main copy — never overwrite
      if [ "$REAP_MODE" = "live" ]; then
        mkdir -p "$MAIN_TOP/$(dirname -- "$_rel")" 2>/dev/null || { _resc_fail=1; break; }
        cp -- "$W/$_rel" "$MAIN_TOP/$_rel" 2>/dev/null || { _resc_fail=1; break; }
        RESCUED_LIST+="  rescued: $_rel  (from $W)"$'\n'
      else
        RESCUED_LIST+="  would rescue: $_rel  (from $W)"$'\n'
      fi
      RESCUED_N=$((RESCUED_N + 1))
    done
  fi
  if [ -n "$_resc_fail" ]; then
    _skip "$W" "rescue of gitignored docs FAILED — refusing to reap"
    continue
  fi
  # ─────────────────────────────────────────────────────────────────────────────────

  if [ "$REAP_MODE" = "live" ]; then
    # Bounded, local, recoverable ops only. Order is idempotent / re-runnable.
    # `git worktree remove` runs WITHOUT --force so it still refuses a genuinely dirty
    # tree; the marker is gitignored (every marked worktree was created after the ignore
    # merged), so it does not block the removal. We deliberately do NOT delete the marker
    # first: on a Windows-lock double-failure the marker must survive so the next run
    # re-evaluates the leftover instead of skipping it forever as "no-marker".
    if ! git worktree remove -- "$W" 2>/dev/null; then
      # Windows file-lock (or partial): best-effort physical removal within the allowed
      # root, THEN prune so the now-missing registration is cleared (prune before rm is
      # a no-op — the dir still exists). Re-runnable next session if a lock persists.
      if _reap_ok_path "$W" && [ -d "$W" ]; then rm -rf "$W" 2>/dev/null || true; fi
      git worktree prune 2>/dev/null || true
    fi
    # branch -D only after the registration is gone (a lingering worktree keeps the
    # branch "checked out" and blocks deletion).
    git branch -D -- "$B" 2>/dev/null || true
    REAPED_LIST+="  reaped:  $W (branch $B, PR #$_merged) [LIVE]"$'\n'; REAPED_N=$((REAPED_N + 1))
  else
    REAPED_LIST+="  reaped:  $W (branch $B, PR #$_merged) [WOULD]"$'\n'; REAPED_N=$((REAPED_N + 1))
  fi
done

# --- report ------------------------------------------------------------------------
# Always append to the gitignored log (even an all-skip run) so the dry-run observation
# phase that gates the live ratchet has a complete audit trail, incl. every skip reason.
mkdir -p docs/sessions 2>/dev/null || true
{
  echo "$(date -Iseconds 2>/dev/null) reaper mode=${REAP_MODE} reaped=${REAPED_N} rescued=${RESCUED_N} remote=${REMOTE_N} skipped=${SKIPPED_N}"
  printf '%s%s%s' "$RESCUED_LIST" "$REAPED_LIST" "$SKIPPED_LIST"
} >> "$LOG" 2>/dev/null || true

# Only surface to the session (stdout) when there is something actionable, to avoid
# noise on the common all-skip start.
if [ "$REAPED_N" -gt 0 ] || [ "$REMOTE_N" -gt 0 ]; then
  echo ""
  echo "== Worktree-reaper (${REAP_MODE}) =="
  [ "$REAP_MODE" = "dry" ] && echo "  (observe-only — sätt JBL_WORKTREE_REAP=live för att faktiskt städa)"
  [ "$REAPED_N" -gt 0 ] && printf '%s' "$REAPED_LIST"
  if [ "$REMOTE_N" -gt 0 ]; then
    echo "  remote-grenar att städa manuellt (git push origin --delete, aldrig automatiskt):"
    printf '%s' "$REMOTE_LIST"
  fi
fi

exit 0
