#!/usr/bin/env bash
#
# Fixture tests for jobbliggaren-heartbeat.sh — gate M-7's predicate and its payload invariant.
#
# Run:  bash deploy/systemd/jobbliggaren-heartbeat.test.sh
#
# WHY THIS EXISTS. The unit needs root, auditd and a real expecter, and that is a real reason not
# to test the orchestration. It is not a reason to leave the decisions untested, and three of
# them carry the whole blast radius:
#
#   1. THE PREDICATE. Every false arm must produce exactly one /fail POST that NAMES the failing
#      predicate — including the two non-vacuity arms (P3 floor timers, P4 audit keys loaded)
#      whose entire purpose is to fail on a box where everything else looks green. Those arms
#      never execute on a healthy box, which is precisely why a green run cannot observe them.
#   2. THE PAYLOAD INVARIANT. Personal data must not reach a third party. The fixture below feeds
#      an address and a mail address through the ONE surface that can carry arbitrary text — a
#      failed unit's name — and asserts they do not come out. It is written to CROSS the control:
#      every character in `155.4.133.179` is inside the character allowlist, so a character-based
#      filter passes this test's input through unchanged and the test fails. Only the shape-based
#      control passes it.
#   3. THE EXIT CONTRACT. The script must exit 0 on every path, including "curl is missing" and
#      "the expecter is unreachable". If it ever failed, it would land on `systemctl --failed`,
#      make P1 permanently false and light the alarm permanently — the failure mode this repo has
#      already repaired twice in other timers.
#
# systemctl, auditctl, df and curl are stubbed on a from-scratch PATH: no root, no daemon, no
# network. The script's absolute paths are rewritten into the fixture tree, exactly as
# jobbliggaren-reconcile.test.sh does; the logic itself is untouched.

set -uo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/jobbliggaren-heartbeat.sh"
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

# A URL distinctive enough that its appearance anywhere in the script's own output is
# unmistakable. The capability-URL leak test greps for this exact token.
readonly TEST_PING_URL="https://hc-ping.example/UUID-CANARY-9f3a"

readonly FIXTURE_SUT="$TMPROOT/heartbeat.sh"
prepare_sut() {
  sed -e "s#^readonly ENV_FILE=.*#readonly ENV_FILE=$TMPROOT/detection.env#" \
    -e "s#^readonly AUDIT_RULES_FILE=.*#readonly AUDIT_RULES_FILE=$TMPROOT/audit.rules#" \
    -e "s#^readonly DOCKER_ROOT=.*#readonly DOCKER_ROOT=$TMPROOT/dockerroot#" \
    "$SUT" >"$FIXTURE_SUT"
  chmod +x "$FIXTURE_SUT"
  mkdir -p "$TMPROOT/dockerroot"
  printf 'HEARTBEAT_PING_URL=%s\n' "$TEST_PING_URL" >"$TMPROOT/detection.env"
}

# --- stubs ------------------------------------------------------------------------------------
# Each stub reads a fixture file so a case can set state without rewriting the stub.

write_stubs() {
  # THE --failed STUB EMITS systemd's LEADING STATUS GLYPH UNLESS `--plain` IS PASSED, because
  # that is what the real command does: `--failed` is `list-units --state=failed`, and only
  # `--plain` removes the bullet column — `--no-legend` drops the header and footer and nothing
  # else. Measured on the box 2026-08-10 against a real failed unit.
  #
  # This is what makes the P1 and payload cases cross their control instead of sitting beside it.
  # An earlier revision of this suite emitted no glyph, so every assertion about unit NAMES was
  # made against a shape production never produces: in production `awk '{print $1}'` returned the
  # glyph, the name never reached the sanitiser, and the payload invariant held only by accident.
  # Drop `--plain` from the script and this suite now goes red.
  cat >"$BIN/systemctl" <<EOF
#!/usr/bin/env bash
case "\$*" in
  *"--failed"*)
      if [[ "\$*" == *--plain* ]]; then
        cat "$TMPROOT/failed-units"
      else
        sed 's/^/\xe2\x97\x8f /' "$TMPROOT/failed-units"
      fi ;;
  *"list-unit-files"*)   cat "$TMPROOT/enabled-timers" ;;
  *"is-active"*)
      for a in "\$@"; do :; done
      grep -qxF "\$a" "$TMPROOT/active-timers" && exit 0 || exit 3 ;;
  *"is-enabled"*)
      for a in "\$@"; do :; done
      grep -qxF "\$a" "$TMPROOT/enabled-set" && exit 0 || exit 1 ;;
  *) exit 0 ;;
