#!/usr/bin/env bash
#
# nocache-stage-guard.sh — the `images` matrix's `nocache` field must name a stage that exists,
# and every leg that upgrades OS packages must have such an entry.
#
# WHY THIS EXISTS, and why a comment was not enough. #1517 repaired a blocking CVE gate by
# passing `no-cache-filters: runtime` to two of five `images` legs, because a cached
# `RUN apk --no-cache upgrade` layer had been serving stale packages for months WHILE THE
# DOCKERFILE CARRIED THE CORRECT REPAIR. The repair now hangs on a string matching a stage
# name across two files that nothing checks together.
#
# BUILDKIT SILENTLY IGNORES AN UNMATCHED STAGE NAME. Measured twice independently on
# 2026-08-27: in buildkit's source, `Client.IsNoCache` (frontend/dockerui/config.go) does a
# per-stage lookup with no reverse validation that each requested name matched something,
# while `resolveTarget` in the same package errors with suggestions on an unknown `--target`
# — buildkit validates one and deliberately does not validate the other; and against
# buildx v0.29.1 with a synthetic two-stage probe, where a deliberately misspelled filter
# left the stage CACHED, exit 0, zero warnings in `--progress=plain` output.
#
# So a stage rename no-ops the filter with nothing to read. `build.yml` already condemns this
# exact shape 600 lines up: "A GLOB THAT MATCHES NOTHING PASSES VACUOUSLY ... turn this gate
# into a green no-op that reads as coverage."
#
# WHAT THE FAILURE ACTUALLY COSTS, stated precisely rather than dramatically. It is a FALSE
# FAIL, not a false pass. `build.yml` builds with `load: true` and Trivy scans the image the
# build step just produced, so scan and artefact are the same object: a stale image is
# measured against a fresh vulnerability DB and the leg goes red. What a disarmed filter buys
# is CI wedged on an alarm that reads like an unrelated new advisory, with the real cause —
# a cache layer — invisible. (`security-auditor`, 2026-08-27, correcting this guard's first
# framing.) That is worth a gate; it is not worth calling a silent clean report.
#
# TWO DIRECTIONS, AND THE SECOND IS THE LOAD-BEARING ONE:
#   1. A leg whose Dockerfile runs a package upgrade must carry a `nocache` naming that
#      stage. Catches: an upgrade added to a leg nobody filtered.
#   2. Every non-empty `nocache` must name a stage that EXISTS in that leg's Dockerfile.
#      Catches: the rename. This is the direction buildkit refuses to check.
#
# THREE OUTCOMES, NEVER COLLAPSED — the house rule (cf. compose-loopback-guard.sh):
#   exit 0 — read both files, and the coupling holds.
#   exit 1 — read both files, and it does not. Names the leg, the field and the file.
#   exit 2 — could not answer. "The guard could not run" must never be indistinguishable
#            from "the guard passed", which is why a missing workflow, an unparseable
#            matrix, a missing Dockerfile and an empty matrix all exit 2 and not 0.
#
# IT READS THE MATRIX WITH A NARROWLY-ANCHORED LINE READER, NOT A YAML PARSER, and that is a
# limitation rather than a design win: the anchor is the exact one-line flow-mapping spelling
# the matrix uses today. Any other legal YAML spelling of the same rows matches ZERO rows —
# which is why zero rows is a REFUSAL (exit 2) and never a pass. A reshaped matrix makes this
# guard say so instead of going quietly green.
#
# Stage names are read from `FROM ... AS <name>`, the only spelling buildkit resolves, and
# compared case-insensitively because buildkit's own lookup is `strings.EqualFold`.

set -euo pipefail

readonly EXIT_OK=0
readonly EXIT_VIOLATION=1
readonly EXIT_REFUSE=2

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT="${NOCACHE_GUARD_ROOT:-$(cd -- "$script_dir/../.." && pwd)}"
readonly REPO_ROOT
WORKFLOW="${NOCACHE_GUARD_WORKFLOW:-$REPO_ROOT/.github/workflows/build.yml}"
readonly WORKFLOW

