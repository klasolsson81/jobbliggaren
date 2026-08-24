import { describe, it, expect } from "vitest";
import svFallback from "../../../messages/sv/fallback.json";
import enFallback from "../../../messages/en/fallback.json";

/**
 * sv/en-paritet för `fallback`-namespacet (#1477). Samma motivation som de
 * övriga paritets-testen: EN är en plain JSON-import som tsc inte korslänkar, så
 * en saknad EN-nyckel ger en tom sträng i runtime utan ett tydligt test.
 *
 * Extra vikt här: namespacet är HELA copyn på varje felyta och varje 404 i
 * appen — nio boundaries läser det och ingen av dem har någon annan text att
 * falla tillbaka på.
 */

function leafPaths(obj: unknown, prefix = ""): string[] {
  if (obj === null || typeof obj !== "object") return [prefix];
  const out: string[] = [];
  for (const [key, value] of Object.entries(obj as Record<string, unknown>)) {
    out.push(...leafPaths(value, prefix ? `${prefix}.${key}` : key));
  }
  return out.sort();
}

describe("fallback i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enFallback)).toEqual(leafPaths(svFallback));
  });

  it("varje boundary-nyckel finns, och ingen är tom i någon katalog", () => {
    // Motfaktum: strukturtestet ovan passerar även om ett värde är "" i EN,
    // vilket renderar en tom rubrik på en felyta i stället för att fällas.
    const expected = [
      "errorBodyRetry",
      "errorTitle",
      "notFound.body",
      "notFound.title",
      "notFound.toOverview",
      "notFound.toStart",
      "retry",
    ];
    expect(leafPaths(svFallback)).toEqual(expected);

    for (const catalog of [svFallback, enFallback]) {
      for (const value of Object.values(catalog)) {
        if (typeof value === "string") expect(value.trim().length).toBeGreaterThan(0);
        else for (const nested of Object.values(value)) expect(nested.trim().length).toBeGreaterThan(0);
      }
    }
  });
});
