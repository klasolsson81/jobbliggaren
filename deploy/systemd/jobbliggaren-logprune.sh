#!/usr/bin/env bash
# jobbliggaren-logprune — give Docker's json-file layer the AGE bound its driver has none of.
#
# usage:  jobbliggaren-logprune.sh [--dry-run]
# stdout: nothing — every diagnostic goes to stderr, so a caller may capture stdout safely
# stderr: every diagnostic
# exit:   0 on success, non-zero on any failure
#
# WHY THIS FILE EXISTS. ADR 0024 D7 policy 1 commits app-log retention to 30 days. Its original
# mechanism (a CloudWatch LogGroup) was torn down by ADR 0066. Two of the three layers that hold
# app events today carry that commitment by their own mechanisms. Docker's `json-file` is the
# third, and it is the one with no age bound at all (#1170). What each of the other two currently
# holds is a live box state and is deliberately not asserted here — `docs/runbooks/log-sink.md`
# §4 is where that is measured, with a date.
#
# THE DRIVER HAS NO TIME AXIS, AND THAT IS A VENDOR FACT RATHER THAN AN OVERSIGHT. The whole
# option set is `max-size`, `max-file`, `labels`, `labels-regex`, `env`, `env-regex`, `compress`
# (docs.docker.com, read 2026-08-28). Removal happens only when "rolling the logs creates excess
# files", i.e. by file COUNT. So `max-size`/`max-file` bind VOLUME, and effective retention is
# budget divided by write rate — which makes it INVERSELY proportional to traffic. The quieter a
# container is, the LONGER it retains, which is exactly backwards from a storage-limitation
# guarantee and is why no volume number can be translated into an age.
#
# WHAT THIS SCRIPT DOES: deletes ROTATED segments (`*-json.log.N`) whose NEWEST line is older
# than the retention window, for every container whose json-file layer can hold data the window
# governs — see the set below for why that is not the same as the app stream.
#
# ⚠ WHAT IT DELIBERATELY DOES NOT DO, AND THE RESIDUAL IS NAMED RATHER THAN IMPLIED. It never
# touches the LIVE segment (`*-json.log`). So the commitment this mechanism can honestly make is
# not "no app-log data older than 30 days" but:
#
#     no app-log data older than 30 days, EXCEPT at most one non-rotated live segment per
#     container, itself capped by `max-size` (10 MB).
#
# That residual is not hypothetical — it is where the PROJECTED breach lives, and the word is
# exact: nothing on this box has yet held app-log data past 30 days, because no app container has
# lived that long. What was measured on 2026-08-28 is the ABSENCE OF A BOUND, not an exceeded
# one — `web` had written 5 lines, ALL at container start, and `caddy` 22 lines within 10 seconds
# of start; neither has ever rotated, so neither has a single segment this script is allowed to
# touch. For those two the residual IS the finding, and #1170 stays open on
# it. Closing it needs either a log-driver change (an ADR 0128 Streams-table decision) or
# truncation of a file the daemon holds open.
#
# WHY NOT TRUNCATE THE LIVE SEGMENT — the alternative was measured and REJECTED (CTO 2026-08-28),
# on a coupling rather than on taste. Docker owns rotation of that file. `jobbliggaren-logship.sh`
# reads the same stream with `docker logs --timestamps --since` and DIES when it exits non-zero
# (that script's `app_rc` guard), withholding its stamp — which latches `logship-fresh`. A
# retention mechanism able to fell the off-box archive's freshness signal is precisely the
# cross-coupling ADR 0128 split #1175 to avoid. The local buffer is the cheap copy; the archive
# is not.
#
# WHY NOT THE journald DRIVER. It would supply a real age ceiling, but `MaxRetentionSec=` is
# host-global: it would bound sshd and auditd too, re-making the defect
# `journald-jobbliggaren-retention.conf` already records making once (an earlier revision set
# `MaxRetentionSec=30day` and SHORTENED the evidence window it exists to secure). It also removes
# the app stream from `docker logs`, which is how logship reads it.
#
# WHY NOT logrotate `maxage`. It acts on already-rotated files, so against the two containers
# whose bound is missing — which have no rotated files — it is INERT, not merely risky. Anything
# it would add lives in its `copytruncate` half, which is the unsupported half, and it puts two
# rotation owners on one file set.
#
# THE WINDOW IS NOT A NEW NUMBER. It is ADR 0024 D7 policy 1's, which ties it to the Art. 17
# restore window (D5/D6). A number chosen here would give one personal datum a different
# retention number in each layer that holds it.
#
# ⚠ The constant below is a DECIDED value, not a measured one, which is why it belongs in code.
# What deliberately does NOT appear here is any claim about what the OTHER layers currently hold
# — Seq's policy is a live box state, and a tracked file asserting its duration is the decay
# class `.github/scripts/seq-retention-duration-guard.sh` was written against (#1170). If the
# commitment moves, it moves in ADR 0024 D7 and every layer follows from there.
set -euo pipefail

