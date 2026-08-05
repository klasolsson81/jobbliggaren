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
# `real_repo_file` pins the delivery itself rather than a fixture of it, so a future edit that
# reintroduces a 0.0.0.0 binding fails here and not only in review.

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

cat >"$TMPROOT/clean.yml" <<'YAML'
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
run clean 0 "$TMPROOT/clean.yml"

# THE COUNTERFACTUAL. A bare HOST:CONTAINER binds every interface. This is the exact form the
# tree carried for months while its comment claimed loopback. Compose emits NO `host_ip` key
# for it rather than `0.0.0.0` — so absence is the violation, and a guard checking only the
# VALUE passes this silently.
cat >"$TMPROOT/bare.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "5435:5432"
YAML
run bare_mapping 1 "$TMPROOT/bare.yml"

# The other half. `0.0.0.0` IS emitted, present and wide — so a guard checking only PRESENCE
# passes this one. Both halves or neither.
cat >"$TMPROOT/explicit.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "0.0.0.0:5435:5432"
YAML
run explicit_wildcard 1 "$TMPROOT/explicit.yml"

cat >"$TMPROOT/unquoted.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - 5435:5432
YAML
run unquoted_bare 1 "$TMPROOT/unquoted.yml"

# One bad entry among good ones is not masked by its neighbours.
cat >"$TMPROOT/mixed.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "127.0.0.1:5341:80"
      - "5342:5341"
      - "127.0.0.1:6379:6379"
YAML
run one_bad_among_good 1 "$TMPROOT/mixed.yml"

# A routable address is a violation, not a refusal: the guard decides IPv4 in full.
cat >"$TMPROOT/lan.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "192.168.1.5:9100:9100"
YAML
run routable_ipv4 1 "$TMPROOT/lan.yml"

# 127.0.0.0/8 is loopback (RFC 1122), so the guard passes it. It is not the house spelling,
# but the guard checks the security property, not the convention.
cat >"$TMPROOT/lo8.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "127.0.0.2:8000:8000"
YAML
run loopback_127_8_ok 0 "$TMPROOT/lo8.yml"

# THE CONTAINER PORT ALONE is not a bare HOST:CONTAINER and does not have its fix. Compose
# emits neither `published` nor `host_ip`, and the result publishes on a RANDOM host port
# across every interface — so exit 1 is right, and the remedy is `127.0.0.1::5435`, which
# keeps the random port and binds it to loopback. Measured, both forms and the remedy.
cat >"$TMPROOT/eph.yml" <<'YAML'
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
run_lines ephemeral_port_is_not_loopback 1 2 'published=<ephemeral> host_ip=<absent>' "$TMPROOT/eph.yml"

cat >"$TMPROOT/eph_ok.yml" <<'YAML'
services:
  c:
    image: z
    ports:
      - "127.0.0.1::5435"
YAML
run ephemeral_bound_to_loopback_ok 0 "$TMPROOT/eph_ok.yml"

cat >"$TMPROOT/v6.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "[::1]:5435:5432"
YAML
run ipv6_loopback_ok 0 "$TMPROOT/v6.yml"

# Volumes, commands and healthchecks are lists too. Under the awk parser this needed
# indentation bookkeeping; compose's model makes it structural. Pinned anyway — with a floor,
# so "it did not read the volume as a port" is asserted rather than assumed.
cat >"$TMPROOT/lists.yml" <<'YAML'
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
run other_lists_are_not_ports 0 --expect-min 1 "$TMPROOT/lists.yml"
run other_lists_do_not_inflate 1 --expect-min 2 "$TMPROOT/lists.yml"

cat >"$TMPROOT/comments.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      # BIND-ADRESSEN ÄR LASTBÄRANDE — se vps-base-hardening.md §12
      # - "5435:5432"   <- an example inside a comment must not be read as an entry
      - "127.0.0.1:5435:5432"
YAML
run comments_ignored 0 "$TMPROOT/comments.yml"

