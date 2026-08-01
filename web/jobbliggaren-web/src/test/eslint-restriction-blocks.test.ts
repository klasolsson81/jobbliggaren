import path from "node:path";
import { ESLint } from "eslint";
import { beforeAll, describe, expect, it } from "vitest";

/**
 * The `no-restricted-syntax` blocks in `eslint.config.mjs` are a BLOCKING gate,
 * and this pins them — for the same reason `scripts/`'s CSS guard is unit-tested
 * (`vitest.config.ts`): a wrong answer here fails an innocent commit and tells its
 * author to do the wrong thing.
 *
 * Three axes, because the gate can die on any of them and only the first is
 * visible in a diff:
 *
 * 1. RELATIONS — which block governs which path. Asserted through ESLint's own
 *    config resolution, so the GLOBS are what is pinned. That is where the defect
 *    was: the blocks ignored `*.test.*` while `vitest.config.ts` collects
 *    `{test,spec}`, so a `.spec` file was linted as product code — and the zone
 *    rule's message would have told its author to import the constant, which is
 *    exactly the change that blinds a test as an oracle.
 * 2. SEVERITY — a rule configured `off` leaves every relation below intact and
 *    every selector in place, and enforces nothing.
 * 3. EFFECT — a selector is pinned by what it CATCHES, not by its text. A stray
 *    space in `Literal[value="Europe/Stockholm "]` reads identically to a
 *    substring check and matches nothing.
 *
 * Relations are compared as sets, never counts: two blocks here carry the same
 * NUMBER of selectors with different contents, so a count-based check passes on a
 * swap.
 *
 * Everything is resolved ONCE, in `beforeAll`, and the assertions are then pure
 * comparisons. The hook carries its own ceiling because what it waits for is a
 * one-time flat-config load (`eslint-config-next` plus typescript-eslint), not
 * anything this file computes: ~3s in isolation and 22s inside a fully loaded
 * worker pool, which the default 5s per-test timeout turned into a flake the full
 * suite caught.
 */

// vitest runs with cwd at the package root, where eslint.config.mjs lives.
const ROOT = process.cwd();

/** Paths are probes — ESLint resolves config by glob, so they need not exist. */
const PATHS = {
  product: "src/lib/i18n/format.ts",
  shadcn: "src/components/ui/status-pill.tsx",
  declaration: "src/lib/time/swedish-calendar.ts",
  intlPin: "src/i18n/request.ts",
  harness: "src/test/render-intl.tsx",
  harnessNested: "src/test/deep/nested/helper.ts",
  testFile: "src/lib/time/swedish-calendar.test.ts",
  specFile: "src/lib/time/swedish-calendar.spec.ts",
  shadcnSpec: "src/components/ui/status-pill.spec.tsx",
  harnessTest: "src/test/render-intl.test.tsx",
} as const;

type Probe = keyof typeof PATHS;

/**
 * One predicate per FACET of the restriction set, keyed on something only that
 * facet's selectors say. Membership is asserted for every one, because the
 * relations below all hold trivially once a whole group is gone: `allExcept()`
 * composes by subtraction, so deleting a group from `ALL_GROUPS` removes it from
 * every block in ONE line — cheaper than it was before this PR, which is why the
 * membership assertions matter more after it than before.
 *
 * Facet, not group: `text-` alone would be satisfied by MUTED's own selectors, so
 * a TYPOGRAPHY predicate written that loosely passes with TYPOGRAPHY deleted. The
 * keys below are taken from the selector strings themselves and are disjoint.
 */
const FACETS = {
  "copy: em-dash": (s: string) => s.includes("—"),
  "copy: ellipsis": (s: string) => s.includes("\\.\\.\\."),
  "typography: arbitrary px": (s: string) => s.includes("text-\\[[0-9]"),
  "typography: default scale": (s: string) => s.includes("text-(xs|sm|base"),
  "typography: raw gray palette": (s: string) =>
    s.includes("(slate|gray|zinc|neutral|stone)"),
  "typography: inline style": (s: string) => s.includes('key.name="fontSize"'),
  "muted-foreground": (s: string) => s.includes("text-muted-foreground"),
  "server action E352": (s: string) => s.includes('directive="use server"'),
  zone: (s: string) => s.includes("Europe/Stockholm"),
} as const;

const isZone = FACETS.zone;
const isMuted = FACETS["muted-foreground"];

type Restriction = { selector: string };

function isRestriction(value: unknown): value is Restriction {
  return (
    typeof value === "object" &&
    value !== null &&
    typeof (value as { selector?: unknown }).selector === "string"
  );
}

/** The configured `no-restricted-syntax` entry, unread and unnarrowed. */
type RuleEntry = { severity: unknown; selectors: string[]; configured: boolean };

const ZONE_MESSAGE = "The raw zone literal is forbidden";
const RAW_LITERAL = 'const probe = "Europe/Stockholm";';

let rules: Record<Probe, RuleEntry>;
let effect: Record<string, number>;

