# Jobbliggaren web (Next.js) Dockerfile — multi-stage, non-root, standalone output.
# Build context: THIS directory, not the repo root — the root .dockerignore excludes
# `web/`, and this app carries its own pnpm-lock.yaml:
#
#   docker build -f web/jobbliggaren-web/Dockerfile \
#     --build-arg NEXT_PUBLIC_SITE_URL=https://<host> web/jobbliggaren-web
#
# BUILT BY CI, NEVER ON THE BOX — capacity condition 1 (ADR 0050 Amendment 2026-08-04
# §2): an 8 GB host cannot run a Next build alongside the stack. That is a requirement
# on whoever builds this file, and nothing in this file can enforce it. Nothing builds
# it yet; the builder and the compose stack that consumes the result by tag both land
# under #196, and until they do the requirement is stated and not held.

# ---------------------------------------------------------------------------
# Stage 1: dependencies
#
# NODE AND PNPM MAJORS ARE PINNED TO WHAT CI RUNS, and neither pin is cosmetic.
# `pnpm-lock.yaml` is lockfileVersion 9.0 and every `pnpm/action-setup` call site in
# the repo pins `version: 9`. There is no `packageManager` field anywhere here, so a
# bare `corepack enable` would resolve whatever pnpm ships as the image default — and
# pnpm 11 reads NONE of the `pnpm` block in package.json (CLAUDE.md §9.2). That block
# is the vulnerability-suppression surface: eight `overrides` entries across seven
# packages, the single accepted advisory, and `ignoredBuiltDependencies`. Dropping it
# would ship an image whose remediations were silently not applied while every gate
# still reported clean. Node follows the same rule for the weaker reason: no CI job
# runs a Node major other than 22, so a different one here builds what nothing tests.
# ---------------------------------------------------------------------------
FROM node:22-alpine AS deps
RUN npm install --global pnpm@9
WORKDIR /app
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

# ---------------------------------------------------------------------------
# Stage 2: build
#
# NEXT_PUBLIC_* IS INLINED AT BUILD TIME, so a runtime environment variable cannot
# correct it later — Next's own documentation names the single-image-many-environments
# case explicitly. `NEXT_PUBLIC_SITE_URL` reaches `metadataBase` (og/twitter images,
# canonical URLs), the `Sitemap:` line in robots.txt and every `<loc>` in sitemap.xml.
# Measured on a build with it unset: robots.txt and sitemap.xml both carried the
# source fallback.
#
# Required rather than defaulted, and the mechanism is worth stating exactly because
# it differs per surface: an unset ARG is the empty string, and `?? fallback` does not
# fire on empty. robots.txt and sitemap.xml would then emit an empty base; `new URL("")`
# throws, so `metadataBase` takes the whole render down instead. Neither is a good
# outcome, and the guard converts both into a named failure on this stage's first
# instruction rather than an opaque one from inside Next.
# ---------------------------------------------------------------------------
FROM deps AS build
ARG NEXT_PUBLIC_SITE_URL
RUN test -n "$NEXT_PUBLIC_SITE_URL" || { \
      echo "NEXT_PUBLIC_SITE_URL build-arg is required: it is inlined at build time and" >&2; \
      echo "cannot be set at runtime. Rebuild with --build-arg NEXT_PUBLIC_SITE_URL=https://<host>" >&2; \
      exit 1; \
    }
ENV NEXT_PUBLIC_SITE_URL=$NEXT_PUBLIC_SITE_URL
COPY . .
ENV NEXT_TELEMETRY_DISABLED=1
# next/font downloads Source Sans 3 and JetBrains Mono at build time and self-hosts
# them, so the build host needs egress to fonts.googleapis.com and fonts.gstatic.com.
# The production branch throws on a failed fetch rather than falling back to system
# fonts, but passes no timeout (`fetch-resource.js`: the timeout is dev-only). So the
# two failure shapes differ: a refused connection or a DNS failure fails fast, while a
# BLACKHOLED network hangs here with no deadline.
RUN pnpm build

# ---------------------------------------------------------------------------
# Stage 3: runtime
# `output: "standalone"` traces the runtime dependency set into .next/standalone,
# so no node_modules is copied. Static assets and public/ are not traced and are
# copied explicitly (Next.js docs).
# ---------------------------------------------------------------------------
FROM node:22-alpine AS runtime
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
WORKDIR /app

COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static
COPY --from=build --chown=node:node /app/public ./public

# npm is removed from the runtime image, and it is a vulnerability fix rather than a size
# one: every HIGH advisory the blocking scanner found in this image lived in npm's own
# bundled dependencies under /usr/local/lib/node_modules/npm. A standalone Next server
# runs `node server.js` and needs no package manager at runtime.
RUN rm -rf /usr/local/lib/node_modules/npm /usr/local/bin/npm /usr/local/bin/npx \
    && rm -rf /opt/yarn* /usr/local/bin/yarn /usr/local/bin/yarnpkg /usr/local/bin/corepack

# The base layer's own OS packages need patching for the same reason caddy's do: a
# published tag is not rebuilt when alpine ships a fix, so the image arrives carrying
# advisories that already have an upstream repair. Measured 2026-08-27 on node:22-alpine:
# libcrypto3 3.5.7-r0 installed, 3.5.8-r0 available (CVE-2026-14456, HIGH). Regenerate
# with `docker run --rm node:22-alpine apk policy libcrypto3`.
#
# ADR 0065 Amendment 2026-07-28 Beslut 1: repair outranks acceptance, and an advisory
# may be accepted only when no dependency-level fix EXISTS. One does, so this is the
# required disposal rather than a trivy ignore entry.
RUN apk --no-cache upgrade

USER node
EXPOSE 3000

CMD ["node", "server.js"]
