import { test, expect, type Page } from "@playwright/test";

// Guards that next.config.ts actually WIRES the security headers onto responses
// (issue #591 / epic #485) — a distinct invariant from the security-headers.ts
// unit contract, which only proves the header set is built correctly. A future
// edit could keep the module correct yet drop the headers() wiring; this catches
// that. No backend/auth needed — both routes are public.
//
// Runs against `pnpm dev` (see playwright.config.ts webServer), so it asserts the
// branch-invariant directives, not the production-only `upgrade-insecure-requests`
// (that + the exact prod policy are covered by the unit contract test and the
// production-build rendered-verify). Not yet CI-wired — rides #574.

async function headersFor(page: Page, path: string): Promise<Record<string, string>> {
  const res = await page.goto(path);
  expect(res, `navigation to ${path} returned a response`).not.toBeNull();
  return res!.headers();
}

// A static marketing route and a dynamic app-shell route — proves the `/(.*)`
// source applies across rendering modes.
for (const path of ["/", "/logga-in"]) {
  test(`serves the security header set on ${path}`, async ({ page }) => {
    const h = await headersFor(page, path);

    expect(h["x-frame-options"]).toBe("DENY");
    expect(h["x-content-type-options"]).toBe("nosniff");
    expect(h["referrer-policy"]).toBe("strict-origin-when-cross-origin");
    expect(h["permissions-policy"]).toContain("camera=()");

    // X-Powered-By fingerprint removed (poweredByHeader: false).
    expect(h["x-powered-by"]).toBeUndefined();

    const csp = h["content-security-policy"];
    expect(csp, "CSP header present").toBeTruthy();
    for (const directive of [
      "default-src 'self'",
      "style-src 'self' 'unsafe-inline'",
      "img-src 'self' data: blob:",
      "font-src 'self'",
      "frame-src 'self' blob:", // CV-preview blob iframe
      "object-src 'none'",
      "base-uri 'self'",
      "form-action 'self'",
      "frame-ancestors 'none'",
    ]) {
      expect(csp, `CSP contains: ${directive}`).toContain(directive);
    }
    // script-src carries 'unsafe-inline' (dev may append 'unsafe-eval').
    expect(csp).toMatch(/script-src 'self' 'unsafe-inline'/);
    // connect-src stays same-origin (dev appends ws: for HMR).
    expect(csp).toMatch(/connect-src 'self'/);
  });
}
