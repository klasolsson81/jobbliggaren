// Browser security headers for the Jobbliggaren frontend — issue #591 (epic #485).
//
// Approach A (non-nonce enforcing CSP) bound by senior-cto-advisor 2026-07-04.
// The resource surface is verified same-origin-only: 0 external scripts, 0
// analytics, 0 external image domains, no external fonts (next/font self-hosts
// Source Sans 3 + JetBrains Mono), and every client-side fetch hits a same-origin
// /api/* route handler (BACKEND_URL is a server-only env getter — the browser
// never connects to the backend origin). So default-src/connect-src stay 'self'.
//
// Next.js App Router emits inline bootstrap scripts and next/font injects inline
// <style>, so script-src/style-src carry 'unsafe-inline'. A nonce would force
// all pages to dynamic rendering (Next.js CSP doc) — regressing ADR 0045 on a
// no-CDN VPS and colliding with the middleware.ts hotspot — for a marginal XSS
// gain the boxed exfil channels (connect/img/form-action/base-uri) already deny.
//
// ⚠ That trade-off was priced before this origin could hold a blob: document built from
// user-uploaded bytes. It is not false, but it is weaker than when it was written, and
// security-auditor re-priced it 2026-09-07: a blob: document inherits its creator's origin AND
// its CSP, so 'unsafe-inline' above is the amplifier that turns "active content" into full XSS
// if lapse-trigger 1 or 2 ever fires (DPIA #659 §11/§12). Exfil is boxed but not closed —
// there is no navigate-to directive. The nonce cost against ADR 0045 stands, so this is a
// re-pricing and not yet a decision to change it.
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

/**
 * Builds the Content-Security-Policy header value.
 *
 * Dev relaxations NEVER weaken the production policy — they are additive to the
 * dev branch only: `'unsafe-eval'` (React's dev error overlay reconstructs
 * server stacks via eval) and `ws:` (HMR websocket). `upgrade-insecure-requests`
 * is production-only because dev serves http on localhost.
 */
export function buildContentSecurityPolicy(isDev: boolean): string {
  const directives = [
    "default-src 'self'",
    `script-src 'self' 'unsafe-inline'${isDev ? " 'unsafe-eval'" : ""}`,
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
    directives.push("upgrade-insecure-requests");
  }

  return directives.join("; ");
}

/**
 * HSTS — gate M-5a (ADR 0050 `Amendment 2026-08-04` §5), which requires the
 * header on BOTH response paths. This is the Next path, and it is the lesser
 * half: under Option B the K2 basic-auth 401 is answered at the edge and never
 * reaches Next, so this header is absent from the first response a browser sees.
 * The edge half is owed by the reverse proxy under #196 and nothing here can
 * stand in for it.
 *
 * The value is the one ADR 0050 §5 prescribes. Nothing enforces that the two
 * emitters agree — the gate is read off `curl -sI` against both paths at
 * cutover, never off either configuration.
 *
 * Omitted on the dev branch: `next dev` serves http on localhost, and an HSTS
 * header there pins the browser to https for `max-age` against a host that has
 * no certificate.
 */
export const STRICT_TRANSPORT_SECURITY = "max-age=31536000; includeSubDomains";

export interface HttpHeader {
  readonly key: string;
  readonly value: string;
}

/**
 * The full browser-security header set served on every response (source
 * `/(.*)` in next.config.ts). `X-Frame-Options: DENY` duplicates
 * `frame-ancestors 'none'` on purpose — belt-and-suspenders for legacy UAs that
 * predate CSP2 (OWASP Secure Headers).
 */
export function buildSecurityHeaders(isDev: boolean): readonly HttpHeader[] {
  return [
    { key: "Content-Security-Policy", value: buildContentSecurityPolicy(isDev) },
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
