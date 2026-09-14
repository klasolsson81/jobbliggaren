import { describe, it, expect } from "vitest";
import svComponents from "../../../messages/sv/components.json";
import enComponents from "../../../messages/en/components.json";

// The `components` namespace had no parity guard before #1682 (design-reviewer, 2026-09-14): sv is
// the typed source of truth, so a missing en key is not a type error and would surface only in the
// English UI. Same shape as pages-parity.test.ts.
function leafPaths(obj: unknown, prefix = ""): string[] {
  if (obj === null || typeof obj !== "object") return [prefix];
  const out: string[] = [];
  for (const [key, value] of Object.entries(obj as Record<string, unknown>)) {
    out.push(...leafPaths(value, prefix ? `${prefix}.${key}` : key));
  }
  return out.sort();
}

describe("components i18n-paritet (sv ↔ en)", () => {
  it("sv och en har identisk nyckel-struktur", () => {
    expect(leafPaths(enComponents)).toEqual(leafPaths(svComponents));
  });

  it("yrkesblockets nycklar finns i båda katalogerna (#1682, ADR 0137)", () => {
    const required = [
      "criterionPicker.occupationChoose",
      "criterionPicker.occupationChangeChoice",
      "criterionPicker.occupationHeading",
      "criterionPicker.occupationIntro",
      "criterionPicker.occupationShare",
      "criterionPicker.occupationNotInRegister",
      "criterionPicker.occupationTooFew",
      "criterionPicker.occupationNotProfiled",
      "criterionPicker.occupationAdsSeen",
      "criterionPicker.occupationTooFewShort",
      "criterionPicker.occupationNotProfiledShort",
    ];
    const sv = new Set(leafPaths(svComponents));
    const en = new Set(leafPaths(enComponents));
    for (const key of required) {
      expect(sv.has(key), `sv saknar ${key}`).toBe(true);
      expect(en.has(key), `en saknar ${key}`).toBe(true);
    }
  });
});
