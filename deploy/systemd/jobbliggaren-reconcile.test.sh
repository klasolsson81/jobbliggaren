#!/usr/bin/env bash
#
# Fixture tests for the two PURE PREDICATES inside jobbliggaren-reconcile.sh.
#
# Run:  bash deploy/systemd/jobbliggaren-reconcile.test.sh
#
# WHY THIS EXISTS. The wrapper as a whole needs a daemon, a registry and root, and that is a
# real reason not to test the orchestration. It is not a reason to leave the two decisions
# inside it untested — and those two carry the larger blast radius:
#
#   1. THE IMAGE CLASSIFIER. Ours → verify · named upstream → skip · anything else → refuse.
#      Its whole purpose is that an image nobody classified FAILS CLOSED, which is exactly the
#      property that cannot be observed from a green run: every image in the compose file today
#      is classified, so the refusal arm never executes in production until the day it matters.
#   2. THE DIGEST RULE. Exactly one repo digest for the image's own repository — zero refuses,
#      several refuse, and index 0 is never assumed.
#
# `docker` is stubbed on PATH, so no daemon, no registry, no network. The verifier is stubbed
# too: this suite is about which images REACH it, not about what it then decides — that is
# verify-image-attestation.test.sh's subject, and the split is deliberate.
#
# THREE OUTCOMES, NEVER COLLAPSED: 0 applied · 1 refused · 2 could not answer.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/jobbliggaren-reconcile.sh"
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

# The wrapper hard-codes /opt/jobbliggaren paths, so the suite runs it against a copy whose
# paths point into the fixture tree. Rewriting the constants is what makes the predicates
# reachable without root; the predicates themselves are untouched.
readonly FIXTURE_SUT="$TMPROOT/reconcile.sh"
prepare_sut() {
  sed -e "s#^readonly COMPOSE_FILE=.*#readonly COMPOSE_FILE=$TMPROOT/docker-compose.yml#" \
    -e "s#^readonly ENV_FILE=.*#readonly ENV_FILE=$TMPROOT/.env#" \
    -e "s#^readonly VERIFIER=.*#readonly VERIFIER=$BIN/verifier.sh#" \
    -e "s#^readonly LOCK=.*#readonly LOCK=$TMPROOT/lock#" \
    -e "s#^readonly STAMP=.*#readonly STAMP=$TMPROOT/stamp#" \
    -e "s#/usr/bin/docker#docker#g" \
    "$SUT" >"$FIXTURE_SUT"
  chmod +x "$FIXTURE_SUT"
  : >"$TMPROOT/docker-compose.yml"
}

# $1 = newline-separated image list `compose config --images` returns.
# $2 = repo digests `docker image inspect` returns for ANY image (one per line).
stub_docker() {
  printf '%s\n' "$1" >"$TMPROOT/images"
  printf '%s\n' "$2" >"$TMPROOT/digests"
  cat >"$BIN/docker" <<EOF
#!/usr/bin/env bash
# `up -d` is matched BEFORE the bare pull arm, because the apply now carries \`--pull never\`
# and would otherwise be swallowed by \`*pull*\` — which is how this stub first reported that
# 'up -d' never ran when it had.
case "\$*" in
  *"config --images"*) cat "$TMPROOT/images" ;;
  *"image inspect"*)   cat "$TMPROOT/digests" ;;
  *"up -d"*)           echo "up: ok" ; echo "\$*" > "$TMPROOT/up-args" ;;
  *pull*)              echo "pull: ok" ;;
  *)                   echo "unexpected docker invocation: \$*" >&2 ; exit 99 ;;
esac
EOF
  chmod +x "$BIN/docker"
}

stub_verifier() {
  cat >"$BIN/verifier.sh" <<EOF
#!/usr/bin/env bash
printf '%s\n' "\$1" >> "$TMPROOT/verified"
exit ${1:-0}
EOF
  chmod +x "$BIN/verifier.sh"
}

