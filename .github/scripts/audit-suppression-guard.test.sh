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
# Every real pnpm lockfile opens with `lockfileVersion`, and the guard now requires it
# before it will read absence out of the file — an empty or truncated lockfile used to
# manufacture a DEAD OVERRIDE for every key. Emit it here so the fixtures carry the
# shape production carries; `Y5` drives the case where it is missing.
mk_lock() { { printf "lockfileVersion: '9.0'\n"; printf '%s\n' "$@"; } > "$TMP/lock.yaml"; }

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
    "findings": [ { "version": "1.1.16", "paths": [] } ] } } }
J
mk_lock "  postcss@8.5.24:" "  eslint@9.0.0:"
out="$(run "$TMP/a.pkg.json" "$TMP/a.audit.json" "$TMP/lock.yaml")"
expect_has  "A1 accepted+dev-only is reported as absent from the --prod set" "$out" "absent from the"
# The note used to end at "still dev-only reachable", which is a REACHABILITY verdict
# drawn from a DECLARED-dependency partition. Measured in this repo: tailwindcss and
# @tailwindcss/postcss are devDependencies that build the production stylesheet, so
# the two differ in fact and not only in principle. Pin the limit, not just the claim.
expect_has  "A1b and the note states that partition is not runtime reachability" "$out" "not runtime reachability"
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
expect_has   "C4 and is reported as absent from the --prod set"     "$out" "absent from the"

# D: an override key naming a package the lockfile does not carry.
cat > "$TMP/d.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "overrides": { "ghost-pkg": "^9.0.0" } } }
J
echo '{ "advisories": {} }' > "$TMP/d.audit.json"
mk_lock "  postcss@8.5.24:"
out="$(run "$TMP/d.pkg.json" "$TMP/d.audit.json" "$TMP/lock.yaml")"
expect_has "D1 an override matching no package is named" "$out" "DEAD OVERRIDE"

# CHECK 4 IS GONE. It compared resolved versions against an open key's target
# floor and called that Beslut 6's pin-back. Measured 2026-07-30: the signature
# is the opposite (an override forces resolution TO the floor), a true pin-back
# needs declared ranges the lockfile does not carry, and the state it DID detect
# is already caught blockingly by `pnpm install --frozen-lockfile` in the
# required `frontend` job. Removing it also removed every false positive this
# guard has ever produced — all three lived in the version extraction it fed.
#
# E1 went with it, and so did F1: F1 asserted the absence of a string no code
# path can emit any more, so it could not fail. E2 and E3 stay, redirected at
# DEAD OVERRIDE — their subjects, a scoped name swallowed by a bare key and a
# plain version entry, are still live for the name lookup.

# F: the same shape but GATED — a gated key legitimately spares consumers
#    outside its range, so it must NOT fire.
cat > "$TMP/f.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "overrides": { "js-yaml@>=4.0.0 <4.3.0": "^4.3.0" } } }
J
echo '{ "advisories": {} }' > "$TMP/f.audit.json"
mk_lock "  js-yaml@3.15.0:" "  js-yaml@4.3.0:"
out="$(run "$TMP/f.pkg.json" "$TMP/f.audit.json" "$TMP/lock.yaml")"
expect_lacks "F2 a gated key with a live package is not dead"                  "$out" "DEAD OVERRIDE"

# E2/E3: the two false positives the fixtures MISSED and the real tree caught
#        (2026-07-29). Both made the guard cry wolf on a healthy repo, which is
#        worse than silence — an observe-only warning nobody trusts is noise.
#
# E2: a scoped package must not be swallowed by a bare-name key. `postcss` and
#     `@tailwindcss/postcss` are different packages; a substring match reported
#     the latter's 4.3.2 as the former's version and "proved" a pin-back.
#     NOTE what this case does and does not separate. Both awk guards reject it —
#     the anchor because `postcss@` starts at position 14, not 1, and the version
#     terminator because position 9 of `@tailwindcss/postcss@4.3.2':` is `d` (in
#     "tailwind"), not a digit. So E2 pins them
#     JOINTLY and cannot fail for one alone; V1 and V2 below separate them, each
#     against a form measured in a real lockfile.
cat > "$TMP/e2.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "overrides": { "postcss": "^8.5.18" } } }
J
echo '{ "advisories": {} }' > "$TMP/e2.audit.json"
mk_lock "  postcss@8.5.24:" "  '@tailwindcss/postcss@4.3.2':"
out="$(run "$TMP/e2.pkg.json" "$TMP/e2.audit.json" "$TMP/lock.yaml")"
expect_lacks "E2 a scoped package is not swallowed by a bare-name key" "$out" "DEAD OVERRIDE"

