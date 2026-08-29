#!/usr/bin/env bash
# jobbliggaren-logship — hourly encrypted off-box archive of this box's forensic log streams,
# and (--check) detection that shipping has stopped.
#
# #1175 (A). senior-cto-advisor bind 2026-08-10: #1175 carries two obligations and asked for one
# mechanism, so it is split. (A) is the DURABLE CORPUS — append-only, encrypted, age-bounded,
# demonstrable. (B) is the queryable Seq sink, whose host is Klas's and which is deferred. This
# file is (A) and nothing else; it is not a log sink and must not be described as one.
#
# WHAT THIS EXISTS TO FIX. ADR 0126 delivered detection but left its own residual in writing:
# "The forensic corpus stays local and root-erasable until #1175 lands." journald's retention
# drop-in names the same successor. Two obligations meet here:
#   · Art. 5(1)(e) — #1170. The journal and the audit log are bounded by SIZE and by nothing else,
#     so neither has an age bound today. A lifecycle rule on the target supplies one.
#   · The root-survivable copy M-7 deferred — see the next paragraph, which is the honest limit.
#
# THE ROOT-SURVIVAL PROPERTY IS NOT IN FORCE YET, AND THIS SCRIPT MUST NOT BE READ AS DELIVERING
# IT. Verification row 27d (vps-deploy-stack.md) was measured FALSE 2026-08-09: the upload
# credential CAN delete — `delete-object` succeeded against the live container. Until an OVH user
# policy with an explicit `Deny s3:DeleteObject` is applied (owner: Klas, first-real-data gate),
# an attacker holding this box's credential deletes the off-box copy too. The archive is
# therefore a durable corpus against ACCIDENT and against a lower-privilege attacker, and a
# DOCUMENTED-BUT-NOT-IN-FORCE one against the ADR 0123 root attacker. Same discipline the backup
# script applies to the same credential, and for the same reason.
#
# THE BOX HOLDS NO PRIVATE KEY. age encrypts to a public recipient, exactly as #197 does, so the
# target holds no readable personal data and OVH's register row does not change class — only its
# categories do. Nothing here can decrypt what it just uploaded, which means this script cannot
# verify its own output is readable; that is proven by the drill in the runbook and by nothing in
# this file.
#
# THREE LEGS. The third is the app stream, and its lifetime is a WHERE and not an IF — see the
# sunset condition written at that leg. An earlier draft of this file omitted it and argued the
# omission; the bind's re-check on 2026-08-11 falsified the load-bearing half of that argument.
#
# NO NEW RESIDENT DAEMON, AND THAT IS LOAD-BEARING RATHER THAN INCIDENTAL. A timer plus a script,
# the same shape as #197 and #198. ADR 0126 refused a 100–200 MB resident agent on this box's
# margin; a design that needed one would have to re-argue that refusal. It does not: `journalctl`,
# `age`, `rclone` and `gzip` are already installed and are invoked, not resident.
set -euo pipefail

# ── Shared literals ──────────────────────────────────────────────────────────────────────────
# KEEP IN SYNC WITH `jobbliggaren-backup.sh`. These three are DUPLICATED ON PURPOSE rather than
# extracted into a common source file. Extracting would edit a delivered #197 script for a DRY
# gain over plumbing rather than over a knowledge piece — and DRY is about knowledge, not about
# text that looks alike. This repo has already made that call twice in this directory, in
# jobbliggaren-backup.sh's PG_* block and jobbliggaren-reconcile.sh's UPSTREAM_ALLOWLIST, and
# both say so in comments. All three fail loudly at run time, so this is a maintenance note and
# not a guard.
readonly HOST_SECRETS_DIR=/run/jobbliggaren/host-secrets
readonly CREDENTIAL_FILE="${HOST_SECRETS_DIR}/Backup__RcloneConfigBase64"
readonly RECIPIENT_FILE=/opt/jobbliggaren/deploy/backup/age.recipient
readonly LOGSHIP_REMOTE="jbl-backup:jobbliggaren-backups"

# The new prefix. Its OWN lifecycle rule, separate from main/ and deks/ — the retention question
# for logs is a different question from the one K4 answered for database artefacts, and giving
# them one rule would silently bind the two numbers together.
readonly REMOTE_PREFIX="hostlogs"

