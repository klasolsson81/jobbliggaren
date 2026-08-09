#!/usr/bin/env bash
#
# Fixture tests for jobbliggaren-backup.sh.
#
# Run:  bash deploy/systemd/jobbliggaren-backup.test.sh
#
# NEEDS NO DAEMON, NO ROOT, NO NETWORK AND NO POSTGRES. `docker`, `age`, `rclone` and `flock` are
# stubbed on a sandboxed PATH, and the rclone stub keeps a real object store on disk so the
# upload/read-back/compare round trip executes for real rather than being asserted about.
#
# WHY THE PATH IS BUILT FROM SCRATCH RATHER THAN PREPENDED TO. The properties worth pinning here
# include "a missing tool produces NO backup", and that cannot be measured by prepending a stub
# directory: `command -v flock` would still find /usr/bin/flock underneath. So the suite links
# exactly the coreutils the script uses into one directory and runs with PATH set to it alone —
# absence is then something the fixture can actually create, one tool at a time. Each of the five
# required tools gets its own case, for the reason the sibling suite states: a loop checked
# through one representative name reports the whole list as covered.
#
# WHAT THIS SUITE DOES NOT MEASURE, NAMED RATHER THAN IMPLIED:
#   * That the artefact decrypts. The box holds no private key by design, so nothing here or on
#     the box can establish it. That is the restore drill's claim (#197 PR-2 + the runbook).
#   * The root requirement. The SUT copy has its EUID guard neutralised so the suite can run
#     unprivileged; the guard's presence is pinned as source text below instead, and the real
#     enforcement is that systemd runs the unit as root.
#   * Anything about the remote's retention behaviour. Retention is server-side by decision
#     (senior-cto-advisor D5) and is therefore not this script's code to test.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/jobbliggaren-backup.sh"
[ -f "$SUT" ] || {
  echo "missing script under test: $SUT" >&2
  exit 1
}

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

readonly HOST_SECRETS="$TMPROOT/run/jobbliggaren/host-secrets"
readonly RECIPIENT="$TMPROOT/deploy/backup/age.recipient"
readonly STAMP="$TMPROOT/var/lib/jobbliggaren/last-successful-backup"
readonly LOCK="$TMPROOT/var/lock/jobbliggaren-backup.lock"
readonly STORE="$TMPROOT/objectstore"   # the rclone stub's world
readonly CALLS="$TMPROOT/calls"         # argv recordings, one file per tool

# A well-formed X25519 age recipient. Shape matters to the SUT (it regex-checks), the bytes do
# not — the age stub never does cryptography.
readonly GOOD_RECIPIENT="age1ql3z7hjy54pw3hyww5ayyfg7zqgvc7w3j2elw8zmrj2kg5sfn9aqmcac8p"

pass=0
fail=0

# ---------------------------------------------------------------------------------------------
# The sandboxed PATH.
# ---------------------------------------------------------------------------------------------
readonly BIN="$TMPROOT/bin"
mkdir -p "$BIN"

REAL_BASH=$(command -v bash)
readonly REAL_BASH

# Every external the SUT invokes, resolved from the host and linked in. If one is missing the
# suite stops here rather than reporting a green run that never executed the code.
# `bash` and `env` are in the list because the stubs carry `#!/usr/bin/env bash` shebangs: with
# PATH set to this directory alone, an absent `env` or `bash` makes every stub exit 127 — which
# the exit-code assertions would have read as a refusal by the script under test. That is the
# rig reporting an unmeasured run as a result, and it happened while this suite was written.
#
# THEY ARE WRAPPERS, NOT SYMLINKS, AND ON WINDOWS THAT IS THE DIFFERENCE BETWEEN RUNNING AND NOT.
# A Git Bash coreutil is an MSYS binary that resolves msys-2.0.dll relative to its own location;
# symlinked into a directory outside /usr/bin it dies with "error while loading shared libraries"
# — which surfaces as exit 127 and would otherwise have been scored as a refusal. The wrapper
# execs the absolute path, so the binary runs from where its libraries are. `#!/bin/sh` is an
# absolute interpreter path deliberately: a `env`-based shebang would need a PATH lookup inside
# the very PATH this is constructing.
for util in bash env date stat tr cut base64 sha256sum install mktemp chmod mkdir grep rm dirname cat touch ls; do
  path=$(command -v "$util" 2>/dev/null) || {
    echo "FIXTURE BROKEN: '$util' not found on this host; the suite cannot build its PATH" >&2
    exit 1
  }
  printf '#!/bin/sh\nexec %s "$@"\n' "'$path'" > "$BIN/$util"
  chmod +x "$BIN/$util"
