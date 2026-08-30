import path from "node:path";
import { ESLint } from "eslint";
import { beforeAll, describe, expect, it } from "vitest";

/**
 * The `no-restricted-syntax` blocks in `eslint.config.mjs` are a BLOCKING gate,
 * and this pins them — for the same reason `scripts/`'s CSS guard is unit-tested
 * (`vitest.config.ts`): a wrong answer here fails an innocent commit and tells its
 * author to do the wrong thing.
 *
 * Four axes, because the gate can die on any of them and only the first is
 * visible in a diff:
 *
 * 1. REACH — which block governs which path, asserted through ESLint's own config
 *    resolution so the GLOBS are what is pinned. That is where the defect was: the
 *    blocks ignored `*.test.*` while `vitest.config.ts` collects `{test,spec}`.
 *    Reach is also the cheapest thing to narrow: `src/**` shortened to
 *    `src/lib/**` costs 491 files every group at once, so the probe set spans
 *    `src/app/` and non-`ui` `src/components/` as well.
 * 2. SEVERITY — a rule configured `off` leaves every relation intact and every
 *    selector in place, and enforces nothing.
 * 3. MEMBERSHIP — `allExcept()` composes by subtraction, so deleting a group from
 *    `ALL_GROUPS` removes it from every block in ONE line, and the relations below
 *    then hold trivially. Asserted per FACET, and the facets must PARTITION the
 *    selector set: a group added without a facet fails, which is the direction a
 *    hand-kept mirror list otherwise loses silently.
 * 4. EFFECT — a selector is pinned by what it CATCHES, not by its text. A stray
 *    space in `Literal[value="Europe/Stockholm "]` reads identically and matches
 *    nothing; `text-muted-foreground` broken to `…-NOPE` keeps its facet satisfied
 *    and kills a design-token ban. Every group is probed, not only ZONE — an
 *    earlier version of this file claimed the axis and covered one group of five.
 *
 * Relations are compared as sets, never counts: two blocks carry the same NUMBER
 * of selectors with different contents, so a count-based check passes on a swap.
 *
 * Effect counts the RULE's messages rather than matching their prose, so rewording
 * a message cannot make a zero-assertion vacuously true. Each probe that is
 * expected to fire triggers exactly one restriction; the rest are written to
 * trigger none, which is the assertion they exist for.
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

/**
 * Paths are probes — ESLint resolves config by glob, so they need not exist, and
 * four of these deliberately do not: the gate is prospective, and requiring the
 * files to exist would repeal it.
 */
