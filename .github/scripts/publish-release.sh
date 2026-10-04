#!/usr/bin/env bash
#
# publish-release.sh — the decisions of `release-images.yml` (#1238, ADR 0149): whether a commit is
# already released, turning five verified images into ONE immutable release record, and moving tags
# only after that record proves itself.
#
# usage:  publish-release.sh frozen  <source-sha> <image-name>
#         publish-release.sh push    <image-name> <short-sha>
#         publish-release.sh record  <source-sha>
#         publish-release.sh verify  <record-digest> <source-sha>
#         publish-release.sh advance <record-digest> <source-sha>
# stdout: `frozen` prints `frozen=true|false`; `push` prints `digest=…`; `record` prints `digest=…`,
#         `attest=true|false`, `repo=…` — GITHUB_OUTPUT lines, each value anchored before it is printed
# stderr: every diagnostic
# exit:   0 done · 1 refused (the registry holds something this run must not overwrite or trust) ·
#         2 could not answer (a lookup, pull, verifier or git question that could not run)
#
# WHY A RECORD AND NOT FIVE TAGS. A consumer pulling five `latest` tags reads five answers at five
# moments, and no ordering of five moves makes that one answer. A consumer reading `dev` reads one
# manifest, and that manifest names everything; moving one tag is one registry write.
#
# A RECORD IS NEVER REWRITTEN, AND A TAG VOUCHES FOR NOTHING. `release:sha-<commit>` is created once,
# in `advance`, after the record's own attestation verifies — a new record is pushed under a PENDING
# tag, so a failed attest leaves no sealed name behind and the next run simply tries
# again. An existing record is reused only when it proves itself: read in the verify-first order
# (deploy/systemd/jobbliggaren-release-record.sh `read`), naming this commit, and all five recorded
# images verifying as built from it. Anything else is red, never overwritten: the repair is deleting
# that version, which is Klas's decision.
#
# `dev` IS MOVED FORWARD ONLY BY THIS PUBLISHER. A run whose commit is older than the one `dev` names
# — a re-run of an old run — publishes its record and moves nothing. Order is git ancestry in this
# job's full clone, never a network call. The box enforces its own monotonic check on top
# (jobbliggaren-reconcile.sh); this file's property is that an honest publisher never moves backwards.
set -euo pipefail

readonly PREFIX="ghcr.io/klasolsson81/jobbliggaren"
readonly RELEASE_REPO="${PREFIX}-release"
readonly CHANNEL_TAG="dev"
readonly VERIFIER="deploy/systemd/verify-image-attestation.sh"
readonly RECORD_TOOL="deploy/systemd/jobbliggaren-release-record.sh"
readonly NAMES=(api worker migrate web caddy)
# Measured 2026-10-03 over 120 runs and 334 publishes: an attestation was readable the moment the
# attest step returned, every time. The retries are for the exception, bounded inside the job timeout.
readonly VERIFY_ATTEMPTS=6
readonly VERIFY_INTERVAL=10
readonly MOVE_ATTEMPTS=3

log() { printf '%s\n' "$*" >&2; }
refuse() {
  log "::error::publish-release: $*"
  exit 1
}
cannot_answer() {
  log "::error::publish-release: could not answer — $*"
  exit 2
}

is_sha() { [[ $1 =~ ^[0-9a-f]{40}$ ]]; }
is_digest() { [[ $1 =~ ^sha256:[0-9a-f]{64}$ ]]; }

# What a reference resolves to: a digest, ABSENT (the registry said the tag does not exist), or DENIED
# (the registry refused to say — before the first publication the release package does not exist, and
# GHCR answers that the same way as a package it will not show). Anything else cannot be answered.
# Absence is read from the message, the way verify-image-attestation.sh reads cosign's: a network
# error read as absence would send a run down the create path over a record that exists.
lookup() {
  local ref="$1" out status=0
  out=$(docker buildx imagetools inspect --format '{{json .Manifest.Digest}}' "$ref" 2>&1) || status=$?
  if [ "$status" -eq 0 ]; then
    out=${out//\"/}
    is_digest "$out" || cannot_answer "$ref resolved to something that is not a digest: $out"
    printf '%s' "$out"
    return 0
  fi
  case "$out" in
  *"not found"* | *"manifest unknown"* | *"MANIFEST_UNKNOWN"* | *"name unknown"* | *"NAME_UNKNOWN"*) printf 'ABSENT' ;;
  *"denied"* | *"403 Forbidden"* | *"DENIED"*) printf 'DENIED' ;;
  *) cannot_answer "could not resolve $ref: $out" ;;
  esac
}

