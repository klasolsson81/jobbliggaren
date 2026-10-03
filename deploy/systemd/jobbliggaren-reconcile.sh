#!/usr/bin/env bash
# jobbliggaren-reconcile — apply ONE verified release record, or nothing (#1238, ADR 0149).
#
# usage:  jobbliggaren-reconcile.sh            the unit's apply
#         jobbliggaren-reconcile.sh --stage    first boot only: verify and tag a release, apply nothing
#         jobbliggaren-reconcile.sh --status   read-only: what is selected, applied, running and checked out
#
# WHAT IT APPLIES. Until #1238 this pulled five `latest` tags, and between the first and the last of
# them a pull could land on a mixed set that every gate then verified, because an attestation binds
# who built an image and never which tree. Now it reads ONE record — the `dev` channel, or the release
# the pin file names — and applies exactly the five images that record names, each verified as built
# from the record's commit, with the compose file that record was released with. The publisher moves
# `dev` only after the whole set and its record are attested, and one tag move is one registry write.
#
# WHY A SCRIPT AND NOT TWO ExecStart LINES. A lock must be held across the whole critical section by
# one process: `ExecStartPre=/usr/bin/flock -n <file> /bin/true` released it when /bin/true exited.
# systemd already serialises the unit against itself; the lock is against a HUMAN running compose, or
# `--stage`, while a reconcile is mid-flight. Verification sits INSIDE the lock, so nothing can move a
# local tag between the check and the apply.
#
# THE ORDER IS SELECT, PROVE, APPLY — AND IT FAILS CLOSED. A refused run applies nothing and the
# running containers keep running: "stale but serving", the right failure for a box whose alarm
# surface is the journal and `systemctl --failed`.
#
# WHAT MANUAL COMMANDS SEE. compose names our images `ghcr.io/klasolsson81/jobbliggaren-<x>:applied`,
# a LOCAL tag this script moves only after everything is proven, and `pull_policy: never` keeps compose
# from ever fetching it. A hand-typed `docker compose up -d --pull never api` therefore recreates from
# the last verified release, never from something a refused run pulled. It still takes no lock and runs
# no verification, which is why the runbook's exceptions first read `--status`.
set -euo pipefail

readonly CHECKOUT=/opt/jobbliggaren
readonly COMPOSE_FILE=/opt/jobbliggaren/deploy/docker-compose.yml
# Read for ONE thing — whether a retired `IMAGE_TAG` key is present — and never echoed: it holds
# credentials. Compose discovers it on its own for interpolation.
readonly ENV_FILE=/opt/jobbliggaren/deploy/.env
readonly VERIFIER=/opt/jobbliggaren/deploy/systemd/verify-image-attestation.sh
readonly RECORD_TOOL=/opt/jobbliggaren/deploy/systemd/jobbliggaren-release-record.sh
readonly LOCK=/run/jobbliggaren-reconcile.lock
readonly STAMP=/var/lib/jobbliggaren/last-successful-reconcile
# What the last successful apply applied: the record's digest, then the record. Written atomically,
# validated by the record tool whenever it is read.
readonly RECEIPT=/var/lib/jobbliggaren/applied-release.env
# Absent: follow the channel. Present: exactly one line, `sha-<40 hex>` or `sha256:<64 hex>`, naming
# the release to hold. Outside the checkout, so `git merge --ff-only` never touches it.
readonly PIN_FILE=/etc/jobbliggaren/release-pin

# The injected crypto secrets, and the shared measurement that says who may read them (#1295).
readonly SECRETS_DIR=/run/jobbliggaren/secrets
readonly RUNTIME_IDS=/opt/jobbliggaren/deploy/systemd/jobbliggaren-runtime-ids.sh

readonly OURS_PREFIX="ghcr.io/klasolsson81/jobbliggaren-"
readonly RELEASE_REPO="${OURS_PREFIX}release"
readonly CHANNEL_TAG="dev"
readonly APPLIED_TAG="applied"
readonly -a RELEASE_IMAGES=(api worker migrate web caddy)
# Every compose service that runs one of our images, and the image it must run. `migrate-rewrap` is the
# operations profile's service and runs the release's migrate image — the binding is checked here, not
# assumed from the compose file.
readonly -A SERVICE_IMAGE=([caddy]=caddy [web]=web [api]=api [worker]=worker [migrate]=migrate [migrate-rewrap]=migrate)
# Services the postcondition reads after an apply (migrate-rewrap never starts with `up`).
readonly -a APPLIED_SERVICES=(caddy web api worker migrate)

