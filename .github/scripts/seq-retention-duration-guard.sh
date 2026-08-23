#!/usr/bin/env bash
# seq-retention-duration-guard.sh — BLOCKING (#1170).
#
# WHAT IT PINS: no artefact-bearing tracked file states a DURATION for Seq's retention policy.
# The duration has exactly one home — docs/runbooks/log-sink.md **§3 step 7**, the operator step
# that sets the policy and carries the number in the curl body. §4 is where the neighbouring
# question lives (whether any policy exists on a given box), and it deliberately carries no
# duration of its own. Do not merge the two pointers: §4's rows also carry the `hostlogs/`
# lifecycle numbers, which belong to a different mechanism entirely.
#
# CLAUDE.md §5 already forbids "a live measured number in a tracked file — it decays within a
# commit or two", and ADR 0032 derived the same rule for this same class: "Never put a retention
# duration back into a `src/` comment. A rule living in thirteen places goes stale in twelve."
#
# WHY THE NUMBER AND NOT THE GRAMMAR. Before #1170 this repo carried "30-day retention policy set
# inside Seq" in deploy/docker-compose.yml and "bara två är åldersbundna: Seq (30 d)" in BUILD.md.
# Both were false on every box that ever ran, and neither was reachable by banning assertive verb
# forms: "Retention: en policy, 30 dagar" is VERBLESS and asserts anyway. Assertion is not a
# closed set; a duration literal is.
#
# ⚠ WHAT IT DOES NOT DO, AND THIS SENTENCE IS LOAD-BEARING: it pins the REPO'S CLAIMS, never the
# box. Green here means "no scoped artefact states a duration beside a Seq mention". It never
# means "retention is set" — measuring that is log-sink.md §4's row, and nothing in CI can stand
# in for it. Confusing the guard with the measurement would rebuild the original defect inside
# its own remedy.
#
# SCOPE is a literal path list, not a glob: a glob that matches nothing passes vacuously. Every
# path must be in the index and on disk or this refuses. log-sink.md is deliberately ABSENT — it
# is the home, it must carry the number, and it carries the curl that sets the policy. ADRs and
# docs/reviews/ are dated provenance (AGENTS.md §1.6), not live claims.
#
# EXIT: 0 the predicate holds · 1 it does not, or a scoped path is missing (fail-closed). There
# is deliberately no third value: the compose guards have one because they delegate to
# `docker compose config` + `jq`, an oracle that can be absent or refuse; this reads file bytes
# after `git ls-files`. That is a design choice about the ORACLE and not a claim that no failure
# exists — `git rev-parse` outside a repository exits 128 and an awk without interval-expression
# support exits 2, and both surface as a failed step rather than a silent pass, which is the
# property that matters.
#
# USAGE: seq-retention-duration-guard.sh [root]
# With no argument it judges THE DELIVERY — this repo. The argument exists for the fixture
# suite, which builds throwaway git repos carrying the same paths; the suite's last cases run
# both forms against the real root so a workflow step and the fixtures cannot disagree.
set -euo pipefail

root="${1:-}"
if [ -z "$root" ]; then
  root=$(git rev-parse --show-toplevel)
fi
cd -- "$root"

