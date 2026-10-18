import { beforeEach, describe, expect, it, vi } from "vitest";

const { authedFetch, getSessionId } = vi.hoisted(() => ({
  authedFetch: vi.fn(),
  getSessionId: vi.fn(),
}));
vi.mock("server-only", () => ({}));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch }));
vi.mock("@/lib/auth/session", () => ({ getSessionId }));

import { dismissNotification, markNotificationRead, markNotificationsReadThrough } from "./notification-feed";

const AD_ID = "7b0a1c52-4a5e-4d0e-9e0a-0b5d9e2f5a11";
const TOKEN = "2026-10-11T07:14:03.123456+00:00";

beforeEach(() => {
  vi.clearAllMocks();
  getSessionId.mockResolvedValue("sess-1");
  authedFetch.mockResolvedValue(new Response(null, { status: 204 }));
});

describe("markNotificationRead (#2130)", () => {
  it.each([
    ["Match", "matches"],
    ["FollowedCompanyAd", "followed-company-ads"],
    ["CompanyWatchCriterionAd", "company-watch-criterion-ads"],
  ] as const)("posts the %s row to its own source segment, with no body", async (kind, segment) => {
    const result = await markNotificationRead(kind, AD_ID);

    expect(result).toEqual({ kind: "ok", data: undefined });
    expect(authedFetch).toHaveBeenCalledExactlyOnceWith(
      "sess-1",
      `/api/v1/me/notification-feed/${segment}/${AD_ID}/read`,
      { method: "POST" },
    );
  });

  // A Server Action is a public endpoint: the argument must not decide where the request goes.
  it.each([
    ["a path-traversing id", "Match", "../../me/profile"],
    ["an id with a query", "Match", `${AD_ID}?x=1`],
    ["an empty id", "Match", ""],
    ["a source that is not a source", "Application", AD_ID],
  ])("never reaches the network for %s", async (_name, kind, jobAdId) => {
    const result = await markNotificationRead(kind as never, jobAdId);

    expect(result).toEqual({ kind: "error" });
    expect(authedFetch).not.toHaveBeenCalled();
  });

  it("is unauthorized without a session, and sends nothing", async () => {
    getSessionId.mockResolvedValue(null);

    expect(await markNotificationRead("Match", AD_ID)).toEqual({ kind: "unauthorized" });
    expect(authedFetch).not.toHaveBeenCalled();
  });

  it.each([
    [401, { kind: "unauthorized" }],
    [403, { kind: "forbidden" }],
    [500, { kind: "error" }],
    [200, { kind: "error" }],
  ])("maps a %s answer to %j, because only a 204 is a mark", async (status, expected) => {
    authedFetch.mockResolvedValue(new Response(null, { status }));

    expect(await markNotificationRead("Match", AD_ID)).toEqual(expected);
  });

  it("maps a 429 to rateLimited with the Retry-After seconds", async () => {
    authedFetch.mockResolvedValue(new Response(null, { status: 429, headers: { "Retry-After": "12" } }));

    expect(await markNotificationRead("Match", AD_ID)).toEqual({ kind: "rateLimited", retryAfterSeconds: 12 });
  });

  it("maps a thrown request to an error", async () => {
    authedFetch.mockRejectedValue(new Error("connection reset"));

    expect(await markNotificationRead("Match", AD_ID)).toEqual({ kind: "error" });
  });
});

describe("dismissNotification (#2151)", () => {
  it.each([
    ["Match", "matches"],
    ["FollowedCompanyAd", "followed-company-ads"],
    ["CompanyWatchCriterionAd", "company-watch-criterion-ads"],
  ] as const)("posts the %s row to its own source segment's dismiss route, with no body", async (kind, segment) => {
    const result = await dismissNotification(kind, AD_ID);

    expect(result).toEqual({ kind: "ok", data: undefined });
    expect(authedFetch).toHaveBeenCalledExactlyOnceWith(
      "sess-1",
      `/api/v1/me/notification-feed/${segment}/${AD_ID}/dismiss`,
      { method: "POST" },
    );
  });

  // A Server Action is a public endpoint: the argument must not decide where the request goes.
  it.each([
    ["a path-traversing id", "Match", "../../me/profile"],
    ["an id with a query", "Match", `${AD_ID}?x=1`],
    ["an empty id", "Match", ""],
    ["a source that is not a source", "Application", AD_ID],
  ])("never reaches the network for %s", async (_name, kind, jobAdId) => {
    expect(await dismissNotification(kind as never, jobAdId)).toEqual({ kind: "error" });
    expect(authedFetch).not.toHaveBeenCalled();
  });

  it("is unauthorized without a session, and sends nothing", async () => {
    getSessionId.mockResolvedValue(null);

    expect(await dismissNotification("Match", AD_ID)).toEqual({ kind: "unauthorized" });
    expect(authedFetch).not.toHaveBeenCalled();
  });

  it.each([
    [401, { kind: "unauthorized" }],
    [403, { kind: "forbidden" }],
    [500, { kind: "error" }],
    [200, { kind: "error" }],
  ])("maps a %s answer to %j, because only a 204 is a dismissal", async (status, expected) => {
    authedFetch.mockResolvedValue(new Response(null, { status }));

    expect(await dismissNotification("Match", AD_ID)).toEqual(expected);
  });

  it("maps a 429 to rateLimited and a thrown request to an error", async () => {
    authedFetch.mockResolvedValueOnce(new Response(null, { status: 429, headers: { "Retry-After": "12" } }));
    expect(await dismissNotification("Match", AD_ID)).toEqual({ kind: "rateLimited", retryAfterSeconds: 12 });

    authedFetch.mockRejectedValueOnce(new Error("connection reset"));
    expect(await dismissNotification("Match", AD_ID)).toEqual({ kind: "error" });
  });
});

describe("markNotificationsReadThrough (#2130)", () => {
  it("posts the token as the exact string, in a body of its own", async () => {
    const result = await markNotificationsReadThrough("FollowedCompanyAd", TOKEN);

    expect(result).toEqual({ kind: "ok", data: undefined });
    expect(authedFetch).toHaveBeenCalledExactlyOnceWith(
      "sess-1",
      "/api/v1/me/notification-feed/followed-company-ads/read",
      { method: "POST", body: JSON.stringify({ throughToken: TOKEN }) },
    );
  });

  it("posts a Branschbevakning token to its own source segment (ADR 0175)", async () => {
    await markNotificationsReadThrough("CompanyWatchCriterionAd", TOKEN);

    expect(authedFetch).toHaveBeenCalledExactlyOnceWith(
      "sess-1",
      "/api/v1/me/notification-feed/company-watch-criterion-ads/read",
      { method: "POST", body: JSON.stringify({ throughToken: TOKEN }) },
    );
  });

  it("never reaches the network for a source that is not a source", async () => {
    expect(await markNotificationsReadThrough("Application" as never, TOKEN)).toEqual({ kind: "error" });
    expect(authedFetch).not.toHaveBeenCalled();
  });
});
