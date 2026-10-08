import { describe, expect, it } from "vitest";
import { accountDeletionReceiptSchema } from "@/lib/dto/admin-accounts";

describe("account deletion receipt", () => {
  const receipt = { userId: "00000000-0000-4000-8000-000000000007", deletedAt: "2026-10-08T12:00:00Z", eligibleAt: "2026-11-07T12:00:00Z", scheduledRunAt: "2026-11-08T04:00:00Z" };

  it("keeps all actual scheduling instants", () => {
    expect(accountDeletionReceiptSchema.parse(receipt)).toEqual(receipt);
  });

  it.each([
    { eligibleAt: receipt.deletedAt },
    { scheduledRunAt: receipt.eligibleAt },
    { scheduledRunAt: "yesterday" },
    { userId: "../other-account" },
  ])("rejects inadmissible receipt fields %j", (delta) => {
    expect(accountDeletionReceiptSchema.safeParse({ ...receipt, ...delta }).success).toBe(false);
  });
});
