import { describe, it, expect } from "vitest";
import { SITE_URL } from "@/lib/site-url";
import robots from "./robots";
import sitemap from "./sitemap";
import { PROTECTED_PREFIXES } from "@/lib/auth/protected-routes";

// SEO foundation (#264) — pins the /robots.txt + /sitemap.xml App Router metadata routes:
// the public marketing surface is indexable, the authed app + API + /gast demo are not, and
// every emitted URL is absolute (SITE_URL-prefixed).

describe("robots.ts", () => {
  const result = robots();
  const rulesRaw = result.rules;
  // Our robots() returns a single rules object (not an array) — narrow for TS (no `!`/any).
  if (Array.isArray(rulesRaw)) {
    throw new Error("robots() should return a single rules object");
  }
  const rules = rulesRaw;
  const disallow = ([] as string[]).concat(rules.disallow ?? []);

  it("allows crawling the root and points at the absolute sitemap", () => {
    expect(rules.userAgent).toBe("*");
    expect(rules.allow).toBe("/");
    expect(result.sitemap).toBe(`${SITE_URL}/sitemap.xml`);
  });

  it("disallows the API, the /gast demo sandbox, and the admin surface (subtree-scoped)", () => {
    expect(disallow).toContain("/api/");
    expect(disallow).toContain("/gast/");
    expect(disallow).toContain("/admin/");
  });

  it("disallows every authenticated app prefix at a segment boundary (#583: P$ exact + P/ subtree)", () => {
    // Authed (app) areas are single-sourced from PROTECTED_PREFIXES (frozen to the (app) route
    // group by protected-routes.test.ts — #513). Each prefix is emitted as BOTH the exact anchor
    // `P$` and the subtree `P/`, mirroring the middleware isProtectedPath predicate so a bare
    // `Disallow: /cv` can never shadow the public /cv-granskning again.
    for (const authed of PROTECTED_PREFIXES) {
      expect(disallow).toContain(`${authed}$`);
      expect(disallow).toContain(`${authed}/`);
    }
  });

  it("carries no authed (app) entry beyond the boundary-encoded PROTECTED_PREFIXES (frozen to the source of truth)", () => {
    // The (app)-slice of the disallow list = everything except the robots-local, non-(app) extras.
    const seoLocal = new Set(["/api/", "/gast/", "/admin/"]);
    const appSlice = disallow.filter((entry) => !seoLocal.has(entry)).sort();
    const expected = [...PROTECTED_PREFIXES]
      .flatMap((prefix) => [`${prefix}$`, `${prefix}/`])
      .sort();

    expect(appSlice).toEqual(expected);
  });

  it("does not shadow the public /cv-granskning while still blocking /cv and its subtree (#583)", () => {
    // Emulate a crawler's robots.txt path matching for our entries: `$` = end-anchor, else prefix.
    // (We emit no `*`; longest-match-wins is moot because `allow` is only the root `/`.)
    const isDisallowed = (path: string): boolean =>
      disallow.some((entry) =>
        entry.endsWith("$")
          ? path === entry.slice(0, -1)
          : path.startsWith(entry)
      );

    // The #583 regression: the public CV-review explainer must stay crawlable.
    expect(isDisallowed("/cv-granskning")).toBe(false);
    // The authed /cv and its subtree must stay blocked.
    expect(isDisallowed("/cv")).toBe(true);
    expect(isDisallowed("/cv/123")).toBe(true);
    // The public /matchning explainer is spared; the authed /matchningar is blocked.
    expect(isDisallowed("/matchning")).toBe(false);
    expect(isDisallowed("/matchningar")).toBe(true);
    // Other public marketing pages are unaffected; the demo subtree is blocked.
    expect(isDisallowed("/om")).toBe(false);
    expect(isDisallowed("/gast/jobb")).toBe(true);
  });
});

describe("sitemap.ts", () => {
  const entries = sitemap();
  const urls = entries.map((e) => e.url);

  it("lists exactly the canonical public marketing pages, all absolute", () => {
    expect(urls).toEqual([
      SITE_URL,
      `${SITE_URL}/om`,
      `${SITE_URL}/kontakt`,
      `${SITE_URL}/for-utvecklare`,
      `${SITE_URL}/integritet`,
      `${SITE_URL}/cookies`,
      `${SITE_URL}/villkor`,
      `${SITE_URL}/tillganglighet`,
      `${SITE_URL}/hjalpcenter`,
      `${SITE_URL}/tips`,
      `${SITE_URL}/vanliga-fragor`,
      `${SITE_URL}/matchning`,
      `${SITE_URL}/cv-granskning`,
    ]);
  });

  it("never lists an authed app route or the /gast demo", () => {
    for (const u of urls) {
      expect(u).not.toMatch(/\/(gast|oversikt|ansokningar|cv|installningar|matchningar|sokningar|sparade)(\/|$)/);
      // /jobb is the authed search; the public demo is /gast/jobb (already excluded above).
      expect(u).not.toBe(`${SITE_URL}/jobb`);
    }
  });

  it("gives the landing the highest priority + a tighter change frequency", () => {
    const landing = entries.find((e) => e.url === SITE_URL);
    expect(landing?.priority).toBe(1);
    expect(landing?.changeFrequency).toBe("weekly");
    for (const e of entries.filter((x) => x.url !== SITE_URL)) {
      expect(e.priority).toBe(0.7);
      expect(e.changeFrequency).toBe("monthly");
    }
  });
});
