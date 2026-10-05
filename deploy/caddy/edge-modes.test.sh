#!/usr/bin/env bash
# The edge's two environment seams, measured on a built image (#1768, ADR 0154).
#
#   bash deploy/caddy/edge-modes.test.sh <caddy image>
#
# SITE_ADMISSION: the gate stands unless the value is exactly `open`. SITE_ALIASES: `apex` adds the
# www 308 and dev. 302 sites, and nothing else adds them. Each admission case runs in a fresh
# container on the shipped Caddyfile's adapted config, with ONE change: the TLS issuer becomes
# Caddy's internal CA, so no ACME request leaves the sandbox. The script asserts that the HTTP app
# it runs is byte-equal to the shipped one, so the route tree under test is the one that ships.
# Nothing here reaches a remote host; the upstream is a `caddy respond` stub named `web`.
set -euo pipefail
export MSYS_NO_PATHCONV=1

image=${1:?pass the caddy image to test}
id="edge-modes-$$-$RANDOM"
scratch=$(mktemp -d)
# Native jq and docker cp on a Windows workstation cannot open an MSYS path.
if command -v cygpath >/dev/null 2>&1; then scratch=$(cygpath -m "$scratch"); fi
network_created=0
owned=()
failures=0

cleanup() {
  local c
  for c in "${owned[@]}"; do docker rm -f "$c" >/dev/null 2>&1 || true; done
  if [ "$network_created" = 1 ]; then docker network rm "$id" >/dev/null 2>&1 || true; fi
  rm -rf "$scratch"
}
trap cleanup EXIT

fail() { echo "FAIL: $*" >&2; failures=$((failures + 1)); }
pass() { echo "ok: $*"; }

SITE=edge.test
HSTS='strict-transport-security: max-age=31536000; includeSubDomains'
MARKER='x-edge-stub: upstream'
USER_NAME=probe
PASSWORD=synthetic-only
HASH=$(docker run --rm --network none --entrypoint caddy "$image" hash-password --plaintext "$PASSWORD")

base_env=(-e "SITE_HOST=$SITE" -e ACME_EMAIL=probe@example.com
  -e ACME_CA=https://acme.invalid/directory
  -e "BASIC_AUTH_USER=$USER_NAME" -e "BASIC_AUTH_HASH=$HASH")

# adapt <out> [docker -e args...] — the shipped Caddyfile's JSON for one environment. Captured and
# judged on its exit code, never through a pipe.
adapt() {
  local out=$1; shift
  docker run --rm --network none "${base_env[@]}" "$@" --entrypoint caddy "$image" \
    adapt --config /etc/caddy/Caddyfile >"$out" 2>"$out.err"
}

# must_adapt — adapt, and stop the run if the shipped config does not load at all: every check
# after it would otherwise compare empty files and pass.
must_adapt() {
  adapt "$@" || { echo "FAIL: caddy adapt refused ${*:2}: $(tail -1 "$1.err")" >&2; exit 1; }
}

hosts_of() {
  jq -c '[.apps.http.servers[].routes[].match[]?.host[]?] | unique' "$1"
}

# --- Static: the admission value never enters the parsed configuration ----------------------
must_adapt "$scratch/unset.json"
must_adapt "$scratch/basic_auth.json" -e SITE_ADMISSION=basic_auth
must_adapt "$scratch/open.json" -e SITE_ADMISSION=open
if cmp -s "$scratch/unset.json" "$scratch/basic_auth.json" && cmp -s "$scratch/unset.json" "$scratch/open.json"; then
  pass "adapt output is identical for unset, basic_auth and open"
else
  fail "adapt output differs by SITE_ADMISSION — the value reached the parsed config"
fi
gate=$(jq -c '[.. | objects | select(.handler? == "authentication")] | length' "$scratch/unset.json")
[ "$gate" = 1 ] && pass "exactly one authentication handler" || fail "authentication handlers: $gate"

# --- Static: aliases only for the exact name `apex` ------------------------------------------
must_adapt "$scratch/aliases-none.json" -e SITE_ALIASES=none
none_hosts=$(hosts_of "$scratch/aliases-none.json")
[ "$none_hosts" = "[\"$SITE\"]" ] && pass "SITE_ALIASES=none serves $SITE alone" || fail "none hosts: $none_hosts"
for v in UNSET '*' 'a*' '?pex'; do
  if [ "$v" = UNSET ]; then must_adapt "$scratch/a.json"; else must_adapt "$scratch/a.json" -e "SITE_ALIASES=$v"; fi
  if cmp -s "$scratch/a.json" "$scratch/aliases-none.json"; then pass "SITE_ALIASES=[$v] adapts exactly as none"
  else fail "SITE_ALIASES=[$v] differs from none"; fi
