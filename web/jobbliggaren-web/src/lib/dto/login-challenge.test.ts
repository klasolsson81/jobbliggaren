import { describe, expect, it } from "vitest";
import { loginChallengeResponseSchema, loginOutcomeSchema } from "./login-challenge";

// The bodies below are the exact key sets the backend's own integration tests pin
// (`LoginOutcomeResultTests`, `LoginChallengeProofTests`, `LoginChallengeNewAddressTests`).
describe("loginOutcomeSchema", () => {
  it.each([
    [{ outcome: "signedIn", sessionId: "opaque-session-id" }],
    [{ outcome: "pendingDeletion", permanentDeletionDate: "2026-10-19" }],
    [{ outcome: "registrationClosed" }],
    [{ outcome: "consentRequired", grantToken: "sample-grant" }],
    [{ outcome: "accountUnavailable" }],
  ])("accepts %o", (body) => {
    expect(loginOutcomeSchema.parse(body)).toEqual(body);
  });

  it.each([
    ["an outcome the backend does not have", { outcome: "loggedIn", sessionId: "s" }],
    ["a signed-in body without its session", { outcome: "signedIn" }],
    ["a consent body without its grant", { outcome: "consentRequired" }],
    ["a deletion date carrying a time", { outcome: "pendingDeletion", permanentDeletionDate: "2026-10-19T00:00:00Z" }],
    ["a bare session body, which is /auth/login's shape and not this one", { sessionId: "s" }],
  ])("refuses %s", (_label, body) => {
    expect(loginOutcomeSchema.safeParse(body).success).toBe(false);
  });
});

describe("loginChallengeResponseSchema", () => {
  it("reads the challenge id", () => {
    expect(loginChallengeResponseSchema.parse({ challengeId: "sample-challenge" })).toEqual({
      challengeId: "sample-challenge",
    });
  });

  it("refuses a body without one", () => {
    expect(loginChallengeResponseSchema.safeParse({}).success).toBe(false);
  });
});
