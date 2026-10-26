import { describe, expect, it } from "vitest";
import { ADMIN_PREVIEW_SENTINEL } from "../../../../scripts/assert-admin-preview-build.mjs";
import { DMARC_LATEST_FAILURES, emailDmarcSchema, emailKindsSchema } from "@/lib/dto/admin-email";
import * as fixtures from "./index";

/**
 * The preview's fixtures are fictional by construction (ADR 0150 D5): reserved-domain addresses,
 * documentation IP ranges, no name field, and the sentinel on every row. The three date exports are
 * the fixed clock and values derived from it, not rows.
 */
const NOT_ROWS = new Set(["FIXTURE_NOW", "PREVIEW_DELETION_EARLIEST", "PREVIEW_ACTIVITY_RECORDED_FROM"]);
const exported = Object.entries(fixtures).filter(([name]) => !NOT_ROWS.has(name));

function strings(value: unknown, acc: string[] = []): string[] {
  if (typeof value === "string") acc.push(value);
  else if (Array.isArray(value)) value.forEach((item) => strings(item, acc));
  else if (value !== null && typeof value === "object") Object.values(value).forEach((item) => strings(item, acc));
  return acc;
}

function keys(value: unknown, acc: string[] = []): string[] {
  if (Array.isArray(value)) value.forEach((item) => keys(item, acc));
  else if (value !== null && typeof value === "object") {
    for (const [key, item] of Object.entries(value)) {
      acc.push(key);
      keys(item, acc);
    }
  }
  return acc;
}

const allStrings = strings(exported.map(([, value]) => value));

// RFC 5737 (IPv4) and RFC 3849 (IPv6) documentation ranges.
const DOCUMENTATION_IP = /^(192\.0\.2|198\.51\.100|203\.0\.113)\.\d{1,3}$|^2001:db8:/i;
// An IPv6 address with a dotted IPv4 tail (#1981 PR 4b) looks like one too, so the range rule sees it.
const LOOKS_LIKE_IP = /^\d{1,3}(\.\d{1,3}){3}$|^[0-9a-f]{0,4}(:[0-9a-f]{0,4}){2,7}$|^[0-9a-f:]*:[0-9a-f:]*\d{1,3}(\.\d{1,3}){3}$/i;

describe("the admin preview's fixtures (ADR 0150 D5)", () => {
  it("exports rows to check", () => {
    expect(exported.length).toBeGreaterThanOrEqual(5);
  });

  it.each(exported)("%s carries the sentinel on every row", (_name, value) => {
    const rows = Array.isArray(value) ? value : [value];
    expect(rows.length).toBeGreaterThan(0);
    for (const row of rows) expect(JSON.stringify(row)).toContain(ADMIN_PREVIEW_SENTINEL);
  });

  it("uses addresses on the sentinel's reserved domain, or a name under it, only", () => {
    const addresses = allStrings.filter((value) => /@/.test(value));
    const domain = (value: string) => value.slice(value.lastIndexOf("@") + 1);
    expect(addresses.length).toBeGreaterThan(0);
    expect(addresses.filter((value) =>
      domain(value) !== ADMIN_PREVIEW_SENTINEL && !domain(value).endsWith(`.${ADMIN_PREVIEW_SENTINEL}`))).toEqual([]);
  });

  it("uses documentation IP ranges only", () => {
    const ips = allStrings.filter((value) => LOOKS_LIKE_IP.test(value));
    expect(ips.length).toBeGreaterThan(0);
    expect(ips.filter((value) => !DOCUMENTATION_IP.test(value))).toEqual([]);
  });

  it("sees an address with a dotted IPv4 tail, and holds it to the documentation ranges", () => {
    expect(allStrings.filter((value) => LOOKS_LIKE_IP.test(value))).toContain("2001:db8::5efe:192.0.2.1");
    expect(LOOKS_LIKE_IP.test("::ffff:10.1.2.3")).toBe(true);
    expect(DOCUMENTATION_IP.test("::ffff:10.1.2.3")).toBe(false);
  });

  it("has no name field, because an account stores none (ADR 0150 D3)", () => {
    expect(keys(exported.map(([, value]) => value)).filter((key) => /name/i.test(key))).toEqual([]);
  });

  it("names no consumer mail domain and none of the handoff's infrastructure", () => {
    expect(
      allStrings.filter((value) => /gmail|hotmail|outlook|live\.se|icloud|telia|yahoo|netcup|strato|n[uü]rnberg/i.test(value)),
    ).toEqual([]);
  });
});