# The artefact-bearing files: the two compose files are read as deployed configuration, BUILD.md
# as the spec, and .env.example as the box's required-keys template — the same SSOT role
# CLAUDE.md §11 gives appsettings.Local.json.example. An operator reads all four as truth about
# what runs.
#
# ⚠ THE FOUR ARE NOT FOUR Art. 5(1)(e) SURFACES, AND THE GUARD MUST NOT IMPLY THEY ARE. The
# criterion is artefact-bearing plus §5's decay rule, both domain-agnostic; the GDPR weight
# attaches to `deploy/` specifically, because that is the box a data subject's log lines land on.
#
# The root (dev) compose is in scope for a positive reason rather than by symmetry: the two
# compose files are the pair most likely to copy prose into each other, so a dev-side "a 30-day
# retention policy set inside Seq" is the upstream SOURCE of the production claim this guard
# exists to stop. Gating only the destination leaves the origin open. A scope reading "every
# compose file, the spec and the env template, except one compose file" would also need an
# exception rule, and exception rules are what erode.
SCOPED_PATHS=(
  "deploy/docker-compose.yml"
  "docker-compose.yml"
  "BUILD.md"
  "deploy/.env.example"
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
# THE UNIT IS A STEM WITH AN OPEN TAIL, AND THAT IS THE REPAIR OF A MEASURED FAIL-OPEN. An
# earlier revision required a non-alphanumeric boundary right after the unit and matched
# case-sensitively, so SEVEN forms this repo actually writes slipped through: "30 dagars",
# "30 dygns", "30 dagarna", "30 DAGAR", "30 Days", "30 D", and every week/month form. "30 dagars"
# is house idiom (docs/runbooks/release-checklist.md) and appeared in this guard's OWN fixture
# file while the guard could not see it. Matching now runs against tolower(), and each unit is a
# stem that may carry any inflection. The `[^ ]*` before the stem is what reaches "30 månader":
# its "må" is non-ASCII, and matching a byte run rather than a letter class keeps that working
# under LC_ALL=C, where a UTF-8 letter class would not.
#
# ⚠ A SEPARATOR IS REQUIRED BEFORE A BARE SINGLE-LETTER "d", AND THAT IS A MEASURED UNDER-REACH
# RATHER THAN AN OVERSIGHT. Solid "27d" is this repo's verification-ROW identifier — BUILD.md
# carries "verifikationsrad 27d:s" three lines from a Seq retention sentence, and an earlier
# revision of this pattern reported it as a duration. A guard that reports row numbers as
# durations gets switched off, so the collision is excluded rather than tolerated. The cost,
# stated so nobody discovers it as a surprise: a future solid "Seq retention 30d" in a scoped
# file passes. "30 d", "30 D", "30-day", "30 dagar", "30 dagars", "30 dagarna", "30 dygns",
# "30days", "4 veckor", "3 månader" and the TimeSpan form do not. The fixture suite asserts each
# of those INDIVIDUALLY rather than letting them sit in a corpus: a guard cannot see its own
# under-reach while every item in a fixture file passes, which is precisely how "30 dagars" came
# to live in this suite for a revision without ever firing.
# ⚠ THE INFLECTIONS ARE ENUMERATED AND THE SPACED FORM KEEPS ITS CLOSING BOUNDARY, BECAUSE AN
# OPEN SUFFIX THERE IS A DIFFERENT DEFECT. A bare stem plus an open tail was tried and measured
# to fire on "30 dagboken", "30 dagbok", "3 monthly" and "4 veckoschema" — a guard that reports
# a diary as a duration gets switched off exactly like one that reports a row number. The open
# tail survives only on the SOLID form (`30days`), where the digit prefix makes a collision
# implausible; that looseness is named here rather than discovered later.
readonly DURATION_RE='[0-9]+[ -][^ ]*(dagarna|dagars|dagar|dagen|dag|dygnens|dygns|dygn|days|day|veckorna|veckors|veckor|veckan|vecka|weeks|week|manaderna|manaders|manader|manaden|manad|months|month|nader|nad)([^a-z0-9]|$)|[0-9]+(dag|dygn|day|veck|week|month)[a-z]*|[0-9]+[ -]d([^a-z0-9]|$)|[0-9]+[.][0-9]{2}:[0-9]{2}:[0-9]{2}'

# Case-insensitive and bounded by non-alphanumerics. UNDERSCORE IS *NOT* A BOUNDARY CHARACTER
# HERE, DELIBERATELY: the env-key forms `SEQ_SERVER_URL`, `SEQ_INGEST_API_KEY`, `Seq__ServerUrl`
# and the volume `seq_data` are all genuinely about Seq, and excluding underscore left exactly
# one line of .env.example's Seq section unreachable. Measured across all scoped files: there is
# no non-Seq `seq`-with-underscore token, so admitting them costs no false positives.
#
# What that changes about the collisions, because the credit moved: "consequence" and "sequence"
# are still excluded by the boundaries — `seq` inside them is flanked by letters. `enable_seqscan`
# is NOT excluded by the underscore any more; it survives because the `s` of "scan" follows `seq`
# directly and fails the closing boundary. Different mechanism, same verdict — and the fixture
# suite carries that case precisely because the mechanism protecting it changed.
readonly SEQ_RE='(^|[^a-zA-Z0-9])[Ss][Ee][Qq]([^a-zA-Z0-9]|$)'

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
          if (tolower(line[i]) !~ dur) continue
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
      echo "::error file=$f,line=${hit%%:*}::states a duration beside a Seq mention. Delete the literal and leave the mechanism: the number belongs in docs/runbooks/log-sink.md §3 step 7 and nowhere else. ${hit#*:}"
    done <<<"$findings"
  fi
done

if [ "$status" -eq 0 ]; then
  echo "OK — no scoped file states a duration beside a Seq mention."
fi
exit "$status"
