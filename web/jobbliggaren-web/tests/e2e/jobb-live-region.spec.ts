import { test, expect } from "@playwright/test";
import { loginAs, ensureConfirmedTestUser } from "./helpers/auth";

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";
const RUN_ID = Date.now();

/**
 * #1505 — `/jobb`'s load-cycle live region, measured where unit tests cannot reach.
 *
 * The regression this exists for is PLACEMENT: a region mounted INSIDE the `<Suspense>` boundary
 * is re-created on every search, and a region an assistive technology has not registered yet
 * announces nothing. jsdom cannot see it — `<Suspense>` emits no DOM nodes of its own when it does
 * not suspend, so the rendered output is byte-identical either way and every unit assertion passes
 * on the defective placement. That mutation was built and measured surviving `page.test.tsx`
 * deliberately, which is why this file exists rather than another unit pin.
 *
 * NODE IDENTITY across a search is therefore the load-bearing assertion here, and it is the one
 * thing only a real browser can answer.
 */

const SEARCH_FIELD_LABEL = "Sök efter yrke, arbetsgivare eller ort";

// The load-cycle region, located STRUCTURALLY rather than by counting. A hydrated `/jobb` carries
// four polite regions, not two: this one, the hero search's tag announcement
// (`jobb-hero-search.tsx:668`), the typeahead's suggestion count (`job-ad-typeahead.tsx:287`,
// mounted behind `hydrated`) and the shell's header stats (`header-stats.tsx:219`, a `<span>`).
// An earlier version of this file asserted `toHaveCount(1)` on the non-atomic selector and PASSED
// in CI — by racing hydration and measuring the pre-hydration DOM, where the typeahead is not yet
// mounted. A green test measuring the wrong state, on a `continue-on-error` lane. Three reviewers
// found it independently.
//
// The scoping below cannot race: `section[aria-labelledby="jobb-results-title"]` is server-rendered
// and no hero region can ever be its descendant.
const RESULTS_SECTION = "section[aria-labelledby='jobb-results-title']";
const LOAD_REGION = `${RESULTS_SECTION} p[aria-live='polite'].sr-only[aria-atomic='true']`;

test.beforeAll(async () => {
  await ensureConfirmedTestUser(BACKEND_URL, RUN_ID);
});

test.describe("/jobb — the load cycle announces through a region that precedes it", () => {
  test.beforeEach(async ({ page }) => {
    await loginAs(page, RUN_ID);
  });

  test("the results section owns exactly one region, and the hero regions stay outside it", async ({
    page,
  }) => {
    await page.goto("/jobb");
    // Wait for hydration before counting anything: the typeahead's region only exists after it,
    // and counting before it is what made the previous version of this test pass while wrong.
    await expect(page.getByLabel(SEARCH_FIELD_LABEL)).toBeEnabled();

    // One region per job is the house rule; one region with two writers would let a filter change
    // overwrite a result count mid-load.
    await expect(page.locator(LOAD_REGION)).toHaveCount(1);
    // Positive control on the scoping: the hero's own polite regions exist and are NOT inside the
    // results section. Asserted as "at least one" — the exact number is a property of the hero,
    // not of this fix, and pinning it here would break on an unrelated hero change.
    const heroRegions = page.locator(
      `p[aria-live='polite'].sr-only:not(${RESULTS_SECTION} *)`,
    );
    expect(await heroRegions.count()).toBeGreaterThan(0);
  });

  test("the region survives a search as the SAME node, and carries the outcome", async ({
    page,
  }) => {
    await page.goto("/jobb");
    const region = page.locator(LOAD_REGION);

    // The load has settled by the time the toolbar count is painted; the region holds the sentence
    // that closed it, so it is non-empty here rather than at first paint.
    await expect(page.locator(".jp-results-count")).toBeVisible();
    await expect(region).not.toHaveText("");
    const before = await region.elementHandle();

    await page.getByLabel(SEARCH_FIELD_LABEL).fill("utvecklare");
    await page.getByRole("button", { name: "Sök", exact: true }).click();
    await expect(page).toHaveURL(/[?&]q=utvecklare/);
    await expect(page.locator(".jp-results-count")).toBeVisible();

    const after = await region.elementHandle();
    // THE assertion. With the region inside the boundary, the search swaps the subtree and this
    // is a different element — which is the defect, and which every jsdom assertion misses.
    expect(
      await page.evaluate(
        ([a, b]) => a === b,
        [before, after] as const,
      ),
    ).toBe(true);

    await before?.dispose();
    await after?.dispose();
  });

  test("the visible count is not itself a live region", async ({ page }) => {
    await page.goto("/jobb");
    const count = page.locator(".jp-results-count");
    await expect(count).toBeVisible();

    // The regression: restoring `role="status"` here re-creates the born-holding-its-text shape
    // AND doubles the announcement now that the surface region carries the same sentence.
    await expect(count).not.toHaveAttribute("role", "status");
    await expect(count).not.toHaveAttribute("aria-live", /.*/);
  });
});
