#!/usr/bin/env bash
#
# Fixture tests for jobbliggaren-release-record.sh (#1238).
#
# Run:  bash deploy/systemd/jobbliggaren-release-record.test.sh
#
# NO DAEMON, NO REGISTRY, NO NETWORK. `read` reaches docker and the verifier, both stubbed: the copy
# under test sits in a fixture directory beside a stub verifier, and docker is redirected to a PATH
# stub. `emit` runs against throwaway git repositories, because the sequence it writes is the commit
# count of the source commit's history. Three cases read the REAL tree: every EF migration is in the
# record, the record names exactly the images `release-images.yml` builds, and every file the real
# compose bind-mounts by relative path is one the deployment hash covers.
#
# THREE OUTCOMES, NEVER COLLAPSED: 0 done · 1 refused · 2 could not answer.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/../.." && pwd)
readonly SUT="$script_dir/jobbliggaren-release-record.sh"
[ -f "$SUT" ] || {
  echo "missing script under test: $SUT" >&2
  exit 1
}

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT
readonly BIN="$TMPROOT/bin" FIXDIR="$TMPROOT/sut"
mkdir -p "$BIN" "$FIXDIR"
# git is not under /usr/bin on every host (Git for Windows keeps it in /mingw64/bin), and emit needs it.
SUT_PATH="$BIN:$(dirname "$(command -v git)"):/usr/bin:/bin"
readonly SUT_PATH

# Presence in the source first, so the absence proof after the redirect means something.
grep -qF -- "/usr/bin/docker" "$SUT" || {
  echo "FIXTURE BROKEN: $SUT does not call docker by absolute path — the redirect is vacuous" >&2
  exit 1
}
readonly FIXTURE_SUT="$FIXDIR/jobbliggaren-release-record.sh"
sed -e "s#/usr/bin/docker#docker#g" "$SUT" >"$FIXTURE_SUT"
if grep -qF -- "/usr/bin/docker" "$FIXTURE_SUT"; then
  echo "FIXTURE BROKEN: the docker redirect did not apply" >&2
  exit 1
fi

pass=0
fail=0