# The validated record a release digest holds, read through the box's own function: identity verified
# before a byte is copied, then validated, then verified bound to its commit, and — given <expected> —
# required to name exactly that commit.
read_record() {
  local digest="$1" expected="${2:-}" status=0
  # Identity first, against the registry: a record that does not prove it was ours never enters this
  # runner's daemon, in a job that holds `id-token: write`.
  bash "$VERIFIER" "$RELEASE_REPO@$digest" >&2 || return $?
  docker pull --quiet "$RELEASE_REPO@$digest" >/dev/null 2>&1 || cannot_answer "could not pull $RELEASE_REPO@$digest"
  if [ -n "$expected" ]; then
    bash "$RECORD_TOOL" read "$RELEASE_REPO@$digest" "$expected" || status=$?
  else
    bash "$RECORD_TOOL" read "$RELEASE_REPO@$digest" || status=$?
  fi
  return "$status"
}

field() { sed -n "s/^$2=//p" <<<"$1"; }

# Pushes <repo>:<tag> and prints the digest THIS push produced: the pushed image's one repo digest for
# <repo>. An attestation signs whatever digest it is handed and a tag is mutable, so the tag is read
# back and anything else there is refused before a digest is printed — a writer racing the push never
# gets its manifest attested.
push_image() {
  local repo="$1" tag="$2" pushed now
  local -a found
  docker push "$repo:$tag" >&2 || cannot_answer "could not push $repo:$tag"
  mapfile -t found < <(docker image inspect --format '{{range .RepoDigests}}{{println .}}{{end}}' "$repo:$tag" 2>/dev/null |
    awk -v r="$repo@" 'index($0, r) == 1' | sort -u)
  [ "${#found[@]}" -eq 1 ] || cannot_answer "expected one $repo digest on the image just pushed as $repo:$tag, found ${#found[@]}"
  pushed="${found[0]#*@}"
  is_digest "$pushed" || cannot_answer "the image pushed as $repo:$tag carries a repo digest that is not one: $pushed"
  now=$(lookup "$repo:$tag")
  [ "$now" = "$pushed" ] || refuse "$repo:$tag names '$now' right after this run pushed $pushed; nothing is attested"
  printf '%s' "$pushed"
}

verify_image() {
  local name="$1" digest="$2" sha="$3" status=0
  bash "$VERIFIER" "$PREFIX-$name@$digest" "$sha" >&2 || status=$?
  [ "$status" -eq 0 ] || log "::error::publish-release: $PREFIX-$name@$digest did not verify as built from $sha (verifier exit $status)"
  return "$status"
}

# A record proves itself: it reads (verify-first, naming <sha>) and every image it names verifies as
# built from <sha>. Prints the record on success.
prove_record() {
  local digest="$1" sha="$2" content name status=0
  content=$(read_record "$digest" "$sha") || return $?
  for name in "${NAMES[@]}"; do
    verify_image "$name" "$(field "$content" "JBL_RELEASE_IMAGE_${name^^}")" "$sha" || return $?
  done
  printf '%s\n' "$content"
}