# Upstream images we deliberately do not verify, named one by one; the trust decision for them is the
# pin in the compose file, which is why each carries a tag. KEEP IN SYNC WITH `deploy/docker-compose.yml`
# — a version bump there without one here refuses the whole apply, hourly.
readonly -a UPSTREAM_ALLOWLIST=(
  "postgres:18.3"
  "redis:8.6-alpine"
  "datalust/seq:2026.1"
)

log() { printf '%s\n' "$*"; }
# To stderr: both are also called inside command substitutions, which would capture stdout and lose the
# reason. The journal keeps both streams.
refuse() {
  log "REFUSING: $*" >&2
  exit 1
}
cannot_answer() {
  log "CANNOT ANSWER: $*" >&2
  exit 2
}

mode=apply
case "${1:-}" in
"") ;;
--stage) mode=stage ;;
--status) mode=status ;;
*)
  log "usage: $0 [--stage | --status]"
  exit 2
  ;;
esac

# BY PATH, never COMPOSE_FILE — the compose guards are structurally blind to that channel (#1217).
compose() { /usr/bin/docker compose -f "$COMPOSE_FILE" "$@"; }

field() { sed -n "s/^$2=//p" <<<"$1"; }

# The pin file, read strictly: absent → the channel; anything but one well-formed line → refused, and
# never a fallback to the channel.
read_selection() {
  if [ ! -e "$PIN_FILE" ]; then
    printf 'channel'
    return 0
  fi
  [ -f "$PIN_FILE" ] && [ -r "$PIN_FILE" ] || refuse "$PIN_FILE exists but is not a readable file"
  local lines value
  lines=$(wc -l <"$PIN_FILE")
  [ "$(wc -c <"$PIN_FILE")" -le 80 ] && [ "$lines" -eq 1 ] || refuse "$PIN_FILE must hold exactly one line"
  value=$(cat "$PIN_FILE")
  [[ $value =~ ^(sha-[0-9a-f]{40}|sha256:[0-9a-f]{64})$ ]] ||
    refuse "$PIN_FILE must name sha-<40 hex> or sha256:<64 hex>; it names something else"
  printf 'pin %s' "$value"
}

# The repo digest a local image carries for THIS repository — exactly one, or nothing.
repo_digest() {
  local ref="$1" repo="$2"
  local -a found
  mapfile -t found < <(
    /usr/bin/docker image inspect --format '{{range .RepoDigests}}{{println .}}{{end}}' "$ref" 2>/dev/null |
      grep -F "${repo}@" | sort -u
  )
  [ "${#found[@]}" -eq 1 ] || return 1
  printf '%s' "${found[0]#*@}"
}

image_id() { /usr/bin/docker image inspect --format '{{.Id}}' "$1" 2>/dev/null; }

