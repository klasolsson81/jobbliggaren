import { describe, it, expect } from "vitest";
import svJobads from "../../../messages/sv/jobads.json";
import enJobads from "../../../messages/en/jobads.json";

/**
 * STEG 5 (grade-filter) — sv/en-paritet för jobads-namespacet. next-intl typar
 * mot SV-katalogen (source of truth); EN är en plain JSON-import som tsc INTE
 * korslänkar, så en saknad EN-nyckel slinker igenom typkollen och ger en tom
 * sträng/fallback i runtime. Detta test pinnar att de två katalogerna har
 * IDENTISK nyckel-struktur (rekursivt) så varje sv-nyckel har en en-motsvarighet
 * (och vice versa) — och täcker explicit den nya `ui.gradeFilter`-grenen.
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

describe("jobads i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enJobads)).toEqual(leafPaths(svJobads));
  });

  it("grade-filter-nycklarna finns i båda katalogerna (STEG 5)", () => {
    const required = [
      "ui.gradeFilter.toggleLabel",
      "ui.gradeFilter.groupLabel",
      "ui.gradeFilter.help",
      "ui.gradeFilter.grade.Basic",
      "ui.gradeFilter.grade.Good",
      "ui.gradeFilter.grade.Strong",
    ];
    const sv = new Set(leafPaths(svJobads));
    const en = new Set(leafPaths(enJobads));
    for (const path of required) {
      expect(sv.has(path), `sv saknar ${path}`).toBe(true);
      expect(en.has(path), `en saknar ${path}`).toBe(true);
    }
  });

  it("grade-filtret erbjuder ALDRIG en Topp-nyckel (Fast-bandet, honest by design)", () => {
    // Kontrollen filtrerar bara Grund/Bra/Stark — Toppmatch kan inte beräknas
    // i listan. Vakt: ingen `grade.Top` i grade-filter-katalogen.
    const sv = new Set(leafPaths(svJobads));
    const en = new Set(leafPaths(enJobads));
    expect(sv.has("ui.gradeFilter.grade.Top")).toBe(false);
    expect(en.has("ui.gradeFilter.grade.Top")).toBe(false);
  });
});
