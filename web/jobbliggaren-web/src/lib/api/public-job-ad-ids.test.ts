import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));

import { getPublicJobAdIds } from "./public-job-ad-ids";
import { JOB_SITEMAP_PART_COUNT } from "@/lib/seo/job-sitemap";

// ADR 0174 — one sitemap part's read: per request, uncached, the crawler's address relayed and nothing else, and every
// failure a wait rather than an empty part.

const ID = "0b0b0b0b-1111-4222-8333-444444444444";
const FORWARDED = { "x-forwarded-for": "203.0.113.9", "x-forwarded-proto": "https" };
const fetchMock = vi.fn<typeof fetch>();

function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json", ...headers } });
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("getPublicJobAdIds", () => {
  it("reads the part from the API, uncached, with only the forwarding headers it was handed and no redirect followed", async () => {
    fetchMock.mockResolvedValue(json({ ids: [ID] }));

    await getPublicJobAdIds(2, FORWARDED);

    const [url, init] = fetchMock.mock.calls[0] ?? [];
    expect(url).toBe(`http://backend.test/api/v1/public/job-ads/listed-ids?part=2&parts=${JOB_SITEMAP_PART_COUNT}`);
    expect(init?.cache).toBe("no-store");
    expect(init?.redirect).toBe("manual");
    expect(init?.headers).toEqual(FORWARDED);
    expect(init?.signal).toBeInstanceOf(AbortSignal);
  });

  it("answers the part's ids", async () => {
    fetchMock.mockResolvedValue(json({ ids: [ID] }));

    expect(await getPublicJobAdIds(1, {})).toEqual({ kind: "ok", ids: [ID] });
  });

  it("answers an empty part as a measured zero", async () => {
    fetchMock.mockResolvedValue(json({ ids: [] }));

    expect(await getPublicJobAdIds(1, {})).toEqual({ kind: "ok", ids: [] });
  });

  it.each([429, 503])("keeps the API's wait on a %i, bounded", async (status) => {
    fetchMock.mockResolvedValue(json({}, status, { "retry-after": "9" }));

    expect(await getPublicJobAdIds(1, {})).toEqual({ kind: "unavailable", retryAfterSeconds: 9 });
  });

  it("never promises more than the ceiling, whatever the API's Retry-After says", async () => {
    fetchMock.mockResolvedValue(json({}, 503, { "retry-after": "99999" }));

    expect(await getPublicJobAdIds(1, {})).toEqual({ kind: "unavailable", retryAfterSeconds: 120 });
  });

  it.each([400, 404, 410, 500, 302])("answers a %i as the default wait, never as an empty part", async (status) => {
    fetchMock.mockResolvedValue(new Response(null, { status }));

    expect(await getPublicJobAdIds(1, {})).toEqual({ kind: "unavailable", retryAfterSeconds: 30 });
  });

  it.each([
    ["a body that is not JSON", new Response("<html>", { status: 200 })],
    ["an upper-case id", json({ ids: [ID.toUpperCase()] })],
    ["a member besides ids", json({ ids: [ID], count: 1 })],
  ])("answers %s as the default wait, and logs no id", async (_, response) => {
    const logged = vi.spyOn(console, "error").mockImplementation(() => {});
    fetchMock.mockResolvedValue(response);

    expect(await getPublicJobAdIds(1, {})).toEqual({ kind: "unavailable", retryAfterSeconds: 30 });
    for (const call of logged.mock.calls) expect(JSON.stringify(call)).not.toMatch(/0b0b0b0b|0B0B0B0B/);
  });

  it("answers a timeout or a network error as the default wait", async () => {
    fetchMock.mockRejectedValueOnce(new DOMException("timed out", "TimeoutError"));
    expect(await getPublicJobAdIds(1, {})).toEqual({ kind: "unavailable", retryAfterSeconds: 30 });

    fetchMock.mockRejectedValueOnce(new TypeError("fetch failed"));
    expect(await getPublicJobAdIds(1, {})).toEqual({ kind: "unavailable", retryAfterSeconds: 30 });
  });
});
