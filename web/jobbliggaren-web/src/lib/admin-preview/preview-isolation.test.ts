import { readdirSync, readFileSync } from "node:fs";
import { basename, dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import * as ts from "typescript";
import { describe, expect, it } from "vitest";
import { resolveSpecifier } from "@/i18n/client-namespace-reachability";

/**
 * The admin preview stays apart from the product (ADR 0150 D5). Its route group holds only
 * `*.preview.tsx` files, which are routes in a build made with the flag and nothing otherwise; each
 * of its route files carries the runtime lock; nothing it reaches reads the backend; and nothing
 * outside it reaches its fixtures or its modules. Reach is the resolved import graph, so a re-export,
 * an index import or a dynamic `import()` counts like any other edge.
 */
const SRC = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const PREVIEW_ROUTES = join(SRC, "app", "(admin-preview)");
const PREVIEW_LIB = join(SRC, "lib", "admin-preview");
const FIXTURES = join(PREVIEW_LIB, "fixtures");
const BACKEND_READERS = ["api", "actions", "http"].map((dir) => join(SRC, "lib", dir) + sep);

function walk(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) walk(full, acc);
    else if (/\.(ts|tsx|cjs|mjs)$/.test(entry.name) && !/\.test\.(ts|tsx)$/.test(entry.name)) acc.push(full);
  }
  return acc;
}

/** The modules a file loads at run time: type-only imports and re-exports are erased, so they are no edge. */
function runtimeSpecifiers(file: string): string[] {
  const source = ts.createSourceFile(file, readFileSync(file, "utf8"), ts.ScriptTarget.Latest, true);
  const specifiers: string[] = [];
  const visit = (node: ts.Node) => {
    if (ts.isImportDeclaration(node) && !node.importClause?.isTypeOnly && ts.isStringLiteral(node.moduleSpecifier)) {
      specifiers.push(node.moduleSpecifier.text);
    } else if (
      ts.isExportDeclaration(node) &&
      !node.isTypeOnly &&
      node.moduleSpecifier !== undefined &&
      ts.isStringLiteral(node.moduleSpecifier)
    ) {
      specifiers.push(node.moduleSpecifier.text);
    } else if (
      ts.isCallExpression(node) &&
      node.expression.kind === ts.SyntaxKind.ImportKeyword &&
      node.arguments[0] !== undefined &&
      ts.isStringLiteral(node.arguments[0])
    ) {
      specifiers.push(node.arguments[0].text);
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
  return specifiers;
}

/**
 * Every specifier a file names, type-only ones included: a cheap scan for the check over all of
 * `src`, where an edge too many only makes the check stricter.
 */
function anySpecifiers(file: string): string[] {
  const source = readFileSync(file, "utf8");
  return [...source.matchAll(/(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s+)["']([^"']+)["']/g)].map((match) => match[1] ?? "");
}

const GRAPH_TIMEOUT_MS = 30_000;

/** Every product module the given files reach, themselves included. */
function reach(entries: ReadonlyArray<string>): Set<string> {
  const seen = new Set<string>();
  const queue = [...entries];
  while (queue.length > 0) {
    const file = queue.pop();
    if (file === undefined || seen.has(file)) continue;
    seen.add(file);
    for (const specifier of runtimeSpecifiers(file)) {
      const next = resolveSpecifier(specifier, file, SRC).file;
      if (next !== null) queue.push(next);
    }
  }
  return seen;
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

  it("reaches no module that reads the backend, from any of its routes", { timeout: GRAPH_TIMEOUT_MS }, () => {
    const reached = [...reach(routeFiles)];
    expect(reached.length).toBeGreaterThan(routeFiles.length);

    const readers = reached.filter((file) => {
      if (BACKEND_READERS.some((dir) => file.startsWith(dir))) return true;
      const source = readFileSync(file, "utf8");
      return /^\s*["']use server["']/m.test(source) || /\bfetch\(/.test(source);
    });
    expect(readers.map(rel)).toEqual([]);
  });

  it("is reached by nothing outside the preview", { timeout: GRAPH_TIMEOUT_MS }, () => {
    const outside = walk(SRC).filter((file) => !file.startsWith(PREVIEW_ROUTES) && !file.startsWith(PREVIEW_LIB));
    const importers = outside.filter((file) =>
      anySpecifiers(file).some((specifier) => {
        const target = resolveSpecifier(specifier, file, SRC).file;
        return target !== null && (target.startsWith(PREVIEW_ROUTES) || target.startsWith(FIXTURES));
      }),
    );
    expect(importers.map(rel)).toEqual([]);
  });
});
