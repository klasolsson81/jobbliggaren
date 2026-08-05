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
# and the other two are `// indirect` requires with no `replace` anywhere in caddy's
# go.mod, so under Minimal Version Selection a require in the main module raises them.
# (An earlier version of this file called them "pinned" and concluded they were
# unrepairable. That was reasoned, not measured, and it was wrong: a require is a floor.)
#
# THE TRADE IS REAL AND IS NAMED: this replaces an upstream-signed binary from a published
# tag with our own, carrying dependency versions upstream never tested together. It is
# taken because the alternative is knowingly shipping five HIGH advisories on the only
# internet-facing process in the stack, behind no CDN and no WAF.
FROM caddy:2-builder-alpine AS build

# --output is explicit because its absence is a SILENT failure: xcaddy writes ./caddy into
# the builder's WORKDIR, which happens to be /usr/bin today. Move that WORKDIR and the COPY
# below picks up the builder image's own stock binary instead — the build stays green and
# produces a plausible image with the five advisories back.
#
# `--with` rather than `--replace`, also deliberate: --with adds a require, which under MVS
# is a FLOOR and can only raise. --replace would be a ceiling and would silently hold these
# versions down if caddy later required newer ones. It is off-label — xcaddy documents
# --with for plugins — and it adds a blank import of each module root, which both of these
# have. The measurement beats the documentation here, but the next person repairing a
# module without an importable root will need --replace and should know why.
RUN xcaddy build v2.11.4 \
      --output /usr/bin/caddy \
      --with golang.org/x/text@v0.39.0 \
      --with google.golang.org/grpc@v1.82.1

FROM caddy:2-alpine

# The base layer's own OS packages still need patching: the published tag is not rebuilt
# when alpine ships a fix, so c-ares, curl and libcurl arrive stale. This closes the class
# that has a fix available upstream.
RUN apk --no-cache upgrade

COPY --from=build /usr/bin/caddy /usr/bin/caddy

COPY Caddyfile /etc/caddy/Caddyfile
