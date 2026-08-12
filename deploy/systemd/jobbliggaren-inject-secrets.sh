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

# The measurement this script SETS the ownership from, and the one jobbliggaren-reconcile.sh
# GATES the apply on, are the same measurement in one file (#1295). Two spellings of it would be
# a rule with two normalisers, which is two rules.
#
# NO `[ -x ]` PRECONDITION GUARD HERE, and the divergence from reconcile is deliberate: that one
# runs unattended on a timer, where "helper missing" must be told apart from "ids disagree" on
# `systemctl --failed`. This one runs with an operator watching, and the call site's own `|| die`
# aborts before a single byte is written.
readonly RUNTIME_IDS=/opt/jobbliggaren/deploy/systemd/jobbliggaren-runtime-ids.sh

# Read for the mail configuration only — the provider, the two credential pointers and the
# region. Those four answers decide whether the SES credentials are required and whether the
# stack can start at all; nothing else in this script consults this file.
readonly ENV_FILE=/opt/jobbliggaren/deploy/.env

# A SECOND DIRECTORY, FOR SECRETS NO CONTAINER MAY SEE (#197). SECRETS_DIR is bind-mounted
# read-only into api and worker; this one is mounted nowhere, stays 0700 root:root, and holds
# what only a host-side root process reads — today the backup upload credential. See
# jobbliggaren-tmpfiles.conf for why the separation is structural rather than a mode bit.
readonly HOST_SECRETS_DIR=/run/jobbliggaren/host-secrets
readonly HOST_DIR_MODE=0700

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

# THE SES CREDENTIALS ARE CONDITIONALLY REQUIRED, WHICH IS WHY THEY ARE NOT IN SECRET_KEYS.
# They are needed under EITHER of the two conditions ses_credentials_required enumerates below —
# the provider being Ses is only one of them — and the injection half prompts under a third,
# JBL_INJECT_SES=1. Listing them above would put a permanent
# MISSING on jobbliggaren-secrets-present.service — the box's only alarm surface — for a state
# that is correct: before the flip there is nothing to inject and the stack is healthy without
# them. An alarm that is always on is an alarm nobody reads, so the condition is expressed
# rather than the entry added. The condition has one home: ses_credentials_required below.
readonly -a SES_SECRET_KEYS=(
  "Email__Ses__AccessKeyId"
  "Email__Ses__SecretAccessKey"
)

# The host-only secrets. Same one-row-per-secret contract as SECRET_KEYS above, different
# destination — and the name is NOT a .NET configuration key, because no .NET process reads
# these. jobbliggaren-backup.sh names the file it wants; the `__` spelling is kept only so the
# two directories read alike to an operator.
readonly -a HOST_SECRET_KEYS=(
  "Backup__RcloneConfigBase64"
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

# ONE NORMALISER FOR `.env`, because two spellings of one rule are two rules — the argument
# RUNTIME_IDS above already makes about the uid measurement (#1295).
#
# It approximates compose's parser rather than reproducing it, and the approximation is
# deliberately WIDER, never narrower. Reading the file with `docker compose config` would be
# exact and is refused: --check runs at boot before dockerd, which an existing case pins.
#
# Measured against Compose v2.40.3 on 2026-08-12 — all five forms render the same value, and
# the four beyond the first are why this function exists rather than a single `sed`:
#
#   EMAIL_PROVIDER=Ses              EMAIL_PROVIDER: Ses           (delimiter = or :)
#   EMAIL_PROVIDER=Ses # flippat    EMAIL_PROVIDER="Ses" # q      (inline comment, quoted or not)
#   export EMAIL_PROVIDER=Ses                                     (export prefix)
#
# `tail -n1` is compose's last-assignment-wins, measured.
#
# A QUOTED VALUE KEEPS ITS `#`, and a naive strip got that backwards in the fail-OPEN direction
# (security-auditor, 2026-08-12). Measured: `EMAIL_PROVIDER='Ses # x'` renders `Ses # x`, a value
# AddEmailSender throws on — so a reader that stripped it to `Ses` answered "configured for SES"
# about a box that does not start. The two quote arms below consume everything after the closing
# quote and branch out with `t`, so the unquoted strip can never run on what was inside them.
env_value() {  # env_value <NAME> -> value on stdout; empty when unset, unreadable or absent
  [[ -r "$ENV_FILE" ]] || return 0
  sed -n -E "s/^[[:space:]]*(export[[:space:]]+)?$1[[:space:]]*[:=][[:space:]]*//p" \
    "$ENV_FILE" 2>/dev/null \
    | tail -n1 \
    | sed -E -e 's/^"([^"]*)".*$/\1/;t' -e "s/^'([^']*)'.*\$/\1/;t" -e 's/[[:space:]]+#.*$//' \
    | tr -d '[:space:]'
}

# THREE-VALUED ON PURPOSE. An absent file, an unset variable and the literal Console all mean
# the same thing here as they do at boot: compose renders `${EMAIL_PROVIDER:-Console}` and
# AddEmailSender falls back to "Console" on a null. But a value that is NEITHER is not a third
# way of saying Console — AddEmailSender's switch ends in `else throw`, so the box does not boot
# on it either, and a detector that answered "not Ses" would go green on a stack that cannot
# start.
#
# Case-insensitive because AddEmailSender compares with OrdinalIgnoreCase
# (DependencyInjection.cs). A guard stricter than the code it guards is the dangerous direction:
# on `EMAIL_PROVIDER=ses` it would decline to prompt while the application accepted the value.
#
# Compose reads the shell environment BEFORE `.env` (precedence: shell > --env-file > .env), so
# an exported variable would defeat this. Measured 2026-08-12: jobbliggaren-reconcile.service
# carries neither `Environment=` nor `EnvironmentFile=`, so nothing in the deploy exports it and
# `.env` is in practice the only source.
email_provider() {
  case "$(env_value EMAIL_PROVIDER | tr '[:upper:]' '[:lower:]')" in
    "" | console) printf 'console' ;;
    ses)          printf 'ses' ;;
    *)            printf 'unknown' ;;
  esac
}

