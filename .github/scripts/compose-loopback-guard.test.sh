#!/usr/bin/env bash
#
# Fixture tests for compose-loopback-guard.sh.
#
# Run:  bash .github/scripts/compose-loopback-guard.test.sh
#
# REQUIRES THE COMPOSE CLI, NOT A DAEMON. The guard reads `docker compose config`, which is
# client-side: every fixture below was authored and measured with the Docker daemon DOWN.
# A missing CLI makes the guard exit 2, so a runner without it fails loudly rather than
# reporting a suite of passes.
#
# WHY THE NEGATIVE FIXTURES CARRY THE WHOLE FILE. A guard whose fixtures all pass has proven
# that it does not crash, not that it catches anything — and this repo has measured that
# failure mode before. `bare_mapping` is the fixture that matters: it is the exact shape the
# tree carried before #1198 (`- "5435:5432"`), and if the guard ever stops failing on it, the
# guard is decoration.
#
# THREE OUTCOMES, ASSERTED SEPARATELY AND NEVER COLLAPSED:
#   exit 0 — read, and loopback-bound.
#   exit 1 — read, and NOT loopback-bound. Names the service and the port.
#   exit 2 — could not answer. "The guard could not run" must never be indistinguishable
#            from "the guard passed", which is why every refusal below asserts 2 and not 1.
#
# SECTION 2 IS THE POINT OF THE REBUILD. Every fixture there was exit 2 under the awk parser
# — a refusal that told the reader nothing — and is exit 1 now, naming the offending service
# and port. Those are the spellings that cost four review rounds (#1206).
#
# SECTION 4 IS ITS COUNTERWEIGHT: what the guard still refuses, and one class it refuses on
# purpose even though refusing UNDER-claims. `[0:0:0:0:0:0:0:1]` IS loopback and compose does
# not normalise it, so calling it a violation would assert a fact the guard has not
# established — #1198's own defect one level down.
#
# SECTION 7 ASKS THE OTHER QUESTION ENTIRELY, and it is the one that had live fail-opens on
# `main`: not "did the guard read this binding right" but "is the artefact it read the one
# that runs". Sections 1–6 would all have passed against a guard pointed at a dead file.
#
# EVERY FIXTURE IS A DIRECTORY, not a file, because the guard's unit is a compose PROJECT —
# see `proj` below. `real_repo_project` pins the delivery itself rather than a fixture of it,
# so a future edit that reintroduces a 0.0.0.0 binding fails here and not only in review.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/compose-loopback-guard.sh"
[ -f "$SUT" ] || { echo "missing script under test: $SUT" >&2; exit 1; }
REPO_ROOT=$(cd -- "$script_dir/../.." && pwd)
readonly REPO_ROOT

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

pass=0
fail=0

# proj <name> — writes stdin to $TMPROOT/<name>/docker-compose.yml and leaves the directory
# for a `run` line to hand the guard.
#
# A FIXTURE IS A DIRECTORY, because the guard's unit is a compose PROJECT and not a compose
# file. That is not decoration: `docker compose` resolves a project from a directory, merging
# an auto-loaded override and honouring its own file precedence, and a suite that handed the
# guard loose files would exercise an entry point production no longer has. It also means
# every fixture below re-proves the reading predicate THROUGH the new selection path rather
# than beside it.
proj() {
  mkdir -p "$TMPROOT/$1"
  cat >"$TMPROOT/$1/docker-compose.yml"
}

# run <name> <expected-exit> <arg...>
run() {
  local name=$1 expected=$2; shift 2
  local actual=0
  bash "$SUT" "$@" >"$TMPROOT/out.txt" 2>&1 || actual=$?
  if [ "$actual" -eq "$expected" ]; then
    pass=$((pass + 1))
    printf 'ok   %-38s (exit %d)\n' "$name" "$actual"
  else
    fail=$((fail + 1))
    printf 'FAIL %-38s expected exit %d, got %d\n' "$name" "$expected" "$actual"
    sed 's/^/       | /' "$TMPROOT/out.txt"
  fi
}

# run_lines <name> <expected-exit> <expected-count> <regex> <arg...>
# Asserts on the OUTPUT, not only the exit code. Needed because the multi-file accumulator's
# failure mode was invisible to an exit-code assertion: fused lines kept the exit code and
# only corrupted the message.
run_lines() {
  local name=$1 expected=$2 count=$3 regex=$4; shift 4
  local actual=0 got
  bash "$SUT" "$@" >"$TMPROOT/out.txt" 2>&1 || actual=$?
  got=$(grep -cE "$regex" "$TMPROOT/out.txt" || true)
  if [ "$actual" -eq "$expected" ] && [ "$got" -eq "$count" ]; then
    pass=$((pass + 1))
    printf 'ok   %-38s (exit %d, %d line(s))\n' "$name" "$actual" "$got"
  else
    fail=$((fail + 1))
    printf 'FAIL %-38s expected exit %d + %d line(s), got exit %d + %d\n' "$name" "$expected" "$count" "$actual" "$got"
    sed 's/^/       | /' "$TMPROOT/out.txt"
  fi
}

# ==========================================================================================
# 1. THE CORE PREDICATE — both halves, in both polarities
# ==========================================================================================

proj clean <<'YAML'
services:
  a:
    image: x
    ports:
      - "127.0.0.1:5435:5432"
      - "127.0.0.1:5341:80"     # trailing comment
    volumes:
      - v:/data
  b:
    image: y
    ports:
      - "127.0.0.1:6379:6379"
YAML
run clean 0 "$TMPROOT/clean"

# THE COUNTERFACTUAL. A bare HOST:CONTAINER binds every interface. This is the exact form the
# tree carried for months while its comment claimed loopback. Compose emits NO `host_ip` key
# for it rather than `0.0.0.0` — so absence is the violation, and a guard checking only the
# VALUE passes this silently.
proj bare <<'YAML'
services:
  a:
    image: x
    ports:
      - "5435:5432"
YAML
run bare_mapping 1 "$TMPROOT/bare"

