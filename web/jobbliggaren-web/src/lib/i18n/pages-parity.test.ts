import { describe, it, expect } from "vitest";
import svPages from "../../../messages/sv/pages.json";
import enPages from "../../../messages/en/pages.json";

/**
 * ADR 0080 Vag 4 PR-5 — sv/en-paritet för `pages`-namespacet, med explicit
 * täckning av den nya `matchningar`-vyns nycklar. next-intl typar mot SV-
 * katalogen (source of truth); EN är en plain JSON-import som tsc INTE
 * korslänkar, så en saknad EN-nyckel slinker igenom typkollen och ger en tom
 * sträng/fallback i runtime. Detta test pinnar IDENTISK nyckel-struktur
 * (rekursivt) över hela namespacet.
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

describe("pages i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enPages)).toEqual(leafPaths(svPages));
  });

  it("matchningar-vyns nycklar finns i båda katalogerna (ADR 0080)", () => {
    const required = [
      "matchningar.title",
      "matchningar.lede",
      "matchningar.listLabel",
      "matchningar.newBadge",
      "matchningar.newBadgeAriaLabel",
      "matchningar.emptyTitle",
      "matchningar.emptyBody",
      "matchningar.loadErrorTitle",
    ];
    const sv = new Set(leafPaths(svPages));
    const en = new Set(leafPaths(enPages));
    for (const path of required) {
      expect(sv.has(path), `sv saknar ${path}`).toBe(true);
      expect(en.has(path), `en saknar ${path}`).toBe(true);
    }
  });
});