# REQUIRED BY EITHER OF TWO INDEPENDENT CAUSES, and binding only to the first was a defect.
#
# (1) The provider is Ses: AddEmailSender throws at registration without the credentials.
# (2) A `_FILE` pointer is set at all: EnvFileSecretsConfiguration throws on a pointer naming a
#     path it cannot read, and it never consults Email:Provider. So a rollback of the flip that
#     leaves the pointers behind is a boot refusal with a provider that is no longer Ses — the
#     state a provider-only predicate reports as healthy.
ses_credentials_required() {
  [[ "$(email_provider)" == "ses" ]] && return 0
  [[ -n "$(env_value EMAIL_SES_ACCESS_KEY_ID_FILE)" ]] && return 0
  [[ -n "$(env_value EMAIL_SES_SECRET_ACCESS_KEY_FILE)" ]] && return 0
  return 1
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

  # THE MAIL BRANCH REPORTS EVERY BOOT REFUSAL THIS FILE CAN SEE BY ABSENCE, not only a missing
  # secret. It validates PRESENCE and never VALUE, so a misspelt pointer path or a region outside
  # the EEA allow-list still reads as healthy here — that allow-list lives in
  # SesClientRegistration.cs and a second spelling of it in bash would be worse than the gap. The unit exists because a crash-looping container does NOT appear in
  # `systemctl --failed`, so a boot refusal this file can see and does not report is a green
  # alarm over a dead box — the failure this whole script is built against.
  env_provider=$(email_provider)

  if [[ "$env_provider" == "unknown" ]]; then
    log "INVALID: EMAIL_PROVIDER='$(env_value EMAIL_PROVIDER)' in ${ENV_FILE} is neither Console"
    log "         nor Ses. AddEmailSender's switch ends in a throw, so api and worker refuse to"
    log "         START on this value — this is not a quieter way of saying Console."
    missing=1
  fi

  if ses_credentials_required; then
    for key in "${SES_SECRET_KEYS[@]}"; do
      if ! has_usable_content "${SECRETS_DIR}/${key}"; then
        log "MISSING: ${SECRETS_DIR}/${key} — required because ${ENV_FILE} has EMAIL_PROVIDER=Ses"
        log "         or a Email__Ses__*_FILE pointer set. api and worker refuse to START"
        log "         (AddEmailSender throws at registration, not at the first send). Re-run this"
        log "         script without arguments to inject it."
        missing=1
      fi
    done
  fi

  # The other half of the same boot refusal: the files can be present while the .env lines that
  # deliver them are not. Checked only under Ses, because only then is each one required — and
  # each is named separately, since "email is broken" costs an operator the hour that naming the
  # variable saves.
  if [[ "$env_provider" == "ses" ]]; then
    for var in EMAIL_SES_ACCESS_KEY_ID_FILE EMAIL_SES_SECRET_ACCESS_KEY_FILE EMAIL_SES_REGION; do
      if [[ -z "$(env_value "$var")" ]]; then
        log "MISSING: ${var} is unset in ${ENV_FILE} while EMAIL_PROVIDER=Ses. The credential"
        log "         files can be injected and api and worker will still refuse to START:"
        log "         an unset pointer reads as 'not configured', which AddEmailSender throws on."
        missing=1
      fi
    done
  fi

  # The host-only directory is checked by the same loop rather than by a second predicate, so a
  # new entry in either array is covered the moment it is added — which is what makes "adding a
  # secret here is the whole change on the host side" true of both destinations. Its absence is
  # a different severity from a missing master key (the stack still serves; only the nightly
  # backup stops), and the message says so instead of leaving an operator to infer it.
  if [[ ! -d "$HOST_SECRETS_DIR" ]]; then
    log "MISSING: $HOST_SECRETS_DIR (directory does not exist)"
    missing=1
  fi
  for key in "${HOST_SECRET_KEYS[@]}"; do
    if ! has_usable_content "${HOST_SECRETS_DIR}/${key}"; then
      log "MISSING: ${HOST_SECRETS_DIR}/${key} — the stack still serves, but the nightly backup"
      log "         cannot upload (#197). jobbliggaren-backup.service will refuse."
      missing=1
    fi
  done

  if [[ $missing -ne 0 ]]; then
    # THIS SUMMARY USED TO PRESCRIBE THE ONE REMEDY, and it stopped being the one remedy when
    # the mail branch above gained lines injection cannot fix — an INVALID provider value and an
    # unset EMAIL_SES_* variable are edits to deploy/.env, not missing files. Telling an operator
    # to re-run this script against those is a remedy that reports success and changes nothing,
    # which is worse than no remedy at all. So the summary now points at the lines rather than
    # replacing them.
    log "Something above is missing or invalid, and api and worker will crash-loop by design"
    log "(fail-closed, never a fallback key). Read the individual lines: those naming a FILE are"
    log "fixed by injecting, those naming a variable are fixed by editing deploy/.env."
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
  # THE RESOLUTION STAYS HERE, THE MEASUREMENT DOES NOT. What the two callers resolve genuinely
  # differs — this one resolves a TAG out of the compose file with a human driving and nothing
  # verified yet, while reconcile passes the digest it has just attested — so sharing the
  # resolution would be sharing the wrong thing. The helper's own diagnostics reach stderr from
  # here, so this call adds no message of its own.
  "$RUNTIME_IDS" "$image"
}

