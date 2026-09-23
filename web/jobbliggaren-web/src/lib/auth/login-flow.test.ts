import { describe, expect, it } from "vitest";
import {
  CODE_PHASE_MAX_AGE_SECONDS,
  CONSENT_PHASE_MAX_AGE_SECONDS,
  NOTICE_PHASE_MAX_AGE_SECONDS,
  OUTCOME_PHASE_MAX_AGE_SECONDS,
  RESEND_COOLDOWN_SECONDS,
  cookieSafeNext,
  decodeLoginFlow,
  encodeLoginFlow,
  maxAgeSecondsFor,
  resendCooldownRemaining,
  type LoginFlow,
} from "./login-flow";

const MINTED_AT = 1_800_000_000;

const code: LoginFlow = {
  phase: "code",
  challengeId: "sample-challenge",
  email: "anna@example.com",
  next: "/ansokningar",
  sentAt: MINTED_AT,
};

const raw = (value: unknown): string =>
  Buffer.from(JSON.stringify(value), "utf8").toString("base64url");

describe("the login flow cookie value", () => {
  it.each<[string, LoginFlow]>([
    ["code", code],
    ["code, dead", { ...code, dead: "burned" }],
    ["consent", { phase: "consent", grantToken: "sample-grant", next: "" }],
    [
      "outcome, pending deletion",
      {
        phase: "outcome",
        result: { outcome: "pendingDeletion", permanentDeletionDate: "2026-10-19" },
      },
    ],
    ["outcome, registration closed", { phase: "outcome", result: { outcome: "registrationClosed" } }],
    ["outcome, account unavailable", { phase: "outcome", result: { outcome: "accountUnavailable" } }],
    ["notice", { phase: "notice", notice: "grantUnusable" }],
    ["notice, account deleted", { phase: "notice", notice: "accountDeleted" }],
  ])("round-trips the %s phase", (_label, flow) => {
    expect(decodeLoginFlow(encodeLoginFlow(flow))).toEqual(flow);
  });

  it.each([
    ["nothing", undefined],
    ["the empty string", ""],
    ["a value that is not base64url JSON", "%%%"],
    ["JSON that is not an object", raw("code")],
    ["an unknown phase", raw({ phase: "signedIn", sessionId: "s" })],
    ["a code phase without its challenge id", raw({ ...code, challengeId: undefined })],
    ["a pending deletion without its date", raw({ phase: "outcome", result: { outcome: "pendingDeletion" } })],
    ["a date that is not a bare ISO date", raw({ phase: "outcome", result: { outcome: "pendingDeletion", permanentDeletionDate: "2026-10-19T00:00:00Z" } })],
    ["an outcome this flow never stores", raw({ phase: "outcome", result: { outcome: "signedIn" } })],
  ])("reads %s as no cookie", (_label, value) => {
    expect(decodeLoginFlow(value)).toBeNull();
  });

  // The forge with a payoff is carrying one phase's field into another phase's reader.
  it.each([
    ["a grant on a code phase", raw({ ...code, grantToken: "sample-grant" })],
    ["an address on a consent phase", raw({ phase: "consent", grantToken: "sample-grant", next: "", email: "anna@example.com" })],
    ["an address on an outcome phase", raw({ phase: "outcome", result: { outcome: "registrationClosed" }, email: "anna@example.com" })],
    ["a date on a closed registration", raw({ phase: "outcome", result: { outcome: "registrationClosed", permanentDeletionDate: "2026-10-19" } })],
  ])("refuses %s", (_label, value) => {
    expect(decodeLoginFlow(value)).toBeNull();
  });
});

describe("maxAgeSecondsFor", () => {
  it("gives a fresh code phase the challenge's whole life", () => {
    expect(maxAgeSecondsFor(code, MINTED_AT)).toBe(CODE_PHASE_MAX_AGE_SECONDS);
  });

  it("counts a rewritten code phase from the mint, so marking it dead never extends it", () => {
    expect(maxAgeSecondsFor({ ...code, dead: "expired" }, MINTED_AT + 600)).toBe(
      CODE_PHASE_MAX_AGE_SECONDS - 600
    );
  });

  it("never returns zero or less, which a browser reads as delete-now or as a session cookie", () => {
    expect(maxAgeSecondsFor(code, MINTED_AT + CODE_PHASE_MAX_AGE_SECONDS + 5)).toBe(1);
  });

  it.each<[LoginFlow, number]>([
    [{ phase: "consent", grantToken: "sample-grant", next: "" }, CONSENT_PHASE_MAX_AGE_SECONDS],
    [{ phase: "outcome", result: { outcome: "accountUnavailable" } }, OUTCOME_PHASE_MAX_AGE_SECONDS],
    [{ phase: "notice", notice: "grantUnusable" }, NOTICE_PHASE_MAX_AGE_SECONDS],
  ])("gives the $phase phase its own lifetime", (flow, expected) => {
    expect(maxAgeSecondsFor(flow, MINTED_AT)).toBe(expected);
  });
});

describe("resendCooldownRemaining", () => {
  it("starts at the full cooldown on the mint", () => {
    expect(resendCooldownRemaining(code, MINTED_AT)).toBe(RESEND_COOLDOWN_SECONDS);
  });

  it("counts down from the mint, so a reload does not restart it", () => {
    expect(resendCooldownRemaining(code, MINTED_AT + 45)).toBe(RESEND_COOLDOWN_SECONDS - 45);
  });

  it("is zero once the cooldown has passed", () => {
    expect(resendCooldownRemaining(code, MINTED_AT + RESEND_COOLDOWN_SECONDS + 1)).toBe(0);
  });
});

describe("cookieSafeNext", () => {
  it("keeps a path that fits", () => {
    expect(cookieSafeNext("/ansokningar/abc-123")).toBe("/ansokningar/abc-123");
  });

  it("drops a path too long for the cookie instead of clipping it into another path", () => {
    expect(cookieSafeNext(`/${"a".repeat(600)}`)).toBe("");
  });
});
