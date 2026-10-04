#!/usr/bin/env bash
#
# Fixture tests for publish-release.sh (#1238).
#
# Run:  bash .github/scripts/publish-release.test.sh
#
# NO DAEMON, NO REGISTRY, NO NETWORK. A file-backed fake registry answers the docker stub: tags map to
# digests, digests to record content and media types, and knobs make it deny, fail, wrap a moved tag in
# an index, or move `dev` behind the publisher's back. The verifier is a stub that knows which commit
# each digest was "built from". The REAL record tool runs (docker redirected to the stub), and the
# publisher runs inside a REAL git repository, because ordering releases is git ancestry.
#
# THE CONTRACT'S REGRESSION CLASSES (#1238 point 5), one or more cases each: a delayed/failed cell (an
# incomplete set), retries (an existing record proven, or refused and never rewritten), tag movement (a
# re-pointed record tag, `dev` moving under the publisher), an old run after a newer one (no move),
# wrong provenance (an image built from another commit), and the digest shape of every move.
#
# THREE OUTCOMES, NEVER COLLAPSED: 0 done · 1 refused · 2 could not answer.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/../.." && pwd)
readonly SUT="$script_dir/publish-release.sh"
readonly RECORD_TOOL_SRC="$repo_root/deploy/systemd/jobbliggaren-release-record.sh"
[ -f "$SUT" ] && [ -f "$RECORD_TOOL_SRC" ] || {
  echo "missing script under test" >&2
  exit 1
}

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT
readonly BIN="$TMPROOT/bin" REG="$TMPROOT/registry" REPO="$TMPROOT/repo" TOOLS="$TMPROOT/tools"
mkdir -p "$BIN" "$REG/tags" "$REG/blobs" "$REG/media" "$REG/built" "$REG/containers" "$REG/repodigests" "$REG/local-image" "$TOOLS"
SUT_PATH="$BIN:$(dirname "$(command -v git)"):/usr/bin:/bin"
readonly SUT_PATH

pass=0
fail=0

# --- the publisher and the record tool, pointed at the fixtures ---------------------------------------
readonly FIXTURE_SUT="$TOOLS/publish-release.sh"
sed -e "s#^readonly VERIFIER=.*#readonly VERIFIER=$TOOLS/verify-image-attestation.sh#" \
  -e "s#^readonly RECORD_TOOL=.*#readonly RECORD_TOOL=$TOOLS/jobbliggaren-release-record.sh#" \
  -e "s#^readonly VERIFY_INTERVAL=.*#readonly VERIFY_INTERVAL=0#" \
  "$SUT" >"$FIXTURE_SUT"
grep -qxF "readonly VERIFIER=$TOOLS/verify-image-attestation.sh" "$FIXTURE_SUT" &&
  grep -qxF "readonly RECORD_TOOL=$TOOLS/jobbliggaren-release-record.sh" "$FIXTURE_SUT" || {
  echo "FIXTURE BROKEN: the publisher's tool paths were not redirected" >&2
  exit 1
}
sed -e "s#/usr/bin/docker#docker#g" "$RECORD_TOOL_SRC" >"$TOOLS/jobbliggaren-release-record.sh"

# The verifier stub: a digest verifies when it is "attested" ($REG/attested/<digest> exists) and, given a
# commit, when that file names it. Every call is logged.
mkdir -p "$REG/attested"
cat >"$TOOLS/verify-image-attestation.sh" <<EOF
#!/usr/bin/env bash
printf '%s\n' "\$*" >>"$REG/verifier-calls"
d="\${1#*@}"
[ -f "$REG/attested/\$d" ] || exit 1
[ "\$#" -lt 2 ] || [ "\$(cat "$REG/attested/\$d")" = "\$2" ] || exit 1
exit 0
EOF
chmod +x "$TOOLS/verify-image-attestation.sh"

# --- the fake registry ----------------------------------------------------------------------------------
key() { printf '%s' "$1" | tr '/:@' '___'; }
set_tag() { printf '%s' "$2" >"$REG/tags/$(key "$1")"; }
get_tag() { cat "$REG/tags/$(key "$1")" 2>/dev/null || true; }
attest() { printf '%s' "$2" >"$REG/attested/$1"; }

