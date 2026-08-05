#!/usr/bin/env bash
#
# Fixture tests for compose-edge-publish-guard.sh.
#
# Run:  bash .github/scripts/compose-edge-publish-guard.test.sh
#
# REQUIRES THE COMPOSE CLI, NOT A DAEMON — `docker compose config` is client-side.
#
# THE NEGATIVE FIXTURES CARRY THE FILE. A guard whose fixtures all pass has proven that it
# does not crash, not that it catches anything. Each of the four predicate clauses has a
# fixture that breaks exactly that clause and nothing else, so a clause deleted from the SUT
# turns exactly one line red.
#
# THREE OUTCOMES, NEVER COLLAPSED:
#   exit 0 — the stack publishes the shape Option B requires.
#   exit 1 — it does not. Names which clause and what was found instead.
#   exit 2 — could not answer. A refusal must never read as a pass.
#
# THE LAST SECTION PINS THE DELIVERY ITSELF, not a fixture of it: the real deploy/ project.
# A future edit that publishes Postgres, renames the edge service, or binds the proxy to
# loopback fails here rather than in review.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/compose-edge-publish-guard.sh"
[ -f "$SUT" ] || {
  echo "missing script under test: $SUT" >&2
  exit 1
}
REPO_ROOT=$(cd -- "$script_dir/../.." && pwd)
readonly REPO_ROOT

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

pass=0
fail=0

# A fixture is a DIRECTORY, because the guard unit is a compose project and not a compose
# file — the same reason the loopback suite gives.
proj() {
  mkdir -p "$TMPROOT/$1"
  cat >"$TMPROOT/$1/docker-compose.yml"
}

run() {
  local name=$1 expected=$2
  shift 2
  local actual=0
  bash "$SUT" "$@" >"$TMPROOT/out.txt" 2>&1 || actual=$?
  if [ "$actual" -eq "$expected" ]; then
    pass=$((pass + 1))
    printf 'ok   %-40s (exit %d)\n' "$name" "$actual"
  else
    fail=$((fail + 1))
    printf 'FAIL %-40s expected exit %d, got %d\n' "$name" "$expected" "$actual"
    sed 's/^/       | /' "$TMPROOT/out.txt"
  fi
}

# Asserts on the MESSAGE as well as the exit code: a clause that fires for the wrong reason
# is a clause that will pass the wrong file later.
run_says() {
  local name=$1 expected=$2 regex=$3
  shift 3
  local actual=0
  bash "$SUT" "$@" >"$TMPROOT/out.txt" 2>&1 || actual=$?
  if [ "$actual" -eq "$expected" ] && grep -qE "$regex" "$TMPROOT/out.txt"; then
    pass=$((pass + 1))
    printf 'ok   %-40s (exit %d, said %s)\n' "$name" "$actual" "$regex"
  else
    fail=$((fail + 1))
    printf 'FAIL %-40s expected exit %d + /%s/, got exit %d\n' "$name" "$expected" "$regex" "$actual"
    sed 's/^/       | /' "$TMPROOT/out.txt"
  fi
}

# ==========================================================================================
# 1. THE SHAPE THE STACK MUST HAVE
# ==========================================================================================

proj clean <<'YAML'
services:
  caddy:
    image: x
    ports:
      - "80:80"
      - "443:443"
  api:
    image: y
  postgres:
    image: z
YAML
run clean 0 "$TMPROOT/clean"

# Explicit wide bind addresses are the same verdict as an omitted one — both publish on
# every interface, which is what the edge requires.
proj clean_explicit_wide <<'YAML'
services:
  caddy:
    image: x
    ports:
      - "0.0.0.0:80:80"
      - "0.0.0.0:443:443"
YAML
run clean_explicit_wide 0 "$TMPROOT/clean_explicit_wide"

# HTTP/3 shares the 443 NUMBER on a second protocol. The clause is about numbers, so a udp
# sibling must not read as a third port.
proj clean_with_http3 <<'YAML'
services:
  caddy:
    image: x
    ports:
      - "80:80"
      - "443:443"
      - "443:443/udp"
YAML
run clean_with_http3 0 "$TMPROOT/clean_with_http3"

# ==========================================================================================
# 2. ONE CLAUSE BROKEN PER FIXTURE
# ==========================================================================================

