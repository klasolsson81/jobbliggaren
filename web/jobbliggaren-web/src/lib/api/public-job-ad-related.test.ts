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

import { getPublicJobAdRelated } from "./public-job-ad-related";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";

const ID = "0b0b0b0b-1111-4222-8333-444444444444";
const SENTINEL_NAME = "Sentinel Namnsson";
const fetchMock = vi.fn<typeof fetch>();

const ROW = {
  id: "0b0b0b0b-1111-4222-8333-555555555555",
  title: "Sjuksköterska",
  companyName: "Region Exempel",
  municipalityName: "Umeå",
  regionName: "Västerbottens län",
  publishedAt: "2026-10-01T08:00:00+00:00",
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

describe("getPublicJobAdRelated", () => {
  it("returns the rows", async () => {
    fetchMock.mockResolvedValue(json({ items: [ROW] }));

    expect(await getPublicJobAdRelated(ID)).toEqual({ kind: "ok", rows: [ROW] });
  });

  it("returns an empty list as no rows", async () => {
    fetchMock.mockResolvedValue(json({ items: [] }));

    expect(await getPublicJobAdRelated(ID)).toEqual({ kind: "ok", rows: [] });
  });

  it("reads once, per request, uncached, relaying only the forwarded headers, following no redirect, within 2 s", async () => {
    const timeout = vi.spyOn(AbortSignal, "timeout");
    fetchMock.mockResolvedValue(json({ items: [ROW] }));
    await getPublicJobAdRelated(ID);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url).toBe(`http://backend.test/api/v1/public/job-ads/${ID}/related`);
    expect(init?.method).toBe("GET");
    expect(init?.cache).toBe("no-store");
    expect(init?.redirect).toBe("manual");
    expect(init?.signal).toBeInstanceOf(AbortSignal);
    expect(Object.keys(init?.headers ?? {}).sort()).toEqual(["x-forwarded-for", "x-forwarded-proto"]);
    expect(init).not.toHaveProperty("next");
    expect(forwardedHeaders).toHaveBeenCalled();
    expect(timeout).toHaveBeenCalledTimes(1);
    expect(timeout.mock.calls[0]?.[0]).toBeLessThanOrEqual(2_000);
  });

  it("reads nothing for an id that is not a uuid", async () => {
    expect(await getPublicJobAdRelated("not-a-uuid")).toEqual({ kind: "unavailable" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([404, 410, 400, 429, 500, 502, 503, 504, 301, 401, 403, 204])(
    "reads a %i as unavailable, so the page leaves the section out",
    async (status) => {
      fetchMock.mockResolvedValue(new Response(null, { status }));
      expect(await getPublicJobAdRelated(ID)).toEqual({ kind: "unavailable" });
    }
  );

  it("reads a network error and a timeout as unavailable", async () => {
    fetchMock.mockRejectedValueOnce(new TypeError("fetch failed"));
    expect(await getPublicJobAdRelated(ID)).toEqual({ kind: "unavailable" });

    fetchMock.mockRejectedValueOnce(new DOMException("timed out", "TimeoutError"));
    expect(await getPublicJobAdRelated(ID)).toEqual({ kind: "unavailable" });
  });

  it("reads a row with a member beyond the six as unavailable, and logs no value from it", async () => {
    const log = vi.spyOn(console, "error").mockImplementation(() => undefined);
    fetchMock.mockResolvedValue(json({ items: [{ ...ROW, description: SENTINEL_NAME }] }));

    expect(await getPublicJobAdRelated(ID)).toEqual({ kind: "unavailable" });
    expect(JSON.stringify(log.mock.calls)).not.toContain(SENTINEL_NAME);
  });

  it("reads an envelope with a key beyond items as unavailable", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    fetchMock.mockResolvedValue(json({ items: [ROW], totalCount: 7 }));

    expect(await getPublicJobAdRelated(ID)).toEqual({ kind: "unavailable" });
  });

  it("reads a top-level array, the shape it is not, as unavailable", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    fetchMock.mockResolvedValue(json([ROW]));

    expect(await getPublicJobAdRelated(ID)).toEqual({ kind: "unavailable" });
  });

  it("lets Next's own control flow through instead of reading it as an outage", async () => {
    const bailout = Object.assign(new Error("Dynamic server usage"), { digest: "DYNAMIC_SERVER_USAGE" });
    fetchMock.mockRejectedValue(bailout);
    await expect(getPublicJobAdRelated(ID)).rejects.toBe(bailout);
  });
});
