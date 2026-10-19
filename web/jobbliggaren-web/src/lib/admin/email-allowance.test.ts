import { describe, expect, it } from "vitest";
import { emailAllowanceFixture } from "@/test/fixtures/admin-overview";
import type { EmailAllowanceDto } from "@/lib/dto/admin-email";
import { allowanceFailureGroup, allowanceFigures, allowancePlanState, monthStart } from "./email-allowance";

const NO_USAGE = {
  observedAt: null, month: null, sent: null, billable: null, estimatedExtraCostCents: null,
  aboveHalfOfSendLimit: null, webhooksCount: null,
} as const;

const base = emailAllowanceFixture();
const withPlan = (plan: EmailAllowanceDto["plan"]): EmailAllowanceDto => ({
  ...base,
  usage: base.usage.status === "Observed" ? { ...base.usage, billable: null, estimatedExtraCostCents: null } : base.usage,
  plan,
});
const failedUsage: EmailAllowanceDto = { ...base, usage: { status: "Failed", reason: "Timeout", ...NO_USAGE } };
const notConfigured: EmailAllowanceDto = {
  ...base,
  usage: { status: "NotObserved", reason: "NotConfigured", ...NO_USAGE },
  plan: { status: "NotObserved", reason: "NotConfigured", active: null, matches: null },
};

describe("allowanceFigures (#1981 PR 1)", () => {
  it("takes the count, the configured free mails and the cost while the plan in force is the configured one", () => {
    expect(allowanceFigures(base)).toEqual({ sent: 1301, included: 300, costCents: 26 });
  });

  it("keeps the count and leaves the allowance and the cost unknown under another plan", () => {
    expect(allowanceFigures(withPlan({ status: "Observed", reason: null, active: "Scale", matches: false })))
      .toEqual({ sent: 1301, included: null, costCents: null });
  });

  it("keeps the configured allowance when only the count failed, since the terms are ours", () => {
    expect(allowanceFigures(failedUsage)).toEqual({ sent: null, included: 300, costCents: null });
  });

  it("has no figure at all where no provider is configured", () => {
    expect(allowanceFigures(notConfigured)).toEqual({ sent: null, included: null, costCents: null });
  });
});

describe("allowancePlanState", () => {
  it.each([
    ["the configured plan", base.plan, null],
    ["another plan", { status: "Observed", reason: null, active: "Scale", matches: false }, { kind: "differs", active: "Scale" }],
    ["a plan this code does not know", { status: "Observed", reason: null, active: "Unknown", matches: false }, { kind: "undetermined" }],
    ["a failed plan read", { status: "Failed", reason: "Forbidden", active: null, matches: null }, { kind: "failed" }],
  ] as const)("reads %s", (_name, plan, expected) => {
    expect(allowancePlanState(withPlan(plan))).toEqual(expected);
  });

  it("says nothing of the plan where no provider is configured: that state has its own line", () => {
    expect(allowancePlanState(notConfigured)).toBeNull();
  });
});

describe("allowanceFailureGroup", () => {
  it.each([
    ["Timeout", "noAnswer"],
    ["Unreachable", "noAnswer"],
    ["ProviderError", "noAnswer"],
    ["RateLimited", "rateLimited"],
    ["Unauthorized", "refused"],
    ["Forbidden", "refused"],
    ["Malformed", "malformed"],
  ] as const)("words %s as %s", (reason, group) => {
    expect(allowanceFailureGroup(reason)).toBe(group);
  });
});

describe("monthStart", () => {
  it("is the month's first instant in UTC, whatever zone the reader is in", () => {
    expect(monthStart("2026-10").toISOString()).toBe("2026-10-01T00:00:00.000Z");
  });
});
