import { describe, it, expect } from "vitest";
import { SITE_URL } from "@/lib/site-url";
import robots from "./robots";
import { marketingSitemapLocs } from "@/lib/seo/marketing-sitemap";
import { publicCatalogueUrl } from "@/lib/job-ads/public-catalogue-url";
import { urlsetXml } from "@/lib/seo/sitemap-xml";
import { PROTECTED_PREFIXES } from "@/lib/auth/protected-routes";

// SEO foundation (#264) — pins /robots.txt and the sitemap's marketing part (ADR 0174): the public
// marketing surface is indexable, the authed app + API + /gast demo are not, and every emitted URL is
// absolute (SITE_URL-prefixed).

/**
 * Google's matching, as robots.txt documents it (developers.google.com/search/docs/crawling-indexing/robots/
 * robots_txt): the longest matching path wins, `Allow` wins a tie, `*` is any run of characters and `$` ends the
 * URL. The path is matched with its query, so `/jobb?q=x` is `/jobb?q=x`.
 */
function patternToRegExp(pattern: string): RegExp {
  const anchored = pattern.endsWith("$");
  const body = anchored ? pattern.slice(0, -1) : pattern;
  const escaped = body.replace(/[.*+?^${}()|[\]\\]/g, "\\$&").replace(/\\\*/g, ".*");
  return new RegExp(`^${escaped}${anchored ? "$" : ""}`);
}

function isCrawlable(group: { allow: string[]; disallow: string[] }, pathAndQuery: string): boolean {
  const matches = (patterns: string[]) =>
    patterns.filter((pattern) => patternToRegExp(pattern).test(pathAndQuery)).map((pattern) => pattern.length);
  const longestAllow = Math.max(-1, ...matches(group.allow));
  const longestDisallow = Math.max(-1, ...matches(group.disallow));
  return longestAllow >= longestDisallow;
}

const JOB_ID = "0b0b0b0b-1111-4222-8333-444444444444";

