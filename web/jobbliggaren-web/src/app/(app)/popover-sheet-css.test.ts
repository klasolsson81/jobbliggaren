import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { readRules } from "@/test/css-rules";

/**
 * #2212: a sheet is the only scroller inside it at every height. Its band stays pinned; its column
 * head and footer are pinned only where the viewport is tall enough to keep three rows between
 * them (WCAG technique C34). Measured 2026-10-24 on the branch before this pin, at 640x360,
 * 320x256, 812x375, 844x390 and 683x384: the list scrolled inside a scrolling sheet, and level two
 * showed one row. jsdom applies no stylesheet, so the rules are pinned from the source; the
 * rendered heights are the matrix's.
 */
const here = dirname(fileURLToPath(import.meta.url));
const APP_CSS = readFileSync(resolve(here, "app.css"), "utf8");
const GLOBALS_CSS = readFileSync(resolve(here, "..", "globals.css"), "utf8");
const SHEET = '.jp-popover[data-placement="sheet"]';
const TALL = "@media (min-height: 396px)";
const appRules = readRules(APP_CSS);
const declared = (selector: string, property: string, inMedia: string | null = null) =>
  appRules
    .filter((r) => r.inMedia === inMedia && r.selector === selector)
    .flatMap((r) => r.declarations.filter((d) => d.property === property).map((d) => d.value))
    .at(-1);

describe("the sheet is placed by its attribute alone (#2212)", () => {
  it("is fixed under the header", () => {
    expect(declared(SHEET, "position")).toBe("fixed");
    expect(declared(SHEET, "top")).toBe("calc(var(--jp-header-h) + 8px)");
  });

  it("is never placed by a media query, which could disagree with usePanelPosition's decision", () => {
    // The popover root itself (a compound selector on `.jp-popover`, no descendant), not its parts.
    const root = /^\.jp-popover(?![\w-])[^\s>+~]*$/;
    const placedByMedia = [...readRules(GLOBALS_CSS), ...appRules].filter(
      (r) =>
        r.inMedia !== null &&
        root.test(r.selector) &&
        !r.selector.includes("[data-placement") &&
        r.properties.includes("position"),
    );
    expect(placedByMedia.map((r) => `${r.inMedia} ${r.selector}`)).toEqual([]);
  });

  it("keeps the anchored browse columns' 60vh cap", () => {
    expect(declared(".jp-popover .jp-popover__col", "max-height")).toBe("60vh");
  });
});

describe("a sheet is its own one scroller (#2212)", () => {
  it("scrolls itself", () => {
    expect(declared(SHEET, "overflow-y")).toBe("auto");
  });

  it("lets nothing inside it scroll", () => {
    const scrolls = appRules
      .filter((r) => r.selector.startsWith(`${SHEET} `))
      .flatMap((r) =>
        r.declarations
          .filter((d) => /^overflow(-y)?$/.test(d.property) && d.value !== "visible")
          .map((d) => `${r.selector} { ${d.property}: ${d.value} }`),
      );
    expect(scrolls).toEqual([]);
    expect(declared(`${SHEET} .jp-popover__col`, "overflow")).toBe("visible");
    expect(declared(`${SHEET} .jp-panel__body`, "overflow")).toBe("visible");
    expect(declared(`${SHEET} .jp-hero-chip__list`, "overflow")).toBe("visible");
  });

  it.each([`${SHEET} > .jp-popover__head`, `${SHEET} > .jp-panel__sectionhead`])("pins its band at every height: %s", (band) => {
    expect(declared(band, "position")).toBe("sticky");
    expect(declared(band, "top")).toBe("0");
  });

  it("pins the column head under the band and the footer at the bottom only where three rows still fit", () => {
    expect(declared(`${SHEET} .jp-popover__colhead`, "position")).toBe("static");
    expect(declared(`${SHEET} .jp-popover__colhead`, "position", TALL)).toBe("sticky");
    expect(declared(`${SHEET} .jp-popover__colhead`, "top", TALL)).toBe("var(--sheet-bar-h)");
    expect(declared(`${SHEET} > .jp-popover__foot`, "position")).toBeUndefined();
    expect(declared(`${SHEET} > .jp-popover__foot`, "position", TALL)).toBe("sticky");
    expect(declared(`${SHEET} > .jp-popover__foot`, "bottom", TALL)).toBe("0");
  });
});
