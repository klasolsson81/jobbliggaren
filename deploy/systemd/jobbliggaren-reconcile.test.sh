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
# The gate's ownership cases brought two platform-dependent skips with them (#1295): a mode case
# that needs a filesystem honouring chmod, and a group case that needs this account to be in more
# than one group. Both are announced, and CI turns each into an error via its own env flag — an
# announced skip is not a measurement.
skipped=0

# The wrapper hard-codes /opt/jobbliggaren paths, so the suite runs it against a copy whose
# paths point into the fixture tree. Rewriting the constants is what makes the predicates
# reachable without root; the predicates themselves are untouched.
readonly FIXTURE_SUT="$TMPROOT/reconcile.sh"
readonly SECRETS="$TMPROOT/secrets"
readonly FIXTURE_IDS="$TMPROOT/runtime-ids.sh"
prepare_sut() {
  sed -e "s#^readonly COMPOSE_FILE=.*#readonly COMPOSE_FILE=$TMPROOT/docker-compose.yml#" \
    -e "s#^readonly ENV_FILE=.*#readonly ENV_FILE=$TMPROOT/.env#" \
    -e "s#^readonly VERIFIER=.*#readonly VERIFIER=$BIN/verifier.sh#" \
    -e "s#^readonly LOCK=.*#readonly LOCK=$TMPROOT/lock#" \
    -e "s#^readonly STAMP=.*#readonly STAMP=$TMPROOT/stamp#" \
    -e "s#^readonly SECRETS_DIR=.*#readonly SECRETS_DIR=$SECRETS#" \
    -e "s#^readonly RUNTIME_IDS=.*#readonly RUNTIME_IDS=$FIXTURE_IDS#" \
    -e "s#/usr/bin/docker#docker#g" \
    "$SUT" >"$FIXTURE_SUT"

  # THE SECRETS_DIR REDIRECT IS PROVEN, AND THIS PROOF IS NOT OPTIONAL. Unproven, a spelling
  # change leaves the copy pointing at the host's real /run/jobbliggaren/secrets — absent on the
  # runner, which takes the gate's SKIP arm, which makes every case below pass for the wrong
  # reason. That is a fail-OPEN rig, and this repo has paid for that class before.
  grep -qxF "readonly SECRETS_DIR=$SECRETS" "$FIXTURE_SUT" || {
    echo "FIXTURE BROKEN: SECRETS_DIR redirect did not apply — every gate case would take the skip arm" >&2
    exit 1
  }
  grep -qxF "readonly RUNTIME_IDS=$FIXTURE_IDS" "$FIXTURE_SUT" || {
    echo "FIXTURE BROKEN: RUNTIME_IDS redirect did not apply — the suite would run the real helper" >&2
    exit 1
  }
  chmod +x "$FIXTURE_SUT"
  : >"$TMPROOT/docker-compose.yml"

  # The gate runs the REAL helper, not a stand-in for it: the two are wired together on the box
  # and a stub here would leave that wiring unmeasured. Only its docker call is redirected onto
  # the same stub everything else in this suite uses.
  sed -e "s#/usr/bin/docker#docker#g" "$script_dir/jobbliggaren-runtime-ids.sh" >"$FIXTURE_IDS"
  # Proven by ABSENCE, so the proof does not break when the call is reformatted.
  grep -qF -- "/usr/bin/docker" "$FIXTURE_IDS" && {
    echo "FIXTURE BROKEN: the helper's docker redirect did not apply" >&2
    exit 1
  }
  chmod +x "$FIXTURE_IDS"
}

# THE RIG MUST BE ABLE TO MEASURE WHAT IT CLAIMS, AND A DISAGREEMENT HERE IS AN ABORT, NOT A SKIP.
# The gate compares the fixture directory's numeric owner against ids the docker stub returns, so
# the suite needs `stat` and `id` to agree about who owns a directory this process just made.
# Unlike the chmod cases elsewhere in this repo — where the platform genuinely cannot express the
# property — a mismatch here means the instrument is wrong, and a skip would hide that.
probe_dir=$(mktemp -d "$TMPROOT/idprobe.XXX")
probe_owner=$(stat -c '%u %g' "$probe_dir")
probe_expected="$(id -u) $(id -g)"
rm -rf "$probe_dir"
if [ "$probe_owner" != "$probe_expected" ]; then
  echo "FIXTURE BROKEN: stat reports '$probe_owner' for a directory this process owns," >&2
  echo "                but id reports '$probe_expected'. The ownership cases cannot be measured." >&2
  exit 1
fi

