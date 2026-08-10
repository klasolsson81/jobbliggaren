#!/usr/bin/env bash
# jobbliggaren-heartbeat — the box's cadenced reader, and its dead-man.
#
# GATE M-7 (#1201). ADR 0050 `Amendment 2026-08-04` §6b states the gate at OBLIGATION level and
# the legal basis lives in that gate row; it is deliberately not restated here, because a second
# formulation is a second place to drift apart. What this file implements is the mechanism
# senior-cto-advisor bound on 2026-08-10, and the obligation it answers is written in
# docs/runbooks/host-detection.md.
#
# TWO PROPERTIES, ONE MECHANISM, AND NEITHER CONTAINS THE OTHER.
#   · Predicate false -> POST to <url>/fail. The box says something is wrong: fast, diagnostic,
#     and it does NOT survive an attacker who controls the box.
#   · No ping at all -> the expecter fires after its grace period. The box stopped saying it is
#     well: slow, carries no diagnosis, and it is the ONE signal a root attacker cannot erase,
#     because silence is the signal. Disarming this unit IS the alarm.
# The honest limit, written here so nobody reads the mechanism as more than it is: an attacker
# who leaves the heartbeat running is not caught by either property. This converts "disarmed"
# into "alerted"; it never converts "compromised" into "alerted".
#
# WHY POLLING RATHER THAN `OnFailure=` DROP-INS. A reader of `systemctl --failed` covers every
# unit, including units that do not exist yet, with no per-unit registration. `OnFailure=` needs
# one drop-in per unit — a hand-maintained list whose failure mode is a NEW unit shipped without
# its drop-in, silently uncovered. Polling is strictly more general, and against a <= 1 h bound
# the extra latency is free. It also means this mechanism edits no unit that #196/#197/#198
# delivered.
#
# THE PREDICATE MUST NOT BE VACUOUS, AND ON THIS BOX IT WOULD BE. `systemctl --failed` is empty
# today because NOTHING FEEDS IT: measured 2026-08-10, the only jobbliggaren unit installed is
# jobbliggaren-reconcile. A green light whose green is true of its evidence and false of its
# subject is worse than no light, so P3 below asserts a floor set of timers is actually running,
# and P4 asserts the audit rules are actually loaded.
#
# EXIT CONTRACT: THIS SCRIPT ALWAYS EXITS 0. It must never land on `systemctl --failed` itself,
# because P1 would then be permanently false and the alarm permanently lit — and an alarm that is
# always lit trains an operator to stop reading it. A bash-level crash is still covered twice
# over: the unit fails AND the ping is not sent.
set -uo pipefail

readonly ENV_FILE=/etc/jobbliggaren/detection.env
readonly AUDIT_RULES_FILE=/etc/audit/rules.d/zz-jobbliggaren.rules

# The floor set: timers whose absence would make P1's emptiness meaningless.
# KEEP IN SYNC AS UNITS LAND. #197's jobbliggaren-backup.timer and #198's
# jobbliggaren-secrets-present.timer belong here the moment they are installed on the box, and
# that handover is written in docs/runbooks/host-detection.md rather than left to memory.
# Note that jobbliggaren-secrets-present.timer cannot be installed before #197's host-secrets are
# provisioned: its --check exits non-zero on those keys too, so it would light the alarm surface
# permanently. Sequencing, not a defect.
readonly FLOOR_TIMERS="jobbliggaren-reconcile.timer jobbliggaren-heartbeat.timer"

# Free-space floor, in percent, on the two filesystems a full disk would stop the stack from.
# This absorbs the DETECTION half of the disk-usage finding security-auditor routed to #196 on
# PR #1229; #196 closed without it. The QUOTA half is deliberately not absorbed — a threshold is
# detection, not a limit — and is filed separately.
readonly DISK_MIN_FREE_PCT=15
readonly DOCKER_ROOT=/var/lib/docker

# Ceiling on how much predicate detail may reach the wire. The body is built from an allowlist,
# never from raw command output, so this is a second bound and not the control itself.
readonly MAX_BODY_BYTES=2000

log() { printf '%s\n' "$*" >&2; }

