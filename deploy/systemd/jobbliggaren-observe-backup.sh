#!/usr/bin/env bash
#
# The backup collector (ADR 0157): publishes backup.json for the admin overview's Backup card.
#
# It reports two facts and judges neither. Whether a stamp is too old, in the future or too far
# from its own start is the API's call, made against the API's clock; this script only says what
# it found. It reads no backup content, no key and no credential: one file's mtime and first
# line, and one `systemctl show` of the timer.
#
#   lastSuccess  the stamp that jobbliggaren-backup.sh writes LAST on its success path.
#                  completedAt = the stamp's mtime = the end of the run, the same instant its
#                                own `--check` measures the 26 h threshold from.
#                  startedAt   = the stamp's content = the run's start, UTC.
#   timer        jobbliggaren-backup.timer as systemd holds it now.
#                  nextRunAt   = NextElapseUSecRealtime: the instant the timer is armed for,
#                                RandomizedDelaySec included (measured on this box, 2026-10-10).
#
# EXIT 0 ON EVERY PATH. This unit is read by nobody but the card, and the card already shows a
# sampler that stopped as an old observation. A non-zero exit would put a cosmetic unit on
# `systemctl --failed`, the one alarm surface the heartbeat pages from (jobbliggaren-heartbeat.sh),
# once a minute for as long as the cause lasts.

set -euo pipefail

# KEEP IN SYNC with STAMP_FILE in jobbliggaren-backup.sh. ObserveUnitFilePinTests compares them.
readonly STAMP_FILE=/var/lib/jobbliggaren/last-successful-backup
readonly TIMER_UNIT=jobbliggaren-backup.timer
# Well under the unit's TimeoutStartSec: a hung bus must end as "unknown", not as a killed unit.
readonly SYSTEMCTL_TIMEOUT_SECONDS=5

# shellcheck source=jobbliggaren-observe-lib.sh
source "${BASH_SOURCE[0]%/*}/jobbliggaren-observe-lib.sh"

# Prints the lastSuccess fragment. Every branch prints; none relies on `set -e`.
last_success() {
  local completed line y mo d h mi s started
  local invalid='"lastSuccess":{"state":"invalid"}'

  if [[ -L "$STAMP_FILE" ]] || { [[ -e "$STAMP_FILE" ]] && [[ ! -f "$STAMP_FILE" ]]; }; then
    printf '%s' "$invalid"; return 0
  fi
  if [[ ! -e "$STAMP_FILE" ]]; then
    printf '%s' '"lastSuccess":{"state":"missing"}'; return 0
  fi

  completed=$(stat -c '%Y' -- "$STAMP_FILE" 2>/dev/null) || completed=""
  line=$(head -c 64 -- "$STAMP_FILE" 2>/dev/null | head -n 1) || line=""
  if [[ ! "$completed" =~ ^[0-9]+$ ]] || ! head -c 1 -- "$STAMP_FILE" >/dev/null 2>&1; then
    printf '%s' '"lastSuccess":{"state":"unreadable"}'; return 0
  fi

  # The content is the run start in the exact form the backup script writes it, and `date` is
  # strict about the rest: month 13, 24:00:00 and 30 February are refused rather than normalised.
  if [[ ! "$line" =~ ^([0-9]{4})([0-9]{2})([0-9]{2})T([0-9]{2})([0-9]{2})([0-9]{2})Z$ ]]; then
    printf '%s' "$invalid"; return 0
  fi
  y=${BASH_REMATCH[1]} mo=${BASH_REMATCH[2]} d=${BASH_REMATCH[3]}
  h=${BASH_REMATCH[4]} mi=${BASH_REMATCH[5]} s=${BASH_REMATCH[6]}
  started=$(date -u -d "${y}-${mo}-${d} ${h}:${mi}:${s} UTC" +%s 2>/dev/null) || { printf '%s' "$invalid"; return 0; }

  printf '"lastSuccess":{"state":"recorded","completedAt":"%s","startedAt":"%s"}' \
    "$(observe_iso "$completed")" "$(observe_iso "$started")"
}

# Prints the timer fragment. TZ=UTC makes systemd print the instant as `Sat 2026-10-10 13:49:41 UTC`
# whatever the box's zone is (Europe/Berlin today), so the parse below has one shape to accept.
# `systemctl show` ignores --timestamp, which was measured; the environment is the lever.
timer_state() {
  local out key value load="" active="" next="" epoch
  local unknown='"timer":{"state":"unknown"}'

  command -v systemctl >/dev/null 2>&1 || { printf '%s' "$unknown"; return 0; }
  out=$(TZ=UTC LC_ALL=C timeout "$SYSTEMCTL_TIMEOUT_SECONDS" systemctl show "$TIMER_UNIT" -p LoadState -p ActiveState -p NextElapseUSecRealtime 2>/dev/null) \
    || { printf '%s' "$unknown"; return 0; }

  while IFS='=' read -r key value; do
    case "$key" in
      LoadState) load="$value" ;;
      ActiveState) active="$value" ;;
      NextElapseUSecRealtime) next="$value" ;;
    esac
  done <<< "$out"

  case "$load" in
    not-found) printf '%s' '"timer":{"state":"notInstalled"}'; return 0 ;;
    loaded | masked) ;;
    *) printf '%s' "$unknown"; return 0 ;;
  esac

  if [[ "$active" == "active" && "$next" =~ ^[A-Z][a-z]{2}\ ([0-9]{4}-[0-9]{2}-[0-9]{2}\ [0-9]{2}:[0-9]{2}:[0-9]{2})\ UTC$ ]]; then
    epoch=$(date -u -d "${BASH_REMATCH[1]} UTC" +%s 2>/dev/null) || { printf '%s' "$unknown"; return 0; }
    printf '"timer":{"state":"scheduled","nextRunAt":"%s"}' "$(observe_iso "$epoch")"
    return 0
  fi
  if [[ "$active" == "inactive" || "$active" == "failed" ]] && [[ -z "$next" ]]; then
    printf '%s' '"timer":{"state":"inactive"}'; return 0
  fi
  printf '%s' "$unknown"
}

main() {
  local last timer
  if last=$(last_success) && timer=$(timer_state); then
    observe_publish backup "\"data\":{${last},${timer}}"
  else
    observe_publish_error backup collector-failed
  fi
}

main || observe_log "backup.json was not published; the card will show the previous sample as old"
exit 0