# E3: a plain, unquoted lockfile entry must not read as a dead override. Its
#     first version claimed to pin a trailing-colon defect in a version
#     comparison; that comparison is gone with check 4, and the defect was
#     measured never to have existed.
cat > "$TMP/e3.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "overrides": { "undici": "^7.28.0" } } }
J
echo '{ "advisories": {} }' > "$TMP/e3.audit.json"
mk_lock "  undici@7.28.0:"
out="$(run "$TMP/e3.pkg.json" "$TMP/e3.audit.json" "$TMP/lock.yaml")"
expect_lacks "E3 a plain version entry is not read as a dead override" "$out" "DEAD OVERRIDE"

# THE CR HARDENING IS FIXTURED ON BOTH READS — T2 and T2b below.
#
# It took four rounds to get here, and the first three were spent on the wrong
# variable. A fixture was written and deleted because it varied the INPUT files'
# line endings, which is not where the CR comes from: jq emits one CR byte when fed
# a file containing zero, because the Windows binary translates its own stdout
# (measured 2026-07-29). That fixture stayed green against a build with the
# hardening removed, which is the definition of decoration.
#
# The conclusion drawn from that failure was that no fixture could exist, and this
# file carried "DECLARED UNEXERCISED" for three rounds. The conclusion was wrong.
# The unvaryable thing is the jq binary's behaviour; the thing that needed varying
# was CARDINALITY. `$( )` eats only the LAST line's CR, so one entry hides the bug
# and two expose it, and jq supplies the CR itself. On Linux CI the strip is a
# no-op; the bug it prevents is visible only on a Windows machine — which is where
# someone would act on the false warning.
#
# One trap for whoever revisits this: msys2's grep does text-mode translation and
# strips CR before matching, so grepping for a carriage return reports "no CR" on a file that
# `od -c` shows carries two. Count the bytes instead.

# T2: the CR hardening on the `ignoreGhsas` read, exercised WITHOUT touching any
#     line endings. `$( )` eats only the last line's CR, so a single entry hides
#     the bug; two entries expose it. Both are live, so a hardened build must emit
#     zero STALE warnings — an unhardened one warns about the first entry, whose
#     GHSA arrives with a trailing CR and therefore matches no advisory.
#
#     This is the fixture the earlier version claimed could not exist ("no fixture
#     can vary it"). The claim was false here, and — as T2b later showed — equally
#     false of the `overrides` read it was actually written about. The variable is
#     CARDINALITY, and jq supplies the CR itself.
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

# T2b: the CR strip on the OVERRIDES read, exercised the same way T2 does the
#      ignoreGhsas one — by cardinality, not by line endings. `$( )` eats only the
#      last key's CR, so a single-key manifest hides the bug; two keys expose it.
#      Both are live in the lockfile, so a hardened build must stay silent, while an
#      unhardened one reports the FIRST key dead — its name arrives with a trailing
#      CR and matches nothing.
#
#      This fixture exists because the strip's written justification was measured
#      FALSE: with the read emitting `key<TAB>value`, the CR landed after the value
#      and could never reach the name. Emitting keys alone moved it onto the key and
#      made the strip real — so the fixture and the justification became true in the
#      same edit.
cat > "$TMP/t2b.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "overrides": { "alpha-pkg": "^1.0.0", "beta-pkg": "^2.0.0" } } }
J
echo '{ "advisories": {} }' > "$TMP/t2b.audit.json"
mk_lock "  alpha-pkg@1.2.3:" "  beta-pkg@2.3.4:"
out="$(run "$TMP/t2b.pkg.json" "$TMP/t2b.audit.json" "$TMP/lock.yaml")"
expect_lacks "T2b two live override keys produce no dead-override warning (CR strip)" "$out" "DEAD OVERRIDE"

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
expect_has   "G4 and states it positively"                        "$out" "no sign the configuration moved"

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
expect_has "G6 settings found in pnpm-workspace.yaml are reported as SKIPPED" "$out" "may already live there"

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

