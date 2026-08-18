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

  // #1117 — the schema became a PARTIAL update: a field that is not being changed is not sent
  // at all, because the display name now carries a server-side invariant that is re-evaluated on
  // every write. Without these, backing out either `.optional()` leaves the suite green while a
  // language change dies for any user whose stored name the invariant would refuse.
  it("accepts a language-only payload (the display name is not being changed)", () => {
    expect(updateMyProfileSchema.safeParse({ language: "en" }).success).toBe(true);
  });

  it("accepts a displayName-only payload (the language is not being changed)", () => {
    expect(
      updateMyProfileSchema.safeParse({ displayName: "Anna Andersson" }).success
    ).toBe(true);
  });

  it("rejects an EMPTY payload", () => {
    // A save that changes nothing would no-op on the server, return 200, and let the card stamp
    // "Sparat" for a change that never happened. Refused in the contract so no future control
    // can reintroduce that by forgetting to pass a field.
    expect(updateMyProfileSchema.safeParse({}).success).toBe(false);
  });

  it("still rejects a present-but-invalid field in a partial payload", () => {
    // Optional means "may be absent", never "may be anything": the min/max still apply
    // whenever the key is there, which is what the schema comment promises.
    expect(updateMyProfileSchema.safeParse({ displayName: "" }).success).toBe(false);
    expect(
      updateMyProfileSchema.safeParse({ displayName: "a".repeat(201) }).success
    ).toBe(false);
    expect(updateMyProfileSchema.safeParse({ language: "de" }).success).toBe(false);
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
