#!/usr/bin/env bash
# codex-agent-parity-guard.sh — BLOCKING (ADR 0135 Amendment 2 / CTO 2026-08-22).
# SET parity, not text drift: every tracked charter in .claude/agents/ has a
# pointer stub in .codex/agents/, plus the jobbpilot-review-discipline skill
# stub — so a Codex-run panel cannot silently lack a reviewer while reporting
# complete. Charter TEXT has one home (.claude/agents/); stubs only point.
# Fail-closed: an empty side errors. Regenerate:
#   diff <(ls .claude/agents/*.md) <(ls .codex/agents/*.toml)
set -euo pipefail
mapfile -t charters < <(git ls-files -- '.claude/agents/*.md' | xargs -n1 basename | sed 's/\.md$//' | sort)
mapfile -t stubs < <(git ls-files -- '.codex/agents/*.toml' | xargs -n1 basename | sed 's/\.toml$//' | sort)
if [ "${#charters[@]}" -eq 0 ] || [ "${#stubs[@]}" -eq 0 ]; then
  echo "::error::empty charter or stub set (${#charters[@]}/${#stubs[@]}) — fail-closed."
  exit 1
fi
expected=$(printf '%s\n' "${charters[@]}" 'jobbpilot-review-discipline' | sort -u)
actual=$(printf '%s\n' "${stubs[@]}" | sort -u)
if [ "$expected" != "$actual" ]; then
  echo "::error::.claude/agents and .codex/agents have drifted apart. Diff (expected vs stubs):"
  diff <(echo "$expected") <(echo "$actual") || true
  exit 1
fi
bad=0
for f in $(git ls-files -- '.codex/agents/*.toml'); do
  if ! grep -q '^sandbox_mode = "workspace-write"$' "$f"; then
    echo "::error::$f lacks explicit sandbox_mode = \"workspace-write\" — a read-only reviewer writes no report and says nothing about it."
    bad=1
  fi
  if ! grep -q 'read FROM DISK\|Read FROM DISK' "$f"; then
    echo "::error::$f lacks the read-from-disk pointer — a stub that does not point is a restatement risk."
    bad=1
  fi
done
exit $bad
