#!/usr/bin/env bash
#
# Fixture tests for jobbliggaren-observe-lib.sh and jobbliggaren-observe-backup.sh.
#
# Run:  bash deploy/systemd/jobbliggaren-observe.test.sh
#
# NEEDS NO DAEMON, NO ROOT AND NO NETWORK. `systemctl` is a stub whose output was copied from the
# box (systemd 257, Debian 13, 2026-10-10) rather than invented: the active timer, the disabled
# timer and the unit that does not exist. `date` is a pass-through that fixes only the one call
# the SUT makes to read "now", so sampledAt is a known string and a changed call shape fails
# loudly instead of drifting.
#
# THE EXPECTED FILES ARE GOLDEN FIXTURES, NOT STRINGS WRITTEN HERE. deploy/systemd/fixtures/
# observations/*.json is exactly what the collector publishes in each scenario, and the API's
# tests parse the same files. A token spelled differently on the two sides therefore fails a test
# instead of parsing as Failed in production. Regenerate after a deliberate contract change:
#   JBL_OBSERVE_WRITE_FIXTURES=1 bash deploy/systemd/jobbliggaren-observe.test.sh
# and read the diff: it is the contract change.
#
# WHAT THIS SUITE DOES NOT MEASURE: how systemd treats the unit's sandbox directives and its
# user. That needs a systemd, and is measured on the box by the runbook's first-start step.
#
# Mode cases need a filesystem that keeps POSIX modes. They run in CI on ubuntu, where
# JBL_REQUIRE_MODE_CASES=1 turns a skip into a failure.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly LIB="$script_dir/jobbliggaren-observe-lib.sh"
readonly COLLECTOR="$script_dir/jobbliggaren-observe-backup.sh"
readonly FIXTURES="$script_dir/fixtures/observations"
for f in "$LIB" "$COLLECTOR"; do
  [ -f "$f" ] || { echo "missing script under test: $f" >&2; exit 1; }
done
mkdir -p "$FIXTURES"

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'chmod -R u+rwx "$TMPROOT" 2>/dev/null || true; rm -rf "$TMPROOT"' EXIT

readonly OBS_DIR="$TMPROOT/run/jobbliggaren/observations"
readonly STAMP="$TMPROOT/var/lib/jobbliggaren/last-successful-backup"
readonly CALLS="$TMPROOT/calls"
readonly SUT_DIR="$TMPROOT/sut"
readonly FAKE_NOW="2026-10-10T13:41:02Z"

pass=0
fail=0
skipped=0
# Every golden file this run compared, or skipped with its reason on the line above; a file in the fixture
# directory that is in neither list was put there by hand, and the C# success paths would trust it unmeasured.
ACCOUNTED=()

# ---------------------------------------------------------------------------------------------
# The sandboxed PATH: wrappers (not symlinks — Git Bash coreutils resolve their DLLs relative to
# themselves) for exactly the externals the SUT uses, plus the two stubs.
# ---------------------------------------------------------------------------------------------
readonly BIN="$TMPROOT/bin"
mkdir -p "$BIN"
REAL_BASH=$(command -v bash)
REAL_DATE=$(command -v date)
readonly REAL_BASH REAL_DATE

for util in bash env stat head mktemp mv chmod rm timeout sleep; do
  path=$(command -v "$util" 2>/dev/null) || { echo "FIXTURE BROKEN: '$util' not found on this host" >&2; exit 1; }
  printf '#!/bin/sh\nexec %s "$@"\n' "'$path'" > "$BIN/$util"
  chmod +x "$BIN/$util"
done

cat > "$BIN/date" <<STUB
#!/bin/sh
# Only the bare "now" call is fixed; every other call is the real date.
if [ "\$#" -eq 2 ] && [ "\$1" = "-u" ] && [ "\$2" = "+%Y-%m-%dT%H:%M:%SZ" ]; then
  printf '%s\n' "$FAKE_NOW"
  exit 0
fi
exec '$REAL_DATE' "\$@"
STUB
chmod +x "$BIN/date"

