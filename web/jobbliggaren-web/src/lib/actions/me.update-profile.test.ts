import { describe, it, expect, vi, beforeEach } from "vitest";

// updateMyProfileAction — the language write behind the Visning card. A Server Action is a public
// endpoint: any signed-in client can post it any argument, so the tests below call it the way such
// a client can, not only the way the card does. The translator mock returns the key verbatim, so
// assertions check the resolved message key.

const { getSessionIdMock, authedFetchMock } = vi.hoisted(() => ({
  getSessionIdMock: vi.fn(async () => "sess-current" as string | null),
  authedFetchMock: vi.fn(),
}));

vi.mock("next/headers", () => ({ cookies: vi.fn() }));
vi.mock("next/navigation", () => ({ redirect: vi.fn() }));
vi.mock("next/cache", () => ({ revalidatePath: vi.fn() }));
vi.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) => key,
}));
vi.mock("@/lib/auth/session", () => ({
  getSessionId: getSessionIdMock,
  deleteSessionCookie: vi.fn(),
  getServerSession: vi.fn(),
}));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: authedFetchMock }));
vi.mock("@/lib/api/me", () => ({
  updateNotificationConsent: vi.fn(),
  updateFollowedCompanyNotificationConsent: vi.fn(),
}));

import { updateMyProfileAction } from "./me";

type ProfileInput = Parameters<typeof updateMyProfileAction>[0];

function fakeResponse(status: number, body?: unknown) {
  return {
    status,
    ok: status >= 200 && status < 300,
    json: vi.fn(async () => body),
  };
}

describe("updateMyProfileAction", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getSessionIdMock.mockResolvedValue("sess-current");
  });

  it("sends the language and never a display name", async () => {
    // No page writes the account name since #1740, so neither does the action behind the page: a
    // name posted anyway is stripped by the schema, not forwarded to PATCH /me/profile.
    authedFetchMock.mockResolvedValue(fakeResponse(200));

    const result = await updateMyProfileAction({
      language: "en",
      displayName: "Anna Andersson",
    } as unknown as ProfileInput);

    expect(result).toEqual({ success: true });
    expect(authedFetchMock).toHaveBeenCalledTimes(1);
    const [, path, init] = authedFetchMock.mock.calls[0]! as [string, string, RequestInit];
    expect(path).toBe("/api/v1/me/profile");
    expect(JSON.parse(init.body as string)).toEqual({ language: "en" });
  });

  it("refuses a payload without a language before anything is sent", async () => {
    const result = await updateMyProfileAction({} as unknown as ProfileInput);

    expect(result).toEqual({ success: false, error: "profile.languageInvalid" });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it("answers a failed write with the generic copy and never reads the body", async () => {
    // The 503 the API writes when the session store is down (Program.cs, StoreUnavailableException).
    // Every authenticated route can answer it, and its body is backend text, which never reaches
    // the UI (mapActionError, TD-10).
    const res = fakeResponse(503, {
      error: "Tjänsten är inte tillgänglig just nu. Försök igen om en stund.",
    });
    authedFetchMock.mockResolvedValue(res);

    const result = await updateMyProfileAction({ language: "sv" });

    expect(result).toEqual({ success: false, error: "account.errors.updateFailed" });
    expect(res.json).not.toHaveBeenCalled();
  });
});
