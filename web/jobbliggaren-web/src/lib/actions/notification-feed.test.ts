import { beforeEach, describe, expect, it, vi } from "vitest";

const { markNotificationRead, markNotificationsReadThrough, revalidatePath } = vi.hoisted(() => ({
  markNotificationRead: vi.fn(),
  markNotificationsReadThrough: vi.fn(),
  revalidatePath: vi.fn(),
}));
vi.mock("@/lib/api/notification-feed", () => ({ markNotificationRead, markNotificationsReadThrough }));
vi.mock("next/cache", () => ({ revalidatePath, revalidateTag: vi.fn(), updateTag: vi.fn() }));
vi.mock("next/headers", () => ({ cookies: vi.fn() }));
vi.mock("next/navigation", () => ({ redirect: vi.fn() }));
vi.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) => `t:${key}`,
}));
import { markAllNotificationsReadAction, markNotificationReadAction } from "./notification-feed";

// Microseconds, as the API writes them: a JS Date would round this down to .123 and leave the
// acknowledged row itself unread.
const MATCH_TOKEN = "2026-10-11T07:14:03.123456+00:00";
const FOLLOW_TOKEN = "2026-10-11T08:00:00.000001+00:00";
const AD_ID = "7b0a1c52-4a5e-4d0e-9e0a-0b5d9e2f5a11";

beforeEach(() => {
  vi.clearAllMocks();
  markNotificationRead.mockResolvedValue({ kind: "ok", data: undefined });
  markNotificationsReadThrough.mockResolvedValue({ kind: "ok", data: undefined });
});

describe("markNotificationReadAction (#2130)", () => {
  it("marks exactly the named row read, through its own kind", async () => {
    const result = await markNotificationReadAction({ kind: "FollowedCompanyAd", jobAdId: AD_ID });

    expect(result).toEqual({ success: true });
    expect(markNotificationRead).toHaveBeenCalledExactlyOnceWith("FollowedCompanyAd", AD_ID);
    expect(markNotificationsReadThrough).not.toHaveBeenCalled();
  });

  // A Server Action is a public endpoint: any signed-in client can post it any argument.
  it.each([
    ["an unknown kind", { kind: "Application", jobAdId: AD_ID }],
    ["no kind", { jobAdId: AD_ID }],
    ["a job ad id that is not a UUID", { kind: "Match", jobAdId: "../me/profile" }],
    ["no job ad id", { kind: "Match" }],
    ["another field", { kind: "Match", jobAdId: AD_ID, userId: "someone-else" }],
  ])("sends nothing for %s", async (_name, input) => {
    const result = await markNotificationReadAction(input as never);

    expect(result).toEqual({ success: false, error: "t:markReadFailed" });
    expect(markNotificationRead).not.toHaveBeenCalled();
  });

  it.each([["error"], ["unauthorized"], ["forbidden"], ["rateLimited"]])(
    "reports a refusal (%s) as a failure",
    async (kind) => {
      markNotificationRead.mockResolvedValue({ kind, retryAfterSeconds: 30 });

      const result = await markNotificationReadAction({ kind: "Match", jobAdId: AD_ID });

      expect(result).toEqual({ success: false, error: "t:markReadFailed" });
    },
  );

  // The write is fired without waiting on the click that opens the ad. An action that re-rendered the route
  // (revalidatePath, refresh, a cookie, a redirect) would re-render a job modal the click had just opened.
  it("never asks Next to re-render anything", async () => {
    await markNotificationReadAction({ kind: "Match", jobAdId: AD_ID });

    expect(revalidatePath).not.toHaveBeenCalled();
  });
});

describe("markAllNotificationsReadAction (#2130)", () => {
  it("sends each source's token as the exact string, one call after the other", async () => {
    const order: string[] = [];
    markNotificationsReadThrough.mockImplementation(async (kind: string) => {
      order.push(`${kind}:start`);
      await Promise.resolve();
      order.push(`${kind}:end`);
      return { kind: "ok", data: undefined };
    });

    const result = await markAllNotificationsReadAction({ match: MATCH_TOKEN, followedCompanyAd: FOLLOW_TOKEN });

    expect(result).toEqual({ success: true });
    expect(markNotificationsReadThrough).toHaveBeenNthCalledWith(1, "Match", MATCH_TOKEN);
    expect(markNotificationsReadThrough).toHaveBeenNthCalledWith(2, "FollowedCompanyAd", FOLLOW_TOKEN);
    expect(order).toEqual(["Match:start", "Match:end", "FollowedCompanyAd:start", "FollowedCompanyAd:end"]);
    expect(markNotificationRead).not.toHaveBeenCalled();
  });

  // The API reads a missing token as a malformed request, and the old `/seen` endpoints read an empty
  // body as "acknowledge through now": a null token sends nothing for that source, never an undefined one.
  it("sends nothing for a source whose token is null, and never an undefined token", async () => {
    const result = await markAllNotificationsReadAction({ match: null, followedCompanyAd: FOLLOW_TOKEN });

    expect(result).toEqual({ success: true });
    expect(markNotificationsReadThrough).toHaveBeenCalledExactlyOnceWith("FollowedCompanyAd", FOLLOW_TOKEN);
    for (const call of markNotificationsReadThrough.mock.calls) {
      expect(call[1]).toEqual(expect.any(String));
    }
  });

  it("sends nothing for a malformed token and reports the failure", async () => {
    const result = await markAllNotificationsReadAction({ match: "yesterday", followedCompanyAd: FOLLOW_TOKEN });

    expect(markNotificationsReadThrough).toHaveBeenCalledExactlyOnceWith("FollowedCompanyAd", FOLLOW_TOKEN);
    expect(result).toEqual({ success: false, error: "t:markReadFailed" });
  });

  it("still sends the second source when the first fails, and reports the failure", async () => {
    markNotificationsReadThrough.mockResolvedValueOnce({ kind: "error" });

    const result = await markAllNotificationsReadAction({ match: MATCH_TOKEN, followedCompanyAd: FOLLOW_TOKEN });

    expect(markNotificationsReadThrough).toHaveBeenCalledTimes(2);
    expect(result).toEqual({ success: false, error: "t:markReadFailed" });
  });

  it("is a no-op success when nothing is unread", async () => {
    const result = await markAllNotificationsReadAction({ match: null, followedCompanyAd: null });

    expect(result).toEqual({ success: true });
    expect(markNotificationsReadThrough).not.toHaveBeenCalled();
  });

  it("never asks Next to re-render anything", async () => {
    await markAllNotificationsReadAction({ match: MATCH_TOKEN, followedCompanyAd: FOLLOW_TOKEN });

    expect(revalidatePath).not.toHaveBeenCalled();
  });
});
