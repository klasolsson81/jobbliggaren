#!/usr/bin/env bash
# audit-suppression-guard.sh — observe-only guard over the vuln gate's two
# escape hatches: `pnpm.auditConfig.ignoreGhsas` (accepted risk) and
# `pnpm.overrides` (repaired risk).
#
# WHY THIS EXISTS. ADR 0065 Amendment 2026-07-28 ratified both mechanisms and
# recorded, as a known gap, that neither is watched: a stale entry produces no
# warning and no exit difference, and a bare GHSA would silently cover a NEW
# path if the package re-entered the production tree. Measured at the time:
# an invented `ignoreGhsas` entry and an invented `overrides` key both exit 0
# with no output at all.
#
# WHO READS IT. `security-auditor`, per her audit area 8 and CLAUDE.md 9.2. That
# is not a formality: `dependabot-automerge.yml`'s own header records that no human
# reads observe-only audit at auto-merge, so a warning with no named reader would
# be the empty signal rather than a fix. The blocking gate cannot do this job
# itself -- it audits with the ignore list APPLIED, and is therefore structurally
# blind to an accepted advisory that has begun reaching production. This guard is
# the always-current measurement she consults, not the alarm.
#
# WHERE IT RUNS, AND WHY NOT ELSEWHERE. The observe-only `audit` job in
# build.yml — never inside `dependabot-automerge.yml`. A path-pinning assertion
# inside a blocking merge control is how the next deadlock gets built: it would
# fail a PR for something that PR did not cause, which is the exact defect the
# amendment exists to repair. So this NEVER exits non-zero on a finding; it
# emits `::warning::` and returns 0. The only non-zero exits are usage errors.
#
# WHAT IT CHECKS — three directions, because the reviewers found that the
# obvious two are the cheap half, and a fourth turned out to be unmeasurable:
#   1. STALE SUPPRESSION — a GHSA in `ignoreGhsas` matching no advisory. Costs
#      tidiness, not safety, but it is what makes "this list must shrink"
#      auditable at all.
#   2. OVER-BROAD SUPPRESSION — an accepted GHSA that now appears in the
#      PRODUCTION advisory set (`pnpm audit --prod`). This is the dangerous one:
#      acceptance was granted on a dev-only reachability argument, and a bare
#      GHSA does not re-check that argument. It asks pnpm for the partition
#      rather than parsing dependency paths — see the note at the check itself
#      for why the path-parsing version could never fire.
#   3. DEAD OVERRIDE — an `overrides` key naming a package absent from the
#      lockfile. A repair that silently stopped applying reads exactly like a
#      repair that is working.
#
# WHAT IT DELIBERATELY DOES NOT CHECK. A fourth direction was built and removed
# 2026-07-30 — ADR 0065 Beslut 6's silent pin-back. It is not lockfile-detectable:
# the signature is the opposite of what a floor comparison sees, and a true
# pin-back needs each consumer's declared range, which pnpm-lock v9 does not carry
# for transitive edges. The reasoning is at the override check below. That gap is
# recorded as OPEN in ADR 0065, not as watched.
#
# INPUTS are files, never the network, so the fixtures can drive every branch.
#   --package-json <f>  the manifest carrying pnpm.auditConfig / pnpm.overrides
#   --audit-json <f>      `pnpm audit --json` output taken WITHOUT the ignore list
#                         applied (otherwise the suppressed advisory is invisible
#                         and check 1 can never fire — the measurement must see
#                         what the suppression hides)
#   --audit-prod-json <f> the same, plus `--prod`. Supplies check 2's reachability
#                         partition. Also taken without the ignore list.
#   --lockfile <f>        pnpm-lock.yaml, for check 3
set -uo pipefail

USAGE="usage: $0 --package-json F --audit-json F --audit-prod-json F --lockfile F
                 [--pnpm-major N] [--workspace-yaml F]"
PKG=""; AUDIT=""; AUDIT_PROD=""; LOCK=""; PNPM_MAJOR=""; WS_YAML=""
while [ $# -gt 0 ]; do
  case "$1" in
    --package-json)    PKG="${2:-}"; shift 2 ;;
    --audit-json)      AUDIT="${2:-}"; shift 2 ;;
    --audit-prod-json) AUDIT_PROD="${2:-}"; shift 2 ;;
    --lockfile)        LOCK="${2:-}"; shift 2 ;;
    --pnpm-major)      PNPM_MAJOR="${2:-}"; shift 2 ;;
    --workspace-yaml)  WS_YAML="${2:-}"; shift 2 ;;
    *) echo "$USAGE" >&2; exit 2 ;;
  esac
