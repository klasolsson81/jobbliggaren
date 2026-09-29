import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * #1914 — a long chip must stay inside its container at every width. jsdom has no layout, so this
 * reads the source; the rendered measurement is the real proof. It pins the two declarations the
 * fix rests on: the list lets its items shrink below their content width (without it the chip's
 * `max-width: 100%` never engages), and the label wraps instead of hiding its rest behind an
 * ellipsis.
 */
const CSS = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), "globals.css"),
  "utf-8",
).replace(/\/\*[\s\S]*?\*\//g, "");

/** The declaration block of the one rule whose selector is exactly `selector`. */
function block(selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\>]/g, "\\$&");
  const matches = [...CSS.matchAll(new RegExp(`(?:^|\\n)${escaped}\\s*\\{([^}]*)\\}`, "g"))];
  expect(matches, `exactly one rule for ${selector}`).toHaveLength(1);
  return matches[0]?.[1] ?? "";
}

describe("globals.css — the chip list keeps a long chip inside its container (#1914)", () => {
  it("lets each list item shrink to the line", () => {
    expect(block(".jp-chiplist > li")).toMatch(/min-width:\s*0\s*;/);
  });

  // Klas 2026-09-29 (a): a wrapped chip keeps corners; on one line --jp-r-chip still draws a pill.
  it("gives the chip its own corner radius, not the pill radius", () => {
    expect(block(".jp-chip")).toMatch(/border-radius:\s*var\(--jp-r-chip\)\s*;/);
  });

  it("wraps a long label inside the chip, never an ellipsis", () => {
    const label = block(".jp-chip__label");
    expect(label).toMatch(/overflow-wrap:\s*anywhere\s*;/);
    expect(label).not.toMatch(/text-overflow:\s*ellipsis/);
    expect(label).not.toMatch(/white-space:\s*nowrap/);
  });
});
