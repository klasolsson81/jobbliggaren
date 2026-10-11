import { describe, it, expect } from "vitest";
import {
  API_ROUTE,
  LOGIN_LINK_REFERRER_POLICY,
  LOGIN_LINK_ROUTE,
  LOGIN_LINK_ROUTE_HEADERS,
  OAUTH_CALLBACK_ROUTE,
  OAUTH_CALLBACK_ROUTE_HEADERS,
  NONCE_REQUEST_HEADER,
  PERMISSIONS_POLICY,
  STRICT_TRANSPORT_SECURITY,
  buildApiContentSecurityPolicy,
  buildApiRouteHeaders,
  buildContentSecurityPolicy,
  buildSecurityHeaders,
  generateNonce,
} from "./security-headers";

const NONCE = "dGVzdC1ub25jZS0xMjM0NQ==";

// Freezes the browser-security header contract (issue #591 / epic #485). CSP is
// invisible to jsdom at runtime, so this contract test + the rendered-verify
// sweep (production build, zero console CSP violations) are the two guards.

/** Parses a CSP string into a directive-name → source-list map. */
function parseCsp(csp: string): Record<string, string> {
  const map: Record<string, string> = {};
  for (const directive of csp.split(";")) {
    const trimmed = directive.trim();
    if (trimmed.length === 0) continue;
    const spaceIdx = trimmed.indexOf(" ");
    if (spaceIdx === -1) {
      map[trimmed] = ""; // valueless directive, e.g. upgrade-insecure-requests
    } else {
      map[trimmed.slice(0, spaceIdx)] = trimmed.slice(spaceIdx + 1).trim();
    }
  }
  return map;
}

describe("generateNonce", () => {
  it("is 128 random bits as base64, and differs on every call", () => {
    const nonces = new Set(Array.from({ length: 50 }, () => generateNonce()));
    expect(nonces.size).toBe(50);
    for (const n of nonces) {
      // 16 bytes -> 24 base64 chars, the last two being padding. A CSP `base64-value`.
      expect(n).toMatch(/^[A-Za-z0-9+/]{22}==$/);
    }
  });

  it("names the request header the root layout reads", () => {
    expect(NONCE_REQUEST_HEADER).toBe("x-nonce");
  });
});

describe("buildContentSecurityPolicy — production", () => {
  const csp = buildContentSecurityPolicy(false, NONCE);
  const d = parseCsp(csp);

  it("locks the same-origin baseline", () => {
    expect(d["default-src"]).toBe("'self'");
    expect(d["font-src"]).toBe("'self'");
    expect(d["base-uri"]).toBe("'self'");
    expect(d["form-action"]).toBe("'self'");
    expect(d["object-src"]).toBe("'none'");
  });

  it("keeps connect-src same-origin (all client fetches hit /api/* handlers)", () => {
    expect(d["connect-src"]).toBe("'self'");
    expect(csp).not.toContain("ws:");
  });

  it("script-src is the nonce + strict-dynamic form — no 'unsafe-inline', no eval (ADR 0163)", () => {
    expect(d["script-src"]).toBe(`'self' 'nonce-${NONCE}' 'strict-dynamic'`);
    expect(d["script-src"]).not.toContain("'unsafe-inline'");
    expect(d["script-src"]).not.toContain("'unsafe-eval'");
  });

  it("keeps 'unsafe-inline' for styles only (next/font and Radix inject <style>)", () => {
    expect(d["style-src"]).toBe("'self' 'unsafe-inline'");
  });

  it("puts the request's nonce in the policy, so two requests never share a policy", () => {
    expect(buildContentSecurityPolicy(false, "AAAA")).not.toBe(
      buildContentSecurityPolicy(false, "BBBB")
    );
  });

  it("allows the CV-preview iframe blob source (frame-src 'self' blob:)", () => {
    // Guards cv-preview.tsx: <iframe src={URL.createObjectURL(pdfBlob)}>.
    // Without blob: here the modal would fall back to default-src 'self' and
    // the preview would be blocked.
    expect(d["frame-src"]).toBe("'self' blob:");
  });

  it("allows self + data + blob images", () => {
    expect(d["img-src"]).toBe("'self' data: blob:");
  });

  it("forbids being framed (clickjacking)", () => {
    expect(d["frame-ancestors"]).toBe("'none'");
  });

  it("upgrades insecure requests in production", () => {
    expect(csp).toContain("upgrade-insecure-requests");
  });
});

