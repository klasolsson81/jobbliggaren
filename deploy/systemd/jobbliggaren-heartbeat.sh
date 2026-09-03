#!/usr/bin/env bash
# jobbliggaren-heartbeat — the box's cadenced reader, and its dead-man.
#
# GATE M-7 (#1201). ADR 0050 `Amendment 2026-08-04` §6b states the gate at OBLIGATION level and
# the legal basis lives in that gate row; it is deliberately not restated here, because a second
# formulation is a second place to drift apart. What this file implements is the mechanism
# senior-cto-advisor bound on 2026-08-10, and the obligation it answers is written in
# docs/runbooks/host-detection.md.
#
# TWO PROPERTIES, ONE MECHANISM. Rationale and the variant verdicts live in ADR 0126 and
# docs/runbooks/host-detection.md §3; what is written here is only what the code cannot show.
#   · Predicate false -> POST to <url>/fail: fast and diagnostic.
#   · No ping at all  -> the expecter fires after its grace period: slow, no diagnosis.
#
# WHAT THE DEAD-MAN ACTUALLY SURVIVES, stated precisely because the obvious reading is too
# generous. It catches an attacker who DISARMS this box's reporting — stopping the timer, killing
# the unit, cutting the network, taking the machine. It does NOT catch the attacker ADR 0123
# names: root reads the ping URL out of /etc/jobbliggaren/detection.env (root-owned, and root
# reads it trivially) and replays success pings from anywhere, so silence never occurs. Cost to
# that attacker: one `cat` and one cron line. So this converts "disarmed" into "alerted" only
# while the credential beside it is not taken too — which is the same mistake-and-lower-privilege
# class the rest of this mechanism is honest about, and NOT a control against root.
#
# THE PREDICATE MUST NOT BE VACUOUS. `systemctl --failed` can be empty because everything is well
# OR because almost nothing feeds it — on this box, three of the four feeding units were shipped
# and never installed. A green light whose green is true of its evidence and false of its subject
# is worse than no light, so P3 asserts a floor set of timers is actually running and P4 asserts
# the audit rules are actually loaded in the kernel. Regenerate the current state with the
# commands in host-detection.md §7 rather than trusting a number written here.
#
# EXIT CONTRACT: THIS SCRIPT ALWAYS EXITS 0. It must never land on `systemctl --failed` itself,
# because P1 would then be false for as long as the failure stands and the alarm lit with it —
# and an alarm that is lit for its own reasons trains an operator to stop reading it. That is
# also why the curl budget below is bounded well under the unit's TimeoutStartSec: a unit killed
# on timeout is a failed unit, and the exit contract would be defeated by the clock rather than
# by the code.
set -uo pipefail

readonly ENV_FILE=/etc/jobbliggaren/detection.env
readonly AUDIT_RULES_FILE=/etc/audit/rules.d/zz-jobbliggaren.rules

# The floor set: timers whose absence would make P1's emptiness meaningless.
# KEEP IN SYNC AS UNITS LAND. #197's jobbliggaren-backup.timer and -backup-fresh.timer, and
# #198's jobbliggaren-host-secrets-present.timer, belong here the moment they are ENABLED on the box, the
# state check_floor_timers actually measures, and that handover is written in
# docs/runbooks/host-detection.md rather than left to memory.
#
# INSTALLED AND ENABLED ARE TWO MOMENTS, AND SINCE #1329 THEY DIVERGE FOR ONLY ONE OF THE TWO.
# The absence detector split per set. jobbliggaren-secrets-present.timer runs --check over the
# crypto secrets and is enabled in the same visit as its install, because that predicate stopped
# reading #197's keys. jobbliggaren-host-secrets-present.timer runs --check-host over exactly those
# keys, so it must not be ENABLED before they are provisioned: enabled early it fails every fire
# and lights the alarm surface permanently — which, through P1 below, holds that surface red. That
# is ONE page at the transition and then silence, not a repeating one: systemctl --failed latches
# and the expecter notifies on the transition, so a second genuine fault inside the window changes
# only a body no notification announces.
# master-key-ops.md §2 owns the ordering. Sequencing, not a defect.
#
# jobbliggaren-secrets-present.timer JOINED THE SET 2026-08-15, the day it was enabled on the box
# (#198's cutover). It is here because `enable` happened, not because its files were installed —
# that is the trigger this block and host-detection.md §7 both name, and check_floor_timers
# measures is-enabled AND is-active, so an installed-but-disabled unit here would fail every fire.
# The host-only timer stays out for exactly that reason until #197's credential exists.
#
# THE LOGSHIP PAIR JOINED 2026-08-18, the day it was enabled on the box (#1175), on the same
# trigger and not on its install — the files had sat in /etc/systemd/system since 2026-08-15.
# The floor is the ONLY surface that catches a disabled logship timer: P1 sees no failure (the
# service skips) and P2 never considers it (a disabled timer is absent from its input).
#
# The cost is that a DELIBERATE disarm alarms too, and there is one — if the journal is found
# contaminated while #197's credential exists, disarming is the correct emergency act. It takes
# BOTH timers (`-fresh` skips on the same condition and latches P1 alone if left armed) and it
# carries a re-arm duty, because the disarm removes the archive and its only probe together.
# log-sink.md §2 carries the commands where an operator would reach for them.
#
# THE PRUNE TIMER JOINED 2026-09-03, the day it was enabled on the box (#1170), on the same
# trigger and not on its install — here the two happened in one visit, so this entry names the
# `enable` deliberately.
readonly FLOOR_TIMERS="jobbliggaren-reconcile.timer jobbliggaren-heartbeat.timer jobbliggaren-secrets-present.timer jobbliggaren-logship.timer jobbliggaren-logship-fresh.timer jobbliggaren-logprune.timer"

