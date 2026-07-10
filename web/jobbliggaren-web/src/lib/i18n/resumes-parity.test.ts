import { describe, it, expect } from "vitest";
import svResumes from "../../../messages/sv/resumes.json";
import enResumes from "../../../messages/en/resumes.json";

/**
 * Fas 4b PR-8.3 — sv/en-paritet för `resumes`-namespacet, med explicit täckning
 * av de nya CV-hubb-/Slutför-guide-/ATS-text-nycklarna. next-intl typar mot SV-
 * katalogen (source of truth); EN är en plain JSON-import som tsc INTE korslänkar,
 * så en saknad EN-nyckel slinker igenom typkollen och ger en tom sträng/fallback i
 * runtime. Detta test pinnar IDENTISK nyckel-struktur (rekursivt) över hela
 * namespacet. Speglar `pages-parity.test.ts` exakt.
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

describe("resumes i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enResumes)).toEqual(leafPaths(svResumes));
  });

  it("PR-8.3-nycklarna (hubb-kort, Slutför-guide, ATS-text) finns i båda katalogerna", () => {
    const required = [
      // Hubb-kortets ursprungs- + granskningsstatus-badges (§5-ärlighet).
      "card.originImport",
      "card.originTemplate",
      "card.template",
      "card.templateName.MorkPanel",
      "card.findingsReview",
      "card.findingsNone",
      "card.findingsCount",
      // ATS-textfliken i CvPreview.
      "preview.atsText",
      "preview.atsTextBanner",
      "preview.atsTextLoading",
      "preview.atsTextNotFound",
      "preview.atsTextError",
      "preview.atsTextRegionLabel",
      // Slutför-guiden.
      "guide.closeConfirmBody",
      "guide.steps.skills",
      "guide.missingInFile",
      "guide.foundInFile",
      "guide.wordCount",
      "guide.save.cta",
      // Discard-actionens felmeddelande.
      "actions.discardFailed",
    ];
    const sv = new Set(leafPaths(svResumes));
    const en = new Set(leafPaths(enResumes));
    for (const path of required) {
      expect(sv.has(path), `sv saknar ${path}`).toBe(true);
      expect(en.has(path), `en saknar ${path}`).toBe(true);
    }
  });
});
