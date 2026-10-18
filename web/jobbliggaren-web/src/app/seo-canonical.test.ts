import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import type { Metadata } from "next";
import { beforeAll, describe, expect, it, vi } from "vitest";
import { SITE_URL } from "@/lib/site-url";
import { marketingSitemapLocs } from "@/lib/seo/marketing-sitemap";
import { metadataBlock } from "@/test/metadata-source";
import robots from "./robots";

/**
 * #2092: every indexable public page names itself as its canonical, absolutely, and no
 * layout names one that other routes would inherit.
 *
 * The indexable surface is DERIVED from the filesystem (every page in the two marketing
 * route groups, and every page of `(public-job)` that is a URL of its own, which is the
 * catalogue: its internal `publik/` pages are reached by a rewrite and are no address)
 * and must equal the set the sitemap lists, so a new public page fails here until it is
 * both listed and self-canonical.
 */

vi.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) => key,
  getLocale: async () => "sv",
}));

// The catalogue's metadata reads its page through the cached reader; an empty page 1 is a real answer (a measured zero).
vi.mock("@/lib/api/public-job-ad-catalogue", () => ({
  getPublicJobAdCatalogue: async () => ({
    kind: "ok",
    page: { items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 },
  }),
}));

type PageModule = { generateMetadata?: () => Promise<Metadata> };

const pages = import.meta.glob("./**/page.tsx");

const PUBLIC_GROUPS = new Set(["(marketing)", "(marketing-inner)"]);
const LOGIN_PAGE = "./(auth)/logga-in/page.tsx";

/** `./(marketing-inner)/om/page.tsx` is `/om`: a route group adds no URL segment. */
function routeOf(key: string): string {
  return `/${key.split("/").slice(2, -1).join("/")}`;
}

const isOwnPublicJobUrl = (key: string) => key.split("/")[1] === "(public-job)" && key.split("/")[2] !== "publik";

const publicPages = Object.keys(pages)
  .filter((key) => PUBLIC_GROUPS.has(key.split("/")[1] ?? "") || isOwnPublicJobUrl(key))
  .sort((a, b) => routeOf(a).localeCompare(routeOf(b)));

const metadataByKey = new Map<string, Metadata | undefined>();

beforeAll(async () => {
  // Importing a page module is slow on a cold transform, so every import happens here,
  // under one generous timeout, and the cases below only read the results.
  for (const key of [...publicPages, LOGIN_PAGE]) {
    const load = pages[key];
    if (load === undefined) throw new Error(`no page module at ${key}`);
    const { generateMetadata } = (await load()) as PageModule;
    if (generateMetadata === undefined) throw new Error(`${key} exports no generateMetadata`);
    metadataByKey.set(key, await generateMetadata());
  }
}, 120_000);

describe("self-canonicals on the indexable public pages (#2092)", () => {
  it("derives exactly the pages the sitemap lists", () => {
    const listed = marketingSitemapLocs()
      .map((url) => (url === SITE_URL ? "/" : url.slice(SITE_URL.length)))
      .sort((a, b) => a.localeCompare(b));

    expect(publicPages.map(routeOf)).toEqual(listed);
  });

  it.each(publicPages)("%s names itself as its absolute canonical", (key) => {
    const route = routeOf(key);

    expect(metadataByKey.get(key)?.alternates?.canonical).toBe(
      route === "/" ? SITE_URL : `${SITE_URL}${route}`
    );
  });

  it("is declared by no layout, so no route inherits a canonical that is not its own", () => {
    const app = dirname(fileURLToPath(import.meta.url));
    const layouts: string[] = [];
    const walk = (dir: string): void => {
      for (const entry of readdirSync(dir, { withFileTypes: true })) {
        const full = join(dir, entry.name);
        if (entry.isDirectory()) walk(full);
        else if (entry.name === "layout.tsx") layouts.push(full);
      }
    };
    walk(app);

    expect(layouts.map((file) => relative(app, file))).toContain("layout.tsx");
    for (const file of layouts) {
      const block = metadataBlock(readFileSync(file, "utf8")) ?? "";
      expect(block, relative(app, file)).not.toMatch(/\b(alternates|canonical)\b/);
    }
  });
});

describe("/logga-in (#2092)", () => {
  it("is noindex", () => {
    expect(metadataByKey.get(LOGIN_PAGE)?.robots).toMatchObject({ index: false });
  });

  it("is not disallowed in robots.txt, because a crawler must fetch it to read the noindex", () => {
    const { rules } = robots();
    const everyone = (Array.isArray(rules) ? rules : [rules]).find((group) => group.userAgent === "*");
    const disallow = ([] as string[]).concat(everyone?.disallow ?? []);
    const blocks = (path: string): boolean =>
      disallow.some((entry) =>
        entry.endsWith("$") ? path === entry.slice(0, -1) : path.startsWith(entry)
      );

    expect(disallow.length).toBeGreaterThan(0);
    expect(blocks("/logga-in")).toBe(false);
  });
});