# ---------------------------------------------------------------------------
# J–X: the paths a mutation matrix found unexercised on 2026-08-01. An assertion that
# a malformed input is SKIPPED passes trivially against a guard that skips
# everything, so those cases need a counterfactual. J, N, O, Q, R and S carry one
# inside the block (J0, N2/N3, O3, Q4, R0, S0); K, L, P, U and V rest on controls
# elsewhere in this file — A4, E3, T2b and the healthy runs above — which is
# sufficient but is NOT the same as "every block carries its own", as an earlier
# revision of this line claimed.
mk_lock "  postcss@8.5.24:" "  eslint@9.0.0:"

# J: the advisories shape assertion is about the VALUE, not the key. `has()` is
#    satisfied by null/string/number/bool; the reads then iterate a non-iterable,
#    jq exits 5 with stderr discarded, and `${prod_hit:-0}` turned the FAILED READ
#    into the number zero. Measured: all four produced the positive "absent from
#    the --prod set" note, byte-identical to a healthy run, on check 2.
printf '{ "advisories": {} }\n' > "$TMP/j.prod.json"
out="$(run "$TMP/a.pkg.json" "$TMP/a.audit.json" "$TMP/lock.yaml" "$TMP/j.prod.json")"
expect_has "J0 control: a well-formed empty --prod set does produce the note" "$out" "absent from the"
for _v in 'null' '"boom"' '123' 'true'; do
  printf '{ "advisories": %s }\n' "$_v" > "$TMP/j.prod.json"
  out="$(run "$TMP/a.pkg.json" "$TMP/a.audit.json" "$TMP/lock.yaml" "$TMP/j.prod.json")"
  expect_has   "J1 --prod advisories:$_v is SKIPPED"                    "$out" "not a clean result"
  expect_lacks "J2 --prod advisories:$_v never claims the --prod set"   "$out" "absent from the"
done

# K: the container can be well-formed while an ELEMENT is not. `{"1":"boom"}` passes
#    the type assertion and errors on `.github_advisory_id`, so the read returns
#    empty — which `${hit:-0}` turned into "matches no advisory in the tree. It
#    suppresses nothing; remove it", an instruction to delete a live acceptance.
printf '{ "advisories": { "1": "boom" } }\n' > "$TMP/k.audit.json"
out="$(run "$TMP/a.pkg.json" "$TMP/k.audit.json" "$TMP/lock.yaml")"
expect_has   "K1 an unindexable advisory element is SKIPPED, not counted as zero" "$out" "returned nothing for"
expect_lacks "K2 and never emits the delete-it instruction"                       "$out" "It suppresses nothing"
# The --prod read has its own copy of the same trap, reachable only past a healthy
# full read, so it needs its own case rather than sharing K1's.
printf '{ "advisories": { "1": "boom" } }\n' > "$TMP/k.prod.json"
out="$(run "$TMP/a.pkg.json" "$TMP/a.audit.json" "$TMP/lock.yaml" "$TMP/k.prod.json")"
expect_has   "K3 an unindexable element in the --prod set is SKIPPED"             "$out" "returned nothing for"
expect_lacks "K4 and never claims absence from the --prod set"                    "$out" "absent from the"

# L: the manifest type check asserts the TOP level only, one level above the blocks
#    the reads consume. Measured: `pnpm.overrides` as a string produced "nothing
#    repaired" plus "no findings", byte-identical to a healthy run.
printf '{ "dependencies": {}, "devDependencies": {}, "pnpm": { "overrides": "garbage" } }\n' > "$TMP/l1.pkg.json"
out="$(run "$TMP/l1.pkg.json" "$TMP/a.audit.json" "$TMP/lock.yaml")"
expect_has   "L1 a non-object pnpm.overrides is SKIPPED"  "$out" "is not an object"
# The needle here was "nothing repaired" until a mutation matrix showed L2 passing
# against a guard with the assertion removed: without a location probe the mutant
# takes the UNVERIFIED branch, whose wording is the actual laundering — it describes
# an unreadable block as an ABSENT one. Assert against the text the mutant emits,
# not against a phrase this fixture never reaches.
expect_lacks "L2 and never describes the unreadable block as absent" "$out" "no overrides in this manifest"
printf '{ "dependencies": {}, "devDependencies": {}, "pnpm": { "auditConfig": { "ignoreGhsas": "GHSA-mh99-v99m-4gvg" } } }\n' > "$TMP/l2.pkg.json"
out="$(run "$TMP/l2.pkg.json" "$TMP/a.audit.json" "$TMP/lock.yaml")"
expect_has   "L3 a non-array ignoreGhsas is SKIPPED"      "$out" "is not an array"
expect_lacks "L4 and never describes the unreadable list as empty" "$out" "no ignoreGhsas entries in this manifest"

