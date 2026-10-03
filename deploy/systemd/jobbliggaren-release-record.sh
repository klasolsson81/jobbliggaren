#!/usr/bin/env bash
# jobbliggaren-release-record — the one home of the release record format (#1238).
#
# usage:  jobbliggaren-release-record.sh read <release-repo@sha256:digest> [<expected-source-sha>]
#         jobbliggaren-release-record.sh validate <record-file>
#         jobbliggaren-release-record.sh receipt <receipt-file>
#         jobbliggaren-release-record.sh deploy-hash <checkout-root>
#         jobbliggaren-release-record.sh migrations <checkout-root>
#         jobbliggaren-release-record.sh emit --source-sha <40-hex> --api <sha256:…> --worker <…>
#                                         --migrate <…> --web <…> --caddy <…> --source-root <dir>
# stdout: `read`, `validate` and `emit` print a validated record; `receipt` the validated receipt;
#         `deploy-hash` one hash; `migrations` one `APP|IDENTITY <id>` line per migration
# stderr: every diagnostic
# exit:   0 done · 1 refused: not a valid or not a proven record, or inputs that cannot make one ·
#         2 could not answer: unreadable input, usage, an unreachable registry, a tree it does not model
#
# WHAT A RECORD IS. One release: one source commit on main, the five images built from it, the
# deployment files those images were built to run under, and the EF migrations the migrate image
# carries. `release-images.yml` emits it only after all five images verified as built from that
# commit, ships it as a scratch image in ghcr.io/klasolsson81/jobbliggaren-release, attests it, and
# only then names it from the `dev` channel. The box reads one record, verifies everything it names,
# and applies nothing it does not name.
#
# ONE FILE FOR BOTH SIDES. The publisher and the box call this script, as they already share
# verify-image-attestation.sh: a format with two parsers is two formats, and a trust ordering with two
# implementations is two orderings.
#
# LINES, NOT JSON. The reader runs as root on a box with no JSON tool established there. A closed set
# of keys, each once, in a fixed order, each value inside an anchored character class (no quotes,
# spaces or `$`), is validated without an interpreter, and a record is never `source`d or `eval`d.
#
# DELIBERATELY ABSENT: registry and repository names (the consumer derives
# `ghcr.io/klasolsson81/jobbliggaren-<name>` itself, so a record names digests and cannot redirect a
# pull), and timestamps or run ids (the record is a pure function of its inputs, so "the same release"
# is byte equality; which run published it is in the record's own attestation).
set -euo pipefail

readonly FORMAT_VERSION=1
readonly REPOSITORY="klasolsson81/jobbliggaren"
readonly SOURCE_REF="refs/heads/main"
# A record is about 5 KiB today (ninety migration ids). The cap bounds what a root reader will copy
# out of an image before it has refused it.
readonly MAX_BYTES=65536
readonly VERIFIER="${BASH_SOURCE[0]%/*}/verify-image-attestation.sh"

readonly -a KEYS=(
  JBL_RELEASE_FORMAT
  JBL_RELEASE_REPOSITORY
  JBL_RELEASE_SOURCE_REF
  JBL_RELEASE_SOURCE_SHA
  JBL_RELEASE_SEQUENCE
  JBL_RELEASE_IMAGE_API
  JBL_RELEASE_IMAGE_WORKER
  JBL_RELEASE_IMAGE_MIGRATE
  JBL_RELEASE_IMAGE_WEB
  JBL_RELEASE_IMAGE_CADDY
  JBL_RELEASE_DEPLOY_SHA256
  JBL_RELEASE_MIGRATIONS_APP
  JBL_RELEASE_MIGRATIONS_IDENTITY
)

# The deployment files the box reads at apply time and a release is bound to: the compose file, and
# every file it bind-mounts by relative path. The fixture suite reads the real compose file and fails
# if it references a relative path missing here. Files the host's own scripts render or check
# (the Redis ACL templates) are not here: `jobbliggaren-redis-secrets.sh --check-images` already
# refuses an apply whose rendered policy does not match them.
readonly -a DEPLOY_FILES=(
  deploy/docker-compose.yml
  deploy/redis/healthcheck.sh
)

readonly MIGRATION_ID_RE='[0-9]{14}_[A-Za-z0-9_]+'

log() { printf '%s\n' "$*" >&2; }
refuse() {
  log "REFUSING: $*"
  exit 1
}
cannot_answer() {
  log "CANNOT ANSWER: $*"
  exit 2
}

