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

import { updateNotificationConsent } from "./me";

const originalFetch = global.fetch;

beforeEach(() => {
  getSessionIdMock.mockResolvedValue("sess-1");
});
afterEach(() => {
  global.fetch = originalFetch;
  vi.restoreAllMocks();
  getSessionIdMock.mockReset();
});

describe("updateNotificationConsent (ADR 0080 Vag 4 PR-6)", () => {
  it("utan session → unauthorized utan backend-rundtur", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await updateNotificationConsent({
      enabled: true,
      cadence: "Weekly",
    });

    expect(result).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("204 → ok (consent sparat)", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    global.fetch = fetchMock;

    const result = await updateNotificationConsent({
      enabled: true,
      cadence: "Daily",
    });

    expect(result).toEqual({ kind: "ok", data: undefined });
  });

  it("PUT mot rätt endpoint med Bearer + {enabled, cadence}-body (wire-värden)", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    global.fetch = fetchMock;

    await updateNotificationConsent({ enabled: true, cadence: "Daily" });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://test-backend/api/v1/me/notification-consent");
    expect(init.method).toBe("PUT");
    expect(
      (init.headers as Record<string, string>).Authorization
    ).toBe("Bearer sess-1");
    expect(
      (init.headers as Record<string, string>)["Content-Type"]
    ).toBe("application/json");
    // Wire-värdena är PascalCase-strängarna (JsonStringEnumConverter), aldrig
    // ordinaler eller de svenska etiketterna.
    expect(JSON.parse(init.body as string)).toEqual({
      enabled: true,
      cadence: "Daily",
    });
  });

  it("opt-out (enabled:false) skickas som full-replace-body", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    global.fetch = fetchMock;

    await updateNotificationConsent({ enabled: false, cadence: "Weekly" });

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(JSON.parse(init.body as string)).toEqual({
      enabled: false,
      cadence: "Weekly",
    });
  });

  it("401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 401 }));
    const result = await updateNotificationConsent({
      enabled: true,
      cadence: "Weekly",
    });
    expect(result).toEqual({ kind: "unauthorized" });
  });

  it("403 → forbidden", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 403 }));
    const result = await updateNotificationConsent({
      enabled: true,
      cadence: "Weekly",
    });
    expect(result).toEqual({ kind: "forbidden" });
  });

  it("429 → rateLimited med Retry-After", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(
        new Response("", { status: 429, headers: { "Retry-After": "30" } })
      );
    const result = await updateNotificationConsent({
      enabled: true,
      cadence: "Weekly",
    });
    expect(result).toEqual({ kind: "rateLimited", retryAfterSeconds: 30 });
  });

  it("400 (Problem) → error (body läses aldrig)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 400 }));
    const result = await updateNotificationConsent({
      enabled: true,
      cadence: "Weekly",
    });
    expect(result).toEqual({ kind: "error" });
  });

  it("network-fail → error (kastar aldrig)", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("ENETUNREACH"));
    const result = await updateNotificationConsent({
      enabled: true,
      cadence: "Weekly",
    });
    expect(result).toEqual({ kind: "error" });
  });
});