# M: requiring BOTH files to carry the counter was itself a bypass, and it went the
#    dangerous way: a --prod file reporting `dependencies: 0` — the exact state the
#    sanity check exists to catch — passed silently whenever the FULL file omitted
#    the counter, which every fixture here does.
printf '{ "advisories": {}, "metadata": { "dependencies": 0 } }\n' > "$TMP/m.prod.json"
out="$(run "$TMP/a.pkg.json" "$TMP/a.audit.json" "$TMP/lock.yaml" "$TMP/m.prod.json")"
expect_has   "M1 one-sided metadata is SKIPPED, not a fall-through" "$out" "the partition cannot be verified"
expect_lacks "M2 and an empty --prod tree never reads as clean"     "$out" "absent from the"

# N: and with the counter on both sides the comparison must actually happen — in
#    both polarities, or M1 could be satisfied by a guard that skips unconditionally.
printf '{ "advisories": { "1": { "github_advisory_id": "GHSA-mh99-v99m-4gvg", "severity": "high" } }, "metadata": { "dependencies": 100 } }\n' > "$TMP/n.full.json"
printf '{ "advisories": {}, "metadata": { "dependencies": 0 } }\n' > "$TMP/n.prod0.json"
out="$(run "$TMP/a.pkg.json" "$TMP/n.full.json" "$TMP/lock.yaml" "$TMP/n.prod0.json")"
expect_has "N1 a --prod run that resolved an empty tree is SKIPPED" "$out" "did not partition a real tree"
# N1 alone asserts only that the sentence was PRINTED, which is not its subject: the
# subject is that the checks stopped. Measured with the `exit 0` removed, the same run
# carried the disclaimer and the verdict it disclaims. Three of the guard's `exit 0`
# are inline rather than via `skip()`, so S2 does not cover them.
expect_lacks "N1b and nothing after the skip runs"                 "$out" "absent from the"
expect_lacks "N1c nor does the summary claim a clean tree"         "$out" "no findings"
printf '{ "advisories": {}, "metadata": { "dependencies": 40 } }\n' > "$TMP/n.prodok.json"
out="$(run "$TMP/a.pkg.json" "$TMP/n.full.json" "$TMP/lock.yaml" "$TMP/n.prodok.json")"
expect_lacks "N2 control: a healthy partition is not SKIPPED"       "$out" "did not partition a real tree"
expect_has   "N3 control: and the suppression checks do run"        "$out" "absent from the"

# O: the location probe matches FORM, not spelling. The first version anchored at
#    column 0 and assumed the key was unquoted, so `"overrides":` — valid YAML pnpm
#    reads identically — produced "the location is verified" with a live acceptance
#    sitting in the probed file.
_ws() { bash "$GUARD" --package-json "$TMP/g.pkg.json" --audit-json "$TMP/g.audit.json" \
        --audit-prod-json "$TMP/g.audit.json" --lockfile "$TMP/lock.yaml" --workspace-yaml "$1" 2>&1; }
printf 'packages:\n  - "."\n"overrides":\n  postcss: ^8.5.18\n' > "$TMP/o1.yaml"
expect_has "O1 a QUOTED workspace key is seen"   "$(_ws "$TMP/o1.yaml")" "may already live there"
printf 'pnpm:\n  overrides:\n    postcss: ^8.5.18\n'            > "$TMP/o2.yaml"
expect_has "O2 an INDENTED workspace key is seen" "$(_ws "$TMP/o2.yaml")" "may already live there"
printf 'packages:\n  - "."\nonlyBuiltDependencies:\n  - esbuild\n' > "$TMP/o3.yaml"
expect_lacks "O3 control: a workspace file carrying none of the keys does not skip" \
             "$(_ws "$TMP/o3.yaml")" "may already live there"

