import { describe, expect, it } from "vitest";
import { emailAllowanceSchema, emailDeliverySchema, emailFailuresSchema } from "./admin-email";

/** The body the API sends (#1981 PR 1), with the wire form of System.Text.Json for a DateTimeOffset in UTC. */
const EXAMPLE = {
  terms: {
    plan: "Essential", includedMonthlyEmails: 300, pricePerThousandExtra: 0.25, currency: "EUR",
    priceReadOn: "2026-10-18", monthlySendLimit: 10000, costCurrency: "SEK", sekPerEur: 11.145, exchangeRateOn: "2026-10-16",
  },
  usage: {
    status: "Observed", reason: null, observedAt: "2026-10-18T19:30:40+00:00", month: "2026-10", sent: 1301,
    billable: 1001, estimatedExtraCostOre: 349, aboveHalfOfSendLimit: false, webhooksCount: 0,
  },
  plan: { status: "Observed", reason: null, active: "Essential", matches: true },
};

const NO_USAGE = {
  observedAt: null, month: null, sent: null, billable: null, estimatedExtraCostOre: null,
  aboveHalfOfSendLimit: null, webhooksCount: null,
};
const notObservedUsage = { status: "NotObserved", reason: "NotConfigured", ...NO_USAGE };
const notObservedPlan = { status: "NotObserved", reason: "NotConfigured", active: null, matches: null };
const failedPlan = { status: "Failed", reason: "Forbidden", active: null, matches: null };
const readKeyMissingUsage = { status: "ReadKeyMissing", reason: null, ...NO_USAGE };
const readKeyMissingPlan = { status: "ReadKeyMissing", reason: null, active: null, matches: null };
const otherPlan = { status: "Observed", reason: null, active: "Scale", matches: false };
const withoutCost = { ...EXAMPLE.usage, billable: null, estimatedExtraCostOre: null };

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
    ["a computed zero cost", { ...EXAMPLE, usage: { ...EXAMPLE.usage, sent: 142, billable: 0, estimatedExtraCostOre: 0 } }],
    ["a read key that is not installed (#1981 PR 2)", { ...EXAMPLE, usage: readKeyMissingUsage, plan: readKeyMissingPlan }],
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
    ["a billable count without its cost", { ...EXAMPLE, usage: { ...EXAMPLE.usage, estimatedExtraCostOre: null } }],
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
    ["a missing read key with a reason", { ...EXAMPLE, usage: { ...readKeyMissingUsage, reason: "NotConfigured" }, plan: readKeyMissingPlan }],
    ["a missing read key that carries a count", { ...EXAMPLE, usage: { ...readKeyMissingUsage, sent: 0 }, plan: readKeyMissingPlan }],
    ["a missing read key on a plan that names one", { ...EXAMPLE, usage: readKeyMissingUsage, plan: { ...readKeyMissingPlan, active: "Essential" } }],
  ])("rejects %s", (_name, body) => {
    expect(parses(body)).toBe(false);
  });
});

/** The delivery read as the API sends it (#1981 PR 2): the provider's counts for the period, asked at `observedAt`. */
const DELIVERY = { period: "h24", status: "Observed", reason: null, observedAt: "2026-10-19T18:59:46+00:00", sent: 57, failed: 2 };
const NO_FIGURES = { observedAt: null, sent: null, failed: null };
const READ_FAILURES = ["Unauthorized", "Forbidden", "RateLimited", "ProviderError", "Timeout", "Unreachable", "Malformed"] as const;

describe("emailDeliverySchema (#1981 PR 2)", () => {
  const delivers = (body: unknown) => emailDeliverySchema.safeParse(body).success;

  it.each<readonly [string, unknown]>([
    ["an observed count", DELIVERY],
    ["an observed zero, which is a value", { ...DELIVERY, sent: 0, failed: 0 }],
    ["another period", { ...DELIVERY, period: "d7" }],
    ["a deployment without Scaleway", { period: "d3", status: "NotObserved", reason: "NotConfigured", ...NO_FIGURES }],
    ["a read key that sees no sends", { period: "d7", status: "NotObservable", reason: null, ...NO_FIGURES }],
    ["a read key that is not installed", { period: "d7", status: "ReadKeyMissing", reason: null, ...NO_FIGURES }],
    ...READ_FAILURES.map((reason) => [`a read that failed with ${reason}`, { period: "h24", status: "Failed", reason, ...NO_FIGURES }] as const),
  ])("accepts %s", (_name, body) => {
    expect(delivers(body)).toBe(true);
  });

  it.each([
    ["an observed read without its figures", { ...DELIVERY, sent: null, failed: null }],
    ["an observed read without its instant", { ...DELIVERY, observedAt: null }],
    ["an observed read with a reason", { ...DELIVERY, reason: "Timeout" }],
    ["an unjudged read that carries a zero", { period: "h24", status: "NotObservable", reason: null, ...NO_FIGURES, failed: 0 }],
    ["a failed read that carries a count", { period: "h24", status: "Failed", reason: "Timeout", ...NO_FIGURES, sent: 0 }],
    ["a failed read that carries an instant", { period: "h24", status: "Failed", reason: "Timeout", ...NO_FIGURES, observedAt: DELIVERY.observedAt }],
    ["a missing read key that carries a zero", { period: "h24", status: "ReadKeyMissing", reason: null, ...NO_FIGURES, sent: 0 }],
    ["NotConfigured as a failure", { period: "h24", status: "Failed", reason: "NotConfigured", ...NO_FIGURES }],
    ["a failed read without its reason", { period: "h24", status: "Failed", reason: null, ...NO_FIGURES }],
    ["a deployment without Scaleway given a failure reason", { period: "h24", status: "NotObserved", reason: "Timeout", ...NO_FIGURES }],
    ["NotObservable with a reason", { period: "h24", status: "NotObservable", reason: "Forbidden", ...NO_FIGURES }],
    ["a missing read key with a reason", { period: "h24", status: "ReadKeyMissing", reason: "NotConfigured", ...NO_FIGURES }],
    ["a failure reason outside the set", { period: "h24", status: "Failed", reason: "Teapot", ...NO_FIGURES }],
    ["a status outside the set", { ...DELIVERY, status: "Pending" }],
    ["a period outside the three", { ...DELIVERY, period: "d30" }],
    ["a negative count", { ...DELIVERY, failed: -1 }],
    ["a fractional count", { ...DELIVERY, sent: 1.5 }],
    ["one of the provider's other counters", { ...DELIVERY, canceled: 0 }],
    ["an instant without an offset", { ...DELIVERY, observedAt: "2026-10-19T18:59:46" }],
  ])("rejects %s", (_name, body) => {
    expect(delivers(body)).toBe(false);
  });
});

