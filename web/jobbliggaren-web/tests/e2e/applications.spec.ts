import { test, expect } from "@playwright/test";
import { loginAs, ensureConfirmedTestUser } from "./helpers/auth";

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";
// Unique run ID ensures each test run starts with a fresh user (no leftover applications).
const RUN_ID = Date.now();

test.beforeAll(async () => {
  await ensureConfirmedTestUser(BACKEND_URL, RUN_ID);
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
  await page.goto("/ny-ansokan");
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
    // Egen unik titel: NEW_TITLE återanvänds av flera tester mot SAMMA användare, så ett
    // filter på den hade blivit en strict-mode-violation så fort ett test ovanför skapade
    // en till. Att scopa till kortet räcker inte — identiteten måste vara unik.
    const listTitle = "Listrad-prov 813";
    await page.goto("/ny-ansokan");
    await page.getByLabel(/Jobbtitel/).fill(listTitle);
    await page.getByLabel(/Företag/).fill(NEW_COMPANY);
    await page.getByRole("button", { name: "Skapa ansökan" }).click();
    await page.waitForURL(/\/ansokningar\/[0-9a-f-]{36}/);
    await page.goto("/ansokningar");
    // Statusgrupperna är hopfällda som default ("Utkast (1) — Klicka för att visa").
    // Disclosure-knappen är den enda med aria-expanded; steg-chippen i pipelinen
    // ("1 UTKAST") matchar också namnet men är ingen disclosure.
    await page
      .getByRole("button", { name: /Utkast/, expanded: false })
      .click();
    // Rad-identitet är titeln (länken); företaget är ett EGET fält i kortet sedan Lista
    // antog Tabellens fältordning (#780/#787) — det ligger alltså utanför länken. Scopa
    // till KORTET (<article>) som bär båda: ett globalt getByText(företag).first() hade
    // kunnat matcha en annan testers rad och gjort testet ordningsberoende.
    const card = page
      .getByRole("article")
      .filter({ has: page.getByRole("link", { name: new RegExp(listTitle) }) });
    await expect(card).toBeVisible();
    await expect(card).toContainText(NEW_COMPANY);
  });
});