describe("buildContentSecurityPolicy — development relaxations (additive only)", () => {
  const dev = buildContentSecurityPolicy(true, NONCE);
  const prod = buildContentSecurityPolicy(false, NONCE);

  it("adds 'unsafe-eval' for the React dev error overlay", () => {
    expect(parseCsp(dev)["script-src"]).toBe(
      `'self' 'nonce-${NONCE}' 'strict-dynamic' 'unsafe-eval'`
    );
  });

  it("still carries no 'unsafe-inline' in script-src on the dev branch", () => {
    expect(parseCsp(dev)["script-src"]).not.toContain("'unsafe-inline'");
  });

  it("adds ws: to connect-src for HMR", () => {
    expect(parseCsp(dev)["connect-src"]).toContain("ws:");
  });

  it("omits upgrade-insecure-requests in dev (localhost is http)", () => {
    expect(dev).not.toContain("upgrade-insecure-requests");
  });

  it("never removes a production restriction — dev only relaxes script/connect", () => {
    // Every non-relaxed directive is byte-identical to production.
    const devMap = parseCsp(dev);
    const prodMap = parseCsp(prod);
    for (const key of Object.keys(prodMap)) {
      if (key === "script-src" || key === "connect-src") continue;
      if (key === "upgrade-insecure-requests") continue;
      expect(devMap[key]).toBe(prodMap[key]);
    }
  });
});

describe("buildSecurityHeaders", () => {
  const headers = buildSecurityHeaders(false);
  const byKey = Object.fromEntries(headers.map((h) => [h.key, h.value]));

  it("serves exactly the five static security headers in production — and NO CSP", () => {
    // The page CSP is per-request (proxy.ts); a static one here would be enforced alongside it.
    expect(headers.map((h) => h.key)).toEqual([
      "X-Frame-Options",
      "X-Content-Type-Options",
      "Referrer-Policy",
      "Permissions-Policy",
      "Strict-Transport-Security",
    ]);
  });

  it("sets the four non-CSP headers to their hardened values", () => {
    expect(byKey["X-Frame-Options"]).toBe("DENY");
    expect(byKey["X-Content-Type-Options"]).toBe("nosniff");
    expect(byKey["Referrer-Policy"]).toBe("strict-origin-when-cross-origin");
    expect(byKey["Permissions-Policy"]).toBe(PERMISSIONS_POLICY);
  });

  it("carries no Content-Security-Policy on either branch", () => {
    for (const isDev of [false, true]) {
      expect(buildSecurityHeaders(isDev).map((h) => h.key)).not.toContain(
        "Content-Security-Policy"
      );
    }
  });

  it("emits HSTS in production, host-only, with the max-age ADR 0050 prescribes", () => {
    // Asserted against the literal rather than against the exported constant: a
    // test that compares the module to itself passes whatever the value drifts to,
    // and this value is half of a two-emitter contract.
    expect(byKey["Strict-Transport-Security"]).toBe(
      "max-age=31536000"
    );
    expect(STRICT_TRANSPORT_SECURITY).toBe(byKey["Strict-Transport-Security"]);
  });

  it("omits HSTS on the dev branch (localhost is http, no certificate)", () => {
    expect(buildSecurityHeaders(true).map((h) => h.key)).not.toContain(
      "Strict-Transport-Security"
    );
  });
});