# --- --status: three states, compared; read-only, takes no lock -----------------------------------------
if [ "$mode" = status ]; then
  [ -x "$RECORD_TOOL" ] || cannot_answer "record tool missing or not executable: $RECORD_TOOL"
  selection=$(read_selection)
  log "selection: ${selection/pin /pin: }"
  if [ ! -e "$RECEIPT" ]; then
    log "receipt:   none — nothing has been applied by a record"
    for name in "${RELEASE_IMAGES[@]}"; do
      id=$(image_id "$OURS_PREFIX$name:$APPLIED_TAG" || true)
      log "applied:   $name ${id:+${id:7:12}}${id:-absent}"
    done
    log "verdict:   NOT APPLIED"
    exit 1
  fi
  receipt=$("$RECORD_TOOL" receipt "$RECEIPT") || cannot_answer "the receipt at $RECEIPT does not validate"
  rec_digest=$(field "$receipt" JBL_RECEIPT_RECORD_DIGEST)
  log "receipt:   release $rec_digest, source $(field "$receipt" JBL_RELEASE_SOURCE_SHA), sequence $(field "$receipt" JBL_RELEASE_SEQUENCE)"
  differs=""
  # A manual compose command re-creates from THIS checkout's compose file and `:applied`, so a checkout
  # advanced past the applied release would put its images under a configuration they were not released
  # with — the pairing an apply refuses.
  deploy_here=$("$RECORD_TOOL" deploy-hash "$CHECKOUT") || cannot_answer "could not hash the deployment files in $CHECKOUT"
  if [ "$deploy_here" = "$(field "$receipt" JBL_RELEASE_DEPLOY_SHA256)" ]; then
    log "config:    the checkout's deployment files match the receipt"
  else
    log "config:    the checkout's deployment files DIFFER from the receipt's"
    differs+=" config"
  fi
  for name in "${RELEASE_IMAGES[@]}"; do
    want=$(image_id "$OURS_PREFIX$name@$(field "$receipt" "JBL_RELEASE_IMAGE_${name^^}")" || true)
    have=$(image_id "$OURS_PREFIX$name:$APPLIED_TAG" || true)
    if [ -n "$want" ] && [ "$have" = "$want" ]; then
      log "applied:   $name matches the receipt"
    else
      log "applied:   $name DIFFERS (${have:-absent})"
      differs+=" applied:$name"
    fi
  done
  for svc in "${APPLIED_SERVICES[@]}"; do
    want=$(image_id "$OURS_PREFIX${SERVICE_IMAGE[$svc]}@$(field "$receipt" "JBL_RELEASE_IMAGE_${SERVICE_IMAGE[$svc]^^}")" || true)
    cid=$(compose ps -a -q "$svc" 2>/dev/null || true)
    have=""
    [ -z "$cid" ] || [ "$(wc -l <<<"$cid")" -ne 1 ] || have=$(/usr/bin/docker inspect --format '{{.Image}}' "$cid" 2>/dev/null || true)
    if [ -n "$want" ] && [ "$have" = "$want" ]; then
      log "running:   $svc matches the receipt"
    else
      log "running:   $svc DIFFERS (${have:-no container})"
      differs+=" running:$svc"
    fi
  done
  if [ -n "$differs" ]; then
    log "verdict:   INCONSISTENT —$differs"
    exit 1
  fi
  log "verdict:   consistent — the checkout's deployment files, :applied, the receipt and the running containers name release $rec_digest"
  exit 0
fi

# flock's ABSENCE must not read as its verdict: a missing binary makes `flock -n 9` fail like a held
# lock, and the unit would exit 0 forever having applied nothing.
command -v flock >/dev/null 2>&1 || {
  log "REFUSING: flock not found — exclusivity cannot be established (install util-linux)"
  exit 2
}

# EXIT 0 WHEN THE LOCK IS HELD: a benign overlap is not a unit failure, and `systemctl --failed` must
# mean something is wrong.
exec 9>"$LOCK"
if ! flock -n 9; then
  log "another reconcile holds $LOCK; this run is a no-op (not a failure)"
  exit 0
fi

[ -f "$COMPOSE_FILE" ] || refuse "no compose file at $COMPOSE_FILE"
[ -x "$VERIFIER" ] || refuse "verifier missing or not executable: $VERIFIER"
[ -x "$RECORD_TOOL" ] || cannot_answer "record tool missing or not executable: $RECORD_TOOL"
[ -x "$RUNTIME_IDS" ] || cannot_answer "runtime-id helper missing or not executable: $RUNTIME_IDS"
[ -r "$ENV_FILE" ] || cannot_answer "$ENV_FILE is not readable"

if [ "$mode" = stage ] && [ -e "$RECEIPT" ]; then
  refuse "--stage is for a first boot only, and $RECEIPT exists: this box has applied a release. Run the unit."
fi

