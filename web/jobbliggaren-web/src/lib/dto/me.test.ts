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
  };

  it("accepts valid profile", () => {
    expect(jobSeekerProfileSchema.safeParse(valid).success).toBe(true);
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
