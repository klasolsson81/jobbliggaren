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
readonly ENV_FILE=/opt/jobbliggaren/deploy/.env
readonly VERIFIER=/opt/jobbliggaren/deploy/systemd/verify-image-attestation.sh
readonly LOCK=/run/jobbliggaren-reconcile.lock
readonly STAMP=/var/lib/jobbliggaren/last-successful-reconcile

# Images built by our workflow, and therefore attestable. Everything else the compose file
# pulls must be on the allowlist below or this script refuses: an unknown image is the shape a
# future service arrives in, and it must fail closed rather than slip past unverified.
readonly OURS_PREFIX="ghcr.io/klasolsson81/jobbliggaren-"

# Upstream images we deliberately do not verify, named one by one. Attestations for these are
# not ours to demand — the trust decision for them is the pin in the compose file, which is why
# each entry carries a tag rather than a bare name.
readonly -a UPSTREAM_ALLOWLIST=(
  "postgres:18.3"
  "redis:8.6-alpine"
)

log() { printf '%s\n' "$*"; }

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

# BY PATH, never COMPOSE_FILE — the compose guards are structurally blind to that channel
# (#1217), so a green guard would vouch for a file the deploy does not run.
compose() { /usr/bin/docker compose -f "$COMPOSE_FILE" "$@"; }

log "pulling images declared in $COMPOSE_FILE"
compose pull --quiet

# The tag is resolved exactly ONCE, here, by asking what the pull actually landed. Everything
# downstream works from the digest. Reading the tag again later would be a second lookup with a
# possibly different answer.
mapfile -t images < <(compose config --images | sort -u)
[ "${#images[@]}" -gt 0 ] || {
  log "REFUSING: compose declared no images"
  exit 1
}

verified=0
skipped=0
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

  if ! "$VERIFIER" "${digests[0]}"; then
    log "REFUSING: $image did not verify. Nothing is applied; the running containers stay up."
    exit 1
  fi
  verified=$((verified + 1))
done

log "verified $verified image(s), skipped $skipped upstream; applying"
compose up -d --remove-orphans

# A SUCCESS STAMP, so that "the box stopped reconciling" is a readable state rather than an
# absence. Refusals are journal lines, and a journal line nobody reads is indistinguishable
# from silence; this file's mtime answers "when did an apply last succeed" in one stat.
mkdir -p "$(dirname "$STAMP")"
date -u +%Y-%m-%dT%H:%M:%SZ >"$STAMP"
log "reconcile complete; stamped $STAMP"
