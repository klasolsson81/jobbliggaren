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
# WHAT IT DOES AND DOES NOT PROVE, and this paragraph is deliberately narrow because an
# earlier version of it was FALSE. It is a REGRESSION guard over the file: it fails if a
# published port written in a block sequence — either indentation style — loses its
# `127.0.0.1:` prefix. Forms it does not model (flow sequences, aliases, long-form
# mappings, an empty block) are REFUSED with exit 2, never passed. It proves NOTHING about
# what is actually listening. ADR 0050 `Amendment 2026-08-04` §5's point "Ingen container publicerar
# till 0.0.0.0" demands a `curl` from outside at cutover, and that requirement is untouched
# by this script — the two answer different questions ("did the file change back?" vs "what
# is running?") and neither substitutes for the other.
#
# SHAPE-BASED, NOT NAME-BASED. It does not know service names or port numbers; it finds
# `ports:` blocks and checks every list item in them, in both indentation styles. A new
# service, a new port, or a renamed service is covered on arrival — which a hand-maintained
# list of expected ports would not be. `--expect-min N` adds the one thing shape alone
# cannot give: a floor, so "passed" cannot be satisfied by a file that stopped publishing.
#
# Usage:  bash .github/scripts/compose-loopback-guard.sh [--expect-min N] [compose-file ...]
#         (defaults to docker-compose.yml at the repo root)
#
# Exit 0 = every published port is loopback-bound. Exit 1 = at least one is not, with the
# offending file:line printed. Exit 2 = a usage or input error, which is deliberately NOT
# folded into exit 1: "the guard could not run" must never read as "the guard passed".

set -euo pipefail

expect_min=0
args=()
while [ "$#" -gt 0 ]; do
  case "$1" in
    --expect-min)
      [ "$#" -ge 2 ] || { echo "compose-loopback-guard: --expect-min needs a value" >&2; exit 2; }
      expect_min=$2; shift 2
      case "$expect_min" in (*[!0-9]*|"") echo "compose-loopback-guard: --expect-min must be a non-negative integer" >&2; exit 2;; esac
      ;;
    --) shift; args+=("$@"); break ;;
    *) args+=("$1"); shift ;;
  esac
done
set -- "${args[@]+"${args[@]}"}"

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