done
[ -n "$PKG" ] && [ -n "$AUDIT" ] && [ -n "$AUDIT_PROD" ] && [ -n "$LOCK" ] || {
  echo "$USAGE" >&2; exit 2; }
for f in "$PKG" "$AUDIT" "$AUDIT_PROD" "$LOCK"; do
  [ -r "$f" ] || { echo "not readable: $f" >&2; exit 2; }
done
command -v jq >/dev/null 2>&1 || { echo "jq required" >&2; exit 2; }

# A FAILED AUDIT MUST NOT LAUNDER INTO A SECURITY INSTRUCTION.
# `pnpm audit` exits non-zero when advisories exist, so the caller cannot use the
# exit code to tell "found things" from "registry unreachable" — it needs
# `|| true`. That left the guard reading whatever landed in the file. Measured
# 2026-07-29: an empty file, a non-JSON error dump, and `{}` each produced
# `STALE SUPPRESSION: GHSA-mh99-v99m-4gvg … remove it` — an instruction to delete
# the house's only live acceptance, which would turn the blocking gate red. So the
# shape is asserted, and an unusable file is reported as SKIPPED, never as clean.
# The manifest gets the same treatment, and it is the input most easily missed:
# it always exists, so `jq … 2>/dev/null` on a truncated or non-JSON file yields
# an empty string that reads as "nothing configured". Measured 2026-07-30: a
# truncated `package.json` produced "nothing accepted", "nothing repaired" and
# "no findings". Two states, one output — the same laundering, third input.
if ! jq -e 'type == "object"' "$PKG" >/dev/null 2>&1; then
  echo "::warning::audit-suppression-guard SKIPPED — $PKG is not parseable JSON, so an empty pnpm block cannot be distinguished from an unreadable one. This is not a clean result."
  exit 0
fi
for f in "$AUDIT" "$AUDIT_PROD"; do
  if ! jq -e 'has("advisories")' "$f" >/dev/null 2>&1; then
    echo "::warning::audit-suppression-guard SKIPPED — $f is not usable pnpm audit JSON (no .advisories). This is not a clean result; the suppression checks did not run."
    exit 0
  fi
done

warn() { echo "::warning::$1"; FINDINGS=$((FINDINGS + 1)); }
note() { echo "::notice::$1"; }
skip() { echo "::warning::audit-suppression-guard SKIPPED — $1 This is not a clean result; the suppression checks did not run."; exit 0; }
FINDINGS=0

# THE GUARD MUST NOT REPORT CLEAN FROM A MANIFEST IT CANNOT SHOW pnpm READS.
#
# It reads `pnpm.auditConfig` / `pnpm.overrides` out of `package.json`. pnpm 11
# does not read that field at all (ADR 0065 Amendment 2026-07-28), and the
# ratified migration moves both settings to `pnpm-workspace.yaml`. Measured
# 2026-07-30 against `jq 'del(.pnpm)' package.json` — which is what pnpm 11
# effectively sees and what the migration produces — the guard printed "nothing
# accepted, nothing to watch", "nothing repaired", and "no findings", while one
# acceptance and eight override keys were fully live at the new location.
#
# That is the same defect as the audit-shape one above: two states, one output.
# So an empty `pnpm` block is only reportable as clean when nothing suggests the
# configuration lives elsewhere. Both probes below are VALUES and FILES, never a
# `pnpm --version` call — the guard takes no network and stays fixturable.
if [ -n "$PNPM_MAJOR" ] && [ "$PNPM_MAJOR" -ge 11 ] 2>/dev/null; then
  skip "pnpm major $PNPM_MAJOR does not read the \`pnpm\` field in package.json, which is the field this guard reads."
fi
if [ -n "$WS_YAML" ] && [ -r "$WS_YAML" ] \
   && grep -qE '^(overrides|auditConfig|ignoreGhsas):' "$WS_YAML" 2>/dev/null; then
  skip "\`$WS_YAML\` carries overrides/auditConfig at the top level, so the configuration has moved; this guard reads the manifest."
fi

