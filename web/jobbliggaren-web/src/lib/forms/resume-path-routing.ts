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
