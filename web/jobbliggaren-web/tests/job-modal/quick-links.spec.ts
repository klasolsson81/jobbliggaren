import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { recentSearch, recentSearchId } from "./fixtures";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

/**
 * The quick links in a real browser (#2265, ADR 0188): the /jobb hero shows the junior link first and then the marked
 * searches, hides the junior link at 0, after a failed count and after a failed profile read, and removes a link with
 * its × the way DESIGN.md's removal rule says (focus to the next link, else the previous, else the h1, and a status).
 * /sokningar and /mina-sidor/snabblankar add and remove through the same writes, and say so when the three are taken.
 *
 * With VISUAL_SHOTS_DIR set, every state is also saved as a screenshot for the design verdict.
 */

let harness: Harness;

test.beforeAll(async () => {
  harness = await startHarness();
});

test.afterAll(async () => {
  await harness.stop();
});

test.beforeEach(async ({ context }) => {
  harness.reset();
  await context.addCookies([
    { name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN, secure: true, httpOnly: true, sameSite: "Strict" },
  ]);
});

test.afterEach(() => {
  expect(harness.misses, "the page read a backend route the harness does not answer").toEqual([]);
});

const JUNIOR = "Juniora IT-jobb";
const BACKEND = "Backendutvecklare Göteborg";
const TESTER = "Testautomatisering";
const UNMARKED = "Frontendutvecklare";
const LONG =
  "Systemutvecklare med erfarenhet av .NET, Azure och PostgreSQL i offentlig sektor eller hos en kommun";

const nav = (page: Page) => page.getByRole("navigation", { name: "Snabblänkar" });
const removeButton = (page: Page, label: string) =>
  page.getByRole("button", { name: `Ta bort snabblänken: ${label}`, exact: true });
const status = (page: Page, text: string) => page.getByRole("status").filter({ hasText: text });

async function shot(page: Page, name: string) {
  const dir = process.env.VISUAL_SHOTS_DIR;
  if (!dir) return;
  mkdirSync(dir, { recursive: true });
  await page.screenshot({ path: join(dir, `${name}.png`), fullPage: true });
}

/**
 * The hero only: the shots compare the band, and a full /jobb page is mostly the list below it. Scoped to `#main`,
 * because a streamed page can hold a second, hidden copy of the band outside it for a moment.
 */
const hero = (page: Page) => page.locator("#main section.jp-hero");

async function heroShot(page: Page, name: string) {
  const dir = process.env.VISUAL_SHOTS_DIR;
  if (!dir) return;
  mkdirSync(dir, { recursive: true });
  await hero(page).screenshot({ path: join(dir, `${name}.png`) });
}

async function noSidewaysScroll(page: Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow, "the page scrolls sideways").toBeLessThanOrEqual(0);
}

function threeLinks() {
  harness.juniorItCount = { status: 200, body: { count: 152 } };
  harness.recentSearches = [
    recentSearch(1, BACKEND, true),
    recentSearch(2, UNMARKED, false),
    recentSearch(3, TESTER, true),
  ];
}

