import { readdirSync, readFileSync } from "node:fs";
import { dirname, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import * as ts from "typescript";
import { describe, expect, it } from "vitest";
import { isServerOnlyNamespace } from "./client-messages";
import {
  hasUseClientDirective,
  reachableNamespaces,
  toPosix,
  type ProviderBoundary,
} from "./client-namespace-reachability";

/**
 * Fitness function for the per-boundary client-i18n payload (#737, epic #737;
 * supersedes the #740/#774 single-payload guard).
 *
 * Each `NextIntlClientProvider` declares the namespaces its boundary needs as an
 * array literal at the call site. This test recomputes that set from the import
 * graph and asserts EQUALITY:
 *
 *   - declared ⊉ required → a client component reads a namespace the provider
 *     does not carry: a blank / `MISSING_MESSAGE` at runtime on that route.
 *     Neither tsc nor vitest sees it — the augmented `Messages` type derives
 *     from the FULL sv catalog and `pickClientMessages` returns `as T`, and the
 *     render shim (test/render-intl.tsx) feeds every test the FULL catalog.
 *   - declared ⊅ required → the payload silently re-inflates toward the ~102 KB
 *     this change removed. Subset-only checking would permit exactly that: a
 *     future CC hitting a MISSING_MESSAGE could paste the whole catalog into a
 *     declaration and stay green. ADR 0045 Beslut 6 is a NON-REGRESSION ratchet,
 *     so the guard has to bite in both directions.
 *
 * Deliberate escape hatch: `// payload-allow: <reason>` on the line above a
 * namespace in the declaration exempts it from the "unused" half (mirroring
 * scripts/guard-css.mjs's `guard-allow:` idiom). Nothing exempts the "missing"
 * half — that one is a runtime break.
 *
 * Group membership is a RELATION, not a partition (a shared component reached by
 * three boundaries is required by all three), which is why the old
 * `ADMIN_SURFACE` path heuristic is deleted rather than generalised: "which
 * boundary owns components/job-ads/*" has no true answer.
 *
 * Known limitations, all matched by IDENTIFIER NAME and all measured at zero
 * occurrences on 2026-07-25 (backstopped by the e2e workflow and `next build`'s
 * runtime `MISSING_MESSAGE`):
 *   - an aliased hook import (`import { useTranslations as t }`) is not seen, so
 *     its namespaces never enter `required`;
 *   - an aliased provider import (`import { NextIntlClientProvider as P }`) is
 *     not seen by `findProviderSites`, so R1 would not classify that site.
 * `useMessages()` used to belong here; it now fails loud instead (it reads the
 * whole payload without naming a namespace, so equality cannot see what the
 * component reads).
 */

const SRC_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const TEST_DIR = resolve(SRC_ROOT, "test");

/**
 * Every `NextIntlClientProvider` call site in the app, with the route subtree it
 * wraps. R1: a provider site that is neither listed here nor allowlisted below
 * fails the test — that is the hole a path heuristic would leave open when a
 * seventh boundary is added later.
 */
const BOUNDARIES: readonly ProviderBoundary[] = [
  { name: "root", providerFile: "app/layout.tsx", routeRoot: "app" },
  { name: "(admin)", providerFile: "app/(admin)/layout.tsx", routeRoot: "app/(admin)" },
  { name: "(app)", providerFile: "app/(app)/layout.tsx", routeRoot: "app/(app)" },
  { name: "(auth)", providerFile: "app/(auth)/layout.tsx", routeRoot: "app/(auth)" },
  // routeRoot is `(guest)/gast`, NOT `(guest)`: the provider sits on
  // gast/layout.tsx and wraps only that subtree. Widening it to the group would
  // be fail-OPEN — a future `(guest)/<x>/page.tsx` would be counted into this
  // boundary's required set (declaration grows, test green) while runtime renders
  // it under the root provider's EMPTY payload. With the narrow root, such a file
  // lands in root's walk instead, where both R3 and the root-empty assertion go
  // red immediately.
  { name: "(guest)", providerFile: "app/(guest)/gast/layout.tsx", routeRoot: "app/(guest)/gast" },
  {
    name: "(marketing)",
    providerFile: "app/(marketing)/layout.tsx",
    routeRoot: "app/(marketing)",
  },
  {
    name: "(marketing-inner)",
    providerFile: "app/(marketing-inner)/layout.tsx",
    routeRoot: "app/(marketing-inner)",
  },
  // The last-line 404 for unmatched URLs. routeRoot is the FILE, not "app":
  // it mounts the public chrome, whose <LanguageSwitcher/> is a client
  // component reading `common`. Counted into root's subtree instead, that
  // namespace would land in the root declaration the assertion below pins to
  // [] — and every document in the app would pay for one 404's header.
  { name: "not-found", providerFile: "app/not-found.tsx", routeRoot: "app/not-found.tsx" },
];

/**
 * Provider sites that legitimately do NOT build their payload with
 * `pickClientMessages`, with the reason each is safe.
 *
 * `global-error.tsx` replaces the root layout entirely when the root itself
 * throws (Next file convention), so it cannot inherit any provider. It seeds a
 * single namespace straight from the Swedish catalog (`messages/sv/fallback.json`)
 * — a hardcoded one-namespace payload is the point: the error page must not
 * depend on the request-scoped config that may be what failed.
 */
const SELF_SEEDED_PROVIDERS: readonly { file: string; seeds: readonly string[] }[] = [
  { file: "app/global-error.tsx", seeds: ["fallback"] },
];

/**
 * Every provider site that OWNS a subtree, for exclusion purposes. A self-seeded
 * provider owns exactly its own file: without this, `global-error.tsx` counts as
 * part of the root subtree and root is charged the namespace it seeds itself,
 * which every document in the app would then pay for.
 */
const ALL_PROVIDER_SUBTREES: readonly ProviderBoundary[] = [
  ...BOUNDARIES,
  ...SELF_SEEDED_PROVIDERS.map(({ file }) => ({
    name: file,
    providerFile: file,
    routeRoot: file,
  })),
];

/**
 * Per-boundary floor on reachable client files (R5). If a walk collapses — a
 * changed route-file convention, a broken resolver — the equality assertion
 * would pass vacuously against an empty required set, and the payloads would
 * silently shrink to nothing. "Frånvaro kräver kontrafaktum": set well below
 * today's counts so ordinary file churn is not brittle.
 */
const MIN_CLIENT_FILES: Readonly<Record<string, number>> = {
  root: 1,
  "(admin)": 3,
  "(app)": 120,
  "(auth)": 10,
  "(guest)": 20,
  "(marketing)": 4,
  "(marketing-inner)": 2,
  "not-found": 2,
};

function collectSourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const child = resolve(dir, entry.name);
    if (entry.isDirectory()) {
      if (child === TEST_DIR) continue; // the render shim, not product source
      collectSourceFiles(child, acc);
    } else if (
      /\.(ts|tsx)$/.test(entry.name) &&
      !/\.(test|spec)\.(ts|tsx)$/.test(entry.name) &&
      !entry.name.endsWith(".d.ts")
    ) {
      acc.push(child);
    }
  }
  return acc;
}

