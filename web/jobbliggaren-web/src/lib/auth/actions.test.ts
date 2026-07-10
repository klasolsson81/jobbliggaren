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
  // Promise<unknown>: the mock stands in for BOTH parseResponse call sites — the 400 error
  // body ({ title } / { errors }) and the success body ({ sessionId }).
  parseResponseMock: vi.fn(async (): Promise<unknown> => ({ sessionId: "sess-1" })),
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
      // PR2b-3b: no rememberMe in the form → a session-scoped session (false).
      rememberMe: false,
    });
    // Second arg is the persistent flag (false when the box is unticked).
    expect(setSessionCookieMock).toHaveBeenCalledWith("sess-1", false);
  });

  it("threads rememberMe=true through the payload and cookie flag when the box is ticked", async () => {
    const fetchMock = vi.fn(async (_url: string, _init: RequestInit) => ({ status: 200, ok: true }) as Response);
    vi.stubGlobal("fetch", fetchMock);

    await expect(
      registerAction(
        null,
        formOf({
          displayName: "Anna Andersson",
          email: "anna@example.se",
          password: "password1",
          // A checked native checkbox posts the literal "on".
          rememberMe: "on",
        }),
      ),
    ).rejects.toThrow(/REDIRECT/);

    const [, init] = fetchMock.mock.calls[0]!;
    const body = JSON.parse(init.body as string);
    expect(body.rememberMe).toBe(true);
    expect(setSessionCookieMock).toHaveBeenCalledWith("sess-1", true);
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

describe("registerAction 400 handling (#616 — breached password reaches the user)", () => {
  const form = () =>
    formOf({ displayName: "Anna Andersson", email: "anna@example.se", password: "password1" });

  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({ status: 400, ok: false }) as Response),
    );
  });

  it("maps the Auth.PwnedPassword ProblemDetails title to the localized breach copy", async () => {
    parseResponseMock.mockResolvedValue({ title: "Auth.PwnedPassword" });

    const result = await registerAction(null, form());

    expect(result).toEqual({ error: "auth.actions.passwordBreached" });
  });

  it("still surfaces the first field error from the errors-dictionary shape", async () => {
    parseResponseMock.mockResolvedValue({
      errors: { Password: ["Fältfel från validatorn."] },
    });

    const result = await registerAction(null, form());

    expect(result).toEqual({ error: "Fältfel från validatorn." });
  });

  it("does not render an unknown ProblemDetails title — falls back to generic copy", async () => {
    parseResponseMock.mockResolvedValue({ title: "Auth.SomethingElse" });

    const result = await registerAction(null, form());

    expect(result).toEqual({ error: "auth.actions.registrationFailed" });
  });
});
