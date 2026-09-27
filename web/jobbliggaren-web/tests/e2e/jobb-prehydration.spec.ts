import { expect } from "@playwright/test";
import { loggedInTest } from "./helpers/session";

const test = loggedInTest(Date.now());

// #1787 — the field is on screen and editable before React attaches to it. These cases decide
// when hydration happens instead of racing it: the app's script chunks are either refused (no
// JavaScript at all) or held back until the term has been typed. Scripts only: the stylesheets
// live in the same directory, and a held stylesheet blocks the parser before the field renders.
const CHUNKS = /\/_next\/static\/chunks\/[^?]*\.js(\?|$)/;
const SEARCH_FIELD_LABEL = "Sök efter yrke, arbetsgivare eller ort";

test.describe("/jobb — a term typed before hydration (#1787)", () => {
  test("without JavaScript, Sök submits the typed term as q", async ({ page }) => {
    let refused = 0;
    await page.route(CHUNKS, (route) => {
      refused += 1;
      return route.abort();
    });
    await page.goto("/jobb");
    await page.getByLabel(SEARCH_FIELD_LABEL).fill("backend");
    await expect.poll(() => refused).toBeGreaterThan(0);
    await page.getByRole("button", { name: "Sök", exact: true }).click();
    await page.waitForURL(/\/jobb\?q=backend(&|$)/);
  });

  test("a term typed while hydration is held back survives it", async ({ page }) => {
    let allowHydration = () => {};
    const hydrationAllowed = new Promise<void>((resolve) => {
      allowHydration = resolve;
    });
    let held = 0;
    await page.route(CHUNKS, async (route) => {
      held += 1;
      await hydrationAllowed;
      await route.continue();
    });
    await page.goto("/jobb", { waitUntil: "domcontentloaded" });
    const field = page.getByLabel(SEARCH_FIELD_LABEL);
    await field.fill("backend");
    await expect.poll(() => held).toBeGreaterThan(0);

    allowHydration();
    await expect(page.getByRole("combobox", { name: SEARCH_FIELD_LABEL })).toBeVisible();
    await expect(field).not.toHaveAttribute("name", "q");
    await expect(field).toHaveValue("backend");

    await page.getByRole("button", { name: "Sök", exact: true }).click();
    await page.waitForURL(/\/jobb\?q=backend/);
  });
});