/** The failures read as the API sends it (#1981 PR 2): newest first, masked, one closed reason, never an id. */
const ROW = { createdAt: "2026-10-19T14:32:51+00:00", recipient: "k•••@example.com", reason: "MailboxNotFound" };
const FAILURES = {
  period: "d3", status: "Observed", reason: null, observedAt: "2026-10-19T18:59:46+00:00",
  failures: [ROW, { createdAt: "2026-10-18T09:05:00+00:00", recipient: "•••", reason: "SoftBounce" }],
  limit: 10,
};
const NO_ROWS = { observedAt: null, failures: null, limit: 10 };
const REASONS = ["MailboxNotFound", "HardBounce", "MailboxFull", "SoftBounce", "Spam", "Blocklisted", "Other"] as const;

describe("emailFailuresSchema (#1981 PR 2)", () => {
  const lists = (body: unknown) => emailFailuresSchema.safeParse(body).success;
  const withRow = (row: Record<string, unknown>) => ({ ...FAILURES, failures: [{ ...ROW, ...row }] });

  it.each<readonly [string, unknown]>([
    ["the latest failures, newest first", FAILURES],
    ["an observed empty list, which is a real empty", { ...FAILURES, failures: [] }],
    ["a list at its limit", { ...FAILURES, failures: Array.from({ length: 10 }, () => ROW) }],
    ["two rows at the same instant", { ...FAILURES, failures: [ROW, ROW] }],
    ["a recipient on a long subdomain", withRow({ recipient: "p•••@post.ett-mycket-langt-underdomannamn.example.org" })],
    ["a first character made of two code points", withRow({ recipient: "é•••@example.test" })],
    ["a recipient whose address had nothing to mask", withRow({ recipient: "•••" })],
    ...REASONS.map((reason) => [`the reason ${reason}`, withRow({ reason })] as const),
    ["a deployment without Scaleway", { period: "d3", status: "NotObserved", reason: "NotConfigured", ...NO_ROWS }],
    ["a list that cannot be judged", { period: "d3", status: "NotObservable", reason: null, ...NO_ROWS }],
    ["a read key that is not installed", { period: "d3", status: "ReadKeyMissing", reason: null, ...NO_ROWS }],
    ["a read that failed", { period: "d3", status: "Failed", reason: "Malformed", ...NO_ROWS }],
  ])("accepts %s", (_name, body) => {
    expect(lists(body)).toBe(true);
  });

  it.each([
    ["rows that are not newest first", { ...FAILURES, failures: [...FAILURES.failures].reverse() }],
    ["more rows than its limit", { ...FAILURES, failures: Array.from({ length: 11 }, () => ROW) }],
    ["another limit", { ...FAILURES, limit: 25 }],
    ["an observed read without its rows", { ...FAILURES, failures: null }],
    ["an unjudged read with an empty list", { period: "d3", status: "NotObservable", reason: null, ...NO_ROWS, failures: [] }],
    ["a failed read with an instant", { period: "d3", status: "Failed", reason: "Timeout", ...NO_ROWS, observedAt: FAILURES.observedAt }],
    ["NotConfigured as a failure", { period: "d3", status: "Failed", reason: "NotConfigured", ...NO_ROWS }],
    ["an address that is not masked", withRow({ recipient: "konto@example.com" })],
    ["a mask that keeps two characters", withRow({ recipient: "ko•••@example.com" })],
    ["a mask with no domain", withRow({ recipient: "k•••@" })],
    ["a mask with no first character", withRow({ recipient: "•••@example.com" })],
    ["a mask with a space in the domain", withRow({ recipient: "k•••@exa mple.com" })],
    ["an empty recipient", withRow({ recipient: "" })],
    ["a reason outside the set", withRow({ reason: "Bounced" })],
    ["the provider's id on a row", withRow({ id: "6f2a1d3e-0000-4000-8000-000000000001" })],
    ["the subject on a row", withRow({ subject: "Din inloggningskod" })],
    ["the provider's message on a row", withRow({ message: "550 5.1.1 User unknown" })],
    ["a row time without an offset", withRow({ createdAt: "2026-10-19T14:32:51" })],
  ])("rejects %s", (_name, body) => {
    expect(lists(body)).toBe(false);
  });
});