# The other half. `0.0.0.0` IS emitted, present and wide — so a guard checking only PRESENCE
# passes this one. Both halves or neither.
proj explicit <<'YAML'
services:
  a:
    image: x
    ports:
      - "0.0.0.0:5435:5432"
YAML
run explicit_wildcard 1 "$TMPROOT/explicit"

proj unquoted <<'YAML'
services:
  a:
    image: x
    ports:
      - 5435:5432
YAML
run unquoted_bare 1 "$TMPROOT/unquoted"

# One bad entry among good ones is not masked by its neighbours.
proj mixed <<'YAML'
services:
  a:
    image: x
    ports:
      - "127.0.0.1:5341:80"
      - "5342:5341"
      - "127.0.0.1:6379:6379"
YAML
run one_bad_among_good 1 "$TMPROOT/mixed"

# A routable address is a violation, not a refusal: the guard decides IPv4 in full.
proj lan <<'YAML'
services:
  a:
    image: x
    ports:
      - "192.168.1.5:9100:9100"
YAML
run routable_ipv4 1 "$TMPROOT/lan"

# 127.0.0.0/8 is loopback (RFC 1122), so the guard passes it. It is not the house spelling,
# but the guard checks the security property, not the convention.
proj lo8 <<'YAML'
services:
  a:
    image: x
    ports:
      - "127.0.0.2:8000:8000"
YAML
run loopback_127_8_ok 0 "$TMPROOT/lo8"

# THE CONTAINER PORT ALONE is not a bare HOST:CONTAINER and does not have its fix. Compose
# emits neither `published` nor `host_ip`, and the result publishes on a RANDOM host port
# across every interface — so exit 1 is right, and the remedy is `127.0.0.1::5435`, which
# keeps the random port and binds it to loopback. Measured, both forms and the remedy.
proj eph <<'YAML'
services:
  a:
    image: x
    ports:
      - "5435"
  b:
    image: y
    ports:
      - target: 5432
YAML
run_lines ephemeral_port_is_not_loopback 1 2 'published=<ephemeral> host_ip=<absent>' "$TMPROOT/eph"

proj eph_ok <<'YAML'
services:
  c:
    image: z
    ports:
      - "127.0.0.1::5435"
YAML
run ephemeral_bound_to_loopback_ok 0 "$TMPROOT/eph_ok"

proj v6 <<'YAML'
services:
  a:
    image: x
    ports:
      - "[::1]:5435:5432"
YAML
run ipv6_loopback_ok 0 "$TMPROOT/v6"

# Volumes, commands and healthchecks are lists too. Under the awk parser this needed
# indentation bookkeeping; compose's model makes it structural. Pinned anyway — with a floor,
# so "it did not read the volume as a port" is asserted rather than assumed.
proj lists <<'YAML'
services:
  a:
    image: x
    ports:
      - "127.0.0.1:5435:5432"
    volumes:
      - jobbliggaren_data:/var/lib/postgresql
    healthcheck:
      test: ["CMD", "true"]
  b:
    image: y
    command:
      - "--flag=1"
YAML
run other_lists_are_not_ports 0 --expect-min 1 "$TMPROOT/lists"
run other_lists_do_not_inflate 1 --expect-min 2 "$TMPROOT/lists"

proj comments <<'YAML'
services:
  a:
    image: x
    ports:
      # BIND-ADRESSEN ÄR LASTBÄRANDE — se vps-base-hardening.md §12
      # - "5435:5432"   <- an example inside a comment must not be read as an entry
      - "127.0.0.1:5435:5432"
YAML
run comments_ignored 0 "$TMPROOT/comments"

# The repo default is core.autocrlf=true.
mkdir -p "$TMPROOT/crlf"
printf 'services:\r\n  a:\r\n    image: x\r\n    ports:\r\n      - "5435:5432"\r\n' >"$TMPROOT/crlf/docker-compose.yml"
run crlf_bare_fails 1 "$TMPROOT/crlf"

# ==========================================================================================
# 2. THE REBUILD'S POINT — seven spellings that were REFUSED (exit 2) and are now CAUGHT
#
# Each of these cost a review round against the awk parser (#1206), and each was left as a
# refusal because teaching a hand-written YAML parser one more spelling never closed the
# class. Compose normalises all seven to one shape before the guard sees them. Both polarities
# in BOTH polarities — a guard that fails everything is as useless as one that passes
# everything, and every one of these seven has a writable clean counterpart, so every one gets
# it. An earlier version of this comment said "both polarities where a clean counterpart is
# writable" while three of them (alias, include, extends) carried only the negative: the
# hedge was doing the work the fixtures were supposed to do.
# ==========================================================================================

proj longform_bad <<'YAML'
services:
  a:
    image: x
    ports:
      - target: 5432
        published: 5435
YAML
run longform_mapping_bare 1 "$TMPROOT/longform_bad"

proj longform_ok <<'YAML'
services:
  a:
    image: x
    ports:
      - target: 5432
        published: 5435
        host_ip: 127.0.0.1
YAML
run longform_mapping_loopback 0 "$TMPROOT/longform_ok"

# Live house idiom — e2e.yml uses the flow form.
proj flow_bad <<'YAML'
services:
  a:
    image: x
    ports: ["5435:5432"]
YAML
run flow_sequence_bare 1 "$TMPROOT/flow_bad"

proj flow_ok <<'YAML'
services:
  a:
    image: x
    ports: ["127.0.0.1:5435:5432"]
YAML
run flow_sequence_loopback 0 "$TMPROOT/flow_ok"

proj qkey_bad <<'YAML'
services:
  a:
    image: x
    "ports":
      - "0.0.0.0:5341:80"
YAML
run quoted_key_bare 1 "$TMPROOT/qkey_bad"

proj qkey_ok <<'YAML'
services:
  a:
    image: x
    'ports':
      - "127.0.0.1:5341:80"
YAML
run quoted_key_loopback 0 "$TMPROOT/qkey_ok"

