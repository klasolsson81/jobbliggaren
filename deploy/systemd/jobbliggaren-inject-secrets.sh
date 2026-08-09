#!/usr/bin/env bash
# jobbliggaren-inject-secrets — write the crypto secrets to tmpfs after a boot, and (--check)
# detect their absence.
#
# WHY THERE IS NO AT-REST COPY TO UNSEAL. Gate B-1 (ADR 0050:566) requires the field-encryption
# master key never be plaintext on disk. The gate's parenthetical names two mechanisms — a
# TPM-bound systemd credential, or sops+age into tmpfs — and MEASUREMENT ON THIS HOST EXHAUSTED
# BOTH (2026-08-09): `systemd-analyze has-tpm2` reports `partial` with no /dev/tpm0 and no
# libtss2, and `sops` is absent from apt on trixie. Without a TPM, a `systemd-creds` blob and
# /var/lib/systemd/credential.secret travel together in any disk snapshot, so that branch is
# obfuscation rather than encryption, and an on-disk age key is the same thing with one more
# supply-chain dependency. The requirement is "never plaintext on disk"; keeping the key only in
# RAM satisfies it more strongly than either named option would have (senior-cto-advisor bind
# 2026-08-09, Q1).
#
# The box was already hardened for exactly this: no disk swap, zram with no writeback device,
# core dumps discarded (vps-base-hardening.md §8), and §7 already records "every reboot destroys
# the RAM-held key and requires re-injection" as the operating model. Auto-reboot is off.
#
# THE COST, NAMED: after an UNPLANNED reboot api and worker crash-loop until an operator runs
# this. There is no log sink (#1175), so jobbliggaren-secrets-present.service is what puts the
# condition on `systemctl --failed` — the box's only alarm surface.
#
# NEVER argv, NEVER history, NEVER the journal. Values are read with `read -rs` from a terminal
# (enforced below, not merely intended). /proc is world-readable, so a secret in a command line
# is a secret published to every local process.
set -euo pipefail

readonly SECRETS_DIR=/run/jobbliggaren/secrets
readonly COMPOSE_FILE=/opt/jobbliggaren/deploy/docker-compose.yml

# The directory mode the running stack needs: root owns it, the container's group may traverse
# but not list. /etc/tmpfiles.d/jobbliggaren.conf creates it 0700 root:root at boot (correct
# posture before injection) and marks mode/owner create-only, so a later `systemd-tmpfiles
# --create` cannot revoke what this script sets.
readonly DIR_MODE=0710

# The .NET configuration keys, which ARE the file names (docker-compose.yml's x-app-secrets
# anchor points at these paths, and the reader maps `__` to the section delimiter). Adding a
# secret here is the whole change on the host side.
readonly -a SECRET_KEYS=(
  "FieldEncryption__LocalMasterKeyBase64"
  "AuditPseudonymization__PepperBase64"
  "CompanyWatchPseudonymization__PepperBase64"
  "CvReviewFingerprintPseudonymization__PepperBase64"
)

# Not a secret: the key identity, stamped into user_data_keys.cmk_key_id and read by the
# re-wrap operation as its idempotency marker (#198, M-3).
readonly KEY_ID_FILE="FieldEncryption__LocalMasterKeyId"
readonly DEFAULT_KEY_ID="local-v1"

# EVERYTHING THIS SCRIPT PRINTS GOES TO STDERR, and that is load-bearing rather than style.
# `uid=$(resolve_runtime_uid)` captures stdout, so a `die` inside it would otherwise land its
# REFUSING line INSIDE the variable and exit 1 with no output at all — at the most expensive
# moment there is. The only thing ever written to stdout is the measured uid/gid pair.
log() { printf '%s\n' "$*" >&2; }
die() { log "REFUSING: $*"; exit 1; }

# A file counts as present only if it holds something the READER will accept. The reader trims
# and treats whitespace-only as absent (EnvFileSecretsConfiguration.cs), so a file holding a
# single space would otherwise pass `-s` here while crash-looping the stack — a false all-clear
# on the box's only alarm surface.
#
# THE TWO PREDICATES AGREE ON ASCII WHITESPACE AND DIVERGE BEYOND IT, which is worth stating
# rather than claiming an equivalence that does not hold. Measured 2026-08-09: a UTF-8 NBSP
# (0xC2 0xA0) survives `tr -d '[:space:]'` while .NET's Trim() removes it, so such a file would
# read as present here and absent to the app — the fail-OPEN direction. Reachable only by
# pasting from a rendered document. Making bash Unicode-aware is not proportionate; leaving the
# gap unnamed is not either. (U+200B is not whitespace to Trim() either, so that class does not
# arise.)
has_usable_content() {
  local path="$1"
  [[ -s "$path" ]] || return 1
  [[ -n "$(tr -d '[:space:]' < "$path" 2>/dev/null)" ]]
}

