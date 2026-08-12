#!/usr/bin/env bash
# jobbliggaren-reconcile — pull the published images, prove they are ours, then apply.
#
# WHY A SCRIPT AND NOT TWO ExecStart LINES. The unit used to carry
# `ExecStartPre=/usr/bin/flock -n <file> /bin/true`, which is a no-op: flock releases the lock
# when /bin/true exits, i.e. inside ExecStartPre, before ExecStart ever runs. Holding a lock
# across a critical section requires one process that keeps the descriptor open for the whole
# section, which is this file. (systemd already serialises start jobs per unit, so the lock is
# not about the timer racing itself — it is about a HUMAN running docker compose by hand while
# a reconcile is mid-flight.)
#
# AND THE VERIFICATION MUST BE INSIDE THE LOCK. Pulling under the lock, releasing, then
# verifying would leave a window in which another actor moves the local tag between the check
# and the apply.
#
# THE ORDER IS PULL, VERIFY, APPLY — AND IT FAILS CLOSED. A refused image is never applied, so
# the containers already running keep running. That is "stale but serving", which is the right
# failure for a box whose only alarm channel is the journal (#1175: no log sink exists yet).
#
# THE BYPASS IS REAL AND IS NAMED HERE RATHER THAN PRETENDED AWAY. This script guards the path
# that goes through it. A `docker compose -f … up -d` typed by hand takes no lock and runs no
# verification, and after a refused run the local `latest` tag already points at the unverified
# image — so a manual apply would deploy exactly what was just refused. Manual applies go
# through `systemctl start jobbliggaren-reconcile.service`; the runbook says so in §3b.
set -euo pipefail

readonly COMPOSE_FILE=/opt/jobbliggaren/deploy/docker-compose.yml
# Not read here, and deliberately named: compose discovers `.env` beside the compose file on
# its own, and IMAGE_TAG (the rollback control) reaches the images through that discovery. A
# reader looking for where the pinned tag enters would otherwise find nothing at all.
readonly ENV_FILE=/opt/jobbliggaren/deploy/.env
readonly VERIFIER=/opt/jobbliggaren/deploy/systemd/verify-image-attestation.sh
readonly LOCK=/run/jobbliggaren-reconcile.lock
readonly STAMP=/var/lib/jobbliggaren/last-successful-reconcile

# The injected crypto secrets, and the shared measurement that says who may read them (#1295).
# SECRETS_DIR is a second literal — jobbliggaren-inject-secrets.sh declares it too — and that is
# named rather than hidden: COMPOSE_FILE already lives in both files, because these are
# standalone executables with no shared config, and inventing one for two path constants would
# cost more than it buys.
readonly SECRETS_DIR=/run/jobbliggaren/secrets
readonly RUNTIME_IDS=/opt/jobbliggaren/deploy/systemd/jobbliggaren-runtime-ids.sh

# Images built by our workflow, and therefore attestable. Everything else the compose file
# pulls must be on the allowlist below or this script refuses: an unknown image is the shape a
# future service arrives in, and it must fail closed rather than slip past unverified.
readonly OURS_PREFIX="ghcr.io/klasolsson81/jobbliggaren-"

# Upstream images we deliberately do not verify, named one by one. Attestations for these are
# not ours to demand — the trust decision for them is the pin in the compose file, which is why
# each entry carries a tag rather than a bare name.
# KEEP IN SYNC WITH `deploy/docker-compose.yml`. A version bump there without one here makes
# this script refuse the whole apply, hourly, with `systemctl --failed` as the only signal on a
# box that has no log sink (#1175). The compose file carries the reciprocal note next to each
# pinned tag.
readonly -a UPSTREAM_ALLOWLIST=(
  "postgres:18.3"
  "redis:8.6-alpine"
  "datalust/seq:2026.1"
)

log() { printf '%s\n' "$*"; }

# flock's ABSENCE must not read as its verdict. Without this guard a missing binary makes
# `flock -n 9` fail with "command not found", which is indistinguishable from "someone else
# holds the lock" — so the unit would exit 0, apply nothing, and report success on every tick,
# forever, silently. Found by the fixture suite on a host without util-linux; the wrapper had
# been one absent package away from being permanently inert while looking healthy.
command -v flock >/dev/null 2>&1 || {
  log "REFUSING: flock not found — exclusivity cannot be established (install util-linux)"
  exit 2
}

