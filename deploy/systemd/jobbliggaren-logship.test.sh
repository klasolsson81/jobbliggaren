#!/usr/bin/env bash
#
# Fixture tests for jobbliggaren-logship.sh — #1175 (A), the off-box log archive.
#
# Run:  bash deploy/systemd/jobbliggaren-logship.test.sh
#
# WHY THIS EXISTS. The unit needs root, a real journal, an age recipient and a real object store,
# and that is a real reason not to test the orchestration. It is not a reason to leave the
# decisions untested, and three of them carry the whole blast radius:
#
#   1. THE CURSOR IS ONLY ADVANCED IF THE SHIP SUCCEEDED. This is the correctness spine of the
#      whole design. `journalctl --cursor-file` writes the cursor as a SIDE EFFECT OF READING, so
#      the naive shape — point it straight at the real cursor file — silently loses a window on
#      every failed upload, with no error anywhere and nothing on the alarm surface. The script
#      reads against a COPY and promotes it only after the ship returns 0. T6 makes rclone fail
#      and asserts the real cursor is byte-identical afterwards; T7 is its counterfactual.
#      Without both, T6 would pass against a script that never wrote a cursor at all.
#   2. AUDIT ROTATION. There is no cursor for auditd, so the script tracks (inode, offset). If
#      rotation is missed, the window between the last offset and the rotation is never shipped —
#      a hole exactly where an attacker's traces would be. T8/T9 drive both arms.
#   3. THE CREDENTIAL AND THE PRIVATE-KEY REFUSAL. The decoded rclone config is a credential; it
#      must never reach the journal. And `age.recipient` is a TRACKED file — a private key landing
#      there would be committed, so the runtime arm refuses it (T5).
#
# journalctl, age and rclone are stubbed on a from-scratch PATH: no root, no daemon, no network.
# The script's absolute paths are rewritten into the fixture tree, exactly as
# jobbliggaren-heartbeat.test.sh and jobbliggaren-reconcile.test.sh do; the logic is untouched.

set -uo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/jobbliggaren-logship.sh"
[ -f "$SUT" ] || { echo "missing script under test: $SUT" >&2; exit 1; }

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT
readonly BIN="$TMPROOT/bin"
mkdir -p "$BIN"

pass=0
fail=0
# Real tools this run had to substitute. Reported in the summary: a green run that stubbed a real
# dependency has measured less than a green run that did not, and the difference must be visible
# rather than inferred from the platform.
STUBBED_REAL_TOOLS=""

# A credential distinctive enough that its appearance anywhere in the script's output is
# unmistakable. T10 greps for this exact token.
readonly TEST_SECRET_TOKEN="RCLONE-SECRET-CANARY-7b21"
readonly TEST_RECIPIENT="age1canaryrecipient000000000000000000000000000000000000000000000"

readonly FIXTURE_SUT="$TMPROOT/logship.sh"

prepare_sut() {
  # Rewrite every absolute path into the fixture tree. The mktemp line is rewritten too: the SUT
  # materialises the decoded credential on /dev/shm deliberately (tmpfs, never persistent disk),
  # and that property is asserted separately in T11 by grepping the SOURCE — rewriting it here
  # would make T11 vacuous if it read the fixture instead.
  sed -e "s#^readonly HOST_SECRETS_DIR=.*#readonly HOST_SECRETS_DIR=$TMPROOT/host-secrets#" \
      -e "s#^readonly CREDENTIAL_FILE=.*#readonly CREDENTIAL_FILE=$TMPROOT/host-secrets/Backup__RcloneConfigBase64#" \
      -e "s#^readonly RECIPIENT_FILE=.*#readonly RECIPIENT_FILE=$TMPROOT/age.recipient#" \
      -e "s#^readonly STATE_DIR=.*#readonly STATE_DIR=$TMPROOT/state#" \
      -e "s#^readonly STAMP_FILE=.*#readonly STAMP_FILE=\"$TMPROOT/state/last-successful-logship\"#" \
      -e "s#^readonly JOURNAL_CURSOR_FILE=.*#readonly JOURNAL_CURSOR_FILE=\"$TMPROOT/state/logship.journal-cursor\"#" \
      -e "s#^readonly AUDIT_STATE_FILE=.*#readonly AUDIT_STATE_FILE=\"$TMPROOT/state/logship.audit-offset\"#" \
      -e "s#^readonly LOCK_FILE=.*#readonly LOCK_FILE=$TMPROOT/logship.lock#" \
      -e "s#^readonly AUDIT_LOG=.*#readonly AUDIT_LOG=$TMPROOT/audit/audit.log#" \
      -e "s#mktemp -d /dev/shm/logship.XXXXXX#mktemp -d $TMPROOT/work.XXXXXX#" \
      "$SUT" >"$FIXTURE_SUT"
  chmod +x "$FIXTURE_SUT"
}