# P: `${PNPM_MAJOR:-0}` defaulted an EMPTY value to the numeric string "0", so
#    `--pnpm-major ""` fell through the numeric check and then failed `[ -n ... ]`,
#    leaving a probe that silently did not run while the output claimed a verified
#    location. Supplied-but-empty is a caller error, not a default.
out="$(bash "$GUARD" --package-json "$TMP/g.pkg.json" --audit-json "$TMP/g.audit.json" \
       --audit-prod-json "$TMP/g.audit.json" --lockfile "$TMP/lock.yaml" --pnpm-major "" 2>&1)"
expect_has   "P1 an empty --pnpm-major is SKIPPED, not defaulted to 0" "$out" "which is not a number"
expect_lacks "P2 and never claims the location was probed"             "$out" "no sign the configuration moved"

# Q: the fixtures above all invent their audit JSON. That makes every check-1 and
#    check-2 premise an ASSUMPTION about a shape pnpm produces, with no independent
#    evidence anywhere in the repo — so `samples/` holds two real captures, taken by
#    area 8's own procedure (the ignore list deleted first, or the suppressed
#    advisory is invisible and check 1 can never fire).
#
#    RECORDED ON pnpm 9.15.9, which is the major the guard actually runs under —
#    `pnpm/action-setup` pins `version: 9` at all five call sites. The first capture
#    was taken on the local 10.28.2, and that was the wrong instrument for exactly
#    the reason this PR exists: evidence true of its source and not of its subject.
#    Re-measured on both majors against the same tree, the difference is real and
#    lands on one field:
#
#      pnpm 10.28.2  findings[].paths: [".>@lhci/cli>uuid"]
#      pnpm  9.15.9  findings[].paths: []
#
#    Everything these checks read — advisory keys, `github_advisory_id`, the
#    metadata counters — is byte-identical across the two. The captures corroborate
#    the numbers the comments cite: 3 advisories in full, 1 in --prod
#    (`@hono/node-server`), 1110 against 487 dependencies.
cat > "$TMP/q.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "auditConfig": { "ignoreGhsas": ["GHSA-mh99-v99m-4gvg"] } } }
J
mk_lock "  x@1.0.0:"
q_out="$(bash "$GUARD" --package-json "$TMP/q.pkg.json" \
        --audit-json "$HERE/samples/pnpm-audit-full.json" \
        --audit-prod-json "$HERE/samples/pnpm-audit-prod.json" \
        --lockfile "$TMP/lock.yaml" --pnpm-major 9 2>&1)"
expect_lacks "Q1 the accepted GHSA is live in the real full capture, so not stale" "$q_out" "STALE SUPPRESSION"
expect_has   "Q2 and absent from the real --prod capture"                          "$q_out" "absent from the"
expect_lacks "Q3 the real counters (1110 vs 487) pass the partition sanity"        "$q_out" "did not partition a real tree"
# Q3 alone passes just as well against a guard that never read the counters, so it
# needs its counterfactual: hand the guard the same two captures the wrong way round
# and 1110 against 487 must FAIL the comparison.
q_swapped="$(bash "$GUARD" --package-json "$TMP/q.pkg.json" \
        --audit-json "$HERE/samples/pnpm-audit-prod.json" \
        --audit-prod-json "$HERE/samples/pnpm-audit-full.json" \
        --lockfile "$TMP/lock.yaml" --pnpm-major 9 2>&1)"
expect_has "Q4 control: the same captures swapped do fail it"                      "$q_swapped" "did not partition a real tree"

# R: the finding COUNTER. `warn` both prints and counts; drop the counting half and
#    the body says DEAD OVERRIDE while the summary says "no findings" — two states,
#    one run, which is the contradiction this guard exists to name in others.
cat > "$TMP/r.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "overrides": { "ghost-pkg": "^9.0.0" } } }
J
echo '{ "advisories": {} }' > "$TMP/r.audit.json"
mk_lock "  postcss@8.5.24:"
out="$(bash "$GUARD" --package-json "$TMP/r.pkg.json" --audit-json "$TMP/r.audit.json" \
      --audit-prod-json "$TMP/r.audit.json" --lockfile "$TMP/lock.yaml" --pnpm-major 9 2>&1)"