cat >"$BIN/docker" <<'EOF'
#!/usr/bin/env bash
REG="__REG__"
key() { printf '%s' "$1" | tr '/:@' '___'; }
printf '%s\n' "$*" >>"$REG/docker-calls"
if [ "$1 $2 $3" = "buildx imagetools inspect" ]; then
  fmt="$5"; ref="$6"
  [ -f "$REG/fail-lookups" ] && { echo "ERROR: dial tcp: i/o timeout" >&2; exit 1; }
  case "$ref" in
  *@sha256:*) d="${ref#*@}"; [ -f "$REG/blobs/$d" ] || [ -f "$REG/media/$d" ] || { echo "ERROR: $ref: not found" >&2; exit 1; } ;;
  *)
    repo="${ref%:*}"
    if [ -f "$REG/deny" ] && [[ $repo == *-release ]] && [ ! -f "$REG/release-exists" ]; then
      echo "ERROR: failed to authorize: 403 Forbidden" >&2; exit 1
    fi
    [ -f "$REG/deny-tag/$(key "$ref")" ] && { echo "ERROR: failed to authorize: 403 Forbidden" >&2; exit 1; }
    # A concurrent writer: the N-th lookup of this tag answers something else.
    if [ -f "$REG/flip/$(key "$ref")" ]; then
      n=$(( $(cat "$REG/flip-count/$(key "$ref")" 2>/dev/null || echo 0) + 1 )); echo "$n" >"$REG/flip-count/$(key "$ref")"
      if [ "$n" -ge "$(cut -d' ' -f1 "$REG/flip/$(key "$ref")")" ]; then
        printf '"%s"\n' "$(cut -d' ' -f2 "$REG/flip/$(key "$ref")")"; exit 0
      fi
    fi
    d=$(cat "$REG/tags/$(key "$ref")" 2>/dev/null) || { echo "ERROR: $ref: not found" >&2; exit 1; }
    ;;
  esac
  case "$fmt" in
  *Digest*) printf '"%s"\n' "$d" ;;
  *MediaType*) printf '"%s"\n' "$(cat "$REG/media/$d" 2>/dev/null || echo application/vnd.docker.distribution.manifest.v2+json)" ;;
  esac
  exit 0
fi
if [ "$1 $2 $3" = "buildx imagetools create" ]; then
  # create --prefer-index=false -t REPO:TAG REPO@DIGEST
  [ "$4" = "--prefer-index=false" ] || { wrapped=1; }
  shift 3
  target="" src=""
  while [ "$#" -gt 0 ]; do case "$1" in -t) target="$2"; shift 2 ;; --prefer-index=false) shift ;; *) src="$1"; shift ;; esac; done
  d="${src#*@}"
  if [ -n "${wrapped:-}" ] || [ -f "$REG/wrap-moves" ]; then d="sha256:$(printf '%s' "index-of-$d" | sha256sum | cut -d' ' -f1)"; fi
  if [ -f "$REG/wrap-tags-matching" ] && [[ $target == *"$(cat "$REG/wrap-tags-matching")"* ]]; then d="sha256:$(printf '%s' "index-of-$d" | sha256sum | cut -d' ' -f1)"; fi
  printf '%s' "$d" >"$REG/tags/$(key "$target")"
  exit 0
fi
case "$1" in
pull) [ -f "$REG/fail-pulls" ] && exit 1; exit 0 ;;
build)
  ctx="${@: -1}"; cp "$ctx/release.env" "$REG/built/release.env"; exit 0 ;;
push)
  ref="${@: -1}"
  repo="${ref%:*}"
  if [[ $repo == *-release ]]; then
    d="sha256:$(sha256sum <"$REG/built/release.env" | cut -d' ' -f1)"
    cp "$REG/built/release.env" "$REG/blobs/$d"
    [ -f "$REG/push-as-index" ] && echo "application/vnd.oci.image.index.v1+json" >"$REG/media/$d"
    touch "$REG/release-exists"
  else
    # A cell's image: only one this "runner" built and loaded can be pushed.
    d=$(cat "$REG/local-image/$(key "$ref")" 2>/dev/null) || { echo "An image does not exist locally with the tag: $ref" >&2; exit 1; }
    printf 'image\n' >"$REG/media/$d"
  fi
  printf '%s' "$d" >"$REG/tags/$(key "$ref")"
  # What `docker image inspect` reports afterwards: the repo digest this push produced.
  printf '%s@%s\n' "$repo" "$d" >>"$REG/repodigests/$(key "$ref")"
  [ -f "$REG/two-repodigests" ] && printf '%s@sha256:%s\n' "$repo" "$(printf 'e%.0s' $(seq 64))" >>"$REG/repodigests/$(key "$ref")"
  [ -f "$REG/seal-during-push" ] && printf '%s' "sha256:$(printf 'f%.0s' $(seq 64))" >"$REG/tags/$(key "${ref%:pending-*}:sha-${ref##*:pending-}")"
  echo "$ref: digest: $d size: 1234"
  exit 0 ;;
image)
  [ "$2" = inspect ] || exit 99
  ref="${@: -1}"
  cat "$REG/repodigests/$(key "$ref")" 2>/dev/null || { echo "Error: No such image: $ref" >&2; exit 1; }
  exit 0 ;;
