/**
 * Static reachability analysis behind the per-boundary client-i18n payload
 * (#737, the sequel step `b1-full-i18n-catalog-hydrated` left open after #740).
 *
 * A `NextIntlClientProvider` payload is a property of the PROVIDER BOUNDARY —
 * the provider call site plus the route subtree it wraps — not of a route
 * group: `(guest)`'s provider sits at `(guest)/gast/layout.tsx`, and
 * `app/not-found.tsx` is a boundary of exactly one file — so "route group"
 * does not name the unit.
 *
 * For a boundary `b`, `requiredNamespaces(b)` is computed as:
 *   entries(b)  = every `.ts`/`.tsx` source file in b's own subtree (parallel
 *                 routes such as `@modal` included), EXCLUDING subtrees owned by
 *                 a nested boundary — those pay their own way. Not a list of
 *                 Next's route conventions; see SOURCE_FILE for why.
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
import { readdirSync, readFileSync, statSync, type Dirent } from "node:fs";
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
  /**
   * A literal specifier that looks like product source but did not resolve, and
   * `useMessages()` calls. Both defeat the analysis in the fail-OPEN direction:
   * a missed import edge shrinks `required`, and `useMessages()` reads the whole
   * payload without naming a namespace, so no namespace enters `required` at all.
   */
  readonly opaqueReferences: string[];
}

// Deliberately NOT a list of Next's route-file conventions. An allowlist is a
// silent hole: a convention it does not recognise (`forbidden.tsx`,
// `unauthorized.tsx`, `global-not-found.tsx` — all real in Next 16.2.9) is never
// walked, so its namespaces never enter the required set and may be omitted from
// a declaration: blank copy, green test. Closing the CLASS instead of the known
// instances means every source file under the subtree is an entry.
//
// Over-approximating is fail-SAFE in this direction: more entries can only make
// `required` larger, so a declaration must grow, never shrink. Measured
// 2026-07-25: it changes no boundary's computed set — every non-route file under
// app/** is already reachable from a route file — so the safety is free today
// and holds for conventions that do not exist yet.
const SOURCE_FILE = /\.tsx?$/;

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

/** Non-source specifiers a product import may legitimately name. */
const NON_SOURCE_SPECIFIER = /\.(css|json|svg|png|jpe?g|webp|woff2?)$/;

interface Resolution {
  readonly file: string | null;
  /**
   * True when the specifier names product source (relative or `@/`), has no
   * non-source extension, and still did not resolve — i.e. a graph edge is
   * missing and `required` is therefore too small. Distinguished from a package
   * or asset import, which is legitimately not walked.
   */
  readonly opaque: boolean;
}

function resolveSpecifier(spec: string, fromFile: string, srcRoot: string): Resolution {
  let base: string;
  if (spec.startsWith("@/")) base = resolve(srcRoot, spec.slice(2));
  else if (spec.startsWith(".")) base = resolve(dirname(fromFile), spec);
  else return { file: null, opaque: false }; // package import — never product source

  for (const candidate of [
    `${base}.tsx`,
    `${base}.ts`,
    resolve(base, "index.tsx"),
    resolve(base, "index.ts"),
  ]) {
    try {
      if (statSync(candidate).isFile()) return { file: candidate, opaque: false };
    } catch {
      /* not this one */
    }
  }
  // A `.css`/`.json`/asset import resolves to nothing walkable BY DESIGN; a bare
  // or `.ts`/`.tsx` specifier that does not resolve is a hole in the graph.
  return { file: null, opaque: !NON_SOURCE_SPECIFIER.test(spec) };
}

interface FileScan {
  readonly imports: string[];
  readonly namespaces: string[];
  readonly unresolvedTranslations: number;
  readonly unresolvedDynamicImports: number;
  readonly opaque: string[];
  readonly isClient: boolean;
}

function scanFile(file: string, srcRoot: string, cache: Map<string, FileScan>): FileScan {
  const cached = cache.get(file);
  if (cached) return cached;

  const text = readFileSync(file, "utf8");
  const sourceFile = parse(file, text);
  const imports: string[] = [];
  const namespaces: string[] = [];
  const opaque: string[] = [];
  let unresolvedTranslations = 0;
  let unresolvedDynamicImports = 0;

  const take = (spec: string): void => {
    const { file: resolved, opaque: isOpaque } = resolveSpecifier(spec, file, srcRoot);
    if (resolved && !isTestFile(resolved)) imports.push(resolved);
    else if (isOpaque) opaque.push(`import "${spec}" did not resolve to product source`);
  };

  const visit = (node: ts.Node): void => {
    // static `import ... from "x"` / `export ... from "x"`
    if (
      (ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) &&
      node.moduleSpecifier &&
      ts.isStringLiteralLike(node.moduleSpecifier)
    ) {
      take(node.moduleSpecifier.text);
    }

    if (ts.isCallExpression(node)) {
      // dynamic `import("x")` — a non-literal specifier makes the graph
      // fail-OPEN (a missed edge silently shrinks the required set), so it must
      // fail loud rather than be skipped.
      if (node.expression.kind === ts.SyntaxKind.ImportKeyword) {
        const arg = node.arguments[0];
        if (arg && ts.isStringLiteralLike(arg)) take(arg.text);
        else unresolvedDynamicImports += 1;
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

      // `useMessages()` hands a component the WHOLE payload without naming a
      // namespace, so nothing enters `required` and the equality check cannot
      // see what the component reads. Zero occurrences today — this keeps it so.
      if (ts.isIdentifier(node.expression) && node.expression.text === "useMessages") {
        opaque.push("useMessages() reads the whole payload without naming a namespace");
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
    opaque,
    isClient: hasUseClientDirective(sourceFile),
  };
  cache.set(file, scan);
  return scan;
}

/** Source files in `dir`, minus subtrees owned by a nested boundary. */
function collectEntries(dir: string, excluded: string[], acc: string[] = []): string[] {
  const dirents: Dirent[] = readdirSync(dir, { withFileTypes: true });
  for (const entry of dirents) {
    const child = resolve(dir, entry.name);
    // A nested boundary owns its subtree (directory) or itself (file) — either
    // way it pays its own payload, so it is not part of this boundary's walk.
    if (excluded.some((e) => child === e || child.startsWith(e + sep))) continue;
    if (entry.isDirectory()) {
      collectEntries(child, excluded, acc);
    } else if (SOURCE_FILE.test(entry.name) && !isTestFile(child)) {
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
  const opaqueReferences: string[] = [];

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

    // NOT wrapped in a try/catch that continues: swallowing a parse/read error
    // would drop the file's namespaces from `required` and leave the equality
    // check green against a payload that is too small. A file under src/ that
    // cannot be read is a broken tree, and should say so.
    const scan = scanFile(file, srcRoot, cache);

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
    for (const reason of scan.opaque) {
      opaqueReferences.push(`  ${toPosix(relative(srcRoot, file))}: ${reason}`);
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
    opaqueReferences,
  };
}