const PATHS = {
  product: "src/lib/i18n/format.ts",
  productApp: "src/app/(app)/aktivitetsrapport/page.tsx",
  productComponent: "src/components/aktivitetsrapport/activity-report-view.tsx",
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
 * One predicate per FACET of the restriction set, keyed on a substring only that
 * facet's selectors carry. Facet, not group: `text-` alone is satisfied by MUTED's
 * own selectors, so a TYPOGRAPHY predicate written that loosely passes with
 * TYPOGRAPHY deleted.
 */
const FACETS = {
  "copy: em-dash": (s: string) => s.includes("—"),
  "copy: ellipsis": (s: string) => s.includes("\\.\\.\\."),
  "typography: arbitrary px": (s: string) => s.includes("text-\\[[0-9]"),
  "typography: default scale": (s: string) => s.includes("text-(xs|sm|base"),
  "typography: raw gray palette": (s: string) =>
    s.includes("(slate|gray|zinc|neutral|stone)"),
  "typography: inline style": (s: string) => s.includes('JSXAttribute[name.name="style"]'),
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

type ResolvedRule = {
  severity: unknown;
  selectors: string[];
  configured: boolean;
  /** Counterfactual for the absence assertions: is the file linted AT ALL? */
  totalRules: number;
};

const RULE = "no-restricted-syntax";
const RAW_LITERAL = 'const probe = "Europe/Stockholm";';

let rules: Record<Probe, ResolvedRule>;
let effect: Record<string, number>;
let zoneMessages: string[];

beforeAll(async () => {
  const eslint = new ESLint({ cwd: ROOT });

  const resolved = await Promise.all(
    (Object.entries(PATHS) as [Probe, string][]).map(async ([key, file]) => {
      const config: unknown = await eslint.calculateConfigForFile(path.join(ROOT, file));
      const configuredRules =
        typeof config === "object" && config !== null
          ? (config as { rules?: Record<string, unknown> }).rules
          : undefined;
      const entry = configuredRules?.[RULE];
      const list = Array.isArray(entry) ? entry : [];
      return [
        key,
        {
          // A rule that is present but `off` enforces nothing, so severity is read
          // rather than sliced away.
          severity: list[0],
          selectors: list.slice(1).filter(isRestriction).map((r) => r.selector),
          configured: entry !== undefined,
          totalRules: Object.keys(configuredRules ?? {}).length,
        },
      ] as const;
    }),
  );

  rules = Object.fromEntries(resolved) as Record<Probe, ResolvedRule>;

  /** Messages this rule produced — not a text match on any particular one. */
  const lint = async (code: string, file: string): Promise<string[]> => {
    const [result] = await eslint.lintText(code, { filePath: path.join(ROOT, file) });
    // Fail closed: no result would otherwise read as "no violations", which is the
    // answer half of these cases are looking for.
    if (!result) throw new Error(`ESLint returned no result for ${file}`);
    return result.messages.filter((m) => m.ruleId === RULE).map((m) => m.message);
  };

  const count = async (code: string, file: string): Promise<number> =>
    (await lint(code, file)).length;

  zoneMessages = await lint(RAW_LITERAL, PATHS.product);

  effect = {
    // ZONE, in three positions and two shapes.
    zoneValue: await count(RAW_LITERAL, PATHS.product),
    zoneTemplate: await count("const probe = `Europe/Stockholm`;", PATHS.product),
    zoneJsxAttribute: await count(
      'const probe = <T tz="Europe/Stockholm" />;',
      PATHS.productComponent,
    ),
    zoneShadcn: await count(RAW_LITERAL, PATHS.shadcn),
    // ZONE must NOT fire on these — the single normaliser, stated as value equality.
    zoneSubstring: await count(
      'const probe = "formats a date in Europe/Stockholm";',
      PATHS.product,
    ),
    zoneComment: await count("// Europe/Stockholm", PATHS.product),
    // ZONE exemptions, each measured on the path it exempts.
    zoneIntlPin: await count(RAW_LITERAL, PATHS.intlPin),
    zoneDeclaration: await count(RAW_LITERAL, PATHS.declaration),
    zoneHarness: await count(RAW_LITERAL, PATHS.harness),
    zoneSpec: await count(RAW_LITERAL, PATHS.specFile),
    // The other four groups, so "pinned by what it catches" is true of the whole
    // rule and not only of the group this PR added.
    copyEmDashInJsx: await count("const probe = <p>a — b</p>;", PATHS.productComponent),
    copyEllipsis: await count('const probe = "vänta...";', PATHS.product),
    typographyDefaultScale: await count('const probe = "text-sm";', PATHS.product),
    typographyInlineStyle: await count(
      'const probe = <div style={{ color: "red" }} />;',
      PATHS.productComponent,
    ),
    mutedInProduct: await count('const probe = "text-muted-foreground";', PATHS.product),
    mutedInShadcn: await count('const probe = "text-muted-foreground";', PATHS.shadcn),
    serverActionSpecifier: await count(
      '"use server";\nexport { helper } from "./helper";',
      PATHS.product,
    ),
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

    it("leaves no selector unclaimed — a new group needs a facet", () => {
      // The direction a hand-kept mirror list loses silently. `allExcept()` gives a
      // new group every block by construction; without this, it would reach no
      // assertion at all.
      const unclaimed = rules.product.selectors.filter(
        (s) => Object.values(FACETS).filter((matches) => matches(s)).length !== 1,
      );
      expect(unclaimed).toEqual([]);
    });
  });

  describe("reach", () => {
    it.each<[string, Probe]>([
      ["src/lib", "product"],
      ["src/app", "productApp"],
      ["src/components outside ui", "productComponent"],
    ])("governs %s with the full restriction set", (_label, probe) => {
      expect(new Set(rules[probe].selectors)).toEqual(new Set(rules.product.selectors));
    });

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
        // `configured`, not an empty selector list: an empty list is also what an
        // unreadable config shape would produce, and those are different facts.
        expect(rules[probe].configured).toBe(false);
        // Counterfactual — absence proves an exemption only if the file is linted
        // at all. Adding these paths to `globalIgnores` would otherwise pass here.
        expect(rules[probe].totalRules).toBeGreaterThan(0);
      },
    );
  });

  describe("effect — what the selectors actually catch", () => {
    it("catches the zone in every shape and position it is claimed to catch", () => {
      expect(effect.zoneValue).toBe(1);
      expect(effect.zoneTemplate).toBe(1);
      expect(effect.zoneJsxAttribute).toBe(1);
      expect(effect.zoneShadcn).toBe(1);
    });

    it("tells the author what to import instead", () => {
      // Identifier-coupled, not prose-coupled: the message may be reworded freely,
      // but a message that no longer names the constant cannot do its one job.
      expect(zoneMessages).toHaveLength(1);
      expect(zoneMessages[0]).toContain("SWEDISH_TIME_ZONE");
    });

    it("is silent on a substring and on a comment — the single normaliser", () => {
      expect(effect.zoneSubstring).toBe(0);
      expect(effect.zoneComment).toBe(0);
    });

    it.each([
      ["the global next-intl pin", "zoneIntlPin"],
      ["the declaration file", "zoneDeclaration"],
      ["the test harness", "zoneHarness"],
      ["a .spec file", "zoneSpec"],
    ])("is silent in %s — the exemption is load-bearing", (_label, key) => {
      expect(effect[key]).toBe(0);
    });

    it.each([
      ["an em-dash in JSX text", "copyEmDashInJsx"],
      ["a literal ellipsis", "copyEllipsis"],
      ["a default Tailwind size class", "typographyDefaultScale"],
      ["an inline style colour", "typographyInlineStyle"],
      ["text-muted-foreground in product code", "mutedInProduct"],
      ["a specifier export from a use-server module", "serverActionSpecifier"],
    ])("catches %s — every group, not only the one this PR added", (_label, key) => {
      expect(effect[key]).toBe(1);
    });

    it("lets shadcn primitives keep text-muted-foreground", () => {
      // The MUTED subtraction, measured by effect rather than by set arithmetic.
      expect(effect.mutedInShadcn).toBe(0);
    });
  });
});

