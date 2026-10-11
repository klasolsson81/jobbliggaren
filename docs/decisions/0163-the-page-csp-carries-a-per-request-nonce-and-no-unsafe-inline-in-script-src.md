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

**D4. The matcher** additionally skips image files (`svg|png|jpg|jpeg|gif|webp|ico`). It deliberately does **not** adopt Next's `missing:` prefetch filter:
the refresh driver has its own prefetch rule and skipping the proxy for a prefetch would skip the auth gate.

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
