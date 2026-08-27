#!/usr/bin/env bash
# jobbliggaren-inject-secrets — write the crypto secrets to tmpfs after a boot, and (--check)
# detect their absence and any drift in the at-rest protection around them
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

# Read for two things. The mail configuration — the provider, the two credential pointers and the
# region — whose four answers decide whether the Scaleway credentials are required and whether the
# stack can start at all; and, since #1320, the file's own owner and mode.
readonly ENV_FILE=/opt/jobbliggaren/deploy/.env

# The OWNER that file must have (#1320). Every permanent plaintext credential the stack has rests
# on its posture — the count is deliberately not written here, because it has already decayed once:
# "seven" was measured 2026-08-10 and #1312 appended two Seq values the next day. Read
# `deploy/.env.example` for the current set. Until this constant existed the posture was
# prescribed in four places and read by none:
# `deploy/.env.example:1`, `deploy/docker-compose.yml`, `docs/runbooks/vps-deploy-stack.md` §2
# and ADR 0049 §B-1. All four are prose. Nothing in this repo WRITES the file — the operator
# copies the template by hand on the box — so there is no setter to give the duty to, and a
# setter could not see the later `chmod` anyway.
#
# ITS OWN CONSTANT, NOT SECRETS_DIR_OWNER. Both are 0 today, and a single constant would assert
# that the secrets directory's owner and this file's owner are one decision. They are not: one is
# set by this script, the other by an operator following a runbook, and a future divergence must
# be expressible without editing an assertion that was never about it.
readonly ENV_FILE_OWNER=0

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

# The directory OWNER the same `install -d` sets, and — since #1319 — reads back. It is declared
# here, beside DIR_MODE, for the reason DIR_MODE's own assertion states: two literals for one
# value is how an assertion quietly keeps checking the old one after the setter changes. `-o` and
# the comparison below now share this one.
#
# ROOT, NOT MERELY "NOT THE CONTAINER'S UID". The directory is 0710, so whoever owns it may
# create, rename and unlink inside it — over the master key and the three peppers. A directory
# left `0710 <container-uid>:<gid>` keeps every bit this box already checks intact (mode 0710
# here, group and file owners in jobbliggaren-reconcile.sh's gate) while handing substitution of
# the master key to any host-side process running as that uid.
#
# NUMERIC, BECAUSE `stat -c '%u'` ANSWERS IN NUMBERS. A name here would need a second normaliser
# to compare against, which is the drift this constant exists to prevent. `install` takes the
# numeric form — measured 2026-08-26 in debian:trixie-slim (GNU coreutils 9.7):
# `install -d -m 0710 -o 0 -g 1654` yields `0:1654 710`.
readonly SECRETS_DIR_OWNER=0

# The .NET configuration keys, which ARE the file names (docker-compose.yml's x-app-secrets
# anchor points at these paths, and the reader maps `__` to the section delimiter). Adding a
# secret here is the whole change on the host side.
readonly -a SECRET_KEYS=(
  "FieldEncryption__LocalMasterKeyBase64"
  "AuditPseudonymization__PepperBase64"
  "CompanyWatchPseudonymization__PepperBase64"
  "CvReviewFingerprintPseudonymization__PepperBase64"
)

# THE SCALEWAY CREDENTIALS ARE CONDITIONALLY REQUIRED, WHICH IS WHY THEY ARE NOT IN SECRET_KEYS.
# They are needed under EITHER of the two conditions scaleway_credentials_required enumerates
# below — the provider being Scaleway is only one of them — and the injection half prompts under
# a third, JBL_INJECT_SCALEWAY=1. Listing them above would put a permanent
# MISSING on jobbliggaren-secrets-present.service — the box's only alarm surface — for a state
# that is correct: before the flip there is nothing to inject and the stack is healthy without
# them. An alarm that is always on is an alarm nobody reads, so the condition is expressed
# rather than the entry added. The condition has one home: scaleway_credentials_required below.
#
# TWO SECRETS WITH SEPARATE LIFECYCLES, NOT TWO HALVES OF ONE (#183). SecretKey authenticates the
# caller and is rotated at the Scaleway console; ProjectId selects the project the mail is billed
# and attributed to and changes only if the project does. Neither is derivable from the other, so
# AddEmailSender requires each independently and names which one is missing — and each therefore
# gets its own file, its own `_FILE` pointer and its own row here. The retired SES arm's pair were
# two halves of one IAM credential and could legitimately have shared a lifecycle; these cannot.
readonly -a SCALEWAY_SECRET_KEYS=(
  "Email__Scaleway__SecretKey"
  "Email__Scaleway__ProjectId"
)

