/**
 * Mappar `serverError.path` från `resumeContentSchema` (Zod) till HTML `id`-attributet
 * på motsvarande form-kontroll i `ResumeContentForm`. Används för programmatisk focus-flytt
 * vid server-validation-fel (TD-15 a11y-pattern).
 *
 * Returnerar `null` om path inte mappar mot ett känt fält — då skippas focus-flytten
 * i `useEffect`-callbacken (better than focusing the wrong element).
 *
 * Path-format:
 * - `personalInfo.<field>` → `pi-<field>`
 * - `summary` → `summary`
 * - `experiences.<idx>.<field>` → `exp-<idx>-<field>`
 * - `educations.<idx>.<field>` → `edu-<idx>-<field>`
 * - `skills.<idx>.<field>` → `skill-<idx>-<field>` (med specialfallet `yearsExperience` → `years`)
 *
 * Function:s domän-kunskap är `ResumeContentForm`-fältuppsättningen. Extraherad till
 * separat modul per TD-46 för isolated unit-tests (komponent-tester slipper kämpa
 * mot jsdom-quirks som HTML5-constraint-validation på `type="email"` / `type="date"`).
 */
export function pathToElementId(path: string): string | null {
  if (path.startsWith("personalInfo.")) {
    return `pi-${path.slice("personalInfo.".length)}`;
  }
  if (path === "summary") return "summary";
  const exp = path.match(/^experiences\.(\d+)\.(.+)$/);
  if (exp) return `exp-${exp[1]}-${exp[2]}`;
  const edu = path.match(/^educations\.(\d+)\.(.+)$/);
  if (edu) return `edu-${edu[1]}-${edu[2]}`;
  const skill = path.match(/^skills\.(\d+)\.(.+)$/);
  if (skill) {
    const field = skill[2] === "yearsExperience" ? "years" : skill[2];
    return `skill-${skill[1]}-${field}`;
  }
  return null;
}

/**
 * Variant för `CvGapFillForm` (F2). `promoteParsedResumeSchema` ger PREFIXADE Zod-paths:
 * `name` (CV-variantens namn, egen kontroll `cv-name`) och `content.<...>` (innehållet).
 * `content.`-prefixet strippas och delegeras till {@link pathToElementId}; `null` → ingen
 * focus-flytt. Extraherad ur komponenten per TD-46 för isolerade unit-test.
 */
export function gapFillPathToElementId(path: string): string | null {
  if (path === "name") return "cv-name";
  if (path.startsWith("content.")) {
    return pathToElementId(path.slice("content.".length));
  }
  return null;
}

/** Wizard-stegen (`CvCompleteGuide`, PR-8.3), 0-indexerade. Exporterade så både
 * routningen och komponenten läser samma index (ingen magisk siffra). */
export const GUIDE_STEP_DETAILS = 0;
export const GUIDE_STEP_EXPERIENCE = 1;
export const GUIDE_STEP_SKILLS = 2;
export const GUIDE_STEP_SAVE = 3;

/**
 * Slutför-guiden (`CvCompleteGuide`, PR-8.3) delar `makePromoteParsedResumeSchema`
 * med gap-fill-formen men har egna element-id (`guide-*`-prefix) OCH ett stegat
 * flöde. En Zod-path mappas därför till BÅDE steget den bor på (så vi kan hoppa
 * dit) och element-id:t att flytta fokus till. `null` = okänd path (ingen
 * fokus-flytt). `elementId` kan vara `null` även när `step` är känt (t.ex. en
 * kompetens-post — chip-listan har ingen per-post-input; vi landar på steget och
 * add-fältet).
 */
export function guidePathToStepAndElementId(
  path: string,
): { step: number; elementId: string | null } | null {
  if (path === "name") {
    return { step: GUIDE_STEP_SAVE, elementId: "guide-cv-name" };
  }
  if (!path.startsWith("content.")) return null;
  const inner = path.slice("content.".length);

  if (inner.startsWith("personalInfo.")) {
    const field = inner.slice("personalInfo.".length);
    return { step: GUIDE_STEP_DETAILS, elementId: `guide-pi-${field}` };
  }
  if (inner === "summary") {
    return { step: GUIDE_STEP_DETAILS, elementId: "guide-summary" };
  }

  const exp = inner.match(/^experiences\.(\d+)\.(.+)$/);
  if (exp) {
    return { step: GUIDE_STEP_EXPERIENCE, elementId: `guide-exp-${exp[1]}-${exp[2]}` };
  }
  const edu = inner.match(/^educations\.(\d+)\.(.+)$/);
  if (edu) {
    return { step: GUIDE_STEP_EXPERIENCE, elementId: `guide-edu-${edu[1]}-${edu[2]}` };
  }

  const sectionEntry = inner.match(/^sections\.(\d+)\.entries\.(\d+)\.(.+)$/);
  if (sectionEntry) {
    const field = sectionEntry[3] === "lines" ? "body" : sectionEntry[3];
    return {
      step: GUIDE_STEP_EXPERIENCE,
      elementId: `guide-section-${sectionEntry[1]}-entry-${sectionEntry[2]}-${field}`,
    };
  }
  const section = inner.match(/^sections\.(\d+)\.(.+)$/);
  if (section) {
    const field = section[2] === "heading" ? "heading" : section[2];
    return {
      step: GUIDE_STEP_EXPERIENCE,
      elementId: `guide-section-${section[1]}-${field}`,
    };
  }

  // Kompetenser + språk är chip-listor utan per-post-input → landa på steget
  // och dess add-fält (ärlig fokus-destination i stället för en gissad post).
  if (inner.startsWith("skills.")) {
    return { step: GUIDE_STEP_SKILLS, elementId: "guide-skills-add" };
  }
  if (inner.startsWith("languages.")) {
    return { step: GUIDE_STEP_SKILLS, elementId: "guide-languages-add" };
  }

  return null;
}
