import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { readRules } from "@/test/css-rules";

/**
 * #2198 — two touch and focus facts the occupation picker's hosts rest on. jsdom applies no
 * stylesheet, so they are pinned from the source; the rendered measurement is the render matrix's.
 */
const APP_CSS = readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), "app.css"), "utf8");

describe("the occupation picker's CSS (#2198)", () => {
  it("gives rows and the focused column heading an inset ring in the rail as in the dialog (design-reviewer B1)", () => {
    const rules = readRules(APP_CSS);
    for (const host of [".jp-matchdialog", ".jp-wizard--rail"]) {
      for (const target of [".jp-checkitem", ".jp-popover-row", ".jp-occsearch__heading"]) {
        const selector = `${host} ${target}:focus-visible`;
        const matching = rules.filter((r) => r.selector === selector && r.inMedia === null);
        expect(matching, selector).toHaveLength(1);
        expect(matching[0]?.declarations).toEqual(
          expect.arrayContaining([
            { property: "outline", value: "2px solid var(--jp-focus)" },
            { property: "outline-offset", value: "-2px" },
          ]),
        );
      }
    }
  });

  it("lifts the 'Lägg till yrken' button to the 44px touch floor at 768px and below (design-reviewer B2)", () => {
    const cta = readRules(APP_CSS).filter(
      (r) => r.selector === ".jp-occpicker__cta" && r.inMedia === "@media (max-width: 768px)",
    );

    expect(cta).toHaveLength(1);
    expect(cta[0]?.declarations).toEqual([
      { property: "height", value: "auto" },
      { property: "min-height", value: "44px" },
    ]);
  });
});
