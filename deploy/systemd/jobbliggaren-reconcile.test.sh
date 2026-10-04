#!/usr/bin/env bash
#
# Fixture tests for jobbliggaren-reconcile.sh (#1238, #1295).
#
# Run:  bash deploy/systemd/jobbliggaren-reconcile.test.sh
#
# WHAT RUNS FOR REAL AND WHAT IS STUBBED. The wrapper needs a daemon, a registry and root, so `docker`
# is a stub backed by files: a registry (tags → digests, digests → record bytes), a local image store
# (references → image ids, the way `docker image inspect` answers), compose's resolved model, and the
# containers an `up` leaves running. The verifier is a stub that knows which commit each digest was
# "built from". Everything else is the real code: the wrapper, the record tool it reads releases with,
# and the runtime-id helper the secrets gate measures with.
#
# THE ORDERING PROPERTIES ARE THE POINT, and an ordering cannot be seen on a happy path. Markers make
# the never-happened assertable: a record copied out before its identity verified, an image tagged
# `:applied` before every image verified, an image RUN before it verified, a receipt written for an
# apply that did not measurably happen.
#
# THREE OUTCOMES, NEVER COLLAPSED: 0 applied · 1 refused · 2 could not answer.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/jobbliggaren-reconcile.sh"
readonly RECORD_TOOL_SRC="$script_dir/jobbliggaren-release-record.sh"
for f in "$SUT" "$RECORD_TOOL_SRC" "$script_dir/jobbliggaren-runtime-ids.sh"; do
  [ -f "$f" ] || {
    echo "missing script under test: $f" >&2
    exit 1
  }
done

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT
readonly BIN="$TMPROOT/bin" REG="$TMPROOT/registry" TOOLS="$TMPROOT/tools" CHECKOUT="$TMPROOT/checkout"
readonly SECRETS="$TMPROOT/secrets" VAR="$TMPROOT/var" ETC="$TMPROOT/etc"
mkdir -p "$BIN" "$REG" "$TOOLS" "$CHECKOUT/deploy/redis" "$VAR" "$ETC"

pass=0
fail=0
# Two platform-dependent skips (#1295): a mode case needs a filesystem honouring chmod, a group case an
# account in more than one group. CI turns each into an error via its own flag.
skipped=0

# --- the code under test, pointed at the fixtures ------------------------------------------------------
# THE ABSENCE PROOFS BELOW ARE FAIL-OPEN WITHOUT THESE: "no absolute docker path in the copy" is just as
# true of a source that never had one. Presence first, absence after.
for f in "$SUT" "$RECORD_TOOL_SRC" "$script_dir/jobbliggaren-runtime-ids.sh"; do
  grep -qF -- "/usr/bin/docker" "$f" || {
    echo "FIXTURE BROKEN: $f does not call docker by absolute path — the redirect proofs are vacuous" >&2
    exit 1
  }
done
readonly FIXTURE_SUT="$TOOLS/reconcile.sh" FIXTURE_IDS="$TOOLS/runtime-ids.sh"
readonly RECEIPT="$VAR/applied-release.env" STAMP="$VAR/stamp" PIN="$ETC/release-pin" ENVF="$CHECKOUT/deploy/.env"
sed -e "s#^readonly CHECKOUT=.*#readonly CHECKOUT=$CHECKOUT#" \
  -e "s#^readonly COMPOSE_FILE=.*#readonly COMPOSE_FILE=$CHECKOUT/deploy/docker-compose.yml#" \
  -e "s#^readonly ENV_FILE=.*#readonly ENV_FILE=$ENVF#" \
  -e "s#^readonly VERIFIER=.*#readonly VERIFIER=$TOOLS/verify-image-attestation.sh#" \
  -e "s#^readonly RECORD_TOOL=.*#readonly RECORD_TOOL=$TOOLS/jobbliggaren-release-record.sh#" \
  -e "s#^readonly LOCK=.*#readonly LOCK=$TMPROOT/lock#" \
  -e "s#^readonly STAMP=.*#readonly STAMP=$STAMP#" \
  -e "s#^readonly RECEIPT=.*#readonly RECEIPT=$RECEIPT#" \
  -e "s#^readonly PIN_FILE=.*#readonly PIN_FILE=$PIN#" \
  -e "s#^readonly SECRETS_DIR=.*#readonly SECRETS_DIR=$SECRETS#" \
  -e "s#^readonly RUNTIME_IDS=.*#readonly RUNTIME_IDS=$FIXTURE_IDS#" \
  -e "s#/usr/bin/docker#docker#g" \
  "$SUT" >"$FIXTURE_SUT"
for want in "readonly SECRETS_DIR=$SECRETS" "readonly RUNTIME_IDS=$FIXTURE_IDS" "readonly RECEIPT=$RECEIPT" \
  "readonly PIN_FILE=$PIN" "readonly CHECKOUT=$CHECKOUT" "readonly RECORD_TOOL=$TOOLS/jobbliggaren-release-record.sh"; do
  grep -qxF "$want" "$FIXTURE_SUT" || {
    echo "FIXTURE BROKEN: redirect did not apply: $want — the suite would touch the host's real paths" >&2
    exit 1
  }
done
sed -e "s#/usr/bin/docker#docker#g" "$RECORD_TOOL_SRC" >"$TOOLS/jobbliggaren-release-record.sh"
sed -e "s#/usr/bin/docker#docker#g" "$script_dir/jobbliggaren-runtime-ids.sh" >"$FIXTURE_IDS"
for f in "$FIXTURE_SUT" "$TOOLS/jobbliggaren-release-record.sh" "$FIXTURE_IDS"; do
  if grep -qF -- "/usr/bin/docker" "$f"; then
    echo "FIXTURE BROKEN: the docker redirect did not apply in $f" >&2
    exit 1
  fi