describe("robots.ts", () => {
  const result = robots();
  const rulesRaw = result.rules;
  const rules = Array.isArray(rulesRaw) ? rulesRaw : [rulesRaw];
  const everyone = rules.find((r) => r.userAgent === "*");
  const training = rules.find((r) => Array.isArray(r.userAgent));
  if (everyone === undefined || training === undefined) {
    throw new Error("robots() should carry the * group and the training-crawler group");
  }
  const disallow = ([] as string[]).concat(everyone.disallow ?? []);
  const crawlable = (pathAndQuery: string) =>
    isCrawlable({ allow: ([] as string[]).concat(everyone.allow ?? []), disallow }, pathAndQuery);

  it("allows crawling the root and points at the absolute sitemap", () => {
    expect(everyone.allow).toBe("/");
    expect(result.sitemap).toBe(`${SITE_URL}/sitemap.xml`);
  });

  it("blocks the AI training crawlers in a group of their own, and names no one else (Klas 2026-10-10, #2177)", () => {
    expect(rules).toHaveLength(2);
    expect(training.userAgent).toEqual([
      "GPTBot",
      "CCBot",
      "ClaudeBot",
      "Applebot-Extended",
      "Amazonbot",
      "meta-externalagent",
    ]);
    expect(training.disallow).toBe("/");
    expect(training.allow).toBeUndefined();
  });

  it("lets Google-Extended and every search crawler in by not naming them (a crawler uses one group; * is not merged)", () => {
    const serialised = JSON.stringify(result.rules);
    expect(serialised).not.toMatch(
      /Google-Extended|Googlebot|Bingbot|OAI-SearchBot|ChatGPT-User|Claude-SearchBot|Claude-User|"Applebot"|Amzn-SearchBot|Amzn-User|Meta-WebIndexer|Meta-ExternalFetcher/i,
    );
  });

  it("disallows the API, the /gast demo sandbox, and the admin surface (subtree-scoped)", () => {
    expect(disallow).toContain("/api/");
    expect(disallow).toContain("/gast/");
    expect(disallow).toContain("/admin/");
  });

  it("disallows the bare /admin overview at a segment boundary (#1973), without shadowing a sibling", () => {
    expect(disallow).toContain("/admin$");
    expect(disallow).not.toContain("/admin");
  });

  it("disallows every authenticated app prefix at a segment boundary (#583: P$ exact + P/ subtree), /jobb apart", () => {
    // Authed (app) areas are single-sourced from PROTECTED_PREFIXES (frozen to the (app) route
    // group by protected-routes.test.ts — #513). Each prefix is emitted as BOTH the exact anchor
    // `P$` and the subtree `P/`, mirroring the middleware isProtectedPath predicate so a bare
    // `Disallow: /cv` can never shadow the public /cv-granskning again.
    for (const authed of PROTECTED_PREFIXES.filter((p) => p !== "/jobb")) {
      expect(disallow).toContain(`${authed}$`);
      expect(disallow).toContain(`${authed}/`);
    }
  });

  it("opens the public job page and keeps the private list closed: /jobb$ and /jobb?, never /jobb/ (ADR 0165)", () => {
    expect(disallow).toContain("/jobb$");
    expect(disallow).toContain("/jobb?");
    expect(disallow).not.toContain("/jobb/");
    expect(disallow).not.toContain("/jobb");
  });

  it("carries no authed (app) entry beyond the boundary-encoded PROTECTED_PREFIXES (frozen to the source of truth)", () => {
    // The (app)-slice of the disallow list = everything except the robots-local, non-(app) extras.
    const seoLocal = new Set(["/api/", "/gast/", "/admin$", "/admin/"]);
    const appSlice = disallow.filter((entry) => !seoLocal.has(entry)).sort();
    const expected = [...PROTECTED_PREFIXES]
      .flatMap((prefix) => (prefix === "/jobb" ? ["/jobb$", "/jobb?"] : [`${prefix}$`, `${prefix}/`]))
      .sort();

    expect(appSlice).toEqual(expected);
  });

  it("never lists the internal route prefix of the public job page: a blocked URL cannot show its 404 (ADR 0165)", () => {
    expect(JSON.stringify(result.rules)).not.toContain("publik");
  });

  it("does not shadow the public /cv-granskning while still blocking /cv and its subtree (#583)", () => {
    // The #583 regression: the public CV-review explainer must stay crawlable.
    expect(crawlable("/cv-granskning")).toBe(true);
    // The authed /cv and its subtree must stay blocked.
    expect(crawlable("/cv")).toBe(false);
    expect(crawlable("/cv/123")).toBe(false);
    // The public /matchning explainer is spared; the authed /matchningar is blocked.
    expect(crawlable("/matchning")).toBe(true);
    expect(crawlable("/matchningar")).toBe(false);
    // Other public marketing pages are unaffected; the demo subtree is blocked.
    expect(crawlable("/om")).toBe(true);
    expect(crawlable("/gast/jobb")).toBe(false);
  });

  it("crawls the public job page at /jobb/<uuid> with or without a query, and nothing else under /jobb", () => {
    expect(crawlable(`/jobb/${JOB_ID}`)).toBe(true);
    expect(crawlable(`/jobb/${JOB_ID}?vidare=1`)).toBe(true);
    expect(crawlable(`/jobb/${JOB_ID}?relaterade=on`)).toBe(true);

    expect(crawlable("/jobb")).toBe(false);
    expect(crawlable("/jobb?q=sjukskoterska")).toBe(false);
    expect(crawlable("/jobb?")).toBe(false);
  });

  it("keeps every signed-in surface closed", () => {
    for (const path of ["/oversikt", "/ansokningar/abc", "/foretag", "/mina-sidor", "/sparade", "/admin", "/admin/x", "/api/v1/job-ads"]) {
      expect(crawlable(path)).toBe(false);
    }
  });
});

describe("the sitemap's marketing part", () => {
  const urls = marketingSitemapLocs();

  it("lists exactly the canonical public marketing pages, all absolute", () => {
    expect(urls).toEqual([
      SITE_URL,
      `${SITE_URL}/om`,
      `${SITE_URL}/kontakt`,
      `${SITE_URL}/for-utvecklare`,
      `${SITE_URL}/integritet`,
      `${SITE_URL}/kontaktperson-i-annons`,
      `${SITE_URL}/cookies`,
      `${SITE_URL}/villkor`,
      `${SITE_URL}/tillganglighet`,
      `${SITE_URL}/hjalpcenter`,
      `${SITE_URL}/tips`,
      `${SITE_URL}/vanliga-fragor`,
      `${SITE_URL}/matchning`,
      `${SITE_URL}/cv-granskning`,
      `${SITE_URL}/lediga-jobb`,
    ]);
  });

  it("lists the catalogue's first page at exactly its canonical, and no other catalogue page (ADR 0176 F8)", () => {
    expect(urls.at(-1)).toBe(publicCatalogueUrl(1));
    expect(urls.filter((u) => u.includes("sida="))).toEqual([]);
  });

  it("never lists an authed app route or the /gast demo", () => {
    for (const u of urls) {
      expect(u).not.toMatch(/\/(gast|oversikt|ansokningar|cv|mina-sidor|matchningar|sokningar|sparade)(\/|$)/);
      // /jobb is the authed search; the public demo is /gast/jobb (already excluded above).
      expect(u).not.toBe(`${SITE_URL}/jobb`);
    }
  });

  it("carries a loc for each page and nothing else: Google ignores priority and changefreq, and no lastmod is trustworthy", () => {
    const xml = urlsetXml(urls);

    expect(xml.match(/<loc>/g)).toHaveLength(urls.length);
    expect(xml).not.toMatch(/<(lastmod|changefreq|priority)>/);
  });
});
