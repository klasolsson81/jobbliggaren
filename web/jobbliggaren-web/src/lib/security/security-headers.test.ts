import { describe, it, expect } from "vitest";
import {
  PERMISSIONS_POLICY,
  buildContentSecurityPolicy,
  buildSecurityHeaders,
} from "./security-headers";

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

describe("buildContentSecurityPolicy — production", () => {
  const csp = buildContentSecurityPolicy(false);
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

  it("allows framework inline scripts/styles but NOT eval in prod", () => {
    expect(d["script-src"]).toBe("'self' 'unsafe-inline'");
    expect(d["script-src"]).not.toContain("'unsafe-eval'");
    expect(d["style-src"]).toBe("'self' 'unsafe-inline'");
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
  const dev = buildContentSecurityPolicy(true);
  const prod = buildContentSecurityPolicy(false);

  it("adds 'unsafe-eval' for the React dev error overlay", () => {
    expect(parseCsp(dev)["script-src"]).toContain("'unsafe-eval'");
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

  it("serves exactly the five security headers", () => {
    expect(headers.map((h) => h.key)).toEqual([
      "Content-Security-Policy",
      "X-Frame-Options",
      "X-Content-Type-Options",
      "Referrer-Policy",
      "Permissions-Policy",
    ]);
  });

  it("sets the four non-CSP headers to their hardened values", () => {
    expect(byKey["X-Frame-Options"]).toBe("DENY");
    expect(byKey["X-Content-Type-Options"]).toBe("nosniff");
    expect(byKey["Referrer-Policy"]).toBe("strict-origin-when-cross-origin");
    expect(byKey["Permissions-Policy"]).toBe(PERMISSIONS_POLICY);
  });

  it("carries the production CSP verbatim", () => {
    expect(byKey["Content-Security-Policy"]).toBe(
      buildContentSecurityPolicy(false)
    );
  });

  it("carries the development CSP verbatim on the dev branch", () => {
    const devByKey = Object.fromEntries(
      buildSecurityHeaders(true).map((h) => [h.key, h.value])
    );
    expect(devByKey["Content-Security-Policy"]).toBe(
      buildContentSecurityPolicy(true)
    );
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
