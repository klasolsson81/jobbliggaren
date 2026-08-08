#!/usr/bin/env bash
#
# Fixture tests for verify-image-attestation.sh.
#
# Run:  bash deploy/systemd/verify-image-attestation.test.sh
#
# NEEDS NO DAEMON, NO REGISTRY AND NO NETWORK. cosign is replaced on PATH by a stub whose
# behaviour each case dictates, so the suite tests the PREDICATE — argument handling, the
# tag refusal, the exit-code split, the identity that gets pinned — rather than Sigstore.
# That separation is the reason this logic lives in its own file: the wrapper around it needs
# a docker daemon, a registry and root, so anything inside the wrapper is untestable in CI.
#
# THE NEGATIVE FIXTURES CARRY THE FILE. A gate whose cases all pass has shown it does not
# crash, not that it refuses anything.
#
# THREE OUTCOMES, NEVER COLLAPSED:
#   exit 0 — this digest was built by our workflow on main.
#   exit 1 — it was not, or we cannot tell why not.
#   exit 2 — could not answer: cosign missing, a tag passed instead of a digest, registry down.
#            "The tool is missing" reading as "verified" is the failure this suite exists for.
#
# WHAT THIS SUITE DOES NOT PIN, measured by mutation 2026-08-08. Every outage pattern in the
# SUT is killed by exactly one case here. The two REFUSAL patterns ("no matching attestations",
# "none of the expected") are not: deleting either leaves the suite green, because the default
# arm returns exit 1 as well. That is the SUT's design rather than a gap — the refusal arms
# exist to label the journal line, and the exit contract is identical either way — but a reader
# should not mistake the two refusal cases below for coverage of those patterns. They pin that
# a refusal is 1 and never 0 or 2, which is the part that matters.

set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
readonly SUT="$script_dir/verify-image-attestation.sh"
[ -f "$SUT" ] || {
  echo "missing script under test: $SUT" >&2
  exit 1
}

TMPROOT=$(mktemp -d)
readonly TMPROOT
trap 'rm -rf "$TMPROOT"' EXIT

readonly BIN="$TMPROOT/bin"
mkdir -p "$BIN"

pass=0
fail=0

# Writes a cosign stub that exits with $1 after printing $2. The stub also records the
# arguments it was called with, so a case can assert on what the SUT actually pinned rather
# than only on the verdict it returned.
stub_cosign() {
  local code="$1" out="${2:-}"
  cat >"$BIN/cosign" <<EOF
#!/usr/bin/env bash
printf '%s\n' "\$@" > "$TMPROOT/last-args"
[ -n '$out' ] && printf '%s\n' '$out'
exit $code
EOF
  chmod +x "$BIN/cosign"
}

# Runs the SUT with ONLY the stub directory plus the real coreutils on PATH.
run_sut() {
  PATH="$BIN:/usr/bin:/bin" bash "$SUT" "$@" >"$TMPROOT/out" 2>&1
}

expect_exit() {
  local want="$1" desc="$2"
  shift 2
  local got=0
  run_sut "$@" || got=$?
  if [ "$got" -eq "$want" ]; then
    pass=$((pass + 1))
    echo "  ok   $desc (exit $got)"
  else
    fail=$((fail + 1))
    echo "  FAIL $desc — wanted exit $want, got $got" >&2
    sed 's/^/       /' "$TMPROOT/out" >&2
  fi
}

readonly DIGEST_REF="ghcr.io/klasolsson81/jobbliggaren-api@sha256:0000000000000000000000000000000000000000000000000000000000000000"

echo "verify-image-attestation.sh"

echo "-- usage"
stub_cosign 0
expect_exit 2 "no argument is a usage error, not a pass"
expect_exit 2 "two arguments is a usage error" "$DIGEST_REF" extra
expect_exit 2 "--help exits 2, never 0" --help

echo "-- the digest requirement"
stub_cosign 0
expect_exit 2 "a TAG is refused rather than resolved" "ghcr.io/klasolsson81/jobbliggaren-api:latest"
expect_exit 2 "a bare image name is refused" "ghcr.io/klasolsson81/jobbliggaren-api"
expect_exit 0 "a digest reference is accepted" "$DIGEST_REF"

echo "-- the tool must be present"
rm -f "$BIN/cosign"
expect_exit 2 "cosign missing is UNANSWERABLE, never verified" "$DIGEST_REF"