reset_fixture() {
  rm -rf "$TMPROOT/state" "$TMPROOT/host-secrets" "$TMPROOT/audit" "$TMPROOT/remote" \
         "$TMPROOT/containers" "$TMPROOT/age.recipient" "$TMPROOT"/work.* \
         "$TMPROOT/logship.lock" 2>/dev/null
  mkdir -p "$TMPROOT/state" "$TMPROOT/host-secrets" "$TMPROOT/audit" "$TMPROOT/remote" \
           "$TMPROOT/containers"
  printf '%s\n' "$TEST_RECIPIENT" >"$TMPROOT/age.recipient"
  # The credential is base64 of an rclone config, exactly as the real injection produces.
  printf '[jbl-backup]\ntype = s3\nsecret_access_key = %s\n' "$TEST_SECRET_TOKEN" \
    | base64 -w0 >"$TMPROOT/host-secrets/Backup__RcloneConfigBase64" 2>/dev/null \
    || printf '[jbl-backup]\ntype = s3\nsecret_access_key = %s\n' "$TEST_SECRET_TOKEN" \
       | base64 >"$TMPROOT/host-secrets/Backup__RcloneConfigBase64"
  : >"$TMPROOT/journal-entries"
  : >"$TMPROOT/rclone-fail"
  : >"$TMPROOT/journal-fail"
  : >"$TMPROOT/docker-fail"
  : >"$TMPROOT/docker-argv"
}

# --- stubs -------------------------------------------------------------------------------------

# rclone rcat writes the object into the fixture "remote" so a test can assert what arrived, and
# fails when the fixture flag file is non-empty so T6 can drive the failure arm.
#
# ITS OWN FUNCTION so T10b can swap in a LEAKING rclone and put this one back afterwards. Calling
# write_stubs to restore would work too, but it would append "flock" to STUBBED_REAL_TOOLS a
# second time and make the run's own honesty note wrong.
write_rclone_stub() {
  cat >"$BIN/rclone" <<STUB
#!/usr/bin/env bash
if [ -s "$TMPROOT/rclone-fail" ]; then
  echo "rclone: simulated upload failure" >&2
  cat >/dev/null
  exit 1
fi
object=""
for a in "\$@"; do object="\$a"; done
mkdir -p "$TMPROOT/remote"
cat >"$TMPROOT/remote/\$(basename "\$object")"
exit 0
STUB
  chmod +x "$BIN/rclone"
}

write_stubs() {
  # journalctl honours --cursor-file the way the real one does: it reads the cursor, emits only
  # what the fixture says is new, and WRITES THE NEW CURSOR AS A SIDE EFFECT OF READING. That
  # side effect is the entire reason T6 exists, so a stub that skipped it would make the suite
  # unable to observe the defect it is written to catch.
  cat >"$BIN/journalctl" <<STUB
#!/usr/bin/env bash
cursor_file=""
for a in "\$@"; do
  case "\$a" in --cursor-file=*) cursor_file="\${a#--cursor-file=}" ;; esac
done
if [ -s "$TMPROOT/journal-fail" ]; then
  echo "journalctl: simulated read failure" >&2
  exit 1
fi
cat "$TMPROOT/journal-entries"
[ -n "\$cursor_file" ] && printf 's=cursor-after-%s\n' "\$(date -u +%s%N)" >"\$cursor_file"
exit 0
STUB

  cat >"$BIN/age" <<'STUB'
#!/usr/bin/env bash
# Pass-through with a marker, so the artefact reaching rclone is provably post-age.
printf 'AGE-ENVELOPE-BEGIN\n'
cat
exit 0
STUB

  write_rclone_stub

  # docker: `inspect` decides whether a container is considered present, `logs` emits whatever the
  # fixture put in the per-container file. Both read fixture files so a case sets state without
  # rewriting the stub.
  cat >"$BIN/docker" <<STUB
#!/usr/bin/env bash
# AN UNREACHABLE DAEMON FAILS EVERY SUBCOMMAND, inspect INCLUDED — and it fails inspect in exactly
# the shape a missing container does. That indistinguishability is the defect T22 pins. Same env
# var and same message as jobbliggaren-logprune.test.sh's arm, because it is the same daemon.
if [ -n "\${DOCKER_STUB_DAEMON_DOWN:-}" ]; then
  echo "Cannot connect to the Docker daemon at unix:///var/run/docker.sock." >&2
  exit 1
fi
verb="\$1"; shift
case "\$verb" in
  version)
    # ANSWERED EXPLICITLY rather than left to the catch-all exit 0 at the bottom. The daemon probe
    # passes either way, so this changes no verdict — it stops the arm T22 turns on from resting
    # on a fallthrough that a later edit could remove without meaning to.
    echo "28.5.1" ;;
  inspect)
    [ -f "$TMPROOT/containers/\$1" ] && exit 0 || exit 1 ;;
  logs)
    # THE ARGV IS THE MEASUREMENT. Without recording it the suite cannot see --since at all, and
    # T20's floor would be asserted against a stub that discards exactly the argument under test.
    printf '%s\n' "\$*" >>"$TMPROOT/docker-argv"
    name=""
    for a in "\$@"; do name="\$a"; done
    [ -f "$TMPROOT/containers/\$name" ] && cat "$TMPROOT/containers/\$name"
    # A READ FAILURE, driven by a fixture file the way rclone-fail and journal-fail are. The real
    # docker writes its diagnosis to stderr, which the script folds into the extract with 2>&1 —
    # so the fixture emits one too, because a stub that failed SILENTLY would not reproduce the
    # shape that makes this failure ship as if it were content.
    if [ -s "$TMPROOT/docker-fail" ]; then
      echo "Error response from daemon: simulated" >&2
      exit 1
    fi
    exit 0 ;;