ids_sorted_unique() {
  local -a ids
  IFS=, read -r -a ids <<<"$1"
  [ "$(printf '%s\n' "${ids[@]}" | LC_ALL=C sort -u)" = "$(printf '%s\n' "${ids[@]}")" ]
}

value_ok() {
  local key="$1" value="$2"
  case "$key" in
  JBL_RELEASE_FORMAT) [[ $value == "$FORMAT_VERSION" ]] ;;
  JBL_RELEASE_REPOSITORY) [[ $value == "$REPOSITORY" ]] ;;
  JBL_RELEASE_SOURCE_REF) [[ $value == "$SOURCE_REF" ]] ;;
  JBL_RELEASE_SOURCE_SHA) [[ $value =~ ^[0-9a-f]{40}$ ]] ;;
  JBL_RELEASE_SEQUENCE) [[ $value =~ ^[1-9][0-9]{0,8}$ ]] ;;
  JBL_RELEASE_IMAGE_*) [[ $value =~ ^sha256:[0-9a-f]{64}$ ]] ;;
  JBL_RELEASE_DEPLOY_SHA256) [[ $value =~ ^[0-9a-f]{64}$ ]] ;;
  JBL_RELEASE_MIGRATIONS_*)
    [[ $value =~ ^${MIGRATION_ID_RE}(,${MIGRATION_ID_RE})*$ ]] && ids_sorted_unique "$value"
    ;;
  *) return 1 ;;
  esac
}

field() { sed -n "s/^$2=//p" <<<"$1"; }

# Byte-counted, not `grep $'\r'`: Git for Windows' grep reads files in text mode and never sees a CR, so
# a grep-based check passes CRLF bytes on a developer host — measured while writing this file.
has_cr() { [ "$(tr -d '\r' <"$1" | wc -c)" -ne "$(wc -c <"$1")" ]; }

cmd_validate() {
  [ "$#" -eq 1 ] || cannot_answer "usage: $0 validate <record-file>"
  local file="$1"
  [ -f "$file" ] && [ -r "$file" ] || cannot_answer "not a readable file: $file"

  local size
  size=$(wc -c <"$file") || cannot_answer "could not measure $file"
  [ "$size" -le "$MAX_BYTES" ] || refuse "record is $size bytes; a release record is never above $MAX_BYTES"
  [ "$size" -gt 0 ] || refuse "record is empty"
  # A CR would ride inside the last value of every line and a NUL would truncate it, so both are
  # refused before anything is split.
  if has_cr "$file"; then refuse "record contains a carriage return"; fi
  if [ "$(LC_ALL=C tr -d '\000' <"$file" | wc -c)" -ne "$size" ]; then refuse "record contains a NUL byte"; fi
  # Command substitution strips one trailing newline, so an empty result means "ends in \n".
  [ -z "$(tail -c 1 "$file")" ] || refuse "record does not end with a newline"

  local -a lines
  mapfile -t lines <"$file"
  [ "${#lines[@]}" -eq "${#KEYS[@]}" ] ||
    refuse "record has ${#lines[@]} lines; a release record has exactly ${#KEYS[@]}"

  local i key value
  for i in "${!KEYS[@]}"; do
    key="${lines[$i]%%=*}"
    value="${lines[$i]#*=}"
    [ "$key" = "${KEYS[$i]}" ] && [ "${lines[$i]}" != "$key" ] ||
      refuse "line $((i + 1)) must be ${KEYS[$i]}=<value>"
    value_ok "$key" "$value" || refuse "line $((i + 1)): $key has a value outside its format"
  done

  cat -- "$file"
}

verify_record() {
  local status=0
  "$VERIFIER" "$@" >&2 || status=$?
  return "$status"
}

