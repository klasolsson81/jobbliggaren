#!/usr/bin/env bash
#
# Fixture tests for jobbliggaren-runtime-ids.sh.
#
# Run:  bash deploy/systemd/jobbliggaren-runtime-ids.test.sh
#
# NEEDS NO DAEMON, NO ROOT AND NO NETWORK. `docker` is stubbed on PATH, so what is measured here
# is the script's own contract — argument rejection, output validation, and the stdout/stderr
# split — never docker's behaviour.
#
# WHY THE STDOUT SPLIT IS PINNED AND NOT JUST DOCUMENTED. Both callers capture stdout into a
# variable. A diagnostic printed there does not look like a broken script; it looks like an id,
# and it reaches `install -o` or a `!=` comparison as one. The suite therefore asserts what
# stdout contains, not only what the exit code is.
#
# A GUARD NO GATE RUNS CANNOT FALL. This script sits inside a deploy gate (#1295), so it is
# wired into build.yml's `scripts` job beside the other fixture suites.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/jobbliggaren-runtime-ids.sh"
[ -f "$SUT" ] || {
  echo "missing script under test: $SUT" >&2
  exit 1
}

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT
readonly BIN="$TMPROOT/bin"
mkdir -p "$BIN"

pass=0
fail=0

# The script calls docker by ABSOLUTE path, which is the property that keeps a root-run gate from
# asking whatever is earliest on PATH. The fixture therefore rewrites it — and PROVES the rewrite
# landed. Without the proof, a spelling change would leave the copy calling the host's real
# docker: on a host that has none every case would pass for the wrong reason, and on a host that
# has one the suite would start pulling images.
readonly FIXTURE_SUT="$TMPROOT/runtime-ids.sh"
# BOTH DIRECTIONS, AND THE FIRST ONE IS WHY. An absence check alone is fail-OPEN: it cannot tell
# "the redirect applied" from "there was nothing to redirect", so a SUT that had lost its absolute
# path would sail through it — measured, with both suites fully green. The presence assertion
# before the sed is what makes the absence assertion after it mean something. (Presence is checked
# in $SUT rather than by quoting the call shape, which would break the day the line is wrapped.)
grep -qF -- "/usr/bin/docker" "$SUT" || {
  echo "FIXTURE BROKEN: $SUT does not call docker by absolute path — the property this suite" >&2
  echo "                claims to protect is gone, and the redirect proof below is vacuous" >&2
  exit 1
}
sed -e "s#/usr/bin/docker#docker#g" "$SUT" >"$FIXTURE_SUT"
grep -qF -- "/usr/bin/docker" "$FIXTURE_SUT" && {
  echo "FIXTURE BROKEN: the docker redirect did not apply — the suite would call the real docker" >&2
  exit 1
}
chmod +x "$FIXTURE_SUT"

# $1 = what the stubbed `docker run` prints; $2 = the exit code it leaves with.
stub_docker() {
  printf '%s' "$1" >"$TMPROOT/docker-out"
  cat >"$BIN/docker" <<EOF
#!/usr/bin/env bash
case "\$*" in
  *"run --rm"*) cat "$TMPROOT/docker-out" ; exit ${2:-0} ;;
  *)            echo "unexpected docker invocation: \$*" >&2 ; exit 99 ;;
esac
EOF
  chmod +x "$BIN/docker"
}

# stdout and stderr are captured to SEPARATE files, which is the whole point of the suite.
run_sut() {
  PATH="$BIN:/usr/bin:/bin" bash "$FIXTURE_SUT" "$@" >"$TMPROOT/out" 2>"$TMPROOT/err"
}

expect_exit() {
  local want="$1" desc="$2" got=0
  shift 2
  run_sut "$@" || got=$?
  if [ "$got" -eq "$want" ]; then
    pass=$((pass + 1))
    echo "  ok   $desc (exit $got)"
  else
    fail=$((fail + 1))
    echo "  FAIL $desc — wanted exit $want, got $got" >&2
    sed 's/^/       /' "$TMPROOT/err" >&2
  fi
}

# A refusal must leave NOTHING on stdout. An empty capture is a caller that fails loudly at the
# next comparison; a half-written one is a caller that compares against a diagnostic.
assert_stdout_empty() {
  if [ -s "$TMPROOT/out" ]; then
    fail=$((fail + 1))
    echo "  FAIL $1 — stdout was not empty on a refusal:" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  else
    pass=$((pass + 1))
    echo "  ok   $1"
  fi
}

