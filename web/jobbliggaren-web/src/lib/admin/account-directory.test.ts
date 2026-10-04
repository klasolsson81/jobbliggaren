import { describe, expect, it } from "vitest";
import { failureOf, wireSort, wireStatus } from "./account-directory";

describe("the account list's wire names (#1974, ADR 0151)", () => {
  it.each([
    ["email", "ascending", "AddressAscending"],
    ["email", "descending", "AddressDescending"],
    ["registeredAt", "descending", "RegisteredNewest"],
    ["registeredAt", "ascending", "RegisteredOldest"],
  ] as const)("asks for %s %s as %s", (key, direction, wire) => {
    expect(wireSort({ key, direction })).toBe(wire);
  });

  it.each([
    ["all", undefined],
    ["active", "Active"],
    ["pendingDeletion", "PendingDeletion"],
    ["profileMissing", "ProfileMissing"],
  ] as const)("filters %s as %s", (filter, wire) => {
    expect(wireStatus(filter)).toBe(wire);
  });

  it.each([
    [{ kind: "rateLimited", retryAfterSeconds: 7 }, { reason: "rateLimited", retryAfterSeconds: 7 }],
    [{ kind: "unauthorized" }, { reason: "unauthorized" }],
    [{ kind: "forbidden" }, { reason: "forbidden" }],
    [{ kind: "notFound" }, { reason: "error" }],
    [{ kind: "error" }, { reason: "error" }],
  ] as const)("words a failed read %o as %o", (result, failure) => {
    expect(failureOf(result)).toEqual(failure);
  });
});
