import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { expect, test, type Locator, type Page } from "@playwright/test";
import { recentSearch, recentSearchId } from "./fixtures";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

/**
 * The quick links in a real browser (#2265, ADR 0188 and its #2275 amendment): the /jobb hero shows the junior link
 * first and then the marked searches, hides the junior link at 0, after a failed count and after a failed profile
 * read, and removes a link with its × the way DESIGN.md's removal rule says (focus to the next link, else the
 * previous, else Anpassa, and a status). Anpassa at the end of the row opens a tick-box list, and
 * /mina-sidor/snabblankar shows the same list; /sokningar adds and removes from each row. All of them write through
 * the same endpoints and say so when the three are taken.
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
/** The hero's own links, without the list's "Alla sökningar" while it is open. */
const heroLinks = (page: Page) => nav(page).locator("a.jp-hero__quicklink");
const anpassa = (page: Page) => nav(page).getByRole("button", { name: "Anpassa snabblänkar", exact: true });
const choose = (page: Page) => nav(page).getByRole("button", { name: "Välj snabblänkar", exact: true });
const picker = (page: Page) => page.getByRole("dialog", { name: "Snabblänkar" });
/** The anchored list sits right under its button, wherever a change to the row has moved the button. */
async function expectListUnderItsButton(page: Page) {
  const button = nav(page).getByRole("button", { name: /^(Anpassa|Välj) snabblänkar$/ });
  await expect(async () => {
    const list = await picker(page).boundingBox();
    const trigger = await button.boundingBox();
    if (!list || !trigger) throw new Error("the list or its button has no box");
    expect(Math.abs(list.x - trigger.x)).toBeLessThan(1.5);
    expect(Math.abs(list.y - (trigger.y + trigger.height + 8))).toBeLessThan(1.5);
  }).toPass();
}
const rowNames = (list: Locator) =>
  list.getByRole("checkbox").evaluateAll((boxes) => boxes.map((box) => box.closest("label")?.textContent ?? ""));
const removeButton = (page: Page, label: string) =>
  page.getByRole("button", { name: `Ta bort snabblänken: ${label}`, exact: true });
const status = (page: Page, text: string) => page.getByRole("status").filter({ hasText: text });

async function shot(page: Page, name: string) {
  const dir = process.env.VISUAL_SHOTS_DIR;
  if (!dir) return;
  mkdirSync(dir, { recursive: true });
  await page.screenshot({ path: join(dir, `${name}.png`), fullPage: true });
}

