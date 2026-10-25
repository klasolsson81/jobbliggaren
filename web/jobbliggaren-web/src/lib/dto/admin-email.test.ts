import { describe, expect, it } from "vitest";
import {
  DMARC_LATEST_FAILURES,
  EMAIL_KINDS,
  emailAllowanceSchema,
  emailDeliverySchema,
  emailDmarcSchema,
  emailFailuresSchema,
  emailKindsSchema,
} from "./admin-email";
import {
  DMARC_FROM,
  DMARC_TO,
  LONG_IPV6,
  cappedDmarc,
  dmarcFailure,
  dmarcIngestion,
  dmarcSource,
  failedDmarc,
  noReportsDmarc,
  observedDmarc,
  unreadDmarc,
} from "@/test/fixtures/admin-email-dmarc";

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
    ["a first digit", withRow({ recipient: "7•••@example.net" })],
    ["a first letter outside ASCII", withRow({ recipient: "å•••@example.org" })],
    ["a first letter outside the BMP, shown whole", withRow({ recipient: "\u{1D400}•••@example.com" })],
    ["a domain in capitals, with a hyphen", withRow({ recipient: "a•••@SUB-DOMAIN.EXAMPLE.TEST" })],
    ["a local part whose first character is not a letter or digit", withRow({ recipient: "•••@example.com" })],
    ["a domain at its longest", withRow({ recipient: `a•••@${"d".repeat(249)}.com` })],
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
    ["a first character the API never shows", withRow({ recipient: "+•••@example.com" })],
    ["a first character with a combining mark, which the API cuts to its base letter", withRow({ recipient: "e\u0301•••@example.test" })],
    ["a lone surrogate as the first character", withRow({ recipient: "\uD800•••@example.com" })],
    ["a mask with a space in the domain", withRow({ recipient: "k•••@exa mple.com" })],
    ["a domain with a character the API hides", withRow({ recipient: "k•••@example_com" })],
    ["a domain with a bidi control", withRow({ recipient: "k•••@example.com\u202Eevil" })],
    ["a domain over its longest", withRow({ recipient: `a•••@${"d".repeat(250)}.com` })],
    ["a domain with a second @", withRow({ recipient: "k•••@a@example.com" })],
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

/** The per-kind read as the API sends it (#1981 PR 3): our own count, seven kinds in a fixed order. */
const quiet = (kind: string) => ({ kind, accepted: 5, failed: 0, latestFailure: null });
const KINDS = {
  period: "d3", status: "Observed", from: "2026-10-20", coverage: "Complete", coveredFrom: null,
  kinds: [
    { kind: "login-challenge", accepted: 12, failed: 2, latestFailure: { day: "2026-10-22", outcomes: ["Refused", "Timeout"] } },
    ...EMAIL_KINDS.slice(1).map(quiet),
  ],
};
const withKind = (index: number, row: Record<string, unknown>) =>
  ({ ...KINDS, kinds: KINDS.kinds.map((kind, at) => (at === index ? { ...kind, ...row } : kind)) });
const NO_KINDS = { coverage: null, coveredFrom: null, kinds: null };

