import path from "node:path";
import { ESLint } from "eslint";
import { beforeAll, describe, expect, it } from "vitest";

/**
 * The `no-restricted-syntax` blocks in `eslint.config.mjs` are a BLOCKING gate,
 * and this pins the relations between them — for the same reason `scripts/`'s CSS
 * guard is unit-tested (`vitest.config.ts`): a wrong answer here fails an innocent
 * commit and tells its author to do the wrong thing.
 *
 * It asserts through ESLint's own config resolution rather than by reading the
 * config object, so the GLOBS are what is pinned. That is where the defect was:
 * the blocks ignored `*.test.*` while `vitest.config.ts` collects `{test,spec}`,
 * so a `.spec` file was linted as product code — and the zone rule's message would
 * have told its author to import the constant, which is exactly the change that
 * blinds a test as an oracle.
 *
 * Relations, not counts. Two blocks here carry the same NUMBER of selectors with
 * different contents, so a count-based check would pass on a swap.
 *
 * Every path below is resolved ONCE, in `beforeAll`, and the assertions are then
 * pure comparisons. The hook carries its own ceiling because what it waits for is
 * a one-time flat-config load (`eslint-config-next` plus typescript-eslint), not
 * anything this file computes: measured at ~3s in isolation and 22s inside a fully
 * loaded worker pool, which the default 5s per-test timeout turned into a flake
 * the first run of the full suite caught.
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

type Restriction = { selector: string };

function isRestriction(value: unknown): value is Restriction {
  return (
    typeof value === "object" &&
    value !== null &&
    typeof (value as { selector?: unknown }).selector === "string"
  );
}

let selectors: Record<Probe, string[]>;

beforeAll(async () => {
  const eslint = new ESLint({ cwd: ROOT });

  const resolved = await Promise.all(
    (Object.entries(PATHS) as [Probe, string][]).map(async ([key, file]) => {
      const config = await eslint.calculateConfigForFile(path.join(ROOT, file));
      const entry = config.rules?.["no-restricted-syntax"];
      // entry[0] is the severity; the rest are the restriction objects.
      const list = Array.isArray(entry) ? entry.slice(1).filter(isRestriction) : [];
      return [key, list.map((r) => r.selector)] as const;
    }),
  );

  selectors = Object.fromEntries(resolved) as Record<Probe, string[]>;
}, 120_000);

const isZone = (s: string) => s.includes("Europe/Stockholm");
const isMuted = (s: string) => s.includes("text-muted-foreground");

describe("eslint.config.mjs — no-restricted-syntax block relations", () => {
  it("gives a product file every group, zone and muted included", () => {
    expect(selectors.product.length).toBeGreaterThan(0);
    expect(selectors.product.some(isZone)).toBe(true);
    expect(selectors.product.some(isMuted)).toBe(true);
  });

  it("gives shadcn primitives the product set MINUS muted, zone kept", () => {
    expect(new Set(selectors.shadcn)).toEqual(
      new Set(selectors.product.filter((s) => !isMuted(s))),
    );
    expect(selectors.shadcn.some(isZone)).toBe(true);
  });

  it.each<[string, Probe]>([
    ["the zone declaration itself", "declaration"],
    ["the global next-intl pin", "intlPin"],
    ["the test harness directory", "harness"],
    ["a nested file in the harness directory", "harnessNested"],
  ])("gives %s the product set MINUS zone", (_label, probe) => {
    expect(new Set(selectors[probe])).toEqual(
      new Set(selectors.product.filter((s) => !isZone(s))),
    );
  });

  it.each<[string, Probe]>([
    ["a .test file", "testFile"],
    ["a .spec file", "specFile"],
    ["a .spec file among the shadcn primitives", "shadcnSpec"],
    ["a .test file inside the harness directory", "harnessTest"],
  ])(
    "exempts %s entirely — test-hood follows vitest's {test,spec}, not just .test",
    (_label, probe) => {
      expect(selectors[probe]).toEqual([]);
    },
  );
});
