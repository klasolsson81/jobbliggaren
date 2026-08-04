#!/usr/bin/env bash
#
# compose-loopback-guard.sh — every published port in docker-compose.yml must bind to
# localhost.
#
# WHY THIS EXISTS, and why a comment was not enough. Until #1198 the dev compose file bound
# five of six published ports to `0.0.0.0` — including a Seq instance running with
# authentication disabled, which held email bodies in plaintext — WHILE THE FILE'S OWN
# COMMENT STATED IT WAS LOOPBACK-BOUND. That comment was wrong for months and no reader
# caught it. Human reading is measured, on this exact file, not to cover this class.
#
# WHAT IT DOES AND DOES NOT PROVE. THIS PARAGRAPH HAS BEEN FALSE THREE TIMES, every time
# because it claimed a universal — "refuses every shape it does not model". Four review
# rounds each produced one more legal spelling it passed silently. So the universal is gone
# and what stands is what the mechanism delivers:
#
#   It reads `ports:` block sequences — both indentation styles, quoted or unquoted key —
#   fails on any entry there that is not loopback-bound, and refuses rather than passes the
#   entry shapes and port-relocating constructs it knows it cannot read. A LOOSE second
#   detector makes the KEY axis fail closed by DIVERGENCE rather than by enumeration, which
#   is the only part of this that generalises beyond the spellings already found.
#
# DO NOT RESTORE A UNIVERSAL HERE. When the next shape gets through, refuse it and leave
# this paragraph specific. A guard that overstates its coverage is precisely the defect
# #1198 was, and writing the stronger sentence is what kept reintroducing it.
#
# It proves NOTHING about what is actually listening: ADR 0050 `Amendment 2026-08-04` §5's point "Ingen container publicerar till
# 0.0.0.0" demands a `curl` from outside at cutover, and that requirement is untouched
# here. The two answer different questions — "did the file change back?" versus "what is
# running?" — and neither substitutes for the other.
#
# SHAPE-BASED, NOT NAME-BASED. It knows no service names and no port numbers; it finds
# `ports:` keys and checks every list item under them. A new service, a new port or a
# rename is covered on arrival, which a hand-maintained list of expected ports would not be.
#
# `--expect-min N` IS LOAD-BEARING, NOT A CONVENIENCE. Shape alone cannot tell "all ports
# are loopback-bound" from "I found no ports". The floor is what turns a pass into "and it
# still publishes at least N", and it is the backstop for any shape the parser misses. With
# no floor given, a run that recognises zero entries is REFUSED — `--expect-min 0` is the
# only way to say "zero is expected", and it must be said explicitly.
#
# Usage:  bash .github/scripts/compose-loopback-guard.sh [--expect-min N] [compose-file ...]
#         (defaults to docker-compose.yml at the repo root)
#
# Exit 0 = every published port is loopback-bound and the floor is met.
# Exit 1 = a published port is not loopback-bound (file:line printed), OR the --expect-min
#          floor is not met. The second case prints no file:line — there is no offending
#          line to point at; the finding is an absence.
# Exit 2 = the guard could not answer: a shape it does not model, or a bad invocation.
#          Deliberately NOT folded into exit 1 — "the guard could not run" must never read
#          as "the guard passed".

set -euo pipefail

expect_min=""
args=()
while [ "$#" -gt 0 ]; do
  case "$1" in
    --expect-min)
      [ "$#" -ge 2 ] || { echo "compose-loopback-guard: --expect-min needs a value" >&2; exit 2; }
      expect_min=$2; shift 2
      case "$expect_min" in
        (*[!0-9]*|"") echo "compose-loopback-guard: --expect-min must be a non-negative integer" >&2; exit 2 ;;
      esac
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

