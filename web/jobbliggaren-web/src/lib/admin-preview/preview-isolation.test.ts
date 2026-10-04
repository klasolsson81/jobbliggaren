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
 * outside it imports its routes, its fixtures, or a module of its library that leads to either.
 * Reach is the resolved import graph, so a re-export, an index import or a dynamic `import()` counts
 * like any other edge, and an import that names product source but does not resolve fails the check.
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

/**
 * The modules a file loads at run time, and every `import()` whose argument is not a literal: type-only
 * imports and re-exports are erased, so they are no edge.
 */
function runtimeSpecifiers(file: string): { readonly specifiers: string[]; readonly computed: string[] } {
  const source = ts.createSourceFile(file, readFileSync(file, "utf8"), ts.ScriptTarget.Latest, true);
  const specifiers: string[] = [];
  const computed: string[] = [];
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
    } else if (ts.isCallExpression(node) && node.expression.kind === ts.SyntaxKind.ImportKeyword) {
      const argument = node.arguments[0];
      if (argument !== undefined && ts.isStringLiteralLike(argument)) specifiers.push(argument.text);
      else computed.push(node.getText(source));
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
  return { specifiers, computed };
}

/**
 * Every specifier a file names, type-only ones included: a cheap scan for the check over all of
 * `src`, where an edge too many only makes the check stricter.
 */
function anySpecifiers(file: string): string[] {
  const source = readFileSync(file, "utf8");
  return [...source.matchAll(/(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s+)["'`]([^"'`]+)["'`]/g)].map((match) => match[1] ?? "");
}

const GRAPH_TIMEOUT_MS = 30_000;

const rel = (file: string) => relative(SRC, file).split(sep).join("/");

/** Every product module the given files reach, themselves included, and every edge that did not resolve. */
function reach(entries: ReadonlyArray<string>): { readonly files: Set<string>; readonly opaque: string[] } {
  const files = new Set<string>();
  const opaque: string[] = [];
  const queue = [...entries];
  while (queue.length > 0) {
    const file = queue.pop();
    if (file === undefined || files.has(file)) continue;
    files.add(file);
    const { specifiers, computed } = runtimeSpecifiers(file);
    for (const specifier of specifiers) {
      const next = resolveSpecifier(specifier, file, SRC);
      if (next.file !== null) queue.push(next.file);
      else if (next.opaque) opaque.push(`${rel(file)} -> ${specifier}`);
    }
    for (const call of computed) opaque.push(`${rel(file)} -> ${call}`);
  }
  return { files, opaque };
}

const inPreview = (file: string) => file.startsWith(PREVIEW_ROUTES) || file.startsWith(FIXTURES);

function isClientModule(file: string): boolean {
  const first = ts.createSourceFile(file, readFileSync(file, "utf8"), ts.ScriptTarget.Latest, true).statements[0];
  return (
    first !== undefined &&
    ts.isExpressionStatement(first) &&
    ts.isStringLiteral(first.expression) &&
    first.expression.text === "use client"
  );
}

const previewFiles = walk(PREVIEW_ROUTES);
const routeFiles = previewFiles.filter((file) => /^(page|layout)\.preview\.tsx$/.test(basename(file)));

describe("the admin preview stays apart from the product (ADR 0150 D5)", () => {
  it("finds the preview's files, so the checks below are not vacuous", () => {
    expect(routeFiles.length).toBeGreaterThanOrEqual(9);
  });

  it("holds only .preview.tsx files, so none is a route without the flag and none reaches an image", () => {
    expect(previewFiles.filter((file) => !file.endsWith(".preview.tsx")).map(rel)).toEqual([]);
  });

  it("holds only pages, layouts and private _preview/ modules, so no route skips the gate", () => {
    const strays = previewFiles.filter((file) => !routeFiles.includes(file) && !rel(file).split("/").includes("_preview"));
    expect(strays.map(rel)).toEqual([]);
  });

  it.each(routeFiles.map(rel))("%s renders per request and checks the flag first", (file) => {
    const source = readFileSync(join(SRC, file), "utf8");
    expect(source).toMatch(/export const dynamic = "force-dynamic";/);
    expect(source).toMatch(/requireAdminPreview\(\);/);
  });

  it("reaches no module that reads the backend, from any of its routes", { timeout: GRAPH_TIMEOUT_MS }, () => {
    const { files, opaque } = reach(routeFiles);
    const reached = [...files];
    expect(reached.length).toBeGreaterThan(routeFiles.length);
    expect(opaque).toEqual([]);

    const readers = reached.filter((file) => {
      if (BACKEND_READERS.some((dir) => file.startsWith(dir))) return true;
      const source = readFileSync(file, "utf8");
      return /^\s*["']use server["']/m.test(source) || /\bfetch\(/.test(source);
    });
    expect(readers.map(rel)).toEqual([]);
  });

  it("ships no fixture in a browser chunk: no client module reaches the fixtures at run time", { timeout: GRAPH_TIMEOUT_MS }, () => {
    const clients = [...reach(routeFiles).files].filter(isClientModule);
    expect(clients.length).toBeGreaterThan(0);
    const leaks = clients.filter((file) => [...reach([file]).files].some((reached) => reached.startsWith(FIXTURES)));
    expect(leaks.map(rel)).toEqual([]);
  });

  it("is reached by nothing outside the preview", { timeout: GRAPH_TIMEOUT_MS }, () => {
    // A library module that leads into the routes or the fixtures is as much the preview as they are.
    const library = walk(PREVIEW_LIB).filter((file) => !file.startsWith(FIXTURES));
    const leading = library.filter((file) => [...reach([file]).files].some(inPreview));
    const isPreview = (file: string) => inPreview(file) || leading.includes(file);

    const outside = walk(SRC).filter((file) => !file.startsWith(PREVIEW_ROUTES) && !file.startsWith(PREVIEW_LIB));
    const importers = outside.filter((file) =>
      anySpecifiers(file).some((specifier) => {
        const target = resolveSpecifier(specifier, file, SRC).file;
        return target !== null && isPreview(target);
      }),
    );
    expect(importers.map(rel)).toEqual([]);
  });
});