esac
EOF

  cat >"$BIN/auditctl" <<EOF
#!/usr/bin/env bash
[ "\$1" = "-l" ] && cat "$TMPROOT/loaded-rules"
exit 0
EOF

  cat >"$BIN/df" <<EOF
#!/usr/bin/env bash
# Mirrors \`df --output=pcent <path>\`: a header line, then the percentage.
echo "Use%"
cat "$TMPROOT/disk-pcent"
EOF

  # Records every invocation as "<url> <body>", one per line, so a case can assert both the
  # number of posts and their destination.
  #
  # THE URL ARRIVES ON STDIN, NOT IN argv. The script passes `--config -` and writes
  # `url = "..."` to stdin, so the capability does not appear in /proc/<pid>/cmdline. This stub
  # must therefore read it the same way — when it parsed argv instead, every case in this suite
  # recorded zero posts and the payload assertions passed VACUOUSLY, because "the address is not
  # in the body" is trivially true of a body that was never sent.
  cat >"$BIN/curl" <<EOF
#!/usr/bin/env bash
url=""; body=""; use_stdin=0
while [ \$# -gt 0 ]; do
  case "\$1" in
    --config) [ "\$2" = "-" ] && use_stdin=1; shift 2 ;;
    --data-binary) body="\$2"; shift 2 ;;
    --max-time|--retry|--retry-delay|--retry-max-time|-H|-X) shift 2 ;;
    -*) shift ;;
    *) url="\$1"; shift ;;
  esac
done
if [ "\$use_stdin" -eq 1 ]; then
  url=\$(sed -n 's/^[[:space:]]*url[[:space:]]*=[[:space:]]*"\(.*\)"[[:space:]]*\$/\1/p')
fi
printf '%s %s\n' "\$url" "\$body" >> "$TMPROOT/posts"
exit 0
EOF

  chmod +x "$BIN/systemctl" "$BIN/auditctl" "$BIN/df" "$BIN/curl"
}

# --- fixture state ----------------------------------------------------------------------------

healthy_state() {
  : >"$TMPROOT/failed-units"
  # THESE THREE FILES MODEL ONE BOX, NOT ONE CONSTANT, and that is the distinction to keep:
  # enabled-timers stubs `list-unit-files --state=enabled` (P2's input), enabled-set stubs
  # `is-enabled` and active-timers stubs `is-active` (P2/P3's). FLOOR_TIMERS is a SUBSET of what
  # a box has enabled, so updating this fixture against the constant rather than against the box
  # is how two of the three drifted apart once already. A timer that is is-enabled must appear in
  # list-unit-files too — a property of systemd this box was checked against, not a count:
  #   systemctl list-unit-files 'jobbliggaren*' --state=enabled
  # Regenerate that command against the box rather than trusting this list to have kept up.
  # Last regenerated 2026-09-03, when #1170's prune timer was enabled.
  printf 'jobbliggaren-reconcile.timer enabled enabled\njobbliggaren-heartbeat.timer enabled enabled\njobbliggaren-secrets-present.timer enabled enabled\njobbliggaren-logship.timer enabled enabled\njobbliggaren-logship-fresh.timer enabled enabled\njobbliggaren-logprune.timer enabled enabled\n' \
    >"$TMPROOT/enabled-timers"
  printf 'jobbliggaren-reconcile.timer\njobbliggaren-heartbeat.timer\njobbliggaren-secrets-present.timer\njobbliggaren-logship.timer\njobbliggaren-logship-fresh.timer\njobbliggaren-logprune.timer\n' >"$TMPROOT/active-timers"
  printf 'jobbliggaren-reconcile.timer\njobbliggaren-heartbeat.timer\njobbliggaren-secrets-present.timer\njobbliggaren-logship.timer\njobbliggaren-logship-fresh.timer\njobbliggaren-logprune.timer\n' >"$TMPROOT/enabled-set"
  cat >"$TMPROOT/audit.rules" <<'RULES'
-w /run/jobbliggaren -p rwa -k jbl-key-tmpfs
-w /etc/sudoers -p wa -k jbl-sudoers
-e 1
RULES
  printf -- '-w /run/jobbliggaren -p rwa -k jbl-key-tmpfs\n-w /etc/sudoers -p wa -k jbl-sudoers\n' \
    >"$TMPROOT/loaded-rules"
  printf '40%%\n' >"$TMPROOT/disk-pcent"
}