esac
exit 0
STUB

  chmod +x "$BIN/journalctl" "$BIN/age" "$BIN/rclone" "$BIN/docker"

  # `flock` IS SUBSTITUTED ONLY WHERE THE REAL ONE IS ABSENT, AND THE SUITE SAYS SO. On CI (and
  # any Linux) the real flock is used and the lock is genuinely exercised; on a workstation
  # without it — Git Bash on Windows has none, measured 2026-08-11 — a permissive stub stands in.
  #
  # This is written as a conditional rather than an unconditional stub because of what happened
  # when it was not there: the script died at its own tool check, and T6 ("a failed ship leaves
  # the cursor untouched") PASSED VACUOUSLY, because nothing ran at all. A suite that substitutes
  # silently reports an unmeasured run as a pass. T16 pins the lock at the source so the stub can
  # never hide its removal.
  if ! command -v flock >/dev/null 2>&1; then
    printf '#!/usr/bin/env bash\nexit 0\n' >"$BIN/flock"
    chmod +x "$BIN/flock"
    STUBBED_REAL_TOOLS="${STUBBED_REAL_TOOLS}${STUBBED_REAL_TOOLS:+, }flock"
  fi
}

run_sut() {
  # A from-scratch PATH: the stubs first, then the real coreutils the script legitimately uses.
  PATH="$BIN:/usr/bin:/bin" "$FIXTURE_SUT" "$@" 2>&1
}

check() {
  local name="$1" condition="$2"
  if [ "$condition" = "0" ]; then
    pass=$((pass + 1)); printf '  PASS  %s\n' "$name"
  else
    fail=$((fail + 1)); printf '  FAIL  %s\n' "$name"
  fi
}

prepare_sut
write_stubs

echo "jobbliggaren-logship.test.sh"
echo

# --- T1..T4: the --check freshness probe --------------------------------------------------------
reset_fixture
out=$(run_sut --check); rc=$?
check "T1  --check with no stamp fails and says so" \
  "$([ "$rc" -ne 0 ] && echo "$out" | grep -q 'never succeeded' && echo 0 || echo 1)"

reset_fixture
printf 'x\n' >"$TMPROOT/state/last-successful-logship"
out=$(run_sut --check); rc=$?
check "T2  --check with a fresh stamp passes" "$([ "$rc" -eq 0 ] && echo 0 || echo 1)"

reset_fixture
printf 'x\n' >"$TMPROOT/state/last-successful-logship"
touch -d '4 hours ago' "$TMPROOT/state/last-successful-logship" 2>/dev/null \
  || touch -t "$(date -d '4 hours ago' +%Y%m%d%H%M 2>/dev/null)" "$TMPROOT/state/last-successful-logship"
out=$(run_sut --check); rc=$?
check "T3  --check with a stale stamp fails" \
  "$([ "$rc" -ne 0 ] && echo "$out" | grep -q 'over the' && echo 0 || echo 1)"

# CROSSES THE CONTROL: a naive `age > MAX` comparison passes a future stamp, because a negative
# age is not greater than the threshold. Only the explicit negative arm catches it, and a broken
# clock or a tampered stamp is exactly how a stopped archive would hide.
reset_fixture
printf 'x\n' >"$TMPROOT/state/last-successful-logship"
touch -d '2 hours' "$TMPROOT/state/last-successful-logship" 2>/dev/null \
  || touch -t "$(date -d '+2 hours' +%Y%m%d%H%M 2>/dev/null)" "$TMPROOT/state/last-successful-logship"
out=$(run_sut --check); rc=$?
check "T4  --check refuses a stamp dated in the FUTURE" \
  "$([ "$rc" -ne 0 ] && echo "$out" | grep -q 'future' && echo 0 || echo 1)"

# --- T5: the private-key refusal ----------------------------------------------------------------
reset_fixture
printf 'AGE-SECRET-KEY-1CANARYPRIVATEKEY0000000000000000000000000000000\n' >"$TMPROOT/age.recipient"
out=$(run_sut); rc=$?
check "T5  a PRIVATE key in the recipient file is refused" \
  "$([ "$rc" -ne 0 ] && echo "$out" | grep -q 'PRIVATE key' && echo 0 || echo 1)"

# --- T6/T7: the correctness spine ---------------------------------------------------------------
reset_fixture
printf 'entry-one\n' >"$TMPROOT/journal-entries"
printf 's=cursor-ORIGINAL\n' >"$TMPROOT/state/logship.journal-cursor"
printf 'x\n' >"$TMPROOT/rclone-fail"
out=$(run_sut); rc=$?
after=$(cat "$TMPROOT/state/logship.journal-cursor")
check "T6  a FAILED ship leaves the journal cursor untouched" \
  "$([ "$rc" -ne 0 ] && [ "$after" = "s=cursor-ORIGINAL" ] && echo 0 || echo 1)"

