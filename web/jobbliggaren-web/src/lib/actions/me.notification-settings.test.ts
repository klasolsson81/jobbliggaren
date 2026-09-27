import { describe, it, expect, vi, beforeEach } from "vitest";

// The three notification-settings actions behind the Notiser card. A Server Action is a public
// endpoint: any signed-in client can post it any argument, including a tab on an earlier build that
// still sends the combined `{ enabled, cadence }` body. Each action forwards only its own named field,
// and refuses a body carrying another's. The translator mock returns `namespace.key`, so assertions
// check the resolved message key.

const { consentApiMock, followConsentApiMock, cadenceApiMock, revalidatePathMock } = vi.hoisted(
  () => ({
    consentApiMock: vi.fn(),
    followConsentApiMock: vi.fn(),
    cadenceApiMock: vi.fn(),
    revalidatePathMock: vi.fn(),
  }),
);

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
}));

import {
  updateDigestCadenceAction,
  updateFollowedCompanyNotificationConsentAction,
  updateNotificationConsentAction,
} from "./me";

type ConsentInput = Parameters<typeof updateNotificationConsentAction>[0];
type FollowInput = Parameters<typeof updateFollowedCompanyNotificationConsentAction>[0];
type CadenceInput = Parameters<typeof updateDigestCadenceAction>[0];

beforeEach(() => {
  vi.clearAllMocks();
  for (const api of [consentApiMock, followConsentApiMock, cadenceApiMock]) {
    api.mockResolvedValue({ kind: "ok", data: undefined });
  }
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
