import { describe, it, expect } from "vitest";
import {
  pathToElementId,
  gapFillPathToElementId,
  guidePathToFormPath,
  guidePathToStepAndElementId,
  GUIDE_STEP_DETAILS,
  GUIDE_STEP_EXPERIENCE,
  GUIDE_STEP_SKILLS,
  GUIDE_STEP_SAVE,
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

describe("resume-path-routing > guidePathToStepAndElementId (Slutför-guiden, PR-8.3)", () => {
  it("mappar CV-variantnamnet name → spara-steget + guide-cv-name", () => {
    expect(guidePathToStepAndElementId("name")).toEqual({
      step: GUIDE_STEP_SAVE,
      elementId: "guide-cv-name",
    });
  });

  describe("content.personalInfo.<field> → uppgifts-steget + guide-pi-<field>", () => {
    it.each([
      ["content.personalInfo.fullName", "guide-pi-fullName"],
      ["content.personalInfo.email", "guide-pi-email"],
      ["content.personalInfo.phone", "guide-pi-phone"],
      ["content.personalInfo.location", "guide-pi-location"],
    ])("mappar %s → uppgifts-steget + %s", (path, elementId) => {
      expect(guidePathToStepAndElementId(path)).toEqual({
        step: GUIDE_STEP_DETAILS,
        elementId,
      });
    });
  });

  it("mappar content.summary → uppgifts-steget + guide-summary", () => {
    expect(guidePathToStepAndElementId("content.summary")).toEqual({
      step: GUIDE_STEP_DETAILS,
      elementId: "guide-summary",
    });
  });

  describe("content.experiences.N.<field> → erfarenhets-steget + guide-exp-N-<field>", () => {
    it.each([
      ["content.experiences.0.role", "guide-exp-0-role"],
      ["content.experiences.0.company", "guide-exp-0-company"],
      ["content.experiences.1.startDate", "guide-exp-1-startDate"],
      ["content.experiences.42.description", "guide-exp-42-description"],
    ])("mappar %s → erfarenhets-steget + %s", (path, elementId) => {
      expect(guidePathToStepAndElementId(path)).toEqual({
        step: GUIDE_STEP_EXPERIENCE,
        elementId,
      });
    });
  });

  describe("content.educations.N.<field> → erfarenhets-steget + guide-edu-N-<field>", () => {
    it.each([
      ["content.educations.0.institution", "guide-edu-0-institution"],
      ["content.educations.1.degree", "guide-edu-1-degree"],
      ["content.educations.2.endDate", "guide-edu-2-endDate"],
    ])("mappar %s → erfarenhets-steget + %s", (path, elementId) => {
      expect(guidePathToStepAndElementId(path)).toEqual({
        step: GUIDE_STEP_EXPERIENCE,
        elementId,
      });
    });
  });

  describe("dynamiska sektioner → erfarenhets-steget (inkl. lines → body-remap)", () => {
    it.each([
      // Sektions-post: `lines` remappas till `body` (den enda avvikande fält-nyckeln).
      ["content.sections.0.entries.1.lines", "guide-section-0-entry-1-body"],
      // Sektions-post, icke-lines-fält passeras verbatim.
      ["content.sections.2.entries.0.title", "guide-section-2-entry-0-title"],
      // Sektions-rubrik (ej entries).
      ["content.sections.0.heading", "guide-section-0-heading"],
    ])("mappar %s → erfarenhets-steget + %s", (path, elementId) => {
      expect(guidePathToStepAndElementId(path)).toEqual({
        step: GUIDE_STEP_EXPERIENCE,
        elementId,
      });
    });
  });

  describe("chip-listor (skills/languages) → kompetens-steget + add-fältet", () => {
    // Chip-listor har ingen per-post-input → landar på steget och dess add-fält
    // (guide-*-add), aldrig en gissad post-input (resume-path-routing.ts:110-117).
    it.each([
      ["content.skills.0.name", "guide-skills-add"],
      ["content.skills.3.yearsExperience", "guide-skills-add"],
      ["content.skills.0", "guide-skills-add"],
      ["content.languages.0.name", "guide-languages-add"],
      ["content.languages.2.proficiency", "guide-languages-add"],
    ])("mappar %s → kompetens-steget + %s", (path, elementId) => {
      expect(guidePathToStepAndElementId(path)).toEqual({
        step: GUIDE_STEP_SKILLS,
        elementId,
      });
    });
  });

  describe("okända/ohanterade paths → null (ingen hopp/fokus-flytt)", () => {
    it.each([
      [""],
      ["garbage"],
      ["Name"], // case-sensitive mot literal "name"
      ["parsedResumeId"], // schema-fält utan guide-kontroll
      ["personalInfo.fullName"], // saknar content.-prefix
      ["content"], // saknar suffix (inner === "")
      ["content."], // tom inner efter prefix
      ["content.unknownField"],
      ["content.experiences.foo.role"], // index ej siffra → matchar ej \d+
      ["content.experiences.-1.role"], // negativ index matchar ej \d+
      ["content.educations.x.degree"], // index ej siffra
    ])("returnerar null för %s", (path) => {
      expect(guidePathToStepAndElementId(path)).toBeNull();
    });
  });
});

describe("resume-path-routing > guidePathToFormPath", () => {
  // Samma Zod-namnrymd, men målet är RHF:s fältväg (form.setError) i stället för
  // ett element-id. Formen och payloaden är inte samma form — `toRawPayload` är en
  // äkta transform — och den enda strukturella skillnaden en path kan träffa är
  // sektionspostens brödtext (form: `body` / payload: `lines`).
  describe("delad namnrymd → content.-prefixet strippas", () => {
    it.each([
      ["name", "name"],
      ["content.personalInfo.fullName", "personalInfo.fullName"],
      ["content.personalInfo.location", "personalInfo.location"],
      ["content.summary", "summary"],
      ["content.experiences.0.company", "experiences.0.company"],
      ["content.experiences.2.startDate", "experiences.2.startDate"],
      ["content.educations.1.degree", "educations.1.degree"],
      ["content.sections.0.heading", "sections.0.heading"],
      ["content.sections.1.entries.2.title", "sections.1.entries.2.title"],
    ])("mappar %s → %s", (path, expected) => {
      expect(guidePathToFormPath(path)).toBe(expected);
    });
  });

  describe("sektionspostens lines → body (den enda strukturella avvikelsen)", () => {
    it.each([
      // Post-nivå (summerad längd över raderna).
      ["content.sections.0.entries.1.lines", "sections.0.entries.1.body"],
      // Rad-nivå: ett issue på en ENSKILD rad hör ändå hemma i textarean som helhet
      // — formen har ingen per-rad-kontroll att markera.
      ["content.sections.2.entries.0.lines.3", "sections.2.entries.0.body"],
    ])("mappar %s → %s", (path, expected) => {
      expect(guidePathToFormPath(path)).toBe(expected);
    });
  });

  describe("utan RHF-kontroll att markera → null (aldrig ett gissat fält)", () => {
    it.each([
      // Chip-listor: chip-listan ÄR fältet; det finns ingen per-post-input att
      // hänga felet på. De ytas via stegets status i stället.
      ["content.skills.0.name"],
      ["content.languages.1.name"],
      // Okänt/ohanterat.
      [""],
      ["garbage"],
      ["parsedResumeId"],
      ["personalInfo.fullName"], // saknar content.-prefix
      ["content.unknownField"],
    ])("returnerar null för %s", (path) => {
      expect(guidePathToFormPath(path)).toBeNull();
    });
  });
});
