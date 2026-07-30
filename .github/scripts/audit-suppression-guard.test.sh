#!/usr/bin/env bash
# Fixtures for audit-suppression-guard.sh.
#
# The guard is observe-only, so its exit code carries no verdict — every case
# below asserts on the EMITTED TEXT. A fixture that only checked `exit 0` would
# pass against a guard that printed nothing at all, which is precisely the
# silence the guard exists to break.
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
GUARD="$HERE/audit-suppression-guard.sh"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
PASS=0; FAIL=0

# run <pkg> <audit-full> <lock> [audit-prod]
# `audit-prod` defaults to an empty advisory set — i.e. the accepted advisory is
# NOT production-reachable, which is the healthy case. Cases that need it non-empty
# pass it explicitly.
run() {
  local prod="${4:-}"
  if [ -z "$prod" ]; then
    prod="$TMP/_empty.audit.json"
    echo '{ "advisories": {} }' > "$prod"
  fi
  bash "$GUARD" --package-json "$1" --audit-json "$2" --audit-prod-json "$prod" \
       --lockfile "$3" 2>&1
}
ok()   { PASS=$((PASS+1)); echo "  ok   — $1"; }
bad()  { FAIL=$((FAIL+1)); echo "  FAIL — $1"; echo "----- output -----"; echo "$2"; echo "------------------"; }
expect_has()  { case "$2" in *"$3"*) ok "$1" ;; *) bad "$1" "$2" ;; esac; }
expect_lacks(){ case "$2" in *"$3"*) bad "$1" "$2" ;; *) ok "$1" ;; esac; }

# ---------------------------------------------------------------- fixtures ---
mk_lock() { printf '%s\n' "$@" > "$TMP/lock.yaml"; }

# A: the shipped shape — one accepted GHSA, dev-only reachable, live override.
cat > "$TMP/a.pkg.json" <<'J'
{ "dependencies": { "shadcn": "^4.12.0" },
  "devDependencies": { "eslint": "^9.0.0" },
  "pnpm": { "overrides": { "postcss": "^8.5.18" },
            "auditConfig": { "ignoreGhsas": ["GHSA-mh99-v99m-4gvg"] } } }
J
cat > "$TMP/a.audit.json" <<'J'
{ "advisories": { "1": { "github_advisory_id": "GHSA-mh99-v99m-4gvg",
    "module_name": "brace-expansion", "severity": "high",
    "findings": [ { "paths": [ ". > eslint@9.0.0 > minimatch@3.1.5 > brace-expansion@1.1.16" ] } ] } } }
J
mk_lock "  postcss@8.5.24:" "  eslint@9.0.0:"
out="$(run "$TMP/a.pkg.json" "$TMP/a.audit.json" "$TMP/lock.yaml")"
expect_has  "A1 accepted+dev-only is reported as still dev-only" "$out" "still dev-only reachable"
expect_lacks "A2 no stale warning when the GHSA matches"         "$out" "STALE SUPPRESSION"
expect_lacks "A3 no over-broad warning on a dev root"            "$out" "OVER-BROAD"
expect_lacks "A4 live override raises nothing"                   "$out" "DEAD OVERRIDE"

# B: the acceptance went stale — nothing in the tree matches it any more.
cat > "$TMP/b.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "auditConfig": { "ignoreGhsas": ["GHSA-0000-0000-0000"] } } }
J
echo '{ "advisories": {} }' > "$TMP/b.audit.json"
mk_lock "  nothing@1.0.0:"
out="$(run "$TMP/b.pkg.json" "$TMP/b.audit.json" "$TMP/lock.yaml")"
expect_has "B1 a suppression matching nothing is named" "$out" "STALE SUPPRESSION"

