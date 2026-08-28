#!/usr/bin/env bash
#
# Fixture tests for jobbliggaren-logprune.sh.
#
# Run:  bash deploy/systemd/jobbliggaren-logprune.test.sh
#
# NEEDS NO DAEMON, NO ROOT AND NO NETWORK. `docker` is stubbed on PATH and the container root is
# redirected into a temp tree, so what is measured is the script's own contract — which segments
# it selects, which it refuses to touch, and how it behaves when a read fails.
#
# THE PROPERTY THIS SUITE EXISTS FOR IS AN ABSENCE. The script must never delete the LIVE segment
# (`*-json.log`): that file is what `docker logs` serves, and `jobbliggaren-logship.sh` dies when
# `docker logs` exits non-zero, withholding its stamp and latching `logship-fresh`. A retention
# mechanism that can fell the off-box archive's freshness signal is worse than none, so "the live
# file is still there afterwards" is asserted in every case that prunes anything — an absence
# nobody asserts is an absence nobody notices when a glob is later relaxed.
#
# A GUARD NO GATE RUNS CANNOT FALL. This suite is wired into build.yml's `scripts` job beside the
# other fixture suites.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/jobbliggaren-logprune.sh"
[ -f "$SUT" ] || {
  echo "missing script under test: $SUT" >&2
  exit 1
}

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT
readonly BIN="$TMPROOT/bin"
readonly CROOT="$TMPROOT/containers"
mkdir -p "$BIN" "$CROOT"

pass=0
fail=0

# ── fixture integrity ────────────────────────────────────────────────────────────────────────
# BOTH DIRECTIONS, AND THE FIRST ONE IS WHY. An absence check alone is fail-OPEN: it cannot tell
# "the redirect applied" from "there was nothing to redirect", so a SUT that had lost its
# absolute docker path — or its real container root — would sail through it. The presence
# assertion before each sed is what makes the absence assertion after it mean anything.
readonly FIXTURE_SUT="$TMPROOT/logprune.sh"

grep -qF -- "/usr/bin/docker" "$SUT" || {
  echo "FIXTURE BROKEN: $SUT does not call docker by absolute path — the redirect below is vacuous" >&2
  exit 1
}
grep -qF -- "/var/lib/docker/containers" "$SUT" || {
  echo "FIXTURE BROKEN: $SUT does not name the real container root — the redirect below is vacuous" >&2
  exit 1
}

sed -e "s#/usr/bin/docker#docker#g" -e "s#/var/lib/docker/containers#${CROOT}#g" "$SUT" >"$FIXTURE_SUT"
chmod +x "$FIXTURE_SUT"

grep -qF -- "/usr/bin/docker" "$FIXTURE_SUT" && {
  echo "FIXTURE BROKEN: the docker redirect did not apply — the suite would call the real docker" >&2
  exit 1
}
grep -qF -- "/var/lib/docker/containers" "$FIXTURE_SUT" && {
  echo "FIXTURE BROKEN: the container-root redirect did not apply — the suite would read the real host" >&2
  exit 1
}

# ── docker stub ──────────────────────────────────────────────────────────────────────────────
# Maps a container name to an id via a file the cases write. An unmapped name exits non-zero,
# which is exactly what the real `docker inspect` does for a container that does not exist.
cat >"$BIN/docker" <<'STUB'
#!/usr/bin/env bash
# A down daemon fails EVERY subcommand, `inspect` included — and it fails `inspect` in exactly the
# shape a missing container does. That indistinguishability is the defect case 12 pins.
if [ -n "${DOCKER_STUB_DAEMON_DOWN:-}" ]; then
  echo "Cannot connect to the Docker daemon at unix:///var/run/docker.sock." >&2
  exit 1
fi
if [ "$1" = "version" ]; then echo "28.5.1"; exit 0; fi
if [ "$1" = "inspect" ] && [ "$2" = "-f" ] && [ "$3" = "{{.Id}}" ]; then
  name=$4
  if [ -f "$DOCKER_STUB_MAP/$name" ]; then cat "$DOCKER_STUB_MAP/$name"; exit 0; fi
  echo "Error: No such object: $name" >&2
  exit 1
