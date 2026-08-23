#!/usr/bin/env bash
# seq-retention-duration-guard.sh — BLOCKING (#1170).
#
# WHAT IT PINS: no artefact-bearing tracked file states a DURATION for Seq's retention policy.
# The duration has exactly one home, docs/runbooks/log-sink.md §4, next to the instrument that
# measures whether a policy exists at all. CLAUDE.md §5 already forbids "a live measured number
# in a tracked file — it decays within a commit or two", and ADR 0032 derived the same rule for
# this same class: "Never put a retention duration back into a `src/` comment. A rule living in
# thirteen places goes stale in twelve of them."
#
# WHY THE NUMBER AND NOT THE GRAMMAR. Before #1170 this repo carried "30-day retention policy set
# inside Seq" in deploy/docker-compose.yml and "bara två är åldersbundna: Seq (30 d)" in BUILD.md.
# Both were false on every box that ever ran, and neither was reachable by banning assertive verb
# forms: "Retention: en policy, 30 dagar" is VERBLESS and asserts anyway. Assertion is not a
# closed set; a duration literal is.
#
# ⚠ WHAT IT DOES NOT DO, AND THIS SENTENCE IS LOAD-BEARING: it pins the REPO'S CLAIMS, never the
# box. Green here means "no scoped artefact states a duration beside a Seq mention". It never means
# "retention is set" — measuring that is log-sink.md §4's row and nothing in CI can stand in for
# it. Confusing the guard with the measurement would rebuild the original defect inside its own
# remedy.
#
# SCOPE is a literal path list, not a glob: a glob that matches nothing passes vacuously. Both
# paths must be in the index and on disk or this refuses. log-sink.md is deliberately ABSENT —
# it is the home, it must carry the number, and it carries the curl that sets the policy. ADRs
# and docs/reviews/ are dated provenance (AGENTS.md §1.6), not live claims.
#
# EXIT: 0 the predicate holds · 1 it does not, or a scoped path is missing (fail-closed). There
# is deliberately no third value: the compose guards have one because they delegate to
# `docker compose config` + `jq`, an oracle that can be absent or refuse. This reads file bytes
# after `git ls-files`. There is no state in which it reads the files and cannot answer.
#
# USAGE: seq-retention-duration-guard.sh [root]
# With no argument it judges THE DELIVERY — this repo. The argument exists for the fixture
# suite, which builds throwaway git repos carrying the same two paths; the suite's last cases
# run both forms against the real root so a workflow step and the fixtures cannot disagree.
set -euo pipefail

root="${1:-}"
if [ -z "$root" ]; then
  root=$(git rev-parse --show-toplevel)
fi
cd -- "$root"

# The artefact-bearing files: compose is read as deployed configuration, BUILD.md as the spec.
SCOPED_PATHS=(
  "deploy/docker-compose.yml"
  "BUILD.md"
)

# Lines this many away on either side still count as the same claim. Measured on the pre-fix
# repo: a line-scoped predicate misses BUILD.md's "Retention: en policy, 30 dagar", whose only
# "Seq" sat three lines above it. The fixture suite pins this boundary rather than assuming it.
readonly WINDOW=3

# THE PREDICATE DOES NOT REQUIRE THE WORD "RETENTION", AND THAT WAS A MEASUREMENT, NOT A
# SIMPLIFICATION. An earlier revision required a retention token alongside the duration; the
# fixture "The Seq corpus ages out after 30 days" passed it — the claim, the number and the decay
# were all there and only the vocabulary was missing. Requiring the token was measured to buy no
# precision either: dropping it leaves the delivery green. So the predicate is a day-duration
# beside a Seq mention, full stop. A Seq-adjacent duration that is NOT retention — compaction's
# seven days of file age, say — fires too, and that is correct rather than collateral: it decays
# in exactly the same way and belongs in the same one home.
#
# Any count-plus-day-unit, not the literal 30: changing the number must not slip through. Third
# alternative is Seq's own TimeSpan wire form, e.g. 30.00:00:00.
#
# ⚠ A SEPARATOR IS REQUIRED BEFORE A SINGLE-LETTER UNIT, AND THAT IS A MEASURED UNDER-REACH RATHER
# THAN AN OVERSIGHT. Solid "27d" is this repo's verification-ROW identifier — BUILD.md:1497 carries
# "verifikationsrad 27d:s" three lines from a Seq retention sentence, and an earlier revision of
# this pattern fired on it. A guard that reports row numbers as durations gets switched off, so
# the collision is excluded rather than tolerated. The cost, stated so nobody discovers it as a
# surprise: a future solid "Seq retention 30d" in a scoped file passes. "30 d", "30-day",
# "30 dagar", "30days" and the TimeSpan form do not. The fixture suite pins both directions.
readonly DURATION_RE='[0-9]+[ -](d|dag|dagar|dygn|day|days)([^a-zA-Z0-9]|$)|[0-9]+(dag|dagar|dygn|day|days)([^a-zA-Z0-9]|$)|[0-9]+[.][0-9]{2}:[0-9]{2}:[0-9]{2}'
# Word-bounded so "consequence", "sequence" and "enable_seqscan" cannot trigger it. Case
# INSENSITIVE, which is stricter than a capitalised-only token and costs nothing once the
# boundaries are there: deploy/docker-compose.yml calls it "the seq service" in lower case.
readonly SEQ_RE='(^|[^a-zA-Z0-9_])[Ss][Ee][Qq]([^a-zA-Z0-9_]|$)'

status=0

for f in "${SCOPED_PATHS[@]}"; do
  if ! git ls-files --error-unmatch -- "$f" >/dev/null 2>&1; then
    echo "::error::scoped path is not tracked: $f — fail-closed. If it was renamed, rename it here too."
    exit 1
  fi
  if [ ! -f "$f" ]; then
    echo "::error::scoped path is tracked but absent from the working tree: $f — fail-closed."
    exit 1
  fi
done

echo "scope: ${SCOPED_PATHS[*]} (window +-${WINDOW} lines)"

for f in "${SCOPED_PATHS[@]}"; do
  findings=$(
    awk -v win="$WINDOW" -v dur="$DURATION_RE" -v seq="$SEQ_RE" '
      { line[NR] = $0 }
      END {
        for (i = 1; i <= NR; i++) {
          if (line[i] !~ dur) continue
          lo = i - win; if (lo < 1) lo = 1
          hi = i + win; if (hi > NR) hi = NR
          hasSeq = 0
          for (j = lo; j <= hi; j++) {
            if (line[j] ~ seq) hasSeq = 1
          }
          if (hasSeq) printf "%d:%s\n", i, substr(line[i], 1, 160)
        }
      }
    ' "$f"
  )
  if [ -n "$findings" ]; then
    status=1
    while IFS= read -r hit; do
      echo "::error file=$f,line=${hit%%:*}::states a duration beside a Seq mention. Delete the literal and leave the mechanism: the number belongs in docs/runbooks/log-sink.md §4 and nowhere else. ${hit#*:}"
    done <<<"$findings"
  fi
done

if [ "$status" -eq 0 ]; then
  echo "OK — no scoped file states a duration beside a Seq mention."
fi
exit "$status"
