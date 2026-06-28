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

import { getJobsWatermark, markJobsSeen } from "./me-jobs";

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

describe("getJobsWatermark (#293/#306)", () => {
  it("utan session → unauthorized utan backend-rundtur", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await getJobsWatermark();

    expect(result).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 { lastSeenJobsAt } → ok med tidsstämpeln", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(jsonResponse({ lastSeenJobsAt: "2026-06-28T08:00:00Z" }));
    const result = await getJobsWatermark();
    expect(result).toEqual({
      kind: "ok",
      data: { lastSeenJobsAt: "2026-06-28T08:00:00Z" },
    });
  });

  it("lastSeenJobsAt === null är honest ok (kall start / första besöket)", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(jsonResponse({ lastSeenJobsAt: null }));
    const result = await getJobsWatermark();
    expect(result).toEqual({ kind: "ok", data: { lastSeenJobsAt: null } });
  });

  it("401 → unauthorized (anon → ingen NY, cold-start)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 401 }));
    const result = await getJobsWatermark();
    expect(result).toEqual({ kind: "unauthorized" });
  });

  it("network-fail → error", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("ENETUNREACH"));
    const result = await getJobsWatermark();
    expect(result).toEqual({ kind: "error" });
  });

  it("shape-mismatch → error (lastSeenJobsAt saknas)", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ seenAt: "x" }));
    const result = await getJobsWatermark();
    expect(result).toEqual({ kind: "error" });
  });
});

describe("markJobsSeen (#293/#306)", () => {
  it("utan session → unauthorized utan backend-rundtur", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await markJobsSeen();

    expect(result).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("204 → ok (watermarken framflyttad)", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    global.fetch = fetchMock;

    const result = await markJobsSeen();

    expect(result).toEqual({ kind: "ok", data: undefined });
    expect(fetchMock).toHaveBeenCalledWith(
      "http://test-backend/api/v1/me/jobs/seen",
      expect.objectContaining({ method: "POST" })
    );
  });

  it("401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 401 }));
    const result = await markJobsSeen();
    expect(result).toEqual({ kind: "unauthorized" });
  });

  it("network-fail → error (icke-kritisk, kastar aldrig)", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("ENETUNREACH"));
    const result = await markJobsSeen();
    expect(result).toEqual({ kind: "error" });
  });
});
