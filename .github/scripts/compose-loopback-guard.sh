#!/usr/bin/env bash
#
# compose-loopback-guard.sh — every published port in a compose file must bind to loopback.
#
# WHY THIS EXISTS, and why a comment was not enough. Until #1198 the dev compose file bound
# five of six published ports to `0.0.0.0` — including a Seq instance running with
# authentication disabled, which held email bodies in plaintext — WHILE THE FILE'S OWN
# COMMENT STATED IT WAS LOOPBACK-BOUND. That comment was wrong for months and no reader
# caught it. Human reading is measured, on this exact file, not to cover this class.
#
# IT READS COMPOSE'S OWN RESOLVED MODEL, NOT THE FILE. The predecessor was a YAML parser
# written in awk, and four review rounds each produced one more legal spelling it passed
# silently — a quoted key, `? ports`, a flow sequence, a long-form mapping. `docker compose
# config` normalises every one of those to a single shape before the guard sees it, so the
# guard has no YAML parser and therefore no spelling to miss. That is the whole reason it
# exists in this form.
#
# WHAT THE REBUILD MADE STRICTER, measured 2026-08-04/05 against Compose v2.40.3. Shapes the
# awk parser could only REFUSE (exit 2 — the reader learns nothing) are now CAUGHT (exit 1,
# naming service and port): `? ports`, quoted keys, flow sequences, long-form mappings, and
# `[::]:`. `include:` and `extends:` were refused as PORTS-OUT-OF-VIEW because their ports
# live in another file; compose resolves both and inlines the ports, so that refusal class is
# gone and those ports are now checked. `network_mode: host` is the one refusal that stays:
# it publishes through no `ports:` key at all, so a ports check is structurally blind to it.
#
# THE PREDICATE HAS TWO HALVES AND NEEDS BOTH:
#   - a bare `5435:5432`  -> the `host_ip` key is ABSENT (compose does not emit `0.0.0.0`)
#   - `0.0.0.0:5435:5432` -> `host_ip: "0.0.0.0"`, present and wide
# Checking only the value passes the first; checking only presence passes the second. So
# absence IS the violation, and so is a present-but-wide value.
#
# WHAT IT DECIDES AND WHAT IT REFUSES TO DECIDE — specific, not a universal:
#   IPv4 it decides in full. 127.0.0.0/8 is loopback (RFC 1122), so `127.0.0.2` passes; any
#   other dotted quad, `0.0.0.0` included, is a violation.
#   IPv6 it decides in exactly two spellings: `::1` passes and `::` is a violation. EVERY
#   OTHER IPv6 SPELLING IS REFUSED (exit 2), NOT JUDGED. Measured: compose does NOT normalise
#   `[0:0:0:0:0:0:0:1]` — it comes back verbatim, and that address IS loopback. Calling it
#   "not bound to loopback" would be asserting a fact this guard has not established, which
#   is #1198's own defect one level down. Deciding it needs IPv6 expansion the guard does not
#   do, so it says so instead of guessing.
#
# DO NOT REPLACE THAT PARAGRAPH WITH A UNIVERSAL. The predecessor's header claimed "refuses
# every shape it does not model" three times and was false three times. When a shape gets
# through, refuse it and leave this paragraph specific.
#
# IT PROVES NOTHING ABOUT WHAT IS LISTENING. ADR 0050 `Amendment 2026-08-04` §5's point
# "Ingen container publicerar till 0.0.0.0" requires a `curl` from outside at cutover, and
# that requirement is untouched here. "Did the file change back?" and "what is running?" are
# different questions and neither substitutes for the other.
#
# SHAPE-BASED, NOT NAME-BASED: no service names, no port numbers. A new service, port or
# rename is covered on arrival.
#
# THE RESIDUAL THIS REBUILD INTRODUCES, named rather than left implicit: the answer now
# depends on a compose CLI VERSION as well as on the file. Every normalisation above is
# behaviour of the binary on the runner, and nothing in this repo pins it — `ubuntu-latest`
# ships whatever Compose v2 it ships. A future compose could change a normalisation and this
# guard would follow it silently. That is a DIFFERENT risk from the one being retired (a
# parser that missed spellings), not a smaller version of it. Partial mitigation, not a fix:
# the version is printed on the OK line, so an answer that changed with the toolchain is
# readable from the CI log instead of having to be re-derived.
#
# `--expect-min N` IS LOAD-BEARING. Shape alone cannot tell "all ports are loopback-bound"
# from "I found no ports". With no floor given, a run recognising zero entries is REFUSED;
# `--expect-min 0` is the only way to say zero is expected. THE UNIT CHANGED WITH THIS
# REWRITE: compose expands `8000-8002:8000-8002` into three entries where the awk parser
# counted one written list item. The repo's file has no ranges, so its floor of 6 is
# unaffected — but the floor now counts published ports, not written lines.
#
# Usage:  bash .github/scripts/compose-loopback-guard.sh [--expect-min N] [compose-file ...]
#         (defaults to docker-compose.yml at the repo root)
#
# Exit 0 = every published port is loopback-bound and the floor is met.
# Exit 1 = a published port is not loopback-bound (service and port printed), OR the
#          --expect-min floor is not met. The second case names no port — the finding is an
#          absence.
# Exit 2 = the guard could not answer: compose refused the file, a tool is missing, a service
#          uses host networking, an entry compose left unresolved, a bind address it will not
#          judge, or a bad invocation. Deliberately NOT folded into exit 1 — "the guard could
#          not run" must never read as "the guard passed".

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

