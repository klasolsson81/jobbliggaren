import { test, expect } from "@playwright/test";
import { loginAs, ensureTestUser } from "./helpers/auth";

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";
// Unique run ID ensures each test run starts with a fresh user (no leftover applications).
const RUN_ID = Date.now();

test.beforeAll(async () => {
  await ensureTestUser(BACKEND_URL, RUN_ID);
});

test.beforeEach(async ({ page }) => {
  await loginAs(page, RUN_ID);
});

test.describe("Pipeline-vy (/ansokningar)", () => {
  test("visar pipeline-sidan med rubriken Ansökningar", async ({ page }) => {
    await page.goto("/ansokningar");
    await expect(page.getByRole("heading", { name: "Ansökningar" })).toBeVisible();
  });

  test("visar länk till Ny ansökan", async ({ page }) => {
    await page.goto("/ansokningar");
    await expect(page.getByRole("link", { name: "Ny ansökan" })).toBeVisible();
  });

  test("visar tom-tillstånd när inga ansökningar finns", async ({ page }) => {
    await page.goto("/ansokningar");
    await expect(page.getByText("Inga ansökningar")).toBeVisible();
  });
});

test.describe("Skapa ansökan (/ansokningar/ny)", () => {
  test("visar formuläret med rätt fält", async ({ page }) => {
    await page.goto("/ansokningar/ny");
    await expect(page.getByRole("heading", { name: "Ny ansökan" })).toBeVisible();
    await expect(page.getByLabel("Personligt brev")).toBeVisible();
    await expect(page.getByRole("button", { name: "Skapa ansökan" })).toBeVisible();
  });

  test("skapar ansökan utan personligt brev och redirectar till detaljvy", async ({ page }) => {
    await page.goto("/ansokningar/ny");
    await page.getByRole("button", { name: "Skapa ansökan" }).click();
    await page.waitForURL(/\/ansokningar\/[0-9a-f-]{36}/);
    await expect(page.getByRole("status")).toContainText("Utkast");
  });

  test("skapar ansökan med personligt brev", async ({ page }) => {
    await page.goto("/ansokningar/ny");
    await page.getByLabel("Personligt brev").fill("Jag söker tjänsten och är väl lämpad.");
    await page.getByRole("button", { name: "Skapa ansökan" }).click();
    await page.waitForURL(/\/ansokningar\/[0-9a-f-]{36}/);
    await expect(page.getByRole("status")).toContainText("Utkast");
  });

  test("visar länk tillbaka till pipeline", async ({ page }) => {
    await page.goto("/ansokningar/ny");
    await expect(page.getByRole("link", { name: "Avbryt" })).toBeVisible();
  });
});

test.describe("Detaljvy (/ansokningar/[id])", () => {
  let applicationId: string;

  test.beforeEach(async ({ page }) => {
    await page.goto("/ansokningar/ny");
    await page.getByRole("button", { name: "Skapa ansökan" }).click();
    await page.waitForURL(/\/ansokningar\/([0-9a-f-]{36})/);
    const match = page.url().match(/\/ansokningar\/([0-9a-f-]{36})/);
    applicationId = match?.[1] ?? "";
  });

  test("visar ansökningens status som Utkast", async ({ page }) => {
    await expect(page.getByRole("status")).toContainText("Utkast");
  });

  test("visar knapp för övergång till Skickad", async ({ page }) => {
    await expect(page.getByRole("button", { name: "Skickad" })).toBeVisible();
  });

  test("visar formulär för att lägga till notering", async ({ page }) => {
    await expect(page.getByRole("textbox", { name: "Notering" })).toBeVisible();
  });

  test("kan lägga till en notering", async ({ page }) => {
    await page.getByRole("textbox", { name: "Notering" }).fill("Intressant tjänst, bra matchning.");
    await page.getByRole("button", { name: "Spara notering" }).click();
    await expect(page.getByText("Intressant tjänst, bra matchning.")).toBeVisible();
  });
});

test.describe("Statusövergång", () => {
  test("kan övergå från Utkast till Skickad", async ({ page }) => {
    await page.goto("/ansokningar/ny");
    await page.getByRole("button", { name: "Skapa ansökan" }).click();
    await page.waitForURL(/\/ansokningar\/[0-9a-f-]{36}/);

    await page.getByRole("button", { name: "Skickad" }).click();
    await expect(page.getByRole("status")).toContainText("Skickad");
  });

  test("destructive transition (Nekad) kräver bekräftelse i dialog", async ({ page }) => {
    await page.goto("/ansokningar/ny");
    await page.getByRole("button", { name: "Skapa ansökan" }).click();
    await page.waitForURL(/\/ansokningar\/[0-9a-f-]{36}/);

    await page.getByRole("button", { name: "Skickad" }).click();
    await expect(page.getByRole("status")).toContainText("Skickad");

    await page.getByRole("button", { name: "Nekad" }).click();
    await expect(page.getByRole("dialog")).toBeVisible();
    await expect(page.getByText("Är du säker")).toBeVisible();
    await page.getByRole("button", { name: "Bekräfta" }).click();
    await expect(page.getByRole("status")).toContainText("Nekad");
  });
});
