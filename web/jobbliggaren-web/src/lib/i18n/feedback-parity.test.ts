import { describe, it, expect } from "vitest";
import svFeedback from "../../../messages/sv/feedback.json";
import enFeedback from "../../../messages/en/feedback.json";
import { FEEDBACK_PAGE_KEYS } from "@/lib/feedback/page-keys";

/**
 * sv/en parity for the `feedback` namespace (#1979 PR3). EN is a plain JSON import that tsc does not
 * cross-check against the typed SV catalogue, so a missing EN key would render an empty string at
 * runtime without this test.
 */

function leaves(obj: unknown, prefix = ""): Array<[string, unknown]> {
  if (obj === null || typeof obj !== "object") return [[prefix, obj]];
  return Object.entries(obj as Record<string, unknown>).flatMap(([key, value]) =>
    leaves(value, prefix ? `${prefix}.${key}` : key),
  );
}

const paths = (obj: unknown) => leaves(obj).map(([path]) => path).sort();

describe("feedback i18n parity (sv ↔ en)", () => {
  it("sv and en have identical key structure", () => {
    expect(paths(enFeedback)).toEqual(paths(svFeedback));
  });

  it("no value is empty in either catalogue", () => {
    for (const catalogue of [svFeedback, enFeedback]) {
      for (const [path, value] of leaves(catalogue)) {
        expect(typeof value, path).toBe("string");
        expect((value as string).trim().length, path).toBeGreaterThan(0);
      }
    }
  });

  it("names every page key in both catalogues, and nothing else", () => {
    for (const catalogue of [svFeedback, enFeedback]) {
      expect(Object.keys(catalogue.pages).sort()).toEqual([...FEEDBACK_PAGE_KEYS].sort());
    }
  });

  it("follows the copy rules: no em-dash, no exclamation mark, the ellipsis character", () => {
    for (const catalogue of [svFeedback, enFeedback]) {
      for (const [path, value] of leaves(catalogue)) {
        expect(value as string, path).not.toMatch(/[—!]|\.\.\./);
      }
    }
  });
});
