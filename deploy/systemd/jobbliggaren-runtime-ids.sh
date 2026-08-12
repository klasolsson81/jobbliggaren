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
# The two callers legitimately resolve DIFFERENT references, and that stays with them: injection
# resolves a tag out of the compose file (nothing is verified yet, a human is driving), reconcile
# passes the digest it has just attested (the TOCTOU argument in its own header). Only the
# measurement is shared; neither the resolution nor the comparison policy is.
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
out=$(/usr/bin/docker run --rm --entrypoint sh "$ref" -c 'id -u; id -g' 2>/dev/null) \
  || die "could not read the runtime ids from '${ref}' (is it pulled? is dockerd up?)"

mapfile -t ids <<<"$out"
uid="${ids[0]:-}"
gid="${ids[1]:-}"

# BOTH lines are validated, and separately. `id -u` succeeding while `id -g` prints nothing would
# otherwise hand the caller an empty gid, which compares unequal to everything and refuses for a
# reason the message would not name.
[[ "$uid" =~ ^[0-9]+$ ]] || die "measured uid is not numeric: '${uid}' (from '${ref}')"
[[ "$gid" =~ ^[0-9]+$ ]] || die "measured gid is not numeric: '${gid}' (from '${ref}')"

printf '%s\n%s\n' "$uid" "$gid"
