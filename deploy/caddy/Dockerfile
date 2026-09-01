# Jobbliggaren edge image. Build context: this directory.
#
# The Caddyfile is baked in rather than bind-mounted so the edge configuration is
# pinned to an image digest and travels with the same reconcile-pull as everything
# else — a mounted file would drift independently of the tag the box runs.
#
# CADDY IS COMPILED HERE RATHER THAN TAKEN FROM THE PUBLISHED TAG, and it is a repair
# rather than a preference. The blocking scanner measured five HIGH advisories in
# `usr/bin/caddy` on `caddy:2-alpine` — three in the Go stdlib it was built with, two in
# `x/text` and `grpc`. Moving to a newer published tag was not an option: measured
# 2026-08-05, `caddy:2-alpine` resolved to 2.11.4 and that was the latest upstream release.
# That measurement is why compiling was chosen; it is not a standing claim, and it stops
# being the question at all now that the binary no longer comes from the tag.
#
# THE VERSION IS PINNED HERE AND NOTHING PROPOSES BUMPS FOR IT. `FROM caddy:2-alpine` no
# longer decides the caddy version — the COPY below overwrites it — so a Dependabot
# base-image bump moves the base layer and not the binary. Raising `v2.11.4` is a manual
# act, and Caddy ships security fixes in patch releases. The gap is registered on the
# `/deploy/caddy` entry in .github/dependabot.yml.
#
# ADR 0065 Amendment 2026-07-28 Beslut 1: repair outranks acceptance, and an advisory may
# be accepted only when no dependency-level fix EXISTS — demonstrated, not asserted. Both
# fixes exist. The builder image carries a Go toolchain new enough for the stdlib three,
# and the others are requires with no `replace` anywhere in caddy's
# go.mod, so under Minimal Version Selection a require in the main module raises them.
# (An earlier version of this file called them "pinned" and concluded they were
# unrepairable. That was reasoned, not measured, and it was wrong: a require is a floor.)
#
# THE TRADE IS REAL AND IS NAMED: this replaces an upstream-signed binary from a published
# tag with our own, carrying dependency versions upstream never tested together. It is
# taken because the alternative is knowingly shipping five HIGH advisories on the only
# internet-facing process in the stack, behind no CDN and no WAF.
FROM caddy:2-builder-alpine AS build

# --output writes to a path nothing else creates. The builder's own stock caddy sits at
# /usr/bin/caddy, so producing there — or relying on the WORKDIR default, which is that
# same directory — means a build that fails to produce a binary still yields a working
# image built from the stock one, with all five advisories back. Writing to /out makes
# that a hard COPY error.
#
# --with, not --replace: --with adds a require, which under MVS is a floor and can only
# raise. --replace is a ceiling and would hold these versions down if caddy later required
# newer ones.
#
# The third entry names a PACKAGE, not the module root, and the asymmetry is deliberate.
# Measured 2026-09-02: `--with golang.org/x/crypto@v0.55.0` fails the build with `cannot
# find module providing package golang.org/x/crypto` — that root carries no .go files, so
# the blank import xcaddy writes has nothing to bind. `go get` resolves a package to its
# containing module and go.mod can only require modules, so the require written is still
# `golang.org/x/crypto v0.55.0` and the floor above is preserved.
# bcrypt because `basic_auth` (Caddyfile) already links it, so the import adds nothing to
# the binary. Never `ssh` (nothing links it today), and never anything under `x509roots/`
# — that is a separate module with its own require, so it would raise the wrong one and
# leave the advisory in place behind a green build.
RUN mkdir -p /out \
    && xcaddy build v2.11.4 \
      --output /out/caddy \
      --with golang.org/x/text@v0.39.0 \
      --with google.golang.org/grpc@v1.82.1 \
      --with golang.org/x/crypto/bcrypt@v0.55.0

FROM caddy:2-alpine AS runtime

# The base layer's own OS packages still need patching: the published tag is not rebuilt
# when alpine ships a fix, so c-ares, curl and libcurl arrive stale. This closes the class
# that has a fix available upstream.
RUN apk --no-cache upgrade

COPY --from=build /out/caddy /usr/bin/caddy

COPY Caddyfile /etc/caddy/Caddyfile

# The three challenge snippets the Caddyfile imports by ACME_CHALLENGE_MODE. Baked in for the
# same reason the Caddyfile is: the edge configuration stays pinned to an image digest, so the
# proof is reproducible from a tag rather than from an operator remembering a bind-mount.
COPY challenge/ /etc/caddy/challenge/