# EXIT 0 WHEN THE LOCK IS HELD, and this is deliberate. A benign overlap — a manual run landing
# on a timer firing — is not a unit failure, and marking it one would put the unit in
# `systemctl --failed`, which is the box's only alarm surface. It must mean something is wrong.
exec 9>"$LOCK"
if ! flock -n 9; then
  log "another reconcile holds $LOCK; this run is a no-op (not a failure)"
  exit 0
fi

[ -f "$COMPOSE_FILE" ] || {
  log "REFUSING: no compose file at $COMPOSE_FILE"
  exit 1
}
[ -x "$VERIFIER" ] || {
  log "REFUSING: verifier missing or not executable: $VERIFIER"
  exit 1
}

# EXIT 2, NOT 1, AND THE DIVERGENCE FROM THE GUARD ABOVE IS DELIBERATE. A missing helper is "the
# check could not run", which is what 2 means in this script's own vocabulary (see the verifier
# loop below). The neighbouring verifier guard's exit 1 predates #1295 and is not this delta's
# to change.
[ -x "$RUNTIME_IDS" ] || {
  log "CANNOT ANSWER: runtime-id helper missing or not executable: $RUNTIME_IDS"
  exit 2
}

# BY PATH, never COMPOSE_FILE — the compose guards are structurally blind to that channel
# (#1217), so a green guard would vouch for a file the deploy does not run.
compose() { /usr/bin/docker compose -f "$COMPOSE_FILE" "$@"; }

log "pulling images declared in $COMPOSE_FILE"
compose pull --quiet

# The image LIST, from compose's own resolved model — client-side, no daemon call. The tag of
# each image is resolved to a digest further down, once, from what the pull landed.
mapfile -t images < <(compose config --images | sort -u)
[ "${#images[@]}" -gt 0 ] || {
  log "REFUSING: compose declared no images"
  exit 1
}

verified=0
skipped=0
# Captured inside the loop and ONLY after this image has verified, so the secrets gate below can
# never be the thing that executes an image the box just refused (#1295).
api_digest=""
for image in "${images[@]}"; do
  case "$image" in
  "$OURS_PREFIX"*) ;;
  *)
    allowed=0
    for u in "${UPSTREAM_ALLOWLIST[@]}"; do
      [ "$image" = "$u" ] && allowed=1 && break
    done
    if [ "$allowed" -eq 1 ]; then
      log "skipping $image (upstream, on the allowlist)"
      skipped=$((skipped + 1))
      continue
    fi
    log "REFUSING: $image is neither ours nor on the upstream allowlist."
    log "  Add it to UPSTREAM_ALLOWLIST with its tag, or publish it through release-images.yml."
    exit 1
    ;;
  esac

  # RepoDigests, not the tag. Select the entry belonging to THIS repository: an image id can
  # carry several (a prior pull by digest, a re-push under another tag), and index 0 is not a
  # contract. Zero entries or two different digests for the same repository both refuse.
  repo="${image%%:*}"
  mapfile -t digests < <(
    /usr/bin/docker image inspect --format '{{range .RepoDigests}}{{println .}}{{end}}' "$image" 2>/dev/null |
      grep -F "${repo}@" | sort -u
  )
  if [ "${#digests[@]}" -ne 1 ]; then
    log "REFUSING: expected exactly one repo digest for $repo, found ${#digests[@]}"
    printf '  %s\n' "${digests[@]:-(none)}"
    exit 1
  fi

  # THE VERIFIER'S THREE OUTCOMES SURVIVE TO THE UNIT'S EXIT STATUS. Collapsing 1 and 2 into
  # one code would leave `systemctl --failed` unable to distinguish "this image is not proven"
  # from "the check could not run" — and on a box whose only alarm surface is that list
  # (#1175: no log sink), the difference is the difference between a compromise and an outage.
  # Both still refuse; only the reported reason differs.
  verify_status=0
  "$VERIFIER" "${digests[0]}" || verify_status=$?
  if [ "$verify_status" -ne 0 ]; then
    log "REFUSING: $image did not verify (verifier exit $verify_status — 1: not proven, 2: could not answer). Nothing is applied; the running containers stay up."
    exit "$verify_status"
  fi
  verified=$((verified + 1))

  # The api image is the representative: it is the one jobbliggaren-inject-secrets.sh measured
  # when it set the ownership, so it is the one the gate must compare against. A divergence
  # BETWEEN our three images is out of scope by decision — all three Dockerfiles declare
  # `USER app`, and nothing on this box or in CI measures a drift between them today — while
  # measuring every OURS_ image here would refuse falsely the day one ships that mounts no
  # secrets.
  #
  # Both separators, because the classifier above is prefix-based and this selector is not: a
  # digest-form entry in the compose model would otherwise leave api_digest empty and turn a
  # healthy box into an hourly exit 2.
  case "$image" in
  "${OURS_PREFIX}api:"* | "${OURS_PREFIX}api@"*) api_digest="${digests[0]}" ;;
  esac
