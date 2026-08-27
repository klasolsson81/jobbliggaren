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

# THE PRODUCTION VALUES THEMSELVES, PINNED AGAINST THE ORIGINAL AND NEVER THE COPY. The seam below
# rewrites both owner constants in the SUT COPY so that both arms are reachable unprivileged — and
# that rewrite is exactly why no case in this file can otherwise see the shipped value. For
# ENV_FILE_OWNER that is harmless: a wrong value fails LOUDLY, because a correct root-owned .env
# would then alarm. For SECRETS_DIR_OWNER it is fail-OPEN, and that is the whole reason these lines
# exist: the same constant feeds `install -d -o` and the assertion, so changing it to the
# container's uid would put the box in precisely the #1319 posture AND make --check certify it,
# with this suite green. Compare DIR_MODE, whose production value IS pinned, because the fixture
# reproduces 0710 and the positive case would fall.
assert_ships_production_owner() {
  grep -qxF -- "readonly $1=0" "$SUT" || {
    echo "FIXTURE BROKEN: $SUT does not ship '$1=0'. Every case here rewrites that constant, so" >&2
    echo "                nothing else in this suite can see the value the box actually runs." >&2
    exit 1
  }
}
assert_ships_production_owner SECRETS_DIR_OWNER
assert_ships_production_owner ENV_FILE_OWNER

# THE OWNER SEAM (#1319, #1320), AND WHY IT MOVES THE EXPECTATION RATHER THAN THE FILESYSTEM.
# --check asserts two ABSOLUTE owners — uid 0, root, for the secrets directory and for
# deploy/.env. This suite does not run as root and cannot `chown`, so the fixture's directory and
# its .env are owned by this account instead. The rig therefore moves the other side: the SUT
# copy's two owner constants are rewritten to whatever owns the fixture. That is the same move
# jobbliggaren-reconcile.test.sh makes for the #1295 ownership cases, where the docker stub
# reports ids that do or do not match the directory this unprivileged process owns — and it makes
# BOTH arms reachable without root, on Windows and on the ubuntu runner alike.
#
# TWO VARIABLES, NOT ONE, and the isolation is the whole reason. With a single expectation a
# wrong-owner case would fire the directory arm and the .env arm together, and neither could be
# shown to work on its own — the fail-open shape this suite's %g-vs-%u case exists to refuse
# ("Only the correct pair passes"). Each case sets exactly the one it is measuring.
readonly FIXTURE_OWNER="$(id -u)"
SUT_SECRETS_DIR_OWNER="$FIXTURE_OWNER"
SUT_ENV_FILE_OWNER="$FIXTURE_OWNER"

# Set per case, never left over: a stale expectation is a case measuring its neighbour's
# condition. seed_all_secrets calls this, so the default for every case is "both correct".
reset_owner_expectations() {
  SUT_SECRETS_DIR_OWNER="$FIXTURE_OWNER"
  SUT_ENV_FILE_OWNER="$FIXTURE_OWNER"
}

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

# The two files that are required ONLY when EMAIL_PROVIDER=Scaleway. Restated here for the same
# reason as the lists above — a test deriving its expectation from the code under test cannot
# detect the code dropping an entry — and they are deliberately NOT seeded by
# seed_all_secrets(): the whole point of the cases below is that a stack without them is
# healthy until the provider is flipped.
#
# Each gets its own case below rather than one representative, and here that is more than the
# usual argument: the two rotate independently (#183), so "one placed, the other not" is a state
# an operator reaches on an ordinary rotation rather than only by mistake.
readonly -a EXPECTED_SCALEWAY_FILES=(
  "Email__Scaleway__SecretKey"
  "Email__Scaleway__ProjectId"
)