test.describe("the /jobb hero", () => {
  for (const width of [390, 800, 1280, 1920, 3440] as const) {
    test(`three links at ${width} px: the junior link first, then the marked searches in the list's order`, async ({
      page,
    }) => {
      threeLinks();
      await page.setViewportSize({ width, height: 900 });
      await page.goto("/jobb");

      const links = nav(page).getByRole("link");
      await expect(links).toHaveCount(3);
      await expect(links.nth(0)).toHaveAccessibleName(`${JUNIOR}, 152 just nu`);
      await expect(links.nth(0)).toHaveAttribute("href", "/junior");
      await expect(links.nth(1)).toHaveAccessibleName(BACKEND);
      await expect(links.nth(1)).toHaveAttribute("href", /\/jobb\?.*q=Backendutvecklare/);
      await expect(links.nth(2)).toHaveAccessibleName(TESTER);
      await expect(removeButton(page, JUNIOR)).toBeVisible();
      await noSidewaysScroll(page);
      await heroShot(page, `hero-three-links-${width}`);
    });
  }

  test("a long search label wraps inside the plate", async ({ page }) => {
    harness.juniorItCount = { status: 200, body: { count: 152 } };
    harness.recentSearches = [recentSearch(1, LONG, true), recentSearch(3, TESTER, true)];
    for (const width of [390, 1280] as const) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto("/jobb");
      await expect(nav(page).getByRole("link")).toHaveCount(3);
      await noSidewaysScroll(page);
      await heroShot(page, `hero-long-label-${width}`);
    }
  });

  test("the keyboard focus ring on a link and on its ×", async ({ page }) => {
    threeLinks();
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/jobb");
    const junior = nav(page).getByRole("link").first();
    await junior.focus();
    await page.keyboard.press("Tab");
    await page.keyboard.press("Shift+Tab");
    await expect(junior).toBeFocused();
    await heroShot(page, "hero-focus-link-1280");
    await page.keyboard.press("Tab");
    await expect(removeButton(page, JUNIOR)).toBeFocused();
    await heroShot(page, "hero-focus-remove-1280");
  });

  const hidden: ReadonlyArray<{ name: string; arrange: () => void }> = [
    { name: "count-zero", arrange: () => (harness.juniorItCount = { status: 200, body: { count: 0 } }) },
    {
      name: "count-error",
      arrange: () => (harness.juniorItCount = { status: 500, body: { title: "Internal Server Error", status: 500 } }),
    },
    {
      name: "profile-error",
      arrange: () => {
        harness.juniorItCount = { status: 200, body: { count: 152 } };
        harness.refuseProfileRead = true;
      },
    },
    {
      name: "junior-off",
      arrange: () => {
        harness.juniorItCount = { status: 200, body: { count: 152 } };
        harness.juniorQuickLinkEnabled = false;
      },
    },
  ];

  for (const state of hidden) {
    test(`${state.name}: the junior link is hidden and a marked search still shows`, async ({ page }) => {
      state.arrange();
      harness.recentSearches = [recentSearch(1, BACKEND, true)];
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.goto("/jobb");
      await expect(nav(page).getByRole("link")).toHaveCount(1);
      await expect(nav(page).getByRole("link")).toHaveAccessibleName(BACKEND);
      await heroShot(page, `hero-${state.name}-1280`);
    });

    test(`${state.name} and no marked search: the hero has no quick links at all`, async ({ page }) => {
      state.arrange();
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.goto("/jobb");
      await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
      await expect(nav(page)).toHaveCount(0);
      await heroShot(page, `hero-${state.name}-no-links-1280`);
    });
  }

  for (const width of [1280, 390] as const) {
    test(`the loading state reserves one link row at ${width} px`, async ({ page }) => {
      threeLinks();
      harness.juniorItCount = { status: 200, body: { count: 152 }, delayMs: 1500 };
      await page.setViewportSize({ width, height: 900 });
      await page.goto("/jobb", { waitUntil: "commit" });
      await expect(page.locator(".jp-hero__quicklinks-reserve")).toBeVisible();
      // A viewport shot, which waits for no element to settle: the skeleton is replaced as soon as the count arrives.
      const dir = process.env.VISUAL_SHOTS_DIR;
      if (dir) {
        mkdirSync(dir, { recursive: true });
        await page.screenshot({ path: join(dir, `hero-loading-${width}.png`) });
      }
      await expect(nav(page).getByRole("link")).toHaveCount(3);
    });
  }

  // What the link row does to the band once the page has come: the shots and the design verdict read these heights.
  for (const width of [1280, 800, 390] as const) {
    test(`the band's height with no link, three links and a long label at ${width} px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      const heightWith = async (arrange: () => void) => {
        harness.reset();
        arrange();
        await page.goto("/jobb");
        await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
        return (await hero(page).boundingBox())?.height;
      };
      const none = await heightWith(() => undefined);
      const three = await heightWith(threeLinks);
      const long = await heightWith(() => {
        harness.juniorItCount = { status: 200, body: { count: 152 } };
        harness.recentSearches = [recentSearch(1, LONG, true), recentSearch(3, TESTER, true)];
      });
      const reading = `${width} px: no link ${none}, three links ${three}, long label ${long}`;
      console.log(`band height ${reading}`);
      test.info().annotations.push({ type: "band height", description: reading });
      if (width === 1280) {
        expect(three).toBe(none);
        expect(long).toBe(none);
      }
    });
  }

  test("a count slower than the reader's 2 s timeout hides the junior link, and the searches still show", async ({
    page,
  }) => {
    threeLinks();
    harness.juniorItCount = { status: 200, body: { count: 152 }, delayMs: 3000 };
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/jobb");
    const links = nav(page).getByRole("link");
    await expect(links).toHaveCount(2);
    await expect(links.nth(0)).toHaveAccessibleName(BACKEND);
    await expect(removeButton(page, JUNIOR)).toHaveCount(0);
  });

  test("× removes a link at once: focus to the next link, then the previous, then the h1, each with a status", async ({
    page,
  }) => {
    threeLinks();
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/jobb");
    const links = nav(page).getByRole("link");

    await removeButton(page, BACKEND).click();
    await expect(links).toHaveCount(2);
    await expect(status(page, `Snabblänken har tagits bort: ${BACKEND}`)).toBeAttached();
    await expect(links.nth(1)).toBeFocused();
    await expect(links.nth(1)).toHaveAccessibleName(TESTER);
    expect(harness.quickLinkWrites.map((write) => write.route)).toEqual([
      `DELETE /api/v1/me/recent-searches/${recentSearchId(1)}/quick-link`,
    ]);
    await heroShot(page, "hero-after-remove-middle-1280");

    await removeButton(page, TESTER).click();
    await expect(links).toHaveCount(1);
    await expect(links.first()).toBeFocused();
    await expect(status(page, `Snabblänken har tagits bort: ${TESTER}`)).toBeAttached();

    await removeButton(page, JUNIOR).click();
    await expect(nav(page)).toHaveCount(0);
    await expect(page.locator("#jobb-hero-title")).toBeFocused();
    await expect(status(page, `Snabblänken har tagits bort: ${JUNIOR}`)).toBeAttached();
    expect(harness.quickLinkWrites.at(-1)).toEqual({
      route: "PUT /api/v1/me/junior-quick-link",
      body: { enabled: false },
    });
    expect(harness.recentSearches.filter((search) => search.isQuickLink)).toEqual([]);
    expect(harness.juniorQuickLinkEnabled).toBe(false);
    await heroShot(page, "hero-after-remove-all-1280");

    await page.reload();
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    await expect(nav(page)).toHaveCount(0);
  });

  test("a refused removal keeps the link, says why and leaves focus on its ×", async ({ page }) => {
    threeLinks();
    harness.refuseNextQuickLinkWrite = { status: 500, body: { title: "Internal Server Error", status: 500 } };
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/jobb");

    await removeButton(page, BACKEND).click();
    await expect(page.getByRole("alert").filter({ hasText: "Snabblänken kunde inte ändras. Försök igen." })).toBeVisible();
    await expect(nav(page).getByRole("link")).toHaveCount(3);
    await expect(removeButton(page, BACKEND)).toBeFocused();
    await heroShot(page, "hero-remove-refused-1280");
    await page.setViewportSize({ width: 390, height: 900 });
    await heroShot(page, "hero-remove-refused-390");
  });
});

test.describe("/sokningar", () => {
  const row = (page: Page, label: string) =>
    page.getByRole("listitem").filter({ has: page.getByRole("heading", { name: label, exact: true }) });

  test("a search becomes a quick link from its row, and the last free place says the three are taken", async ({
    page,
  }) => {
    harness.recentSearches = [
      recentSearch(1, BACKEND, true),
      recentSearch(2, UNMARKED, false),
      recentSearch(3, TESTER, false),
    ];
    for (const width of [1280, 390] as const) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto("/sokningar");
      await expect(row(page, BACKEND).getByRole("button", { name: "Ta bort som snabblänk" })).toBeVisible();
      await expect(row(page, UNMARKED).getByRole("button", { name: "Lägg till som snabblänk" })).toBeVisible();
      await noSidewaysScroll(page);
      await shot(page, `sokningar-room-${width}`);
    }

    await page.setViewportSize({ width: 1280, height: 900 });
    await row(page, UNMARKED).getByRole("button", { name: "Lägg till som snabblänk" }).click();
    await expect(status(page, `Snabblänken har lagts till: ${UNMARKED}`)).toBeAttached();
    await expect(row(page, UNMARKED).getByRole("button", { name: "Ta bort som snabblänk" })).toBeVisible();
    await expect(page.getByText("Du har tre snabblänkar, vilket är det högsta.", { exact: false })).toBeVisible();
    await expect(row(page, TESTER).getByRole("button", { name: "Lägg till som snabblänk" })).toHaveCount(0);
    expect(harness.quickLinkWrites.map((write) => write.route)).toEqual([
      `PUT /api/v1/me/recent-searches/${recentSearchId(2)}/quick-link`,
    ]);
    await shot(page, "sokningar-full-1280");
    await page.setViewportSize({ width: 390, height: 900 });
    await shot(page, "sokningar-full-390");
  });

  test("an add the API refuses as full says so", async ({ page }) => {
    harness.recentSearches = [recentSearch(1, BACKEND, true), recentSearch(2, UNMARKED, false)];
    harness.refuseNextQuickLinkWrite = { status: 409, body: { title: "QuickLink.MaxTotal", status: 409 } };
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/sokningar");
    await row(page, UNMARKED).getByRole("button", { name: "Lägg till som snabblänk" }).click();
    await expect(
      page.getByRole("alert").filter({ hasText: "Du har redan tre snabblänkar. Ta bort en för att lägga till en ny." })
    ).toBeVisible();
    await shot(page, "sokningar-refused-full-1280");
  });
});

test.describe("/mina-sidor/snabblankar", () => {
  const current = (page: Page) => page.getByRole("region", { name: "Dina snabblänkar" });
  const addable = (page: Page) => page.getByRole("region", { name: "Lägg till en snabblänk" });

  test("a new account: the junior link, and no search to add yet", async ({ page }) => {
    for (const width of [1280, 390] as const) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto("/mina-sidor/snabblankar");
      await expect(current(page).getByRole("button", { name: `Ta bort snabblänken: ${JUNIOR}` })).toBeVisible();
      await expect(addable(page).getByText("Gör en sökning under Jobb, så kan du lägga till den här.")).toBeVisible();
      await noSidewaysScroll(page);
      await shot(page, `settings-new-account-${width}`);
    }
  });

  test("three links: each can be removed, and the add list says the three are taken", async ({ page }) => {
    harness.recentSearches = [
      recentSearch(1, BACKEND, true),
      recentSearch(2, UNMARKED, false),
      recentSearch(3, TESTER, true),
    ];
    for (const width of [1280, 390] as const) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto("/mina-sidor/snabblankar");
      await expect(current(page).getByRole("listitem")).toHaveCount(3);
      await expect(addable(page).getByText("Du har tre snabblänkar, vilket är det högsta.", { exact: false })).toBeVisible();
      await shot(page, `settings-full-${width}`);
    }
  });

  test("the junior link moves between the lists, and focus and the status follow", async ({ page }) => {
    harness.recentSearches = [recentSearch(1, BACKEND, true), recentSearch(2, UNMARKED, false)];
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/mina-sidor/snabblankar");

    await current(page).getByRole("button", { name: `Ta bort snabblänken: ${JUNIOR}` }).click();
    await expect(status(page, `Snabblänken har tagits bort: ${JUNIOR}`)).toBeAttached();
    await expect(addable(page).getByRole("button", { name: `Lägg till som snabblänk: ${JUNIOR}` })).toBeVisible();
    await expect(current(page).getByRole("button", { name: `Ta bort snabblänken: ${BACKEND}` })).toBeFocused();
    await shot(page, "settings-junior-removed-1280");

    await addable(page).getByRole("button", { name: `Lägg till som snabblänk: ${JUNIOR}` }).click();
    await expect(status(page, `Snabblänken har lagts till: ${JUNIOR}`)).toBeAttached();
    await expect(current(page).getByRole("button", { name: `Ta bort snabblänken: ${JUNIOR}` })).toBeVisible();
    await expect(addable(page).getByRole("button", { name: `Lägg till som snabblänk: ${UNMARKED}` })).toBeFocused();
    expect(harness.quickLinkWrites).toEqual([
      { route: "PUT /api/v1/me/junior-quick-link", body: { enabled: false } },
      { route: "PUT /api/v1/me/junior-quick-link", body: { enabled: true } },
    ]);
  });

  test("a write that raced another says so and changes nothing", async ({ page }) => {
    harness.recentSearches = [recentSearch(2, UNMARKED, false)];
    harness.refuseNextQuickLinkWrite = { status: 409, body: { title: "QuickLink.SlotTaken", status: 409 } };
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/mina-sidor/snabblankar");
    await addable(page).getByRole("button", { name: `Lägg till som snabblänk: ${UNMARKED}` }).click();
    await expect(
      page.getByRole("alert").filter({ hasText: "Snabblänkarna ändrades samtidigt i ett annat fönster. Försök igen." })
    ).toBeVisible();
    await expect(addable(page).getByRole("button", { name: `Lägg till som snabblänk: ${UNMARKED}` })).toBeFocused();
    await shot(page, "settings-conflict-1280");
    await page.setViewportSize({ width: 390, height: 900 });
    await shot(page, "settings-conflict-390");
  });
});