# --check is the absence detector, and it lives HERE rather than in the unit so the list of
# file names has exactly one home. jobbliggaren-secrets-present.service runs it on a timer.
# It must stat files and nothing else — it runs at boot, when dockerd may not be up, so it must
# never touch docker.
if [[ "${1:-}" == "--check" ]]; then
  missing=0

  # The DIRECTORY is checked too. Files present but the directory un-traversable by the
  # container's group is a real crash-loop state that a files-only sweep reports as healthy.
  if [[ ! -d "$SECRETS_DIR" ]]; then
    log "MISSING: $SECRETS_DIR (directory does not exist)"
    missing=1
  else
    dir_mode=$(stat -c '%a' "$SECRETS_DIR" 2>/dev/null || echo "?")
    # Compared against DIR_MODE, not a second spelling of it: two literals for one value is
    # how an assertion quietly keeps checking the old one after the setter changes.
    if [[ "$dir_mode" != "${DIR_MODE#0}" ]]; then
      log "WRONG MODE: $SECRETS_DIR is $dir_mode, expected ${DIR_MODE#0} — the container's group"
      log "            cannot traverse it. After a reboot this is expected and the files are gone"
      log "            too; if the files ARE present, api/worker crash-loop despite them."
      missing=1
    fi
  fi

  for key in "${SECRET_KEYS[@]}" "$KEY_ID_FILE"; do
    if ! has_usable_content "${SECRETS_DIR}/${key}"; then
      log "MISSING: ${SECRETS_DIR}/${key}"
      missing=1
    fi
  done

  if [[ $missing -ne 0 ]]; then
    log "The box has booted without usable crypto secrets. api and worker are crash-looping"
    log "by design (fail-closed, never a fallback key). Inject with:"
    log "  sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh"
    exit 1
  fi
  log "all secrets present in ${SECRETS_DIR}"
  exit 0
fi

