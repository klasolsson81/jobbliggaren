import { describe, it, expect } from "vitest";
import { currentUserSchema, jobSeekerProfileSchema } from "./me";

describe("currentUserSchema", () => {
  const valid = {
    userId: "11111111-1111-1111-1111-111111111111",
    email: "user@example.com",
    roles: ["Admin"],
  };

  it("accepts valid CurrentUser", () => {
    expect(currentUserSchema.safeParse(valid).success).toBe(true);
  });

  it("accepts empty roles array", () => {
    expect(
      currentUserSchema.safeParse({ ...valid, roles: [] }).success
    ).toBe(true);
  });

  it("rejects when roles missing (TD-7 original Major 1 regression)", () => {
    const withoutRoles: Partial<typeof valid> = { ...valid };
    delete withoutRoles.roles;
    expect(currentUserSchema.safeParse(withoutRoles).success).toBe(false);
  });

  it("rejects roles as null", () => {
    expect(
      currentUserSchema.safeParse({ ...valid, roles: null }).success
    ).toBe(false);
  });

  it("rejects non-string entries in roles array", () => {
    expect(
      currentUserSchema.safeParse({ ...valid, roles: [1, 2] }).success
    ).toBe(false);
  });

  it("rejects when userId missing", () => {
    const withoutUserId: Partial<typeof valid> = { ...valid };
    delete withoutUserId.userId;
    expect(currentUserSchema.safeParse(withoutUserId).success).toBe(false);
  });
});

describe("jobSeekerProfileSchema", () => {
  const valid = {
    id: "22222222-2222-2222-2222-222222222222",
    displayName: "Anna",
    language: "sv",
    emailNotifications: true,
    weeklySummary: false,
    createdAt: "2026-05-11T10:00:00Z",
    // F4-12 PR-B (ADR 0076) — matchnings-önskemål + härlett nudge-flagg.
    hasStatedDesiredOccupation: false,
    preferredOccupationGroups: [],
    preferredRegions: [],
    // Spår 3 PR-D — kommun-axeln (required, ej optional).
    preferredMunicipalities: [],
    preferredEmploymentTypes: [],
    // STEG 3 / ADR 0079 — kompetens-axeln + erfarenhet (required; nullable int).
    preferredSkills: [],
    experienceYears: null,
  };

  it("accepts valid profile", () => {
    expect(jobSeekerProfileSchema.safeParse(valid).success).toBe(true);
  });

  it("accepts a stated experienceYears integer", () => {
    expect(
      jobSeekerProfileSchema.safeParse({ ...valid, experienceYears: 5 }).success
    ).toBe(true);
  });

  it("rejects when preferredSkills missing (kontraktsdrift)", () => {
    const withoutSkills: Partial<typeof valid> = { ...valid };
    delete withoutSkills.preferredSkills;
    expect(jobSeekerProfileSchema.safeParse(withoutSkills).success).toBe(false);
  });

  it("rejects when preferredMunicipalities missing (kontraktsdrift)", () => {
    const withoutMunicipalities: Partial<typeof valid> = { ...valid };
    delete withoutMunicipalities.preferredMunicipalities;
    expect(
      jobSeekerProfileSchema.safeParse(withoutMunicipalities).success
    ).toBe(false);
  });

  it("rejects when emailNotifications is non-boolean", () => {
    expect(
      jobSeekerProfileSchema.safeParse({
        ...valid,
        emailNotifications: "true",
      }).success
    ).toBe(false);
  });

  it("rejects when createdAt missing", () => {
    const withoutCreatedAt: Partial<typeof valid> = { ...valid };
    delete withoutCreatedAt.createdAt;
    expect(jobSeekerProfileSchema.safeParse(withoutCreatedAt).success).toBe(
      false
    );
  });
});