# Reads a record image and prints its record, in the only order in which its bytes may be trusted:
#
#   1. the record digest verifies as built by our workflow on main — BEFORE a single byte of it is
#      copied out, so nothing a registry writer placed there reaches dockerd's archive path, tar or the
#      validator unproven (security-auditor, form round 2026-10-03, Major 1);
#   2. the one file is copied out of a container that is created and never started, capped, and
#      accepted only as a tar holding exactly one regular member named release.env;
#   3. the record validates;
#   4. the digest verifies AGAIN, now bound to the commit the record names: an attestation is stored
#      per digest, so without this a record from another commit could stand under any tag;
#   5. with <expected-source-sha>, the record must name exactly that commit — a tag is a policy, and
#      `release:sha-<X>` re-pointed at Y's record carries Y's valid attestation with it (Major 2).
cmd_read() {
  [ "$#" -ge 1 ] && [ "$#" -le 2 ] || cannot_answer "usage: $0 read <release-repo@sha256:digest> [<expected-source-sha>]"
  local ref="$1" expected="${2:-}"
  [[ $ref =~ ^[a-z0-9][a-z0-9.-]*(:[0-9]+)?(/[a-z0-9._-]+)+@sha256:[0-9a-f]{64}$ ]] ||
    cannot_answer "read takes a digest reference, got '$ref'"
  [ -z "$expected" ] || [[ $expected =~ ^[0-9a-f]{40}$ ]] ||
    cannot_answer "expected source sha must be 40 lowercase hex, got '$expected'"
  [ -x "$VERIFIER" ] || cannot_answer "verifier missing or not executable: $VERIFIER"

  local status=0
  verify_record "$ref" || status=$?
  [ "$status" -eq 0 ] || {
    log "REFUSING: $ref is not a record our workflow attested (verifier exit $status); not one byte of it was read"
    exit "$status"
  }

  local work name
  work=$(mktemp -d)
  # A generated name: a fixed one would collide with a crashed predecessor's container, and the trap
  # removes this run's container whatever happens after it exists.
  name="jbl-release-read-$$-$RANDOM"
  # shellcheck disable=SC2064 # expand now: the trap must remove THIS container and THIS directory
  trap "/usr/bin/docker rm -f -v '$name' >/dev/null 2>&1 || true; rm -rf -- '$work'" EXIT
  /usr/bin/docker create --name "$name" --pull never --network none "$ref" /none >/dev/null 2>&1 ||
    cannot_answer "could not create a container from $ref (is it pulled?)"
  # The archive is capped before anything parses it. A record within the cap makes a tar of well under
  # MAX_BYTES + 8 KiB (one 512-byte header, padding, two end blocks); reaching the cap means oversized.
  /usr/bin/docker cp "$name:/release.env" - 2>/dev/null | head -c "$((MAX_BYTES + 8192))" >"$work/record.tar" || true
  [ -s "$work/record.tar" ] || refuse "$ref holds no /release.env"
  [ "$(wc -c <"$work/record.tar")" -lt "$((MAX_BYTES + 8192))" ] || refuse "$ref's /release.env is larger than a record can be"

  # Exactly one member, a regular file, named release.env — a symlink, a directory or a second entry is
  # not a record. `tar -tv` prints the type in the first column: `-` is a regular file.
  local listing
  listing=$(tar -tvf "$work/record.tar" 2>/dev/null) || refuse "$ref's /release.env is not a readable archive"
  [ "$(printf '%s\n' "$listing" | grep -c .)" -eq 1 ] || refuse "$ref's archive holds more than one member"
  [[ $listing == -* && $listing =~ [[:space:]]release\.env$ ]] ||
    refuse "$ref's /release.env is not a regular file"
  tar -xOf "$work/record.tar" release.env >"$work/release.env" 2>/dev/null || refuse "$ref's /release.env could not be extracted"

  local record source_sha
  record=$(cmd_validate "$work/release.env")
  source_sha=$(field "$record" JBL_RELEASE_SOURCE_SHA)

  status=0
  verify_record "$ref" "$source_sha" || status=$?
  [ "$status" -eq 0 ] || {
    log "REFUSING: $ref names source $source_sha, and its attestation was not made by a build of that commit (verifier exit $status)"
    exit "$status"
  }
  [ -z "$expected" ] || [ "$source_sha" = "$expected" ] ||
    refuse "$ref is the release of $source_sha, not of $expected"

  printf '%s\n' "$record"
}

# The compose file as it is hashed: without its full-line comments and blank lines, which make up most of
# the file and change far more often than its configuration. Trailing comments stay — telling one from a
# `#` inside a value needs a YAML parser, and the box has none. The fixture suite proves, on every commit,
# that this form and the committed file resolve to the identical compose model
# (`config --no-interpolate --no-path-resolution --profile ops`), so the hash ignores exactly what compose
# ignores.
canonical_compose() {
  sed -E '/^[[:space:]]*#/d; /^[[:space:]]*$/d' -- "$1"
}

