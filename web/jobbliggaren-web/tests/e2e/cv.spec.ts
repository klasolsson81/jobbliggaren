import { test, expect } from "@playwright/test";
import {
  loginAs,
  ensureConfirmedTestUser,
  seedResumeViaApi,
} from "./helpers/auth";

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
  test("visar tom-tillstånd när inga CV finns, med import som enda ingång", async ({
    page,
  }) => {
    await page.goto("/cv");
    await expect(page.getByRole("heading", { name: "CV" })).toBeVisible();
    await expect(page.getByText("Inga CV ännu")).toBeVisible();

    // #1061: skapa-från-grunden är deferrad. Hubben får inte erbjuda den i vare sig
    // plattan eller tomt-tillståndet, och import är kvar som enda ingång.
    await expect(page.getByRole("link", { name: "Nytt CV" })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Skapa första CV" })).toHaveCount(0);
    await expect(page.locator('a[href="/cv/ny"]')).toHaveCount(0);
    await expect(
      page.getByRole("link", { name: "Importera CV" }).first(),
    ).toBeVisible();
  });
});

test.describe("Skapa CV (/cv/ny) — deferrad (#1061)", () => {
  // Det gamla blocket körde hela skapa-flödet. Flödet finns inte längre; det som pinnas nu
  // är att en GISSAD URL inte når det. Testet körs som inloggad med flit — en utloggad
  // besökare hade redirectats av session-grinden och aldrig mätt deferralen.
  test("en gissad /cv/ny-URL når inte skapa-formuläret", async ({ page }) => {
    await page.goto("/cv/ny");

    await expect(page.getByLabel("Namn på CV")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Skapa CV" })).toHaveCount(0);
    await expect(page).not.toHaveURL(/\/logga-in/);
  });
});

test.describe("Detaljvy och redigering (/cv/[id])", () => {
  // Seedas via API sedan #1061 stängde /cv/ny. Sidan själv är oförändrad och fortfarande
  // live — det är bara vägen till precondition som flyttat.
  test("kan fylla i sammanfattning och lägga till en erfarenhet", async ({
    page,
  }) => {
    const id = await seedResumeViaApi(
      BACKEND_URL,
      RUN_ID,
      "CV för redigering",
      "Bertil Berg",
    );
    await page.goto(`/cv/${id}`);

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
    const id = await seedResumeViaApi(
      BACKEND_URL,
      RUN_ID,
      "CV med skill-fel",
      "Cecilia Carlsson",
    );
    await page.goto(`/cv/${id}`);

    await page.getByRole("button", { name: "Lägg till färdighet" }).click();
    await page.getByLabel("Namn", { exact: true }).fill("C#");
    await page.getByLabel("År (valfritt)").fill("75");
    await page.getByRole("button", { name: "Spara CV" }).click();

    await expect(page.getByText("Maxvärde för år är 70.")).toBeVisible();
  });

  test("kan radera CV via bekräftelsedialog", async ({ page }) => {
    const id = await seedResumeViaApi(
      BACKEND_URL,
      RUN_ID,
      "CV att radera",
      "Doris Dahl",
    );
    await page.goto(`/cv/${id}`);

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
    const id = await seedResumeViaApi(
      BACKEND_URL,
      RUN_ID,
      "Gammalt namn",
      "Erik Eriksson",
    );
    await page.goto(`/cv/${id}`);

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