run_sut() {
  PATH="$SUT_PATH" bash "$FIXTURE_SUT" "$@" >"$TMPROOT/out" 2>"$TMPROOT/err"
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

d() { printf 'sha256:%s' "$(printf "$1%.0s" $(seq 64))"; }
D_API=$(d 1)
D_WORKER=$(d 2)
D_MIGRATE=$(d 3)
D_WEB=$(d 4)
D_CADDY=$(d 5)
readonly D_API D_WORKER D_MIGRATE D_WEB D_CADDY

# --- a source repository in the shape the publisher checks out ------------------------------------
readonly REPO="$TMPROOT/repo"
git_q() { git -C "$REPO" -c user.name=fixture -c user.email=fixture@example.invalid "$@" >/dev/null; }
mig() {
  printf 'namespace X\n{\n    [DbContext(typeof(%s))]\n    [Migration("%s")]\n    partial class M\n    {\n    }\n}\n' "$2" "$3" >"$1"
}
make_repo() {
  rm -rf "$REPO"
  mkdir -p "$REPO/src/Infra/Persistence/Migrations" "$REPO/src/Infra/Identity/Migrations" "$REPO/deploy/redis"
  git init -q "$REPO"
  git -C "$REPO" config core.autocrlf false
  printf 'services: {}\n' >"$REPO/deploy/docker-compose.yml"
  printf '#!/bin/sh\nexit 0\n' >"$REPO/deploy/redis/healthcheck.sh"
  for file in persistent.acl.template volatile.acl.template operator-persistent.acl.template operator-volatile.acl.template \
    predecessor-1976/persistent.acl.template predecessor-1976/volatile.acl.template \
    predecessor-1976/operator-persistent.acl.template predecessor-1976/operator-volatile.acl.template; do
    mkdir -p "$(dirname "$REPO/deploy/redis/$file")"
    cp "$repo_root/deploy/redis/$file" "$REPO/deploy/redis/$file"
  done
  mkdir -p "$REPO/deploy/systemd"
  for file in jobbliggaren-reconcile.sh jobbliggaren-redis-secrets.sh jobbliggaren-redis-policy.py; do
    cp "$script_dir/$file" "$REPO/deploy/systemd/$file"
  done
  mig "$REPO/src/Infra/Persistence/Migrations/20260419145850_InitialCreate.Designer.cs" AppDbContext 20260419145850_InitialCreate
  mig "$REPO/src/Infra/Persistence/Migrations/20260927121429_AddXmin.Designer.cs" AppDbContext 20260927121429_AddXmin
  mig "$REPO/src/Infra/Identity/Migrations/20260506091354_InitialIdentity.Designer.cs" AppIdentityDbContext 20260506091354_InitialIdentity
  # A model snapshot carries [DbContext] and no [Migration]; it is not a migration and must not count.
  printf '    [DbContext(typeof(AppDbContext))]\n    partial class AppDbContextModelSnapshot\n' \
    >"$REPO/src/Infra/Persistence/Migrations/AppDbContextModelSnapshot.cs"
  git_q add -A
  git_q commit -q -m one
  printf 'two\n' >"$REPO/README"
  git_q add -A
  git_q commit -q -m two
}
head_sha() { git -C "$REPO" rev-parse HEAD; }
commit_all() {
  git_q add -A
  git_q commit -q -m "$1"
}
emit_at() {
  run_sut emit --source-sha "$1" --api "$D_API" --worker "$D_WORKER" --migrate "$D_MIGRATE" \
    --web "$D_WEB" --caddy "$D_CADDY" --source-root "$REPO"
}
# The refusal cases go through expect_exit WITH the real subcommand. Passing a helper's name instead
# would reach the SUT as an unknown subcommand, exit 2 for that reason, and pass every "stops the emit"
# case without the emit ever running — measured while writing this suite.
# Each is also BOUND TO ITS MESSAGE: several distinct checks share an exit code, so the code alone
# cannot say which one answered.
expect_emit() {
  local message="$4"
  expect_exit "$1" "$2" emit --source-sha "$3" --api "$D_API" --worker "$D_WORKER" --migrate "$D_MIGRATE" \
    --web "$D_WEB" --caddy "$D_CADDY" --source-root "$REPO"
  check 'grep -qF -- "$message" "$TMPROOT/err"' "  … and it is the check that says: $message"
}

echo "jobbliggaren-release-record.sh"

echo "-- emit"
make_repo
SHA=$(head_sha)
got=0
emit_at "$SHA" || got=$?
check '[ "$got" -eq 0 ]' "a clean, full checkout of the source commit emits a record (exit $got)"
cp "$TMPROOT/out" "$TMPROOT/good.env"
deploy=$(PATH="$SUT_PATH" bash "$FIXTURE_SUT" deploy-hash "$REPO")
cat >"$TMPROOT/expected.env" <<EOF
JBL_RELEASE_FORMAT=1
JBL_RELEASE_REPOSITORY=klasolsson81/jobbliggaren
JBL_RELEASE_SOURCE_REF=refs/heads/main
JBL_RELEASE_SOURCE_SHA=$SHA
JBL_RELEASE_SEQUENCE=2
JBL_RELEASE_IMAGE_API=$D_API
JBL_RELEASE_IMAGE_WORKER=$D_WORKER
JBL_RELEASE_IMAGE_MIGRATE=$D_MIGRATE
JBL_RELEASE_IMAGE_WEB=$D_WEB
JBL_RELEASE_IMAGE_CADDY=$D_CADDY
JBL_RELEASE_DEPLOY_SHA256=$deploy
JBL_RELEASE_MIGRATIONS_APP=20260419145850_InitialCreate,20260927121429_AddXmin
JBL_RELEASE_MIGRATIONS_IDENTITY=20260506091354_InitialIdentity
EOF
check 'cmp -s "$TMPROOT/good.env" "$TMPROOT/expected.env"' "the record is exactly the expected bytes, in the fixed order, sequence = commit count"

emit_at "$SHA"
check 'cmp -s "$TMPROOT/out" "$TMPROOT/good.env"' "emitting twice gives the same bytes — the record is a pure function of its inputs"

mig "$REPO/src/Infra/Persistence/Migrations/20260501000000_Middle.Designer.cs" AppDbContext 20260501000000_Middle
commit_all three
emit_at "$(head_sha)"
check 'grep -qx "JBL_RELEASE_MIGRATIONS_APP=20260419145850_InitialCreate,20260501000000_Middle,20260927121429_AddXmin" "$TMPROOT/out"' \
  "migration ids are in ordinal (EF apply) order whatever the file order"
check 'grep -qx "JBL_RELEASE_SEQUENCE=3" "$TMPROOT/out"' "a later commit on the same history has a higher sequence"

expect_emit 1 "a source sha that is not the checked-out commit is refused — the tree read must be the commit named" "$SHA" "not the source commit"

printf 'edited\n' >>"$REPO/deploy/docker-compose.yml"
expect_emit 1 "uncommitted changes in the tree are refused" "$(head_sha)" "uncommitted changes"
git -C "$REPO" checkout -q -- deploy/docker-compose.yml

git clone -q --depth 1 "file://$REPO" "$TMPROOT/shallow" 2>/dev/null
expect_exit 2 "a SHALLOW clone cannot answer — every sequence would read 1" \
  emit --source-sha "$(git -C "$TMPROOT/shallow" rev-parse HEAD)" --api "$D_API" --worker "$D_WORKER" --migrate "$D_MIGRATE" --web "$D_WEB" --caddy "$D_CADDY" --source-root "$TMPROOT/shallow"
check 'grep -qF "shallow clone" "$TMPROOT/err"' "  … and it is the shallow-clone check that answered"

make_repo
mig "$REPO/src/Infra/Other.Designer.cs" ReportingDbContext 20260601000000_Report
commit_all third-context
expect_emit 2 "a THIRD DbContext stops the emit — the format would silently drop its migrations" "$(head_sha)" "ReportingDbContext"

make_repo
printf '    [DbContext(typeof(AppDbContext))]\n    [Migration( "20260601000000_Spaced" )]\n' >"$REPO/src/Infra/Persistence/Migrations/Spaced.cs"
commit_all spaced
expect_emit 2 "an attribute in a spelling the reader does not model stops the emit, never skips it" "$(head_sha)" "unrecognised [Migration"

make_repo
printf '    [Migration("20260601000000_NoContext")]\n' >"$REPO/src/Infra/Persistence/Migrations/NoContext.cs"
commit_all no-context
expect_emit 2 "a migration with no DbContext beside it stops the emit" "$(head_sha)" "expected one of each"

make_repo
git_q rm -q -r src/Infra/Identity/Migrations
commit_all no-identity
expect_emit 2 "a context with NO migrations stops the emit rather than writing an empty list" "$(head_sha)" "no IDENTITY migrations"

make_repo
mkdir -p "$REPO/src/Infra/bin/Debug"
mig "$REPO/src/Infra/bin/Debug/20990101000000_BuildOutput.Designer.cs" AppDbContext 20990101000000_BuildOutput
printf 'bin/\n' >"$REPO/.gitignore"
commit_all ignore-bin
emit_at "$(head_sha)"
check '! grep -q BuildOutput "$TMPROOT/out"' "build output under bin/ is not read as source"

make_repo
rm -f "$REPO/deploy/redis/healthcheck.sh"
commit_all no-healthcheck
expect_emit 2 "a bound deployment file missing from the tree cannot be hashed" "$(head_sha)" "deployment file missing"

make_repo
SHA=$(head_sha)
expect_exit 1 "an uppercase source SHA is refused" emit --source-sha "${SHA^^}" --api "$D_API" --worker "$D_WORKER" --migrate "$D_MIGRATE" --web "$D_WEB" --caddy "$D_CADDY" --source-root "$REPO"
expect_exit 1 "a digest without its algorithm is refused" emit --source-sha "$SHA" --api "${D_API#sha256:}" --worker "$D_WORKER" --migrate "$D_MIGRATE" --web "$D_WEB" --caddy "$D_CADDY" --source-root "$REPO"
expect_exit 1 "a missing image digest is refused" emit --source-sha "$SHA" --api "$D_API" --worker "$D_WORKER" --migrate "$D_MIGRATE" --web "$D_WEB" --source-root "$REPO"
expect_exit 2 "an unknown option is a usage error" emit --source-sha "$SHA" --registry evil.example
expect_exit 2 "an unknown subcommand is a usage error" publish

echo "-- deploy-hash"
make_repo
for file in deploy/redis/persistent.acl.template deploy/redis/volatile.acl.template \
  deploy/redis/operator-persistent.acl.template deploy/redis/operator-volatile.acl.template \
  deploy/redis/predecessor-1976/persistent.acl.template deploy/redis/predecessor-1976/volatile.acl.template \
  deploy/redis/predecessor-1976/operator-persistent.acl.template deploy/redis/predecessor-1976/operator-volatile.acl.template \
  deploy/systemd/jobbliggaren-reconcile.sh deploy/systemd/jobbliggaren-redis-secrets.sh deploy/systemd/jobbliggaren-redis-policy.py; do
  before=$(PATH="$SUT_PATH" bash "$FIXTURE_SUT" deploy-hash "$REPO")
  printf '\n# fixture policy/procedure edit\n' >>"$REPO/$file"
  after=$(PATH="$SUT_PATH" bash "$FIXTURE_SUT" deploy-hash "$REPO")
  check '[ "$before" != "$after" ]' "release hash binds $file"
  git -C "$REPO" checkout -q -- "$file"
done
dh() { PATH="$SUT_PATH" bash "$FIXTURE_SUT" deploy-hash "$REPO"; }
make_repo
printf 'services:\n  api:\n    image: x:applied\n' >"$REPO/deploy/docker-compose.yml"
h1=$(dh)
printf '# a comment line\n\n    # an indented comment line\n' >>"$REPO/deploy/docker-compose.yml"
h2=$(dh)
check '[ "$h1" = "$h2" ]' "full-line comments and blank lines in compose do not change the hash"
printf '    pull_policy: never\n' >>"$REPO/deploy/docker-compose.yml"
h3=$(dh)
check '[ "$h2" != "$h3" ]' "a configuration line in compose does"
sed -i 's/image: x:applied/image: x:applied  # a trailing comment/' "$REPO/deploy/docker-compose.yml"
h4=$(dh)
check '[ "$h3" != "$h4" ]' "a trailing comment does too — telling it from a # inside a value needs a YAML parser"
printf '# a comment\n' >>"$REPO/deploy/redis/healthcheck.sh"
h5=$(dh)
check '[ "$h4" != "$h5" ]' "the bind-mounted script is hashed as raw bytes: even a comment there changes the hash"
printf 'unrelated\n' >"$REPO/deploy/notes.txt"
h6=$(dh)
check '[ "$h5" = "$h6" ]' "a file outside the bound set does not change it"
sed 's/$/\r/' "$REPO/deploy/docker-compose.yml" >"$REPO/c" && mv "$REPO/c" "$REPO/deploy/docker-compose.yml"
expect_exit 1 "a compose file with carriage returns is refused, not hashed — the file is pinned LF" deploy-hash "$REPO"
make_repo
sed 's/$/\r/' "$REPO/deploy/redis/healthcheck.sh" >"$REPO/c" && mv "$REPO/c" "$REPO/deploy/redis/healthcheck.sh"
expect_exit 1 "so is a bind-mounted file with carriage returns" deploy-hash "$REPO"

echo "-- receipt"
make_repo
SHA=$(head_sha)
emit_at "$SHA"
cp "$TMPROOT/out" "$TMPROOT/rec.env"
{
  printf 'JBL_RECEIPT_RECORD_DIGEST=%s\n' "$D_CADDY"
  cat "$TMPROOT/rec.env"
} >"$TMPROOT/receipt"
expect_exit 0 "a receipt is one digest line and a valid record" receipt "$TMPROOT/receipt"
check 'cmp -s "$TMPROOT/out" "$TMPROOT/receipt"' "and it is echoed byte for byte"
cp "$TMPROOT/rec.env" "$TMPROOT/receipt"
expect_exit 1 "a receipt without its digest line refuses" receipt "$TMPROOT/receipt"
# Bound to the message — measured by mutation: with the line-1 check deleted, the shortened record
# still fails validation and this case stayed green for the wrong reason.
check 'grep -q "line 1 must be JBL_RECEIPT_RECORD_DIGEST" "$TMPROOT/err"' "  … and it is the digest-line check that answered"
{
  printf 'JBL_RECEIPT_RECORD_DIGEST=%s\n' "$D_CADDY"
  sed '/^JBL_RELEASE_SEQUENCE=/d' "$TMPROOT/rec.env"
} >"$TMPROOT/receipt"
expect_exit 1 "a receipt whose record is invalid refuses — it is held to the record's own validator" receipt "$TMPROOT/receipt"
expect_exit 2 "a missing receipt cannot be answered (the caller decides what absence means)" receipt "$TMPROOT/none"

echo "-- validate: the closed format"
expect_exit 0 "the emitted record validates" validate "$TMPROOT/good.env"
check 'cmp -s "$TMPROOT/out" "$TMPROOT/good.env"' "and validate echoes exactly the validated bytes"

mutant() {
  local desc="$1" script="$2"
  sed -e "$script" "$TMPROOT/good.env" >"$TMPROOT/m.env"
  expect_exit 1 "$desc" validate "$TMPROOT/m.env"
}
mutant "a missing key" '/^JBL_RELEASE_IMAGE_WEB=/d'
mutant "an extra key" '$a JBL_RELEASE_EXTRA=1'
mutant "a duplicated key" '/^JBL_RELEASE_IMAGE_API=/p'
mutant "two keys swapped" '/^JBL_RELEASE_IMAGE_API=/{h;d};/^JBL_RELEASE_IMAGE_WORKER=/G'
mutant "a future format version" 's/^JBL_RELEASE_FORMAT=1$/JBL_RELEASE_FORMAT=2/'
mutant "another repository" 's#^JBL_RELEASE_REPOSITORY=.*#JBL_RELEASE_REPOSITORY=attacker/jobbliggaren#'
mutant "another source ref" 's#^JBL_RELEASE_SOURCE_REF=.*#JBL_RELEASE_SOURCE_REF=refs/heads/feature#'
mutant "an abbreviated source SHA" 's/^JBL_RELEASE_SOURCE_SHA=\(.......\).*/JBL_RELEASE_SOURCE_SHA=\1/'
mutant "a zero sequence" 's/^JBL_RELEASE_SEQUENCE=.*/JBL_RELEASE_SEQUENCE=0/'
mutant "a sequence with a leading zero" 's/^JBL_RELEASE_SEQUENCE=.*/JBL_RELEASE_SEQUENCE=07/'
mutant "a negative sequence" 's/^JBL_RELEASE_SEQUENCE=.*/JBL_RELEASE_SEQUENCE=-1/'
mutant "an image named by TAG, not digest" 's#^JBL_RELEASE_IMAGE_API=.*#JBL_RELEASE_IMAGE_API=latest#'
mutant "an image redirected to another registry" "s#^JBL_RELEASE_IMAGE_API=.*#JBL_RELEASE_IMAGE_API=evil.example/x@$D_API#"
mutant "a value carrying a shell expansion" 's#^JBL_RELEASE_DEPLOY_SHA256=.*#JBL_RELEASE_DEPLOY_SHA256=$(reboot)#'
mutant "a value with a space" 's#^JBL_RELEASE_MIGRATIONS_IDENTITY=.*#JBL_RELEASE_MIGRATIONS_IDENTITY=20260506091354_InitialIdentity x#'
mutant "an empty migration list" 's#^JBL_RELEASE_MIGRATIONS_IDENTITY=.*#JBL_RELEASE_MIGRATIONS_IDENTITY=#'
mutant "migration ids out of order" 's#^JBL_RELEASE_MIGRATIONS_APP=.*#JBL_RELEASE_MIGRATIONS_APP=20260927121429_AddXmin,20260419145850_InitialCreate#'
mutant "a duplicated migration id" 's#^JBL_RELEASE_MIGRATIONS_APP=.*#JBL_RELEASE_MIGRATIONS_APP=20260419145850_InitialCreate,20260419145850_InitialCreate#'
mutant "a line with no '='" 's#^JBL_RELEASE_SOURCE_REF=.*#JBL_RELEASE_SOURCE_REF#'
mutant "a comment line" '1i # a comment'
mutant "a blank line" '1i\
'

printf '%s' "$(cat "$TMPROOT/good.env")" >"$TMPROOT/m.env"
expect_exit 1 "no trailing newline" validate "$TMPROOT/m.env"
sed 's/$/\r/' "$TMPROOT/good.env" >"$TMPROOT/m.env"
expect_exit 1 "CRLF line endings" validate "$TMPROOT/m.env"
# Bound to the message: a CR inside a value also fails that value's charset, so the exit code alone
# would stay green with the CR check deleted — which is exactly how a text-mode grep went unnoticed.
check 'grep -q "carriage return" "$TMPROOT/err"' "  … and it is the carriage-return check that answered"
{
  cat "$TMPROOT/good.env"
  printf 'X\000Y\n'
} >"$TMPROOT/m.env"
expect_exit 1 "a NUL byte" validate "$TMPROOT/m.env"
check 'grep -q "NUL byte" "$TMPROOT/err"' "  … and it is the NUL check that answered"
: >"$TMPROOT/m.env"
expect_exit 1 "an empty file" validate "$TMPROOT/m.env"
head -c 70000 /dev/zero | tr '\0' 'A' >"$TMPROOT/m.env"
expect_exit 1 "a file above the size cap" validate "$TMPROOT/m.env"
expect_exit 2 "a file that does not exist cannot be answered" validate "$TMPROOT/nope.env"
expect_exit 2 "validate with no file is a usage error" validate

echo "-- read: verify, copy, validate, verify again"
GOOD_SHA=$(sed -n 's/^JBL_RELEASE_SOURCE_SHA=//p' "$TMPROOT/good.env")
readonly GOOD_SHA
# The verifier stub answers its N-th call with the N-th code in $TMPROOT/verifier-codes and records
# every argument list, so a case can assert both the ORDER of the two checks and what each pinned.
cat >"$FIXDIR/verify-image-attestation.sh" <<EOF
#!/usr/bin/env bash
printf '%s\n' "\$*" >>"$TMPROOT/verifier-calls"
n=\$(wc -l <"$TMPROOT/verifier-calls")
code=\$(sed -n "\${n}p" "$TMPROOT/verifier-codes")
exit "\${code:-0}"
EOF
chmod +x "$FIXDIR/verify-image-attestation.sh"
# docker stub: `create` records its arguments, `cp` emits a tar of $TMPROOT/image/, `rm` records that
# the container went away.
mkdir -p "$TMPROOT/image"
cat >"$BIN/docker" <<EOF
#!/usr/bin/env bash
case "\$1" in
  create) printf '%s\n' "\$*" >"$TMPROOT/create-args"; [ -f "$TMPROOT/create-fails" ] && exit 1; echo cid123 ;;
  cp)     (cd "$TMPROOT/image" && tar -cf - \$(ls -A)) 2>/dev/null ;;
  rm)     printf '%s\n' "\$*" >"$TMPROOT/rm-args" ;;
  *)      echo "unexpected docker invocation: \$*" >&2; exit 99 ;;