# The repo default is core.autocrlf=true.
printf 'services:\r\n  a:\r\n    image: x\r\n    ports:\r\n      - "5435:5432"\r\n' >"$TMPROOT/crlf.yml"
run crlf_bare_fails 1 "$TMPROOT/crlf.yml"

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

cat >"$TMPROOT/longform_bad.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - target: 5432
        published: 5435
YAML
run longform_mapping_bare 1 "$TMPROOT/longform_bad.yml"

cat >"$TMPROOT/longform_ok.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - target: 5432
        published: 5435
        host_ip: 127.0.0.1
YAML
run longform_mapping_loopback 0 "$TMPROOT/longform_ok.yml"

# Live house idiom — e2e.yml uses the flow form.
cat >"$TMPROOT/flow_bad.yml" <<'YAML'
services:
  a:
    image: x
    ports: ["5435:5432"]
YAML
run flow_sequence_bare 1 "$TMPROOT/flow_bad.yml"

cat >"$TMPROOT/flow_ok.yml" <<'YAML'
services:
  a:
    image: x
    ports: ["127.0.0.1:5435:5432"]
YAML
run flow_sequence_loopback 0 "$TMPROOT/flow_ok.yml"

cat >"$TMPROOT/qkey_bad.yml" <<'YAML'
services:
  a:
    image: x
    "ports":
      - "0.0.0.0:5341:80"
YAML
run quoted_key_bare 1 "$TMPROOT/qkey_bad.yml"

cat >"$TMPROOT/qkey_ok.yml" <<'YAML'
services:
  a:
    image: x
    'ports':
      - "127.0.0.1:5341:80"
YAML
run quoted_key_loopback 0 "$TMPROOT/qkey_ok.yml"

# YAML explicit-key syntax puts the colon on the NEXT line, so no colon-anchored key test
# could ever fire. It took a second, deliberately loose divergence detector to make the awk
# parser merely REFUSE it.
cat >"$TMPROOT/ekey_bad.yml" <<'YAML'
services:
  a:
    image: x
    ? ports
    : - "0.0.0.0:9999:9999"
YAML
run explicit_key_syntax_bare 1 "$TMPROOT/ekey_bad.yml"

cat >"$TMPROOT/ekey_ok.yml" <<'YAML'
services:
  a:
    image: x
    ? ports
    : - "127.0.0.1:9999:9999"
YAML
run explicit_key_syntax_loopback 0 "$TMPROOT/ekey_ok.yml"

cat >"$TMPROOT/alias.yml" <<'YAML'
x-ports: &p
  - "5435:5432"
services:
  a:
    image: x
    ports: *p
YAML
run anchor_alias_bare 1 "$TMPROOT/alias.yml"

cat >"$TMPROOT/alias_ok.yml" <<'YAML'
x-ports: &p
  - "127.0.0.1:5435:5432"
services:
  a:
    image: x
    ports: *p
YAML
run anchor_alias_loopback 0 "$TMPROOT/alias_ok.yml"

# `[::]` is the IPv6 wildcard, present and wide. The awk parser refused every bracketed form
# that was not literally `[::1]:`, so this arrived as a refusal rather than a finding.
cat >"$TMPROOT/v6wild.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "[::]:6379:6379"
YAML
run ipv6_wildcard_caught 1 "$TMPROOT/v6wild.yml"

# `include:` and `extends:` pull ports in from another file. The awk parser could only report
# PORTS-OUT-OF-VIEW — the ports genuinely were not in the file it was reading. Compose
# resolves both and inlines the ports, so they are now CHECKED, and a 0.0.0.0 binding hiding
# in an included file is caught — provided the including file is one the guard is given.
#
# DO NOT READ THIS AS #196 COVERAGE. An earlier version of this comment cited #196 as the
# beneficiary, which overstated the wiring: the guard resolves an included file only when it
# is included FROM a file it was handed, and a deploy compose file added by #196 will not be
# included from `docker-compose.yml`. #196's file is gated by the tripwire at the bottom of
# this suite, which refuses to let it arrive unjudged — not by these two fixtures.
mkdir -p "$TMPROOT/inc"
cat >"$TMPROOT/inc/base.yml" <<'YAML'
services:
  frombase:
    image: b
    ports:
      - "0.0.0.0:7777:7777"
