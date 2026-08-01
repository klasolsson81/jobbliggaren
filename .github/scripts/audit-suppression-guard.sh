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
# 2026-07-30 — ADR 0065 Beslut 6's silent pin-back. It is not detectable from the
# LOCKFILE: the signature is the opposite of what a floor comparison sees, and a
# true pin-back needs each consumer's declared range, which pnpm-lock v9 does not
# carry for transitive edges. Read that scope literally rather than as "unclosable":
# it is unclosable for a FILE-ONLY guard, which is what this is by construction, and
# an installed tree does exist at the call site — `audit` runs
# `pnpm install --frozen-lockfile` before this. The reasoning is at the override
# check below. That gap is recorded as OPEN in ADR 0065, not as watched.
#
# INPUTS are files, never the network, so the fixtures can drive every branch.
#   --package-json <f>  the manifest carrying pnpm.auditConfig / pnpm.overrides
#   --audit-json <f>      `pnpm audit --json` output taken WITHOUT the ignore list
#                         applied (otherwise the suppressed advisory is invisible
#                         and check 1 can never fire — the measurement must see
#                         what the suppression hides)
#   --audit-prod-json <f> the same, plus `--prod`. Supplies check 2's partition —
#                         DECLARED dependencies, which is not the same as runtime
#                         reachability; see the note at the check. Also taken
#                         without the ignore list.
#   --lockfile <f>        pnpm-lock.yaml, for check 3
set -uo pipefail

USAGE="usage: $0 --package-json F --audit-json F --audit-prod-json F --lockfile F
                 [--pnpm-major N] [--workspace-yaml F]"
PKG=""; AUDIT=""; AUDIT_PROD=""; LOCK=""; PNPM_MAJOR=""; WS_YAML=""
# Tracked separately from the value, because "not supplied" and "supplied empty" are
# different states that an empty string cannot tell apart — and they were being
# collapsed. See the location section below.
PNPM_MAJOR_SET=""
while [ $# -gt 0 ]; do
  case "$1" in
    --package-json)    PKG="${2:-}"; shift 2 ;;
    --audit-json)      AUDIT="${2:-}"; shift 2 ;;
    --audit-prod-json) AUDIT_PROD="${2:-}"; shift 2 ;;
    --lockfile)        LOCK="${2:-}"; shift 2 ;;
    --pnpm-major)      PNPM_MAJOR="${2:-}"; PNPM_MAJOR_SET=1; shift 2 ;;
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
# `has("advisories")` was the wrong assertion, and it was wrong in this guard's own
# defect class. `has` tests for the KEY, so it is satisfied by `"advisories": null`,
# `"boom"`, `123` and `true`. The reads below then iterate a non-iterable, jq exits
# 5, stderr is discarded, and `${prod_hit:-0}` turns the FAILED READ into the number
# zero. Measured 2026-08-01, 4 of 4 such values: the positive claim "absent from the
# --prod set — still dev-only reachable", byte-identical to a healthy run, on the
# check this guard itself calls the dangerous one. Absence read as fact, inside the
# guard against it. So assert the VALUE, not the key.
for f in "$AUDIT" "$AUDIT_PROD"; do
  if ! jq -e '.advisories | type == "object" or type == "array"' "$f" >/dev/null 2>&1; then
    echo "::warning::audit-suppression-guard SKIPPED — $f is not usable pnpm audit JSON: .advisories is absent or is not a container these checks can iterate. This is not a clean result; the suppression checks did not run."
    exit 0
  fi