# YAML explicit-key syntax puts the colon on the NEXT line, so no colon-anchored key test
# could ever fire. It took a second, deliberately loose divergence detector to make the awk
# parser merely REFUSE it.
proj ekey_bad <<'YAML'
services:
  a:
    image: x
    ? ports
    : - "0.0.0.0:9999:9999"
YAML
run explicit_key_syntax_bare 1 "$TMPROOT/ekey_bad"

proj ekey_ok <<'YAML'
services:
  a:
    image: x
    ? ports
    : - "127.0.0.1:9999:9999"
YAML
run explicit_key_syntax_loopback 0 "$TMPROOT/ekey_ok"

proj alias <<'YAML'
x-ports: &p
  - "5435:5432"
services:
  a:
    image: x
    ports: *p
YAML
run anchor_alias_bare 1 "$TMPROOT/alias"

proj alias_ok <<'YAML'
x-ports: &p
  - "127.0.0.1:5435:5432"
services:
  a:
    image: x
    ports: *p
YAML
run anchor_alias_loopback 0 "$TMPROOT/alias_ok"

# `[::]` is the IPv6 wildcard, present and wide. The awk parser refused every bracketed form
# that was not literally `[::1]:`, so this arrived as a refusal rather than a finding.
proj v6wild <<'YAML'
services:
  a:
    image: x
    ports:
      - "[::]:6379:6379"
YAML
run ipv6_wildcard_caught 1 "$TMPROOT/v6wild"

# `include:` and `extends:` pull ports in from another file. The awk parser could only report
# PORTS-OUT-OF-VIEW — the ports genuinely were not in the file it was reading. Compose
# resolves both and inlines the ports, so they are now CHECKED, and a 0.0.0.0 binding hiding
# in an included file is caught — provided the including file is one the guard is given.
#
# DO NOT READ THIS AS #196 COVERAGE. An earlier version of this comment cited #196 as the
# beneficiary, which overstated the wiring: the guard resolves an included file only when the
# PROJECT it was pointed at reaches it, and a deploy compose file added by #196 lives in a
# directory of its own that nothing here points at. #196's file is gated by the tripwire at
# the bottom of this suite, which refuses to let it arrive unjudged — not by these fixtures.
# The `base.yml` files below are not compose default names, so they are reached only through
# the `include:`/`extends:` keys under test and never as a project's own base file.
proj inc_bad <<'YAML'
include:
  - base.yml
services:
  a:
    image: x
    ports:
      - "127.0.0.1:5435:5432"
YAML
cat >"$TMPROOT/inc_bad/base.yml" <<'YAML'
services:
  frombase:
    image: b
    ports:
      - "0.0.0.0:7777:7777"
YAML
run include_is_resolved_and_checked 1 "$TMPROOT/inc_bad"

proj ext_bad <<'YAML'
services:
  a:
    image: x
    extends:
      file: base.yml
      service: frombase
YAML
cat >"$TMPROOT/ext_bad/base.yml" <<'YAML'
services:
  frombase:
    image: b
    ports:
      - "0.0.0.0:7777:7777"
YAML
run extends_is_resolved_and_checked 1 "$TMPROOT/ext_bad"

# The clean counterparts. Without them these two pin only that the resolution path can FAIL,
# which a path that always failed would satisfy too.
proj inc_ok <<'YAML'
include:
  - base_ok.yml
services:
  a:
    image: x
    ports:
      - "127.0.0.1:5435:5432"
YAML
cat >"$TMPROOT/inc_ok/base_ok.yml" <<'YAML'
services:
  frombase_ok:
    image: b
    ports:
      - "127.0.0.1:7777:7777"
YAML
run include_resolved_loopback 0 --expect-min 2 "$TMPROOT/inc_ok"

proj ext_ok <<'YAML'
services:
  a:
    image: x
    extends:
      file: base_ok.yml
      service: frombase_ok
YAML
cat >"$TMPROOT/ext_ok/base_ok.yml" <<'YAML'
services:
  frombase_ok:
    image: b
    ports:
      - "127.0.0.1:7777:7777"
YAML
run extends_resolved_loopback 0 --expect-min 1 "$TMPROOT/ext_ok"

# ==========================================================================================
# 3. THE UNIT OF --expect-min CHANGED, and it is pinned rather than left as prose
#
# Compose expands a range into one entry per published port; the awk parser counted the
# written list item, i.e. one. A floor inherited from the old unit would therefore be wrong
# by the width of every range in the file.
# ==========================================================================================

proj range <<'YAML'
services:
  a:
    image: x
    ports:
      - "127.0.0.1:8000-8002:8000-8002"
YAML
run range_counts_three 0 --expect-min 3 "$TMPROOT/range"
run range_is_not_one_entry 1 --expect-min 4 "$TMPROOT/range"

# ==========================================================================================
# 4. WHAT THE GUARD STILL REFUSES — and the class it refuses on purpose
# ==========================================================================================

# Host networking publishes every port the process binds, through no `ports:` key at all. A
# ports check is structurally blind to it, so it is refused. This is the one refusal class the
# rebuild does NOT retire.
proj hostnet <<'YAML'
services:
  a:
    image: x
    network_mode: host
    ports:
      - "127.0.0.1:5435:5432"
YAML
run host_networking_refused 2 "$TMPROOT/hostnet"

# THE SECOND ROUTE TO HOST NETWORKING, and it carries no `network_mode` key at all. A service
# attached to a top-level network that resolves to the Docker network named `host` gets host
# networking; the model shows only `networks: {hostnet: null}`. Measured: the awk predecessor
# on cf642a71 exits 0 on this same file, so the gap predates the rewrite — it is closed here
# because this suite's guard claims host networking is refused.
proj hostnet_ext <<'YAML'
networks:
  hostnet:
    external: true
    name: host
services:
  a:
    image: x
    networks: [hostnet]
    ports:
      - "127.0.0.1:5435:5432"
YAML
run host_network_via_external_refused 2 "$TMPROOT/hostnet_ext"