# T6's counterfactual. Without it T6 would also pass against a script that never advanced the
# cursor at all — an inert guarantee, which is the failure class this repo keeps measuring.
reset_fixture
printf 'entry-one\n' >"$TMPROOT/journal-entries"
printf 's=cursor-ORIGINAL\n' >"$TMPROOT/state/logship.journal-cursor"
out=$(run_sut); rc=$?
after=$(cat "$TMPROOT/state/logship.journal-cursor")
check "T7  a SUCCESSFUL ship DOES advance the journal cursor" \
  "$([ "$rc" -eq 0 ] && [ "$after" != "s=cursor-ORIGINAL" ] && echo 0 || echo 1)"

# --- T8/T9: audit rotation ----------------------------------------------------------------------
# Rotation is signalled by a changed inode. The state names an inode that cannot be the current
# one, so the script must ship from offset 0 rather than from the stale offset.
reset_fixture
printf 'audit-line-1\naudit-line-2\n' >"$TMPROOT/audit/audit.log"
printf '999999999 5\n' >"$TMPROOT/state/logship.audit-offset"
printf 'rotated-tail\n' >"$TMPROOT/audit/audit.log.1"
out=$(run_sut); rc=$?
check "T8  a changed inode is read as rotation and ships from offset 0" \
  "$([ "$rc" -eq 0 ] && echo "$out" | grep -q 'rotation or first run' && echo 0 || echo 1)"

check "T8b the just-rotated audit.log.1 is shipped too, not dropped" \
  "$(ls "$TMPROOT/remote" 2>/dev/null | grep -q 'rotated' && echo 0 || echo 1)"

# No new bytes: the script must not ship an empty artefact, but must still record state.
reset_fixture
printf 'audit-line-1\n' >"$TMPROOT/audit/audit.log"
inode=$(stat -c '%i' "$TMPROOT/audit/audit.log")
size=$(stat -c '%s' "$TMPROOT/audit/audit.log")
printf '%s %s\n' "$inode" "$size" >"$TMPROOT/state/logship.audit-offset"
out=$(run_sut); rc=$?
check "T9  no new audit bytes ships no audit artefact" \
  "$([ "$rc" -eq 0 ] && echo "$out" | grep -q 'no new bytes' \
     && ! ls "$TMPROOT/remote" 2>/dev/null | grep -q '^audit-' && echo 0 || echo 1)"

# --- T10: the credential must not reach the journal ---------------------------------------------
reset_fixture
printf 'entry-one\n' >"$TMPROOT/journal-entries"
out=$(run_sut); rc=$?
# The `rc -eq 0` half is not decoration: without it every EARLY death passes this test, because a
# script that never decoded the credential trivially never printed it. That is the same vacuous
# shape T6 was rescued from, one file over — and it was found by an auditor, not by this suite.
check "T10 the run succeeds AND the decoded credential never appears in its output" \
  "$([ "$rc" -eq 0 ] && ! echo "$out" | grep -q "$TEST_SECRET_TOKEN" && echo 0 || echo 1)"

# --- T10b: CROSSES THE CONTROL ------------------------------------------------------------------
# T10 is an absence assertion, and an absence assertion that has never been shown to fail cannot
# be told from a broken grep. This drives the leak through the one channel the credential really
# reaches: ship() pipes into `rclone rcat --config <decoded credential>`, and rclone's stdout is
# the script's stdout, so an rclone that dumps its config file — what `--log-level DEBUG` or a
# careless diagnostic would do — puts the secret in exactly the place T10 looks. The assertion is
# that T10's predicate then FAILS.
reset_fixture
printf 'entry-one\n' >"$TMPROOT/journal-entries"
cat >"$BIN/rclone" <<STUB
#!/usr/bin/env bash
config=""; prev=""
for a in "\$@"; do [ "\$prev" = "--config" ] && config="\$a"; prev="\$a"; done
[ -n "\$config" ] && [ -r "\$config" ] && cat "\$config"
object=""
for a in "\$@"; do object="\$a"; done
mkdir -p "$TMPROOT/remote"
cat >"$TMPROOT/remote/\$(basename "\$object")"
exit 0
STUB
chmod +x "$BIN/rclone"
out=$(run_sut); rc=$?
check "T10b a LEAKING rclone makes T10's predicate fail (the rig can see a leak)" \
  "$([ "$rc" -eq 0 ] && echo "$out" | grep -q "$TEST_SECRET_TOKEN" && echo 0 || echo 1)"
write_rclone_stub

# --- T11: no plaintext credential on persistent disk --------------------------------------------
# Asserted against the SOURCE, not the rewritten fixture — the fixture deliberately relocates the
# working directory so the suite can run anywhere, which would make a fixture-side assertion
# vacuous. This is the same reason the heartbeat suite pins its own paths at the source.
check "T11 the working directory is on tmpfs (/dev/shm) in the SOURCE" \
  "$(grep -q 'mktemp -d /dev/shm/logship' "$SUT" && echo 0 || echo 1)"

