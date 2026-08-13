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
readonly HOST_SECRETS="$TMPROOT/run/jobbliggaren/host-secrets"
readonly ENV_FIXTURE="$TMPROOT/deploy.env"

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

# The host-only files (#197). Restated here for the same reason as the list above: a test that
# derives its expectation from the code under test cannot detect the code dropping an entry.
# These live in a DIFFERENT directory, and that separation is the control — see
# jobbliggaren-tmpfiles.conf. A case that seeded them into $SECRETS would pass while the
# structural property it exists to protect was gone.
readonly -a EXPECTED_HOST_FILES=(
  "Backup__RcloneConfigBase64"
)

# The two files that are required ONLY when EMAIL_PROVIDER=Ses. Restated here for the same
# reason as the lists above — a test deriving its expectation from the code under test cannot
# detect the code dropping an entry — and they are deliberately NOT seeded by
# seed_all_secrets(): the whole point of the cases below is that a stack without them is
# healthy until the provider is flipped.
readonly -a EXPECTED_SES_FILES=(
  "Email__Ses__AccessKeyId"
  "Email__Ses__SecretAccessKey"
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
  # The .env fixture is reset HERE rather than by each case, so "no provider set" is the state a
  # case starts from structurally instead of by being written before the block that sets one.
  # It was order-dependent until 2026-08-12: nothing mismeasured, but the property held because
  # of where the SES block sat in the file, which is not a property at all.
  rm -f "$ENV_FIXTURE"
  mkdir -p "$SECRETS"
  # 0710 is the mode the running stack needs and the mode --check asserts: root owns the
  # directory, the container's group traverses it. The fixture must reproduce the production
  # shape, not a convenient one.
  chmod 0710 "$SECRETS" 2>/dev/null || true
  for f in "${EXPECTED_FILES[@]}"; do
    printf '%s' "seeded-value-for-$f" > "$SECRETS/$f"
  done
  mkdir -p "$HOST_SECRETS"
  chmod 0700 "$HOST_SECRETS" 2>/dev/null || true
  for f in "${EXPECTED_HOST_FILES[@]}"; do
    printf '%s' "seeded-value-for-$f" > "$HOST_SECRETS/$f"
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
  sed \
    -e "s#^readonly SECRETS_DIR=.*#readonly SECRETS_DIR=$SECRETS#" \
    -e "s#^readonly HOST_SECRETS_DIR=.*#readonly HOST_SECRETS_DIR=$HOST_SECRETS#" \
    -e "s#^readonly ENV_FILE=.*#readonly ENV_FILE=$ENV_FIXTURE#" \
    "$SUT" > "$sut_copy"
  grep -qxF "readonly SECRETS_DIR=$SECRETS" "$sut_copy" || {
    echo "FIXTURE BROKEN: SECRETS_DIR redirect did not apply — the suite would measure the host" >&2
    exit 1
  }
  grep -qxF "readonly HOST_SECRETS_DIR=$HOST_SECRETS" "$sut_copy" || {
    echo "FIXTURE BROKEN: HOST_SECRETS_DIR redirect did not apply — the suite would measure the host" >&2
    exit 1
  }
  # Same proof as the two above, and it earns its place for a sharper reason: the SES cases are
  # the only ones whose EXPECTED result depends on a file's CONTENT rather than its presence. An
  # unapplied redirect here would point at the real /opt/jobbliggaren/deploy/.env, which on a
  # developer machine and on a CI runner alike does not exist — so email_provider would answer
  # `console`, the provider-is-Ses cases would measure the not-Ses branch, and BOTH would still
  # report the exit code they wanted. Green for the opposite reason.
  grep -qxF "readonly ENV_FILE=$ENV_FIXTURE" "$sut_copy" || {
    echo "FIXTURE BROKEN: ENV_FILE redirect did not apply — the suite would measure the host" >&2
    exit 1
  }
  printf '%s' "$sut_copy"
}

# The provider is written by the case that needs it, never left over from the previous one:
# these cases differ ONLY in this file's content, so a stale value is a case measuring its
# neighbour's condition.
set_env_provider() {
  local value="${1:-}"
  if [ -z "$value" ]; then
    rm -f "$ENV_FIXTURE"
    return
  fi
  write_env "SITE_HOST=jobbliggaren.se" "EMAIL_PROVIDER=$value"
}

