import { describe, it, expect, vi, beforeEach } from "vitest";

// ADR 0162 (#2130): the two switches for what reaches the bell. They are delivery choices, never consents
// (security V3): each action forwards only its own `{ enabled }`, refuses a body carrying anything else
// (a Server Action is a public endpoint, and a tab on an earlier build can post a combined body), and
// touches no consent's or mail pause's write. The translator mock returns `namespace.key`.

const { matchesApiMock, followedApiMock, consentApiMock, followConsentApiMock, mailApiMock, revalidatePathMock } =
  vi.hoisted(() => ({
    matchesApiMock: vi.fn(),
    followedApiMock: vi.fn(),
    consentApiMock: vi.fn(),
    followConsentApiMock: vi.fn(),
    mailApiMock: vi.fn(),
    revalidatePathMock: vi.fn(),
  }));

vi.mock("next/headers", () => ({ cookies: vi.fn() }));
vi.mock("next/navigation", () => ({ redirect: vi.fn() }));
vi.mock("next/cache", () => ({ revalidatePath: revalidatePathMock }));
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => (key: string) => `${namespace}.${key}`,
}));
vi.mock("@/lib/auth/session", () => ({
  getSessionId: vi.fn(async () => "sess-current"),
  deleteSessionCookie: vi.fn(),
  getServerSession: vi.fn(),
  setSessionCookie: vi.fn(),
}));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: vi.fn() }));
vi.mock("@/lib/api/me", () => ({
  updateNotificationFeedMatches: matchesApiMock,
  updateNotificationFeedFollowedAds: followedApiMock,
  updateNotificationConsent: consentApiMock,
  updateFollowedCompanyNotificationConsent: followConsentApiMock,
  updateNotificationMail: mailApiMock,
  updateDigestCadence: vi.fn(),
  updateTopMatchMail: vi.fn(),
  updateMatchSummaryMail: vi.fn(),
}));

import { updateNotificationFeedFollowedAdsAction, updateNotificationFeedMatchesAction } from "./me";

type BellInput = Parameters<typeof updateNotificationFeedMatchesAction>[0];

const OTHER_WRITES = [consentApiMock, followConsentApiMock, mailApiMock];

beforeEach(() => {
  vi.clearAllMocks();
  for (const api of [matchesApiMock, followedApiMock, ...OTHER_WRITES]) {
    api.mockResolvedValue({ kind: "ok", data: undefined });
  }
});

describe.each([
  [
    "updateNotificationFeedMatchesAction",
    updateNotificationFeedMatchesAction,
    matchesApiMock,
    followedApiMock,
    "backgroundMatch",
  ],
  [
    "updateNotificationFeedFollowedAdsAction",
    updateNotificationFeedFollowedAdsAction,
    followedApiMock,
    matchesApiMock,
    "followedCompanyNotifications",
  ],
])("%s", (_name, action, api, siblingApi, copy) => {
  it.each([true, false])(
    "forwards exactly {enabled: %s} through its own write alone and revalidates the page",
    async (enabled) => {
      expect(await action({ enabled })).toEqual({ success: true });

      expect(api).toHaveBeenCalledExactlyOnceWith({ enabled });
      for (const other of [siblingApi, ...OTHER_WRITES]) expect(other).not.toHaveBeenCalled();
      expect(revalidatePathMock).toHaveBeenCalledWith("/mina-sidor/notiser");
    },
  );

  it.each([
    ["a consent", { enabled: true, backgroundMatchNotificationsEnabled: true }],
    ["the other bell switch", { enabled: true, followedAdsEnabled: false }],
    ["a mail pause", { enabled: true, mailEnabled: false }],
  ])("refuses a body that also carries %s, and sends nothing", async (_extra, body) => {
    const result = await action(body as unknown as BellInput);

    expect(result).toEqual({ success: false, error: `settings.${copy}.errors.invalidInput` });
    for (const candidate of [api, siblingApi, ...OTHER_WRITES]) expect(candidate).not.toHaveBeenCalled();
  });

  it("refuses a body without enabled, never reading it as false", async () => {
    const result = await action({} as unknown as BellInput);

    expect(result).toEqual({ success: false, error: `settings.${copy}.errors.invalidInput` });
    expect(api).not.toHaveBeenCalled();
  });

  it.each([
    ["unauthorized", { kind: "unauthorized" }, "notLoggedIn"],
    ["rateLimited", { kind: "rateLimited", retryAfterSeconds: 30 }, "tooManyAttempts"],
    ["error", { kind: "error" }, "saveFailed"],
  ])("maps %s to its copy and does not revalidate", async (_kind, apiResult, key) => {
    api.mockResolvedValue(apiResult);

    expect(await action({ enabled: false })).toEqual({ success: false, error: `settings.${copy}.errors.${key}` });
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });
});
