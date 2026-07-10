import { describe, it, expect } from "vitest";
import {
  GAP_TASK_KEYS,
  TOTAL_GAP_TASKS,
  countCompletedTasks,
  deriveGapSummaryFromForm,
  type GapTaskFormValues,
} from "./gap-tasks";
import type { ParsedGapSummary } from "@/lib/dto/parsed-resume";

/**
 * Fas 4b PR-8.3 (CTO-bind Q5) — SSOT för de nio bekräfta-uppgifterna. Mätaren på
 * hubbens åtgärdskort och Slutför-guidens steg-gate MÅSTE räkna samma nio nycklar
 * med samma närvaro-semantik som backends `ParsedGapSummary.FromContent`:
 * whitespace-only text = SAKNAS, tom collection = SAKNAS.
 */

const ALL_TRUE: ParsedGapSummary = {
  hasFullName: true,
  hasEmail: true,
  hasPhone: true,
  hasLocation: true,
  hasProfile: true,
  hasExperience: true,
  hasEducation: true,
  hasSkills: true,
  hasLanguages: true,
};

const ALL_FALSE: ParsedGapSummary = {
  hasFullName: false,
  hasEmail: false,
  hasPhone: false,
  hasLocation: false,
  hasProfile: false,
  hasExperience: false,
  hasEducation: false,
  hasSkills: false,
  hasLanguages: false,
};

function fullForm(): GapTaskFormValues {
  return {
    personalInfo: {
      fullName: "Anna Andersson",
      email: "anna@example.com",
      phone: "070-000 00 00",
      location: "Göteborg",
    },
    summary: "Erfaren backend-utvecklare",
    experiences: [{}],
    educations: [{}],
    skills: [{ name: "C#" }],
    languages: [{ name: "Svenska" }],
  };
}

function emptyForm(): GapTaskFormValues {
  return {
    personalInfo: { fullName: "", email: "", phone: "", location: "" },
    summary: "",
    experiences: [],
    educations: [],
    skills: [],
    languages: [],
  };
}

describe("GAP_TASK_KEYS / TOTAL_GAP_TASKS", () => {
  it("innehåller exakt nio ordnade nycklar", () => {
    expect(GAP_TASK_KEYS).toEqual([
      "hasFullName",
      "hasEmail",
      "hasPhone",
      "hasLocation",
      "hasProfile",
      "hasExperience",
      "hasEducation",
      "hasSkills",
      "hasLanguages",
    ]);
  });

  it("TOTAL_GAP_TASKS är 9 och lika med GAP_TASK_KEYS.length", () => {
    expect(TOTAL_GAP_TASKS).toBe(9);
    expect(TOTAL_GAP_TASKS).toBe(GAP_TASK_KEYS.length);
  });
});

describe("countCompletedTasks", () => {
  it("räknar 0 när ingen uppgift är klar", () => {
    expect(countCompletedTasks(ALL_FALSE)).toBe(0);
  });

  it("räknar delmängd (fyra klara)", () => {
    expect(
      countCompletedTasks({
        ...ALL_FALSE,
        hasFullName: true,
        hasEmail: true,
        hasExperience: true,
        hasSkills: true,
      }),
    ).toBe(4);
  });

  it("räknar alla nio när allt är klart", () => {
    expect(countCompletedTasks(ALL_TRUE)).toBe(9);
  });
});

describe("deriveGapSummaryFromForm — närvaro-semantik", () => {
  it("markerar allt närvarande för en fullt ifylld form", () => {
    expect(deriveGapSummaryFromForm(fullForm())).toEqual(ALL_TRUE);
  });

  it("markerar allt saknat för en tom form", () => {
    expect(deriveGapSummaryFromForm(emptyForm())).toEqual(ALL_FALSE);
  });

  it("whitespace-only text räknas som SAKNAS (paritet IsNullOrWhiteSpace)", () => {
    const values: GapTaskFormValues = {
      ...emptyForm(),
      personalInfo: {
        fullName: "   ",
        email: "\t",
        phone: "\n",
        location: "     ",
      },
      summary: "   ",
    };
    const gaps = deriveGapSummaryFromForm(values);
    expect(gaps.hasFullName).toBe(false);
    expect(gaps.hasEmail).toBe(false);
    expect(gaps.hasPhone).toBe(false);
    expect(gaps.hasLocation).toBe(false);
    expect(gaps.hasProfile).toBe(false);
  });

  it("tom array räknas som SAKNAS för sektion (Count > 0)", () => {
    const gaps = deriveGapSummaryFromForm({
      ...fullForm(),
      experiences: [],
      educations: [],
    });
    expect(gaps.hasExperience).toBe(false);
    expect(gaps.hasEducation).toBe(false);
  });

  it("icke-tom experiences/educations räknas som närvarande", () => {
    const gaps = deriveGapSummaryFromForm({
      ...emptyForm(),
      experiences: [{}],
      educations: [{}, {}],
    });
    expect(gaps.hasExperience).toBe(true);
    expect(gaps.hasEducation).toBe(true);
  });

  it("kompetens/språk med endast whitespace-namn räknas som SAKNAS", () => {
    const gaps = deriveGapSummaryFromForm({
      ...fullForm(),
      skills: [{ name: "   " }],
      languages: [{ name: "" }],
    });
    expect(gaps.hasSkills).toBe(false);
    expect(gaps.hasLanguages).toBe(false);
  });

  it("kompetens/språk räknas närvarande om minst ett namn är ifyllt", () => {
    const gaps = deriveGapSummaryFromForm({
      ...emptyForm(),
      skills: [{ name: "  " }, { name: "TypeScript" }],
      languages: [{ name: "Svenska" }],
    });
    expect(gaps.hasSkills).toBe(true);
    expect(gaps.hasLanguages).toBe(true);
  });
});