# Clause 1 — the shape #1198 was about, one level up: a data service that publishes.
proj postgres_also_publishes <<'YAML'
services:
  caddy:
    image: x
    ports:
      - "80:80"
      - "443:443"
  postgres:
    image: z
    ports:
      - "127.0.0.1:5432:5432"
YAML
run_says postgres_also_publishes 1 'PUBLISHER-COUNT' "$TMPROOT/postgres_also_publishes"

# Clause 1 again, in the other direction: nothing publishes at all. A stack with no edge is
# not a clean stack, and "no ports, therefore all ports are fine" is vacuous truth.
proj nothing_publishes <<'YAML'
services:
  api:
    image: y
  postgres:
    image: z
YAML
run_says nothing_publishes 1 'PUBLISHER-COUNT' "$TMPROOT/nothing_publishes"

# Clause 2 — something publishes 80/443, but it is the API rather than the proxy. Every
# other clause holds, so only the name check can catch this.
proj api_is_the_publisher <<'YAML'
services:
  api:
    image: y
    ports:
      - "80:80"
      - "443:443"
YAML
run_says api_is_the_publisher 1 'PUBLISHER-NOT-EDGE' "$TMPROOT/api_is_the_publisher"

# Clause 3 — the ACME port dropped. This is the shape that costs a certificate: 443 serves,
# so the site looks alive, and renewal dies about sixty days later.
proj port_80_missing <<'YAML'
services:
  caddy:
    image: x
    ports:
      - "443:443"
YAML
run_says port_80_missing 1 'PORT-SET' "$TMPROOT/port_80_missing"

# Clause 3 — an extra port on the edge itself, which no clause but this one sees.
proj edge_publishes_extra <<'YAML'
services:
  caddy:
    image: x
    ports:
      - "80:80"
      - "443:443"
      - "2019:2019"
YAML
run_says edge_publishes_extra 1 'PORT-SET' "$TMPROOT/edge_publishes_extra"

# Clause 4 — the reverse of #1198, and the reason this guard is not the loopback guard. The
# file reads as hardened and the site is unreachable, with no certificate ever issued.
proj edge_bound_to_loopback <<'YAML'
services:
  caddy:
    image: x
    ports:
      - "127.0.0.1:80:80"
      - "127.0.0.1:443:443"
YAML
run_says edge_bound_to_loopback 1 'EDGE-NOT-WIDE' "$TMPROOT/edge_bound_to_loopback"

# ==========================================================================================
# 3. WHAT THE GUARD REFUSES RATHER THAN JUDGES
# ==========================================================================================

# An unexpanded variable comes back as a raw string under --no-interpolate. Refused, because
# the guard cannot read what compose did not resolve.
proj unresolved_entry <<'YAML'
services:
  caddy:
    image: x
    ports:
      - "${BIND}:80:80"
      - "443:443"
YAML
run_says unresolved_entry 2 'UNRESOLVED-ENTRY' "$TMPROOT/unresolved_entry"

# A container port alone publishes on a RANDOM host port. There is no number to compare, so
# this is a refusal and not a PORT-SET finding.
proj ephemeral_publish <<'YAML'
services:
  caddy:
    image: x
    ports:
      - "80"
      - "443:443"
YAML
run_says ephemeral_publish 2 'EPHEMERAL-PUBLISH' "$TMPROOT/ephemeral_publish"

# A range is not a single number — but only the LONG form reaches the guard as one.
# Measured: compose EXPANDS a short-form range into one entry per port, each with a plain
# numeric `published`, so that spelling is read in full and lands as a PORT-SET finding.
# Both fixtures are kept because the difference is invisible from the file.
proj port_range_short_form <<'YAML'
services:
  caddy:
    image: x
    ports:
      - "8000-8005:8000-8005"
YAML
run_says port_range_short_form 1 'PORT-SET' "$TMPROOT/port_range_short_form"

proj port_range_long_form <<'YAML'
services:
  caddy:
    image: x
    ports:
      - target: 8000
        published: "8000-8005"
        protocol: tcp
        mode: ingress
YAML
run_says port_range_long_form 2 'UNREADABLE-PUBLISH' "$TMPROOT/port_range_long_form"

# A host-networked service listens on every host interface and has ZERO ports entries,
# so the publisher count cannot see it. Measured against the guard before this clause
# existed: exit 0, with the message saying nothing else publishes while 5432 was open on
# the host. The edge itself is correct in this fixture, which is what makes it a test of
# the new clause and not of the old ones.
proj host_networked_service <<'YAML'
services:
  caddy:
    image: x
    ports:
      - "80:80"
      - "443:443"
  postgres:
    image: z
    network_mode: host
