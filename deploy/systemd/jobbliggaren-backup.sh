#!/usr/bin/env bash
# jobbliggaren-backup — nightly encrypted offsite backup of the production database, and
# (--check) detection that a backup has stopped happening.
#
# ADR 0050 `Amendment 2026-08-04` §7 sets the requirements this discharges: age encryption
# client-side BEFORE upload regardless of target, EU jurisdiction, K4 = 30 days retention that is
# demonstrable, a target whose failure domain is independent of both this box and the operator's
# workstation, and the age PRIVATE key never stored with the ciphertext. #197 owns the mechanism.
#
# THE BOX HOLDS NO PRIVATE KEY, AND THAT IS THE DESIGN RATHER THAN AN OMISSION. age encrypts to a
# public recipient, so requirement (b) is satisfied structurally: there is nothing here to steal
# that would read a backup. The cost is stated plainly — this script can produce a backup it
# cannot itself verify decrypts, so decryptability is proven by the restore drill and by nothing
# in this file. See docs/runbooks/backup-restore.md.
#
# TWO ARTEFACTS PER RUN, AND THE SPLIT IS THE POINT (senior-cto-advisor bind 2026-08-09, D2).
# The main artefact carries every table EXCEPT the contents of user_data_keys; the wrapped
# per-user DEKs travel as their own artefact of which exactly one verified generation is
# retained. A restore pairs any main artefact within the retention window with the CURRENT DEK
# artefact — so a user hard-deleted since that main artefact was taken has no DEK anywhere in
# what we hold, and their field-encrypted columns are unreadable by any combination of artefacts
# in our possession. A single full dump would carry the wrapped DEK beside the ciphertext it
# unwraps, which is the unwritten premise that makes ADR 0049 Beslut 2's claim, and the published
# sentence in content-legal.json, true or false.
#
# THE BOX APPENDS AND NEVER PRUNES (D5). Retention of main artefacts is the target's own
# lifecycle policy — a rule a third party enforces and we can export, which is what Art. 5(2)
# demonstrability asks for, and which a credential without DELETE keeps out of reach of a
# compromised box. The only object this script replaces is the DEK generation, and it does that
# by overwrite-after-verification rather than by deleting anything (see promote_dek below).
set -euo pipefail

readonly HOST_SECRETS_DIR=/run/jobbliggaren/host-secrets
readonly CREDENTIAL_FILE="${HOST_SECRETS_DIR}/Backup__RcloneConfigBase64"
readonly RECIPIENT_FILE=/opt/jobbliggaren/deploy/backup/age.recipient
readonly STAMP_FILE=/var/lib/jobbliggaren/last-successful-backup
readonly LOCK_FILE=/var/lock/jobbliggaren-backup.lock
readonly PG_CONTAINER=jobbliggaren-postgres
readonly PG_DATABASE=jobbliggaren
readonly PG_USER=postgres

# The DEK table, named once. It appears in two pg_dump invocations with opposite polarity, and a
# typo in either direction is silent: excluded from both leaves the DEKs unbacked-up, included in
# both reconstitutes the single-artefact shape this design exists to avoid.
readonly DEK_TABLE=user_data_keys

# The remote. The name before the colon must match a section header in the injected rclone
# config; everything after it is the bucket or directory. THIS IS THE ONLY TARGET-SPECIFIC LINE
# IN THE FILE — a vendor change is this constant plus a new credential, and no code.
readonly BACKUP_REMOTE="jbl-backup:jobbliggaren-backups"

# Object layout under that root. `staged` is the freshly uploaded DEK artefact whose round trip
# has not been checked yet; `verified` is the one a restore uses. Two names, never one: promoting
# by overwriting the single object a restore depends on would, on a failed write, leave zero
# readable DEK generations — and without a DEK generation every retained main artefact is
# permanently unreadable.
readonly MAIN_PREFIX="main"
readonly DEK_STAGED="deks/staged.dump.age"
readonly DEK_VERIFIED="deks/verified.dump.age"

# A backup older than this reads as a stopped backup. 26 h rather than 24 h: the nightly timer
# carries RandomizedDelaySec, and a threshold equal to the period alarms on ordinary jitter,
# which trains an operator to stop reading the only alarm surface there is (#1175).
readonly MAX_STAMP_AGE_SECONDS=$((26 * 3600))