done
chmod +x "$FIXTURE_SUT" "$TOOLS/jobbliggaren-release-record.sh" "$FIXTURE_IDS"

# THE RIG MUST MEASURE WHAT IT CLAIMS: the ownership cases compare a directory's numeric owner against
# ids the stub returns, so stat and id must agree about a directory this process made.
probe_dir=$(mktemp -d "$TMPROOT/idprobe.XXX")
if [ "$(stat -c '%u %g' "$probe_dir")" != "$(id -u) $(id -g)" ]; then
  echo "FIXTURE BROKEN: stat and id disagree about who owns $probe_dir" >&2
  exit 1
fi
rm -rf "$probe_dir"

# --- the verifier stub --------------------------------------------------------------------------------
mkdir -p "$REG/attested"
cat >"$TOOLS/verify-image-attestation.sh" <<EOF
#!/usr/bin/env bash
printf '%s\n' "\$*" >>"$REG/verifier-calls"
[ -f "$REG/verifier-cannot" ] && exit 2
d="\${1#*@}"
[ -f "$REG/attested/\$d" ] || exit 1
[ "\$#" -lt 2 ] || [ "\$(cat "$REG/attested/\$d")" = "\$2" ] || exit 1
exit 0
EOF
chmod +x "$TOOLS/verify-image-attestation.sh"

# --- the docker stub: a registry, a local store, compose and containers, all files -------------------------
cat >"$BIN/docker" <<'EOF'
#!/usr/bin/env bash
REG="__REG__"
key() { printf '%s' "$1" | tr '/:@' '___'; }
idof() { printf 'sha256:%s' "$(printf 'image-of-%s' "$1" | sha256sum | cut -d' ' -f1)"; }
printf '%s\n' "$*" >>"$REG/docker-calls"
mkdir -p "$REG/local" "$REG/tags" "$REG/blobs" "$REG/known" "$REG/running" "$REG/containers"
if [ "$1" = compose ]; then
  shift; [ "$1" = -f ] && shift 2
  profile=""; [ "$1" = --profile ] && { profile="$2"; shift 2; }
  case "$1" in
  config)
    [ "$2" = --images ] || { echo "stub: unexpected compose config $*" >&2; exit 99; }
    # What compose v5.5.1 printed for an unterminated quote in .env (security-auditor, 2026-10-04).
    if [ -f "$REG/compose-config-leaks" ]; then
      echo 'unterminated quoted value "SECRET-FROM-DOT-ENV' >&2
      [ "$(cat "$REG/compose-config-leaks")" = fail ] && exit 1
    fi
    if [ -n "${3:-}" ]; then
      awk -v s="$3" '$1 == s { print $2 }' "$REG/compose-images"
    else
      awk '$1 != "migrate-rewrap" || p { print $2 }' p="$profile" "$REG/compose-images" | sort -u
    fi
    exit 0 ;;
  up)
    printf '%s\n' "$*" >"$REG/up-args"
    [ -f "$REG/up-fails" ] && exit 1
    while read -r svc img; do
      [ "$svc" = migrate-rewrap ] && continue
      [ -f "$REG/up-skips-$svc" ] && continue
      id=$(cat "$REG/local/$(key "$img")" 2>/dev/null) || { echo "No such image: $img" >&2; exit 1; }
      printf '%s' "$id" >"$REG/running/$svc"
    done <"$REG/compose-images"
    exit 0 ;;
  esac
  echo "stub: unexpected compose $*" >&2; exit 99
fi
case "$1" in
ps)
  project="" svc="" oneoff=""
  while [ "$#" -gt 0 ]; do
    case "$1" in
    --filter)
      case "$2" in
      label=com.docker.compose.project=*) project="${2#*project=}" ;;
      label=com.docker.compose.service=*) svc="${2#*service=}" ;;
      label=com.docker.compose.oneoff=*) oneoff="${2#*oneoff=}" ;;
      esac
      shift 2 ;;
    *) shift ;;
    esac
  done
  [ "$project" = jobbliggaren-prod ] || exit 0
  [ -f "$REG/running/$svc" ] && echo "cid-$svc"
  [ -f "$REG/oneoff-$svc" ] && [ "$oneoff" != False ] && echo "cid-$svc-run-1"
  exit 0 ;;