test.describe("Skapa ansökan (/ny-ansokan)", () => {
  test("visar formuläret med rätt fält", async ({ page }) => {
    await page.goto("/ny-ansokan");
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
    await page.goto("/ny-ansokan");
    await page.getByRole("button", { name: "Skapa ansökan" }).click();
    // Klientvalidering blockerar — ingen redirect till detaljvy.
    await expect(page).toHaveURL(/\/ny-ansokan$/);
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
    await page.goto("/ny-ansokan");
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
    await page.goto("/ny-ansokan");
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

  // Noteringsfältet ligger bakom "+ Lägg till anteckning"-disclosuren i
  // Anteckningar-sektionen (#805/#818) — det är inte längre ett alltid-synligt
  // formulär. Scenariointentionen (skriv notering → spara → syns) är oförändrad.
  test("visar formulär för att lägga till notering", async ({ page }) => {
    await page.getByRole("button", { name: "Lägg till anteckning" }).click();
    await expect(
      page.getByRole("textbox", { name: "Notering" })
    ).toBeVisible();
  });

  test("kan lägga till en notering", async ({ page }) => {
    await page.getByRole("button", { name: "Lägg till anteckning" }).click();
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

  // #565 — OCKLUSIONS-REPRON, OMPEKAD (#813). Invarianten är oförändrad: en dialog som
  // öppnas INIFRÅN radklicks-modalen måste ligga ovanpå scrimen och vara KLICKBAR.
  //
  // Den gamla klickvägen finns inte kvar (statusbytet i modalen är numera direktbyte med
  // ångra-toast, ingen bekräftelsedialog), men den ockluderande ytan gör det: den
  // intercepting-routen lever (`app/(app)/@modal/(.)ansokningar/[id]`), `.jp-modal-scrim`
  // ligger på z-80, och dialoger öppnas fortfarande inifrån modalkroppen — bl.a. den här
  // (Logga uppföljning) och anteckningarnas hjälpdialog. Alltså två överlägg, alltså
  // exakt #565:s buggklass, alltså regressionsskydd som måste finnas kvar. (Ett tidigare
  // utkast av #813 raderade specen på premissen att routen var borta — den premissen var
  // fel: sökningen tittade under `ansokningar/` och kunde per konstruktion aldrig hitta
  // `@modal`, som är en parallell syskon-route. code-reviewer fångade det.)
  //
  // jsdom-guarden (dialog.zindex.test.tsx) räcker INTE som ersättning och säger det
  // själv: den saknar paint-/stacking-modell, "which is exactly why the bug shipped
  // green". Bara ett äkta hit-test i en riktig webbläsare fångar ocklusionen.
  test("radklick-modal: dialog öppnad inifrån modalen syns ovanpå scrimen och är klickbar (#565)", async ({
    page,
  }) => {
    // Egen unik titel → entydig rad (undviker strict-mode-krock med andra testers rader).
    const probeTitle = "Ocklusionsprov 565";
    await page.goto("/ny-ansokan");
    await page.getByLabel(/Jobbtitel/).fill(probeTitle);
    await page.getByLabel(/Företag/).fill(NEW_COMPANY);
    await page.getByRole("button", { name: "Skapa ansökan" }).click();
    await page.waitForURL(/\/ansokningar\/[0-9a-f-]{36}/);

    // Öppna detaljen via SOFT-NAV radklick → intercepting-route-modal (scrim).
    // Modalen är en intercepting route: den triggas BARA på klient-navigering. Klickar vi
    // raden innan sidan hydrerat beter sig <Link> som en vanlig <a> → hård navigering →
    // helsidan, ingen scrim, och hela ocklusions-testet blir vakuöst. Vänta ut hydreringen.
    await page.goto("/ansokningar");
    await page.waitForLoadState("networkidle");
    await page
      .getByRole("button", { name: /Utkast/, expanded: false })
      .click();
    await page.getByRole("link", { name: new RegExp(probeTitle) }).click();

    // Bekräfta att vi faktiskt står i modalen — annars vore hit-testet nedan vakuöst
    // (helsidan har ingen scrim och kan därför inte ockludera någonting).
    await expect(page.locator(".jp-modal-scrim")).toBeVisible();

    // Öppna uppföljnings-dialogen INIFRÅN modalen. Den portaleras till <body> — SYSKON
    // till modalpanelen, inte barn. Shellen har också role="dialog"; disambiguera på titeln.
    // exact: "+ Lägg till" är prefix till "+ Lägg till anteckning" (strict-mode-krock).
    await page
      .getByRole("button", { name: "+ Lägg till", exact: true })
      .click();
    const followUpDialog = page
      .getByRole("dialog")
      .filter({ hasText: "Logga uppföljning" });
    await expect(followUpDialog).toBeVisible();

    // OCKLUSIONS-GUARDEN — mät ocklusion, inte tal.
    //
    // Den gamla specen påstod att ett äkta klick ÄR hit-testet ("ockluderas dialogen fångar
    // panelen klicket och Playwright kastar"). Det är FALSKT här, och specen hade aldrig
    // körts så ingen upptäckte det: Radix DismissableLayer sätter `pointer-events: none` på
    // <body> för en MODAL dialog (`modal` default true), och `.jp-modal-scrim` deklarerar
    // ingen egen pointer-events → den ÄRVER `none`. Scrimen kan alltså inte fånga klicket
    // oavsett vad den målar över. Bevisat med mutation: regressera dialogen till shadcn-
    // defaulten z-50, så den målas UNDER scrimens z-80, och klick-testet går ändå grönt.
    // (Villkorat: med `modal={false}`, eller om scrimen får egen `pointer-events: auto`,
    // återvänder interceptionen. Proben nedan är oberoende av allt detta.)
    //
    // Så vi frågar webbläsaren vad som FAKTISKT ligger överst i dialogens mittpunkt.
    // elementFromPoint respekterar verklig paint order tvärs stacking contexts OCH portaler
    // — men hoppar över pointer-events:none. Vi återställer därför scrimens hit-testbarhet
    // för just proben. Det gör guarden robust mot de tysta haverierna en ren z-jämförelse
    // missar: byter någon DialogPortal-container till modalpanelen hamnar dialogen INUTI
    // scrimens z-80-context och målas under panelen — medan computed z-index fortfarande
    // läser 110 mot 80 och en taljämförelse hade sagt "grönt".
    const probe = await page.evaluate(() => {
      const scrim = document.querySelector<HTMLElement>(".jp-modal-scrim");
      const dialog = document.querySelector<HTMLElement>(
        "[data-slot='dialog-content']"
      );
      if (!scrim || !dialog) return { ok: false, occluder: "SAKNAS", z: "" };
      const prev = scrim.style.pointerEvents;
      scrim.style.pointerEvents = "auto";
      const r = dialog.getBoundingClientRect();
      const top = document.elementFromPoint(
        r.left + r.width / 2,
        r.top + r.height / 2
      );
      scrim.style.pointerEvents = prev;
      return {
        ok: dialog.contains(top),
        occluder: (top as HTMLElement | null)?.className ?? "null",
        z: `dialog=${getComputedStyle(dialog).zIndex} scrim=${getComputedStyle(scrim).zIndex}`,
      };
    });
    expect(
      probe.ok,
      `dialogen ockluderas av: ${probe.occluder} (${probe.z})`
    ).toBe(true);

    // Dialogen är också FUNKTIONELL inifrån modalen. Assertera preconditionen först —
    // annars är "tomlägestexten är borta" vakuöst sant. Modalen renderar `emptyDrawer`,
    // INTE `empty` (den senare finns aldrig i modalens DOM — en tidigare version av det här
    // testet asserterade just den och kunde därför inte falla).
    const emptyDrawer = page.getByText("Inga uppföljningar ännu.");
    await expect(emptyDrawer).toBeVisible();

    await followUpDialog
      .getByRole("button", { name: "Spara uppföljning" })
      .click();

    // Positiv assertion: uppföljningen sparades faktiskt (misslyckas server-actionen
    // stannar dialogen kvar med en role="alert" och tomläget står still).
    await expect(page.getByText(/uppföljning sparad/i)).toBeVisible();
    await expect(emptyDrawer).toHaveCount(0);
  });
});
