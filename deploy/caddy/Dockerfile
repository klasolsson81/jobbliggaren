# Jobbliggaren edge image. Build context: this directory.
#
# The Caddyfile is baked in rather than bind-mounted so the edge configuration is
# pinned to an image digest and travels with the same reconcile-pull as everything
# else — a mounted file would drift independently of the tag the box runs.
FROM caddy:2-alpine

# Patch the base layer's OS packages. The blocking scanner in `ci` measured five fixable
# HIGH advisories here (c-ares, curl, libcurl) that the published tag has not been rebuilt
# for. `apk upgrade` closes exactly the class that has a fix available upstream; anything
# still reported after this is a vulnerability in the caddy binary itself and needs an
# upstream release, not a change here.
RUN apk --no-cache upgrade


COPY Caddyfile /etc/caddy/Caddyfile
