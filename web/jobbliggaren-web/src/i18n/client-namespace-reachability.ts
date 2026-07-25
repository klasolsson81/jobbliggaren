/**
 * Static reachability analysis behind the per-boundary client-i18n payload
 * (#737, the sequel step `b1-full-i18n-catalog-hydrated` left open after #740).
 *
 * A `NextIntlClientProvider` payload is a property of the PROVIDER BOUNDARY —
 * the provider call site plus the route subtree it wraps — not of a route
 * group: `(marketing)` has no layout at all and `(guest)`'s provider sits at
 * `(guest)/gast/layout.tsx`, so "route group" does not name the unit.
 *
 * For a boundary `b`, `requiredNamespaces(b)` is computed as:
 *   entries(b)  = every route file in b's own subtree (page/layout/template/
 *                 error/not-found/default, parallel routes included), EXCLUDING
 *                 subtrees owned by a nested boundary — those pay their own way.
 *   walk        = transitive static `import` + `import()` edges from entries.
 *   collect     = `useTranslations("<ns>")` string literals in files carrying a
 *                 `"use client"` directive prologue (and in every file reached
 *                 THROUGH such a file — the whole client subtree ships).
 *
 * Group membership is a RELATION, not a partition: a shared component reached
 * by three boundaries is required by all three. That is why this replaces the
 * `ADMIN_SURFACE` path heuristic rather than generalising it — "which boundary
 * owns components/job-ads/*" has no true answer, and reachability never asks.
 *
 * Exported for the fitness function (`client-namespace-payload.test.ts`); it is
 * deliberately NOT used at runtime. Keeping the graph walk in test-only code
 * means a bug here fails a test loudly instead of shipping a route with missing
 * copy (the reason build-time codegen was rejected — CTO bind D2, 2026-07-25).
 */
import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, relative, resolve, sep } from "node:path";
import * as ts from "typescript";

/** A `NextIntlClientProvider` call site and the route subtree it wraps. */
export interface ProviderBoundary {
  /** Stable name used in failure messages and as the declaration's key. */
  readonly name: string;
  /** File holding the `NextIntlClientProvider` call, relative to src/, posix. */
  readonly providerFile: string;
  /**
   * Route subtree the boundary wraps, relative to src/, posix. A directory
   * collects every route file beneath it; a FILE is its own single entry —
   * `global-error.tsx` replaces the root layout entirely and seeds its own
   * provider, so it is a boundary of exactly one route file, not part of root's
   * subtree.
   */
  readonly routeRoot: string;
}

export interface Reachability {
  /** Top-level namespaces the boundary's client subtree references. */
  readonly namespaces: Set<string>;
  /** Client files reached — the per-boundary counterfactual (R5). */
  readonly clientFileCount: number;
  /** Files reached in total, client or server. */
  readonly fileCount: number;
  /** `import()` / `require` with a non-literal specifier (R4) — fail loud. */
  readonly dynamicUnresolved: string[];
  /** `useTranslations()` with a non-literal namespace — fail loud. */
  readonly unresolvedCalls: string[];
}

const ROUTE_FILE = /^(page|layout|template|error|not-found|default|loading|global-error)\.tsx?$/;

export function toPosix(p: string): string {
  return p.split(sep).join("/");
}

function isTestFile(p: string): boolean {
  const b = p.replace(/\\/g, "/").split("/").pop() ?? "";
  return /\.(test|spec)\.(ts|tsx)$/.test(b) || b.endsWith(".d.ts");
}

/** True iff the file opens with a `"use client"` directive prologue entry. */
export function hasUseClientDirective(sourceFile: ts.SourceFile): boolean {
  for (const stmt of sourceFile.statements) {
    if (ts.isExpressionStatement(stmt) && ts.isStringLiteralLike(stmt.expression)) {
      if (stmt.expression.text === "use client") return true;
    } else {
      break; // the prologue ends at the first non-directive statement
    }
  }
  return false;
}

function parse(file: string, text: string): ts.SourceFile {
  return ts.createSourceFile(
    file,
    text,
    ts.ScriptTarget.Latest,
    /* setParentNodes — the walks use forEachChild only */ false,
    file.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS
  );
}

function resolveSpecifier(spec: string, fromFile: string, srcRoot: string): string | null {
  let base: string;
  if (spec.startsWith("@/")) base = resolve(srcRoot, spec.slice(2));
  else if (spec.startsWith(".")) base = resolve(dirname(fromFile), spec);
  else return null; // package import — never product source

  for (const candidate of [
    `${base}.tsx`,
    `${base}.ts`,
    resolve(base, "index.tsx"),
    resolve(base, "index.ts"),
  ]) {
    try {
      if (statSync(candidate).isFile()) return candidate;
    } catch {
      /* not this one */
    }
  }
  return null;
}

interface FileScan {
  readonly imports: string[];
  readonly namespaces: string[];
  readonly unresolvedTranslations: number;
  readonly unresolvedDynamicImports: number;
  readonly isClient: boolean;
}

