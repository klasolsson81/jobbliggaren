# Jobbliggaren edge image. Build context: this directory.
#
# The Caddyfile is baked in rather than bind-mounted so the edge configuration is
# pinned to an image digest and travels with the same reconcile-pull as everything
# else — a mounted file would drift independently of the tag the box runs.
FROM caddy:2-alpine

COPY Caddyfile /etc/caddy/Caddyfile
