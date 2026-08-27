#!/usr/bin/env bash
#
# nocache-stage-guard.sh — the `images` matrix's `nocache` field must name the stage that
# actually upgrades OS packages, that stage must exist, and the field must reach buildx.
#
# WHY THIS EXISTS, and why a comment was not enough. #1517 repaired a blocking CVE gate by
# passing `no-cache-filters` to two of five `images` legs, because a cached
# `RUN apk --no-cache upgrade` layer had been serving stale packages for months WHILE THE
# DOCKERFILE CARRIED THE CORRECT REPAIR. That repair is a string in one file matching a stage
# name in another, wired through a third — and nothing checked them together.
#
# BUILDKIT WILL NOT CHECK IT, and that is deliberate on its part. Measured 2026-08-27 two
# independent ways: in buildkit's source the no-cache lookup compares per stage with no
# reverse validation that each requested name matched something, while `resolveTarget` in the
# same package errors with suggestions on an unknown `--target`; and against buildx v0.29.1,
# where a deliberately misspelled filter left the stage CACHED, exit 0, zero warnings.
#
# `build.yml` already condemns this exact shape 600 lines up: "A GLOB THAT MATCHES NOTHING
# PASSES VACUOUSLY ... turn this gate into a green no-op that reads as coverage."
#
# WHAT THE FAILURE COSTS, stated as narrowly as it is true. Mostly a FALSE FAIL rather than a
# false pass: the build uses `load: true` and Trivy scans the image the build step just
# produced, so scan target and artefact are the same object and a stale image is measured
# against a fresh DB. What a disarmed filter usually buys is CI wedged on an alarm that reads
# like an unrelated new advisory, with the real cause invisible. THE BOUND MATTERS THOUGH, and
# `security-auditor` qualified her own correction to say so: the Trivy step is
# `severity: HIGH,CRITICAL` with `ignore-unfixed: true`, so staleness that is entirely below
# HIGH, or that has no published fix, passes green either way. So: never a false pass FOR THE
# CLASS THIS GATE BLOCKS ON, which is not the same as never a false pass.
#
# FOUR THINGS IT CHECKS. The first three are the coupling; the fourth is the reader itself.
#   1. Every leg whose Dockerfile upgrades OS packages carries a `nocache` — for the verbs
#      the Dockerfile reader models, which is NOT every verb. See the limitation below.
#   2. Every non-empty `nocache` names a stage that EXISTS, and specifically the stage the
#      upgrade RUN lives in. Naming a real-but-wrong stage passed the first version of this
#      guard while buildkit un-cached something else entirely.
#   3. The workflow actually wires `matrix.nocache` into `no-cache-filters`. Delete that one
#      line and the whole repair goes inert — measured, and the first version of this guard
#      answered "coupling holds" to it.
#   4. The matrix reader saw every row that is there. See below, because this is the one that
#      nearly shipped as decoration.
#
# THE READER IS ANCHORED AND THAT IS A LIMITATION, NOT A DESIGN WIN. Matrix rows are matched
# by the exact one-line flow-mapping spelling used today. A row written any other legal way
# matches nothing — and the first version of this guard treated only the ALL-ROWS case as a
# refusal, so a HETEROGENEOUS matrix (one row reshaped, or one row added in block form) was
# read partially and reported "coupling holds". Three reviewers measured that independently.
# So the count is checked FIRST: rows matched must equal sequence entries declared, and any
# shortfall is a refusal. That is the same floor `build.yml`'s `deploy/systemd` gate already
# applies to its own glob, for the same stated reason.
#
# AND THERE IS A SECOND READER WITH A SECOND BLIND SPOT, declared here because the first
# version declared only the matrix one — and the two fail in OPPOSITE directions, which is what
# makes the silence dangerous. The matrix reader fails CLOSED: a shape it cannot read makes the
# count disagree and the guard refuses. The Dockerfile reader fails OPEN: a shape it cannot read
# is simply not an upgrade, and the leg passes. Known unmodelled shapes, measured 2026-08-27 and
# none of them present in this repo's five Dockerfiles: heredoc (RUN <<EOF), exec-form
# (RUN ["apk","upgrade"]), and any package manager outside apk/apt/apt-get/dnf/microdnf/yum/
# zypper. Reachability is zero today; the point is that the claim is bounded rather than the gap
# hidden (security-auditor, 2026-08-27).
#
# THREE OUTCOMES, NEVER COLLAPSED — the house rule (cf. compose-loopback-guard.sh):
#   exit 0 — read everything, and the coupling holds.
#   exit 1 — read everything, and it does not. Names the leg and what is wrong.
#   exit 2 — could not answer. "The guard could not run" must never be indistinguishable
#            from "the guard passed", which is why a missing workflow, a partially readable
#            matrix, a missing Dockerfile, an omitted field, an unreadable stage list and a
#            missing buildx wiring all exit 2 rather than 0.

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

