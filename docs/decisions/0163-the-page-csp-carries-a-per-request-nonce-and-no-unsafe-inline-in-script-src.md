# ADR 0163 — The page CSP carries a per-request nonce and no `'unsafe-inline'` in `script-src`

**Date:** 2026-10-11
**Status:** Accepted
**Decider:** Klas Olsson (directive 2026-10-11: a nonce-based CSP so `'unsafe-inline'` leaves `script-src`; goal A+ on Mozilla Observatory
and securityheaders.com). Three further answers in the same session: HSTS is **not** touched (ADR 0050 stands); `/api/*` keeps a static
nonce-less policy; the `blob:` question is answered by measurement (D5), with a defence-in-depth change to `cv-preview.tsx` (D6).
**Supersedes:** the "Approach A" bind of `senior-cto-advisor` 2026-07-04 (issue [#591](https://github.com/klasolsson81/jobbliggaren/issues/591),
epic #485), which kept `'unsafe-inline'` in `script-src` to avoid forcing dynamic rendering.
**Related:** [#2126](https://github.com/klasolsson81/jobbliggaren/issues/2126) (this change) · ADR 0050 (Amendment 2026-10-06: the HSTS value is
host-only, no `includeSubDomains`, no `preload`; Amendment 2026-10-07: one external HSTS field, M-5a) · ADR 0101 (Amendment 2026-09-06/07)
and DPIA #659 §11 (the CV-preview `blob:` iframe and its lapse triggers) · ADR 0045 (performance budgets) · ADR 0154 (every merge is a deploy)
**Measured against:** `origin/main` at `beac82b17`; production, anonymously and read-only, 2026-10-11; Chromium (Playwright `chromium-1243`) on a
local page and on a local production build.

---

## Context

The page policy was a static string built by `security-headers.ts` and served by `next.config.ts` `headers()`:
`script-src 'self' 'unsafe-inline'`. It was **not** in Caddy; the Caddyfile has never carried a CSP. Next's inline bootstrap and RSC payload
scripts needed `'unsafe-inline'` because a static header has no per-request value to authorise them with.

Measured 2026-10-11:

- **Production is open.** `SITE_ADMISSION=open` since Switch 2 (2026-10-07; read on its `.env` key and in runtime at 2026-10-07T16:35:18Z,
  `docs/current-work.md`). An anonymous `HEAD https://jobbliggaren.se/` answers 200 with the app's CSP, one `Strict-Transport-Security`, and
  `Via: 1.1 Caddy`; `/logga-in` and `/api/health` answer 200; `dev.` answers 302 to the apex. External scanners therefore see the application's own
  headers. *(The first draft of this change's plan assumed the gate was still up and that a scan would read Caddy's 401. That assumption was wrong and is
  withdrawn.)*
- **No page route changes rendering mode.** `next build` on `beac82b17` and on this change print byte-identical route tables: 109 `ƒ` (dynamic)
  and 3 `○` (`/icon.svg`, `/robots.txt`, `/sitemap.xml`). Every page was already dynamic because the root layout reads the `NEXT_LOCALE` cookie
  (`getLocale()`); `cacheComponents` is explicitly `false`. The cost the 2026-07-04 bind priced — forcing dynamic rendering — was nil by the time it
  came up for decision again.
- **The `blob:` document, measured.** A `blob:` document inherits its creator's origin and its CSP. On a page served with the old policy, an inline
  script inside an `text/html` blob **ran**. Under the nonce policy it was **blocked** (`script-src-elem`, reported from the `blob:` frame). A
  `application/pdf` blob framed the same way loaded the browser's PDF viewer under both policies with **zero** violations.

## Decision

**D1. The page CSP is written by `proxy.ts`, per request.** A fresh nonce (128 random bits, base64, Web Crypto) goes into the policy and into an
`x-nonce` request header, on the request (so Next stamps its inline scripts while rendering) and on the response (so the browser enforces it).
`proxy()` wraps the routing logic, so no branch can forget it; a CSP or `x-nonce` the client sent is overwritten, never appended.

**D2. `script-src 'self' 'nonce-…' 'strict-dynamic'`**, with `'unsafe-eval'` added in development only. All other directives are unchanged, including
`style-src 'self' 'unsafe-inline'` (next/font, Radix and input-otp inject `<style>`; BUILD.md §3.1).

**D3. The static header set no longer carries a CSP.** `/api/*` is outside the proxy matcher and gets its own route entry with a static policy that
differs from the page policy in `script-src` only (`'self'`). One CSP header per response, either way.

**D4. The matcher** excludes Next's static assets and `/api/` only. A first version also skipped image files by extension; it was withdrawn on
review (#2128, security-auditor and code-reviewer): a dynamic route and the not-found page answer to any suffix, and `GET /finns-inte.png` or
`/jobb/abc.png` rendered HTML with no CSP at all. Files under `public/` therefore pass through the proxy and carry the policy too. Next's `missing:`
prefetch filter is deliberately not adopted either: the refresh driver has its own prefetch rule, and skipping the proxy for a prefetch would skip the
auth gate. Pinned with Next's own matcher compiler (`unstable_doesMiddlewareMatch`) and with a served-response e2e on `/finns-inte.png`.

**D5. The `blob:` document is an argument for the nonce, not an obstacle.** The 2026-07-04 bind predates the CV preview; the 2026-09-07 re-pricing
(security-auditor) named `'unsafe-inline'` as the amplifier that would turn active content in a `blob:` document into XSS if lapse trigger 1 or 2 ever
fired. The nonce removes that amplifier for inline script: uploaded bytes cannot know a nonce minted for another request. No separate CSP for that route
is needed. **Not measured:** Firefox's and Safari's PDF viewers, and the real preview with a real upload (the signed-in e2e spec covers it and is run by
the stack-owner session).

**D6. `cv-preview.tsx` fixes the blob's type.** Before: `await res.blob()`, whose type is the response header's value verbatim (including any
parameter). After: `new Blob([await res.arrayBuffer()], { type: contentType })`, where `contentType` is the already-allowlisted key
(`application/pdf` or the docx type). A response typed `text/html` was already refused before any blob existed (`KIND_BY_CONTENT_TYPE.get` → error); this
makes the blob's type independent of the header even if that allowlist were widened. Pinned in `cv-preview.test.tsx`; the mock deliberately has no
`blob()`, so a revert fails the test.

**D7. Caddy** (`deploy/caddy/Caddyfile`): `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Resource-Policy: same-origin` as a site-level
snippet (`isolation`) like `hsts`, with the upstream's copies stripped by `header_down` so each appears once (Next sets COOP itself on the cookie-bounce
document, #2094); and `Via` removed. **`header_down -Via` does not remove it** — Caddy adds `Via` after the response-header operations (measured on 2.11.4):
the working form is the site-level `header -Via`. `encode zstd gzip` was already present. `edge-modes.test.sh` pins one COOP, one CORP and no `Via` on the
proxied 200, the gate's 401 and the ACME 404 (77/77 checks pass on a Caddy 2.11.4 image carrying this Caddyfile).

**D8. HSTS is unchanged.** `includeSubDomains` and `preload` stay out (ADR 0050 Amendment 2026-10-06: HTTPS is not valid for every name under the
domain, measured `autoconfig.jobbliggaren.se` presenting `CN=*.strato.de`). Neither is needed for the scanners' top grade.

## Consequences

- Every page response now has a unique body; none was cacheable before either (`Cache-Control: private, no-cache, no-store`).
- A third-party script, a `next/script` tag or any new inline script must take the nonce (`headers().get("x-nonce")`; `ThemeScript` already does) or be
  loaded by an already-trusted script (`strict-dynamic`). `ThemeScript` is dormant (dark mode is off) and is covered by the prop, not by a test that renders it.
- `'self'` is ignored by CSP3 browsers once `strict-dynamic` is present; it stays as the fallback for older ones.
- **A+ is not claimed.** The change was measured on header content and behaviour, not by running either scanner; Klas runs both after the release.
- `next dev` rewrites `Cache-Control` on every response, so the two `/logga-in/lank` cache assertions in `security-headers.spec.ts` fail against `pnpm dev`
  (by Next's own source, `base-server.js`; not re-run on `main`). They pass against the production build, which is what CI serves.

## Lapse triggers (when this should be reopened)

1. A route is made static (PPR, `cacheComponents: true`, `export const dynamic = "force-static"` on a page): its inline scripts get no nonce and the page
   breaks. The route-table comparison in the PR is the instrument.
2. A document-returning route handler appears under `/api/` that needs inline script: the static `/api/*` policy forbids it.
3. The proxy matcher stops matching a page route.
4. Any widening of `script-src`, or of what `cv-preview.tsx` accepts into a `blob:` (DPIA #659 §11 lapse triggers 1 and 2).

## Amendment 2026-10-11 — CORP is `cross-origin` on the image and manifest routes

**Bug.** D7 put `Cross-Origin-Resource-Policy: same-origin` on every response, including the share-card image. Preview tools that render the image in the
visitor's browser (opengraphpreview.com) showed only the alt text. **Measured 2026-10-11, anonymously:** `curl -I` on `/opengraph-image` (with and
without a query), `/twitter-image`, `/icon.svg`, `/apple-icon`, `/favicon.ico` and `/manifest.webmanifest` answered 200 with the right content type and
`Cross-Origin-Resource-Policy: same-origin`; the CSP on them is the page policy and no `Cross-Origin-Embedder-Policy` is sent. The image is a valid
33 577-byte PNG, `og:image` is an absolute `https://jobbliggaren.se/opengraph-image?<hash>`, and a request with a `LinkedInBot` user agent gets the same
200. An `Image()` created on `https://example.com` and pointed at each of the five image routes failed on all five. Next writes no CORP of its own, so
the edge is the only writer.

**Correction to the earlier comment.** The Caddyfile said CORP "does not stop a social crawler". That holds for a crawler fetching server-side (LinkedIn,
Facebook, X, Slack), which does not enforce CORP. It does not hold for a browser rendering the image for another origin, which is the case that broke.

**Decision.** `isolation` (Caddyfile) picks the value with one `map` on `{path}` and writes it with one `header`: `cross-origin` for
`/opengraph-image`, `/twitter-image`, `/apple-icon`, `/icon.svg`, `/favicon.ico` and
`/manifest.webmanifest`; `same-origin` for everything else, `robots.txt` and `sitemap.xml` included (crawlers fetch them, no page embeds them). The
upstream's field is still dropped by `header_down`, so each response carries one. **A second form was measured and rejected:** a default
`header ?Cross-Origin-Resource-Policy` plus an exception left Caddy's own 401 and 503 with no CORP field at all (17 failing checks in
`edge-modes.test.sh` on 2.11.4).

**What stays.** COOP is `same-origin` everywhere. The CSP and the proxy matcher are untouched: the image routes pass the proxy unauthenticated (200,
no redirect; `proxy.test.ts` and `security-headers.spec.ts` pin it), and a CSP on an image response restricts nothing about who may embed it.

**Pinned.** `edge-modes.test.sh` requires one `cross-origin` CORP on the seven image and manifest paths and one
`same-origin` CORP on fourteen look-alikes and ordinary pages; the upstream stub sends `same-origin` on every path, so a kept copy fails it (114/114 on a
Caddy 2.11.4 image carrying this Caddyfile).

**Not measured here.** The rendering in opengraphpreview.com and LinkedIn Post Inspector after the release; Klas checks both.

**Lapse trigger 5.** A new metadata route meant to be shown on another site (a per-page image route, a different file name, a `generateImageMetadata`
route with an id segment) that the `map` pattern does not match.

**Lapse trigger 5 fired, 2026-10-11 ([#2146](https://github.com/klasolsson81/jobbliggaren/pull/2146)).** The manifest now names three PNGs for Android
installs under `/icons/` (`icon-192`, `icon-512`, `icon-maskable-512`). They are brand images of the same class as the five already `cross-origin`, so the
`map` lists them by exact name (`icons/icon-(192|512|maskable-512)\.png`, no character class: every cross-origin path stays an explicit, pinned entry),
and `edge-modes.test.sh` and `proxy.test.ts` carry a row for each. A new size or file name fires the trigger again.

**Lapse trigger 5 fired, 2026-10-25 ([#2232](https://github.com/klasolsson81/jobbliggaren/pull/2232), #2214).** The public job page names a square
brand image at `/delningsbild` as its link preview (ADR 0165 Amendment 2026-10-25). It is a static brand image of the same class and reads nothing, so
the `map` lists it by exact name. `edge-modes.test.sh` carries it as `cross-origin`, with and without a query, and five look-alikes as `same-origin`
(`/delningsbildx`, `/xdelningsbild`, `/delningsbild/`, `/delningsbild/x`, `/jobb/123/delningsbild`): 129/129 on an edge image built from this
Caddyfile, 2026-10-25. `proxy.test.ts` and `security-headers.spec.ts` carry a row each. `security-auditor`'s plan read of the same day raised no
objection to `cross-origin` on it.