done
# And a --prod file can be well-formed yet meaningless. Measured 2026-07-30: one
# that resolved an EMPTY tree (`metadata.dependencies: 0`) produced the POSITIVE
# claim "absent from the --prod set — still dev-only reachable", byte-identical to
# the healthy run. Shape alone does not carry it; the partition has to have
# partitioned something.
#
# The counter is consulted only when BOTH files carry it. Absence is not evidence of
# an empty tree — the fixtures omit `metadata` entirely, and reading absence as zero
# made every one of them skip: the same absence-as-fact error, caught by the fixtures
# in the one edit where I would have sworn it could not happen.
#
# But requiring both was itself a bypass, and it went the dangerous way. Measured
# 2026-08-01: a --prod file carrying `dependencies: 0` — exactly the state this check
# exists to catch — passes silently to "still dev-only reachable" the moment the FULL
# file omits the counter. One-sided absence is now its own SKIPPED rather than a
# fall-through, so the three states are separated: both present means compare,
# exactly one present means the comparison is impossible, neither means a fixture
# that legitimately has no counter.
# INTEGER, not `numbers`. jq's `numbers` admits floats and `[ ]` does not, so a
# fractional counter made both tests true, then made both `[ ]` comparisons error to
# stderr and evaluate false — and the guard fell through to the whole positive
# "absent from the --prod set" claim. Measured with 0.5 against 100.5. Not reachable
# from pnpm output; reachable from anything else that writes this file.
# PRESENCE and USABILITY are separate questions, and collapsing them re-created the
# bug twice. Absent on both sides is the fixture case and proceeds. Present anywhere
# means the counter is claimed, and then BOTH sides must carry a whole number or the
# comparison is impossible — one-sided absence used to fall through (a --prod file
# reporting 0 passed silently whenever the full file omitted the counter), and a
# FRACTIONAL counter did the same by a different route: jq's `numbers` admits floats
# while `[ ]` does not, so both comparisons errored to stderr and evaluated false.
# Measured, 0.5 against 100.5. Neither is reachable from pnpm output; both are
# reachable from anything else that writes these files.
_pd_present=""; _fd_present=""; _pd_ok=""; _fd_ok=""
jq -e '.metadata.dependencies != null' "$AUDIT_PROD" >/dev/null 2>&1 && _pd_present=1
jq -e '.metadata.dependencies != null' "$AUDIT"      >/dev/null 2>&1 && _fd_present=1
jq -e '.metadata.dependencies | type == "number" and . == floor' "$AUDIT_PROD" >/dev/null 2>&1 && _pd_ok=1
jq -e '.metadata.dependencies | type == "number" and . == floor' "$AUDIT"      >/dev/null 2>&1 && _fd_ok=1
if [ -n "$_pd_present" ] || [ -n "$_fd_present" ]; then
  if [ -z "$_pd_ok" ] || [ -z "$_fd_ok" ]; then
    echo "::warning::audit-suppression-guard SKIPPED — metadata.dependencies is claimed by at least one audit file but is missing or not a whole number on one side, so the partition cannot be verified. A --prod run that resolved an empty tree would read as clean here. This is not a clean result."
    exit 0
  fi
  _pd="$(jq -r '.metadata.dependencies' "$AUDIT_PROD")"
  _fd="$(jq -r '.metadata.dependencies' "$AUDIT")"
  if [ "$_pd" -le 0 ] || [ "$_pd" -gt "$_fd" ]; then
    echo "::warning::audit-suppression-guard SKIPPED — the --prod audit reports $_pd dependencies against $_fd in the full audit, so it did not partition a real tree. This is not a clean result."
    exit 0
  fi
fi

warn() { echo "::warning::$1"; FINDINGS=$((FINDINGS + 1)); }
note() { echo "::notice::$1"; }
skip() { echo "::warning::audit-suppression-guard SKIPPED — $1 This is not a clean result; the suppression checks did not run."; exit 0; }
FINDINGS=0

# The manifest type check above asserts the TOP level only, and that is one level too
# shallow. Measured 2026-08-01: a manifest whose `pnpm.overrides` is the string
# "garbage" produced "no overrides — nothing repaired, and the location is verified"
# plus "no findings" — byte-identical to a healthy run, because jq errors on the
# read and an empty result reads as "nothing configured". Same for `ignoreGhsas` as
# a string. Two states, one output, one level down: the fourth input to carry this
# guard's own defect class. Assert the sub-blocks the reads actually consume.
# Written as "absent, or the right type" rather than `(x // {}) | type == "object"`.
# The `//` form is one value class short: it fires on `null` AND on `false`, so
# `"overrides": false` defaulted to `{}` and reported "nothing repaired" — the same
# laundering the assertion exists to stop, surviving inside it.
jq -e '.pnpm.overrides == null or (.pnpm.overrides | type == "object")' "$PKG" >/dev/null 2>&1 \
  || skip "\`pnpm.overrides\` in $PKG is present but is not an object, so the override checks cannot read it and an unreadable block would be indistinguishable from an empty one."