# Free-space floor, in percent. This absorbs the DETECTION half of a disk-usage finding
# security-auditor routed to #196, which closed without it. The QUOTA half is deliberately not
# absorbed — a threshold is detection, not a limit — and is filed as its own issue.
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
# THE CONTROL IS THE SHAPE, AND IT IS UNCONDITIONAL. A character allowlist cannot do this job:
# every character of `155.4.133.179` and of `someone@example.com` is inside any sane allowlist,
# so a character filter emits both verbatim. An earlier revision reduced only tokens containing
# `@`, which made the guarantee conditional on the very thing an attacker controls — security
# auditor measured `155.4.133.179` passing through untouched.
#
# So the token must MATCH a unit name to be reported at all, and anything else is replaced by a
# marker. A unit name is `<name>[@<instance>].<type>`: the instance part is where systemd puts
# arbitrary escaped data (per-connection units are literally
# `sshd@<addr>:<port>-<addr>:<port>.service`), so it is dropped always; `<type>` must be a known
# unit type; and `<name>` must be plain — letters, digits, underscore, hyphen. Requiring no dots
# in `<name>` is what stops `155.4.133.179.service` from being reported as a "unit name".
#
# The cost, stated rather than discovered: a legitimate unit whose name contains a dot (some
# path-derived .mount units) is reported as the marker instead of by name. That is a diagnostic
# loss on a rare class, bought for a guarantee that holds for every input rather than for most.
readonly UNIT_SHAPE_REJECTED="unit-shape-rejected"

