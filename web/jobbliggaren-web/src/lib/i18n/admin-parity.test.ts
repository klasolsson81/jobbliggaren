import { describe, it, expect } from "vitest";
import svAdmin from "../../../messages/sv/admin.json";
import enAdmin from "../../../messages/en/admin.json";

/**
 * sv/en parity for the admin namespace (#1973). next-intl types against the Swedish catalog, and
 * the English one is a plain JSON import that tsc does not cross-check, so a missing English key
 * would pass the type check and render a fallback at runtime.
 *
 * What this guards: key STRUCTURE only. Value drift between the locales is deliberately left
 * unguarded, as in `landing-parity.test.ts`.
 */

// Recursive, sorted dot-paths of every LEAF key in a message object.
function leafPaths(obj: unknown, prefix = ""): string[] {
  if (obj === null || typeof obj !== "object") return [prefix];
  const out: string[] = [];
  for (const [key, value] of Object.entries(obj as Record<string, unknown>)) {
    out.push(...leafPaths(value, prefix ? `${prefix}.${key}` : key));
  }
  return out.sort();
}

describe("admin i18n parity (sv ↔ en)", () => {
  it("sv and en have identical key structure", () => {
    expect(leafPaths(enAdmin)).toEqual(leafPaths(svAdmin));
  });
});
