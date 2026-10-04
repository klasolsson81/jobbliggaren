#!/usr/bin/env bash
#
# package-retention-guard.sh — nothing in .github/workflows or .github/scripts deletes a container
# package or any of its versions (#1238).
#
# usage:  package-retention-guard.sh [<root>]
#
# WHY THIS EXISTS. A release record names image digests, and rolling back means pinning an older record
# (ADR 0149, R3). GHCR deletes nothing on its own (GitHub Docs, "Deleting and restoring a package", read
# 2026-10-03), so the rollback window is "every record since the first one" — for exactly as long as no
# automation deletes versions. That was a measurement on 2026-10-03 (no workflow or script did), and a
# measurement is not a rule. This makes it one: deleting a version is Klas's decision, taken by hand,
# never a workflow's.
#
# WHAT IT REFUSES, on every non-comment line of `.github/workflows/*.y*ml` and `.github/scripts/*.sh`
# (this guard and its suite excepted — they must name what they refuse):
#   - the `delete-package-versions` action, in any owner or version;
#   - a REST call that names a `/packages/` path — one version or the whole package — and a DELETE on
#     the same line (`gh api -X DELETE …/packages/…`, `curl -X DELETE …`, `--method DELETE`).
#
# THE EXIT CONTRACT IS THE HOUSE'S: 0 nothing deletes · 1 something does · 2 could not answer.
set -euo pipefail

ROOT="${1:-.}"
readonly ROOT

shopt -s nullglob
files=("$ROOT"/.github/workflows/*.yml "$ROOT"/.github/workflows/*.yaml "$ROOT"/.github/scripts/*.sh)
shopt -u nullglob
[ "${#files[@]}" -gt 0 ] || {
  echo "::error::package-retention-guard: could not answer — no workflow or script found under $ROOT/.github" >&2
  exit 2
}

found=0
for f in "${files[@]}"; do
  case "$(basename "$f")" in package-retention-guard.sh | package-retention-guard.test.sh) continue ;; esac
  [ -r "$f" ] || {
    echo "::error::package-retention-guard: could not answer — unreadable: $f" >&2
    exit 2
  }
  lineno=0
  while IFS= read -r raw || [ -n "$raw" ]; do
    lineno=$((lineno + 1))
    line=${raw%$'\r'}
    trimmed=${line#"${line%%[![:space:]]*}"}
    case "$trimmed" in "#"*) continue ;; esac
    hit=0
    case "$line" in *delete-package-versions*) hit=1 ;; esac
    if [[ $line == *"/packages/"* && $line =~ (^|[^A-Za-z])DELETE([^A-Za-z]|$) ]]; then hit=1; fi
    if [ "$hit" -eq 1 ]; then
      echo "::error file=$f,line=$lineno::package-retention-guard: this deletes a container package or its versions: ${trimmed}" >&2
      found=$((found + 1))
    fi
  done <"$f"
done

if [ "$found" -gt 0 ]; then
  echo "  A released record names its image digests; deleting a version can break a rollback (ADR 0149, R3)." >&2
  echo "  Deleting a package version is Klas's decision, made by hand — never automation." >&2
  exit 1
fi
echo "package-retention-guard: nothing under $ROOT/.github deletes a package or its versions (${#files[@]} files read)"