done

# ---------------------------------------------------------------------------------------------
# THE SECRETS GATE (#1295). The injected secrets are owned by the ids of the image that was
# current AT INJECTION TIME; this pull may have brought a different one. A base-image bump that
# moves uid or gid makes the read-only mount unreadable — the directory is 0710 root:<gid> so
# group traversal is the container's only way in, and the files are 0400 <uid> so the owner is
# the only reader — and the app then reports a MISSING KEY rather than a permission problem.
#
# It sits here, after the verify loop and before the word "applying" is ever printed, for two
# reasons that are not interchangeable: measuring the ids RUNS the image, so it must follow
# attestation; and a refusal must not be preceded by a line announcing an apply.
# ---------------------------------------------------------------------------------------------
# No `shopt -s nullglob`: setting it here would leave the shell in a state this script did not
# find it in, and the literal-pattern case it exists to avoid is one `-e` test.
regular_secrets=()
for f in "$SECRETS_DIR"/*; do
  # An unmatched glob stays literal, and so does one against a directory that does not exist.
  [ -e "$f" ] || continue
  # REGULAR FILES ONLY. A directory or socket under $SECRETS_DIR is not something an image's uid
  # has to own, and counting one would turn the skip arm into a permanent refusal on a box where
  # nothing was ever injected.
  if [ -f "$f" ]; then
    regular_secrets+=("$f")
  fi
done

if [ "${#regular_secrets[@]}" -eq 0 ]; then
  # NOT A HOLE, AND IT NEEDS NO TIME BOUND. If nothing has been injected there is nothing an
  # image bump can make unreadable, and whichever of inject and apply happens last establishes
  # the coupling: a later injection measures the image this run is about to apply. A gate that
  # refused here would be permanently red from the hour it shipped until cutover day, and
  # `systemctl --failed` must mean something is wrong.
  log "no injected secrets in $SECRETS_DIR — ownership gate skipped (nothing to be unreadable)"
else
  [ -n "$api_digest" ] || {
    log "CANNOT ANSWER: secrets are injected in $SECRETS_DIR but no ${OURS_PREFIX}api image was"
    log "  verified in this run, so the ids they must match cannot be determined. Nothing applied."
    exit 2
  }

  # A DIGEST, AND THE SCRIPT SAYS SO RATHER THAN ASSUMING IT. What makes running this image safe
  # is that its content is addressed by the hash the verifier just cleared; a tag would be a
  # different artefact by the time it is run. The helper's charset admits both forms — it serves
  # a caller that legitimately passes a tag — so the constraint belongs here, at the caller that
  # has one.
  case "$api_digest" in
  *@sha256:*) ;;
  *)
    log "CANNOT ANSWER: the api reference to measure is not a digest ('$api_digest')."
    log "  Measuring runs the image, and only a digest is the artefact attestation cleared."
    log "  Nothing is applied; the running containers stay up."
    exit 2
    ;;
  esac

  ids_out=$("$RUNTIME_IDS" "$api_digest") || {
    log "CANNOT ANSWER: could not measure the runtime ids from the verified api image."
    log "  Nothing is applied; the running containers stay up."
    exit 2
  }
  mapfile -t runtime_ids <<<"$ids_out"
  want_uid="${runtime_ids[0]:-}"
  want_gid="${runtime_ids[1]:-}"
  # The helper already validates its own output. Re-validating here is not distrust of it but of
  # the SEAM: it is a separate executable on the box, and a gate that compares against an empty
  # string would refuse with a message naming no number at all.
  if ! [[ "$want_uid" =~ ^[0-9]+$ && "$want_gid" =~ ^[0-9]+$ ]]; then
    log "CANNOT ANSWER: $RUNTIME_IDS succeeded but did not return two numeric ids."
    log "  Nothing is applied; the running containers stay up."
    exit 2
  fi

  # A FAILING `stat` IS "COULD NOT ANSWER", NOT A REFUSAL, and without this it would be neither:
  # under `set -e` the assignment would exit 1 with no journal line at all, which reads on
  # `systemctl --failed` as an ownership refusal that never named a number.
  dir_gid=$(stat -c '%g' "$SECRETS_DIR") || {
    log "CANNOT ANSWER: could not stat $SECRETS_DIR. Nothing is applied."
    exit 2
  }
  if [ "$dir_gid" != "$want_gid" ]; then
    log "REFUSING: the incoming api image cannot TRAVERSE $SECRETS_DIR."
    log "  directory group is $dir_gid; the image runs as gid $want_gid. The directory is 0710,"
    log "  so group traversal is the container's only way in — api and worker would report a"
    log "  missing master key. Nothing is applied; the running containers stay up."
    log "  Repair by re-owning, NEVER by re-injecting (master-key-ops.md §3). NOT chown -R from"
    log "  the directory: that chowns the operand too, and root must stay its owner."
    log "    sudo chown root:$want_gid $SECRETS_DIR"
    log "    sudo chown $want_uid:$want_gid $SECRETS_DIR/*"
    log "    sudo systemctl start jobbliggaren-reconcile.service"
    exit 1
  fi

  for f in "${regular_secrets[@]}"; do
    # OWNER AND MODE IN ONE `stat`, because the claim this gate logs is READABILITY and the owner
    # alone does not establish it: a file with the right owner and mode 0000 crash-loops the
    # stack with the same "missing master key" this gate exists to prevent. Nothing else on the
    # box reads the files' mode — `--check` reads the directory's.
    file_meta=$(stat -c '%u %a' "$f") || {
      log "CANNOT ANSWER: could not stat $f. Nothing is applied."
      exit 2
    }
    file_uid="${file_meta% *}"
    file_mode="${file_meta#* }"
    if [ "$file_uid" != "$want_uid" ]; then
      log "REFUSING: the incoming api image cannot READ the injected secrets."
      log "  $f is owned by uid $file_uid; the image runs as uid $want_uid. The files are 0400,"
      log "  so the owner is the only reader. Nothing is applied; the running containers stay up."
      log "  Repair by re-owning, NEVER by re-injecting (master-key-ops.md §3). The directory is"
      log "  NOT part of it — root owns that, and chown -R from the directory would take it too."
      log "    sudo chown $want_uid:$want_gid $SECRETS_DIR/*"
      log "    sudo systemctl start jobbliggaren-reconcile.service"
      exit 1
    fi
    # The owner's read bit, not the exact 0400: injection writes 0400, but refusing every other
    # mode would make this gate an opinion about permissions rather than a statement about
    # readability.
    if (( (8#$file_mode & 0400) == 0 )); then
      log "REFUSING: $f is owned by the right uid ($file_uid) but its mode is $file_mode —"
      log "  the owner cannot read it, so api and worker would report a missing master key"
      log "  anyway. Nothing is applied; the running containers stay up."
      log "    sudo chmod 0400 $f"
      log "    sudo systemctl start jobbliggaren-reconcile.service"
      exit 1
    fi
  done

  log "injected secrets are readable by the incoming image (uid $want_uid, gid $want_gid)"
fi

log "verified $verified image(s), skipped $skipped upstream; applying"

# `--pull never` COMPLETES THE TOCTOU ARGUMENT. Verification ran against the digests already
# on disk; if `up -d` were free to consult the registry again it could resolve a tag to
# something newer than what was verified, and the whole check would guard a different image.
# compose's default is `missing`, which would only pull an absent image — but "would only" is
# an assumption about a version, and the box runs Compose v5.4.0 while this file's behavioural
# notes were taken on 2.40.3. Stating it removes the assumption instead of documenting it.
compose up -d --remove-orphans --pull never

# A SUCCESS STAMP, so that "the box stopped reconciling" is a readable state rather than an
# absence. Refusals are journal lines, and a journal line nobody reads is indistinguishable
# from silence; this file's mtime answers "when did an apply last succeed" in one stat.
mkdir -p "$(dirname "$STAMP")"
date -u +%Y-%m-%dT%H:%M:%SZ >"$STAMP"
log "reconcile complete; stamped $STAMP"