# The three .env lines the flip needs beside the files. Restated for the same reason, and kept as
# a list so a case can hold every one but the variable under test.
readonly -a EXPECTED_SCALEWAY_VARS=(
  "EMAIL_SCALEWAY_SECRET_KEY_FILE"
  "EMAIL_SCALEWAY_PROJECT_ID_FILE"
  "EMAIL_SCALEWAY_REGION"
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
  reset_owner_expectations
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
    -e "s#^readonly SECRETS_DIR_OWNER=.*#readonly SECRETS_DIR_OWNER=$SUT_SECRETS_DIR_OWNER#" \
    -e "s#^readonly ENV_FILE_OWNER=.*#readonly ENV_FILE_OWNER=$SUT_ENV_FILE_OWNER#" \
    "$SUT" > "$sut_copy"
  grep -qxF "readonly SECRETS_DIR=$SECRETS" "$sut_copy" || {
    echo "FIXTURE BROKEN: SECRETS_DIR redirect did not apply — the suite would measure the host" >&2
    exit 1
  }
  grep -qxF "readonly HOST_SECRETS_DIR=$HOST_SECRETS" "$sut_copy" || {
    echo "FIXTURE BROKEN: HOST_SECRETS_DIR redirect did not apply — the suite would measure the host" >&2
    exit 1
  }
  # Same proof as the two above, and it earns its place for a sharper reason: the mail cases are
  # the only ones whose EXPECTED result depends on a file's CONTENT rather than its presence. An
  # unapplied redirect here would point at the real /opt/jobbliggaren/deploy/.env, which on a
  # developer machine and on a CI runner alike does not exist — so email_provider would answer
  # `console`, the provider-is-Scaleway cases would measure the not-Scaleway branch, and BOTH
  # would still report the exit code they wanted. Green for the opposite reason.
  grep -qxF "readonly ENV_FILE=$ENV_FIXTURE" "$sut_copy" || {
    echo "FIXTURE BROKEN: ENV_FILE redirect did not apply — the suite would measure the host" >&2
    exit 1
  }
  # THE TWO OWNER REDIRECTS, PROVEN FOR A REASON THE OTHERS DO NOT CARRY. An unapplied redirect
  # here does not point the suite at the host — it leaves the constant at its production value 0,
  # which no fixture path is owned by. Every exit-0 case in this file would then fail with a
  # posture line, which reads as a broken SUT rather than a broken rig: the suite would be
  # reporting a defect in the thing it is measuring, caused by itself. Measured before this proof
  # existed: 5 of 66 cases went red for exactly that reason.
  grep -qxF "readonly SECRETS_DIR_OWNER=$SUT_SECRETS_DIR_OWNER" "$sut_copy" || {
    echo "FIXTURE BROKEN: SECRETS_DIR_OWNER redirect did not apply — every exit-0 case would" >&2
    echo "                report a posture fault against the rig, not against the SUT" >&2
    exit 1
  }
  grep -qxF "readonly ENV_FILE_OWNER=$SUT_ENV_FILE_OWNER" "$sut_copy" || {
    echo "FIXTURE BROKEN: ENV_FILE_OWNER redirect did not apply — every exit-0 case with an .env" >&2
    echo "                would report a posture fault against the rig, not against the SUT" >&2
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
  # 0600 IS THE PRODUCTION SHAPE AND THE FIXTURE MUST CARRY IT, for the same reason
  # seed_all_secrets chmods the directory 0710: a fixture built by the runner's umask is 0644,
  # which is a state --check now REFUSES (#1320). Leaving it would make every mail case a posture
  # case by accident, and the wrong-mode case below would then prove nothing — it would be
  # measuring the default rather than a deviation from it. Best-effort, like every other chmod
  # here: a filesystem that ignores it takes the announced skip.
  chmod 0600 "$ENV_FIXTURE" 2>/dev/null || true
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
# `systemctl --failed`. Measured on the box 2026-08-13. That entry holds heartbeat P1 red:
# one page, then a surface deaf to the next fault (host-detection.md §7, expecter
# side, 2026-08-17). Both halves are asserted: --check goes GREEN, --check-host goes RED,
# in one and the
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

echo "-- every CRASH-LOOP branch of --check reaches the BLOCKING summary, and only those (#1328)"
# ⚠ AMENDED 2026-08-16 (#183 E4): the claim was "every branch", and that stopped being true when
# --check gained the key-expiry branches. Those reach a DIFFERENT summary on purpose — an expired
# key is not a crash-loop, and the advance notice is not a fault at all. They are pinned in their
# own block below, which asserts the blocking summary ABSENT. Do not add them here: this block's
# invariant is about the branches that DO crash-loop, and widening it would erase the distinction
# the expiry work exists to draw.
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

  # Scaleway files absent, all three .env variables set — so the variable branch stays quiet and
  # the file branch is the only one firing.
  seed_all_secrets
  write_env "EMAIL_PROVIDER=Scaleway" "EMAIL_SCALEWAY_SECRET_KEY_FILE=/x" \
    "EMAIL_SCALEWAY_PROJECT_ID_FILE=/y" "EMAIL_SCALEWAY_REGION=fr-par"
  run_check || true
  assert_blocking_summary "a missing Scaleway credential file"

  # The mirror: files present, one variable unset.
  seed_all_secrets
  for k in "${EXPECTED_SCALEWAY_FILES[@]}"; do printf '%s' "seeded-value-for-$k" > "$SECRETS/$k"; done
  write_env "EMAIL_PROVIDER=Scaleway" "EMAIL_SCALEWAY_SECRET_KEY_FILE=/x" \
    "EMAIL_SCALEWAY_PROJECT_ID_FILE=/y"
  run_check || true
  assert_blocking_summary "an unset EMAIL_SCALEWAY_REGION"

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

echo "-- the key's expiry date: which branch, which summary, which exit (#183 E4)"
# THE ONE BRANCH SET IN --check THAT IS NOT A CRASH-LOOP, and the distinction is the whole point:
# an EXPIRED key exits non-zero onto the fault surface, while the advance NOTICE exits 0 and must
# NOT reach the blocking summary. Folding the notice back into `missing` — which is exactly what
# a later edit would do if nothing pinned it — leaves every other assertion in this file green
# while a healthy box latches systemctl --failed for the whole notice window, suppressing the
# transition every heartbeat predicate needs in order to notify. That is #1328's defect in a new
# branch, so it gets #1328's treatment: one isolated case each, bound to MESSAGES.
#
# EVERY FIXTURE DATE IS COMPUTED FROM `date`, never written as a literal. A hardcoded 2027-08-16
# passes today and silently becomes an EXPIRED test in August 2027 — a case that still reports
# green while measuring the opposite branch.
seed_scaleway_flip() {   # seed_scaleway_flip <expiry-line...> — provider + files + pointers set
  seed_all_secrets
  local k
  for k in "${EXPECTED_SCALEWAY_FILES[@]}"; do printf '%s' "seeded-value-for-$k" > "$SECRETS/$k"; done
  write_env "EMAIL_PROVIDER=Scaleway" "EMAIL_SCALEWAY_SECRET_KEY_FILE=/x" \
    "EMAIL_SCALEWAY_PROJECT_ID_FILE=/y" "EMAIL_SCALEWAY_REGION=fr-par" "$@"
}

# want_exit · a substring that must appear ("" = assert nothing) · whether the BLOCKING summary
# must be present · description. The blocking half is the pin; exit code alone cannot say which
# summary an operator was shown.
expect_expiry() {
  local want_exit="$1" want_line="$2" want_blocking="$3" desc="$4"
  local got=0 ok=1
  run_check || got=$?
  [ "$got" -eq "$want_exit" ] || ok=0
  if [ -n "$want_line" ] && ! grep -qF "$want_line" "$TMPROOT/out"; then ok=0; fi
  if grep -qF "$BLOCKING_SUMMARY" "$TMPROOT/out"; then
    [ "$want_blocking" = "yes" ] || ok=0
  else
    [ "$want_blocking" = "no" ] || ok=0
  fi
  if [ "$ok" -eq 1 ]; then
    pass=$((pass + 1)); echo "  ok   $desc (exit $got)"
  else
    fail=$((fail + 1)); echo "  FAIL $desc — wanted exit $want_exit, got $got" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

# MODE-GATED FOR THE SAME REASON AS THE #1328 BLOCK, and here it bites harder: where chmod is
# unavailable the WRONG MODE branch sets `missing` in EVERY case, so the exit-0 cases can never
# reach 0 and the exit-1 cases reach it through the wrong branch, carrying the blocking summary
# this block exists to assert ABSENT. Measured while writing these: all seven failed on a Windows
# filesystem with the expiry logic entirely correct. They RUN in CI (build.yml sets
# JBL_REQUIRE_MODE_CASES=1 on ubuntu, where a skip is an error).
if [ "$MODE_ENFORCED" = "yes" ]; then
seed_scaleway_flip "EMAIL_SCALEWAY_KEY_EXPIRES_AT=$(date -u -d '+5 days' +%F)"
expect_expiry 0 "NOTICE: the Scaleway API key expires" no \
  "a key inside the notice window is a journal line, not a fault"

# THE COUNTERFACTUAL, and without it the case above passes for the wrong reason: a check that
# never fires at all would also exit 0 and print no blocking summary. This one proves the notice
# window has an outside edge.
seed_scaleway_flip "EMAIL_SCALEWAY_KEY_EXPIRES_AT=$(date -u -d '+400 days' +%F)"
expect_expiry 0 "all secrets present" no \
  "a key far outside the notice window is silent"

seed_scaleway_flip "EMAIL_SCALEWAY_KEY_EXPIRES_AT=$(date -u -d '-1 day' +%F)"
expect_expiry 1 "EXPIRED: the Scaleway API key expired" no \
  "an expired key exits non-zero WITHOUT the crash-loop summary"

seed_scaleway_flip
expect_expiry 1 "MISSING: EMAIL_SCALEWAY_KEY_EXPIRES_AT" no \
  "an unset expiry date under Scaleway fails closed"

seed_scaleway_flip "EMAIL_SCALEWAY_KEY_EXPIRES_AT=not-a-date"
expect_expiry 1 "INVALID: EMAIL_SCALEWAY_KEY_EXPIRES_AT" no \
  "an unreadable expiry date is a failure, never 'no expiry'"

# THE FAIL-OPEN `date` ALONE LEAVES: relative forms parse, and they re-resolve against the clock
# every run, so the remaining days never shrink and the notice can never fire. Indistinguishable
# from a healthy key, which is the one direction this check cannot afford.
seed_scaleway_flip "EMAIL_SCALEWAY_KEY_EXPIRES_AT=nextyear"
expect_expiry 1 "INVALID: EMAIL_SCALEWAY_KEY_EXPIRES_AT" no \
  "a self-renewing relative date is rejected by the shape guard"

# PROVIDER-GATED: before the flip the branch must be inert, or every Console box on an old date
# would light up for a key it does not use.
seed_all_secrets
write_env "EMAIL_PROVIDER=Console" "EMAIL_SCALEWAY_KEY_EXPIRES_AT=$(date -u -d '-1 day' +%F)"
expect_expiry 0 "all secrets present" no \
  "an expired date is inert while the provider is Console"
else
  skipped=$((skipped + 7))
  echo "  SKIP expiry cases: this filesystem does not honour chmod, so the mode branch sets the"
  echo "       blocking counter in every case — the exit-0 cases can never reach 0, and the"
  echo "       exit-1 cases reach it through the wrong branch carrying the summary these pins"
  echo "       assert ABSENT. Seven cases. They RUN in CI."
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
# before the mail work — and that branch, which is the one that reads a file, would be the
# unproven one. Scaleway is set so the whole branch executes under the poisoned PATH.
set_env_provider "Scaleway"
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

echo "-- the Scaleway credentials are conditional on EMAIL_PROVIDER (#183)"
#
# EVERY CASE HERE IS BOUND TO THE FILE NAMES, NEVER TO THE EXIT CODE. Where chmod is
# unavailable the directory-mode branch already sets missing=1, so an exit-code assertion would
# report the wanted number without having measured this condition at all — and the two
# provider-is-Scaleway cases would be indistinguishable from the two provider-is-Console ones.

seed_all_secrets
set_env_provider ""
run_check || true
if ! grep -qE "Email__Scaleway__(SecretKey|ProjectId)|INVALID: EMAIL_PROVIDER" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   no .env at all does not demand the Scaleway credentials"
else
  fail=$((fail + 1)); echo "  FAIL an absent .env demanded the Scaleway credentials" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

seed_all_secrets
set_env_provider "Console"
run_check || true
if ! grep -qE "Email__Scaleway__(SecretKey|ProjectId)|INVALID: EMAIL_PROVIDER" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   EMAIL_PROVIDER=Console does not demand the Scaleway credentials"
else
  fail=$((fail + 1)); echo "  FAIL EMAIL_PROVIDER=Console demanded the Scaleway credentials" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

# One case per file rather than one representative, for the reason the suite already states
# about its other lists: a loop that checked only the first name would report both as covered.
# The OTHER file is seeded in each case, so a passing case measures this file alone. Here it is
# load-bearing beyond that argument: the two secrets have separate rotation lifecycles, so
# "SecretKey placed, ProjectId not yet" is a state an operator reaches on an ordinary rotation
# rather than only by mistake.
for missing_key in "${EXPECTED_SCALEWAY_FILES[@]}"; do
  seed_all_secrets
  set_env_provider "Scaleway"
  for k in "${EXPECTED_SCALEWAY_FILES[@]}"; do
    [ "$k" = "$missing_key" ] && continue
    printf '%s' "seeded-value-for-$k" > "$SECRETS/$k"
  done
  run_check || true
  if grep -qF "MISSING: $SECRETS/$missing_key" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   EMAIL_PROVIDER=Scaleway demands $missing_key"
  else
    fail=$((fail + 1)); echo "  FAIL EMAIL_PROVIDER=Scaleway did not demand $missing_key" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
done

# CROSSES THE PREDICATE'S OWN THRESHOLD. AddEmailSender compares the provider with
# OrdinalIgnoreCase, so `scaleway` IS Scaleway to the application. A name-exact guard would
# decline to demand the files while the app accepted the value, and the box would boot into
# AddEmailSender's registration-time throw with the credentials absent — precisely the state
# --check exists to catch, missed by the detector itself.
seed_all_secrets
set_env_provider "scaleway"
run_check || true
if grep -qF "MISSING: $SECRETS/Email__Scaleway__SecretKey" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   a lower-case scaleway is Scaleway, as it is to AddEmailSender"
else
  fail=$((fail + 1)); echo "  FAIL a lower-case scaleway did not demand the credentials" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

seed_all_secrets
set_env_provider "Scaleway"
for k in "${EXPECTED_SCALEWAY_FILES[@]}"; do
  printf '%s' "seeded-value-for-$k" > "$SECRETS/$k"
done
run_check || true
if ! grep -qE "MISSING: .*Email__Scaleway__" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   EMAIL_PROVIDER=Scaleway with both files present reports neither"
else
  fail=$((fail + 1)); echo "  FAIL a present Scaleway credential was still reported missing" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo "-- A RETIRED PROVIDER NAME IS A BOOT REFUSAL, NOT A QUIETER CONSOLE (#183, E1 b71c14de)"
#
# THIS IS security-auditor's MAJOR 1 FROM E1, AND IT IS THE CASE THAT CLOSES IT. While the
# SES arm existed, `EMAIL_PROVIDER=Ses` was a value the box booted on and this detector answered
# `ses` to. E1 DELETED that arm rather than repointing it: AddEmailSender's switch ends in
# `else throw`, so `Ses` now reaches the same throw as `Resend`, pinned on the application side by
# AddEmailSenderGateTests. A detector that still mapped `ses` to a bootable state — or, worse, to
# `console` — would report "all secrets present" over a stack that refuses to start, on the box's
# only alarm surface, and it would do so for exactly the operator most likely to type it: one
# following a stale instruction, a stale runbook, or this repository's own history.
#
# The mitigation E1 shipped with was "unreachable until E2 itself writes a provider value onto the
# box", and that expires in this very commit — deploy/.env.example now instructs an operator to
# write EMAIL_PROVIDER. So the false-GREEN is closed here, before anything can write one.
#
# BOUND TO THE `INVALID:` LINE AND TO THE VALUE IT NAMES, never to the exit code: --check reaches
# exit 1 by six branches, and on a chmod-less filesystem the mode branch reaches it first. Only
# this string separates "reported the retired name" from "failed for some other reason".
#
# BOTH CASES ALSO ASSERT THAT NO CREDENTIAL IS DEMANDED. A mutant that kept a `ses)` arm alive
# would print MISSING lines for the Scaleway files instead of INVALID, exit 1 all the same, and
# satisfy any assertion that only read the exit code.
for retired in "Ses" "ses" "Resend"; do
  seed_all_secrets
  set_env_provider "$retired"
  run_check || true
  if grep -qF "INVALID: EMAIL_PROVIDER='$retired'" "$TMPROOT/out" \
    && ! grep -qE "MISSING: .*Email__Scaleway__" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   EMAIL_PROVIDER=$retired is INVALID and demands no credential"
  else
    fail=$((fail + 1)); echo "  FAIL EMAIL_PROVIDER=$retired was not reported INVALID" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
done

# AND THE RETIRED POINTER NAMES ARE INERT. `EMAIL_SES_*` is not a spelling of the current
# variables: nothing reads it, so a leftover line from the SES era must neither demand a file nor
# satisfy a requirement. The fixture sets the provider to Console so the ONLY thing that could
# fire is a surviving SES pointer arm — which is what this case exists to prove is gone.
seed_all_secrets
write_env "EMAIL_PROVIDER=Console" \
  "EMAIL_SES_ACCESS_KEY_ID_FILE=/run/app-secrets/Email__Ses__AccessKeyId" \
  "EMAIL_SES_SECRET_ACCESS_KEY_FILE=/run/app-secrets/Email__Ses__SecretAccessKey" \
  "EMAIL_SES_REGION=eu-north-1"
run_check || true
if ! grep -qE "Email__Ses__|EMAIL_SES_|Email__Scaleway__|INVALID: EMAIL_PROVIDER" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   leftover EMAIL_SES_* lines are read by nothing"
else
  fail=$((fail + 1)); echo "  FAIL a retired EMAIL_SES_* line still reached a branch" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo "-- the .env forms compose accepts, all of which mean Scaleway"
#
# THE FIRST IMPLEMENTATION READ ONLY THE FIRST OF THESE and answered not-configured to the rest,
# which is the fail-OPEN direction: compose renders the value, AddEmailSender throws at
# registration, the containers crash-loop, and --check exits 0 saying "all secrets present" over a
# dead box. All three reviewing agents found it independently.
#
# WHAT COMPOSE v2.40.3 WAS MEASURED ON, 2026-08-12, IS THE FORM AND NOT THE VALUE — delimiter,
# quoting, `export` and inline-comment handling are properties of its parser and are blind to the
# provider name on the right-hand side. The literal changed with #183; the parser behaviour these
# lines are the fixture for did not, and no line below claims a 2026-08-12 measurement of the
# string `Scaleway`.
for env_line in \
  'EMAIL_PROVIDER=Scaleway' \
  'EMAIL_PROVIDER=Scaleway # flippat 2026-08-15' \
  'export EMAIL_PROVIDER=Scaleway' \
  'EMAIL_PROVIDER: Scaleway' \
  'EMAIL_PROVIDER="Scaleway" # quoted plus comment' \
  '  EMAIL_PROVIDER   =   scaleway   ' \
  ; do
  seed_all_secrets
  write_env "SITE_HOST=jobbliggaren.se" "$env_line"
  run_check || true
  if grep -qF "MISSING: $SECRETS/Email__Scaleway__SecretKey" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   [$env_line] is Scaleway"
  else
    fail=$((fail + 1)); echo "  FAIL [$env_line] was not read as Scaleway" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
done

echo "-- a quoted value KEEPS its hash, and compose treats that as a boot refusal"
# The fix for the parser introduced this one and security-auditor measured it: compose renders
# `<value> # x` for a single-quoted value, which AddEmailSender throws on, while a naive strip
# read it as the bare value and reported a box configured for mail that does not start. WIDER is
# fail-closed everywhere except here, which is why the strip now stops at the closing quote.
seed_all_secrets
write_env "SITE_HOST=jobbliggaren.se" "EMAIL_PROVIDER='Scaleway # not really'"
run_check || true
if grep -qF "INVALID: EMAIL_PROVIDER=" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   a quoted hash makes the value unknown, as it is to compose"
else
  fail=$((fail + 1)); echo "  FAIL a quoted hash was stripped and read as Scaleway" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

echo "-- an .env that exists without the key is the box's state today"
# THE MUTATION THIS EXISTS FOR: `== "scaleway"` -> `!= "console"` survived the whole suite before
# this case, and under it the box's own .env — which has no EMAIL_PROVIDER line at all — would
# take a permanent MISSING. That is verbatim the outcome the design exists to avoid, and no
# case could see it, because every fixture either had no file or had the key.
seed_all_secrets
write_env "SITE_HOST=jobbliggaren.se" "POSTGRES_APP_PASSWORD=x"
run_check || true
if ! grep -qE "Email__Scaleway__|EMAIL_SCALEWAY_|INVALID: EMAIL_PROVIDER" "$TMPROOT/out"; then
  pass=$((pass + 1)); echo "  ok   an .env with no EMAIL_PROVIDER line demands nothing"
else
  fail=$((fail + 1)); echo "  FAIL an .env with no EMAIL_PROVIDER line demanded mail config" >&2
  sed 's/^/       /' "$TMPROOT/out" >&2
fi

# (The "neither Console nor Scaleway is not a quieter Console" case is not repeated here: the
# retired-provider loop above already asserts it for `Resend`, and asserts the stronger property
# that no credential is demanded either. Two homes for one rule is two rules.)

echo "-- a _FILE pointer alone demands the credentials, whatever the provider says"
# EnvFileSecretsConfiguration throws on a pointer naming a path it cannot read and never
# consults Email:Provider, so this is a boot refusal with the provider back on Console — the
# state reached by rolling the flip BACK and rebooting, which a provider-only predicate calls
# healthy.
#
# ONE CASE PER POINTER, not one representative, and here that is more than the usual argument:
# the two secrets have separate rotation lifecycles, so a rollback can plausibly leave EITHER
# pointer behind on its own, and a predicate reading only the first would call the other healthy.
for spec in "EMAIL_SCALEWAY_SECRET_KEY_FILE:Email__Scaleway__SecretKey" \
            "EMAIL_SCALEWAY_PROJECT_ID_FILE:Email__Scaleway__ProjectId"; do
  pointer=${spec%%:*}; file=${spec#*:}
  seed_all_secrets
  write_env "EMAIL_PROVIDER=Console" "${pointer}=/run/app-secrets/${file}"
  run_check || true
  if grep -qF "MISSING: $SECRETS/$file" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   ${pointer} alone demands the files even under Console"
  else
    fail=$((fail + 1)); echo "  FAIL ${pointer} under Console demanded nothing" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
done

echo "-- injected files do not make the flip complete"
# The other half of the same boot refusal: files present, .env lines absent. One case per
# variable, because a loop checking only the first would report all three as covered.
for scw_var in "${EXPECTED_SCALEWAY_VARS[@]}"; do
  seed_all_secrets
  for k in "${EXPECTED_SCALEWAY_FILES[@]}"; do
    printf '%s' "seeded-value-for-$k" > "$SECRETS/$k"
  done
  # Everything set EXCEPT the one under test, so a pass measures that variable alone.
  lines=("EMAIL_PROVIDER=Scaleway")
  for other in "${EXPECTED_SCALEWAY_VARS[@]}"; do
    [ "$other" = "$scw_var" ] && continue
    lines+=("$other=set-for-fixture")
  done
  write_env "${lines[@]}"
  run_check || true
  if grep -qF "MISSING: ${scw_var} is unset" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   EMAIL_PROVIDER=Scaleway demands ${scw_var}"
  else
    fail=$((fail + 1)); echo "  FAIL a missing ${scw_var} was not reported" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
done

echo "-- the at-rest posture: the secrets directory's OWNER (#1319)"
# WHAT THIS SECTION MEASURES AND WHY IT CAN. --check now asserts that root owns the secrets
# directory. The suite is not root, so it moves the EXPECTATION (SUT_SECRETS_DIR_OWNER) rather
# than the filesystem — see the seam's note at the top. Both arms are therefore reachable
# unprivileged.
#
# THE PRODUCTION TRIPLE ITSELF (0710 root:<container-gid>) IS NOT MEASURED HERE and cannot be;
# its proof is the cutover row in vps-deploy-stack.md. What is measured is the PREDICATE.

# A uid that is certainly not the fixture's owner, derived from it rather than hard-coded: a
# literal would collide the day this suite runs as that uid, and the case would silently invert.
readonly NOT_FIXTURE_OWNER=$((FIXTURE_OWNER + 1))

# The posture summary, by a substring unique to it. The NEGATIVE half of each assertion below is
# what makes it a pin rather than a coincidence: a posture fault wrongly routed into `missing`
# still prints its own WRONG OWNER line and still exits 1, so only the absence of the crash-loop
# summary tells the two apart — and telling them apart is the whole reason the third flag exists.
readonly POSTURE_SUMMARY="Do not read a serving stack as"

assert_posture_summary() {
  local desc="$1"
  if grep -qF "$POSTURE_SUMMARY" "$TMPROOT/out" && ! grep -qF "$BLOCKING_SUMMARY" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   $desc summarises as posture, not as a crash-loop"
  else
    fail=$((fail + 1)); echo "  FAIL $desc did not summarise as posture" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

assert_output_has() {
  if grep -qF -- "$1" "$TMPROOT/out"; then
    pass=$((pass + 1)); echo "  ok   $2"
  else
    fail=$((fail + 1)); echo "  FAIL $2 — the output did not name it:" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

assert_output_lacks() {
  if grep -qF -- "$1" "$TMPROOT/out"; then
    fail=$((fail + 1)); echo "  FAIL $2 — the output named it and should not have:" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  else
    pass=$((pass + 1)); echo "  ok   $2"
  fi
}

# EVERY CASE BELOW NEEDS A CLEAN DIRECTORY POSTURE, not only a chmod-able .env — which is why
# this gate sits at the section and not at the mode cases. Where chmod is ignored the fixture
# directory is 0755, --check's own mode arm fires, `missing` is set, and the blocking summary
# prints beside every posture line. That defeats the negative half of assert_posture_summary and
# both independence assertions at once. Measured on Git Bash before the gate moved out: 4 of these
# went red for the platform rather than for the SUT, which is a rig reporting a defect it caused.
if [ "$MODE_ENFORCED" = "yes" ]; then
  seed_all_secrets
  write_env "SITE_HOST=jobbliggaren.se"
  SUT_SECRETS_DIR_OWNER="$NOT_FIXTURE_OWNER"
  expect_check 1 "a secrets directory owned by anyone but root refuses"
  assert_output_has "WRONG OWNER: $SECRETS" "and the line names the directory"
  assert_posture_summary "the directory-owner fault"
  # THE MODE IS CORRECT IN THIS CASE, deliberately. Without that the arm could be piggybacking on
  # the mode branch — both set a flag from the same `if`-chain — and a predicate that only ever
  # fires alongside another is not shown to work.
  assert_output_lacks "WRONG MODE: $SECRETS" "and it is the OWNER arm firing, not the mode arm"
  # INDEPENDENCE, the property this suite's %g-vs-%u case establishes for the other gate: a wrong
  # directory owner must not drag deploy/.env into the report. Without it, one over-broad predicate
  # would satisfy both sections here and neither would have measured its own subject.
  assert_output_lacks "$ENV_FIXTURE" "and it says nothing about deploy/.env"

  # THE REPAIR IS THE DIRECTORY ALONE. `chown -R` from the directory is the defect that produced
  # this state in the first place (#1319: three published repair commands took the operand too),
  # so a repair published here that recursed would re-create it. And a glob cannot be run by the
  # operator it addresses — 0710 denies the read to every non-root user and the shell expands
  # before sudo elevates — which is the same measurement vps-deploy-stack.md row 32b's drill took.
  if grep -qF -- "chown -R" "$TMPROOT/out"; then
    fail=$((fail + 1)); echo "  FAIL the owner repair published a recursive chown" >&2
  else
    pass=$((pass + 1)); echo "  ok   the owner repair is not recursive"
  fi
  if grep -qE -- '(chown|chmod|stat)[^|]*/\*' "$TMPROOT/out"; then
    fail=$((fail + 1)); echo "  FAIL the owner repair published a shell glob the operator cannot expand" >&2
  else
    pass=$((pass + 1)); echo "  ok   the owner repair publishes no glob"
  fi

  echo "-- the at-rest posture: deploy/.env's owner and mode (#1320)"
  # Every permanent plaintext credential the stack has rests on this file's posture, and until
  # #1320 it was prescribed in four places and read by none of them.

  seed_all_secrets
  write_env "SITE_HOST=jobbliggaren.se"
  SUT_ENV_FILE_OWNER="$NOT_FIXTURE_OWNER"
  expect_check 1 "an .env owned by anyone but root refuses"
  assert_output_has "WRONG OWNER: $ENV_FIXTURE" "and the line names the file"
  assert_posture_summary "the .env-owner fault"
  # The mirror of the independence assertion above, and it must be here too: the two arms read two
  # different paths, and either one alone reporting both would go unnoticed with only one direction
  # measured.
  assert_output_lacks "WRONG OWNER: $SECRETS" "and it says nothing about the secrets directory"

  # 0644 — the mode a fresh file gets under the default umask, which is exactly how this drifts
  # in the field: an operator recreates the file and never runs the chmod the four prescriptions
  # ask for.
  seed_all_secrets
  write_env "SITE_HOST=jobbliggaren.se"
  chmod 0644 "$ENV_FIXTURE"
  expect_check 1 "a world-readable .env refuses"
  assert_output_has "WRONG MODE: $ENV_FIXTURE" "and the line names the file and its mode"
  assert_posture_summary "the .env-mode fault"

  # 0640 — the GROUP bit alone. A mask that only covered `other` would call this healthy, and a
  # group-readable credentials file is the shape a "let the deploy group read it" convenience
  # takes. This case is what makes the mask 0077 rather than 0007.
  seed_all_secrets
  write_env "SITE_HOST=jobbliggaren.se"
  chmod 0640 "$ENV_FIXTURE"
  expect_check 1 "a group-readable .env refuses too"
  assert_output_has "WRONG MODE: $ENV_FIXTURE" "and the group bit alone is enough to refuse"

  # 0400 PASSES, AND THIS CASE IS THE MASK'S OTHER HALF. The assertion is "no non-root reader",
  # not "the mode is 600" — jobbliggaren-reconcile.sh wrote that precedent for the files' 0400.
  # An `== 600` implementation passes every case above and fails this one; without it the
  # difference between a property and an opinion about permissions is unmeasured.
  seed_all_secrets
  write_env "SITE_HOST=jobbliggaren.se"
  chmod 0400 "$ENV_FIXTURE"
  expect_check 0 "a stricter-than-prescribed .env (0400) is a pass, not a deviation"

  seed_all_secrets
  write_env "SITE_HOST=jobbliggaren.se"
  chmod 0600 "$ENV_FIXTURE"
  expect_check 0 "the prescribed 0600 with a root-owned directory is a clean posture"

  # A SYMLINKED .env IS MEASURED AT ITS TARGET, and this pair is what fails without the `-L`. A
  # symlink's own mode is 0777 on Linux and means nothing; `chmod` follows the link while a bare
  # `stat` does not, so a link-measuring arm refuses, publishes a chmod, and that chmod changes a
  # target which was already correct — an alarm nobody can clear, which rows 30/32b of
  # vps-deploy-stack.md call worse than no gate at all.
  seed_all_secrets
  write_env "SITE_HOST=jobbliggaren.se"
  mv "$ENV_FIXTURE" "$TMPROOT/env-target"
  chmod 0600 "$TMPROOT/env-target"
  if ln -s "$TMPROOT/env-target" "$ENV_FIXTURE" 2>/dev/null && [ -L "$ENV_FIXTURE" ]; then
    expect_check 0 "a symlinked .env whose TARGET is 0600 is a clean posture, not a refusal"

    # The other direction, so the case above cannot be passing merely because the arm went quiet:
    # the same link over a world-readable target must still refuse.
    chmod 0644 "$TMPROOT/env-target"
    expect_check 1 "and the same link over a 0644 target still refuses"
    assert_output_has "WRONG MODE: $ENV_FIXTURE" "naming the path the operator will chmod"
  else
    skipped=$((skipped + 3))
    echo "  SKIP the symlinked-.env cases: this filesystem does not create symlinks (Windows"
    echo "       without developer mode). They RUN in CI on ubuntu."
  fi
  rm -f "$ENV_FIXTURE" "$TMPROOT/env-target"
else
  skipped=$((skipped + 21))
  echo "  SKIP the whole at-rest posture section: this filesystem does not honour chmod"
  echo "       (Git Bash/Windows), so the directory cannot be put in the 0710 posture these"
  echo "       cases measure against. They RUN in CI on ubuntu, where JBL_REQUIRE_MODE_CASES"
  echo "       makes a skip an error."
fi

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
