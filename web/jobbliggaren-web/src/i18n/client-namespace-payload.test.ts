import { readdirSync, readFileSync } from "node:fs";
import { dirname, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import * as ts from "typescript";
import { describe, expect, it } from "vitest";
import svMessages from "../../messages/sv";
import { pickClientMessages } from "./client-messages";

/**
 * Fitness function for the #740 client-i18n-payload prune (#774, epic #737).
 *
 * `pickClientMessages` (client-messages.ts) trims the `NextIntlClientProvider`
 * payload to the namespaces client components actually use — stripping
 * `content-*`, `metadata`, `errors` from every provider and `admin` from the
 * root provider (re-provided only in the `(admin)` layout). Neither tsc nor
 * vitest sees that prune:
 *   - the augmented next-intl `Messages` type (types/messages.d.ts) is derived
 *     from the FULL sv catalog, and `pickClientMessages` returns `as T`, so the
 *     compiler treats every namespace as referenceable everywhere;
 *   - the render shim (test/render-intl.tsx) feeds every test the FULL catalog,
 *     so a client component reading a stripped namespace still resolves.
 *
 * So a future `useTranslations("admin")` in a client component outside `(admin)`
 * would fail ONLY at runtime (a blank / `MISSING_MESSAGE` on a specific route).
 * This test closes that gap statically: it parses every `"use client"` file's
 * `useTranslations("<ns>")` calls and asserts the referenced top-level
 * namespaces are a subset of what the applicable client provider carries.
 *
 * The allowed sets are DERIVED from `pickClientMessages` itself (never a
 * hardcoded list), so the guard stays correct when the prune rule changes —
 * one source of truth.
 *
 * Known limitations (backstopped by `next build`'s `MISSING_MESSAGE` at
 * runtime, so this guard is defense-in-depth, not the only net):
 *   - matches `useTranslations` by identifier name; an aliased import
 *     (`import { useTranslations as t }`) is not seen.
 *   - a client hook/helper in a file WITHOUT its own `"use client"` directive
 *     (client-side only transitively) is not classified as a client file, and
 *     the `(admin)` classification is by file path, not the render graph.
 */

// The two client providers carry different sets: the root provider strips
// `admin`; the `(admin)` layout re-provides it. A client file is checked
// against the admin set iff it lives on the admin surface (see ADMIN_SURFACE).
const rootAllowed = new Set(Object.keys(pickClientMessages(svMessages)));
const adminAllowed = new Set(
  Object.keys(pickClientMessages(svMessages, { includeAdmin: true }))
);

const SRC_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
// Only the top-level src/test shim/setup dir is skipped (anchored below), never
// an arbitrary directory named "test" at any depth.
const TEST_DIR = resolve(SRC_ROOT, "test");

// Paths (relative to src/, forward-slash) rendered under the `(admin)` layout's
// provider, which carries `admin`. Verified: the only admin CLIENT component
// (AdminNav) is imported solely by app/(admin)/layout.tsx; every other admin
// consumer under app/(admin)/admin/** is a SERVER component (no "use client")
// reading the full server-side catalog, so it is skipped by this scan. If an
// admin client component is ever imported into a non-admin client tree this
// path heuristic breaks — but the blast radius is exactly one namespace
// (`admin`), since adminAllowed = rootAllowed ∪ {admin}.
const ADMIN_SURFACE = ["app/(admin)/", "components/admin/"];

function toPosix(p: string): string {
  return p.split(sep).join("/");
}

function collectSourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      const child = resolve(dir, entry.name);
      // Skip the test-only render shim/setup dir; everything else is product
      // source. Anchored to src/test so a future product dir merely named
      // "test" is not silently dropped from the scan.
      if (child === TEST_DIR) continue;
      collectSourceFiles(child, acc);
    } else if (
      /\.(ts|tsx)$/.test(entry.name) &&
      !/\.(test|spec)\.(ts|tsx)$/.test(entry.name) &&
      !entry.name.endsWith(".d.ts")
    ) {
      acc.push(resolve(dir, entry.name));
    }
  }
  return acc;
}

/** True iff the file opens with a `"use client"` directive prologue entry. */
function hasUseClientDirective(sourceFile: ts.SourceFile): boolean {
  for (const stmt of sourceFile.statements) {
    if (ts.isExpressionStatement(stmt) && ts.isStringLiteralLike(stmt.expression)) {
      if (stmt.expression.text === "use client") return true;
      // still inside the string-literal directive prologue — keep scanning
    } else {
      break; // the prologue ends at the first non-directive statement
    }
  }
  return false;
}

interface ScanResult {
  namespaces: Set<string>;
  // useTranslations() calls whose namespace argument is not a string literal —
  // the guard cannot statically prove the invariant for these.
  unresolved: number;
}

