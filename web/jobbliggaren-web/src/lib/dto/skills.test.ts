import { describe, it, expect } from "vitest";
import { skillOptionSchema, skillOptionsSchema } from "./skills";

describe("skillOptionSchema (STEG 3 / ADR 0079)", () => {
  it("accepterar ett giltigt {conceptId, label}", () => {
    expect(
      skillOptionSchema.safeParse({ conceptId: "skill_react", label: "React" })
        .success
    ).toBe(true);
  });

  it("avvisar tom label", () => {
    expect(
      skillOptionSchema.safeParse({ conceptId: "skill_react", label: "" }).success
    ).toBe(false);
  });

  it("avvisar conceptId med ogiltiga tecken (defense-in-depth)", () => {
    expect(
      skillOptionSchema.safeParse({ conceptId: "bad id!", label: "X" }).success
    ).toBe(false);
  });

  it("avvisar conceptId över 32 tecken", () => {
    expect(
      skillOptionSchema.safeParse({ conceptId: "a".repeat(33), label: "X" })
        .success
    ).toBe(false);
  });
});

describe("skillOptionsSchema", () => {
  it("accepterar en tom lista", () => {
    expect(skillOptionsSchema.safeParse([]).success).toBe(true);
  });

  it("accepterar en lista av giltiga optioner", () => {
    expect(
      skillOptionsSchema.safeParse([
        { conceptId: "skill_sql", label: "SQL" },
        { conceptId: "skill_react", label: "React" },
      ]).success
    ).toBe(true);
  });
});