# C: THE DANGEROUS ONE — the accepted GHSA has entered the PRODUCTION set.
#
#    This asks `pnpm audit --prod` rather than parsing `findings[].paths`. The
#    first version parsed paths and could not fire at all: pnpm emits `.>root>kid`
#    with no spaces and no versions, so the split yielded an empty root and every
#    path was skipped — and its fixtures fed a path format pnpm never produces, so
#    they asserted a production fact on a premise production cannot produce (§5
#    `Tests:`). Measured 2026-07-29: `--prod` → 1 advisory, `--dev` → 2 including
#    the accepted one, so set membership is the real question.
cat > "$TMP/c.pkg.json" <<'J'
{ "dependencies": { "shadcn": "^4.12.0" }, "devDependencies": { "eslint": "^9.0.0" },
  "pnpm": { "auditConfig": { "ignoreGhsas": ["GHSA-mh99-v99m-4gvg"] } } }
J
# The shape below is what pnpm actually emits — no `paths` are consulted at all now.
cat > "$TMP/c.audit.json" <<'J'
{ "advisories": { "1": { "github_advisory_id": "GHSA-mh99-v99m-4gvg",
    "module_name": "brace-expansion", "severity": "high" } } }
J
cat > "$TMP/c.prod.json" <<'J'
{ "advisories": { "1": { "github_advisory_id": "GHSA-mh99-v99m-4gvg",
    "module_name": "brace-expansion", "severity": "high" } } }
J
mk_lock "  shadcn@4.12.0:"
out="$(run "$TMP/c.pkg.json" "$TMP/c.audit.json" "$TMP/lock.yaml" "$TMP/c.prod.json")"
expect_has "C1 an accepted GHSA present in the --prod set is named" "$out" "OVER-BROAD SUPPRESSION"
expect_has "C2 the warning routes to the security-auditor trigger" "$out" "Beslut 4"

# C3: THE NEGATION, without which C1 proves nothing. Same advisory, same manifest,
#     absent from --prod → must stay silent and say so.
out="$(run "$TMP/c.pkg.json" "$TMP/c.audit.json" "$TMP/lock.yaml")"
expect_lacks "C3 the same advisory absent from --prod does not fire" "$out" "OVER-BROAD"
expect_has   "C4 and is reported as still dev-only"                 "$out" "still dev-only reachable"

# D: an override key naming a package the lockfile does not carry.
cat > "$TMP/d.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "overrides": { "ghost-pkg": "^9.0.0" } } }
J
echo '{ "advisories": {} }' > "$TMP/d.audit.json"
mk_lock "  postcss@8.5.24:"
out="$(run "$TMP/d.pkg.json" "$TMP/d.audit.json" "$TMP/lock.yaml")"
expect_has "D1 an override matching no package is named" "$out" "DEAD OVERRIDE"

# CHECK 4 IS GONE, and so are the fixtures that exercised it (E1, E3, F1).
# It compared resolved versions against an open key's target floor and called
# that Beslut 6's pin-back. Measured 2026-07-30: the signature is the opposite
# (an override forces resolution TO the floor), a true pin-back needs declared
# ranges the lockfile does not carry, and the state it DID detect is already
# caught blockingly by `pnpm install --frozen-lockfile` in the required
# `frontend` job. Removing it also removed every false positive this guard has
# ever produced — all three lived in the version extraction it fed.

# F: the same shape but GATED — a gated key legitimately spares consumers
#    outside its range, so it must NOT fire.
cat > "$TMP/f.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "overrides": { "js-yaml@>=4.0.0 <4.3.0": "^4.3.0" } } }
J
echo '{ "advisories": {} }' > "$TMP/f.audit.json"
mk_lock "  js-yaml@3.15.0:" "  js-yaml@4.3.0:"
out="$(run "$TMP/f.pkg.json" "$TMP/f.audit.json" "$TMP/lock.yaml")"
expect_lacks "F1 a gated key does not fire on the line it deliberately spares" "$out" "PINNED A CONSUMER BACK"
expect_lacks "F2 a gated key with a live package is not dead"                  "$out" "DEAD OVERRIDE"

