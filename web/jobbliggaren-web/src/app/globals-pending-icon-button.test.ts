import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { readRules } from "@/test/css-rules";

/**
 * #2029 — a row's remove button keeps focus while its removal runs, so it is `aria-disabled`, never
 * `disabled`. A screen reader hears the state; this rule gives sighted users the same signal. jsdom
 * applies no stylesheet, so it is pinned from the source.
 */
const GLOBALS = readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), "globals.css"), "utf8");

describe("a waiting icon button looks unavailable (#2029)", () => {
  it("dims an aria-disabled .jp-icon-btn exactly as a disabled .jp-btn, at every width", () => {
    const rules = readRules(GLOBALS);
    const declarationsOf = (selector: string) =>
      rules.filter((r) => r.selector === selector && r.inMedia === null).map((r) => r.declarations);

    const disabledButton = declarationsOf(".jp-btn:disabled");
    expect(disabledButton).toHaveLength(1);
    expect(declarationsOf('.jp-icon-btn[aria-disabled="true"]')).toEqual(disabledButton);
  });
});
