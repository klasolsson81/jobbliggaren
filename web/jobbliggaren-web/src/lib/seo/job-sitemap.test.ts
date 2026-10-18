import { describe, expect, it } from "vitest";
import { SITE_URL } from "@/lib/site-url";
import { isProtectedPath } from "@/lib/auth/protected-routes";
import {
  JOB_SITEMAP_PART_COUNT,
  jobSitemapParts,
  jobSitemapPath,
  MARKETING_SITEMAP_PATH,
  SITEMAP_INDEX_PATH,
  sitemapIndexLocs,
} from "./job-sitemap";

// ADR 0174 — where the sitemap's files live: all at the site root, which a sitemap must be to cover /jobb/<uuid>, and
// none under a protected prefix, which would send a crawler to the login page.

describe("job sitemap addressing", () => {
  it("numbers the parts from 1 to the part count", () => {
    expect(jobSitemapParts()).toEqual(Array.from({ length: JOB_SITEMAP_PART_COUNT }, (_, i) => i + 1));
  });

  it("lists the marketing part and then every job part, absolutely", () => {
    expect(sitemapIndexLocs()).toEqual([
      `${SITE_URL}${MARKETING_SITEMAP_PATH}`,
      ...jobSitemapParts().map((part) => `${SITE_URL}/sitemap-jobb-${part}.xml`),
    ]);
  });

  it("keeps every file at the site root, outside every protected prefix", () => {
    for (const path of [SITEMAP_INDEX_PATH, MARKETING_SITEMAP_PATH, ...jobSitemapParts().map(jobSitemapPath)]) {
      expect(path, path).toMatch(/^\/[^/]+\.xml$/);
      expect(isProtectedPath(path), path).toBe(false);
    }
  });
});