done

make_stub() {
  local name="$1"
  cat > "$BIN/$name"
  chmod +x "$BIN/$name"
}

# docker: answers the running-container probe and plays pg_dump. Records its full argv so the
# tests can assert on the dump flags — which is the only place the exclude/include polarity of
# user_data_keys is observable from outside.
make_stub docker <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$CALLS/docker"
case "$1" in
  inspect) [ "${DOCKER_PG_RUNNING:-true}" = "true" ] && echo true || echo false ;;
  exec)
    if [ "${DOCKER_DUMP_FAILS:-}" = "main" ] && printf '%s' "$*" | grep -q -- '--exclude-table-data'; then
      echo "pg_dump: stub failure (main)" >&2; exit 3
    fi
    if [ "${DOCKER_DUMP_FAILS:-}" = "dek" ] && printf '%s' "$*" | grep -q -- '--data-only'; then
      echo "pg_dump: stub failure (dek)" >&2; exit 3
    fi
    printf 'PGDMP-STUB-PAYLOAD %s\n' "$*"
    ;;
  *) echo "docker stub: unexpected verb '$1'" >&2; exit 64 ;;
esac
STUB

# age: records argv, then frames stdin so the framing is observable in the stored object. It
# never encrypts — the property under test is that the bytes leaving the box passed THROUGH age,
# and that the recipient reached it as -r.
make_stub age <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$CALLS/age"
printf 'age-encrypted-frame:'
cat
STUB

# rclone: a real little object store. rcat writes stdin to a file named after the object, cat
# reads it back. That makes the SUT's round-trip comparison a genuine measurement rather than a
# tautology, and lets a test corrupt an object between the two halves.
make_stub rclone <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$CALLS/rclone"
verb="$1"; shift
# Drop the flags the SUT passes; what remains is the object reference.
obj=""
while [ $# -gt 0 ]; do
  case "$1" in
    --config|--log-level|--retries) shift 2 ;;
    *) obj="$1"; shift ;;
  esac
done
target="$STORE/$(printf '%s' "$obj" | tr '/:' '__')"
case "$verb" in
  rcat)
    [ "${RCLONE_FAILS_ON:-}" = "$obj" ] && { echo "rclone stub: refusing $obj" >&2; exit 7; }
    cat > "$target"
    ;;
  cat)
    [ -f "$target" ] || { echo "rclone stub: no such object $obj" >&2; exit 3; }
    cat "$target"
    ;;
  *) echo "rclone stub: unexpected verb '$verb'" >&2; exit 64 ;;
esac
STUB

make_stub flock <<'STUB'
#!/usr/bin/env bash
exit 0
STUB

# ---------------------------------------------------------------------------------------------
# The SUT copy: constants redirected at the fixture, EUID guard neutralised, EVERY rewrite proved.
#
# Without the proofs a renamed constant would leave the copy pointing at the real /run and
# /var/lib paths, and the suite would measure the host — passing or failing for reasons that have
# nothing to do with the code. That failure class has bitten this repo before.
# ---------------------------------------------------------------------------------------------
readonly SUT_COPY="$TMPROOT/sut.sh"
build_sut_copy() {
  sed \
    -e "s#^readonly HOST_SECRETS_DIR=.*#readonly HOST_SECRETS_DIR=$HOST_SECRETS#" \
    -e "s#^readonly RECIPIENT_FILE=.*#readonly RECIPIENT_FILE=$RECIPIENT#" \
    -e "s#^readonly STAMP_FILE=.*#readonly STAMP_FILE=$STAMP#" \
    -e "s#^readonly LOCK_FILE=.*#readonly LOCK_FILE=$LOCK#" \
    -e 's#^\[\[ ${EUID} -eq 0 \]\].*#EUID_GUARD_NEUTRALISED_BY_FIXTURE=1#' \
    "$SUT" > "$SUT_COPY"

  local expected=(
    "readonly HOST_SECRETS_DIR=$HOST_SECRETS"
    "readonly RECIPIENT_FILE=$RECIPIENT"
    "readonly STAMP_FILE=$STAMP"
    "readonly LOCK_FILE=$LOCK"
    "EUID_GUARD_NEUTRALISED_BY_FIXTURE=1"
  )
  local line
  for line in "${expected[@]}"; do
    grep -qxF "$line" "$SUT_COPY" || {
      echo "FIXTURE BROKEN: rewrite did not apply -> '$line'. The suite would measure the host." >&2
      exit 1
    }
  done
  # The EUID guard must still exist in the REAL script; neutralising it in the copy is only
  # allowed because the original enforces it.
  grep -q 'EUID.*-eq 0' "$SUT" || {
    echo "FIXTURE BROKEN: the real script no longer carries a root guard, but the copy assumes one" >&2
    exit 1
  }
}
build_sut_copy