esac
EOF
chmod +x "$BIN/docker"
readonly REF="ghcr.io/klasolsson81/jobbliggaren-release@$D_API"

read_case() {
  : >"$TMPROOT/verifier-calls"
  printf '%s\n' "$@" >"$TMPROOT/verifier-codes"
  rm -f "$TMPROOT/create-args" "$TMPROOT/rm-args"
}
image_holds() {
  rm -rf "$TMPROOT/image"
  mkdir -p "$TMPROOT/image"
  cp "$1" "$TMPROOT/image/release.env"
}

image_holds "$TMPROOT/good.env"
read_case 0 0
expect_exit 0 "a proven record image reads" read "$REF"
check 'cmp -s "$TMPROOT/out" "$TMPROOT/good.env"' "and prints exactly the validated record"
check '[ "$(sed -n 1p "$TMPROOT/verifier-calls")" = "$REF" ]' "the FIRST check is the identity alone, before the commit is known"
check '[ "$(sed -n 2p "$TMPROOT/verifier-calls")" = "$REF $GOOD_SHA" ]' "the SECOND check binds the digest to the commit the record names"
check 'grep -qF -- "--pull never" "$TMPROOT/create-args"' "the container is created with --pull never: read never fetches"
check 'grep -qF -- "--network none" "$TMPROOT/create-args"' "and with no network"
check 'grep -qE -- "--name jbl-release-read-[0-9]+-[0-9]+" "$TMPROOT/create-args"' "and under a generated name"
check 'grep -qF -- "-v" "$TMPROOT/rm-args" && grep -qF "jbl-release-read-" "$TMPROOT/rm-args"' "and it is removed with its volumes"

