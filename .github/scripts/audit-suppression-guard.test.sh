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

run() { # run <pkg> <audit> <lock>
  bash "$GUARD" --package-json "$1" --audit-json "$2" --lockfile "$3" 2>&1
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

# C: THE DANGEROUS ONE — the same bare GHSA now reaches a production root.
#    Acceptance was granted dev-only; a bare GHSA never re-checks that.
cat > "$TMP/c.pkg.json" <<'J'
{ "dependencies": { "shadcn": "^4.12.0" }, "devDependencies": { "eslint": "^9.0.0" },
  "pnpm": { "auditConfig": { "ignoreGhsas": ["GHSA-mh99-v99m-4gvg"] } } }
J
cat > "$TMP/c.audit.json" <<'J'
{ "advisories": { "1": { "github_advisory_id": "GHSA-mh99-v99m-4gvg",
    "module_name": "brace-expansion", "severity": "high",
    "findings": [ { "paths": [
      ". > eslint@9.0.0 > minimatch@3.1.5 > brace-expansion@1.1.16",
      ". > shadcn@4.12.0 > ts-morph@26.0.0 > brace-expansion@1.1.16" ] } ] } } }
J
mk_lock "  shadcn@4.12.0:"
out="$(run "$TMP/c.pkg.json" "$TMP/c.audit.json" "$TMP/lock.yaml")"
expect_has "C1 a production path under an accepted GHSA is named" "$out" "OVER-BROAD SUPPRESSION"
expect_has "C2 the offending production root is named"            "$out" "shadcn"

# D: an override key naming a package the lockfile does not carry.
cat > "$TMP/d.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "overrides": { "ghost-pkg": "^9.0.0" } } }
J
echo '{ "advisories": {} }' > "$TMP/d.audit.json"
mk_lock "  postcss@8.5.24:"
out="$(run "$TMP/d.pkg.json" "$TMP/d.audit.json" "$TMP/lock.yaml")"
expect_has "D1 an override matching no package is named" "$out" "DEAD OVERRIDE"

# E: open key pinned a consumer BACK below its own floor — the silent one.
cat > "$TMP/e.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {},
  "pnpm": { "overrides": { "sharp": "^0.35.0" } } }
J
echo '{ "advisories": {} }' > "$TMP/e.audit.json"
mk_lock "  sharp@0.34.5:"
out="$(run "$TMP/e.pkg.json" "$TMP/e.audit.json" "$TMP/lock.yaml")"
expect_has "E1 a resolved version below an open key's floor is named" "$out" "PINNED A CONSUMER BACK"

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

# E3: lockfile entries end in `:`. Leaving it on the version made every
#     comparison compare against `7.28.0:` and fire on an exact match.
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

# G: empty configuration is a legitimate state, not a finding.
cat > "$TMP/g.pkg.json" <<'J'
{ "dependencies": {}, "devDependencies": {}, "pnpm": {} }
J
echo '{ "advisories": {} }' > "$TMP/g.audit.json"
mk_lock "  x@1.0.0:"
out="$(run "$TMP/g.pkg.json" "$TMP/g.audit.json" "$TMP/lock.yaml")"
expect_has  "G1 an empty ignore list is stated, not warned" "$out" "nothing accepted"
expect_lacks "G2 empty configuration raises no warning"     "$out" "::warning::"

# H: observe-only — a tree full of findings still exits 0. If this ever fails,
#    the guard has become a gate, which is the one thing it must never be.
bash "$GUARD" --package-json "$TMP/c.pkg.json" --audit-json "$TMP/c.audit.json" --lockfile "$TMP/lock.yaml" >/dev/null 2>&1
[ $? -eq 0 ] && ok "H1 findings do not change the exit code (observe-only)" \
             || bad "H1 findings do not change the exit code (observe-only)" "exit != 0"

# I: usage errors DO exit non-zero — a guard that cannot read its inputs must
#    not report silence as cleanliness.
bash "$GUARD" --package-json "$TMP/nope.json" --audit-json "$TMP/g.audit.json" --lockfile "$TMP/lock.yaml" >/dev/null 2>&1
[ $? -eq 2 ] && ok "I1 an unreadable input fails loudly" || bad "I1 an unreadable input fails loudly" "exit != 2"

echo
echo "audit-suppression-guard fixtures: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ] || exit 1
