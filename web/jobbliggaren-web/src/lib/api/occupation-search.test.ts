import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

const { getSessionIdMock } = vi.hoisted(() => ({
  getSessionIdMock: vi.fn<() => Promise<string | null>>(),
}));
vi.mock("@/lib/auth/session", () => ({
  getSessionId: getSessionIdMock,
}));

import { searchOccupations } from "./occupation-search";

const body = {
  rows: [
    {
      kind: "Group",
      groupConceptId: "DJh5_yyF_hEM",
      groupLabel: "Mjukvaru- och systemutvecklare m.fl.",
      matchedNames: ["Backend-utvecklare"],
      labelHit: false,
      headWord: null,
      variants: [],
      exactMatch: true,
    },
  ],
  totalRows: 1,
};

function jsonResponse(value: unknown, status = 200): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("searchOccupations (#2198)", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    getSessionIdMock.mockResolvedValue("sess-1");
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
    getSessionIdMock.mockReset();
  });

  it("fetches uncached, with no revalidate window, and sends only q", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(body));
    global.fetch = fetchMock;

    const result = await searchOccupations("Backend-utvecklare");

    expect(result).toEqual({ kind: "ok", data: body });
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit & { next?: unknown }];
    const parsed = new URL(url);
    expect(`${parsed.origin}${parsed.pathname}`).toBe(
      "http://test-backend/api/v1/job-ads/taxonomy/occupations/search",
    );
    expect([...parsed.searchParams.keys()]).toEqual(["q"]);
    expect(parsed.searchParams.get("q")).toBe("Backend-utvecklare");
    expect(init.cache).toBe("no-store");
    expect(init).not.toHaveProperty("next");
    expect((init.headers as Record<string, string>).Authorization).toBe("Bearer sess-1");
  });

  it("keeps åäö and & inside the one q it sends", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(body));
    global.fetch = fetchMock;
    const q = "åäö & a=b";

    await searchOccupations(q);

    const sent = new URL(String(fetchMock.mock.calls[0]?.[0]));
    expect([...sent.searchParams.keys()]).toEqual(["q"]);
    expect(sent.searchParams.getAll("q")).toEqual([q]);
  });

  it("is unauthorized without a session and never reaches the backend", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    expect(await searchOccupations("chef")).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("maps 401, 429 with Retry-After, 500, a shape mismatch and a network failure", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
    expect(await searchOccupations("chef")).toEqual({ kind: "unauthorized" });

    global.fetch = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 429, headers: { "Retry-After": "7" } }));
    expect(await searchOccupations("chef")).toEqual({ kind: "rateLimited", retryAfterSeconds: 7 });

    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 500 }));
    expect(await searchOccupations("chef")).toEqual({ kind: "error" });

    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ rows: "nope", totalRows: 0 }));
    expect(await searchOccupations("chef")).toEqual({ kind: "error" });

    global.fetch = vi.fn().mockRejectedValue(new Error("network"));
    expect(await searchOccupations("chef")).toEqual({ kind: "error" });
  });
});
