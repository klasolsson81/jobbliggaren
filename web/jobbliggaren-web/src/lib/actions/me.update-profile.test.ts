import { describe, it, expect, vi, beforeEach } from "vitest";

// #1117 — updateMyProfileAction's 400 handling. The personnummer refusal is an AGGREGATE
// invariant, so it can reach this action in exactly one shape: a ProblemDetails `title`. The
// client-side Zod schema knows only length and can never catch it, and mapActionError
// discriminates on status alone — so without the whitelisted arm the user is told "could not
// update the profile" for a refusal that names precisely what to change. The translator mock
// returns the key verbatim, so assertions check the resolved message key.

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
  setSessionCookie: vi.fn(),
  deleteSessionCookie: vi.fn(),
  getServerSession: vi.fn(),
}));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: authedFetchMock }));
vi.mock("@/lib/api/me", () => ({
  updateNotificationConsent: vi.fn(),
  updateFollowedCompanyNotificationConsent: vi.fn(),
}));

import { updateMyProfileAction } from "./me";

function fakeResponse(status: number, body?: unknown): Response {
  return {
    status,
    ok: status >= 200 && status < 300,
    json: async () => body,
  } as unknown as Response;
}

describe("updateMyProfileAction 400 handling", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getSessionIdMock.mockResolvedValue("sess-current");
  });

  it("maps the display-name personnummer refusal to copy that names what to change", async () => {
    authedFetchMock.mockResolvedValue(
      fakeResponse(400, { title: "JobSeeker.DisplayNamePersonnummerMustBeRemoved" }),
    );

    const result = await updateMyProfileAction({ displayName: "Anna 811218-9876", language: "sv" });

    expect(result).toEqual({
      success: false,
      error: "account.errors.displayNamePersonnummer",
      // Names the input, so the card can mark exactly that control invalid and focus it.
      field: "displayName",
    });
  });

  it("does not render an unknown ProblemDetails title — falls back to generic copy", async () => {
    authedFetchMock.mockResolvedValue(fakeResponse(400, { title: "JobSeeker.SomethingElse" }));

    const result = await updateMyProfileAction({ displayName: "Anna Andersson", language: "sv" });

    expect(result).toEqual({ success: false, error: "account.errors.invalidInput" });
  });

  it("falls back to generic copy when the 400 body is not JSON at all", async () => {
    // readProblemTitle resolves a non-JSON body to null rather than throwing, so a reverse
    // proxy's own 400 must not take the whitelisted arm.
    authedFetchMock.mockResolvedValue({
      status: 400,
      ok: false,
      json: async () => {
        throw new Error("not json");
      },
    } as unknown as Response);

    const result = await updateMyProfileAction({ displayName: "Anna Andersson", language: "sv" });

    expect(result).toEqual({ success: false, error: "account.errors.invalidInput" });
  });

  it("does NOT name a field on a non-field failure", async () => {
    // The discriminator is only meaningful if its absence is pinned too: without this, stamping
    // every failure would pass the positive test above while marking the name input invalid for
    // a network fault the user cannot fix by editing it.
    authedFetchMock.mockRejectedValue(new Error("network down"));

    const result = await updateMyProfileAction({
      displayName: "Anna Andersson",
      language: "sv",
    });

    expect(result).toEqual({ success: false, error: "account.errors.network" });
    expect(result).not.toHaveProperty("field");
  });

  it("does NOT name a field for an unknown 400 title", async () => {
    authedFetchMock.mockResolvedValue(fakeResponse(400, { title: "JobSeeker.SomethingElse" }));

    const result = await updateMyProfileAction({
      displayName: "Anna Andersson",
      language: "sv",
    });

    expect(result).not.toHaveProperty("field");
  });

  it("passes a successful update through unchanged", async () => {
    authedFetchMock.mockResolvedValue(fakeResponse(200));

    const result = await updateMyProfileAction({ displayName: "Anna Andersson", language: "sv" });

    expect(result).toEqual({ success: true });
  });
});