# THE ORDER ITSELF. Nothing a registry writer placed in an image may be copied out before the image is
# proven ours — the marker the docker stub writes on `create` is what makes "never" assertable.
read_case 1
expect_exit 1 "an image our workflow did not attest refuses" read "$REF"
check '[ ! -f "$TMPROOT/create-args" ]' "and not one byte of it was copied out — create was never called"
read_case 2
expect_exit 2 "a verifier that cannot answer is 'cannot answer', and nothing is read either" read "$REF"
check '[ ! -f "$TMPROOT/create-args" ]' "and create was never called"

read_case 0 1
expect_exit 1 "a record whose digest was not built from the commit it names refuses" read "$REF"

image_holds "$TMPROOT/good.env"
read_case 0 0
expect_exit 0 "an expected commit that matches reads" read "$REF" "$GOOD_SHA"
read_case 0 0
expect_exit 1 "a record that is another commit's release refuses under a pin naming this one" read "$REF" "$(printf 'f%.0s' $(seq 40))"
read_case 0 0
expect_exit 2 "a malformed expected commit is a usage error" read "$REF" "NOTASHA"

sed '/^JBL_RELEASE_IMAGE_WEB=/d' "$TMPROOT/good.env" >"$TMPROOT/m.env"
image_holds "$TMPROOT/m.env"
read_case 0 0
expect_exit 1 "a proven image holding an invalid record refuses" read "$REF"
check '[ "$(wc -l <"$TMPROOT/verifier-calls")" -eq 1 ]' "and the commit check never ran on an invalid record"