# …and compose fills `name` in from the key when it is omitted, so the short spelling is the
# same finding and must not need a second rule.
proj hostnet_key <<'YAML'
networks:
  host:
    external: true
services:
  a:
    image: x
    networks: [host]
    ports:
      - "127.0.0.1:5435:5432"
YAML
run host_network_short_spelling_refused 2 "$TMPROOT/hostnet_key"

# The counterweight: an ordinary named network must NOT be mistaken for the host network.
proj ordinary_net <<'YAML'
networks:
  backend:
    external: true
services:
  a:
    image: x
    networks: [backend]
    ports:
      - "127.0.0.1:5435:5432"
YAML
run ordinary_network_not_refused 0 "$TMPROOT/ordinary_net"

# ...and a network key literally called `host` WITHOUT `external:` is a project-scoped bridge
# network that merely shares the name. Compose resolves it to `<project>_host`, so it must not
# be refused — the rule keys on the RESOLVED name, not on the key.
proj local_host_net <<'YAML'
networks:
  host: {}
services:
  a:
    image: x
    networks: [host]
    ports:
      - "127.0.0.1:5435:5432"
YAML
run local_network_named_host_ok 0 "$TMPROOT/local_host_net"

# THE NAME LOOKUP ONLY WORKS ON A NAME COMPOSE RESOLVED. Under `--no-interpolate` a network
# written `name: "${HOSTNET}"` keeps the raw string, so a literal comparison cannot see that
# it becomes `host` at `up` time — measured, the guard exited 0 on exactly this file while
# `docker compose config` with the variable set returns `name: "host"`. Refused as
# UNRESOLVED-NETWORK-NAME, which is the ports axis's UNRESOLVED-ENTRY rule one noun over.
proj varnet <<'YAML'
networks:
  n:
    external: true
    name: "${HOSTNET}"
services:
  a:
    image: x
    networks: [n]
    ports:
      - "127.0.0.1:5435:5432"
YAML
run unresolved_network_name_refused 2 "$TMPROOT/varnet"

# SEAT 2 — the service's OWN network_mode. Same defeat, and this seat outlived the first
# repair by a round: the fix went where the finding pointed instead of where the class lives.
for spelling in '${NETMODE}' '$NETMODE' '${NETMODE:-host}'; do
  proj varmode <<YAML
services:
  a:
    image: x
    network_mode: "$spelling"
    ports:
      - "127.0.0.1:5435:5432"
YAML
  run "unresolved_network_mode_refused[$spelling]" 2 "$TMPROOT/varmode"
done

# `\${NETMODE:-host}` above is the one that matters most: it names `host` as its own DEFAULT,
# so the file gives host networking with no variable set at all — and it read as clean.

# SEAT 3 — which network is joined. The host network is declared and readable; the
# ATTACHMENT is not, so the guard cannot tell whether this service joins it.
proj varref <<'YAML'
networks:
  hostnet:
    external: true
    name: host
services:
  a:
    image: x
    networks: ["${NET}"]
    ports:
      - "127.0.0.1:5435:5432"
YAML
run unresolved_network_ref_refused 2 "$TMPROOT/varref"

# THE COUNTERWEIGHTS FOR SEAT 2. Without these the rule could drift into refusing every
# `network_mode`, which would fail every legitimate file. All three are resolved literals and
# none is host networking.
for mode in bridge none 'service:api'; do
  proj plainmode <<YAML
services:
  api:
    image: y
    ports:
      - "127.0.0.1:8080:8080"
  a:
    image: x
    network_mode: "$mode"
YAML
  run "resolved_network_mode_ok[$mode]" 0 "$TMPROOT/plainmode"
done

# The counterweight: an unresolved network name nobody is attached to publishes nothing, so
# it must not be refused. Without this the rule could drift into refusing any variable.
proj varnet_unused <<'YAML'
networks:
  n:
    external: true
    name: "${HOSTNET}"
services:
  a:
    image: x
    ports:
      - "127.0.0.1:5435:5432"
YAML
run unresolved_network_unattached_ok 0 "$TMPROOT/varnet_unused"

# `--no-interpolate` does NOT normalise an entry carrying an unexpanded variable — measured:
# it comes back as the raw string, so the model is not uniformly a mapping. The guard cannot
# read a bind address it has not seen expanded.
proj var <<'YAML'
services:
  a:
    image: x
    ports:
      - "127.0.0.1:${HOST_PORT}:5432"
YAML
run unexpanded_variable_refused 2 "$TMPROOT/var"

# The same raw-string class, reached without any variable at all: a hostname bind address.
# Named separately because "unresolved" reads as "interpolation" and this is not that.
proj hostname <<'YAML'
services:
  a:
    image: x
    ports:
      - "localhost:9000:9000"
YAML
run hostname_bind_refused 2 "$TMPROOT/hostname"

# THE HONEST REFUSAL. `[0:0:0:0:0:0:0:1]` IS loopback, and compose does not normalise the
# address — it strips the brackets and hands back `0:0:0:0:0:0:0:1`. Reporting that as "not
# bound to loopback" would assert a fact the guard has not established, which is exactly the
# class #1198 was. Exit 2 says "I will not judge this spelling"; exit 1 would be a false
# statement about a correct binding.
proj v6full <<'YAML'
services:
  a:
    image: x
    ports:
      - "[0:0:0:0:0:0:0:1]:5435:5432"
YAML
run ipv6_fullform_unjudged 2 "$TMPROOT/v6full"

# The same refusal UNDER-claims here — `fe80::1` is link-local, not loopback, so this could in
# principle be exit 1. It is exit 2 because the guard decides IPv6 in two spellings only, and
# a rule that judged this one would have to judge the fixture above too. Both fail the build;
# only one of the two possible errors is a false claim, and this is the side that avoids it.
proj v6ll <<'YAML'
services:
  a:
    image: x
    ports:
      - "[fe80::1]:9200:9200"
YAML
run ipv6_linklocal_unjudged 2 "$TMPROOT/v6ll"