readonly STATE_DIR=/var/lib/jobbliggaren
readonly STAMP_FILE="${STATE_DIR}/last-successful-logship"
readonly JOURNAL_CURSOR_FILE="${STATE_DIR}/logship.journal-cursor"
readonly AUDIT_STATE_FILE="${STATE_DIR}/logship.audit-offset"
readonly LOCK_FILE=/var/lock/jobbliggaren-logship.lock

readonly AUDIT_LOG=/var/log/audit/audit.log

# The app leg's window floor. Parity with jobbliggaren-logprune.sh, both following ADR 0024 D7
# policy 1 — this file does not decide the number and must not derive it. Changing it is a D7
# amendment, not an edit here.
readonly RETENTION_DAYS=30

# Freshness threshold for --check. The timer runs hourly; 150 min tolerates one entirely missed
# run plus jitter before the box's alarm surface lights — jobbliggaren-logship-fresh.timer is
# what lights it. The pair was enabled 2026-08-18, but this threshold is still UNREAD: the probe
# SKIPS on its ConditionPathExists until #197's credential exists, so enable was necessary rather
# than sufficient. Same wording as jobbliggaren-logship.timer's, deliberately. Set to
# the period it would alarm on ordinary lateness, which is the mistake
# jobbliggaren-backup-fresh.timer's 26 h note records.
readonly MAX_AGE_SECONDS=9000

log() { printf 'logship: %s\n' "$*"; }
die() { printf 'logship: %s\n' "$*" >&2; exit 1; }

