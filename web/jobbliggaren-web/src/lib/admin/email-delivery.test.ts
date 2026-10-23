import { describe, expect, it } from "vitest";
import type { EmailDeliveryDto, EmailFailuresDto } from "@/lib/dto/admin-email";
import {
  EMAIL_DELIVERY_PERIODS,
  emailDeliveryPeriod,
  emailReadLine,
  failureMinute,
  isAlertingRead,
  isSharedCause,
  withEmailPeriod,
} from "./email-delivery";

const NO_FIGURES = { observedAt: null, sent: null, failed: null } as const;
const observed: EmailDeliveryDto = {
  period: "h24", status: "Observed", reason: null, observedAt: "2026-10-19T18:59:46+00:00", sent: 57, failed: 2,
};

describe("emailDeliveryPeriod (#1981 PR 2)", () => {
  it.each([
    ["h24", "h24"],
    ["d3", "d3"],
    ["d7", "d7"],
  ] as const)("reads %s as itself", (value, period) => {
    expect(emailDeliveryPeriod(value)).toBe(period);
  });

  it.each([
    ["no parameter", undefined],
    ["an empty value", ""],
    ["another case", "H24"],
    ["a period the page does not offer", "d30"],
    ["a time", "2026-10-19T00:00:00Z"],
    ["an empty repeat", []],
  ] as const)("reads %s as 7 dygn", (_name, value) => {
    expect(emailDeliveryPeriod(value)).toBe("d7");
  });

  it("reads a repeated parameter by its first value", () => {
    expect(emailDeliveryPeriod(["h24", "d3"])).toBe("h24");
    expect(emailDeliveryPeriod(["x", "d3"])).toBe("d7");
  });

  it("offers the three periods, shortest first", () => {
    expect(EMAIL_DELIVERY_PERIODS).toEqual(["h24", "d3", "d7"]);
  });
});

describe("withEmailPeriod", () => {
  it("writes every period out, the default too", () => {
    expect(withEmailPeriod("/admin/e-post", "d7")).toBe("/admin/e-post?period=d7");
    expect(withEmailPeriod("/api/v1/admin/email/delivery", "h24")).toBe("/api/v1/admin/email/delivery?period=h24");
  });
});

describe("emailReadLine", () => {
  it("has no line for an observed read: it shows its figures", () => {
    expect(emailReadLine(observed)).toBeNull();
  });

  it.each([
    [{ period: "h24", status: "NotObserved", reason: "NotConfigured", ...NO_FIGURES }, "notConfigured"],
    [{ period: "h24", status: "NotObservable", reason: null, ...NO_FIGURES }, "notObservable"],
    [{ period: "h24", status: "ReadKeyMissing", reason: null, ...NO_FIGURES }, "readKeyMissing"],
    [{ period: "h24", status: "Failed", reason: "Timeout", ...NO_FIGURES }, "failure.noAnswer"],
    [{ period: "h24", status: "Failed", reason: "Unreachable", ...NO_FIGURES }, "failure.noAnswer"],
    [{ period: "h24", status: "Failed", reason: "ProviderError", ...NO_FIGURES }, "failure.noAnswer"],
    [{ period: "h24", status: "Failed", reason: "RateLimited", ...NO_FIGURES }, "failure.rateLimited"],
    [{ period: "h24", status: "Failed", reason: "Unauthorized", ...NO_FIGURES }, "failure.keyRefused"],
    [{ period: "h24", status: "Failed", reason: "Forbidden", ...NO_FIGURES }, "failure.readRefused"],
    [{ period: "h24", status: "Failed", reason: "Malformed", ...NO_FIGURES }, "failure.malformed"],
  ] as const)("words %o by %s, the allowance's own line", (read, line) => {
    expect(emailReadLine(read)).toBe(line);
  });

  it("words a failures read by the same lines", () => {
    const failures: EmailFailuresDto = { period: "d3", status: "ReadKeyMissing", reason: null, observedAt: null, failures: null, limit: 10 };
    expect(emailReadLine(failures)).toBe("readKeyMissing");
  });
});

describe("isAlertingRead", () => {
  it.each([
    ["an observed read", observed, false],
    ["a deployment without Scaleway", { period: "h24", status: "NotObserved", reason: "NotConfigured", ...NO_FIGURES }, false],
    ["a read key that sees no sends", { period: "h24", status: "NotObservable", reason: null, ...NO_FIGURES }, true],
    ["a read key that is not installed", { period: "h24", status: "ReadKeyMissing", reason: null, ...NO_FIGURES }, true],
    ["a failed read", { period: "h24", status: "Failed", reason: "Forbidden", ...NO_FIGURES }, true],
  ] as const)("says %s is %s", (_name, read, alerting) => {
    expect(isAlertingRead(read)).toBe(alerting);
  });
});

describe("failureMinute", () => {
  it.each([
    ["2026-10-19T14:32:51+00:00", "2026-10-19T14:32Z"],
    ["2026-10-19T14:32:00Z", "2026-10-19T14:32Z"],
    ["2026-10-19T14:32:59.9999999+00:00", "2026-10-19T14:32Z"],
    ["2026-10-19T16:32:51+02:00", "2026-10-19T14:32Z"],
  ])("keeps %s to the minute, in UTC, as %s", (createdAt, minute) => {
    expect(failureMinute(createdAt)).toBe(minute);
  });
});

describe("isSharedCause (#1981 PR 3)", () => {
  const refused = { kind: "failed", line: "Din session saknar Admin-rollen." } as const;
  const loaded = { kind: "loaded", data: observed } as const;

  it.each([
    ["every region pending", [{ kind: "loading" }, { kind: "loading" }, { kind: "loading" }]],
    ["every region refused for the same session", [refused, refused, refused]],
  ] as const)("is shared with %s", (_name, reads) => {
    expect(isSharedCause(reads)).toBe(true);
  });

  it.each([
    ["every read failed on its own", [{ kind: "failed" }, { kind: "failed" }, { kind: "failed" }]],
    ["one region answered", [refused, refused, loaded]],
    ["one region pending", [{ kind: "loading" }, { kind: "loading" }, refused]],
    ["two refusals that differ", [refused, refused, { kind: "failed", line: "Du är inte inloggad längre." }]],
    ["every region answered", [loaded, loaded, loaded]],
  ] as const)("is not shared where %s", (_name, reads) => {
    expect(isSharedCause(reads)).toBe(false);
  });
});