YAML
cat >"$TMPROOT/inc/included.yml" <<'YAML'
include:
  - base.yml
services:
  a:
    image: x
    ports:
      - "127.0.0.1:5435:5432"
YAML
run include_is_resolved_and_checked 1 "$TMPROOT/inc/included.yml"

cat >"$TMPROOT/inc/extended.yml" <<'YAML'
services:
  a:
    image: x
    extends:
      file: base.yml
      service: frombase
YAML
run extends_is_resolved_and_checked 1 "$TMPROOT/inc/extended.yml"

# The clean counterparts. Without them these two pin only that the resolution path can FAIL,
# which a path that always failed would satisfy too.
cat >"$TMPROOT/inc/base_ok.yml" <<'YAML'
services:
  frombase_ok:
    image: b
    ports:
      - "127.0.0.1:7777:7777"
YAML
cat >"$TMPROOT/inc/included_ok.yml" <<'YAML'
include:
  - base_ok.yml
services:
  a:
    image: x
    ports:
      - "127.0.0.1:5435:5432"
YAML
run include_resolved_loopback 0 --expect-min 2 "$TMPROOT/inc/included_ok.yml"

cat >"$TMPROOT/inc/extended_ok.yml" <<'YAML'
services:
  a:
    image: x
    extends:
      file: base_ok.yml
      service: frombase_ok
YAML
run extends_resolved_loopback 0 --expect-min 1 "$TMPROOT/inc/extended_ok.yml"

# ==========================================================================================
# 3. THE UNIT OF --expect-min CHANGED, and it is pinned rather than left as prose
#
# Compose expands a range into one entry per published port; the awk parser counted the
# written list item, i.e. one. A floor inherited from the old unit would therefore be wrong
# by the width of every range in the file.
# ==========================================================================================

cat >"$TMPROOT/range.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "127.0.0.1:8000-8002:8000-8002"
YAML
run range_counts_three 0 --expect-min 3 "$TMPROOT/range.yml"
run range_is_not_one_entry 1 --expect-min 4 "$TMPROOT/range.yml"

# ==========================================================================================
# 4. WHAT THE GUARD STILL REFUSES — and the class it refuses on purpose
# ==========================================================================================

# Host networking publishes every port the process binds, through no `ports:` key at all. A
# ports check is structurally blind to it, so it is refused. This is the one refusal class the
# rebuild does NOT retire.
cat >"$TMPROOT/hostnet.yml" <<'YAML'
services:
  a:
    image: x
    network_mode: host
    ports:
      - "127.0.0.1:5435:5432"
YAML
run host_networking_refused 2 "$TMPROOT/hostnet.yml"

# THE SECOND ROUTE TO HOST NETWORKING, and it carries no `network_mode` key at all. A service
# attached to a top-level network that resolves to the Docker network named `host` gets host
# networking; the model shows only `networks: {hostnet: null}`. Measured: the awk predecessor
# on cf642a71 exits 0 on this same file, so the gap predates the rewrite — it is closed here
# because this suite's guard claims host networking is refused.
cat >"$TMPROOT/hostnet_ext.yml" <<'YAML'
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
run host_network_via_external_refused 2 "$TMPROOT/hostnet_ext.yml"

# …and compose fills `name` in from the key when it is omitted, so the short spelling is the
# same finding and must not need a second rule.
cat >"$TMPROOT/hostnet_key.yml" <<'YAML'
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
run host_network_short_spelling_refused 2 "$TMPROOT/hostnet_key.yml"

# The counterweight: an ordinary named network must NOT be mistaken for the host network.
cat >"$TMPROOT/ordinary_net.yml" <<'YAML'
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
run ordinary_network_not_refused 0 "$TMPROOT/ordinary_net.yml"