pull)
  ref="${@: -1}"
  [ -f "$REG/pull-fails" ] && { echo "denied" >&2; exit 1; }
  case "$ref" in
  *@sha256:*) d="${ref#*@}"; [ -f "$REG/known/$d" ] || exit 1 ;;
  postgres:* | redis:* | datalust/*) printf '%s' "$(idof "$ref")" >"$REG/local/$(key "$ref")"; exit 0 ;;
  *) d=$(cat "$REG/tags/$(key "$ref")" 2>/dev/null) || { echo "not found" >&2; exit 1; }
     [ -f "$REG/flip-after-pull/$(key "$ref")" ] && cp "$REG/flip-after-pull/$(key "$ref")" "$REG/tags/$(key "$ref")"
     printf '%s' "$(idof "$d")" >"$REG/local/$(key "$ref")" ;;
  esac
  printf '%s' "$(idof "$d")" >"$REG/local/$(key "${ref%%[:@]sha256*}@$d")"
  printf '%s' "$(idof "$d")" >"$REG/local/$(key "${ref%@*}@$d")"
  exit 0 ;;
image)
  [ "$2" = inspect ] || exit 99
  fmt="$4"; ref="$5"
  id=$(cat "$REG/local/$(key "$ref")" 2>/dev/null) || exit 1
  case "$fmt" in
  *RepoDigests*)
    repo="${ref%%@*}"; repo="${repo%:*}"
    # A docker that reports another repo digest for the release image than the one pulled.
    if [ -f "$REG/repodigest-as" ] && [[ $repo == *-release ]]; then printf '%s@%s\n' "$repo" "$(cat "$REG/repodigest-as")"; exit 0; fi
    for f in "$REG"/local/*; do
      n=$(basename "$f")
      [ "$(cat "$f")" = "$id" ] || continue
      case "$n" in *_sha256_*) printf '%s@sha256:%s\n' "$repo" "${n##*_sha256_}" ;; esac
    done | sort -u
    [ -f "$REG/extra-repodigest" ] && printf '%s@sha256:%s\n' "$repo" "$(printf 'e%.0s' $(seq 64))"
    ;;
  *.Id*) printf '%s\n' "$id" ;;
  esac
  exit 0 ;;
tag)
  n=$(( $(cat "$REG/tag-count" 2>/dev/null || echo 0) + 1 )); echo "$n" >"$REG/tag-count"
  # Fails exactly the N-th tag of the run, so the restore's own tags after it succeed.
  [ -f "$REG/tag-fails-at" ] && [ "$n" -eq "$(cat "$REG/tag-fails-at")" ] && exit 1
  src="$2" dst="$3"
  case "$src" in
  sha256:*) id="$src" ;;
  *) id=$(cat "$REG/local/$(key "$src")" 2>/dev/null) || exit 1 ;;
  esac
  printf '%s' "$id" >"$REG/local/$(key "$dst")"; exit 0 ;;
rmi)
  [ -f "$REG/rmi-fails" ] && exit 1
  rm -f "$REG/local/$(key "${@: -1}")"; exit 0 ;;
create)
  shift; name="" ref=""
  while [ "$#" -gt 0 ]; do case "$1" in --name) name="$2"; shift 2 ;; --pull|--network) shift 2 ;; *) [ -z "$ref" ] && ref="$1"; shift ;; esac; done
  touch "$REG/created"
  d="${ref#*@}"; [ -f "$REG/blobs/$d" ] || exit 1
  printf '%s' "$d" >"$REG/containers/$name"; exit 0 ;;
cp)
  name="${2%%:*}"; d=$(cat "$REG/containers/$name") || exit 1
  t=$(mktemp -d); cp "$REG/blobs/$d" "$t/release.env"; (cd "$t" && tar -cf - release.env); rm -rf "$t"; exit 0 ;;
rm) exit 0 ;;
inspect)
  cid="${@: -1}"; cat "$REG/running/${cid#cid-}"; echo; exit 0 ;;
run)
  echo "$*" >"$REG/idmeasured"
  cat "$REG/ids-out"; exit "$(cat "$REG/ids-exit")" ;;
esac
echo "unexpected docker invocation: $*" >&2
exit 99
EOF
sed -i "s#__REG__#$REG#" "$BIN/docker"
chmod +x "$BIN/docker"

# `flock` is absent on some developer hosts (Git Bash); the acquire path is stubbed there, and the two
# cases ABOUT the lock skip themselves rather than assert against a stub.
HAVE_FLOCK=$(command -v flock >/dev/null 2>&1 && echo 1 || echo 0)
readonly HAVE_FLOCK
if [ "$HAVE_FLOCK" -eq 0 ]; then
  printf '#!/usr/bin/env bash\nexit 0\n' >"$BIN/flock"
  chmod +x "$BIN/flock"
fi

# --- fixtures ------------------------------------------------------------------------------------------
readonly PREFIX="ghcr.io/klasolsson81/jobbliggaren" RELEASE="ghcr.io/klasolsson81/jobbliggaren-release"
readonly NAMES=(api worker migrate web caddy)
key() { printf '%s' "$1" | tr '/:@' '___'; }
digest_of() { printf 'sha256:%s' "$(printf '%s' "$1" | sha256sum | cut -d' ' -f1)"; }
idof() { printf 'sha256:%s' "$(printf 'image-of-%s' "$1" | sha256sum | cut -d' ' -f1)"; }
sha_of() { printf '%s' "$1" | sha1sum | cut -d' ' -f1; }
readonly SHA1=$(sha_of one) SHA2=$(sha_of two) SHA3=$(sha_of three)

printf 'services: {}\n' >"$CHECKOUT/deploy/docker-compose.yml"
printf '#!/bin/sh\n' >"$CHECKOUT/deploy/redis/healthcheck.sh"
DEPLOY=$(PATH="$BIN:/usr/bin:/bin" bash "$TOOLS/jobbliggaren-release-record.sh" deploy-hash "$CHECKOUT")
readonly DEPLOY

default_compose() {
  cat >"$REG/compose-images" <<EOF
caddy $PREFIX-caddy:applied
web $PREFIX-web:applied
api $PREFIX-api:applied
worker $PREFIX-worker:applied
migrate $PREFIX-migrate:applied
migrate-rewrap $PREFIX-migrate:applied
postgres postgres:18.3
redis redis:8.6-alpine
seq datalust/seq:2026.1
EOF
}

# A published release of commit <sha> at <sequence>: five attested images, and a record (attested) in the
# registry under `sha-<sha>`. Optional <app-ids> overrides the AppDbContext migration list. Prints the
# record digest.
release() {
  local sha="$1" seq="$2" app="${3:-20260101000000_A,20260201000000_B}" name d rec
  for name in "${NAMES[@]}"; do
    d=$(digest_of "$name-$sha")
    touch "$REG/known/$d"
    printf '%s' "$sha" >"$REG/attested/$d"
  done
  {
    printf 'JBL_RELEASE_FORMAT=1\nJBL_RELEASE_REPOSITORY=klasolsson81/jobbliggaren\nJBL_RELEASE_SOURCE_REF=refs/heads/main\n'
    printf 'JBL_RELEASE_SOURCE_SHA=%s\nJBL_RELEASE_SEQUENCE=%s\n' "$sha" "$seq"
    for name in API WORKER MIGRATE WEB CADDY; do printf 'JBL_RELEASE_IMAGE_%s=%s\n' "$name" "$(digest_of "${name,,}-$sha")"; done
    printf 'JBL_RELEASE_DEPLOY_SHA256=%s\n' "$DEPLOY"
    printf 'JBL_RELEASE_MIGRATIONS_APP=%s\nJBL_RELEASE_MIGRATIONS_IDENTITY=20260101000000_I\n' "$app"
  } >"$REG/rec.tmp"
  rec="sha256:$(sha256sum <"$REG/rec.tmp" | cut -d' ' -f1)"
  mv "$REG/rec.tmp" "$REG/blobs/$rec"
  touch "$REG/known/$rec"
  printf '%s' "$sha" >"$REG/attested/$rec"
  printf '%s' "$rec" >"$REG/tags/$(key "$RELEASE:sha-$sha")"
  printf '%s' "$rec"
}
channel() { printf '%s' "$1" >"$REG/tags/$(key "$RELEASE:dev")"; }
applied_is() { # <sha>: every :applied tag names that release's image
  local name
  for name in "${NAMES[@]}"; do
    [ "$(cat "$REG/local/$(key "$PREFIX-$name:applied")" 2>/dev/null)" = "$(idof "$(digest_of "$name-$1")")" ] || return 1
  done
}

reset() {
  rm -rf "$REG/local" "$REG/tags" "$REG/blobs" "$REG/known" "$REG/running" "$REG/containers" "$REG/attested" "$REG/flip-after-pull"
  mkdir -p "$REG/local" "$REG/tags" "$REG/blobs" "$REG/known" "$REG/running" "$REG/containers" "$REG/attested" "$REG/flip-after-pull"
  rm -f "$REG"/up-args "$REG"/up-fails "$REG"/up-skips-* "$REG"/oneoff-* "$REG"/compose-config-leaks "$REG"/pull-fails "$REG"/extra-repodigest "$REG"/repodigest-as "$REG"/tag-count \
    "$REG"/tag-fails-at "$REG"/rmi-fails "$REG"/created "$REG"/idmeasured "$REG"/verifier-cannot \
    "$REG"/docker-calls "$REG"/verifier-calls "$RECEIPT" "$STAMP" "$PIN"
  default_compose
  printf 'POSTGRES_APP_PASSWORD=x\n#IMAGE_TAG=sha-0000000\n' >"$ENVF"
  rm -rf "$SECRETS"
  printf '%s\n%s\n' "$(id -u)" "$(id -g)" >"$REG/ids-out"
  printf '0' >"$REG/ids-exit"
}

run_sut() {
  PATH="$BIN:/usr/bin:/bin" bash "$FIXTURE_SUT" "$@" >"$TMPROOT/out" 2>&1
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
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}
check() {
  if eval "$1"; then
    pass=$((pass + 1))
    echo "  ok   $2"
  else
    fail=$((fail + 1))
    echo "  FAIL $2" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}
said() { grep -qF -- "$1" "$TMPROOT/out"; }
applied() { [ -f "$REG/up-args" ]; }
no_receipt() { [ ! -e "$RECEIPT" ] && [ ! -e "$STAMP" ]; }
nothing_tagged() { ! grep -q "^tag " "$REG/docker-calls" 2>/dev/null; }

echo "jobbliggaren-reconcile.sh"

echo "-- the happy path: one record, followed from the channel"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
expect_exit 0 "a proven release is applied"
check 'applied' "and 'up -d' ran"
check 'grep -qF -- "--pull never" "$REG/up-args"' "with --pull never: up does not consult the registry again"
check 'applied_is "$SHA1"' "every :applied tag names the record's image"
check '[ -f "$RECEIPT" ] && [ -f "$STAMP" ]' "a receipt and the success stamp are written"
check 'PATH="$BIN:/usr/bin:/bin" bash "$TOOLS/jobbliggaren-release-record.sh" receipt "$RECEIPT" >/dev/null && grep -qx "JBL_RECEIPT_RECORD_DIGEST=$REC" "$RECEIPT"' \
  "and the receipt validates and names the record digest"
check '[ "$(sed -n 1p "$REG/verifier-calls")" = "$RELEASE@$REC" ]' "the record's identity was checked first, before its commit was known"
check '[ "$(sed -n 2p "$REG/verifier-calls")" = "$RELEASE@$REC $SHA1" ]' "then the record bound to its commit"
check '[ "$(grep -c " $SHA1\$" "$REG/verifier-calls")" -eq 6 ]' "and all five images, each bound to that commit"
check 'said "verified 5 image(s)"' "the journal names the verified count"
check '[ "$(grep -c "^pull --quiet $RELEASE:dev" "$REG/docker-calls")" -eq 1 ]' "the channel is read exactly once"

reset
REC=$(release "$SHA1" 10)
channel "$REC"
run_sut
: >"$REG/docker-calls"
expect_exit 0 "the same release again is a re-apply, not a refusal"

echo "-- selection"
reset
REC1=$(release "$SHA1" 10)
REC2=$(release "$SHA2" 11)
channel "$REC2"
printf 'sha-%s\n' "$SHA1" >"$PIN"
expect_exit 0 "a sha-<commit> pin applies that release, not the channel's"
check 'applied_is "$SHA1"' "and :applied names the pinned release"

reset
REC1=$(release "$SHA1" 10)
REC2=$(release "$SHA2" 11)
printf '%s' "$REC2" >"$REG/tags/$(key "$RELEASE:sha-$SHA1")"
printf 'sha-%s\n' "$SHA1" >"$PIN"
expect_exit 1 "a pin whose tag was re-pointed at another commit's record refuses — a tag vouches for nothing"
check '! applied && nothing_tagged' "and nothing is tagged or applied"

reset
REC1=$(release "$SHA1" 10)
printf '%s\n' "$REC1" >"$PIN"
expect_exit 0 "a digest pin applies exactly that record"
reset
REC1=$(release "$SHA1" 10)
printf '%s\n' "$REC1" >"$PIN"
# UNREACHABLE STATE, DECLARED: a real `docker pull repo@sha256:X` records X as the repo digest, so no
# actor produces a pinned pull carrying another one. The case asserts only that the read side refuses
# if that invariant ever breaks.
printf '%s' "$(digest_of something-else)" >"$REG/repodigest-as"
expect_exit 1 "a digest pin whose pulled image carries another repo digest refuses"
# Bound to the message — measured by mutation: with the equality check deleted, the later read of the
# other digest still failed, green for the wrong reason.
check 'said "but $PIN pins $REC1"' "and it is the pin's own equality check that answered"

for bad in "" "latest" "sha-$SHA1 sha-$SHA2" "sha-${SHA1:0:7}"; do
  reset
  release "$SHA1" 10 >/dev/null
  channel "$(cat "$REG/tags/$(key "$RELEASE:sha-$SHA1")")"
  printf '%s\n' "$bad" >"$PIN"
  expect_exit 1 "a pin file holding '$bad' refuses and never falls back to the channel"
  check '! applied' "  … nothing applied"
done
reset
release "$SHA1" 10 >/dev/null
printf 'sha-%s\nsha-%s\n' "$SHA1" "$SHA1" >"$PIN"
expect_exit 1 "a pin file with two lines refuses"

reset
REC=$(release "$SHA1" 10)
channel "$REC"
printf 'POSTGRES_APP_PASSWORD=x\nIMAGE_TAG=sha-1234567\n' >"$ENVF"
expect_exit 1 "a set IMAGE_TAG refuses — it would pin nothing, silently"
check '! said "POSTGRES_APP_PASSWORD" && ! said "sha-1234567"' "and no line of .env is ever printed"
check '! grep -q "^pull" "$REG/docker-calls"' "and it refused before any network call"

echo "-- the compose binding (before any network call)"
for broken in "api $PREFIX-api:latest" "migrate-rewrap $PREFIX-api:applied" "api $PREFIX-web:applied"; do
  reset
  release "$SHA1" 10 >/dev/null
  channel "$(cat "$REG/tags/$(key "$RELEASE:sha-$SHA1")")"
  svc=${broken%% *}
  awk -v s="$svc" -v line="$broken" '$1 == s { print line; next } { print }' "$REG/compose-images" >"$REG/ci" && mv "$REG/ci" "$REG/compose-images"
  expect_exit 1 "compose binding '$broken' refuses"
  check '! grep -q "^pull" "$REG/docker-calls"' "  … before any pull"
done
for extra in "mongo mongo:7" "postgres postgres:19.0"; do
  reset
  release "$SHA1" 10 >/dev/null
  channel "$(cat "$REG/tags/$(key "$RELEASE:sha-$SHA1")")"
  printf '%s\n' "$extra" >>"$REG/compose-images"
  expect_exit 1 "an image that is neither ours nor allow-listed ('${extra#* }') refuses"
done

echo "-- one snapshot, read verify-first"
reset
touch "$REG/pull-fails"
expect_exit 2 "a release that cannot be pulled cannot be answered (a private or unpublished package reads the same)"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
touch "$REG/extra-repodigest"
expect_exit 1 "two repo digests for the release repository refuse — index 0 is not a contract"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
rm -f "$REG/attested/$REC"
expect_exit 1 "a record our workflow did not attest refuses"
check '[ ! -f "$REG/created" ]' "and not one byte of it was copied out — create never ran"
check 'nothing_tagged && ! applied' "and nothing was tagged or applied"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
printf '%s' "$SHA2" >"$REG/attested/$REC"
expect_exit 1 "a record attested as another commit's build refuses"
reset
REC1=$(release "$SHA1" 10)
REC2=$(release "$SHA2" 11)
channel "$REC1"
printf '%s' "$REC2" >"$REG/flip-after-pull/$(key "$RELEASE:dev")"
expect_exit 0 "dev moving right after the snapshot changes nothing in this run"
check 'grep -qx "JBL_RECEIPT_RECORD_DIGEST=$REC1" "$RECEIPT" && applied_is "$SHA1"' "it applies — and records — the release it snapshotted"

echo "-- what compose says about .env stays out of the journal (security-auditor N5)"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
printf 'warn' >"$REG/compose-config-leaks"
expect_exit 0 "compose writing a value from .env to stderr on every config call still applies"
check '! said "SECRET-FROM-DOT-ENV"' "and nothing compose wrote reaches the output"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
printf 'fail' >"$REG/compose-config-leaks"
expect_exit 2 "compose failing on a malformed .env cannot be answered"
check '! said "SECRET-FROM-DOT-ENV" && said "could not resolve service"' "with the wrapper's own line, not compose's"

echo "-- containers are the service's, never a one-off run's (dotnet-architect P4)"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
touch "$REG/oneoff-migrate"
expect_exit 0 "a left-over one-off migrate container does not fail the postcondition"
check '[ -f "$RECEIPT" ]' "and the receipt is written"
project_in_compose=$(sed -n 's/^name: //p' "$script_dir/../docker-compose.yml" | tr -d '\r')
project_in_wrapper=$(sed -n 's/^readonly COMPOSE_PROJECT="\(.*\)"$/\1/p' "$SUT")
check '[ -n "$project_in_compose" ] && [ "$project_in_compose" = "$project_in_wrapper" ]' \
  "the wrapper's project constant is the real compose file's name: ($project_in_compose / $project_in_wrapper)"

echo "-- the deployment configuration a release was built with"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
printf 'services:\n  api: {}\n' >"$CHECKOUT/deploy/docker-compose.yml"
expect_exit 1 "a checkout whose compose configuration differs refuses"
check 'said "merge --ff-only $SHA1"' "and prints the exact command that advances the checkout to the release"
check 'nothing_tagged && ! applied' "and nothing was tagged or applied"
printf 'services: {}\n# a comment that changes no configuration\n\n' >"$CHECKOUT/deploy/docker-compose.yml"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
expect_exit 0 "a comment-only difference in compose still applies (the canonical form)"
printf 'services: {}\n' >"$CHECKOUT/deploy/docker-compose.yml"

echo "-- following the channel never moves backwards"
reset
OLD=$(release "$SHA1" 10)
NEW=$(release "$SHA2" 11)
channel "$NEW"
run_sut
channel "$OLD"
: >"$REG/docker-calls"
expect_exit 1 "a channel moved back to an older sequence refuses"
check 'nothing_tagged && applied_is "$SHA2"' "and :applied still names the applied release"
reset
NEW=$(release "$SHA2" 11)
channel "$NEW"
run_sut
SAME=$(release "$SHA3" 11)
channel "$SAME"
expect_exit 1 "the same sequence with another commit refuses (a diverged history)"
reset
A=$(release "$SHA1" 10 "20260101000000_A,20260201000000_B")
channel "$A"
run_sut
B=$(release "$SHA2" 11 "20260101000000_A")
channel "$B"
expect_exit 1 "a newer release that lacks an applied migration refuses before the destructive up"
check 'said "20260201000000_B"' "and names the migration"
reset
A=$(release "$SHA2" 11)
channel "$A"
run_sut
B=$(release "$SHA1" 10)
printf 'sha-%s\n' "$SHA1" >"$PIN"
expect_exit 0 "a PIN to an older release is an operator's act: it applies, and migrate's gate decides"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
run_sut
sed -i "s/^JBL_RELEASE_MIGRATIONS_IDENTITY=.*/JBL_RELEASE_MIGRATIONS_IDENTITY=20260101000000_I,20260301000000_J/" "$REG/blobs/$(release "$SHA2" 11)" 2>/dev/null || true
REC2=$(cat "$REG/tags/$(key "$RELEASE:sha-$SHA2")")
NEWBYTES="$REG/blobs/$REC2"
NEWREC="sha256:$(sha256sum <"$NEWBYTES" | cut -d' ' -f1)"
mv "$NEWBYTES" "$REG/blobs/$NEWREC"
touch "$REG/known/$NEWREC"
printf '%s' "$SHA2" >"$REG/attested/$NEWREC"
channel "$NEWREC"
expect_exit 0 "a release adding Identity migrations applies"
check 'said "20260301000000_J"' "and the journal names the Identity migration the unit does not apply"