log() { printf '%s\n' "$*" >&2; }
die() { log "REFUSING: $*"; exit 1; }

# Parity, not a new choice — see the header. Changing it is an ADR 0024 D7 amendment.
readonly RETENTION_DAYS=30

# ⚠ THIS IS NOT `jobbliggaren-logship.sh`'s ARRAY, AND THE DIFFERENCE IS THE WHOLE POINT.
# That one carries ADR 0128 §2's Streams table — FOUR containers — and answers *which stream is
# archived*, whose exclusion reason is operability: `postgres` and `redis` were left out as
# carrying "connection and authentication traces rather than app events". This array answers a
# different question — *which local log surface is bound by the retention window* — and that one
# is keyed to PERSONAL DATA, not to app events. The two sets are not the same set, and reusing
# the app stream here would silently swap the criterion (security-auditor, 2026-08-28).
#
# WHY THE OPERABILITY CRITERION CANNOT BE BORROWED — measured on the box 2026-08-28, not assumed.
# A container excluded for not emitting *app events* can still hold personal data the window
# governs: postgres logged constraint KEY VALUES, not merely column names, read off this box's
# own log rather than off the documentation. ⚠ The specifics are NOT restated here — they live
# in log-sink.md §4. Both that this comment used to carry were falsified within six days: that
# postgres ran with no `log_*` override (this repo set one), and which key was the bearer (the
# `(UserId, OrganizationNumber)` index stores an HMAC token, ADR 0090 D5).
# The conclusion survives both; the restatement did not.
#
# EXPANDING THIS ARRAY IS NOT A CHANGE TO ADR 0128's TABLE (CTO 2026-08-28). logship's note that
# adding a name is a Streams-table change is true OF LOGSHIP, which adds a stream, an archive
# object and a personal-data flow. This script is SUBTRACTIVE: it ships nothing, reads nothing
# out, and creates no object, so it cannot widen exposure and changes no row in that table.
#
# NOR IS IT A FOURTH RETENTION NUMBER. The window follows the DATA, not the container: D7
# policy 1 derives it from the Art. 17 restore window (D5/D6), and a row bearing a user id is
# governed by that window wherever it is written. A separate number for these five would be the
# fourth; the same number is the rule.
#
# THE SET IS THE COMMITMENT'S, NOT TODAY'S MEASUREMENT'S. Some of these sit inside the window
# today and some do not, but every one of those margins is a WRITE RATE, and a write rate is not
# a bound — a busy container's falls toward zero the moment its work stops. A scope tracking
# today's rates would carry an expiry date; this one does not. The per-container figures are
# deliberately not repeated here: they decay, and ADR 0024's Amendment 2026-08-28 rejects that
# form explicitly.
#
# ⚠ ONE COST, NAMED RATHER THAN DISCOVERED: `seq`'s container log is the only place an ingestion
# refusal appears (ADR 0128 §2), and it is age-bounded here like everything else.
readonly -a RETENTION_BOUND_CONTAINERS=(
  jobbliggaren-api
  jobbliggaren-worker
  jobbliggaren-web
  jobbliggaren-caddy
  jobbliggaren-postgres
  jobbliggaren-redis
  jobbliggaren-seq
  jobbliggaren-migrate
  jobbliggaren-migrate-rewrap
)

DRY_RUN=0
case "${1:-}" in
  --dry-run) DRY_RUN=1 ;;
  "") ;;
  *) die "usage: $0 [--dry-run] (got '$1')" ;;
esac
readonly DRY_RUN

command -v /usr/bin/docker >/dev/null 2>&1 || die "/usr/bin/docker is not present"

# ⚠ THE BINARY IS NOT THE DAEMON, AND THE DIFFERENCE IS A SILENT NO-OP.
# `docker inspect` against a down daemon fails exactly like a container that does not exist, and
# the loop below is written to treat a missing container as a normal skip. Without this probe the
# whole pass reports a skip per container and `pruned=0` and exits 0 — BYTE-IDENTICAL to a healthy run on a
# box with nothing to prune, which is this box's normal output today. A retention mechanism that
# fails silently is the defect this unit exists to repair, so the failure has to be loud enough to
# reach `systemctl --failed`, which is the surface the heartbeat's P1 reads.
#
# The unit also carries `After=docker.service`, but ordering alone is not enough: it does not hold
# for a hand run, and it does not survive a daemon that is ordered-up but not yet accepting.
/usr/bin/docker version --format '{{.Server.Version}}' >/dev/null 2>&1 ||
  die "the docker daemon is not reachable — refusing to report a vacuous pruned=0"