# EXIT CONTRACT, three outcomes and they are never collapsed:
#   0  a backup was produced and the DEK generation is verified
#   1  the run refused or failed — no backup was produced
#   2  the environment could not be established (a tool or input is missing)
# Both non-zero codes fail the unit, which is the correct outcome for either; the distinction is
# for whoever reads the journal, and for the fixture suite. The reason 2 exists at all is the
# reconcile script's written lesson: a tool's ABSENCE must never read as its verdict.
readonly EXIT_FAILED=1
readonly EXIT_UNAVAILABLE=2

log() { printf '%s\n' "$*" >&2; }
die() { log "REFUSING: $*"; exit "$EXIT_FAILED"; }
die_unavailable() { log "CANNOT ANSWER: $*"; exit "$EXIT_UNAVAILABLE"; }

# ---------------------------------------------------------------------------------------------
# --check — the freshness probe.
#
# It lives here rather than in the unit so the stamp path has exactly one home, and it stats one
# file and nothing else: no docker, no network, no remote listing. That is what makes it safe to
# run at boot before dockerd, and it is also what keeps it honest — a probe that talked to the
# remote could report a backup as fresh because an OLD object is still there.
#
# WHAT IT DOES NOT SEE, NAMED: the stamp is written by this script, so the probe measures "the
# nightly run last succeeded", not "an artefact exists offsite and decrypts". The second is the
# drill's job and is owned by the runbook, not by any timer on this box.
# ---------------------------------------------------------------------------------------------
if [[ "${1:-}" == "--check" ]]; then
  if [[ ! -f "$STAMP_FILE" ]]; then
    log "MISSING: $STAMP_FILE — no backup run has ever succeeded on this box."
    log "Run one by hand and read the journal:"
    log "  sudo systemctl start jobbliggaren-backup.service"
    log "  journalctl -u jobbliggaren-backup.service -n 50"
    exit "$EXIT_FAILED"
  fi

  stamp_mtime=$(stat -c '%Y' "$STAMP_FILE" 2>/dev/null) || die_unavailable "could not stat $STAMP_FILE"
  now=$(date +%s)
  age_seconds=$((now - stamp_mtime))

  # A stamp from the FUTURE is not fresh, it is a broken clock or a tampered file, and the
  # subtraction above would report it as comfortably recent. Fail rather than flatter.
  if [[ "$age_seconds" -lt 0 ]]; then
    log "STAMP IN THE FUTURE: $STAMP_FILE is dated $((-age_seconds))s ahead of now."
    exit "$EXIT_FAILED"
  fi

  if [[ "$age_seconds" -gt "$MAX_STAMP_AGE_SECONDS" ]]; then
    log "STALE: last successful backup was $((age_seconds / 3600))h ago (threshold $((MAX_STAMP_AGE_SECONDS / 3600))h)."
    log "The nightly backup has stopped. Read the journal for the last attempt:"
    log "  journalctl -u jobbliggaren-backup.service -n 50"
    exit "$EXIT_FAILED"
  fi

  log "backup is fresh: last success $((age_seconds / 3600))h ago"
  exit 0
fi

