import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// env måste mockas innan modulen importeras (env läses vid första anrop).
vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

// `server-only` exporterar ingenting men kastar i klient-bundel. Mocka det.
vi.mock("server-only", () => ({}));

describe("fetchLandingStats (ADR 0064)", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    // React.cache memoiserar per-request — i vitest-jsdom är "samma request"
    // hela testlivstiden. Reset:a för varje test via dynamisk re-import
    // istället: enklare att skippa cache:n i test genom att mocka fetch
    // varje gång och verifiera siste anropet.
    vi.resetModules();
  });

  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  it("returnerar parsad DTO vid 200 OK + giltig shape", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          activeCount: 45_580,
          newToday: 312,
          isStale: false,
          refreshedAt: "2026-05-23T12:00:00+00:00",
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      ),
    );
    const { fetchLandingStats } = await import("./landing");
    const result = await fetchLandingStats();
    expect(result).not.toBeNull();
    expect(result!.activeCount).toBe(45_580);
    expect(result!.newToday).toBe(312);
    expect(result!.isStale).toBe(false);
  });

  it("returnerar null vid 5xx (backend nere)", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(new Response("", { status: 503 }));
    const { fetchLandingStats } = await import("./landing");
    const result = await fetchLandingStats();
    expect(result).toBeNull();
  });

  it("returnerar null vid 429 rate-limit (caller renderar civilt)", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(new Response("", { status: 429 }));
    const { fetchLandingStats } = await import("./landing");
    const result = await fetchLandingStats();
    expect(result).toBeNull();
  });

  it("returnerar null vid network-fail (fetch kastar)", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("ENETUNREACH"));
    const { fetchLandingStats } = await import("./landing");
    const result = await fetchLandingStats();
    expect(result).toBeNull();
  });

  it("returnerar null vid shape-mismatch (saknad isStale)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          activeCount: 1,
          newToday: 1,
          refreshedAt: null,
          // isStale saknas → Zod-fel → null returneras
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      ),
    );
    const { fetchLandingStats } = await import("./landing");
    const result = await fetchLandingStats();
    expect(result).toBeNull();
  });
});
