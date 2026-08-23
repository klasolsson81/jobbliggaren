#!/usr/bin/env bash
# seq-retention-duration-guard.test.sh — fixtures for the #1170 guard.
#
# THE LAST TWO CASES PIN THE DELIVERY ITSELF, not a fixture of it: the real repo root, once by
# argument and once by the default. A workflow step that passes no argument must judge the same
# thing the explicit form does, or the step and this suite disagree silently.
#
# Every case that asserts 0 is as load-bearing as every case that asserts 1. A guard on prose
# that fires on honest text gets switched off, and a switched-off guard is worth less than none.
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/seq-retention-duration-guard.sh"
REPO_ROOT=$(cd -- "$script_dir/../.." && pwd)
readonly REPO_ROOT

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

fails=0
cases=0

# Build a throwaway project with the scoped paths tracked. Writes are done afterwards by
# `put`, then `stage` puts them in the index — `git ls-files --error-unmatch` reads the index.
mkproj() {
  local d="$TMPROOT/$1"
  mkdir -p "$d/deploy"
  : >"$d/BUILD.md"
  : >"$d/deploy/docker-compose.yml"
  : >"$d/docker-compose.yml"
  : >"$d/deploy/.env.example"
  git -C "$d" init -q
  git -C "$d" add -A >/dev/null
  printf '%s' "$d"
}

put() { cat >"$1/$2"; }
stage() { git -C "$1" add -A >/dev/null; }

run() {
  local name=$1 expected=$2 dir=$3
  local actual=0
  cases=$((cases + 1))
  bash "$SUT" "$dir" >"$TMPROOT/out.txt" 2>&1 || actual=$?
  if [ "$actual" -ne "$expected" ]; then
    fails=$((fails + 1))
    echo "FAIL  $name — expected exit $expected, got $actual"
    sed 's/^/        /' "$TMPROOT/out.txt"
  else
    echo "ok    $name (exit $actual)"
  fi
}

# ==========================================================================================
# 1. The honest shape passes
# ==========================================================================================
d=$(mkproj clean)
put "$d" BUILD.md <<'EOF'
- Seq self-hosted on the box. Retention: one policy, set BY HAND inside Seq; there is no
  environment variable for it. The duration lives in docs/runbooks/log-sink.md §4.
EOF
put "$d" deploy/docker-compose.yml <<'EOF'
# the seq service below (queryable, box-local, a retention policy set BY HAND inside Seq)
EOF
stage "$d"
run honest_mechanism_prose 0 "$d"

# ==========================================================================================
# 2. The three claims that actually shipped, each in its own shape
# ==========================================================================================
d=$(mkproj hist_compose)
put "$d" deploy/docker-compose.yml <<'EOF'
# each: the seq service below (queryable, box-local, 30-day retention policy set inside Seq);
EOF
stage "$d"
run historical_compose_claim 1 "$d"

# The one that defeats a LINE-scoped predicate: the duration line carries no "Seq" at all.
d=$(mkproj hist_verbless)
put "$d" BUILD.md <<'EOF'
    query-API:t på 80 svarar 401 utan autentisering och att 5341 bär 404 på query-vägen.
    Seq lyssnar för ingestion på 5341 och serverar sitt UI på 80.
    Försvaret är autentisering, inte topologi.
    Retention: en policy, 30 dagar.
EOF
stage "$d"
run historical_verbless_claim 1 "$d"

d=$(mkproj hist_count)
put "$d" BUILD.md <<'EOF'
- **Tre lager håller app-events, och bara två är åldersbundna:** Seq (30 d), off-box-arkivet
EOF
stage "$d"
run historical_count_claim 1 "$d"

# ==========================================================================================
# 3. Fresh wording — the point of grading the NUMBER rather than the grammar
# ==========================================================================================
d=$(mkproj fresh)
put "$d" BUILD.md <<'EOF'
The Seq corpus ages out after 30 days, so nothing older is queryable.
EOF
stage "$d"
run freshly_invented_claim 1 "$d"

# Seq's own TimeSpan wire form, which is how the runbook writes it.
d=$(mkproj timespan)
put "$d" deploy/docker-compose.yml <<'EOF'
# Seq retention is posted as {"RetentionTime":"30.00:00:00","RemovedSignalExpression":null}
EOF
stage "$d"
run timespan_wire_form 1 "$d"

