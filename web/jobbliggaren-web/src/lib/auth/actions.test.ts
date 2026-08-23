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

import { loginAction, registerAction } from "./actions";

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
        formOf({
          displayName: "Anna Andersson",
          email: "anna@example.se",
          password: "password1",
          acceptTerms: "on",
        }),
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
          acceptTerms: "on",
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

  // #1479 — the acceptance is enforced here and not only by the checkbox's `required`, because
  // the Server Action is reachable by a POST that never rendered the form. The assertion that
  // carries the point is `fetchMock`: no account may be created without the acceptance.
  it("refuses without calling fetch when the terms box is absent", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const result = await registerAction(
      null,
      formOf({
        displayName: "Anna Andersson",
        email: "anna@example.se",
        password: "password1",
      }),
    );

    expect(result).toEqual({
      error: "auth.actions.termsRequired",
      field: "acceptTerms",
    });
    expect(fetchMock).not.toHaveBeenCalled();
    expect(setSessionCookieMock).not.toHaveBeenCalled();
    expect(redirectMock).not.toHaveBeenCalled();
  });

  it("refuses a value the checkbox cannot post, rather than treating it as acceptance", async () => {
    // A native checkbox posts exactly "on" or nothing. Anything else reached this action by a
    // route that bypassed the form, and "present" must not be read as "accepted".
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const result = await registerAction(
      null,
      formOf({
        displayName: "Anna Andersson",
        email: "anna@example.se",
        password: "password1",
        acceptTerms: "false",
      }),
    );

    expect(result).toEqual({
      error: "auth.actions.termsRequired",
      field: "acceptTerms",
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("registerAction 400 handling (#616 breached password, #1117 display-name refusal)", () => {
  const form = () =>
    formOf({
      displayName: "Anna Andersson",
      email: "anna@example.se",
      password: "password1",
      acceptTerms: "on",
    });

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

  it("maps the #1117 display-name refusal to copy that names what to change", async () => {
    // The aggregate invariant answers with a ProblemDetails title, NOT the FluentValidation
    // errors-dictionary, so without its own arm this refusal reads as "registration failed"
    // and the user is told nothing about the name they typed.
    parseResponseMock.mockResolvedValue({
      title: "JobSeeker.DisplayNamePersonnummerMustBeRemoved",
    });

    const result = await registerAction(null, form());

    expect(result).toEqual({
      error: "auth.actions.displayNamePersonnummer",
      // Names the input so RegisterForm can mark it invalid and move focus there.
      field: "displayName",
    });
  });

  it("does NOT name a field for an unknown ProblemDetails title", async () => {
    // The absence is the half that matters: stamping every failure would mark the name input
    // invalid for causes the user cannot fix by editing the name.
    parseResponseMock.mockResolvedValue({ title: "Auth.SomethingElse" });

    const result = await registerAction(null, form());

    expect(result).not.toHaveProperty("field");
  });
});

describe("registerAction 503 handling (ADR 0083 Amendment 2026-08-03 — registration gate)", () => {
  const form = () =>
    formOf({
      displayName: "Anna Andersson",
      email: "anna@example.se",
      password: "password1",
      acceptTerms: "on",
    });

  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({ status: 503, ok: false }) as Response),
    );
  });

  it("returns registrationsClosed for OUR 503, without cookie or redirect", async () => {
    // Its own state, not `error`: RegisterForm renders a role="status" panel in place of the form
    // rather than red assertive text above a live submit button.
    parseResponseMock.mockResolvedValue({ title: "Auth.RegistrationsClosed" });

    const result = await registerAction(null, form());

    expect(result).toEqual({ registrationsClosed: true });
    expect(setSessionCookieMock).not.toHaveBeenCalled();
    expect(redirectMock).not.toHaveBeenCalled();
  });

  it("does NOT claim the gate is closed for the session-store 503", async () => {
    // The counterfactual that makes the title check load-bearing rather than argued — stubbed as the
    // shape the named actor ACTUALLY emits. Program.cs's SessionStoreUnavailableException arm writes
    // `{ error: ex.Message }`, plain JSON with no `title` field at all, so problemTitleSchema (which
    // is non-strict) parses it to `{ title: undefined }`. The OPEN instant-login path calls
    // sessionStore.CreateAsync, so this is what a Redis outage during an open registration looks
    // like here — and it must not tell the user registration is not open yet.
    parseResponseMock.mockResolvedValue({});

    const result = await registerAction(null, form());

    expect(result).toEqual({ error: "auth.actions.serverUnreachable" });
  });

  it("treats a 503 with an unparseable body as transport, not as the gate", async () => {
    // A proxy 503 is HTML, not application/problem+json — parseResponse throws and the branch must
    // fall through rather than guess.
    parseResponseMock.mockRejectedValue(new Error("not problem+json"));

    const result = await registerAction(null, form());

    expect(result).toEqual({ error: "auth.actions.serverUnreachable" });
  });
});

describe("registerAction 202 handling (#714 — email-confirmation-first)", () => {
  const form = () =>
    formOf({
      displayName: "Anna Andersson",
      email: "anna@example.se",
      password: "password1",
      acceptTerms: "on",
    });

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("returns pendingConfirmation on 202 WITHOUT setting a cookie or redirecting", async () => {
    // 202 = no session was issued (identical for a fresh or a taken address). The action must NOT
    // parse a sessionId, set the cookie, or redirect — it shows the check-inbox state.
    vi.stubGlobal("fetch", vi.fn(async () => ({ status: 202, ok: true }) as Response));

    const result = await registerAction(null, form());

    // #733: the submitted email is echoed back so the check-inbox panel can resend the link.
    expect(result).toEqual({
      pendingConfirmation: true,
      email: "anna@example.se",
    });
    expect(setSessionCookieMock).not.toHaveBeenCalled();
    expect(redirectMock).not.toHaveBeenCalled();
  });

  it("still logs in on the legacy 200 path (flag OFF) — cookie + redirect", async () => {
    // The FE is flag-agnostic: a 200 with a sessionId still instant-logs-in.
    parseResponseMock.mockResolvedValue({ sessionId: "sess-1" });
    vi.stubGlobal("fetch", vi.fn(async () => ({ status: 200, ok: true }) as Response));

    await expect(registerAction(null, form())).rejects.toThrow(/REDIRECT/);
    expect(setSessionCookieMock).toHaveBeenCalledWith("sess-1", false);
  });
});

describe("loginAction 403 handling (#714 — email-not-confirmed gate)", () => {
  const form = () => formOf({ email: "anna@example.se", password: "password1" });

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("maps a 403 to the actionable email-not-confirmed copy, no cookie or redirect", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => ({ status: 403, ok: false }) as Response));

    const result = await loginAction(null, form());

    // #733: the state also carries emailNotConfirmed so LoginForm can offer the resend action.
    // #791: and the submitted email, so the resend reads it from the action state (the live form
    // input is reset by React 19 after the action, so it would be empty at click time).
    expect(result).toEqual({
      error: "auth.actions.emailNotConfirmed",
      emailNotConfirmed: true,
      email: "anna@example.se",
    });
    expect(setSessionCookieMock).not.toHaveBeenCalled();
    expect(redirectMock).not.toHaveBeenCalled();
  });

  it("keeps mapping a 401 to the generic login-failed copy (not the 403 copy)", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => ({ status: 401, ok: false }) as Response));

    const result = await loginAction(null, form());

    expect(result).toEqual({ error: "auth.actions.loginFailed" });
  });
});