jq -e '.pnpm.auditConfig.ignoreGhsas == null or (.pnpm.auditConfig.ignoreGhsas | type == "array")' "$PKG" >/dev/null 2>&1 \
  || skip "\`pnpm.auditConfig.ignoreGhsas\` in $PKG is present but is not an array, so the suppression checks cannot read it and an unreadable list would be indistinguishable from an empty one."
# The lockfile is the one input with no shape contract at all, and its failure mode is
# loud rather than silent: an empty or truncated file makes EVERY override key report
# DEAD OVERRIDE (measured: 8 false alarms against this tree). Fail-closed, but this
# file's own position is that a false alarm in an observe-only signal is worse than
# silence, so assert the format's required top-level key before trusting absence.
grep -qE "^[[:space:]]*[\"']?lockfileVersion[\"']?[[:space:]]*:" "$LOCK" 2>/dev/null \
  || skip "$LOCK carries no \`lockfileVersion\` key, so it is not a pnpm lockfile this guard can read, and every override key would report as absent from it."

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
# `${PNPM_MAJOR:-0}` defaulted an EMPTY value to the string "0", which is numeric, so
# `--pnpm-major ""` fell straight through this check and then failed `[ -n ... ]`
# below — leaving the caller with a probe that silently did not run. Paired with a
# bare `--workspace-yaml` that reported "the location is verified" off one probe of
# two (measured 2026-08-01). Supplied-but-empty is a caller error, not a default.
if [ -n "$PNPM_MAJOR_SET" ]; then
  case "$PNPM_MAJOR" in
    ''|*[!0-9]*) skip "the pnpm major was passed as '"'"'$PNPM_MAJOR'"'"', which is not a number, so the location cannot be verified." ;;
  esac
fi
if [ -n "$PNPM_MAJOR" ] && [ "$PNPM_MAJOR" -ge 11 ]; then
  skip "pnpm major $PNPM_MAJOR does not read the \`pnpm\` field in package.json, which is the field this guard reads."
fi
# FORM, NOT NAME. The first version anchored at column 0 and assumed the key was
# unquoted, so it saw `overrides:` and missed `"overrides":` — valid YAML pnpm reads
# identically. Measured 2026-08-01 with a live acceptance and a live override sitting
# in the probed file: the quoted form produced "the location is verified". A probe
# that reports VERIFIED on a form it cannot parse is worse than no probe, and this is
# the house rule that guards match shape rather than spelling.
# Supplied-but-unreadable is a caller error, exactly as it is for --pnpm-major. The
# `-r` test below guards the `grep` call only; the POSITIVE claim further down keys
# off `[ -z "$WS_YAML" ]`, which tests non-emptiness, not readability. So a mistyped
# path silenced the UNVERIFIED warning and printed "the location probes found no sign
# the configuration moved" instead — measured 2026-08-01, byte-identical to a run
# where the probe really did read the file. The comment above about `--pnpm-major ""`
# names this exact pairing ("off one probe of two"); that round fixed one half of it.
# `-f` as well as `-r`: a DIRECTORY passes `-r`, and `grep` then fails on it with the
# same silence as a missing file. Measured — both forms produced the healthy text.
if [ -n "$WS_YAML" ] && { [ ! -f "$WS_YAML" ] || [ ! -r "$WS_YAML" ]; }; then
  skip "\`$WS_YAML\` was passed as --workspace-yaml but cannot be read as a file, so the location probe never ran. A probe that did not run must not read as a probe that found nothing."
fi
if [ -n "$WS_YAML" ] && [ -f "$WS_YAML" ] && [ -r "$WS_YAML" ] \
   && grep -qE "^[[:space:]]*[\"']?(overrides|auditConfig|ignoreGhsas)[\"']?[[:space:]]*:" "$WS_YAML" 2>/dev/null; then
  skip "\`$WS_YAML\` names overrides/auditConfig/ignoreGhsas, so the configuration may already live there; this guard reads the manifest."