// ── The `no-console` block ──────────────────────────────────────────────────
// Its own resolution rather than the harness above: that one is bound to a single
// module-level RULE, and `no-console` is a different key with a different shape
// (no selector list, so MEMBERSHIP becomes "does an allow-list make it inert").
//
// Pinned on the same four axes and for the same stated reason as its neighbour —
// the gate can die on any of them and only the first is visible in a diff. It is
// the gate standing behind the SECURITY (§5) docblocks under src/lib/actions/ and
// src/components/auth/, whose promise is that a token or an address never reaches
// the stdout `jobbliggaren-logship.sh` ships offsite.
describe("eslint.config.mjs — no-console block", () => {
  const CONSOLE_RULE = "no-console";
  const CALL = "console.error('probe');";
  const EXEMPTED = "// eslint-disable-next-line no-console\nconsole.error('probe');";

  let severityOf: (file: string) => number | undefined;
  let countIn: (code: string, file: string) => Promise<number>;

  beforeAll(async () => {
    const eslint = new ESLint({ cwd: ROOT });
    const cache = new Map<string, number | undefined>();

    const probes = [
      PATHS.product,
      PATHS.productApp,
      PATHS.productComponent,
      PATHS.shadcn,
      PATHS.testFile,
      "next.config.ts",
    ];
    for (const file of probes) {
      const config: unknown = await eslint.calculateConfigForFile(path.join(ROOT, file));
      const configured =
        typeof config === "object" && config !== null
          ? (config as { rules?: Record<string, unknown> }).rules?.[CONSOLE_RULE]
          : undefined;
      cache.set(file, Array.isArray(configured) ? Number(configured[0]) : undefined);
    }

    severityOf = (file) => cache.get(file);
    countIn = async (code, file) => {
      const [result] = await eslint.lintText(code, { filePath: path.join(ROOT, file) });
      // Fail closed: a missing result would otherwise read as "no violations".
      if (!result) throw new Error(`ESLint returned no result for ${file}`);
      return result.messages.filter((m) => m.ruleId === CONSOLE_RULE).length;
    };
  }, 120_000);

  describe("severity", () => {
    it.each([
      ["a product file", PATHS.product],
      ["an app route", PATHS.productApp],
      // The main no-restricted-syntax block drops this directory; the console gate
      // carries its own ignores so it does not inherit that hole.
      ["a shadcn primitive", PATHS.shadcn],
    ])("is error, not warn, for %s", (_label, file) => {
      // `lint` is a bare eslint with no --max-warnings, so severity 1 would clear
      // both the pre-commit hook and CI while measuring nothing.
      expect(severityOf(file)).toBe(2);
    });
  });

  describe("reach", () => {
    it("does not gate test files", () => {
      expect(severityOf(PATHS.testFile)).toBeUndefined();
    });

    it("does not reach next.config.ts, which runs in the same container", () => {
      // The block's `files` is src/** only. Recorded as a measured limit rather
      // than a claim of full coverage; there is no console call there today.
      expect(severityOf("next.config.ts")).toBeUndefined();
    });
  });

  describe("effect — what the gate actually catches", () => {
    it("catches console.error, so no allow-list has made it inert", async () => {
      // Every known call site in the repo is console.error. An { allow: ["error"] }
      // would leave the rule configured and enforcing nothing.
      expect(await countIn(CALL, PATHS.product)).toBe(1);
    });

    it("catches a call inside the directory the sibling block drops", async () => {
      expect(await countIn(CALL, PATHS.shadcn)).toBe(1);
    });

    it("lets a line-local exemption through", async () => {
      expect(await countIn(EXEMPTED, PATHS.product)).toBe(0);
    });

    it("does not fire in a test file", async () => {
      expect(await countIn(CALL, PATHS.testFile)).toBe(0);
    });
  });
});