# The scan, in awk so the indentation bookkeeping stays in one place.
#
# THE ORDERING OF THE THREE TESTS BELOW IS THE WHOLE CONTROL, and an earlier version of
# this script got it wrong in a way that returned SILENT GREEN on a 0.0.0.0 binding. It
# closed the block on `indent <= ports_indent` BEFORE testing for a list item — so YAML's
# perfectly legal flush form
#
#     ports:
#     - "5435:5432"
#
# closed the block and the entry was never checked. `exit 0`, fixtures 11/11, redis on
# 0.0.0.0. Measured on the real file, not hypothesised. A list item is therefore tested
# FIRST, and only a NON-list-item can close a block.
#
#   * `ports:` opens a block at indent N. Anything after the colon other than a comment —
#     a flow sequence (`ports: ["5435:5432"]`), an alias (`ports: *p`) — is a form this
#     guard does not model, so it is reported and exits 2. Never 0.
#   * A list item (`- ...`) at indent >= N is a port entry. Flush and indented forms both.
#   * A NON-list-item at indent <= N closes the block. That is what keeps `volumes:`,
#     `healthcheck:` and the next service out.
#   * A NON-list-item at indent > N is a long-form mapping or something else unmodelled:
#     reported, exit 2.
#   * A block that closes having recognised ZERO entries is itself reported — an empty or
#     unreadable `ports:` is not a passing `ports:`.
#   * Comments and blank lines are never entries, never open and never close a block.
#
# A port entry passes only if its value begins `127.0.0.1:`.
violations=$(
  awk '
    function indent(s,   i) { i = match(s, /[^ ]/); return (i == 0) ? 0 : i - 1 }
    function close_block() {
      if (in_ports && entries == 0) {
        printf "%s:%d: EMPTY-PORTS-BLOCK (opened here, no entry recognised)\n", FILENAME, ports_line
      }
      in_ports = 0
    }

    FNR == 1 { close_block() }   # a block cannot span files

    { line = $0; sub(/\r$/, "", line) }

    line ~ /^[[:space:]]*$/ { next }
    line ~ /^[[:space:]]*#/ { next }

    {
      ind = indent(line)
      is_item = (line ~ /^[[:space:]]*-[[:space:]]/)

      # 1. entry test FIRST — a list item at or below the key indent still belongs to it
      if (in_ports && is_item && ind >= ports_indent) {
        value = line
        sub(/^[[:space:]]*-[[:space:]]*/, "", value)
        sub(/[[:space:]]+#.*$/, "", value)
        gsub(/^["'"'"']|["'"'"']$/, "", value)
        sub(/[[:space:]]+$/, "", value)

        if (value == "") {
          printf "%s:%d: UNPARSED-ENTRY (empty list item)\n", FILENAME, FNR
          next
        }
        entries++
        if (value !~ /^127\.0\.0\.1:/) {
          printf "%s:%d: %s\n", FILENAME, FNR, value
        }
        next
      }

      # 2. only a NON-list-item may close the block
      if (in_ports && !is_item && ind <= ports_indent) { close_block() }

      # 3. a new block, and anything trailing the colon is a form we do not model
      if (line ~ /^[[:space:]]*ports:/) {
        rest = line
        sub(/^[[:space:]]*ports:/, "", rest)
        sub(/[[:space:]]*#.*$/, "", rest)
        sub(/^[[:space:]]+/, "", rest); sub(/[[:space:]]+$/, "", rest)
        if (rest != "") {
          printf "%s:%d: UNPARSED-PORTS-FORM %s\n", FILENAME, FNR, line
          in_ports = 0
          next
        }
        in_ports = 1; ports_indent = ind; ports_line = FNR; entries = 0
        next
      }

      if (!in_ports) { next }

      # 4. inside a block, not a list item, deeper than the key: long-form or unknown
      printf "%s:%d: UNPARSED-ENTRY %s\n", FILENAME, FNR, line
    }

    END { close_block() }
  ' "${files[@]}"
)

# How many entries the scan actually RECOGNISED. `--expect-min` turns "the file passed"
# into "the file passed and still publishes at least N ports", which a file with the ports
# block deleted, renamed or restructured cannot satisfy vacuously. A pin that its subject
# can satisfy by not existing pins nothing.
recognised=$(
  awk '
    function indent(s,   i) { i = match(s, /[^ ]/); return (i == 0) ? 0 : i - 1 }
    { line = $0; sub(/\r$/, "", line) }
    line ~ /^[[:space:]]*$/ { next }
    line ~ /^[[:space:]]*#/ { next }
    {
      ind = indent(line); is_item = (line ~ /^[[:space:]]*-[[:space:]]/)
      if (in_ports && is_item && ind >= ports_indent) {
        v = line; sub(/^[[:space:]]*-[[:space:]]*/, "", v); sub(/[[:space:]]+#.*$/, "", v)
        if (v != "") n++
        next
      }
      if (in_ports && !is_item && ind <= ports_indent) { in_ports = 0 }
      if (line ~ /^[[:space:]]*ports:/) {
        rest = line; sub(/^[[:space:]]*ports:/, "", rest); sub(/[[:space:]]*#.*$/, "", rest)
        sub(/^[[:space:]]+/, "", rest); sub(/[[:space:]]+$/, "", rest)
        if (rest != "") { in_ports = 0; next }
        in_ports = 1; ports_indent = ind; next
      }
    }
    END { print n + 0 }
  ' "${files[@]}"
)

if [ -n "$violations" ]; then
  # "could not answer" is exit 2 and must never collapse into exit 1. All three markers
  # below mean the guard did not READ the ports, not that it read them and they were bad.
  if printf '%s
' "$violations" | grep -qE 'UNPARSED-ENTRY|UNPARSED-PORTS-FORM|EMPTY-PORTS-BLOCK'; then
    echo "::error::compose-loopback-guard: a ports: block was in a form this guard does not model." >&2
    echo "It reads block sequences in BOTH indentation styles. Flow sequences (ports: [...])," >&2
    echo "aliases (ports: *x), long-form mappings and empty blocks are REFUSED, never skipped." >&2
    echo "Extend the guard rather than removing it." >&2
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

if [ "$recognised" -lt "$expect_min" ]; then
  echo "::error::compose-loopback-guard: expected at least $expect_min published port(s), recognised $recognised" >&2
  echo "Passing with fewer than expected means the ports moved, were deleted, or were" >&2
  echo "written in a form this guard does not model — none of which is a clean bill." >&2
  exit 1
fi

echo "compose-loopback-guard: OK — $recognised published port(s), all loopback-bound (${#files[@]} file(s))"