# ==========================================================================================
# 5. THE GUARD CANNOT REPORT CLEAN WHEN IT READ NOTHING
# ==========================================================================================

proj noports <<'YAML'
services:
  a:
    image: x
YAML
run zero_recognised_refused 2 "$TMPROOT/noports"
run zero_allowed_explicitly 0 --expect-min 0 "$TMPROOT/noports"

# A file compose itself rejects is a refusal, never a pass. The awk parser had its own
# EMPTY-PORTS-BLOCK class for `ports:` with nothing under it; compose rejects that outright
# ("services.a.ports must be a array"), so the class moved into the tool.
proj emptyports <<'YAML'
services:
  a:
    image: x
    ports:
  b:
    image: y
YAML
run compose_refuses_empty_ports 2 "$TMPROOT/emptyports"

mkdir -p "$TMPROOT/broken"
printf 'services:\n  a:\n   image: x\n  ports: [\n' >"$TMPROOT/broken/docker-compose.yml"
run compose_refuses_broken_yaml 2 "$TMPROOT/broken"

# ==========================================================================================
# 6. THE CLASSIFIER MUST NOT LOSE ITS ANSWER TO A DEAD PRODUCER
#
# `grep -q` leaves at its first match. Behind a PIPE, a producer still holding more than a
# pipe buffer dies of SIGPIPE, `pipefail` makes 141 the pipeline status, the `if` goes false,
# and the guard falls through to the exit-1 branch — announcing "not bound to loopback" about
# entries it had only failed to read. That was #1206's Blocker; the fix is the herestring, and
# this fixture is what makes its removal fail.
#
# WRITTEN AT 4 000 BECAUSE THE PROPERTY IS ONLY DECIDABLE THERE. At fixture scale the same
# defect is load-dependent (0 of 150 runs on a quiet machine); above a pipe buffer it is
# deterministic. 4 000 entries produce ~220 kB of refusal lines.
#
# NO `--expect-min 0` HERE, deliberately. Its predecessor carried one, and it was inert: the
# classifier exits 2 long before any floor is evaluated. A flag that cannot change the outcome
# reads as part of the setup and is not.
mkdir -p "$TMPROOT/bigrefuse"
{
  printf 'services:\n  s:\n    image: x\n    ports:\n'
  i=1; while [ "$i" -le 4000 ]; do printf '      - "127.0.0.1:${P%s}:5432"\n' "$i"; i=$((i + 1)); done
} >"$TMPROOT/bigrefuse/docker-compose.yml"
run classifier_survives_large_refusal_list 2 "$TMPROOT/bigrefuse"

# ==========================================================================================
# 7. WHICH PROJECT IS JUDGED — the half the guard used to get wrong
#
# Every fixture above asks "does the guard read this binding correctly". These ask the other
# question, and it is the one that had two live fail-opens on `main`: IS THE ARTEFACT IT READ
# THE ONE THAT RUNS. Under the old `-f <path>` the answer was no in two measured ways, and
# compose's own resolution introduced a third hazard in the other direction the moment the
# `-f` came off.
#
# THE NEGATIVE HALF IS WHAT CARRIES THESE. A pair where the wide port sits in the file the
# guard used to read proves nothing — it would pass under either semantics. The load-bearing
# fixtures are the ones where the two semantics DISAGREE: the wide port is in the file the old
# guard could not see, or the clean bill is in the file compose no longer reads.
# ==========================================================================================

# GAP 1 — `docker compose up` auto-loads a sibling override, and `-f` suppressed it. Measured
# 2026-08-05: base loopback-bound + this override published `0.0.0.0:9999` and the guard said
# `OK — 1 published port(s), all loopback-bound`, exit 0. Older than the config rewrite; the
# awk predecessor exits 0 on it too. The override file is NOT gitignored, so it was committable.
proj override_wide <<'YAML'
services:
  a:
    image: x
    ports:
      - "127.0.0.1:5435:5432"
YAML
cat >"$TMPROOT/override_wide/docker-compose.override.yml" <<'YAML'
services:
  b:
    image: y
    ports:
      - "0.0.0.0:9999:9999"
YAML
run_lines override_is_loaded_and_judged 1 1 ': NOT-LOOPBACK service=b published=9999' "$TMPROOT/override_wide"

# The counterweight, and its floor is the assertion. Without `--expect-min 2` this fixture is
# satisfied by a guard that never opened the override at all — the same vacuous exit 0 the
# gap consisted of. The floor makes it cross: the override's port has to be COUNTED.
proj override_clean <<'YAML'
services:
  a:
    image: x
    ports:
      - "127.0.0.1:5435:5432"
YAML
cat >"$TMPROOT/override_clean/docker-compose.override.yml" <<'YAML'
services:
  b:
    image: y
    ports:
      - "127.0.0.1:9999:9999"
YAML
run override_clean_is_counted 0 --expect-min 2 "$TMPROOT/override_clean"

# GAP 2 — a committed `compose.yaml` OUTRANKS `docker-compose.yml` in compose's own file
# precedence. Measured: with both present, bare `docker compose config` reports only the
# shadowing file's services while the old guard reported OK on the one nobody runs.
proj shadowed_clean <<'YAML'
services:
  ignored:
    image: x
    ports:
      - "127.0.0.1:5435:5432"
YAML
cat >"$TMPROOT/shadowed_clean/compose.yaml" <<'YAML'
services:
  shadow:
    image: x
    ports:
      - "0.0.0.0:7777:7777"
YAML
run_lines shadowing_file_is_judged 1 1 ': NOT-LOOPBACK service=shadow published=7777' "$TMPROOT/shadowed_clean"

# THE SHARPER POLARITY, and it is the one that discriminates the two semantics outright: the
# wide binding is in `docker-compose.yml` — the file the OLD guard was hardcoded to read — and
# `compose.yaml` shadows it clean. The old guard fails this file; the new one passes it,
# because compose does not read the shadowed file and neither should its guard. A suite that
# only had the fixture above would be satisfied by a guard that read BOTH files.
proj shadowed_wide <<'YAML'
services:
  ignored:
    image: x
    ports:
      - "0.0.0.0:5435:5432"