function parse(file: string, text: string): ts.SourceFile {
  return ts.createSourceFile(
    file,
    text,
    ts.ScriptTarget.Latest,
    false,
    file.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS
  );
}

/** Files (relative, posix) that render a `NextIntlClientProvider`. */
function findProviderSites(): string[] {
  const sites: string[] = [];
  for (const file of collectSourceFiles(SRC_ROOT)) {
    const text = readFileSync(file, "utf8");
    if (!text.includes("NextIntlClientProvider")) continue;
    const sourceFile = parse(file, text);
    let renders = false;
    const visit = (node: ts.Node): void => {
      if (
        (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) &&
        node.tagName.getText(sourceFile) === "NextIntlClientProvider"
      ) {
        renders = true;
      }
      ts.forEachChild(node, visit);
    };
    visit(sourceFile);
    if (renders) sites.push(toPosix(relative(SRC_ROOT, file)));
  }
  return sites.sort();
}

interface Declaration {
  readonly namespaces: string[];
  /** Namespaces carrying a `// payload-allow:` comment on the preceding line. */
  readonly allowed: Set<string>;
  readonly nonLiteral: boolean;
  readonly callCount: number;
}

/** Extract the array-literal argument of `pickClientMessages(_, [...])` (R2). */
function readDeclaration(relPosix: string): Declaration {
  const file = resolve(SRC_ROOT, relPosix);
  const text = readFileSync(file, "utf8");
  const sourceFile = parse(file, text);
  const lines = text.split(/\r?\n/);
  const namespaces: string[] = [];
  const allowed = new Set<string>();
  let nonLiteral = false;
  let callCount = 0;

  const visit = (node: ts.Node): void => {
    if (
      ts.isCallExpression(node) &&
      ts.isIdentifier(node.expression) &&
      node.expression.text === "pickClientMessages"
    ) {
      callCount += 1;
      const arg = node.arguments[1];
      if (arg && ts.isArrayLiteralExpression(arg)) {
        for (const element of arg.elements) {
          if (ts.isStringLiteralLike(element)) {
            namespaces.push(element.text);
            const line = sourceFile.getLineAndCharacterOfPosition(element.getStart(sourceFile)).line;
            const previous = lines[line - 1] ?? "";
            if (/\/\/\s*payload-allow:/.test(previous)) allowed.add(element.text);
          } else {
            nonLiteral = true;
          }
        }
      } else {
        nonLiteral = true;
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);

  return { namespaces, allowed, nonLiteral, callCount };
}

describe("client i18n payload is scoped per provider boundary (#737)", () => {
  it("every NextIntlClientProvider site is a declared boundary or an allowlisted exception (R1)", () => {
    const sites = findProviderSites();
    const known = new Set<string>([
      ...BOUNDARIES.map((b) => b.providerFile),
      ...SELF_SEEDED_PROVIDERS.map(({ file }) => file),
    ]);
    const unclassified = sites.filter((s) => !known.has(s));


    expect(
      unclassified,
      "A NextIntlClientProvider renders in a file this guard does not know about. " +
        "It ships an unverified client payload — add it to BOUNDARIES (with the " +
        "route subtree it wraps) or, if it seeds its own messages, to " +
        "SELF_SEEDED_PROVIDERS with the reason."
    ).toEqual([]);

    // Counterfactual: if the scan broke, `unclassified` would be empty too.
    expect(sites.length, "provider-site scan found nothing — the scanner is broken").toBeGreaterThanOrEqual(
      BOUNDARIES.length
    );
    for (const boundary of BOUNDARIES) {
      expect(sites, `${boundary.name}: no NextIntlClientProvider in ${boundary.providerFile}`).toContain(
        boundary.providerFile
      );
    }
  });

  it("every boundary declares its namespaces as string literals (R2)", () => {
    for (const boundary of BOUNDARIES) {
      const declaration = readDeclaration(boundary.providerFile);
      expect(
        declaration.callCount,
        `${boundary.name}: expected exactly one pickClientMessages() call in ${boundary.providerFile}`
      ).toBe(1);
      expect(
        declaration.nonLiteral,
        `${boundary.name}: pickClientMessages() must take an array of string literals — ` +
          "a computed set cannot be verified against the import graph"
      ).toBe(false);
    }
  });

  it("no boundary declares a server-only namespace (R6)", () => {
    for (const boundary of BOUNDARIES) {
      const serverOnly = readDeclaration(boundary.providerFile).namespaces.filter(
        isServerOnlyNamespace
      );
      expect(
        serverOnly,
        `${boundary.name}: content-*/metadata/errors are server-rendered only and are ` +
          "filtered by pickClientMessages regardless — remove them from the declaration"
      ).toEqual([]);
    }
  });

  it("each boundary's declaration EQUALS what its client subtree reaches (R3)", () => {
    const problems: string[] = [];

    for (const boundary of BOUNDARIES) {
      const declaration = readDeclaration(boundary.providerFile);
      const reach = reachableNamespaces(boundary, ALL_PROVIDER_SUBTREES, SRC_ROOT);
      const declared = new Set(declaration.namespaces);

      const missing = [...reach.namespaces].filter((ns) => !declared.has(ns)).sort();
      const unused = [...declared]
        .filter((ns) => !reach.namespaces.has(ns) && !declaration.allowed.has(ns))
        .sort();

      if (missing.length > 0) {
        problems.push(
          `  ${boundary.name} (${boundary.providerFile}) MISSING [${missing.join(", ")}] — ` +
            "a client component in this boundary calls useTranslations() for it, so it " +
            "renders blank / MISSING_MESSAGE at runtime. Add it to the declaration."
        );
      }
      if (unused.length > 0) {
        problems.push(
          `  ${boundary.name} (${boundary.providerFile}) UNUSED [${unused.join(", ")}] — ` +
            "no client component in this boundary reads it, so it is dead payload in every " +
            "document this boundary serves. Remove it, or justify with a preceding " +
            "`// payload-allow: <reason>` line."
        );
      }
    }

    if (problems.length > 0) {
      throw new Error(
        "Client i18n payload declarations do not match the import graph:\n" + problems.join("\n")
      );
    }
  });

  it("the import graph is fully resolvable — no non-literal import() or useTranslations() (R4)", () => {
    const problems: string[] = [];
    for (const boundary of BOUNDARIES) {
      const reach = reachableNamespaces(boundary, ALL_PROVIDER_SUBTREES, SRC_ROOT);
      // A non-literal dynamic import makes the walk fail-OPEN: the missed edge
      // shrinks `required`, and the equality check then green-lights a payload
      // that is too small.
      problems.push(...reach.dynamicUnresolved.map((d) => `  ${boundary.name}: ${d.trim()}`));
      problems.push(...reach.unresolvedCalls.map((d) => `  ${boundary.name}: ${d.trim()}`));
      problems.push(...reach.opaqueReferences.map((d) => `  ${boundary.name}: ${d.trim()}`));
    }
    if (problems.length > 0) {
      throw new Error(
        "The payload invariant cannot be proven statically — use string literals, or teach " +
          "the guard:\n" + problems.join("\n")
      );
    }
  });

  it("each boundary reaches a non-trivial client subtree (R5 counterfactual)", () => {
    // Without this, a collapsed walk (changed route conventions, broken
    // resolver) yields an empty required set, equality passes vacuously against
    // an emptied declaration, and every payload silently drops to nothing.
    let totalClientFiles = 0;
    for (const boundary of BOUNDARIES) {
      const reach = reachableNamespaces(boundary, ALL_PROVIDER_SUBTREES, SRC_ROOT);
      totalClientFiles += reach.clientFileCount;
      // A missing floor must FAIL rather than default to 1: a boundary added
      // without one would silently carry no counterfactual — exactly the
      // boundary R1 exists for.
      const floor = MIN_CLIENT_FILES[boundary.name];
      expect(
        floor,
        `${boundary.name}: no MIN_CLIENT_FILES floor — add one, set well below today's count`
      ).toBeDefined();
      expect(
        reach.clientFileCount,
        `${boundary.name}: reached only ${reach.clientFileCount} client files — the import-graph ` +
          "walk looks collapsed, so the equality assertion above is vacuous"
      ).toBeGreaterThanOrEqual(floor ?? Number.POSITIVE_INFINITY);
    }
    expect(totalClientFiles).toBeGreaterThanOrEqual(150);
  });

  it("a self-seeded provider reaches no more than the namespaces it seeds", () => {
    // Otherwise this is the one provider payload in the app nothing verifies.
    // Pull a shared <Button>/ui/dialog into the crash surface and it reads a
    // namespace the hardcoded seed does not carry. Neither the equality check
    // (this file is excluded from root's walk by design), nor
    // global-error.test.tsx (asserts today's three strings), nor a rendered
    // crawl would see that.
    for (const { file, seeds } of SELF_SEEDED_PROVIDERS) {
      const boundary: ProviderBoundary = { name: file, providerFile: file, routeRoot: file };
      const reach = reachableNamespaces(boundary, ALL_PROVIDER_SUBTREES, SRC_ROOT);
      const unseeded = [...reach.namespaces].filter((ns) => !seeds.includes(ns)).sort();
      expect(
        unseeded,
        `${file} seeds [${seeds.join(", ")}] but its client subtree also reads ` +
          `[${unseeded.join(", ")}] — that copy renders blank. Seed it, or keep the ` +
          "surface a leaf."
      ).toEqual([]);
    }
  });

  it("the root boundary carries no namespaces — its payload is paid by every document", () => {
    // Root wraps every route, and React context REPLACES rather than merges, so
    // anything root carries is paid on top of the nested boundary's own set.
    // Root's only client reach is the theme provider, which reads no messages.
    // This is asserted separately from the equality check because it is the
    // property the whole change rests on: if root re-acquires a namespace, all
    // eight measured documents pay for it again.
    const declaration = readDeclaration("app/layout.tsx");
    expect(declaration.namespaces).toEqual([]);
  });

  it("the guard's own escape hatch is not silently in use", () => {
    // A `payload-allow:` that nobody revisits is how a ratchet rots. Surfacing
    // the count keeps its use deliberate and reviewable.
    const inUse = BOUNDARIES.flatMap((boundary) => {
      const declaration = readDeclaration(boundary.providerFile);
      return [...declaration.allowed].map((ns) => `${boundary.name}:${ns}`);
    });
    expect(inUse, "payload-allow exemptions in use — re-justify or remove").toEqual([]);
  });
});

describe("client components only read namespaces their boundary carries", () => {
  it("scans a non-trivial number of client components (counterfactual)", () => {
    let clientFiles = 0;
    for (const file of collectSourceFiles(SRC_ROOT)) {
      const text = readFileSync(file, "utf8");
      if (!text.includes("useTranslations")) continue;
      if (!hasUseClientDirective(parse(file, text))) continue;
      clientFiles += 1;
    }
    // ~114 "use client" files reference useTranslations today; a FLOOR (not just
    // > 0) also catches a partial scan collapse.
    expect(clientFiles).toBeGreaterThanOrEqual(50);
  });
});
