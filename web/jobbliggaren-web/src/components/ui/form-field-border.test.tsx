import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { readFileSync } from "node:fs";
import path from "node:path";
import { Input } from "./input";
import { Textarea } from "./textarea";
import { Select, SelectTrigger } from "./select";

// WCAG 2.1 SC 1.4.11 gives UI components a 3:1 floor against adjacent colour.
// In light the field fill and the page canvas are near-identical, so the fill
// draws no boundary and the border is the control's only affordance. In dark
// the fill itself carries the boundary, which is why the light failure could
// sit unnoticed behind a passing dark theme. Both halves of that asymmetry are
// asserted below rather than stated here, so neither can decay into a false
// comment.
//
// The chain that carries the finding has three links, and each is pinned:
//   1. the three primitives reference the interactive border utility,
//   2. `@theme inline` bridges that utility to the source token, and
//   3. the token's value clears 3:1 against the fill.
// No guard can fail for another's reason. A class-string pin stays green if
// someone lowers the token's value; a value assertion stays green if someone
// points the components back at the failing token; and both stayed green when
// the bridge row was deleted -- Tailwind v4 is css-first, so it drops an
// unknown utility silently and the border falls back with nothing failing.
// That third link is why link 3 resolves THROUGH the bridge symbol.

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

const DARK_SELECTOR = '[data-theme="dark"]';

function readCss(): string {
  const cssPath = path.resolve(__dirname, "../../app/globals.css");
  // Comments and the `@custom-variant` at-rule both mention the dark selector
  // without opening a dark block. Left in, the scanner below runs from the
  // at-rule to the next `{` and swallows the whole light :root, so every token
  // resolves as missing.
  return readFileSync(cssPath, "utf8")
    .replace(/\/\*[\s\S]*?\*\//g, "")
    .replace(/@custom-variant[^;]*;/g, "");
}

function blockEnd(css: string, from: number): number {
  const open = css.indexOf("{", from);
  if (open === -1) return -1;
  let depth = 0;
  for (let j = open; j < css.length; j++) {
    if (css[j] === "{") depth++;
    else if (css[j] === "}") {
      depth--;
      if (depth === 0) return j;
    }
  }
  return -1;
}

function collect(css: string): Map<string, string> {
  const tokens = new Map<string, string>();
  const decl = /(--[a-z0-9-]+)\s*:\s*([^;]+);/gi;
  let m: RegExpExecArray | null;
  while ((m = decl.exec(css)) !== null) {
    const [, name, value] = m;
    if (name && value) tokens.set(name, value.trim());
  }
  return tokens;
}

// Light-theme custom properties only: the dark blocks are cut out so a dark
// redefinition of the same token cannot be read as the light value.
function readLightTokens(): Map<string, string> {
  const raw = readCss();
  let masked = "";
  let i = 0;
  while (i < raw.length) {
    const next = raw.indexOf(DARK_SELECTOR, i);
    if (next === -1) {
      masked += raw.slice(i);
      break;
    }
    masked += raw.slice(i, next);
    const end = blockEnd(raw, next);
    if (end === -1) {
      i = next + 1;
      continue;
    }
    i = end + 1;
  }
  return collect(masked);
}

// The mirror image: light declarations first (so `var()` chains into shared
// tokens still resolve), then the ROOT dark blocks layered on top.
//
// Only the root blocks. globals.css also carries scoped dark overrides such as
// `[data-theme="dark"] .jp-header`, which deliberately re-declares
// --jp-surface as white because the header stays light in dark mode. Those are
// values for one subtree, not the theme, and folding them in reported the dark
// field as 1.10:1 against a white surface it never sits on.
function readDarkTokens(): Map<string, string> {
  const raw = readCss();
  const tokens = readLightTokens();
  let i = 0;
  while (i < raw.length) {
    const next = raw.indexOf(DARK_SELECTOR, i);
    if (next === -1) break;
    const end = blockEnd(raw, next);
    if (end === -1) {
      i = next + 1;
      continue;
    }
    const between = raw.slice(next + DARK_SELECTOR.length, raw.indexOf("{", next));
    if (between.trim() === "") {
      for (const [k, v] of collect(raw.slice(next, end + 1))) tokens.set(k, v);
    }
    i = end + 1;
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

describe("form-field resting border: the @theme bridge", () => {
  it("--color-border-input exists and routes the utility to the interactive token", () => {
    const tokens = readLightTokens();
    // globals.css is the only source of the `border-border-input` utility --
    // there is no tailwind.config.*, so v4 generates it from this row alone.
    expect(tokens.get("--color-border-input")).toBe("var(--jp-border-input)");
  });
});

describe("form-field resting border: token value", () => {
  // Resolved through the bridge symbol, not the source token: that is what makes
  // deleting the @theme row fail these assertions instead of passing silently.
  it("the bridged border token clears the 3:1 UI floor against the field fill in light", () => {
    const tokens = readLightTokens();
    const border = resolveHex(tokens, "--color-border-input");
    const fill = resolveHex(tokens, "--jp-surface-primary");
    expect(contrastRatio(border, fill)).toBeGreaterThanOrEqual(UI_CONTRAST_FLOOR);
  });

  it("the bridged border token clears the 3:1 UI floor against the page canvas in light", () => {
    const tokens = readLightTokens();
    const border = resolveHex(tokens, "--color-border-input");
    const canvas = resolveHex(tokens, "--jp-surface-2");
    expect(contrastRatio(border, canvas)).toBeGreaterThanOrEqual(UI_CONTRAST_FLOOR);
  });

  it("the light fill draws no boundary of its own, so the border must", () => {
    const tokens = readLightTokens();
    const fill = resolveHex(tokens, "--jp-surface-primary");
    const canvas = resolveHex(tokens, "--jp-surface-2");
    // The premise of the whole finding: in light these two are near-identical,
    // so nothing but the border separates the control from the page.
    expect(contrastRatio(fill, canvas)).toBeLessThan(UI_CONTRAST_FLOOR);
  });

  it("in dark the fill carries the boundary instead, which is why light failed alone", () => {
    const dark = readDarkTokens();
    const fill = resolveHex(dark, "--jp-dark-field-bg");
    const surface = resolveHex(dark, "--jp-surface");
    expect(contrastRatio(fill, surface)).toBeGreaterThanOrEqual(UI_CONTRAST_FLOOR);
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