cmd_frozen() {
  [ "$#" -eq 2 ] || cannot_answer "usage: $0 frozen <source-sha> <image-name>"
  local sha="$1" name="$2" existing content status=0
  is_sha "$sha" || refuse "not a source sha: $sha"
  case " ${NAMES[*]} " in *" $name "*) ;; *) refuse "not one of the released images: $name" ;; esac

  existing=$(lookup "$RELEASE_REPO:sha-$sha")
  case "$existing" in
  ABSENT)
    printf 'frozen=false\n'
    return 0
    ;;
  DENIED)
    # Before the first publication the package does not exist, and the fan-in is what creates it. A
    # cell that builds here can at worst rebuild an image of a commit whose record it could not see;
    # the fan-in re-checks strictly before anything is sealed, and a record names digests, not tags.
    log "::warning::$RELEASE_REPO is not readable (absent before the first publication, or a permission problem); building"
    printf 'frozen=false\n'
    return 0
    ;;
  esac
  content=$(read_record "$existing" "$sha") || {
    status=$?
    log "::error::publish-release: $RELEASE_REPO:sha-$sha exists and does not prove itself; it is never overwritten (repair: delete that version — Klas's decision)"
    exit "$status"
  }
  verify_image "$name" "$(field "$content" "JBL_RELEASE_IMAGE_${name^^}")" "$sha" || exit $?
  log "$sha is released as $existing; this cell's image is the one it names and verifies — nothing to build"
  printf 'frozen=true\n'
}

# A cell's image, under its immutable `sha-<short>` tag; the digest printed is the one attested.
cmd_push() {
  [ "$#" -eq 2 ] || cannot_answer "usage: $0 push <image-name> <short-sha>"
  local name="$1" short="$2" digest
  case " ${NAMES[*]} " in *" $name "*) ;; *) refuse "not one of the released images: $name" ;; esac
  [[ $short =~ ^[0-9a-f]{7}$ ]] || refuse "not a short sha: $short"
  digest=$(push_image "$PREFIX-$name" "sha-$short")
  printf 'digest=%s\n' "$digest"
}

# Resolves the five images this commit's cells published, verifies each as built from the commit, and
# emits the record from them.
emit_record() {
  local sha="$1" short="${1:0:7}" name digest
  local -A digests=()
  for name in "${NAMES[@]}"; do
    digest=$(lookup "$PREFIX-$name:sha-$short")
    case "$digest" in
    ABSENT | DENIED) refuse "$PREFIX-$name:sha-$short is not readable ($digest): the set is incomplete, so nothing is released" ;;
    esac
    verify_image "$name" "$digest" "$sha" || exit $?
    digests[$name]="$digest"
  done
  bash "$RECORD_TOOL" emit --source-sha "$sha" \
    --api "${digests[api]}" --worker "${digests[worker]}" --migrate "${digests[migrate]}" \
    --web "${digests[web]}" --caddy "${digests[caddy]}" --source-root .
}

cmd_record() {
  [ "$#" -eq 1 ] || cannot_answer "usage: $0 record <source-sha>"
  local sha="$1" existing work digest media status=0
  is_sha "$sha" || refuse "not a source sha: $sha"

  existing=$(lookup "$RELEASE_REPO:sha-$sha")
  if is_digest "$existing"; then
    prove_record "$existing" "$sha" >/dev/null || {
      status=$?
      log "::error::publish-release: $RELEASE_REPO:sha-$sha exists and does not prove itself; it is never overwritten (repair: delete that version — Klas's decision)"
      exit "$status"
    }
    log "$sha is already released as $existing, and it proves itself again"
    printf 'digest=%s\nattest=false\nrepo=%s\n' "$existing" "$RELEASE_REPO"
    return 0
  fi

  work=$(mktemp -d)
  # shellcheck disable=SC2064 # expand now: the trap removes THIS directory
  trap "rm -rf -- '$work'" EXIT
  emit_record "$sha" >"$work/release.env"
  printf 'FROM scratch\nCOPY release.env /release.env\n' >"$work/Dockerfile"
  docker build --quiet --provenance=false --sbom=false -t "$RELEASE_REPO:pending-$sha" "$work" >/dev/null ||
    cannot_answer "could not build the record image"
  digest=$(push_image "$RELEASE_REPO" "pending-$sha")

  # One image manifest, as the five images are: the only shape whose digest survives the tag moves in
  # `advance` (an index would be re-wrapped, and no attestation names the wrapper).
  media=$(docker buildx imagetools inspect --format '{{json .Manifest.MediaType}}' "$RELEASE_REPO@$digest" 2>&1) ||
    cannot_answer "could not read the media type of $RELEASE_REPO@$digest"
  case "${media//\"/}" in
  application/vnd.docker.distribution.manifest.v2+json | application/vnd.oci.image.manifest.v1+json) ;;
  *) refuse "the record was pushed as '${media//\"/}', not as a single image manifest" ;;
  esac

  # Strict now that the package exists (the push created it if it did not): a record that was there
  # all along under the sealed name, unreadable before, is not something this run may race.
  existing=$(lookup "$RELEASE_REPO:sha-$sha")
  [ "$existing" = ABSENT ] || refuse "$RELEASE_REPO:sha-$sha is '$existing' after the pending push; refusing to race a sealed record"

  log "pushed the record for $sha as $digest (pending; sealed only after it verifies)"
  printf 'digest=%s\nattest=true\nrepo=%s\n' "$digest" "$RELEASE_REPO"
}

