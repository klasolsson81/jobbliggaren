import { describe, it, expect } from "vitest";
import svValidation from "../../../messages/sv/validation.json";
import enValidation from "../../../messages/en/validation.json";

/**
 * Fas 4b PR-8.3 — sv/en-paritet för `validation`-namespacet. Slutför-guiden delar
 * `makePromoteParsedResumeSchema` med gap-fill-formen och tillförde superset-
 * fältens felnycklar (språk + egna sektioner, ADR 0095 D-C/D-E). next-intl typar
 * mot SV-katalogen; EN korslänkas inte av tsc, så en saknad EN-nyckel ger ett tomt
 * felmeddelande i runtime. Detta test pinnar IDENTISK nyckel-struktur (rekursivt)
 * över hela namespacet. Speglar `pages-parity.test.ts` exakt.
 */

// Rekursiva, sorterade dot-paths för alla LÖV-nycklar i ett message-objekt.
function leafPaths(obj: unknown, prefix = ""): string[] {
  if (obj === null || typeof obj !== "object") return [prefix];
  const out: string[] = [];
  for (const [key, value] of Object.entries(obj as Record<string, unknown>)) {
    out.push(...leafPaths(value, prefix ? `${prefix}.${key}` : key));
  }
  return out.sort();
}

describe("validation i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enValidation)).toEqual(leafPaths(svValidation));
  });

  it("Slutför-guidens superset-valideringsnycklar finns i båda katalogerna", () => {
    const required = [
      "resume.cvNameRequired",
      "resume.languageNameRequired",
      "resume.languageNameMax",
      "resume.sectionHeadingRequired",
      "resume.sectionEntryTitleRequired",
      "resume.sectionEntryTooLong",
    ];
    const sv = new Set(leafPaths(svValidation));
    const en = new Set(leafPaths(enValidation));
    for (const path of required) {
      expect(sv.has(path), `sv saknar ${path}`).toBe(true);
      expect(en.has(path), `en saknar ${path}`).toBe(true);
    }
  });
});
