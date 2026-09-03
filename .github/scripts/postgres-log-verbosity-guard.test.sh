#!/usr/bin/env bash
#
# postgres-log-verbosity-guard.test.sh — fixtures for the guard, plus the repo's own project.
#
# THE CONTROL CROSSES THE THRESHOLD IN BOTH DIRECTIONS. A guard that only ever sees a passing
# file measures nothing: it cannot be told from `exit 0`. Every invariant case here has a
# negative twin that must FAIL, and the refusal cases assert exit 2 rather than 1 — "the
# question was never answered" and "the answer is no" are different results and a suite that
# collapses them would pass a guard that had gone blind.
#
# THE OVERRIDE CASE IS THE ONE A STRING GREP WOULD MISS. `-c log_error_verbosity=terse` followed
# by `-c log_error_verbosity=default` contains the required string and runs with `default`,
# because postgres applies -c left to right. It is the reason the guard reads the LAST setting.
#
# READ THE SUMMARY LINE, never `$?` after a pipe: `bash …test.sh | tail` reports tail's status.

set -uo pipefail

GUARD="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/postgres-log-verbosity-guard.sh"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TMPROOT="$(mktemp -d)"
trap 'rm -rf "$TMPROOT"' EXIT

pass=0
fail=0

ok() {
  pass=$((pass + 1))
  echo "  PASS  $1"
}
no() {
  fail=$((fail + 1))
  echo "  FAIL  $1"
}

# assert_exit <expected> <label> <dir>
assert_exit() {
  local expected=$1 label=$2 dir=$3 actual out
  out=$(bash "$GUARD" "$dir" 2>&1)
  actual=$?
  if [ "$actual" -eq "$expected" ]; then
    ok "$label (exit $actual)"
  else
    no "$label — expected exit $expected, got $actual"
    echo "        $out" | head -3
  fi
}

# assert_message <needle> <label> <dir>
assert_message() {
  local needle=$1 label=$2 dir=$3 out
  out=$(bash "$GUARD" "$dir" 2>&1)
  if printf '%s' "$out" | grep -q -- "$needle"; then
    ok "$label"
  else
    no "$label — output did not contain: $needle"
    echo "        $out" | head -3
  fi
}

# fixture <name> — body on stdin; sets FIXTURE_DIR (not echoed: a heredoc inside $() warns)
fixture() {
  local name=$1
  local dir="$TMPROOT/$name"
  mkdir -p "$dir"
  {
    echo "services:"
    echo "  postgres:"
    echo "    image: postgres:18.3"
    cat
  } >"$dir/docker-compose.yml"
  FIXTURE_DIR="$dir"
}

echo "postgres-log-verbosity-guard — fixtures"
echo

# --- the invariant holds -------------------------------------------------------------------

fixture list-form <<'YML'
    command:
      - postgres
      - -c
      - log_error_verbosity=terse
YML
assert_exit 0 "list form, two argv entries" "$FIXTURE_DIR"

fixture joined-form <<'YML'
    command:
      - postgres
      - -clog_error_verbosity=terse
YML
assert_exit 0 "joined form (-cname=value, one argv entry)" "$FIXTURE_DIR"

fixture string-form <<'YML'
    command: postgres -c log_error_verbosity=terse
YML
assert_exit 0 "string form (compose splits it)" "$FIXTURE_DIR"

fixture among-others <<'YML'
    command:
      - postgres
      - -c
      - shared_buffers=640MB
      - -c
      - log_error_verbosity=terse
      - -c
      - max_wal_size=4GB
YML
assert_exit 0 "set among other -c flags" "$FIXTURE_DIR"

# --- the invariant is broken ---------------------------------------------------------------

fixture absent <<'YML'
    command:
      - postgres
      - -c
      - shared_buffers=640MB
YML
assert_exit 1 "NOT set at all" "$FIXTURE_DIR"
assert_message "does not set log_error_verbosity" "absent: message names the missing setting" "$FIXTURE_DIR"

fixture no-command <<'YML'
    environment:
      POSTGRES_PASSWORD: x
YML
assert_exit 1 "no command at all" "$FIXTURE_DIR"

fixture wrong-value <<'YML'
    command:
      - postgres
      - -c
      - log_error_verbosity=default
YML
assert_exit 1 "set to default" "$FIXTURE_DIR"

fixture verbose-value <<'YML'
    command:
      - postgres
      - -c
      - log_error_verbosity=verbose
