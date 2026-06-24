import { describe, it, expect } from "vitest";
import svOversikt from "../../../messages/sv/oversikt.json";
import enOversikt from "../../../messages/en/oversikt.json";

/**
 * ADR 0079 STEG 6 (live match-count) — sv/en-paritet för oversikt-namespacet.
 * next-intl typar mot SV-katalogen (source of truth); EN är en plain JSON-import
 * som tsc INTE korslänkar, så en saknad EN-nyckel slinker igenom typkollen och
 * ger en tom sträng/fallback i runtime. Detta test pinnar IDENTISK nyckel-
 * struktur (rekursivt) och täcker explicit den nya `notices.matchTextZero`-
 * nollstate-nyckeln.
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

describe("oversikt i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enOversikt)).toEqual(leafPaths(svOversikt));
  });

  it("match-notis-nycklarna finns i båda katalogerna (STEG 6)", () => {
    const required = ["notices.matchText", "notices.matchTextZero"];
    const sv = new Set(leafPaths(svOversikt));
    const en = new Set(leafPaths(enOversikt));
    for (const path of required) {
      expect(sv.has(path), `sv saknar ${path}`).toBe(true);
      expect(en.has(path), `en saknar ${path}`).toBe(true);
    }
  });
});