# `--no-interpolate` does NOT normalise an entry carrying an unexpanded variable — measured:
# it comes back as the raw string, so the model is not uniformly a mapping. The guard cannot
# read a bind address it has not seen expanded.
cat >"$TMPROOT/var.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "127.0.0.1:${HOST_PORT}:5432"
YAML
run unexpanded_variable_refused 2 "$TMPROOT/var.yml"

# The same raw-string class, reached without any variable at all: a hostname bind address.
# Named separately because "unresolved" reads as "interpolation" and this is not that.
cat >"$TMPROOT/hostname.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "localhost:9000:9000"
YAML
run hostname_bind_refused 2 "$TMPROOT/hostname.yml"

# THE HONEST REFUSAL. `[0:0:0:0:0:0:0:1]` IS loopback, and compose does not normalise the
# address — it strips the brackets and hands back `0:0:0:0:0:0:0:1`. Reporting that as "not
# bound to loopback" would assert a fact the guard has not established, which is exactly the
# class #1198 was. Exit 2 says "I will not judge this spelling"; exit 1 would be a false
# statement about a correct binding.
cat >"$TMPROOT/v6full.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "[0:0:0:0:0:0:0:1]:5435:5432"
YAML
run ipv6_fullform_unjudged 2 "$TMPROOT/v6full.yml"

# The same refusal UNDER-claims here — `fe80::1` is link-local, not loopback, so this could in
# principle be exit 1. It is exit 2 because the guard decides IPv6 in two spellings only, and
# a rule that judged this one would have to judge the fixture above too. Both fail the build;
# only one of the two possible errors is a false claim, and this is the side that avoids it.
cat >"$TMPROOT/v6ll.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "[fe80::1]:9200:9200"
YAML
run ipv6_linklocal_unjudged 2 "$TMPROOT/v6ll.yml"

# ==========================================================================================
# 5. THE GUARD CANNOT REPORT CLEAN WHEN IT READ NOTHING
# ==========================================================================================

cat >"$TMPROOT/noports.yml" <<'YAML'
services:
  a:
    image: x
YAML
run zero_recognised_refused 2 "$TMPROOT/noports.yml"
run zero_allowed_explicitly 0 --expect-min 0 "$TMPROOT/noports.yml"

# A file compose itself rejects is a refusal, never a pass. The awk parser had its own
# EMPTY-PORTS-BLOCK class for `ports:` with nothing under it; compose rejects that outright
# ("services.a.ports must be a array"), so the class moved into the tool.
cat >"$TMPROOT/emptyports.yml" <<'YAML'
services:
  a:
    image: x
    ports:
  b:
    image: y
YAML
run compose_refuses_empty_ports 2 "$TMPROOT/emptyports.yml"

printf 'services:\n  a:\n   image: x\n  ports: [\n' >"$TMPROOT/broken.yml"
run compose_refuses_broken_yaml 2 "$TMPROOT/broken.yml"

run missing_file 2 "$TMPROOT/does-not-exist.yml"

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
{
  printf 'services:\n  s:\n    image: x\n    ports:\n'
  i=1; while [ "$i" -le 4000 ]; do printf '      - "127.0.0.1:${P%s}:5432"\n' "$i"; i=$((i + 1)); done
} >"$TMPROOT/bigrefuse.yml"
run classifier_survives_large_refusal_list 2 "$TMPROOT/bigrefuse.yml"

# ==========================================================================================
# 7. THE DELIVERY ITSELF — not a fixture of the file, the file
# ==========================================================================================

run real_repo_file 0 "$REPO_ROOT/docker-compose.yml"

# `exit 0` above is satisfied vacuously by a file with no ports or a deleted ports block. The
# floor makes the pin cross the threshold of the property it pins: the file publishes six
# ports today — including the two behind the `test` profile, which compose's model carries
# WITHOUT `--profile`, so their bindings are checked rather than merely asserted — and a
# restructure that hides them fails here instead of going green.
run real_repo_file_floor 0 --expect-min 6 "$REPO_ROOT/docker-compose.yml"

run floor_can_fail 1 --expect-min 99 "$REPO_ROOT/docker-compose.yml"
run floor_accepts_equals_form 0 --expect-min=6 "$REPO_ROOT/docker-compose.yml"

