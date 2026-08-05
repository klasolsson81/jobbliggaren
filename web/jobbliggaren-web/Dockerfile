# Jobbliggaren web (Next.js) Dockerfile — multi-stage, non-root, standalone output.
# Build context: THIS directory (`docker build -f web/jobbliggaren-web/Dockerfile
# web/jobbliggaren-web`), not the repo root — the root .dockerignore excludes `web/`,
# and this app carries its own pnpm-lock.yaml.

# ---------------------------------------------------------------------------
# Stage 1: dependencies
# ---------------------------------------------------------------------------
FROM node:24-alpine AS deps
RUN corepack enable
WORKDIR /app
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

# ---------------------------------------------------------------------------
# Stage 2: build
# `next build` runs here and never on the box — ADR 0050 Amendment 2026-08-04 §2,
# capacity condition 1 (8 GB cannot host a Next build alongside the stack).
# ---------------------------------------------------------------------------
FROM deps AS build
COPY . .
ENV NEXT_TELEMETRY_DISABLED=1
RUN pnpm build

# ---------------------------------------------------------------------------
# Stage 3: runtime
# `output: "standalone"` traces the runtime dependency set into .next/standalone,
# so no node_modules is copied. Static assets and public/ are not traced and are
# copied explicitly (Next.js docs).
# ---------------------------------------------------------------------------
FROM node:24-alpine AS runtime
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    HOSTNAME=0.0.0.0
WORKDIR /app

COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static
COPY --from=build --chown=node:node /app/public ./public

USER node
EXPOSE 3000

CMD ["node", "server.js"]
