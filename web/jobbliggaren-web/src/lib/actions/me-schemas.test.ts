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

describe("updateMyProfileSchema", () => {
  it("accepts language=sv", () => {
    expect(updateMyProfileSchema.safeParse({ language: "sv" }).success).toBe(true);
  });

  it("accepts language=en", () => {
    expect(updateMyProfileSchema.safeParse({ language: "en" }).success).toBe(true);
  });

  it("rejects unsupported language", () => {
    expect(updateMyProfileSchema.safeParse({ language: "fr" }).success).toBe(false);
  });

  it("rejects an EMPTY payload", () => {
    // A save that changes nothing would no-op on the server, return 200, and let the card stamp
    // "Sparat" for a change that never happened.
    expect(updateMyProfileSchema.safeParse({}).success).toBe(false);
  });

  it("strips a display name instead of passing it on", () => {
    // No page writes the account name since #1740, so the schema does not carry one: a name in
    // the payload is dropped before the action sends anything.
    const result = updateMyProfileSchema.safeParse({
      language: "sv",
      displayName: "Anna Andersson",
    });
    expect(result.success).toBe(true);
    if (result.success) expect(result.data).toEqual({ language: "sv" });
  });

  // TD-115: the emailNotifications/weeklySummary fields were retired from this
  // schema (they gated no email path) — their non-boolean rejection tests are gone.
});