/** A viewport shot: a full-page one resizes the viewport, which measures an open list again and would hide a miss. */
async function viewShot(page: Page, name: string) {
  const dir = process.env.VISUAL_SHOTS_DIR;
  if (!dir) return;
  mkdirSync(dir, { recursive: true });
  await page.screenshot({ path: join(dir, `${name}.png`) });
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

/** Another tab marks a search after this page rendered, so the backend's count no longer matches the page's. */
function markElsewhere(n: number) {
  const id = recentSearchId(n);
  harness.recentSearches = [
    ...harness.recentSearches.filter((search) => search.id !== id),
    { ...recentSearch(n, `Markerad i en annan flik ${n}`, true), id },
  ];
}

/** Another tab deletes a search after this page rendered. */
function deleteElsewhere(n: number) {
  harness.recentSearches = harness.recentSearches.filter((search) => search.id !== recentSearchId(n));
}

const NOT_FOUND = "Sökningen finns inte längre. Den kan ha tagits bort.";
const FULL = "Du har redan 3 snabblänkar. Ta bort en för att lägga till en ny.";

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
      await expect(links.nth(0)).toHaveAttribute("href", "/jobb?junior=on");
      await expect(links.nth(1)).toHaveAccessibleName(BACKEND);
      await expect(links.nth(1)).toHaveAttribute("href", /\/jobb\?.*q=Backendutvecklare/);
      await expect(links.nth(2)).toHaveAccessibleName(TESTER);
      await expect(removeButton(page, JUNIOR)).toBeVisible();
      await expect(anpassa(page)).toHaveText("Anpassa");
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

    test(`${state.name} and no marked search: no link, and Välj snabblänkar unless the profile failed`, async ({
      page,
    }) => {
      state.arrange();
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.goto("/jobb");
      await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
      if (state.name === "profile-error") {
        await expect(nav(page)).toHaveCount(0);
      } else {
        await expect(heroLinks(page)).toHaveCount(0);
        await expect(choose(page)).toBeVisible();
      }
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

  test("× removes a link at once: focus to the next link, then the previous, then Välj snabblänkar, each with a status", async ({
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
    await expect(heroLinks(page)).toHaveCount(0);
    await expect(choose(page)).toBeFocused();
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
    await expect(heroLinks(page)).toHaveCount(0);
    await expect(choose(page)).toBeVisible();
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

test.describe("the hero's Anpassa list", () => {
  const box = (page: Page, name: string) => picker(page).getByRole("checkbox", { name, exact: true });
  const PICKER_FULL = "Du kan ha högst 3 snabblänkar. Kryssa ur en för att välja en annan.";

  for (const width of [1280, 800, 390] as const) {
    test(`at ${width} px: a box per link, ticked as the row is, and the others unavailable at three`, async ({ page }) => {
      threeLinks();
      await page.setViewportSize({ width, height: 900 });
      await page.goto("/jobb");

      await anpassa(page).click();
      await expect(picker(page)).toBeVisible();
      await expect(picker(page).getByRole("checkbox")).toHaveCount(4);
      await expect(box(page, JUNIOR)).toBeChecked();
      await expect(box(page, BACKEND)).toBeChecked();
      await expect(box(page, TESTER)).toBeChecked();
      await expect(box(page, UNMARKED)).not.toBeChecked();
      await expect(box(page, UNMARKED)).toBeDisabled();
      await expect(picker(page).getByRole("status")).toHaveText(PICKER_FULL);
      await expect(box(page, UNMARKED)).toHaveAccessibleDescription(PICKER_FULL);
      await expect(picker(page).getByRole("link", { name: "Alla sökningar" })).toHaveCount(0);
      await noSidewaysScroll(page);
      if (width !== 390) await expectListUnderItsButton(page);
      await viewShot(page, `hero-picker-full-${width}`);
    });
  }

  test("an untick and a tick change the row at once, and focus stays on the box", async ({ page }) => {
    threeLinks();
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/jobb");
    await anpassa(page).click();

    await box(page, BACKEND).click();
    await expect(heroLinks(page)).toHaveCount(2);
    await expect(box(page, BACKEND)).not.toBeChecked();
    await expect(box(page, BACKEND)).toBeFocused();
    await expect(box(page, UNMARKED)).toBeEnabled();
    // Two links fit on one line, so the button moved up and to the left, and the list with it.
    await expectListUnderItsButton(page);
    await viewShot(page, "hero-picker-after-untick-1280");

    await box(page, UNMARKED).click();
    await expect(heroLinks(page)).toHaveCount(3);
    await expect(heroLinks(page).nth(1)).toHaveAccessibleName(UNMARKED);
    await expect(box(page, UNMARKED)).toBeChecked();
    await expect(box(page, UNMARKED)).toBeFocused();
    // Three links wrap the row again: the list moves down with the button instead of covering the new line.
    await expectListUnderItsButton(page);
    await viewShot(page, "hero-picker-after-tick-1280");
    expect(harness.quickLinkWrites.map((write) => write.route)).toEqual([
      `DELETE /api/v1/me/recent-searches/${recentSearchId(1)}/quick-link`,
      `PUT /api/v1/me/recent-searches/${recentSearchId(2)}/quick-link`,
    ]);
    // The rows stay where the list put them when it opened.
    expect(await rowNames(picker(page))).toEqual([JUNIOR, BACKEND, TESTER, UNMARKED]);
  });

  test("Välj snabblänkar with no link: a tick on the junior box shows the junior link, and the button becomes Anpassa", async ({
    page,
  }) => {
    harness.juniorItCount = { status: 200, body: { count: 152 } };
    harness.juniorQuickLinkEnabled = false;
    harness.recentSearches = [recentSearch(2, UNMARKED, false)];
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/jobb");
    await expect(heroLinks(page)).toHaveCount(0);
    await heroShot(page, "hero-choose-empty-1280");

    await choose(page).click();
    await box(page, JUNIOR).click();

    await expect(heroLinks(page)).toHaveCount(1);
    await expect(heroLinks(page).first()).toHaveAccessibleName(`${JUNIOR}, 152 just nu`);
    await expect(anpassa(page)).toBeVisible();
    await expect(box(page, JUNIOR)).toBeFocused();
    expect(harness.quickLinkWrites).toEqual([{ route: "PUT /api/v1/me/junior-quick-link", body: { enabled: true } }]);
  });

  test("the keyboard: Enter opens the list, Space ticks, Escape closes it and focus returns to the button", async ({
    page,
  }) => {
    threeLinks();
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/jobb");

    await anpassa(page).focus();
    await page.keyboard.press("Enter");
    await expect(picker(page)).toBeVisible();
    await expect(anpassa(page)).toHaveAttribute("aria-expanded", "true");
    await box(page, TESTER).focus();
    await page.keyboard.press("Space");
    await expect(box(page, TESTER)).not.toBeChecked();
    await expect(heroLinks(page)).toHaveCount(2);
    await expect(box(page, TESTER)).toBeFocused();
    await viewShot(page, "hero-picker-focus-1280");

    await page.keyboard.press("Escape");
    await expect(picker(page)).toHaveCount(0);
    await expect(anpassa(page)).toBeFocused();
    await expect(anpassa(page)).toHaveAttribute("aria-expanded", "false");
  });

  const refusals: ReadonlyArray<{ name: string; marked: boolean; elsewhere: () => void; alert: string }> = [
    { name: "a tick refused because another tab filled the cap", marked: false, elsewhere: () => markElsewhere(9), alert: FULL },
    { name: "a tick on a search another tab deleted", marked: false, elsewhere: () => deleteElsewhere(2), alert: NOT_FOUND },
    { name: "an untick of a search another tab deleted", marked: true, elsewhere: () => deleteElsewhere(2), alert: NOT_FOUND },
  ];

  for (const refusal of refusals) {
    test(`${refusal.name} says why, puts the box back and leaves focus on it`, async ({ page }) => {
      harness.juniorItCount = { status: 200, body: { count: 152 } };
      harness.recentSearches = [recentSearch(1, BACKEND, true), recentSearch(2, UNMARKED, refusal.marked)];
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.goto("/jobb");
      await anpassa(page).click();
      refusal.elsewhere();

      await box(page, UNMARKED).focus();
      await page.keyboard.press("Space");

      await expect(picker(page).getByRole("alert").filter({ hasText: refusal.alert })).toBeVisible();
      if (refusal.marked) await expect(box(page, UNMARKED)).toBeChecked();
      else await expect(box(page, UNMARKED)).not.toBeChecked();
      await expect(box(page, UNMARKED)).toBeFocused();
      await viewShot(page, `hero-picker-refused-${refusal.marked ? "untick" : "tick"}-${refusal.alert === FULL ? "full" : "gone"}-1280`);
    });
  }

  test("a write that raced another says so and changes nothing", async ({ page }) => {
    harness.recentSearches = [recentSearch(2, UNMARKED, false)];
    harness.refuseNextQuickLinkWrite = { status: 409, body: { title: "QuickLink.SlotTaken", status: 409 } };
    await page.setViewportSize({ width: 390, height: 900 });
    await page.goto("/jobb");
    await choose(page).click();

    await box(page, UNMARKED).click();

    await expect(
      picker(page).getByRole("alert").filter({ hasText: "Snabblänkarna ändrades samtidigt. Försök igen." })
    ).toBeVisible();
    await expect(box(page, UNMARKED)).not.toBeChecked();
    await expect(box(page, UNMARKED)).toBeFocused();
    await viewShot(page, "hero-picker-conflict-390");
  });

  test("Alla sökningar, shown when the list leaves searches out, opens the settings page with all of them", async ({
    page,
  }) => {
    threeLinks();
    harness.recentSearches = [
      ...harness.recentSearches,
      ...[4, 5, 6, 7, 8, 9].map((n) => recentSearch(n, `Sökning ${n}`, false)),
    ];
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/jobb");
    await anpassa(page).click();

    await picker(page).getByRole("link", { name: "Alla sökningar" }).click();

    await expect(page).toHaveURL(/\/mina-sidor\/snabblankar$/);
    await expect(page.getByRole("region", { name: "Snabblänkar" }).getByRole("checkbox")).toHaveCount(1 + 2 + 7);
  });

  test("a second tick made while the first write runs is kept, and the two are written in order", async ({ page }) => {
    harness.juniorItCount = { status: 200, body: { count: 152 } };
    harness.juniorQuickLinkEnabled = false;
    harness.recentSearches = [recentSearch(1, BACKEND, false), recentSearch(2, UNMARKED, false)];
    harness.quickLinkWriteDelayMs = 1200;
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/jobb");
    await choose(page).click();

    await box(page, BACKEND).click();
    await page.waitForTimeout(200);
    await box(page, UNMARKED).click();

    await expect(box(page, BACKEND)).toBeChecked();
    await expect(box(page, UNMARKED)).toBeChecked();
    await viewShot(page, "hero-picker-two-ticks-pending-1280");
    await expect(heroLinks(page)).toHaveCount(2, { timeout: 10_000 });
    await expect(box(page, UNMARKED)).toBeFocused();
    expect(harness.quickLinkWrites.map((write) => write.route)).toEqual([
      `PUT /api/v1/me/recent-searches/${recentSearchId(1)}/quick-link`,
      `PUT /api/v1/me/recent-searches/${recentSearchId(2)}/quick-link`,
    ]);
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
    await expect(page.getByText("Du kan ha högst 3 snabblänkar.", { exact: false })).toBeVisible();
    await expect(row(page, TESTER).getByRole("button", { name: "Lägg till som snabblänk" })).toHaveCount(0);
    expect(harness.quickLinkWrites.map((write) => write.route)).toEqual([
      `PUT /api/v1/me/recent-searches/${recentSearchId(2)}/quick-link`,
    ]);
    await shot(page, "sokningar-full-1280");
    await page.setViewportSize({ width: 390, height: 900 });
    await shot(page, "sokningar-full-390");
  });

  // A refusal keeps the control and its focus: the page is not refreshed under the keyboard (design-reviewer's
  // re-check of the first fix round).
  const refusals: ReadonlyArray<{
    name: string;
    marked: boolean;
    elsewhere: () => void;
    button: string;
    alert: string;
  }> = [
    { name: "an add refused because another tab filled the cap", marked: false, elsewhere: () => markElsewhere(9), button: "Lägg till som snabblänk", alert: FULL },
    { name: "an add of a search another tab deleted", marked: false, elsewhere: () => deleteElsewhere(2), button: "Lägg till som snabblänk", alert: NOT_FOUND },
    { name: "a removal of a search another tab deleted", marked: true, elsewhere: () => deleteElsewhere(2), button: "Ta bort som snabblänk", alert: NOT_FOUND },
  ];

  for (const refusal of refusals) {
    test(`${refusal.name} says why and leaves focus on the control`, async ({ page }) => {
      harness.recentSearches = [recentSearch(1, BACKEND, true), recentSearch(2, UNMARKED, refusal.marked)];
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.goto("/sokningar");
      const control = row(page, UNMARKED).getByRole("button", { name: refusal.button });
      refusal.elsewhere();

      await control.focus();
      await page.keyboard.press("Enter");

      await expect(page.getByRole("alert").filter({ hasText: refusal.alert })).toBeVisible();
      await expect(control).toBeFocused();
      await shot(page, `sokningar-refused-${refusal.button === "Ta bort som snabblänk" ? "remove" : "add"}-${refusal.alert === FULL ? "full" : "gone"}-1280`);
    });
  }
});

test.describe("/mina-sidor/snabblankar", () => {
  const card = (page: Page) => page.getByRole("region", { name: "Snabblänkar" });
  const box = (page: Page, name: string) => card(page).getByRole("checkbox", { name, exact: true });

  test("a new account: the junior box ticked, and no search to tick yet", async ({ page }) => {
    for (const width of [1280, 390] as const) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto("/mina-sidor/snabblankar");
      await expect(box(page, JUNIOR)).toBeChecked();
      await expect(card(page).getByText("Gör en sökning under Jobb, så kan du lägga till den här.")).toBeVisible();
      await noSidewaysScroll(page);
      await shot(page, `settings-new-account-${width}`);
    }
  });

  test("every recent search is offered, and at three the others are unavailable and the list says why", async ({
    page,
  }) => {
    harness.recentSearches = [
      recentSearch(1, BACKEND, true),
      recentSearch(2, UNMARKED, false),
      recentSearch(3, TESTER, true),
      ...[4, 5, 6, 7, 8, 9, 10].map((n) => recentSearch(n, `Sökning ${n}`, false)),
    ];
    for (const width of [1280, 390] as const) {
      await page.setViewportSize({ width, height: 900 });
      await page.goto("/mina-sidor/snabblankar");
      await expect(card(page).getByRole("checkbox")).toHaveCount(11);
      expect(await rowNames(card(page))).toEqual([
        JUNIOR,
        BACKEND,
        TESTER,
        UNMARKED,
        ...[4, 5, 6, 7, 8, 9, 10].map((n) => `Sökning ${n}`),
      ]);
      await expect(box(page, UNMARKED)).toBeDisabled();
      await expect(card(page).getByText("Du kan ha högst 3 snabblänkar. Kryssa ur en för att välja en annan.")).toBeVisible();
      await noSidewaysScroll(page);
      await shot(page, `settings-full-${width}`);
    }
  });

  test("the junior box: an untick and a tick write at once, and focus stays on the box", async ({ page }) => {
    harness.recentSearches = [recentSearch(1, BACKEND, true), recentSearch(2, UNMARKED, false)];
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/mina-sidor/snabblankar");

    await box(page, JUNIOR).click();
    await expect(box(page, JUNIOR)).not.toBeChecked();
    await expect(box(page, JUNIOR)).toBeFocused();
    await shot(page, "settings-junior-unticked-1280");

    await box(page, JUNIOR).click();
    await expect(box(page, JUNIOR)).toBeChecked();
    await expect(box(page, JUNIOR)).toBeFocused();
    expect(harness.quickLinkWrites).toEqual([
      { route: "PUT /api/v1/me/junior-quick-link", body: { enabled: false } },
      { route: "PUT /api/v1/me/junior-quick-link", body: { enabled: true } },
    ]);
    expect(harness.juniorQuickLinkEnabled).toBe(true);
  });

  for (const refusal of [
    { name: "a tick refused because another tab filled the cap", elsewhere: () => markElsewhere(9), alert: FULL },
    { name: "a tick on a search another tab deleted", elsewhere: () => deleteElsewhere(2), alert: NOT_FOUND },
  ]) {
    test(`${refusal.name} says why, puts the box back and leaves focus on it`, async ({ page }) => {
      harness.recentSearches = [recentSearch(1, BACKEND, true), recentSearch(2, UNMARKED, false)];
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.goto("/mina-sidor/snabblankar");
      refusal.elsewhere();

      await box(page, UNMARKED).focus();
      await page.keyboard.press("Space");

      await expect(page.getByRole("alert").filter({ hasText: refusal.alert })).toBeVisible();
      await expect(box(page, UNMARKED)).not.toBeChecked();
      await expect(box(page, UNMARKED)).toBeFocused();
    });
  }

  test("a write that raced another says so and changes nothing", async ({ page }) => {
    harness.recentSearches = [recentSearch(2, UNMARKED, false)];
    harness.refuseNextQuickLinkWrite = { status: 409, body: { title: "QuickLink.SlotTaken", status: 409 } };
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/mina-sidor/snabblankar");

    await box(page, UNMARKED).click();

    await expect(
      page.getByRole("alert").filter({ hasText: "Snabblänkarna ändrades samtidigt. Försök igen." })
    ).toBeVisible();
    await expect(box(page, UNMARKED)).not.toBeChecked();
    await expect(box(page, UNMARKED)).toBeFocused();
    await shot(page, "settings-conflict-1280");
    await page.setViewportSize({ width: 390, height: 900 });
    await shot(page, "settings-conflict-390");
  });
});
