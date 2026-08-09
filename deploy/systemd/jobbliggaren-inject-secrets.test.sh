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
skipped=0

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

# Whether this filesystem honours chmod at all. Git Bash on Windows does not — `chmod 0710`
# returns 0 and the directory stays 0755 — so the directory-mode cases cannot be measured
# there. They are SKIPPED LOUDLY rather than silently tolerated: a suite that quietly drops a
# case on the developer's machine and runs it only in CI is a suite whose local green means
# less than it appears to. CI runs on ubuntu, where they execute.
MODE_ENFORCED=no
probe=$(mktemp -d "$TMPROOT/modeprobe.XXX")
chmod 0710 "$probe" 2>/dev/null || true
[ "$(stat -c '%a' "$probe" 2>/dev/null)" = "710" ] && MODE_ENFORCED=yes
readonly MODE_ENFORCED
rm -rf "$probe"

seed_all_secrets() {
  rm -rf "$TMPROOT/run"
  mkdir -p "$SECRETS"
  # 0710 is the mode the running stack needs and the mode --check asserts: root owns the
  # directory, the container's group traverses it. The fixture must reproduce the production
  # shape, not a convenient one.
  chmod 0710 "$SECRETS" 2>/dev/null || true
  for f in "${EXPECTED_FILES[@]}"; do
    printf '%s' "seeded-value-for-$f" > "$SECRETS/$f"
  done
}

# Builds a copy of the SUT with SECRETS_DIR redirected at the fixture, and PROVES the redirect
# landed. Without that proof a change to the declaration's spelling would leave the copy pointing
# at the real /run path, and every case would measure the host instead of the fixture — passing
# or failing for reasons unrelated to the code. The rig reporting an unmeasured run as a result
# is a known failure class here.
#
# Extracted to one function deliberately: three call sites used to build this copy, two of them
# with a duplicated and UNVERIFIED sed. On a runner that happened to have /run/jobbliggaren/secrets
# populated, those two would have passed for the wrong reason.
make_sut_copy() {
  local sut_copy="$TMPROOT/sut.sh"
  sed "s#^readonly SECRETS_DIR=.*#readonly SECRETS_DIR=$SECRETS#" "$SUT" > "$sut_copy"
  grep -qxF "readonly SECRETS_DIR=$SECRETS" "$sut_copy" || {
    echo "FIXTURE BROKEN: SECRETS_DIR redirect did not apply — the suite would measure the host" >&2
    exit 1
  }
  printf '%s' "$sut_copy"
}

run_check() {
  PATH="/usr/bin:/bin" bash "$(make_sut_copy)" --check >"$TMPROOT/out" 2>&1
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
if [ "$MODE_ENFORCED" = "yes" ]; then
  seed_all_secrets
  expect_check 0 "all five secrets present is a pass"
else
  skipped=$((skipped + 1))
  echo "  SKIP positive case: --check asserts directory mode 0710 and this filesystem does not"
  echo "       honour chmod (Git Bash/Windows). It RUNS in CI on ubuntu."
fi

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
seed_all_secrets
: > "$SECRETS/FieldEncryption__LocalMasterKeyBase64"
expect_check 1 "a zero-byte master key is detected as missing"

echo "-- a WHITESPACE-ONLY file is not a present secret either"
# THE PREDICATE-DIVERGENCE CASE, and it is the one that matters most. The reader trims and
# treats whitespace-only as absent, so a file holding a single space passes a size test while
# the app refuses to boot. Before this case existed, --check used `-s` alone: it reported
# "all secrets present" while api and worker crash-looped, which is a false all-clear on the
# box's only alarm surface. A zero-byte file (above) does NOT cross this threshold — `-s`
# catches that one — so a suite with only the zero-byte case proves nothing about whitespace.
#
# Producible by the actor the runbook itself names: the operator, editing a file by hand during
# rotation, or any `echo > file`. The three peppers have no content validation at all, so
# nothing upstream would reject it.
for ws in " " "  " "$(printf '\n')" "$(printf '\t')"; do
  seed_all_secrets
  printf '%s' "$ws" > "$SECRETS/AuditPseudonymization__PepperBase64"
  expect_check 1 "a whitespace-only pepper is detected as missing"
done

echo "-- the DIRECTORY's mode is load-bearing, not just the files"
# Files present but the directory un-traversable by the container's group is a real crash-loop
# state: systemd-tmpfiles can revoke the injection script's chmod on any later --create. A
# files-only sweep reports that state as healthy, which is the second false all-clear.
if [ "$MODE_ENFORCED" = "yes" ]; then
  seed_all_secrets
  chmod 0700 "$SECRETS"
  expect_check 1 "a 0700 directory (container cannot traverse) is detected"
  if grep -qF "WRONG MODE" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   the failure names the mode problem, not a missing file"
  else
    fail=$((fail + 1)); echo "  FAIL the 0700 directory was reported as a missing file" >&2
  fi
else
  skipped=$((skipped + 2))
  echo "  SKIP directory-mode cases: this filesystem does not honour chmod (Git Bash/Windows)."
  echo "       They RUN in CI on ubuntu. A local green here does not cover them."
fi

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
sut_copy=$(make_sut_copy)
got=0
PATH="$TMPROOT/bin:/usr/bin:/bin" bash "$sut_copy" --check >"$TMPROOT/out" 2>&1 || got=$?
# The property under test is "docker was never invoked", which holds regardless of the
# verdict --check reaches, so this case does not depend on the mode assertion.
if ! grep -qF "DOCKER WAS INVOKED" "$TMPROOT/out"; then
  pass=$((pass + 1))
  echo "  ok   --check completed without invoking docker"
else
  fail=$((fail + 1))
  echo "  FAIL --check invoked docker (exit $got)" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo "-- argument handling"
seed_all_secrets
got=0
PATH="/usr/bin:/bin" bash "$(make_sut_copy)" --nonsense >"$TMPROOT/out" 2>&1 || got=$?
if [ "$got" -eq 1 ] && grep -qF "unknown argument" "$TMPROOT/out"; then
  pass=$((pass + 1))
  echo "  ok   an unknown argument is refused, never treated as --check"
else
  fail=$((fail + 1))
  echo "  FAIL an unknown argument was not refused (exit $got)" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo
echo "passed: $pass   failed: $fail   skipped: $skipped"
[ "$fail" -eq 0 ]
