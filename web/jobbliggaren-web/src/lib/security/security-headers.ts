// Browser security headers for the Jobbliggaren frontend — issue #591 (epic #485).
//
// Nonce-based CSP (ADR 0163, Klas-direktiv 2026-10-11) — supersedes "Approach A" (non-nonce,
// senior-cto-advisor 2026-07-04). The resource surface is verified same-origin-only: 0 external
// scripts, 0 analytics, 0 external image domains, no external fonts (next/font self-hosts
// Source Sans 3 + JetBrains Mono), and every client-side fetch hits a same-origin /api/* route
// handler (BACKEND_URL is a server-only env getter — the browser never connects to the backend
// origin). So default-src/connect-src stay 'self'.
//
// `script-src` carries a per-request nonce + 'strict-dynamic' and NO 'unsafe-inline'. The page
// policy is built by `proxy.ts` (it needs a fresh nonce per request, so it cannot live in
// next.config.ts `headers()`); Next reads the nonce back out of the request's CSP header and
// stamps it on its own inline bootstrap/RSC scripts. The cost the 2026-07-04 bind priced —
// forcing dynamic rendering — was measured 2026-10-11 to be nil: every page route was already
// `ƒ` (the root layout reads the NEXT_LOCALE cookie), only the metadata files are `○`.
//
// `style-src` keeps 'unsafe-inline': next/font and Radix inject <style> at runtime (BUILD.md
// §3.1, input-otp), and style injection is not script execution.
//
// The nonce is also what hardens the `blob:` document below. A `blob:` document inherits its
// creator's origin AND its CSP, so under the old 'unsafe-inline' a blob of type text/html would
// have run inline script in the app's origin; under a nonce policy it cannot (measured
// 2026-10-11, ADR 0163). The CV-preview blob type is additionally pinned in cv-preview.tsx.
//
// `/api/*` is outside the proxy matcher, so it keeps a STATIC policy (no nonce, no inline
// script): the OAuth callback answers with a small HTML document (inline <style> only) and the
// CV routes stream user bytes. Exactly one CSP header per response either way.
//
// frame-src 'self' blob: is MANDATORY: the CV-preview modal renders the user's own
// uploaded PDF via <iframe src={blobUrl}> (cv-preview.tsx, blobUrl =
// URL.createObjectURL), a blob: URL that would otherwise fall back to
// default-src 'self' and be blocked. DOCX is downloaded, never framed.
//
// The blob: indirection is not incidental, and it is the mechanism ADR 0101
// `Amendment 2026-09-06` / DPIA #659 §11 oblige that surface to name: framing the
// BFF route directly cannot work here, because frame-ancestors 'none' and
// X-Frame-Options: DENY below are served on `/(.*)` — route handlers included —
// and deny same-origin framing too. Loosening either is DPIA #659 §11's
// lapse-trigger 4, so this is the only branch available to that feature.
//
// This module is pure and framework-free so it is unit-testable and frozen by a
// co-located contract test; next.config.ts is the sole consumer.

/**
 * Permissions-Policy — deny powerful features the app does not use. Curated,
 * well-supported tokens only (deprecated tokens like `interest-cohort` are
 * omitted: they emit console warnings a civic utility should not produce).
 * `browsing-topics=()` opts out of the Topics API, matching the privacy-first,
 * no-tracking posture.
 */
export const PERMISSIONS_POLICY = [
  "camera=()",
  "microphone=()",
  "geolocation=()",
  "payment=()",
  "usb=()",
  "browsing-topics=()",
].join(", ");

/** The request header the proxy writes the nonce into, for Server Components that need it. */
export const NONCE_REQUEST_HEADER = "x-nonce";

/**
 * 128 random bits, base64 (a CSP `base64-value`). Per request, never reused: a nonce that repeats
 * is a constant an attacker can learn. Web Crypto only — the proxy and the tests share it.
 */
export function generateNonce(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return btoa(String.fromCharCode(...bytes));
}

function directives(isDev: boolean, scriptSrc: string): string {
  const list = [
    "default-src 'self'",
    `script-src ${scriptSrc}`,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",
    "font-src 'self'",
    `connect-src 'self'${isDev ? " ws:" : ""}`,
    "frame-src 'self' blob:",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
  ];

  if (!isDev) {
    list.push("upgrade-insecure-requests");
  }

  return list.join("; ");
}

/**
 * Builds the page Content-Security-Policy for one request (`proxy.ts` is the only caller).
 *
 * Dev relaxations NEVER weaken the production policy — they are additive to the
 * dev branch only: `'unsafe-eval'` (React's dev error overlay reconstructs
 * server stacks via eval) and `ws:` (HMR websocket). `upgrade-insecure-requests`
 * is production-only because dev serves http on localhost.
 */