run_sut() {
  : >"$TMPROOT/posts"
  PATH="$BIN:/usr/bin:/bin" bash "$FIXTURE_SUT" >"$TMPROOT/stdout" 2>"$TMPROOT/stderr"
  printf '%s' "$?" >"$TMPROOT/exit"
}

# --- assertions -------------------------------------------------------------------------------

ok() {
  pass=$((pass + 1))
  echo "  PASS  $1"
}
no() {
  fail=$((fail + 1))
  echo "  FAIL  $1"
  [ $# -gt 1 ] && echo "        $2"
}

assert_exit_zero() {
  local got
  got=$(cat "$TMPROOT/exit")
  [ "$got" = "0" ] && ok "$1: exits 0" || no "$1: exits 0" "got $got"
}

# `grep -c` prints 0 AND exits 1 when there are no matches, so an `|| echo 0` fallback appends a
# SECOND zero and the caller compares against "0\n0". Swallow the exit status instead; grep's own
# output is already the count.
posts_to() { grep -c "^$1" "$TMPROOT/posts" 2>/dev/null || true; }

assert_one_success_post() {
  local s f
  s=$(posts_to "$TEST_PING_URL ")
  f=$(posts_to "$TEST_PING_URL/fail")
  { [ "$s" = "1" ] && [ "$f" = "0" ]; } &&
    ok "$1: exactly one success ping, no fail ping" ||
    no "$1: exactly one success ping, no fail ping" "success=$s fail=$f"
}

assert_one_fail_post_naming() {
  local needle="$2" f
  f=$(posts_to "$TEST_PING_URL/fail")
  if [ "$f" != "1" ]; then
    no "$1: exactly one fail ping" "fail-post count=$f"
    return
  fi
  if grep -q "$needle" "$TMPROOT/posts"; then
    ok "$1: one fail ping naming '$needle'"
  else
    no "$1: one fail ping naming '$needle'" "body was: $(cat "$TMPROOT/posts")"
  fi
}

# --- the cases --------------------------------------------------------------------------------

prepare_sut
write_stubs

echo "healthy box"
healthy_state
run_sut
assert_exit_zero "healthy"
assert_one_success_post "healthy"

echo
echo "P1 — a failed unit"
healthy_state
printf 'some-broken.service loaded failed failed\n' >"$TMPROOT/failed-units"
run_sut
assert_exit_zero "P1"
assert_one_fail_post_naming "P1" "failed-units="

echo
echo "P2 — an enabled timer that is not active (stopped or masked)"
healthy_state
printf 'jobbliggaren-reconcile.timer\n' >"$TMPROOT/active-timers"
run_sut
assert_exit_zero "P2"
assert_one_fail_post_naming "P2" "enabled-but-inactive="

echo
echo "P3 — the non-vacuity floor: a floor timer is gone"
healthy_state
printf 'jobbliggaren-heartbeat.timer\n' >"$TMPROOT/enabled-set"
printf 'jobbliggaren-heartbeat.timer\n' >"$TMPROOT/active-timers"
: >"$TMPROOT/enabled-timers"
run_sut
assert_exit_zero "P3"
assert_one_fail_post_naming "P3" "floor-timer-down="

echo
echo "P4 — audit keys defined but NOT loaded in the kernel (the false-green trap)"
healthy_state
: >"$TMPROOT/loaded-rules"
run_sut
assert_exit_zero "P4-not-loaded"
assert_one_fail_post_naming "P4-not-loaded" "audit-rules-not-loaded=jbl-"

echo
echo "P4 — auditctl absent entirely"
healthy_state
rm -f "$BIN/auditctl"
run_sut
assert_exit_zero "P4-auditctl-absent"
assert_one_fail_post_naming "P4-auditctl-absent" "auditctl-absent"
write_stubs

echo
echo "P4 — the rules file itself is gone"
healthy_state
rm -f "$TMPROOT/audit.rules"
run_sut
assert_exit_zero "P4-file-absent"
assert_one_fail_post_naming "P4-file-absent" "audit-rules-file-absent"

echo
echo "P4 — the rules file exists but defines no jbl- keys (the falsest green)"
# An empty expectation compared against an empty kernel would agree perfectly. This is the arm
# the script's own comment calls the falsest green there is, and it was the one arm with no case.
healthy_state
printf -- '-e 1\n' >"$TMPROOT/audit.rules"
: >"$TMPROOT/loaded-rules"
run_sut
assert_exit_zero "P4-no-keys"
assert_one_fail_post_naming "P4-no-keys" "audit-rules-file-defines-no-keys"

echo
echo "P4 — a COMMENTED-OUT rule must not mint a phantom key"
# The rules file is comment-heavy by house style and its prose contains `ausearch -k jbl-...`
# examples. A key that exists only in a comment can never be loaded, so counting it would light
# the alarm permanently — the worst outcome this mechanism has.
healthy_state
cat >"$TMPROOT/audit.rules" <<'RULES'
-w /run/jobbliggaren -p rwa -k jbl-key-tmpfs
# example: sudo ausearch -k jbl-not-a-real-rule
#-w /etc/retired -p wa -k jbl-retired
-e 1
RULES
printf -- '-w /run/jobbliggaren -p rwa -k jbl-key-tmpfs\n' >"$TMPROOT/loaded-rules"
run_sut
assert_exit_zero "P4-comments"
assert_one_success_post "P4-comments"

echo
echo "P4 — one key, two paths, only ONE loaded (the key-set blind spot)"
# Seven of ten real keys are carried by more than one watch. Comparing key SETS reports green as
# soon as any rule per key loads, so a single watch failing to load — exactly what happens when
# its path does not exist yet — would be invisible. This case must go red.
healthy_state
cat >"$TMPROOT/audit.rules" <<'RULES'
-w /home/jpadmin/.ssh -p wa -k jbl-authkeys
-w /root/.ssh -p wa -k jbl-authkeys
-e 1
RULES
printf -- '-w /home/jpadmin/.ssh -p wa -k jbl-authkeys
' >"$TMPROOT/loaded-rules"
run_sut
assert_exit_zero "P4-partial-key"
assert_one_fail_post_naming "P4-partial-key" "jbl-authkeys@/root/.ssh"

echo
echo "P5 — disk below the free-space floor"
healthy_state
printf '95%%\n' >"$TMPROOT/disk-pcent"
run_sut
assert_exit_zero "P5"
assert_one_fail_post_naming "P5" "disk-low="

echo
echo "PAYLOAD INVARIANT — personal data in a failed unit name must not reach the wire"
# This input CROSSES the control on purpose. systemd names per-connection units with the peer's
# address in the instance part, and every character of an address or a mail address is inside the
# character allowlist — so a character filter emits them verbatim and this case fails. Only
# dropping the instance part by SHAPE passes it.
healthy_state
printf 'sshd@155.4.133.179:22-10.0.0.9:41000.service loaded failed failed\n' \
  >"$TMPROOT/failed-units"
run_sut
assert_one_fail_post_naming "positive-control for [no address reaches the wire]" "failed-units="
assert_exit_zero "payload"
if grep -qE '155\.4\.133\.179|10\.0\.0\.9' "$TMPROOT/posts"; then
  no "payload: no address reaches the wire" "posted: $(cat "$TMPROOT/posts")"
else
  ok "payload: no address reaches the wire"
fi
if grep -q 'sshd@' "$TMPROOT/posts"; then
  ok "payload: the template name is still reported (diagnostic value kept)"
else
  no "payload: the template name is still reported" "posted: $(cat "$TMPROOT/posts")"
fi

healthy_state
printf 'notify@someone.private@example.com.service loaded failed failed\n' >"$TMPROOT/failed-units"
run_sut
assert_one_fail_post_naming "positive-control for [no mail address reaches the wire]" "failed-units="
if grep -q 'someone.private@example.com' "$TMPROOT/posts"; then
  no "payload: no mail address reaches the wire" "posted: $(cat "$TMPROOT/posts")"
else
  ok "payload: no mail address reaches the wire"
fi

# WITHOUT AN `@` AT ALL — the case an earlier revision of the sanitiser passed through verbatim.
# Every character of an address is inside any character allowlist, so this input is only stopped
# by requiring the token to MATCH a unit shape. security-auditor measured the old behaviour:
# `155.4.133.179` came out unchanged.
healthy_state
printf '155.4.133.179 loaded failed failed\n' >"$TMPROOT/failed-units"
run_sut
assert_one_fail_post_naming "positive-control for [a bare address (no @) does not reach the wire]" "failed-units="
if grep -q '155\.4\.133\.179' "$TMPROOT/posts"; then
  no "payload: a bare address (no @) does not reach the wire" "posted: $(cat "$TMPROOT/posts")"
else
  ok "payload: a bare address (no @) does not reach the wire"
fi

# An address wearing a unit suffix. The shape check must reject it on the NAME part containing
# dots, not merely on the suffix being unknown.
healthy_state
printf '155.4.133.179.service loaded failed failed\n' >"$TMPROOT/failed-units"
run_sut
assert_one_fail_post_naming "positive-control for [an address wearing .service does not reach the wire]" "failed-units="
if grep -q '155\.4\.133\.179' "$TMPROOT/posts"; then
  no "payload: an address wearing .service does not reach the wire" "posted: $(cat "$TMPROOT/posts")"
else
  ok "payload: an address wearing .service does not reach the wire"
fi

# And the ordinary case still reports by name — a control that rejected everything would pass
# every case above while destroying the predicate's whole diagnostic value.
healthy_state
printf 'jobbliggaren-backup.service loaded failed failed\n' >"$TMPROOT/failed-units"
run_sut
if grep -q 'jobbliggaren-backup.service' "$TMPROOT/posts"; then
  ok "payload: an ordinary unit is still reported by name"
else
  no "payload: an ordinary unit is still reported by name" "posted: $(cat "$TMPROOT/posts")"
fi

echo
echo "CAPABILITY URL — never echoed to stdout or stderr"
# WITH A POSITIVE CONTROL. Without asserting that the run actually reached the post path, a
# script that exited early would pass this case in silence — the canary would be absent because
# nothing happened, not because nothing leaked.
healthy_state
run_sut
assert_one_success_post "url-canary(success path)"
if grep -q "UUID-CANARY-9f3a" "$TMPROOT/stdout" "$TMPROOT/stderr"; then
  no "url: never appears in script output (success path)" "$(cat "$TMPROOT/stdout" "$TMPROOT/stderr")"
else
  ok "url: never appears in script output (success path)"
fi

# The FAIL path with a broken curl exercises both diagnostic branches in post() and the
# "heartbeat: FAILING" log line — the places a future edit would most plausibly interpolate the
# URL into a message.
healthy_state
printf 'some-broken.service loaded failed failed\n' >"$TMPROOT/failed-units"
cat >"$BIN/curl" <<'EOF'
#!/usr/bin/env bash
exit 7
EOF
chmod +x "$BIN/curl"
run_sut
if grep -q "UUID-CANARY-9f3a" "$TMPROOT/stdout" "$TMPROOT/stderr"; then
  no "url: never appears in script output (fail path, curl broken)" "$(cat "$TMPROOT/stderr")"
else
  ok "url: never appears in script output (fail path, curl broken)"
fi
write_stubs

echo
echo "EXIT CONTRACT — the script never fails its own unit"
# PATH IS BUILT FROM SCRATCH FOR THIS CASE, not merely prepended. With `$BIN` in front of
# /usr/bin, `command -v curl` still finds the real binary underneath and the guard branch is
# never reached — the case would then quietly measure the same thing as `curl-fails` below. This
# is the house pattern from jobbliggaren-backup.test.sh, which records the same trap.
healthy_state
mkdir -p "$TMPROOT/nocurl"
for t in bash sed awk grep tr sort head tail cat cut wc printf; do
  p=$(command -v "$t" 2>/dev/null) && cp "$p" "$TMPROOT/nocurl/$t" 2>/dev/null
done
cp "$BIN/systemctl" "$BIN/auditctl" "$BIN/df" "$TMPROOT/nocurl/" 2>/dev/null

# The scratch PATH is only a measurement if the tools the script needs are actually reachable on
# it. On a filesystem where copying the shell does not produce a runnable binary (Git Bash on
# Windows), the run would exit 127 and a naive assertion would read that as "the script failed" —
# a rig defect reported as a finding. So the case checks its own precondition and ANNOUNCES a
# skip instead, which is the house pattern from jobbliggaren-reconcile.test.sh. On the ubuntu
# runner, where the blocking `scripts` job runs, nothing is skipped.
# The precondition must be that the scratch PATH RUNS, not merely that a file called bash sits on
# it: a copied binary can exist and still fail to execute (Git Bash binaries need their DLLs), and
# the run would then exit 127 — a rig defect that a naive assertion reports as a finding against
# the script. So prove the toolchain end to end first.
scratch_works=0
if PATH="$TMPROOT/nocurl" "$TMPROOT/nocurl/bash" -c 'printf x | tr x y' 2>/dev/null | grep -q y; then
  scratch_works=1
fi
if [ "$scratch_works" -eq 1 ] &&
  ! PATH="$TMPROOT/nocurl" command -v curl >/dev/null 2>&1; then
  : >"$TMPROOT/posts"
  PATH="$TMPROOT/nocurl" "$TMPROOT/nocurl/bash" "$FIXTURE_SUT" \
    >"$TMPROOT/stdout" 2>"$TMPROOT/stderr"
  printf '%s' "$?" >"$TMPROOT/exit"
  assert_exit_zero "curl-absent(PATH from scratch)"
elif [ "${JBL_REQUIRE_SCRATCH_PATH:-0}" = "1" ]; then
  # An announced skip is not a measurement, so on the runner a skip is a FAILURE. Without this
  # the V3 closing argument ("it measures on ubuntu") would be a claim enforced by nothing — the
  # same reasoning the inject-secrets suite already carries as JBL_REQUIRE_MODE_CASES, in this
  # very CI job.
  no "curl-absent(PATH from scratch): skipped while JBL_REQUIRE_SCRATCH_PATH=1" \
    "the scratch PATH must be runnable on the CI runner; a skip here is an unmeasured case"
else
  echo "  SKIP  curl-absent(PATH from scratch): scratch PATH is not runnable on this host"
  echo "        (an announced skip is not a measurement — set JBL_REQUIRE_SCRATCH_PATH=1 to"
  echo "         make it an error, which CI does)"
fi
write_stubs

healthy_state
cat >"$BIN/curl" <<'EOF'
#!/usr/bin/env bash
exit 7
EOF
chmod +x "$BIN/curl"
run_sut
assert_exit_zero "curl-fails"
write_stubs

healthy_state
rm -f "$TMPROOT/detection.env"
run_sut
assert_exit_zero "env-file-absent"
if [ "$(wc -l <"$TMPROOT/posts")" = "0" ]; then
  ok "env-absent: posts nothing (the dead-man is the backstop)"
else
  no "env-absent: posts nothing" "$(cat "$TMPROOT/posts")"
fi
prepare_sut

echo
echo "----------------------------------------"
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ] || exit 1