# E2/E3: the two false positives the fixtures MISSED and the real tree caught
#        (2026-07-29). Both made the guard cry wolf on a healthy repo, which is
#        worse than silence — an observe-only warning nobody trusts is noise.
#
# E2: a scoped package must not be swallowed by a bare-name key. `postcss` and
#     `@tailwindcss/postcss` are different packages; a substring match reported
#     the latter's 4.3.2 as the former's version and "proved" a pin-back.
cat > "$TMP/e2.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "overrides": { "postcss": "^8.5.18" } } }
J
echo '{ "advisories": {} }' > "$TMP/e2.audit.json"
mk_lock "  postcss@8.5.24:" "  '@tailwindcss/postcss@4.3.2':"
out="$(run "$TMP/e2.pkg.json" "$TMP/e2.audit.json" "$TMP/lock.yaml")"
expect_lacks "E2 a scoped package is not swallowed by a bare-name key" "$out" "PINNED A CONSUMER BACK"

# E3: pins the equality escape on the comparison, NOT what its first version
#     claimed. That version said the lockfile's trailing `:` made the comparison
#     fire on an exact match. Measured isolated: zero warnings — `sort -V` orders
#     `7.28.0` before `7.28.0:`, so the condition never holds, and the colon in
#     fact MASKED the CR bug. The claimed defect did not exist; only the CR one
#     did. The version terminator is kept because it is correct, and is DECLARED
#     UNEXERCISED: no fixture here kills its mutant.
cat > "$TMP/e3.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "overrides": { "undici": "^7.28.0" } } }
J
echo '{ "advisories": {} }' > "$TMP/e3.audit.json"
mk_lock "  undici@7.28.0:"
out="$(run "$TMP/e3.pkg.json" "$TMP/e3.audit.json" "$TMP/lock.yaml")"
expect_lacks "E3 a version equal to the floor is not read as below it" "$out" "PINNED A CONSUMER BACK"
expect_lacks "E4 and the package is not reported dead either"          "$out" "DEAD OVERRIDE"

# NO CRLF FIXTURE, AND THAT IS A FINDING RATHER THAN AN OVERSIGHT.
#
# The guard strips CR from every jq read. A fixture for it was written, and
# then deleted: it varied the INPUT files' line endings, which is not where the
# CR comes from. Measured 2026-07-29 — jq emits one CR byte when fed a file
# containing zero, because the Windows binary translates its own stdout. The
# fixture therefore stayed green against a build with the hardening removed,
# which is the definition of decoration.
#
# The cause is the jq binary's platform behaviour, which a fixture cannot vary,
# so this path is DECLARED UNEXERCISED. On Linux CI the strip is a no-op; the
# bug it prevents is visible only on a Windows machine.
#
# One trap for whoever tries again: msys2's grep does text-mode translation and
# strips CR before matching, so grepping for a carriage return reports "no CR" on a file that
# `od -c` shows carries two. Count the bytes instead.

# T2: the CR hardening on the `ignoreGhsas` read, exercised WITHOUT touching any
#     line endings. `$( )` eats only the last line's CR, so a single entry hides
#     the bug; two entries expose it. Both are live, so a hardened build must emit
#     zero STALE warnings — an unhardened one warns about the first entry, whose
#     GHSA arrives with a trailing CR and therefore matches no advisory.
#
#     This is the fixture the earlier version claimed could not exist ("no fixture
#     can vary it"). That claim was true of the `overrides` read and false here:
#     the variable is CARDINALITY, and jq supplies the CR itself.
cat > "$TMP/t2.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "auditConfig": { "ignoreGhsas": ["GHSA-aaaa-aaaa-aaaa", "GHSA-bbbb-bbbb-bbbb"] } } }
J
cat > "$TMP/t2.audit.json" <<'J'
{ "advisories": {
  "1": { "github_advisory_id": "GHSA-aaaa-aaaa-aaaa", "module_name": "a", "severity": "high" },
  "2": { "github_advisory_id": "GHSA-bbbb-bbbb-bbbb", "module_name": "b", "severity": "high" } } }
J
mk_lock "  x@1.0.0:"
out="$(run "$TMP/t2.pkg.json" "$TMP/t2.audit.json" "$TMP/lock.yaml")"
expect_lacks "T2 two live entries produce no stale warning (CR strip on ignoreGhsas)" "$out" "STALE SUPPRESSION"

