import { readdirSync, readFileSync } from "node:fs";
import { basename, dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * The admin preview stays apart from the product (ADR 0150 D5). Its route group holds only
 * `*.preview.tsx` files, which are routes in a build made with the flag and nothing otherwise; each
 * of its route files carries the runtime lock; it reads no backend; and nothing outside it imports
 * its fixtures or its modules.
 */
const SRC = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const PREVIEW_ROUTES = join(SRC, "app", "(admin-preview)");
const PREVIEW_LIB = join(SRC, "lib", "admin-preview");

function walk(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) walk(full, acc);
    else if (/\.(ts|tsx|cjs|mjs)$/.test(entry.name) && !/\.test\.(ts|tsx)$/.test(entry.name)) acc.push(full);
  }
  return acc;
}

const rel = (file: string) => relative(SRC, file).split(sep).join("/");
const previewFiles = walk(PREVIEW_ROUTES);
const routeFiles = previewFiles.filter((file) => /^(page|layout)\.preview\.tsx$/.test(basename(file)));

describe("the admin preview stays apart from the product (ADR 0150 D5)", () => {
  it("finds the preview's files, so the checks below are not vacuous", () => {
    expect(routeFiles.length).toBeGreaterThanOrEqual(9);
  });

  it("holds only .preview.tsx files, so none is a route without the flag and none reaches an image", () => {
    expect(previewFiles.filter((file) => !file.endsWith(".preview.tsx")).map(rel)).toEqual([]);
  });

  it.each(routeFiles.map(rel))("%s renders per request and checks the flag first", (file) => {
    const source = readFileSync(join(SRC, file), "utf8");
    expect(source).toMatch(/export const dynamic = "force-dynamic";/);
    expect(source).toMatch(/requireAdminPreview\(\);/);
  });

  it("reads no backend", () => {
    const readers = [...previewFiles, ...walk(join(PREVIEW_LIB, "fixtures"))].filter((file) =>
      /from "@\/lib\/(api|actions)|\bfetch\(/.test(readFileSync(file, "utf8")),
    );
    expect(readers.map(rel)).toEqual([]);
  });

  it("is imported by nothing outside the preview", () => {
    const outside = walk(SRC).filter((file) => !file.startsWith(PREVIEW_ROUTES) && !file.startsWith(PREVIEW_LIB));
    const importers = outside.filter((file) =>
      /from "[^"]*(admin-preview\/fixtures|\.preview)"/.test(readFileSync(file, "utf8")),
    );
    expect(importers.map(rel)).toEqual([]);
  });
});
