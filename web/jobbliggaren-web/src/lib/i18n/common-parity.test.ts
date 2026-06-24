import { describe, it, expect } from "vitest";
import svCommon from "../../../messages/sv/common.json";
import enCommon from "../../../messages/en/common.json";

/**
 * ADR 0080 Vag 4 PR-5 — sv/en-paritet för `common`-namespacet, med explicit
 * täckning av den nya nav-länken `userMenu.minaMatchningar`. Samma motivation som
 * de övriga paritets-testen: EN är en plain JSON-import som tsc inte korslänkar,
 * så en saknad EN-nyckel ger en tom sträng i runtime utan ett tydligt test.
 */

function leafPaths(obj: unknown, prefix = ""): string[] {
  if (obj === null || typeof obj !== "object") return [prefix];
  const out: string[] = [];
  for (const [key, value] of Object.entries(obj as Record<string, unknown>)) {
    out.push(...leafPaths(value, prefix ? `${prefix}.${key}` : key));
  }
  return out.sort();
}

describe("common i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enCommon)).toEqual(leafPaths(svCommon));
  });

  it("'Mina matchningar'-nav-länken finns i båda katalogerna (ADR 0080)", () => {
    const sv = new Set(leafPaths(svCommon));
    const en = new Set(leafPaths(enCommon));
    expect(sv.has("userMenu.minaMatchningar")).toBe(true);
    expect(en.has("userMenu.minaMatchningar")).toBe(true);
  });
});