fi

# CR HANDLING, SCALED PER CALL SITE rather than claimed for all of them.
#
# What it defends against, measured 2026-07-29: the override floor arrived as
# `7.28.0\r`, so `7.28.0` != `7.28.0\r` and the pin-back warning fired on an EXACT
# match. The CR does NOT come from the input encoding — jq emits one CR byte even
# when fed a file containing zero, because the Windows binary translates its own
# stdout. `$( )` then eats only the LAST line's CR, so lines 1..n-1 keep theirs.
#
# BOTH reads are load-bearing, and both are fixtured. The mechanism is the same at
# each: `$( )` eats only the LAST line's CR, so one entry hides the bug and two
# expose it. Vary the CARDINALITY and let jq supply the CR — no input file needs a
# single edited line ending.
#   - the `ignoreGhsas` read below: `T2` does that with two accepted GHSAs.
#   - the `overrides` read below: `T2b` does that with two override keys. This one
#     was declared unfixturable for three rounds ("the cause is the jq binary's
#     platform behaviour, which no fixture can vary"). That was wrong about the
#     read, not about jq: the platform behaviour is indeed unvaryable, and it is
#     also not what needed varying.
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
    note "no ignoreGhsas entries — nothing accepted, and the location probes found no sign the configuration moved."
  fi
else
  while IFS= read -r ghsa; do
    [ -n "$ghsa" ] || continue
    hit="$(jq -r --arg g "$ghsa" \
      '[(.advisories // {})[] | select((.github_advisory_id|tostring) == $g)] | length' \
      "$AUDIT" 2>/dev/null)"
    # An empty result is a FAILED READ, never a count of zero. The container type is
    # asserted above, but an advisory ELEMENT can still be unindexable (`{"1":"boom"}`
    # passes the container check and errors on `.github_advisory_id`), and jq's stderr
    # is discarded here. `${hit:-0}` would have turned that into "matches no advisory
    # in the tree. It suppresses nothing; remove it" — an instruction to delete a live
    # acceptance, derived from a read that never happened.
    [ -n "$hit" ] || skip "the advisory scan of $AUDIT returned nothing for $ghsa, so a failed read cannot be told apart from a clean one."
    if [ "$hit" = "0" ]; then
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
    # (An earlier draft also claimed `paths` was empty in this tree, and a later
    # revision called that claim false — "measured populated in four variants,
    # `.>@lhci/cli>uuid` and the rest" — and filed it as a false ornament beside a
    # true reason. THE CORRECTION WAS THE THING THAT WAS WRONG, and it was wrong for
    # the reason this whole file keeps circling: it was measured on the wrong pnpm.
    # Measured 2026-08-01 against the same tree and the same lockfile, both majors:
    #
    #   pnpm 10.28.2  findings: [{"version":"8.3.2","paths":[".>@lhci/cli>uuid"]}]
    #   pnpm  9.15.9  findings: [{"version":"8.3.2","paths":[]}]
    #
    # Every advisory, both `--prod` and full. `pnpm/action-setup` pins major 9 at all
    # five call sites, so at the place this guard actually runs, `paths` IS empty and
    # the original draft was right. The path-parsing check therefore could not fire
    # for TWO independent reasons on two different majors — the separator on 10, an
    # empty array on 9 — and the removal stands either way. This is also the only
    # field that differs between the two captures; everything the checks read
    # (advisory keys, `github_advisory_id`, the metadata counters) is identical.)
    #
    # `pnpm audit --prod` does the partition properly. Measured on the same tree
    # with the ignore list removed: `--prod` → 1 advisory (`@hono/node-server`),
    # `--dev` → 2 (`uuid`, `brace-expansion`). So membership in the `--prod` set IS
    # the question, and it is format-independent.
    prod_hit="$(jq -r --arg g "$ghsa" \
      '[(.advisories // {})[] | select((.github_advisory_id|tostring) == $g)] | length' \
      "$AUDIT_PROD" 2>/dev/null)"
    [ -n "$prod_hit" ] || skip "the advisory scan of $AUDIT_PROD returned nothing for $ghsa, so a failed read cannot be told apart from an empty production set."
    if [ "$prod_hit" != "0" ]; then
      warn "OVER-BROAD SUPPRESSION: $ghsa is accepted, but it now appears in the PRODUCTION dependency set (\`pnpm audit --prod\`). The acceptance was granted on a dev-only reachability argument; that argument no longer holds. Re-review it (security-auditor trigger, ADR 0065 Beslut 4)."
    else
      note "$ghsa: accepted, and absent from the \`pnpm audit --prod\` set. Note what that is and is not: a DECLARED-dependency partition, not runtime reachability. A devDependency can still generate shipped output — measured in this tree, \`tailwindcss\` and \`@tailwindcss/postcss\` are devDependencies and build the production stylesheet — so absence here means the acceptance's premise holds on the axis pnpm can answer, not that the package cannot reach production."
    fi
  done <<EOF