create)
  shift; name="" ref=""
  while [ "$#" -gt 0 ]; do case "$1" in --name) name="$2"; shift 2 ;; --pull|--network) shift 2 ;; *) [ -z "$ref" ] && ref="$1"; shift ;; esac; done
  d="${ref#*@}"; [ -f "$REG/blobs/$d" ] || exit 1
  printf '%s' "$d" >"$REG/containers/$name"; exit 0 ;;
cp)
  name="${2%%:*}"; d=$(cat "$REG/containers/$name") || exit 1
  t=$(mktemp -d); cp "$REG/blobs/$d" "$t/release.env"; (cd "$t" && tar -cf - release.env); rm -rf "$t"; exit 0 ;;
rm) exit 0 ;;
esac
echo "unexpected docker invocation: $*" >&2
exit 99
EOF
sed -i "s#__REG__#$REG#" "$BIN/docker"
chmod +x "$BIN/docker"

# --- a real git history: C1 <- C2 <- C3 on main, and D2 on a branch off C1 ---------------------------------
git_q() { git -C "$REPO" -c user.name=fixture -c user.email=fixture@example.invalid "$@" >/dev/null; }
mkdir -p "$REPO/src/I/Persistence/Migrations" "$REPO/src/I/Identity/Migrations" "$REPO/deploy/redis"
git init -q "$REPO"
git -C "$REPO" config core.autocrlf false
printf 'services: {}\n' >"$REPO/deploy/docker-compose.yml"
printf '#!/bin/sh\n' >"$REPO/deploy/redis/healthcheck.sh"
printf '    [DbContext(typeof(AppDbContext))]\n    [Migration("20260101000000_A")]\n' >"$REPO/src/I/Persistence/Migrations/A.Designer.cs"
printf '    [DbContext(typeof(AppIdentityDbContext))]\n    [Migration("20260101000000_B")]\n' >"$REPO/src/I/Identity/Migrations/B.Designer.cs"
git_q add -A && git_q commit -q -m c1
C1=$(git -C "$REPO" rev-parse HEAD)
git_q commit -q --allow-empty -m c2
C2=$(git -C "$REPO" rev-parse HEAD)
git_q commit -q --allow-empty -m c3
C3=$(git -C "$REPO" rev-parse HEAD)
git_q checkout -q -b side "$C1" && git_q commit -q --allow-empty -m d2
D2=$(git -C "$REPO" rev-parse HEAD)
git_q checkout -q "$C3"
readonly C1 C2 C3 D2

readonly PREFIX="ghcr.io/klasolsson81/jobbliggaren" RELEASE="ghcr.io/klasolsson81/jobbliggaren-release"
readonly NAMES=(api worker migrate web caddy)
digest_of() { printf 'sha256:%s' "$(printf '%s' "$1" | sha256sum | cut -d' ' -f1)"; }