rm -rf "$TMPROOT/image" && mkdir -p "$TMPROOT/image"
read_case 0 0
expect_exit 1 "a record image with no /release.env refuses" read "$REF"

# Large enough that the ARCHIVE reaches read's own cap; a file just over MAX_BYTES would pass the copy and
# be refused by validate instead, which is a different check.
head -c 200000 /dev/zero | tr '\0' 'A' >"$TMPROOT/huge"
image_holds "$TMPROOT/huge"
read_case 0 0
expect_exit 1 "an oversized /release.env refuses" read "$REF"
check 'grep -q "larger than a record can be" "$TMPROOT/err"' "and it is the size cap that refused it"

rm -rf "$TMPROOT/image" && mkdir -p "$TMPROOT/image"
cp "$TMPROOT/good.env" "$TMPROOT/image/real.env"
ln -s real.env "$TMPROOT/image/release.env" 2>/dev/null || true
if [ -L "$TMPROOT/image/release.env" ]; then
  rm -f "$TMPROOT/image/real.env"
  read_case 0 0
  expect_exit 1 "a SYMLINK named release.env refuses — only a regular file is a record" read "$REF"
else
  # An announced skip is not a measurement: CI sets the flag, so on ubuntu a skip here is a failure.
  echo "  skip symlink case: this filesystem cannot create a symlink (Git Bash without symlink rights)"
  if [ "${JBL_REQUIRE_SYMLINK_CASES:-0}" = "1" ]; then
    fail=$((fail + 1))
    echo "  FAIL JBL_REQUIRE_SYMLINK_CASES=1 but no symlink could be created" >&2
  fi
