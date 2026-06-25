import { describe, it, expect } from "vitest";
import { createTranslator } from "next-intl";
import { makeUpdateMyProfileSchema } from "./me-schemas";
import svValidation from "../../../messages/sv/validation.json";

// Real next-intl translator scoped to the `validation` namespace (Swedish
// catalog = source of truth). In production the factory receives this `t` from
// `useTranslations("validation")` / `getTranslations("validation")`.
const t = createTranslator({
  locale: "sv",
  messages: { validation: svValidation },
  namespace: "validation",
});

const updateMyProfileSchema = makeUpdateMyProfileSchema(t);

const base = {
  displayName: "Anna Andersson",
  language: "sv" as const,
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

  // TD-115: the emailNotifications/weeklySummary fields were retired from this
  // schema (they gated no email path) — their non-boolean rejection tests are gone.
});