reset_world() {
  rm -rf "$TMPROOT/run" "$TMPROOT/deploy" "$TMPROOT/var" "$STORE" "$CALLS"
  mkdir -p "$HOST_SECRETS" "$(dirname "$RECIPIENT")" "$(dirname "$STAMP")" \
           "$(dirname "$LOCK")" "$STORE" "$CALLS"
  printf '%s' "$GOOD_RECIPIENT" > "$RECIPIENT"
  # base64 of a plausible rclone config. Content is irrelevant to the stub; the SUT only
  # requires that it decodes to something non-empty.
  printf '[jbl-backup]\ntype = s3\n' | base64 > "$HOST_SECRETS/Backup__RcloneConfigBase64"
  unset DOCKER_PG_RUNNING DOCKER_DUMP_FAILS RCLONE_FAILS_ON
}

# Runs the SUT with a PATH containing exactly the tools named, plus the coreutils.
run_sut() {
  local -a omit=()
  while [ "${1:-}" = "--omit" ]; do omit+=("$2"); shift 2; done

  local runbin="$TMPROOT/runbin"
  rm -rf "$runbin"; mkdir -p "$runbin"
  local f base skip
  for f in "$BIN"/*; do
    base=$(basename "$f")
    skip=no
    for o in "${omit[@]:-}"; do [ "$base" = "$o" ] && skip=yes; done
    [ "$skip" = yes ] || cp "$f" "$runbin/$base"
  done

  env -i \
    PATH="$runbin" \
    HOME="$TMPROOT" \
    STORE="$STORE" \
    CALLS="$CALLS" \
    DOCKER_PG_RUNNING="${DOCKER_PG_RUNNING:-true}" \
    DOCKER_DUMP_FAILS="${DOCKER_DUMP_FAILS:-}" \
    RCLONE_FAILS_ON="${RCLONE_FAILS_ON:-}" \
    "$REAL_BASH" "$SUT_COPY" "$@" >"$TMPROOT/out" 2>&1
}

# An exit of 127 means "command not found" — the shape a broken PATH takes. It is never a
# verdict the script under test issued, so no case may be allowed to score against it.
guard_not_127() {
  local got="$1" desc="$2"
  [ "$got" -ne 127 ] || {
    echo "FIXTURE BROKEN: '$desc' exited 127 (command not found). The sandboxed PATH is" >&2
    echo "                incomplete and this run measured nothing." >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
    exit 1
  }
}

expect_exit() {
  local want="$1" desc="$2"; shift 2
  local got=0
  run_sut "$@" || got=$?
  guard_not_127 "$got" "$desc"
  if [ "$got" -eq "$want" ]; then
    pass=$((pass + 1)); echo "  ok   $desc (exit $got)"
  else
    fail=$((fail + 1)); echo "  FAIL $desc — wanted exit $want, got $got" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

check() {
  local desc="$1"; shift
  if "$@"; then
    pass=$((pass + 1)); echo "  ok   $desc"
  else
    fail=$((fail + 1)); echo "  FAIL $desc" >&2
    [ -f "$TMPROOT/out" ] && sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

stored() { printf '%s' "$STORE/$(printf '%s' "$1" | tr '/:' '__')"; }

echo "== preflight: every required tool is required =="
# Each name separately. Together these also prove the SUT refuses BEFORE dumping: an absent tool
# must leave the object store empty.
for tool in docker age rclone sha256sum flock; do
  reset_world
  expect_exit 2 "missing '$tool' -> exit 2 (cannot answer)" --omit "$tool"
  check "missing '$tool' uploaded nothing" [ -z "$(ls -A "$STORE")" ]
done

echo "== preflight: recipient =="
reset_world; rm -f "$RECIPIENT"
expect_exit 2 "recipient file absent -> exit 2"

reset_world; printf '%s' "AGE-SECRET-KEY-1QQQQQQQQQQQQQQQQ" > "$RECIPIENT"
expect_exit 1 "a PRIVATE key in the recipient file is refused"
check "the refusal names it as a private key" grep -qi "PRIVATE key" "$TMPROOT/out"

reset_world; printf '%s' "age1NOT-BECH32!!" > "$RECIPIENT"
expect_exit 1 "malformed recipient is refused"

reset_world; printf '%s' "  $GOOD_RECIPIENT  " > "$RECIPIENT"
expect_exit 0 "surrounding whitespace in the recipient file is tolerated"

echo "== preflight: credential =="
reset_world; rm -f "$HOST_SECRETS/Backup__RcloneConfigBase64"
expect_exit 2 "credential absent -> exit 2 (tmpfs is empty after a boot)"

reset_world; : > "$HOST_SECRETS/Backup__RcloneConfigBase64"
expect_exit 1 "empty credential is refused"

reset_world; printf 'not~valid~base64~~~' > "$HOST_SECRETS/Backup__RcloneConfigBase64"
expect_exit 1 "credential that is not base64 is refused"

echo "== preflight: the database must be there =="
reset_world; DOCKER_PG_RUNNING=false
expect_exit 1 "postgres container not running -> refuse"
unset DOCKER_PG_RUNNING

echo "== happy path =="
reset_world
expect_exit 0 "a complete run succeeds"
check "the stamp is written" [ -s "$STAMP" ]
check "a main artefact was uploaded" bash -c '[ -n "$(ls -A "'"$STORE"'" | grep main)" ]'
check "the DEK generation was promoted" [ -f "$(stored "jbl-backup:jobbliggaren-backups/deks/verified.dump.age")" ]
check "the staged DEK object exists too" [ -f "$(stored "jbl-backup:jobbliggaren-backups/deks/staged.dump.age")" ]

# The polarity of the two dumps is the decision (senior-cto-advisor D2) and it is invisible
# everywhere except in the argv the container was handed.
check "the main dump excludes user_data_keys DATA (not the table)" \
  grep -q -- '--exclude-table-data=user_data_keys' "$CALLS/docker"
check "the main dump does not exclude the table definition" \
  bash -c '! grep -q -- "--exclude-table=user_data_keys" "'"$CALLS"'/docker"'
check "the DEK dump is data-only and scoped to user_data_keys" \
  grep -q -- '--data-only --table=user_data_keys' "$CALLS/docker"
check "main is dumped BEFORE the DEKs" bash -c \
  'x=$(grep -n -- "--exclude-table-data" "'"$CALLS"'/docker" | head -1 | cut -d: -f1);
   y=$(grep -n -- "--data-only" "'"$CALLS"'/docker" | head -1 | cut -d: -f1); [ "$x" -lt "$y" ]'

check "age was invoked with -r (a recipient)" grep -q -- '-r age1' "$CALLS/age"
check "age was NEVER invoked with -i (an identity)" bash -c '! grep -q -- " -i " "'"$CALLS"'/age"'
check "everything uploaded passed through age" bash -c \
  'for f in "'"$STORE"'"/*; do head -c 20 "$f" | grep -q "age-encrypted-frame:" || exit 1; done'

check "no plaintext dump survives anywhere under the fixture root" bash -c \
  '! grep -rl "PGDMP-STUB-PAYLOAD" "'"$TMPROOT"'/run" "'"$TMPROOT"'/var" 2>/dev/null | grep -q .'
check "the working directory was cleaned up" bash -c \
  '! ls -d "'"$HOST_SECRETS"'"/backup.* 2>/dev/null | grep -q .'

echo "== failure directions =="
reset_world; DOCKER_DUMP_FAILS=main
expect_exit 1 "a failing pg_dump on the main artefact fails the run"
check "…and writes no stamp" [ ! -f "$STAMP" ]
check "…and promotes no DEK generation" [ ! -f "$(stored "jbl-backup:jobbliggaren-backups/deks/verified.dump.age")" ]
unset DOCKER_DUMP_FAILS

reset_world; DOCKER_DUMP_FAILS=dek
expect_exit 1 "a failing pg_dump on the DEK artefact fails the run"
check "…and writes no stamp" [ ! -f "$STAMP" ]
check "…and promotes no DEK generation" [ ! -f "$(stored "jbl-backup:jobbliggaren-backups/deks/verified.dump.age")" ]
unset DOCKER_DUMP_FAILS

reset_world; RCLONE_FAILS_ON="jbl-backup:jobbliggaren-backups/deks/staged.dump.age"
expect_exit 1 "a failing DEK upload fails the run"
check "…and promotes no DEK generation" [ ! -f "$(stored "jbl-backup:jobbliggaren-backups/deks/verified.dump.age")" ]
unset RCLONE_FAILS_ON

# THE ROUND TRIP MUST BE ABLE TO FAIL, and this is the case that proves it does. Without it the
# comparison could be tautological — comparing a value against itself — and every run would pass.
echo "== the round trip actually verifies =="
reset_world
cat > "$BIN/rclone" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$CALLS/rclone"
verb="$1"; shift
obj=""
while [ $# -gt 0 ]; do
  case "$1" in
    --config|--log-level|--retries) shift 2 ;;
    *) obj="$1"; shift ;;
  esac
done
target="$STORE/$(printf '%s' "$obj" | tr '/:' '__')"
case "$verb" in
  rcat) cat > "$target"; printf 'corrupted' >> "$target" ;;   # the object is not what we sent
  cat)  cat "$target" ;;
esac
STUB
chmod +x "$BIN/rclone"
expect_exit 1 "a DEK object that differs from what was uploaded is refused"
check "…the refusal names the mismatch" grep -qi "does not match what was" "$TMPROOT/out"
check "…and nothing is promoted" [ ! -f "$(stored "jbl-backup:jobbliggaren-backups/deks/verified.dump.age")" ]
check "…and no stamp is written" [ ! -f "$STAMP" ]
build_sut_copy   # (rclone stub is restored below)
make_stub rclone <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$CALLS/rclone"
verb="$1"; shift
obj=""
while [ $# -gt 0 ]; do
  case "$1" in
    --config|--log-level|--retries) shift 2 ;;
    *) obj="$1"; shift ;;
  esac
done
target="$STORE/$(printf '%s' "$obj" | tr '/:' '__')"
case "$verb" in
  rcat) [ "${RCLONE_FAILS_ON:-}" = "$obj" ] && { exit 7; }; cat > "$target" ;;
  cat)  [ -f "$target" ] || exit 3; cat "$target" ;;
esac
STUB

echo "== --check, the freshness probe =="
reset_world; rm -f "$STAMP"
expect_exit 1 "no stamp at all -> stale" --check

reset_world; printf 'x' > "$STAMP"
expect_exit 0 "a stamp written just now -> fresh" --check

reset_world; printf 'x' > "$STAMP"
touch -d '27 hours ago' "$STAMP" 2>/dev/null || touch -t "$(date -d '27 hours ago' +%Y%m%d%H%M 2>/dev/null || echo 197001010000)" "$STAMP"
expect_exit 1 "a stamp 27h old -> stale (threshold is 26h)" --check
check "…the message says how old it is" grep -qi "STALE" "$TMPROOT/out"

reset_world; printf 'x' > "$STAMP"
touch -d '25 hours ago' "$STAMP" 2>/dev/null || touch "$STAMP"
expect_exit 0 "a stamp 25h old -> still fresh (the threshold is not 24h)" --check

reset_world; printf 'x' > "$STAMP"
touch -d '2 hours' "$STAMP" 2>/dev/null || touch "$STAMP"
expect_exit 1 "a stamp dated in the FUTURE is not fresh" --check

reset_world
expect_exit 1 "--check does not accept a second argument" --check --force 2>/dev/null || true

echo "== argument handling =="
reset_world
expect_exit 1 "an unknown argument is refused" --backup-everything

echo ""
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ]
