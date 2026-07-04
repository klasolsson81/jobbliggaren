import { describe, it, expect, vi, beforeEach } from "vitest";

// #541 regression guard: the open-registration form was broken because
// registerAction posted only { email, password } while the backend
// RegisterCommandValidator requires DisplayName (NotEmpty) -> every form
// registration 400'd. These tests pin that displayName reaches the payload.

const { redirectMock, setSessionCookieMock, parseResponseMock } = vi.hoisted(() => ({
  redirectMock: vi.fn((path: string) => {
    throw new Error(`REDIRECT:${path}`);
  }),
  setSessionCookieMock: vi.fn(),
  parseResponseMock: vi.fn(async () => ({ sessionId: "sess-1" })),
}));

vi.mock("next/headers", () => ({ cookies: vi.fn() }));
vi.mock("next/navigation", () => ({ redirect: redirectMock }));
vi.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) => key,
}));
vi.mock("@/lib/auth/session", () => ({
  setSessionCookie: setSessionCookieMock,
  deleteSessionCookie: vi.fn(),
}));
vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://backend.test" } }));
vi.mock("@/lib/dto/_helpers", () => ({ parseResponse: parseResponseMock }));

import { registerAction } from "./actions";

function formOf(entries: Record<string, string>): FormData {
  const f = new FormData();
  for (const [k, v] of Object.entries(entries)) f.set(k, v);
  return f;
}

describe("registerAction (#541 — DisplayName must reach the backend)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    parseResponseMock.mockResolvedValue({ sessionId: "sess-1" });
  });

  it("includes displayName in the POST /auth/register payload", async () => {
    const fetchMock = vi.fn(async (_url: string, _init: RequestInit) => ({ status: 200, ok: true }) as Response);
    vi.stubGlobal("fetch", fetchMock);

    // Success path ends in redirect(), which throws — assert it got that far.
    await expect(
      registerAction(
        null,
        formOf({ displayName: "Anna Andersson", email: "anna@example.se", password: "password1" }),
      ),
    ).rejects.toThrow(/REDIRECT/);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe("http://backend.test/api/v1/auth/register");
    const body = JSON.parse(init.body as string);
    expect(body).toMatchObject({
      displayName: "Anna Andersson",
      email: "anna@example.se",
      password: "password1",
    });
    expect(setSessionCookieMock).toHaveBeenCalledWith("sess-1");
  });

  it("blocks submit without calling fetch when displayName is missing", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const result = await registerAction(
      null,
      formOf({ email: "anna@example.se", password: "password1" }),
    );

    expect(result).toEqual({ error: "auth.actions.registrationFieldsRequired" });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
