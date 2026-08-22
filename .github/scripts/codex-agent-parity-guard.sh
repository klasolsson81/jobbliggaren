#!/usr/bin/env bash
# codex-agent-parity-guard.sh — BLOCKING (ADR 0135 Amendment 2 / CTO 2026-08-22).
# SET parity, not text drift: every tracked charter (.claude/agents/*.md) and
# every tracked skill (.claude/skills/*/SKILL.md) has a pointer stub in
# .codex/agents/ — both sets DERIVED from the index, nothing hardcoded — so a
# Codex-run panel cannot silently lack a reviewer or a canonical spec while
# reporting complete. Charter/skill TEXT has one home; stubs only point, and
# each stub must point at ITS OWN source. Fail-closed: empty sides error.
# Regenerate: bash .github/scripts/codex-agent-parity-guard.sh
set -euo pipefail
mapfile -t charters < <(git ls-files -- '.claude/agents/*.md' | xargs -n1 basename 2>/dev/null | sed 's/\.md$//' | sort)
mapfile -t skills < <(git ls-files -- '.claude/skills/*/SKILL.md' | awk -F/ '{print $3}' | sort)
mapfile -t stubs < <(git ls-files -- '.codex/agents/*.toml' | xargs -n1 basename 2>/dev/null | sed 's/\.toml$//' | sort)
if [ "${#charters[@]}" -eq 0 ] || [ "${#skills[@]}" -eq 0 ] || [ "${#stubs[@]}" -eq 0 ]; then
  echo "::error::empty set (charters=${#charters[@]} skills=${#skills[@]} stubs=${#stubs[@]}) — fail-closed."
  exit 1
fi
expected=$(printf '%s\n' "${charters[@]}" "${skills[@]}" | sort -u)
actual=$(printf '%s\n' "${stubs[@]}" | sort -u)
if [ "$expected" != "$actual" ]; then
  echo "::error::.codex/agents has drifted from .claude/agents + .claude/skills."
  echo "Remedy: add a pointer stub for each '<' name below; delete the orphan stub for each '>'."
  diff <(echo "$expected") <(echo "$actual") || true
  exit 1
fi
bad=0
for f in $(git ls-files -- '.codex/agents/*.toml'); do
  n=$(basename "$f" .toml)
  if ! grep -q '^sandbox_mode = "workspace-write"' "$f"; then
    echo "::error::$f lacks explicit sandbox_mode = \"workspace-write\" — a read-only reviewer writes no report and says nothing about it."
    bad=1
  fi
  if ! grep -q "\.claude/agents/$n\.md\|\.claude/skills/$n/SKILL\.md" "$f"; then
    echo "::error::$f does not point at its OWN source (.claude/agents/$n.md or .claude/skills/$n/SKILL.md)."
    bad=1
  fi
  if ! grep -q 'CHARTER UNREADABLE' "$f"; then
    echo "::error::$f lacks the fail-loud clause (CHARTER UNREADABLE) — a reviewer must stop, never review from nothing."
    bad=1
  fi
done
exit $bad