# One invocation per file: several `-f` in ONE compose invocation would MERGE them with
# override semantics, which answers a different question than "is each of these clean".
# `clean.yml` alone exits 0, so the order here is load-bearing.
run second_file_still_checked 1 "$TMPROOT/clean.yml" "$TMPROOT/bare.yml"

# ...and the accumulator must keep the two files' findings on SEPARATE LINES. The fixture
# above cannot see this: only one of its files carries a finding, so the accumulator's only
# stateful branch is never crossed. With the earlier `violations=$(printf '%s%s\n' …)` the
# command substitution stripped the trailing newline and the last finding of file 1 fused with
# the first of file 2 into one line naming two files — with the exit code unchanged, which is
# exactly why an exit-code assertion could not pin it.
run_lines both_files_report_separate_lines 1 2 '^.*: NOT-LOOPBACK ' "$TMPROOT/bare.yml" "$TMPROOT/explicit.yml"

# ==========================================================================================
# 8. THE TRIPWIRE — a compose file cannot arrive in this repo unjudged
#
# The guard is shape-based in how it reads a file and NAME-BASED in which file it reads: the
# suite hands it one hardcoded path. Compose's own project resolution is wider, in two ways
# both measured 2026-08-05 and both older than this rewrite (the awk predecessor exits 0 on
# each): `docker compose up` auto-loads a sibling `docker-compose.override.yml` that `-f`
# suppresses, and a committed `compose.yaml` OUTRANKS `docker-compose.yml` outright — bare
# `docker compose config` then reads only the shadowing file while the guard reports OK on
# the one nobody runs. Neither is fixed here: fixing them means judging a PROJECT rather than
# a FILE, which reverses the guard's own "ONE INVOCATION PER FILE, deliberately" and is a
# separate change (CTO 2026-08-05 — follow-up PR).
#
# WHAT THIS BLOCK DOES INSTEAD, and it is deliberately dumber than a predicate: it asserts
# that the set of TRACKED compose files is still the set the suite gates. It decides nothing
# about any file's contents. A new compose file — #196's deploy stack is the expected one —
# turns this RED the moment it is committed, so it cannot arrive silently ungated. That is
# what makes the follow-up unskippable rather than remembered: the build stops.
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
readonly COMPOSE_FILE_PATTERN='(^|/)(docker-)?compose([.-][A-Za-z0-9_.-]+)?\.ya?ml$'
readonly GATED_COMPOSE_FILES='docker-compose.yml'

if ! tracked=$(git -C "$REPO_ROOT" ls-files 2>/dev/null); then
  fail=$((fail + 1))
  printf 'FAIL %-38s could not list tracked files (not a git repo?)\n' "compose_file_set_is_gated"
else
  found=$(printf '%s\n' "$tracked" | grep -E "$COMPOSE_FILE_PATTERN" | sort | paste -sd' ' -)
  if [ "$found" = "$GATED_COMPOSE_FILES" ]; then
    pass=$((pass + 1))
    printf 'ok   %-38s (%s)\n' "compose_file_set_is_gated" "$found"
  else
    fail=$((fail + 1))
    printf 'FAIL %-38s tracked compose files changed\n' "compose_file_set_is_gated"
    printf '       | gated:  %s\n' "$GATED_COMPOSE_FILES"
    printf '       | found:  %s\n' "$found"
    printf '       |\n'
    printf '       | A compose file was added, renamed or removed. It is NOT gated by this\n'
    printf '       | suite, and compose may even PREFER it to the file that is (a committed\n'
    printf '       | compose.yaml outranks docker-compose.yml). Decide which verdict it owes\n'
    printf '       | before updating GATED_COMPOSE_FILES -- the loopback predicate is the\n'
    printf '       | WRONG verdict for a reverse proxy, which must publish 80/443 wide.\n'
  fi
fi

echo
echo "compose-loopback-guard fixtures: $pass passed, $fail failed"
[ "$fail" -eq 0 ] || exit 1
