#!/usr/bin/env bash
#
# postgres-log-verbosity-guard.sh — the postgres service must run with
# `log_error_verbosity=terse`, so a constraint violation cannot write its DETAIL line
# into the container log.
#
# WHY THIS EXISTS. Postgres logs an ERROR together with its DETAIL, and DETAIL names the
# CONSTRAINT KEY VALUES — `Key (normalized_user_name)=(<a real email address>) already
# exists`. That lands in the container's json-file log, whose LIVE segment
# `jobbliggaren-logprune` cannot reach: the prune removes rotated segments only, so the live
# one is bounded by `max-size` and a write rate rather than by an age. `terse` is what keeps
# the values out, and it is one line in a compose `command:` list that any later edit can
# drop. Nothing else would report that: the heartbeat's P3 floor sees timers, and no other
# CI job reads this key. `security-auditor` graded the underlying finding a Major
# (2026-09-03, #1170); a remediation nothing pins is a remediation with a decay date.
#
# IT READS COMPOSE'S OWN RESOLVED MODEL, NOT THE FILE — the same rule
# `compose-loopback-guard.sh` exists to enforce, and for the same reason: a YAML parser has
# a spelling to miss, and `docker compose config` normalises every legal spelling of
# `command:` (string form, list form, quoted keys, flow sequences) to one JSON array before
# the guard sees it. `--no-interpolate` is deliberate: the guard reads STRUCTURE, never
# values, so it must not require the project's secrets to be present in order to answer.
#
# THE PREDICATE IS "THE LAST ONE WINS", NOT "IT APPEARS SOMEWHERE". Postgres applies `-c`
# left to right, so `-c log_error_verbosity=terse … -c log_error_verbosity=default` runs
# with `default`. A guard that greps for the string passes that file while the box leaks.
# It therefore collects every setting of the key in order and asserts the LAST is `terse`.
#
# WHAT IT REFUSES RATHER THAN ANSWERS (exit 2 — the reader learns nothing, which is the
# honest outcome). A `postgresql.conf` mounted into the service, or a `PGOPTIONS` entry in
# its environment: both set the same GUC through a channel the command array does not show,
# so a verdict read off the command alone would overstate its own coverage. That is the
# #1198 defect this family of guards was written against.
#
# Exit codes:
#   0 = the resolved command sets log_error_verbosity=terse, and last
#   1 = it does not (the invariant is broken) — the message names what was found
#   2 = refused: the question could not be answered (bad args, no compose file, missing
#       tool, absent service, or a channel this guard cannot see)

set -euo pipefail

readonly SERVICE="postgres"
readonly SETTING="log_error_verbosity"
readonly REQUIRED="terse"

usage() {
  echo "usage: $0 [project-dir]" >&2
  echo "  project-dir defaults to the repo deploy/ directory." >&2
}

if [ "$#" -gt 1 ]; then
  usage
  exit 2
fi

case "${1:-}" in
  -h | --help)
    usage
    exit 2
    ;;
esac

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
dir="${1:-$repo_root/deploy}"

if [ ! -d "$dir" ]; then
  echo "::error::postgres-log-verbosity-guard: not a directory: $dir" >&2
  exit 2
fi

compose_base_names=(compose.yaml compose.yml docker-compose.yaml docker-compose.yml)
has_base=0
for n in "${compose_base_names[@]}"; do
  if [ -f "$dir/$n" ]; then
    has_base=1
    break
  fi
done
if [ "$has_base" -eq 0 ]; then
  echo "::error::postgres-log-verbosity-guard: no compose file in $dir" >&2
  echo "Refused rather than answered. Compose walks UP when a directory carries none of" >&2
  echo "${compose_base_names[*]}, so the verdict would be about an ancestor project." >&2
  exit 2
fi

# The caller's COMPOSE_* environment can select a different file or profile set, which would
# make the verdict about a model the repo does not carry.
for v in "${!COMPOSE_@}"; do unset "$v"; done

command -v docker >/dev/null 2>&1 || {
  echo "::error::postgres-log-verbosity-guard: docker not on PATH." >&2
  exit 2
}
command -v jq >/dev/null 2>&1 || {
  echo "::error::postgres-log-verbosity-guard: jq not on PATH." >&2
  exit 2
}

model=$(cd "$dir" && docker compose config --no-interpolate --format json 2>/dev/null) || {
  echo "::error::postgres-log-verbosity-guard: 'docker compose config' failed in $dir" >&2
  echo "Refused rather than answered: an unresolvable project has no command array to read." >&2
  exit 2
}

if [ "$(printf '%s' "$model" | jq -r --arg s "$SERVICE" 'has("services") and (.services | has($s))')" != "true" ]; then
  echo "::error::postgres-log-verbosity-guard: no '$SERVICE' service in the resolved model of $dir" >&2
  exit 2
fi

# --- refusals: channels that set the same GUC and that the command array does not show ----