fi

image_holds "$TMPROOT/good.env"
cp "$TMPROOT/good.env" "$TMPROOT/image/second.env"
read_case 0 0
expect_exit 1 "an archive with a second member refuses" read "$REF"
# Bound to the message — measured by mutation: with the one-member check deleted, the regular-file
# check still refused (the listing's last line names the second file), green for the wrong reason.
check 'grep -q "holds more than one member" "$TMPROOT/err"' "  … and it is the one-member check that answered"

image_holds "$TMPROOT/good.env"
touch "$TMPROOT/create-fails"
read_case 0 0
expect_exit 2 "an image not present locally cannot be answered (and is not fetched)" read "$REF"
rm -f "$TMPROOT/create-fails"

read_case 0 0
expect_exit 2 "read refuses a TAG reference" read "ghcr.io/klasolsson81/jobbliggaren-release:dev"
expect_exit 2 "read refuses a reference that would reach docker's flag parser" read "--privileged@$D_API"

echo "-- the real tree"
got=0
run_sut migrations "$repo_root" || got=$?
check '[ "$got" -eq 0 ]' "the real source tree's migrations are readable (exit $got)"
app_ids=$(grep -c '^APP ' "$TMPROOT/out" || true)
id_ids=$(grep -c '^IDENTITY ' "$TMPROOT/out" || true)
app_files=$(find "$repo_root/src/Jobbliggaren.Infrastructure/Persistence/Migrations" -name '*.Designer.cs' | wc -l)
id_files=$(find "$repo_root/src/Jobbliggaren.Infrastructure/Identity/Migrations" -name '*.Designer.cs' | wc -l)
check '[ "$app_ids" -eq "$app_files" ] && [ "$app_files" -gt 0 ]' "every AppDbContext migration is declared ($app_ids ids, $app_files designer files)"
check '[ "$id_ids" -eq "$id_files" ] && [ "$id_files" -gt 0 ]' "every AppIdentityDbContext migration is declared ($id_ids ids, $id_files designer files)"