# --- T12: the shared literals still match #197's -------------------------------------------------
# The KEEP-IN-SYNC note in both files is prose; this is the instrument. If jobbliggaren-backup.sh
# ever moves the credential path or the remote name, these two files diverge silently and this
# archive stops shipping on a box where the backup still works.
backup_sut="$script_dir/jobbliggaren-backup.sh"
if [ -f "$backup_sut" ]; then
  for literal in HOST_SECRETS_DIR CREDENTIAL_FILE RECIPIENT_FILE; do
    a=$(grep -E "^readonly ${literal}=" "$SUT" | head -1 | sed 's/.*=//')
    b=$(grep -E "^readonly ${literal}=" "$backup_sut" | head -1 | sed 's/.*=//')
    check "T12 ${literal} matches jobbliggaren-backup.sh" \
      "$([ -n "$a" ] && [ "$a" = "$b" ] && echo 0 || echo 1)"
  done
  a=$(grep -E "^readonly LOGSHIP_REMOTE=" "$SUT" | head -1 | sed 's/.*=//')
  b=$(grep -E "^readonly BACKUP_REMOTE=" "$backup_sut" | head -1 | sed 's/.*=//')
  check "T12 the remote matches jobbliggaren-backup.sh's" \
    "$([ -n "$a" ] && [ "$a" = "$b" ] && echo 0 || echo 1)"
elif [ -n "${JBL_REQUIRE_SIBLING_SCRIPTS:-}" ]; then
  # An announced skip is not a measurement. T12 is the ONLY check binding this file to #197's,
  # and the moment it matters is exactly the moment the sibling is renamed or moved — when a
  # skip would go quiet instead of red. CI sets this; a workstation run may legitimately skip.
  check "T12 jobbliggaren-backup.sh is present (JBL_REQUIRE_SIBLING_SCRIPTS)" 1
else
  echo "  SKIP  T12 jobbliggaren-backup.sh not found"
fi

# --- T13/T14: the app leg ------------------------------------------------------------------------
reset_fixture
printf 'api line one\n' >"$TMPROOT/containers/jobbliggaren-api"
out=$(run_sut); rc=$?
check "T13 container output is shipped as its own artefact" \
  "$([ "$rc" -eq 0 ] && ls "$TMPROOT/remote" 2>/dev/null | grep -q '^app-' && echo 0 || echo 1)"

# CROSSES THE CONTROL: the extract is never empty — the script writes a `===== name =====` header
# per running container before reading any logs. A naive `[ -s "$file" ]` therefore ships a
# header-only artefact every single hour, forever. Only the `grep -qv '^===== '` arm sees it.
reset_fixture
: >"$TMPROOT/containers/jobbliggaren-api"
out=$(run_sut); rc=$?
check "T14 a header-only app extract is NOT shipped" \
  "$([ "$rc" -eq 0 ] && echo "$out" | grep -q 'no container output' \
     && ! ls "$TMPROOT/remote" 2>/dev/null | grep -q '^app-' && echo 0 || echo 1)"

# --- T15: the app leg is permanent, and no sunset rule may creep back -----------------------------
# The original bind carried "remove the app leg when (B) lands". That was withdrawn on 2026-08-11
# once (B) was bound to the production box: a box-local Seq is not a second OFF-BOX store, so
# removing this leg would delete the only off-box copy rather than de-duplicate it. The decision
# lives in prose, so this is the instrument that stops a future edit from restoring it — and it
# asserts BOTH halves, because a file that merely lacks the word "sunset" proves nothing.
check "T15 the app leg is declared permanent" \
  "$(grep -q 'THIS LEG IS PERMANENT' "$SUT" && echo 0 || echo 1)"
# T15b was first written as an ABSENCE test — grep for a sunset directive, fail if present. It
# fired on the file's own HISTORICAL sentence describing the withdrawn rule, and no narrowing
# fixes that: a pattern that matches the directive is a substring of the prose explaining why the
# directive was withdrawn, so it cannot discriminate the two. Absence over prose is the wrong
# instrument. What is asserted instead is that the withdrawal and its REASON are both recorded —
# an edit that restores the sunset rule has to delete these to be coherent, and one that leaves
# them in place while restoring it is self-contradicting on the same screen.
check "T15b the withdrawal is recorded with its reason (box-local Seq is not a second off-box store)" \
  "$(grep -q 'not a second OFF-BOX store' "$SUT" && echo 0 || echo 1)"
check "T15c the file instructs against restoring the unconditional form" \
  "$(grep -q 'Do not restore the unconditional form' "$SUT" && echo 0 || echo 1)"

# --- T16: the lock exists at the source -----------------------------------------------------------
# The flock stub above is permissive where the real tool is absent, so a run on such a host cannot
# observe the lock. This pin is what stops that substitution from hiding the lock's removal: it
# reads the SOURCE, not the fixture, and it is the reason the stub is allowed to be permissive.
check "T16 the script takes an exclusive lock spanning the whole run" \
  "$(grep -q 'flock -n 9' "$SUT" && echo 0 || echo 1)"