cmd_verify() {
  [ "$#" -eq 2 ] || cannot_answer "usage: $0 verify <record-digest> <source-sha>"
  local digest="$1" sha="$2" attempt status=0
  is_digest "$digest" || refuse "not a digest: $digest"
  is_sha "$sha" || refuse "not a source sha: $sha"
  for ((attempt = 1; attempt <= VERIFY_ATTEMPTS; attempt++)); do
    status=0
    read_record "$digest" "$sha" >/dev/null || status=$?
    [ "$status" -ne 0 ] || return 0
    [ "$attempt" -lt "$VERIFY_ATTEMPTS" ] || break
    log "the record does not read as $sha's yet (exit $status, attempt $attempt/$VERIFY_ATTEMPTS); retrying in ${VERIFY_INTERVAL}s"
    sleep "$VERIFY_INTERVAL"
  done
  log "::error::publish-release: the record $digest did not prove itself after $VERIFY_ATTEMPTS attempts"
  exit "$status"
}

# Moves a tag onto a digest in the same repository and proves where it landed. `--prefer-index=false`
# is the whole correctness of the command: buildx's default wraps a single manifest in a new manifest
# list — a different digest no attestation names (measured 2026-10-03, buildx v0.37.1).
move_tag() {
  local repo="$1" tag="$2" digest="$3" attempt landed=""
  for ((attempt = 1; attempt <= MOVE_ATTEMPTS; attempt++)); do
    if docker buildx imagetools create --prefer-index=false -t "$repo:$tag" "$repo@$digest" >&2; then
      landed=$(lookup "$repo:$tag")
      [ "$landed" = "$digest" ] && return 0
    fi
    log "moving $repo:$tag to $digest did not land (now '${landed:-unknown}', attempt $attempt/$MOVE_ATTEMPTS)"
  done
  refuse "$repo:$tag did not land on $digest after $MOVE_ATTEMPTS attempts (it names '${landed:-unknown}')"
}

# Is <a> an ancestor of <b> in this clone? 0 yes · 1 no · 2 could not answer (a commit it does not hold).
ancestor() {
  local status=0
  git cat-file -e "$1^{commit}" 2>/dev/null && git cat-file -e "$2^{commit}" 2>/dev/null || return 2
  git merge-base --is-ancestor "$1" "$2" || status=$?
  case "$status" in
  0 | 1) return "$status" ;;
  *) return 2 ;;
  esac
}