# The record names exactly the images release-images.yml builds — a sixth image in the matrix turns
# this red instead of shipping releases that leave it out.
matrix=$(sed -nE 's/^[[:space:]]*- \{ name: ([a-z]+),.*/\1/p' "$repo_root/.github/workflows/release-images.yml" | LC_ALL=C sort | paste -sd' ' -)
record=$(sed -nE 's/^[[:space:]]*JBL_RELEASE_IMAGE_([A-Z]+)$/\1/p' "$SUT" | tr 'A-Z' 'a-z' | LC_ALL=C sort | paste -sd' ' -)
check '[ -n "$matrix" ] && [ "$matrix" = "$record" ]' "the record format names exactly the workflow's images (matrix: $matrix · record: $record)"

echo "-- protected references share the receipt validator"
{ printf 'JBL_RECEIPT_RECORD_DIGEST=%s\n' "$(d 6)"; cat "$TMPROOT/good.env"; } >"$TMPROOT/retention-receipt"
expect_exit 0 "protected-refs returns the receipt's six references" protected-refs "$TMPROOT/retention-receipt"
cat >"$TMPROOT/protected-expected" <<EOF
ghcr.io/klasolsson81/jobbliggaren-release@$(d 6)
ghcr.io/klasolsson81/jobbliggaren-api@$D_API
ghcr.io/klasolsson81/jobbliggaren-worker@$D_WORKER
ghcr.io/klasolsson81/jobbliggaren-migrate@$D_MIGRATE
ghcr.io/klasolsson81/jobbliggaren-web@$D_WEB
ghcr.io/klasolsson81/jobbliggaren-caddy@$D_CADDY
EOF
check 'cmp -s "$TMPROOT/out" "$TMPROOT/protected-expected"' "exact record and five app references, with no schema change"
sed -i 's/JBL_RELEASE_SEQUENCE=2/JBL_RELEASE_SEQUENCE=broken/' "$TMPROOT/retention-receipt"
expect_exit 1 "malformed receipt emits no protection" protected-refs "$TMPROOT/retention-receipt"
check '[ ! -s "$TMPROOT/out" ]' "no partial stdout before complete validation"
# THE PROOF THE CANONICAL FORM SHIPS WITH (senior-cto-advisor, 2026-10-03: never ship the canonicalisation
# without it). The real compose file and its canonical form must resolve to the identical compose model —
# so stripping full-line comments and blank lines can never hide a configuration change from the hash.
# `--no-interpolate` keeps `.env` (and every credential in it) out of the comparison. Needs the compose CLI
# but no daemon; CI sets JBL_REQUIRE_COMPOSE_CASES so a missing CLI there is a failure, not a skip.
if docker compose version >/dev/null 2>&1; then
  mkdir -p "$TMPROOT/proof"
  tr -d '\r' <"$repo_root/deploy/docker-compose.yml" >"$TMPROOT/proof/docker-compose.yml"
  sed -n '/^canonical_compose() {/,/^}/p' "$SUT" >"$TMPROOT/proof/canon.sh"
  # shellcheck disable=SC1091 # the function under test, extracted from the SUT itself
  (. "$TMPROOT/proof/canon.sh" && canonical_compose "$TMPROOT/proof/docker-compose.yml") >"$TMPROOT/proof/canonical.yml"
  resolve() { docker compose -f "$1" --profile ops config --no-interpolate --no-path-resolution 2>"$TMPROOT/proof/err"; }
  got=0
  resolve "$TMPROOT/proof/docker-compose.yml" >"$TMPROOT/proof/a" || got=$?
  resolve "$TMPROOT/proof/canonical.yml" >"$TMPROOT/proof/b" || got=$?
  check '[ "$got" -eq 0 ] && [ -s "$TMPROOT/proof/a" ] && cmp -s "$TMPROOT/proof/a" "$TMPROOT/proof/b"' \
    "the real compose file and its canonical form resolve to the identical model ($(wc -l <"$TMPROOT/proof/docker-compose.yml") → $(wc -l <"$TMPROOT/proof/canonical.yml") lines)"
  check '[ "$(wc -l <"$TMPROOT/proof/canonical.yml")" -lt "$(wc -l <"$TMPROOT/proof/docker-compose.yml")" ]' \
    "and the canonical form really is shorter — the proof is not comparing a file with itself"
