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

import {
  updateDigestCadence,
  updateFollowedCompanyNotificationConsent,
  updateNotificationConsent,
} from "./me";

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

    const result = await updateNotificationConsent({ enabled: true });

    expect(result).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("204 → ok (consent sparat)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));

    const result = await updateNotificationConsent({ enabled: true });

    expect(result).toEqual({ kind: "ok", data: undefined });
  });

  it("PUT mot samtyckes-endpointen med Bearer + en body som bär {enabled} och INGEN kadens", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    global.fetch = fetchMock;

    await updateNotificationConsent({ enabled: true });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      "http://test-backend/api/v1/me/background-match-notification-consent"
    );
    expect(init.method).toBe("PUT");
    expect(
      (init.headers as Record<string, string>).Authorization
    ).toBe("Bearer sess-1");
    expect(
      (init.headers as Record<string, string>)["Content-Type"]
    ).toBe("application/json");
    const body = JSON.parse(init.body as string) as Record<string, unknown>;
    expect(body).toEqual({ enabled: true });
    expect(body).not.toHaveProperty("cadence");
  });

  it("bygger bodyn av det namngivna fältet: en extra nyckel i indata följer aldrig med", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    global.fetch = fetchMock;

    await updateNotificationConsent({
      enabled: false,
      cadence: "Daily",
    } as unknown as { enabled: boolean });

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(JSON.parse(init.body as string)).toEqual({ enabled: false });
  });

  it("401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 401 }));
    expect(await updateNotificationConsent({ enabled: true })).toEqual({
      kind: "unauthorized",
    });
  });

  it("403 → forbidden", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 403 }));
    expect(await updateNotificationConsent({ enabled: true })).toEqual({
      kind: "forbidden",
    });
  });

  it("429 → rateLimited med Retry-After", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(
        new Response("", { status: 429, headers: { "Retry-After": "30" } })
      );
    expect(await updateNotificationConsent({ enabled: true })).toEqual({
      kind: "rateLimited",
      retryAfterSeconds: 30,
    });
  });

  it("400 (Problem) → error (body läses aldrig)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 400 }));
    expect(await updateNotificationConsent({ enabled: true })).toEqual({
      kind: "error",
    });
  });

  it("404 (en backend som ännu inte har vägen) → error", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 404 }));
    expect(await updateNotificationConsent({ enabled: false })).toEqual({
      kind: "error",
    });
  });

  it("409 (en samtidig skrivning vann alla omförsök, ADR 0146) → error, så att ett vägrat tillbakadragande syns", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 409 }));
    expect(await updateNotificationConsent({ enabled: false })).toEqual({
      kind: "error",
    });
  });

  it("network-fail → error (kastar aldrig)", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("ENETUNREACH"));
    expect(await updateNotificationConsent({ enabled: true })).toEqual({
      kind: "error",
    });
  });
});

describe("updateDigestCadence (ADR 0087 D2)", () => {
  it("utan session → unauthorized utan backend-rundtur", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await updateDigestCadence({ cadence: "Daily" });

    expect(result).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("PUT mot kadens-endpointen med Bearer + en body som bär {cadence} och INGET samtycke", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    global.fetch = fetchMock;

    const result = await updateDigestCadence({ cadence: "Daily" });

    expect(result).toEqual({ kind: "ok", data: undefined });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://test-backend/api/v1/me/digest-cadence");
    expect(init.method).toBe("PUT");
    expect((init.headers as Record<string, string>).Authorization).toBe(
      "Bearer sess-1"
    );
    // Wire-värdena är PascalCase-strängarna (JsonStringEnumConverter), aldrig
    // ordinaler eller de svenska etiketterna.
    const body = JSON.parse(init.body as string) as Record<string, unknown>;
    expect(body).toEqual({ cadence: "Daily" });
    expect(body).not.toHaveProperty("enabled");
  });

  it("bygger bodyn av det namngivna fältet: ett samtycke i indata följer aldrig med", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    global.fetch = fetchMock;

    await updateDigestCadence({
      cadence: "Weekly",
      enabled: true,
    } as unknown as { cadence: "Weekly" });

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(JSON.parse(init.body as string)).toEqual({ cadence: "Weekly" });
  });

  it("429 → rateLimited med Retry-After", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(
        new Response("", { status: 429, headers: { "Retry-After": "30" } })
      );
    expect(await updateDigestCadence({ cadence: "Daily" })).toEqual({
      kind: "rateLimited",
      retryAfterSeconds: 30,
    });
  });

  it("400 (Problem) → error (body läses aldrig)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 400 }));
    expect(await updateDigestCadence({ cadence: "Daily" })).toEqual({
      kind: "error",
    });
  });

  it("network-fail → error (kastar aldrig)", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("ENETUNREACH"));
    expect(await updateDigestCadence({ cadence: "Daily" })).toEqual({
      kind: "error",
    });
  });
});

