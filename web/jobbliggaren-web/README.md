# jobbliggaren-web

The Next.js frontend for [Jobbliggaren](../../README.md). It renders every page and
is the only part of the system the browser talks to: Server Components and Server
Actions call the ASP.NET Core API over the internal network, and the API is never exposed
directly.

## Stack

Next.js 16 (App Router) · React 19 · TypeScript 6 in strict mode · Tailwind CSS 4 ·
shadcn/ui on Radix · next-intl (Swedish by default, English as a secondary locale) ·
React Hook Form with Zod ·
Vitest with Testing Library · Playwright. Exact versions are in [`package.json`](package.json).

## Running locally

The app needs a running API. Start the backend first, as described in the root
[Getting started](../../README.md#getting-started) section, then run:

```bash
pnpm install
BACKEND_URL=http://localhost:5049 pnpm dev   # http://localhost:3000
```

Use pnpm 9, the version CI and the Dockerfile use; the lockfile is in pnpm 9's format.
Use Node.js 22.

There is no `.env.example` and no `.env.local` is required. The app reads these
environment variables:

| Variable | Purpose |
|---|---|
| `BACKEND_URL` | Base URL of the API. Required: server code throws without it. |
| `NEXT_PUBLIC_SITE_URL` | Canonical site URL for `robots.txt` and the sitemap. Optional. |
| `APP_VERSION` | Release commit, stamped into feedback submissions. Optional. |
| `DEV_TOOLS_RESET_ENABLED` | Shows the data-reset tool on `/oversikt` in a production build; development always shows it. Optional. |
| `ADMIN_PREVIEW_ENABLED` | Build-time flag for the admin preview with fixture data. Never set for a deployed build. |

## Scripts

| Command | What it does |
|---|---|
| `pnpm dev` | Development server |
| `pnpm build` | Production build, then a check that the admin preview is absent unless its flag was set |
| `pnpm start` | Serve the production build |
| `pnpm lint` | ESLint, including the project's own rules |
| `pnpm exec tsc --noEmit` | Type check |
| `pnpm run guard:css` | Checks that the listed stylesheets use design tokens rather than raw colours and font sizes |
| `pnpm test` | Vitest, once. `pnpm test:watch` runs it in watch mode. |
| `pnpm test:e2e` | Playwright against `tests/e2e` at `PLAYWRIGHT_BASE_URL` (default http://localhost:3000); reuses a running server or starts `pnpm dev` |
| `pnpm visual-verify` | Screenshots for design review; see the [runbook](../../docs/runbooks/frontend-visual-verification.md) |

The other Playwright suites (`job-modal`, `admin`, `feedback` and so on) each have their
own `playwright.*.config.ts` and run against fixture servers. Run one with
`pnpm exec playwright test -c <config>`. The CI setup is in
[`docs/runbooks/e2e-ci.md`](../../docs/runbooks/e2e-ci.md).

## Layout

```text
src/
├── app/                 # routes, grouped by audience
│   ├── (app)/           # signed-in pages: /oversikt, /jobb, /ansokningar, /foretag, /cv, /mina-sidor …
│   ├── (auth)/          # sign-in: /logga-in and its code, link and terms steps
│   ├── (admin)/         # /admin
│   ├── (admin-preview)/ # admin preview with fixture data, built only behind its flag
│   ├── (guest)/         # /gast: a demo with sample data
│   ├── (marketing)/     # the landing page
│   ├── (marketing-inner)/ # information pages: privacy, terms, help, accessibility …
│   └── api/             # route handlers: OAuth, uploads, feedback, client-side reads
├── components/          # UI by feature, plus ui/ for the shadcn primitives
├── lib/                 # API clients, Server Actions, DTO schemas, auth, i18n helpers
├── i18n/                # next-intl configuration
└── proxy.ts             # request guard for protected routes and session refresh
messages/{sv,en}/        # UI copy, one JSON file per namespace
tests/                   # Playwright suites
```

Route names are Swedish, because they are part of the product; code identifiers are
English.

## Conventions

These are defined once, in the repository root:

- [`AGENTS.md`](../../AGENTS.md) §4 (TypeScript and Next.js), §5 (anti-patterns) and §10
  (Swedish UI copy).
- [`DESIGN.md`](../../DESIGN.md) for the design system and its tokens.
- [`AGENTS.md`](AGENTS.md) in this folder, for the build checks that apply to changes at
  the Server/Client Component boundary.

## Production

`next.config.ts` sets `output: "standalone"`. The [`Dockerfile`](Dockerfile) builds a
non-root image that runs as the `web` service behind Caddy, as defined in
[`deploy/docker-compose.yml`](../../deploy/docker-compose.yml).
