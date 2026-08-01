import path from "node:path";
import { ESLint } from "eslint";
import { describe, expect, it } from "vitest";

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
 */

// vitest runs with cwd at the package root, where eslint.config.mjs lives.
const ROOT = process.cwd();
const eslint = new ESLint({ cwd: ROOT });

type Restriction = { selector: string };

function isRestriction(value: unknown): value is Restriction {
  return (
    typeof value === "object" &&
    value !== null &&
    typeof (value as { selector?: unknown }).selector === "string"
  );
}

/** The selectors `no-restricted-syntax` is configured with for `file`. */
async function selectorsFor(file: string): Promise<string[]> {
  const config = await eslint.calculateConfigForFile(path.join(ROOT, file));
  const entry = config.rules?.["no-restricted-syntax"];
  if (!Array.isArray(entry)) return [];
  // entry[0] is the severity; the rest are the restriction objects.
  return entry.slice(1).filter(isRestriction).map((r) => r.selector);
}

const isZone = (s: string) => s.includes("Europe/Stockholm");
const isMuted = (s: string) => s.includes("text-muted-foreground");

describe("eslint.config.mjs — no-restricted-syntax block relations", () => {
  it("gives a product file every group, zone and muted included", async () => {
    const product = await selectorsFor("src/lib/i18n/format.ts");

    expect(product.length).toBeGreaterThan(0);
    expect(product.some(isZone)).toBe(true);
    expect(product.some(isMuted)).toBe(true);
  });

  it("gives shadcn primitives the product set MINUS muted, zone kept", async () => {
    const product = await selectorsFor("src/lib/i18n/format.ts");
    const ui = await selectorsFor("src/components/ui/status-pill.tsx");

    expect(new Set(ui)).toEqual(new Set(product.filter((s) => !isMuted(s))));
    expect(ui.some(isZone)).toBe(true);
  });

  it.each([
    ["the zone declaration itself", "src/lib/time/swedish-calendar.ts"],
    ["the global next-intl pin", "src/i18n/request.ts"],
    ["the test harness directory", "src/test/render-intl.tsx"],
    ["a nested file in the harness directory", "src/test/deep/nested/helper.ts"],
  ])("gives %s the product set MINUS zone", async (_label, file) => {
    const product = await selectorsFor("src/lib/i18n/format.ts");
    const exempt = await selectorsFor(file);

    expect(new Set(exempt)).toEqual(new Set(product.filter((s) => !isZone(s))));
  });

  it.each([
    ["a .test file", "src/lib/time/swedish-calendar.test.ts"],
    ["a .spec file", "src/lib/time/swedish-calendar.spec.ts"],
    ["a .spec file among the shadcn primitives", "src/components/ui/status-pill.spec.tsx"],
    ["a .test file inside the harness directory", "src/test/render-intl.test.tsx"],
  ])(
    "exempts %s entirely — test-hood follows vitest's {test,spec}, not just .test",
    async (_label, file) => {
      expect(await selectorsFor(file)).toEqual([]);
    },
  );
});
