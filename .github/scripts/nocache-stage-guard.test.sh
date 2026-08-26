#!/usr/bin/env bash
#
# Fixture tests for nocache-stage-guard.sh.
#
# Run:  bash .github/scripts/nocache-stage-guard.test.sh
#
# WHY THE NEGATIVE FIXTURES CARRY THE WHOLE FILE. A guard whose fixtures all pass has proven
# that it does not crash, not that it catches anything — the house rule, stated in
# compose-loopback-guard.test.sh and measured on this repo before.
#
# EVERY FIXTURE IN SECTIONS 2 AND 3 IS A PROBE A REVIEWER ACTUALLY RAN against the FIRST
# version of this guard, and every one of them came back `exit 0, coupling holds`. They are
# fixtures now precisely because that version read as coverage it did not have:
#   heterogeneous_row / extra_block_leg  — the matrix reader is blinded PER ROW, not
#       all-or-nothing (found independently by three reviewers, 2026-08-27)
#   regex_metachar                        — `grep -i -F` was `grep -i` in effect; `runtime*`
#       matched `runtime` while buildkit's strings.EqualFold matched nothing
#   upgrade_in_other_stage                — filter named a real stage that was not the one
#       carrying the upgrade RUN
#   wiring_deleted                        — the `no-cache-filters` line is the third link in
#       the coupling and nothing asserted it
#   multiline_run / dnf_upgrade           — the upgrade detector required the package manager
#       on the same physical line as `RUN`
#
# THREE OUTCOMES, ASSERTED SEPARATELY AND NEVER COLLAPSED:
#   exit 0 — read everything, and the coupling holds.
#   exit 1 — read everything, and it does not. Names the leg.
#   exit 2 — could not answer. "The guard could not run" must never be indistinguishable
#            from "the guard passed", which is why every refusal below asserts 2, not 1.
#
# THE GUARD'S DIAGNOSTIC IS PRINTED ON FAILURE, and that is deliberate. Its whole added value
# over an exit code is naming WHICH leg and WHY; a suite that swallows that leaves CI reading
# `want 0, got 1` with the cause invisible — the same complaint this guard makes about
# buildkit.
#
# SECTION 6 ASKS THE OTHER QUESTION ENTIRELY: not "does the guard judge a fixture right" but
# "is the artefact it reads the one that ships". `real_repo` pins the delivery itself.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/nocache-stage-guard.sh"
[ -f "$SUT" ] || { echo "missing script under test: $SUT" >&2; exit 1; }
REPO_ROOT=$(cd -- "$script_dir/../.." && pwd)
readonly REPO_ROOT

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

pass=0
fail=0

WIRING='          no-cache-filters: ${{ matrix.nocache }}'

# mk <name> <matrix-rows-file-content> [wiring:yes|no]
# Writes a workflow with the given matrix rows. Dockerfiles are added by the caller.
mk() {
  local name="$1" rows="$2" wiring="${3:-yes}"
  local root="$TMPROOT/$name"
  mkdir -p "$root/.github/workflows"
  {
    printf 'jobs:\n  images:\n    strategy:\n      matrix:\n        include:\n'
    printf '%s\n' "$rows"
    printf '    steps:\n      - uses: docker/build-push-action@v7\n        with:\n'
    [ "$wiring" = "yes" ] && printf '%s\n' "$WIRING"
  } > "$root/.github/workflows/build.yml"
  echo "$root"
}

# df <root> <relpath> <lines...>
df() {
  local root="$1" rel="$2"; shift 2
  mkdir -p "$root/$(dirname "$rel")"
  printf '%s\n' "$@" > "$root/$rel"
}

expect() {
  local label="$1" root="$2" want="$3"
  local got=0 out
  out=$(NOCACHE_GUARD_ROOT="$root" bash "$SUT" 2>&1) || got=$?
  if [ "$got" = "$want" ]; then
    printf '  ok    %-40s exit %s\n' "$label" "$got"; pass=$((pass + 1))
  else
    printf '  FAIL  %-40s want %s, got %s\n' "$label" "$want" "$got"
    printf '%s\n' "$out" | sed 's/^/          | /'
    fail=$((fail + 1))
  fi
}

ROW_API='          - { name: api, context: ".", file: "api/Dockerfile", nocache: "" }'

