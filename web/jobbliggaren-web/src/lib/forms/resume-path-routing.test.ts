import { describe, it, expect } from "vitest";
import {
  pathToElementId,
  gapFillPathToElementId,
} from "./resume-path-routing";

describe("resume-path-routing > pathToElementId", () => {
  describe("personalInfo.* → pi-* (per resume-schemas.ts:46-63)", () => {
    it.each([
      ["personalInfo.fullName", "pi-fullName"],
      ["personalInfo.email", "pi-email"],
      ["personalInfo.phone", "pi-phone"],
      ["personalInfo.location", "pi-location"],
    ])("mappar %s → %s", (path, expected) => {
      expect(pathToElementId(path)).toBe(expected);
    });
  });

  describe("summary (toppnivå)", () => {
    it("mappar summary → summary", () => {
      expect(pathToElementId("summary")).toBe("summary");
    });
  });

  describe("experiences.N.field → exp-N-field", () => {
    it.each([
      ["experiences.0.role", "exp-0-role"],
      ["experiences.0.company", "exp-0-company"],
      ["experiences.1.startDate", "exp-1-startDate"],
      ["experiences.42.description", "exp-42-description"],
    ])("mappar %s → %s", (path, expected) => {
      expect(pathToElementId(path)).toBe(expected);
    });
  });

  describe("educations.N.field → edu-N-field (per resume-schemas.ts:86-104)", () => {
    it.each([
      ["educations.0.institution", "edu-0-institution"],
      ["educations.0.degree", "edu-0-degree"],
      ["educations.1.startDate", "edu-1-startDate"],
      ["educations.2.endDate", "edu-2-endDate"],
    ])("mappar %s → %s", (path, expected) => {
      expect(pathToElementId(path)).toBe(expected);
    });
  });

  describe("skills.N.field → skill-N-field (per resume-schemas.ts:106-120)", () => {
    it.each([
      ["skills.0.name", "skill-0-name"],
      ["skills.1.name", "skill-1-name"],
      // yearsExperience är schema-paths men HTML-id:t är "years"
      // (kortare för UI). Specialfall i pathToElementId.
      ["skills.0.yearsExperience", "skill-0-years"],
      ["skills.3.yearsExperience", "skill-3-years"],
    ])("mappar %s → %s", (path, expected) => {
      expect(pathToElementId(path)).toBe(expected);
    });
  });

  describe("unknown paths returnerar null", () => {
    it.each([
      [""],
      ["unknownField"],
      ["personalInfo"], // saknar dot-suffix
      ["experiences"], // saknar index
      ["experiences.foo.role"], // index är inte siffra
      ["skills.abc.name"], // index är inte siffra
      ["SUMMARY"], // case-sensitive
      ["experiences.-1.role"], // negativ index ska inte matcha \d+
    ])("returnerar null för %s", (path) => {
      expect(pathToElementId(path)).toBeNull();
    });
  });
});

describe("resume-path-routing > gapFillPathToElementId (F2)", () => {
  it("mappar CV-variantnamnet name → cv-name", () => {
    expect(gapFillPathToElementId("name")).toBe("cv-name");
  });

  describe("content.<...> strippar prefixet och delegerar till pathToElementId", () => {
    it.each([
      ["content.personalInfo.fullName", "pi-fullName"],
      ["content.summary", "summary"],
      ["content.experiences.0.startDate", "exp-0-startDate"],
      ["content.educations.1.degree", "edu-1-degree"],
      ["content.skills.2.yearsExperience", "skill-2-years"],
    ])("mappar %s → %s", (path, expected) => {
      expect(gapFillPathToElementId(path)).toBe(expected);
    });
  });

  describe("okända/ohanterade paths → null (ingen focus-flytt)", () => {
    it.each([
      [""],
      ["parsedResumeId"], // schema-fält men ingen kontroll → ingen focus-flytt
      ["content"], // saknar suffix
      ["content.unknownField"],
      ["personalInfo.fullName"], // saknar content.-prefix
      ["Name"], // case-sensitive
    ])("returnerar null för %s", (path) => {
      expect(gapFillPathToElementId(path)).toBeNull();
    });
  });
});
