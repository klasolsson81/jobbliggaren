# Jobbliggaren web (Next.js) Dockerfile — multi-stage, non-root, standalone output.
# Build context: THIS directory (`docker build -f web/jobbliggaren-web/Dockerfile
# web/jobbliggaren-web`), not the repo root — the root .dockerignore excludes `web/`,
# and this app carries its own pnpm-lock.yaml.
#
# BUILT BY CI, NEVER ON THE BOX. That is capacity condition 1 (ADR 0050 Amendment
# 2026-08-04 §2) and it is a requirement on whoever builds this file, not a property
# of the file: an 8 GB host cannot run a Next build alongside the stack. The builder
# is `.github/workflows/release-images.yml` and the deploy stack consumes the result
# by tag — `deploy/docker-compose.yml` declares no `build:` for any service, which is
# what keeps the requirement true rather than merely stated. Both land under #196.

# ---------------------------------------------------------------------------
# Stage 1: dependencies
#
# NODE AND PNPM MAJORS ARE PINNED TO WHAT CI RUNS, and neither pin is cosmetic.
# `pnpm-lock.yaml` is lockfileVersion 9.0 and `.github/workflows/build.yml` pins
# pnpm 9 for every job. There is no `packageManager` field anywhere in this repo, so
# a bare `corepack enable` would resolve whatever pnpm ships as the image default —
# and pnpm 11 reads NONE of the `pnpm` block in package.json (CLAUDE.md §9.2). That
# block is the vulnerability-suppression surface: seven `overrides`, the single
# accepted advisory, and `ignoredBuiltDependencies`. Dropping it would ship an image
# whose remediations were silently not applied while every gate still reported clean.
# Node follows the same rule for the weaker reason: no CI job runs a Node major other
# than 22, so a different one here builds what nothing tests.
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
# Measured on a build with it unset: all three carried the source fallback.
#
# Required rather than defaulted: an unset ARG is the empty string, and `?? fallback`
# does not fire on empty, so a defaulted build would emit an empty base URL instead of
# a wrong one. Failing here costs a build; the alternative ships silently.
# ---------------------------------------------------------------------------
FROM deps AS build
ARG NEXT_PUBLIC_SITE_URL
RUN test -n "$NEXT_PUBLIC_SITE_URL" || { \
      echo "NEXT_PUBLIC_SITE_URL build-arg is required (it is inlined and cannot be set at runtime)" >&2; \
      exit 1; \
    }
ENV NEXT_PUBLIC_SITE_URL=$NEXT_PUBLIC_SITE_URL
COPY . .
ENV NEXT_TELEMETRY_DISABLED=1
# next/font downloads Source Sans 3 and JetBrains Mono at build time and self-hosts
# them. The production branch throws on a failed fetch rather than falling back to
# system fonts, but it passes no timeout — so a build host without egress to Google
# Fonts hangs here rather than failing.
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

# NPM IS REMOVED FROM THE RUNTIME IMAGE, and it is a vulnerability fix rather than a size
# one. Every HIGH advisory the blocking scanner found in this image lived in npm's own
# bundled dependencies under /usr/local/lib/node_modules/npm — measured: brace-expansion,
# ip-address, picomatch, sigstore, tar, and nothing else. A standalone Next server runs
# `node server.js` and needs no package manager at runtime, so the whole surface goes.
RUN rm -rf /usr/local/lib/node_modules/npm /usr/local/bin/npm /usr/local/bin/npx \n    && rm -rf /opt/yarn* /usr/local/bin/yarn /usr/local/bin/yarnpkg /usr/local/bin/corepack

USER node
EXPOSE 3000

CMD ["node", "server.js"]