[[ $# -eq 0 ]] || die "unknown argument '$1' (use no arguments to inject, or --check)"

[[ ${EUID} -eq 0 ]] || die "must run as root (writes to ${SECRETS_DIR} and chowns to the container uid)"

# A pipe would put the secret in argv and in shell history — which this script's own header
# forbids. The invariant belongs in the machinery, not only in the prose.
[[ -t 0 ]] || die "stdin is not a terminal. Values are prompted for deliberately: piping one in
puts it in argv and in shell history, which is exactly what this script exists to avoid."

# THE RUNTIME UID AND GID ARE MEASURED FROM THE IMAGE, NEVER HARDCODED AND NEVER INFERRED FROM
# EACH OTHER. All three service images declare `USER app` (Api/Dockerfile, Worker/Dockerfile,
# Migrate/Dockerfile). A hardcoded number couples this host to a base-image detail, and the
# coupling breaks at cutover — the most expensive moment to discover it. Group traversal is the
# container's ONLY way into a 0710 directory, so deriving gid from uid would reintroduce the
# same class one level down: an image where gid != uid makes the mount unreadable, and the app
# then fails with "master key missing" rather than "permission denied".
resolve_runtime_ids() {
  local image
  image=$(docker compose -f "$COMPOSE_FILE" config --images 2>&1 | grep -m1 -F 'jobbliggaren-api') \
    || die "could not resolve the api image from ${COMPOSE_FILE} (compose interpolates .env —
a missing required variable fails here)"
  [[ -n "$image" ]] || die "no api image found in ${COMPOSE_FILE}"
  # stderr is in that pipe, so a compose error line containing the substring would otherwise be
  # handed to `docker run` as an image name and fail for the wrong stated reason.
  [[ "$image" =~ ^[a-z0-9./_-]+:[A-Za-z0-9._-]+$ ]]     || die "resolved api image is not an image reference: '${image}' (compose likely errored)"
  docker run --rm --entrypoint sh "$image" -c 'id -u; id -g' 2>/dev/null \
    || die "could not read the runtime uid/gid from ${image} (is it pulled? is dockerd up?)"
}

mapfile -t runtime_ids < <(resolve_runtime_ids)
uid="${runtime_ids[0]:-}"
gid="${runtime_ids[1]:-}"
[[ "$uid" =~ ^[0-9]+$ ]] || die "measured uid is not numeric: '${uid}'"
[[ "$gid" =~ ^[0-9]+$ ]] || die "measured gid is not numeric: '${gid}'"
log "container runtime ids measured from the api image: uid=${uid} gid=${gid}"

install -d -m "$DIR_MODE" -o root -g "$gid" "$SECRETS_DIR"

write_secret() {
  local name="$1" value="$2" path="${SECRETS_DIR}/$1"
  # Created 0400 from the outset. A plain redirect would create the file under root's umask
  # (typically 0644) and only narrow it afterwards; the parent's 0710 closes that window in
  # practice, but the class is removable rather than merely bounded.
  install -m 0400 -o "$uid" -g "$gid" /dev/null "$path"
  # printf '%s' — no trailing newline is written at all. The reader trims as a backstop, but
  # writing exactly the bytes intended is the control; relying on the backstop is not.
  printf '%s' "$value" > "$path"
  log "wrote ${name} (${#value} chars, mode 0400, owner ${uid}:${gid})"
}

# THE KEY IDENTITY IS WRITTEN FIRST, AND IT IS BOUND TO THE MASTER KEY'S PRESENCE.
#
# Getting this wrong is a data-loss path, not an inconvenience, and the earlier version of this
# script had it: the identity was written last and only when absent, defaulting to local-v1. So
# after a rotation to local-v2, the next reboot cleared tmpfs and re-injection silently restored
# `local-v1` alongside v2 key bytes. Every row created after that is stamped with the retired
# identity while wrapped under the new key — and the re-wrap's compare-and-swap on
# `cmk_key_id == oldKeyId` then skips exactly those rows, which become unrecoverable the moment
# the v2 key is discarded. That is the failure FieldEncryptionOptions.LocalMasterKeyId exists to
# prevent, reintroduced one level down.
#
# So: identity and bytes are one unit. If the master key is being written, the identity is
# written in the same run; if the identity already exists it is reported, never silently kept.
master_key_file="${SECRETS_DIR}/FieldEncryption__LocalMasterKeyBase64"
key_id_path="${SECRETS_DIR}/${KEY_ID_FILE}"

if has_usable_content "$master_key_file" && ! has_usable_content "$key_id_path"; then
  die "the master key is present but ${KEY_ID_FILE} is not. That pair must never diverge —
remove both and re-inject, or write the identity that matches the key already in place."
fi

if ! has_usable_content "$master_key_file"; then
  # Fresh master key incoming: the identity must be stated for THIS key, not inherited.
  key_id="${JBL_MASTER_KEY_ID:-}"
  if [[ -z "$key_id" ]]; then
    printf 'Key identity for the master key about to be injected [%s]: ' "$DEFAULT_KEY_ID" >&2
    read -r key_id
    key_id="${key_id:-$DEFAULT_KEY_ID}"
  fi
  [[ "$key_id" =~ ^[A-Za-z0-9._-]+$ ]] || die "key identity must match [A-Za-z0-9._-]+, got '${key_id}'"
  rm -f "$key_id_path"
  write_secret "$KEY_ID_FILE" "$key_id"
else
  log "${KEY_ID_FILE} already present ($(cat "$key_id_path")) — master key unchanged, identity kept"
fi

for key in "${SECRET_KEYS[@]}"; do
  if has_usable_content "${SECRETS_DIR}/${key}"; then
    log "${key} already present — skipping (remove the file first to replace it)"
    continue
  fi

  printf 'Value for %s: ' "$key" >&2
  read -rs value
  printf '\n' >&2
  # Whitespace-only is rejected here, at the prompt, rather than downstream by --check: the
  # operator is standing right there, and the reader would treat such a value as absent anyway.
  [[ -n "${value//[[:space:]]/}" ]] || die "${key} was empty or whitespace-only — nothing
written, and the run is aborted so a partially injected directory is never mistaken for a
complete one"

  # The master key must decode to 32 bytes (AES-256). Catching it here turns a crash-loop with a
  # startup message into an immediate, local error.
  if [[ "$key" == "FieldEncryption__LocalMasterKeyBase64" ]]; then
    decoded_len=$(printf '%s' "$value" | base64 -d 2>/dev/null | wc -c) \
      || die "${key} is not valid base64"
    [[ "$decoded_len" -eq 32 ]] \
      || die "${key} must decode to 32 bytes (AES-256), got ${decoded_len}"
  fi

  write_secret "$key" "$value"
  unset value
done

log ""
log "Injected. api and worker recover on their own restart backoff (restart: unless-stopped) —"
log "no 'compose up' and no reconcile run is needed. Verify with:"
log "  sudo $0 --check"
log "  docker inspect -f '{{.State.Health.Status}}' jobbliggaren-api    # expect: healthy"