# --- 1. selection and the compose binding — no network yet ------------------------------------------------
selection=$(read_selection)
case "$selection" in
channel) ref="$RELEASE_REPO:$CHANNEL_TAG" pinned_digest="" expected_sha="" ;;
"pin sha-"*) ref="$RELEASE_REPO:${selection#pin }" pinned_digest="" expected_sha="${selection#pin sha-}" ;;
"pin sha256:"*) ref="$RELEASE_REPO@${selection#pin }" pinned_digest="${selection#pin }" expected_sha="" ;;
esac
log "selection: ${selection/pin /pin }"

# The retired rollback control: a pin there would now pin nothing, silently. Presence of the key is all
# that is read; no line of the file is ever printed.
if grep -qE '^[[:space:]]*(export[[:space:]]+)?IMAGE_TAG[[:space:]]*=' "$ENV_FILE"; then
  refuse "$ENV_FILE sets IMAGE_TAG, which no longer pins anything (#1238). Remove it; pin a release in $PIN_FILE."
fi

# Every service that runs one of our images names it as the local `:applied` tag, and every other
# image compose declares is an allow-listed upstream one — including the operations profile.
for svc in "${!SERVICE_IMAGE[@]}"; do
  declared=$(compose --profile ops config --images "$svc") || cannot_answer "compose could not resolve service $svc"
  [ "$declared" = "${OURS_PREFIX}${SERVICE_IMAGE[$svc]}:$APPLIED_TAG" ] ||
    refuse "compose service $svc runs '$declared', not ${OURS_PREFIX}${SERVICE_IMAGE[$svc]}:$APPLIED_TAG"
done
mapfile -t images < <(compose --profile ops config --images | sort -u)
[ "${#images[@]}" -gt 0 ] || refuse "compose declared no images"
upstream=()
for image in "${images[@]}"; do
  case "$image" in
  "$OURS_PREFIX"*":$APPLIED_TAG")
    name=${image#"$OURS_PREFIX"}
    name=${name%:"$APPLIED_TAG"}
    case " ${RELEASE_IMAGES[*]} " in *" $name "*) continue ;; esac
    refuse "$image is ours by name but not one of the released images"
    ;;
  esac
  allowed=0
  for u in "${UPSTREAM_ALLOWLIST[@]}"; do [ "$image" = "$u" ] && allowed=1 && break; done
  [ "$allowed" -eq 1 ] || refuse "$image is neither one of the released images nor on the upstream allowlist"
  upstream+=("$image")
done

# --- 2. one snapshot of the release, read only in the verify-first order ---------------------------------
/usr/bin/docker pull --quiet "$ref" >/dev/null 2>&1 ||
  cannot_answer "could not pull $ref (before activation the package may not be public yet, or not published yet)"
rec_digest=$(repo_digest "$ref" "$RELEASE_REPO") || refuse "expected exactly one repo digest for $RELEASE_REPO on $ref"
[ -z "$pinned_digest" ] || [ "$rec_digest" = "$pinned_digest" ] || refuse "pulled $rec_digest, but $PIN_FILE pins $pinned_digest"

record_status=0
if [ -n "$expected_sha" ]; then
  record=$("$RECORD_TOOL" read "$RELEASE_REPO@$rec_digest" "$expected_sha") || record_status=$?
else
  record=$("$RECORD_TOOL" read "$RELEASE_REPO@$rec_digest") || record_status=$?
fi
[ "$record_status" -eq 0 ] || {
  log "REFUSING: the release $rec_digest is not a proven record (exit $record_status); nothing is applied"
  exit "$record_status"
}
source_sha=$(field "$record" JBL_RELEASE_SOURCE_SHA)
sequence=$(field "$record" JBL_RELEASE_SEQUENCE)
log "release $rec_digest: source $source_sha, sequence $sequence"

# --- 3. the deployment configuration it was released with -------------------------------------------------
deploy_here=$("$RECORD_TOOL" deploy-hash "$CHECKOUT") || cannot_answer "could not hash the deployment files in $CHECKOUT"
if [ "$deploy_here" != "$(field "$record" JBL_RELEASE_DEPLOY_SHA256)" ]; then
  log "REFUSING: this checkout's deployment files are not the ones release $source_sha was built to run with."
  log "  checkout: $deploy_here"
  log "  release:  $(field "$record" JBL_RELEASE_DEPLOY_SHA256)"
  log "  Nothing is applied; the running containers stay up. To apply this release, advance the checkout"
  log "  to its commit (a deploy, on Klas's GO; stop the timer first — vps-deploy-stack.md §3b):"
  log "    sudo git -C $CHECKOUT fetch origin main && sudo git -C $CHECKOUT merge --ff-only $source_sha"
  exit 1
