import { describe, it, expect } from "vitest";
import { createTranslator } from "next-intl";
import {
  makeCreateApplicationSchema,
  makeTransitionStatusSchema,
  makeAddFollowUpSchema,
  makeAddNoteSchema,
} from "./application-schemas";
import svValidation from "../../../messages/sv/validation.json";

// Real next-intl translator scoped to the `validation` namespace (Swedish
// catalog = source of truth). In production the factories receive this `t` from
// `useTranslations("validation")` / `getTranslations("validation")`.
const t = createTranslator({
  locale: "sv",
  messages: { validation: svValidation },
  namespace: "validation",
});

const createApplicationSchema = makeCreateApplicationSchema(t);
const transitionStatusSchema = makeTransitionStatusSchema(t);
const addFollowUpSchema = makeAddFollowUpSchema(t);
const addNoteSchema = makeAddNoteSchema(t);

const VALID_GUID = "550e8400-e29b-41d4-a716-446655440000";

// Manuell ansökan (jobAdId == null): Jobbtitel + Företag obligatoriska.
// Annonslänk scheme-validerad, Sista ansökningsdag + personligt brev frivilliga.
// Inget Källa-fält (Source struken — manuell ansökan är implicit Source=Manual).
const VALID_CREATE = {
  title: "Backend-utvecklare",
  company: "Volvo",
};

describe("createApplicationSchema", () => {
  it("accepts a valid minimal application (title + company)", () => {
    expect(createApplicationSchema.safeParse(VALID_CREATE).success).toBe(true);
  });

  it("rejects empty title with 'Jobbtitel'-message", () => {
    const result = createApplicationSchema.safeParse({
      ...VALID_CREATE,
      title: "",
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues[0]?.message).toMatch(/Jobbtitel/);
    }
  });

  it("rejects whitespace-only title (trim before min)", () => {
    expect(
      createApplicationSchema.safeParse({ ...VALID_CREATE, title: "   " })
        .success
    ).toBe(false);
  });

  it("rejects empty company", () => {
    const result = createApplicationSchema.safeParse({
      ...VALID_CREATE,
      company: "",
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues[0]?.message).toMatch(/Företag/);
    }
  });

  it("rejects a url with a non-http(s) scheme (javascript:)", () => {
    expect(
      createApplicationSchema.safeParse({
        ...VALID_CREATE,
        url: "javascript:alert(1)",
      }).success
    ).toBe(false);
  });

  it("rejects a url with a data: scheme", () => {
    expect(
      createApplicationSchema.safeParse({
        ...VALID_CREATE,
        url: "data:text/html,<script>1</script>",
      }).success
    ).toBe(false);
  });

  it("accepts a valid https url", () => {
    expect(
      createApplicationSchema.safeParse({
        ...VALID_CREATE,
        url: "https://example.com/jobb/123",
      }).success
    ).toBe(true);
  });

  it("accepts an omitted/empty url (frivilligt)", () => {
    expect(
      createApplicationSchema.safeParse({ ...VALID_CREATE, url: "" }).success
    ).toBe(true);
    expect(createApplicationSchema.safeParse(VALID_CREATE).success).toBe(true);
  });

  it("treats expiresAt as optional and accepts a valid date", () => {
    expect(
      createApplicationSchema.safeParse({ ...VALID_CREATE, expiresAt: "" })
        .success
    ).toBe(true);
    expect(
      createApplicationSchema.safeParse({
        ...VALID_CREATE,
        expiresAt: "2026-06-01",
      }).success
    ).toBe(true);
  });

  it("rejects an invalid expiresAt date", () => {
    expect(
      createApplicationSchema.safeParse({
        ...VALID_CREATE,
        expiresAt: "not-a-date",
      }).success
    ).toBe(false);
  });

  it("keeps coverLetter optional with a 5000-char ceiling", () => {
    expect(
      createApplicationSchema.safeParse({
        ...VALID_CREATE,
        coverLetter: "a".repeat(5000),
      }).success
    ).toBe(true);
    expect(
      createApplicationSchema.safeParse({
        ...VALID_CREATE,
        coverLetter: "a".repeat(5001),
      }).success
    ).toBe(false);
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