# The hash a release binds its deployment files to: the canonical compose form, and every other bound file
# as raw bytes. Never `docker compose config` output, which would interpolate `.env`'s credentials into what
# is hashed. A carriage return anywhere is refused rather than hashed: the files are pinned LF in
# .gitattributes, so a CR means a checkout that is not what git holds.
cmd_deploy_hash() {
  [ "$#" -eq 1 ] || cannot_answer "usage: $0 deploy-hash <checkout-root>"
  local root="$1" file listing digest
  listing=""
  for file in "${DEPLOY_FILES[@]}"; do
    [ -f "$root/$file" ] && [ -r "$root/$file" ] || cannot_answer "deployment file missing: $root/$file"
    if has_cr "$root/$file"; then
      refuse "$root/$file contains carriage returns; it is pinned LF, so this is not the committed file"
    fi
    if [ "$file" = deploy/docker-compose.yml ]; then
      digest=$(canonical_compose "$root/$file" | sha256sum | cut -d' ' -f1)
    else
      digest=$(sha256sum -- "$root/$file" | cut -d' ' -f1)
    fi
    listing+="$digest  $file"$'\n'
  done
  printf '%s' "$listing" | sha256sum | cut -d' ' -f1
}

# What the box recorded after its last successful apply: one line naming the record's digest, then the
# record itself. Held to the same validator as a fresh record, so a receipt cannot carry anything a record
# could not.
cmd_receipt() {
  [ "$#" -eq 1 ] || cannot_answer "usage: $0 receipt <receipt-file>"
  local file="$1" first tmp status=0
  [ -f "$file" ] && [ -r "$file" ] || cannot_answer "not a readable file: $file"
  [ "$(wc -c <"$file")" -le "$((MAX_BYTES + 256))" ] || refuse "receipt is larger than a receipt can be"
  first=$(head -n 1 -- "$file")
  [[ $first =~ ^JBL_RECEIPT_RECORD_DIGEST=sha256:[0-9a-f]{64}$ ]] ||
    refuse "receipt line 1 must be JBL_RECEIPT_RECORD_DIGEST=sha256:<64 hex>"
  tmp=$(mktemp)
  tail -n +2 -- "$file" >"$tmp"
  (cmd_validate "$tmp" >/dev/null) || status=$?
  rm -f -- "$tmp"
  [ "$status" -eq 0 ] || exit "$status"
  cat -- "$file"
}

