#!/usr/bin/env bash
set -euo pipefail
set +x

fail() { printf 'REFUSING: Redis account maintenance %s\n' "$1" >&2; exit 1; }
[[ $EUID -eq 0 ]] || fail 'requires root'
[[ $# -ge 2 ]] || fail 'requires an operation and verified account UUID'
operation=$1
account=${2,,}
[[ $account =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]] || fail 'account UUID is invalid'
readonly tombstone="jobbliggaren:user:$account:deleted"
readonly session_index="jobbliggaren:user:$account:sessions"
case "$operation" in
  mark-deleted)
    [[ $# -eq 4 && $3 == --ttl-seconds ]] || fail 'mark-deleted requires explicit --ttl-seconds'
    ttl=$4
    [[ $ttl =~ ^[1-9][0-9]*$ && ${#ttl} -le 19 ]] || fail 'TTL must be positive whole seconds'
    [[ ${#ttl} -lt 19 || $ttl < 9223372036854775808 ]] || fail 'TTL exceeds Redis EX integer range'
    ;;
  check-deleted|clear-deleted|delete-session-index)
    [[ $# -eq 2 ]] || fail 'unexpected arguments'
    ;;
  delete-known-session)
    [[ $# -eq 4 && $3 == --session-key ]] || fail 'delete-known-session requires --session-key'
    [[ $4 =~ ^jobbliggaren:session:[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$ ]] || fail 'session key must contain a canonical SHA-256 base64url hash'
    session_key=$4
    ;;
  *) fail 'unknown operation';;
esac

readonly DEPLOY=/opt/jobbliggaren/deploy
readonly PASSWORD_FILE=/run/jobbliggaren/redis/operator/persistent-password
bash "$DEPLOY/systemd/jobbliggaren-redis-secrets.sh" --check >/dev/null 2>&1 || fail 'credential set is not verified'
container=$(/usr/bin/docker compose -f "$DEPLOY/docker-compose.yml" ps -q redis 2>/dev/null) || fail 'cannot locate persistent Redis'
[[ $container =~ ^[a-f0-9]{64}$ ]] || fail 'persistent Redis container is ambiguous or absent'
state=$(/usr/bin/docker inspect --format '{{.State.Running}} {{.Image}}' "$container" 2>/dev/null) || fail 'cannot inspect persistent Redis'
[[ $state =~ ^true\ (sha256:[a-f0-9]{64})$ ]] || fail 'persistent Redis is not running with a resolved image'
readonly image=${BASH_REMATCH[1]}
readonly cli_name="jbl-redis-maint-$(cat /proc/sys/kernel/random/uuid)"
cleanup() { /usr/bin/docker rm -f "$cli_name" >/dev/null 2>&1 || true; }
trap cleanup EXIT

cli() {
  response=$(/usr/bin/timeout --signal=TERM 15 /usr/bin/docker run --rm -i --name "$cli_name" \
    --network "container:$container" --pull never --read-only --user 65534:65534 \
    --cap-drop ALL --security-opt no-new-privileges --entrypoint redis-cli "$image" \
    -e --raw --user operator-persistent --askpass -h 127.0.0.1 -p 6379 -n 0 "$@" \
    < "$PASSWORD_FILE" 2>&1) || fail 'command failed; no result verified'
}
expect_bit() { [[ $response == 0 || $response == 1 ]] || fail 'unexpected command response'; }
case "$operation" in
  mark-deleted)
    cli SET "$tombstone" 1 EX "$ttl"
    [[ $response == OK ]] || fail 'deletion marker write was not confirmed'
    cli EXISTS "$tombstone"
    [[ $response == 1 ]] || fail 'deletion marker presence was not confirmed'
    printf 'Deletion marker set and presence verified.\n'
    ;;
  check-deleted)
    cli EXISTS "$tombstone"
    expect_bit
    if [[ $response == 1 ]]; then printf 'Deletion marker present.\n'; else printf 'Deletion marker absent.\n'; fi
    ;;
  clear-deleted)
    cli DEL "$tombstone"
    expect_bit
    cli EXISTS "$tombstone"
    [[ $response == 0 ]] || fail 'deletion marker removal was not confirmed'
    printf 'Deletion marker absence verified.\n'
    ;;
  delete-session-index|delete-known-session)
    key=$session_index
    [[ $operation != delete-known-session ]] || key=$session_key
    cli DEL "$key"
    expect_bit
    printf 'Requested session deletion completed (deleted=%s).\n' "$response"
    ;;
esac