YAML
cat >"$TMPROOT/shadowed_wide/compose.yaml" <<'YAML'
services:
  shadow:
    image: x
    ports:
      - "127.0.0.1:7777:7777"
YAML
run shadowed_file_is_not_judged 0 --expect-min 1 "$TMPROOT/shadowed_wide"

# ALL FOUR DEFAULT BASE NAMES RESOLVE, and this is a counterweight to the guard's own
# pre-flight rather than to compose. The guard enumerates the four names itself to decide
# whether a directory has a compose file of its own; an enumeration narrower than compose's
# would refuse a legitimate project, and one wider would let the upward walk through. Each is
# written WIDE so the fixture fails if the file was never read at all.
for base in compose.yaml compose.yml docker-compose.yaml docker-compose.yml; do
  mkdir -p "$TMPROOT/basename_$base"
  cat >"$TMPROOT/basename_$base/$base" <<'YAML'
services:
  a:
    image: x
    ports:
      - "0.0.0.0:5435:5432"
YAML
  run "default_base_name_resolves[$base]" 1 "$TMPROOT/basename_$base"
done

# THE HAZARD PROJECT SEMANTICS INTRODUCES, and the reason the guard checks those names at all:
# COMPOSE WALKS UP. Measured — `--project-directory <child with no compose file>` resolves the
# PARENT's file and names the project after the child. Without the pre-flight this fixture
# would inherit the parent's verdict, which is a clean bill of health for a project nobody
# asked about: the same defect one level up from the one the guard exists to close.
mkdir -p "$TMPROOT/walkup/empty_child"
cat >"$TMPROOT/walkup/docker-compose.yml" <<'YAML'
services:
  fromparent:
    image: x
    ports:
      - "127.0.0.1:5435:5432"
YAML
run parent_project_is_not_inherited 2 "$TMPROOT/walkup/empty_child"

# ...and an override ALONE does not stop the walk. Measured, and this one is worse than the
# empty child: compose loads the parent's base file and DROPS this override silently, so the
# wide port below appears in no model at all. `docker-compose.override.yml` is not one of the
# four base names for exactly this reason.
mkdir -p "$TMPROOT/walkup/only_override"
cat >"$TMPROOT/walkup/only_override/docker-compose.override.yml" <<'YAML'
services:
  orphan:
    image: y
    ports:
      - "0.0.0.0:9999:9999"
YAML
run override_alone_does_not_stop_the_walk 2 "$TMPROOT/walkup/only_override"

# THE ARGUMENT IS A DIRECTORY, AND COMPOSE WILL NOT SAY SO. Measured: `--project-directory`
# handed a FILE path exits 0 — compose reads it as a directory name, walks up to the parent
# and judges that project, naming it `docker-composeyml`. So a caller still passing the old
# file-path interface would receive a confident verdict about a different project.
#
# BOTH ASSERT THE MESSAGE, NOT ONLY THE EXIT CODE, and that is not belt-and-braces. Measured
# by mutation: with the `-d` branch deleted, both of these still exit 2 — the base-name check
# behind it refuses a path that holds none of compose's four default names, and a file and a
# missing path both hold none. So the exit code does not cross the `-d` branch at all, and a
# fixture asserting only the exit code would have gone green on a guard that no longer had it.
# What the branch actually buys is a true diagnosis: `no compose file in <…/docker-compose.yml>`
# is a strange thing to say about a path that IS a compose file, and it points the reader at
# the file's contents instead of at the interface they got wrong.
run_lines file_path_argument_refused 2 1 'not a directory: .*docker-compose\.yml' "$TMPROOT/clean/docker-compose.yml"

run_lines missing_dir 2 1 'not a directory: .*does-not-exist' "$TMPROOT/does-not-exist"

# UNTRACKED LOCAL STATE MUST NOT RESELECT THE FILE, and this is the hazard project semantics
# INTRODUCES rather than inherits: `COMPOSE_FILE` overrides a directory's own resolution, while
# the old `-f` form ignored it entirely (measured). Both fixtures point the guard at
# `$TMPROOT/bare`, which publishes `5435:5432` WIDE, while the redirection names a
# loopback-bound file. Before the repair each printed `OK — 1 published port(s), all
# loopback-bound` and exited 0 — the verdict describing the environment instead of the
# artefact, which is #1198's own shape.
#
# EXIT 1 IS THE ASSERTION, NOT EXIT 2. The guard neutralises rather than refuses, so the right
# answer is the wide project's own verdict, naming the wide project's own service. An exit-2
# assertion here would be satisfied by a guard that had merely stopped working.
export COMPOSE_FILE="$TMPROOT/clean/docker-compose.yml"
run_lines ambient_compose_file_does_not_reselect 1 1 ': NOT-LOOPBACK service=a published=5435' "$TMPROOT/bare"
unset COMPOSE_FILE

# THE SECOND SEAT, and it needs a different mechanism: compose reads the project's own `.env`
# for its OWN configuration, not only for interpolation, so clearing the environment does not
# reach it. THE PATH HERE IS ABSOLUTE ON PURPOSE — a relative value resolves against the
# CALLER'S CWD, so a relative fixture would pass without ever crossing the control, which is
# the likeliest way this repair ships untested.
#
# AND IT IS PLATFORM-SHAPED, which is the second likeliest way. Argument paths are rewritten
# for a Windows `docker.exe` on their way out of MSYS; the CONTENTS of `.env` are not, so a
# `/tmp/...` value there is read as `C:\tmp\...` and compose fails to find it. The fixture
# would still go red under mutation — 2 is as far from 1 as 0 is — but it would be crossing a
# path error instead of the false-clean verdict it exists to pin, on every developer machine.
# Measured 2026-08-05: with the value converted, the unrepaired guard reports
# `OK — all loopback-bound` and exits 0, on both platforms.
proj dotenv_wide <<'YAML'
services:
  a:
    image: x
    ports:
      - "5435:5432"
