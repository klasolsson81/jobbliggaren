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

# 6. Parallell-session collision-pre-flight (CLAUDE.md §6.5, Modell 1).
#    Två sessioner i SAMMA arbetskopia delar en HEAD/index → den enas
#    `git checkout` reverterar tyst den andras arbetsträd (incident 2026-06-28).
#    Modell 1: varje session jobbar i en EGEN c:/tmp-worktree, ALDRIG i
#    huvudkopian. Hooken ytlägger fleet-läget + varnar om du startas i
#    huvudkopian (där .git är en katalog; i en worktree är .git en fil).
echo ""
echo "== Parallella sessioner (git worktree list) =="
git worktree list 2>/dev/null || echo "  (ej ett git-repo)"
# Huvudkopia ⇔ git-dir == git-common-dir; en linked worktree (Path A inne i
# trädet ELLER Path B i c:/tmp) har en EGEN git-dir skild från common-dir.
# Robustare än `[ -d .git ]` (Path A-worktree under .claude/worktrees/ ligger
# inom huvudkopians träd) — dotnet-architect 2026-06-28.
_gitdir=$(git rev-parse --git-dir 2>/dev/null)
_commondir=$(git rev-parse --git-common-dir 2>/dev/null)
if [ -n "$_gitdir" ] && [ "$_gitdir" = "$_commondir" ]; then
    _branch=$(git branch --show-current 2>/dev/null)
    echo "⚠ Du står i HUVUDKOPIAN (branch: ${_branch:-?}). Modell 1: jobba ALDRIG"
    echo "  här — skapa en isolerad worktree FÖRST (annars rycker en annan sessions"
    echo "  checkout ditt arbetsträd):"
    echo "    git worktree add c:/tmp/jbl-<slug> origin/main -b <type>/<slug>"
    echo "    pwsh scripts/sync-worktree-docs.ps1 c:/tmp/jbl-<slug> && cd c:/tmp/jbl-<slug>"
    if [ -n "$_branch" ] && [ "$_branch" != "main" ]; then
        echo "⚠ HUVUDKOPIANS HEAD = '$_branch' (ej main) — en annan session äger den."
        echo "  Rör den INTE (ingen checkout/commit/stack-omstart här)."
    fi
fi

exit 0
