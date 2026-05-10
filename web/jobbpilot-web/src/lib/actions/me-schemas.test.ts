import { describe, it, expect } from "vitest";
import { updateMyProfileSchema } from "./me-schemas";

const base = {
  displayName: "Anna Andersson",
  language: "sv" as const,
  emailNotifications: true,
  weeklySummary: false,
};

describe("updateMyProfileSchema", () => {
  it("accepts valid profile", () => {
    expect(updateMyProfileSchema.safeParse(base).success).toBe(true);
  });

  it("rejects empty displayName", () => {
    expect(
      updateMyProfileSchema.safeParse({ ...base, displayName: "" }).success
    ).toBe(false);
  });

  it("trims whitespace from displayName", () => {
    const result = updateMyProfileSchema.safeParse({
      ...base,
      displayName: "  Anna  ",
    });
    expect(result.success).toBe(true);
    if (result.success) expect(result.data.displayName).toBe("Anna");
  });

  it("rejects displayName longer than 200 chars", () => {
    expect(
      updateMyProfileSchema.safeParse({
        ...base,
        displayName: "a".repeat(201),
      }).success
    ).toBe(false);
  });

  it("accepts displayName at exactly 200 chars (boundary)", () => {
    expect(
      updateMyProfileSchema.safeParse({
        ...base,
        displayName: "a".repeat(200),
      }).success
    ).toBe(true);
  });

  it("accepts language=sv", () => {
    expect(
      updateMyProfileSchema.safeParse({ ...base, language: "sv" }).success
    ).toBe(true);
  });

  it("accepts language=en", () => {
    expect(
      updateMyProfileSchema.safeParse({ ...base, language: "en" }).success
    ).toBe(true);
  });

  it("rejects unsupported language", () => {
    expect(
      updateMyProfileSchema.safeParse({ ...base, language: "fr" }).success
    ).toBe(false);
  });

  it("rejects non-boolean emailNotifications", () => {
    expect(
      updateMyProfileSchema.safeParse({
        ...base,
        emailNotifications: "true",
      }).success
    ).toBe(false);
  });

  it("rejects non-boolean weeklySummary", () => {
    expect(
      updateMyProfileSchema.safeParse({ ...base, weeklySummary: 1 }).success
    ).toBe(false);
  });
});