# Writes the fixture verbatim, one argument per line. The parser cases need RAW lines rather
# than a value: every form that broke the first implementation is a property of the LINE — an
# export prefix, a colon delimiter, a trailing comment — and a helper taking a value cannot
# express any of them. Passing no arguments writes an .env that EXISTS and is EMPTY, which is a
# distinct state from having no file at all.
write_env() {
  : > "$ENV_FIXTURE"
  local line
  for line in "$@"; do printf '%s\n' "$line" >> "$ENV_FIXTURE"; done
}

run_check() {
  PATH="/usr/bin:/bin" bash "$(make_sut_copy)" --check >"$TMPROOT/out" 2>&1
}

# The host-only detector is a SEPARATE entry point since #1329, so it needs a separate runner.
# Same fixture, same redirected copy — only the argument differs.
run_check_host() {
  PATH="/usr/bin:/bin" bash "$(make_sut_copy)" --check-host >"$TMPROOT/out" 2>&1
}

# The two summaries, by a substring unique to each. Bound to the SUMMARY and never to a per-line
# message, because which entry point answered is invisible to every `MISSING:` assertion here.
readonly BLOCKING_SUMMARY="crash-loop by design"
readonly HOST_ONLY_SUMMARY="A host-only secret is absent"

# Asserts the blocking summary, and the ABSENCE of the host-only one. The negative half is what
# makes it a pin: a check moved to the wrong entry point still prints its own MISSING line, still
# exits 1, and still satisfies every other assertion in this suite.
assert_blocking_summary() {
  local desc="$1"
  if grep -qF "$BLOCKING_SUMMARY" "$TMPROOT/out" && ! grep -qF "$HOST_ONLY_SUMMARY" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   $desc summarises as blocking"
  else
    fail=$((fail + 1)); echo "  FAIL $desc did not summarise as blocking" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

expect_check_host() {
  local want="$1" desc="$2"
  local got=0
  run_check_host || got=$?
  if [ "$got" -eq "$want" ]; then
    pass=$((pass + 1)); echo "  ok   $desc (exit $got)"
  else
    fail=$((fail + 1)); echo "  FAIL $desc — wanted exit $want, got $got" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
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

  # Recover what CAN be measured here. Without this, a chmod-less run has NO exit-0 assertion
  # at all -- the positive case is skipped and the docker case asserts only that docker was not
  # invoked -- so the file loop's happy path would go entirely unmeasured locally.
  seed_all_secrets
  run_check || true
  if grep -q "MISSING:" "$TMPROOT/out"; then
    fail=$((fail + 1))
    echo "  FAIL with all five files seeded, --check still reported a MISSING file" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  else
    pass=$((pass + 1))
    echo "  ok   with all five seeded, no file is reported missing (mode branch not measured)"
  fi
fi

echo "-- every secret is individually load-bearing"
for missing in "${EXPECTED_FILES[@]}"; do
  seed_all_secrets
  rm -f "$SECRETS/$missing"
  expect_check 1 "missing $missing is detected"

  # The journal line must NAME the file. An operator reading `systemctl --failed` at 03:00
  # needs to know which one, and a bare exit code does not carry that.
  if grep -qF "MISSING: ${SECRETS}/${missing}" "$TMPROOT/out"; then
    pass=$((pass + 1))
    echo "  ok   the failure names $missing"
  else
    fail=$((fail + 1))
    echo "  FAIL the failure did not name $missing" >&2
  fi
done

echo "-- the host-only secrets answer on their OWN entry point (#1329)"
# Their absence has a different consequence from a missing master key — the stack keeps serving
# and only the nightly backup stops — so since the split they are a different check, a different
# unit and a different alarm. The message is asserted, not just the exit code.
for missing in "${EXPECTED_HOST_FILES[@]}"; do
  seed_all_secrets
  rm -f "$HOST_SECRETS/$missing"
  expect_check_host 1 "missing $missing is detected by --check-host"
  if grep -qF "MISSING: ${HOST_SECRETS}/${missing}" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   the failure names $missing in the host-only directory"
  else
    fail=$((fail + 1)); echo "  FAIL the failure did not name $missing" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
done

echo "-- THE WHOLE POINT OF THE SPLIT: --check is silent about the host-only set (#1329)"
# THIS IS THE CASE THE SPLIT EXISTS FOR, and it is a counterfactual, not a tautology. Before it,
# an absent rclone credential failed --check, so jobbliggaren-secrets-present.timer could not be
# enabled until #197's ops half landed — and enabling it anyway put a permanent entry on
# `systemctl --failed`, which is a continuous page through heartbeat P1. Measured on the box
# 2026-08-13. Both halves are asserted: --check goes GREEN, --check-host goes RED, in one and the
# same fixture state.
#
# Mode-gated because --check asserts the directory mode, which a chmod-less filesystem cannot
# reproduce; the --check-host half needs no such gate and is asserted above regardless.
if [ "$MODE_ENFORCED" = "yes" ]; then
  seed_all_secrets
  rm -f "$HOST_SECRETS/Backup__RcloneConfigBase64"
  expect_check 0 "--check passes with the host-only credential absent"
  expect_check_host 1 "--check-host fails in the very same state"
else
  skipped=$((skipped + 2))
  echo "  SKIP the split's counterfactual: --check asserts directory mode and this filesystem"
  echo "       does not honour chmod. It RUNS in CI."
fi

# THE DIRECTORIES MUST NOT BE INTERCHANGEABLE, and this is the case that says so. Seeding the
# credential into the MOUNTED directory instead of the host-only one must still read as missing:
# if --check-host accepted either location, the structural separation that keeps the credential
# away from api and worker could be undone by an operator's typo and nothing would report it.
seed_all_secrets
rm -f "$HOST_SECRETS/Backup__RcloneConfigBase64"
printf '%s' "seeded-in-the-wrong-place" > "$SECRETS/Backup__RcloneConfigBase64"
run_check_host || true
if grep -qF "MISSING: ${HOST_SECRETS}/Backup__RcloneConfigBase64" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   the credential in the MOUNTED directory does not satisfy the check"
else
  fail=$((fail + 1)); echo "  FAIL a credential in the container-readable directory was accepted" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo "-- every branch of --check reaches the BLOCKING summary, and only those (#1328)"
# WHAT THE SPLIT DID NOT REMOVE. --check now answers for one set, so its single summary is true
# of every route that reaches it — but "every route" is still six branches, and which summary a
# branch reaches is invisible to every per-line and exit-code assertion in this file. Measured
# 2026-08-13 on the pre-split shape: moving the INVALID-provider branch alone to the host counter
# left the ENTIRE suite green while --check told the operator the stack was serving over a box
# AddEmailSender refuses to start. The same blindness would let a branch drift to --check-host.
#
# The host-only summary is asserted ABSENT in each case, which is what makes these pins rather
# than restatements of the exit code.
#
# Mode-gated, and not incidentally: where chmod is unavailable the WRONG MODE branch fires in
# every case, so each would pass for a reason that is not its own.
if [ "$MODE_ENFORCED" = "yes" ]; then
  # EVERY BLOCKING BRANCH, ONE ISOLATED CASE EACH — not one representative. Measured 2026-08-13:
  # moving the INVALID-provider branch alone from `missing` to `host_missing` left the ENTIRE
  # suite green while --check told the operator the stack was serving over a box AddEmailSender
  # refuses to start. Every other assertion here binds to a per-line string or to exit 1, and a
  # miscounted branch changes neither. Each case below isolates ONE branch, so a pass measures
  # that branch alone.
  #
  # The `-d` branch for SECRETS_DIR is deliberately absent, and named rather than faked: with no
  # directory the five file branches fire too, so no fixture can isolate it. The file loop's own
  # case below covers the counter it shares.
  seed_all_secrets; rm -f "$SECRETS/FieldEncryption__LocalMasterKeyBase64"; run_check || true
  assert_blocking_summary "a missing crypto file"

  seed_all_secrets; chmod 0700 "$SECRETS"; run_check || true
  assert_blocking_summary "a wrong directory mode"

  seed_all_secrets; set_env_provider "Resend"; run_check || true
  assert_blocking_summary "an invalid EMAIL_PROVIDER"

  # SES files absent, all three .env variables set — so the variable branch stays quiet and the
  # file branch is the only one firing.
  seed_all_secrets
  write_env "EMAIL_PROVIDER=Ses" "EMAIL_SES_ACCESS_KEY_ID_FILE=/x" \
    "EMAIL_SES_SECRET_ACCESS_KEY_FILE=/y" "EMAIL_SES_REGION=eu-north-1"
  run_check || true
  assert_blocking_summary "a missing SES credential file"

  # The mirror: files present, one variable unset.
  seed_all_secrets
  for k in "${EXPECTED_SES_FILES[@]}"; do printf '%s' "seeded-value-for-$k" > "$SECRETS/$k"; done
  write_env "EMAIL_PROVIDER=Ses" "EMAIL_SES_ACCESS_KEY_ID_FILE=/x" \
    "EMAIL_SES_SECRET_ACCESS_KEY_FILE=/y"
  run_check || true
  assert_blocking_summary "an unset EMAIL_SES_REGION"

  # BOTH SETS ABSENT — the state the box is in after EVERY reboot, since /run is tmpfs. Since the
  # split the two entry points answer independently, so this case pins that independence in the
  # one state where a shared predicate would show: --check must still summarise as blocking, and
  # must not borrow the host-only wording for a host-only file it no longer reads.
  seed_all_secrets
  rm -f "$SECRETS/FieldEncryption__LocalMasterKeyBase64"
  rm -f "$HOST_SECRETS/Backup__RcloneConfigBase64"
  run_check || true
  assert_blocking_summary "both sets absent (the post-reboot state)"

  # And --check must not NAME the host-only file at all: it is no longer its business, and a
  # MISSING line for a file this entry point does not own is how the two sets creep back together.
  if ! grep -qF "$HOST_SECRETS" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   --check does not mention the host-only directory"
  else
    fail=$((fail + 1)); echo "  FAIL --check still reports on the host-only set" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
else
  skipped=$((skipped + 7))
  echo "  SKIP summary cases: this filesystem does not honour chmod, so the mode branch sets the"
  echo "       blocking counter in every case and each would pass for a reason that is not its"
  echo "       own. Six branch cases plus the host-only-directory pin. They RUN in CI."
fi

echo "-- a missing host-only DIRECTORY is the post-reboot state"
seed_all_secrets
rm -rf "$HOST_SECRETS"
run_check_host || true
if grep -qF "MISSING: $HOST_SECRETS (directory does not exist)" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   a missing host-only directory is reported as a missing DIRECTORY"
else
  fail=$((fail + 1)); echo "  FAIL a missing host-only directory was not distinguished" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo "-- an EMPTY file is not a present secret"
# Message-bound for the same reason as the whitespace block below: --check reaches exit 1 by
# more than one route, so an exit code alone cannot say WHICH check fired.
seed_all_secrets
: > "$SECRETS/FieldEncryption__LocalMasterKeyBase64"
run_check || true
if grep -qF "MISSING: ${SECRETS}/FieldEncryption__LocalMasterKeyBase64" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   a zero-byte master key is reported missing by name"
else
  fail=$((fail + 1)); echo "  FAIL a zero-byte master key was NOT reported missing" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

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
#
# THE ASSERTION IS ON THE MESSAGE, NOT ON THE EXIT CODE, and that is not decoration. --check
# has more than one reason to exit 1 (a wrong directory mode is another), so on a filesystem
# that ignores chmod the mode branch fires first and the exit code alone would report these
# cases as passing while the whitespace predicate was gone. Measured: with the whitespace half
# of has_usable_content deleted, an exit-code-only assertion stayed green here. Binding to
# "MISSING: <the pepper>" makes the case fail for the one reason it exists to detect.
for ws in " " "  " "$(printf '\n')" "$(printf '\t')"; do
  seed_all_secrets
  printf '%s' "$ws" > "$SECRETS/AuditPseudonymization__PepperBase64"
  run_check || true
  if grep -qF "MISSING: ${SECRETS}/AuditPseudonymization__PepperBase64" "$TMPROOT/out"; then
    pass=$((pass + 1))
    echo "  ok   a whitespace-only pepper is reported missing by name"
  else
    fail=$((fail + 1))
    echo "  FAIL a whitespace-only pepper was NOT reported missing" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
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
# OVERDETERMINED, and named as such: with no directory the -d branch fires AND all five file
# branches fire. Deleting the -d check entirely leaves this case green (stat fails, dir_mode
# becomes "?", WRONG MODE fires). So it is bound to the directory branch's own wording, which
# is the only string that distinguishes "no directory" from "the files are gone".
rm -rf "$TMPROOT/run"
run_check || true
if grep -qF "MISSING: $SECRETS (directory does not exist)" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   a missing directory is reported as a missing DIRECTORY"
else
  fail=$((fail + 1)); echo "  FAIL a missing directory was not distinguished from missing files" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo "-- --check never touches docker"
# It runs at boot, potentially before dockerd. If it ever shells out to docker, a boot-time
# run would fail for the wrong reason and the alarm would mean something other than what it
# says. Proven by putting a docker on PATH that fails loudly if invoked.
seed_all_secrets
# The probe must CROSS the branch it is testing. With no .env the mail branch is skipped
# entirely, so this case would have proven docker-freedom only of the code path that existed
# before the SES work — and the new branch, which is the one that reads a file, would be the
# unproven one. Ses is set so the whole branch executes under the poisoned PATH.
set_env_provider "Ses"
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

# A TRAILING ARGUMENT AFTER A FLAG THAT MATCHED, WHICH THE CASE ABOVE CANNOT REACH. `--nonsense`
# matches neither branch and falls to the catch-all; a first argument that DID match never gets
# there, so each branch needs its own guard and each guard needs its own case.
#
# BOUND TO THE MESSAGE, NEVER TO THE EXIT CODE, AND THAT ALONE IS WHAT MAKES THESE
# COUNTERFACTUALS. Exit 1 is shared with the catch-all above and with every absence in the suite,
# so it cannot tell which refusal fired; the parenthetical can. Measured 2026-08-13: each
# `(use <flag> on its own)` string has exactly ONE emitting source, its own guard, so deleting that
# guard puts the string out of reach and the case fells the mutant whatever the fixture holds.
#
# SEEDED COMPLETE FOR TWO OTHER REASONS, neither of them the counterfactual — an earlier revision
# of this comment claimed the seed carried it, which is false in the direction that matters: it
# would invite the next case to copy the seed and bind to the exit code alone.
#   1. It reproduces PRODUCTION'S DAMAGE SHAPE. With the guard gone and the fixture complete,
#      `--check --host` exits 0 saying "all secrets present" — the operator asked about the
#      host-only set, spelled it one keystroke off, and was told the OTHER set was fine. Against an
#      incomplete fixture the same mutant exits 1 with the wrong message, which fails the case for
#      a reason an operator never meets.
#   2. Independence from whatever the previous case left behind — the reason seed_all_secrets
#      clears $TMPROOT/run and $ENV_FIXTURE at all (see its own note: order-dependence was
#      removed 2026-08-12).
for spec in "--check:--host" "--check-host:--host"; do
  flag=${spec%%:*}; extra=${spec#*:}
  seed_all_secrets
  got=0
  PATH="/usr/bin:/bin" bash "$(make_sut_copy)" "$flag" "$extra" >"$TMPROOT/out" 2>&1 || got=$?
  if [ "$got" -eq 1 ] && grep -qF "(use $flag on its own)" "$TMPROOT/out"; then
    pass=$((pass + 1))
    echo "  ok   $flag refuses a trailing argument rather than swallowing it"
  else
    fail=$((fail + 1))
    echo "  FAIL $flag did not refuse '$extra' naming itself (exit $got)" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
done

echo "-- the SES credentials are conditional on EMAIL_PROVIDER (#183)"
#
# EVERY CASE HERE IS BOUND TO THE FILE NAMES, NEVER TO THE EXIT CODE. Where chmod is
# unavailable the directory-mode branch already sets missing=1, so an exit-code assertion would
# report the wanted number without having measured this condition at all — and the two
# provider-is-Ses cases would be indistinguishable from the two provider-is-Console ones.

seed_all_secrets
set_env_provider ""
run_check || true
if ! grep -qE "Email__Ses__(AccessKeyId|SecretAccessKey)|INVALID: EMAIL_PROVIDER" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   no .env at all does not demand the SES credentials"
else
  fail=$((fail + 1)); echo "  FAIL an absent .env demanded the SES credentials" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

seed_all_secrets
set_env_provider "Console"
run_check || true
if ! grep -qE "Email__Ses__(AccessKeyId|SecretAccessKey)|INVALID: EMAIL_PROVIDER" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   EMAIL_PROVIDER=Console does not demand the SES credentials"
else
  fail=$((fail + 1)); echo "  FAIL EMAIL_PROVIDER=Console demanded the SES credentials" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

# One case per file rather than one representative, for the reason the suite already states
# about its other lists: a loop that checked only the first name would report both as covered.
# The OTHER file is seeded in each case, so a passing case measures this file alone.
for missing_key in "${EXPECTED_SES_FILES[@]}"; do
  seed_all_secrets
  set_env_provider "Ses"
  for k in "${EXPECTED_SES_FILES[@]}"; do
    [ "$k" = "$missing_key" ] && continue
    printf '%s' "seeded-value-for-$k" > "$SECRETS/$k"
  done
  run_check || true
  if grep -qF "MISSING: $SECRETS/$missing_key" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   EMAIL_PROVIDER=Ses demands $missing_key"
  else
    fail=$((fail + 1)); echo "  FAIL EMAIL_PROVIDER=Ses did not demand $missing_key" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
done

# CROSSES THE PREDICATE'S OWN THRESHOLD. AddEmailSender compares the provider with
# OrdinalIgnoreCase, so `ses` IS Ses to the application. A name-exact guard would decline to
# demand the files while the app accepted the value, and the box would boot into
# AddEmailSender's registration-time throw with the credentials absent — precisely the state
# --check exists to catch, missed by the detector itself.
seed_all_secrets
set_env_provider "ses"
run_check || true
if grep -qF "MISSING: $SECRETS/Email__Ses__AccessKeyId" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   a lower-case ses is Ses, as it is to AddEmailSender"
else
  fail=$((fail + 1)); echo "  FAIL a lower-case ses did not demand the SES credentials" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

seed_all_secrets
set_env_provider "Ses"
for k in "${EXPECTED_SES_FILES[@]}"; do
  printf '%s' "seeded-value-for-$k" > "$SECRETS/$k"
done
run_check || true
if ! grep -qE "MISSING: .*Email__Ses__" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   EMAIL_PROVIDER=Ses with both files present reports neither"
else
  fail=$((fail + 1)); echo "  FAIL a present SES credential was still reported missing" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo "-- the .env forms compose accepts, all of which mean Ses"
#
# THE FIRST IMPLEMENTATION READ ONLY THE FIRST OF THESE and answered not-Ses to the rest, which
# is the fail-OPEN direction: compose renders Ses, AddEmailSender throws at registration, the
# containers crash-loop, and --check exits 0 saying "all secrets present" over a dead box. All
# three reviewing agents found it independently. Each line below was measured against Compose
# v2.40.3 on 2026-08-12 to render `Ses`; they are the fixture BECAUSE compose accepts them, not
# because a parser happens to.
for env_line in \
  'EMAIL_PROVIDER=Ses' \
  'EMAIL_PROVIDER=Ses # flippat 2026-08-12' \
  'export EMAIL_PROVIDER=Ses' \
  'EMAIL_PROVIDER: Ses' \
  'EMAIL_PROVIDER="Ses" # quoted plus comment' \
  '  EMAIL_PROVIDER   =   ses   ' \
  ; do
  seed_all_secrets
  write_env "SITE_HOST=jobbliggaren.se" "$env_line"
  run_check || true
  if grep -qF "MISSING: $SECRETS/Email__Ses__AccessKeyId" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   [$env_line] is Ses"
  else
    fail=$((fail + 1)); echo "  FAIL [$env_line] was not read as Ses" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
done

echo "-- a quoted value KEEPS its hash, and compose treats that as a boot refusal"
# The fix for the parser introduced this one and security-auditor measured it: compose renders
# `Ses # x` for a single-quoted value, which AddEmailSender throws on, while a naive strip read
# it as `Ses` and reported a box configured for SES that does not start. WIDER is fail-closed
# everywhere except here, which is why the strip now stops at the closing quote.
seed_all_secrets
write_env "SITE_HOST=jobbliggaren.se" "EMAIL_PROVIDER='Ses # not really'"
run_check || true
if grep -qF "INVALID: EMAIL_PROVIDER=" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   a quoted hash makes the value unknown, as it is to compose"
else
  fail=$((fail + 1)); echo "  FAIL a quoted hash was stripped and read as Ses" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo "-- an .env that exists without the key is the box's state today"
# THE MUTATION THIS EXISTS FOR: `== "ses"` -> `!= "console"` survived the whole suite before
# this case, and under it the box's own .env — which has no EMAIL_PROVIDER line at all — would
# take a permanent MISSING. That is verbatim the outcome the design exists to avoid, and no
# case could see it, because every fixture either had no file or had the key.
seed_all_secrets
write_env "SITE_HOST=jobbliggaren.se" "POSTGRES_APP_PASSWORD=x"
run_check || true
if ! grep -qE "Email__Ses__|EMAIL_SES_|INVALID: EMAIL_PROVIDER" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   an .env with no EMAIL_PROVIDER line demands nothing"
else
  fail=$((fail + 1)); echo "  FAIL an .env with no EMAIL_PROVIDER line demanded SES config" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo "-- a value that is neither Console nor Ses is not a quieter Console"
# AddEmailSender's switch ends in `else throw`, so the box does not boot on this value either.
# A detector that answered "not Ses" would go green on a stack that cannot start.
seed_all_secrets
set_env_provider "Resend"
run_check || true
if grep -qF "INVALID: EMAIL_PROVIDER='Resend'" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   an unknown provider is reported by name, not silently ignored"
else
  fail=$((fail + 1)); echo "  FAIL an unknown provider was treated as Console" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo "-- a _FILE pointer alone demands the credentials, whatever the provider says"
# EnvFileSecretsConfiguration throws on a pointer naming a path it cannot read and never
# consults Email:Provider, so this is a boot refusal with the provider back on Console — the
# state reached by rolling the flip BACK and rebooting, which a provider-only predicate calls
# healthy.
seed_all_secrets
write_env "EMAIL_PROVIDER=Console" \
  "EMAIL_SES_ACCESS_KEY_ID_FILE=/run/app-secrets/Email__Ses__AccessKeyId"
run_check || true
if grep -qF "MISSING: $SECRETS/Email__Ses__AccessKeyId" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   a set pointer demands the file even under Console"
else
  fail=$((fail + 1)); echo "  FAIL a set pointer under Console demanded nothing" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo "-- injected files do not make the flip complete"
# The other half of the same boot refusal: files present, .env lines absent. One case per
# variable, because a loop checking only the first would report all three as covered.
for ses_var in EMAIL_SES_ACCESS_KEY_ID_FILE EMAIL_SES_SECRET_ACCESS_KEY_FILE EMAIL_SES_REGION; do
  seed_all_secrets
  for k in "${EXPECTED_SES_FILES[@]}"; do
    printf '%s' "seeded-value-for-$k" > "$SECRETS/$k"
  done
  # Everything set EXCEPT the one under test, so a pass measures that variable alone.
  lines=("EMAIL_PROVIDER=Ses")
  for other in EMAIL_SES_ACCESS_KEY_ID_FILE EMAIL_SES_SECRET_ACCESS_KEY_FILE EMAIL_SES_REGION; do
    [ "$other" = "$ses_var" ] && continue
    lines+=("$other=set-for-fixture")
  done
  write_env "${lines[@]}"
  run_check || true
  if grep -qF "MISSING: ${ses_var} is unset" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   EMAIL_PROVIDER=Ses demands ${ses_var}"
  else
    fail=$((fail + 1)); echo "  FAIL a missing ${ses_var} was not reported" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
done

echo
echo "passed: $pass   failed: $fail   skipped: $skipped"

# THE SKIP IS ACCOUNTED FOR, NOT MERELY ANNOUNCED. Without this, a run that skipped the
# directory-mode cases exits identically to one that ran them, and "they RUN in CI" would be a
# claim enforced by nothing. build.yml sets JBL_REQUIRE_MODE_CASES=1, so on the runner a skip
# is a failure rather than a line of prose.
if [ -n "${JBL_REQUIRE_MODE_CASES:-}" ] && [ "$skipped" -ne 0 ]; then
  echo "FAIL: JBL_REQUIRE_MODE_CASES is set but $skipped case(s) were skipped." >&2
  echo "      This environment must honour chmod; a skip here means the suite measured less" >&2
  echo "      than it reports." >&2
  exit 1
fi

[ "$fail" -eq 0 ]
