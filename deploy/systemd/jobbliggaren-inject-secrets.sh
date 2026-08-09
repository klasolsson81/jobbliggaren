#!/usr/bin/env bash
# jobbliggaren-inject-secrets — write the four crypto secrets to tmpfs after a boot.
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
# NEVER argv, NEVER history, NEVER the journal. Values are read with `read -rs` from the
# terminal. /proc is world-readable, so a secret in a command line is a secret published to
# every local process.
set -euo pipefail

readonly SECRETS_DIR=/run/jobbliggaren/secrets
readonly COMPOSE_FILE=/opt/jobbliggaren/deploy/docker-compose.yml

# The .NET configuration keys, which ARE the file names (docker-compose.yml's x-app-secrets
# anchor points at these paths, and the reader maps `__` to the section delimiter). Adding a
# secret here is the whole change on the host side.
readonly -a SECRET_KEYS=(
  "FieldEncryption__LocalMasterKeyBase64"
  "AuditPseudonymization__PepperBase64"
  "CompanyWatchPseudonymization__PepperBase64"
  "CvReviewFingerprintPseudonymization__PepperBase64"
)

# Not a secret, and not prompted for: the key identity travels with the key bytes so the pair is
# always written together (#198 M-3 — it is the re-wrap idempotency marker).
readonly KEY_ID_FILE="FieldEncryption__LocalMasterKeyId"
readonly DEFAULT_KEY_ID="local-v1"

log() { printf '%s\n' "$*"; }
die() { log "REFUSING: $*"; exit 1; }

# --check is the absence detector, and it lives HERE rather than in the unit so the list of
# file names has exactly one home. jobbliggaren-secrets-present.service runs it on a timer;
# a missing secret then lands the box in `systemctl --failed`, which is its only alarm
# surface (#1175: no log sink). It must stat files and nothing else — it runs at boot, when
# dockerd may not be up, so it must never touch docker.
if [[ "${1:-}" == "--check" ]]; then
  missing=0
  for key in "${SECRET_KEYS[@]}" "$KEY_ID_FILE"; do
    if [[ ! -s "${SECRETS_DIR}/${key}" ]]; then
      log "MISSING: ${SECRETS_DIR}/${key}"
      missing=1
    fi
  done
  if [[ $missing -ne 0 ]]; then
    log "The box has booted without its crypto secrets. api and worker are crash-looping"
    log "by design (fail-closed, never a fallback key). Inject with:"
    log "  sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh"
    exit 1
  fi
  log "all secrets present in ${SECRETS_DIR}"
  exit 0
fi

[[ $# -eq 0 ]] || die "unknown argument '$1' (use no arguments to inject, or --check)"

[[ ${EUID} -eq 0 ]] || die "must run as root (writes to ${SECRETS_DIR} and chowns to the container uid)"

# THE RUNTIME UID IS MEASURED FROM THE IMAGE, NEVER HARDCODED. All three service images declare
# `USER app` (Api/Dockerfile, Worker/Dockerfile, Migrate/Dockerfile). A hardcoded number couples
# this host to a base-image detail, and the coupling breaks at cutover — the most expensive
# moment to discover it. A 0700 root-owned directory bind-mounted into a container running as a
# non-root user is simply unreadable, and the app fails options validation with a message about
# a missing key rather than about a permission.
resolve_runtime_uid() {
  local image
  image=$(docker compose -f "$COMPOSE_FILE" config --images 2>/dev/null \
          | grep -F 'jobbliggaren-api' | head -1) \
    || die "could not resolve the api image from ${COMPOSE_FILE}"
  [[ -n "$image" ]] || die "no api image found in ${COMPOSE_FILE}"
  docker run --rm --entrypoint id "$image" -u 2>/dev/null \
    || die "could not read the runtime uid from ${image} (is it pulled?)"
}

uid=$(resolve_runtime_uid)
[[ "$uid" =~ ^[0-9]+$ ]] || die "measured uid is not numeric: '${uid}'"
gid="$uid"
log "container runtime uid measured from the api image: ${uid}"

install -d -m 0710 -o root -g "$gid" "$SECRETS_DIR"

write_secret() {
  local name="$1" value="$2" path="${SECRETS_DIR}/$1"
  # printf '%s' — no trailing newline is written at all. The reader trims as a backstop, but a
  # pepper is HMAC input: one stray byte changes every derived value, and a pepper cannot be
  # rotated against data already pseudonymised under it. Do not rely on the backstop.
  printf '%s' "$value" > "$path"
  chown "${uid}:${gid}" "$path"
  chmod 0400 "$path"
  log "wrote ${name} (${#value} chars, mode 0400, owner ${uid})"
}

for key in "${SECRET_KEYS[@]}"; do
  if [[ -e "${SECRETS_DIR}/${key}" ]]; then
    log "${key} already present — skipping (remove the file first to replace it)"
    continue
  fi

  printf 'Value for %s: ' "$key" >&2
  read -rs value
  printf '\n' >&2
  [[ -n "$value" ]] || die "${key} was empty — nothing written, and the run is aborted so a
partially injected directory is never mistaken for a complete one"

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

if [[ ! -e "${SECRETS_DIR}/${KEY_ID_FILE}" ]]; then
  write_secret "$KEY_ID_FILE" "${JBL_MASTER_KEY_ID:-$DEFAULT_KEY_ID}"
fi

log ""
log "Injected. api and worker recover on their own restart backoff (restart: unless-stopped) —"
log "no 'compose up' and no reconcile run is needed. Verify with:"
log "  docker inspect -f '{{.State.Health.Status}}' jobbliggaren-api    # expect: healthy"