# --- T17: a FAILED journal read is not an empty window ------------------------------------------
# Both leave the extract empty, and the empty-window branch promotes the cursor and writes the
# stamp. If they converge, a permanently broken journal leg is indistinguishable from a quiet box
# on every surface this mechanism feeds: the stamp is fresh, --check is green, systemctl --failed
# is empty and the dead-man stays silent. All three assertions are needed — the exit code alone
# would also pass against a script that died for some earlier reason.
reset_fixture
printf 'entry-one
' >"$TMPROOT/journal-entries"
printf 's=cursor-ORIGINAL
' >"$TMPROOT/state/logship.journal-cursor"
printf 'x
' >"$TMPROOT/journal-fail"
out=$(run_sut); rc=$?
after=$(cat "$TMPROOT/state/logship.journal-cursor")
check "T17 a failed journal read dies, keeps the cursor and writes no stamp"   "$([ "$rc" -ne 0 ] && [ "$after" = "s=cursor-ORIGINAL" ]      && [ ! -f "$TMPROOT/state/last-successful-logship" ] && echo 0 || echo 1)"

check "T17b the failure names journalctl rather than reporting an empty window"   "$(echo "$out" | grep -q 'journalctl exited' && echo 0 || echo 1)"

# --- T19: a FAILED docker read must not be anchored past ----------------------------------------
# The app leg has no cursor; its window is anchored on the stamp. So a failed read that still
# writes the stamp loses the window between the previous stamp and this run, permanently and
# silently — and it is louder than the journal case rather than quieter, because `2>&1` folds
# docker's error text into the extract, where it clears the header check and SHIPS. The artefact
# then looks like content. All three assertions are needed: the run must fail, the stamp must not
# exist, and what was collected must still have shipped.
reset_fixture
printf 'api line one\n' >"$TMPROOT/containers/jobbliggaren-api"
printf 'x\n' >"$TMPROOT/docker-fail"
out=$(run_sut); rc=$?
check "T19 a failed docker read fails the run and writes NO stamp" \
  "$([ "$rc" -ne 0 ] && [ ! -f "$TMPROOT/state/last-successful-logship" ] && echo 0 || echo 1)"
check "T19b the failure names docker rather than passing as an empty window" \
  "$(echo "$out" | grep -q 'docker logs exited' && echo 0 || echo 1)"
# CROSSES THE CONTROL: without this, T19 would also pass against a script that shipped nothing at
# all — which would be a different defect (a hole) wearing the same exit code.
check "T19c what WAS collected still shipped before the run failed" \
  "$(ls "$TMPROOT/remote" 2>/dev/null | grep -q '^app-' && echo 0 || echo 1)"

# --- T20: the app leg's window is FLOORED at the retention window, in both branches --------------
# #1561. With no stamp the leg used to read the container's entire json-file layer and ship it
# off-box inside an `age` envelope this box cannot decrypt to erase selectively. The floor is
# ADR 0024 D7 policy 1's window, parity with jobbliggaren-logprune.sh.
#
# WHY THE STAMP IS BACKDATED RATHER THAN HAND-INVENTED (§5 `Tests:`). Production sets the stamp's
# mtime itself — `touch -d "@${run_epoch}"` at the end of every successful run — so "a stamp whose
# mtime is older than the window" is a state src/ produces, reached here through the same property:
# a box that was down, or a leg that withheld its stamp, for longer than the window.
since_epoch_of_last_logs_call() {
  local ts
  ts=$(grep -- '--since' "$TMPROOT/docker-argv" 2>/dev/null | tail -1 \
       | sed -n 's/.*--since \([^ ]*\).*/\1/p')
  [ -n "$ts" ] || { echo ""; return; }
  date -u -d "$ts" +%s 2>/dev/null
}

# Tolerance, not equality: the script computes its own floor a moment after the case computes the
# reference. 120 s is far tighter than the 15 days separating T20c's two candidate answers, so it
# cannot let the wrong branch pass.
within() {
  local got="$1" want="$2" slack=120 delta
  [ -n "$got" ] || return 1
  delta=$(( got - want )); [ "$delta" -lt 0 ] && delta=$(( -delta ))
  [ "$delta" -le "$slack" ]
}

reset_fixture
printf 'api line one\n' >"$TMPROOT/containers/jobbliggaren-api"
run_sut >/dev/null 2>&1
now=$(date -u +%s)
got=$(since_epoch_of_last_logs_call)
check "T20  with NO stamp the window opens at the floor, not at the container's start" \
  "$(within "$got" "$(( now - 30 * 86400 ))" && echo 0 || echo 1)"

# The unchanged arm. Without it T20 would also pass against a script that had replaced the stamp
# anchor with a fixed 30-day window — which would ship 30 days every hour, forever.
reset_fixture
printf 'api line one\n' >"$TMPROOT/containers/jobbliggaren-api"
stamp_epoch=$(( $(date -u +%s) - 7200 ))
printf '20260101T000000Z\n' >"$TMPROOT/state/last-successful-logship"
touch -d "@${stamp_epoch}" "$TMPROOT/state/last-successful-logship"
run_sut >/dev/null 2>&1
got=$(since_epoch_of_last_logs_call)
check "T20b a stamp INSIDE the window still anchors the window, unchanged" \
  "$(within "$got" "$stamp_epoch" && echo 0 || echo 1)"