# A MISSING TOOL IS EXIT 2, NEVER A PASS. Checked before any file is read, so the failure
# names the tool rather than surfacing as an empty scan that looks clean. `docker compose
# version` is the one that matters: a host carrying only Compose v1 (`docker-compose`) has
# `docker` on PATH and cannot answer.
command -v docker >/dev/null 2>&1 || { echo "::error::compose-loopback-guard: docker not on PATH — cannot resolve the compose model." >&2; exit 2; }
command -v jq >/dev/null 2>&1 || { echo "::error::compose-loopback-guard: jq not on PATH." >&2; exit 2; }
compose_version=$(docker compose version --short 2>/dev/null) || {
  echo "::error::compose-loopback-guard: 'docker compose' unavailable (Compose v2 required)." >&2
  exit 2
}

errfile=$(mktemp)
trap 'rm -f "$errfile"' EXIT

# `--no-interpolate` so the guard needs no `.env`: it must gate the file in CI, where no
# secret exists, and the repo's compose file makes three variables hard `:?` requirements.
# Verified 2026-08-04 that no `ports:` value in this repo is interpolated, so nothing under
# ports is lost. It also runs with the daemon DOWN — `config` is client-side, and every
# measurement behind this file was taken that way.
#
# ONE INVOCATION PER FILE, deliberately: passing several `-f` to one invocation makes compose
# MERGE them with override semantics, which answers a different question than "is each of
# these files clean".
#
# Profile-gated services are included WITHOUT `--profile '*'` — measured on this repo's own
# file, whose `postgres-test` and `redis-test` sit behind the `test` profile and whose six
# ports are all counted. Their bindings were otherwise provable only by not running them.
violations=""
recognised=0

