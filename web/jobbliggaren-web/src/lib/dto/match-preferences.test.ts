import { describe, it, expect } from "vitest";
import {
  occupationCandidateSchema,
  occupationDerivationResultSchema,
} from "./match-preferences";

describe("occupationDerivationResultSchema", () => {
  const valid = {
    title: "systemutvecklare",
    candidates: [
      {
        occupationGroupConceptId: "grp_12345",
        occupationGroupLabel: "Mjukvaru- och systemutvecklare",
      },
    ],
  };

  it("accepterar ett giltigt derive-svar", () => {
    const result = occupationDerivationResultSchema.safeParse(valid);
    expect(result.success).toBe(true);
  });

  it("accepterar tomt kandidat-set (inga förslag)", () => {
    const result = occupationDerivationResultSchema.safeParse({
      title: "",
      candidates: [],
    });
    expect(result.success).toBe(true);
  });

  it("strippar okända fält på kandidaten (matchKind/matchedOn) per icke-strikt Zod", () => {
    // Backend skickar matchKind (heltal på wire, ingen global
    // JsonStringEnumConverter) + matchedOn — FE-DTO:n behöver dem inte och
    // strippar dem (ADR 0020 §4). Bevisar att de inte överlever parsen.
    const withExtras = {
      title: "systemutvecklare",
      candidates: [
        {
          occupationGroupConceptId: "grp_12345",
          occupationGroupLabel: "Mjukvaru- och systemutvecklare",
          matchKind: 1,
          matchedOn: "systemutvecklare",
        },
      ],
    };

    const result = occupationDerivationResultSchema.safeParse(withExtras);

    expect(result.success).toBe(true);
    if (result.success) {
      const candidate = result.data.candidates[0]!;
      expect(candidate).toEqual({
        occupationGroupConceptId: "grp_12345",
        occupationGroupLabel: "Mjukvaru- och systemutvecklare",
      });
      expect("matchKind" in candidate).toBe(false);
      expect("matchedOn" in candidate).toBe(false);
    }
  });

  it("avvisar en kandidat utan occupationGroupConceptId (required)", () => {
    const result = occupationDerivationResultSchema.safeParse({
      title: "x",
      candidates: [{ occupationGroupLabel: "Saknar id" }],
    });
    expect(result.success).toBe(false);
  });

  it("avvisar en kandidat utan occupationGroupLabel (required)", () => {
    const result = occupationCandidateSchema.safeParse({
      occupationGroupConceptId: "grp_12345",
    });
    expect(result.success).toBe(false);
  });

  it("avvisar ett concept-id som bryter mönstret (otillåtna tecken)", () => {
    const result = occupationCandidateSchema.safeParse({
      occupationGroupConceptId: "bad id!",
      occupationGroupLabel: "Ogiltigt id",
    });
    expect(result.success).toBe(false);
  });
});
