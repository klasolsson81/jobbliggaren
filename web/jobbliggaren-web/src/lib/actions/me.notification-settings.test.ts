import { describe, it, expect, vi, beforeEach } from "vitest";

// The notification-settings actions behind the Notiser card. A Server Action is a public endpoint:
// any signed-in client can post it any argument, including a tab on an earlier build that still
// sends the combined `{ enabled, cadence }` body. Each action forwards only its own named field, and
// refuses a body carrying another's. The translator mock returns `namespace.key`, so assertions check
// the resolved message key.

const {
  consentApiMock,
  followConsentApiMock,
  cadenceApiMock,
  mailApiMock,
  topMatchApiMock,
  summaryApiMock,
  revalidatePathMock,
} = vi.hoisted(() => ({
  consentApiMock: vi.fn(),
  followConsentApiMock: vi.fn(),
  cadenceApiMock: vi.fn(),
  mailApiMock: vi.fn(),
  topMatchApiMock: vi.fn(),
  summaryApiMock: vi.fn(),
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
  updateNotificationConsent: consentApiMock,
  updateFollowedCompanyNotificationConsent: followConsentApiMock,
  updateDigestCadence: cadenceApiMock,
  updateNotificationMail: mailApiMock,
  updateTopMatchMail: topMatchApiMock,
  updateMatchSummaryMail: summaryApiMock,
}));

import {
  updateDigestCadenceAction,
  updateFollowedCompanyNotificationConsentAction,
  updateMatchSummaryMailAction,
  updateNotificationConsentAction,
  updateNotificationMailAction,
  updateTopMatchMailAction,
} from "./me";

type ConsentInput = Parameters<typeof updateNotificationConsentAction>[0];
type FollowInput = Parameters<typeof updateFollowedCompanyNotificationConsentAction>[0];
type CadenceInput = Parameters<typeof updateDigestCadenceAction>[0];
type MailSwitchInput = Parameters<typeof updateNotificationMailAction>[0];

const ALL_APIS = [
  consentApiMock,
  followConsentApiMock,
  cadenceApiMock,
  mailApiMock,
  topMatchApiMock,
  summaryApiMock,
];

beforeEach(() => {
  vi.clearAllMocks();
  for (const api of ALL_APIS) {
    api.mockResolvedValue({ kind: "ok", data: undefined });
  }
});

// ADR 0164 (#2107): the e-mail switches. None of them reaches a consent's or the cadence's write
// (security V3), and their failures read as the background-match copy, which names no purpose.
describe.each([
  ["updateNotificationMailAction", updateNotificationMailAction, mailApiMock],
  ["updateTopMatchMailAction", updateTopMatchMailAction, topMatchApiMock],
  ["updateMatchSummaryMailAction", updateMatchSummaryMailAction, summaryApiMock],
])("%s", (_name, action, api) => {
  it.each([true, false])(
    "forwards exactly {enabled: %s} through its own write alone and revalidates the page",
    async (enabled) => {
      expect(await action({ enabled })).toEqual({ success: true });

      expect(api).toHaveBeenCalledTimes(1);
      expect(api).toHaveBeenCalledWith({ enabled });
      for (const other of ALL_APIS.filter((candidate) => candidate !== api)) {
        expect(other).not.toHaveBeenCalled();
      }
      expect(revalidatePathMock).toHaveBeenCalledWith("/mina-sidor/notiser");
    },
  );

  it.each([
    ["a cadence", { enabled: true, cadence: "Daily" }],
    ["a consent", { enabled: true, backgroundMatchNotificationsEnabled: true }],
  ])("refuses a body that also carries %s, and sends nothing", async (_extra, body) => {
    const result = await action(body as unknown as MailSwitchInput);

    expect(result).toEqual({
      success: false,
      error: "settings.backgroundMatch.errors.invalidInput",
    });
    for (const candidate of ALL_APIS) expect(candidate).not.toHaveBeenCalled();
  });

  it.each([
    ["unauthorized", { kind: "unauthorized" }, "settings.backgroundMatch.errors.notLoggedIn"],
    ["rateLimited", { kind: "rateLimited", retryAfterSeconds: 30 }, "settings.backgroundMatch.errors.tooManyAttempts"],
    ["forbidden", { kind: "forbidden" }, "settings.backgroundMatch.errors.saveFailed"],
    ["error", { kind: "error" }, "settings.backgroundMatch.errors.saveFailed"],
  ])("maps %s to its copy and does not revalidate", async (_kind, apiResult, error) => {
    api.mockResolvedValue(apiResult);

    expect(await action({ enabled: false })).toEqual({ success: false, error });
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });
});

