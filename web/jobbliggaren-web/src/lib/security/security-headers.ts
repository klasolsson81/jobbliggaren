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
// frame-src 'self' blob: is MANDATORY: the CV-preview modal renders the fetched
// PDF via <iframe src={blobUrl}> (cv-preview.tsx, blobUrl = URL.createObjectURL),
// a blob: URL that would otherwise fall back to default-src 'self' and be blocked.
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
  ];
}