function scanUseTranslations(sourceFile: ts.SourceFile): ScanResult {
  const namespaces = new Set<string>();
  let unresolved = 0;

  const visit = (node: ts.Node): void => {
    // A real call `useTranslations("x")` is a CallExpression; the type position
    // `ReturnType<typeof useTranslations<"validation">>` is a TypeQuery with no
    // call arguments, so the AST excludes it for free (a regex could not).
    if (
      ts.isCallExpression(node) &&
      ts.isIdentifier(node.expression) &&
      node.expression.text === "useTranslations"
    ) {
      const arg = node.arguments[0];
      if (arg && ts.isStringLiteralLike(arg)) {
        // Top-level namespace = the segment before the first dot
        // (`applications.ui` → `applications`; `content-faq` → `content-faq`).
        const dot = arg.text.indexOf(".");
        namespaces.add(dot === -1 ? arg.text : arg.text.slice(0, dot));
      } else {
        unresolved += 1;
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);

  return { namespaces, unresolved };
}

function isAdminSurface(relPosix: string): boolean {
  return ADMIN_SURFACE.some((prefix) => relPosix.startsWith(prefix));
}

describe("client i18n namespace payload (#774 — guards the #740 prune)", () => {
  const missing: string[] = [];
  const unresolvedCalls: string[] = [];
  let clientFilesScanned = 0;
  let clientFilesWithUseTranslations = 0;

  for (const file of collectSourceFiles(SRC_ROOT)) {
    const text = readFileSync(file, "utf8");
    // `useTranslations` never reaches a server component's client payload; a
    // cheap substring pre-filter avoids parsing the whole tree.
    if (!text.includes("useTranslations")) continue;

    const kind = file.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS;
    const sourceFile = ts.createSourceFile(
      file,
      text,
      ts.ScriptTarget.Latest,
      /* setParentNodes — not needed, the walk uses forEachChild only */ false,
      kind
    );

    // Server components legitimately reach the full catalog — skip them.
    if (!hasUseClientDirective(sourceFile)) continue;
    clientFilesScanned += 1;

    const relPosix = toPosix(relative(SRC_ROOT, file));
    const { namespaces, unresolved } = scanUseTranslations(sourceFile);
    if (namespaces.size > 0 || unresolved > 0) clientFilesWithUseTranslations += 1;

    const admin = isAdminSurface(relPosix);
    const allowed = admin ? adminAllowed : rootAllowed;
    const providerName = admin ? "(admin) provider" : "root provider";

    for (const ns of namespaces) {
      if (!allowed.has(ns)) {
        missing.push(
          `  ${relPosix}: useTranslations("${ns}") — "${ns}" is not in the ${providerName} ` +
            `client payload (the #740 prune strips it). Add it back in pickClientMessages ` +
            `(accept the payload cost) or move the usage server-side / into the (admin) group.`
        );
      }
    }
    if (unresolved > 0) {
      unresolvedCalls.push(
        `  ${relPosix}: ${unresolved} useTranslations() call(s) with a non-literal or missing ` +
          `namespace argument — the guard cannot verify these. Use a string literal or teach the test.`
      );
    }
  }

  it("scans a non-trivial number of client components (counterfactual for a broken scanner)", () => {
    // If the scan silently found nothing (wrong root, changed extension, broken
    // walk), the subset assertion below would pass vacuously. ~114 "use client"
    // files reference useTranslations today; a FLOOR (not just > 0) also catches
    // a partial scan collapse — a refactor shrinking collectSourceFiles to a
    // handful would otherwise let the subset assertion pass vacuously for the
    // rest ("Frånvaro kräver kontrafaktum"). Set well below today's count so
    // file churn does not make it brittle.
    const MIN_CLIENT_FILES = 50;
    expect(clientFilesScanned).toBeGreaterThanOrEqual(MIN_CLIENT_FILES);
    expect(clientFilesWithUseTranslations).toBeGreaterThanOrEqual(MIN_CLIENT_FILES);
  });

  it("every client component only references namespaces its client provider carries", () => {
    if (missing.length > 0) {
      throw new Error(
        "A client component references an i18n namespace the client provider does not carry " +
          "(the #740 prune stripped it). This fails at runtime as a blank / MISSING_MESSAGE:\n" +
          missing.join("\n")
      );
    }
  });

  it("fails loud on useTranslations() calls the guard cannot statically resolve", () => {
    if (unresolvedCalls.length > 0) {
      throw new Error(
        "useTranslations() called with a non-literal namespace in a client component — " +
          "the payload-subset invariant cannot be proven statically:\n" +
          unresolvedCalls.join("\n")
      );
    }
  });
});
