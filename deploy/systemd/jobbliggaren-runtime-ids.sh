#!/usr/bin/env bash
# jobbliggaren-runtime-ids — read the runtime uid and gid out of ONE image reference.
#
# usage:  jobbliggaren-runtime-ids.sh <image-ref>
# stdout: exactly two lines — uid, then gid
# stderr: every diagnostic
# exit:   0 on success, non-zero on any failure
#
# WHY THIS IS ITS OWN FILE. Two callers need the same answer and must not be able to disagree
# about how it is obtained: jobbliggaren-inject-secrets.sh SETS the ownership of
# /run/jobbliggaren/secrets from it, and jobbliggaren-reconcile.sh GATES the apply on it. If the
# gate measured differently from how the setter set, the gate would produce false refusals or
# false passes for a reason neither file could show (#1295).
#
# Only the MEASUREMENT is shared. Each caller keeps its own resolution and its own comparison
# policy, and the reason each resolves a different shape is written where that resolution lives.
#
# WHAT THE ARGUMENT CHECKS BELOW ARE, AND ARE NOT. They are a SYNTAX filter, not a trust
# boundary. There is no registry allowlist here and there cannot be one: the two callers pass
# references of different shapes, and deciding which registries are acceptable is a policy
# neither of them delegates. Every security property of this script is therefore INHERITED from
# its callers — reconcile has attested its digest, injection has an operator at the keyboard —
# and a third caller would inherit none of it. Read that before adding one.
#
# EVERYTHING EXCEPT THE TWO NUMBERS GOES TO STDERR, and that is load-bearing rather than style —
# the same reason jobbliggaren-inject-secrets.sh:68-71 gives. The caller captures stdout, so a
# diagnostic printed there would land INSIDE the caller's variable and be parsed as an id.
set -euo pipefail

log() { printf '%s\n' "$*" >&2; }
die() { log "REFUSING: $*"; exit 1; }

[[ $# -eq 1 ]] || die "usage: $0 <image-ref> (got $# arguments)"

ref="$1"
[[ -n "$ref" ]] || die "empty image reference"

# A LEADING DASH WOULD REACH docker's FLAG PARSER, not its image slot. This script is invoked by
# root from a unit; an argument that turns into an option is the difference between reading an id
# and running something else entirely.
case "$ref" in
-*) die "image reference may not begin with '-': '${ref}'" ;;
esac

# Uppercase is admitted DELIBERATELY, and the CTO bind's narrower charset is widened here on
# purpose: a tag may legally carry it, and jobbliggaren-inject-secrets.sh's own reference guard
# already allows `:[A-Za-z0-9._-]+`. Rejecting it would make this script refuse a reference its
# caller had just accepted — a false refusal, which on this box is the always-lit alarm every
# unit here is written against. What the class excludes is what a shell or a flag parser would
# read as something other than a name: whitespace, quotes, `$`, backticks, `;`, `-` in front.
[[ "$ref" =~ ^[A-Za-z0-9./_:@-]+$ ]] || die "image reference contains characters outside
[A-Za-z0-9./_:@-]: '${ref}'"

# Absolute path, as jobbliggaren-reconcile.sh already does for docker: PATH resolution in a
# root-run gate lets anything earlier on PATH answer the question.
#
# CONTAINED, because one of the two callers runs an image nothing has attested. Reading two
# numbers needs no network, no capabilities and no way to acquire more — and the compose file
# already sets `no-new-privileges` on all nine services, so an uncontained `docker run` here
# would be the loosest execution on the box. `--network none` also removes the default bridge
# and `NET_RAW` with it.
out=$(/usr/bin/docker run --rm --network none --cap-drop ALL \
  --security-opt no-new-privileges --entrypoint sh "$ref" -c 'id -u; id -g' 2>/dev/null) \
  || die "could not read the runtime ids from '${ref}' (is it pulled? is dockerd up?)"

mapfile -t ids <<<"$out"

# EXACTLY two lines, not "at least two". A third line is not a malformed answer to ignore — on
# the injection path the image is unattested, and root then chowns the master key to whatever
# the first two numeric lines said while a later line went unread. Measured 2026-08-12: without
# this, output of `1654\n1654\nEXTRA` exits 0 and reports the pair.
[[ "${#ids[@]}" -eq 2 ]] || die "expected exactly two lines from '${ref}', got ${#ids[@]}"

uid="${ids[0]:-}"
gid="${ids[1]:-}"

# BOTH lines are validated, and separately. `id -u` succeeding while `id -g` prints nothing would
# otherwise hand the caller an empty gid, which compares unequal to everything and refuses for a
# reason the message would not name.
[[ "$uid" =~ ^[0-9]+$ ]] || die "measured uid is not numeric: '${uid}' (from '${ref}')"
[[ "$gid" =~ ^[0-9]+$ ]] || die "measured gid is not numeric: '${gid}' (from '${ref}')"

printf '%s\n%s\n' "$uid" "$gid"
