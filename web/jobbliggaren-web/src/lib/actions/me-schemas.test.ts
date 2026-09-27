import { describe, it, expect } from "vitest";
import { createTranslator } from "next-intl";
import {
  makeUpdateDigestCadenceSchema,
  makeUpdateFollowedCompanyNotificationConsentSchema,
  makeUpdateMyProfileSchema,
  makeUpdateNotificationConsentSchema,
} from "./me-schemas";
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

// Each notification setting has its own schema, and each refuses the others' keys: a tab on an
// earlier build may still post the combined `{ enabled, cadence }` body, and cutting it down to one
// field would turn a cadence click into a consent write.
describe("the notification-settings schemas", () => {
  const consent = makeUpdateNotificationConsentSchema(t);
  const followConsent = makeUpdateFollowedCompanyNotificationConsentSchema(t);
  const cadence = makeUpdateDigestCadenceSchema(t);

  it("each accepts its own value", () => {
    expect(consent.safeParse({ enabled: false }).success).toBe(true);
    expect(followConsent.safeParse({ enabled: true }).success).toBe(true);
    expect(cadence.safeParse({ cadence: "Daily" }).success).toBe(true);
  });

  it("the consent schemas refuse a cadence, rather than dropping it", () => {
    expect(consent.safeParse({ enabled: true, cadence: "Daily" }).success).toBe(false);
    expect(followConsent.safeParse({ enabled: true, cadence: "Daily" }).success).toBe(false);
  });

  it("the cadence schema refuses a consent value, rather than dropping it", () => {
    expect(cadence.safeParse({ cadence: "Daily", enabled: true }).success).toBe(false);
  });

  it("each refuses an empty body and a wrong type", () => {
    expect(consent.safeParse({}).success).toBe(false);
    expect(followConsent.safeParse({}).success).toBe(false);
    expect(cadence.safeParse({}).success).toBe(false);
    expect(consent.safeParse({ enabled: "true" }).success).toBe(false);
    expect(cadence.safeParse({ cadence: "Hourly" }).success).toBe(false);
  });
});