/** A fixture as the API would send it: the sentinel each row carries is the preview's, not the contract's. */
function unmarked(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(unmarked);
  if (value === null || typeof value !== "object") return value;
  return Object.fromEntries(Object.entries(value).filter(([key]) => key !== "fixture").map(([key, item]) => [key, unmarked(item)]));
}

describe("the e-post preview's count per mail kind (#1981 PR 3, ADR 0150 D5)", () => {
  const answers = Object.entries(fixtures.PREVIEW_EMAIL_KINDS)
    .filter(([scenario]) => scenario !== "fixture")
    .flatMap(([scenario, byPeriod]) => Object.entries(byPeriod)
      .filter(([period]) => period !== "fixture")
      .map(([period, answer]) => [`${scenario} ${period}`, answer] as const));

  it.each(answers)("answers %s in a form the API's contract admits", (_name, answer) => {
    expect(emailKindsSchema.safeParse(unmarked(answer)).error).toBeUndefined();
  });

  it("covers every state an answer has, and every failure class", () => {
    const parsed = answers.map(([, answer]) => emailKindsSchema.parse(unmarked(answer)));
    expect(new Set(parsed.map((answer) => answer.status))).toEqual(new Set(["Observed", "NotObserved", "NotObservable"]));
    expect(new Set(parsed.flatMap((answer) => answer.status === "Observed" ? [answer.coverage] : []))).toEqual(new Set(["Complete", "Partial"]));
    const classes = parsed.flatMap((answer) => answer.kinds ?? []).flatMap((row) => row.latestFailure?.outcomes ?? []);
    expect(new Set(classes)).toEqual(new Set(["Refused", "RateLimited", "Unreachable", "ProviderError", "Timeout", "Other"]));
  });
});

describe("the e-post preview's DMARC reports (#1981 PR 4b, ADR 0150 D5)", () => {
  const answers = Object.entries(fixtures.PREVIEW_EMAIL_DMARC).filter(([scenario]) => scenario !== "fixture");

  it.each(answers)("answers %s in a form the API's contract admits", (_name, answer) => {
    expect(emailDmarcSchema.safeParse(unmarked(answer)).error).toBeUndefined();
  });

  it("covers every state, every failure class before a success and after one, the cap, and every line", () => {
    const parsed = answers.map(([, answer]) => emailDmarcSchema.parse(unmarked(answer)));
    expect(new Set(parsed.map((answer) => answer.status))).toEqual(
      new Set(["Observed", "NoReports", "Failed", "NotObservable", "NotConfigured", "Misconfigured"]),
    );
    const failed = parsed.flatMap((answer) => (answer.status === "Failed" ? [answer.ingestion.latestFailure] : []));
    expect(new Set(failed)).toEqual(new Set(DMARC_LATEST_FAILURES));
    const observed = parsed.flatMap((answer) => (answer.status === "Observed" ? [answer] : []));
    expect(observed.some((answer) => answer.ingestion.latestFailure !== null && answer.ingestion.signInResumesAt !== null)).toBe(true);
    expect(observed.some((answer) => answer.ingestion.stale && answer.ingestion.latestFailure === null)).toBe(true);
    expect(observed.some((answer) => answer.sourceCount > answer.sources.length)).toBe(true);
    expect(observed.some((answer) => answer.conflictingReports > 0)).toBe(true);
    for (const count of ["skipped", "rejected", "volumeCapped", "abandoned", "retryPending"] as const) {
      expect(observed.some((answer) => answer.mailbox[count] > 0), count).toBe(true);
    }
    const sources = observed.flatMap((answer) => answer.sources);
    expect(new Set(sources.map((source) => source.flagged))).toEqual(new Set([true, false]));
    expect(sources.every((source) => source.headerFrom === "jobbliggaren.se" || source.headerFrom.endsWith(".jobbliggaren.se"))).toBe(true);
    expect(new Set(sources.map((source) => source.headerFrom === "jobbliggaren.se"))).toEqual(new Set([true, false]));
    expect(sources.some((source) => source.address.includes(":") && source.address.includes("."))).toBe(true);
  });
});
