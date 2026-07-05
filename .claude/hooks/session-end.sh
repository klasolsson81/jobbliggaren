#!/usr/bin/env bash
# SessionEnd hook — RECORD + REPORT only, NEVER deletes (issue #673, CTO bind
# docs/reviews/2026-07-05-673-session-end-cleanup-cto.md, ADR 0094).
#
# On a genuinely-terminal reason, close-stamp the CURRENT worktree's marker so a
# FUTURE session's SessionStart reaper (worktree-reaper.sh) may retire it once its PR
# has merged. Deletion is deliberately deferred to that future start — you cannot
# git-worktree-remove the tree you stand in, and on Windows the cwd locks it.
#
# Reason semantics (official hooks docs, fetched 2026-07-05):
#   clear | resume | logout | prompt_input_exit | bypass_permissions_disabled | other
# CRITICAL: /clear fires SessionEnd(reason=clear) and THEN a fresh SessionStart in the
# same worktree — so close-stamping on clear/resume would falsely retire a LIVE
# worktree. Only genuinely-terminal reasons close-stamp. `other` is deliberately
# EXCLUDED for now (catch-all / ambiguous); widen only after the reason value on real
# termination is empirically confirmed (see the ADR rollout gate). Not close-stamping
# is the fail-safe direction: the worktree is merely reported, never auto-reaped.
set -u

MARKER=".jbl-worktree.json"

_stdin="$(cat 2>/dev/null || true)"
REASON=""
if command -v jq >/dev/null 2>&1 && [ -n "$_stdin" ]; then
  REASON="$(printf '%s' "$_stdin" | jq -r '.reason // empty' 2>/dev/null || true)"
fi

git rev-parse --git-dir >/dev/null 2>&1 || exit 0
CUR_TOP="$(git rev-parse --show-toplevel 2>/dev/null || true)"
CUR_GITDIR="$(git rev-parse --git-dir 2>/dev/null || true)"
CUR_COMMON="$(git rev-parse --git-common-dir 2>/dev/null || true)"

# Terminal reasons only (never clear/resume; `other` excluded pending verification).
case "$REASON" in
  logout|prompt_input_exit) TERMINAL=1 ;;
  *) TERMINAL=0 ;;
esac

# Close-stamp the current LINKED worktree's marker (never the main copy, which is never
# reaped). No marker -> nothing to stamp (a session that predates the reaper).
if [ "$TERMINAL" = "1" ] \
   && [ -n "$CUR_TOP" ] && [ -n "$CUR_GITDIR" ] && [ "$CUR_GITDIR" != "$CUR_COMMON" ] \
   && [ -f "$CUR_TOP/$MARKER" ] && command -v jq >/dev/null 2>&1; then
  _tmp="${CUR_TOP}/${MARKER}.tmp.$$"
  if jq --arg closed "$(date -Iseconds 2>/dev/null || echo unknown)" '.closed_at = $closed' \
        "$CUR_TOP/$MARKER" > "$_tmp" 2>/dev/null; then
    mv "$_tmp" "$CUR_TOP/$MARKER" 2>/dev/null || rm -f "$_tmp" 2>/dev/null || true
    echo "✓ Worktree-markör stängd (reason=${REASON}) — nästa sessions start-reaper städar den när PR:en mergats."
  else
    rm -f "$_tmp" 2>/dev/null || true
  fi
fi

exit 0