expect_has   "R1 the finding is emitted"                            "$out" "DEAD OVERRIDE"
expect_lacks "R2 and the summary does not also report no findings"  "$out" "no findings"
cat > "$TMP/r0.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {}, "pnpm": { "overrides": { "postcss": "^8.5.18" } } }
J
out="$(bash "$GUARD" --package-json "$TMP/r0.pkg.json" --audit-json "$TMP/r.audit.json" \
      --audit-prod-json "$TMP/r.audit.json" --lockfile "$TMP/lock.yaml" --pnpm-major 9 2>&1)"
expect_has   "R0 control: a clean run does report no findings"      "$out" "no findings"

# S: `skip` must EXIT. Without the exit it prints "the suppression checks did not
#    run" and then runs them, so the same output carries the disclaimer and the
#    verdict it disclaims.
printf 'packages:\n  - "."\noverrides:\n  postcss: ^8.5.18\n' > "$TMP/s.ws.yaml"
out="$(bash "$GUARD" --package-json "$TMP/r0.pkg.json" --audit-json "$TMP/r.audit.json" \
      --audit-prod-json "$TMP/r.audit.json" --lockfile "$TMP/lock.yaml" \
      --workspace-yaml "$TMP/s.ws.yaml" 2>&1)"
expect_has   "S1 the skip is reported"                              "$out" "did not run"
expect_lacks "S2 and nothing after it runs"                         "$out" "audit-suppression-guard: no findings"
out="$(bash "$GUARD" --package-json "$TMP/r0.pkg.json" --audit-json "$TMP/r.audit.json" \
      --audit-prod-json "$TMP/r.audit.json" --lockfile "$TMP/lock.yaml" --pnpm-major 9 2>&1)"
expect_has   "S0 control: the same inputs without the skip do reach the summary" "$out" "audit-suppression-guard: no findings"

# U: a STALE entry must `continue`. Without it the same GHSA is reported as matching
#    no advisory AND as accepted-but-absent-from-production, in one run.
cat > "$TMP/u.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "auditConfig": { "ignoreGhsas": ["GHSA-ghost-ghost-ghost"] } } }
J
echo '{ "advisories": { "1": { "github_advisory_id": "GHSA-other-other-other" } } }' > "$TMP/u.audit.json"
echo '{ "advisories": {} }' > "$TMP/u.prod.json"
out="$(bash "$GUARD" --package-json "$TMP/u.pkg.json" --audit-json "$TMP/u.audit.json" \
      --audit-prod-json "$TMP/u.prod.json" --lockfile "$TMP/lock.yaml" --pnpm-major 9 2>&1)"
expect_has   "U1 the stale entry is named"                          "$out" "STALE SUPPRESSION"
expect_lacks "U2 and does not also get a --prod verdict"            "$out" "absent from the"

# V: the two awk guards, separated. E2 pins them only jointly; these do not, and
#    each uses a form measured in a real pnpm lockfile rather than a crafted one.
#
# V1 — the ANCHOR alone. `url` and `base64url` are both real npm packages. The
#      version terminator does NOT reject this one: it reads position 5, which is
#      the `6` of base64, so only the anchor stands between a dead `url` override
#      and silence.
cat > "$TMP/v1.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {}, "pnpm": { "overrides": { "url": "^0.11.4" } } }
J
mk_lock "  base64url@1.0.0:"
out="$(bash "$GUARD" --package-json "$TMP/v1.pkg.json" --audit-json "$TMP/r.audit.json" \
      --audit-prod-json "$TMP/r.audit.json" --lockfile "$TMP/lock.yaml" --pnpm-major 9 2>&1)"
expect_has "V1 a name that merely ENDS the key does not count as present" "$out" "DEAD OVERRIDE"
# V2 — the VERSION TERMINATOR alone, against the lockfile's own `overrides:` block.
#      Measured in this repo's real lockfile: three such lines
#      (`js-yaml@>=4.0.0 <4.3.0:`, `brace-expansion@<1.1.16:`,
#      `brace-expansion@>=2.0.0 <=5.0.7:`). They start with the bare name and `@`,
#      so the anchor passes them; only the digit test tells a DECLARED repair from
#      an INSTALLED package.
cat > "$TMP/v2.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {}, "pnpm": { "overrides": { "js-yaml": "^4.3.0" } } }
J
mk_lock "overrides:" "  js-yaml@>=4.0.0 <4.3.0: ^4.3.0" "  postcss@8.5.24:"
out="$(bash "$GUARD" --package-json "$TMP/v2.pkg.json" --audit-json "$TMP/r.audit.json" \
      --audit-prod-json "$TMP/r.audit.json" --lockfile "$TMP/lock.yaml" --pnpm-major 9 2>&1)"
