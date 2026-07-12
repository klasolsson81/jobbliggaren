import { test, expect } from "@playwright/test";
import { loginAs, ensureConfirmedTestUser } from "./helpers/auth";

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";
// Unique run ID ensures each test run starts with a fresh user (no leftover CVs).
const RUN_ID = Date.now();

test.beforeAll(async () => {
  await ensureConfirmedTestUser(BACKEND_URL, RUN_ID);
});

test.beforeEach(async ({ page }) => {
  await loginAs(page, RUN_ID);
});

test.describe("CV-lista (/cv)", () => {
  test("visar tom-tillstånd när inga CV finns", async ({ page }) => {
    await page.goto("/cv");
    await expect(page.getByRole("heading", { name: "CV" })).toBeVisible();
    await expect(page.getByText("Inga CV ännu")).toBeVisible();
    await expect(page.getByRole("link", { name: "Nytt CV" })).toBeVisible();
  });
});

test.describe("Skapa CV (/cv/ny)", () => {
  test("skapar ett CV och redirectar till detaljvy", async ({ page }) => {
    await page.goto("/cv/ny");
    await expect(page.getByRole("heading", { name: "Nytt CV" })).toBeVisible();

    await page.getByLabel("Namn på CV").fill("Mitt master-CV");
    await page.getByLabel("Fullständigt namn").fill("Anna Andersson");
    await page.getByRole("button", { name: "Skapa CV" }).click();

    await page.waitForURL(/\/cv\/[0-9a-f-]{36}/);
    await expect(
      page.getByRole("heading", { name: "Mitt master-CV" })
    ).toBeVisible();
  });
});

test.describe("Detaljvy och redigering (/cv/[id])", () => {
  test("kan fylla i sammanfattning och lägga till en erfarenhet", async ({
    page,
  }) => {
    // Skapa ett nytt CV
    await page.goto("/cv/ny");
    await page.getByLabel("Namn på CV").fill("CV för redigering");
    await page.getByLabel("Fullständigt namn").fill("Bertil Berg");
    await page.getByRole("button", { name: "Skapa CV" }).click();
    await page.waitForURL(/\/cv\/[0-9a-f-]{36}/);

    // Fyll i sammanfattning
    await page
      .getByLabel("Sammanfattning")
      .fill("Erfaren backend-utvecklare med fokus på .NET.");

    // Lägg till en erfarenhet
    await page.getByRole("button", { name: "Lägg till erfarenhet" }).click();
    await page.getByLabel("Företag").fill("Acme AB");
    await page.getByLabel("Roll").fill("Utvecklare");
    await page.getByLabel("Startdatum").first().fill("2024-01-01");

    // Spara — /cv/[id] har flera role="status"-regioner (innehållsformulärets
    // spar-status + mallbyggarens). Scopa till spar-statusen, annars strict-mode.
    await page.getByRole("button", { name: "Spara CV" }).click();
    await expect(
      page.getByRole("status").filter({ hasText: "Sparat" })
    ).toBeVisible();

    // Verifiera att data finns kvar efter omladdning
    await page.reload();
    await expect(page.getByLabel("Sammanfattning")).toHaveValue(
      "Erfaren backend-utvecklare med fokus på .NET."
    );
    await expect(page.getByLabel("Företag")).toHaveValue("Acme AB");
  });

  test("validerar att skill-år ej kan vara över 70", async ({ page }) => {
    await page.goto("/cv/ny");
    await page.getByLabel("Namn på CV").fill("CV med skill-fel");
    await page.getByLabel("Fullständigt namn").fill("Cecilia Carlsson");
    await page.getByRole("button", { name: "Skapa CV" }).click();
    await page.waitForURL(/\/cv\/[0-9a-f-]{36}/);

    await page.getByRole("button", { name: "Lägg till färdighet" }).click();
    await page.getByLabel("Namn", { exact: true }).fill("C#");
    await page.getByLabel("År (valfritt)").fill("75");
    await page.getByRole("button", { name: "Spara CV" }).click();

    await expect(page.getByText("Maxvärde för år är 70.")).toBeVisible();
  });

  test("kan radera CV via bekräftelsedialog", async ({ page }) => {
    // Skapa ett CV att radera
    await page.goto("/cv/ny");
    await page.getByLabel("Namn på CV").fill("CV att radera");
    await page.getByLabel("Fullständigt namn").fill("Doris Dahl");
    await page.getByRole("button", { name: "Skapa CV" }).click();
    await page.waitForURL(/\/cv\/[0-9a-f-]{36}/);

    await page.getByRole("button", { name: "Radera CV" }).click();
    await expect(page.getByRole("dialog")).toBeVisible();
    await expect(page.getByText("Radera CV?")).toBeVisible();
    await page.getByRole("button", { name: "Bekräfta radering" }).click();

    await page.waitForURL("**/cv");
    await expect(
      page.getByRole("link", { name: /CV att radera/ })
    ).toHaveCount(0);
  });

  test("kan byta namn på CV", async ({ page }) => {
    await page.goto("/cv/ny");
    await page.getByLabel("Namn på CV").fill("Gammalt namn");
    await page.getByLabel("Fullständigt namn").fill("Erik Eriksson");
    await page.getByRole("button", { name: "Skapa CV" }).click();
    await page.waitForURL(/\/cv\/[0-9a-f-]{36}/);

    await page.getByRole("button", { name: "Byt namn" }).click();
    await expect(page.getByRole("dialog")).toBeVisible();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("Namn").fill("Nytt namn");
    await dialog.getByRole("button", { name: "Spara" }).click();

    await expect(
      page.getByRole("heading", { name: "Nytt namn" })
    ).toBeVisible();
  });
});