done
must_adapt "$scratch/aliases-apex.json" -e SITE_ALIASES=apex
apex_hosts=$(hosts_of "$scratch/aliases-apex.json")
[ "$apex_hosts" = "[\"dev.$SITE\",\"$SITE\",\"www.$SITE\"]" ] && pass "SITE_ALIASES=apex adds www and dev." || fail "apex hosts: $apex_hosts"
if adapt "$scratch/apx.json" -e SITE_ALIASES=apx; then fail "SITE_ALIASES=apx adapted; the runbook preflight assumes it refuses"
else pass "an unknown alias name refuses to adapt (the runbook preflight's case)"; fi

for admission in UNSET basic_auth open; do
  for aliases in UNSET none apex; do
    args=()
    [ "$admission" = UNSET ] || args+=(-e "SITE_ADMISSION=$admission")
    [ "$aliases" = UNSET ] || args+=(-e "SITE_ALIASES=$aliases")
    must_adapt "$scratch/combo.json" "${args[@]}"
  done
done
pass "every combination of SITE_ADMISSION and SITE_ALIASES adapts"

# --- Runtime -------------------------------------------------------------------------------
docker network create "$id" >/dev/null; network_created=1
stub="$id-web"; owned+=("$stub")
docker run -d --name "$stub" --network "$id" --network-alias web --entrypoint caddy "$image" \
  respond --listen :3000 --header "X-Edge-Stub: upstream" --body upstream >/dev/null

# run_edge <name> <aliases> [docker -e args...] — sets PORT to the published 443 port. Not called in
# a command substitution: a subshell would lose the failure count and the cleanup list.
run_edge() {
  local name=$1 aliases=$2; shift 2
  local shipped="$scratch/$name.shipped.json" test_cfg="$scratch/$name.test.json"
  must_adapt "$shipped" -e "SITE_ALIASES=$aliases"
  jq '.apps.tls.automation.policies = [{"issuers": [{"module": "internal"}]}]' "$shipped" >"$test_cfg"
  if ! cmp -s <(jq -S .apps.http "$shipped") <(jq -S .apps.http "$test_cfg"); then
    fail "$name: the issuer swap changed apps.http"
  fi
  owned+=("$name")
  docker create --name "$name" --network "$id" -p 127.0.0.1::443 \
    "${base_env[@]}" -e "SITE_ALIASES=$aliases" "$@" --entrypoint caddy "$image" \
    run --config /etc/caddy/test.json >/dev/null
  docker cp -q "$test_cfg" "$name:/etc/caddy/test.json"
  docker start "$name" >/dev/null
  PORT=$(docker port "$name" 443/tcp | head -1 | sed 's/.*://')
  local i
  for i in $(seq 1 40); do
    curl -sk -o "$scratch/body" --max-time 2 --connect-to "$SITE:443:127.0.0.1:$PORT" "https://$SITE/.well-known/acme-challenge/ready" && break
    sleep 0.25
  done
}

# get <port> <host> <path> [curl args...] — response headers, lower-cased, CR stripped.
get() {
  local port=$1 host=$2 path=$3; shift 3
  curl -sk -D - -o "$scratch/body" --max-time 20 --connect-to "$host:443:127.0.0.1:$port" "$@" "https://$host$path" \
    | tr -d '\r' | sed 's/^\([^:]*\):/\L\1:/'
}

status_of() { head -1 <<<"$1" | awk '{print $2}'; }

check_running() {
  [ "$(docker inspect -f '{{.State.Running}}' "$1")" = true ] && pass "$1: still running" || fail "$1: the edge stopped"
}

case_no=0
for value in UNSET basic_auth open Open OPEN ' open' 'open ' opne 'o*' '?pen' '*' 'open x' 'open basic_auth' '' \
  'x" == "x' '" == "" ? false : "'; do
  case_no=$((case_no + 1))
  name="$id-a$case_no"
  if [ "$value" = UNSET ]; then run_edge "$name" none; else run_edge "$name" none -e "SITE_ADMISSION=$value"; fi
  port=$PORT
  h=$(get "$port" "$SITE" /)
  if [ "$value" = open ]; then
    [ "$(status_of "$h")" = 200 ] && grep -qx "$MARKER" <<<"$h" && grep -qx "$HSTS" <<<"$h" \
      && pass "[$value] 200 from upstream, no credentials, HSTS" || fail "[$value] expected 200 from upstream: $(status_of "$h")"
  else
    [ "$(status_of "$h")" = 401 ] && grep -qi '^www-authenticate: basic' <<<"$h" && grep -qx "$HSTS" <<<"$h" \
      && ! grep -qx "$MARKER" <<<"$h" \
      && pass "[$value] 401 Basic with HSTS, upstream not reached" || fail "[$value] expected the gate: $(status_of "$h")"
  fi
  h=$(get "$port" "$SITE" /.well-known/acme-challenge/x)
  [ "$(status_of "$h")" = 404 ] && grep -qx "$HSTS" <<<"$h" && ! grep -qx "$MARKER" <<<"$h" \
    && pass "[$value] ACME prefix answered at the edge: 404 with HSTS" || fail "[$value] ACME prefix: $(status_of "$h")"
  check_running "$name"
  docker rm -f "$name" >/dev/null