echo "-- every image, bound to the record's commit — no mixed set"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
printf '%s' "$SHA2" >"$REG/attested/$(digest_of "web-$SHA1")"
expect_exit 1 "an image built from ANOTHER commit refuses the whole release"
check 'nothing_tagged && ! applied && no_receipt' "and nothing is tagged, applied or recorded"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
run_sut
: >"$REG/verifier-calls"
REC2=$(release "$SHA2" 11)
channel "$REC2"
touch "$REG/verifier-cannot"
expect_exit 2 "a verifier that cannot answer keeps its own code (2), never 1, never 0"
check 'applied_is "$SHA1"' "and :applied still names the applied release"

echo "-- :applied, the apply and the receipt"
reset
REC1=$(release "$SHA1" 10)
channel "$REC1"
run_sut
REC2=$(release "$SHA2" 11)
channel "$REC2"
rm -f "$REG/tag-count"
printf '3' >"$REG/tag-fails-at"
expect_exit 1 "a tag failure midway fails the run"
check 'applied_is "$SHA1"' "and :applied is restored to what the run found"
check 'grep -qx "JBL_RECEIPT_RECORD_DIGEST=$REC1" "$RECEIPT"' "and the receipt still names the applied release"
reset
REC1=$(release "$SHA1" 10)
channel "$REC1"
run_sut
REC2=$(release "$SHA2" 11)
channel "$REC2"
touch "$REG/up-fails"
rm -f "$STAMP"
expect_exit 1 "an up that fails fails the run"
check 'applied_is "$SHA1" && [ ! -e "$STAMP" ]' "and :applied is restored, and no stamp is written"
check 'grep -qx "JBL_RECEIPT_RECORD_DIGEST=$REC1" "$RECEIPT"' "and the receipt is not rewritten"
reset
REC1=$(release "$SHA1" 10)
channel "$REC1"
run_sut
REC2=$(release "$SHA2" 11)
channel "$REC2"
touch "$REG/up-fails" "$REG/rmi-fails"
rm -f "$REG/local/$(key "$PREFIX-api:applied")"
expect_exit 2 "a restore that itself fails says :applied may be mixed (2)"
check 'said "may be mixed"' "and says so"
# Killed a surviving mutant: the case below has NO web container at all, so the "no single container"
# check answered and the image comparison was never reached. Here web keeps the previous release.
# THE ACTOR, for this case and the next: compose's own `up` re-creates a service whose image moved, so
# `up-skips-web` stands for a hand-typed, lockless `docker` or `compose` command that moves `:applied`
# or re-creates web between the tag phase and the postcondition — the one the wrapper's header names
# under WHAT MANUAL COMMANDS SEE.
reset
REC1=$(release "$SHA1" 10)
channel "$REC1"
run_sut
REC2=$(release "$SHA2" 11)
channel "$REC2"
touch "$REG/up-skips-web"
expect_exit 1 "a service still running the PREVIOUS release's image after up fails the postcondition"
check 'said "service web runs"' "and it is the image comparison that answered"
check 'grep -qx "JBL_RECEIPT_RECORD_DIGEST=$REC1" "$RECEIPT" && applied_is "$SHA1"' "the receipt still names the applied release, and :applied is put back"