# T3: a scoped override key must not be read as dead. The lockfile quotes scoped
#     entries, so dropping the quote-strip makes every scoped key look absent —
#     measured as a false DEAD OVERRIDE against the real lockfile.
cat > "$TMP/t3.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "overrides": { "@tailwindcss/postcss": "^4.3.0" } } }
J
echo '{ "advisories": {} }' > "$TMP/t3.audit.json"
mk_lock "  '@tailwindcss/postcss@4.3.2':"
out="$(run "$TMP/t3.pkg.json" "$TMP/t3.audit.json" "$TMP/lock.yaml")"
expect_lacks "T3 a quoted scoped lockfile entry is not read as a dead override" "$out" "DEAD OVERRIDE"

# T4/T5: a failed audit must be reported as SKIPPED, never laundered into a
#        security instruction. `pnpm audit` exits non-zero when advisories exist,
#        so the caller needs `|| true` and cannot distinguish failure by exit code.
#        Measured 2026-07-29 on the first version: an empty file, an error dump and
#        `{}` each produced "STALE SUPPRESSION … remove it" against the house's only
#        live acceptance — an instruction that would turn the blocking gate red.
cat > "$TMP/t4.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "auditConfig": { "ignoreGhsas": ["GHSA-mh99-v99m-4gvg"] } } }
J
mk_lock "  x@1.0.0:"
: > "$TMP/t4.empty.json"
out="$(run "$TMP/t4.pkg.json" "$TMP/t4.empty.json" "$TMP/lock.yaml")"
expect_has  "T4 an empty audit file is reported as skipped" "$out" "SKIPPED"
expect_lacks "T5 and never as a stale suppression"          "$out" "STALE SUPPRESSION"

echo 'ERR  Registry unreachable' > "$TMP/t6.err.json"
out="$(run "$TMP/t4.pkg.json" "$TMP/t6.err.json" "$TMP/lock.yaml")"
expect_has  "T6 a non-JSON error dump is reported as skipped" "$out" "SKIPPED"
expect_lacks "T7 and never as a stale suppression"            "$out" "STALE SUPPRESSION"

# T4b/T6b/T8b: the SAME three inputs as --audit-prod-json. Without these, narrowing
#     the shape assertion to `$AUDIT` alone survives every fixture — measured
#     2026-07-30 — and a broken --prod file then manufactures the POSITIVE claim
#     "absent from the --prod set — still dev-only reachable" out of a failed
#     audit, on the check this guard calls the dangerous one.
mk_lock "  x@1.0.0:"
echo '{ "advisories": {} }' > "$TMP/ok.audit.json"
# Created here rather than reused from T8 below: at this point in the file T8 has
# not run, so the path would not exist and the guard would exit 2 on an unreadable
# input — a usage error masquerading as the property under test. (Measured: that is
# exactly how this fixture first failed.)
echo '{}' > "$TMP/tprod.emptyobj.json"
for bad in "$TMP/t4.empty.json" "$TMP/t6.err.json" "$TMP/tprod.emptyobj.json"; do
  out="$(run "$TMP/t4.pkg.json" "$TMP/ok.audit.json" "$TMP/lock.yaml" "$bad")"
  expect_has  "T-prod a broken --prod file ($(basename "$bad")) is skipped" "$out" "SKIPPED"
  expect_lacks "T-prod and never asserts dev-only reachability"             "$out" "still dev-only"
done

# T-pkg: a truncated manifest must not read as "nothing configured".
printf '{ "dependencies": ' > "$TMP/tpkg.broken.json"
out="$(run "$TMP/tpkg.broken.json" "$TMP/ok.audit.json" "$TMP/lock.yaml")"
expect_has  "T-pkg an unparseable manifest is skipped" "$out" "SKIPPED"
expect_lacks "T-pkg and never reports no findings"     "$out" "no findings"

# T8: `{}` is valid JSON but not audit JSON — the case the shape assertion exists
#     for, since `-r` alone accepts it.
echo '{}' > "$TMP/t8.json"
out="$(run "$TMP/t4.pkg.json" "$TMP/t8.json" "$TMP/lock.yaml")"
expect_has "T8 valid JSON without .advisories is skipped, not called clean" "$out" "SKIPPED"