describe("the /api/* policy (the proxy does not run there)", () => {
  it("is static: self-only scripts, no nonce, no inline script", () => {
    const d = parseCsp(buildApiContentSecurityPolicy(false));
    expect(d["script-src"]).toBe("'self'");
    expect(d["frame-ancestors"]).toBe("'none'");
    expect(d["object-src"]).toBe("'none'");
    expect(d["style-src"]).toBe("'self' 'unsafe-inline'"); // the OAuth callback document's <style>
    expect(buildApiContentSecurityPolicy(false)).toContain("upgrade-insecure-requests");
  });

  it("differs from the page policy in script-src ONLY", () => {
    const api = parseCsp(buildApiContentSecurityPolicy(false));
    const page = parseCsp(buildContentSecurityPolicy(false, NONCE));
    expect(Object.keys(api)).toEqual(Object.keys(page));
    for (const key of Object.keys(page)) {
      if (key === "script-src") continue;
      expect(api[key]).toBe(page[key]);
    }
  });

  it("is wired as one route entry carrying exactly one CSP header", () => {
    expect(API_ROUTE).toBe("/api/:path*");
    expect(buildApiRouteHeaders(false).map((h) => h.key)).toEqual(["Content-Security-Policy"]);
  });
});

describe("PERMISSIONS_POLICY", () => {
  it("denies the powerful features the app does not use", () => {
    for (const feature of [
      "camera",
      "microphone",
      "geolocation",
      "payment",
      "usb",
      "browsing-topics",
    ]) {
      expect(PERMISSIONS_POLICY).toContain(`${feature}=()`);
    }
  });

  it("avoids deprecated tokens that log console warnings", () => {
    expect(PERMISSIONS_POLICY).not.toContain("interest-cohort");
  });
});

// The one route with headers of its own (#1738). What these CANNOT show is that the route entry
// wins over the global one in a served response; `tests/e2e/security-headers.spec.ts` measures that.
describe("the login link route's headers", () => {
  it("names the route the mail's link lands on", () => {
    expect(LOGIN_LINK_ROUTE).toBe("/logga-in/lank");
  });

  it("forbids caching and keeps the referrer inside the origin", () => {
    expect(LOGIN_LINK_ROUTE_HEADERS).toEqual([
      { key: "Cache-Control", value: "no-store" },
      { key: "Referrer-Policy", value: "same-origin" },
    ]);
  });

  it("is never a policy that lets the token-bearing URL cross the origin", () => {
    // `same-origin` and `no-referrer` both hold that. `no-referrer` is refused for another
    // reason, recorded at the constant: it breaks the no-JS POST this page must support.
    expect(["same-origin"]).toContain(LOGIN_LINK_REFERRER_POLICY);
  });

  it("overrides ONLY the referrer policy of the global set, and adds no second CSP", () => {
    const globalKeys = buildSecurityHeaders(false).map((header) => header.key);
    const shared = LOGIN_LINK_ROUTE_HEADERS.map((header) => header.key).filter((key) =>
      globalKeys.includes(key)
    );

    expect(shared).toEqual(["Referrer-Policy"]);
  });
});

describe("the external login callback's headers", () => {
  it("names the route the provider sends the browser back to", () => {
    expect(OAUTH_CALLBACK_ROUTE).toBe("/api/auth/oauth/:provider/callback");
  });

  it("forbids caching, sends no referrer and keeps the page out of an index", () => {
    expect(OAUTH_CALLBACK_ROUTE_HEADERS).toEqual([
      { key: "Cache-Control", value: "no-store" },
      { key: "Referrer-Policy", value: "no-referrer" },
      { key: "X-Robots-Tag", value: "noindex" },
    ]);
  });

  it("overrides ONLY the referrer policy of the global set, and adds no second CSP", () => {
    const globalKeys = buildSecurityHeaders(false).map((header) => header.key);
    const shared = OAUTH_CALLBACK_ROUTE_HEADERS.map((header) => header.key).filter((key) =>
      globalKeys.includes(key)
    );

    expect(shared).toEqual(["Referrer-Policy"]);
  });
});