fi

# --- 4. channel acceptance against the last applied release ------------------------------------------------
if [ -e "$RECEIPT" ]; then
  receipt=$("$RECORD_TOOL" receipt "$RECEIPT") || cannot_answer "the receipt at $RECEIPT does not validate; nothing is applied"
  r_sha=$(field "$receipt" JBL_RELEASE_SOURCE_SHA)
  r_seq=$(field "$receipt" JBL_RELEASE_SEQUENCE)
  missing=$(comm -23 <(field "$receipt" JBL_RELEASE_MIGRATIONS_APP | tr ',' '\n' | LC_ALL=C sort) \
    <(field "$record" JBL_RELEASE_MIGRATIONS_APP | tr ',' '\n' | LC_ALL=C sort) | paste -sd, -)
  added_identity=$(comm -13 <(field "$receipt" JBL_RELEASE_MIGRATIONS_IDENTITY | tr ',' '\n' | LC_ALL=C sort) \
    <(field "$record" JBL_RELEASE_MIGRATIONS_IDENTITY | tr ',' '\n' | LC_ALL=C sort) | paste -sd, -)
  if [ "$selection" = channel ]; then
    if [ "$sequence" -lt "$r_seq" ] || { [ "$sequence" -eq "$r_seq" ] && [ "$source_sha" != "$r_sha" ]; }; then
      refuse "the channel names release $source_sha (sequence $sequence), behind the applied $r_sha (sequence $r_seq); following the channel never moves backwards — pin a release to do that deliberately"
    fi
    [ -z "$missing" ] ||
      refuse "release $source_sha lacks migrations the applied release holds ($missing); following the channel never moves the schema backwards"
  else
    log "pinned: the applied release is $r_sha (sequence $r_seq); the schema gate in migrate decides compatibility${missing:+ — this release lacks $missing}"
  fi
  [ -z "$added_identity" ] ||
    log "NOTE: release $source_sha adds Identity migrations the unit does not apply ($added_identity) — vps-deploy-stack.md §3c"
else
  log "no receipt at $RECEIPT: this is the first apply by a record (a bootstrap; it may move backwards once)"
fi

# --- 5. each image: pulled by the digest the record names, verified as built from its commit -------------
declare -A digest_of=()
for name in "${RELEASE_IMAGES[@]}"; do
  digest=$(field "$record" "JBL_RELEASE_IMAGE_${name^^}")
  repo="$OURS_PREFIX$name"
  /usr/bin/docker pull --quiet "$repo@$digest" >/dev/null 2>&1 || cannot_answer "could not pull $repo@$digest"
  found=$(repo_digest "$repo@$digest" "$repo") || refuse "expected exactly one repo digest for $repo"
  [ "$found" = "$digest" ] || refuse "$repo@$digest carries the repo digest $found"
  verify_status=0
  "$VERIFIER" "$repo@$digest" "$source_sha" || verify_status=$?
  if [ "$verify_status" -ne 0 ]; then
    log "REFUSING: $repo@$digest did not verify as built from $source_sha (verifier exit $verify_status — 1: not proven, 2: could not answer). Nothing is applied; the running containers stay up."
    exit "$verify_status"
  fi
  digest_of[$name]="$digest"
done
api_digest="${OURS_PREFIX}api@${digest_of[api]}"
worker_digest="${OURS_PREFIX}worker@${digest_of[worker]}"

for image in "${upstream[@]}"; do
  /usr/bin/docker pull --quiet "$image" >/dev/null 2>&1 || cannot_answer "could not pull the upstream image $image"
  log "pulled $image (upstream, on the allowlist)"
done

