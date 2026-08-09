#!/usr/bin/env bash
#
# Fixture tests for jobbliggaren-inject-secrets.sh.
#
# Run:  bash deploy/systemd/jobbliggaren-inject-secrets.test.sh
#
# NEEDS NO DAEMON, NO ROOT AND NO NETWORK. The suite drives `--check`, which by design stats
# files and nothing else — that is what makes it runnable at boot before dockerd, and it is
# what makes it testable here. The injection path needs root, a docker daemon and a terminal
# for `read -rs`, so it is out of reach of CI; the runbook's cutover rows are its proof.
#
# WHY --check IS THE THING WORTH PINNING. It is the box's only alarm for a missing key: a
# crash-looping container never appears in `systemctl --failed`, so if this predicate is wrong
# an unplanned reboot leaves the API down with nothing on the only surface anyone reads
# (#1175: no log sink). A false "all present" is the failure this suite exists for.
#
# THE NEGATIVE FIXTURES CARRY THE FILE. A detector whose cases all pass has shown it does not
# crash, not that it detects anything. Each secret gets its own removal case rather than one
# representative: a loop that checked only the first name would report the whole list as
# covered — the exact shape mutation-testing caught in the attestation suite on 2026-08-08.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/jobbliggaren-inject-secrets.sh"
[ -f "$SUT" ] || {
  echo "missing script under test: $SUT" >&2
  exit 1
}

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

readonly SECRETS="$TMPROOT/run/jobbliggaren/secrets"

pass=0
fail=0

# The five files the running stack needs. Deliberately RESTATED here rather than sourced from
# the SUT: a test that derives its expectation from the code under test cannot detect the code
# dropping an entry. If these two lists disagree, that disagreement is the finding.
readonly -a EXPECTED_FILES=(
  "FieldEncryption__LocalMasterKeyBase64"
  "FieldEncryption__LocalMasterKeyId"
  "AuditPseudonymization__PepperBase64"
  "CompanyWatchPseudonymization__PepperBase64"
  "CvReviewFingerprintPseudonymization__PepperBase64"
)

seed_all_secrets() {
  rm -rf "$TMPROOT/run"
  mkdir -p "$SECRETS"
  for f in "${EXPECTED_FILES[@]}"; do
    printf '%s' "seeded-value-for-$f" > "$SECRETS/$f"
  done
}

# Runs --check against the fixture directory. SECRETS_DIR is readonly in the SUT, so the
# fixture redirects it by rewriting that one literal into a copy — the alternative (making the
# path an environment override) would add a production seam that exists only for tests.
run_check() {
  local sut_copy="$TMPROOT/sut.sh"
  sed "s#^readonly SECRETS_DIR=.*#readonly SECRETS_DIR=$SECRETS#" "$SUT" > "$sut_copy"
  # Prove the redirect actually landed. Without this, a change to the declaration's spelling
  # would silently leave the copy pointing at the REAL /run path, and every case below would
  # measure the host instead of the fixture — passing or failing for reasons unrelated to the
  # code. The rig reporting an unmeasured run as a result is a known failure class here.
  grep -qxF "readonly SECRETS_DIR=$SECRETS" "$sut_copy" || {
    echo "FIXTURE BROKEN: SECRETS_DIR redirect did not apply — the suite would measure the host" >&2
    exit 1
  }
  PATH="/usr/bin:/bin" bash "$sut_copy" --check >"$TMPROOT/out" 2>&1
}

expect_check() {
  local want="$1" desc="$2"
  local got=0
  run_check || got=$?
  if [ "$got" -eq "$want" ]; then
    pass=$((pass + 1))
    echo "  ok   $desc (exit $got)"
  else
    fail=$((fail + 1))
    echo "  FAIL $desc — wanted exit $want, got $got" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

echo "jobbliggaren-inject-secrets.sh --check"

echo "-- the positive case"
seed_all_secrets
expect_check 0 "all five secrets present is a pass"

echo "-- every secret is individually load-bearing"
for missing in "${EXPECTED_FILES[@]}"; do
  seed_all_secrets
  rm -f "$SECRETS/$missing"
  expect_check 1 "missing $missing is detected"

  # The journal line must NAME the file. An operator reading `systemctl --failed` at 03:00
  # needs to know which one, and a bare exit code does not carry that.
  if grep -qF "$missing" "$TMPROOT/out"; then
    pass=$((pass + 1))
    echo "  ok   the failure names $missing"
  else
    fail=$((fail + 1))
    echo "  FAIL the failure did not name $missing" >&2
  fi
done

echo "-- an EMPTY file is not a present secret"
# The reader treats whitespace-only content as absent, so a zero-byte file would otherwise
# crash-loop the stack while the detector reported everything healthy — the exact
# false-negative this suite exists for.
seed_all_secrets
: > "$SECRETS/FieldEncryption__LocalMasterKeyBase64"
expect_check 1 "a zero-byte master key is detected as missing"

echo "-- a missing directory is the post-reboot state"
rm -rf "$TMPROOT/run"
expect_check 1 "no secrets directory at all is detected"

echo "-- --check never touches docker"
# It runs at boot, potentially before dockerd. If it ever shells out to docker, a boot-time
# run would fail for the wrong reason and the alarm would mean something other than what it
# says. Proven by putting a docker on PATH that fails loudly if invoked.
seed_all_secrets
mkdir -p "$TMPROOT/bin"
cat >"$TMPROOT/bin/docker" <<'EOF'
#!/usr/bin/env bash
echo "DOCKER WAS INVOKED" >&2
exit 97
EOF
chmod +x "$TMPROOT/bin/docker"
sut_copy="$TMPROOT/sut.sh"
sed "s#^readonly SECRETS_DIR=.*#readonly SECRETS_DIR=$SECRETS#" "$SUT" > "$sut_copy"
got=0
PATH="$TMPROOT/bin:/usr/bin:/bin" bash "$sut_copy" --check >"$TMPROOT/out" 2>&1 || got=$?
if [ "$got" -eq 0 ] && ! grep -qF "DOCKER WAS INVOKED" "$TMPROOT/out"; then
  pass=$((pass + 1))
  echo "  ok   --check completed without invoking docker"
else
  fail=$((fail + 1))
  echo "  FAIL --check invoked docker or did not pass (exit $got)" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo "-- argument handling"
seed_all_secrets
got=0
PATH="/usr/bin:/bin" bash "$sut_copy" --nonsense >"$TMPROOT/out" 2>&1 || got=$?
if [ "$got" -eq 1 ] && grep -qF "unknown argument" "$TMPROOT/out"; then
  pass=$((pass + 1))
  echo "  ok   an unknown argument is refused, never treated as --check"
else
  fail=$((fail + 1))
  echo "  FAIL an unknown argument was not refused (exit $got)" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ]
