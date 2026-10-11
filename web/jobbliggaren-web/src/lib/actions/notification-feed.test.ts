import { beforeEach, describe, expect, it, vi } from "vitest";

const { markMatchesSeen, markFollowedAdsSeen } = vi.hoisted(() => ({
  markMatchesSeen: vi.fn(),
  markFollowedAdsSeen: vi.fn(),
}));
vi.mock("@/lib/api/me-matches", () => ({ markMatchesSeen }));
vi.mock("@/lib/api/company-follows", () => ({ markFollowedAdsSeen }));
vi.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) => `t:${key}`,
}));
import { markNotificationsReadAction } from "./notification-feed";

// Microseconds, as the API writes them: a JS Date would round this down to .123 and leave the
// acknowledged row itself unread.
const MATCH_TOKEN = "2026-10-11T07:14:03.123456+00:00";
const FOLLOW_TOKEN = "2026-10-11T08:00:00.000001+00:00";

beforeEach(() => {
  markMatchesSeen.mockReset().mockResolvedValue({ kind: "ok", data: undefined });
  markFollowedAdsSeen.mockReset().mockResolvedValue({ kind: "ok", data: undefined });
});

describe("markNotificationsReadAction (#1666)", () => {
  it("sends each source's token as the exact string, one call after the other", async () => {
    const order: string[] = [];
    markMatchesSeen.mockImplementation(async () => {
      order.push("match:start");
      await Promise.resolve();
      order.push("match:end");
      return { kind: "ok", data: undefined };
    });
    markFollowedAdsSeen.mockImplementation(async () => {
      order.push("follow");
      return { kind: "ok", data: undefined };
    });

    const result = await markNotificationsReadAction({ match: MATCH_TOKEN, followedCompanyAd: FOLLOW_TOKEN });

    expect(result).toEqual({ success: true });
    expect(markMatchesSeen).toHaveBeenCalledWith(MATCH_TOKEN);
    expect(markFollowedAdsSeen).toHaveBeenCalledWith(FOLLOW_TOKEN);
    expect(order).toEqual(["match:start", "match:end", "follow"]);
  });

  // An empty /seen body means "acknowledge through now" to the backend, which would swallow rows
  // that arrived after the user read the number. A null token sends nothing.
  it("sends nothing for a source whose token is null, and never an undefined token", async () => {
    const result = await markNotificationsReadAction({ match: null, followedCompanyAd: FOLLOW_TOKEN });

    expect(result).toEqual({ success: true });
    expect(markMatchesSeen).not.toHaveBeenCalled();
    expect(markFollowedAdsSeen).toHaveBeenCalledWith(FOLLOW_TOKEN);
    for (const call of [...markMatchesSeen.mock.calls, ...markFollowedAdsSeen.mock.calls]) {
      expect(call[0]).toEqual(expect.any(String));
    }
  });

  it("sends nothing for a malformed token and reports the failure", async () => {
    const result = await markNotificationsReadAction({ match: "yesterday", followedCompanyAd: FOLLOW_TOKEN });

    expect(markMatchesSeen).not.toHaveBeenCalled();
    expect(markFollowedAdsSeen).toHaveBeenCalledWith(FOLLOW_TOKEN);
    expect(result).toEqual({ success: false, error: "t:markReadFailed" });
  });

  it("still sends the second source when the first fails, and reports the failure", async () => {
    markMatchesSeen.mockResolvedValue({ kind: "error" });

    const result = await markNotificationsReadAction({ match: MATCH_TOKEN, followedCompanyAd: FOLLOW_TOKEN });

    expect(markFollowedAdsSeen).toHaveBeenCalledWith(FOLLOW_TOKEN);
    expect(result).toEqual({ success: false, error: "t:markReadFailed" });
  });

  it("is a no-op success when nothing is unread", async () => {
    const result = await markNotificationsReadAction({ match: null, followedCompanyAd: null });

    expect(result).toEqual({ success: true });
    expect(markMatchesSeen).not.toHaveBeenCalled();
    expect(markFollowedAdsSeen).not.toHaveBeenCalled();
  });
});
