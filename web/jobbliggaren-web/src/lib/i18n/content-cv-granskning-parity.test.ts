import { describe, it, expect } from "vitest";
import svCv from "../../../messages/sv/content-cv-granskning.json";
import enCv from "../../../messages/en/content-cv-granskning.json";

/**
 * #368 — sv/en-paritet för `content-cv-granskning` (förklaringssidan Så granskar
 * vi ditt CV). next-intl typar mot SV-katalogen; EN är en plain JSON-import som
 * tsc INTE korslänkar. Detta test pinnar IDENTISK nyckel-struktur (rekursivt,
 * inkl. array-längder + null-mönstret per verdikt-exempel) + att tone-värdena
 * är oförändrade (tone bär aldrig text, ska ej översättas).
 */

function leafPaths(obj: unknown, prefix = ""): string[] {
  if (obj === null || typeof obj !== "object") return [prefix];
  const out: string[] = [];
  for (const [key, value] of Object.entries(obj as Record<string, unknown>)) {
    out.push(...leafPaths(value, prefix ? `${prefix}.${key}` : key));
  }
  return out.sort();
}

describe("content-cv-granskning i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur (inkl. null-mönster)", () => {
    expect(leafPaths(enCv)).toEqual(leafPaths(svCv));
  });

  it("verdikt- och band-tones är oförändrade sv/en", () => {
    expect(svCv.verdicts.items.map((i) => i.tone)).toEqual([
      "success",
      "warning",
      "danger",
      "neutral",
    ]);
    expect(enCv.verdicts.items.map((i) => i.tone)).toEqual(
      svCv.verdicts.items.map((i) => i.tone),
    );
    expect(enCv.noScore.bands.map((b) => b.tone)).toEqual(
      svCv.noScore.bands.map((b) => b.tone),
    );
  });
});
