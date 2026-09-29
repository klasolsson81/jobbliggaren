import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * #1914 — a long chip must stay inside its container at every width. jsdom has no layout, so this
 * reads the source; the rendered measurement is the real proof. It pins the declaration the fix
 * rests on: the label wraps instead of hiding its rest behind an ellipsis.
 */
const CSS = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), "globals.css"),
  "utf-8",
).replace(/\/\*[\s\S]*?\*\//g, "");

/** Every rule block, top-level or inside an at-rule, with its selector list trimmed and split. */
const RULES = [...CSS.matchAll(/([^{}]+)\{([^{}]*)\}/g)].map((m) => ({
  selectors: (m[1] ?? "")
    .split(";")
    .at(-1)
    ?.split(",")
    .map((s) => s.trim().replace(/\s*([>+~])\s*/g, " $1 ").replace(/\s+/g, " ")) ?? [],
  body: m[2] ?? "",
}));

/** Every value `property` is declared with, in every block whose selector list names `selector`. */
function declared(selector: string, property: string): string[] {
  return RULES.filter((r) => r.selectors.includes(selector)).flatMap((r) =>
    [...r.body.matchAll(new RegExp(`(?:^|;)\\s*${property}\\s*:\\s*([^;]+)`, "g"))].map((m) =>
      (m[1] ?? "").trim(),
    ),
  );
}

describe("globals.css — the chip list keeps a long chip inside its container (#1914)", () => {
  it("reads rules nested in an at-rule, not only those in column 0", () => {
    expect(declared(".jp-chip__remove", "width").length).toBeGreaterThan(1);
  });

  // Klas 2026-09-29 (a): a wrapped chip keeps corners; on one line --jp-r-chip still draws a pill.
  it("gives the chip its own corner radius, not the pill radius", () => {
    const radii = declared(".jp-chip", "border-radius");
    expect(radii.length).toBeGreaterThan(0);
    expect(radii.every((v) => v === "var(--jp-r-chip)"), radii.join(" | ")).toBe(true);
  });

  it("wraps a long label inside the chip, never an ellipsis", () => {
    const wraps = declared(".jp-chip__label", "overflow-wrap");
    expect(wraps.length).toBeGreaterThan(0);
    expect(wraps.every((v) => v === "anywhere"), wraps.join(" | ")).toBe(true);
    expect(declared(".jp-chip__label", "text-overflow")).not.toContain("ellipsis");
  });

  it.each([".jp-chiplist", ".jp-chiplist > li", ".jp-chip", ".jp-chip--removable", ".jp-chip__label"])(
    "never stops the label wrapping from %s",
    (selector) => {
      expect(declared(selector, "white-space").filter((v) => /^(nowrap|pre)\b/.test(v))).toEqual([]);
      expect(declared(selector, "text-wrap")).not.toContain("nowrap");
      expect(declared(selector, "text-wrap-mode")).not.toContain("nowrap");
    },
  );
});