# ⚠ THESE REFUSALS ARE THE HALF A SPELLING CAN DEFEAT, so each reads every shape compose
# emits rather than the one this repo happens to write. Measured 2026-09-03 against Compose
# v2.40.3: `environment` in LIST form stays a JSON array (compose does not normalise it to an
# object), `env_file` is NOT resolved into `environment` at all, and a conf file can arrive
# through `configs:` instead of `volumes:`. Reading only the object form and only volumes
# answered `exit 0` on all three — which is the guard's own header defect, one section down.
conf_targets=$(printf '%s' "$model" | jq -r --arg s "$SERVICE" '
  ((.services[$s].volumes // []) + (.services[$s].configs // []))
  | map(if type == "object" then (.target // "") else tostring end)
  | map(select(test("postgresql[.]conf$|^/etc/postgresql")))
  | join(" ")')
if [ -n "$conf_targets" ]; then
  echo "::error::postgres-log-verbosity-guard: '$SERVICE' mounts a postgres config file ($conf_targets)." >&2
  echo "Refused rather than answered: a mounted conf sets the same GUC through a channel the" >&2
  echo "command array does not show, so a verdict read off the command would overstate itself." >&2
  exit 2
fi

pgoptions=$(printf '%s' "$model" | jq -r --arg s "$SERVICE" '
  (.services[$s].environment // {})
  | if type == "object" then (.PGOPTIONS // "" | tostring)
    elif type == "array" then (map(select(type == "string" and startswith("PGOPTIONS="))) | join(" "))
    else "" end')
if [ -n "$pgoptions" ] && [ "$pgoptions" != "null" ]; then
  echo "::error::postgres-log-verbosity-guard: '$SERVICE' sets PGOPTIONS in its environment." >&2
  echo "Refused rather than answered: PGOPTIONS carries GUCs the command array does not show." >&2
  exit 2
fi

env_files=$(printf '%s' "$model" | jq -r --arg s "$SERVICE" '
  (.services[$s].env_file // [])
  | if type == "array" then map(if type == "object" then (.path // "") else tostring end) | join(" ")
    else tostring end')
if [ -n "$env_files" ] && [ "$env_files" != "null" ]; then
  echo "::error::postgres-log-verbosity-guard: '$SERVICE' carries env_file ($env_files)." >&2
  echo "Refused rather than answered: compose does not resolve env_file into the model, so its" >&2
  echo "contents — PGOPTIONS among them — are invisible here. Presence alone is the refusal." >&2
  exit 2
fi

# --- the predicate ------------------------------------------------------------------------

# Compose normalises `command:` to an array. Postgres reaches the same GUC through THREE argv
# spellings and the last one wins across all of them, so all three are collected in order:
# `-c name=value` (two entries), `-cname=value` (one), and the LONG option `--name=value`,
# whose name postgres reads with hyphens and underscores interchangeably. Measured 2026-09-03
# (security-auditor): `-c log_error_verbosity=terse --log-error-verbosity=default` starts
# clean and runs with `default` — collecting only the `-c` forms answered OK on it.
values=$(printf '%s' "$model" | jq -r --arg s "$SERVICE" --arg k "$SETTING" '
  def norm: gsub("-"; "_");
  (.services[$s].command // [])
  | if type == "string" then (. / " ") else . end
  | . as $cmd
  | [ range(0; ($cmd | length)) as $i
      | $cmd[$i] as $tok
      | if $tok == "-c" and ($i + 1) < ($cmd | length)
             and (($cmd[$i+1] | split("=") | .[0] | norm) == $k)
             and ($cmd[$i+1] | contains("="))
        then ($cmd[$i+1] | split("=") | .[1:] | join("="))
        elif ($tok | startswith("-c")) and ($tok | contains("="))
             and (($tok | ltrimstr("-c") | split("=") | .[0] | norm) == $k)
        then ($tok | split("=") | .[1:] | join("="))
        elif ($tok | startswith("--")) and ($tok | contains("="))
             and (($tok | ltrimstr("-") | ltrimstr("-") | split("=") | .[0] | norm) == $k)
        then ($tok | split("=") | .[1:] | join("="))
        else empty end ]
  | join(" ")')
if [ -z "$values" ]; then
  echo "::error::postgres-log-verbosity-guard: '$SERVICE' does not set $SETTING." >&2
  echo "Expected '-c $SETTING=$REQUIRED' in the resolved command. Without it postgres runs the" >&2
  echo "default verbosity, and a constraint violation writes its DETAIL — the constraint KEY" >&2
  echo "VALUES — into the container log. See docs/runbooks/log-sink.md §4 and #1170." >&2
  exit 1
fi

# shellcheck disable=SC2086
set -- $values
last=${!#}

if [ "$last" != "$REQUIRED" ]; then
  echo "::error::postgres-log-verbosity-guard: '$SERVICE' runs with $SETTING=$last, not $REQUIRED." >&2
  echo "Settings found, in order: $values" >&2
  echo "Postgres applies -c left to right, so the LAST one is what runs." >&2
  exit 1
fi

if [ "$#" -gt 1 ]; then
  echo "postgres-log-verbosity-guard: OK — $SERVICE sets $SETTING $# times ($values); the last is $REQUIRED"
else
  echo "postgres-log-verbosity-guard: OK — $SERVICE sets $SETTING=$REQUIRED"
fi
