#!/usr/bin/env bash
# agents-md-budget-guard.sh — BLOCKING (ADR 0135 Decision 2).
# Codex reads AGENTS.md natively under a combined project-doc cap
# (project_doc_max_bytes, 32,768 bytes default) and SILENTLY TRUNCATES past
# it — an unreadable failure mode, so the bound is enforced here, fail-closed.
# Bounds: root AGENTS.md <= 26000; root + web/jobbliggaren-web/AGENTS.md
# <= 28000 (margin for the block `next dev` rewrites in the web file).
# Regenerate: wc -c AGENTS.md web/jobbliggaren-web/AGENTS.md
set -euo pipefail
root="AGENTS.md"
web="web/jobbliggaren-web/AGENTS.md"
for f in "$root" "$web"; do
  if [ ! -f "$f" ]; then
    echo "::error::$f missing — the budget guard is fail-closed."
    exit 1
  fi
done
r=$(wc -c < "$root")
w=$(wc -c < "$web")
c=$((r + w))
echo "root=$r web=$w combined=$c (bounds: root<=26000, combined<=28000)"
if [ "$r" -gt 26000 ]; then
  echo "::error::AGENTS.md is $r bytes > 26000 (ADR 0135) — trim, or move a section to CLAUDE.md and update its §-index."
  exit 1
fi
if [ "$c" -gt 28000 ]; then
  echo "::error::combined AGENTS.md set is $c bytes > 28000 — Codex truncates silently past its cap."
  exit 1
fi