# One line per EF migration: `APP <id>` or `IDENTITY <id>`, read from the `[Migration("…")]` attribute
# EF itself reads and the `[DbContext(typeof(…))]` beside it in the same file. Every shape it does not
# model stops it rather than skipping a file: a migration missed here is a migration the record does
# not declare.
cmd_migrations() {
  [ "$#" -eq 1 ] || cannot_answer "usage: $0 migrations <checkout-root>"
  local root="$1" file id ctx
  local -a id_lines ctx_lines
  [ -d "$root/src" ] || cannot_answer "no src/ under $root"
  while IFS= read -r -d '' file; do
    mapfile -t id_lines < <(grep -F '[Migration(' -- "$file" | tr -d '\r')
    mapfile -t ctx_lines < <(grep -F '[DbContext(' -- "$file" | tr -d '\r')
    [ "${#id_lines[@]}" -eq 1 ] && [ "${#ctx_lines[@]}" -eq 1 ] ||
      cannot_answer "$file carries ${#id_lines[@]} [Migration(…)] and ${#ctx_lines[@]} [DbContext(…)] line(s); expected one of each"
    [[ ${id_lines[0]} =~ ^[[:space:]]*\[Migration\(\"(${MIGRATION_ID_RE})\"\)\][[:space:]]*$ ]] ||
      cannot_answer "unrecognised [Migration(…)] shape in $file: ${id_lines[0]}"
    id="${BASH_REMATCH[1]}"
    [[ ${ctx_lines[0]} =~ ^[[:space:]]*\[DbContext\(typeof\(([A-Za-z0-9_.]+)\)\)\][[:space:]]*$ ]] ||
      cannot_answer "unrecognised [DbContext(…)] shape in $file: ${ctx_lines[0]}"
    ctx="${BASH_REMATCH[1]}"
    case "$ctx" in
    AppDbContext) printf 'APP %s\n' "$id" ;;
    AppIdentityDbContext) printf 'IDENTITY %s\n' "$id" ;;
    *) cannot_answer "$file names DbContext '$ctx', which the record format does not carry — extend it first" ;;
    esac
  done < <(grep -rlZ --include='*.cs' --exclude-dir=bin --exclude-dir=obj -F '[Migration(' -- "$root/src" || true)
}

join_ids() {
  local prefix="$1" listing="$2" joined
  joined=$(printf '%s\n' "$listing" | sed -n "s/^$prefix //p" | LC_ALL=C sort | paste -sd, -)
  [ -n "$joined" ] || cannot_answer "no $prefix migrations found under the source root"
  printf '%s' "$joined"
}

cmd_emit() {
  local source_sha="" root="" api="" worker="" migrate="" web="" caddy=""
  while [ "$#" -gt 0 ]; do
    [ "$#" -ge 2 ] || cannot_answer "usage: option $1 needs a value"
    case "$1" in
    --source-sha) source_sha="$2" ;;
    --api) api="$2" ;;
    --worker) worker="$2" ;;
    --migrate) migrate="$2" ;;
    --web) web="$2" ;;
    --caddy) caddy="$2" ;;
    --source-root) root="$2" ;;
    *) cannot_answer "usage: unknown option $1" ;;
    esac
    shift 2
  done

  [[ $source_sha =~ ^[0-9a-f]{40}$ ]] || refuse "--source-sha must be 40 lowercase hex, got '$source_sha'"
  local name digest
  for name in api worker migrate web caddy; do
    digest="${!name}"
    [[ $digest =~ ^sha256:[0-9a-f]{64}$ ]] || refuse "--$name must be sha256:<64 hex>, got '$digest'"
  done
  [ -d "$root" ] || cannot_answer "source root is not a directory: $root"

  # The files read below must be the commit the record names, and the sequence must count all of its
  # history: a shallow clone counts 1 for every commit, which would make every release look like the
  # first one and the box's anti-rollback check silently inert.
  local shallow head sequence
  shallow=$(git -C "$root" rev-parse --is-shallow-repository 2>/dev/null) || cannot_answer "$root is not a git checkout"
  [ "$shallow" = false ] || cannot_answer "$root is a shallow clone; the release sequence needs full history"
  head=$(git -C "$root" rev-parse HEAD) || cannot_answer "could not read HEAD in $root"
  [ "$head" = "$source_sha" ] || refuse "the tree at $root is $head, not the source commit $source_sha"
  git -C "$root" diff --quiet HEAD -- || refuse "the tree at $root has uncommitted changes"
  sequence=$(git -C "$root" rev-list --count "$source_sha") || cannot_answer "could not count the history of $source_sha"

  local deploy listing app identity
  deploy=$(cmd_deploy_hash "$root")
  listing=$(cmd_migrations "$root")
  app=$(join_ids APP "$listing")
  identity=$(join_ids IDENTITY "$listing")

  local tmp
  tmp=$(mktemp)
  # shellcheck disable=SC2064 # expand now: the trap must remove THIS file
  trap "rm -f -- '$tmp'" EXIT
  {
    printf 'JBL_RELEASE_FORMAT=%s\n' "$FORMAT_VERSION"
    printf 'JBL_RELEASE_REPOSITORY=%s\n' "$REPOSITORY"
    printf 'JBL_RELEASE_SOURCE_REF=%s\n' "$SOURCE_REF"
    printf 'JBL_RELEASE_SOURCE_SHA=%s\n' "$source_sha"
    printf 'JBL_RELEASE_SEQUENCE=%s\n' "$sequence"
    printf 'JBL_RELEASE_IMAGE_API=%s\n' "$api"
    printf 'JBL_RELEASE_IMAGE_WORKER=%s\n' "$worker"
    printf 'JBL_RELEASE_IMAGE_MIGRATE=%s\n' "$migrate"
    printf 'JBL_RELEASE_IMAGE_WEB=%s\n' "$web"
    printf 'JBL_RELEASE_IMAGE_CADDY=%s\n' "$caddy"
    printf 'JBL_RELEASE_DEPLOY_SHA256=%s\n' "$deploy"
    printf 'JBL_RELEASE_MIGRATIONS_APP=%s\n' "$app"
    printf 'JBL_RELEASE_MIGRATIONS_IDENTITY=%s\n' "$identity"
  } >"$tmp"

  # The emitter is held to the validator, so the two can never disagree about what a record is.
  cmd_validate "$tmp"
}

[ "$#" -ge 1 ] || cannot_answer "usage: $0 read|validate|receipt|deploy-hash|migrations|emit …"
subcommand="$1"
shift
case "$subcommand" in
read) cmd_read "$@" ;;
validate) cmd_validate "$@" ;;
receipt) cmd_receipt "$@" ;;
deploy-hash) cmd_deploy_hash "$@" ;;
migrations) cmd_migrations "$@" ;;
emit) cmd_emit "$@" ;;
*) cannot_answer "usage: unknown subcommand '$subcommand'" ;;
esac
