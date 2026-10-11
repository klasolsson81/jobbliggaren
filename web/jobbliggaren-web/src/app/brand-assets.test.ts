import { createHash } from "node:crypto";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it, vi } from "vitest";

vi.mock("next-intl/server", () => ({
  getTranslations: async () => (key: string) => key,
  getLocale: async () => "sv",
}));

import manifest from "./manifest";

// The raster brand icons are generated from src/app/icon.svg (scripts/generate-brand-icons.mjs). What these
// tests pin is what a visitor's browser, an Android launcher or a link preview would otherwise get wrong
// without anyone noticing: `/favicon.ico` is what Safari, bookmarks and many embedded browsers fetch directly
// and what a search engine may read, while the SVG and the manifest serve the rest.

const webRoot = resolve(__dirname, "../..");
const read = (relative: string) => readFileSync(resolve(webRoot, relative));

const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

function pngSize(png: Buffer) {
  expect(png.subarray(0, 8).equals(PNG_SIGNATURE)).toBe(true);
  return { width: png.readUInt32BE(16), height: png.readUInt32BE(20) };
}

function icoFrames(ico: Buffer) {
  expect(ico.readUInt16LE(0)).toBe(0);
  expect(ico.readUInt16LE(2)).toBe(1);
  const count = ico.readUInt16LE(4);
  return Array.from({ length: count }, (_, i) => {
    const entry = 6 + i * 16;
    const size = ico.readUInt8(entry) || 256;
    const length = ico.readUInt32LE(entry + 8);
    const offset = ico.readUInt32LE(entry + 12);
    return { size, data: ico.subarray(offset, offset + length) };
  });
}

describe("favicon.ico", () => {
  // Written by: the create-next-app scaffold (commit 6acf85f45), a white triangle on a black circle, which is
  // Vercel's mark. It stayed in the tree for months while icon.svg carried the real mark, so a browser that
  // takes the .ico showed another company's logo. The pin names the one file that must never come back.
  const SCAFFOLD_DEFAULT_SHA256 = "2b8ad2d33455a8f736fc3a8ebf8f0bdea8848ad4c0db48a2833bd0f9cd775932";

  it("is not the create-next-app default", () => {
    const hash = createHash("sha256").update(read("src/app/favicon.ico")).digest("hex");
    expect(hash).not.toBe(SCAFFOLD_DEFAULT_SHA256);
  });

  it("carries PNG frames at 16, 32, 48 and 256", () => {
    const frames = icoFrames(read("src/app/favicon.ico"));
    expect(frames.map((f) => f.size).sort((a, b) => a - b)).toEqual([16, 32, 48, 256]);
    for (const frame of frames) expect(pngSize(frame.data)).toEqual({ width: frame.size, height: frame.size });
  });
});

describe("manifest icons", () => {
  it("names a file that exists, at the size it declares, for every PNG under /icons/", async () => {
    const { icons = [] } = await manifest();
    const files = icons.filter((icon) => icon.src.startsWith("/icons/"));
    expect(files.length).toBeGreaterThanOrEqual(3);
    for (const icon of files) {
      const relative = `public${icon.src}`;
      expect(existsSync(resolve(webRoot, relative)), icon.src).toBe(true);
      const [w, h] = (icon.sizes ?? "").split("x").map(Number);
      expect(pngSize(read(relative)), icon.src).toEqual({ width: w, height: h });
    }
  });

  it("offers what Android needs: a 192 and a 512 PNG, and a maskable 512", async () => {
    const { icons = [] } = await manifest();
    const has = (sizes: string, purpose: string) =>
      icons.some((i) => i.sizes === sizes && i.purpose === purpose && i.type === "image/png");
    expect(has("192x192", "any")).toBe(true);
    expect(has("512x512", "any")).toBe(true);
    expect(has("512x512", "maskable")).toBe(true);
  });
});

describe("public/", () => {
  // Written by: the create-next-app scaffold. Nothing references them, and next.svg is the Next.js/Vercel
  // wordmark served at /next.svg.
  it.each(["next.svg", "globe.svg", "file.svg", "window.svg"])("holds no scaffold leftover %s", (name) => {
    expect(existsSync(resolve(webRoot, "public", name))).toBe(false);
  });
});
