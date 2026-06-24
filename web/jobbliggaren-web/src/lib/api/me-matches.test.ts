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

import { getMyMatches, getNewMatchCount, markMatchesSeen } from "./me-matches";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const originalFetch = global.fetch;

beforeEach(() => {
  getSessionIdMock.mockResolvedValue("sess-1");
});
afterEach(() => {
  global.fetch = originalFetch;
  vi.restoreAllMocks();
  getSessionIdMock.mockReset();
});

describe("getNewMatchCount (ADR 0080 Vag 4 PR-5)", () => {
  it("utan session → unauthorized utan backend-rundtur", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await getNewMatchCount();

    expect(result).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 { count } → ok med siffran", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ count: 7 }));
    const result = await getNewMatchCount();
    expect(result).toEqual({ kind: "ok", data: { count: 7 } });
  });

  it("count === 0 är honest ok (ingen ny match)", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ count: 0 }));
    const result = await getNewMatchCount();
    expect(result).toEqual({ kind: "ok", data: { count: 0 } });
  });

  it("401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 401 }));
    const result = await getNewMatchCount();
    expect(result).toEqual({ kind: "unauthorized" });
  });

  it("network-fail → error", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("ENETUNREACH"));
    const result = await getNewMatchCount();
    expect(result).toEqual({ kind: "error" });
  });

  it("shape-mismatch → error (count saknas)", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ total: 3 }));
    const result = await getNewMatchCount();
    expect(result).toEqual({ kind: "error" });
  });
});

describe("getMyMatches (ADR 0080 Vag 4 PR-5)", () => {
  const validItem = {
    jobAdId: "11111111-1111-1111-1111-111111111111",
    title: "Systemutvecklare",
    company: "Skatteverket",
    url: "https://example.se/ad/1",
    grade: "Strong",
    createdAt: "2026-06-14T08:00:00+00:00",
    isNew: true,
  };

  it("utan session → unauthorized utan backend-rundtur", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await getMyMatches();

    expect(result).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 [ item ] → ok med listan", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse([validItem]));
    const result = await getMyMatches();
    expect(result.kind).toBe("ok");
    if (result.kind === "ok") {
      expect(result.data).toHaveLength(1);
      expect(result.data[0]?.grade).toBe("Strong");
      expect(result.data[0]?.isNew).toBe(true);
    }
  });

  it("tom lista → ok med []", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse([]));
    const result = await getMyMatches();
    expect(result).toEqual({ kind: "ok", data: [] });
  });

  it("url === null tolereras (nullable)", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(jsonResponse([{ ...validItem, url: null }]));
    const result = await getMyMatches();
    expect(result.kind).toBe("ok");
    if (result.kind === "ok") expect(result.data[0]?.url).toBeNull();
  });

  it("okänd grad (Basic persisteras aldrig) → error (strikt enum)", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(jsonResponse([{ ...validItem, grade: "Basic" }]));
    const result = await getMyMatches();
    expect(result).toEqual({ kind: "error" });
  });

  it("429 → rateLimited", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(
        new Response("", { status: 429, headers: { "Retry-After": "30" } })
      );
    const result = await getMyMatches();
    expect(result).toEqual({ kind: "rateLimited", retryAfterSeconds: 30 });
  });

  it("network-fail → error", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("ENETUNREACH"));
    const result = await getMyMatches();
    expect(result).toEqual({ kind: "error" });
  });
});

describe("markMatchesSeen (ADR 0080 Vag 4 PR-5)", () => {
  it("utan session → unauthorized utan backend-rundtur", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await markMatchesSeen();

    expect(result).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("204 → ok (vattenmärket avancerat)", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    global.fetch = fetchMock;

    const result = await markMatchesSeen();

    expect(result).toEqual({ kind: "ok", data: undefined });
    // POST mot rätt endpoint.
    expect(fetchMock).toHaveBeenCalledWith(
      "http://test-backend/api/v1/me/matches/seen",
      expect.objectContaining({ method: "POST" })
    );
  });

  it("401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 401 }));
    const result = await markMatchesSeen();
    expect(result).toEqual({ kind: "unauthorized" });
  });

  it("network-fail → error (icke-kritisk, kastar aldrig)", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("ENETUNREACH"));
    const result = await markMatchesSeen();
    expect(result).toEqual({ kind: "error" });
  });
});
