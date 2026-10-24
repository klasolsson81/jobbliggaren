import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { readRules } from "@/test/css-rules";

/**
 * #2212: a sheet's list keeps a floor of three rows (144px) wherever it sits, so the sheet, not the
 * list, gives way on a short viewport. Measured 2026-10-24 on the branch before this pin: in Yrke's
 * sheet at 640x360 and 812x375 the floor sat on the frame that also holds the search field, the
 * browse column shrank to 29px and 45px, and the footer covered its first row. jsdom applies no
 * stylesheet, so the rules are pinned from the source; the rendered heights are the matrix's.
 */
const APP_CSS = readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), "app.css"), "utf8");
const rules = readRules(APP_CSS).filter((r) => r.inMedia === null);
const minHeight = (selector: string) =>
  rules
    .filter((r) => r.selector === selector)
    .flatMap((r) => r.declarations.filter((d) => d.property === "min-height").map((d) => d.value))
    .at(-1);

describe("a sheet's list keeps three rows (#2212)", () => {
  it.each([
    '.jp-popover[data-placement="sheet"] > .jp-popover__body',
    '.jp-popover[data-placement="sheet"] > .jp-panel__body',
    '.jp-popover[data-placement="sheet"] .jp-popover__frame > .jp-popover__body',
    '.jp-popover[data-placement="sheet"] .jp-popover__results',
  ])("%s", (selector) => {
    expect(minHeight(selector)).toBe("144px");
  });

  it("lets Yrke's frame hold its field and that floor, so the sheet scrolls instead", () => {
    expect(minHeight('.jp-popover[data-placement="sheet"] > .jp-popover__frame')).toBe("min-content");
  });
});