fi
exit 0
STUB
chmod +x "$BIN/docker"
export DOCKER_STUB_MAP="$TMPROOT/map"
mkdir -p "$DOCKER_STUB_MAP"

# ── helpers ──────────────────────────────────────────────────────────────────────────────────
ts_days_ago() { date -u -d "$1 days ago" +%Y-%m-%dT%H:%M:%S.000000000Z; }

# One json-file line with a controlled timestamp.
jline() { printf '{"log":"%s\\n","stream":"stdout","time":"%s"}\n' "$2" "$1"; }

reset_world() {
  rm -rf "${CROOT:?}"/* "${DOCKER_STUB_MAP:?}"/*
  mkdir -p "$CROOT" "$DOCKER_STUB_MAP"
}

# Register a container with a 64-hex id and create its log directory.
mk_container() {
  local name=$1 id=$2
  printf '%s' "$id" >"$DOCKER_STUB_MAP/$name"
  mkdir -p "$CROOT/$id"
  printf '%s' "$CROOT/$id"
}

run_sut() { PATH="$BIN:$PATH" "$FIXTURE_SUT" "$@" 2>&1; }

check() {
  local label=$1 cond=$2
  if [ "$cond" = "1" ]; then
    pass=$((pass + 1)); echo "  ok   — $label"
  else
    fail=$((fail + 1)); echo "  FAIL — $label" >&2
  fi
}

readonly ID_API=$(printf 'a%.0s' {1..1})$(printf '0%.0s' {1..63})
readonly ID_WEB=$(printf 'b%.0s' {1..1})$(printf '0%.0s' {1..63})

echo "== case 1: a rotated segment whose newest line has aged out is pruned =="
reset_world
d=$(mk_container jobbliggaren-api "$ID_API")
jline "$(ts_days_ago 40)" "old" >"$d/$ID_API-json.log.1"
jline "$(ts_days_ago 1)"  "live" >"$d/$ID_API-json.log"
out=$(run_sut) || true
check "rotated segment removed"            "$([ ! -f "$d/$ID_API-json.log.1" ] && echo 1 || echo 0)"
check "LIVE segment survives"              "$([ -f "$d/$ID_API-json.log" ] && echo 1 || echo 0)"
check "reports pruned=1"                   "$(echo "$out" | grep -q 'pruned=1 ' && echo 1 || echo 0)"

echo "== case 2: a rotated segment still inside the window is kept =="
reset_world
d=$(mk_container jobbliggaren-api "$ID_API")
jline "$(ts_days_ago 5)" "recent" >"$d/$ID_API-json.log.1"
jline "$(ts_days_ago 1)" "live"   >"$d/$ID_API-json.log"
out=$(run_sut) || true
check "young rotated segment kept"         "$([ -f "$d/$ID_API-json.log.1" ] && echo 1 || echo 0)"
check "reports kept=1"                     "$(echo "$out" | grep -q 'kept=1' && echo 1 || echo 0)"

echo "== case 3: THE SAFETY PROPERTY — an ancient LIVE segment is never touched =="
reset_world
d=$(mk_container jobbliggaren-web "$ID_WEB")
# This is the measured shape of `web` on the box 2026-08-28: a startup burst, never rotated,
# arbitrarily old, and no rotated sibling at all.
jline "$(ts_days_ago 400)" "next-banner" >"$d/$ID_WEB-json.log"
out=$(run_sut) || true
check "live segment still present"         "$([ -f "$d/$ID_WEB-json.log" ] && echo 1 || echo 0)"
check "nothing was pruned"                 "$(echo "$out" | grep -q 'pruned=0' && echo 1 || echo 0)"
check "says it may not act on it"          "$(echo "$out" | grep -q 'no rotated segments' && echo 1 || echo 0)"

echo "== case 4: a segment whose newest line is unreadable is KEPT, not deleted =="
reset_world
d=$(mk_container jobbliggaren-api "$ID_API")
printf 'not json at all\n' >"$d/$ID_API-json.log.1"
: >"$d/$ID_API-json.log.2"
out=$(run_sut) || true
check "unparsable segment kept"            "$([ -f "$d/$ID_API-json.log.1" ] && echo 1 || echo 0)"
check "empty segment kept"                 "$([ -f "$d/$ID_API-json.log.2" ] && echo 1 || echo 0)"
check "counted as unreadable=2"            "$(echo "$out" | grep -q 'unreadable=2' && echo 1 || echo 0)"

echo "== case 5: an absent container is skipped, not a failure =="
reset_world
# `set +e` is NOT decoration: under the suite's own `set -euo pipefail` a non-zero run would kill
# the suite at this line, with no label, no failing-case name and no `passed:` summary. The
# property stays gated either way, but the diagnostic is what tells you WHICH case broke.
set +e
out=$(run_sut); rc=$?
set -e
check "exit 0 with no containers present"  "$([ "$rc" -eq 0 ] && echo 1 || echo 0)"
check "names the skip"                     "$(echo "$out" | grep -q 'no such container' && echo 1 || echo 0)"

echo "== case 6: a docker id that is not 64 hex is REFUSED before it reaches a path =="
reset_world
printf '%s' '../../../etc' >"$DOCKER_STUB_MAP/jobbliggaren-api"
set +e
out=$(run_sut); rc=$?
set -e
check "exits non-zero"                     "$([ "$rc" -ne 0 ] && echo 1 || echo 0)"
check "refuses to build a path"            "$(echo "$out" | grep -q 'REFUSING' && echo 1 || echo 0)"

echo "== case 7: --dry-run reports but deletes nothing =="
reset_world
d=$(mk_container jobbliggaren-api "$ID_API")
jline "$(ts_days_ago 40)" "old" >"$d/$ID_API-json.log.1"
out=$(run_sut --dry-run) || true
check "segment still present after dry-run" "$([ -f "$d/$ID_API-json.log.1" ] && echo 1 || echo 0)"
check "reports WOULD PRUNE"                 "$(echo "$out" | grep -q 'WOULD PRUNE' && echo 1 || echo 0)"

echo "== case 8: an unknown argument is refused =="
set +e
out=$(run_sut --wat); rc=$?
set -e
check "exits non-zero"                     "$([ "$rc" -ne 0 ] && echo 1 || echo 0)"
check "names the usage"                    "$(echo "$out" | grep -q 'usage' && echo 1 || echo 0)"

echo "== case 9: the window is 30 days — the boundary is measured, not assumed =="
reset_world
d=$(mk_container jobbliggaren-api "$ID_API")
jline "$(ts_days_ago 31)" "just-outside" >"$d/$ID_API-json.log.1"
jline "$(ts_days_ago 29)" "just-inside"  >"$d/$ID_API-json.log.2"
out=$(run_sut) || true
check "31 days old is pruned"              "$([ ! -f "$d/$ID_API-json.log.1" ] && echo 1 || echo 0)"
check "29 days old is kept"                "$([ -f "$d/$ID_API-json.log.2" ] && echo 1 || echo 0)"

echo "== case 10: a segment is judged by its NEWEST line, not its oldest =="
reset_world
d=$(mk_container jobbliggaren-api "$ID_API")
# Oldest line has aged out; newest has not. Anchoring on the oldest would delete entries that
# are still inside the window, which is the direction that loses data owed to a reader.
{ jline "$(ts_days_ago 40)" "first"; jline "$(ts_days_ago 2)" "last"; } >"$d/$ID_API-json.log.1"
out=$(run_sut) || true
check "mixed-age segment kept"             "$([ -f "$d/$ID_API-json.log.1" ] && echo 1 || echo 0)"

echo "== case 11: all NINE retention-bound containers are covered, BEHAVIOURALLY =="
# ⚠ THIS CASE USED TO GREP THE SUT'S SOURCE TEXT, AND THAT WAS NOT COVERAGE. A grep for the array
# literal cannot see whether the LOOP reads the array: replacing
# `for container in "${APP_CONTAINERS[@]}"` with two hard-coded names left the literal untouched
# and the whole suite green, while `worker` and `caddy` silently stopped being processed — one of
# which is a container this change exists for. Every name below is now exercised through the real
# path: its own container, its own aged segment, its own live segment that must survive.
#
# NINE, not the app stream's four (CTO 2026-08-28): the prune's set is keyed to personal data,
# not to app events, so it deliberately diverges from `jobbliggaren-logship.sh`'s array. A case
# that iterated four would pass while five containers silently reached no age bound.
reset_world
for n in jobbliggaren-api jobbliggaren-worker jobbliggaren-web jobbliggaren-caddy jobbliggaren-postgres jobbliggaren-redis jobbliggaren-seq jobbliggaren-migrate jobbliggaren-migrate-rewrap; do
  # A distinct 64-hex id per container, so a path collision cannot make one stand in for another.
  id=$(printf '%s' "$n" | md5sum | cut -c1-32)$(printf '0%.0s' {1..32})
  d=$(mk_container "$n" "$id")
  jline "$(ts_days_ago 40)" "old" >"$d/$id-json.log.1"
  jline "$(ts_days_ago 1)"  "live" >"$d/$id-json.log"
  eval "D_${n//-/_}=$d"; eval "I_${n//-/_}=$id"
done
out=$(run_sut) || true
check "all nine aged segments pruned"      "$(echo "$out" | grep -q 'pruned=9' && echo 1 || echo 0)"
for n in jobbliggaren-api jobbliggaren-worker jobbliggaren-web jobbliggaren-caddy jobbliggaren-postgres jobbliggaren-redis jobbliggaren-seq jobbliggaren-migrate jobbliggaren-migrate-rewrap; do
  d=$(eval "echo \$D_${n//-/_}"); id=$(eval "echo \$I_${n//-/_}")
  check "$n: rotated segment removed"      "$([ ! -f "$d/$id-json.log.1" ] && echo 1 || echo 0)"
  check "$n: LIVE segment survives"        "$([ -f "$d/$id-json.log" ] && echo 1 || echo 0)"
done

echo "== case 12: a DOWN docker daemon fails loudly, never as a vacuous pruned=0 =="
# The defect this pins: `docker inspect` against a down daemon fails exactly like a missing
# container, so without the daemon probe the run reports a skip per container and `pruned=0` and exits 0 —
# byte-identical to a healthy pass on a box with nothing to prune.
reset_world
d=$(mk_container jobbliggaren-api "$ID_API")
jline "$(ts_days_ago 40)" "old" >"$d/$ID_API-json.log.1"
set +e
out=$(DOCKER_STUB_DAEMON_DOWN=1 run_sut); rc=$?
set -e
check "exits non-zero"                     "$([ "$rc" -ne 0 ] && echo 1 || echo 0)"
check "says it refuses"                    "$(echo "$out" | grep -q 'REFUSING' && echo 1 || echo 0)"
check "names the daemon, not the container" "$(echo "$out" | grep -q 'daemon is not reachable' && echo 1 || echo 0)"
# ⚠ ANCHORED TO THE SUMMARY LINE, NOT THE SUBSTRING. The first form of this assertion grepped
# for `pruned=0` and failed against a correct SUT — because the die message itself ends
# "refusing to report a vacuous pruned=0". The assertion was matching its own diagnostic. What
# must be absent is the run's SUMMARY, which only prints when the loop completed.
check "no summary line is printed"         "$(echo "$out" | grep -q '^logprune: pruned=' && echo 0 || echo 1)"
check "aged segment left untouched"        "$([ -f "$d/$ID_API-json.log.1" ] && echo 1 || echo 0)"

echo
echo "passed: $pass  failed: $fail"
[ "$fail" -eq 0 ] || exit 1
