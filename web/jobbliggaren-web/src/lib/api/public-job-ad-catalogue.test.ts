import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));
vi.mock("@/lib/http/forwarded-headers", () => ({
  forwardedHeaders: vi.fn(async () => ({ "x-forwarded-for": "203.0.113.9", "x-forwarded-proto": "https" })),
}));
// `cache()` is a request-scope memo in a Server Component; here it must not hide a second call.
vi.mock("react", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react")>()),
  cache: <T,>(fn: T) => fn,
}));

import { getPublicJobAdCatalogue } from "./public-job-ad-catalogue";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";

const SENTINEL_NAME = "Sentinel Namnsson";
const fetchMock = vi.fn<typeof fetch>();

const ROW = {
  id: "0b0b0b0b-1111-4222-8333-444444444444",
  title: "Sjuksköterska",
  companyName: "Region Exempel",
  municipalityName: "Umeå",
  regionName: "Västerbottens län",
  publishedAt: "2026-10-01T08:00:00+00:00",
};
const PAGE = { items: [ROW], totalCount: 41, page: 2, pageSize: 20, totalPages: 3 };

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("getPublicJobAdCatalogue", () => {
  it("returns the page", async () => {
    fetchMock.mockResolvedValue(json(PAGE));

    expect(await getPublicJobAdCatalogue(2)).toEqual({ kind: "ok", page: PAGE });
  });

  it("parses the empty corpus's page 1, whose totalPages is 0", async () => {
    const empty = { items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 };
    fetchMock.mockResolvedValue(json(empty));

    expect(await getPublicJobAdCatalogue(1)).toEqual({ kind: "ok", page: empty });
  });

  it("reads once, per request, uncached, relaying only the forwarded headers and following no redirect", async () => {
    fetchMock.mockResolvedValue(json(PAGE));
    await getPublicJobAdCatalogue(2);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url).toBe("http://backend.test/api/v1/public/job-ads/catalogue?page=2");
    expect(init?.cache).toBe("no-store");
    expect(init?.redirect).toBe("manual");
    expect(init?.signal).toBeInstanceOf(AbortSignal);
    expect(Object.keys(init?.headers ?? {}).sort()).toEqual(["x-forwarded-for", "x-forwarded-proto"]);
    expect(init).not.toHaveProperty("next");
    expect(forwardedHeaders).toHaveBeenCalled();
  });

  it.each([404, 400])("reads a %i as a page that does not exist", async (status) => {
    fetchMock.mockResolvedValue(new Response(null, { status }));
    expect(await getPublicJobAdCatalogue(9)).toEqual({ kind: "notFound" });
  });

  it.each([429, 500, 502, 503, 504, 301, 401, 403, 410])(
    "reads a %i as unavailable, never as a page that does not exist",
    async (status) => {
      fetchMock.mockResolvedValue(new Response(null, { status, headers: { "Retry-After": "9" } }));
      expect(await getPublicJobAdCatalogue(1)).toEqual({ kind: "unavailable" });
    }
  );

  it("reads a network error and a timeout as unavailable", async () => {
    fetchMock.mockRejectedValueOnce(new TypeError("fetch failed"));
    expect(await getPublicJobAdCatalogue(1)).toEqual({ kind: "unavailable" });

    fetchMock.mockRejectedValueOnce(new DOMException("timed out", "TimeoutError"));
    expect(await getPublicJobAdCatalogue(1)).toEqual({ kind: "unavailable" });
  });

  it("reads a row with a member beyond the six as unavailable, and logs no value from it", async () => {
    const log = vi.spyOn(console, "error").mockImplementation(() => undefined);
    fetchMock.mockResolvedValue(json({ ...PAGE, items: [{ ...ROW, description: SENTINEL_NAME }] }));

    expect(await getPublicJobAdCatalogue(1)).toEqual({ kind: "unavailable" });
    expect(JSON.stringify(log.mock.calls)).not.toContain(SENTINEL_NAME);
  });

  it("reads a page with a key beyond the five as unavailable", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    fetchMock.mockResolvedValue(json({ ...PAGE, asOf: "2026-10-18T12:00:00Z" }));

    expect(await getPublicJobAdCatalogue(1)).toEqual({ kind: "unavailable" });
  });

  it("lets Next's own control flow through instead of reading it as an outage", async () => {
    const bailout = Object.assign(new Error("Dynamic server usage"), { digest: "DYNAMIC_SERVER_USAGE" });
    fetchMock.mockRejectedValue(bailout);
    await expect(getPublicJobAdCatalogue(1)).rejects.toBe(bailout);
  });
});
