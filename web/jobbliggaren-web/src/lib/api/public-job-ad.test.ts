import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));
vi.mock("@/lib/http/forwarded-headers", () => ({
  forwardedHeaders: vi.fn(async () => ({ "x-forwarded-for": "203.0.113.9" })),
}));
// `cache()` is a request-scope memo in a Server Component; here it must not hide a second call.
vi.mock("react", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react")>()),
  cache: <T,>(fn: T) => fn,
}));

import { getPublicJobAd } from "./public-job-ad";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";

const ID = "0b0b0b0b-1111-4222-8333-444444444444";
const SENTINEL_NAME = "Sentinel Namnsson";
const fetchMock = vi.fn<typeof fetch>();

const WIRE = {
  id: ID,
  title: "Sjuksköterska",
  companyName: "Region Exempel",
  description: "Vi söker en sjuksköterska.",
  url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
  publishedAt: "2026-10-01T08:00:00+00:00",
  expiresAt: null,
  municipalityConceptId: null,
  regionConceptId: null,
  occupationGroupConceptId: null,
  employmentTypeConceptId: null,
  worktimeExtentConceptId: null,
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

describe("getPublicJobAd", () => {
  it("returns the listed ad", async () => {
    fetchMock.mockResolvedValue(json(WIRE));
    const result = await getPublicJobAd(ID);

    expect(result.kind).toBe("ok");
    if (result.kind === "ok") expect(result.jobAd.title).toBe("Sjuksköterska");
  });

  it("reads once, per request, uncached, with the client address relayed and no redirect followed (ADR 0160 D5, D6, D9.4)", async () => {
    fetchMock.mockResolvedValue(json(WIRE));
    await getPublicJobAd(ID);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url).toBe(`http://backend.test/api/v1/public/job-ads/${ID}`);
    expect(init?.cache).toBe("no-store");
    expect(init?.redirect).toBe("manual");
    expect(init?.headers).toEqual({ "x-forwarded-for": "203.0.113.9" });
    expect(init).not.toHaveProperty("next");
    expect(forwardedHeaders).toHaveBeenCalled();
  });

  it.each([404, 410])("reads a %i as a 404 for the page (the ad ended after the edge's probe answered 200)", async (status) => {
    fetchMock.mockResolvedValue(new Response(null, { status }));
    expect(await getPublicJobAd(ID)).toEqual({ kind: "notFound" });
  });

  it.each([429, 500, 502, 503, 504, 301, 401, 403])(
    "reads a %i as unavailable, never as a removal (ADR 0160 D9.5)",
    async (status) => {
      fetchMock.mockResolvedValue(new Response(null, { status, headers: { "Retry-After": "9" } }));
      expect(await getPublicJobAd(ID)).toEqual({ kind: "unavailable" });
    }
  );

  it("reads a network error and a timeout as unavailable", async () => {
    fetchMock.mockRejectedValueOnce(new TypeError("fetch failed"));
    expect(await getPublicJobAd(ID)).toEqual({ kind: "unavailable" });

    fetchMock.mockRejectedValueOnce(new DOMException("timed out", "TimeoutError"));
    expect(await getPublicJobAd(ID)).toEqual({ kind: "unavailable" });
  });

  it("reads an answer that is not the contract as unavailable, never as an empty page, and logs no value from it", async () => {
    const log = vi.spyOn(console, "error").mockImplementation(() => undefined);
    fetchMock.mockResolvedValue(json({ ...WIRE, title: 42, description: SENTINEL_NAME }));

    expect(await getPublicJobAd(ID)).toEqual({ kind: "unavailable" });
    expect(JSON.stringify(log.mock.calls)).not.toContain(SENTINEL_NAME);
  });

  it("reads an unparseable body as unavailable", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    fetchMock.mockResolvedValue(new Response("<html>", { status: 200 }));
    expect(await getPublicJobAd(ID)).toEqual({ kind: "unavailable" });
  });

  it("lets Next's own control flow through instead of reading it as an outage", async () => {
    const bailout = Object.assign(new Error("Dynamic server usage"), { digest: "DYNAMIC_SERVER_USAGE" });
    fetchMock.mockRejectedValue(bailout);
    await expect(getPublicJobAd(ID)).rejects.toBe(bailout);
  });

  it("answers an id that is not a GUID with a 404 and never asks the API", async () => {
    expect(await getPublicJobAd("not-a-guid")).toEqual({ kind: "notFound" });
    expect(await getPublicJobAd("../admin")).toEqual({ kind: "notFound" });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
