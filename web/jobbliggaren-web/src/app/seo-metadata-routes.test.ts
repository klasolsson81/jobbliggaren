import { describe, it, expect } from "vitest";
import { SITE_URL } from "@/lib/site-url";
import robots from "./robots";
import sitemap from "./sitemap";

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

  it("disallows the API and the /gast demo sandbox", () => {
    expect(disallow).toContain("/api/");
    expect(disallow).toContain("/gast");
  });

  it("disallows every authenticated app prefix (mirrors middleware + the (app) routes)", () => {
    // The middleware PROTECTED_PREFIXES + the remaining (app) routes — private areas, never indexed.
    for (const authed of [
      "/oversikt", "/jobb", "/ansokningar", "/cv", "/installningar",
      "/sokningar", "/sparade", "/matchningar", "/aktivitetsrapport", "/ny-ansokan", "/admin",
    ]) {
      expect(disallow).toContain(authed);
    }
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
      `${SITE_URL}/integritet`,
      `${SITE_URL}/cookies`,
      `${SITE_URL}/villkor`,
      `${SITE_URL}/tillganglighet`,
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
