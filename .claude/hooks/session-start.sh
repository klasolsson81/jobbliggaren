#!/usr/bin/env bash
set -u

# 1. Docker-status
if ! docker info >/dev/null 2>&1; then
    echo "⚠ Docker körs inte. Starta Docker Desktop för att kunna köra tester."
elif ! docker compose ps --format json 2>/dev/null | grep -q '"State":"running"'; then
    echo "ℹ Docker Compose-tjänster är nere. Kör 'docker compose up -d' för dev-miljön."
else
    echo "✓ Docker Compose-tjänster uppe."
fi

# 2. .env-fil
if [ ! -f .env ]; then
    echo "⚠ .env saknas i repo-roten. Kopiera från .env.example om du inte redan gjort det."
else
    echo "✓ .env finns."
fi

# 3. Uncommitted changes från förra sessionen
if ! git diff --quiet 2>/dev/null || ! git diff --cached --quiet 2>/dev/null; then
    echo "⚠ Oparsade ändringar finns från förra sessionen — kolla 'git status' innan du börjar."
    git status --short | head -10
fi

# 4. current-work.md
if [ -f docs/current-work.md ]; then
    echo ""
    echo "== docs/current-work.md (senaste session) =="
    head -40 docs/current-work.md
else
    echo "ℹ docs/current-work.md finns inte än — skapas vid första /session-end."
fi

# 5. Frontend node_modules-drift mot pin/lockfile
#    Lokal regressions-audit 2026-06-07: en detached dev-server körde på stale
#    node_modules (next 16.2.4) medan lockfilen bumpats (16.2.7, Dependabot) —
#    jest-worker-render-barnen kraschade på uncachade routes och maskerade felet
#    som "Jest worker encountered N child process exceptions". Icke-blockerande
#    parity-check (Twelve-Factor §10): jämför installerad next mot package.json-pin.
WEB_DIR="web/jobbpilot-web"
if [ -f "$WEB_DIR/package.json" ] && [ -f "$WEB_DIR/node_modules/next/package.json" ]; then
    declared=$(grep -oE '"next"[[:space:]]*:[[:space:]]*"[^"]+"' "$WEB_DIR/package.json" | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)
    installed=$(grep -oE '"version"[[:space:]]*:[[:space:]]*"[^"]+"' "$WEB_DIR/node_modules/next/package.json" | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)
    if [ -n "$declared" ] && [ -n "$installed" ] && [ "$declared" != "$installed" ]; then
        echo ""
        echo "⚠ Frontend dep-drift: next i node_modules ($installed) ≠ package.json-pin ($declared)."
        echo "  Kör 'pnpm install' i $WEB_DIR och starta om 'pnpm dev' — en stale dev-worker"
        echo "  ger maskerade RSC-krascher (\"Jest worker ... child process exceptions\")."
    else
        echo "✓ Frontend node_modules i synk med next-pin (${declared:-okänd})."
    fi
fi

exit 0