# $1 = newline-separated image list `compose config --images` returns.
# $2 = repo digests `docker image inspect` returns for ANY image (one per line).
stub_docker() {
  printf '%s\n' "$1" >"$TMPROOT/images"
  printf '%s\n' "$2" >"$TMPROOT/digests"
  cat >"$BIN/docker" <<EOF
#!/usr/bin/env bash
# THE BACKTICKS BELOW ARE ESCAPED AND MUST STAY SO. This heredoc is unquoted (<<EOF), so an
# unescaped pair is command substitution — the shell ran \`up -d\` every time this stub was
# written and printed "up: command not found" to stderr, 12 times per suite run on the baseline.
# Noise beside a fixture's own output is how a real failure goes unread.
# \`up -d\` is matched BEFORE the bare pull arm, because the apply now carries \`--pull never\`
# and would otherwise be swallowed by \`*pull*\` — which is how this stub first reported that
# 'up -d' never ran when it had.
case "\$*" in
  *"id -u"*)           echo "\$*" > "$TMPROOT/idmeasured" ; cat "$TMPROOT/ids-out" ; exit "\$(cat "$TMPROOT/ids-exit")" ;;
  *"config --images"*) cat "$TMPROOT/images" ;;
  *"image inspect"*)   cat "$TMPROOT/digests" ;;
  *"up -d"*)           echo "up: ok" ; echo "\$*" > "$TMPROOT/up-args" ;;
  *pull*)              echo "pull: ok" ;;
  *)                   echo "unexpected docker invocation: \$*" >&2 ; exit 99 ;;
esac
EOF
  chmod +x "$BIN/docker"
}

# What the image "reports" as its runtime ids. The MARKER the arm above writes is what makes the
# arm's NON-invocation assertable — an ordering property cannot be pinned by observing the happy
# path alone.
# $1 = uid, $2 = gid, $3 = the exit code the measurement leaves with (default 0).
stub_runtime_ids() {
  printf '%s\n%s\n' "$1" "$2" >"$TMPROOT/ids-out"
  printf '%s' "${3:-0}" >"$TMPROOT/ids-exit"
}
stub_runtime_ids "$(id -u)" "$(id -g)"

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
  rm -f "$TMPROOT/up-args" "$TMPROOT/stamp" "$TMPROOT/idmeasured"
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

echo "-- the secrets ownership gate (#1295)"
# WHAT THIS SECTION MEASURES, AND WHY IT CAN. The gate compares the secrets directory's owner
# against the ids of the image about to be applied. The fixture cannot `chown` — it is not root —
# so it moves the OTHER side: the docker stub reports ids that do or do not match the directory
# this unprivileged process owns. Both arms are therefore reachable without root, on Windows and
# on the ubuntu runner alike.
#
# The production ownership triple itself (0710 root:<gid>, files 0400 <uid>) is NOT measured
# here and cannot be; its proof is the cutover row in vps-deploy-stack.md.

seed_secrets() {
  rm -rf "$SECRETS"
  mkdir -p "$SECRETS"
  printf '%s' "seeded" >"$SECRETS/FieldEncryption__LocalMasterKeyBase64"
  printf '%s' "seeded" >"$SECRETS/AuditPseudonymization__PepperBase64"
}