sanitize_token() {
  local raw="$1" out type rest name
  out=$(printf '%s' "$raw" | tr -cd 'A-Za-z0-9@._:-')

  type="${out##*.}"
  case "$type" in
    service | timer | socket | mount | automount | swap | target | path | slice | scope | device) ;;
    *)
      printf '%s' "$UNIT_SHAPE_REJECTED"
      return 0
      ;;
  esac

  rest="${out%.*}"   # everything before the last dot
  name="${rest%%@*}" # the template name, before any instance

  case "$name" in
    '' | *[!A-Za-z0-9_-]*)
      printf '%s' "$UNIT_SHAPE_REJECTED"
      return 0
      ;;
  esac

  if [ "$rest" != "$name" ]; then
    printf '%.64s' "${name}@.${type}"
  else
    printf '%.64s' "${name}.${type}"
  fi
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
#
# `--plain` IS LOAD-BEARING, NOT TIDINESS. `--failed` is `list-units --state=failed`, and
# list-units prints a leading status glyph that only `--plain` removes — `--no-legend` drops the
# header and footer and nothing else. Without it `awk '{print $1}'` yields the glyph, the name is
# lost to the character filter, and the body reads `failed-units=1:` with no name at all.
# Measured on the box 2026-08-10 against a real failed unit: `[●]` without the flag,
# `[jbl-probe-fail.service]` with it. The alarm still fired either way — what was inert was the
# diagnosis, which is the entire value of this predicate over the dead-man.
check_failed_units() {
  local failed count names
  failed=$(systemctl --failed --plain --no-legend --no-pager 2>/dev/null | awk '{print $1}')
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
# The expectation is derived from the rules file, never from a literal here — two homes for one
# list is how they drift. Both a missing rules file and a missing auditctl are failures, not
# passes: an empty expectation compared against an empty kernel would be the falsest green there
# is, and it is precisely what an ordering mistake, or a watch on a path that did not exist at
# load time, produces.
#
# COMPARE RULES, NOT KEYS, AND THE DIFFERENCE IS A REAL BLIND SPOT. Seven of the ten keys in the
# rules file are carried by more than one watch (`jbl-authkeys` by /root/.ssh AND
# /home/jpadmin/.ssh, `jbl-cron` by three paths, and so on). Comparing key SETS therefore reports
# green whenever at least one rule per key loaded — so a single watch silently failing to load,
# which is exactly what happens when its path does not exist yet, would be invisible to the
# predicate written to catch it. Pairs of (path, key) restore per-rule granularity. Measured
# 2026-08-10: `auditctl -l` prints watches back in the rules file's own form
# (`-w <path> -p <perms> -k <key>`), so the two sides are directly comparable.
audit_watch_pairs() {
  # stdin -> "<path>|<key>" per watch rule, deduplicated. Comment lines are stripped FIRST: this
  # file is comment-heavy by house style, and a commented-out rule — the likeliest future edit —
  # would otherwise mint a phantom expectation that can never be loaded, i.e. a permanently lit
  # alarm. The rules file's own prose already contains an `ausearch -k jbl-key-tmpfs` example.
  # `-p` is deliberately not part of the pair, and the order of -p and -k is not assumed:
  # auditd accepts `-w <path> -k <key> -p <perms>` as readily as the house form, and an earlier
  # revision of this extraction required the house order — so a rule written the other way would
  # have silently dropped out of the expectation and never been verified at all.
  grep -v '^[[:space:]]*#' |
    sed -E -n 's/^[[:space:]]*-w[[:space:]]+([^[:space:]]+)[[:space:]].*-k[[:space:]]+(jbl-[A-Za-z0-9_-]+).*$/\1|\2/p' |
    sort -u
}

check_audit_rules_loaded() {
  local expected loaded missing="" pair
  if [ ! -r "$AUDIT_RULES_FILE" ]; then
    fail_with "audit-rules-file-absent"
    return 0
  fi
  if ! command -v auditctl >/dev/null 2>&1; then
    fail_with "auditctl-absent"
    return 0
  fi
  expected=$(audit_watch_pairs <"$AUDIT_RULES_FILE" 2>/dev/null)
  if [ -z "$expected" ]; then
    fail_with "audit-rules-file-defines-no-keys"
    return 0
  fi
  loaded=$(auditctl -l 2>/dev/null | audit_watch_pairs)
  # Neither path nor key is run through sanitize_token. That function reduces UNIT NAMES to a unit
  # shape, and neither of these is unit-shaped — every one would come out as the rejection marker,
  # so the body would say how MANY rules were missing but never which, which is the whole
  # diagnostic value of this arm. They need no reduction either: both come from a root-owned file
  # we ship, and the extraction above already bounds the key to `jbl-[A-Za-z0-9_-]+`.
  while IFS= read -r pair; do
    [ -z "$pair" ] && continue
    printf '%s\n' "$loaded" | grep -qxF "$pair" ||
      missing="${missing}${missing:+,}${pair##*|}@${pair%%|*}"
  done <<<"$expected"
  [ -z "$missing" ] || fail_with "audit-rules-not-loaded=${missing}"
}

# P5 — free space where exhaustion would stop the stack. Detection only, never a quota.
#
# On this box both paths resolve to the same filesystem (measured 2026-08-10: / and
# /var/lib/docker are both /dev/vda4), so the two checks agree. They are kept separate because a
# future box may separate them, and a check that silently assumed one filesystem would then miss
# the one that fills.
#
# Fixed labels rather than sanitize_token: that function exists to reduce UNIT NAMES, and a
# filesystem path is neither personal data nor unit-shaped — running one through it produced an
# empty label for `/` and `varlibdocker` for the other, losing diagnostics for no gain.
check_disk() {
  local entry path label used free low=""
  for entry in "root:/" "docker:$DOCKER_ROOT"; do
    label="${entry%%:*}"
    path="${entry#*:}"
    [ -d "$path" ] || continue
    used=$(df --output=pcent "$path" 2>/dev/null | tail -1 | tr -cd '0-9')
    [ -z "$used" ] && continue
    free=$((100 - used))
    [ "$free" -lt "$DISK_MIN_FREE_PCT" ] &&
      low="${low}${low:+,}${label}=${free}pct"
  done
  [ -z "$low" ] || fail_with "disk-low=${low}"
}

# ------------------------------------------------------------------------------------------
# The wire. The URL is a capability: possession is authority. Rationale for choosing a credential
# shape whose theft grants nothing new is in ADR 0126; what matters here is the handling.
#
# THE URL IS PASSED ON STDIN, NOT ON THE COMMAND LINE. An argument is world-readable in
# /proc/<pid>/cmdline for the lifetime of the process, so `curl … "$url"` publishes the
# credential to every local reader. `--config -` reads it from stdin instead, which no other
# process can see. It is also never logged and never included in a diagnostic.
#
# THE RETRY BUDGET IS BOUNDED UNDER THE UNIT'S TimeoutStartSec, AND THE TWO ARE ONE DESIGN.
# `--max-time` is PER ATTEMPT, not for the whole operation, so `--max-time 20 --retry 2` can run
# 3×20 + 2×3 = 66 s — past a 60 s timeout, at which point systemd kills the unit and it lands on
# the failure list, defeating the exit contract by the clock. `--retry-max-time` bounds the whole
# retry sequence; keep the total comfortably under the unit's timeout when changing either.
post() {
  local url="$1" body="$2"
  command -v curl >/dev/null 2>&1 || {
    log "curl absent: cannot report; the expecter's grace period is the backstop"
    return 0
  }
  # Failure to reach the expecter is NOT an error here: the dead-man is precisely the mechanism
  # that covers an unreachable expecter.
  printf 'url = "%s"\n' "$url" |
    curl -fsS --config - \
      --max-time 10 --retry 2 --retry-delay 3 --retry-max-time 30 \
      -X POST -H 'Content-Type: text/plain' \
      --data-binary "$body" >/dev/null 2>&1 ||
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