reset
REC=$(release "$SHA1" 10)
channel "$REC"
touch "$REG/up-skips-web"
expect_exit 1 "a service not running the release's image after up fails the postcondition"
check 'no_receipt' "and no receipt or stamp is written for an apply that did not measurably happen"
check '[ -z "$(cat "$REG/local/$(key "$PREFIX-web:applied")" 2>/dev/null)" ]' "and :applied is put back to what the run found (here: none)"

echo "-- --stage: a first boot"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
expect_exit 0 "--stage verifies and tags a release on a box that has applied nothing" --stage
check 'applied_is "$SHA1" && ! applied && no_receipt' "it tags :applied and applies nothing, writes no receipt"
run_sut
expect_exit 1 "--stage on a box with a receipt refuses — it is first-boot only" --stage
reset
REC=$(release "$SHA1" 10)
channel "$REC"
printf '%s' "$SHA2" >"$REG/attested/$(digest_of "api-$SHA1")"
expect_exit 1 "--stage refuses an unproven image like the apply does" --stage
check 'nothing_tagged' "and tags nothing"

echo "-- --status"
reset
expect_exit 1 "--status with nothing applied says NOT APPLIED" --status
REC=$(release "$SHA1" 10)
channel "$REC"
run_sut --stage
expect_exit 1 "--status on a staged box (no receipt) still says NOT APPLIED" --status
check 'grep -qE "^applied:   api [0-9a-f]{12}$" "$TMPROOT/out"' "and prints each :applied image by its short id alone"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
run_sut
expect_exit 0 "--status after an apply is consistent" --status
check 'said "verdict:   consistent"' "and says so"
printf 'services:\n  api: {}\n' >"$CHECKOUT/deploy/docker-compose.yml"
expect_exit 1 "--status sees a checkout advanced past the applied release's configuration" --status
check 'said "config:    the checkout'"'"'s deployment files DIFFER"' "and names it"
printf 'services: {}\r\n' >"$CHECKOUT/deploy/docker-compose.yml"
expect_exit 2 "--status cannot answer when the checkout's deployment files cannot be hashed" --status
printf 'services: {}\n' >"$CHECKOUT/deploy/docker-compose.yml"
cp "$RECEIPT" "$TMPROOT/receipt.saved"
printf 'not a receipt\n' >"$RECEIPT"
expect_exit 2 "--status cannot answer over a receipt that does not validate" --status
cp "$TMPROOT/receipt.saved" "$RECEIPT"
printf 'services: {}\n' >"$CHECKOUT/deploy/docker-compose.yml"
printf '%s' "$(idof other)" >"$REG/running/api"
rm -f "$REG/docker-calls" "$REG/up-args" "$TMPROOT/lock"
expect_exit 1 "--status sees a container running something else" --status
check 'said "running:   api DIFFERS"' "and names it"
check '[ ! -e "$TMPROOT/lock" ] && [ ! -f "$REG/up-args" ] && ! grep -qE "^(pull|tag|rmi|create|run) " "$REG/docker-calls"' \
  "(--status is read-only: it takes no lock, pulls, tags or runs nothing, and applies nothing)"

