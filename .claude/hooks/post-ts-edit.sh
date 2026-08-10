#!/usr/bin/env bash
set -u
TOOL_INPUT="${CLAUDE_TOOL_INPUT:-$(cat)}"
FILE=$(echo "$TOOL_INPUT" | jq -r '.file_path // empty' 2>/dev/null)

if [ -z "$FILE" ] || [[ "$FILE" != *.ts && "$FILE" != *.tsx ]]; then
    exit 0
fi

cd web/jobbliggaren-web 2>/dev/null || exit 0

# eslint --fix på berörd fil (ingen prettier — CLAUDE.md §4/§11)
npx eslint --fix "$FILE" >/dev/null 2>&1 || true

# Typecheck hela FE (snabb när vi använder --incremental)
if ! npx tsc --noEmit --incremental >/dev/null 2>&1; then
    echo "⚠ TypeScript-fel efter ändring i $FILE. Kör 'pnpm tsc --noEmit' för detaljer."
fi

exit 0