# CR HANDLING, SCALED PER CALL SITE rather than claimed for all of them.
#
# What it defends against, measured 2026-07-29: the override floor arrived as
# `7.28.0\r`, so `7.28.0` != `7.28.0\r` and the pin-back warning fired on an EXACT
# match. The CR does NOT come from the input encoding — jq emits one CR byte even
# when fed a file containing zero, because the Windows binary translates its own
# stdout. `$( )` then eats only the LAST line's CR, so lines 1..n-1 keep theirs.
#
#   - the `overrides` read below: 8 keys, 7 of them carrying CR. It survives only
#     as the package-NAME lookup now — the version comparison the CR actually
#     corrupted is gone with check 4, so the false warnings it caused cannot
#     recur. Kept because a CR would still break an exact name match. No fixture,
#     and that is DECLARED: the cause is the jq binary's platform behaviour, which
#     no fixture can vary.
#   - the `ignoreGhsas` read below: also load-bearing, but only at two or more
#     entries, since `$( )` eats a single entry's CR. That IS fixturable without
#     touching any line endings — vary the CARDINALITY and let jq supply the CR.
#     `T2` does exactly that.
#
# A third call site read `dependencies` to classify production roots. It is gone:
# the check it fed now asks `pnpm audit --prod` instead. Its stated reason was also
# wrong and should not be resurrected — pnpm reports `metadata.devDependencies: 0`
# for this tree (a counter beside `metadata.dependencies: 1110`), but that is a
# COUNTER, not the filter, and
# `--prod`/`--dev` partition correctly (measured).
#
# On Linux CI jq emits LF and these `tr` calls are no-ops. The bug they prevent is
# visible only on a developer's Windows machine — which is the machine where
# someone might act on a false warning.

# ---- 1 + 2: the suppression list -------------------------------------------
IGNORED="$(jq -r '(.pnpm.auditConfig.ignoreGhsas // [])[]' "$PKG" 2>/dev/null | tr -d '\r')"
if [ -z "$IGNORED" ]; then
  if [ -z "$PNPM_MAJOR" ] && [ -z "$WS_YAML" ]; then
    warn "EMPTY CONFIG, UNVERIFIED LOCATION: no ignoreGhsas entries in this manifest — but neither --pnpm-major nor --workspace-yaml was supplied, so the guard cannot distinguish \"nothing is accepted\" from \"the configuration moved to pnpm-workspace.yaml and this manifest is no longer the place pnpm reads\". Those two states are indistinguishable from here."
  else
    note "no ignoreGhsas entries — nothing accepted, and the location is verified."
  fi
else
  while IFS= read -r ghsa; do
    [ -n "$ghsa" ] || continue
    hit="$(jq -r --arg g "$ghsa" \
      '[(.advisories // {})[] | select((.github_advisory_id // .id|tostring) == $g)] | length' \
      "$AUDIT" 2>/dev/null)"
    if [ "${hit:-0}" = "0" ]; then
      warn "STALE SUPPRESSION: $ghsa is in ignoreGhsas but matches no advisory in the tree. It suppresses nothing; remove it (ADR 0065 Amendment 2026-07-28: this list must shrink)."
      continue
    fi
    # The acceptance rests on reachability, not severity — so re-check reachability.
    #
    # THIS ASKS pnpm, IT DOES NOT PARSE PATHS. The first version walked
    # `findings[].paths` and matched the first hop against `dependencies`. It could
    # not fire: pnpm emits `.>root>child` — no spaces, no versions — so an
    # `-F ' > '` split yielded an empty root and every path was skipped. Worse, a
    # correct dev-only verdict and a total parse miss produced byte-identical
    # output, so the run that "verified" it could not tell them apart.
    # (An earlier draft also claimed `paths` was empty in this tree. That was
    # false — measured populated in four variants, `.>@lhci/cli>uuid` and the
    # rest. The separator alone carries the argument; the extra claim was a false
    # ornament beside a true reason, which is the defect class this repo keeps
    # paying for.)
    #
    # `pnpm audit --prod` does the partition properly. Measured on the same tree
    # with the ignore list removed: `--prod` → 1 advisory (`@hono/node-server`),
    # `--dev` → 2 (`uuid`, `brace-expansion`). So membership in the `--prod` set IS
    # the question, and it is format-independent.
    prod_hit="$(jq -r --arg g "$ghsa" \
      '[(.advisories // {})[] | select((.github_advisory_id // .id|tostring) == $g)] | length' \
      "$AUDIT_PROD" 2>/dev/null)"
    if [ "${prod_hit:-0}" != "0" ]; then
      warn "OVER-BROAD SUPPRESSION: $ghsa is accepted, but it now appears in the PRODUCTION dependency set (\`pnpm audit --prod\`). The acceptance was granted on a dev-only reachability argument; that argument no longer holds. Re-review it (security-auditor trigger, ADR 0065 Beslut 4)."
    else
      note "$ghsa: accepted, and absent from the --prod set — still dev-only reachable."
    fi
  done <<EOF