# THE EXPIRY LEAD TIME, AND WHY AN EXPIRY CHECK EXISTS AT ALL (#183 E4, security-auditor Major 3).
# Scaleway caps an API key's life at one year and there is no instance-role equivalent, so the
# SecretKey above is long-lived by construction and dies on a DATE. Nothing on this box would
# notice: --check measures PRESENCE and an expired key is present; ScalewayEmailSender's
# CanDeliver is an unconditional `true`, so AuthOptionsValidator's sender interlock passes; and
# every send site fails per-message and silently by design. The stack stays green while mail
# stops — and after the registration gate opens, a locked-out user's only recovery channel is the
# mail that never comes, with kontakt@ a measured blackhole behind it.
#
# TWO HALVES, AND ONLY ONE OF THEM IS THIS FILE'S (senior-cto-advisor, binding 2026-08-16).
#
#   THE SILENCE — an expired key that nothing reports — is a FAULT, and faults belong on the fault
#   surface. EXPIRED, unset and unparseable exit non-zero into systemctl --failed.
#
#   THE ADVANCE WARNING — "a human owes a console errand on a date" — is a CALENDAR OBLIGATION,
#   not a fault, and it is #1267's class verbatim ("Detta är en kalenderförpliktelse, inte
#   detektion"). The Scaleway key is that class's second instance; its date is registered in
#   docs/runbooks/master-key-ops.md, which satisfies #1267 AC 1. AC 2 — the reminder's delivery —
#   is not built, and this file says so rather than pretending to cover it.
#
# WHY THE WARNING MAY NOT EXIT NON-ZERO, AND THE REASON IS NOT ALARM FATIGUE. systemctl --failed
# LATCHES, and jobbliggaren-heartbeat.sh notifies on the TRANSITION into failure: while the box is
# already red a new, genuine fault changes only the body — which nobody reads, because no
# transition announced it. A lead time on the fault surface therefore disables P1-P5's ability to
# notify for the whole window. That is a detection regression bought for a maintenance reminder,
# and it is SCALE-INVARIANT: 7 days or 1 day shrinks the window, not the class.
#
# 90 DAYS, AND THE NUMBER CHANGED MEANING WITH THE SURFACE. It is no longer an alarm lead time but
# a JOURNAL VISIBILITY WINDOW: the notice costs nothing, so the only thing that matters is the
# chance that some ATTENDED --check run (a flip, a key visit, a deploy) falls inside it. Attended
# visits are irregular, so the window widens rather than narrows. The real lead time lives on
# #1267, where it is a reminder and not a page.
#
# DELIBERATELY NOT AN IAM LOOKUP. Asking Scaleway for expires_at would need either IAM read on the
# sending key — widening it past TEM-send, which is the boundary the org/project binding exists to
# hold (release-checklist.md §2.5 punkt 1 forutsattning 1) — or a second credential with its own
# lifecycle. A static date is weaker information and a stronger control: no network, no
# credential, and it fails closed when unset.
#
# NAMED EXPIRY_NOTICE_DAYS, never *_WARN_DAYS: "warn" reads as an alarm lead time, and that is how
# the semantics would drift back onto the fault surface.
readonly EXPIRY_NOTICE_DAYS=90

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
# WHAT WAS MEASURED IS THE FORM, NOT THE VALUE, and the distinction survives the provider swap
# intact. Against Compose v2.40.3 on 2026-08-12 all five forms below rendered one and the same
# value; delimiter, quoting, `export` and inline-comment handling are properties of compose's
# parser and are blind to which provider name sits on the right-hand side. The literal is spelled
# `Scaleway` here because that is the value a reader will actually meet (#183) — the 2026-08-12
# date belongs to the parser behaviour, never to that string. The four forms beyond the first are
# why this function exists rather than a single `sed`:
#
#   EMAIL_PROVIDER=Scaleway            EMAIL_PROVIDER: Scaleway     (delimiter = or :)
#   EMAIL_PROVIDER=Scaleway # flippat  EMAIL_PROVIDER="Scaleway" #q (inline comment, quoted or not)
#   export EMAIL_PROVIDER=Scaleway                                  (export prefix)
#
# `tail -n1` is compose's last-assignment-wins, measured.
#
# A QUOTED VALUE KEEPS ITS `#`, and a naive strip got that backwards in the fail-OPEN direction
# (security-auditor, 2026-08-12). Measured: `EMAIL_PROVIDER='Ses # x'` rendered `Ses # x`, a value
# AddEmailSender throws on — so a reader that stripped it to `Ses` answered "configured for mail"
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
# on it either, and a detector that answered "not Scaleway" would go green on a stack that cannot
# start.
#
# `Ses` IS SUCH A VALUE NOW, AND ITS ARM WAS DELETED RATHER THAN REPOINTED (#183, E1 landed as
# b71c14de). While the SES arm existed this function answered `ses` and the box booted on it.
# AddEmailSender's SES branch is gone: `Ses` reaches the same `else throw` as `Resend` before it,
# pinned by AddEmailSenderGateTests. So `Ses` falls to the catch-all below and --check reports
# INVALID — which is the truth about a box carrying that value, and is what makes this the
# fail-CLOSED direction. Repointing `ses` at `console` would have been the opposite: a green
# alarm over a stack that refuses to start, on the box's only alarm surface, and reachable by
# exactly the operator most likely to type it — one following a stale instruction. It is
# security-auditor's Major 1 from E1 (`docs/reviews/2026-08-15-183-scaleway-arm-security-auditor.md`),
# and it is closed HERE rather than by anything the flip does later, because the mitigation it had
# ("unreachable until E2 writes a provider value") expires in the same commit that teaches this
# file to write one.
#
# Case-insensitive because AddEmailSender compares with OrdinalIgnoreCase
# (DependencyInjection.cs). A guard stricter than the code it guards is the dangerous direction:
# on `EMAIL_PROVIDER=scaleway` it would decline to prompt while the application accepted the value.
#
# Compose reads the shell environment BEFORE `.env` (precedence: shell > --env-file > .env), so
# an exported variable would defeat this. Measured 2026-08-12: jobbliggaren-reconcile.service
# carries neither `Environment=` nor `EnvironmentFile=`, so nothing in the deploy exports it and
# `.env` is in practice the only source.
email_provider() {
  case "$(env_value EMAIL_PROVIDER | tr '[:upper:]' '[:lower:]')" in
    "" | console) printf 'console' ;;
    scaleway)     printf 'scaleway' ;;
    *)            printf 'unknown' ;;
  esac
}

