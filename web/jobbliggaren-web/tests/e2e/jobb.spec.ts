import { test, expect } from "@playwright/test";
import { loginAs, ensureConfirmedTestUser } from "./helpers/auth";

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";
const RUN_ID = Date.now();

// Sökytan är hero-sökningen (JobbHeroSearch/JobbHeroFilters): ett namngivet
// sökfält + "Sök", och Ort/Yrke/Filter-popovers. De tidigare selektorerna
// ("Sökord", "Sortering", "Återställ", en enda Filter-disclosure) speglade en
// äldre yta och hade aldrig körts i CI (#813). Scenariointentionen är bevarad:
// fältet är alltid synligt, väljarna är namn-baserade (aldrig råa SSYK-koder,
// ADR 0043), submit skriver ?q= och rensningen tar tillbaka oss till /jobb.
//
// "Sök" kräver { exact: true } — "Rensa sökfältet" substring-matchar annars
// samma namn (strict-mode-violation), samma fälla som PasswordInputs
// "Visa lösenord" i auth-specarna.
const SEARCH_FIELD_LABEL = "Sök efter yrke, arbetsgivare eller ort";

test.beforeAll(async () => {
  await ensureConfirmedTestUser(BACKEND_URL, RUN_ID);
});

test.describe("/jobb — auth-gating", () => {
  test("redirects to /logga-in when not signed in", async ({ page }) => {
    await page.goto("/jobb");
    await expect(page).toHaveURL(/\/logga-in/);
  });
});

test.describe("/jobb — auth-gated rendering", () => {
  test.beforeEach(async ({ page }) => {
    await loginAs(page, RUN_ID);
  });

  test("visar Jobb-rubriken", async ({ page }) => {
    await page.goto("/jobb");
    await expect(
      page.getByRole("heading", { name: "Sök jobb", level: 1 })
    ).toBeVisible();
  });

  test("visar sökytans alltid-synliga fält + filter-ytor", async ({ page }) => {
    await page.goto("/jobb");
    await expect(page.getByLabel(SEARCH_FIELD_LABEL)).toBeVisible();
    await expect(
      page.getByRole("button", { name: "Sök", exact: true })
    ).toBeVisible();
    for (const filter of ["Ort", "Yrke", "Filter"]) {
      await expect(
        page.getByRole("button", { name: filter, exact: true })
      ).toBeVisible();
    }
  });

  test("filter-popovern exponerar namn-baserade yrkes-/läns-väljare", async ({
    page,
  }) => {
    await page.goto("/jobb");

    // Civic-utility: väljarna heter Yrkesområde/Yrkesgrupper och Län/Kommuner
    // — ingen "SSYK-kod" exponeras för användaren.
    await page.getByRole("button", { name: "Yrke", exact: true }).click();
    await expect(page.getByText("Yrkesområde", { exact: true })).toBeVisible();
    await expect(page.getByText("Yrkesgrupper", { exact: true })).toBeVisible();
    await page.keyboard.press("Escape");

    await page.getByRole("button", { name: "Ort", exact: true }).click();
    await expect(page.getByText("Län", { exact: true })).toBeVisible();
    await expect(page.getByText("Kommuner", { exact: true })).toBeVisible();
  });

  test("submit av sökord uppdaterar URL till ?q=...", async ({ page }) => {
    await page.goto("/jobb");
    await page.getByLabel(SEARCH_FIELD_LABEL).fill("backend");
    await page.getByRole("button", { name: "Sök", exact: true }).click();
    await page.waitForURL(/\/jobb\?q=backend/);
  });

  test("Rensa sökord och filter returnerar till /jobb", async ({ page }) => {
    await page.goto("/jobb?q=backend");
    await page
      .getByRole("button", { name: "Rensa sökord och filter" })
      .click();
    await page.waitForURL(/\/jobb$/);
  });

  test("nav-länk Jobb syns i layout", async ({ page }) => {
    await page.goto("/ansokningar");
    await expect(
      page.getByRole("navigation", { name: "Huvudnavigation" }).getByRole("link", { name: "Jobb" })
    ).toBeVisible();
  });
});