# ------------------------------------------------------------------------------------------
# PII invariant, implemented as a mechanism rather than as an instruction.
#
# Everything that reaches the wire passes through here. The contents of an audit record are NEVER
# posted — only a count. ADR 0123 places the operator's workstation inside the trust boundary,
# and a source address is personal data (Breyer C-582/14, the precedent this repo already applies
# in vps-base-hardening.md §6.5).
#
# THE CONTROL IS THE SHAPE, NOT A CHARACTER SET, AND THE DIFFERENCE IS NOT ACADEMIC. A character
# allowlist of [A-Za-z0-9@._-] passes `155.4.133.179` and `someone@example.com` through
# UNCHANGED — every character in both is "allowed". systemd puts arbitrary escaped data in the
# INSTANCE part of a templated unit name, and per-connection units are named exactly that way
# (`sshd@<addr>:<port>-<addr>:<port>.service`), so the address of whoever connected can reach a
# failure list verbatim. That is the whole exposure, and a character filter does not touch it.
#
# So: drop the instance part outright and keep only the template name and the unit type, which
# together carry all the diagnostic value and none of the payload. `sshd@1.2.3.4:22-....service`
# becomes `sshd@.service`; `jobbliggaren-backup.service` is unchanged. Anything that is not
# shaped like a unit name at all still passes the character filter and the length bound.
sanitize_token() {
  local raw="$1" out template suffix
  out=$(printf '%s' "$raw" | tr -cd 'A-Za-z0-9@._:-')
  if [[ "$out" == *@* ]]; then
    template="${out%%@*}"
    suffix="${out##*.}"
    # Only re-attach a suffix that looks like a unit type; otherwise leave it off entirely
    # rather than risk carrying instance data that happened to follow the last dot.
    case "$suffix" in
      service | timer | socket | mount | scope | slice | path | target | device | swap)
        out="${template}@.${suffix}"
        ;;
      *) out="${template}@" ;;
    esac
  fi
  # A colon is legal in an instance name and is now gone with it; strip any survivor so no
  # `host:port` shape can leave in a token that was not unit-shaped.
  out=$(printf '%s' "$out" | tr -d ':')
  printf '%.64s' "$out"
}

# ------------------------------------------------------------------------------------------
# The predicates. Each appends a short, allowlisted reason to FAILURES on a false verdict and
# never echoes raw command output.
FAILURES=""

fail_with() {
  local reason="$1"
  FAILURES="${FAILURES}${FAILURES:+; }${reason}"
}

# P1 — the existing alarm surface is clean.
check_failed_units() {
  local failed count names
  failed=$(systemctl --failed --no-legend --no-pager 2>/dev/null | awk '{print $1}')
  [ -z "$failed" ] && return 0
  count=$(printf '%s\n' "$failed" | grep -c . || true)
  names=""
  while IFS= read -r unit; do
    [ -z "$unit" ] && continue
    names="${names}${names:+,}$(sanitize_token "$unit")"
  done <<<"$failed"
  fail_with "failed-units=${count}:${names}"
}

# P2 — every ENABLED jobbliggaren timer is also ACTIVE. Derived, never a list: this catches a
# `stop` or a `mask` on any current or future unit, which is the failure `systemctl --failed`
# structurally cannot show (a unit that is never triggered is not failed).
check_enabled_timers_active() {
  local enabled inactive=""
  enabled=$(systemctl list-unit-files 'jobbliggaren-*.timer' --state=enabled \
    --no-legend --no-pager 2>/dev/null | awk '{print $1}')
  [ -z "$enabled" ] && return 0
  while IFS= read -r timer; do
    [ -z "$timer" ] && continue
    systemctl is-active --quiet "$timer" 2>/dev/null ||
      inactive="${inactive}${inactive:+,}$(sanitize_token "$timer")"
  done <<<"$enabled"
  [ -z "$inactive" ] || fail_with "enabled-but-inactive=${inactive}"
}

# P3 — the non-vacuity floor. Without this, an empty failure list on a box where nothing runs
# reports perfect health, which is exactly the state measured on 2026-08-10.
check_floor_timers() {
  local missing="" t
  for t in $FLOOR_TIMERS; do
    if ! systemctl is-enabled --quiet "$t" 2>/dev/null || ! systemctl is-active --quiet "$t" 2>/dev/null; then
      missing="${missing}${missing:+,}$(sanitize_token "$t")"
    fi
  done
  [ -z "$missing" ] || fail_with "floor-timer-down=${missing}"
}