YAML
dotenv_target="$TMPROOT/clean/docker-compose.yml"
if command -v cygpath >/dev/null 2>&1; then
  dotenv_target=$(cygpath -m "$dotenv_target")
fi
printf 'COMPOSE_FILE=%s\n' "$dotenv_target" >"$TMPROOT/dotenv_wide/.env"
run_lines dotenv_compose_file_does_not_reselect 1 1 ': NOT-LOOPBACK service=a published=5435' "$TMPROOT/dotenv_wide"

# ...and neutralising never became refusing. `COMPOSE_PROJECT_NAME` is cleared by the same
# prefix rule though it cannot reach a port, so this pins that the guard still ANSWERS with an
# ambient variable set. Without it, a repair that swapped clearing for an exit-2 refusal would
# leave the two fixtures above green — they only assert that the redirection failed.
export COMPOSE_PROJECT_NAME=jbl_ambient_probe
run ambient_project_name_still_answers 0 "$TMPROOT/clean"
unset COMPOSE_PROJECT_NAME

# ==========================================================================================
# 8. THE DELIVERY ITSELF — not a fixture of the repo's project, the repo's project
# ==========================================================================================

run real_repo_project 0 "$REPO_ROOT"

# `exit 0` above is satisfied vacuously by a project with no ports or a deleted ports block.
# The floor makes the pin cross the threshold of the property it pins: the project publishes
# six ports today — including the two behind the `test` profile, which compose's model carries
# WITHOUT `--profile`, so their bindings are checked rather than merely asserted — and a
# restructure that hides them fails here instead of going green. Six is also the number the
# project resolves to, not just the number written in `docker-compose.yml`: measured
# 2026-08-05, the repo root has no override and no shadowing file, so the two are equal today
# and this fixture is what makes a future divergence visible.
run real_repo_project_floor 0 --expect-min 6 "$REPO_ROOT"

run floor_can_fail 1 --expect-min 99 "$REPO_ROOT"
run floor_accepts_equals_form 0 --expect-min=6 "$REPO_ROOT"

# ONE INVOCATION PER PROJECT: two arguments are two projects and must not be merged into one.
# (Merging WITHIN a project is now wanted — section 7 — but across projects it would answer a
# different question than "is each of these clean".) The `clean` project alone exits 0, so the
# order here is load-bearing.
run second_project_still_checked 1 "$TMPROOT/clean" "$TMPROOT/bare"

# ...and the accumulator must keep the two projects' findings on SEPARATE LINES. The fixture
# above cannot see this: only one of its projects carries a finding, so the accumulator's only
# stateful branch is never crossed. With the earlier `violations=$(printf '%s%s\n' …)` the
# command substitution stripped the trailing newline and the last finding of project 1 fused
# with the first of project 2 into one line naming two — with the exit code unchanged, which
# is exactly why an exit-code assertion could not pin it.
run_lines both_projects_report_separate_lines 1 2 '^.*: NOT-LOOPBACK ' "$TMPROOT/bare" "$TMPROOT/explicit"

# ...AND THE ACCUMULATOR NOW DECIDES THE EXIT CODE, WHICH IT DID NOT BEFORE. Under the old
# enumerating classifier, fusing two lines was harmless to the verdict — `grep -qE` matches
# substrings, so gluing could only ADD marker hits. Under the inverted predicate (#1216) it can
# SUBTRACT one: a refusal glued onto the end of a NOT-LOOPBACK finding yields a line containing
# `: NOT-LOOPBACK `, which `-v` excludes, so the refusal vanishes and a run that must exit 2
# exits 1 — the guard announcing "not bound to loopback" about a state it never read.
#
# Project 1 is a finding, project 2 is a refusal, and the refusal is what must survive. The
# fixture above cannot see this: both of its projects carry findings of the SAME kind, so no
# fusion of theirs could ever cross the predicate.
run refusal_survives_the_accumulator 2 "$TMPROOT/bare" "$TMPROOT/var"

# ==========================================================================================
# 9. THE TRIPWIRE — a compose file cannot arrive in this repo unjudged
#
# ITS REASON NARROWED WITH PROJECT SEMANTICS, and the narrowing is the point of reading this
# block again rather than skimming it. When the guard read one hardcoded FILE, this tripwire
# covered three things at once: a sibling `docker-compose.override.yml` the `-f` suppressed, a
# shadowing `compose.yaml` that outranked the gated file, and a compose file somewhere else in
# the tree. The guard now resolves the PROJECT (section 7), so the first two are no longer
# ungated — they are merged, or they shadow, and either way the verdict follows compose.
#
# WHAT IS LEFT IS STILL REAL, AND IT IS TWO THINGS, NOT NONE:
#   - A compose file in a directory NOBODY GATES. The suite points the guard at `$REPO_ROOT`
#     and at nothing else, so `deploy/docker-compose.yml` — #196's expected shape — is
#     unjudged no matter how well the root project is judged. This is the case the tripwire
#     exists for now.
#   - A root-level compose file COMPOSE DOES NOT AUTO-LOAD. `docker-compose.prod.yml` sits in
#     a gated directory and matches the pattern below, yet compose merges only a base file and
#     its override, so it too is unjudged. Being inside a gated project is NOT the same as
#     being read by it, and a tripwire keyed on the directory alone would have missed this.
#
# AND A THIRD ARM THAT IS NOT A GAP BUT STILL WANTS A HUMAN: a file the root project now DOES
# absorb changes what the gated verdict covers without `docker-compose.yml` changing at all.
# Going red there is correct — the three arms are indistinguishable by name, so the tripwire
# stops and asks rather than guessing which one arrived.
#
# IT IS STILL DELIBERATELY DUMBER THAN A PREDICATE: it asserts that the set of TRACKED compose
# files is the set this suite accounts for, and decides nothing about any file's contents.
#
# NOT #196'S GUARD. #196's file will legitimately publish 80 and 443 WIDE — a containerised
# Caddy must, or ACME HTTP-01 cannot complete — so this suite's loopback predicate is the
# wrong verdict for it and pointing it here would be wrong. It owes its own predicate. This
# block only refuses to let it pass unnoticed.
#
# THE PATTERN IS A NAME PATTERN AND CANNOT BE COMPLETE. It covers compose's own default names
# and the ordinary `-suffix`/`.suffix` variants; a file deliberately named something else
# (`stack.yml`) is invisible to it, as it is to compose without `-f`. The CTO's proposed form
# was measured to also match `composer-notes.yml`, so the suffix is anchored on `.` or `-`.
# IT ALSO OVER-REACHES, and that is the cheaper direction: `docs/compose-notes.yml` matches
# and is not a compose file. Tripping on one costs a line in ACCOUNTED_COMPOSE_FILES; missing
# a real one costs an unjudged file, so the pattern is deliberately generous.
readonly COMPOSE_FILE_PATTERN='(^|/)(docker-)?compose([.-][A-Za-z0-9_.-]+)?\.ya?ml$'
# ACCOUNTED, NOT GATED — the list was called GATED_COMPOSE_FILES while the guard read one file
# and the two words meant the same thing. Under project semantics they part company: a
# `docs/compose-notes.yml` or a `docker-compose.prod.yml` would belong on this list and be
# judged by nothing, so a name promising "gated" would vouch for a property the entry does not
# have. That is the defect class this whole guard exists to close, and it does not get an
# exemption for living in a variable name.
#
# Space-separated and `sort`-ordered, matching what compose_files_in emits. One entry today,
# and it IS gated (the root project resolves it); whoever adds the second is doing it under a
# red build, so the ordering rule is written here rather than left to be re-derived.
readonly ACCOUNTED_COMPOSE_FILES='docker-compose.yml'