beforeAll(async () => {
  const eslint = new ESLint({ cwd: ROOT });

  const resolved = await Promise.all(
    (Object.entries(PATHS) as [Probe, string][]).map(async ([key, file]) => {
      const config: unknown = await eslint.calculateConfigForFile(path.join(ROOT, file));
      const configuredRules =
        typeof config === "object" && config !== null
          ? (config as { rules?: Record<string, unknown> }).rules
          : undefined;
      const entry = configuredRules?.["no-restricted-syntax"];
      const list = Array.isArray(entry) ? entry : [];
      return [
        key,
        {
          // A rule that is present but `off` enforces nothing, so severity is
          // read rather than sliced away.
          severity: list[0],
          selectors: list.slice(1).filter(isRestriction).map((r) => r.selector),
          configured: entry !== undefined,
        },
      ] as const;
    }),
  );

  rules = Object.fromEntries(resolved) as Record<Probe, RuleEntry>;

  // EFFECT: lint the same source text against different paths and count the zone
  // rule's own errors. This is what a selector-text check cannot do — a selector
  // that matches nothing still reads correctly.
  const countZoneErrors = async (code: string, file: string): Promise<number> => {
    const [result] = await eslint.lintText(code, { filePath: path.join(ROOT, file) });
    // Fail closed: no result would otherwise read as "no violations", which is the
    // answer every one of these cases is looking for.
    if (!result) throw new Error(`ESLint returned no result for ${file}`);
    return result.messages.filter(
      (m) => m.ruleId === "no-restricted-syntax" && m.message.startsWith(ZONE_MESSAGE),
    ).length;
  };

  effect = {
    productRaw: await countZoneErrors(RAW_LITERAL, PATHS.product),
    productTemplate: await countZoneErrors(
      "const probe = `Europe/Stockholm`;",
      PATHS.product,
    ),
    productSubstring: await countZoneErrors(
      'const probe = "formats a date in Europe/Stockholm";',
      PATHS.product,
    ),
    productComment: await countZoneErrors("// Europe/Stockholm", PATHS.product),
    shadcnRaw: await countZoneErrors(RAW_LITERAL, PATHS.shadcn),
    intlPinRaw: await countZoneErrors(RAW_LITERAL, PATHS.intlPin),
    declarationRaw: await countZoneErrors(RAW_LITERAL, PATHS.declaration),
    harnessRaw: await countZoneErrors(RAW_LITERAL, PATHS.harness),
    specRaw: await countZoneErrors(RAW_LITERAL, PATHS.specFile),
  };
}, 120_000);

describe("eslint.config.mjs — no-restricted-syntax blocks", () => {
  describe("severity", () => {
    it.each<[string, Probe]>([
      ["a product file", "product"],
      ["a shadcn primitive", "shadcn"],
      ["an exempt path", "intlPin"],
    ])("configures the rule as an ERROR for %s, not warn and not off", (_label, probe) => {
      // `calculateConfigForFile` normalises severity to a number: 2 error, 1 warn,
      // 0 off. Measured, not assumed — the string form fails here.
      expect(rules[probe].severity).toBe(2);
    });
  });

  describe("membership", () => {
    it.each(Object.entries(FACETS))(
      "keeps the %s selectors reachable from a product file",
      (_name, matches) => {
        expect(rules.product.selectors.some(matches)).toBe(true);
      },
    );
  });

  describe("relations", () => {
    it("gives shadcn primitives the product set MINUS muted, zone kept", () => {
      expect(new Set(rules.shadcn.selectors)).toEqual(
        new Set(rules.product.selectors.filter((s) => !isMuted(s))),
      );
      expect(rules.shadcn.selectors.some(isZone)).toBe(true);
    });

    it.each<[string, Probe]>([
      ["the zone declaration itself", "declaration"],
      ["the global next-intl pin", "intlPin"],
      ["the test harness directory", "harness"],
      ["a nested file in the harness directory", "harnessNested"],
    ])("gives %s the product set MINUS zone", (_label, probe) => {
      expect(new Set(rules[probe].selectors)).toEqual(
        new Set(rules.product.selectors.filter((s) => !isZone(s))),
      );
    });

    it.each<[string, Probe]>([
      ["a .test file", "testFile"],
      ["a .spec file", "specFile"],
      ["a .spec file among the shadcn primitives", "shadcnSpec"],
      ["a .test file inside the harness directory", "harnessTest"],
    ])(
      "leaves the rule UNCONFIGURED for %s — test-hood follows {test,spec}, not just .test",
      (_label, probe) => {
        // `toBeUndefined`, not `toEqual([])`: an empty selector list is also what
        // an unreadable config shape would produce, and those are different facts.
        expect(rules[probe].configured).toBe(false);
        expect(rules[probe].selectors).toEqual([]);
      },
    );
  });

  describe("effect — what the selectors actually catch", () => {
    it("catches the literal, and the template form, in product code", () => {
      expect(effect.productRaw).toBe(1);
      expect(effect.productTemplate).toBe(1);
      expect(effect.shadcnRaw).toBe(1);
    });

    it("is silent on a substring and on a comment — the single normaliser", () => {
      // Value-equality is the whole population rule. A test NAME reading
      // "formats … in Europe/Stockholm" is this case, and must not be caught.
      expect(effect.productSubstring).toBe(0);
      expect(effect.productComment).toBe(0);
    });

    it.each([
      ["the global next-intl pin", "intlPinRaw"],
      ["the declaration file", "declarationRaw"],
      ["the test harness", "harnessRaw"],
      ["a .spec file", "specRaw"],
    ])("is silent in %s — the exemption is load-bearing", (_label, key) => {
      expect(effect[key]).toBe(0);
    });
  });
});