# THE CONTROL THAT CROSSES THE THRESHOLD, and the reason the floor is in both branches rather than
# only the stamp-less one. Without this case the suite cannot tell a floor from a first-run
# special case: a script that floored only when the stamp is absent passes T20 and T20b and fails
# only here.
reset_fixture
printf 'api line one\n' >"$TMPROOT/containers/jobbliggaren-api"
stale_epoch=$(( $(date -u +%s) - 45 * 86400 ))
printf '20260101T000000Z\n' >"$TMPROOT/state/last-successful-logship"
touch -d "@${stale_epoch}" "$TMPROOT/state/last-successful-logship"
run_sut >/dev/null 2>&1
now=$(date -u +%s)
got=$(since_epoch_of_last_logs_call)
check "T20c a stamp OLDER than the window is clamped to the floor, not honoured" \
  "$(within "$got" "$(( now - 30 * 86400 ))" && echo 0 || echo 1)"

# --- T22: an UNREACHABLE daemon is not a quiet skip ----------------------------------------------
# #1316, the third door into the loss `429c1e69` repaired. `docker inspect` exits 1 against an
# unreachable daemon in exactly the shape it uses for a container that does not exist, and the loop
# reads that shape as a normal skip. Every container is skipped, the extract stays empty, `app_rc`
# is never set — and the stamp is written, anchoring the next run past a window nothing read.
#
# Three assertions, T19's shape. The third is not decoration: "container missing" is the diagnosis
# an operator would otherwise act on, and it sends them to the wrong box entirely.
reset_fixture
printf 'api line one\n' >"$TMPROOT/containers/jobbliggaren-api"
out=$(DOCKER_STUB_DAEMON_DOWN=1 run_sut); rc=$?
check "T22 an unreachable docker daemon fails the run and writes NO stamp" \
  "$([ "$rc" -ne 0 ] && [ ! -f "$TMPROOT/state/last-successful-logship" ] && echo 0 || echo 1)"
check "T22b the failure names the DAEMON rather than a missing container" \
  "$(echo "$out" | grep -q 'daemon is not reachable' && echo 0 || echo 1)"

# CROSSES THE CONTROL: without this, T22 also passes against a script that died on every failed
# `inspect` — which would break the case the loop's `|| continue` exists for. Three of the four
# containers are absent here by construction, and the run must still succeed and still ship what
# the fourth gave.
#
# IT ALSO CARRIES T22's POSITIVE COUNTERFACTUAL. T22 asserts that a dead daemon writes NO stamp,
# and without the `-f` below that half would pass just as well against a script that never wrote
# the stamp at all — the inert-guarantee shape T6/T7 exist to cross on the journal cursor, which
# T17's and T19's `[ ! -f ]` halves inherit from here.
reset_fixture
printf 'api line one\n' >"$TMPROOT/containers/jobbliggaren-api"
out=$(run_sut); rc=$?
check "T22c a MISSING container is a skip, the stamp IS written, and the run succeeds" \
  "$([ "$rc" -eq 0 ] && [ -f "$TMPROOT/state/last-successful-logship" ] \
     && ls "$TMPROOT/remote" 2>/dev/null | grep -q '^app-' && echo 0 || echo 1)"

# --- T22d: the probe's PLACEMENT, which is what keeps `Requires=` refused --------------------------
# The ordering was asserted in three places — the unit header, the probe's own comment and the PR
# body — and instrumented in none. It is the whole reason a dead daemon may fail this run at all:
# the two legs carrying the forensic obligation have already had their turn by then, so failing
# here does not suppress the archive of the journal that would explain the docker fault.
#
# Measured before this case existed: hoisting the probe into the precondition block — where this
# file already keeps its `command -v` checks, which is what makes the move plausible as a future
# tidy-up — left every other case in this suite passing.
reset_fixture
printf 'journal line one\n' >"$TMPROOT/journal-entries"
printf 'audit line one\n'   >"$TMPROOT/audit/audit.log"
printf 'api line one\n'     >"$TMPROOT/containers/jobbliggaren-api"
out=$(DOCKER_STUB_DAEMON_DOWN=1 run_sut); rc=$?
check "T22d a dead daemon fails the run only AFTER journal and audit have had their turn" \
  "$([ "$rc" -ne 0 ] \
     && ls "$TMPROOT/remote" 2>/dev/null | grep -q '^journal-' \
     && ls "$TMPROOT/remote" 2>/dev/null | grep -q '^audit-' && echo 0 || echo 1)"

# --- T23: a stamp dated in the FUTURE anchors nothing --------------------------------------------
# #1316's second door, security-auditor's Minor 3 on PR #1567. #1561's floor clamps DOWNWARD only,
# so a future stamp passed through and became `--since <future>` — which docker accepts silently
# (rc=0, empty output), unlike a malformed date, which exits 1. Header-only extract, ship
# suppressed, `app_rc` 0, stamp written, next run anchored past the window.
#
# §5 `Tests:` — THE ACTOR IS NAMED, because production cannot produce this state. Every successful
# run ends `touch -d "@${run_epoch}"`, so no path in this repo writes an mtime ahead of its own
# run's start. What does: a clock that ran backwards, a file restored from a later backup, or
# tampering. None of the three is callable from here, so naming them is the whole obligation.
# Whether any is reachable on the box is UNMEASURED and this case asserts nothing about it.
reset_fixture
printf 'api line one\n' >"$TMPROOT/containers/jobbliggaren-api"
future_epoch=$(( $(date -u +%s) + 7200 ))
printf '20260101T000000Z\n' >"$TMPROOT/state/last-successful-logship"
touch -d "@${future_epoch}" "$TMPROOT/state/last-successful-logship"
out=$(run_sut); rc=$?
now=$(date -u +%s)
got=$(since_epoch_of_last_logs_call)
check "T23 a stamp dated in the FUTURE is refused, and the window opens at the floor" \
  "$(within "$got" "$(( now - 30 * 86400 ))" && echo 0 || echo 1)"
