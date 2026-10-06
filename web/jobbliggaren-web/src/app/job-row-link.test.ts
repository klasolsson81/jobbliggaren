import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { readRules } from "@/test/css-rules";

/**
 * #2012 — a job card whose title is its row link (`.jp-job__rowlink`, stretched over the card by
 * `::after`) and which also carries controls. Each control has to paint above the overlay, or a
 * click meant for it lands on the row link: measured without the lift, both the external link and
 * the remove button opened the ad, and no removal reached the backend. jsdom paints nothing, so the
 * stacking is pinned from the stylesheet.
 *
 * The controls are lifted, never the actions zone: the match chip and the gaps belong to the card's
 * target, which is what the card's pointer and hover border promise.
 */
const GLOBALS = readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), "globals.css"), "utf8");

describe("a job card's controls sit above its row link (#2012)", () => {
  it("lifts every link and button in the actions of a card with a row link, at every width", () => {
    const rules = readRules(GLOBALS);
    for (const control of ["a", "button"]) {
      const selector = `.jp-job:has(.jp-job__rowlink) > .jp-job__actions > ${control}`;
      expect(
        rules.filter((r) => r.selector === selector).map((r) => ({ inMedia: r.inMedia, declarations: r.declarations })),
        `${selector} — one rule, outside any media query, setting exactly the lift`,
      ).toEqual([
        {
          inMedia: null,
          declarations: [
            { property: "position", value: "relative" },
            { property: "z-index", value: "1" },
          ],
        },
      ]);
    }
  });

  it("never lifts the actions zone itself", () => {
    const zoneLifts = readRules(GLOBALS)
      .filter((r) => /\.jp-job__actions$/.test(r.selector))
      .filter((r) => r.properties.includes("z-index") || r.properties.includes("position"));
    expect(
      zoneLifts.map((r) => r.selector),
      "a lifted zone takes the match chip and the gaps out of the card's target",
    ).toEqual([]);
  });
});
