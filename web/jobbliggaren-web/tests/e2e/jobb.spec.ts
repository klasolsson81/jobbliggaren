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

    // Civic-utility (ADR 0043): väljarna heter Yrkesområde/Yrkesgrupper och Län/Kommuner.
    // Rubrikerna ensamma är en svag assertion — den verkliga intentionen är att INGA RÅA
    // SSYK-KODER exponeras för användaren. Assertera den: ett alternativ får aldrig vara
    // en naken sifferkod. (Den gamla specen asserterade namngivna kontroller; ytan är
    // omgjord, men intentionen ska inte tappas på vägen.)
    await page.getByRole("button", { name: "Yrke", exact: true }).click();
    await expect(page.getByText("Yrkesområde", { exact: true })).toBeVisible();
    await expect(page.getByText("Yrkesgrupper", { exact: true })).toBeVisible();
    const yrkeOptions = await page
      .getByRole("dialog")
      .or(page.locator(".jp-hero-popover"))
      .last()
      .getByRole("checkbox")
      .or(page.getByRole("option"))
      .allInnerTexts();
    for (const label of yrkeOptions) {
      expect(label.trim()).not.toMatch(/^\d{4}$/);
    }
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

  // #823 — ERSÄTTER det borttagna "q=1 tecken ger felmeddelande"-testet (#813 raderade det
  // dokumenterat: copyn det asserterade fanns inte längre, och BETEENDET var trasigt —
  // ett enteckens sök gav ett tekniskt felkort). Här asserteras beteendet #823 skapar.
  //
  // Semantiken speglar backendens SearchQueryParser: ett för kort sökord används inte, men
  // frågan körs vidare på sina dimensioner. Alltså: inget felkort, och inget q i URL:en.
  test("ett enteckens sökord ger vägledning — inte ett tekniskt felkort (#823)", async ({
    page,
  }) => {
    await page.goto("/jobb");
    await page.getByLabel(SEARCH_FIELD_LABEL).fill("a");
    await page.getByRole("button", { name: "Sök", exact: true }).click();

    // Vägledningen står i hjälpraden. (Notistexten finns även i komponentens aria-live-
    // region — scopa till den SYNLIGA raden, annars strict-mode.)
    await expect(
      page.locator("p.jp-hero__searchhelp--notice")
    ).toContainText(/”a” är kortare än 2 tecken och används inte/);
    // Hjälptexten står kvar — notisen läggs TILL, den ersätter inte instruktionen.
    await expect(
      page.locator("p.jp-hero__searchhelp:not(.jp-hero__searchhelp--notice)")
    ).toContainText(/Ord blir taggar i filterraden/);
    // SETTLA navigeringen först. Klicket triggar router.replace (klient-side RSC-fetch);
    // utvärderas de negativa assertions dessförinnan passerar de trivialt på första pollen,
    // medan URL:en fortfarande är /jobb och gamla resultat står kvar — dvs. de hade inte
    // kunnat falla. (code-reviewer fångade det; samma defektklass som resten av sessionen.)
    await page.waitForURL((u) => !new URL(u).searchParams.has("q"));

    // Det avgörande: backendens 400-väg nås aldrig, så teknisk-fel-kortet syns inte.
    await expect(
      page.getByText("Kunde inte ladda jobbannonser")
    ).toHaveCount(0);
  });

  // #823 — direktlänken. Klienten kan inte grinda en bokmärkt/handredigerad URL; page.tsx
  // klampar därför ett för kort q server-side (paritet med parsern). Utan den klampen
  // 400:ar backend och sidan målar teknisk-fel-kortet.
  test("direktlänk /jobb?q=a renderar träfflistan, inte ett felkort (#823)", async ({
    page,
  }) => {
    await page.goto("/jobb?q=a");
    await expect(
      page.getByText("Kunde inte ladda jobbannonser")
    ).toHaveCount(0);
    // Och heron ärver inte det förgiftade q:t — fältet är tomt, så nästa sökning
    // skickar inte med "a" igen.
    await expect(page.getByLabel(SEARCH_FIELD_LABEL)).toHaveValue("");
  });

  test("nav-länk Jobb syns i layout", async ({ page }) => {
    await page.goto("/ansokningar");
    await expect(
      page.getByRole("navigation", { name: "Huvudnavigation" }).getByRole("link", { name: "Jobb" })
    ).toBeVisible();
  });
});