for f in "${files[@]}"; do
  if ! model=$(docker compose -f "$f" config --no-interpolate --format json 2>"$errfile"); then
    echo "::error::compose-loopback-guard: compose refused $f — the guard cannot answer." >&2
    cat "$errfile" >&2
    exit 2
  fi

  # AN ENTRY IS NOT ALWAYS A MAPPING, and the shapes that stay raw are not exotic. Measured:
  # an unexpanded `"127.0.0.1:${HOST_PORT}:5432"` under `--no-interpolate`, and a hostname
  # bind address `"localhost:9000:9000"`, both come back as the RAW STRING. The guard cannot
  # read a bind address compose did not resolve, so it refuses. Letting jq crash on it would
  # refuse too — with a message blaming the model instead of naming the entry.
  out=$(jq -r --arg f "$f" '
    def is_ipv4: (type == "string") and test("^[0-9]{1,3}(\\.[0-9]{1,3}){3}$");
    def is_loopback: (. == "::1") or (is_ipv4 and startswith("127."));
    def is_wide: (. == "::") or (is_ipv4 and (startswith("127.") | not));

    (.services // {}) as $svc
    | [ $svc | to_entries[]
        | select(.value.network_mode == "host")
        | "\($f): HOST-NETWORKING service=\(.key)" ]
    + [ $svc | to_entries[] as $s
        | ($s.value.ports // [])[]
        | select(type != "object")
        | "\($f): UNRESOLVED-ENTRY service=\($s.key) entry=\(. | tostring)" ]
    + [ $svc | to_entries[] as $s
        | ($s.value.ports // [])[]
        | select(type == "object")
        | select(has("host_ip") and (((.host_ip | is_loopback) or (.host_ip | is_wide)) | not))
        | "\($f): UNJUDGED-BIND-IP service=\($s.key) published=\(.published) host_ip=\(.host_ip)" ]
    + [ $svc | to_entries[] as $s
        | ($s.value.ports // [])[]
        | select(type == "object")
        | select((has("host_ip") | not) or (.host_ip | is_wide))
        | "\($f): NOT-LOOPBACK service=\($s.key) published=\(.published) host_ip=\(.host_ip // "<absent>")" ]
    | .[]
  ' <<<"$model") || { echo "::error::compose-loopback-guard: could not read the resolved model for $f." >&2; exit 2; }

  n=$(jq '[(.services // {})[] | (.ports // []) | length] | add // 0' <<<"$model")
  recognised=$((recognised + n))

  if [ -n "$out" ]; then
    violations=$(printf '%s%s\n' "$violations" "$out")
  fi
done

if [ -n "$violations" ]; then
  # "could not answer" is exit 2 and must never collapse into exit 1. The markers below all
  # mean the guard did not READ the binding, not that it read it and it was wide.
  #
  # HERESTRING, NOT A PIPE. `grep -q` leaves at its first match; a producer still holding
  # more than a pipe buffer then dies of SIGPIPE, and `pipefail` makes that the pipeline's
  # status — so the `if` went false on exactly the inputs it exists to catch, and the guard
  # printed "not bound to loopback" about entries it had only failed to read. Measured on the
  # awk predecessor (#1206), and the reason that guard's Blocker was a Blocker. Do not
  # restore the pipe; `classifier_survives_large_refusal_list` fails if it comes back.
  if grep -qE 'HOST-NETWORKING|UNRESOLVED-ENTRY|UNJUDGED-BIND-IP' <<<"$violations"; then
    echo "::error::compose-loopback-guard: a published port is in a state this guard will not judge." >&2
    echo "HOST-NETWORKING publishes outside ports: entirely. UNRESOLVED-ENTRY is an entry compose" >&2
    echo "left as a raw string (an unexpanded variable, a hostname bind address). UNJUDGED-BIND-IP" >&2
    echo "is an IPv6 spelling other than ::1 or :: — it may well BE loopback, and saying otherwise" >&2
    echo "would assert a fact the guard has not established. All are refused, never passed." >&2
    printf '%s\n' "$violations" >&2
    exit 2
  fi
  echo "::error::compose-loopback-guard: published port(s) not bound to loopback" >&2
  printf '%s\n' "$violations" >&2
  echo >&2
  echo "Every published port must be written 127.0.0.1:HOST:CONTAINER (#1198)." >&2
  echo "host_ip=<absent> means a bare HOST:CONTAINER — compose omits the key rather than" >&2
  echo "emitting 0.0.0.0, and that binds every interface, not only loopback." >&2
  exit 1
fi

if [ -z "$expect_min" ]; then
  # No floor given: zero recognised entries is refused rather than reported as clean.
  # "I found no ports, therefore all ports are fine" is vacuous truth wearing a clean bill of
  # health.
  if [ "$recognised" -eq 0 ]; then
    echo "::error::compose-loopback-guard: recognised ZERO published ports." >&2
    echo "Refused rather than reported clean. Pass --expect-min 0 if zero really is expected." >&2
    exit 2
  fi
elif [ "$recognised" -eq 0 ] && [ "$expect_min" -gt 0 ]; then
  # Refused here for the same reason as the no-floor branch. `--expect-min 0` is exempt on
  # purpose — it is the one way to STATE that zero is expected, and collapsing it into a
  # refusal would leave no way to say so at all.
  echo "::error::compose-loopback-guard: recognised ZERO published ports (floor was $expect_min)." >&2
  echo "Pass --expect-min 0 if zero really is expected here." >&2
  exit 2
elif [ "$recognised" -lt "$expect_min" ]; then
  echo "::error::compose-loopback-guard: expected at least $expect_min published port(s), recognised $recognised" >&2
  echo "Passing with fewer than expected means the ports moved or were deleted — not a clean bill." >&2
  exit 1
fi

echo "compose-loopback-guard: OK — $recognised published port(s), all loopback-bound (${#files[@]} file(s), Compose $compose_version)"
