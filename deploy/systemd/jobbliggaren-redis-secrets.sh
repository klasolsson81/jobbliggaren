#!/usr/bin/env bash
set -euo pipefail
set +x

readonly ROOT=/run/jobbliggaren/redis
readonly POLICY=/opt/jobbliggaren/deploy/redis
readonly COMPOSE=/opt/jobbliggaren/deploy/docker-compose.yml
readonly IDS=/opt/jobbliggaren/deploy/systemd/jobbliggaren-runtime-ids.sh
readonly LOCK=/run/jobbliggaren-reconcile.lock
readonly -a USERS=(api-persistent api-volatile worker-persistent health-persistent health-volatile operator-persistent operator-volatile)
declare -A passwords=()
declare -A reader_uid=() reader_gid=()
fail() { printf 'REFUSING: Redis secret %s\n' "$*" >&2; exit 1; }
[[ $EUID -eq 0 ]] || fail 'operation requires root'
[[ ( $# -eq 1 && ( $1 == --check || $1 == --inject ) ) || ( $# -eq 3 && $1 == --check-images ) ]] || fail 'use --check, --inject or --check-images API_DIGEST WORKER_DIGEST'
readonly API_IMAGE=${2:-} WORKER_IMAGE=${3:-}
if [[ $1 == --check-images ]]; then
  [[ $API_IMAGE =~ @sha256:[a-f0-9]{64}$ && $WORKER_IMAGE =~ @sha256:[a-f0-9]{64}$ ]] || fail 'incoming application images must be verified digests'
fi
[[ $(findmnt -n -o FSTYPE -T /run) == tmpfs ]] || fail 'root must reside on tmpfs'
[[ ! -L "$ROOT" ]] || fail 'root is a symlink'

valid_password() { [[ $1 =~ ^[A-Fa-f0-9]{64}$ ]]; }
render_policy() {
  local store=$1 rendered placeholder digest user
  [[ $store == persistent || $store == volatile ]] || fail 'unknown policy store'
  rendered=$(cat "$POLICY/$store.acl.template")
  for user in "${USERS[@]}"; do
    placeholder=${user^^}; placeholder=${placeholder//-/_}
    digest=$(printf '%s' "${passwords[$user]}" | sha256sum); digest=${digest%% *}
    rendered=${rendered//\{\{${placeholder}_SHA256\}\}/$digest}
  done
  [[ "$rendered" != *'{{'* && "$rendered" != *'}}'* ]] || fail 'ACL has an unresolved placeholder'
  printf '%s\n' "$rendered"
  digest=$(printf '%s' "${passwords[operator-$store]}" | sha256sum); digest=${digest%% *}
  rendered=$(cat "$POLICY/operator-$store.acl.template")
  rendered=${rendered//\{\{OPERATOR_SHA256\}\}/$digest}
  [[ "$rendered" != *'{{'* && "$rendered" != *'}}'* ]] || fail 'operator ACL has an unresolved placeholder'
  printf '%s\n' "$rendered"
}

read_ids() {
  local role uid gid extra
  while read -r role uid gid extra; do
    [[ $role =~ ^(api|worker|redis)$ && $uid =~ ^[0-9]+$ && $gid =~ ^[0-9]+$ && -z $extra ]] || fail 'reader metadata is malformed'
    [[ -z ${reader_uid[$role]:-} ]] || fail 'reader metadata repeats a role'
    reader_uid[$role]=$uid; reader_gid[$role]=$gid
  done < "$ROOT/readers"
  [[ ${#reader_uid[@]} -eq 3 ]] || fail 'reader metadata is incomplete'
}

check_file() {
  local path=$1 uid=$2 gid=$3
  [[ ! -L "$path" && -f "$path" && -s "$path" ]] || fail 'file missing, empty or linked'
  [[ $(stat -c '%u:%g:%a' "$path") == "$uid:$gid:400" ]] || fail 'file ownership or mode differs'
}

check_set() {
  local purpose role dir user store value endpoint uid gid
  [[ -d "$ROOT" && $(stat -c '%u:%g:%a' "$ROOT") == 0:0:755 ]] || fail 'root posture differs'
  check_file "$ROOT/complete" 0 0
  check_file "$ROOT/readers" 0 0
  read_ids
  for purpose in api-persistent api-volatile worker-persistent persistent volatile operator; do
    role=redis
    [[ $purpose == api-* ]] && role=api
    [[ $purpose == worker-* ]] && role=worker
    uid=${reader_uid[$role]}; gid=${reader_gid[$role]}
    [[ $purpose == operator ]] && { uid=0; gid=0; }
    dir="$ROOT/$purpose"
    [[ ! -L "$dir" && -d "$dir" && $(stat -c '%u:%g:%a' "$dir") == "0:$gid:710" ]] || fail 'service directory posture differs'
    case "$purpose" in
      api-*|worker-*)
        check_file "$dir/connection" "$uid" "$gid"
        value=$(cat "$dir/connection")
        endpoint=redis; [[ $purpose == api-volatile ]] && endpoint=redis-volatile
        [[ $value == "$endpoint:6379,user=$purpose,password="* ]] || fail 'connection endpoint or identity differs'
        value=${value#"$endpoint:6379,user=$purpose,password="}; valid_password "$value" || fail 'connection credential is malformed'
        passwords[$purpose]=$value
        [[ $(find "$dir" -mindepth 1 -maxdepth 1 | wc -l) -eq 1 ]] || fail 'unexpected application file'
        ;;
      persistent|volatile)
        check_file "$dir/users.acl" "$uid" "$gid"
        check_file "$dir/health-password" "$uid" "$gid"
        value=$(cat "$dir/health-password"); valid_password "$value" || fail 'health credential is malformed'
        passwords[health-$purpose]=$value
        [[ $(find "$dir" -mindepth 1 -maxdepth 1 | wc -l) -eq 2 ]] || fail 'unexpected Redis file'
        ;;
      operator)
        for store in persistent volatile; do
          check_file "$dir/$store-password" 0 0
          value=$(cat "$dir/$store-password"); valid_password "$value" || fail 'operator credential is malformed'
          passwords[operator-$store]=$value
        done
        [[ $(find "$dir" -mindepth 1 -maxdepth 1 | wc -l) -eq 2 ]] || fail 'unexpected operator file'
        ;;
    esac
  done
  [[ $(printf '%s\n' "${passwords[@]}" | sort -u | wc -l) -eq ${#USERS[@]} ]] || fail 'identities share a credential'
  for store in persistent volatile; do
    cmp -s <(render_policy "$store") "$ROOT/$store/users.acl" || fail 'ACL does not match the complete credential set'
  done
}

measure_ids() {
  local role image output uid gid image_id
  for role in api worker redis redis-volatile; do
    image=$(/usr/bin/docker compose -f "$COMPOSE" config --format json |
      python3 -c 'import json,sys; print(json.load(sys.stdin)["services"][sys.argv[1]]["image"])' "$role")
    [[ $role != api || -z $API_IMAGE ]] || image=$API_IMAGE
    [[ $role != worker || -z $WORKER_IMAGE ]] || image=$WORKER_IMAGE
    image_id=$(/usr/bin/docker image inspect --format '{{.Id}}' "$image")
    [[ $image_id =~ ^sha256:[a-f0-9]{64}$ ]] || fail 'cannot resolve local immutable image'
    if [[ $role == redis || $role == redis-volatile ]]; then
      output=$(/usr/bin/docker run --rm --network none --cap-drop ALL --security-opt no-new-privileges \
        --pull never --entrypoint sh "$image_id" -c 'id -u redis; id -g redis')
    else
      output=$("$IDS" "$image_id")
    fi
    mapfile -t pair <<< "$output"
    [[ ${#pair[@]} -eq 2 && ${pair[0]} =~ ^[0-9]+$ && ${pair[1]} =~ ^[0-9]+$ ]] || fail 'cannot measure image reader'
    uid=${pair[0]}; gid=${pair[1]}
    if [[ $role == redis-volatile ]]; then
      [[ $uid == "${reader_uid[redis]}" && $gid == "${reader_gid[redis]}" ]] || fail 'Redis images have different readers'
      continue
    fi
    if [[ $1 == compare ]]; then
      [[ ${reader_uid[$role]} == "$uid" && ${reader_gid[$role]} == "$gid" ]] || fail 'incoming image reader differs; re-own the existing set, never rotate to fix ownership'
    else
      reader_uid[$role]=$uid; reader_gid[$role]=$gid
    fi
  done
}

if [[ $1 != --inject ]]; then
  check_set
  [[ $1 != --check-images ]] || measure_ids compare
  printf 'Redis credential set verified.\n'
  exit 0
fi
[[ -t 0 ]] || fail 'injection reads protected terminal input only'
exec 8>"$LOCK"
flock -n 8 || fail 'reconciliation is in progress'
if [[ -e "$ROOT" ]]; then
  check_set
  measure_ids compare
  printf 'Redis credential set already present; no rotation performed.\n'
  exit 0
fi
measure_ids populate
terminal_state=$(stty -g)
stty -echo
trap 'stty "$terminal_state"' EXIT
for user in "${USERS[@]}"; do
  printf 'Independent 64-hex credential for %s: ' "$user" >&2
  read -rs value; printf '\n' >&2
  valid_password "$value" || fail 'credential must be an independent 32-byte random value encoded as hex'
  passwords[$user]=$value
  unset value
done
[[ $(printf '%s\n' "${passwords[@]}" | sort -u | wc -l) -eq ${#USERS[@]} ]] || fail 'identities share a credential'
stty "$terminal_state"
trap - EXIT
umask 077
stage=$(mktemp -d "$ROOT.staging.XXXXXX")
trap 'rm -rf -- "$stage"' EXIT
for purpose in api-persistent api-volatile worker-persistent persistent volatile operator; do
  role=redis
  [[ $purpose == api-* ]] && role=api
  [[ $purpose == worker-* ]] && role=worker
  uid=${reader_uid[$role]}; gid=${reader_gid[$role]}
  [[ $purpose == operator ]] && { uid=0; gid=0; }
  install -d -m 0710 -o 0 -g "$gid" "$stage/$purpose"
  case "$purpose" in
    api-*|worker-*)
      endpoint=redis; [[ $purpose == api-volatile ]] && endpoint=redis-volatile
      printf '%s' "$endpoint:6379,user=$purpose,password=${passwords[$purpose]}" > "$stage/$purpose/connection"
      ;;
    persistent|volatile)
      render_policy "$purpose" > "$stage/$purpose/users.acl"
      printf '%s' "${passwords[health-$purpose]}" > "$stage/$purpose/health-password"
      ;;
    operator)
      for store in persistent volatile; do printf '%s' "${passwords[operator-$store]}" > "$stage/operator/$store-password"; done
      ;;
  esac
  find "$stage/$purpose" -mindepth 1 -maxdepth 1 -type f -exec chown "$uid:$gid" {} + -exec chmod 0400 {} +
done
for role in api worker redis; do printf '%s %s %s\n' "$role" "${reader_uid[$role]}" "${reader_gid[$role]}"; done > "$stage/readers"
printf 'complete\n' > "$stage/complete"
chmod 0400 "$stage/readers" "$stage/complete"
chmod 0755 "$stage"
mv -T "$stage" "$ROOT"
trap - EXIT
printf 'Complete Redis credential set published to tmpfs; no service restarted.\n'