[[ $# -eq 0 ]] || die "unknown argument '$1' (use no arguments to run a backup, or --check)"
[[ ${EUID} -eq 0 ]] || die "must run as root (reads ${HOST_SECRETS_DIR} and talks to the docker socket)"

# ---------------------------------------------------------------------------------------------
# Preflight. Every input is checked BEFORE the first dump, so a run that cannot finish does not
# start — a half-run leaves a main artefact offsite with no matching DEK generation, and the
# ordering invariant below is what keeps such a pair from ever being restorable-looking.
# ---------------------------------------------------------------------------------------------
for tool in docker age rclone sha256sum flock; do
  command -v "$tool" >/dev/null 2>&1 \
    || die_unavailable "'$tool' is not installed. This run produces NO backup rather than an
unencrypted or unverified one. Install it (see docs/runbooks/backup-restore.md §2) and re-run."
done

[[ -r "$RECIPIENT_FILE" ]] || die_unavailable "age recipient not readable at ${RECIPIENT_FILE}.
The recipient is the PUBLIC half of the backup identity and is installed by the operator; the
private half must never be on this box (ADR 0050 Amendment 2026-08-04 §7 requirement b)."

recipient=$(tr -d '[:space:]' < "$RECIPIENT_FILE")
# Shape-checked, not merely non-empty. An X25519 age recipient is `age1` followed by bech32
# characters; a private key starts `AGE-SECRET-KEY-`, and rejecting that spelling here is the one
# cheap guard against requirement (b) being violated by a paste into the wrong file.
[[ "$recipient" == age1* ]] || die "recipient in ${RECIPIENT_FILE} does not start with 'age1'.
If it starts with AGE-SECRET-KEY- that is a PRIVATE key and it must not be on this box."
[[ "$recipient" =~ ^age1[0-9a-z]+$ ]] || die "recipient in ${RECIPIENT_FILE} is not a well-formed
age recipient: '${recipient}'"

[[ -r "$CREDENTIAL_FILE" ]] || die_unavailable "upload credential not readable at
${CREDENTIAL_FILE}. It lives on tmpfs and is re-injected after every boot:
  sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh"
[[ -s "$CREDENTIAL_FILE" ]] || die "upload credential at ${CREDENTIAL_FILE} is empty"

docker inspect -f '{{.State.Running}}' "$PG_CONTAINER" 2>/dev/null | grep -qx true \
  || die "container ${PG_CONTAINER} is not running — there is nothing to dump"

# ---------------------------------------------------------------------------------------------
# Working state. The rclone config is a credential, so it is materialised on the SAME tmpfs the
# injected secret already lives on and never on persistent disk. The trap removes it on every
# exit path including failure.
#
# The DEK ciphertext is staged here too — deliberately, and only the DEK artefact. It is the one
# whose bytes must be re-read after upload to check the round trip, and at one row per user it is
# small. The main artefact is never staged: it streams, so its PLAINTEXT exists only inside a
# pipe and its ciphertext only in flight.
# ---------------------------------------------------------------------------------------------
WORKDIR=$(mktemp -d "${HOST_SECRETS_DIR}/backup.XXXXXX") \
  || die_unavailable "could not create a working directory under ${HOST_SECRETS_DIR} (is the
tmpfs mounted? has the injection script run since the last boot?)"
readonly WORKDIR
chmod 0700 "$WORKDIR"
trap 'rm -rf "$WORKDIR"' EXIT

rclone_config="${WORKDIR}/rclone.conf"
install -m 0600 /dev/null "$rclone_config"
base64 -d < "$CREDENTIAL_FILE" > "$rclone_config" \
  || die "the upload credential is not valid base64. It is the base64 of a complete rclone
config file; see docs/runbooks/backup-restore.md §2."
[[ -s "$rclone_config" ]] || die "the decoded rclone config is empty"

export RCLONE_CONFIG="$rclone_config"
# rclone's own log must never carry the config it just read.
readonly RCLONE_FLAGS=(--config "$rclone_config" --log-level NOTICE --retries 3)

# One run at a time. A second run overlapping the first would interleave two DEK generations
# through the staged/verified promotion below and could promote the older of the two.
exec 9>"$LOCK_FILE"
flock -n 9 || die "another jobbliggaren-backup run holds ${LOCK_FILE}"

run_stamp=$(date -u +%Y%m%dT%H%M%SZ)
readonly run_stamp
started_at=$(date +%s)

# ---------------------------------------------------------------------------------------------
# THE ORDER OF THE TWO DUMPS IS LOAD-BEARING AND IT IS NOT A STYLE CHOICE.
#
# Main first, DEKs second. A user who registers BETWEEN the two dumps then appears in the DEK
# artefact but not in the main one, which restores harmlessly: the staging-table INSERT in the
# restore procedure drops DEK rows whose owner is absent. Reversed, that same user would land in
# a main artefact with no DEK anywhere, and their fields would be permanently unreadable — silent
# data loss wearing erasure's clothes.
# ---------------------------------------------------------------------------------------------
main_object="${BACKUP_REMOTE}/${MAIN_PREFIX}/jobbliggaren-${run_stamp}.dump.age"
log "main artefact -> ${main_object}"

# `docker exec` without -t: a TTY would translate line endings and corrupt a binary dump.
# --exclude-table-data, NOT --exclude-table: the table's DEFINITION must be restored (empty) so
# the DEK artefact has somewhere to land and so the schema is complete.
set +e
docker exec "$PG_CONTAINER" \
  pg_dump -U "$PG_USER" -d "$PG_DATABASE" -Fc --no-owner --no-privileges \
    --exclude-table-data="$DEK_TABLE" \
  | age -r "$recipient" \
  | rclone rcat "${RCLONE_FLAGS[@]}" "$main_object"
main_status=("${PIPESTATUS[@]}")
set -e

# EVERY STAGE OF THE PIPE IS CHECKED, not just the last. `set -o pipefail` alone would tell us
# that something failed and not what; worse, a pg_dump that dies partway still feeds age a
# truncated but perfectly well-formed stream, which encrypts and uploads happily. A silently
# truncated backup that reports success is the single failure this whole issue exists to prevent.
for i in "${!main_status[@]}"; do
  [[ "${main_status[$i]}" -eq 0 ]] || die "main artefact stage $((i + 1)) of 3 exited ${main_status[$i]}
(1=pg_dump 2=age 3=rclone). No stamp is written and no DEK artefact is promoted."
done

# ---------------------------------------------------------------------------------------------
# The DEK artefact: dump, encrypt, hash locally, upload to `staged`, read it back, compare.
# ---------------------------------------------------------------------------------------------
dek_local="${WORKDIR}/deks.dump.age"
install -m 0600 /dev/null "$dek_local"

set +e
docker exec "$PG_CONTAINER" \
  pg_dump -U "$PG_USER" -d "$PG_DATABASE" -Fc --no-owner --no-privileges \
    --data-only --table="$DEK_TABLE" \
  | age -r "$recipient" > "$dek_local"
dek_status=("${PIPESTATUS[@]}")
set -e

for i in "${!dek_status[@]}"; do
  [[ "${dek_status[$i]}" -eq 0 ]] || die "DEK artefact stage $((i + 1)) of 2 exited ${dek_status[$i]}
(1=pg_dump 2=age). The main artefact for ${run_stamp} is offsite but this run did not promote a
DEK generation; the previously verified one is untouched and still pairs with it."
done

[[ -s "$dek_local" ]] || die "the encrypted DEK artefact is empty"

local_sha=$(sha256sum < "$dek_local" | cut -d' ' -f1)
log "DEK artefact: $(stat -c '%s' "$dek_local") bytes, sha256 ${local_sha}"

rclone rcat "${RCLONE_FLAGS[@]}" "${BACKUP_REMOTE}/${DEK_STAGED}" < "$dek_local" \
  || die "upload of the staged DEK artefact failed"

# THE ROUND TRIP IS THE VERIFICATION, and it is compared against the bytes we sent rather than
# against a size or a remote-reported hash. It proves the object is retrievable and intact; it
# does NOT prove it decrypts, because this box holds no private key by design. Decryptability is
# the drill's claim to make, and the runbook says so in those words.
remote_sha=$(rclone cat "${RCLONE_FLAGS[@]}" "${BACKUP_REMOTE}/${DEK_STAGED}" | sha256sum | cut -d' ' -f1) \
  || die "could not read back the staged DEK artefact"

[[ "$remote_sha" == "$local_sha" ]] || die "the staged DEK artefact does not match what was
uploaded (local ${local_sha}, remote ${remote_sha}). NOT promoted — the previously verified DEK
generation is untouched and still pairs with every retained main artefact."

# Promotion writes the SAME LOCAL BYTES that were just verified, never a server-side copy of the
# staged object: a copy would be a second thing to go wrong between two objects we have already
# established agree with what we hold.
rclone rcat "${RCLONE_FLAGS[@]}" "${BACKUP_REMOTE}/${DEK_VERIFIED}" < "$dek_local" \
  || die "promotion of the DEK artefact failed. ${DEK_STAGED} is verified and current — a restore
may use it; see docs/runbooks/backup-restore.md §5."

install -d -m 0755 "$(dirname "$STAMP_FILE")"
printf '%s\n' "$run_stamp" > "$STAMP_FILE"

log "backup complete in $(( $(date +%s) - started_at ))s: main ${run_stamp}, DEK generation promoted"
