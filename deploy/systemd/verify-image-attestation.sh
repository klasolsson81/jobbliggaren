#!/usr/bin/env bash
# verify-image-attestation — did OUR workflow, on main, build this exact image?
#
# The reconcile unit pulls five images from GHCR every hour and applies them as root. Without
# this predicate the whole trust chain is "nobody has taken over the GitHub account yet":
# `latest` is mutable, so the Trivy gate in `release-images.yml` scans the image the workflow
# BUILT and says nothing about the image the box pulls an hour later under the same tag.
#
# THIS TAKES A DIGEST, NEVER A TAG, AND THAT IS THE POINT. Verifying `…:latest` and then
# letting `docker compose up` resolve `latest` again is two lookups with two possible answers —
# the TOCTOU the tag mutability creates in the first place. The caller resolves the tag ONCE,
# by pulling, and passes the digest belonging to that image's OWN repository — never simply
# `{{index .RepoDigests 0}}`, because an image id can carry several and index 0 is not a
# contract. `jobbliggaren-reconcile.sh` is the reference caller and refuses on zero or
# on two distinct digests for the same repository.
#
# WHY cosign AND NOT `gh attestation verify` (senior-cto-advisor, 2026-08-08). Debian 13 ships
# cosign 2.5.0, which is exactly the version that introduced `--new-bundle-format`, and apt
# security-maintains it. The gh route needs >= 2.97.0 (2.97.0 fixes a verification BYPASS:
# regex metacharacters in the identity matchers were not escaped), Debian ships 2.46.0, and
# GitHub's own apt repo serves only "latest" — so the box's unattended-upgrades would move the
# gate's own binary under it, unpinnable. gh also refuses to run without GH_TOKEN in every mode
# (cli/cli#11803, open); in OCI-bundle mode the check is presence-only, so a dummy string works
# today. Building a security control on an open bug's current behaviour pins the moment, not
# the contract — and the next operator reads the dummy token as breakage and "fixes" it with a
# real PAT.
#
# THE TRUST ROOT IS FETCHED, NOT PINNED, and that is deliberate. A pinned root can never learn
# that a key was revoked — TUF's entire threat model is safe transport over an untrusted
# channel, so pinning shrinks the availability surface and GROWS the trust surface. The failure
# shapes decide it: fetched fails on a TUF outage and self-heals on the next tick, which is the
# service unit's own stated logic; pinned fails ~3x a year at root rotation, forever, silently,
# on a box whose only alarm channel is the journal (#1175 — no log sink exists).
#
# EXIT CONTRACT, and 2 never collapses into 1:
#   0 — this digest carries a provenance attestation from OUR workflow, on main.
#   1 — it does not. The image may be fine; it is not PROVEN, and that is the same decision.
#   2 — could not answer (cosign absent, bad usage, registry unreachable). A refusal must
#       never read as a pass, and "the tool is missing" must never read as "verified".
set -euo pipefail

readonly OIDC_ISSUER="https://token.actions.githubusercontent.com"
readonly SIGNER_REPO="klasolsson81/jobbliggaren"
readonly SIGNER_WORKFLOW=".github/workflows/release-images.yml"
readonly SIGNER_REF="refs/heads/main"

# The exact SAN the workflow's OIDC token carries. An exact match pins BOTH the workflow file
# and the ref: a build of the same file from a feature branch produces a different identity and
# fails here, which is the property `--signer-workflow`-style matching gives up.
readonly CERT_IDENTITY="https://github.com/${SIGNER_REPO}/${SIGNER_WORKFLOW}@${SIGNER_REF}"

usage() {
  echo "usage: $0 <image@sha256:digest>" >&2
  echo "  Takes a DIGEST reference. A tag is refused, not resolved." >&2
}

if [ "$#" -ne 1 ]; then
  usage
  exit 2
fi

case "${1:-}" in
  -h | --help)
    usage
    exit 2
    ;;
esac

readonly REF="$1"

# A tag reaching this point is a caller bug, and resolving it here would silently reintroduce
# the second lookup this script exists to remove. Refuse rather than help.
case "$REF" in
  *@sha256:*) ;;
  *)
    echo "::error::verify-image-attestation: not a digest reference: $REF" >&2
    echo "  Resolve the tag once and pass the digest for THIS repository — e.g." >&2
    echo "    docker image inspect --format '{{range .RepoDigests}}{{println .}}{{end}}' <image> | grep '^<repo>@'" >&2
    echo "  and refuse unless exactly one matches; index 0 is not a contract." >&2
    echo "  and pass the result. Verifying a tag and then applying it is a TOCTOU." >&2
    exit 2
    ;;
esac

if ! command -v cosign >/dev/null 2>&1; then
  echo "::error::verify-image-attestation: cosign not found on PATH." >&2
  echo "  Debian 13: apt-get install cosign (ships 2.5.0, the minimum for --new-bundle-format)." >&2
  exit 2
fi

# `--new-bundle-format` is required on cosign 2.5.x and is the default from 3.0; passing it
# explicitly keeps one command correct on both, which matters because the box is on Debian's
# 2.5.0 while CI and a future upgrade may be on 3.x.
#
# Captured, not piped. A pipeline's exit status is the LAST command's, so `cosign … | grep …`
# would report grep's verdict on an empty stream — the shape that has produced a green gate
# over an unmeasured run in this repo before.
output=$(
  cosign verify-attestation \
    --new-bundle-format \
    --type slsaprovenance1 \
    --certificate-oidc-issuer "$OIDC_ISSUER" \
    --certificate-identity "$CERT_IDENTITY" \
    "$REF" 2>&1
) && status=0 || status=$?

if [ "$status" -eq 0 ]; then
  echo "verified: $REF"
  echo "  built by $SIGNER_WORKFLOW on $SIGNER_REF in $SIGNER_REPO"
  exit 0
fi

# cosign does not give distinct exit codes for "no attestation", "wrong identity" and "network
# failure" — it exits 1 for all three. Read the message so a registry outage is reported as
# unanswerable (2) rather than as a failed verification (1): the operator response differs, and
# an outage that reads as a compromise trains people to override the gate.
case "$output" in
*"no matching attestations"* | *"no attestations found"* | *"none of the expected"*)
  echo "::error::verify-image-attestation: NOT verified: $REF" >&2
  echo "$output" >&2
  exit 1
  ;;
# Each arm below has a case in the suite that ONLY it catches, so removing any one of them
# turns exactly one line red. `i/o timeout` used to sit here too and was removed: `timeout`
# already subsumes it, and a pattern no case can distinguish is a pattern nobody is checking.
*"connection refused"* | *"no such host"* | *"timeout"* | *"TLS handshake"* | *"unexpected status"* | *"500 Internal"* | *"502 Bad"* | *"503 Service"*)
  echo "::error::verify-image-attestation: could not answer for $REF (registry or TUF unreachable)" >&2
  echo "$output" >&2
  exit 2
  ;;
*)
  # Unrecognised failure. It goes to 1, not 2: an unknown reason to distrust an image is still
  # a reason to distrust it, and the reconcile caller treats 1 and 2 identically anyway (it
  # refuses to apply). The split exists for the human reading the journal afterwards.
  echo "::error::verify-image-attestation: NOT verified: $REF" >&2
  echo "$output" >&2
  exit 1
  ;;
esac
