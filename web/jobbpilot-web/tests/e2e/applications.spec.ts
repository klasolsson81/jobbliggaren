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

// UX-ÄNDRINGS-MAPPNING (STOPP 3b — /ansokningar-omarbetning):
//
// 1. "Ändra status"-disclosure-knappen är BORTTAGEN. StatusEditCard är
//    persistent (ingen inline-expand — Klas: bröt flödet). Gamla selektorn
//    `button[name="Ändra status"]` ersätts av:
//      - 1-övergångsfall (Utkast→Skickad): enskild knapp "Markera som Skickad"
//      - fler-övergångsfall: radiogrupp + [Spara] (Variant A, disabled tills
//        val ≠ nuvarande status)
//    Scenariointentionen (skapa→lista→detalj→status-flöde) bevaras; bara
//    selektorerna anpassas. Ingen täckning försvagas.
//
// 2. Tom /ansokningar/ny-submit redirectar EJ längre — Jobbtitel + Företag
//    är `required` (klientvalidering). Alla skapa-scenarier fyller därför
//    dessa fält. Ett nytt scenario verifierar att tom submit stannar kvar.
//
// 3. Status-regionen exponeras via aria-labelledby="status-edit-title"
//    (rubriktext "Status") — `getByRole("region", { name: "Status" })`
//    fungerar fortsatt eftersom det tillgängliga namnet är "Status".

const NEW_TITLE = "Backend-utvecklare";
const NEW_COMPANY = "Volvo";

async function createApplication(page: import("@playwright/test").Page) {
  await page.goto("/ansokningar/ny");
  await page.getByLabel(/Jobbtitel/).fill(NEW_TITLE);
  await page.getByLabel(/Företag/).fill(NEW_COMPANY);
  await page.getByRole("button", { name: "Skapa ansökan" }).click();
  await page.waitForURL(/\/ansokningar\/[0-9a-f-]{36}/);
}

test.describe("Pipeline-vy (/ansokningar)", () => {
  test("visar pipeline-sidan med rubriken Ansökningar", async ({ page }) => {
    await page.goto("/ansokningar");
    await expect(
      page.getByRole("heading", { name: "Ansökningar" })
    ).toBeVisible();
  });

  test("visar länk till Ny ansökan", async ({ page }) => {
    await page.goto("/ansokningar");
    await expect(
      page.getByRole("link", { name: "Ny ansökan" })
    ).toBeVisible();
  });

  test("visar tom-tillstånd när inga ansökningar finns", async ({ page }) => {
    await page.goto("/ansokningar");
    await expect(page.getByText("Inga ansökningar")).toBeVisible();
  });

  test("skapad ansökan dyker upp som rad i listan", async ({ page }) => {
    await createApplication(page);
    await page.goto("/ansokningar");
    // Rad-identitet = "titel — företag" (ApplicationRow). Manuell ansökan
    // projicerar en jobAd-summary, så identiteten finns.
    await expect(
      page.getByText(`${NEW_TITLE} — ${NEW_COMPANY}`)
    ).toBeVisible();
  });
});

