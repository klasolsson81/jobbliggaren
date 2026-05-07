#!/usr/bin/env bash
#
# guard-spec-files.sh — PreToolUse hook
#
# Blocks Edit/Write/Bash operations on spec files (BUILD.md, CLAUDE.md, DESIGN.md)
# unless an approval token exists.
#
# Approval mechanism (file-based, works in both CLI and Agent SDK):
#   Create the token file before the edit:
#     bash .claude/hooks/approve-spec-edit.sh
#   The token is single-use — consumed on first approved edit.
#
# Legacy prompt-based approval (CLAUDE_USER_PROMPT) is retained as fallback
# for CLI mode where the variable is available.
#
# Bash-native JSON parsing (no jq dependency) — see ADR 0006 §4 for rationale.
#
# Exit codes:
#   0 = allow (file is not protected, OR approval token found)
#   2 = block (protected file, no approval token, OR JSON parse failure on
#       protected-file path)

set -u

TOOL_INPUT="${CLAUDE_TOOL_INPUT:-$(cat)}"

# If TOOL_INPUT is empty, nothing to check — allow
if [ -z "$TOOL_INPUT" ]; then
    exit 0
fi

# Bash-native file_path extraction (no jq).
# Handles both formats:
#   {"file_path": "..."}                                       (flat)
#   {"tool_input": {"file_path": "..."}, ...}                  (Claude Code real)
#
# Limitations (acceptable for our use case):
#   - Does not handle escaped quotes in values (filesystems disallow " anyway)
#   - Takes first file_path occurrence (sufficient for both supported formats)
extract_file_path() {
    local json="$1"
    echo "$json" \
        | grep -oE '"file_path"[[:space:]]*:[[:space:]]*"[^"]+"' \
        | head -1 \
        | sed -E 's/.*"file_path"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/'
}

# Bash-native command extraction for Bash tool inputs (e.g. sed -i CLAUDE.md).
extract_command() {
    local json="$1"
    echo "$json" \
        | grep -oE '"command"[[:space:]]*:[[:space:]]*"[^"]+"' \
        | head -1 \
        | sed -E 's/.*"command"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/'
}

FILE=$(extract_file_path "$TOOL_INPUT")
COMMAND=$(extract_command "$TOOL_INPUT")

# Determine target — file_path takes precedence; fall back to command for Bash tool
TARGET=""
if [ -n "$FILE" ]; then
    TARGET="$FILE"
elif [ -n "$COMMAND" ]; then
    # For Bash tool: check if command mentions a spec file
    if echo "$COMMAND" | grep -qE '(BUILD|CLAUDE|DESIGN)\.md'; then
        TARGET="$COMMAND"
    fi
fi

# If we extracted neither file_path nor relevant command, nothing to guard
if [ -z "$TARGET" ]; then
    exit 0
fi

# Approval token file — single-use, consumed on first approved edit
APPROVAL_TOKEN="${CLAUDE_PROJECT_DIR:-$(pwd)}/.claude/spec-edit-approved"

# Check if target references a protected spec file
case "$TARGET" in
    *BUILD.md*|*CLAUDE.md*|*DESIGN.md*)
        # Check file-based approval token first (works in CLI + Agent SDK)
        if [ -f "$APPROVAL_TOKEN" ]; then
            rm -f "$APPROVAL_TOKEN"
            exit 0
        fi
        # Fallback: prompt-based approval (CLI mode only — CLAUDE_USER_PROMPT set by CLI harness)
        LAST_PROMPT="${CLAUDE_USER_PROMPT:-}"
        if echo "$LAST_PROMPT" | grep -qiE \
            '(godkänt|approved|uppdatera.*(build|claude|design)\.md|fixa.*(build|claude|design)\.md|STEG [0-9]+.*(BUILD|CLAUDE|DESIGN)\.md)'; then
            # Approval phrase found — allow
            exit 0
        fi
        # No approval — block
        echo "[guard-spec-files] BLOCKED: $TARGET is a protected spec file." >&2
        echo "[guard-spec-files] To approve, run:" >&2
        echo "[guard-spec-files]   bash .claude/hooks/approve-spec-edit.sh" >&2
        echo "[guard-spec-files] Token is single-use and consumed on first approved edit." >&2
        exit 2
        ;;
esac

# Not a protected file — allow
exit 0
