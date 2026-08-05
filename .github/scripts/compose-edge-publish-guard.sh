#!/usr/bin/env bash
# compose-edge-publish-guard — the production stack publishes ONE service, and it is the edge.
#
# WHY THIS EXISTS RATHER THAN POINTING compose-loopback-guard.sh AT deploy/. That guard fails
# any published port without a loopback bind address, so it scores exit 1 on Caddy 80/443 —
# the one binding that is correct. Under ADR 0050 Option B the API, Worker, Postgres, Redis
# and web publish nothing at all, and the reverse proxy must be reachable from the public
# internet or ACME HTTP-01 cannot complete. Same engine, opposite verdict (#196, #1215).
#
# THE PREDICATE. Clauses 1-3 are the ones #1215 named; clause 4 is an addition, and it is
# named as one in the PR that introduced it:
#   1. exactly one service publishes         -> which is also "nothing else publishes"
#   2. it is the edge service                -> EDGE_SERVICE below
#   3. its published ports are exactly 80 and 443
#   4. every one of those binds WIDE         -> a loopback-bound edge is an unreachable site
#                                               and a certificate that can never issue
#
# EXIT CONTRACT, and 2 never collapses into 1: 0 the predicate holds · 1 it does not · 2 the
# guard could not answer. Findings carry U+0001 at position 0; every refusal producer is
# untagged, so a refusal class added later needs no second edit (#1216).
set -euo pipefail

readonly EDGE_SERVICE="caddy"
readonly EXPECTED_PORTS="80 443"

usage() {
  echo "usage: $0 [project-dir]" >&2
  echo "  project-dir defaults to the repo deploy/ directory." >&2
}

if [ "$#" -gt 1 ]; then
  usage
  exit 2
fi

case "${1:-}" in
  -h | --help)
    usage
    exit 2
    ;;
esac

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
dir="${1:-$repo_root/deploy}"

# A file path is refused HERE because compose does not refuse it: it reads the path as a
# directory name, walks up to the parent, and judges that project instead.
if [ ! -d "$dir" ]; then
  echo "::error::compose-edge-publish-guard: not a directory: $dir" >&2
  exit 2
fi

compose_base_names=(compose.yaml compose.yml docker-compose.yaml docker-compose.yml)
has_base=0
for n in "${compose_base_names[@]}"; do
  if [ -f "$dir/$n" ]; then
    has_base=1
    break
  fi
done
if [ "$has_base" -eq 0 ]; then
  echo "::error::compose-edge-publish-guard: no compose file in $dir" >&2
  echo "Refused rather than answered. Compose walks UP when a directory carries none of" >&2
  echo "${compose_base_names[*]}, so the verdict would be about an ancestor project." >&2
  exit 2
fi

# Ambient seat, cleared before ANY docker compose call including the version probe. Keyed on
# the prefix rather than an enumeration: a future file-selecting variable defeats a list
# silently. The project own .env is the second seat and needs the empty env-file below.
for v in "${!COMPOSE_@}"; do unset "$v"; done

command -v docker >/dev/null 2>&1 || {
  echo "::error::compose-edge-publish-guard: docker not on PATH." >&2
  exit 2
}
command -v jq >/dev/null 2>&1 || {
  echo "::error::compose-edge-publish-guard: jq not on PATH." >&2
  exit 2
}
compose_version=$(docker compose version --short 2>/dev/null) || {
  echo "::error::compose-edge-publish-guard: docker compose unavailable (v2 or newer required)." >&2
  exit 2
}

errfile=$(mktemp)
# A real empty file rather than /dev/null: MSYS rewrites the POSIX path on its way to a
# Windows docker.exe, and this suite runs on both.
nullenv=$(mktemp)
trap 'rm -f "$errfile" "$nullenv"' EXIT

if ! model=$(docker compose --project-directory "$dir" --env-file "$nullenv" config --no-interpolate --format json 2>"$errfile"); then
  echo "::error::compose-edge-publish-guard: compose refused the project in $dir." >&2
  cat "$errfile" >&2
  exit 2