YAML
run_says host_networked_service 2 'HOST-NETWORK-OR-MODE' "$TMPROOT/host_networked_service"

# A REFUSAL MUST WIN OVER A FINDING. This file breaks clause 2 as well (the publisher is not
# the edge), so if refusals and findings were merged into one verdict the exit code would be
# 1 and the reader would be told the wrong thing about a port nobody read.
proj refusal_outranks_finding <<'YAML'
services:
  api:
    image: y
    ports:
      - "${BIND}:80:80"
YAML
run refusal_outranks_finding 2 "$TMPROOT/refusal_outranks_finding"

# ==========================================================================================
# 4. INVOCATION — a guard that cannot run must never look like a guard that passed
# ==========================================================================================

run missing_dir 2 "$TMPROOT/does-not-exist"

# A file path is not a project directory. Compose would silently walk up and judge the
# parent, so the guard refuses the argument itself.
run file_path_argument_refused 2 "$TMPROOT/clean/docker-compose.yml"

mkdir -p "$TMPROOT/empty_dir"
run no_compose_file 2 "$TMPROOT/empty_dir"

run too_many_arguments 2 "$TMPROOT/clean" "$TMPROOT/clean"

# ==========================================================================================
# 5. THE VERDICT RESTS ON THE ARTEFACT, NEVER ON THE ENVIRONMENT
# ==========================================================================================

# COMPOSE_FILE overrides a project own file resolution. Pointed at a broken project with the
# variable naming a clean file, a guard that let it through would be reporting on the
# checker environment instead of the artefact — #1198 defect class rebuilt inside the remedy.
proj env_override_target <<'YAML'
services:
  api:
    image: y
    ports:
      - "9999:9999"
YAML
cp "$TMPROOT/clean/docker-compose.yml" "$TMPROOT/env_override_target/clean-copy.yml"
(
  export COMPOSE_FILE="$TMPROOT/env_override_target/clean-copy.yml"
  run_says ambient_compose_file_ignored 1 'PUBLISHER-NOT-EDGE' "$TMPROOT/env_override_target"
)

# The second seat: the same variable inside the project own .env, which clearing the
# environment does not reach. It needs the empty --env-file on the compose call.
proj dotenv_override_target <<'YAML'
services:
  api:
    image: y
    ports:
      - "9999:9999"
YAML
cp "$TMPROOT/clean/docker-compose.yml" "$TMPROOT/dotenv_override_target/clean-copy.yml"
printf 'COMPOSE_FILE=%s\n' "$TMPROOT/dotenv_override_target/clean-copy.yml" >"$TMPROOT/dotenv_override_target/.env"
run_says dotenv_compose_file_ignored 1 'PUBLISHER-NOT-EDGE' "$TMPROOT/dotenv_override_target"

# ==========================================================================================
# 6. THE SOURCE ITSELF
# ==========================================================================================

# An apostrophe anywhere inside the jq block — including in a comment — terminates the shell
# single-quoted string and the script dies somewhere else entirely. It has happened four
# times in this repo. Rewrite the wording; never escape it.
jq_block_apostrophes() {
  awk '/^out=\$\(jq -r/{inblock=1; next} inblock && /^  . <<<"\$model"/{inblock=0} inblock' "$SUT" |
    tr -cd "'" | wc -c
}
n=$(jq_block_apostrophes)
if [ "$n" -eq 0 ]; then
  pass=$((pass + 1))
  printf 'ok   %-40s (%d in the jq block)\n' jq_block_carries_no_apostrophe "$n"
else
  fail=$((fail + 1))
  printf 'FAIL %-40s found %d apostrophe(s) in the jq block\n' jq_block_carries_no_apostrophe "$n"
fi

# ==========================================================================================
# 7. THE DELIVERY, NOT A FIXTURE OF IT
# ==========================================================================================

run real_deploy_project 0 "$REPO_ROOT/deploy"

# The default argument is the delivery. A guard invoked with no argument in CI must judge the
# same project the explicit form does, or the workflow step and this suite disagree silently.
run real_deploy_project_by_default 0

echo
if [ "$fail" -gt 0 ]; then
  echo "compose-edge-publish-guard fixtures: $pass passed, $fail failed"
  exit 1
fi
echo "compose-edge-publish-guard fixtures: $pass passed, 0 failed"