# The first three outputs are verbatim `TZ=UTC LC_ALL=C systemctl show jobbliggaren-*.timer -p
# LoadState -p ActiveState -p NextElapseUSecRealtime`. systemd prints the keys in its own order,
# not the requested one, and the SUT must not depend on it.
cat > "$BIN/systemctl" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$CALLS/systemctl"
printf 'TZ=%s LC_ALL=%s\n' "${TZ:-}" "${LC_ALL:-}" >> "$CALLS/systemctl.env"
case "${SYSTEMCTL_MODE:-active}" in
  active)   printf 'NextElapseUSecRealtime=Sat 2026-10-10 13:49:41 UTC\nLoadState=loaded\nActiveState=active\n' ;;
  inactive) printf 'NextElapseUSecRealtime=\nLoadState=loaded\nActiveState=inactive\n' ;;
  notfound) printf 'NextElapseUSecRealtime=\nLoadState=not-found\nActiveState=inactive\n' ;;
  masked)   printf 'NextElapseUSecRealtime=\nLoadState=masked\nActiveState=inactive\n' ;;
  nonext)   printf 'NextElapseUSecRealtime=\nLoadState=loaded\nActiveState=active\n' ;;
  garbage)  printf 'NextElapseUSecRealtime=tomorrow at tea time\nLoadState=loaded\nActiveState=active\n' ;;
  zone)     printf 'NextElapseUSecRealtime=Sat 2026-10-10 15:49:41 CEST\nLoadState=loaded\nActiveState=active\n' ;;
  badstate) printf 'NextElapseUSecRealtime=\nLoadState=error\nActiveState=inactive\n' ;;
  injected) printf 'NextElapseUSecRealtime=Sat 2026-10-10 13:49:41 UTC","x":"y\nLoadState=loaded\nActiveState=active\n' ;;
  # A real systemctl is one process; a TERM from `timeout` must end this stub the same way, so the
  # sleep is a child it kills. Without that kill the stub answers `active` after 30 s, which would
  # read as a scheduled timer instead of an unknown one.
  hang)     trap 'kill "$pid" 2>/dev/null; exit 143' TERM
            sleep 30 & pid=$!; wait "$pid"
            printf 'NextElapseUSecRealtime=Sat 2026-10-10 13:49:41 UTC
LoadState=loaded
ActiveState=active
' ;;
  fail)     echo "Failed to connect to bus" >&2; exit 1 ;;
  *) echo "systemctl stub: unknown mode" >&2; exit 64 ;;
esac
STUB
chmod +x "$BIN/systemctl"

# ---------------------------------------------------------------------------------------------
# The SUT copy: both scripts side by side with their absolute paths redirected at the fixture.
# Every rewrite is proved, or the suite would measure the host.
# ---------------------------------------------------------------------------------------------
build_sut() {
  mkdir -p "$SUT_DIR"
  sed -e "s#^readonly OBSERVATIONS_DIR=.*#readonly OBSERVATIONS_DIR=$OBS_DIR#" "$LIB" > "$SUT_DIR/jobbliggaren-observe-lib.sh"
  sed -e "s#^readonly STAMP_FILE=.*#readonly STAMP_FILE=$STAMP#" \
      -e "s#^readonly SYSTEMCTL_TIMEOUT_SECONDS=.*#readonly SYSTEMCTL_TIMEOUT_SECONDS=4#" \
      "$COLLECTOR" > "$SUT_DIR/jobbliggaren-observe-backup.sh"
  local line
  for line in "readonly OBSERVATIONS_DIR=$OBS_DIR:jobbliggaren-observe-lib.sh" \
              "readonly STAMP_FILE=$STAMP:jobbliggaren-observe-backup.sh" \
              "readonly SYSTEMCTL_TIMEOUT_SECONDS=4:jobbliggaren-observe-backup.sh" \
              "readonly TIMER_UNIT=jobbliggaren-backup.timer:jobbliggaren-observe-backup.sh"; do
    grep -qxF "${line%:*}" "$SUT_DIR/${line##*:}" \
      || { echo "FIXTURE BROKEN: '${line%:*}' is not in ${line##*:}; the suite would measure the host" >&2; exit 1; }
  done
}
build_sut

reset_world() {
  rm -rf "$TMPROOT/run" "$TMPROOT/var" "$CALLS"
  mkdir -p "$OBS_DIR" "$(dirname "$STAMP")" "$CALLS"
  unset SYSTEMCTL_MODE
}

# A stamp the way jobbliggaren-backup.sh leaves one: the run start as its content, the run end as
# its mtime.
write_stamp() { printf '%s\n' "$1" > "$STAMP"; touch -d "$2" "$STAMP"; }