echo "-- the verdict split"
stub_cosign 1 "Error: no matching attestations found"
expect_exit 1 "no attestation is a refusal" "$DIGEST_REF"

stub_cosign 1 "Error: none of the expected identities matched"
expect_exit 1 "wrong signer identity is a refusal" "$DIGEST_REF"

# EVERY outage pattern gets a case, not one representative. A suite that exercises one arm of
# an alternation reports the whole alternation as covered: mutation-verified 2026-08-08, an
# edit that deleted the `connection refused` and `no such host` arms left the suite green
# because only `i/o timeout` was ever driven through it. Each arm now turns exactly one line
# red when it is removed.
stub_cosign 1 "Error: GET https://ghcr.io/v2/: dial tcp: i/o timeout"
expect_exit 2 "outage: a timeout is unanswerable, not a refusal" "$DIGEST_REF"

stub_cosign 1 "Error: GET https://ghcr.io/v2/: dial tcp 140.82.121.33:443: connection refused"
expect_exit 2 "outage: connection refused is unanswerable" "$DIGEST_REF"

stub_cosign 1 "Error: Get \"https://tuf-repo-cdn.sigstore.dev/\": dial tcp: lookup tuf-repo-cdn.sigstore.dev: no such host"
expect_exit 2 "outage: DNS failure (no such host) is unanswerable" "$DIGEST_REF"

# These two deliberately avoid the words "timeout" and "unexpected status". A message carrying
# either is caught by those arms regardless, so a case built from one would report this arm as
# covered while proving nothing about it — measured, both survived mutation until the wording
# was made disjoint.
stub_cosign 1 "Error: net/http: TLS handshake error, remote error: bad certificate"
expect_exit 2 "outage: a TLS handshake failure is unanswerable" "$DIGEST_REF"

stub_cosign 1 "Error: GET https://ghcr.io/v2/token: 503 Service Unavailable"
expect_exit 2 "outage: a bare 503 is unanswerable" "$DIGEST_REF"

stub_cosign 1 "Error: GET https://ghcr.io/v2/token: 502 Bad Gateway"
expect_exit 2 "outage: a bare 502 is unanswerable" "$DIGEST_REF"

stub_cosign 1 "Error: GET https://ghcr.io/v2/token: 500 Internal Server Error"
expect_exit 2 "outage: a bare 500 is unanswerable" "$DIGEST_REF"

stub_cosign 1 "Error: reading bundle: unexpected status while fetching referrers"
expect_exit 2 "outage: cosign's generic HTTP failure is unanswerable" "$DIGEST_REF"

stub_cosign 1 "Error: something nobody has seen before"
expect_exit 1 "an unrecognised failure refuses rather than passes" "$DIGEST_REF"

echo "-- what the gate actually pins"
# The verdict alone cannot catch an identity that silently loosened: a stub exiting 0 passes
# whatever the SUT asked for. These assert on the arguments themselves.
stub_cosign 0
run_sut "$DIGEST_REF" || true
assert_arg() {
  local needle="$1" desc="$2"
  if grep -qxF -- "$needle" "$TMPROOT/last-args"; then
    pass=$((pass + 1))
    echo "  ok   $desc"
  else
    fail=$((fail + 1))
    echo "  FAIL $desc — not among the arguments passed to cosign" >&2
    sed 's/^/       /' "$TMPROOT/last-args" >&2
  fi
}
assert_arg "https://github.com/klasolsson81/jobbliggaren/.github/workflows/release-images.yml@refs/heads/main" \
  "the identity pins workflow AND ref, exactly"
assert_arg "https://token.actions.githubusercontent.com" "the OIDC issuer is GitHub Actions"
assert_arg "--new-bundle-format" "the new bundle format is requested (required on 2.5.x)"
assert_arg "$DIGEST_REF" "the digest reaches cosign unchanged"

# A trust root passed with --trusted-root would silence revocation. Its ABSENCE is the
# decision (see the script header), so it is pinned as an absence.
if grep -qxF -- "--trusted-root" "$TMPROOT/last-args"; then
  fail=$((fail + 1))
  echo "  FAIL the trust root must be FETCHED, not pinned — a pinned root cannot learn of a revocation" >&2
else
  pass=$((pass + 1))
  echo "  ok   no pinned trust root (revocation stays reachable)"
fi

echo
echo "passed: $pass   failed: $fail"
[ "$fail" -eq 0 ]