# REQUIRED BY EITHER OF TWO INDEPENDENT CAUSES, and binding only to the first was a defect.
#
# (1) The provider is Scaleway: AddEmailSender throws at registration without the credentials.
# (2) A `_FILE` pointer is set at all: EnvFileSecretsConfiguration throws on a pointer naming a
#     path it cannot read, and it never consults Email:Provider. So a rollback of the flip that
#     leaves the pointers behind is a boot refusal with a provider that is no longer Scaleway —
#     the state a provider-only predicate reports as healthy.
#
# BOTH POINTERS ARE READ, and that is cause (2)'s shape rather than a pair of spellings: the two
# secrets have separate lifecycles (see SCALEWAY_SECRET_KEYS), so an operator can leave one behind
# while removing the other, and either survivor alone is the boot refusal this predicate exists to
# anticipate.
scaleway_credentials_required() {
  [[ "$(email_provider)" == "scaleway" ]] && return 0
  [[ -n "$(env_value EMAIL_SCALEWAY_SECRET_KEY_FILE)" ]] && return 0
  [[ -n "$(env_value EMAIL_SCALEWAY_PROJECT_ID_FILE)" ]] && return 0
  return 1
}

# THE ABSENCE DETECTORS, AND THERE ARE TWO OF THEM — ONE PER SET, ONE PER OWNER (#1329).
#
# `--check` answers for everything api and worker read: the crypto secrets, their directory, and
# the mail configuration that decides whether the stack boots at all. `jobbliggaren-secrets-
# present.service` runs it.
#
# `--check-host` answers for the host-only set — today #197's backup credential, read by no
# container. `jobbliggaren-host-secrets-present.service` runs it.
#
# WHY THEY ARE SEPARATE, and it is not tidiness. One predicate over both sets meant one exit
# code, one unit and one alarm for two sets with different owners (#198 and #197), different
# severity (the box is down / the nightly backup is), and different provisioning lifecycles. The
# cost was measured twice: the summary could not be true of both at once (#1328), and the timer
# could not be enabled at all until the LOWER-severity set was provisioned, because its absence
# failed the whole predicate (#1329). Split, the crypto alarm arms on the day the crypto secrets
# exist and owes #197 nothing.
#
# NEITHER MAY TOUCH DOCKER. Both run at boot, when dockerd may not be up; they stat files and
# read deploy/.env, and nothing else. The file-name arrays stay in this one file so that adding
# a secret remains the whole change on the host side, for either destination.
if [[ "${1:-}" == "--check" ]]; then
  # Same guard, and the same spelling, as the --check-host branch below and as
  # jobbliggaren-backup.sh's --check. Measured 2026-08-13 in debian:trixie-slim against the state
  # #198's cutover left behind — crypto present, rclone credential absent — with this line absent:
  # `--check --host` and `--check host` both matched HERE, swept the crypto set, ignored the rest
  # and exited 0 saying "all secrets present". The operator asked about the host-only set and was
  # given a green light for the other one, which is #1328's defect class — one answer covering two
  # disjoint sets — pointing the other way. The closing `Verify with:` block prints both flags
  # side by side, so the two spellings are one keystroke apart at exactly the moment they matter.
  [[ $# -eq 1 ]] || die "unknown argument '$2' (use --check on its own)"
  missing=0
  # A SECOND FLAG, NOT A SECOND USE OF `missing`, and the reason is a sentence rather than style.
  # The `missing` summary below tells the operator api and worker crash-loop. That claim is true of
  # every branch that sets it and FALSE of an expiring key: the stack serves perfectly while mail
  # dies. Folding this into `missing` would print a false diagnosis at the one moment the operator
  # most needs a true one — the same defect #1328 measured, where a shared predicate made the
  # crash-loop sentence false for the host-only set.
  expiring=0

  # A THIRD FLAG, AND THE THIRD SUMMARY BELOW IS WHY IT IS NOT `missing` (#1319, #1320). A posture
  # fault means every secret is present and readable, the stack is serving, and mail is fine —
  # what has drifted is the at-rest protection AROUND those secrets. Routing it into `missing`
  # would print "api and worker will crash-loop" over a healthy box, which is #1328's defect class
  # exactly: an operator who reads a crash-loop alarm and then finds api serving learns to discount
  # the alarm, and the next real one is ignored. `expiring` exists for the same reason.
  posture=0

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

    # THE DIRECTORY'S OWNER (#1319), read back from the constant `install -d -o` set it with.
    # Before this arm nothing on this box read it: --check read the mode two lines above,
    # jobbliggaren-reconcile.sh's gate reads the directory's GROUP and the files' owner and mode,
    # and /etc/tmpfiles.d/jobbliggaren.conf marks owner create-only so it can neither revoke nor
    # re-assert it. A directory left `0710 <container-uid>:<gid>` passed all three.
    #
    # A SEPARATE `stat`, NOT `%u %a` IN ONE, because the two arms report different faults under
    # different summaries: the mode arm is a crash-loop (the container cannot traverse), this one
    # is a posture fault (it traverses perfectly, and the owner can substitute the key). One
    # measurement would tempt one message for two states.
    #
    # AN UNREAD OWNER IS NOT A WRONG OWNER, and the mode arm's `?` sentinel cannot be borrowed
    # here: this arm publishes a command, and `sudo chown 0:? <dir>` is one its addressee cannot
    # run (measured: `chown: invalid group: '0:?'`) — the #1317 class. jobbliggaren-reconcile.sh
    # already spells this state CANNOT ANSWER and keeps it distinct from a refusal.
    if ! dir_uid=$(stat -c '%u' "$SECRETS_DIR" 2>/dev/null); then
      log "CANNOT ANSWER: $SECRETS_DIR exists but its owner could not be read, so whether root"
      log "               still owns the directory holding the master key is unknown."
      posture=1
    elif [[ "$dir_uid" != "$SECRETS_DIR_OWNER" ]]; then
      # The group is preserved rather than prescribed: this arm is about the owner, and the group
      # is jobbliggaren-reconcile.sh's to judge against the image about to run. A repair naming a
      # group would be guessing at the other gate's answer. No fallback: reaching here means the
      # stat above succeeded.
      dir_gid=$(stat -c '%g' "$SECRETS_DIR")
      log "WRONG OWNER: $SECRETS_DIR is owned by uid $dir_uid, expected $SECRETS_DIR_OWNER (root)."
      log "             THIS DOES NOT BLOCK THE CONTAINER: the directory is ${DIR_MODE#0}, so it"
      log "             still traverses by group and still reads each file as its owner. What the"
      log "             wrong owner holds is create, rename and unlink INSIDE it — the master"
      log "             key and the three peppers can be SUBSTITUTED without changing a mode bit"
      log "             and without this box noticing."
      log "             Repair the DIRECTORY only; the files keep the container's uid:"
      log "               sudo chown ${SECRETS_DIR_OWNER}:${dir_gid} $SECRETS_DIR"
      posture=1
    fi
  fi

  for key in "${SECRET_KEYS[@]}" "$KEY_ID_FILE"; do
    if ! has_usable_content "${SECRETS_DIR}/${key}"; then
      log "MISSING: ${SECRETS_DIR}/${key}"
      missing=1
    fi
  done

  # deploy/.env's POSTURE (#1320). ENV_FILE is this script's own constant, and this branch already
  # reaches the file — though read the precision, because the looser claim is false: env_value
  # reads it only from the mail branch below and only when it is readable, so before this arm a
  # --check run could complete without ever having opened it. After this arm every run stats it.
  #
  # WHAT RESTS ON IT: every permanent plaintext credential in that file. master-key-ops.md records
  # that they stay in plaintext deliberately; a named non-goal is not a reason to leave the one
  # control unread.
  #
  # OWNER AND MODE IN ONE `stat`, because neither is the property alone. `0600 someuser:someuser`
  # is a flawless mode over credentials someuser reads, and a mode check alone would report it
  # healthy — the half-a-property class this repo has been caught by before.
  #
  # `-L`, BECAUSE `chmod` FOLLOWS AND `stat` DOES NOT. Measured: a symlinked .env pointing at a
  # correct 0600 file reports the LINK's 0777 and refuses, and the repair this arm publishes then
  # changes the target — which was already right — so the alarm cannot be cleared. Rows 30/32b of
  # vps-deploy-stack.md: a gate that cannot clear itself is worse than none. `-e` above follows
  # links too, so a dangling one is an absence and the two agree.
  #
  # A MASK, NOT `== 600`. The property is "no non-root reader", not an opinion about permissions;
  # jobbliggaren-reconcile.sh wrote that precedent for the files' 0400. 0400 and 0600 pass, 0640
  # and 0644 do not, and the four prose prescriptions stay true without an edit.
  # CONDITIONAL ON THE FILE EXISTING, AND THAT IS NOT A HOLE. The property asserted here is
  # "the plaintext credentials in this file have no non-root reader". With no file there are no
  # credentials on disk and the property is vacuously satisfied — nothing is exposed by a file
  # that is not there. Fail-open would be a file that EXISTS, is world-readable, and reads green;
  # that is what the arms below refuse.
  #
  # WHAT AN ABSENT .env MEANS FOR THE STACK IS A DIFFERENT QUESTION with a different remedy, and
  # deliberately not this detector's: injection does not create the file, so routing it into the
  # summary below would prescribe a remedy that reports success and changes nothing.
  if [[ ! -e "$ENV_FILE" ]]; then
    :
  elif env_meta=$(stat -L -c '%u %a' "$ENV_FILE" 2>/dev/null); then
    env_uid="${env_meta% *}"
    env_mode="${env_meta#* }"
    if [[ "$env_uid" != "$ENV_FILE_OWNER" ]]; then
      log "WRONG OWNER: $ENV_FILE is owned by uid $env_uid, expected $ENV_FILE_OWNER (root)."
      log "             THIS DOES NOT BLOCK COMPOSE — it reads the file as root either way. What"
      log "             the wrong owner gains is READ access to every plaintext credential in it."
      log "               sudo chown ${ENV_FILE_OWNER}:${ENV_FILE_OWNER} $ENV_FILE"
      posture=1
    fi
    if (( (8#$env_mode & 8#0077) != 0 )); then
      log "WRONG MODE: $ENV_FILE is $env_mode — it grants group or other a bit. Expected none outside"
      log "            the owner's: 0600 is what the runbook prescribes, and 0400 passes too."
      log "            THIS BLOCKS NOTHING — it widens who may READ every plaintext credential in"
      log "            it beyond root."
      log "              sudo chmod 0600 $ENV_FILE"
      posture=1
    fi
  else
    # THE FILE IS THERE AND ITS PROTECTION IS UNKNOWN — the one state that must not read green,
    # and the reason this is an `elif` chain rather than a bare `stat`. It is a different answer
    # from the absence above: something on disk holds those credentials and this check could not
    # measure who may read it. "I could not measure the protection" and "the protection is
    # correct" are exactly the two answers this arm exists to keep apart.
    #
    # env_value's own `[[ -r "$ENV_FILE" ]] || return 0` fails OPEN by design, and correctly so
    # for its question (an unset variable is compose's ${EMAIL_PROVIDER:-Console} default). A
    # posture assertion must not inherit that.
    log "CANNOT ANSWER: $ENV_FILE exists but could not be stat'ed, so who may read it is unknown."
    log "               This is not a statement that it is wrong. The stack's plaintext credentials"
    log "               live in that file and this check could not measure their protection."
    posture=1
  fi

  # WHAT THE MAIL BRANCH SEES: every file and every .env line that is ABSENT, plus the TWO values
  # it reads — EMAIL_PROVIDER's, through email_provider above, and EMAIL_SCALEWAY_KEY_EXPIRES_AT's,
  # in the expiry branch below. What it never reads is the VALUE of the pointers and the region, so
  # a misspelt pointer path, or a region outside the allow-list, still reads as healthy here.
  #
  # THE RULE IS "NO VALUE WITH A SECOND READER", NOT "NO VALUES" (amended 2026-08-16, #183 E4).
  # That was always the reason: pointers and region are refused inside AddScalewayEmailClient at
  # registration, so re-reading them here would only duplicate a check the stack already fails
  # loudly on. An EXPIRED KEY IS REFUSED NOWHERE — CanDeliver is unconditionally true and every
  # send fails per-message — so that value has no second reader and must be read here or nowhere.
  # The count above is the rule's own tripwire: a third value added without a reason of this shape
  # makes this sentence false.
  #
  # THE LINE IS PREDICATE vs REPORT, NOT "one normaliser per rule" (dotnet-architect, PR #1341).
  # email_provider() is already a second spelling of AddEmailSender's switch and is kept, so that
  # argument cannot be what separates the two cases. What separates them: this script NEEDS the
  # provider to decide its own control flow — which files to demand, whether to prompt — while a
  # region verdict would steer nothing here and be a pure report. And the drift directions differ.
  # A list that GROWS (Scaleway adding a region) leaves a stale bash copy failing CLOSED: noise, an
  # operator argues with it. The provider list SHRANK, which left the old copy failing OPEN — a
  # green alarm over a box that cannot boot, which is the damage this PR repairs.
  #
  # A wrong region is therefore not undetected, only detected elsewhere: EnsureAllowedRegion runs
  # inside AddScalewayEmailClient, so it is a REGISTRATION refusal like every other mail
  # misconfiguration here, never a 404 on the first live send. The unit exists because a
  # crash-looping container does NOT appear in `systemctl --failed`, so a boot refusal this file
  # can see and does not report is a green alarm over a dead box — the failure this whole script
  # is built against.
  env_provider=$(email_provider)

  if [[ "$env_provider" == "unknown" ]]; then
    log "INVALID: EMAIL_PROVIDER='$(env_value EMAIL_PROVIDER)' in ${ENV_FILE} is neither Console"
    log "         nor Scaleway. AddEmailSender's switch ends in a throw, so api and worker refuse"
    log "         to START on this value — this is not a quieter way of saying Console. Note that"
    log "         'Ses' (#183) and 'Resend' (ADR 0124) are in this class: their arms were deleted,"
    log "         not repointed, so a stale instruction naming either takes the box down."
    missing=1
  fi

  if scaleway_credentials_required; then
    for key in "${SCALEWAY_SECRET_KEYS[@]}"; do
      if ! has_usable_content "${SECRETS_DIR}/${key}"; then
        log "MISSING: ${SECRETS_DIR}/${key} — required because ${ENV_FILE} has"
        log "         EMAIL_PROVIDER=Scaleway or a Email__Scaleway__*_FILE pointer set. api and"
        log "         worker refuse to START (AddEmailSender throws at registration, not at the"
        log "         first send). Re-run this script without arguments to inject it."
        missing=1
      fi
    done
  fi

  # The other half of the same boot refusal: the files can be present while the .env lines that
  # deliver them are not. Checked only under Scaleway, because only then is each one required —
  # and each is named separately, since "email is broken" costs an operator the hour that naming
  # the variable saves.
  if [[ "$env_provider" == "scaleway" ]]; then
    for var in EMAIL_SCALEWAY_SECRET_KEY_FILE EMAIL_SCALEWAY_PROJECT_ID_FILE EMAIL_SCALEWAY_REGION; do
      if [[ -z "$(env_value "$var")" ]]; then
        log "MISSING: ${var} is unset in ${ENV_FILE} while EMAIL_PROVIDER=Scaleway. The"
        log "         credential files can be injected and api and worker will still refuse to"
        log "         START: an unset pointer reads as 'not configured', which AddEmailSender"
        log "         throws on."
        if [[ "$var" == "EMAIL_SCALEWAY_REGION" ]]; then
          log "         This one must be fr-par — the only region ScalewayClientRegistration's"
          log "         allow-list admits. Its VALUE is not read here; a wrong one is refused at"
          log "         registration, so the box says so on the next start rather than here."
        fi
        missing=1
      fi
    done

    # THE KEY'S DEATH DATE — the one place on this box that can see it coming (see
    # EXPIRY_NOTICE_DAYS above for why it is a stored date and not an IAM lookup).
    #
    # THIS BRANCH READS A VALUE, which every other line in --check deliberately does not. That is
    # not the rule being broken but the rule's reason not applying: the pointers and the region are
    # refused at registration, so reading them here would duplicate a check the stack already
    # fails loudly. An expired key is refused NOWHERE — that is the whole finding — so this value
    # has no second reader and must be read here or nowhere.
    expiry=$(env_value EMAIL_SCALEWAY_KEY_EXPIRES_AT)
    if [[ -z "$expiry" ]]; then
      log "MISSING: EMAIL_SCALEWAY_KEY_EXPIRES_AT is unset in ${ENV_FILE} while"
      log "         EMAIL_PROVIDER=Scaleway. Read the date in the Scaleway console (IAM -> API"
      log "         keys -> the key -> Expiration) and set it as YYYY-MM-DD. Unset fails closed"
      log "         on purpose: the alternative is an alarm that cannot fire, which is the state"
      log "         this check was added to end."
      expiring=1
    elif [[ ! "$expiry" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] \
      || ! expiry_epoch=$(date -u -d "$expiry" +%s 2>/dev/null); then
      log "INVALID: EMAIL_SCALEWAY_KEY_EXPIRES_AT='${expiry}' in ${ENV_FILE} is not a calendar"
      log "         date this script accepts. Use YYYY-MM-DD. A value it cannot read is treated"
      log "         as a failure, never as 'no expiry' — an unreadable date must not read as safe."
      # THE SHAPE GUARD RUNS FIRST, AND IT CLOSES A FAIL-OPEN THAT `date` ALONE LEAVES WIDE.
      # `date -d` accepts relative forms: `nextyear`, `+1 year`, `tomorrow` all parse, and each
      # resolves against the CURRENT clock, so the remaining days never shrink and the notice
      # never fires — a value that is silently self-renewing. That is the one failure direction
      # this check cannot afford, because it is indistinguishable from a healthy key.
      expiring=1
    else
      remaining_days=$(( (expiry_epoch - $(date -u +%s)) / 86400 ))
      # `<= 0` rather than `< 0`: integer division truncates toward zero, so a key that died
      # earlier TODAY yields 0 and would otherwise be reported by the notice branch — a softer
      # sentence for a harder state, for one day a year.
      if (( remaining_days <= 0 )); then
        log "EXPIRED: the Scaleway API key expired on ${expiry}."
        log "         Outbound mail is failing SILENTLY right now — api and worker are healthy,"
        log "         --check finds every file present, and each send fails per-message. If the"
        log "         registration gate is open, account confirmation and password reset are both"
        log "         dead and the published rights channel does not receive."
        expiring=1
      elif (( remaining_days <= EXPIRY_NOTICE_DAYS )); then
        # NOTICE, NOT A FAULT — and it deliberately does NOT set `expiring`, so this run still
        # exits 0. See EXPIRY_NOTICE_DAYS above: a lead time on a LATCHING surface suppresses the
        # transition every other predicate needs in order to notify. The line says so itself,
        # because an operator who reads a warning must not have to infer the exit code from it.
        log "NOTICE: the Scaleway API key expires on ${expiry}, in ${remaining_days} day(s)."
        log "        NOTHING IS FAILING — this run exits 0 and the box stays green. It is a"
        log "        calendar obligation (#1267), not a fault. Generate a replacement in the"
        log "        Scaleway console, re-run this script without arguments to inject it, update"
        log "        EMAIL_SCALEWAY_KEY_EXPIRES_AT in the same pass, and restart api and worker."
        log "        No page will chase you: the reminder half of #1267 is not built."
      fi
    fi
  fi

  # THIS SUMMARY USED TO PRESCRIBE THE ONE REMEDY, and it stopped being the one remedy when
  # the mail branch above gained lines injection cannot fix — an INVALID provider value and an
  # unset EMAIL_SCALEWAY_* variable are edits to deploy/.env, not missing files. Telling an operator
  # to re-run this script against those is a remedy that reports success and changes nothing,
  # which is worse than no remedy at all. So the summary points at the lines rather than
  # replacing them.
  #
  # IT IS NOW TRUE OF EVERY ROUTE THAT REACHES IT, which is what the split bought. While the
  # host-only set shared this predicate the sentence was false whenever only that set was absent
  # — the state #198's cutover left behind, measured on the box 2026-08-13 with api and web
  # healthy (#1328). Every branch above stops api and worker from starting, so the crash-loop
  # wording holds for all of them and needs no second summary to qualify it.
  if [[ $missing -ne 0 ]]; then
    log "Something above is missing or invalid, and api and worker will crash-loop by design"
    log "(fail-closed, never a fallback key). Read the individual lines: those naming a FILE are"
    log "fixed by injecting, those naming a variable are fixed by editing deploy/.env."
    log "  sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh"
  fi

  # ITS OWN SENTENCE, AND THE DISTINCTION IS THE POINT: in this state the stack is HEALTHY. An
  # operator who reads the crash-loop summary above and then finds api serving would conclude the
  # alarm is wrong and learn to discount it — which is how a real one gets ignored later.
  #
  # ONLY EXPIRED / UNSET / UNREADABLE REACH THIS. The advance notice exits 0 and never gets here,
  # by the latching argument in EXPIRY_NOTICE_DAYS — so every state that does reach it is one
  # where mail is ALREADY dead, not one where it will be.
  if [[ $expiring -ne 0 ]]; then
    log "The key line above is NOT a crash-loop: api and worker serve normally and only outbound"
    log "mail is affected. It exits non-zero because mail is dead NOW — an expired key, or a"
    log "date this script cannot read and therefore cannot vouch for. systemctl --failed is this"
    log "box's only fault surface, and a silent mail outage no surface carries is worse than a"
    log "loud one an operator can triage."
  fi

  # ITS OWN SENTENCE, AND IT ASSERTS NO HEALTH FACT — which is the difference between this block
  # and the `expiring` one above. `expiring`'s fault class ENTAILS a serving stack, so it may say
  # so; this one can be reached while `missing` is also set, and --check-host's comment records
  # what a health claim made from this unit is worth ("it cannot know whether they are serving …
  # that claim, made from here, is what #1328 measured as false").
  #
  # SO THE SENTENCE DOES THE OTHER JOB INSTEAD, which was always the real one: an operator who
  # reads an alarm, finds api serving, and concludes the alarm is wrong will discount the next
  # one too. Naming that inference is what prevents it — and unlike a health claim, it is true on
  # every branch that reaches here.
  if [[ $posture -ne 0 ]]; then
    log "The posture line(s) above are about the PROTECTION around the secrets, not their presence,"
    log "and none of them says api, worker or mail is failing. Do not read a serving stack as"
    log "evidence that this alarm is wrong — that inference is the one thing this line exists to"
    log "prevent. It exits non-zero because systemctl --failed is this box's only fault surface."
  fi

  if [[ $missing -ne 0 || $expiring -ne 0 || $posture -ne 0 ]]; then
    exit 1
  fi
  log "all secrets present in ${SECRETS_DIR}"
  exit 0
fi

# THE HOST-ONLY DETECTOR. Same file-name arrays, same has_usable_content predicate, same
# no-docker rule — a different set, a different unit and a different severity.
#
# ITS ABSENCE IS NOT A BOOT FAILURE, and the wording says so without claiming anything about the
# stack: this check reads nothing api or worker read, so it cannot know whether they are serving.
# That claim, made from here, is what #1328 measured as false.
if [[ "${1:-}" == "--check-host" ]]; then
  # Same guard, and the same spelling, as jobbliggaren-backup.sh's --check branch: a first
  # argument that matched never reaches the catch-all below, so a trailing argument would be
  # swallowed here rather than refused.
  [[ $# -eq 1 ]] || die "unknown argument '$2' (use --check-host on its own)"
  host_missing=0

  # NO DIRECTORY-MODE ASSERTION HERE, and the asymmetry with --check is the tmpfiles file's, not
  # an omission. jobbliggaren-tmpfiles.conf writes `d /run/jobbliggaren/secrets :0700 :root :root`
  # — the `:` prefix makes mode and owner create-only, precisely so a later `systemd-tmpfiles
  # --create` cannot revoke the 0710 grant the injection sets, which is why --check must measure
  # that directory's mode. The host-only line carries NO `:` prefix, so tmpfiles re-asserts
  # 0700 root:root on it at every boot instead. There is no drifted mode for this branch to catch
  # that the box does not already repair.
  if [[ ! -d "$HOST_SECRETS_DIR" ]]; then
    log "MISSING: $HOST_SECRETS_DIR (directory does not exist)"
    host_missing=1
  fi
  for key in "${HOST_SECRET_KEYS[@]}"; do
    if ! has_usable_content "${HOST_SECRETS_DIR}/${key}"; then
      log "MISSING: ${HOST_SECRETS_DIR}/${key} — host-only, read by no container. The nightly"
      log "         backup cannot upload (#197): jobbliggaren-backup.service SKIPS its scheduled"
      log "         run on ConditionPathExists rather than failing, so this line is its alarm."
      host_missing=1
    fi
  done

  if [[ $host_missing -ne 0 ]]; then
    log "A host-only secret is absent. This says nothing about api and worker — run --check for"
    log "that. The consequence is the nightly backup (#197). Same script, which prompts for the"
    log "host-only set after the crypto secrets it may already be holding:"
    log "  sudo /opt/jobbliggaren/deploy/systemd/jobbliggaren-inject-secrets.sh"
    exit 1
  fi
  log "all host-only secrets present in ${HOST_SECRETS_DIR}"
  exit 0
fi

[[ $# -eq 0 ]] || die "unknown argument '$1' (no arguments to inject, --check for the secrets api
and worker read, or --check-host for the host-only set)"

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

install -d -m "$DIR_MODE" -o "$SECRETS_DIR_OWNER" -g "$gid" "$SECRETS_DIR"

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

# The Scaleway credentials. Same discipline as the loop above — terminal-only, never argv,
# skipped when already present — and the same abort on an empty value: an operator who has flipped
# the provider and then pressed Enter has produced precisely the state that stops the stack from
# booting, so it fails here, at the keyboard, rather than on the next restart.
#
# JBL_INJECT_SCALEWAY EXISTS TO REMOVE A DOWNTIME WINDOW, and without it the flip cannot be
# performed without one. Both conditions that make the credentials required are also conditions
# that make the stack refuse to start while the files are absent: setting EMAIL_PROVIDER=Scaleway
# throws at registration, and setting a `_FILE` pointer throws on an unreadable path. So an
# operator editing `.env` first has already taken the box down before this script would offer to
# prompt. With the override the order inverts and the window closes: inject, THEN edit, then
# restart. Same shape as JBL_MASTER_KEY_ID below — an env var that answers a prompt the operator
# would otherwise have to reach by changing production state first.
if scaleway_credentials_required || [[ "${JBL_INJECT_SCALEWAY:-}" == "1" ]]; then
  for key in "${SCALEWAY_SECRET_KEYS[@]}"; do
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
  log "EMAIL_PROVIDER is not Scaleway in ${ENV_FILE} and no Email__Scaleway__*_FILE pointer is"
  log "set — Scaleway credentials not prompted for. To place them BEFORE the flip (which is the"
  log "order that avoids downtime), re-run with JBL_INJECT_SCALEWAY=1. The flip itself is"
  log "release-checklist.md §2.5 and is never this script's."
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
# BOTH, because this run wrote to BOTH directories. --check stopped reading the host-only set at
# the split, so on its own it is a green light over the very credential the prompt above just
# took — the same mistake backup-restore.md §3 now warns about, one layer closer to the operator.
log "  sudo $0 --check        # the secrets api and worker read"
log "  sudo $0 --check-host   # the host-only set this run also wrote"
log "  docker inspect -f '{{.State.Health.Status}}' jobbliggaren-api    # expect: healthy"