echo "-- the lock"
reset
REC=$(release "$SHA1" 10)
channel "$REC"
mv "$BIN/flock" "$BIN/flock.hidden" 2>/dev/null || true
if PATH="$BIN:/usr/bin:/bin" command -v flock >/dev/null 2>&1; then
  echo "  skip real flock is on PATH outside \$BIN; cannot hide it for this case"
else
  expect_exit 2 "flock absent → exit 2 (cannot answer), not 0"
  check '! applied' "and nothing is applied"
fi
mv "$BIN/flock.hidden" "$BIN/flock" 2>/dev/null || true
if [ "$HAVE_FLOCK" -eq 0 ]; then
  echo "  skip lock-held behaviour needs a real flock; this host has none (stubbed elsewhere)"
else
  exec 8>"$TMPROOT/lock"
  flock -n 8
  expect_exit 0 "lock held → exit 0 (a benign overlap is not a unit failure)"
  exec 8>&-
  check '! applied && no_receipt' "and a locked-out run applies and records nothing"
fi

echo "-- the secrets ownership gate (#1295)"
seed_secrets() {
  rm -rf "$SECRETS"
  mkdir -p "$SECRETS"
  printf '%s' seeded >"$SECRETS/FieldEncryption__LocalMasterKeyBase64"
  printf '%s' seeded >"$SECRETS/AuditPseudonymization__PepperBase64"
}
stub_ids() { printf '%s\n%s\n' "$1" "$2" >"$REG/ids-out"; printf '%s' "${3:-0}" >"$REG/ids-exit"; }
fresh() { reset; REC=$(release "$SHA1" 10); channel "$REC"; seed_secrets; }