# `flock` is util-linux and is absent on some developer hosts (Git Bash on Windows has no
# such binary). The suite must run in both places, so where the real one is missing it is
# stubbed to "lock acquired" — the acquire path is what every case below needs. The two cases
# that are ABOUT the lock skip themselves rather than assert against a stub, and say so.
readonly HAVE_FLOCK=$(command -v flock >/dev/null 2>&1 && echo 1 || echo 0)
if [ "$HAVE_FLOCK" -eq 0 ]; then
  printf '#!/usr/bin/env bash\nexit 0\n' >"$BIN/flock"
  chmod +x "$BIN/flock"
fi

run_sut() {
  : >"$TMPROOT/verified"
  rm -f "$TMPROOT/up-args" "$TMPROOT/stamp"
  PATH="$BIN:/usr/bin:/bin" bash "$FIXTURE_SUT" >"$TMPROOT/out" 2>&1
}

expect_exit() {
  local want="$1" desc="$2" got=0
  run_sut || got=$?
  if [ "$got" -eq "$want" ]; then
    pass=$((pass + 1))
    echo "  ok   $desc (exit $got)"
  else
    fail=$((fail + 1))
    echo "  FAIL $desc — wanted exit $want, got $got" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

assert_applied() {
  if [ -f "$TMPROOT/up-args" ]; then
    pass=$((pass + 1))
    echo "  ok   $1"
  else
    fail=$((fail + 1))
    echo "  FAIL $1 — 'up -d' never ran" >&2
  fi
}

assert_not_applied() {
  if [ -f "$TMPROOT/up-args" ]; then
    fail=$((fail + 1))
    echo "  FAIL $1 — 'up -d' RAN after a refusal; the box would have deployed it" >&2
  else
    pass=$((pass + 1))
    echo "  ok   $1"
  fi
}

prepare_sut
readonly OURS="ghcr.io/klasolsson81/jobbliggaren-api"
readonly DIGEST="$OURS@sha256:1111111111111111111111111111111111111111111111111111111111111111"

echo "jobbliggaren-reconcile.sh — the two pure predicates"

echo "-- the image classifier"
stub_docker "$OURS:latest" "$DIGEST"
stub_verifier 0
expect_exit 0 "one of ours, verifying, is applied"
assert_applied "and 'up -d' actually ran"

stub_docker "$OURS:latest
postgres:18.3
redis:8.6-alpine" "$DIGEST"
stub_verifier 0
expect_exit 0 "the two allow-listed upstream images are skipped, not refused"

# THE ARM THAT NEVER RUNS IN PRODUCTION UNTIL IT MATTERS.
stub_docker "$OURS:latest
mongo:7" "$DIGEST"
stub_verifier 0
expect_exit 1 "an UNKNOWN image refuses the whole apply"
assert_not_applied "and nothing is applied when an image is unclassified"

# An upstream image at a different tag is a different artifact, so the allowlist is per-tag.
stub_docker "postgres:19.0" "$DIGEST"
stub_verifier 0
expect_exit 1 "an allow-listed image at an UNLISTED tag still refuses"

stub_docker "" "$DIGEST"
stub_verifier 0
expect_exit 1 "an empty image list refuses rather than applying nothing successfully"

echo "-- the digest rule"
stub_docker "$OURS:latest" ""
stub_verifier 0
expect_exit 1 "ZERO repo digests refuses"
assert_not_applied "and nothing is applied"

stub_docker "$OURS:latest" "$OURS@sha256:1111111111111111111111111111111111111111111111111111111111111111
$OURS@sha256:2222222222222222222222222222222222222222222222222222222222222222"
stub_verifier 0
expect_exit 1 "TWO distinct digests for the same repo refuses — index 0 is not a contract"

# A digest belonging to a DIFFERENT repository must not be mistaken for this image's.
stub_docker "$OURS:latest" "ghcr.io/someone-else/other@sha256:3333333333333333333333333333333333333333333333333333333333333333"
stub_verifier 0
expect_exit 1 "a digest from another repository does not satisfy this image"

echo "-- refusal propagates, and nothing is applied"
stub_docker "$OURS:latest" "$DIGEST"
stub_verifier 1
expect_exit 1 "a verifier refusal (exit 1) refuses the apply"
assert_not_applied "and the running containers are left alone"

stub_docker "$OURS:latest" "$DIGEST"
stub_verifier 2
# 2 SURVIVES to the unit's status rather than collapsing into 1: `systemctl --failed` is this
# box's only alarm surface, and "not proven" and "the check could not run" call for different
# responses. Both still refuse the apply.
expect_exit 2 "a verifier 'cannot answer' (exit 2) refuses AND keeps its own code"
assert_not_applied "and still nothing is applied"

echo "-- what reaches the verifier"
stub_docker "$OURS:latest
postgres:18.3" "$DIGEST"
stub_verifier 0
run_sut || true
if [ "$(wc -l <"$TMPROOT/verified")" -eq 1 ] && grep -qF "@sha256:" "$TMPROOT/verified"; then
  pass=$((pass + 1))
  echo "  ok   exactly one image reached the verifier, and by DIGEST not tag"
else
  fail=$((fail + 1))
  echo "  FAIL wrong set reached the verifier:" >&2
  sed 's/^/       /' "$TMPROOT/verified" >&2
fi

# The TOCTOU argument's other half: the apply must not be free to consult the registry again.
if grep -qF -- "--pull never" "$TMPROOT/up-args"; then
  pass=$((pass + 1))
  echo "  ok   'up -d' is pinned to local images (--pull never)"
else
  fail=$((fail + 1))
  echo "  FAIL 'up -d' may re-resolve tags — verification would guard a different image" >&2
fi

echo "-- a missing flock is UNANSWERABLE, never a silent no-op"
# The wrapper had this defect: with flock absent, `if ! flock -n 9` failed with "command not
# found", took the lock-held branch, and exited 0 having applied nothing — a unit reporting
# success on every tick forever. Found by running this suite on a host without util-linux.
stub_docker "$OURS:latest" "$DIGEST"
stub_verifier 0
mv "$BIN/flock" "$BIN/flock.hidden" 2>/dev/null || true
hidden_real=0
PATH="$BIN:/usr/bin:/bin" command -v flock >/dev/null 2>&1 && hidden_real=1
if [ "$hidden_real" -eq 1 ]; then
  echo "  skip real flock is on PATH outside \$BIN; cannot hide it for this case"
else
  expect_exit 2 "flock absent → exit 2 (cannot answer), not 0"
  assert_not_applied "and nothing is applied"
fi
mv "$BIN/flock.hidden" "$BIN/flock" 2>/dev/null || true

echo "-- the lock"
if [ "$HAVE_FLOCK" -eq 0 ]; then
  echo "  skip lock-held behaviour needs a real flock; this host has none (stubbed elsewhere)"
else
  stub_docker "$OURS:latest" "$DIGEST"
  stub_verifier 0
  # Hold the lock from another process: a benign overlap must be exit 0 AND apply nothing.
  exec 8>"$TMPROOT/lock"
  flock -n 8
  got=0
  run_sut || got=$?
  exec 8>&-
  if [ "$got" -eq 0 ]; then
    pass=$((pass + 1))
    echo "  ok   lock held → exit 0 (a benign overlap is not a unit failure)"
  else
    fail=$((fail + 1))
    echo "  FAIL lock held → exit $got; systemctl --failed is this box's only alarm surface" >&2
  fi
  assert_not_applied "and a locked-out run applies nothing"
  if [ -f "$TMPROOT/stamp" ]; then
    fail=$((fail + 1))
    echo "  FAIL a locked-out run stamped success" >&2
  else
    pass=$((pass + 1))
    echo "  ok   and it does not stamp success"
  fi
fi

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ]