function scanFile(file: string, srcRoot: string, cache: Map<string, FileScan>): FileScan {
  const cached = cache.get(file);
  if (cached) return cached;

  const text = readFileSync(file, "utf8");
  const sourceFile = parse(file, text);
  const imports: string[] = [];
  const namespaces: string[] = [];
  let unresolvedTranslations = 0;
  let unresolvedDynamicImports = 0;

  const visit = (node: ts.Node): void => {
    // static `import ... from "x"` / `export ... from "x"`
    if (
      (ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) &&
      node.moduleSpecifier &&
      ts.isStringLiteralLike(node.moduleSpecifier)
    ) {
      const r = resolveSpecifier(node.moduleSpecifier.text, file, srcRoot);
      if (r && !isTestFile(r)) imports.push(r);
    }

    if (ts.isCallExpression(node)) {
      // dynamic `import("x")` — a non-literal specifier makes the graph
      // fail-OPEN (a missed edge silently shrinks the required set), so it must
      // fail loud rather than be skipped.
      if (node.expression.kind === ts.SyntaxKind.ImportKeyword) {
        const arg = node.arguments[0];
        if (arg && ts.isStringLiteralLike(arg)) {
          const r = resolveSpecifier(arg.text, file, srcRoot);
          if (r && !isTestFile(r)) imports.push(r);
        } else {
          unresolvedDynamicImports += 1;
        }
      }

      // `useTranslations("ns.path")` — a real call is a CallExpression; the type
      // position `ReturnType<typeof useTranslations<"validation">>` is a
      // TypeQuery with no arguments, so the AST excludes it for free.
      if (ts.isIdentifier(node.expression) && node.expression.text === "useTranslations") {
        const arg = node.arguments[0];
        if (arg && ts.isStringLiteralLike(arg)) {
          const dot = arg.text.indexOf(".");
          namespaces.push(dot === -1 ? arg.text : arg.text.slice(0, dot));
        } else {
          unresolvedTranslations += 1;
        }
      }
    }

    ts.forEachChild(node, visit);
  };
  visit(sourceFile);

  const scan: FileScan = {
    imports,
    namespaces,
    unresolvedTranslations,
    unresolvedDynamicImports,
    isClient: hasUseClientDirective(sourceFile),
  };
  cache.set(file, scan);
  return scan;
}

/** Route files in `dir`, minus subtrees owned by a nested boundary. */
function collectEntries(dir: string, excluded: string[], acc: string[] = []): string[] {
  let dirents;
  try {
    dirents = readdirSync(dir, { withFileTypes: true });
  } catch {
    return acc;
  }
  for (const entry of dirents) {
    const child = resolve(dir, entry.name);
    // A nested boundary owns its subtree (directory) or itself (file) — either
    // way it pays its own payload, so it is not part of this boundary's walk.
    if (excluded.some((e) => child === e || child.startsWith(e + sep))) continue;
    if (entry.isDirectory()) {
      collectEntries(child, excluded, acc);
    } else if (ROUTE_FILE.test(entry.name) && !isTestFile(child)) {
      acc.push(child);
    }
  }
  return acc;
}

/**
 * Walk `boundary`'s subtree and return the namespaces its client payload must
 * carry. `allBoundaries` is needed to subtract nested boundaries' subtrees.
 */
export function reachableNamespaces(
  boundary: ProviderBoundary,
  allBoundaries: readonly ProviderBoundary[],
  srcRoot: string,
  cache: Map<string, FileScan> = new Map()
): Reachability {
  const routeRoot = resolve(srcRoot, boundary.routeRoot);
  const nested = allBoundaries
    .filter((b) => b.name !== boundary.name)
    .map((b) => resolve(srcRoot, b.routeRoot))
    .filter((other) => other !== routeRoot && other.startsWith(routeRoot + sep));

  const namespaces = new Set<string>();
  const clientFiles = new Set<string>();
  const allFiles = new Set<string>();
  const dynamicUnresolved: string[] = [];
  const unresolvedCalls: string[] = [];

  // (file, inClientSubtree) — a file reached both server-side and through a
  // client boundary must be walked twice; only the client pass contributes.
  const seen = new Set<string>();
  // A file-valued routeRoot is its own single entry (global-error.tsx); a
  // directory collects every route file beneath it, minus nested boundaries.
  const entries = statSync(routeRoot).isFile()
    ? [routeRoot]
    : collectEntries(routeRoot, nested);
  const stack: Array<[string, boolean]> = entries.map((f) => [f, false]);

  while (stack.length > 0) {
    const [file, inClient] = stack.pop()!;
    const key = `${inClient ? "C" : "S"}:${file}`;
    if (seen.has(key)) continue;
    seen.add(key);
    allFiles.add(file);

    let scan: FileScan;
    try {
      scan = scanFile(file, srcRoot, cache);
    } catch {
      continue; // unreadable file — not a product source we can reason about
    }

    const nowClient = inClient || scan.isClient;
    if (nowClient) {
      clientFiles.add(file);
      for (const ns of scan.namespaces) namespaces.add(ns);
      if (scan.unresolvedTranslations > 0) {
        unresolvedCalls.push(
          `  ${toPosix(relative(srcRoot, file))}: ${scan.unresolvedTranslations} ` +
            `useTranslations() call(s) with a non-literal namespace`
        );
      }
    }
    if (scan.unresolvedDynamicImports > 0) {
      dynamicUnresolved.push(
        `  ${toPosix(relative(srcRoot, file))}: ${scan.unresolvedDynamicImports} ` +
          `import() call(s) with a non-literal specifier`
      );
    }

    for (const imp of scan.imports) stack.push([imp, nowClient]);
  }

  return {
    namespaces,
    clientFileCount: clientFiles.size,
    fileCount: allFiles.size,
    dynamicUnresolved,
    unresolvedCalls,
  };
}