refuse() { echo "REFUSED: $*" >&2; exit "$EXIT_REFUSE"; }
violate() { echo "VIOLATION: $*" >&2; violations=$((violations + 1)); }

[ -f "$WORKFLOW" ] || refuse "workflow not found: $WORKFLOW"

# --- read the matrix rows -----------------------------------------------------------------
# One row per line: "<name>TAB<file>TAB<has_nocache>TAB<nocache>". A row missing name or file
# is a refusal: the guard must not silently skip a leg it could not read.
rows=$(
  awk '
    /^          - \{ name: / {
      line = $0
      name = ""; file = ""; nocache = ""; has_nocache = 0
      if (match(line, /name: [A-Za-z0-9_-]+/))       { name = substr(line, RSTART + 6, RLENGTH - 6) }
      if (match(line, /file: "[^"]*"/))              { file = substr(line, RSTART + 7, RLENGTH - 8) }
      if (match(line, /nocache: "[^"]*"/))           { nocache = substr(line, RSTART + 10, RLENGTH - 11); has_nocache = 1 }
      printf "%s\t%s\t%d\t%s\n", name, file, has_nocache, nocache
    }
  ' "$WORKFLOW"
)

[ -n "$rows" ] || refuse "no images matrix rows matched in $WORKFLOW — the shape changed, and a zero-row sweep passes vacuously"

violations=0
legs=0

# TAB IS IFS WHITESPACE, so a run of tabs collapses to ONE delimiter and an empty field in
# the middle silently shifts every later field left. The possibly-empty `nocache` is
# therefore emitted LAST, where emptiness reads as emptiness. Pinned by the
# `empty_nocache_not_swallowed` fixture, which failed against this guard's first version.
while IFS=$'\t' read -r name file has_nocache nocache; do
  [ -n "$name" ] || refuse "a matrix row parsed with no name — cannot vouch for this shape"
  [ -n "$file" ] || refuse "leg '$name' parsed with no file — cannot vouch for this shape"
  [ "$has_nocache" = "1" ] || refuse "leg '$name' has no nocache field — every leg must state one, empty or not, so omission cannot read as 'no upgrade'"

  dockerfile="$REPO_ROOT/$file"
  [ -f "$dockerfile" ] || refuse "leg '$name' points at a missing Dockerfile: $file"
  legs=$((legs + 1))

  # Stage names buildkit would resolve.
  stages=$(grep -oiE '^FROM[[:space:]]+[^[:space:]]+([[:space:]]+AS[[:space:]]+[A-Za-z0-9_.-]+)' "$dockerfile" \
           | grep -oiE '[[:space:]]AS[[:space:]]+[A-Za-z0-9_.-]+$' \
           | awk '{print tolower($2)}' || true)

  # A package-upgrade RUN, in any of the three package managers this repo's bases use.
  upgrade_line=$(grep -nE '^[[:space:]]*RUN[[:space:]].*((apk[[:space:]].*upgrade)|(apt-get[[:space:]].*upgrade)|(dnf[[:space:]].*update))' "$dockerfile" | head -1 || true)

  # --- direction 2: a named stage must exist (the rename catch) ---------------------------
  if [ -n "$nocache" ]; then
    if ! printf '%s\n' "$stages" | grep -qix -- "$nocache"; then
      violate "leg '$name' filters stage '$nocache', which does not exist in $file. Buildkit ignores this silently, so the filter is inert and the upgrade layer is served from cache. Stages present: $(printf '%s ' $stages)"
    fi
  fi

  # --- direction 1: an upgrading leg must be filtered -------------------------------------
  if [ -n "$upgrade_line" ] && [ -z "$nocache" ]; then
    violate "leg '$name' runs a package upgrade ($file:${upgrade_line%%:*}) but carries no nocache entry, so that layer is cached and goes stale"
  fi
done <<< "$rows"

[ "$legs" -gt 0 ] || refuse "zero legs examined"

if [ "$violations" -gt 0 ]; then
  echo "nocache-stage-guard: $violations violation(s) across $legs leg(s)." >&2
  exit "$EXIT_VIOLATION"
fi

echo "nocache-stage-guard: $legs leg(s) checked, coupling holds."
exit "$EXIT_OK"