fresh
expect_exit 0 "ids agree → the apply proceeds"
check '[ -f "$REG/idmeasured" ] && grep -qF -- "@sha256:" "$REG/idmeasured"' "the image WAS measured, by DIGEST"
for flag in "--pull never" "--network none" "--cap-drop ALL" "--security-opt no-new-privileges"; do
  check 'grep -qF -- "$flag" "$REG/idmeasured"' "the measurement runs contained ($flag)"
done

fresh
stub_ids "$(id -u)" "$(($(id -g) + 1))"
expect_exit 1 "gid drift → refuses"
check 'said "cannot TRAVERSE" && said "chown root:"' "naming the traversal axis and keeping root as the dir's owner"
check '! said "chown -R" && grep -qE "^[[:space:]]*sudo find [^|]*-mindepth 1 -maxdepth 1 -exec chown " "$TMPROOT/out"' "with a bounded, directory-excluding find — never chown -R"
check '! grep -qE "(chown|chmod|stat)[^|]*/\*" "$TMPROOT/out"' "and never a shell glob the operator's shell cannot expand"
check 'nothing_tagged && ! applied' "and nothing is tagged or applied"

fresh
stub_ids "$(($(id -u) + 1))" "$(id -g)"
expect_exit 1 "uid drift → refuses"
check 'said "cannot READ the injected secrets"' "naming the owner axis"
check '! said "chown -R" && grep -qE "^[[:space:]]*sudo find [^|]*-mindepth 1 -maxdepth 1 -exec chown " "$TMPROOT/out"' "with the same bounded find"