# EVERY REFUSAL IS BOUND TO ITS OWN MESSAGE, not to exit 1. This script refuses by four routes
# and they overlap: the empty argument is also rejected by the charset regex, and the missing
# argument is also caught further down by `set -u`. An exit-code-only case therefore stays green
# with its own guard deleted — measured: removing the `-n` guard leaves the suite 20/0.
assert_err_contains() {
  if grep -qF -- "$1" "$TMPROOT/err"; then
    pass=$((pass + 1))
    echo "  ok   $2"
  else
    fail=$((fail + 1))
    echo "  FAIL $2 — the refusal did not name it:" >&2
    sed 's/^/       /' "$TMPROOT/err" >&2
  fi
}

echo "jobbliggaren-runtime-ids.sh"

echo "-- the argument contract"
stub_docker $'1654\n1654' 0

expect_exit 1 "no argument at all refuses"
assert_stdout_empty "and prints nothing to stdout"
assert_err_contains "usage:" "and it is the ARITY guard that refused"

expect_exit 1 "an empty argument refuses" ""
assert_stdout_empty "and prints nothing to stdout"
assert_err_contains "empty image reference" "and it is the EMPTINESS guard that refused"

# THE ARM THAT PROTECTS A ROOT-RUN `docker run`: an argument that begins with a dash would reach
# docker's FLAG parser rather than its image slot.
expect_exit 1 "a leading dash refuses" "--privileged"
assert_stdout_empty "and prints nothing to stdout"
assert_err_contains "may not begin with" "and it is the DASH guard that refused, not the charset"

expect_exit 1 "a reference with a shell metacharacter refuses" 'ghcr.io/x/y:$(whoami)'
assert_err_contains "outside" "and it is the CHARSET guard that refused"
expect_exit 1 "a reference with whitespace refuses" "ghcr.io/x/y latest"
expect_exit 1 "two arguments refuse" "ghcr.io/x/y:latest" "extra"
assert_err_contains "usage:" "and the arity guard names itself"

# Uppercase is admitted deliberately — a tag may carry it, and the caller's own reference guard
# already allows it. This case is what stops a later "tighten the charset" from reintroducing a
# false refusal.
stub_docker $'1654\n1654' 0
expect_exit 0 "an uppercase TAG is accepted, not refused" "ghcr.io/klasolsson81/jobbliggaren-api:Latest"

echo "-- what docker returns is validated, never trusted"
stub_docker $'1654\n1654' 1
expect_exit 1 "docker failing refuses" "ghcr.io/x/y:latest"
assert_stdout_empty "and prints nothing to stdout"

stub_docker $'1654' 0
expect_exit 1 "ONE line of output refuses — the gid would otherwise be empty" "ghcr.io/x/y:latest"
assert_stdout_empty "and prints nothing to stdout"

stub_docker $'app\n1654' 0
expect_exit 1 "a non-numeric uid refuses" "ghcr.io/x/y:latest"

stub_docker $'1654\napp' 0
expect_exit 1 "a non-numeric gid refuses — validated SEPARATELY from the uid" "ghcr.io/x/y:latest"

stub_docker "" 0
expect_exit 1 "empty output refuses" "ghcr.io/x/y:latest"

# MORE lines is a distinct failure from FEWER, and only one of them was pinned. Probed
# 2026-08-12 against the pre-fix script: `1654\n1654\nEXTRA` exited 0 and reported the pair, so
# root would have chowned the master key to the first two numeric lines of an image's output
# while a later line went unread — and on the injection path that image is unattested.
stub_docker $'1654\n1654\nEXTRA-LINE' 0
expect_exit 1 "a THIRD line refuses — 'at least two' is not the contract" "ghcr.io/x/y:latest"
assert_stdout_empty "and prints nothing to stdout"
assert_err_contains "exactly two lines" "and the refusal names the arity of the OUTPUT"

echo "-- the happy path, and the stdout discipline"
stub_docker $'1654\n1655' 0
expect_exit 0 "a digest reference measures both ids" \
  "ghcr.io/klasolsson81/jobbliggaren-api@sha256:1111111111111111111111111111111111111111111111111111111111111111"

# Bound to the CONTENT, not to the exit code: a script that exited 0 having printed nothing would
# hand its caller an empty pair, and every id comparison downstream would then refuse for a
# reason no message names.
if [ "$(cat "$TMPROOT/out")" = $'1654\n1655' ]; then
  pass=$((pass + 1))
  echo "  ok   stdout is EXACTLY the two ids, uid first"
else
  fail=$((fail + 1))
  echo "  FAIL stdout was not exactly the two ids:" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

# gid != uid on purpose. Deriving one from the other is the class the caller's own header warns
# about, and a fixture where they are equal cannot tell a correct script from one that prints the
# uid twice.
if [ "$(sed -n 2p "$TMPROOT/out")" = "1655" ]; then
  pass=$((pass + 1))
  echo "  ok   the gid is the SECOND line and is not a copy of the uid"
else
  fail=$((fail + 1))
  echo "  FAIL the second line was not the measured gid" >&2
fi

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ]