assert_output_contains() {
  if grep -qF -- "$1" "$TMPROOT/out"; then
    pass=$((pass + 1))
    echo "  ok   $2"
  else
    fail=$((fail + 1))
    echo "  FAIL $2 — the output did not name it:" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

assert_ids_measured() {
  if [ -f "$TMPROOT/idmeasured" ]; then
    pass=$((pass + 1))
    echo "  ok   $1"
  else
    fail=$((fail + 1))
    echo "  FAIL $1 — the image was never asked for its ids" >&2
  fi
}

assert_ids_not_measured() {
  if [ -f "$TMPROOT/idmeasured" ]; then
    fail=$((fail + 1))
    echo "  FAIL $1 — the box RAN an image it had just refused" >&2
  else
    pass=$((pass + 1))
    echo "  ok   $1"
  fi
}

seed_secrets
stub_docker "$OURS:latest" "$DIGEST"
stub_verifier 0
stub_runtime_ids "$(id -u)" "$(id -g)"
expect_exit 0 "ids agree → the apply proceeds"
assert_applied "and 'up -d' ran"
assert_ids_measured "and the image WAS measured (the gate did not silently skip)"

# WHAT REFERENCE REACHED THE MEASUREMENT, not merely that one did. Running the image is safe only
# because its content is addressed by the digest attestation just cleared; with `api_digest`
# assigned the TAG instead, every other case in this section still passes.
if grep -qF -- "@sha256:" "$TMPROOT/idmeasured"; then
  pass=$((pass + 1))
  echo "  ok   and it was measured BY DIGEST, not by tag"
else
  fail=$((fail + 1))
  echo "  FAIL the measurement ran against a non-digest reference:" >&2
  sed 's/^/       /' "$TMPROOT/idmeasured" >&2
fi

# The containment flags are part of the mechanism, not of its style: one of the two callers runs
# an unattested image. Bound here because this is where the real helper is exercised end to end.
for flag in "--network none" "--cap-drop ALL" "--security-opt no-new-privileges"; do
  if grep -qF -- "$flag" "$TMPROOT/idmeasured"; then
    pass=$((pass + 1))
    echo "  ok   the measurement runs contained ($flag)"
  else
    fail=$((fail + 1))
    echo "  FAIL the measurement ran WITHOUT $flag" >&2
  fi
done

# BOUND TO THE MESSAGE, NEVER TO THE EXIT CODE. Reconcile reaches exit 1 by many routes; an
# exit-code-only assertion here stays green with either half of the comparison deleted.
stub_runtime_ids "$(id -u)" "$(($(id -g) + 1))"
expect_exit 1 "gid drift → refuses"
assert_not_applied "and nothing is applied — stale but serving"
assert_output_contains "cannot TRAVERSE" "and the refusal names the traversal axis"

stub_runtime_ids "$(($(id -u) + 1))" "$(id -g)"
expect_exit 1 "uid drift → refuses even though the group still traverses"
assert_not_applied "and nothing is applied"
assert_output_contains "cannot READ the injected secrets" "and the refusal names the owner axis"

# THE ORDERING PROPERTY. Measuring the ids RUNS the image, so it must never happen for an image
# attestation refused. Without this case a later refactor can hoist the gate above the verify
# loop and every other case stays green.
stub_runtime_ids "$(id -u)" "$(id -g)"
stub_verifier 1
expect_exit 1 "a refused image still refuses with secrets present"
assert_ids_not_measured "and a REFUSED image is never run to read its ids"

stub_verifier 0
stub_runtime_ids "$(id -u)" "$(id -g)" 97
expect_exit 2 "the measurement failing is 'cannot answer' (2), never 'refused' (1) and never 0"
assert_not_applied "and nothing is applied"

# The arm that never runs in production until it matters: secrets injected, but nothing in the
# compose model is our api image, so the ids they must match cannot be determined.
stub_runtime_ids "$(id -u)" "$(id -g)"
stub_docker "postgres:18.3" "$DIGEST"
stub_verifier 0
expect_exit 2 "secrets present but no api image → cannot answer, not a wave-through"
assert_not_applied "and nothing is applied"
# BOUND TO THE MESSAGE for the same reason cases above are: with the emptiness check removed the
# run still reaches exit 2, by way of the helper refusing an empty reference. Two paths, one code
# — an exit-code-only assertion here would pass with the check deleted.
assert_output_contains "api image was" "and it says WHICH question it could not answer"

echo "-- and the gate is silent when there is nothing to protect"
stub_docker "$OURS:latest" "$DIGEST"
stub_verifier 0
rm -rf "$SECRETS"
mkdir -p "$SECRETS"
expect_exit 0 "an EMPTY secrets directory skips the gate and applies"
assert_applied "and 'up -d' ran"
assert_ids_not_measured "and no docker run was spent measuring ids"
assert_output_contains "ownership gate skipped" "and the skip is on the journal, not silent"

rm -rf "$SECRETS"
expect_exit 0 "a MISSING secrets directory skips the gate and applies"
assert_applied "and 'up -d' ran"
assert_ids_not_measured "and still no image is run"

# THE -f FILTER. Without it a subdirectory counts as an injected secret, and the skip arm on an
# uninjected box becomes a permanent refusal — the always-lit alarm this whole file family is
# written against.
rm -rf "$SECRETS"
mkdir -p "$SECRETS/not-a-secret"
expect_exit 0 "a NON-REGULAR entry does not count as an injected secret"
assert_applied "and 'up -d' ran"
assert_output_contains "ownership gate skipped" "and the gate still reports a skip"

echo "-- the gate's own preconditions"
# THE HELPER GUARD. Deleting `[ -x "$RUNTIME_IDS" ]` leaves every other case green, because every
# other case has a working helper.
seed_secrets
stub_docker "$OURS:latest" "$DIGEST"
stub_verifier 0
stub_runtime_ids "$(id -u)" "$(id -g)"
mv "$FIXTURE_IDS" "$FIXTURE_IDS.hidden"
expect_exit 2 "a MISSING runtime-id helper is 'cannot answer' (2), not a refusal and not a pass"
assert_not_applied "and nothing is applied"
assert_ids_not_measured "and nothing was run"
# BOUND TO THE GUARD'S OWN MESSAGE. Measured: with the guard deleted this case still exits 2,
# because the absent helper then fails at the call site and lands in the measurement's own
# `|| exit 2`. Two paths, one code — the exit-code assertion above passes either way, and only
# this line says WHICH check answered.
assert_output_contains "runtime-id helper missing" "and it is the PRECONDITION guard that answered"
mv "$FIXTURE_IDS.hidden" "$FIXTURE_IDS"

# A file the owner cannot read fails the gate's own CLAIM, which is readability and not ownership.
seed_secrets
chmod 0000 "$SECRETS/FieldEncryption__LocalMasterKeyBase64" 2>/dev/null || true
if [ "$(stat -c '%a' "$SECRETS/FieldEncryption__LocalMasterKeyBase64")" = "0" ]; then
  expect_exit 1 "right owner but mode 0000 → refuses; ownership alone is not readability"
  assert_not_applied "and nothing is applied"
  assert_output_contains "the owner cannot read it" "and the refusal names the mode, not the owner"
else
  skipped=$((skipped + 1))
  echo "  SKIP mode 0000 case: this filesystem does not honour chmod (Git Bash/Windows)."
  echo "       It RUNS in CI on ubuntu, where JBL_REQUIRE_MODE_CASES makes a skip an error."
  if [ "${JBL_REQUIRE_MODE_CASES:-0}" = "1" ]; then
    fail=$((fail + 1))
    echo "  FAIL JBL_REQUIRE_MODE_CASES=1 but chmod is not honoured here" >&2
  fi
fi
chmod 0400 "$SECRETS/FieldEncryption__LocalMasterKeyBase64" 2>/dev/null || true

echo "-- the rig can tell a GROUP from an OWNER"
# WITHOUT THIS THE TWO CENTRAL ASSERTIONS ARE INDISTINGUISHABLE. The fixture's uid equals its gid
# (measured: both platforms in play), so swapping `stat -c '%g'` for `%u` on the directory — or
# `%u` for `%g` on a file — leaves every other case in this section green. The only way to cross
# it unprivileged is a directory whose group is a SECONDARY group of this user, which is a group
# `chgrp` is permitted to set. Where no second group exists there is nothing to measure with, and
# that is a skip rather than an abort: unlike the stat-vs-id probe, this is a property of the
# host's account, not of the instrument.
second_gid=""
for g in $(id -G); do
  if [ "$g" != "$(id -g)" ]; then second_gid="$g"; break; fi
done

if [ -z "$second_gid" ]; then
  skipped=$((skipped + 1))
  echo "  SKIP no secondary group on this host (id -G = $(id -G)); the %g-vs-%u swap cannot be"
  echo "       distinguished here. It RUNS in CI, where JBL_REQUIRE_GROUP_CASES makes it an error."
  if [ "${JBL_REQUIRE_GROUP_CASES:-0}" = "1" ]; then
    fail=$((fail + 1))
    echo "  FAIL JBL_REQUIRE_GROUP_CASES=1 but no secondary group was available" >&2
  fi
else
  seed_secrets
  chgrp "$second_gid" "$SECRETS" 2>/dev/null || true
  chgrp "$second_gid" "$SECRETS"/* 2>/dev/null || true
  if [ "$(stat -c '%g' "$SECRETS")" != "$second_gid" ]; then
    skipped=$((skipped + 1))
    echo "  SKIP chgrp to $second_gid did not take on this filesystem"
  else
    # dir group = second_gid, file owner = id -u. A gate reading %u of the directory would
    # compare $(id -u) against want_gid and refuse; a gate reading %g of a file would compare
    # $second_gid against want_uid and refuse. Only the correct pair passes.
    stub_runtime_ids "$(id -u)" "$second_gid"
    stub_docker "$OURS:latest" "$DIGEST"
    stub_verifier 0
    expect_exit 0 "dir group != file owner, and the gate reads the RIGHT one of each"
    assert_applied "and 'up -d' ran"
  fi
  rm -rf "$SECRETS"
fi

echo
echo "passed: $pass   failed: $fail   skipped: $skipped"
[ "$fail" -eq 0 ]
