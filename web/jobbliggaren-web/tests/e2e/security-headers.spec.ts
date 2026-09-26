import { test, expect, type Page } from "@playwright/test";

// Guards that next.config.ts actually WIRES the security headers onto responses
// (issue #591 / epic #485) — a distinct invariant from the security-headers.ts
// unit contract, which only proves the header set is built correctly. A future
// edit could keep the module correct yet drop the headers() wiring; this catches
// that. No backend/auth needed — both routes are public.
//
// Mode-agnostic by construction: CI serves a production build and local runs serve
// `pnpm dev` (playwright.config.ts webServer), so the shared assertions cover only the
// branch-invariant directives. The dev/prod difference is then asserted as an
// INVARIANT rather than assumed (#813): a policy carrying the dev relaxations must not
// also carry the production-only `upgrade-insecure-requests`, and vice versa. That
// catches a mode mixup — e.g. a prod deploy that accidentally ships the dev CSP —
// which a purely branch-invariant spec would wave through.

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

    // Mode consistency (#813): the two CSP branches are mutually exclusive. Whichever
    // build served this response, its relaxations and its hardening must belong to the
    // SAME branch — directive leakage across the branches (say `'unsafe-eval'`
    // unconditionally, or `ws:` without the isDev gate) fails here.
    const isDevPolicy = csp!.includes("'unsafe-eval'");

    // Anchor the mode where it is actually KNOWN. The consistency check above is
    // self-referential — it validates whatever policy was served against itself, so a
    // production build that accidentally shipped the whole dev CSP would still be
    // internally consistent and pass. CI serves `pnpm start` (a production build), so
    // there the expected branch is not a guess: assert it. That is what turns this from
    // "the policy is coherent" into "the policy is the RIGHT one for this build".
    if (process.env.CI) {
      expect(isDevPolicy, "CI serves a production build — the CSP must be the prod branch")
        .toBe(false);
    }
    if (isDevPolicy) {
      expect(csp, "dev CSP appends the HMR websocket").toContain("connect-src 'self' ws:");
      expect(csp, "dev CSP must NOT carry upgrade-insecure-requests").not.toContain(
        "upgrade-insecure-requests"
      );
    } else {
      expect(csp, "prod CSP upgrades insecure requests").toContain(
        "upgrade-insecure-requests"
      );
      expect(csp, "prod CSP must NOT allow ws:").not.toContain("ws:");
    }
  });
}

// #1738 — `/logga-in/lank` carries a single-use login token in its URL, and is the one route with
// headers of its own (`LOGIN_LINK_ROUTE_HEADERS`). The unit contract proves the entry is built and
// ordered; only a served response proves it WINS over the global `/(.*)` block, and "a route rule
// that does not win is a rule that does not exist" (ADR 0142). Measured on the GET and on the form
// POST, which is what actually consumes the token.
test.describe("/logga-in/lank overrides the referrer policy and forbids caching", () => {
  const PATH = "/logga-in/lank?token=not-a-live-token";

  function expectLinkRouteHeaders(headers: Record<string, string>, which: string): void {
    // Exact, not `toContain`: if Next emitted both rules a browser would see a comma-joined
    // value, and a presence check would pass on it.
    expect(headers["referrer-policy"], `${which}: exactly one referrer policy`).toBe("same-origin");
    expect(headers["cache-control"], `${which}: never cached`).toContain("no-store");
    // The rest of the global set still applies: the route entry adds to it, it does not replace it.
    expect(headers["x-frame-options"], `${which}: global set intact`).toBe("DENY");
    expect(headers["content-security-policy"], `${which}: CSP intact`).toContain("form-action 'self'");
  }

  test("on the GET", async ({ page }) => {
    expectLinkRouteHeaders(await headersFor(page, PATH), "GET");
  });

  test("on the form POST", async ({ page }) => {
    await page.goto(PATH);
    const [response] = await Promise.all([
      page.waitForResponse((res) => res.request().method() === "POST" && res.url().includes("/logga-in/lank")),
      page.getByRole("button", { name: "Logga in" }).click(),
    ]);

    expectLinkRouteHeaders(response.headers(), "POST");
  });
});
