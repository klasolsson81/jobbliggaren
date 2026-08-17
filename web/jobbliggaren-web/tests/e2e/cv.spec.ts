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

    // Positiv assertion först: enbart negationer skulle vara gröna även om sidan
    // svarade 500, eller renderade en tom vit yta.
    await expect(
      page.getByRole("heading", { name: "Sidan finns inte" }),
    ).toBeVisible();
    await expect(page.getByLabel("Namn på CV")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Skapa CV" })).toHaveCount(0);
    await expect(page).not.toHaveURL(/\/logga-in/);
  });
});

test.describe("Redigering pausad (/cv/[id]) — #1373", () => {
  // Det gamla blocket ("Detaljvy och redigering") körde WYSIWYG-redigeraren. Den finns
  // inte längre i MVP:n; det som pinnas nu är att en GISSAD URL inte når den, och att de
  // två kontroller som LÅG på sidan — radera och byt namn — inte strandade med den.
  // Testet körs som inloggad med flit: en utloggad besökare hade redirectats av
  // session-grinden och aldrig mätt pausen.
  test("en gissad /cv/[id]-URL når inte redigeringsformuläret", async ({
    page,
  }) => {
    const id = await seedResumeViaApi(
      BACKEND_URL,
      RUN_ID,
      "CV bakom grinden",
      "Bertil Berg",
    );
    await page.goto(`/cv/${id}`);

    // Positiv assertion först: enbart negationer hade varit gröna även om sidan svarade
    // 500 eller renderade en tom vit yta.
    await expect(
      page.getByRole("heading", { name: "Sidan finns inte" }),
    ).toBeVisible();
    await expect(page.getByLabel("Sammanfattning")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Spara CV" })).toHaveCount(0);
    await expect(page).not.toHaveURL(/\/logga-in/);
  });

  test("hubbens kort erbjuder ingen väg in i redigeringsvyn", async ({
    page,
  }) => {
    const id = await seedResumeViaApi(
      BACKEND_URL,
      RUN_ID,
      "CV utan redigeringslänk",
      "Frida Falk",
    );
    await page.goto("/cv");

    await expect(page.locator(`a[href="/cv/${id}"]`)).toHaveCount(0);
    // Motpol: granskningen ÄR produkten efter pivoten och måste vara nåbar från kortet.
    await expect(page.locator(`a[href="/cv/${id}/granska"]`).first()).toBeVisible();
  });
});

test.describe("CV-hantering från hubben (#1373)", () => {
  // Radering och namnbyte flyttade hit när /cv/[id] grindades. Raderingen bär GDPR-vikt:
  // den är enda kvarvarande vägen att återkalla personnummer-samtycket för ett sparat CV
  // (Art. 7(3) — återkallelse ska vara lika lätt som samtycket var att ge).
  test("kan radera CV via bekräftelsedialog på kortet", async ({ page }) => {
    await seedResumeViaApi(BACKEND_URL, RUN_ID, "CV att radera", "Doris Dahl");
    await page.goto("/cv");

    const card = page.locator(".jp-cv").filter({ hasText: "CV att radera" });
    await expect(card).toBeVisible();
    await card.getByRole("button", { name: "Radera CV" }).click();

    await expect(page.getByRole("dialog")).toBeVisible();
    await expect(page.getByText("Radera CV?")).toBeVisible();
    await page.getByRole("button", { name: "Bekräfta radering" }).click();

    await expect(
      page.locator(".jp-cv").filter({ hasText: "CV att radera" }),
    ).toHaveCount(0);
  });

  test("kan byta namn på CV från kortet", async ({ page }) => {
    await seedResumeViaApi(BACKEND_URL, RUN_ID, "Gammalt namn", "Erik Eriksson");
    await page.goto("/cv");

    const card = page.locator(".jp-cv").filter({ hasText: "Gammalt namn" });
    await card.getByRole("button", { name: "Byt namn" }).click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByLabel("Namn").fill("Nytt namn");
    await dialog.getByRole("button", { name: "Spara" }).click();

    await expect(
      page.getByRole("heading", { name: "Nytt namn" }),
    ).toBeVisible();
  });
});
