import { describe, it, expect } from "vitest";
import svSettings from "../../../messages/sv/settings.json";
import enSettings from "../../../messages/en/settings.json";

/**
 * ADR 0080 Vag 4 PR-6 — sv/en-paritet för `settings`-namespacet, med explicit
 * täckning av det nya `backgroundMatch`-kortets nycklar (matchningsnotis-
 * consent). next-intl typar mot SV-katalogen (source of truth); EN är en plain
 * JSON-import som tsc INTE korslänkar, så en saknad EN-nyckel slinker igenom
 * typkollen och ger en tom sträng/fallback i runtime. Detta test pinnar
 * IDENTISK nyckel-struktur (rekursivt) över hela namespacet.
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

describe("settings i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enSettings)).toEqual(leafPaths(svSettings));
  });

  it("backgroundMatch-kortets nycklar finns i båda katalogerna (ADR 0080 PR-6)", () => {
    const required = [
      "backgroundMatch.title",
      "backgroundMatch.intro",
      "backgroundMatch.toggleLabel",
      "backgroundMatch.toggleDescription",
      "backgroundMatch.cadenceLabel",
      "backgroundMatch.cadenceDaily",
      "backgroundMatch.cadenceWeekly",
      "backgroundMatch.cadenceHint",
      "backgroundMatch.cadenceHintDisabled",
      "backgroundMatch.savedAt",
      "backgroundMatch.errors.notLoggedIn",
      "backgroundMatch.errors.invalidInput",
      "backgroundMatch.errors.saveFailed",
      "backgroundMatch.errors.tooManyAttempts",
    ];
    const sv = new Set(leafPaths(svSettings));
    const en = new Set(leafPaths(enSettings));
    for (const path of required) {
      expect(sv.has(path), `sv saknar ${path}`).toBe(true);
      expect(en.has(path), `en saknar ${path}`).toBe(true);
    }
  });
});
