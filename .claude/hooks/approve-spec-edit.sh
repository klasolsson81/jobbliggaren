#!/usr/bin/env bash
#
# approve-spec-edit.sh — skapar single-use godkännande-token för guard-spec-files.sh
#
# Kör detta innan Claude Code editerar BUILD.md, CLAUDE.md eller DESIGN.md:
#   bash .claude/hooks/approve-spec-edit.sh
#
# Tokenfilen konsumeras automatiskt vid första godkända edit.

set -u

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TOKEN="${PROJECT_DIR}/.claude/spec-edit-approved"

touch "$TOKEN"
echo "[approve-spec-edit] Token skapad: $TOKEN"
echo "[approve-spec-edit] Nästa edit av BUILD.md / CLAUDE.md / DESIGN.md är godkänd (single-use)."