$IGNORED
EOF
fi

# ---- 3: the override block --------------------------------------------------
# Keys only. The targets had exactly one reader — check 4's floor comparison — and
# went with it, so emitting them now would be dead data. That also makes the CR
# strip load-bearing where it previously was NOT: with `key<TAB>value` the CR landed
# after the VALUE and could never reach the name (measured: `aaa<TAB>^1.0.0\r`), so
# the justification written above it was false. Emitting keys alone puts the CR on
# the key, where it would break the exact-name match this loop depends on.
KEYS="$(jq -r '(.pnpm.overrides // {}) | keys[]' "$PKG" 2>/dev/null | tr -d '\r')"
if [ -z "$KEYS" ]; then
  if [ -z "$PNPM_MAJOR" ] && [ -z "$WS_YAML" ]; then
    warn "EMPTY CONFIG, UNVERIFIED LOCATION: no overrides in this manifest, and the location is unverified — see the ignoreGhsas note above. An empty block and a moved block look identical from here."
  else
    note "no overrides — nothing repaired, and the location probes found no sign the configuration moved."
  fi
else
  while IFS= read -r key; do
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
    # all? It applies two guards, each separately load-bearing, each with a case
    # measured in a REAL pnpm lockfile. (Kept out here rather than inside the awk
    # program: that program is single-quoted, so an apostrophe in a comment ends the
    # shell string — done once already, and it silently broke every DEAD OVERRIDE
    # fixture until the suite caught it.)
    #
    #   1. THE ANCHOR — the name must start the line, not merely occur in it. The
    #      measured case is `postcss` swallowing `@tailwindcss/postcss`; the sharper
    #      one is `url` against `base64url@1.0.0:`, where guard 2 does NOT help,
    #      because it reads position 5 and finds the `6` of base64. Both are real npm
    #      packages, and the swallowing shape is common here: 685 lines in
    #      `web/jobbliggaren-web/pnpm-lock.yaml` match `^  '?@scope/name@<digit>`,
    #      carrying 330 distinct scoped names. (An earlier revision said "683" and
    #      named neither the population nor the quantity; no predicate reproduces it.)
    #   2. THE VERSION TERMINATOR — what follows the name must be a version. The
    #      earlier comment justified this with `foo@workspace:`, and that form could
    #      not be reproduced: a workspace lockfile writes `specifier: workspace:*`
    #      and `version: link:../lib` under `importers`, never a `pkg@workspace:`
    #      key (measured 2026-08-01 against a generated workspace). The true case is
    #      the lockfile OWN overrides block — measured, 3 live lines here
    #      (`js-yaml@>=4.0.0 <4.3.0:` and two `brace-expansion` ranges). Those start
    #      with the bare name and `@`, so guard 1 passes them, and only the digit
    #      test separates a repair that is merely DECLARED from a package that is
    #      installed. Without it a dead override reads as live: a false negative on
    #      exactly the direction this check exists for.
    if ! awk -v n="$name" '
      {
        line = $0
        sub(/^[[:space:]]+/, "", line)
        sub(/^'"'"'/, "", line)                     # lockfile may quote scoped keys
        if (index(line, n "@") != 1) next           # 1. must start with the exact name
        if (substr(line, length(n) + 2) !~ /^[0-9]/) next  # 2. and be followed by a version
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