expect_has "V2 the lockfile's own overrides declaration is not an installed package" "$out" "DEAD OVERRIDE"

# W: `--workspace-yaml` pointing at something unreadable. `-r` guards the `grep` call
#    only; the POSITIVE claim keys off `[ -z "$WS_YAML" ]`, which tests non-emptiness.
#    So a mistyped path SILENCED the UNVERIFIED warning and printed the healthy text
#    instead — the fifth instance of this guard's own defect, and the half left behind
#    when the `--pnpm-major ""` half was fixed one commit earlier.
_wsr() { bash "$GUARD" --package-json "$TMP/g.pkg.json" --audit-json "$TMP/g.audit.json" \
         --audit-prod-json "$TMP/g.audit.json" --lockfile "$TMP/lock.yaml" --workspace-yaml "$1" 2>&1; }
out="$(_wsr "$TMP/does-not-exist.yaml")"
expect_has   "W1 an unreadable --workspace-yaml is a caller error"   "$out" "cannot be read"
expect_lacks "W2 and never claims the probe found nothing"           "$out" "no sign the configuration moved"
out="$(_wsr "$TMP")"
expect_has   "W3 a directory is the same caller error"               "$out" "cannot be read"
out="$(_wsr "$TMP/o3.yaml")"
expect_lacks "W0 control: a readable file without the keys is not a caller error" "$out" "cannot be read"

# X: SEVERITY AND COUNTING at the call sites, not only in the helper. R pins that
#    `warn` counts, using DEAD OVERRIDE — the hygiene finding. Measured 2026-08-01:
#    turning `warn` into `note` at the three OTHER call sites survives the whole
#    suite, including on OVER-BROAD, the one this guard's header calls the dangerous
#    one. A mutant there emits the full advisory text as a ::notice:: and then
#    "no findings." in the same run.
#    Each case must ISOLATE its call site. A first attempt reused the A/C/G fixtures
#    and every counting assertion became a passenger: those runs emit a SECOND
#    warning (the UNVERIFIED pair), so the summary never says "no findings" no matter
#    what the call site under test does. `--pnpm-major 9` silences the location
#    branch; the manifests below leave exactly one finding possible.
_x() { bash "$GUARD" --package-json "$1" --audit-json "$2" --audit-prod-json "$3" \
       --lockfile "$TMP/lock.yaml" --pnpm-major 9 2>&1; }
out="$(_x "$TMP/u.pkg.json" "$TMP/u.audit.json" "$TMP/u.prod.json")"
expect_has   "X1 STALE SUPPRESSION is a warning, not a notice"       "$out" "::warning::STALE SUPPRESSION"
expect_lacks "X2 and is counted"                                     "$out" "no findings"
out="$(_x "$TMP/c.pkg.json" "$TMP/c.audit.json" "$TMP/c.prod.json")"
expect_has   "X3 OVER-BROAD SUPPRESSION is a warning, not a notice"  "$out" "::warning::OVER-BROAD SUPPRESSION"
expect_lacks "X4 and is counted"                                     "$out" "no findings"
# The two UNVERIFIED call sites are separate, and a fixture with an empty `pnpm` block
# fires BOTH — so demoting either one alone stays invisible. Give each its own
# manifest: overrides present (and live) isolates the ignoreGhsas branch, and an
# ignoreGhsas that is present and not stale isolates the overrides branch. No
# `--pnpm-major` here, since the unverified location IS the subject.
cat > "$TMP/x5.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {}, "pnpm": { "overrides": { "postcss": "^8.5.18" } } }
J
out="$(run "$TMP/x5.pkg.json" "$TMP/r.audit.json" "$TMP/lock.yaml")"
expect_has   "X5 UNVERIFIED LOCATION (ignoreGhsas side) is a warning" "$out" "::warning::EMPTY CONFIG, UNVERIFIED LOCATION: no ignoreGhsas"
expect_lacks "X6 and is counted"                                      "$out" "no findings"
cat > "$TMP/x7.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "auditConfig": { "ignoreGhsas": ["GHSA-mh99-v99m-4gvg"] } } }
J
out="$(run "$TMP/x7.pkg.json" "$TMP/c.audit.json" "$TMP/lock.yaml")"
expect_has   "X7 UNVERIFIED LOCATION (overrides side) is a warning"   "$out" "::warning::EMPTY CONFIG, UNVERIFIED LOCATION: no overrides"
expect_lacks "X8 and is counted"                                      "$out" "no findings"