# ── --check: the freshness probe ─────────────────────────────────────────────────────────────
#
# Lives here rather than in a separate unit so the stamp path has exactly one home. It stats one
# file and exits; it neither ships nor touches the credential, which is why it can run on a box
# where the credential is absent.
#
# WHAT IT DOES NOT SEE, NAMED: this script writes the stamp, so the probe measures "a run
# reported success", never "an artefact is readable at the target". An upload that lands
# corrupt is invisible here and is caught only by the drill. Same limit, same wording, as
# jobbliggaren-backup.sh's --check.
if [[ "${1:-}" == "--check" ]]; then
  [[ $# -eq 1 ]] || die "unknown argument '$2' (use --check on its own)"

  [[ -f "$STAMP_FILE" ]] || die "no logship stamp at ${STAMP_FILE}: shipping has never succeeded"

  stamp_mtime=$(stat -c '%Y' "$STAMP_FILE" 2>/dev/null) \
    || die "could not stat ${STAMP_FILE}"
  now=$(date -u +%s)
  age_seconds=$((now - stamp_mtime))

  # A stamp from the FUTURE is not fresh — it is a broken clock or a tampered file, and reporting
  # it as fresh would let either hide a stopped archive. Same arm, same reason, as the backup
  # probe's.
  if (( age_seconds < 0 )); then
    die "logship stamp is dated in the future (${age_seconds}s): clock or tampering, not freshness"
  fi
  if (( age_seconds > MAX_AGE_SECONDS )); then
    die "last successful logship was ${age_seconds}s ago, over the ${MAX_AGE_SECONDS}s threshold"
  fi

  log "last successful logship ${age_seconds}s ago, within ${MAX_AGE_SECONDS}s"
  exit 0
fi

[[ $# -eq 0 ]] || die "unknown argument '$1' (use no arguments to ship, or --check)"

# ── Preconditions ────────────────────────────────────────────────────────────────────────────
for tool in journalctl gzip age rclone flock base64 install; do
  command -v "$tool" >/dev/null 2>&1 || die "required tool '${tool}' is not on PATH"
done

[[ -r "$CREDENTIAL_FILE" ]] \
  || die "upload credential not readable at ${CREDENTIAL_FILE}. It lives on tmpfs and is gone
after every boot until an operator re-injects it — that is this box's operating model, not a
fault. The unit's ConditionPathExists should have skipped this run; if you are seeing this
message the condition and this check have drifted apart."

[[ -r "$RECIPIENT_FILE" ]] || die "age recipient not readable at ${RECIPIENT_FILE}"
recipient=$(grep -v '^[[:space:]]*#' "$RECIPIENT_FILE" | grep -v '^[[:space:]]*$' | head -1)
[[ -n "$recipient" ]] || die "age recipient file ${RECIPIENT_FILE} holds no recipient line"

# Refuse a PRIVATE key in the recipient file outright. The file is named "recipient" and is
# tracked in git; a private key landing here would be committed. #197 pins the same invariant in
# a test, and the runtime arm costs one line.
case "$recipient" in
  AGE-SECRET-KEY-*) die "${RECIPIENT_FILE} contains a PRIVATE key, not a recipient. Refusing." ;;
esac

install -d -m 0755 "$STATE_DIR"

# ── The lock ─────────────────────────────────────────────────────────────────────────────────
# One process for the whole run. The cursor file and the audit offset are both read-modify-write
# state; two overlapping runs would each advance them and each ship a hole.
exec 9>"$LOCK_FILE"
flock -n 9 || die "another jobbliggaren-logship run holds ${LOCK_FILE}"

run_stamp=$(date -u +%Y%m%dT%H%M%SZ)
readonly run_stamp
# The run's START, kept because the stamp's mtime is what the app leg anchors its next window on.
# Stamping the finish time would open a hole the length of the upload: entries written while this
# run was uploading fall after the previous read and before the recorded time, so no artefact
# would ever carry them.
run_epoch=$(date -u +%s)
readonly run_epoch

# Working state on tmpfs. The rclone config is a credential and must not touch persistent disk;
# gate B-1 forbids a disk swap file for the same class of reason, so materialising a credential
# on /var would be the same mistake by another route.
WORKDIR=$(mktemp -d /dev/shm/logship.XXXXXX)
readonly WORKDIR
chmod 0700 "$WORKDIR"
cleanup() { rm -rf "$WORKDIR"; }
trap cleanup EXIT

rclone_config="${WORKDIR}/rclone.conf"
install -m 0600 /dev/null "$rclone_config"
base64 -d < "$CREDENTIAL_FILE" > "$rclone_config" \
  || die "the upload credential is not valid base64 (expected the base64 of a complete rclone
config file, the same artefact jobbliggaren-backup.sh consumes)"
[[ -s "$rclone_config" ]] || die "the decoded rclone config is empty"

# ONE MECHANISM, NOT TWO. No `export RCLONE_CONFIG` beside this array: it would silently rescue a
# future rclone call that forgot the flags, turning the flags into a guarantee that does not hold.
# Copied deliberately from jobbliggaren-backup.sh, whose comment argues the same point.
readonly RCLONE_FLAGS=(--config "$rclone_config" --log-level NOTICE --retries 3)

shipped=0

# ship <local-file> <remote-basename>
#
# gzip → age → rclone, as one pipeline with no plaintext intermediate on disk beyond the caller's
# own extract, which lives on tmpfs. PIPESTATUS is read rather than trusting the last exit code:
# under `pipefail` a failing gzip would surface, but the STAGE is what an operator needs and the
# aggregate does not name it.
ship() {
  local local_file="$1" remote_basename="$2" object rc
  object="${LOGSHIP_REMOTE}/${REMOTE_PREFIX}/${remote_basename}"

  # The pipeline runs inside `if !` so `errexit` does not fell the shell at the pipe itself —
  # as a bare statement it would, and everything below here would be unreachable.
  if ! { gzip -c "$local_file" \
    | age -r "$recipient" \
    | rclone rcat "${RCLONE_FLAGS[@]}" "$object"; }; then
    rc=("${PIPESTATUS[@]}")
    die "shipping ${remote_basename} failed (gzip=${rc[0]} age=${rc[1]} rclone=${rc[2]}).
No stamp is written and no cursor is advanced, so the next run re-ships this window."
  fi

  log "shipped ${remote_basename}"
  shipped=$((shipped + 1))
}

# ── Stream 1: the host journal ───────────────────────────────────────────────────────────────
#
# `--cursor-file` READS the cursor and WRITES the new one in a single invocation, and systemd
# owns that atomicity. The alternative an earlier draft reached for — `--since "1 hour ago"` —
# is the trap named in the bind: a wall-clock window drops entries that land across a boundary
# and double-ships across a restart. Requires systemd ≥ 242; the box runs 257 (measured
# 2026-08-11).
#
# THE CURSOR IS ONLY ADVANCED IF THE SHIP SUCCEEDS, and that ordering is the whole correctness
# argument. journalctl writes the cursor file as a side effect of reading, so the extract is
# taken against a COPY of the cursor and the real file is moved into place afterwards. Without
# this, a failed upload would still have advanced the cursor and the window would be lost with
# no error anywhere.
journal_extract="${WORKDIR}/journal-${run_stamp}.export"
journal_cursor_new="${WORKDIR}/journal-cursor.new"

if [[ -f "$JOURNAL_CURSOR_FILE" ]]; then
  cp "$JOURNAL_CURSOR_FILE" "$journal_cursor_new"
fi

# `-o export` is the serialisation format journald itself defines for transfer: it preserves every
# field, including the ones `-o short` drops. `--no-pager` because a pager under systemd would
# hang the unit until TimeoutStartSec.
# An empty window and a FAILED read must not converge on the same branch: both leave the extract
# empty, and the empty-window branch promotes the cursor and writes the stamp. A permanently
# broken journal leg would then be indistinguishable from a quiet box on every surface this
# mechanism exists to feed.
journal_rc=0
journalctl --cursor-file="$journal_cursor_new" --no-pager -o export > "$journal_extract" \
  || journal_rc=$?
[[ "$journal_rc" -eq 0 ]] || die "journalctl exited ${journal_rc}. No cursor is promoted and no
stamp is written, so the next run re-reads this window."

if [[ -s "$journal_extract" ]]; then
  ship "$journal_extract" "journal-${run_stamp}.export.gz.age"
  mv "$journal_cursor_new" "$JOURNAL_CURSOR_FILE"
  chmod 0600 "$JOURNAL_CURSOR_FILE"
else
  # An empty window is normal on a quiet box and is not a failure. The cursor is still advanced —
  # there is nothing to lose by doing so and a stuck cursor would re-read the same empty window
  # forever. journalctl wrote it already; only the promotion is ours.
  [[ -f "$journal_cursor_new" ]] && mv "$journal_cursor_new" "$JOURNAL_CURSOR_FILE"
  log "journal: no new entries since the last cursor"
fi

# ── Stream 2: the audit log ──────────────────────────────────────────────────────────────────
#
# auditd writes to its own file, NOT to the journal (measured 2026-08-10), so it needs its own
# leg. ADR 0126 Decision 4 left its retention "bounded by size and by nothing else" and escalated
# the number to Klas; this leg is what turns that open escalation into a value someone must type
# into a lifecycle rule.
#
# THERE IS NO CURSOR HERE, SO ROTATION HAS TO BE DETECTED RATHER THAN ASSUMED AWAY. The state is
# (inode, offset). auditd rotates at max_log_file=8 MB keeping num_logs=5 — both read from
# host-detection.md §7, which dates them, rather than repeated here, because one measurement with
# two homes gets two dates the first time either is re-measured. HOW OFTEN that is depends on the
# audit write rate, and that rate has no measured home: `ls -l /var/log/audit/` across two days
# is the instrument. Rotation frequency is not what this state machine turns on, though — only
# that rotation happens at all, since a silently-dropped one is exactly the hole an attacker's
# window would fall into.
#
# RESIDUAL, NAMED RATHER THAN SOLVED: if the log rotates TWICE between two runs, the middle file
# is never shipped. At 8 MB per rotation against an hourly cadence that is not reachable at any
# write rate this box has produced, but it is a real hole and the drill in the runbook is what
# would surface it.
audit_prev_inode=""
audit_prev_offset=0
if [[ -f "$AUDIT_STATE_FILE" ]]; then
  # Format: "<inode> <offset>". A malformed line resets to a full ship rather than guessing —
  # re-shipping a window is redundant, skipping one is a hole.
  read -r audit_prev_inode audit_prev_offset < "$AUDIT_STATE_FILE" || true
  [[ "$audit_prev_offset" =~ ^[0-9]+$ ]] || { audit_prev_inode=""; audit_prev_offset=0; }
fi

if [[ -r "$AUDIT_LOG" ]]; then
  audit_inode=$(stat -c '%i' "$AUDIT_LOG")
  audit_size=$(stat -c '%s' "$AUDIT_LOG")
  audit_extract="${WORKDIR}/audit-${run_stamp}.log"

  if [[ "$audit_inode" != "$audit_prev_inode" || "$audit_size" -lt "$audit_prev_offset" ]]; then
    # Rotation, or a first run. Ship the just-rotated file whole if it is there — it holds the
    # tail of the previous window and nothing else will ever carry it — then the current file
    # from its start. Truncation-in-place (size < offset, same inode) is treated identically:
    # whatever the cause, reading from the stale offset would ship garbage or nothing.
    if [[ -n "$audit_prev_inode" && -r "${AUDIT_LOG}.1" ]]; then
      cp "${AUDIT_LOG}.1" "${WORKDIR}/audit-${run_stamp}-rotated.log"
      ship "${WORKDIR}/audit-${run_stamp}-rotated.log" "audit-${run_stamp}-rotated.log.gz.age"
    fi
    audit_prev_offset=0
    log "audit: rotation or first run detected; shipping from offset 0"
  fi

  if [[ "$audit_size" -gt "$audit_prev_offset" ]]; then
    # `tail -c +N` is 1-indexed on BYTES, so the first unshipped byte is offset+1.
    tail -c "+$((audit_prev_offset + 1))" "$AUDIT_LOG" > "$audit_extract"
    ship "$audit_extract" "audit-${run_stamp}.log.gz.age"
    printf '%s %s\n' "$audit_inode" "$audit_size" > "$AUDIT_STATE_FILE"
    chmod 0600 "$AUDIT_STATE_FILE"
  else
    log "audit: no new bytes since offset ${audit_prev_offset}"
    printf '%s %s\n' "$audit_inode" "$audit_size" > "$AUDIT_STATE_FILE"
    chmod 0600 "$AUDIT_STATE_FILE"
  fi
else
  # Not fatal. auditd is #1201's artefact and this script does not own its presence; a box without
  # it still has a journal worth archiving. It IS worth a line in the journal, because silence
  # here would read as "nothing to ship".
  log "audit: ${AUDIT_LOG} is not readable; skipping that leg"
fi

# ── Stream 3: the application containers ─────────────────────────────────────────────────────
#
# THIS LEG IS PERMANENT. IT IS NOT AN INTERIM, AND (B) LANDING DOES NOT RETIRE IT.
#
# The bind originally carried a sunset rule — drop this leg once the Seq sink (B) exists, on an
# Art. 5(1)(c) argument against holding two off-box stores of the same personal data. That rule
# was withdrawn on 2026-08-11 when (B) was bound to the PRODUCTION BOX: a box-local Seq is
# not a second OFF-BOX store, so the minimisation argument never engages, and acting on the
# withdrawn rule would delete the only off-box copy of the app stream rather than de-duplicate
# anything.
#
# Do not restore the unconditional form. Seq and this archive hold the same events for different
# purposes: Seq is queryable and root-erasable; this is neither.
#
# WALL-CLOCK, AND THAT IS A REAL WEAKNESS RATHER THAN A CHOICE. Docker exposes no cursor, so this
# leg cannot have the property `--cursor-file` gives the journal leg. It is anchored to the
# PREVIOUS RUN'S STAMP, floored at the window (see the anchor below), rather than to a fixed
# "1 hour ago", which handles a missed run correctly (a three-hour gap ships three hours). Two
# residuals stay, named:
#   · entries written between the stamp and the read can be shipped twice;
#   · a container restarting mid-window can interleave such that ordering is not preserved.
# Duplicates in a forensic archive are benign; a hole is not. If this leg becomes permanent (see
# above), replacing it with a journald logging driver is the repair — and it is NOT free:
# ADR 0126 Decision 4 pinned
# `SystemKeepFree=2G` to widen the journal's evidence window, and app volume competes with it.
#
# THIS IS THE ONLY LEG CARRYING DATA-SUBJECT PERSONAL DATA. The journal carries the operator's
# own source addresses; the audit log carries uid/exe/path. This one carries users. That is why
# the register's OVH row gains a category alongside this change — in the register itself, which is
# gitignored and therefore not in this diff, so do not look for it here — and why the retention number on the
# `hostlogs/` prefix is not a housekeeping detail.
#
# This leg is built for the corpus it will carry, not the one it carries today: the product
# tables are empty until the registration gate opens, so early artefacts are near-empty. That is
# a state, not a measurement — it changes at the first registration — so no count is quoted here.
# The leg's lifetime is permanent under the current bind either way.
# FOUR OF THE NINE CONTAINERS, and the omission is deliberate rather than a list that fell behind.
# ADR 0128's Streams table fixes these four as the app stream. Left out, with what is lost by it:
# the two migrate containers (exit by design, and reconcile's own record of each apply IS in the
# journal, which the journal leg ships); postgres and redis (connection and authentication traces,
# not app events); and seq itself — whose log is the only place an ingest-auth refusal appears
# after log-sink.md §3 step 5, and which reaches NO leg at all, because docker writes container
# output to json-file rather than to the journal. Adding one is a change to ADR 0128's table, not to this array alone.
readonly -a APP_CONTAINERS=(
  jobbliggaren-api
  jobbliggaren-worker
  jobbliggaren-web
  jobbliggaren-caddy
)

if command -v docker >/dev/null 2>&1; then
  # The anchor, floored: max(stamp, now - RETENTION_DAYS), and the floor is in BOTH branches
  # rather than only the stamp-less one. A stamp older than the window anchors exactly as far
  # back as no stamp at all — a box that was down, or a leg that withheld its stamp on purpose —
  # so a floor living in one branch leaves the same read reachable through the other. What this
  # leg can state is therefore unconditional: it never reads a line older than the window. The
  # number is ADR 0024 D7 policy 1's, parity with jobbliggaren-logprune.sh (#1561).
  floor_epoch=$(date -u -d "${RETENTION_DAYS} days ago" +%s) ||
    die "could not compute the app-leg window floor (needs GNU date)"
  app_epoch="$floor_epoch"
  if [[ -f "$STAMP_FILE" ]]; then
    stamp_epoch=$(stat -c '%Y' "$STAMP_FILE") ||
      die "could not read the stamp's mtime at ${STAMP_FILE}"
    if [[ "$stamp_epoch" -gt "$floor_epoch" ]]; then
      app_epoch="$stamp_epoch"
    fi
  fi
  app_since=$(date -u -d "@${app_epoch}" +%Y-%m-%dT%H:%M:%SZ)

  app_extract="${WORKDIR}/app-${run_stamp}.log"
  : > "$app_extract"

  # THE SAME ASYMMETRY THE JOURNAL LEG WAS REPAIRED FOR, AND THE REPAIR HAS TO BE DIFFERENT HERE.
  # There a failed read and an empty window both produced an empty extract; here a failed read is
  # worse than invisible, because `2>&1` puts docker's error text INSIDE the extract, it clears
  # `grep -qv '^===== '`, the artefact ships, the stamp is written — and the next run anchors
  # `--since` on THIS run's start. The window between the previous stamp and now is then never
  # read again by anything. The one thing this leg has no cursor for is exactly what silently
  # goes missing.
  #
  # The anchor is the stamp, floored at the window (see the anchor above), so the symmetric cure
  # is to withhold the stamp: the next run re-reads this window back to the floor, and duplicates
  # in a forensic archive are benign while a hole is not (this file's own doctrine, stated twice).
  # What has already been promoted stays promoted — the journal
  # cursor and the audit offset are separate state and their windows did ship.
  app_rc=0
  for container in "${APP_CONTAINERS[@]}"; do
    # A container that is not running is not an error: the migrate containers exit by design and
    # a service may be down for a reason this script does not own.
    docker inspect "$container" >/dev/null 2>&1 || continue
    {
      printf '===== %s =====\n' "$container"
      docker logs --timestamps --since "$app_since" "$container" 2>&1 || app_rc=$?
    } >> "$app_extract"
  done

  # Only ship if something beyond the per-container headers landed. Without this the archive
  # accrues one near-empty object per hour forever, which is noise in the very corpus an
  # incident reader has to page through.
  if [[ -s "$app_extract" ]] && grep -qv '^===== ' "$app_extract"; then
    ship "$app_extract" "app-${run_stamp}.log.gz.age"
  else
    log "app: no container output in this window"
  fi

  # Ship first, THEN fail. What was read is worth keeping even when part of the window is
  # suspect — the die below only withholds the stamp, so the next run re-reads the same window
  # against an archive that already holds whatever this one managed to collect.
  [[ "$app_rc" -eq 0 ]] || die "docker logs exited ${app_rc} for at least one container. No stamp
is written, so the next run re-reads this window rather than anchoring past it."
else
  log "app: docker is not on PATH; skipping that leg"
fi

# ── The stamp ────────────────────────────────────────────────────────────────────────────────
# Written LAST and only on success, so --check measures completed runs. A stamp written earlier
# would report freshness for a run that then failed to ship anything. STATE_DIR was created at
# the top of the run; creating it twice said the second one carried a reason, and it did not.
printf '%s\n' "$run_stamp" > "$STAMP_FILE"
chmod 0644 "$STAMP_FILE"
# Backdate to the run's start — see run_epoch. `--check` stays correct because a start time is
# conservative in the direction that matters: it can report stale early, never fresh late.
touch -d "@${run_epoch}" "$STAMP_FILE"

log "run ${run_stamp} complete; ${shipped} artefact(s) shipped to ${REMOTE_PREFIX}/"