describe("emailKindsSchema (#1981 PR 3)", () => {
  const counts = (body: unknown) => emailKindsSchema.safeParse(body).success;

  it.each<readonly [string, unknown]>([
    ["the seven kinds in their order", KINDS],
    ["a count that began inside the period", { ...KINDS, coverage: "Partial", coveredFrom: "2026-10-23T05:12:34+00:00" }],
    ["a start with the database's six decimals of a second", { ...KINDS, coverage: "Partial", coveredFrom: "2026-10-23T05:13:27.123456+00:00" }],
    ["every kind at zero", { ...KINDS, kinds: EMAIL_KINDS.map((kind) => ({ kind, accepted: 0, failed: 0, latestFailure: null })) }],
    ["every failure class on one day, in their order", withKind(0, {
      latestFailure: { day: "2026-10-22", outcomes: ["Refused", "RateLimited", "Unreachable", "ProviderError", "Timeout", "Other"] },
    })],
    ["one failure class", withKind(3, { failed: 1, latestFailure: { day: "2026-10-21", outcomes: ["Other"] } })],
    ["a deployment without Scaleway", { period: "d3", status: "NotObserved", from: null, ...NO_KINDS }],
    ["a count with no sends while the provider sent", { period: "h24", status: "NotObservable", from: "2026-10-22", ...NO_KINDS }],
  ])("accepts %s", (_name, body) => {
    expect(counts(body)).toBe(true);
  });

  it.each<readonly [string, unknown]>([
    ["six kinds", { ...KINDS, kinds: KINDS.kinds.slice(0, 6) }],
    ["eight kinds", { ...KINDS, kinds: [...KINDS.kinds, quiet("feedback-reply")] }],
    ["a kind twice", withKind(1, { kind: "login-challenge" })],
    ["the kinds in another order", { ...KINDS, kinds: [...KINDS.kinds].reverse() }],
    ["a kind outside the seven", withKind(6, { kind: "newsletter" })],
    ["a failure without its latest day", withKind(0, { latestFailure: null })],
    ["a latest day without a failure", withKind(1, { latestFailure: { day: "2026-10-22", outcomes: ["Refused"] } })],
    ["a latest day without a class", withKind(0, { latestFailure: { day: "2026-10-22", outcomes: [] } })],
    ["a class twice", withKind(0, { latestFailure: { day: "2026-10-22", outcomes: ["Timeout", "Timeout"] } })],
    ["the classes out of their order", withKind(0, { latestFailure: { day: "2026-10-22", outcomes: ["Timeout", "Refused"] } })],
    ["Accepted as a failure class", withKind(0, { latestFailure: { day: "2026-10-22", outcomes: ["Accepted"] } })],
    ["the old name of the default class", withKind(0, { latestFailure: { day: "2026-10-22", outcomes: ["Interrupted"] } })],
    ["a latest failure with a time of day", withKind(0, { latestFailure: { day: "2026-10-22T13:04:00+00:00", outcomes: ["Refused"] } })],
    ["a negative count", withKind(2, { accepted: -1 })],
    ["a fractional count", withKind(2, { accepted: 1.5 })],
    ["a recipient on a row", withKind(0, { recipient: "k•••@example.com" })],
    ["a total beside the kinds", { ...KINDS, total: 21 }],
    ["a partial count without its start", { ...KINDS, coverage: "Partial" }],
    ["a complete count with a start", { ...KINDS, coveredFrom: "2026-10-23T05:12:34+00:00" }],
    ["a start without an offset", { ...KINDS, coverage: "Partial", coveredFrom: "2026-10-23T05:12:34" }],
    ["a first day with a time", { ...KINDS, from: "2026-10-20T00:00:00+02:00" }],
    ["an observed read without its first day", { ...KINDS, from: null }],
    ["an observed read without its kinds", { ...KINDS, kinds: null }],
    ["a coverage outside the two", { ...KINDS, coverage: "Unknown" }],
    ["an unobserved read with a first day", { period: "d3", status: "NotObserved", from: "2026-10-20", ...NO_KINDS }],
    ["an unobservable read without its first day", { period: "d3", status: "NotObservable", from: null, ...NO_KINDS }],
    ["an unobservable read with its kinds", { period: "d3", status: "NotObservable", from: "2026-10-20", ...NO_KINDS, kinds: [] }],
    ["a failed state, which our own read never answers", { period: "d3", status: "Failed", from: null, ...NO_KINDS }],
    ["a period outside the three", { ...KINDS, period: "d30" }],
  ])("rejects %s", (_name, body) => {
    expect(counts(body)).toBe(false);
  });
});

