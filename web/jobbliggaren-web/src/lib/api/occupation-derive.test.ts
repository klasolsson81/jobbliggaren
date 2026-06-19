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

import { deriveOccupations } from "./occupation-derive";

const validBody = {
  title: "systemutvecklare",
  candidates: [
    {
      occupationGroupConceptId: "grp_12345",
      occupationGroupLabel: "Mjukvaru- och systemutvecklare",
    },
  ],
};

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("deriveOccupations", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    getSessionIdMock.mockResolvedValue("sess-1");
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
    getSessionIdMock.mockReset();
  });

  it("tom titel → ok med tomma kandidater UTAN backend-rundtur", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await deriveOccupations("");

    expect(result).toEqual({ kind: "ok", data: { title: "", candidates: [] } });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("enbart blanksteg → ok med tomma kandidater UTAN backend-rundtur", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await deriveOccupations("   ");

    expect(result).toEqual({ kind: "ok", data: { title: "", candidates: [] } });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("returnerar unauthorized utan session (BFF läser aldrig backend utan Bearer)", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await deriveOccupations("systemutvecklare");

    expect(result).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 med giltig body → ok + parsade kandidater och anropar derive-endpoint med Bearer", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(validBody));
    global.fetch = fetchMock;

    const result = await deriveOccupations("systemutvecklare");

    expect(result.kind).toBe("ok");
    if (result.kind === "ok") {
      expect(result.data.candidates).toHaveLength(1);
      expect(result.data.candidates[0]?.occupationGroupConceptId).toBe(
        "grp_12345"
      );
    }
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      "http://test-backend/api/v1/saved-searches/derive?title=systemutvecklare"
    );
    expect((init.headers as Record<string, string>).Authorization).toBe(
      "Bearer sess-1"
    );
  });

  it("trimmar titeln i query (omger blanksteg)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(validBody));
    global.fetch = fetchMock;

    await deriveOccupations("  systemutvecklare  ");

    const [url] = fetchMock.mock.calls[0] as [string];
    expect(url).toBe(
      "http://test-backend/api/v1/saved-searches/derive?title=systemutvecklare"
    );
  });

  it("mappar 401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
    expect(await deriveOccupations("x")).toEqual({ kind: "unauthorized" });
  });

  it("mappar 429 → rateLimited med retryAfterSeconds", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(null, { status: 429, headers: { "Retry-After": "30" } })
    );
    expect(await deriveOccupations("x")).toEqual({
      kind: "rateLimited",
      retryAfterSeconds: 30,
    });
  });

  it("mappar 500 → error", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 500 }));
    expect(await deriveOccupations("x")).toEqual({ kind: "error" });
  });

  it("mappar shape-mismatch → error (fail-loud)", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ title: "x" }));
    expect(await deriveOccupations("x")).toEqual({ kind: "error" });
  });

  it("mappar nätverksfel (throw) → error", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));
    expect(await deriveOccupations("x")).toEqual({ kind: "error" });
  });
});