YML
assert_exit 1 "set to verbose" "$FIXTURE_DIR"

# THE CASE A STRING GREP PASSES AND THE BOX STILL LEAKS.
fixture overridden-later <<'YML'
    command:
      - postgres
      - -c
      - log_error_verbosity=terse
      - -c
      - log_error_verbosity=default
YML
assert_exit 1 "terse OVERRIDDEN by a later default (the grep-passes case)" "$FIXTURE_DIR"
assert_message "the LAST one is what runs" "override: message explains left-to-right" "$FIXTURE_DIR"

# ...and the same shape in the order that is fine.
fixture overridden-to-terse <<'YML'
    command:
      - postgres
      - -c
      - log_error_verbosity=default
      - -c
      - log_error_verbosity=terse
YML
assert_exit 0 "default overridden BY a later terse" "$FIXTURE_DIR"

# --- refusals: exit 2, not 1 ---------------------------------------------------------------

fixture conf-mounted <<'YML'
    command:
      - postgres
      - -c
      - log_error_verbosity=terse
    volumes:
      - ./postgresql.conf:/etc/postgresql/postgresql.conf
YML
assert_exit 2 "REFUSES when a postgresql.conf is mounted" "$FIXTURE_DIR"

fixture pgoptions <<'YML'
    command:
      - postgres
      - -c
      - log_error_verbosity=terse
    environment:
      PGOPTIONS: "-c log_error_verbosity=verbose"
YML
assert_exit 2 "REFUSES when PGOPTIONS is set" "$FIXTURE_DIR"

mkdir -p "$TMPROOT/no-service"
cat >"$TMPROOT/no-service/docker-compose.yml" <<'YML'
services:
  redis:
    image: redis:8.6-alpine
YML
assert_exit 2 "REFUSES when there is no postgres service" "$TMPROOT/no-service"

mkdir -p "$TMPROOT/empty"
assert_exit 2 "REFUSES when the directory holds no compose file" "$TMPROOT/empty"

assert_exit 2 "REFUSES a path that is not a directory" "$TMPROOT/empty/nope"

if [ "$(bash "$GUARD" a b >/dev/null 2>&1; echo $?)" -eq 2 ]; then
  ok "REFUSES more than one argument (exit 2)"
else
  no "REFUSES more than one argument"
fi

# --- the spellings a -c-only collector misses (measured 2026-09-03) -------------------------

fixture long-option <<'YML'
    command:
      - postgres
      - --log-error-verbosity=terse
YML
assert_exit 0 "LONG option form (--name=value, hyphens)" "$FIXTURE_DIR"

# security-auditor measured this exact file starting clean and running with `default`.
fixture long-option-overrides <<'YML'
    command:
      - postgres
      - -c
      - log_error_verbosity=terse
      - --log-error-verbosity=default
YML
assert_exit 1 "-c terse OVERRIDDEN by a long-option default (mixed spelling)" "$FIXTURE_DIR"

fixture long-option-underscores <<'YML'
    command:
      - postgres
      - --log_error_verbosity=default
YML
assert_exit 1 "LONG option with underscores, set to default" "$FIXTURE_DIR"

# --- refusal channels a single-shape reader misses ------------------------------------------

# dotnet-architect measured: compose does NOT normalise list-form environment to an object.
fixture env-list-form <<'YML'
    command:
      - postgres
      - -c
      - log_error_verbosity=terse
    environment:
      - PGOPTIONS=-c log_error_verbosity=verbose
YML
assert_exit 2 "REFUSES PGOPTIONS in LIST-form environment" "$FIXTURE_DIR"

# compose does not resolve env_file into the model, so its contents are invisible here.
fixture env-file-present <<'YML'
    command:
      - postgres
      - -c
      - log_error_verbosity=terse
    env_file:
      - ./pg.env
YML
assert_exit 2 "REFUSES when env_file is present (contents unread)" "$FIXTURE_DIR"

fixture conf-via-configs <<'YML'
    command:
      - postgres
      - -c
      - log_error_verbosity=terse
    configs:
      - source: pgconf
        target: /etc/postgresql/postgresql.conf
YML
assert_exit 2 "REFUSES a conf delivered through configs: rather than volumes:" "$FIXTURE_DIR"

# --- the repo's own project ----------------------------------------------------------------

echo
echo "postgres-log-verbosity-guard — the repo's own deploy project"
assert_exit 0 "deploy/ satisfies the invariant" "$REPO_ROOT/deploy"

echo
echo "----------------------------------------"
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
