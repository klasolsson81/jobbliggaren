#!/usr/bin/env bash
# agents-md-budget-guard.sh — BLOCKING (ADR 0135 Decision 2, bounds amended
# 2026-08-22 per C1's own derivation: 28,000 made the root bound unreachable).
# Codex reads every tracked AGENTS.md along root->cwd under one combined cap
# (project_doc_max_bytes, 32,768 bytes default) and SILENTLY TRUNCATES past
# it, so the bound is enforced here, fail-closed, over ALL tracked AGENTS.md.
# Regenerate: git ls-files '*AGENTS.md' | xargs wc -c
set -euo pipefail
ROOT_BOUND=26000
COMBINED_BOUND=30000
mapfile -t files < <(git ls-files -- 'AGENTS.md' '**/AGENTS.md')
if [ "${#files[@]}" -lt 2 ]; then
  echo "::error::expected >=2 tracked AGENTS.md, found ${#files[@]} — fail-closed."
  exit 1
fi
if [ ! -f "AGENTS.md" ]; then
  echo "::error::root AGENTS.md missing — fail-closed."
  exit 1
fi
r=$(wc -c < AGENTS.md)
c=0
for f in "${files[@]}"; do
  c=$((c + $(wc -c < "$f")))
done
echo "root=$r combined=$c over ${#files[@]} files (bounds: root<=$ROOT_BOUND, combined<=$COMBINED_BOUND)"
if [ "$((ROOT_BOUND + c - r))" -gt "$COMBINED_BOUND" ]; then
  echo "::error::bounds are inconsistent: root<=$ROOT_BOUND is unreachable when the other files hold $((c - r)) bytes."
  exit 1
fi
if [ "$r" -gt "$ROOT_BOUND" ]; then
  echo "::error::AGENTS.md is $r bytes > $ROOT_BOUND (ADR 0135) — trim, or move a section to CLAUDE.md and update its §-index."
  exit 1
fi
if [ "$c" -gt "$COMBINED_BOUND" ]; then
  echo "::error::combined AGENTS.md set is $c bytes > $COMBINED_BOUND — trim, or move a section to CLAUDE.md and update its §-index (Codex truncates silently at its own cap above this bound)."
  exit 1
fi
# Section-namespace integrity: every § lives in exactly one of the two spec
# files, and the union is the frozen 16-section set the citations resolve against.
a_secs=$(sed -n 's/^## \([0-9][0-9.]*\)[. ].*/\1/p' AGENTS.md | sed 's/\.$//' | sort -u)
c_secs=$(sed -n 's/^## \([0-9][0-9.]*\)[. ].*/\1/p' CLAUDE.md | sed 's/\.$//' | sort -u)
dup=$(comm -12 <(echo "$a_secs") <(echo "$c_secs"))
if [ -n "$dup" ]; then
  echo "::error::section(s) present in BOTH spec files: $dup"
  exit 1
fi
expected=$(printf '%s\n' 1 1.5 1.6 2 3 4 5 6 6.5 7 8 9 10 11 12 13 | sort -u)
actual=$(printf '%s\n%s\n' "$a_secs" "$c_secs" | sort -u)
if [ "$actual" != "$expected" ]; then
  echo "::error::spec section union drifted from the 16-section set. Diff:"
  diff <(echo "$expected") <(echo "$actual") || true
  exit 1
fi