# ==========================================================================================
# 4. Honest text that MUST NOT fire — the four ways this predicate could become useless
# ==========================================================================================
# (a) A retention duration with nothing to do with Seq. THE CASE HAS TO CROSS THE PREDICATE TO
#     MEAN ANYTHING: "90 dagars" must match DURATION_RE, so that passing measures Seq-ABSENCE
#     rather than a non-matching string failing to match. An earlier revision of this fixture was
#     vacuous for exactly that reason — the inflected form slipped the pattern, so the case
#     asserted nothing. The two probes below pin the crossing itself.
d=$(mkproj unrelated)
put "$d" BUILD.md <<'EOF'
`audit_log` är partitionerad per dag med 90 dagars retention och egen policyrad.
EOF
stage "$d"
run unrelated_retention_duration 0 "$d"

# ...and the SAME line with a Seq mention added must fire. If this passed, case (a) above would
# be proving nothing.
d=$(mkproj unrelated_crossed)
put "$d" BUILD.md <<'EOF'
Seq och `audit_log` är partitionerade per dag med 90 dagars retention.
EOF
stage "$d"
run unrelated_control_actually_crosses 1 "$d"

# (b) THE COLLISION TRAP. "27d" is this repo's verification-ROW identifier, and BUILD.md really
#     does carry one within three lines of a Seq retention sentence. An earlier revision of the
#     pattern fired here.
d=$(mkproj rowid)
put "$d" BUILD.md <<'EOF'
- **Tre lager är tänkta att hålla app-events, och åldersgränserna är mekanismer:** Seq
  (en handsatt policy), off-box-arkivet (en lifecycle-regel), och Dockers `json-file`.
- **Varaktighet** — kopian som ska överleva en root-angripare, och den egenskapen är inte
  i kraft förrän verifikationsrad 27d:s `Deny s3:DeleteObject` är applicerad.
EOF
stage "$d"
run row_identifier_collision 0 "$d"

# (c) The retraction form. It is this repo's house style and must survive — WITHOUT re-stating
#     the number. A retraction that quotes the duration is still a duration in the file, and
#     case (d) pins that it is treated as one.
d=$(mkproj retraction)
put "$d" deploy/docker-compose.yml <<'EOF'
# An earlier revision of this line said the Seq retention policy was "set inside Seq" — it was
# not, on any box, and nothing in this file could have told you otherwise (#1170). Whether it
# is in force is a measurement, and its home is log-sink.md §4.
EOF
stage "$d"
run retraction_without_the_number 0 "$d"

# (d) ...and the same retraction WITH the number quoted back fires, deliberately: a quoted
#     figure decays exactly like an asserted one.
d=$(mkproj retraction_quoting)
put "$d" deploy/docker-compose.yml <<'EOF'
# An earlier revision of this line called the Seq policy "30-day retention set inside Seq".
EOF
stage "$d"
run retraction_quoting_the_number 1 "$d"

# (e) The widening is deliberate and pinned here rather than left to be discovered: a
#     Seq-adjacent duration that is not retention at all fires too. Compaction's seven days
#     decays exactly like the retention number and belongs in the same one home.
d=$(mkproj seq_adjacent_nonretention)
put "$d" BUILD.md <<'EOF'
Seq reclaims disk through compaction, which runs at 7 days of file age.
EOF
stage "$d"
run seq_adjacent_non_retention_duration 1 "$d"

# ==========================================================================================
# 4b. INFLECTION AND CASE — the seven forms an earlier revision let through
# ==========================================================================================
# "30 dagars" is house idiom and appeared in THIS file while the guard could not see it.
i=0
for form in "30 dagars" "30 dygns" "30 dagarna" "30 DAGAR" "30 Days" "30 D" "4 veckor" \
            "6 månaders" "3 månaderna" "3 månadens" "3 månads" "3 Månader"; do
  i=$((i + 1))
  d=$(mkproj "inflect$i")
  printf 'Seq behåller sin korpus i %s innan den rensas.\n' "$form" >"$d/BUILD.md"
  stage "$d"
  run "inflected_form_${i}" 1 "$d"
done

# ==========================================================================================
# 4c. .env.example is in scope, and the underscore forms reach it
# ==========================================================================================
d=$(mkproj envfile)
put "$d" deploy/.env.example <<'EOF'
# Structured log sink. SEQ_SERVER_URL attaches the provider; retention is 30 dagar.
EOF
stage "$d"
run env_example_is_scoped 1 "$d"