# The refusal is SAID. T4's counterpart on the shipping side: a silent fallback would leave an
# operator reading a 30-day re-ship with nothing naming why.
check "T23b the refusal is logged rather than silent" \
  "$(echo "$out" | grep -q 'dated after this run started' && echo 0 || echo 1)"

# --- T21: the floor's number is PARITY, and a KEEP-IN-SYNC note is not an instrument ------------
# jobbliggaren-logship.sh declares RETENTION_DAYS as parity with jobbliggaren-logprune.sh, both
# following ADR 0024 D7 policy 1. That note is prose; this is the instrument — T12's form, for the
# same reason. If D7 moves and only the prune follows, this leg keeps reading 30 days back and
# ships past-window lines off-box into objects this box cannot decrypt, with nothing red.
prune_sut="$script_dir/jobbliggaren-logprune.sh"
if [ -f "$prune_sut" ]; then
  a=$(grep -E "^readonly RETENTION_DAYS=" "$SUT" | head -1 | sed 's/.*=//')
  b=$(grep -E "^readonly RETENTION_DAYS=" "$prune_sut" | head -1 | sed 's/.*=//')
  check "T21 RETENTION_DAYS matches jobbliggaren-logprune.sh" \
    "$([ -n "$a" ] && [ "$a" = "$b" ] && echo 0 || echo 1)"
elif [ -n "${JBL_REQUIRE_SIBLING_SCRIPTS:-}" ]; then
  # An announced skip is not a measurement — T12's own reason, and it holds here for the same one:
  # the moment this check matters is the moment the sibling is renamed or moved.
  check "T21 jobbliggaren-logprune.sh is present (JBL_REQUIRE_SIBLING_SCRIPTS)" 1
else
  echo "  SKIP  T21 jobbliggaren-logprune.sh not found"
fi

# --- T18: --check has a CONSUMER, and that is what makes it a control ----------------------------
# T1-T4 measure the probe's behaviour and say nothing about whether anything calls it. It was
# delivered with no caller: three files asserted that a stopped archive lights the box's alarm
# surface, and none of them would have. The gap is invisible to every runtime surface, because
# jobbliggaren-logship.service's ConditionPathExists makes a credential-less run SKIP — inactive,
# logged, explicitly not failed — so an archive that never once succeeds is not on
# `systemctl --failed` either. Source assertions, in the T12/T15/T16 family: the units are
# installed by hand from the repo, so the repo is where the coupling can be pinned at all.
fresh_service="$script_dir/jobbliggaren-logship-fresh.service"
fresh_timer="$script_dir/jobbliggaren-logship-fresh.timer"
check "T18  a unit exists whose ExecStart calls this script with --check" \
  "$(grep -qE '^ExecStart=.*jobbliggaren-logship\.sh --check$' "$fresh_service" 2>/dev/null && echo 0 || echo 1)"
# Same condition as the shipping unit, and it is not cosmetic: without it the probe fails on every
# box between a reboot and a credential injection — the designed operating model — and an alarm
# that is always lit trains its reader to stop reading.
check "T18b the probe carries the shipping unit's ConditionPathExists" \
  "$([ "$(grep -c '^ConditionPathExists=/run/jobbliggaren/host-secrets/Backup__RcloneConfigBase64$' "$fresh_service" 2>/dev/null)" = 1 ] \
     && [ "$(grep -c '^ConditionPathExists=/run/jobbliggaren/host-secrets/Backup__RcloneConfigBase64$' "$script_dir/jobbliggaren-logship.service" 2>/dev/null)" = 1 ] \
     && echo 0 || echo 1)"
# OnCalendar and not OnUnitActiveSec: a Type=oneshot unit never becomes ACTIVE (systemd#21600), so
# the wrong spelling would neither re-fire nor clear. This repo has repaired that defect once
# already, in jobbliggaren-secrets-present.timer.
check "T18c the probe's timer is OnCalendar-driven, not OnUnitActiveSec" \
  "$(grep -q '^OnCalendar=' "$fresh_timer" 2>/dev/null && ! grep -q '^OnUnitActiveSec=' "$fresh_timer" 2>/dev/null && echo 0 || echo 1)"

echo
if [ -n "$STUBBED_REAL_TOOLS" ]; then
  printf 'NOTE: this run SUBSTITUTED real tools: %s\n' "$STUBBED_REAL_TOOLS"
  printf '      Those behaviours are NOT measured here. CI has them and does measure them.\n'
fi
printf '%d passed, %d failed\n' "$pass" "$fail"
[ "$fail" -eq 0 ]