# COMMAND substitution, not process substitution, and the `|| die` is not decoration: a `die`
# inside resolve_runtime_ids runs in the SUBSHELL and cannot stop this script. `< <(…)` would
# have swallowed the failure and left the pair empty.
ids_out=$(resolve_runtime_ids) || die "the container runtime ids could not be measured — nothing
has been written, and the injection is aborted rather than guessing an owner"
mapfile -t runtime_ids <<<"$ids_out"
uid="${runtime_ids[0]:-}"
gid="${runtime_ids[1]:-}"
# The seam's guard, not a second copy of the helper's contract: an empty pair would otherwise
# reach `install -o` and fail there, reporting a problem with install rather than with the
# measurement.
[[ "$uid" =~ ^[0-9]+$ ]] || die "measured uid is not numeric: '${uid}'"
[[ "$gid" =~ ^[0-9]+$ ]] || die "measured gid is not numeric: '${gid}'"
log "container runtime ids measured from the api image: uid=${uid} gid=${gid}"

install -d -m "$DIR_MODE" -o root -g "$gid" "$SECRETS_DIR"

# Destination and owner are parameters rather than globals: the two directories have different
# readers (a container process vs a host root process) and therefore different owners, and a
# single function that knew only one of them would have needed a second copy of the same careful
# install/printf sequence.
write_secret() {
  local dir="$1" owner_uid="$2" owner_gid="$3" name="$4" value="$5" path="$1/$4"
  # Created 0400 from the outset. A plain redirect would create the file under root's umask
  # (typically 0644) and only narrow it afterwards; the parent's 0710 closes that window in
  # practice, but the class is removable rather than merely bounded.
  install -m 0400 -o "$owner_uid" -g "$owner_gid" /dev/null "$path"
  # printf '%s' — no trailing newline is written at all. The reader trims as a backstop, but
  # writing exactly the bytes intended is the control; relying on the backstop is not.
  printf '%s' "$value" > "$path"
  log "wrote ${name} (${#value} chars, mode 0400, owner ${owner_uid}:${owner_gid})"
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
  write_secret "$SECRETS_DIR" "$uid" "$gid" "$KEY_ID_FILE" "$key_id"
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

  write_secret "$SECRETS_DIR" "$uid" "$gid" "$key" "$value"
  unset value
done

# The SES credentials. Same discipline as the loop above — terminal-only, never argv, skipped
# when already present — and the same abort on an empty value: an operator who has flipped the
# provider and then pressed Enter has produced precisely the state that stops the stack from
# booting, so it fails here, at the keyboard, rather than on the next restart.
#
# JBL_INJECT_SES EXISTS TO REMOVE A DOWNTIME WINDOW, and without it the flip cannot be performed
# without one. Both conditions that make the credentials required are also conditions that make
# the stack refuse to start while the files are absent: setting EMAIL_PROVIDER=Ses throws at
# registration, and setting a `_FILE` pointer throws on an unreadable path. So an operator
# editing `.env` first has already taken the box down before this script would offer to prompt.
# With the override the order inverts and the window closes: inject, THEN edit, then restart.
# Same shape as JBL_MASTER_KEY_ID below — an env var that answers a prompt the operator would
# otherwise have to reach by changing production state first.
if ses_credentials_required || [[ "${JBL_INJECT_SES:-}" == "1" ]]; then
  for key in "${SES_SECRET_KEYS[@]}"; do
    if has_usable_content "${SECRETS_DIR}/${key}"; then
      log "${key} already present — skipping (remove the file first to replace it)"
      continue
    fi

    printf 'Value for %s: ' "$key" >&2
    read -rs value
    printf '\n' >&2
    [[ -n "${value//[[:space:]]/}" ]] || die "${key} was empty or whitespace-only — nothing
written, and the run is aborted so a partially injected directory is never mistaken for a
complete one"

    write_secret "$SECRETS_DIR" "$uid" "$gid" "$key" "$value"
    unset value
  done
else
  log "EMAIL_PROVIDER is not Ses in ${ENV_FILE} and no Email__Ses__*_FILE pointer is set — SES"
  log "credentials not prompted for. To place them BEFORE the flip (which is the order that"
  log "avoids downtime), re-run with JBL_INJECT_SES=1. The flip itself is release-checklist.md"
  log "§2.5 and is never this script's."
fi

# ---------------------------------------------------------------------------------------------
# The host-only secrets (#197). Same prompt discipline, different destination and a root owner:
# nothing in a container may read these, and the directory they land in is mounted nowhere.
# ---------------------------------------------------------------------------------------------
install -d -m "$HOST_DIR_MODE" -o root -g root "$HOST_SECRETS_DIR"

for key in "${HOST_SECRET_KEYS[@]}"; do
  if has_usable_content "${HOST_SECRETS_DIR}/${key}"; then
    log "${key} already present — skipping (remove the file first to replace it)"
    continue
  fi

  printf 'Value for %s (host-only, no container reads it): ' "$key" >&2
  read -rs value
  printf '\n' >&2
  [[ -n "${value//[[:space:]]/}" ]] || die "${key} was empty or whitespace-only — nothing
written, and the run is aborted so a partially injected directory is never mistaken for a
complete one"

  # The rclone credential is the base64 of a complete rclone config file. Catching a paste of
  # the RAW config here turns a nightly unit failure — noticed a day later, if at all — into an
  # immediate error with the operator still at the keyboard.
  if [[ "$key" == "Backup__RcloneConfigBase64" ]]; then
    printf '%s' "$value" | base64 -d > /dev/null 2>&1 \
      || die "${key} is not valid base64. It is the base64 OF an rclone config file, not the
config itself: produce it with  base64 -w0 < rclone.conf"
  fi

  write_secret "$HOST_SECRETS_DIR" root root "$key" "$value"
  unset value
done

log ""
log "Injected. api and worker recover on their own restart backoff (restart: unless-stopped) —"
log "no 'compose up' and no reconcile run is needed. Verify with:"
log "  sudo $0 --check"
log "  docker inspect -f '{{.State.Health.Status}}' jobbliggaren-api    # expect: healthy"
