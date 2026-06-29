import { describe, it, expect } from "vitest";
import svM from "../../../messages/sv/content-matchning.json";
import enM from "../../../messages/en/content-matchning.json";

/**
 * #365 — sv/en-paritet för `content-matchning` (förklaringssidan Så fungerar
 * matchningen). next-intl typar mot SV-katalogen; EN är en plain JSON-import som
 * tsc INTE korslänkar. Detta test pinnar IDENTISK nyckel-struktur (rekursivt,
 * inkl. array-längder) + att grad-enumet är oförändrat.
 */

function leafPaths(obj: unknown, prefix = ""): string[] {
  if (obj === null || typeof obj !== "object") return [prefix];
  const out: string[] = [];
  for (const [key, value] of Object.entries(obj as Record<string, unknown>)) {
    out.push(...leafPaths(value, prefix ? `${prefix}.${key}` : key));
  }
  return out.sort();
}

describe("content-matchning i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enM)).toEqual(leafPaths(svM));
  });

  it("grad-enumet är de fyra gröna stegen i fallande ordning, identiskt sv/en", () => {
    const order = ["Top", "Strong", "Good", "Basic"];
    expect(svM.grades.items.map((i) => i.grade)).toEqual(order);
    expect(enM.grades.items.map((i) => i.grade)).toEqual(order);
  });
});