# The cutoff is computed ONCE. Computing it per file would let a long run straddle a second
# boundary and apply two different windows inside one pass.
cutoff_epoch=$(date -u -d "${RETENTION_DAYS} days ago" +%s) ||
  die "could not compute the cutoff (needs GNU date)"
readonly cutoff_epoch
log "logprune: window=${RETENTION_DAYS}d cutoff=$(date -u -d "@${cutoff_epoch}" +%Y-%m-%dT%H:%M:%SZ) dry_run=${DRY_RUN}"

# Read the newest line's docker timestamp out of a rotated segment.
#
# WHY THE UNESCAPED KEY IS SAFE TO MATCH. A json-file line is
# `{"log":"…","stream":"…","time":"…"}` and the payload is JSON-ESCAPED, so a `"time":"` that
# appears inside logged text arrives as `\"time\":\"`. The unescaped `,"time":"` therefore occurs
# exactly once per line, as the real key — which is what makes a regex adequate here and a JSON
# parser (and a python3 dependency in a unit that runs as root) unnecessary.
newest_timestamp() {
  local file=$1 line ts
  line=$(tail -n 1 "$file" 2>/dev/null) || return 1
  [[ -n "$line" ]] || return 1
  ts=$(printf '%s' "$line" | sed -n 's/.*,"time":"\([^"]*\)".*/\1/p')
  [[ -n "$ts" ]] || return 1
  printf '%s' "$ts"
}

pruned=0
kept=0
skipped=0

for container in "${RETENTION_BOUND_CONTAINERS[@]}"; do
  # A container that is not running is not an error. Reconcile recreates these on every image
  # change, so a pass landing inside that window must not fail the unit.
  if ! id=$(/usr/bin/docker inspect -f '{{.Id}}' "$container" 2>/dev/null); then
    log "  ${container}: no such container — skipping"
    continue
  fi

  # THE ID SHAPE IS VALIDATED BEFORE IT REACHES A PATH. Everything below builds a filesystem
  # path from this value and then deletes inside it, running as root. A 64-hex id cannot carry
  # `..`, a slash, or a glob character, so validating the shape is what keeps the deletion
  # confined to one container's own directory — the check is the boundary, not the path string.
  if [[ ! "$id" =~ ^[0-9a-f]{64}$ ]]; then
    die "${container}: docker returned an id that is not 64 hex characters — refusing to build a path from it"
  fi

  dir="/var/lib/docker/containers/${id}"
  [[ -d "$dir" ]] || { log "  ${container}: ${dir} is absent — skipping"; continue; }

  # ⚠ THE GLOB IS THE SAFETY PROPERTY. `*-json.log.[0-9]*` matches the ROTATED segments and
  # cannot match the live `*-json.log`, which carries no suffix. The live file is what
  # `docker logs` and therefore logship read; deleting it is the failure mode this whole script
  # is written around. Do not relax this pattern.
  shopt -s nullglob
  segments=("${dir}/${id}-json.log."[0-9]*)
  shopt -u nullglob

  if [[ ${#segments[@]} -eq 0 ]]; then
    log "  ${container}: no rotated segments — nothing this script may act on"
    continue
  fi

  for seg in "${segments[@]}"; do
    if ! ts=$(newest_timestamp "$seg"); then
      # An unreadable or empty segment is REPORTED and KEPT. Deleting on a failed read would
      # make every parse bug a data-loss bug, and this script's whole subject is a corpus.
      log "  $(basename "$seg"): could not read a timestamp — KEEPING"
      skipped=$((skipped + 1))
      continue
    fi

    if ! seg_epoch=$(date -u -d "$ts" +%s 2>/dev/null); then
      log "  $(basename "$seg"): unparsable timestamp '${ts}' — KEEPING"
      skipped=$((skipped + 1))
      continue
    fi

    # NEWEST line, not oldest: a segment is removable only when EVERY line in it has aged out.
    # Anchoring on the oldest line would delete entries still inside the window.
    if [[ "$seg_epoch" -lt "$cutoff_epoch" ]]; then
      if [[ "$DRY_RUN" -eq 1 ]]; then
        log "  $(basename "$seg"): newest=${ts} — WOULD PRUNE"
      else
        rm -f -- "$seg" || die "failed to remove ${seg}"
        log "  $(basename "$seg"): newest=${ts} — pruned"
      fi
      pruned=$((pruned + 1))
    else
      kept=$((kept + 1))
    fi
  done
done

log "logprune: pruned=${pruned} kept=${kept} unreadable=${skipped}"
exit 0