# G: empty configuration is a legitimate state, not a finding.
cat > "$TMP/g.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {}, "pnpm": {} }
J
echo '{ "advisories": {} }' > "$TMP/g.audit.json"
mk_lock "  x@1.0.0:"
# G1/G2 measure the UNVERIFIED-LOCATION case: an empty pnpm block with no probe
# supplied. That is not a clean state — pnpm 11 does not read this field at all,
# and the ratified pnpm-workspace.yaml migration empties it while the settings
# stay live elsewhere. Measured 2026-07-30: against `del(.pnpm)` the guard used
# to print "nothing accepted" and "no findings" with 1 acceptance and 8 override
# keys alive at the new location. So an empty block reports as unverified.
out="$(run "$TMP/g.pkg.json" "$TMP/g.audit.json" "$TMP/lock.yaml")"
expect_has "G1 an empty block with no location probe is flagged unverified" "$out" "UNVERIFIED LOCATION"
expect_has "G2 and names the state it cannot distinguish"                   "$out" "the configuration moved"

# G3/G4: with the location verified, an empty block IS a clean state and must not
# warn — otherwise the guard cries wolf on every repo that accepts nothing.
out="$(bash "$GUARD" --package-json "$TMP/g.pkg.json" --audit-json "$TMP/g.audit.json"         --audit-prod-json "$TMP/g.audit.json" --lockfile "$TMP/lock.yaml"         --pnpm-major 9 2>&1)"
expect_lacks "G3 a verified location makes an empty block silent" "$out" "::warning::"
expect_has   "G4 and states it positively"                        "$out" "location is verified"

# G5: pnpm 11 reads a different file, so the guard cannot speak about this tree.
out="$(bash "$GUARD" --package-json "$TMP/a.pkg.json" --audit-json "$TMP/a.audit.json"         --audit-prod-json "$TMP/g.audit.json" --lockfile "$TMP/lock.yaml"         --pnpm-major 11 2>&1)"
expect_has "G5 pnpm 11 is reported as SKIPPED, not clean" "$out" "SKIPPED"

# G6: a workspace file carrying the settings means they moved.
printf 'packages:
  - "."
overrides:
  postcss: ^8.5.18
' > "$TMP/ws.yaml"
out="$(bash "$GUARD" --package-json "$TMP/g.pkg.json" --audit-json "$TMP/g.audit.json"         --audit-prod-json "$TMP/g.audit.json" --lockfile "$TMP/lock.yaml"         --workspace-yaml "$TMP/ws.yaml" 2>&1)"
expect_has "G6 settings found in pnpm-workspace.yaml are reported as SKIPPED" "$out" "configuration has moved"

# H: observe-only — a tree full of findings still exits 0. If this ever fails,
#    the guard has become a gate, which is the one thing it must never be.
#    Asserts on a run that DOES produce a finding, verified below, so a usage
#    error cannot masquerade as the property under test — which is exactly what
#    happened when the guard gained its fourth argument and this case still
#    passed three flags.
h_out="$(run "$TMP/c.pkg.json" "$TMP/c.audit.json" "$TMP/lock.yaml" "$TMP/c.prod.json")"
h_rc=$?
expect_has "H0 the observe-only case really produces a finding" "$h_out" "::warning::"
[ "$h_rc" -eq 0 ] && ok "H1 findings do not change the exit code (observe-only)" \
                  || bad "H1 findings do not change the exit code (observe-only)" "exit=$h_rc"

# I: usage errors DO exit non-zero — a guard that cannot read its inputs must
#    not report silence as cleanliness.
bash "$GUARD" --package-json "$TMP/nope.json" --audit-json "$TMP/g.audit.json" \
     --audit-prod-json "$TMP/g.audit.json" --lockfile "$TMP/lock.yaml" >/dev/null 2>&1
[ $? -eq 2 ] && ok "I1 an unreadable input fails loudly" || bad "I1 an unreadable input fails loudly" "exit != 2"

echo
echo "audit-suppression-guard fixtures: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ] || exit 1
