import { describe, it, expect, vi, beforeEach } from "vitest";

// #1171 — resetPasswordAction. Unlike the request half this action MAY discriminate on the
// ProblemDetails title, because the backend only reaches a password error after verifying the token —
// so naming the broken rule tells the holder of a valid token nothing new, while every token rejection
// collapses to one message. These pin both halves of that split.

const { fetchMock } = vi.hoisted(() => ({ fetchMock: vi.fn() }));

vi.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) => key,
}));
vi.mock("@/lib/http/forwarded-headers", () => ({
  forwardedHeaders: async () => ({}),
}));
vi.mock("@/lib/env", () => ({ env: { BACKEND_URL: "http://api.test" } }));

import { resetPasswordAction } from "./reset-password";

const UID = "6f9619ff-8b86-d011-b42d-00c04fc964ff";
const TOKEN = "Q2ZESjhL-nP_ab12CD"; // gitleaks:allow
const PASSWORD = "ettNyttLosen123";

function form(
  overrides: Partial<{ uid: string; token: string; newPassword: string }> = {},
): FormData {
  const fd = new FormData();
  fd.set("uid", overrides.uid ?? UID);
  fd.set("token", overrides.token ?? TOKEN);
  fd.set("newPassword", overrides.newPassword ?? PASSWORD);
  return fd;
}

function fakeResponse(status: number, body: unknown = {}): Response {
  return {
    status,
    ok: status >= 200 && status < 300,
    json: async () => body,
  } as unknown as Response;
}

describe("resetPasswordAction", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal("fetch", fetchMock);
  });

  it("reports done on 204 and issues no session", async () => {
    fetchMock.mockResolvedValueOnce(fakeResponse(204));

    const result = await resetPasswordAction(null, form());

    expect(result).toEqual({ done: true });
    // The backend deliberately returns no session (the /confirm-email-change precedent), so there is
    // nothing for this action to set. Pinned so a future "helpful" auto-login has to be deliberate.
    const init = fetchMock.mock.calls[0]?.[1] as RequestInit;
    expect(init.headers).not.toHaveProperty("Authorization");
  });

  it("maps a breached password to its own copy, not the invalid-link copy", async () => {
    // Reachable ONLY with a valid token, so it is safe to be specific — and a user who picked a
    // breached password must be told that rather than that their link is broken.
    fetchMock.mockResolvedValueOnce(
      fakeResponse(400, { title: "Auth.PwnedPassword" }),
    );

    const result = await resetPasswordAction(null, form());

    expect(result).toEqual({ error: "auth.actions.passwordBreached" });
  });

  it("maps a password-field validation failure to the length copy, not the invalid-link copy", async () => {
    // The SHAPE production actually emits for a too-short password, measured rather than assumed:
    // ResetPasswordCommandValidator carries the same 12-character floor as Identity, so
    // ValidationBehavior fells it BEFORE the handler and no Auth.PasswordTooShort title is ever
    // produced on this route. Asserting a title here would test a state production cannot reach.
    // Getting the mapping wrong is the harmful direction: telling someone holding a VALID token that
    // their link is broken sends them to request one they do not need.
    fetchMock.mockResolvedValueOnce(
      fakeResponse(400, { errors: { NewPassword: ["Lösenordet måste vara minst 12 tecken."] } }),
    );

    const result = await resetPasswordAction(null, form());

    expect(result).toEqual({ error: "auth.resetPassword.passwordTooShort" });
  });

  it("collapses every token rejection to one uniform message", async () => {
    fetchMock.mockResolvedValueOnce(
      fakeResponse(400, { title: "Auth.InvalidPasswordResetToken" }),
    );

    const result = await resetPasswordAction(null, form());

    expect(result).toEqual({ error: "auth.resetPassword.invalidBody" });
  });

  it("treats a 400 with no recognised title as an invalid link", async () => {
    fetchMock.mockResolvedValueOnce(fakeResponse(400, { title: "Something.Else" }));

    const result = await resetPasswordAction(null, form());

    expect(result).toEqual({ error: "auth.resetPassword.invalidBody" });
  });

  it("treats 429 and 5xx as retryable rather than as a broken link", async () => {
    // Telling a user their link is broken when the server merely hiccupped sends them to request a
    // second link they do not need — and spends their cooldown window.
    for (const status of [429, 500, 503]) {
      fetchMock.mockResolvedValueOnce(fakeResponse(status));
      const result = await resetPasswordAction(null, form());
      expect(result).toEqual({ error: "auth.resetPassword.networkError" });
    }
  });

  it("returns the network message when the transport throws", async () => {
    fetchMock.mockRejectedValueOnce(new TypeError("fetch failed"));

    const result = await resetPasswordAction(null, form());

    expect(result).toEqual({ error: "auth.resetPassword.networkError" });
  });

  it("rejects a missing uid or token without calling the backend", async () => {
    for (const overrides of [{ uid: "" }, { token: "" }]) {
      const result = await resetPasswordAction(null, form(overrides));
      expect(result).toEqual({ error: "auth.resetPassword.invalidBody" });
    }
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("rejects a short password client-side without calling the backend", async () => {
    const result = await resetPasswordAction(null, form({ newPassword: "kort" }));

    expect(result).toEqual({ error: "auth.resetPassword.passwordTooShort" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("never logs the token", async () => {
    const spy = vi.spyOn(console, "log").mockImplementation(() => {});
    const errSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    fetchMock.mockResolvedValueOnce(fakeResponse(400, { title: "X" }));

    await resetPasswordAction(null, form());

    expect(spy).not.toHaveBeenCalled();
    expect(errSpy).not.toHaveBeenCalled();
    spy.mockRestore();
    errSpy.mockRestore();
  });
});
