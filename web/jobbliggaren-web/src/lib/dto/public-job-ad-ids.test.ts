import { describe, expect, it } from "vitest";
import { publicJobAdIdsSchema } from "./public-job-ad-ids";

// ADR 0174 — a sitemap part's wire: ids alone, each in the one spelling a job page's URL has.

const ID = "0b0b0b0b-1111-4222-8333-444444444444";

describe("publicJobAdIdsSchema", () => {
  it("accepts lower-case ids, and an empty part", () => {
    expect(publicJobAdIdsSchema.safeParse({ ids: [ID] }).success).toBe(true);
    expect(publicJobAdIdsSchema.safeParse({ ids: [] }).success).toBe(true);
  });

  it.each([
    ["an upper-case id, which is not the canonical spelling", { ids: [ID.toUpperCase()] }],
    ["an id with hex digits in capitals", { ids: ["ABCDEFAB-ABCD-4ABC-8ABC-ABCDEFABCDEF"] }],
    ["something that is not an id", { ids: ["../admin"] }],
    ["a member besides ids", { ids: [ID], asOf: "2026-10-18T12:00:00Z" }],
    ["no ids at all", {}],
    ["ids that are not a list", { ids: ID }],
  ])("refuses %s", (_, body) => {
    expect(publicJobAdIdsSchema.safeParse(body).success).toBe(false);
  });
});