cmd_advance() {
  [ "$#" -eq 2 ] || cannot_answer "usage: $0 advance <record-digest> <source-sha>"
  local digest="$1" sha="$2" content sealed current current_content current_sha name image landed status=0
  is_digest "$digest" || refuse "not a digest: $digest"
  is_sha "$sha" || refuse "not a source sha: $sha"

  # The mover proves its own precondition instead of trusting the step before it.
  content=$(read_record "$digest" "$sha") || {
    status=$?
    log "::error::publish-release: refusing to move anything onto a record that does not prove itself"
    exit "$status"
  }

  # Seal: the immutable name, created once.
  sealed=$(lookup "$RELEASE_REPO:sha-$sha")
  case "$sealed" in
  ABSENT) move_tag "$RELEASE_REPO" "sha-$sha" "$digest" ;;
  "$digest") ;;
  DENIED) cannot_answer "$RELEASE_REPO:sha-$sha is not readable; nothing is sealed or moved" ;;
  *) refuse "$RELEASE_REPO:sha-$sha already names $sealed, not this record $digest; a sealed record is never rewritten" ;;
  esac

  current=$(lookup "$RELEASE_REPO:$CHANNEL_TAG")
  case "$current" in
  "$digest") log "$CHANNEL_TAG already names this release" ;;
  ABSENT)
    log "$CHANNEL_TAG does not exist yet: this is the channel's first release"
    move_tag "$RELEASE_REPO" "$CHANNEL_TAG" "$digest"
    ;;
  DENIED) cannot_answer "$RELEASE_REPO:$CHANNEL_TAG is not readable; the channel is not moved" ;;
  *)
    # A present channel must prove itself before its commit is believed (M5): a `dev` an actor with
    # `packages: write` pointed somewhere else is red, never silently overwritten.
    current_content=$(read_record "$current") || {
      status=$?
      log "::error::publish-release: $CHANNEL_TAG names $current, which does not prove itself; the channel is not moved (repair: re-point it to a proven record — Klas's decision)"
      exit "$status"
    }
    current_sha=$(field "$current_content" JBL_RELEASE_SOURCE_SHA)
    [ "$current_sha" != "$sha" ] ||
      refuse "$CHANNEL_TAG names a different record ($current) for the same commit $sha; one commit has one record"
    status=0
    ancestor "$current_sha" "$sha" || status=$?
    case "$status" in
    0)
      move_tag "$RELEASE_REPO" "$CHANNEL_TAG" "$digest"
      log "$CHANNEL_TAG moved from $current_sha to $sha"
      ;;
    1)
      status=0
      ancestor "$sha" "$current_sha" || status=$?
      case "$status" in
      0)
        log "::notice::$CHANNEL_TAG names $current_sha, which is newer than $sha; the release stays published and nothing moves"
        return 0
        ;;
      1) refuse "$current_sha and $sha are on diverged histories; main is linear, so this is not a state to move from" ;;
      *) cannot_answer "could not order $sha against $current_sha" ;;
      esac
      ;;
    *) cannot_answer "this clone does not hold $current_sha (or $sha); releases cannot be ordered" ;;
    esac
    ;;
  esac
  landed=$(lookup "$RELEASE_REPO:$CHANNEL_TAG")
  [ "$landed" = "$digest" ] || refuse "$CHANNEL_TAG names '$landed' after the move, not $digest"

  # `latest` follows the channel, never the run. The box's pre-#1238 consumer still pulls these five;
  # they move seconds apart, which is narrower than the per-cell minutes it replaces and is not atomic —
  # only the record, applied by the box's own consumer, closes the mixed window.
  for name in "${NAMES[@]}"; do
    image=$(field "$content" "JBL_RELEASE_IMAGE_${name^^}")
    move_tag "$PREFIX-$name" latest "$image"
  done
  for name in "${NAMES[@]}"; do
    image=$(field "$content" "JBL_RELEASE_IMAGE_${name^^}")
    [ "$(lookup "$PREFIX-$name:latest")" = "$image" ] || refuse "$PREFIX-$name:latest does not name the record's image after the moves"
  done
  log "latest names the record's image for all ${#NAMES[@]}"
}

[ "$#" -ge 1 ] || cannot_answer "usage: $0 frozen|push|record|verify|advance …"
subcommand="$1"
shift
case "$subcommand" in
frozen) cmd_frozen "$@" ;;
push) cmd_push "$@" ;;
record) cmd_record "$@" ;;
verify) cmd_verify "$@" ;;
advance) cmd_advance "$@" ;;
*) cannot_answer "usage: unknown subcommand '$subcommand'" ;;
esac
