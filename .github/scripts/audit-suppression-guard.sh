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
# WHERE IT RUNS, AND WHY NOT ELSEWHERE. The observe-only `audit` job in
# build.yml — never inside `dependabot-automerge.yml`. A path-pinning assertion
# inside a blocking merge control is how the next deadlock gets built: it would
# fail a PR for something that PR did not cause, which is the exact defect the
# amendment exists to repair. So this NEVER exits non-zero on a finding; it
# emits `::warning::` and returns 0. The only non-zero exits are usage errors.
#
# WHAT IT CHECKS — four directions, because the reviewers found that the
# obvious two are the cheap half:
#   1. DEAD SUPPRESSION  — a GHSA in `ignoreGhsas` matching no advisory. Costs
#      tidiness, not safety, but it is what makes "this list must shrink"
#      auditable at all.
#   2. OVER-BROAD SUPPRESSION — an ignored GHSA whose paths reach a production
#      root. This is the dangerous one: acceptance was granted on a dev-only
#      reachability argument, and a bare GHSA does not re-check that argument.
#   3. DEAD OVERRIDE — an `overrides` key naming a package absent from the
#      lockfile. A repair that silently stopped applying reads exactly like a
#      repair that is working.
#   4. OVER-MATCHING OPEN KEY — an open (unversioned) key whose target range
#      excludes a version the lockfile actually resolved. Open form matches
#      every consumer forever, so when one legitimately crosses a major it is
#      pinned BACK, silently. The gated form's failure was loud; this one is not.
#
# INPUTS are files, never the network, so the fixtures can drive every branch.
#   --package-json <f>  the manifest carrying pnpm.auditConfig / pnpm.overrides
#   --audit-json <f>    `pnpm audit --json` output taken WITHOUT the ignore list
#                       applied (otherwise the suppressed advisory is invisible
#                       and check 1 can never fire — the measurement must see
#                       what the suppression hides)
#   --lockfile <f>      pnpm-lock.yaml, for checks 3 and 4
set -uo pipefail

PKG=""; AUDIT=""; LOCK=""
while [ $# -gt 0 ]; do
  case "$1" in
    --package-json) PKG="${2:-}"; shift 2 ;;
    --audit-json)   AUDIT="${2:-}"; shift 2 ;;
    --lockfile)     LOCK="${2:-}"; shift 2 ;;
    *) echo "usage: $0 --package-json F --audit-json F --lockfile F" >&2; exit 2 ;;
  esac
done
[ -n "$PKG" ] && [ -n "$AUDIT" ] && [ -n "$LOCK" ] || {
  echo "usage: $0 --package-json F --audit-json F --lockfile F" >&2; exit 2; }
for f in "$PKG" "$AUDIT" "$LOCK"; do
  [ -r "$f" ] || { echo "not readable: $f" >&2; exit 2; }
done
command -v jq >/dev/null 2>&1 || { echo "jq required" >&2; exit 2; }

warn() { echo "::warning::$1"; FINDINGS=$((FINDINGS + 1)); }
note() { echo "::notice::$1"; }
FINDINGS=0

# Production roots = `dependencies`. Everything else is dev. The distinction is
# the whole basis on which an acceptance is granted, so it is read from the
# manifest rather than from pnpm's own dev/prod split — measured 2026-07-28,
# pnpm reports `devDependencies: 0` for this tree, so its split is not the one
# package.json declares and cannot be used here.
# `tr -d '\r'` on every jq read is load-bearing, and DECLARED UNEXERCISED by the
# fixtures — deliberately, because no fixture can reach it.
#
# What it defends against, measured 2026-07-29 against the real tree: the floor
# arrived as `7.28.0\r`, so `7.28.0` != `7.28.0\r` and the pin-back warning fired
# on an EXACT match, three times, on a healthy repo.
#
# The CR does NOT come from the input encoding, which is the obvious guess and is
# wrong. Measured: jq emits one CR byte even when fed a file containing zero. It
# is the Windows jq binary translating its own stdout. So a fixture that varies
# the input file's line endings tests something that is not the cause — the first
# version of this guard shipped exactly that fixture, and it stayed green against
# a build with the hardening removed. It was deleted rather than kept as
# decoration.
#
# Consequence worth knowing: on Linux CI, jq emits LF and these `tr` calls are
# no-ops. The bug they prevent is visible ONLY on a developer's Windows machine —
# which is the machine where someone might act on a false warning.
PROD_ROOTS="$(jq -r '(.dependencies // {}) | keys[]' "$PKG" 2>/dev/null | tr -d '\r' | sort -u)"