# P4 — the detection configuration's own integrity: are the rules actually LOADED in the kernel?
#
# The expected key set is derived from the rules file, never from a literal here — two homes for
# one list is how they drift. Both a missing rules file and a missing auditctl are failures, not
# passes: an empty expectation compared against an empty kernel would be the falsest green there
# is, and it is precisely what an ordering mistake (rules.d sort order, or a watch on a tmpfs
# path that did not exist at load time) produces.
check_audit_rules_loaded() {
  local expected loaded missing="" k
  if [ ! -r "$AUDIT_RULES_FILE" ]; then
    fail_with "audit-rules-file-absent"
    return 0
  fi
  if ! command -v auditctl >/dev/null 2>&1; then
    fail_with "auditctl-absent"
    return 0
  fi
  expected=$(grep -oE -- '-k[[:space:]]+jbl-[A-Za-z0-9_-]+' "$AUDIT_RULES_FILE" 2>/dev/null |
    awk '{print $2}' | sort -u)
  if [ -z "$expected" ]; then
    fail_with "audit-rules-file-defines-no-keys"
    return 0
  fi
  loaded=$(auditctl -l 2>/dev/null | grep -oE -- '-k[[:space:]]+jbl-[A-Za-z0-9_-]+' |
    awk '{print $2}' | sort -u)
  while IFS= read -r k; do
    [ -z "$k" ] && continue
    printf '%s\n' "$loaded" | grep -qxF "$k" || missing="${missing}${missing:+,}$(sanitize_token "$k")"
  done <<<"$expected"
  [ -z "$missing" ] || fail_with "audit-keys-not-loaded=${missing}"
}

# P5 — free space on the two filesystems whose exhaustion stops the stack. Detection only.
check_disk() {
  local path used free low=""
  for path in / "$DOCKER_ROOT"; do
    [ -d "$path" ] || continue
    used=$(df --output=pcent "$path" 2>/dev/null | tail -1 | tr -cd '0-9')
    [ -z "$used" ] && continue
    free=$((100 - used))
    [ "$free" -lt "$DISK_MIN_FREE_PCT" ] &&
      low="${low}${low:+,}$(sanitize_token "$path")=${free}pct"
  done
  [ -z "$low" ] || fail_with "disk-low=${low}"
}

# ------------------------------------------------------------------------------------------
# The wire. The URL is a capability: possession is authority, so it is read from a root-owned
# file and never logged, never echoed, and never included in any diagnostic. A capability URL
# that reaches a log is a leaked credential.
#
# What a stolen ping URL buys an attacker is deliberately small: suppressing or faking this
# box's alarms. A root attacker can do that anyway by leaving the heartbeat running, so the
# credential adds no capability that the threat model did not already grant. That is the reason
# this channel shape was chosen over one whose credential opens a second failure domain.
post() {
  local url="$1" body="$2"
  command -v curl >/dev/null 2>&1 || {
    log "curl absent: cannot report; the expecter's grace period is the backstop"
    return 0
  }
  # --max-time bounds the unit; failure to reach the expecter is NOT an error here, because the
  # dead-man is exactly the mechanism that covers an unreachable expecter.
  curl -fsS --max-time 20 --retry 2 --retry-delay 3 \
    -X POST -H 'Content-Type: text/plain' \
    --data-binary "$body" "$url" >/dev/null 2>&1 ||
    log "ping did not complete; the expecter's grace period is the backstop"
  return 0
}

main() {
  if [ ! -r "$ENV_FILE" ]; then
    log "REFUSING: $ENV_FILE is not readable — no ping URL, so this box cannot report."
    log "The expecter's grace period is the backstop and will fire. See host-detection.md."
    return 0
  fi
  # shellcheck disable=SC1090
  . "$ENV_FILE"
  if [ -z "${HEARTBEAT_PING_URL:-}" ]; then
    log "REFUSING: HEARTBEAT_PING_URL is unset in $ENV_FILE — nothing to ping."
    return 0
  fi

  check_failed_units
  check_enabled_timers_active
  check_floor_timers
  check_audit_rules_loaded
  check_disk

  if [ -z "$FAILURES" ]; then
    post "$HEARTBEAT_PING_URL" "ok"
    log "heartbeat: all predicates hold"
    return 0
  fi

  local body
  body=$(printf '%.*s' "$MAX_BODY_BYTES" "$FAILURES")
  post "${HEARTBEAT_PING_URL%/}/fail" "$body"
  # Logged to the journal as well as posted: the journal is the forensic copy, the ping is the
  # awareness. They are different jobs and one does not replace the other.
  log "heartbeat: FAILING — $body"
  return 0
}

main "$@"
exit 0
