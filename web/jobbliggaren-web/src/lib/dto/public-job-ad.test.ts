import { describe, expect, it } from "vitest";
import { publicJobAdDtoSchema } from "./public-job-ad";

const WIRE = {
  id: "0b0b0b0b-1111-4222-8333-444444444444",
  title: "Sjuksköterska",
  companyName: "Region Exempel",
  description: "Vi söker en sjuksköterska.",
  url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
  publishedAt: "2026-10-01T08:00:00+00:00",
  expiresAt: "2026-11-01T08:00:00+00:00",
  municipalityConceptId: "m1",
  municipalityName: "Kommunen",
  regionConceptId: "r1",
  regionName: "Länet",
  occupationGroupConceptId: "o1",
  employmentTypeConceptId: "e1",
  worktimeExtentConceptId: "w1",
};

describe("publicJobAdDtoSchema", () => {
  it("reads the eleven members the page uses and strips the rest of the wire (the occupation group stays unread)", () => {
    const parsed = publicJobAdDtoSchema.parse(WIRE);

    expect(Object.keys(parsed).sort()).toEqual(
      [
        "companyName",
        "description",
        "expiresAt",
        "id",
        "publishedAt",
        "title",
        "url",
        "municipalityConceptId",
        "municipalityName",
        "regionConceptId",
        "regionName",
        "employmentTypeConceptId",
        "worktimeExtentConceptId",
      ].sort()
    );
    expect("occupationGroupConceptId" in parsed).toBe(false);
  });

  it("accepts a null name (an id the snapshot does not know) and an employment id the catalogue does not know", () => {
    const parsed = publicJobAdDtoSchema.parse({
      ...WIRE,
      municipalityName: null,
      regionName: null,
      employmentTypeConceptId: "drifted_id",
      worktimeExtentConceptId: null,
    });
    expect(parsed.municipalityName).toBeNull();
    expect(parsed.employmentTypeConceptId).toBe("drifted_id");
  });

  it("strips a member nobody asked for, so a field added to the wire later cannot reach the page", () => {
    const parsed = publicJobAdDtoSchema.parse({
      ...WIRE,
      contacts: [{ name: "Anna", email: "a@example.test" }],
      organizationNumber: "5560000000",
      rawPayload: "{}",
      extractedTerms: ["x"],
    });

    expect(JSON.stringify(parsed)).not.toMatch(/Anna|a@example|5560000000|rawPayload|extractedTerms|contacts/);
  });

  it("accepts an ad with no deadline", () => {
    expect(publicJobAdDtoSchema.parse({ ...WIRE, expiresAt: null }).expiresAt).toBeNull();
  });

  it.each(["javascript:alert(1)", "data:text/html,x", "vbscript:x", "//evil.test", "ftp://x.test"])(
    "refuses the url %s, which would end up in an href",
    (url) => {
      expect(publicJobAdDtoSchema.safeParse({ ...WIRE, url }).success).toBe(false);
    }
  );

  it.each([
    ["an id that is not a GUID", { id: "not-a-guid" }],
    ["a date that does not read", { publishedAt: "yesterday" }],
    ["a missing title", { title: undefined }],
    ["a missing description", { description: undefined }],
  ])("refuses %s", (_label, patch) => {
    expect(publicJobAdDtoSchema.safeParse({ ...WIRE, ...patch }).success).toBe(false);
  });
});
