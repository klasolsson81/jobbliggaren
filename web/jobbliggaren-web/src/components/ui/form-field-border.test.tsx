import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { readFileSync } from "node:fs";
import path from "node:path";
import { Input } from "./input";
import { Textarea } from "./textarea";
import { Select, SelectTrigger } from "./select";

// WCAG 2.1 SC 1.4.11 gives UI components a 3:1 floor against adjacent colour.
// In light the field fill is #FFFFFF on a #F4F6FA canvas (1.08:1), so the fill
// draws no boundary and the border is the control's only affordance. In dark
// the fill itself carries the boundary, which is why the light failure could
// sit unnoticed behind a passing dark theme.
//
// The finding makes two separate claims, so this file carries two guards:
//   1. the three primitives reference the interactive border token, and
//   2. that token's value actually clears 3:1 against the fill.
// Neither guard can fail for the other's reason. A class-string pin stays green
// if someone lowers the token's value; a value assertion stays green if someone
// points the components back at the failing token.

const UI_CONTRAST_FLOOR = 3;

function srgbChannel(value: number): number {
  const c = value / 255;
  return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
}

function relativeLuminance(hex: string): number {
  const h = hex.replace("#", "");
  const channel = (offset: number): number =>
    srgbChannel(parseInt(h.slice(offset, offset + 2), 16));
  return 0.2126 * channel(0) + 0.7152 * channel(2) + 0.0722 * channel(4);
}

function contrastRatio(a: string, b: string): number {
  const [la, lb] = [relativeLuminance(a), relativeLuminance(b)];
  const [hi, lo] = la > lb ? [la, lb] : [lb, la];
  return (hi + 0.05) / (lo + 0.05);
}

// Light-theme custom properties only. The dark blocks are masked out first so a
// dark redefinition of the same token cannot be read as the light value.
function readLightTokens(): Map<string, string> {
  const cssPath = path.resolve(__dirname, "../../app/globals.css");
  // Comments and the `@custom-variant dark (...)` at-rule both mention
  // [data-theme="dark"] without opening a dark block. Left in, the mask below
  // runs from the at-rule on line 11 to the next `{` and swallows the whole
  // light :root, so every token resolves as missing.
  const raw = readFileSync(cssPath, "utf8")
    .replace(/\/\*[\s\S]*?\*\//g, "")
    .replace(/@custom-variant[^;]*;/g, "");

  let masked = "";
  let i = 0;
  while (i < raw.length) {
    const next = raw.indexOf('[data-theme="dark"]', i);
    if (next === -1) {
      masked += raw.slice(i);
      break;
    }
    masked += raw.slice(i, next);
    const open = raw.indexOf("{", next);
    if (open === -1) {
      i = next + 1;
      continue;
    }
    let depth = 0;
    let j = open;
    for (; j < raw.length; j++) {
      if (raw[j] === "{") depth++;
      else if (raw[j] === "}") {
        depth--;
        if (depth === 0) break;
      }
    }
    i = j + 1;
  }

  const tokens = new Map<string, string>();
  const decl = /(--[a-z0-9-]+)\s*:\s*([^;]+);/gi;
  let m: RegExpExecArray | null;
  while ((m = decl.exec(masked)) !== null) {
    const [, name, value] = m;
    if (name && value) tokens.set(name, value.trim());
  }
  return tokens;
}

// Follows `var(--x)` indirection: --jp-surface-primary -> --jp-surface -> #FFFFFF.
function resolveHex(tokens: Map<string, string>, name: string): string {
  let value = tokens.get(name);
  for (let hops = 0; hops < 10; hops++) {
    if (!value) break;
    const hex = value.match(/#[0-9a-f]{6}/i);
    if (hex?.[0]) return hex[0].toUpperCase();
    const ref = value.match(/var\(\s*(--[a-z0-9-]+)/i);
    if (!ref?.[1]) break;
    value = tokens.get(ref[1]);
  }
  throw new Error(`could not resolve ${name} to a hex value`);
}

describe("form-field resting border: token wiring", () => {
  it("Input references the interactive border token, not the decorative one", () => {
    const { container } = render(<Input />);
    const el = container.querySelector('[data-slot="input"]');
    expect(el).not.toBeNull();
    expect(el!.className).toContain("border-border-input");
    expect(el!.className).not.toMatch(/(^|\s)border-input(\s|$)/);
  });

  it("Textarea references the interactive border token, not the decorative one", () => {
    const { container } = render(<Textarea />);
    const el = container.querySelector('[data-slot="textarea"]');
    expect(el).not.toBeNull();
    expect(el!.className).toContain("border-border-input");
    expect(el!.className).not.toMatch(/(^|\s)border-input(\s|$)/);
  });

  it("SelectTrigger references the interactive border token, not the decorative one", () => {
    const { container } = render(
      <Select>
        <SelectTrigger />
      </Select>
    );
    const el = container.querySelector('[data-slot="select-trigger"]');
    expect(el).not.toBeNull();
    expect(el!.className).toContain("border-border-input");
    expect(el!.className).not.toMatch(/(^|\s)border-input(\s|$)/);
  });
});

describe("form-field resting border: token value", () => {
  it("--jp-border-input clears the 3:1 UI floor against the field fill in light", () => {
    const tokens = readLightTokens();
    const border = resolveHex(tokens, "--jp-border-input");
    const fill = resolveHex(tokens, "--jp-surface-primary");
    expect(contrastRatio(border, fill)).toBeGreaterThanOrEqual(UI_CONTRAST_FLOOR);
  });

  it("--jp-border-input clears the 3:1 UI floor against the page canvas in light", () => {
    const tokens = readLightTokens();
    const border = resolveHex(tokens, "--jp-border-input");
    const canvas = resolveHex(tokens, "--jp-surface-2");
    expect(contrastRatio(border, canvas)).toBeGreaterThanOrEqual(UI_CONTRAST_FLOOR);
  });

  it("the contrast helper agrees with the published ratios for both border tokens", () => {
    const tokens = readLightTokens();
    const fill = resolveHex(tokens, "--jp-surface-primary");
    // --jp-border is the decorative token the three primitives used to carry.
    // Pinning its failing ratio keeps the guard honest: if this ever reads as
    // passing, the helper is broken, not the palette.
    expect(contrastRatio(resolveHex(tokens, "--jp-border"), fill)).toBeLessThan(
      UI_CONTRAST_FLOOR
    );
    expect(
      contrastRatio(resolveHex(tokens, "--jp-border-input"), fill)
    ).toBeGreaterThanOrEqual(UI_CONTRAST_FLOOR);
  });
});