export function buildContentSecurityPolicy(isDev: boolean, nonce: string): string {
  return directives(
    isDev,
    `'self' 'nonce-${nonce}' 'strict-dynamic'${isDev ? " 'unsafe-eval'" : ""}`
  );
}

/**
 * The static policy for `/api/*`, where the proxy does not run. No inline script, no nonce: a
 * route handler that answers with a document has none to run.
 */
export function buildApiContentSecurityPolicy(isDev: boolean): string {
  return directives(isDev, "'self'");
}

/**
 * Direct Next responses emit this host-only policy. Under Option B, Caddy
 * removes upstream HSTS and emits the single browser-visible field on both
 * proxied Next responses and its own responses (ADR 0050 §5, Amendment
 * 2026-10-07). Gate M-5a is measured externally on both response paths.
 *
 * Omitted on the dev branch: `next dev` serves http on localhost, and an HSTS
 * header there pins the browser to https for `max-age` against a host that has
 * no certificate.
 */
export const STRICT_TRANSPORT_SECURITY = "max-age=31536000";

export interface HttpHeader {
  readonly key: string;
  readonly value: string;
}

/**
 * The static browser-security header set served on every response (source `/(.*)` in
 * next.config.ts). It carries NO Content-Security-Policy: the page policy is per-request
 * (`proxy.ts`) and `/api/*` has its own entry (`API_ROUTE_HEADERS`) — a second CSP here would
 * be enforced alongside the nonce one. `X-Frame-Options: DENY` duplicates
 * `frame-ancestors 'none'` on purpose — belt-and-suspenders for legacy UAs that
 * predate CSP2 (OWASP Secure Headers).
 */
export function buildSecurityHeaders(isDev: boolean): readonly HttpHeader[] {
  return [
    { key: "X-Frame-Options", value: "DENY" },
    { key: "X-Content-Type-Options", value: "nosniff" },
    { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
    { key: "Permissions-Policy", value: PERMISSIONS_POLICY },
    ...(isDev
      ? []
      : [
          {
            key: "Strict-Transport-Security",
            value: STRICT_TRANSPORT_SECURITY,
          },
        ]),
  ];
}

/**
 * `/api/*` is excluded from the proxy matcher, so its CSP is the static one. Set as its own route
 * entry in next.config.ts, which the proxy never overlaps: one CSP per response.
 */
export const API_ROUTE = "/api/:path*";

export function buildApiRouteHeaders(isDev: boolean): readonly HttpHeader[] {
  return [
    { key: "Content-Security-Policy", value: buildApiContentSecurityPolicy(isDev) },
  ];
}

/**
 * `/logga-in/lank` carries a single-use login token in its query string (ADR 0142 "Page form").
 *
 * `no-store` on the GET and on the POST: a login URL must never be served out of a cache.
 *
 * `same-origin`, for a reason that is this page's alone: its form must work without JavaScript, and a no-JS form POST is a navigate-mode
 * request. Under `no-referrer` the Fetch standard serializes that request's `Origin` as `null`,
 * and Next refuses a Server Action whose `Origin` does not match the host (`action-handler.js`,
 * "Invalid Server Actions request"). `same-origin` still strips the referrer on every cross-origin request, and the CSP
 * admits no cross-origin subresource to begin with; the edge drops the whole request-header map
 * from its log (`CaddyfileTokenScrubbingPinTests`), so a same-origin `Referer` is not persisted
 * there either (security-auditor, #1738 M-1).
 *
 * One home for the value: the route entry in `next.config.ts` and the page's metadata both read
 * it, because a meta tag alone can stream after subresources have already been requested. That
 * the route entry WINS over the global `/(.*)` block is measured in
 * `tests/e2e/security-headers.spec.ts`: a route rule that does not win is a rule that does not
 * exist.
 */
export const LOGIN_LINK_ROUTE = "/logga-in/lank";
export const LOGIN_LINK_REFERRER_POLICY = "same-origin";

export const LOGIN_LINK_ROUTE_HEADERS: readonly HttpHeader[] = [
  { key: "Cache-Control", value: "no-store" },
  { key: "Referrer-Policy", value: LOGIN_LINK_REFERRER_POLICY },
];

/**
 * An external login's callback (#1744, ADR 0142 D8) carries the provider's single-use `code` and the
 * flow's `state` in its query. `no-referrer` here, where the link landing needs `same-origin`: the
 * callback's document is the one page it serves and has no form. Set as a route entry after the global
 * block rather than in the route handler, because the route entry's precedence is the one measured
 * (security-auditor m-1(c)); the document's first meta tag says the same thing.
 */
export const OAUTH_CALLBACK_ROUTE = "/api/auth/oauth/:provider/callback";

export const OAUTH_CALLBACK_ROUTE_HEADERS: readonly HttpHeader[] = [
  { key: "Cache-Control", value: "no-store" },
  { key: "Referrer-Policy", value: "no-referrer" },
  { key: "X-Robots-Tag", value: "noindex" },
];