describe("updateNotificationConsentAction", () => {
  it("forwards exactly {enabled} and revalidates the page", async () => {
    const result = await updateNotificationConsentAction({ enabled: false });

    expect(result).toEqual({ success: true });
    expect(consentApiMock).toHaveBeenCalledWith({ enabled: false });
    expect(cadenceApiMock).not.toHaveBeenCalled();
    expect(revalidatePathMock).toHaveBeenCalledWith("/mina-sidor/notiser");
  });

  it("refuses the combined body of an earlier build, in its own copy, and sends nothing", async () => {
    const result = await updateNotificationConsentAction({
      enabled: true,
      cadence: "Daily",
    } as unknown as ConsentInput);

    expect(result).toEqual({
      success: false,
      error: "settings.backgroundMatch.errors.invalidInput",
    });
    expect(consentApiMock).not.toHaveBeenCalled();
    expect(cadenceApiMock).not.toHaveBeenCalled();
  });

  it.each([
    ["unauthorized", { kind: "unauthorized" }, "settings.backgroundMatch.errors.notLoggedIn"],
    ["rateLimited", { kind: "rateLimited", retryAfterSeconds: 30 }, "settings.backgroundMatch.errors.tooManyAttempts"],
    ["error", { kind: "error" }, "settings.backgroundMatch.errors.saveFailed"],
  ])("maps %s to its copy and does not revalidate", async (_kind, apiResult, error) => {
    consentApiMock.mockResolvedValue(apiResult);

    expect(await updateNotificationConsentAction({ enabled: true })).toEqual({
      success: false,
      error,
    });
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });
});

describe("updateFollowedCompanyNotificationConsentAction", () => {
  it("forwards exactly {enabled}", async () => {
    expect(await updateFollowedCompanyNotificationConsentAction({ enabled: true })).toEqual({
      success: true,
    });
    expect(followConsentApiMock).toHaveBeenCalledWith({ enabled: true });
  });

  it("refuses a body carrying a cadence, in its own copy, and sends nothing", async () => {
    const result = await updateFollowedCompanyNotificationConsentAction({
      enabled: true,
      cadence: "Daily",
    } as unknown as FollowInput);

    expect(result).toEqual({
      success: false,
      error: "settings.followedCompanyNotifications.errors.invalidInput",
    });
    expect(followConsentApiMock).not.toHaveBeenCalled();
  });
});

describe("updateDigestCadenceAction", () => {
  it("forwards exactly {cadence}, never a consent, and revalidates the page", async () => {
    const result = await updateDigestCadenceAction({ cadence: "Daily" });

    expect(result).toEqual({ success: true });
    expect(cadenceApiMock).toHaveBeenCalledWith({ cadence: "Daily" });
    expect(consentApiMock).not.toHaveBeenCalled();
    expect(followConsentApiMock).not.toHaveBeenCalled();
    expect(revalidatePathMock).toHaveBeenCalledWith("/mina-sidor/notiser");
  });

  it("refuses a body carrying a consent value and sends nothing", async () => {
    const result = await updateDigestCadenceAction({
      cadence: "Daily",
      enabled: true,
    } as unknown as CadenceInput);

    expect(result).toEqual({
      success: false,
      error: "settings.backgroundMatch.errors.invalidInput",
    });
    expect(cadenceApiMock).not.toHaveBeenCalled();
    expect(consentApiMock).not.toHaveBeenCalled();
  });

  it("maps a failed save to the settings copy", async () => {
    cadenceApiMock.mockResolvedValue({ kind: "error" });

    expect(await updateDigestCadenceAction({ cadence: "Weekly" })).toEqual({
      success: false,
      error: "settings.backgroundMatch.errors.saveFailed",
    });
  });
});
