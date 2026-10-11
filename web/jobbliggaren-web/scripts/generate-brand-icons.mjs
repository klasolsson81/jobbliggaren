/**
 * Regenerates the raster brand icons from `src/app/icon.svg`, the mirror of `BrandMarkSvg` (the one geometry
 * source). Run from `web/jobbliggaren-web` after any change to the mark:
 *
 *   node scripts/generate-brand-icons.mjs
 *
 * Writes `src/app/favicon.ico` (PNG frames at 16, 32, 48 and 256) and `public/icons/icon-192.png`,
 * `icon-512.png` and `icon-maskable-512.png`. The maskable icon is the mark full-bleed on the brand green: the
 * ring sits inside the 80 % safe circle, so an Android launcher can crop it to any shape.
 */
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { chromium } from "@playwright/test";

const root = resolve(import.meta.dirname, "..");
const svg = readFileSync(resolve(root, "src/app/icon.svg"), "utf8");
// The maskable background is the mark's own disc colour, read from the SVG so the two cannot drift apart.
const BRAND_GREEN = svg.match(/<circle[^>]*\br="45"[^>]*\bfill="(#[0-9A-Fa-f]{6})"/)?.[1];
if (!BRAND_GREEN) throw new Error("icon.svg has no disc circle (r=45) with a hex fill");

async function render(page, size, background) {
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(
    `<!doctype html><style>html,body{margin:0;background:${background ?? "transparent"}}` +
      `svg{display:block;width:${size}px;height:${size}px}</style>${svg}`
  );
  return page.screenshot({
    type: "png",
    omitBackground: background === undefined,
    clip: { x: 0, y: 0, width: size, height: size },
  });
}

// ICO container with PNG-compressed frames (every current browser reads them). 256 is written as 0.
function packIco(frames) {
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(frames.length, 4);
  let offset = header.length + 16 * frames.length;
  const directory = frames.map(({ size, png }) => {
    const entry = Buffer.alloc(16);
    entry.writeUInt8(size >= 256 ? 0 : size, 0);
    entry.writeUInt8(size >= 256 ? 0 : size, 1);
    entry.writeUInt16LE(1, 4);
    entry.writeUInt16LE(32, 6);
    entry.writeUInt32LE(png.length, 8);
    entry.writeUInt32LE(offset, 12);
    offset += png.length;
    return entry;
  });
  return Buffer.concat([header, ...directory, ...frames.map(({ png }) => png)]);
}

const browser = await chromium.launch();
try {
  const page = await browser.newPage({ deviceScaleFactor: 1 });

  const frames = [];
  for (const size of [16, 32, 48, 256]) frames.push({ size, png: await render(page, size) });
  writeFileSync(resolve(root, "src/app/favicon.ico"), packIco(frames));

  mkdirSync(resolve(root, "public/icons"), { recursive: true });
  writeFileSync(resolve(root, "public/icons/icon-192.png"), await render(page, 192));
  writeFileSync(resolve(root, "public/icons/icon-512.png"), await render(page, 512));
  writeFileSync(resolve(root, "public/icons/icon-maskable-512.png"), await render(page, 512, BRAND_GREEN));
} finally {
  await browser.close();
}
console.log("favicon.ico and public/icons/*.png written from src/app/icon.svg");