describe("emailDmarcSchema (#1981 PR 4b, ADR 0185 D9)", () => {
  const reads = (body: unknown) => emailDmarcSchema.safeParse(body).success;
  const observed = observedDmarc();
  const withSource = (index: number, change: Record<string, unknown>) =>
    observedDmarc({ sources: observed.sources.map((source, at) => (at === index ? { ...source, ...change } : source)) });
  const longestDomain = `${"a".repeat(63)}.${"b".repeat(63)}.${"c".repeat(63)}.${"d".repeat(45)}.jobbliggaren.se`;

  it.each<readonly [string, unknown]>([
    ["figures with a failing, a DKIM-only and an SPF-only source", observed],
    ["the cap: 25 of 31 sources at IPv6 length", cappedDmarc()],
    ["a failure after an earlier success, figures kept", observedDmarc({ ingestion: dmarcFailure("Protocol") })],
    ["a refused sign-in after a success, with when it resumes", observedDmarc({ ingestion: dmarcFailure("AuthRefused") })],
    ["a sign-in with no answer after a success", observedDmarc({ ingestion: dmarcFailure("AuthUnconfirmed") })],
    ["a success older than the threshold", observedDmarc({ ingestion: dmarcIngestion({ stale: true }) })],
    ["a covered span that begins before the window", observedDmarc({ coveredFrom: "2026-10-17T00:00:00+00:00" })],
    ["conflicting reports and every mailbox count", observedDmarc({
      conflictingReports: 2, mailbox: { skipped: 1, rejected: 2, volumeCapped: 3, abandoned: 4, retryPending: 5 },
    })],
    ["an ISATAP address with its dotted tail", observedDmarc({
      sources: [dmarcSource("2001:db8::5efe:192.0.2.1", "jobbliggaren.se", 51, 48, 48, 3)], sourceCount: 1,
    })],
    ["an address at the 45 characters the alphabet allows", observedDmarc({
      sources: [dmarcSource("2001:db8:ffff:ffff:ffff:ffff:255.255.255.255", "jobbliggaren.se", 51, 48, 48, 3)], sourceCount: 1,
    })],
    ["a header-from domain at its 253 characters", observedDmarc({
      sources: [dmarcSource("192.0.2.10", longestDomain, 51, 48, 48, 3)], sourceCount: 1,
    })],
    ["the window's end with the clock's seven decimals", observedDmarc({ to: DMARC_TO, from: DMARC_FROM })],
    ["no report in the window", noReportsDmarc()],
    ["no report, with conflicting reports kept out", noReportsDmarc({ conflictingReports: 1 })],
    ["no report and a stale read", noReportsDmarc({ ingestion: dmarcIngestion({ stale: true }) })],
    ...DMARC_LATEST_FAILURES.map((failure) => [`never read: ${failure}`, failedDmarc(failure)] as const),
    ["no run yet", unreadDmarc("NotObservable")],
    ["no password in this deployment", unreadDmarc("NotConfigured")],
    ["a blank password", unreadDmarc("Misconfigured")],
  ])("accepts %s", (_name, body) => {
    expect(emailDmarcSchema.safeParse(body).error).toBeUndefined();
  });

  it.each<readonly [string, unknown]>([
    ["a key outside the contract", { ...observed, reporterNames: [] }],
    ["a missing key", Object.fromEntries(Object.entries(observed).filter(([key]) => key !== "sourceCount"))],
    ["a reporter's name on a source", withSource(0, { reporter: "reporter.example" })],
    ["a status outside the six", { ...observed, status: "Partial" }],
    ["figures without a report", { ...noReportsDmarc(), messages: 3 }],
    ["a window on a mailbox never read", { ...failedDmarc("Timeout"), from: DMARC_FROM }],
    ["a figure where the mailbox is not read here", { ...unreadDmarc("NotConfigured"), reports: 0 }],
    ["Succeeded as the latest failure", observedDmarc({ ingestion: { ...dmarcIngestion({ stale: true }), latestFailure: "Succeeded" as never } })],
    ["NotConfigured as the latest failure", observedDmarc({ ingestion: { ...dmarcIngestion({ stale: true }), latestFailure: "NotConfigured" as never } })],
    ["Incomplete after a success", observedDmarc({ ingestion: dmarcFailure("Incomplete") })],
    ["a failed latest run that is not stale", observedDmarc({ ingestion: { ...dmarcFailure("Timeout"), stale: false } })],
    ["a resume without a failure", observedDmarc({ ingestion: dmarcIngestion({ signInResumesAt: "2026-10-26T08:23:09+00:00" }) })],
    ["a resume before the attempt", observedDmarc({
      ingestion: { ...dmarcFailure("AuthRefused"), signInResumesAt: "2026-10-24T08:23:09+00:00" },
    })],
    ["a failed state after a success", { ...failedDmarc("Timeout"), ingestion: dmarcFailure("Timeout") }],
    ["a window without a success", noReportsDmarc({ ingestion: dmarcFailure("Timeout", null) })],
    ["a window of another length", observedDmarc({ from: "2026-10-24T09:00:00.1234567+00:00" })],
    ["a covered span after the window", observedDmarc({ coveredFrom: "2026-10-26T00:00:00+00:00", coveredTo: "2026-10-27T00:00:00+00:00" })],
    ["a covered span that ends before it begins", observedDmarc({ coveredFrom: "2026-10-24T00:00:00+00:00", coveredTo: "2026-10-23T00:00:00+00:00" })],
    ["more reporters than reports", observedDmarc({ reporters: 3 })],
    ["more failing than counted", observedDmarc({ failingMessages: 52 })],
    ["fewer sources than shown though all are", observedDmarc({ sourceCount: 4 })],
    ["more than the cap", observedDmarc({
      sources: Array.from({ length: 26 }, (_, i) => dmarcSource(`192.0.2.${i}`, "jobbliggaren.se", 1, 1, 1, 0)),
      sourceCount: 26, messages: 26, failingMessages: 0,
    })],
    ["totals that are not the sums when all are shown", observedDmarc({ messages: 52 })],
    ["the sources out of their order", observedDmarc({ sources: [...observed.sources].reverse() })],
    ["a source and domain twice", observedDmarc({
      sources: [observed.sources[0], observed.sources[0], observed.sources[1]].filter((s) => s !== undefined),
      messages: 46, failingMessages: 6,
    })],
    ["a flag that disagrees with the failing count", withSource(1, { flagged: true })],
    ["a figure larger than its source's messages", withSource(1, { dkimAligned: 41 })],
    ["a source without a message", withSource(2, { messages: 0, spfAligned: 0 })],
    ["an address with markup", withSource(0, { address: "<b>198.51.100.7</b>" })],
    ["an address in upper case", withSource(2, { address: LONG_IPV6.toUpperCase() })],
    ["an address with a zone", withSource(2, { address: "2001:db8::1%eth0" })],
    ["an address longer than the alphabet allows", withSource(2, { address: `${LONG_IPV6}:ffff:ff` })],
    ["a domain outside the policy domain", withSource(0, { headerFrom: "example.com" })],
    ["a look-alike of the policy domain", withSource(0, { headerFrom: "notjobbliggaren.se" })],
    ["a domain in upper case", withSource(0, { headerFrom: "Jobbliggaren.se" })],
    ["a domain with a bidi control", withSource(0, { headerFrom: "mail‮.jobbliggaren.se" })],
    ["a domain with a space", withSource(0, { headerFrom: "mail .jobbliggaren.se" })],
    ["a domain longer than 253 characters", withSource(0, { headerFrom: `x${longestDomain}` })],
    ["a negative count", observedDmarc({ conflictingReports: -1 })],
    ["a fractional count", observedDmarc({ mailbox: { ...observed.mailbox, skipped: 0.5 } })],
    ["an instant without an offset", observedDmarc({ to: "2026-10-25T09:00:00" })],
  ])("rejects %s", (_name, body) => {
    expect(reads(body)).toBe(false);
  });

  it("names no value it refuses in its message, so a logged miss carries no address or domain", () => {
    const result = emailDmarcSchema.safeParse(withSource(0, { address: "198.51.100.77x", headerFrom: "evil.example" }));
    expect(result.success).toBe(false);
    const messages = JSON.stringify(result.error?.issues.map((issue) => issue.message));
    expect(messages).not.toContain("198.51.100.77x");
    expect(messages).not.toContain("evil.example");
  });
});