else
  echo "  skip the canonical-form proof: no docker compose CLI on this host"
  if [ "${JBL_REQUIRE_COMPOSE_CASES:-0}" = "1" ]; then
    fail=$((fail + 1))
    echo "  FAIL JBL_REQUIRE_COMPOSE_CASES=1 but the compose CLI is missing" >&2
  fi
fi

# Every file the real compose names by a relative path is one the deployment hash covers. The reader
# takes a volume's short form, quoted or not, a long form's source and an env_file, each `./` or
# `../`; a parent path stays as written, so it can never match DEPLOY_FILES.
relative_paths() {
  tr -d '\r' <"$1" | sed -nE \
    -e 's#^[[:space:]]*-[[:space:]]+["'"'"']?(\.\.?/[^:"'"'"']+).*#\1#p' \
    -e 's#^[[:space:]]*(source|env_file):[[:space:]]+["'"'"']?(\.\.?/[^"'"'"'[:space:]]+).*#\2#p' |
    sed -E 's#^\./#deploy/#' | LC_ALL=C sort -u
}
printf '      - "./redis/x.sh:/usr/local/bin/x:ro"\n' >"$TMPROOT/quoted.yml"
check '[ "$(relative_paths "$TMPROOT/quoted.yml")" = deploy/redis/x.sh ]' "the reader sees a quoted short-form mount"
printf '      - ../secrets/z:/run/z:ro\n' >"$TMPROOT/parent.yml"
check '[ "$(relative_paths "$TMPROOT/parent.yml")" = ../secrets/z ]' "the reader sees a parent-directory mount, and leaves it unbindable"
printf '    env_file: ./app.env\n' >"$TMPROOT/envfile.yml"
check '[ "$(relative_paths "$TMPROOT/envfile.yml")" = deploy/app.env ]' "the reader sees an env_file"
mounted=$(relative_paths "$repo_root/deploy/docker-compose.yml")
bound=$(sed -n '/^readonly -a DEPLOY_FILES=(/,/^)/p' "$SUT" | sed -n 's/^[[:space:]]*\(deploy\/[^[:space:]]*\)$/\1/p')
missing=$(comm -23 <(printf '%s\n' "$mounted") <(printf '%s\n' "$bound" | LC_ALL=C sort -u))
check '[ -n "$mounted" ] && [ -z "$missing" ]' "every relatively mounted file is in DEPLOY_FILES (mounted: $(echo $mounted); missing: ${missing:-none})"

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ]
