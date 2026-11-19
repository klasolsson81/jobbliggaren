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

import { getPublicJuniorItJobAds } from "./public-junior-it-job-ads";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";

const SENTINEL_NAME = "Sentinel Namnsson";
const fetchMock = vi.fn<typeof fetch>();

const ROW = {
  id: "0c0c0c0c-1111-4222-8333-444444444444",
  title: "Junior utvecklare",
  companyName: "Exempel AB",
  municipalityName: "Göteborg",
  regionName: "Västra Götalands län",
  publishedAt: "2026-11-10T08:00:00+00:00",
  expiresAt: "2026-12-10T22:59:59+00:00",
};
const ANSWER = {
  rows: { items: [ROW], totalCount: 41, page: 2, pageSize: 20, totalPages: 3 },
  places: [{ municipalityConceptId: "PVZL_BQT_XtL", name: "Göteborg", count: 41 }],
};

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

describe("getPublicJuniorItJobAds", () => {
  it("returns the page and the kommun options", async () => {
    fetchMock.mockResolvedValue(json(ANSWER));

    expect(await getPublicJuniorItJobAds("PVZL_BQT_XtL", "ExpiresAtAsc", 2)).toEqual({ kind: "ok", page: ANSWER });
  });

  it("parses a row without a last publication day", async () => {
    const answer = { ...ANSWER, rows: { ...ANSWER.rows, items: [{ ...ROW, expiresAt: null }] } };
    fetchMock.mockResolvedValue(json(answer));

    expect(await getPublicJuniorItJobAds(null, "PublishedAtDesc", 1)).toEqual({ kind: "ok", page: answer });
  });

  it("reads once, per request, uncached, relaying only the forwarded headers and following no redirect", async () => {
    fetchMock.mockResolvedValue(json(ANSWER));
    await getPublicJuniorItJobAds("PVZL_BQT_XtL", "ExpiresAtAsc", 2);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url).toBe(
      "http://backend.test/api/v1/public/job-ads/junior?sortBy=ExpiresAtAsc&page=2&municipality=PVZL_BQT_XtL"
    );
    expect(init?.cache).toBe("no-store");
    expect(init?.redirect).toBe("manual");
    expect(init?.signal).toBeInstanceOf(AbortSignal);
    expect(Object.keys(init?.headers ?? {}).sort()).toEqual(["x-forwarded-for", "x-forwarded-proto"]);
    expect(init).not.toHaveProperty("next");
    expect(forwardedHeaders).toHaveBeenCalled();
  });

  it("sends no kommun when every kommun is asked for", async () => {
    fetchMock.mockResolvedValue(json(ANSWER));
    await getPublicJuniorItJobAds(null, "PublishedAtDesc", 1);

    expect(fetchMock.mock.calls[0]?.[0]).toBe("http://backend.test/api/v1/public/job-ads/junior?sortBy=PublishedAtDesc&page=1");
  });

  it.each([404, 400])("reads a %i as a page that does not exist", async (status) => {
    fetchMock.mockResolvedValue(new Response(null, { status }));
    expect(await getPublicJuniorItJobAds(null, "PublishedAtDesc", 9)).toEqual({ kind: "notFound" });
  });

  it.each([429, 500, 502, 503, 504, 301, 401, 403, 410])(
    "reads a %i as unavailable, never as a page that does not exist",
    async (status) => {
      fetchMock.mockResolvedValue(new Response(null, { status, headers: { "Retry-After": "9" } }));
      expect(await getPublicJuniorItJobAds(null, "PublishedAtDesc", 1)).toEqual({ kind: "unavailable" });
    }
  );

  it("reads a network error and a timeout as unavailable", async () => {
    fetchMock.mockRejectedValueOnce(new TypeError("fetch failed"));
    expect(await getPublicJuniorItJobAds(null, "PublishedAtDesc", 1)).toEqual({ kind: "unavailable" });

    fetchMock.mockRejectedValueOnce(new DOMException("timed out", "TimeoutError"));
    expect(await getPublicJuniorItJobAds(null, "PublishedAtDesc", 1)).toEqual({ kind: "unavailable" });
  });

  it("reads a row with a member beyond the seven as unavailable, and logs no value from it", async () => {
    const log = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const answer = { ...ANSWER, rows: { ...ANSWER.rows, items: [{ ...ROW, description: SENTINEL_NAME }] } };
    fetchMock.mockResolvedValue(json(answer));

    expect(await getPublicJuniorItJobAds(null, "PublishedAtDesc", 1)).toEqual({ kind: "unavailable" });
    expect(JSON.stringify(log.mock.calls)).not.toContain(SENTINEL_NAME);
  });

  it.each([
    ["an envelope key", { ...ANSWER, asOf: "2026-11-19T12:00:00Z" }],
    ["a page key", { ...ANSWER, rows: { ...ANSWER.rows, asOf: "x" } }],
    ["an option key keyed by employer", { ...ANSWER, places: [{ ...ANSWER.places[0], employer: SENTINEL_NAME }] }],
  ])("reads %s beyond the delivered set as unavailable", async (_label, answer) => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    fetchMock.mockResolvedValue(json(answer));

    expect(await getPublicJuniorItJobAds(null, "PublishedAtDesc", 1)).toEqual({ kind: "unavailable" });
  });

  it("lets Next's own control flow through instead of reading it as an outage", async () => {
    const bailout = Object.assign(new Error("Dynamic server usage"), { digest: "DYNAMIC_SERVER_USAGE" });
    fetchMock.mockRejectedValue(bailout);
    await expect(getPublicJuniorItJobAds(null, "PublishedAtDesc", 1)).rejects.toBe(bailout);
  });
});
