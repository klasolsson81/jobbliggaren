import { describe, it, expect } from "vitest";
import svFaq from "../../../messages/sv/content-faq.json";
import enFaq from "../../../messages/en/content-faq.json";
import svTips from "../../../messages/sv/content-tips.json";
import enTips from "../../../messages/en/content-tips.json";

/**
 * #261 — sv/en-paritet för de nya content-namespacen (`content-faq`,
 * `content-tips`). next-intl typar mot SV-katalogen (source of truth); EN är en
 * plain JSON-import som tsc INTE korslänkar, så en saknad EN-nyckel slinker
 * igenom typkollen och ger en tom sträng i runtime. Detta test pinnar IDENTISK
 * nyckel-struktur (rekursivt) över båda namespacen.
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

describe("content i18n-paritet (sv ↔ en)", () => {
  it("content-faq: sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enFaq)).toEqual(leafPaths(svFaq));
  });

  it("content-tips: sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enTips)).toEqual(leafPaths(svTips));
  });

  it("content-faq har åtta frågor med q+a i båda katalogerna", () => {
    const required = [
      "pris",
      "kalla",
      "matchning",
      "ai",
      "konto",
      "uppgifter",
      "cv",
      "radera",
    ];
    const sv = new Set(leafPaths(svFaq));
    const en = new Set(leafPaths(enFaq));
    for (const key of required) {
      for (const leaf of [`items.${key}.q`, `items.${key}.a`]) {
        expect(sv.has(leaf), `sv saknar ${leaf}`).toBe(true);
        expect(en.has(leaf), `en saknar ${leaf}`).toBe(true);
      }
    }
  });
});