test.describe("Skapa ansökan (/ansokningar/ny)", () => {
  test("visar formuläret med rätt fält", async ({ page }) => {
    await page.goto("/ansokningar/ny");
    await expect(
      page.getByRole("heading", { name: "Ny ansökan" })
    ).toBeVisible();
    await expect(page.getByLabel(/Jobbtitel/)).toBeVisible();
    await expect(page.getByLabel(/Företag/)).toBeVisible();
    await expect(page.getByLabel("Personligt brev")).toBeVisible();
    await expect(
      page.getByRole("button", { name: "Skapa ansökan" })
    ).toBeVisible();
  });

  test("tom submit stannar kvar på formuläret (Jobbtitel/Företag krävs)", async ({
    page,
  }) => {
    await page.goto("/ansokningar/ny");
    await page.getByRole("button", { name: "Skapa ansökan" }).click();
    // Klientvalidering blockerar — ingen redirect till detaljvy.
    await expect(page).toHaveURL(/\/ansokningar\/ny$/);
    await expect(
      page.getByRole("heading", { name: "Ny ansökan" })
    ).toBeVisible();
  });

  test("skapar ansökan och redirectar till detaljvy", async ({ page }) => {
    await createApplication(page);
    const statusRegion = page.getByRole("region", { name: "Status" });
    await expect(statusRegion).toContainText("Utkast");
  });

  test("skapar ansökan med personligt brev", async ({ page }) => {
    await page.goto("/ansokningar/ny");
    await page.getByLabel(/Jobbtitel/).fill(NEW_TITLE);
    await page.getByLabel(/Företag/).fill(NEW_COMPANY);
    await page
      .getByLabel("Personligt brev")
      .fill("Jag söker tjänsten och är väl lämpad.");
    await page.getByRole("button", { name: "Skapa ansökan" }).click();
    await page.waitForURL(/\/ansokningar\/[0-9a-f-]{36}/);
    await expect(
      page.getByRole("region", { name: "Status" })
    ).toContainText("Utkast");
  });

  test("visar länk tillbaka till pipeline", async ({ page }) => {
    await page.goto("/ansokningar/ny");
    await expect(page.getByRole("link", { name: "Avbryt" })).toBeVisible();
  });
});

test.describe("Detaljvy (/ansokningar/[id])", () => {
  test.beforeEach(async ({ page }) => {
    await createApplication(page);
  });

  test("visar ansökningens status som Utkast i Status-regionen", async ({
    page,
  }) => {
    const statusRegion = page.getByRole("region", { name: "Status" });
    await expect(statusRegion).toContainText("Nuvarande status:");
    await expect(statusRegion).toContainText("Utkast");
  });

  test("visar enskild knapp 'Markera som Skickad' (ingen disclosure längre)", async ({
    page,
  }) => {
    // Utkast har exakt en övergång → enskild primär knapp, ingen radiogrupp.
    await expect(
      page.getByRole("button", { name: "Markera som Skickad" })
    ).toBeVisible();
    await expect(
      page.getByRole("button", { name: "Ändra status" })
    ).toHaveCount(0);
  });

  test("visar formulär för att lägga till notering", async ({ page }) => {
    await expect(
      page.getByRole("textbox", { name: "Notering" })
    ).toBeVisible();
  });

  test("kan lägga till en notering", async ({ page }) => {
    await page
      .getByRole("textbox", { name: "Notering" })
      .fill("Intressant tjänst, bra matchning.");
    await page.getByRole("button", { name: "Spara notering" }).click();
    await expect(
      page.getByText("Intressant tjänst, bra matchning.")
    ).toBeVisible();
  });
});

test.describe("Statusövergång", () => {
  test("kan övergå från Utkast till Skickad via enskild knapp", async ({
    page,
  }) => {
    await createApplication(page);

    const statusRegion = page.getByRole("region", { name: "Status" });
    await page
      .getByRole("button", { name: "Markera som Skickad" })
      .click();
    await expect(statusRegion).toContainText("Skickad");
  });

  test("destruktiv övergång (Nekad) kräver bekräftelse i dialog", async ({
    page,
  }) => {
    await createApplication(page);

    const statusRegion = page.getByRole("region", { name: "Status" });
    // Utkast → Skickad (enskild knapp)
    await page
      .getByRole("button", { name: "Markera som Skickad" })
      .click();
    await expect(statusRegion).toContainText("Skickad");

    // Skickad ger flera övergångar → radiogrupp + [Spara] (Variant A).
    await page.getByRole("radio", { name: "Nekad" }).click();
    await page.getByRole("button", { name: "Spara" }).click();

    // Destruktiv → Dialog-bekräftelse innan action.
    await expect(page.getByRole("dialog")).toBeVisible();
    await expect(
      page.getByRole("heading", { name: "Markera som Nekad?" })
    ).toBeVisible();
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "Markera som Nekad" })
      .click();
    await expect(statusRegion).toContainText("Nekad");
  });
});