if [ "$mode" = apply ]; then
  # ---------------------------------------------------------------------------------------------------------
  # THE SECRETS GATE (#1295). The injected secrets are owned by the ids of the image that was current AT
  # INJECTION TIME; this release may bring a different one. A base-image bump that moves uid or gid makes
  # the read-only mount unreadable — the directory is 0710 root:<gid>, the files 0400 <uid> — and the app
  # then reports a MISSING KEY rather than a permission problem. Measuring the ids RUNS the image, so it
  # follows the verification above, and a refusal is never preceded by a line announcing an apply.
  # ---------------------------------------------------------------------------------------------------------
  regular_secrets=()
  for f in "$SECRETS_DIR"/*; do
    [ -e "$f" ] || continue
    if [ -f "$f" ]; then
      regular_secrets+=("$f")
    fi
  done

  if [ "${#regular_secrets[@]}" -eq 0 ]; then
    # NOT A HOLE: if nothing has been injected there is nothing an image bump can make unreadable, and a
    # later injection measures the image this run is about to apply.
    log "no injected secrets in $SECRETS_DIR — ownership gate skipped (nothing to be unreadable)"
  else
    ids_out=$("$RUNTIME_IDS" "$api_digest") || {
      log "CANNOT ANSWER: could not measure the runtime ids from the verified api image."
      log "  Nothing is applied; the running containers stay up."
      exit 2
    }
    mapfile -t runtime_ids <<<"$ids_out"
    want_uid="${runtime_ids[0]:-}"
    want_gid="${runtime_ids[1]:-}"
    if ! [[ "$want_uid" =~ ^[0-9]+$ && "$want_gid" =~ ^[0-9]+$ ]]; then
      log "CANNOT ANSWER: $RUNTIME_IDS succeeded but did not return two numeric ids."
      log "  Nothing is applied; the running containers stay up."
      exit 2
    fi

    dir_gid=$(stat -c '%g' "$SECRETS_DIR") || {
      log "CANNOT ANSWER: could not stat $SECRETS_DIR. Nothing is applied."
      exit 2
    }
    if [ "$dir_gid" != "$want_gid" ]; then
      log "REFUSING: the incoming api image cannot TRAVERSE $SECRETS_DIR."
      log "  directory group is $dir_gid; the image runs as gid $want_gid. The directory is 0710,"
      log "  so group traversal is the container's only way in — api and worker would report a"
      log "  missing master key. Nothing is applied; the running containers stay up."
      log "  Repair by re-owning, NEVER by re-injecting (master-key-ops.md §3). Run the two"
      log "  re-owning commands below, then start the unit. The files form is a find, not a glob:"
      log "  0710 denies the read to every non-root user and YOUR shell expands a glob before"
      log "  sudo elevates, so a glob reaches chown unexpanded. -mindepth 1 keeps the directory"
      log "  out — root owns it, and a recursive chown would take it."
      log "  These lines are long and journalctl's pager chops them; read with --no-pager."
      log "    sudo chown root:$want_gid $SECRETS_DIR"
      log "    sudo find $SECRETS_DIR -mindepth 1 -maxdepth 1 -exec chown $want_uid:$want_gid {} +"
      log "    sudo systemctl start jobbliggaren-reconcile.service"
      exit 1
    fi

    for f in "${regular_secrets[@]}"; do
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
        log "  NOT part of it — root owns that, and -mindepth 1 is what keeps it out. A glob would"
        log "  not do: 0710 denies the read to every non-root user and YOUR shell expands a glob"
        log "  before sudo elevates, so it would reach chown unexpanded."
        log "  These lines are long and journalctl's pager chops them; read with --no-pager."
        log "    sudo find $SECRETS_DIR -mindepth 1 -maxdepth 1 -exec chown $want_uid:$want_gid {} +"
        log "    sudo systemctl start jobbliggaren-reconcile.service"
        exit 1
      fi
      if (((8#$file_mode & 0400) == 0)); then
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

  if grep -q 'ConnectionStrings__Redis_FILE:' "$COMPOSE_FILE"; then
    bash /opt/jobbliggaren/deploy/systemd/jobbliggaren-redis-secrets.sh --check-images "$api_digest" "$worker_digest" || {
      log "REFUSING: Redis credential mounts do not match the incoming readers; nothing is applied."
      exit 1
    }
  fi
fi

# --- 6. move :applied, apply, prove, record — or put :applied back ------------------------------------------
declare -A prior=()
for name in "${RELEASE_IMAGES[@]}"; do
  prior[$name]=$(image_id "$OURS_PREFIX$name:$APPLIED_TAG" || true)
done

restore_applied() {
  local name ok=0
  for name in "${RELEASE_IMAGES[@]}"; do
    if [ -n "${prior[$name]}" ]; then
      /usr/bin/docker tag "${prior[$name]}" "$OURS_PREFIX$name:$APPLIED_TAG" || ok=1
    elif image_id "$OURS_PREFIX$name:$APPLIED_TAG" >/dev/null; then
      /usr/bin/docker rmi "$OURS_PREFIX$name:$APPLIED_TAG" >/dev/null || ok=1
    fi
  done
  return "$ok"
}

in_tag_phase=0
on_exit() {
  local status=$?
  if [ "$in_tag_phase" -eq 1 ]; then
    in_tag_phase=0
    if restore_applied; then
      log "the run failed after :applied was moved; :applied is restored to what this run found"
    else
      log "CANNOT ANSWER: the run failed after :applied was moved, and restoring it failed — local :applied"
      log "  may be mixed. Do not run a manual compose command; read --status and start the unit again."
      status=2
    fi
  fi
  exit "$status"
}
trap on_exit EXIT

in_tag_phase=1
for name in "${RELEASE_IMAGES[@]}"; do
  /usr/bin/docker tag "$OURS_PREFIX$name@${digest_of[$name]}" "$OURS_PREFIX$name:$APPLIED_TAG"
done

if [ "$mode" = stage ]; then
  in_tag_phase=0
  log "staged release $rec_digest (source $source_sha): :applied names its verified images; nothing applied, no receipt"
  exit 0
fi

log "verified ${#RELEASE_IMAGES[@]} image(s), pulled ${#upstream[@]} upstream; applying release $source_sha"

# `--pull never` (and `pull_policy: never` in compose) complete the TOCTOU argument: `up` must not
# consult the registry again and resolve anything to something newer than what was verified.
compose up -d --remove-orphans --pull never

# THE POSTCONDITION: every service runs exactly the image the record names. A receipt is written only
# for an apply that measurably happened.
for svc in "${APPLIED_SERVICES[@]}"; do
  want=$(image_id "$OURS_PREFIX${SERVICE_IMAGE[$svc]}@${digest_of[${SERVICE_IMAGE[$svc]}]}") ||
    cannot_answer "could not read the image id of the verified ${SERVICE_IMAGE[$svc]} image"
  cid=$(compose ps -a -q "$svc")
  [ -n "$cid" ] && [ "$(wc -l <<<"$cid")" -eq 1 ] || refuse "after the apply, service $svc has no single container"
  have=$(/usr/bin/docker inspect --format '{{.Image}}' "$cid") || cannot_answer "could not inspect the $svc container"
  [ "$have" = "$want" ] || refuse "after the apply, service $svc runs $have, not the release's ${SERVICE_IMAGE[$svc]} image $want"
done

mkdir -p "$(dirname "$RECEIPT")"
receipt_tmp=$(mktemp "$RECEIPT.XXXXXX")
{
  printf 'JBL_RECEIPT_RECORD_DIGEST=%s\n' "$rec_digest"
  printf '%s\n' "$record"
} >"$receipt_tmp"
"$RECORD_TOOL" receipt "$receipt_tmp" >/dev/null || {
  rm -f -- "$receipt_tmp"
  cannot_answer "the receipt this run composed does not validate"
}
chmod 0644 "$receipt_tmp"
mv -f -- "$receipt_tmp" "$RECEIPT"
in_tag_phase=0

# A SUCCESS STAMP, so "the box stopped reconciling" is a readable state rather than an absence.
mkdir -p "$(dirname "$STAMP")"
date -u +%Y-%m-%dT%H:%M:%SZ >"$STAMP"
log "reconcile complete: release $source_sha applied; receipt $RECEIPT; stamped $STAMP"
