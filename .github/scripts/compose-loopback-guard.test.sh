#!/usr/bin/env bash
#
# Fixture tests for compose-loopback-guard.sh.
#
# Run:  bash .github/scripts/compose-loopback-guard.test.sh
#
# WHY THE NEGATIVE FIXTURES CARRY THE WHOLE FILE. A guard whose fixtures all pass has
# proven that it does not crash, not that it catches anything — and this repo has measured
# that failure mode before. `bare_mapping` is the fixture that matters: it is the exact
# shape the tree carried before #1198 (`- "5435:5432"`), and if the guard ever stops
# failing on it, the guard is decoration.
#
# `real_repo_file` pins the delivery itself rather than a fixture of it, so a future edit
# that reintroduces a 0.0.0.0 binding fails here and not only in review.
#
# Exit 2 is asserted separately from exit 1 on purpose: "the guard could not run" must
# never be indistinguishable from "the guard passed".

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

# run <name> <expected-exit> <file...>
run() {
  local name=$1 expected=$2; shift 2
  local actual=0
  bash "$SUT" "$@" >"$TMPROOT/out.txt" 2>&1 || actual=$?
  if [ "$actual" -eq "$expected" ]; then
    pass=$((pass + 1))
    printf 'ok   %-24s (exit %d)\n' "$name" "$actual"
  else
    fail=$((fail + 1))
    printf 'FAIL %-24s expected exit %d, got %d\n' "$name" "$expected" "$actual"
    sed 's/^/       | /' "$TMPROOT/out.txt"
  fi
}

# --- 1. the clean shape passes ------------------------------------------------------
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

# --- 2. THE COUNTERFACTUAL: the pre-#1198 shape must FAIL ---------------------------
# A bare HOST:CONTAINER mapping binds 0.0.0.0. This is the exact form the tree carried
# for months while its comment claimed loopback. If this fixture ever goes green, the
# guard has stopped guarding.
cat >"$TMPROOT/bare.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "5435:5432"
YAML
run bare_mapping 1 "$TMPROOT/bare.yml"

# --- 3. an explicit 0.0.0.0 must FAIL ------------------------------------------------
cat >"$TMPROOT/explicit.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - "0.0.0.0:5435:5432"
YAML
run explicit_wildcard 1 "$TMPROOT/explicit.yml"

# --- 4. an unquoted bare mapping must FAIL (quoting is not the control) --------------
cat >"$TMPROOT/unquoted.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      - 5435:5432
YAML
run unquoted_bare 1 "$TMPROOT/unquoted.yml"

# --- 5. one bad entry among good ones must FAIL (not masked by its neighbours) -------
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

# --- 6. the block must CLOSE — a later list is not a ports list ----------------------
# Without indentation bookkeeping, `- v:/data` under `volumes:` would be read as a port
# entry and reported. This fixture pins that it is not.
cat >"$TMPROOT/close.yml" <<'YAML'
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
run block_closes 0 "$TMPROOT/close.yml"

# --- 7. comments inside the block are not entries ------------------------------------
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

# --- 8. an unmodelled entry shape exits 2, never 0 -----------------------------------
# Long-form mappings are not list items of the shape this guard reads. Reporting beats
# skipping: a silently unchecked port is the defect, not the fix.
cat >"$TMPROOT/longform.yml" <<'YAML'
services:
  a:
    image: x
    ports:
      target: 5432
      published: 5435
YAML
run longform_unparsed 2 "$TMPROOT/longform.yml"

# --- 9. a missing file exits 2, never 0 ----------------------------------------------
run missing_file 2 "$TMPROOT/does-not-exist.yml"

# --- 10. CRLF input is handled (the repo default is core.autocrlf=true) --------------
printf 'services:\r\n  a:\r\n    image: x\r\n    ports:\r\n      - "5435:5432"\r\n' >"$TMPROOT/crlf.yml"
run crlf_bare_fails 1 "$TMPROOT/crlf.yml"

# --- 11. THE DELIVERY ITSELF -----------------------------------------------------------
# Not a fixture of the file — the file. This is what makes the guard a guard over the
# repo rather than over its own test data.
run real_repo_file 0 "$REPO_ROOT/docker-compose.yml"

echo
echo "compose-loopback-guard fixtures: $pass passed, $fail failed"
[ "$fail" -eq 0 ] || exit 1