$IGNORED
EOF
fi

# ---- 3 + 4: the override block ---------------------------------------------
KEYS="$(jq -r '(.pnpm.overrides // {}) | to_entries[] | "\(.key)\t\(.value)"' "$PKG" 2>/dev/null | tr -d '\r')"
if [ -z "$KEYS" ]; then
  if [ -z "$PNPM_MAJOR" ] && [ -z "$WS_YAML" ]; then
    warn "EMPTY CONFIG, UNVERIFIED LOCATION: no overrides in this manifest, and the location is unverified — see the ignoreGhsas note above. An empty block and a moved block look identical from here."
  else
    note "no overrides — nothing repaired, and the location is verified."
  fi
else
  while IFS=$'\t' read -r key target; do
    [ -n "$key" ] || continue
    # `pkg` or `pkg@<range>`; the range may itself contain '@' only in scopes,
    # which always lead, so split on the LAST '@' that is not position 0.
    name="$key"
    case "${key#@}" in *@*) name="${key%@*}" ;; esac
    # PRESENCE ONLY — no version is extracted, deliberately.
    #
    # A fourth check used to live here: it compared each resolved version against
    # the open key's target floor and warned when one fell below, calling that ADR
    # 0065 Beslut 6's silent pin-back. It was removed 2026-07-30, on measurement:
    #   - it cannot detect a pin-back. The signature is the OPPOSITE — an override
    #     forces resolution TO the floor, so the resolved version lands >= floor,
    #     never below. On this tree `sharp` floor 0.35.0 resolves 0.35.3, and that
    #     is the ADR's own named instance.
    #   - a true pin-back needs each consumer's DECLARED range, which pnpm-lock v9
    #     does not carry for transitive edges (`next@16.2.11` records
    #     `sharp: 0.35.3(...)`, a resolved version). It is not lockfile-detectable
    #     at all, so no file-only guard can see it.
    #   - what it DID detect — a manifest declaring a repair the lockfile does not
    #     carry — is already caught, blockingly and upstream. Measured: raising one
    #     override target without regenerating the lockfile makes
    #     `pnpm install --frozen-lockfile` exit 1 with
    #     `ERR_PNPM_LOCKFILE_CONFIG_MISMATCH … "overrides" configuration doesn't
    #     match the value found in the lockfile`, in the REQUIRED `frontend` job.
    #     Reporting it again here, observe-only, is duplication.
    #   - and it was the entire false-positive surface: every false alarm across
    #     three review rounds (scoped-name swallow, version terminator, the CR bug)
    #     lived in the version extraction and the `sort -V` comparison it fed.
    #
    # So the awk below answers one question: does the lockfile carry this package at
    # all? Exact-name match only — a substring match made `postcss` swallow
    # `@tailwindcss/postcss` (measured), and the lockfile quotes scoped keys.
    if ! awk -v n="$name" '
      {
        line = $0
        sub(/^[[:space:]]+/, "", line)
        sub(/^'"'"'/, "", line)                     # lockfile may quote scoped keys
        if (index(line, n "@") != 1) next           # must start with the exact name
        if (substr(line, length(n) + 2) !~ /^[0-9]/) next  # `foo@workspace:` is not a version
        found = 1
      }
      END { exit !found }' "$LOCK" 2>/dev/null; then
      warn "DEAD OVERRIDE: key '$key' names a package absent from the lockfile. It repairs nothing; a repair that stopped applying reads exactly like one that works."
    fi
  done <<EOF
$KEYS
EOF
fi

if [ "$FINDINGS" -eq 0 ]; then
  note "audit-suppression-guard: no findings."
else
  echo "::notice::audit-suppression-guard: $FINDINGS finding(s) — observe-only, not a gate."
fi
exit 0