done

# The gate admits, not only refuses.
name="$id-cred"; run_edge "$name" none; port=$PORT
h=$(get "$port" "$SITE" / -u "$USER_NAME:$PASSWORD")
[ "$(status_of "$h")" = 200 ] && grep -qx "$MARKER" <<<"$h" && pass "gated edge admits the credential" || fail "credential refused: $(status_of "$h")"
docker rm -f "$name" >/dev/null

# Aliases at runtime, admission unset: the redirects carry HSTS, keep path and query, never proxy.
name="$id-apex"; run_edge "$name" apex; port=$PORT
h=$(get "$port" "www.$SITE" '/p?q=1')
[ "$(status_of "$h")" = 308 ] && grep -qx "location: https://$SITE/p?q=1" <<<"$h" && grep -qx "$HSTS" <<<"$h" \
  && ! grep -qx "$MARKER" <<<"$h" && pass "www 308 to the apex, path and query kept, HSTS" || fail "www: $(status_of "$h") $(grep '^location' <<<"$h" || true)"
h=$(get "$port" "dev.$SITE" '/p?q=1')
[ "$(status_of "$h")" = 302 ] && grep -qx "location: https://$SITE/p?q=1" <<<"$h" && grep -qx "$HSTS" <<<"$h" \
  && ! grep -qx "$MARKER" <<<"$h" && pass "dev. 302 to the apex, path and query kept, HSTS" || fail "dev.: $(status_of "$h") $(grep '^location' <<<"$h" || true)"
h=$(get "$port" "www.$SITE" '/p?q=1' -H "Host: WWW.$SITE:443")
grep -qx "location: https://$SITE/p?q=1" <<<"$h"   && pass "the redirect target is SITE_HOST, never the request's Host" || fail "Host-derived location: $(grep '^location' <<<"$h" || true)"
h=$(get "$port" "$SITE" /)
[ "$(status_of "$h")" = 401 ] && pass "aliases leave admission alone: apex still 401" || fail "apex with aliases: $(status_of "$h")"
if curl -sk -o "$scratch/body" --max-time 5 --connect-to "unknown.test:443:127.0.0.1:$port" https://unknown.test/ 2>/dev/null; then
  fail "a name the edge does not serve completed a TLS handshake"
else
  pass "an unknown SNI is not served"
fi
check_running "$name"
docker rm -f "$name" >/dev/null

# M-5a: Caddy's own 503 while web is down carries HSTS, in both modes (ADR 0154 §5 C6). Gated, an
# anonymous request still meets the 401 first; the credential reaches the 503.
name="$id-503g"; run_edge "$name" none; port_gated=$PORT
name_open="$id-503o"; run_edge "$name_open" none -e SITE_ADMISSION=open; port_open=$PORT
docker stop -t 1 "$stub" >/dev/null
# Past health_interval + health_timeout, so the active check has marked web down: before that a
# request fails its dial and reads 502, after it Caddy answers 503 without dialling.
sleep 9
h=$(get "$port_gated" "$SITE" /)
[ "$(status_of "$h")" = 401 ] && grep -qx "$HSTS" <<<"$h" && pass "gated edge, web down, anonymous: 401 with HSTS" || fail "gated, web down: $(status_of "$h")"
h=$(get "$port_gated" "$SITE" / -u "$USER_NAME:$PASSWORD")
[ "$(status_of "$h")" = 503 ] && grep -qx "$HSTS" <<<"$h" && pass "gated edge, web down, past the gate: 503 with HSTS" || fail "gated past the gate, web down: $(status_of "$h")"
h=$(get "$port_open" "$SITE" /)
[ "$(status_of "$h")" = 503 ] && grep -qx "$HSTS" <<<"$h" && pass "open edge, web down: 503 with HSTS" || fail "open, web down: $(status_of "$h")"
docker rm -f "$name" "$name_open" >/dev/null

if [ "$failures" -gt 0 ]; then echo "$failures check(s) failed"; exit 1; fi
echo "all edge-mode checks passed"