fresh
printf '%s' "$SHA2" >"$REG/attested/$(digest_of "api-$SHA1")"
expect_exit 1 "a refused image still refuses with secrets present"
check '[ ! -f "$REG/idmeasured" ]' "and a REFUSED image is never run to read its ids"

fresh
stub_ids "$(id -u)" "$(id -g)" 97
expect_exit 2 "the measurement failing is 'cannot answer' (2)"
check 'nothing_tagged' "and nothing is tagged"

fresh
rm -rf "$SECRETS"
mkdir -p "$SECRETS/not-a-secret"
expect_exit 0 "a non-regular entry is not an injected secret: the gate is skipped, on the journal"
check 'said "ownership gate skipped" && [ ! -f "$REG/idmeasured" ]' "and no image is run"

fresh
mv "$FIXTURE_IDS" "$FIXTURE_IDS.hidden"
expect_exit 2 "a missing runtime-id helper is 'cannot answer' (2)"
check 'said "runtime-id helper missing"' "and it is the precondition guard that answered"
mv "$FIXTURE_IDS.hidden" "$FIXTURE_IDS"

fresh
chmod 0000 "$SECRETS/FieldEncryption__LocalMasterKeyBase64" 2>/dev/null || true
if [ "$(stat -c '%a' "$SECRETS/FieldEncryption__LocalMasterKeyBase64")" = "0" ]; then
  expect_exit 1 "right owner but mode 0000 → refuses; ownership alone is not readability"
  check 'said "the owner cannot read it" && said "sudo chmod 0400"' "naming the mode and its repair"
else
  skipped=$((skipped + 1))
  echo "  SKIP mode 0000 case: this filesystem does not honour chmod. It RUNS in CI (JBL_REQUIRE_MODE_CASES)."
  [ "${JBL_REQUIRE_MODE_CASES:-0}" != "1" ] || { fail=$((fail + 1)); echo "  FAIL JBL_REQUIRE_MODE_CASES=1 but chmod is not honoured" >&2; }
fi
chmod 0400 "$SECRETS/FieldEncryption__LocalMasterKeyBase64" 2>/dev/null || true

# The fixture's uid equals its gid on both platforms in play, so swapping `%g` for `%u` passes every case
# above. Only a directory whose group is a SECONDARY group of this account tells them apart.
second_gid=""
for g in $(id -G); do [ "$g" != "$(id -g)" ] && { second_gid="$g"; break; }; done
if [ -z "$second_gid" ]; then
  skipped=$((skipped + 1))
  echo "  SKIP no secondary group on this host; the %g-vs-%u swap cannot be told apart here (CI: JBL_REQUIRE_GROUP_CASES)."
  [ "${JBL_REQUIRE_GROUP_CASES:-0}" != "1" ] || { fail=$((fail + 1)); echo "  FAIL JBL_REQUIRE_GROUP_CASES=1 but no secondary group" >&2; }
else
  fresh
  chgrp "$second_gid" "$SECRETS" "$SECRETS"/* 2>/dev/null || true
  if [ "$(stat -c '%g' "$SECRETS")" != "$second_gid" ]; then
    skipped=$((skipped + 1))
    echo "  SKIP chgrp to $second_gid did not take on this filesystem"
    [ "${JBL_REQUIRE_GROUP_CASES:-0}" != "1" ] || { fail=$((fail + 1)); echo "  FAIL JBL_REQUIRE_GROUP_CASES=1 but chgrp did not take" >&2; }
  else
    stub_ids "$(id -u)" "$second_gid"
    expect_exit 0 "dir group != file owner, and the gate reads the RIGHT one of each"
  fi
fi

echo
echo "passed: $pass   failed: $fail   skipped: $skipped"
[ "$fail" -eq 0 ]
