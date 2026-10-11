import { beforeEach, describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

const { getSessionId, authedFetch, markMatchesSeen, markFollowedAdsSeen } = vi.hoisted(() => ({
  getSessionId: vi.fn(),
  authedFetch: vi.fn(),
  markMatchesSeen: vi.fn(),
  markFollowedAdsSeen: vi.fn(),
}));
vi.mock("@/lib/auth/session", () => ({ getSessionId }));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch }));
vi.mock("@/lib/api/me-matches", () => ({ markMatchesSeen }));
vi.mock("@/lib/api/company-follows", () => ({ markFollowedAdsSeen }));
import { GET } from "./route";

const AD = "00000000-0000-4000-8000-000000000001";
const FEED = {
  items: [
    {
      kind: "Match",
      jobAdId: AD,
      title: "Utvecklare",
      companyName: "Bolag AB",
      occurredAt: "2026-10-11T07:14:03.123456+00:00",
      isUnread: true,
      grade: "Top",
    },
  ],
  sources: [
    { kind: "Match", unreadCount: 1, acknowledgedThrough: "2026-10-11T07:14:03.123456+00:00" },
    { kind: "FollowedCompanyAd", unreadCount: 0, acknowledgedThrough: null },
  ],
};

function request() {
  return new NextRequest("http://localhost/api/me/notification-feed?userId=someone-else");
}

beforeEach(() => {
  getSessionId.mockReset().mockResolvedValue("session");
  authedFetch.mockReset();
  markMatchesSeen.mockReset();
  markFollowedAdsSeen.mockReset();
});

describe("GET /api/me/notification-feed (#1666)", () => {
  it("returns the feed, keeps the token string exact, and forwards cancellation", async () => {
    authedFetch.mockResolvedValue(Response.json(FEED));
    const incoming = request();

    const response = await GET(incoming);

    expect(response.status).toBe(200);
    expect(response.headers.get("cache-control")).toBe("private, no-store");
    expect(await response.json()).toEqual(FEED);
    expect(authedFetch).toHaveBeenCalledWith("session", "/api/v1/me/notification-feed", { signal: incoming.signal });
  });

  it("passes only the fields it names, whatever the backend adds", async () => {
    authedFetch.mockResolvedValue(
      Response.json({
        ...FEED,
        debug: "internal",
        items: [{ ...FEED.items[0], url: "javascript:alert(1)", organizationNumber: "5560000000" }],
      }),
    );

    const body = await (await GET(request())).json();

    expect(body).not.toHaveProperty("debug");
    expect(body.items[0]).not.toHaveProperty("url");
    expect(body.items[0]).not.toHaveProperty("organizationNumber");
  });

  it.each([
    [401, 401],
    [403, 403],
    [404, 502],
    [500, 502],
  ])("maps backend %i to %i with an empty, uncached body", async (backend, expected) => {
    authedFetch.mockResolvedValue(new Response("private backend detail", { status: backend }));

    const response = await GET(request());

    expect(response.status).toBe(expected);
    expect(response.headers.get("cache-control")).toBe("private, no-store");
    expect(await response.text()).toBe("");
  });

  it("carries Retry-After on 429", async () => {
    authedFetch.mockResolvedValue(new Response(null, { status: 429, headers: { "Retry-After": "17" } }));

    const response = await GET(request());

    expect(response.status).toBe(429);
    expect(response.headers.get("retry-after")).toBe("17");
    expect(response.headers.get("cache-control")).toBe("private, no-store");
  });

  it("is 401 without a session and never reaches the backend", async () => {
    getSessionId.mockResolvedValue(null);

    const response = await GET(request());

    expect(response.status).toBe(401);
    expect(authedFetch).not.toHaveBeenCalled();
  });

  it("answers a body that does not parse with 502, never a 200 with an empty feed", async () => {
    authedFetch.mockResolvedValue(Response.json({ items: "nope", sources: [] }));

    expect((await GET(request())).status).toBe(502);
  });

  // ADR 0018 amendment 2026-10-10: a cross-site GET arrives signed in, so a GET must never write.
  it("never acknowledges anything", async () => {
    authedFetch.mockResolvedValue(Response.json(FEED));

    await GET(request());

    expect(markMatchesSeen).not.toHaveBeenCalled();
    expect(markFollowedAdsSeen).not.toHaveBeenCalled();
    expect(authedFetch).toHaveBeenCalledTimes(1);
    expect(authedFetch.mock.calls[0]?.[2]).not.toHaveProperty("method");
  });
});