# Y: the remaining input contracts, each of which had exactly one pinned argument.
# Y1/Y2 — a non-integer counter. jq's `numbers` admits floats; `[ ]` does not, so both
#         comparisons errored to stderr and evaluated false, and the guard fell through
#         to the positive claim.
printf '{ "advisories": {}, "metadata": { "dependencies": 0.5 } }\n'   > "$TMP/y.prod.json"
printf '{ "advisories": {}, "metadata": { "dependencies": 100.5 } }\n' > "$TMP/y.full.json"
out="$(run "$TMP/a.pkg.json" "$TMP/y.full.json" "$TMP/lock.yaml" "$TMP/y.prod.json")"
expect_lacks "Y1 a fractional dependency counter never reaches the positive claim" "$out" "absent from the"
expect_has   "Y2 it is reported as an unusable partition instead"                  "$out" "not a clean result"
# Y3/Y4 — `false` is not an absent sub-block. `(x // {})` fires on null AND false, so
#         `"overrides": false` defaulted to `{}` and reported nothing repaired.
printf '{ "dependencies": {}, "devDependencies": {}, "pnpm": { "overrides": false } }\n' > "$TMP/y1.pkg.json"
out="$(run "$TMP/y1.pkg.json" "$TMP/r.audit.json" "$TMP/lock.yaml")"
expect_has   "Y3 a false pnpm.overrides is SKIPPED, not defaulted"                 "$out" "is not an object"
printf '{ "dependencies": {}, "devDependencies": {}, "pnpm": { "auditConfig": { "ignoreGhsas": false } } }\n' > "$TMP/y2.pkg.json"
out="$(run "$TMP/y2.pkg.json" "$TMP/r.audit.json" "$TMP/lock.yaml")"
expect_has   "Y4 a false ignoreGhsas is SKIPPED, not defaulted"                    "$out" "is not an array"
# Y5/Y6 — the lockfile was the one input with no shape contract at all. Empty or
#         truncated, every override key reported DEAD OVERRIDE: 8 false alarms against
#         the real manifest. Fail-closed, but this file's own position is that a false
#         alarm in an observe-only signal is worse than silence.
: > "$TMP/empty.lock.yaml"
out="$(bash "$GUARD" --package-json "$TMP/r0.pkg.json" --audit-json "$TMP/r.audit.json" \
      --audit-prod-json "$TMP/r.audit.json" --lockfile "$TMP/empty.lock.yaml" --pnpm-major 9 2>&1)"
expect_has   "Y5 a lockfile without lockfileVersion is SKIPPED"                    "$out" "not a pnpm lockfile"
expect_lacks "Y6 and never manufactures a dead override from it"                   "$out" "DEAD OVERRIDE"
printf 'lockfileVersion: "9.0"\n  postcss@8.5.24:\n' > "$TMP/ok.lock.yaml"
out="$(bash "$GUARD" --package-json "$TMP/r0.pkg.json" --audit-json "$TMP/r.audit.json" \
      --audit-prod-json "$TMP/r.audit.json" --lockfile "$TMP/ok.lock.yaml" --pnpm-major 9 2>&1)"
expect_lacks "Y0 control: a real lockfile is not SKIPPED"                          "$out" "not a pnpm lockfile"
# Y7 — the readability contract was pinned for one of four required inputs (I1).
for _arg in --audit-json --audit-prod-json --lockfile; do
  bash "$GUARD" --package-json "$TMP/r0.pkg.json" --audit-json "$TMP/r.audit.json" \
       --audit-prod-json "$TMP/r.audit.json" --lockfile "$TMP/lock.yaml" \
       "$_arg" "$TMP/nope-$_arg" >/dev/null 2>&1
  [ $? -eq 2 ] && ok "Y7 an unreadable $_arg fails loudly" \
               || bad "Y7 an unreadable $_arg fails loudly" "exit != 2"
done

echo
echo "audit-suppression-guard fixtures: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ] || exit 1
