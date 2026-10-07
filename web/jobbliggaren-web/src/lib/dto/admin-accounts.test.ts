import { describe, expect, it } from "vitest";
import {
  accountSearchResponseSchema,
  pendingEmailChangeReadSchema,
  toAccountsPage,
  toPendingEmailChange,
} from "./admin-accounts";

const ITEM = {
  id: "0192f3a4-5b6c-7d8e-9f01-23456789abcd",
  email: "konto.a@example.test",
  role: "User",
  status: "Active",
  emailConfirmed: true,
  isSuspended: false,
  registeredAt: "2026-09-28T12:02:00Z",
  deletionEarliest: null,
  applicationCount: 4,
};

function answer(items: ReadonlyArray<Record<string, unknown>>) {
  return {
    accounts: { items, totalCount: items.length, page: 1, pageSize: 25, totalPages: 1 },
    counts: { total: 10, active: 4, suspended: 3, pendingDeletion: 1, profileMissing: 2 },
  };
}

describe("the account directory's wire shapes (#1974, ADR 0151)", () => {
  it("maps each count to its own filter", () => {
    const page = toAccountsPage(accountSearchResponseSchema.parse(answer([ITEM])));

    expect(page.counts).toEqual({ all: 10, active: 4, suspended: 3, pendingDeletion: 1, profileMissing: 2 });
  });

  it("reads a suspended account and its separate access flag", () => {
    const page = toAccountsPage(accountSearchResponseSchema.parse(answer([{ ...ITEM, status: "Suspended", isSuspended: true }])));

    expect(page.rows[0]?.status).toBe("suspended");
    expect(page.rows[0]?.isSuspended).toBe(true);
  });

  it("retains suspension independently when a pending deletion takes status priority", () => {
    const page = toAccountsPage(accountSearchResponseSchema.parse(answer([
      { ...ITEM, status: "PendingDeletion", isSuspended: true, deletionEarliest: "2026-11-07" },
    ])));

    expect(page.rows[0]).toMatchObject({ status: "pendingDeletion", isSuspended: true, deletionEarliest: "2026-11-07" });
  });

  it("reads an id outside RFC 9562's version bits instead of failing the page", () => {
    // Declared unreachable: the provider's Guid key generator mints version 7 ids. The column and the
    // backend's route take any GUID, so this pins only that one row of another shape cannot fail the page.
    const outside = { ...ITEM, id: "00000000-0000-0000-0000-000000000001" };

    expect(accountSearchResponseSchema.safeParse(answer([outside, ITEM])).success).toBe(true);
  });
});

describe("the pending address change's wire shape (#1975, ADR 0153)", () => {
  const INSTANTS = { completableFrom: "2026-10-08T12:00:00+00:00", expiresAt: "2026-10-09T12:00:00+00:00" };

  it.each([
    ["CodeBurned", "codeBurned"],
    ["Pending", "pending"],
  ] as const)("reads the state %s as %s, with both instants as sent", (wire, state) => {
    const read = pendingEmailChangeReadSchema.parse({ pending: { state: wire, ...INSTANTS } });

    expect(read.pending === null ? null : toPendingEmailChange(read.pending)).toEqual({ state, ...INSTANTS });
  });
});