reset_registry() {
  rm -rf "$REG"/tags/* "$REG"/blobs/* "$REG"/media/* "$REG"/attested/* "$REG"/containers/* "$REG"/flip "$REG"/flip-count "$REG"/repodigests/* "$REG"/local-image/* "$REG"/deny-tag
  mkdir -p "$REG/flip" "$REG/flip-count" "$REG/deny-tag"
  rm -f "$REG"/deny "$REG"/release-exists "$REG"/fail-lookups "$REG"/fail-pulls "$REG"/wrap-moves "$REG"/wrap-tags-matching "$REG"/push-as-index \
    "$REG"/seal-during-push "$REG"/docker-calls "$REG"/verifier-calls "$REG"/two-repodigests
}
# An image a cell built and loaded under <ref>, with the digest a push of it would produce.
local_image() { printf '%s' "$2" >"$REG/local-image/$(key "$1")"; }
# The five images a cell set published for <commit>, each attested as built from it.
publish_cells() {
  local sha="$1" name d
  for name in "${NAMES[@]}"; do
    d=$(digest_of "$name-$sha")
    set_tag "$PREFIX-$name:sha-${sha:0:7}" "$d"
    attest "$d" "$sha"
    printf 'image\n' >"$REG/media/$d"
  done
}
# A full release of <commit> as a previous run would have left it: cells, a record, attested, sealed.
release_of() {
  local sha="$1" out d
  publish_cells "$sha"
  out=$(cd "$REPO" && git checkout -q "$sha" 2>/dev/null && PATH="$SUT_PATH" bash "$FIXTURE_SUT" record "$sha" 2>/dev/null) || {
    git -C "$REPO" checkout -q "$C3"
    echo "FIXTURE BROKEN: could not build a release of $sha" >&2
    exit 1
  }
  git -C "$REPO" checkout -q "$C3"
  d=$(sed -n 's/^digest=//p' <<<"$out")
  attest "$d" "$sha"
  set_tag "$RELEASE:sha-$sha" "$d"
  printf '%s' "$d"
}

run_sut() {
  (cd "$REPO" && PATH="$SUT_PATH" bash "$FIXTURE_SUT" "$@") >"$TMPROOT/out" 2>"$TMPROOT/err"
}
expect_exit() {
  local want="$1" desc="$2" got=0
  shift 2
  run_sut "$@" || got=$?
  if [ "$got" -eq "$want" ]; then
    pass=$((pass + 1))
    echo "  ok   $desc (exit $got)"
  else
    fail=$((fail + 1))
    echo "  FAIL $desc — wanted exit $want, got $got" >&2
    sed 's/^/       /' "$TMPROOT/err" >&2
  fi
}
check() {
  if eval "$1"; then
    pass=$((pass + 1))
    echo "  ok   $2"
  else
    fail=$((fail + 1))
    echo "  FAIL $2" >&2
  fi
}
no_tag_moved() { ! grep -q "imagetools create" "$REG/docker-calls" 2>/dev/null; }
nothing_pushed() { ! grep -q "^push" "$REG/docker-calls" 2>/dev/null; }

echo "publish-release.sh"

echo "-- frozen"
reset_registry
expect_exit 0 "no record for the commit: not frozen" frozen "$C3" api
check 'grep -qx "frozen=false" "$TMPROOT/out"' "and it says frozen=false"

reset_registry
touch "$REG/deny"
expect_exit 0 "the release package unreadable (before the first publication): not frozen, with a warning" frozen "$C3" api
check 'grep -qx "frozen=false" "$TMPROOT/out" && grep -q "::warning::" "$TMPROOT/err"' "and the warning is on the run"

reset_registry
REL=$(release_of "$C3")
: >"$REG/docker-calls"
expect_exit 0 "a released commit whose record proves itself is frozen" frozen "$C3" api
check 'grep -qx "frozen=true" "$TMPROOT/out"' "and it says frozen=true"

reset_registry
REL2=$(release_of "$C2")
set_tag "$RELEASE:sha-$C3" "$REL2"
expect_exit 1 "a record tag re-pointed at another commit's record refuses — a tag vouches for nothing" frozen "$C3" api
check 'grep -q "never overwritten" "$TMPROOT/err"' "and it names the repair instead of overwriting"

reset_registry
REL=$(release_of "$C3")
attest "$(digest_of "api-$C3")" "$C2"
expect_exit 1 "a released commit whose own image no longer verifies as built from it refuses" frozen "$C3" api

reset_registry
touch "$REG/fail-lookups"
expect_exit 2 "a lookup that cannot answer is 'cannot answer', never 'not frozen'" frozen "$C3" api
expect_exit 1 "an image name outside the five refuses" frozen "$C3" mongo

echo "-- push: a cell's image, attested only as the digest its own push produced"
reset_registry
local_image "$PREFIX-api:sha-${C3:0:7}" "$(digest_of "api-$C3")"
expect_exit 0 "a cell's image is pushed under its sha- tag" push api "${C3:0:7}"
check 'grep -qx "digest=$(digest_of "api-$C3")" "$TMPROOT/out" && [ "$(get_tag "$PREFIX-api:sha-${C3:0:7}")" = "$(digest_of "api-$C3")" ]' \
  "and the digest printed for the attestation is the one the push produced"
reset_registry
local_image "$PREFIX-api:sha-${C3:0:7}" "$(digest_of "api-$C3")"
printf '1 %s' "$(digest_of racer)" >"$REG/flip/$(key "$PREFIX-api:sha-${C3:0:7}")"
expect_exit 1 "a tag re-pointed between the push and its read-back is refused" push api "${C3:0:7}"
check '! grep -q "^digest=" "$TMPROOT/out"' "and no digest is printed, so nothing is attested"
reset_registry
local_image "$PREFIX-api:sha-${C3:0:7}" "$(digest_of "api-$C3")"
touch "$REG/two-repodigests"
expect_exit 2 "an image carrying two digests for its repository cannot say which one it pushed" push api "${C3:0:7}"
check '! grep -q "^digest=" "$TMPROOT/out"' "and prints none"
reset_registry
expect_exit 1 "an image name outside the five refuses" push mongo "${C3:0:7}"
expect_exit 1 "a short sha that is not one refuses" push api latest
check 'nothing_pushed' "and nothing was pushed"

echo "-- record: a new release"
reset_registry
publish_cells "$C3"
expect_exit 0 "five images verified as built from the commit become one record" record "$C3"
NEW=$(sed -n 's/^digest=//p' "$TMPROOT/out")
check 'grep -qx "attest=true" "$TMPROOT/out" && grep -qx "repo=$RELEASE" "$TMPROOT/out"' "and it asks for the record to be attested, naming the release repository"
check '[ "$(get_tag "$RELEASE:pending-$C3")" = "$NEW" ]' "the record is pushed under the PENDING tag"
check '[ -z "$(get_tag "$RELEASE:sha-$C3")" ]' "and the sealed name does not exist yet — it is created only after the record verifies"
check 'grep -qx "JBL_RELEASE_SOURCE_SHA=$C3" "$REG/blobs/$NEW" && grep -qx "JBL_RELEASE_IMAGE_WEB=$(digest_of "web-$C3")" "$REG/blobs/$NEW"' \
  "the record names the commit and the digests the cells published"
check 'no_tag_moved' "and nothing mutable moved"

reset_registry
publish_cells "$C3"
rm -f "$REG/tags/$(key "$PREFIX-caddy:sha-${C3:0:7}")"
expect_exit 1 "a cell that published nothing (failed or still running) leaves no complete set and no record" record "$C3"
check 'nothing_pushed' "and nothing was pushed"

reset_registry
publish_cells "$C3"
attest "$(digest_of "web-$C3")" "$C2"
expect_exit 1 "an image built from ANOTHER commit refuses the whole set — no mixed release" record "$C3"
check 'nothing_pushed' "and nothing was pushed"

reset_registry
publish_cells "$C3"
touch "$REG/push-as-index"
expect_exit 1 "a record pushed as an index refuses — its digest would not survive the tag moves" record "$C3"

reset_registry
publish_cells "$C3"
touch "$REG/seal-during-push"
expect_exit 1 "a sealed name that appears during the push is never raced" record "$C3"

reset_registry
publish_cells "$C3"
printf '1 %s' "$(digest_of racer)" >"$REG/flip/$(key "$RELEASE:pending-$C3")"
expect_exit 1 "a pending tag re-pointed between the push and its read-back is refused" record "$C3"
check '! grep -q "^digest=" "$TMPROOT/out" && ! grep -q "^attest=" "$TMPROOT/out"' "and nothing is handed to the attest step"

reset_registry
publish_cells "$C3"
touch "$REG/deny"
expect_exit 0 "the very first publication (package absent, lookups denied) still publishes" record "$C3"
check 'grep -qx "attest=true" "$TMPROOT/out"' "and asks for the attestation"

echo "-- record: a retry"
reset_registry
REL=$(release_of "$C3")
: >"$REG/docker-calls"
expect_exit 0 "an existing record that proves itself is reused" record "$C3"
check 'grep -qx "digest=$REL" "$TMPROOT/out" && grep -qx "attest=false" "$TMPROOT/out"' "it is the existing digest and needs no new attestation"
check 'nothing_pushed' "and nothing is pushed — a release is never rewritten"

reset_registry
REL2=$(release_of "$C2")
publish_cells "$C3"
set_tag "$RELEASE:sha-$C3" "$REL2"
: >"$REG/docker-calls"
expect_exit 1 "an existing record that is another commit's is refused and never rewritten" record "$C3"
check 'nothing_pushed' "and nothing is pushed"

reset_registry
REL=$(release_of "$C3")
rm -f "$REG/attested/$REL"
expect_exit 1 "an existing record that does not verify is refused, not re-attested over unread bytes" record "$C3"
check '! grep -q "^create" "$REG/docker-calls"' "and not one byte of it was copied out"

echo "-- verify"
reset_registry
publish_cells "$C3"
run_sut record "$C3"
NEW=$(sed -n 's/^digest=//p' "$TMPROOT/out")
attest "$NEW" "$C3"
expect_exit 0 "an attested record verifies" verify "$NEW" "$C3"
rm -f "$REG/attested/$NEW"
: >"$REG/verifier-calls"
expect_exit 1 "a record whose attestation never becomes readable fails after the bounded retries" verify "$NEW" "$C3"
ATTEMPTS=$(sed -n 's/^readonly VERIFY_ATTEMPTS=//p' "$SUT")
check '[ "$ATTEMPTS" -gt 1 ] && [ "$(grep -cxF "$RELEASE@$NEW" "$REG/verifier-calls")" -eq "$ATTEMPTS" ]' "and it read the record's identity exactly VERIFY_ATTEMPTS times, and that is more than once"

echo "-- advance"
fresh_release() { # <commit>: cells published, record pushed and attested, not sealed; prints the digest
  publish_cells "$1"
  local out
  out=$(cd "$REPO" && git checkout -q "$1" && PATH="$SUT_PATH" bash "$FIXTURE_SUT" record "$1" 2>/dev/null)
  git -C "$REPO" checkout -q "$C3"
  local d
  d=$(sed -n 's/^digest=//p' <<<"$out")
  attest "$d" "$1"
  printf '%s' "$d"
}
latest_is_record() {
  local name
  for name in "${NAMES[@]}"; do
    [ "$(get_tag "$PREFIX-$name:latest")" = "$(digest_of "$name-$1")" ] || return 1
  done
}

reset_registry
NEW=$(fresh_release "$C3")
expect_exit 0 "the channel's first release: sealed, dev created, latest moved" advance "$NEW" "$C3"
check '[ "$(get_tag "$RELEASE:sha-$C3")" = "$NEW" ] && [ "$(get_tag "$RELEASE:dev")" = "$NEW" ]' "sha-<commit> and dev both name the record"
check 'latest_is_record "$C3"' "all five latest tags name the record's images"

reset_registry
OLD=$(release_of "$C2")
set_tag "$RELEASE:dev" "$OLD"
NEW=$(fresh_release "$C3")
expect_exit 0 "dev on an ancestor moves forward to the new release" advance "$NEW" "$C3"
check '[ "$(get_tag "$RELEASE:dev")" = "$NEW" ] && latest_is_record "$C3"' "dev and latest name the new release"

reset_registry
NEWER=$(release_of "$C3")
set_tag "$RELEASE:dev" "$NEWER"
OLD=$(fresh_release "$C2")
: >"$REG/docker-calls"
expect_exit 0 "a re-run of an OLDER commit publishes its record and moves nothing" advance "$OLD" "$C2"
check '[ "$(get_tag "$RELEASE:dev")" = "$NEWER" ]' "dev still names the newer release"
check '! grep -q "imagetools create -t\|create --prefer-index=false -t ghcr.io/klasolsson81/jobbliggaren-api:latest" "$REG/docker-calls"' "latest is left alone"
check '[ "$(get_tag "$RELEASE:sha-$C2")" = "$OLD" ]' "but the old commit's record is sealed under its own name"

reset_registry
NEW=$(release_of "$C3")
set_tag "$RELEASE:dev" "$NEW"
expect_exit 0 "dev already naming this release moves nothing and converges latest" advance "$NEW" "$C3"
check 'latest_is_record "$C3"' "latest names the record's images"

reset_registry
NEW=$(fresh_release "$C3")
set_tag "$RELEASE:dev" "$(digest_of junk)"
printf 'junk\n' >"$REG/blobs/$(digest_of junk)"
: >"$REG/docker-calls"
expect_exit 1 "a dev that does not prove itself is red and never overwritten" advance "$NEW" "$C3"
check '[ "$(get_tag "$RELEASE:dev")" = "$(digest_of junk)" ]' "dev is untouched"

reset_registry
SIDE=$(release_of "$D2")
set_tag "$RELEASE:dev" "$SIDE"
NEW=$(fresh_release "$C3")
expect_exit 1 "dev on a diverged history is red — main is linear" advance "$NEW" "$C3"
check '[ "$(get_tag "$RELEASE:dev")" = "$SIDE" ]' "and dev is untouched"

reset_registry
NEW=$(fresh_release "$C3")
OTHER=$(release_of "$C2")
set_tag "$RELEASE:sha-$C3" "$OTHER"
expect_exit 1 "a sealed name that already names another record is never rewritten" advance "$NEW" "$C3"
check '[ "$(get_tag "$RELEASE:sha-$C3")" = "$OTHER" ] && [ -z "$(get_tag "$RELEASE:dev")" ]' "and nothing moved"

reset_registry
NEW=$(fresh_release "$C3")
touch "$REG/wrap-moves"
expect_exit 1 "a move that lands on a re-wrapped (index) digest is red — no attestation names the wrapper" advance "$NEW" "$C3"

reset_registry
NEW=$(fresh_release "$C3")
rm -f "$REG/attested/$NEW"
: >"$REG/docker-calls"
expect_exit 1 "the mover proves its own precondition: an unproven record moves nothing" advance "$NEW" "$C3"
check 'no_tag_moved' "and nothing moved"

# Killed a surviving mutant: a precondition read WITHOUT the commit passed any proven record.
reset_registry
OTHER=$(fresh_release "$C2")
: >"$REG/docker-calls"
expect_exit 1 "the mover refuses a proven record that is ANOTHER commit's release" advance "$OTHER" "$C3"
check 'no_tag_moved' "and nothing moved"

# Killed a surviving mutant: without the per-move read-back only dev and latest were re-checked
# later, so a seal landing on a re-wrapped digest went unnoticed.
reset_registry
NEW=$(fresh_release "$C3")
printf ':sha-' >"$REG/wrap-tags-matching"
expect_exit 1 "a seal that lands on a re-wrapped digest is red even when dev and latest land right" advance "$NEW" "$C3"

reset_registry
NEW=$(fresh_release "$C3")
printf '2 %s' "$(digest_of moved-by-someone)" >"$REG/flip/$(key "$RELEASE:dev")"
expect_exit 1 "dev moved by someone else between the move and the read-back is red" advance "$NEW" "$C3"

reset_registry
OLD=$(release_of "$C2")
set_tag "$RELEASE:dev" "$OLD"
NEW=$(fresh_release "$C3")
git -C "$REPO" update-ref -d refs/heads/side >/dev/null 2>&1 || true
expect_exit 0 "ordering reads ancestry from the clone" advance "$NEW" "$C3"

reset_registry
NEW=$(fresh_release "$C3")
touch "$REG/deny-tag/$(key "$RELEASE:dev")"
expect_exit 2 "a dev the registry will not show cannot be answered" advance "$NEW" "$C3"
check 'grep -q "the channel is not moved" "$TMPROOT/err" && ! latest_is_record "$C3"' "the channel is not moved, and latest is left alone"

reset_registry
NEW=$(fresh_release "$C3")
C2REC=$(fresh_release "$C2")
UNKNOWN=$(printf '%s' not-in-this-clone | sha1sum | cut -d' ' -f1)
sed "s/^JBL_RELEASE_SOURCE_SHA=.*/JBL_RELEASE_SOURCE_SHA=$UNKNOWN/" "$REG/blobs/$C2REC" >"$REG/alien.env"
ALIEN="sha256:$(sha256sum <"$REG/alien.env" | cut -d' ' -f1)"
mv "$REG/alien.env" "$REG/blobs/$ALIEN"
attest "$ALIEN" "$UNKNOWN"
set_tag "$RELEASE:dev" "$ALIEN"
expect_exit 2 "a dev whose commit this clone does not hold cannot be ordered" advance "$NEW" "$C3"
check '[ "$(get_tag "$RELEASE:dev")" = "$ALIEN" ] && grep -q "does not hold" "$TMPROOT/err"' "dev is untouched, and the refusal names why"

echo "-- usage"
expect_exit 2 "an unknown subcommand is a usage error" publish
expect_exit 1 "a malformed source sha is refused" record "NOTASHA"
expect_exit 1 "a malformed digest is refused" advance "sha256:short" "$C3"

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ]