# ---- 1 + 2: the suppression list -------------------------------------------
IGNORED="$(jq -r '(.pnpm.auditConfig.ignoreGhsas // [])[]' "$PKG" 2>/dev/null | tr -d '\r')"
if [ -z "$IGNORED" ]; then
  note "no ignoreGhsas entries — nothing accepted, nothing to watch."
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
    # The acceptance rests on reachability, not severity. Re-check it.
    paths="$(jq -r --arg g "$ghsa" \
      '[(.advisories // {})[] | select((.github_advisory_id // .id|tostring) == $g) | (.findings // [])[] | (.paths // [])[]] | .[]' \
      "$AUDIT" 2>/dev/null)"
    prod_hit=""
    while IFS= read -r p; do
      [ -n "$p" ] || continue
      # A path reads ". > root@ver > ...". Take the first hop as its root.
      root="$(printf '%s' "$p" | awk -F ' > ' '{print $2}' | sed 's/@[^@]*$//')"
      [ -n "$root" ] || continue
      if printf '%s\n' "$PROD_ROOTS" | grep -qxF "$root"; then
        prod_hit="$root"; break
      fi
    done <<EOF
$paths
EOF
    if [ -n "$prod_hit" ]; then
      warn "OVER-BROAD SUPPRESSION: $ghsa is accepted, but a path now reaches it through the production dependency '$prod_hit'. The acceptance was granted on a dev-only reachability argument; that argument no longer holds. Re-review it (security-auditor trigger, ADR 0065 Beslut 4)."
    else
      note "$ghsa: accepted, still dev-only reachable."
    fi
  done <<EOF
$IGNORED
EOF
fi

# ---- 3 + 4: the override block ---------------------------------------------
KEYS="$(jq -r '(.pnpm.overrides // {}) | to_entries[] | "\(.key)\t\(.value)"' "$PKG" 2>/dev/null | tr -d '\r')"
if [ -z "$KEYS" ]; then
  note "no overrides — nothing repaired, nothing to watch."
else
  while IFS=$'\t' read -r key target; do
    [ -n "$key" ] || continue
    # `pkg` or `pkg@<range>`; the range may itself contain '@' only in scopes,
    # which always lead, so split on the LAST '@' that is not position 0.
    name="$key"; gated="no"
    case "${key#@}" in *@*) name="${key%@*}"; gated="yes" ;; esac
    # Exact-name match only. Two traps, both measured against the real tree on
    # 2026-07-29 before the fixtures covered them:
    #   - a substring match makes `postcss` swallow `@tailwindcss/postcss`,
    #     reporting that package's 4.3.2 as postcss's version;
    #   - pnpm-lock entries end in `:` (and may carry a `(peer)` suffix), so a
    #     naive cut leaves `7.28.0:` and every comparison against it is false.
    # So: anchor the whole key, allow an optional leading quote, and stop the
    # version at the first character that cannot be part of one.
    resolved="$(awk -v n="$name" '
      {
        line = $0
        sub(/^[[:space:]]+/, "", line)
        sub(/^'"'"'/, "", line)                     # lockfile may quote scoped keys
        idx = index(line, n "@")
        if (idx != 1) next                          # must start with the exact name
        rest = substr(line, length(n) + 2)
        if (rest !~ /^[0-9]/) next                  # `foo@workspace:` etc. are not versions
        match(rest, /^[0-9][0-9A-Za-z.+-]*/)
        print substr(rest, 1, RLENGTH)
      }' "$LOCK" 2>/dev/null | sort -u)"
    if [ -z "$resolved" ]; then
      warn "DEAD OVERRIDE: key '$key' names a package absent from the lockfile. It repairs nothing; a repair that stopped applying reads exactly like one that works."
      continue
    fi
    if [ "$gated" = "no" ]; then
      # Open key: matches every consumer forever. Flag any resolved version
      # below the target floor — that is the silent pin-back.
      floor="$(printf '%s' "$target" | sed 's/^[^0-9]*//')"
      low=""
      while IFS= read -r v; do
        [ -n "$v" ] || continue
        if [ "$(printf '%s\n%s\n' "$floor" "$v" | sort -V | head -1)" = "$v" ] && [ "$v" != "$floor" ]; then
          low="$v"; break
        fi
      done <<EOF
$resolved
EOF
      if [ -n "$low" ]; then
        warn "OPEN KEY PINNED A CONSUMER BACK: '$name' resolves $low, below the open key's floor $floor. An open key matches every consumer regardless of its declared range, so a consumer that crossed a major was forced back — silently (ADR 0065 Beslut 6)."
      fi
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
