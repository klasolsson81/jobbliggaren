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

// The load-cycle region, distinguished from the hero search's own filter region by `aria-atomic`:
// the announcer swaps whole sentences and sets it, the hero one does not. Both are `sr-only`
// `role="status"` and would otherwise resolve to two elements — the exact strict-mode collision
// that broke `foretag-sok-live-commit.spec.ts` when #1092 added the second region there.
const LOAD_REGION = "p[aria-live='polite'].sr-only[aria-atomic='true']";
const FILTER_REGION = "p[aria-live='polite'].sr-only:not([aria-atomic])";

test.beforeAll(async () => {
  await ensureConfirmedTestUser(BACKEND_URL, RUN_ID);
});

test.describe("/jobb — the load cycle announces through a region that precedes it", () => {
  test.beforeEach(async ({ page }) => {
    await loginAs(page, RUN_ID);
  });

  test("exactly two regions, with distinct jobs", async ({ page }) => {
    await page.goto("/jobb");

    // Two regions with two writers is the house rule; one region with two would let a filter
    // change overwrite a result count mid-load.
    await expect(page.locator(LOAD_REGION)).toHaveCount(1);
    // Positive control on the split: if the announcer ever loses `aria-atomic`, the locator above
    // starts matching the hero region instead and would pass for the wrong element.
    await expect(page.locator(FILTER_REGION)).toHaveCount(1);
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
