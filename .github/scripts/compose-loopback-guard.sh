#!/usr/bin/env bash
#
# compose-loopback-guard.sh — every published port in docker-compose.yml must bind
# to 127.0.0.1.
#
# WHY THIS EXISTS, and why a comment was not enough. Until #1198 the dev compose file
# bound five of six published ports to `0.0.0.0` — including a Seq instance running with
# authentication disabled, which held email bodies in plaintext — WHILE THE FILE'S OWN
# COMMENT STATED IT WAS LOOPBACK-BOUND. That comment was wrong for months and no reader
# caught it. Human reading is measured, on this exact file, not to cover this class.
#
# WHAT IT DOES AND DOES NOT PROVE. It is a REGRESSION guard over the file: it fails if a
# published port ever loses its `127.0.0.1:` prefix again. It proves NOTHING about what is
# actually listening. ADR 0050 `Amendment 2026-08-04` §5's point "Ingen container publicerar
# till 0.0.0.0" demands a `curl` from outside at cutover, and that requirement is untouched
# by this script — the two answer different questions ("did the file change back?" vs "what
# is running?") and neither substitutes for the other.
#
# SHAPE-BASED, NOT NAME-BASED. It does not know service names, port numbers or how many
# entries there are; it finds `ports:` blocks by indentation and checks every list item in
# them. A new service, a new port, or a renamed service is covered on arrival — which is
# the property a hand-maintained list of expected ports would not have.
#
# Usage:  bash .github/scripts/compose-loopback-guard.sh [compose-file ...]
#         (defaults to docker-compose.yml at the repo root)
#
# Exit 0 = every published port is loopback-bound. Exit 1 = at least one is not, with the
# offending file:line printed. Exit 2 = a usage or input error, which is deliberately NOT
# folded into exit 1: "the guard could not run" must never read as "the guard passed".

set -euo pipefail

if [ "$#" -gt 0 ]; then
  files=("$@")
else
  script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
  repo_root=$(cd -- "$script_dir/../.." && pwd)
  files=("$repo_root/docker-compose.yml")
fi

for f in "${files[@]}"; do
  if [ ! -f "$f" ]; then
    echo "compose-loopback-guard: no such file: $f" >&2
    exit 2
  fi
done

# The scan, in awk so the indentation bookkeeping stays in one place:
#
#   * `ports:` at indent N opens a block.
#   * A list item (`- ...`) at indent > N inside that block is a port entry.
#   * Any non-blank, non-comment line at indent <= N closes the block. This is what keeps
#     `expose:`, `volumes:` and the next service from being read as port entries.
#   * Comments and blank lines inside the block are skipped, never treated as entries.
#
# A port entry passes only if its value begins `127.0.0.1:`. Long-form (`target:`/
# `published:`) mappings are NOT list items of this shape; if one is ever introduced the
# guard will not see it, so `exit 2` on an unrecognised entry shape is deliberate below —
# an unparseable entry is reported, never silently skipped.
violations=$(
  awk '
    function indent(s,   i) { i = match(s, /[^ ]/); return (i == 0) ? 0 : i - 1 }

    { line = $0; sub(/\r$/, "", line) }

    # blank or comment: never opens, closes, or is checked
    line ~ /^[[:space:]]*$/ { next }
    line ~ /^[[:space:]]*#/ { next }

    {
      ind = indent(line)

      if (in_ports && ind <= ports_indent) { in_ports = 0 }

      if (line ~ /^[[:space:]]*ports:[[:space:]]*(#.*)?$/) {
        in_ports = 1; ports_indent = ind; next
      }

      if (!in_ports) { next }

      if (line ~ /^[[:space:]]*-[[:space:]]/) {
        value = line
        sub(/^[[:space:]]*-[[:space:]]*/, "", value)     # strip the list marker
        sub(/[[:space:]]+#.*$/, "", value)               # strip a trailing comment
        gsub(/^["'"'"']|["'"'"']$/, "", value)           # strip surrounding quotes
        sub(/[[:space:]]+$/, "", value)

        if (value == "") { next }

        if (value !~ /^127\.0\.0\.1:/) {
          printf "%s:%d: %s\n", FILENAME, FNR, value
        }
        next
      }

      # Inside a ports: block but not a list item and not a comment — long-form mapping or
      # something this guard does not model. Report rather than skip.
      printf "%s:%d: UNPARSED-ENTRY %s\n", FILENAME, FNR, line
    }
  ' "${files[@]}"
)

if [ -n "$violations" ]; then
  if printf '%s\n' "$violations" | grep -q 'UNPARSED-ENTRY'; then
    echo "::error::compose-loopback-guard: an entry inside a ports: block was not recognised." >&2
    echo "The guard models the short-form list syntax only. Extend it rather than removing it." >&2
    printf '%s\n' "$violations" >&2
    exit 2
  fi
  echo "::error::compose-loopback-guard: published port(s) not bound to 127.0.0.1" >&2
  printf '%s\n' "$violations" >&2
  echo >&2
  echo "Every published port must be written 127.0.0.1:HOST:CONTAINER (#1198)." >&2
  echo "A bare HOST:CONTAINER binds 0.0.0.0 — reachable from the LAN, not only localhost." >&2
  exit 1
fi

echo "compose-loopback-guard: OK — every published port is loopback-bound (${#files[@]} file(s))"
