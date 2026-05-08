import { describe, it, expect } from "vitest";
import {
  createApplicationSchema,
  transitionStatusSchema,
  addFollowUpSchema,
  addNoteSchema,
} from "./application-schemas";

const VALID_GUID = "550e8400-e29b-41d4-a716-446655440000";

describe("createApplicationSchema", () => {
  it("accepts empty input (coverLetter is optional)", () => {
    expect(createApplicationSchema.safeParse({}).success).toBe(true);
  });

  it("accepts valid coverLetter", () => {
    expect(
      createApplicationSchema.safeParse({ coverLetter: "Jag söker tjänsten som..." }).success
    ).toBe(true);
  });

  it("rejects coverLetter longer than 5000 chars", () => {
    const result = createApplicationSchema.safeParse({
      coverLetter: "a".repeat(5001),
    });
    expect(result.success).toBe(false);
  });

  it("accepts coverLetter at exactly 5000 chars", () => {
    expect(
      createApplicationSchema.safeParse({ coverLetter: "a".repeat(5000) }).success
    ).toBe(true);
  });
});

describe("transitionStatusSchema", () => {
  it("accepts valid applicationId and targetStatus", () => {
    expect(
      transitionStatusSchema.safeParse({
        applicationId: VALID_GUID,
        targetStatus: "Submitted",
      }).success
    ).toBe(true);
  });

  it("rejects invalid GUID format", () => {
    const result = transitionStatusSchema.safeParse({
      applicationId: "not-a-guid",
      targetStatus: "Submitted",
    });
    expect(result.success).toBe(false);
  });

  it("rejects empty targetStatus", () => {
    const result = transitionStatusSchema.safeParse({
      applicationId: VALID_GUID,
      targetStatus: "",
    });
    expect(result.success).toBe(false);
  });
});

describe("addFollowUpSchema", () => {
  it("accepts valid follow-up", () => {
    expect(
      addFollowUpSchema.safeParse({
        applicationId: VALID_GUID,
        channel: "Email",
        scheduledAt: "2026-05-10T10:00:00Z",
      }).success
    ).toBe(true);
  });

  it("accepts all valid channels", () => {
    for (const channel of ["Email", "LinkedIn", "Phone", "Other"]) {
      expect(
        addFollowUpSchema.safeParse({
          applicationId: VALID_GUID,
          channel,
          scheduledAt: "2026-05-10T10:00:00Z",
        }).success
      ).toBe(true);
    }
  });

  it("rejects invalid channel", () => {
    const result = addFollowUpSchema.safeParse({
      applicationId: VALID_GUID,
      channel: "Fax",
      scheduledAt: "2026-05-10T10:00:00Z",
    });
    expect(result.success).toBe(false);
  });

  it("rejects invalid date", () => {
    const result = addFollowUpSchema.safeParse({
      applicationId: VALID_GUID,
      channel: "Email",
      scheduledAt: "not-a-date",
    });
    expect(result.success).toBe(false);
  });

  it("rejects note longer than 1000 chars", () => {
    const result = addFollowUpSchema.safeParse({
      applicationId: VALID_GUID,
      channel: "Email",
      scheduledAt: "2026-05-10T10:00:00Z",
      note: "a".repeat(1001),
    });
    expect(result.success).toBe(false);
  });
});

describe("addNoteSchema", () => {
  it("accepts valid note", () => {
    expect(
      addNoteSchema.safeParse({
        applicationId: VALID_GUID,
        content: "Hade ett bra samtal med rekryteraren.",
      }).success
    ).toBe(true);
  });

  it("rejects empty content", () => {
    const result = addNoteSchema.safeParse({
      applicationId: VALID_GUID,
      content: "",
    });
    expect(result.success).toBe(false);
  });

  it("rejects content longer than 5000 chars", () => {
    const result = addNoteSchema.safeParse({
      applicationId: VALID_GUID,
      content: "a".repeat(5001),
    });
    expect(result.success).toBe(false);
  });
});
