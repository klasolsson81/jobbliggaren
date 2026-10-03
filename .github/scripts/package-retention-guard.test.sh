#!/usr/bin/env bash
#
# Fixture tests for package-retention-guard.sh (#1238).
#
# Run:  bash .github/scripts/package-retention-guard.test.sh
#
# Each case writes a small `.github` tree; the last case runs the guard on the REAL repository, which is
# the one that protects the rollback window.
#
# THREE OUTCOMES, NEVER COLLAPSED: 0 nothing deletes · 1 something does · 2 could not answer.
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/package-retention-guard.sh"
repo_root=$(cd -- "$script_dir/../.." && pwd)
readonly repo_root

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

pass=0
fail=0

expect() {
  local want="$1" desc="$2" root="$3" got=0
  bash "$SUT" "$root" >"$TMPROOT/out" 2>&1 || got=$?
  if [ "$got" -eq "$want" ]; then
    pass=$((pass + 1))
    echo "  ok   $desc (exit $got)"
  else
    fail=$((fail + 1))
    echo "  FAIL $desc — wanted exit $want, got $got" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

tree() { # <name> <relative file> <content>
  local root="$TMPROOT/$1"
  mkdir -p "$root/.github/workflows" "$root/.github/scripts"
  printf 'name: ok\non: push\n' >"$root/.github/workflows/ok.yml"
  mkdir -p "$(dirname "$root/$2")"
  printf '%s\n' "$3" >"$root/$2"
  printf '%s' "$root"
}

echo "package-retention-guard.sh"
expect 0 "a tree that deletes nothing" "$(tree clean .github/scripts/x.sh 'echo hello')"
expect 1 "the delete-package-versions action" \
  "$(tree action .github/workflows/clean.yml '      - uses: actions/delete-package-versions@v5')"
expect 1 "a fork of the action under another owner" \
  "$(tree fork .github/workflows/clean.yml '      - uses: someone/delete-package-versions@main')"
expect 1 "gh api -X DELETE on a package version" \
  "$(tree gh .github/scripts/prune.sh 'gh api -X DELETE /user/packages/container/jobbliggaren-api/versions/123')"
expect 1 "curl --request DELETE on a package version" \
  "$(tree curl .github/scripts/prune.sh 'curl -sS --request DELETE https://api.github.com/user/packages/container/x/versions/9')"
expect 1 "--method DELETE in a workflow step" \
  "$(tree method .github/workflows/clean.yml '        run: gh api --method DELETE "/orgs/o/packages/container/x/versions/$ID"')"
expect 0 "a comment that names the action is not a deletion" \
  "$(tree comment .github/workflows/clean.yml '# never use delete-package-versions here')"
expect 0 "a DELETE of something that is not a package version" \
  "$(tree branch .github/scripts/b.sh 'gh api -X DELETE /repos/o/r/git/refs/heads/old')"
mkdir -p "$TMPROOT/none"
expect 2 "a tree with no .github cannot be answered" "$TMPROOT/none"
expect 0 "the REAL repository deletes no package versions" "$repo_root"

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ]