# The env-key form must count as a Seq mention on its own — the underscore is deliberately NOT
# a boundary character. This is the probe that justified dropping it.
d=$(mkproj underscore_key)
put "$d" deploy/.env.example <<'EOF'
SEQ_SERVER_URL=http://seq:5341
filler one
filler two
The sink keeps events for 30 days.
EOF
stage "$d"
run underscore_env_key_counts_as_seq 1 "$d"

# ...and so must the volume name, which sits in compose's own Seq comment.
d=$(mkproj volume_token)
put "$d" deploy/docker-compose.yml <<'EOF'
  # seq_data holds the event store and is trimmed after 30 dagar.
EOF
stage "$d"
run seq_data_volume_counts_as_seq 1 "$d"

# THE CONTROL THAT MUST EXIST BECAUSE THE MECHANISM PROTECTING IT CHANGED. `enable_seqscan` was
# excluded by the underscore boundary before; now it survives only because the "s" of "scan"
# follows "seq" directly and fails the closing boundary. Different mechanism, same verdict.
d=$(mkproj seqscan)
put "$d" BUILD.md <<'EOF'
`CountWithBitmapPlanAsync` sätter `SET LOCAL enable_seqscan = off` för count-vägar,
och planen räknas om efter 30 dagar av statistikdrift.
EOF
stage "$d"
run enable_seqscan_is_not_a_seq_mention 0 "$d"

# The dev compose is the UPSTREAM SOURCE of the production claim, not a lesser copy of it.
d=$(mkproj rootcompose)
put "$d" docker-compose.yml <<'EOF'
  # seq (dev): UI on 5341, ingestion on 5342, 30 dagars retention.
EOF
stage "$d"
run root_compose_is_scoped 1 "$d"

# 4d. THE FALSE POSITIVES AN OPEN STEM PRODUCED — a diary is not a duration. The last four came
#     from an open PREFIX in front of the month stems; those words are already in BUILD.md, so
#     they were latent rather than absent.
i=0
for form in "30 dagboken" "30 dagbok" "3 monthly" "4 veckoschema" "30 skillnader" "30 kostnader" "3 byggnader" "5 mognad"; do
  i=$((i + 1))
  d=$(mkproj "notaduration$i")
  printf 'Seq skriver %s i sin egen katalog och det är inte en lagringstid.\n' "$form" >"$d/BUILD.md"
  stage "$d"
  run "open_stem_false_positive_${i}" 0 "$d"
done

# ==========================================================================================
# 5. The window is pinned, not assumed
# ==========================================================================================
d=$(mkproj win3)
put "$d" BUILD.md <<'EOF'
Seq is the queryable sink and its retention is a policy.
filler one
filler two
The corpus is trimmed to 30 dagar.
EOF
stage "$d"
run window_edge_exactly_three 1 "$d"

d=$(mkproj win4)
put "$d" BUILD.md <<'EOF'
Seq is the queryable sink and its retention is a policy.
filler one
filler two
filler three
The corpus is trimmed to 30 dagar.
EOF
stage "$d"
run window_edge_four_is_out_of_range 0 "$d"

# ==========================================================================================
# 6. Fail-closed — a scoped path that is not in the index refuses rather than passing
# ==========================================================================================
d=$(mkproj untracked_build)
rm -f "$d/BUILD.md"
git -C "$d" rm --cached BUILD.md >/dev/null 2>&1 || true
run missing_scoped_path_fails_closed 1 "$d"

d=$(mkproj untracked_compose)
git -C "$d" rm --cached deploy/docker-compose.yml >/dev/null 2>&1 || true
run untracked_scoped_path_fails_closed 1 "$d"

# .env.example carries the same refusal — a rename there must not pass vacuously either.
d=$(mkproj untracked_envfile)
git -C "$d" rm --cached deploy/.env.example >/dev/null 2>&1 || true
run untracked_env_example_fails_closed 1 "$d"

# ==========================================================================================
# 7. THE DELIVERY, NOT A FIXTURE OF IT
# ==========================================================================================
run real_repo_by_argument 0 "$REPO_ROOT"

cases=$((cases + 1))
actual=0
(cd "$REPO_ROOT" && bash "$SUT") >"$TMPROOT/out.txt" 2>&1 || actual=$?
if [ "$actual" -ne 0 ]; then
  fails=$((fails + 1))
  echo "FAIL  real_repo_by_default — expected exit 0, got $actual"
  sed 's/^/        /' "$TMPROOT/out.txt"
else
  echo "ok    real_repo_by_default (exit 0)"
fi

echo
if [ "$fails" -ne 0 ]; then
  echo "$fails of $cases case(s) FAILED"
  exit 1
fi
echo "all $cases cases passed"