echo "== 1. the coupling holds =="
r=$(mk ok "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "runtime" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile 'FROM scratch AS deps' 'FROM scratch AS runtime' 'RUN apk --no-cache upgrade'
expect "filter names the upgrading stage" "$r" 0

r=$(mk noup "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile 'FROM scratch AS runtime' 'RUN echo hello'
expect "no upgrade, no filter" "$r" 0

r=$(mk list "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "deps,runtime" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile 'FROM scratch AS deps' 'RUN apk --no-cache upgrade' 'FROM scratch AS runtime' 'RUN apk --no-cache upgrade'
expect "comma list, both stages upgrade" "$r" 0

r=$(mk platform "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "runtime" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile 'FROM --platform=$BUILDPLATFORM scratch AS runtime' 'RUN apk --no-cache upgrade'
expect "FROM --platform is read, not misdiagnosed" "$r" 0

echo "== 2. violations — every one passed the first version =="
r=$(mk renamed "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "runtime" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile 'FROM scratch AS svelte' 'RUN apk --no-cache upgrade'
expect "renamed_stage: filter names a dead stage" "$r" 1

r=$(mk unfiltered "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile 'FROM scratch AS runtime' 'RUN apk --no-cache upgrade'
expect "upgrade with no filter at all" "$r" 1

r=$(mk metachar "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "runtime*" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile 'FROM scratch AS runtime' 'RUN apk --no-cache upgrade'
expect "regex_metachar: runtime* is not a stage" "$r" 1

# ISOLATES check 2a, and the isolation is the point. The fixture above is ALSO caught by the
# stage-binding check, so it survives a mutation that drops `-F` — it passes for the wrong
# reason. Here there is no upgrade at all, so only the does-this-stage-exist check can speak:
# with `-F` the metachar matches nothing and this violates; without it, BRE `runtime*` matches
# `runtime` and the guard reports coupling holds.
r=$(mk metachar2 "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "runtime*" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile 'FROM scratch AS runtime' 'RUN echo no-upgrade'
expect "regex_metachar isolated (no upgrade)" "$r" 1

r=$(mk otherstage "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "runtime" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile 'FROM scratch AS deps' 'RUN apk --no-cache upgrade' 'FROM scratch AS runtime'
expect "upgrade_in_other_stage: deps upgrades" "$r" 1

r=$(mk multiline "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile 'FROM scratch AS runtime' 'RUN set -eux; \' '    apt-get update; \' '    apt-get -y upgrade'
expect "multiline_run: upgrade on a continuation" "$r" 1

r=$(mk dnf "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile 'FROM scratch AS runtime' 'RUN dnf -y upgrade'
expect "dnf_upgrade: canonical spelling" "$r" 1

echo "== 3. refusals — 'could not run' is not 'passed' =="
missing="$TMPROOT/no_workflow"; mkdir -p "$missing"
expect "workflow absent" "$missing" 2

r=$(mk reshaped '          - name: api'$'\n''            file: "api/Dockerfile"')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
expect "matrix fully reshaped to block form" "$r" 2

r=$(mk hetero "$ROW_API"$'\n''          - name: web'$'\n''            file: "web/Dockerfile"'$'\n''            nocache: "runtme"')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile 'FROM scratch AS runtime' 'RUN apk --no-cache upgrade'
expect "heterogeneous_row: one row block form" "$r" 2

r=$(mk extraleg "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "runtime" }'$'\n''          - name: newsvc'$'\n''            file: "svc/Dockerfile"')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile 'FROM scratch AS runtime' 'RUN apk --no-cache upgrade'
df "$r" svc/Dockerfile 'FROM scratch AS runtime' 'RUN apk --no-cache upgrade'
expect "extra_block_leg: sixth leg unread" "$r" 2

r=$(mk gone "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "runtime" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
expect "leg points at a missing Dockerfile" "$r" 2

r=$(mk nofield '          - { name: api, context: ".", file: "api/Dockerfile" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
expect "leg omits the nocache field entirely" "$r" 2

r=$(mk nowiring "$ROW_API" no)
df "$r" api/Dockerfile 'FROM scratch AS runtime'
expect "wiring_deleted: no-cache-filters absent" "$r" 2

r=$(mk nostages "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "runtime" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime'
df "$r" web/Dockerfile '# no FROM line at all'
expect "nostages: filter set, stages unreadable" "$r" 2

echo "== 4. regression: the IFS bug this guard actually had =="
# Against v1 every leg with `nocache: ""` refused as "has no nocache field", because an empty
# middle field collapsed under IFS-whitespace and shifted has_nocache into it.
r=$(mk empty1 "$ROW_API")
df "$r" api/Dockerfile 'FROM scratch AS runtime'
expect "empty_nocache_not_swallowed (only leg)" "$r" 0

# Distinct from the above and from section 1's `ok`: here the EMPTY field sits on the leg that
# is checked FIRST while a filled one follows, which is the ordering that shifts fields.
r=$(mk empty2 "$ROW_API"$'\n''          - { name: web, context: "web", file: "web/Dockerfile", nocache: "runtime" }')
df "$r" api/Dockerfile 'FROM scratch AS runtime' 'RUN echo no-upgrade-here'
df "$r" web/Dockerfile 'FROM scratch AS runtime' 'RUN apk --no-cache upgrade'
expect "empty field precedes a filled one" "$r" 0

echo "== 5. the delivered artefact, not a fixture of it =="
expect "real_repo: build.yml + real Dockerfiles" "$REPO_ROOT" 0

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ] || exit 1
