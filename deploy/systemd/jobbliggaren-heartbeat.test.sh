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
  cat >"$BIN/systemctl" <<EOF
#!/usr/bin/env bash
case "\$*" in
  *"--failed"*)          cat "$TMPROOT/failed-units" ;;
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
  cat >"$BIN/curl" <<EOF
#!/usr/bin/env bash
url=""; body=""
while [ \$# -gt 0 ]; do
  case "\$1" in
    --data-binary) body="\$2"; shift 2 ;;
    -*) case "\$1" in
          --max-time|--retry|--retry-delay|-H|-X) shift 2 ;;
          *) shift ;;
        esac ;;
    *) url="\$1"; shift ;;
  esac
done
printf '%s %s\n' "\$url" "\$body" >> "$TMPROOT/posts"
exit 0
EOF

  chmod +x "$BIN/systemctl" "$BIN/auditctl" "$BIN/df" "$BIN/curl"
}

# --- fixture state ----------------------------------------------------------------------------

healthy_state() {
  : >"$TMPROOT/failed-units"
  printf 'jobbliggaren-reconcile.timer enabled enabled\njobbliggaren-heartbeat.timer enabled enabled\n' \
    >"$TMPROOT/enabled-timers"
  printf 'jobbliggaren-reconcile.timer\njobbliggaren-heartbeat.timer\n' >"$TMPROOT/active-timers"
  printf 'jobbliggaren-reconcile.timer\njobbliggaren-heartbeat.timer\n' >"$TMPROOT/enabled-set"
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
assert_one_fail_post_naming "P4-not-loaded" "audit-keys-not-loaded="

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
if grep -q 'someone.private@example.com' "$TMPROOT/posts"; then
  no "payload: no mail address reaches the wire" "posted: $(cat "$TMPROOT/posts")"
else
  ok "payload: no mail address reaches the wire"
fi

echo
echo "CAPABILITY URL — never echoed to stdout or stderr"
healthy_state
run_sut
if grep -q "UUID-CANARY-9f3a" "$TMPROOT/stdout" "$TMPROOT/stderr"; then
  no "url: never appears in script output" "$(cat "$TMPROOT/stdout" "$TMPROOT/stderr")"
else
  ok "url: never appears in script output"
fi

echo
echo "EXIT CONTRACT — the script never fails its own unit"
healthy_state
rm -f "$BIN/curl"
run_sut
assert_exit_zero "curl-absent"
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
