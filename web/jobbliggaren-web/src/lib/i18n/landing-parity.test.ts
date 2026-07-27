import { describe, it, expect } from "vitest";
import svLanding from "../../../messages/sv/landing.json";
import enLanding from "../../../messages/en/landing.json";

/**
 * sv/en-paritet för landing-namespacet.
 *
 * next-intl typar mot SV-katalogen (source of truth); EN är en plain JSON-import
 * som tsc INTE korslänkar, så en saknad EN-nyckel slinker igenom typkollen och
 * ger en tom sträng/fallback i runtime.
 *
 * `landing` och `guest` var de två sista namespaces utan paritetstest.
 *
 * VAD DETTA VAKTAR: nyckel-STRUKTUR, plus FEATURE_KEYS-kontraktet nedan —
 * att listan i `landing-features.tsx` har en key+body i BÅDA katalogerna.
 * Det andra testet är inte redundant mot det första: döps `features.companyWatch`
 * om i BÅDA katalogerna förblir strukturtestet grönt medan komponenten kastar i
 * runtime, eftersom FEATURE_KEYS är en hårdkodad konstant som `t()` slår upp
 * vid render.
 *
 * VAD DETTA INTE VAKTAR: att sv och en säger samma SAK. Ändras ett värde i den
 * ena katalogen och inte i den andra är båda testerna gröna — nyckeln finns
 * kvar, bara innehållet driver isär. Värdedrift lämnas medvetet ovaktad;
 * översättningar ska legitimt skilja sig, så den axeln går inte att pinna
 * generellt.
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

// Speglar FEATURE_KEYS i src/components/landing/landing-features.tsx.
const FEATURE_KEYS = [
  "search",
  "matching",
  "applications",
  "cvReview",
  "companyWatch",
  "reminders",
];

describe("landing i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enLanding)).toEqual(leafPaths(svLanding));
  });

  it("varje funktionscell har key + body i båda katalogerna", () => {
    const sv = new Set(leafPaths(svLanding));
    const en = new Set(leafPaths(enLanding));
    for (const feature of FEATURE_KEYS) {
      for (const leaf of ["key", "body"]) {
        const path = `features.${feature}.${leaf}`;
        expect(sv.has(path), `sv saknar ${path}`).toBe(true);
        expect(en.has(path), `en saknar ${path}`).toBe(true);
      }
    }
  });
});