run_collector() {
  local -a omit=()
  while [ "${1:-}" = "--omit" ]; do omit+=("$2"); shift 2; done
  local runbin="$TMPROOT/runbin"
  rm -rf "$runbin"; mkdir -p "$runbin"
  local f base skip
  for f in "$BIN"/*; do
    base=$(basename "$f"); skip=no
    for o in "${omit[@]:-}"; do [ "$base" = "$o" ] && skip=yes; done
    [ "$skip" = yes ] || cp "$f" "$runbin/$base"
  done
  env -i PATH="$runbin" HOME="$TMPROOT" CALLS="$CALLS" TZ="Europe/Berlin" \
    SYSTEMCTL_MODE="${SYSTEMCTL_MODE:-active}" \
    "$REAL_BASH" "$SUT_DIR/jobbliggaren-observe-backup.sh" >"$TMPROOT/out" 2>&1
}

# 127 is "command not found": the shape a broken PATH takes, never a verdict of the SUT.
guard_not_127() {
  [ "$1" -ne 127 ] || {
    echo "FIXTURE BROKEN: '$2' exited 127. The sandboxed PATH is incomplete and this run measured nothing." >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
    exit 1
  }
}

check() {
  local desc="$1"; shift
  if "$@"; then pass=$((pass + 1)); echo "  ok   $desc"
  else
    fail=$((fail + 1)); echo "  FAIL $desc" >&2
    [ -f "$TMPROOT/out" ] && sed 's/^/       /' "$TMPROOT/out" >&2
    [ -f "$OBS_DIR/backup.json" ] && sed 's/^/       published: /' "$OBS_DIR/backup.json" >&2
  fi
}

# Runs the collector and holds it to the contract's two promises: it exits 0, and the published
# file is byte-for-byte the golden fixture (or becomes it, when regenerating).
expect_fixture() {
  local desc="$1" fixture="$2" got=0
  shift 2
  ACCOUNTED+=("$fixture")
  run_collector "$@" || got=$?
  guard_not_127 "$got" "$desc"
  check "$desc: exit 0" test "$got" -eq 0
  if [ -n "${JBL_OBSERVE_WRITE_FIXTURES:-}" ]; then
    cp "$OBS_DIR/backup.json" "$FIXTURES/$fixture"
    echo "       wrote $fixture"
  fi
  check "$desc: publishes $fixture" cmp -s "$OBS_DIR/backup.json" "$FIXTURES/$fixture"
}

# For cases whose file is not a fixture: the exact text, or just the exit.
published() { cat "$OBS_DIR/backup.json"; }
expect_published() {
  local desc="$1" want="$2" got=0
  shift 2
  run_collector "$@" || got=$?
  guard_not_127 "$got" "$desc"
  check "$desc: exit 0" test "$got" -eq 0
  check "$desc" test "$(published 2>/dev/null)" = "$want"
}
head_of() { printf '{"schema":1,"source":"backup","sampledAt":"%s",' "$FAKE_NOW"; }
RECORDED='"lastSuccess":{"state":"recorded","completedAt":"2026-10-10T02:19:07Z","startedAt":"2026-10-10T02:15:41Z"}'
SCHEDULED='"timer":{"state":"scheduled","nextRunAt":"2026-10-10T13:49:41Z"}'

echo "== the facts, as the box produces them =="
reset_world; write_stamp 20261010T021541Z 2026-10-10T02:19:07Z
expect_fixture "a recorded stamp and a scheduled timer" backup-recorded-scheduled.json

echo "== the state the box is in today: no stamp, timer disabled =="
reset_world; SYSTEMCTL_MODE=inactive
expect_fixture "no stamp and an inactive timer" backup-missing-inactive.json

echo "== the stamp =="
reset_world; : > "$STAMP"
expect_fixture "an empty stamp is invalid" backup-invalid-scheduled.json

for bad in "not-a-stamp" "20261310T021541Z" "20261010T240000Z" "20260230T021541Z" "20261010T021541" "20261010T021541Z extra" "  20261010T021541Z" "2026-10-10T02:15:41Z"; do
  reset_world; write_stamp "$bad" 2026-10-10T02:19:07Z
  expect_published "content '$bad' is invalid, not normalised" \
    "$(head_of)"'"data":{"lastSuccess":{"state":"invalid"},'"$SCHEDULED"'}}'
done

reset_world; mkdir -p "$STAMP"
expect_published "a directory where the stamp should be is invalid" \
  "$(head_of)"'"data":{"lastSuccess":{"state":"invalid"},'"$SCHEDULED"'}}'

reset_world; printf 'x\n' > "$TMPROOT/elsewhere"; ln -s "$TMPROOT/elsewhere" "$STAMP" 2>/dev/null || true
if [ -L "$STAMP" ]; then
  expect_published "a symlink where the stamp should be is invalid" \
    "$(head_of)"'"data":{"lastSuccess":{"state":"invalid"},'"$SCHEDULED"'}}'
else
  skipped=$((skipped + 1)); echo "  skip symlink case: this filesystem cannot make a symlink"
fi

# The collector reports a stamp dated after its own sample; judging it is the API's job, made
# against the API's clock. Pinning that the collector stays silent keeps one rule in one place.
reset_world; write_stamp 20261010T021541Z 2026-10-11T02:19:07Z
expect_fixture "a stamp dated after the sample is reported, not judged" backup-future-stamp.json

reset_world; write_stamp 20261010T021541Z 2026-10-10T02:19:07Z; chmod 000 "$STAMP"
if ! cat "$STAMP" >/dev/null 2>&1; then
  expect_fixture "an unreadable stamp is reported as unreadable" backup-unreadable-stamp.json
else
  ACCOUNTED+=("backup-unreadable-stamp.json")
  skipped=$((skipped + 1)); echo "  skip unreadable-stamp case: this user can read a mode-000 file (root or a mode-less filesystem)"
fi

echo "== the timer =="
declare -A TIMER_CASES=(
  [masked]='{"state":"inactive"}'
  [nonext]='{"state":"unknown"}'
  [garbage]='{"state":"unknown"}'
  [zone]='{"state":"unknown"}'
  [badstate]='{"state":"unknown"}'
  [injected]='{"state":"unknown"}'
  [fail]='{"state":"unknown"}'
  [hang]='{"state":"unknown"}'
)
for mode in masked nonext garbage zone badstate injected fail hang; do
  reset_world; write_stamp 20261010T021541Z 2026-10-10T02:19:07Z; SYSTEMCTL_MODE="$mode"
  SECONDS=0
  expect_published "systemctl mode '$mode'" \
    "$(head_of)"'"data":{'"$RECORDED"',"timer":'"${TIMER_CASES[$mode]}"'}}'
  if [ "$mode" = hang ]; then
    check "a systemctl that hangs is ended by the collector's own timeout, far inside TimeoutStartSec" test "$SECONDS" -lt 20
  fi
done

reset_world; write_stamp 20261010T021541Z 2026-10-10T02:19:07Z; SYSTEMCTL_MODE=notfound
expect_fixture "a timer unit that is not installed" backup-recorded-notinstalled.json

reset_world; write_stamp 20261010T021541Z 2026-10-10T02:19:07Z; SYSTEMCTL_MODE=garbage
expect_fixture "a timer systemd cannot be read for" backup-recorded-timer-unknown.json

reset_world; write_stamp 20261010T021541Z 2026-10-10T02:19:07Z
expect_published "no systemctl on the PATH -> timer unknown, stamp still reported" \
  "$(head_of)"'"data":{'"$RECORDED"',"timer":{"state":"unknown"}}}' --omit systemctl

reset_world; write_stamp 20261010T021541Z 2026-10-10T02:19:07Z
run_collector || true
check "the collector asks systemd one fixed question about the backup timer" \
  test "$(cat "$CALLS/systemctl")" = "show jobbliggaren-backup.timer -p LoadState -p ActiveState -p NextElapseUSecRealtime"
check "…in UTC and the C locale, whatever the box's zone is" \
  test "$(cat "$CALLS/systemctl.env")" = "TZ=UTC LC_ALL=C"

echo "== publishing =="
reset_world; write_stamp 20261010T021541Z 2026-10-10T02:19:07Z
run_collector || true
check "exactly backup.json is left behind: no temporary file survives" \
  test "$(ls -A "$OBS_DIR")" = "backup.json"

reset_world; printf '{"old":true}\n' > "$OBS_DIR/backup.json"; write_stamp 20261010T021541Z 2026-10-10T02:19:07Z
run_collector || true
check "an earlier file is replaced whole, never appended to" \
  test "$(grep -c '"old"' "$OBS_DIR/backup.json" || true)" = "0"

reset_world; write_stamp 20261010T021541Z 2026-10-10T02:19:07Z
run_collector || true
check "the file is one line of JSON text from a closed alphabet" \
  bash -c '[[ "$(cat "$1")" =~ ^[]A-Za-z0-9:,{}\"._[-]+$ ]]' _ "$OBS_DIR/backup.json"
check "…and names no path, user or unit file" \
  bash -c '! grep -qE "/|jbl-|\.service|\.timer" "$1"' _ "$OBS_DIR/backup.json"

reset_world; rm -rf "$OBS_DIR"; write_stamp 20261010T021541Z 2026-10-10T02:19:07Z
got=0; run_collector || got=$?; guard_not_127 "$got" "missing directory"
check "a missing directory still exits 0: the card shows the gap, the unit stays off --failed" test "$got" -eq 0
check "…and the journal says why" grep -q "not published" "$TMPROOT/out"

if [ "$(chmod 700 "$TMPROOT" 2>/dev/null; stat -c '%a' "$TMPROOT" 2>/dev/null)" = "700" ]; then
  reset_world; write_stamp 20261010T021541Z 2026-10-10T02:19:07Z
  run_collector || true
  check "the published file is 0644: the API container's user must be able to read it" \
    test "$(stat -c '%a' "$OBS_DIR/backup.json")" = "644"

  reset_world; write_stamp 20261010T021541Z 2026-10-10T02:19:07Z; chmod 500 "$OBS_DIR"
  if ! touch "$OBS_DIR/probe" 2>/dev/null; then
    got=0; run_collector || got=$?; guard_not_127 "$got" "read-only directory"
    check "a directory that cannot be written still exits 0" test "$got" -eq 0
    check "…and leaves nothing behind" test -z "$(ls -A "$OBS_DIR")"
  else
    rm -f "$OBS_DIR/probe"; skipped=$((skipped + 1)); echo "  skip read-only-directory case: this user writes to a mode-500 directory"
  fi
  chmod 700 "$OBS_DIR"
else
  skipped=$((skipped + 1)); echo "  skip mode cases: this filesystem does not keep POSIX modes"
fi

echo "== the library alone =="
lib_call() {
  env -i PATH="$TMPROOT/runbin" HOME="$TMPROOT" "$REAL_BASH" -c 'source "$1"; shift; "$@"' _ "$SUT_DIR/jobbliggaren-observe-lib.sh" "$@" >"$TMPROOT/out" 2>&1
}
run_collector --omit systemctl || true   # leaves $TMPROOT/runbin populated for lib_call and the case below

reset_world
mkdir -p "$TMPROOT/nolib"; cp "$SUT_DIR/jobbliggaren-observe-backup.sh" "$TMPROOT/nolib/"
got=0
env -i PATH="$TMPROOT/runbin" HOME="$TMPROOT" CALLS="$CALLS" "$REAL_BASH" "$TMPROOT/nolib/jobbliggaren-observe-backup.sh" >"$TMPROOT/out" 2>&1 || got=$?
guard_not_127 "$got" "a collector without its library"
check "a collector whose library is missing still exits 0: the unit stays off --failed" test "$got" -eq 0
check "…says why in the journal" grep -q "library could not be loaded" "$TMPROOT/out"
check "…and publishes nothing" test -z "$(ls -A "$OBS_DIR")"

reset_world
lib_call observe_publish_error backup collector-failed || true
if [ -n "${JBL_OBSERVE_WRITE_FIXTURES:-}" ]; then cp "$OBS_DIR/backup.json" "$FIXTURES/backup-error.json"; echo "       wrote backup-error.json"; fi
check "an error envelope is the golden backup-error.json: a token and no data" cmp -s "$OBS_DIR/backup.json" "$FIXTURES/backup-error.json"
ACCOUNTED+=("backup-error.json")

reset_world
got=0; lib_call observe_publish_error backup "Bad Token" || got=$?
check "an error token with a space is refused" test "$got" -ne 0
check "…and nothing is published" test -z "$(ls -A "$OBS_DIR")"

reset_world
got=0; lib_call observe_publish backup '"data":{"x":"a b"}' || got=$?
check "a body with a space is refused" test "$got" -ne 0
got=0; lib_call observe_publish backup '"data":{"x":"a\"b"}' || got=$?
check "a body with a backslash is refused" test "$got" -ne 0
got=0; lib_call observe_publish backup '"data":{"x":"$(id)"}' || got=$?
check "a body with a shell metacharacter is refused" test "$got" -ne 0
check "…and none of the three published anything" test -z "$(ls -A "$OBS_DIR")"

got=0; lib_call observe_publish "../backup" '"data":{}' || got=$?
check "a source name that is not [a-z]+ is refused" test "$got" -ne 0

echo "== the golden files =="
listed() { printf '%s\n' "$@" | sort -u | tr '\n' ' '; }
check "every file in fixtures/observations is one this suite produces" \
  test "$(listed $(ls -A "$FIXTURES"))" = "$(listed "${ACCOUNTED[@]}")"

echo ""
echo "passed: $pass   failed: $fail   skipped: $skipped"
if [ -n "${JBL_REQUIRE_MODE_CASES:-}" ] && [ "$skipped" -ne 0 ]; then
  echo "JBL_REQUIRE_MODE_CASES is set and $skipped case(s) were skipped; a skip is not a measurement." >&2
  exit 1
fi
[ "$fail" -eq 0 ]