# --- check 3: the field must actually reach buildx -----------------------------------------
# Without this line every other check in this file is theatre: the matrix can be perfect and
# buildx never receives a filter. Measured 2026-08-27 — deleting it made the previous version
# of this guard answer "coupling holds" with exit 0.
wiring_hits=$(grep -cE '^[[:space:]]*no-cache-filters:[[:space:]]*\$\{\{[[:space:]]*matrix\.nocache[[:space:]]*\}\}' "$WORKFLOW" || true)
[ "$wiring_hits" = "1" ] \
  || refuse "expected exactly one 'no-cache-filters: matrix.nocache' line in $WORKFLOW, found $wiring_hits. Zero means every check below is inert; more than one means this guard cannot tell which step is actually wired."

# --- check 4: the reader must have seen every row ------------------------------------------
declared=$(awk '
  /^      matrix:[[:space:]]*$/             { in_m = 1; next }
  in_m && /^        include:[[:space:]]*$/  { in_i = 1; next }
  in_i && /^[[:space:]]*$/                 { next }
  in_i && /^[[:space:]]*#/                 { next }
  in_i && /^          - /                   { n++; next }
  in_i && !/^          /                    { in_i = 0; in_m = 0 }
  END { print n + 0 }
' "$WORKFLOW")

rows=$(awk '
  /^          - \{ name: / {
    line = $0
    name = ""; file = ""; nocache = ""; has_nocache = 0
    if (match(line, /name: [A-Za-z0-9_-]+/))  { name = substr(line, RSTART + 6, RLENGTH - 6) }
    if (match(line, /file: "[^"]*"/))         { file = substr(line, RSTART + 7, RLENGTH - 8) }
    if (match(line, /nocache: "[^"]*"/))      { nocache = substr(line, RSTART + 10, RLENGTH - 11); has_nocache = 1 }
    printf "%s\t%s\t%d\t%s\n", name, file, has_nocache, nocache
  }
' "$WORKFLOW")

matched=0
[ -n "$rows" ] && matched=$(printf '%s\n' "$rows" | grep -c '' || true)

[ "$declared" -gt 0 ] || refuse "no matrix sequence entries found in $WORKFLOW — the shape changed, and a zero-row sweep passes vacuously"
[ "$matched" = "$declared" ] || refuse "matrix declares $declared entries but the reader matched $matched — a row is spelled in a way this anchor does not read, and a partially-read matrix reports coverage it does not have"

violations=0
legs=0

# TAB IS IFS WHITESPACE, so a run of tabs collapses to ONE delimiter and an empty field in the
# middle silently shifts every later field left. The possibly-empty `nocache` is therefore
# emitted LAST, where emptiness reads as emptiness. Pinned by the `empty_nocache_not_swallowed`
# fixture, which failed against this guard's first version.
while IFS=$'\t' read -r name file has_nocache nocache; do
  [ -n "$name" ] || refuse "a matrix row parsed with no name — cannot vouch for this shape"
  [ -n "$file" ] || refuse "leg '$name' parsed with no file — cannot vouch for this shape"
  [ "$has_nocache" = "1" ] || refuse "leg '$name' has no nocache field — every leg must state one, empty or not, so omission cannot read as 'no upgrade'"

  dockerfile="$REPO_ROOT/$file"
  [ -f "$dockerfile" ] || refuse "leg '$name' points at a missing Dockerfile: $file"
  legs=$((legs + 1))

  # Parse the Dockerfile into stages, and bind every package-upgrade RUN to the stage that
  # CONTAINS it. Line continuations are folded first: a `RUN` whose upgrade sits on a
  # continuation line was invisible to the previous version, and this repo already writes
  # multi-line RUN in three places.
  parsed=$(awk '
    function flush(l,   low, nm, rest) {
      low = tolower(l)
      if (low ~ /^[[:space:]]*from[[:space:]]/) {
        rest = l
        sub(/^[[:space:]]*[Ff][Rr][Oo][Mm][[:space:]]+/, "", rest)
        while (rest ~ /^--[^[:space:]]+[[:space:]]+/) { sub(/^--[^[:space:]]+[[:space:]]+/, "", rest) }
        nm = ""
        if (match(tolower(rest), /[[:space:]]as[[:space:]]+[a-z0-9_.-]+[[:space:]]*$/)) {
          nm = substr(rest, RSTART, RLENGTH)
          sub(/^[[:space:]]*[Aa][Ss][[:space:]]+/, "", nm)
          gsub(/[[:space:]]/, "", nm)
        }
        if (nm == "") { nm = "«unnamed-" idx "»" }
        idx++
        cur = tolower(nm)
        print "STAGE\t" cur
        return
      }
      # Verb sets differ PER MANAGER and that is not tidiness. For apk and apt-get, `update`
      # is a pure index refresh and must NOT match. For the yum family `update` IS an alias
      # for `upgrade` — v1 caught `dnf -y update` and an earlier version of this file lost it
      # by treating the alias as merely non-canonical (`code-reviewer`, 2026-08-27).
      is_up = 0
      if (low ~ /(^|[^a-z-])(apk|apt|apt-get)[[:space:]][^|;&]*(upgrade|dist-upgrade)/) { is_up = 1 }
      if (low ~ /(^|[^a-z-])(dnf|microdnf|yum)[[:space:]][^|;&]*(upgrade|update)/) { is_up = 1 }
      if (low ~ /(^|[^a-z-])zypper[[:space:]][^|;&]*([[:space:]](up|dup|patch)([[:space:]]|$)|upgrade|update)/) { is_up = 1 }
      if (low ~ /^[[:space:]]*run[[:space:]]/ && is_up) {
        print "UPGRADE\t" cur "\t" lineno
      }
    }
    BEGIN { idx = 0; cur = "" }
    {
      l = $0; sub(/\r$/, "", l)
      if (cont) { l = acc " " l } else { lineno = NR }
      if (l ~ /\\[[:space:]]*$/) { sub(/\\[[:space:]]*$/, "", l); acc = l; cont = 1; next }
      cont = 0; acc = ""
      flush(l)
    }
    END { if (cont) flush(acc) }
  ' "$dockerfile")

  stages=$(printf '%s\n' "$parsed" | awk -F'\t' '$1 == "STAGE" { print $2 }')
  upgrades=$(printf '%s\n' "$parsed" | awk -F'\t' '$1 == "UPGRADE" { print $2 "\t" $3 }')

  # An unreadable stage list is a REFUSAL, not a violation: "I could not read any stages" is a
  # different claim from "that stage does not exist", and only one of them is about the matrix.
  if [ -n "$nocache" ] && [ -z "$stages" ]; then
    refuse "leg '$name' filters '$nocache' but no stage could be read from $file — the guard cannot tell a rename from an unreadable file"
  fi

  # `no-cache-filters` is a LIST input, so a comma-separated value is legal and each element
  # is checked on its own.
  filtered=$(printf '%s' "$nocache" | tr ',' '\n' | sed 's/^[[:space:]]*//; s/[[:space:]]*$//' | tr '[:upper:]' '[:lower:]' | grep -v '^$' || true)

  # --- check 2a: every filtered name must name a real stage (the rename catch) --------------
  # FIXED-STRING, and case normalised by hand rather than with `grep -i`. Without `-F` a
  # value like `runtime*` is read as a regex, matches `runtime`, and the guard reports
  # "coupling holds" while buildkit — which compares with strings.EqualFold — matches
  # nothing and leaves the layer cached. Both silent. And `-i -F` TOGETHER aborts
  # (SIGABRT, exit 134) on GNU grep 3.0 as shipped with Git for Windows, while working
  # on the CI runner — a guard that only crashes on a developer's machine is worse than
  # one that crashes everywhere, so the case fold is done in the shell and never by grep.
  if [ -n "$filtered" ]; then
    while IFS= read -r want; do
      printf '%s\n' "$stages" | grep -qxF -- "$want" || violate "leg '$name' filters stage '$want', which does not exist in $file. Buildkit ignores this silently, so the filter is inert and the upgrade layer is served from cache. Stages present: $(printf '%s ' $stages)"
    done <<< "$filtered"
  fi

  # --- check 1 + 2b: every upgrading stage must be among the filtered ones ------------------
  # Not "an upgrade exists somewhere and a filter exists somewhere" — the previous version
  # asked only that, and passed a Dockerfile upgrading in `deps` while filtering `runtime`.
  if [ -n "$upgrades" ]; then
    while IFS=$'\t' read -r ustage uline; do
      [ -n "$ustage" ] || continue
      if [ -z "$filtered" ]; then
        violate "leg '$name' upgrades OS packages in stage '$ustage' ($file:$uline) but carries no nocache entry, so that layer is cached and goes stale"
      elif ! printf '%s\n' "$filtered" | grep -qxF -- "$ustage"; then
        violate "leg '$name' upgrades OS packages in stage '$ustage' ($file:$uline) but filters '$nocache' — buildkit un-caches the named stage and not this one, so the upgrade layer is still served from cache"
      fi
    done <<< "$upgrades"
  fi
done <<< "$rows"

[ "$legs" -gt 0 ] || refuse "zero legs examined"

if [ "$violations" -gt 0 ]; then
  echo "nocache-stage-guard: $violations violation(s) across $legs leg(s)." >&2
  exit "$EXIT_VIOLATION"
fi

echo "nocache-stage-guard: $legs leg(s) checked ($declared declared, $matched read), coupling holds."
exit "$EXIT_OK"
