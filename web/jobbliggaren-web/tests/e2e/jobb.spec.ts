import { test, expect } from "@playwright/test";
import { loginAs, ensureConfirmedTestUser } from "./helpers/auth";

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";
const RUN_ID = Date.now();

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
    await expect(page.getByRole("heading", { name: "Jobb" })).toBeVisible();
  });

  test("visar sök-ytans alltid-synliga fält + Filter-disclosure", async ({
    page,
  }) => {
    await page.goto("/jobb");
    // ADR 0043 — sökord + sortering är alltid synliga; yrkes-/läns-väljarna
    // (namn-pickers, ej råa SSYK-koder) ligger bakom Filter-disclosuren.
    await expect(page.getByLabel("Sökord")).toBeVisible();
    await expect(page.getByLabel("Sortering")).toBeVisible();
    await expect(
      page.getByRole("button", { name: /^Filter/ })
    ).toBeVisible();
    await expect(page.getByRole("button", { name: "Sök" })).toBeVisible();
    await expect(
      page.getByRole("button", { name: "Återställ" })
    ).toBeVisible();
  });

  test("Filter-disclosure exponerar namn-baserade yrkes-/läns-väljare", async ({
    page,
  }) => {
    await page.goto("/jobb");
    await page.getByRole("button", { name: /^Filter/ }).click();
    // Civic-utility: väljarna heter Yrkesområde/Yrke/Län — ingen "SSYK-kod".
    await expect(page.getByLabel("Yrkesområde")).toBeVisible();
    await expect(page.getByLabel("Yrke")).toBeVisible();
    await expect(page.getByLabel("Län")).toBeVisible();
  });

  test("submit av sökord uppdaterar URL till ?q=...", async ({ page }) => {
    await page.goto("/jobb");
    await page.getByLabel("Sökord").fill("backend");
    await page.getByRole("button", { name: "Sök" }).click();
    await page.waitForURL(/\/jobb\?q=backend/);
  });

  test("validation: q=1 tecken ger felmeddelande och blockerar submit", async ({
    page,
  }) => {
    await page.goto("/jobb");
    await page.getByLabel("Sökord").fill("a");
    await page.getByRole("button", { name: "Sök" }).click();
    await expect(page.getByRole("alert")).toContainText(
      /Söktexten måste vara 2–100 tecken/
    );
    // URL ska INTE ha ändrats
    await expect(page).toHaveURL(/\/jobb$/);
  });

  test("Återställ rensar filter och returnerar till /jobb", async ({ page }) => {
    await page.goto("/jobb?q=backend");
    await page.getByRole("button", { name: "Återställ" }).click();
    await page.waitForURL(/\/jobb$/);
  });

  test("nav-länk Jobb syns i layout", async ({ page }) => {
    await page.goto("/ansokningar");
    await expect(
      page.getByRole("navigation", { name: "Huvudnavigation" }).getByRole("link", { name: "Jobb" })
    ).toBeVisible();
  });
});
