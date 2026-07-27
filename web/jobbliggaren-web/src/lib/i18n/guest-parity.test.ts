import { describe, it, expect } from "vitest";
import svGuest from "../../../messages/sv/guest.json";
import enGuest from "../../../messages/en/guest.json";

/**
 * sv/en-paritet för guest-namespacet.
 *
 * next-intl typar mot SV-katalogen (source of truth); EN är en plain JSON-import
 * som tsc INTE korslänkar, så en saknad EN-nyckel slinker igenom typkollen och
 * ger en tom sträng/fallback i runtime.
 *
 * `guest` och `landing` var de två sista namespaces utan paritetstest. Testet
 * pinnar dessutom att BÅDA sidorna av demolägets kan-/kräver-konto-lista finns
 * i båda katalogerna: listan är hela poängen med välkomstmodalen, och en
 * ensidig katalog gör gränsen otydlig i stället för att fela synligt.
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

describe("guest i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enGuest)).toEqual(leafPaths(svGuest));
  });

  it("välkomstmodalens båda sidor finns i båda katalogerna", () => {
    const required = [
      "welcome.canDoBrowse",
      "welcome.canDoPipeline",
      "welcome.canDoResumes",
      "welcome.needsAccountSearch",
      "welcome.needsAccountEdit",
      "welcome.needsAccountNotices",
    ];
    const sv = new Set(leafPaths(svGuest));
    const en = new Set(leafPaths(enGuest));
    for (const path of required) {
      expect(sv.has(path), `sv saknar ${path}`).toBe(true);
      expect(en.has(path), `en saknar ${path}`).toBe(true);
    }
  });
});
