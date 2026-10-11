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
    // script-src is the nonce + strict-dynamic form and NEVER carries 'unsafe-inline' (ADR 0163);
    // dev appends 'unsafe-eval'.
    expect(csp).toMatch(/script-src 'self' 'nonce-[A-Za-z0-9+/]{22}==' 'strict-dynamic'( 'unsafe-eval')?;/);
    expect(csp!.match(/script-src[^;]*/)![0]).not.toContain("unsafe-inline");
    // Exactly ONE policy: Playwright joins a repeated field with ", ", which a CSP never contains.
    // A second CSP (say, a static one left in next.config.ts) would be enforced alongside the nonce one.
    expect(csp).not.toContain(",");
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

// ADR 0163 — the nonce is per request, and Next must actually stamp it on its own inline scripts. A
// policy whose nonce no script carries would pass every header assertion above and blank the site.
test("each request gets its own nonce", async ({ request }) => {
  const nonceOf = async () => {
    const csp = (await request.get("/logga-in")).headers()["content-security-policy"];
    return /'nonce-([^']+)'/.exec(csp ?? "")?.[1];
  };
  const [a, b] = [await nonceOf(), await nonceOf()];
  expect(a).toBeTruthy();
  expect(b).toBeTruthy();
  expect(a).not.toBe(b);
});

for (const path of ["/", "/logga-in", "/vanliga-fragor"]) {
  test(`every inline script on ${path} carries the request's nonce`, async ({ page }) => {
    const h = await headersFor(page, path);
    const nonce = /'nonce-([^']+)'/.exec(h["content-security-policy"] ?? "")?.[1];
    expect(nonce, "the response CSP names a nonce").toBeTruthy();

    const inline = await page.evaluate(() =>
      // JSON-LD is a data block, not executable script: CSP does not apply to it.
      [...document.scripts]
        // Empty ones are `next dev` plumbing appended at runtime, not script that executes anything.
        .filter((s) => !s.src && s.type !== "application/ld+json" && s.textContent.trim() !== "")
        .map((s) => s.nonce)
    );
    // Next streams its RSC payload and bootstrap as inline scripts; zero would mean the nonce was
    // never applied or the page is not server-rendered at all.
    expect(inline.length, "the document has inline scripts").toBeGreaterThan(0);
    for (const n of inline) expect(n, "inline script nonce").toBe(nonce);
  });
}

// The matcher must not skip a page route because of its suffix: these paths render HTML (the not-found
// page, and a dynamic route whose layout redirects), and the HTML must carry the policy (#2128).
for (const path of ["/finns-inte.png", "/finns-inte.svg", `/jobb/abc.png`]) {
  test(`${path} carries exactly one nonce CSP`, async ({ request }) => {
    const res = await request.get(path, { maxRedirects: 0 });
    const csp = res.headers()["content-security-policy"] ?? "";

    expect(csp).toMatch(/script-src 'self' 'nonce-[A-Za-z0-9+/]{22}==' 'strict-dynamic'/);
    expect(csp).not.toContain(",");
  });
}

test("/api/* is outside the proxy and keeps the static policy: self-only scripts, no nonce, one header", async ({
  request,
}) => {
  const csp = (await request.get("/api/health")).headers()["content-security-policy"] ?? "";

  expect(csp).toBeTruthy();
  expect(csp).toMatch(/script-src 'self';/);
  expect(csp).not.toContain("nonce-");
  expect(csp.match(/script-src[^;]*/)![0]).not.toContain("unsafe-inline");
  expect(csp).not.toContain(",");
  expect(csp).toContain("frame-ancestors 'none'");
});

// #2094 — the proxy's Strict-cookie bounce sets its own headers on a Response it builds. A served response
// proves they win over the global `/(.*)` block and that the global set still applies (security-auditor,
// #2094 form round, M-1). Requested through the `request` fixture with the Fetch Metadata of a click in webmail.
test("the Strict-cookie bounce carries its own headers, and the global set still applies", async ({ request }) => {
  const res = await request.get("/matchningar", {
    maxRedirects: 0,
    headers: { "sec-fetch-site": "cross-site", "sec-fetch-mode": "navigate", "sec-fetch-dest": "document" },
  });
  const h = res.headers();

  expect(res.status()).toBe(200);
  // Exact: two rules would arrive comma-joined, and a presence check would pass on them.
  expect(h["referrer-policy"]).toBe("no-referrer");
  expect(h["cross-origin-opener-policy"]).toBe("same-origin");
  expect(h["cache-control"]).toContain("no-store");
  expect(h["set-cookie"]).toBeUndefined();
  expect(h["x-frame-options"]).toBe("DENY");
  expect(h["content-security-policy"]).toContain("frame-ancestors 'none'");
});

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

// #1744 — the OAuth callback's URL carries the provider's code and state, so its document sends no
// referrer, is stored nowhere and is never indexed. Measured on a refusal, which reaches no backend:
// without the state cookie nothing is forwarded.
test.describe("the OAuth callback sends no referrer and forbids caching and indexing", () => {
  test("on a refusal", async ({ page }) => {
    const h = await headersFor(page, "/api/auth/oauth/google/callback?code=not-a-code&state=not-a-state");

    // Exact: the route handler and next.config both set it, and a comma-joined value would pass a
    // presence check.
    expect(h["referrer-policy"]).toBe("no-referrer");
    expect(h["cache-control"]).toContain("no-store");
    expect(h["x-robots-tag"]).toBe("noindex");
    expect(h["x-frame-options"]).toBe("DENY");
  });
});