describe("updateFollowedCompanyNotificationConsent (bevakning F4, #803)", () => {
  it("utan session → unauthorized utan backend-rundtur", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await updateFollowedCompanyNotificationConsent({
      enabled: true,
    });

    expect(result).toEqual({ kind: "unauthorized" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("PUT mot FÖLJ-endpointen med Bearer + en body som bär {enabled} och INGEN kadens", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    global.fetch = fetchMock;

    await updateFollowedCompanyNotificationConsent({ enabled: true });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    // Egen endpoint per samtyckesändamål — aldrig matchnings-endpointen.
    expect(url).toBe(
      "http://test-backend/api/v1/me/followed-company-notification-consent"
    );
    expect(init.method).toBe("PUT");
    expect((init.headers as Record<string, string>).Authorization).toBe(
      "Bearer sess-1"
    );
    // Kadensen är DELAD (ADR 0087 D2) och har en egen endpoint. Att skicka den
    // här skulle implicera en andra, oberoende takt som inte finns.
    const body = JSON.parse(init.body as string) as Record<string, unknown>;
    expect(body).toEqual({ enabled: true });
    expect(body).not.toHaveProperty("cadence");
  });

  it("204 → ok (samtycke sparat)", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));

    const result = await updateFollowedCompanyNotificationConsent({
      enabled: true,
    });

    expect(result).toEqual({ kind: "ok", data: undefined });
  });

  it("opt-out (Art. 7(3)-withdrawal) skickas som {enabled:false}", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    global.fetch = fetchMock;

    await updateFollowedCompanyNotificationConsent({ enabled: false });

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(JSON.parse(init.body as string)).toEqual({ enabled: false });
  });

  it("401 → unauthorized", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 401 }));
    const result = await updateFollowedCompanyNotificationConsent({
      enabled: true,
    });
    expect(result).toEqual({ kind: "unauthorized" });
  });

  it("403 → forbidden", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 403 }));
    const result = await updateFollowedCompanyNotificationConsent({
      enabled: true,
    });
    expect(result).toEqual({ kind: "forbidden" });
  });

  it("429 → rateLimited med Retry-After", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(
        new Response("", { status: 429, headers: { "Retry-After": "30" } })
      );
    const result = await updateFollowedCompanyNotificationConsent({
      enabled: true,
    });
    expect(result).toEqual({ kind: "rateLimited", retryAfterSeconds: 30 });
  });

  it("400 (Problem) → error (body läses aldrig, TD-10)", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 400 }));
    const result = await updateFollowedCompanyNotificationConsent({
      enabled: true,
    });
    expect(result).toEqual({ kind: "error" });
  });

  it("409 (en samtidig skrivning vann alla omförsök, ADR 0146) → error, så att ett vägrat tillbakadragande syns", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("", { status: 409 }));
    const result = await updateFollowedCompanyNotificationConsent({
      enabled: false,
    });
    expect(result).toEqual({ kind: "error" });
  });

  it("network-fail → error (kastar aldrig)", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("ENETUNREACH"));
    const result = await updateFollowedCompanyNotificationConsent({
      enabled: true,
    });
    expect(result).toEqual({ kind: "error" });
  });
});