# `|| true` IS LOAD-BEARING. `grep` exits 1 when nothing matches; under `set -o pipefail`
# that becomes the substitution's status and `set -e` kills the whole suite ON THIS LINE —
# no FAIL row, no gated/found comparison, none of the six diagnostic lines, and no summary.
# Measured: exit 1, zero output. The awk predecessor carried this exact lesson in a comment
# and it did not travel with the rewrite. The reachable trigger is the arm the message itself
# names: a compose file REMOVED or renamed out of the pattern.
compose_files_in() {
  printf '%s\n' "$1" | { grep -E "$COMPOSE_FILE_PATTERN" || true; } | sort | paste -sd' ' -
}

# assert_compose_set <name> <expected> <newline-separated paths>
# The three arms are asserted on SYNTHETIC input because the live block below can only ever
# exercise one of them — the repo has the files it has. `if ! got=$(...)` also catches the
# abort above and reports it, instead of the suite dying mid-run.
assert_compose_set() {
  local name=$1 expected=$2 input=$3 got=""
  if ! got=$(compose_files_in "$input"); then
    fail=$((fail + 1))
    printf 'FAIL %-38s the matcher ABORTED (grep found nothing and killed the pipeline)\n' "$name"
    return
  fi
  if [ "$got" = "$expected" ]; then
    pass=$((pass + 1))
    printf 'ok   %-38s ([%s])\n' "$name" "$got"
  else
    fail=$((fail + 1))
    printf 'FAIL %-38s expected [%s], got [%s]\n' "$name" "$expected" "$got"
  fi
}

assert_compose_set compose_matcher_empty_set '' 'src/a.cs
docs/b.md'
assert_compose_set compose_matcher_finds_accounted 'docker-compose.yml' 'docker-compose.yml
src/a.cs'
assert_compose_set compose_matcher_finds_arrival 'compose.yaml docker-compose.yml' 'docker-compose.yml
compose.yaml'
assert_compose_set compose_matcher_rejects_lookalikes '' 'docs/composer-notes.yml
pnpm-lock.yaml
.github/workflows/e2e.yml
src/Composer.cs
decompose.yml'

if ! tracked=$(git -C "$REPO_ROOT" ls-files 2>/dev/null); then
  fail=$((fail + 1))
  printf 'FAIL %-38s could not list tracked files (not a git repo?)\n' "compose_file_set_is_accounted"
else
  found=$(compose_files_in "$tracked")
  if [ "$found" = "$ACCOUNTED_COMPOSE_FILES" ]; then
    pass=$((pass + 1))
    printf 'ok   %-38s (%s)\n' "compose_file_set_is_accounted" "$found"
  else
    fail=$((fail + 1))
    printf 'FAIL %-38s tracked compose files changed\n' "compose_file_set_is_accounted"
    printf '       | accounted:  %s\n' "$ACCOUNTED_COMPOSE_FILES"
    printf '       | found:      %s\n' "$found"
    printf '       |\n'
    printf '       | A compose file was added, renamed or removed. Decide which of three it is\n'
    printf '       | before updating ACCOUNTED_COMPOSE_FILES:\n'
    printf '       |   1. The root project now MERGES it (an override) or is SHADOWED by it\n'
    printf '       |      (compose.yaml outranks docker-compose.yml). It is judged already --\n'
    printf '       |      confirm the root verdict still says what you meant, then list it.\n'
    printf '       |   2. It sits at the root but compose does NOT auto-load it\n'
    printf '       |      (docker-compose.prod.yml). Being in a gated directory is not being\n'
    printf '       |      read by it: nothing judges this one.\n'
    printf '       |   3. It is in a directory nothing points the guard at (deploy/). Also\n'
    printf '       |      unjudged, and this is the expected shape for #196.\n'
    printf '       |\n'
    printf '       | For 2 and 3 the loopback predicate may be the WRONG verdict anyway -- a\n'
    printf '       | reverse proxy must publish 80/443 wide, so #196 owes its own predicate.\n'
  fi
fi

echo
echo "compose-loopback-guard fixtures: $pass passed, $fail failed"
[ "$fail" -eq 0 ] || exit 1