# ONE PARSER, ONE ANSWER. An earlier version ran a SECOND awk program to count entries for
# the floor, and the two had already diverged — which matters precisely because the floor
# is what catches shapes the parser misses. A backstop computed by a drifting copy of the
# thing it backs up is not a backstop. The scan emits its count on a marker line instead.
#
# THE ORDER OF THE TESTS IS THE WHOLE CONTROL. Getting it wrong returned SILENT GREEN on a
# 0.0.0.0 binding once already: the close test ran before the entry test, so a port at the
# same indentation as its `ports:` key closed the block instead of being checked. That is
# valid YAML and it is what `docker compose config` itself emits.
#
#   * `ports:`, quoted or not, opens a block. Anything after the colon other than a comment
#     — a flow sequence, an alias — is a shape this guard does not model: reported, exit 2.
#   * A list item at indent >= the key's is an entry. Both indentation styles.
#   * Only a NON-list-item at indent <= the key's closes a block.
#   * An entry that is not port-shaped is reported as UNPARSED, never as a violation.
#     Saying "not bound to 127.0.0.1" about a value the guard did not understand would be
#     asserting a fact it has not established.
#   * Loopback means `127.0.0.1:` or the IPv6 form `[::1]:`. Both are localhost.
#   * A block that closes having recognised zero entries is reported.
scan=$(
  awk '
    function indent(s,   i) { i = match(s, /[^ ]/); return (i == 0) ? 0 : i - 1 }

    # ONE NORMALISER, EVERY KEY AXIS. The guard has four of them — `ports`, `network_mode`,
    # `include`, `extends` — and each was originally spelled out on its own. That meant a
    # spelling fix landed on one axis and not the others: round 9 taught the `ports` axis
    # about quoted keys, and when two more axes arrived they did not inherit it, so
    # `"extends":` and `? network_mode` passed silently. Every key test now goes through
    # here, so the next spelling is one edit for all four rather than four edits or, more
    # likely, one.
    function keyname(s,   t) {
      t = s
      sub(/[[:space:]]+#.*$/, "", t)          # trailing comment
      sub(/^[[:space:]]*\?[[:space:]]+/, "", t)  # YAML explicit-key syntax
      gsub(/["'"'"']/, "", t)                 # quoted key
      sub(/[[:space:]]+$/, "", t)
      return t
    }

    function close_block() {
      if (in_ports && entries == 0) {
        printf "%s:%d: EMPTY-PORTS-BLOCK (opened here, no entry recognised)\n", ports_file, ports_line
      }
      in_ports = 0
    }

    FNR == 1 { close_block() }

    { line = $0; sub(/\r$/, "", line) }

    line ~ /^[[:space:]]*$/ { next }
    line ~ /^[[:space:]]*#/ { next }

    {
      ind = indent(line)
      is_item = (line ~ /^[[:space:]]*-[[:space:]]/)

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
        if (value !~ /^[0-9[]/) {
          printf "%s:%d: UNPARSED-ENTRY %s\n", FILENAME, FNR, value
          next
        }
        total++
        entries++
        # An IPv6 literal in a spelling this guard does not read — `[0:0:0:0:0:0:0:1]` is
        # genuine loopback — must not be reported as "not bound to localhost". That would
        # assert a fact the guard has not established, which is the thing it exists to stop.
        if (value ~ /^\[/ && value !~ /^\[::1\]:/) {
          printf "%s:%d: UNPARSED-ENTRY %s\n", FILENAME, FNR, value
          next
        }
        if (value !~ /^127\.0\.0\.1:/ && value !~ /^\[::1\]:/) {
          printf "%s:%d: %s\n", FILENAME, FNR, value
        }
        next
      }

      if (in_ports && !is_item && ind <= ports_indent) { close_block() }

      if (keyname(line) ~ /^[[:space:]]*ports[[:space:]]*:/) {
        rest = keyname(line)
        sub(/^[[:space:]]*ports[[:space:]]*:/, "", rest)
        sub(/[[:space:]]*#.*$/, "", rest)
        sub(/^[[:space:]]+/, "", rest); sub(/[[:space:]]+$/, "", rest)
        if (rest != "") {
          printf "%s:%d: UNPARSED-PORTS-FORM %s\n", FILENAME, FNR, line
          in_ports = 0
          next
        }
        in_ports = 1; ports_indent = ind; ports_line = FNR; ports_file = FILENAME; entries = 0; blocks++
        next
      }

      # THREE CONSTRUCTS MOVE PORTS OUT OF THIS GUARD FIELD OF VIEW. None is a spelling of
      # `ports:` — they are ways for a published port to exist that this file cannot see —
      # so each is refused rather than passed over in silence:
      #
      #   network_mode: host   publishes nothing through `ports:` at all, and is BROADER
      #                        than what #1198 closed: every port the process binds.
      #   include:             pulls in another compose file, whose ports are not here.
      #   extends:             inherits a service, ports included, from another file.
      #
      # `include:` and `extends:` are ordinary Compose, not exotica — and #196 adds compose
      # files, where splitting one with `include:` is exactly how it is done.
      #
      # The comment is stripped BEFORE matching: an anchored match without stripping let
      # `network_mode: host   # kör mot värdnätet` through, and trailing comments on such
      # lines are this repo own style.
      bare = keyname(line)

      # YAML explicit-key syntax puts the VALUE on the next line (`? network_mode` /
      # `: host`), so the key line carries no value to read. The guard cannot tell a host
      # network from a bridge one there — and its rule is to refuse what it cannot read,
      # not to guess.
      #
      # SPECIFIC, NOT UNIVERSAL — see the header. A key written `? name` on ONE line is
      # refused, on all four axes. `?` alone on its own line with the key on the NEXT is a
      # different shape and is NOT read: measured exit 0 for `network_mode`, `include` and
      # `extends`. `ports` survives it only because the loose divergence detector counts the
      # word and no block opens; the other three have no such backstop.
      if (line ~ /^[[:space:]]*\?[[:space:]]/ && bare ~ /^[[:space:]]*(ports|network_mode|include|extends)[[:space:]]*$/) {
        printf "%s:%d: UNREADABLE-EXPLICIT-KEY %s\n", FILENAME, FNR, bare
        next
      }

      if (bare ~ /^[[:space:]]*network_mode[[:space:]]*:[[:space:]]*host$/) {
        printf "%s:%d: HOST-NETWORKING %s\n", FILENAME, FNR, bare
        next
      }
      if (bare ~ /^[[:space:]]*(include|extends)[[:space:]]*:/) {
        printf "%s:%d: PORTS-OUT-OF-VIEW %s\n", FILENAME, FNR, bare
        next
      }

      if (!in_ports) { next }

      printf "%s:%d: UNPARSED-ENTRY %s\n", FILENAME, FNR, line
    }

    END {
      close_block()
      printf "##RECOGNISED##%d\n", total + 0
      printf "##BLOCKS##%d\n", blocks + 0
    }
  ' "${files[@]}"
)

# Markers are matched ANCHORED and digits-only. Unanchored, a file literally NAMED
# `##RECOGNISED##9.yml` would have its violation line swallowed by the filter — the guard
# would run, find the violation, and print OK. Unreachable from CI; fixed anyway.
recognised=$(printf '%s\n' "$scan" | sed -n 's/^##RECOGNISED##\([0-9][0-9]*\)$/\1/p' | tail -1)
blocks=$(printf '%s\n' "$scan" | sed -n 's/^##BLOCKS##\([0-9][0-9]*\)$/\1/p' | tail -1)
violations=$(printf '%s\n' "$scan" | grep -vE '^##(RECOGNISED|BLOCKS)##[0-9]+$' || true)

# THE KEY AXIS FAILS CLOSED, and this is how. Every other axis refuses what it cannot
# model; the KEY test could only ever miss SILENTLY, because a spelling that does not match
# simply never opens a block — no entry, no marker, no complaint. Round 9 added one
# alternation for quoted keys, which closed that instance and not the class: `? ports`
# (YAML explicit-key syntax, accepted by Compose) still passed with exit 0.
#
# So a SECOND, DELIBERATELY LOOSE detector counts anything that looks like a ports key, and
# **the divergence between the two counts is the signal**: loose > strict means a spelling
# reached Compose that the parser never opened.
#
# THIS IS NOT THE DOUBLE PARSER ROUND 9 REMOVED, and the difference is the whole point.
# That one computed the SAME quantity twice and could drift silently in either direction.
# This one computes a DIFFERENT quantity, and their EQUALITY is the invariant. Do not
# "simplify" it by making both strict — that reintroduces the blind spot it exists to find.
# `|| true` is load-bearing under `set -o pipefail`: grep -c exits 1 when a file has NO
# match, which killed the whole script with exit 1 on any compose file without a ports key.
# Exit 1 reads as "a violation was found" — so the failure mode was the guard reporting a
# violation it had not found. Caught by tracing the run rather than by reading it.
# Deliberately WORD-level, not colon-level: YAML's explicit-key syntax puts the colon on
# the NEXT line (`? ports` / `: - "..."`), and a colon-anchored detector missed it — which
# is how `? ports` passed with exit 0 in round 10. Comment lines are excluded so prose
# about ports does not inflate the count. Measured against the real file: exactly the five
# `ports:` keys, no false positives.
loose=$( { grep -hcE '^[[:space:]]*[^#]*(^|[^A-Za-z_.-])["'"'"']?ports["'"'"']?([^A-Za-z_.-]|$)' "${files[@]}" 2>/dev/null || true; } | awk '{ n += $1 } END { print n + 0 }')
if [ "${loose:-0}" -gt "${blocks:-0}" ]; then
  echo "::error::compose-loopback-guard: ${loose} ports-like key(s) present, but only ${blocks:-0} block(s) opened." >&2
  echo "A spelling reached Compose that this guard's key test does not recognise, so its" >&2
  echo "entries were never checked. Extend the key test rather than removing this one." >&2
  printf '%s\n' "$violations" >&2
  exit 2
fi

if [ -n "$violations" ]; then
  # "could not answer" is exit 2 and must never collapse into exit 1. The markers below all
  # mean the guard did not READ the ports, not that it read them and they were bad.
  #
  # HERESTRING, NOT A PIPE. `grep -q` leaves at its first match; a producer still holding
  # more than a pipe buffer then dies of SIGPIPE, and `pipefail` makes that the pipeline's
  # status — so the `if` went false on exactly the inputs it exists to catch. Do not
  # restore the pipe; `classifier_survives_large_violation_list` fails if it comes back.
  if grep -qE 'UNPARSED-ENTRY|UNPARSED-PORTS-FORM|EMPTY-PORTS-BLOCK|HOST-NETWORKING|PORTS-OUT-OF-VIEW|UNREADABLE-EXPLICIT-KEY' <<<"$violations"; then
    echo "::error::compose-loopback-guard: a ports: entry was in a shape this guard does not model." >&2
    echo "It reads block sequences in BOTH indentation styles. Flow sequences (ports: [...])," >&2
    echo "aliases (ports: *x), long-form mappings and empty blocks are REFUSED, never skipped." >&2
    echo "Extend the guard rather than removing it." >&2
    printf '%s\n' "$violations" >&2
    exit 2
  fi
  echo "::error::compose-loopback-guard: published port(s) not bound to localhost" >&2
  printf '%s\n' "$violations" >&2
  echo >&2
  echo "Every published port must be written 127.0.0.1:HOST:CONTAINER (#1198)." >&2
  echo "A bare HOST:CONTAINER binds 0.0.0.0 — reachable from the LAN, not only localhost." >&2
  exit 1
fi

if [ -z "$expect_min" ]; then
  # No floor given: zero recognised entries is refused rather than reported as clean.
  # "I found no ports, therefore all ports are fine" is vacuous truth wearing a clean bill
  # of health — the same shape EMPTY-PORTS-BLOCK exists to kill, one level up.
  if [ "$recognised" -eq 0 ]; then
    echo "::error::compose-loopback-guard: recognised ZERO published ports." >&2
    echo "That is refused rather than reported clean: it usually means the ports are written" >&2
    echo "in a shape this guard does not read, not that there are none. Pass --expect-min 0" >&2
    echo "if zero really is expected." >&2
    exit 2
  fi
elif [ "$recognised" -eq 0 ] && [ "$expect_min" -gt 0 ]; then
  # Zero recognised is refused here too, and for the same reason as the no-floor branch:
  # the guard cannot tell "no ports" from "read no ports", and a floor above zero says the
  # caller expected some. `--expect-min 0` is exempt on purpose — it is the one way to
  # STATE that zero is expected, and collapsing it into a refusal would leave no way to say
  # so at all.
  echo "::error::compose-loopback-guard: recognised ZERO published ports (floor was $expect_min)." >&2
  echo "Pass --expect-min 0 if zero really is expected here." >&2
  exit 2
elif [ "$recognised" -lt "$expect_min" ]; then
  echo "::error::compose-loopback-guard: expected at least $expect_min published port(s), recognised $recognised" >&2
  echo "Passing with fewer than expected means the ports moved, were deleted, or were" >&2
  echo "written in a shape this guard does not model — none of which is a clean bill." >&2
  exit 1
fi

echo "compose-loopback-guard: OK — $recognised published port(s), all loopback-bound (${#files[@]} file(s))"