fi

# Compose warnings survive a successful run — a shadowing second file is announced here and
# nowhere else. Surfaced, never classified: they do not touch the exit code.
if [ -s "$errfile" ]; then
  sed 's/^/compose-edge-publish-guard: compose said: /' "$errfile" >&2
fi

out=$(jq -r --arg p "$dir" --arg edge "$EDGE_SERVICE" --arg want "$EXPECTED_PORTS" '
    # The finding token, built rather than escaped so it stays visible in this source.
    def soh: [1] | implode;
    # A value carrying a newline would forge a whole line, and one carrying U+0001 would forge
    # the token itself. Both are neutralised at the source, so no producer has to remember.
    def oneline: tostring | gsub("\n"; "\\n") | gsub("\r"; "\\r") | gsub("\\x01"; "\\u0001");
    def pp: $p | oneline;
    # published is a STRING in the short form and a NUMBER in an unquoted long form.
    def is_port_number: (type == "number") or ((type == "string") and test("^[0-9]+$"));
    def port_text: tostring;
    def is_ipv4: (type == "string") and test("^[0-9]{1,3}(\\.[0-9]{1,3}){3}$");
    # EXACTLY the two wildcards, never a complement. Widening this to "any IPv4 that is
    # not 127." passed an edge bound to 10.0.0.5 while still refusing the real IPv6
    # wildcard - the guard certifying the opposite of what its header promises, which is
    # #1198 rebuilt inside the remedy. Anything not decided here is UNJUDGED-BIND-IP.
    def is_wide_ip: (. == "0.0.0.0") or (. == "::");
    def is_loopback_ip: (. == "::1") or (is_ipv4 and startswith("127."));
    def binds_wide: (has("host_ip") | not) or (.host_ip | is_wide_ip);
    def bind_unreadable: has("host_ip")
        and ((.host_ip | is_wide_ip) or (.host_ip | is_loopback_ip) | not);

    def is_unresolved: (type == "string") and test("\\$");
    def host_nets: [ (.networks // {}) | to_entries[]
                     | select(.value.name == "host" or .value.driver == "host") | .key ];
    def unresolved_nets: [ (.networks // {}) | to_entries[]
                           | select((.value.name | is_unresolved)
                                    or (.value.driver | is_unresolved)) | .key ];
    def attached: (.networks // {}) | if type == "object" then keys else . end;

    ($want | split(" ") | sort) as $wanted
    | host_nets as $hostnets
    | unresolved_nets as $unresolved
    | (.services // {}) as $svc
    | [ $svc | to_entries[] | select(((.value.ports // []) | length) > 0) ] as $pub
    | [ $pub[] | .key ] as $names
    | [ $pub[] | .value.ports[] ] as $entries
    | [ $entries[] | select(type == "object")
        | select(has("published")) | select(.published | is_port_number)
        | .published | port_text ] as $readable

    # REFUSALS from here down are untagged, so any one of them makes the whole run exit 2.
    # A host-networked service listens on every host interface with ZERO ports entries,
    # so the publisher count above cannot see it. Measured: postgres with
    # network_mode host alongside a correct caddy scored exit 0 and the message said
    # nothing else publishes. Refused rather than judged, for the same reason the
    # loopback guard refuses this axis: the value can be a variable, and what it becomes
    # at up time is not readable here.
    | [ $svc | to_entries[] | select(.value.network_mode != null)
        | "\(pp): HOST-NETWORK-OR-MODE service=\(.key|oneline) network_mode=\(.value.network_mode|oneline)" ]
    + [ $svc | to_entries[] as $s | ($s.value | attached)[]
        | select(. as $n | $hostnets | index($n))
        | "\(pp): HOST-NETWORK-OR-MODE service=\($s.key|oneline) via=network:\(.|oneline)" ]
    + [ $svc | to_entries[] as $s | ($s.value | attached)[] as $n
        | select(($unresolved | index($n)) or ($n | is_unresolved))
        | "\(pp): UNRESOLVED-NETWORK service=\($s.key|oneline) network=\($n|oneline)" ]

    + [ $entries[] | select(type != "object")
        | "\(pp): UNRESOLVED-ENTRY entry=\(.|oneline)" ]
    + [ $entries[] | select(type == "object") | select(has("published") | not)
        | "\(pp): EPHEMERAL-PUBLISH service-port=\(.target // "<none>"|oneline)" ]
    + [ $entries[] | select(type == "object") | select(has("published"))
        | select((.published | is_port_number) | not)
        | "\(pp): UNREADABLE-PUBLISH published=\(.published|oneline)" ]
    + [ $entries[] | select(type == "object") | select(bind_unreadable)
        | "\(pp): UNJUDGED-BIND-IP host_ip=\(.host_ip|oneline)" ]

    # Findings — tagged.
    + [ select(($pub | length) != 1)
        | "\(soh)\(pp): PUBLISHER-COUNT expected 1 publishing service, found \($pub|length) [\($names|join(", ")|oneline)]" ]
    + [ select(($pub | length) == 1) | select($names[0] != $edge)
        | "\(soh)\(pp): PUBLISHER-NOT-EDGE the publishing service is \($names[0]|oneline), expected \($edge|oneline)" ]
    + [ select(($readable | unique) != $wanted)
        | "\(soh)\(pp): PORT-SET published ports are [\($readable|unique|join(", ")|oneline)], expected [\($wanted|join(", ")|oneline)]" ]
    + [ $entries[] | select(type == "object") | select(binds_wide | not)
        | "\(soh)\(pp): EDGE-NOT-WIDE published=\(.published // "<ephemeral>"|oneline) host_ip=\(.host_ip|oneline)" ]
    | .[]
  ' <<<"$model") || {
  echo "::error::compose-edge-publish-guard: could not read the resolved model for $dir." >&2
  exit 2
}

if [ -n "$out" ]; then
  # Herestring, not a pipe: grep -q leaves at its first match, a producer holding more than a
  # pipe buffer then dies of SIGPIPE, and pipefail makes that the pipeline status — so the test
  # would go false on exactly the inputs it exists to catch. `^$` is load-bearing too: the
  # herestring appends a newline, and without it that blank line makes every run exit 2.
  if grep -qvE $'^\001|^$' <<<"$out"; then
    echo "::error::compose-edge-publish-guard: a published port is in a state this guard will not judge." >&2
    echo "UNRESOLVED-ENTRY is an entry compose left as a raw string (an unexpanded variable, a" >&2
    echo "hostname bind address). EPHEMERAL-PUBLISH is an entry with no published port at all," >&2
    echo "which takes a random host port. UNREADABLE-PUBLISH is a published value that is not a" >&2
    echo "single number, such as a range. Write the edge ports literally." >&2
    echo "The rule, not the list: anything the guard did not read lands here, so a marker not" >&2
    echo "named above is one too." >&2
    sed $'s/^\001//' <<<"${out%$'\n'}" >&2
    exit 2
  fi
  echo "::error::compose-edge-publish-guard: the stack does not publish the shape Option B requires." >&2
  sed $'s/^\001//' <<<"${out%$'\n'}" >&2
  echo >&2
  echo "Expected exactly one publishing service, named $EDGE_SERVICE, publishing exactly" >&2
  echo "$EXPECTED_PORTS, each bound wide. Nothing else publishes: under Option B the API is not" >&2
  echo "edge-exposed, and Postgres and Redis are reachable only on the internal network." >&2
  echo "EDGE-NOT-WIDE means the reverse proxy was bound to loopback, which is an unreachable" >&2
  echo "site and a certificate that can never be issued — neither of which fails loudly." >&2
  exit 1
fi

echo "compose-edge-publish-guard: OK — $EDGE_SERVICE publishes $EXPECTED_PORTS and nothing else publishes (Compose $compose_version)"
