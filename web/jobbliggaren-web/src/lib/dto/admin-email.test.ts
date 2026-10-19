import { describe, expect, it } from "vitest";
import { emailAllowanceSchema } from "./admin-email";

/** The body the API sends (#1981 PR 1), with the wire form of System.Text.Json for a DateTimeOffset in UTC. */
const EXAMPLE = {
  terms: {
    plan: "Essential", includedMonthlyEmails: 300, pricePerThousandExtra: 0.25, currency: "EUR",
    priceReadOn: "2026-10-18", monthlySendLimit: 10000,
  },
  usage: {
    status: "Observed", reason: null, observedAt: "2026-10-18T19:30:40+00:00", month: "2026-10", sent: 1301,
    billable: 1001, estimatedExtraCostCents: 26, aboveHalfOfSendLimit: false, webhooksCount: 0,
  },
  plan: { status: "Observed", reason: null, active: "Essential", matches: true },
};

const NO_USAGE = {
  observedAt: null, month: null, sent: null, billable: null, estimatedExtraCostCents: null,
  aboveHalfOfSendLimit: null, webhooksCount: null,
};
const notObservedUsage = { status: "NotObserved", reason: "NotConfigured", ...NO_USAGE };
const notObservedPlan = { status: "NotObserved", reason: "NotConfigured", active: null, matches: null };
const failedPlan = { status: "Failed", reason: "Forbidden", active: null, matches: null };
const otherPlan = { status: "Observed", reason: null, active: "Scale", matches: false };
const withoutCost = { ...EXAMPLE.usage, billable: null, estimatedExtraCostCents: null };

const parses = (body: unknown) => emailAllowanceSchema.safeParse(body).success;

describe("emailAllowanceSchema (#1981 PR 1, ADR 0178)", () => {
  it("accepts the API's example", () => {
    expect(parses(EXAMPLE)).toBe(true);
  });

  it.each([
    ["no provider in this deployment", { ...EXAMPLE, usage: notObservedUsage, plan: notObservedPlan }],
    ["a failed count and a plan in force", { ...EXAMPLE, usage: { status: "Failed", reason: "Timeout", ...NO_USAGE } }],
    ["a count under another plan, without a cost", { ...EXAMPLE, usage: withoutCost, plan: otherPlan }],
    ["a count under an unknown plan, without a cost", {
      ...EXAMPLE, usage: withoutCost, plan: { status: "Observed", reason: null, active: "Unknown", matches: false },
    }],
    ["a count whose plan read failed, without a cost", { ...EXAMPLE, usage: withoutCost, plan: failedPlan }],
    ["a month that cannot be named and unreadable webhooks", { ...EXAMPLE, usage: { ...EXAMPLE.usage, month: null, webhooksCount: null } }],
    ["fractional seconds in the instant", { ...EXAMPLE, usage: { ...EXAMPLE.usage, observedAt: "2026-10-18T19:30:40.1234567+00:00" } }],
    ["a computed zero cost", { ...EXAMPLE, usage: { ...EXAMPLE.usage, sent: 142, billable: 0, estimatedExtraCostCents: 0 } }],
  ])("accepts %s", (_name, body) => {
    expect(parses(body)).toBe(true);
  });

  it.each([
    ["an extra key at the top", { ...EXAMPLE, priceUrl: "https://example.test" }],
    ["an extra key in the terms", { ...EXAMPLE, terms: { ...EXAMPLE.terms, vat: false } }],
    ["an extra key in the usage", { ...EXAMPLE, usage: { ...EXAMPLE.usage, customBlocklistsCount: 0 } }],
    ["an extra key in the plan", { ...EXAMPLE, plan: { ...EXAMPLE.plan, offerName: "essential" } }],
    ["a missing key", { terms: EXAMPLE.terms, usage: EXAMPLE.usage }],
    ["a cost while the plan does not match", { ...EXAMPLE, plan: otherPlan }],
    ["a cost while the plan read failed", { ...EXAMPLE, plan: failedPlan }],
    ["no cost while the plan is in force", { ...EXAMPLE, usage: withoutCost }],
    ["a billable count without its cost", { ...EXAMPLE, usage: { ...EXAMPLE.usage, estimatedExtraCostCents: null } }],
    ["NotConfigured as a failure of the count", { ...EXAMPLE, usage: { status: "Failed", reason: "NotConfigured", ...NO_USAGE } }],
    ["NotConfigured as a failure of the plan", { ...EXAMPLE, usage: withoutCost, plan: { ...failedPlan, reason: "NotConfigured" } }],
    ["a failure reason outside the set", { ...EXAMPLE, usage: { status: "Failed", reason: "Teapot", ...NO_USAGE } }],
    ["a failed count that carries a figure", { ...EXAMPLE, usage: { status: "Failed", reason: "Timeout", ...NO_USAGE, sent: 0 } }],
    ["an unobserved count that carries a figure", { ...EXAMPLE, usage: { ...notObservedUsage, sent: 0 }, plan: notObservedPlan }],
    ["an observed count without its instant", { ...EXAMPLE, usage: { ...EXAMPLE.usage, observedAt: null } }],
    ["a negative count", { ...EXAMPLE, usage: { ...EXAMPLE.usage, sent: -1 } }],
    ["a fractional count", { ...EXAMPLE, usage: { ...EXAMPLE.usage, sent: 1.5 } }],
    ["a negative webhook count", { ...EXAMPLE, usage: { ...EXAMPLE.usage, webhooksCount: -1 } }],
    ["a month that is not YYYY-MM", { ...EXAMPLE, usage: { ...EXAMPLE.usage, month: "2026-13" } }],
    ["an instant without an offset", { ...EXAMPLE, usage: { ...EXAMPLE.usage, observedAt: "2026-10-18T19:30:40" } }],
    ["another currency", { ...EXAMPLE, terms: { ...EXAMPLE.terms, currency: "SEK" } }],
    ["a price read on no date", { ...EXAMPLE, terms: { ...EXAMPLE.terms, priceReadOn: "yesterday" } }],
    ["a plan outside the set", { ...EXAMPLE, plan: { ...EXAMPLE.plan, active: "Enterprise" } }],
    ["a match the plans do not support", { ...EXAMPLE, usage: withoutCost, plan: { ...otherPlan, matches: true } }],
    ["an unknown plan that matches", { ...EXAMPLE, plan: { ...EXAMPLE.plan, active: "Unknown" } }],
    ["an observed plan with a reason", { ...EXAMPLE, plan: { ...EXAMPLE.plan, reason: "Timeout" } }],
  ])("rejects %s", (_name, body) => {
    expect(parses(body)).toBe(false);
  });
});
