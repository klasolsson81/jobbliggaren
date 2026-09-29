import { describe, expect, it } from "vitest";
import { createTranslator } from "next-intl";
import svValidation from "../../../messages/sv/validation.json";
import { makeUpdateMatchPreferencesSchema } from "./match-preferences-schemas";

// The PATCH body's grammar (ADR 0147 D1–D3), row by row. A default or an `.optional()` on a
// member of a present part would let a forgotten member bind to a clearing value; these rows are
// what keeps that out (dotnet-architect R10, senior-cto-advisor 6.3).

const t = createTranslator({
  locale: "sv",
  messages: { validation: svValidation },
  namespace: "validation",
});
const schema = makeUpdateMatchPreferencesSchema(t);

const ok = (body: unknown) => schema.safeParse(body).success;

describe("the PATCH body: which parts it carries", () => {
  it.each([
    ["occupations", { occupations: { preferredOccupationGroups: ["grp_a"] } }],
    ["skills", { skills: { preferredSkills: ["sk_a"] } }],
    [
      "locations",
      {
        locations: {
          preferredRegions: ["r_a"],
          preferredMunicipalities: [],
          preferredRemote: false,
        },
      },
    ],
    ["employmentTypes", { employmentTypes: { preferredEmploymentTypes: ["et_a"] } }],
    ["experience", { experience: { experienceYears: 5 } }],
  ])("admits %s alone", (_part, body) => {
    expect(ok(body)).toBe(true);
  });

  it("admits the rail's four parts without experience", () => {
    expect(
      ok({
        occupations: { preferredOccupationGroups: [], preferredOccupationExperience: [] },
        skills: { preferredSkills: [] },
        locations: { preferredRegions: [], preferredMunicipalities: [], preferredRemote: true },
        employmentTypes: { preferredEmploymentTypes: [] },
      })
    ).toBe(true);
  });

  it("refuses a body with no part", () => {
    expect(ok({})).toBe(false);
  });

  it("refuses a member the endpoint does not know", () => {
    expect(ok({ skills: { preferredSkills: [] }, profile: {} })).toBe(false);
  });

  it("refuses the flat body of the full PUT", () => {
    expect(
      ok({
        preferredOccupationGroups: [],
        preferredRegions: [],
        preferredMunicipalities: [],
        preferredRemote: false,
        preferredEmploymentTypes: [],
        preferredSkills: [],
        experienceYears: null,
        preferredOccupationExperience: [],
      })
    ).toBe(false);
  });
});

describe("the PATCH body: the members of a present part", () => {
  it.each([
    ["occupations without its groups", { occupations: {} }],
    ["skills without its list", { skills: {} }],
    ["skills with a null list", { skills: { preferredSkills: null } }],
    [
      "locations without preferredRemote",
      { locations: { preferredRegions: [], preferredMunicipalities: [] } },
    ],
    [
      "locations without municipalities",
      { locations: { preferredRegions: [], preferredRemote: false } },
    ],
    ["employmentTypes without its list", { employmentTypes: {} }],
    ["experience without experienceYears", { experience: {} }],
    ["experience over 70 years", { experience: { experienceYears: 71 } }],
    ["experience under 0 years", { experience: { experienceYears: -1 } }],
    ["experience in a fraction", { experience: { experienceYears: 2.5 } }],
    ["a part carrying a member of another part", { skills: { preferredSkills: [], preferredRemote: true } }],
    ["a concept id outside the pattern", { skills: { preferredSkills: ["not valid!"] } }],
    [
      "a list over the cap of 400",
      { skills: { preferredSkills: Array.from({ length: 401 }, (_, i) => `sk_${i}`) } },
    ],
  ])("refuses %s", (_row, body) => {
    expect(ok(body)).toBe(false);
  });

  it("admits experience null, which clears the stated years", () => {
    expect(ok({ experience: { experienceYears: null } })).toBe(true);
  });

  it("leaves the years overlay out when the caller leaves it out, so the server keeps them", () => {
    const parsed = schema.safeParse({ occupations: { preferredOccupationGroups: ["grp_a"] } });

    expect(parsed.success).toBe(true);
    expect(parsed.data?.occupations).toEqual({ preferredOccupationGroups: ["grp_a"] });
    expect(parsed.data?.occupations).not.toHaveProperty("preferredOccupationExperience");
  });

  it("keeps an empty years overlay, which clears them", () => {
    const parsed = schema.safeParse({
      occupations: { preferredOccupationGroups: ["grp_a"], preferredOccupationExperience: [] },
    });

    expect(parsed.data?.occupations?.preferredOccupationExperience).toEqual([]);
  });

  it("refuses a years entry outside 0..70", () => {
    expect(
      ok({
        occupations: {
          preferredOccupationGroups: ["grp_a"],
          preferredOccupationExperience: [{ conceptId: "grp_a", years: 71 }],
        },
      })
    ).toBe(false);
  });
});
