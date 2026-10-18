// @vitest-environment node
// The routes run on Node and answer with undici's Response.
import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));

import { SITE_URL } from "@/lib/site-url";
import { jobSitemapParts, sitemapIndexLocs } from "@/lib/seo/job-sitemap";
import { marketingSitemapLocs } from "@/lib/seo/marketing-sitemap";
import { MAX_URLS_PER_SITEMAP } from "@/lib/seo/sitemap-xml";
import * as indexRoute from "./sitemap.xml/route";
import * as marketingRoute from "./sitemap-sidor.xml/route";
import * as partOneRoute from "./sitemap-jobb-1.xml/route";
import * as partTwoRoute from "./sitemap-jobb-2.xml/route";

// ADR 0174 — the sitemap's four files, answered per request with no-store, and the fitness rules that keep them so: no
// file under app/ claims /sitemap.xml as a metadata route, every route is rendered per request (the build has no
// backend), and one route folder exists per part.

const ID_ONE = "0b0b0b0b-1111-4222-8333-444444444444";
const ID_TWO = "1c1c1c1c-1111-4222-8333-444444444444";
const fetchMock = vi.fn<typeof fetch>();

const here = dirname(fileURLToPath(import.meta.url));
const app = dirname(here);

function request(path: string, headers: Record<string, string> = {}): Request {
  return new Request(`http://localhost${path}`, { headers });
}

function locsOf(xml: string): string[] {
  return [...xml.matchAll(/<loc>([^<]*)<\/loc>/g)].map((match) => match[1] ?? "");
}

function expectXml(response: Response): void {
  expect(response.status).toBe(200);
  expect(response.headers.get("content-type")).toBe("application/xml; charset=utf-8");
  expect(response.headers.get("cache-control")).toBe("no-store");
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("the sitemap's files", () => {
  it("the index lists the marketing part and every job part, reading no data", async () => {
    const response = indexRoute.GET();

    expectXml(response);
    expect(locsOf(await response.text())).toEqual(sitemapIndexLocs());
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("the marketing part lists the marketing pages, reading no data", async () => {
    const response = marketingRoute.GET();

    expectXml(response);
    expect(locsOf(await response.text())).toEqual(marketingSitemapLocs());
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("a job part lists each id at its page's canonical URL, asking the API for its own part with the crawler's address", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ ids: [ID_ONE, ID_TWO] }), { status: 200 }));

    const response = await partOneRoute.GET(
      request("/sitemap-jobb-1.xml?part=2&parts=16", {
        "x-forwarded-for": "203.0.113.7",
        cookie: "__Host-jobbliggaren_session=x",
        authorization: "Bearer x",
      })
    );

    expectXml(response);
    expect(locsOf(await response.text())).toEqual([`${SITE_URL}/jobb/${ID_ONE}`, `${SITE_URL}/jobb/${ID_TWO}`]);
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url, "the part is the route file's, never the query string's").toMatch(/[?&]part=1&parts=2$/);
    expect(init?.headers).toEqual({ "x-forwarded-for": "203.0.113.7" });
  });

  it("each job part asks for its own part", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ ids: [] }), { status: 200 }));

    await partTwoRoute.GET(request("/sitemap-jobb-2.xml"));

    expect(String(fetchMock.mock.calls[0]?.[0])).toMatch(/[?&]part=2&/);
  });

  it("a job part that cannot be read is a 503 with Retry-After, no body and no-store, never an empty part", async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 503, headers: { "retry-after": "60" } }));

    const response = await partOneRoute.GET(request("/sitemap-jobb-1.xml"));

    expect(response.status).toBe(503);
    expect(response.headers.get("retry-after")).toBe("60");
    expect(response.headers.get("cache-control")).toBe("no-store");
    expect(await response.text()).toBe("");
  });

  it("a job part over a file's limit is a 503, logged by its count and nothing else", async () => {
    const logged = vi.spyOn(console, "error").mockImplementation(() => {});
    const ids = Array.from({ length: MAX_URLS_PER_SITEMAP + 1 }, (_, i) => `00000000-0000-4000-8000-${String(i).padStart(12, "0")}`);
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ ids }), { status: 200 }));

    const response = await partOneRoute.GET(request("/sitemap-jobb-1.xml"));

    expect(response.status).toBe(503);
    expect(response.headers.get("retry-after")).toBe("30");
    expect(logged).toHaveBeenCalledTimes(1);
    expect(logged.mock.calls[0]?.[1]).toEqual({ part: 1, count: MAX_URLS_PER_SITEMAP + 1 });
  });
});

describe("the sitemap's fitness rules", () => {
  it("every file is rendered per request, never prerendered or revalidated", () => {
    for (const route of [indexRoute, marketingRoute, partOneRoute, partTwoRoute]) {
      expect(route.dynamic).toBe("force-dynamic");
      expect(route).not.toHaveProperty("revalidate");
      expect(route).not.toHaveProperty("generateStaticParams");
    }
  });

  it("no file under app/ is a metadata sitemap, which would claim /sitemap.xml as well", () => {
    const metadataSitemaps: string[] = [];
    const walk = (dir: string): void => {
      for (const entry of readdirSync(dir, { withFileTypes: true })) {
        const full = join(dir, entry.name);
        if (entry.isDirectory()) walk(full);
        else if (/^sitemap\.(ts|tsx|js|jsx|xml)$/.test(entry.name)) metadataSitemaps.push(relative(app, full));
      }
    };
    walk(app);

    expect(metadataSitemaps).toEqual([]);
  });

  it("one route folder exists per job part, and no other", () => {
    const folders = readdirSync(here, { withFileTypes: true })
      .filter((entry) => entry.isDirectory() && entry.name.startsWith("sitemap-jobb-"))
      .map((entry) => entry.name)
      .sort();

    expect(folders).toEqual(jobSitemapParts().map((part) => `sitemap-jobb-${part}.xml`).sort());
    for (const folder of folders) {
      const part = Number(/^sitemap-jobb-(\d+)\.xml$/.exec(folder)?.[1]);
      expect(readFileSync(join(here, folder, "route.ts"), "utf8"), folder).toContain(`jobSitemapPartResponse(${part}, request)`);
    }
  });
});
