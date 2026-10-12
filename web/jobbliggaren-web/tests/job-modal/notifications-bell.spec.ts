import { expect, test, type Page } from "@playwright/test";
import { ADS } from "./fixtures";
import { APP_ORIGIN, QUIET_NOTIFICATION_FEED, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

/**
 * The header bell (#1666, #2130) in a real browser: the badge and the button's name carry the unread count,
 * a row opens its ad in the intercepted job modal and marks that row read, and "Markera alla som lästa" sends
 * each source's token, as the exact string the feed returned, to that source's own read endpoint. Microsecond
 * tokens pin the string pass-through: a token re-serialised through a JS Date would lose them.
 *
 * The click-to-read tests prove what a unit test cannot: that a Server Action fired WITHOUT waiting from the
 * click that also opens the ad as the intercepted modal leaves that modal mounted, while the action is in
 * flight and after it answers; the write must still land, and nothing may re-render under the modal.
 */

let harness: Harness;

const MATCH_TOKEN = "2026-10-11T07:14:03.123456+00:00";
const FOLLOW_TOKEN = "2026-10-11T08:00:00.000001+00:00";

const UNREAD_FEED = {
  items: [
    {
      kind: "FollowedCompanyAd",
      jobAdId: ADS.saved.id,
      title: ADS.saved.title,
      companyName: ADS.saved.company,
      occurredAt: FOLLOW_TOKEN,
      isUnread: true,
      grade: null,
    },
    {
      kind: "Match",
      jobAdId: ADS.open.id,
      title: ADS.open.title,
      companyName: ADS.open.company,
      occurredAt: MATCH_TOKEN,
      isUnread: true,
      grade: "Top",
    },
  ],
  sources: [
    { kind: "Match", unreadCount: 1, acknowledgedThrough: MATCH_TOKEN },
    { kind: "FollowedCompanyAd", unreadCount: 1, acknowledgedThrough: FOLLOW_TOKEN },
  ],
};

test.beforeAll(async () => {
  harness = await startHarness();
});

test.afterAll(async () => {
  await harness.stop();
});

test.beforeEach(async ({ context }) => {
  harness.reset();
  harness.notificationFeed = UNREAD_FEED;
  await context.addCookies([
    { name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN, secure: true, httpOnly: true, sameSite: "Strict" },
  ]);
});

test.afterEach(() => {
  expect(harness.misses, "the pages read a backend route the harness does not answer").toEqual([]);
});

test("the bell names its unread count and a row opens the ad in the job modal", async ({ page }) => {
  await page.goto("/sparade");
  const bell = page.getByRole("button", { name: "Notiser, 2 olästa" });
  await expect(bell).toBeVisible();
  await expect(page.locator(".jp-notif-badge")).toHaveText("2");

  await bell.click();
  const panel = page.getByRole("dialog", { name: "Notiser" });
  await expect(panel.getByText("2 olästa")).toBeVisible();
  await expect(panel.getByText("Toppmatch")).toBeVisible();
  await expect(panel.getByText("Bevakar")).toBeVisible();
  // The panel holds notifications and nothing else (#2130): the rows' own links, no fixed footer.
  await expect(panel.getByRole("link")).toHaveCount(2);
  await expect(panel.locator(".jp-notif__foot")).toHaveCount(0);

  await panel.getByRole("link", { name: ADS.open.title }).click();
  await expect(page).toHaveURL(`/jobb/${ADS.open.id}`);
  await expect(page.getByRole("dialog", { name: ADS.open.title })).toContainText("Öppna annonsen");
  await expect(page.getByRole("dialog", { name: "Notiser" })).toHaveCount(0);
});

test("Markera alla som lästa sends each token unchanged to its own bell endpoint and moves no watermark", async ({ page }) => {
  await page.goto("/sparade");
  await page.getByRole("button", { name: "Notiser, 2 olästa" }).click();
  const panel = page.getByRole("dialog", { name: "Notiser" });
  harness.notificationFeed = {
    ...QUIET_NOTIFICATION_FEED,
    items: UNREAD_FEED.items.map((item) => ({ ...item, isUnread: false })),
  };

  await panel.getByRole("button", { name: "Markera alla som lästa" }).click();

  await expect(panel.getByText("Inga olästa")).toBeVisible();
  expect(harness.readReceipts).toEqual([
    { route: "POST /api/v1/me/notification-feed/matches/read", body: { throughToken: MATCH_TOKEN } },
    { route: "POST /api/v1/me/notification-feed/followed-company-ads/read", body: { throughToken: FOLLOW_TOKEN } },
  ]);
  // The bell no longer acknowledges the sources' watermarks (Klas 2026-10-11): Översikt and the lists keep their numbers.
  expect(harness.seenReceipts).toEqual([]);
  await expect(page.locator(".jp-notif-badge")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Notiser" })).toBeVisible();
});

// The sheet at phone width: inside the window, nothing scrolls sideways.
test("the panel fits a 375 px screen and the page does not scroll sideways", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto("/sparade");
  await page.getByRole("button", { name: "Notiser, 2 olästa" }).click();
  const panel = page.getByRole("dialog", { name: "Notiser" });
  await expect(panel).toBeVisible();

  const box = await panel.boundingBox();
  expect(box).not.toBeNull();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(375);
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(overflow).toBeLessThanOrEqual(0);
});

test("opening the bell writes nothing", async ({ page }) => {
  await page.goto("/sparade");
  await page.getByRole("button", { name: "Notiser, 2 olästa" }).click();
  await expect(page.getByRole("dialog", { name: "Notiser" }).getByText("2 olästa")).toBeVisible();
  // A write the click started would still be in flight here; wait for the network to settle first.
  await page.waitForLoadState("networkidle");

  expect(harness.seenReceipts).toEqual([]);
  expect(harness.readReceipts).toEqual([]);
  expect(harness.requests.filter((route) => route.startsWith("POST ") && /\/seen|notification-feed/.test(route))).toEqual([]);
});

const READ_FOLLOWED = `POST /api/v1/me/notification-feed/followed-company-ads/${ADS.saved.id}/read`;
const READ_MATCH = `POST /api/v1/me/notification-feed/matches/${ADS.open.id}/read`;
const dots = (page: Page) => page.locator(".jp-notif__dot");

// The load-bearing proof of click-to-read, at two speeds. The click opens the ad as the intercepted modal AND fires
// the mark without waiting. With the answer held for five seconds, a navigation queued behind the pending action
// would show as a modal that is slow to open; with the backend answering at once, the response lands while the modal
// is opening. The modal is tagged, so any re-render that unmounted and remounted it drops the tag, and the
// heading's x position shows the page behind it never moved. (Measured 2026-10-12: an action that also called
// revalidatePath passed this too, so the no-re-render rule on the action is guarded by the action's own unit test,
// not by this one.)
for (const holdMs of [0, 5_000]) {
  test(`a click on a row opens the ad in the modal over an unmoved page, and the modal stays mounted while the mark is held ${holdMs} ms and after it lands`, async ({ page }) => {
    harness.readMarkDelayMs = holdMs;
    await page.goto("/sparade");
    const heading = page.locator("main h1");
    const left = (await heading.boundingBox())?.x;
    await page.getByRole("button", { name: "Notiser, 2 olästa" }).click();
    const panel = page.getByRole("dialog", { name: "Notiser" });
    await expect(dots(page)).toHaveCount(2);

    await panel.getByRole("link", { name: ADS.open.title }).click();

    // The modal must not wait for the mark: it opens well inside the hold.
    const modal = page.getByRole("dialog", { name: ADS.open.title });
    await expect(page).toHaveURL(`/jobb/${ADS.open.id}`, { timeout: 2_500 });
    await expect(modal).toContainText("Öppna annonsen", { timeout: 2_500 });
    await modal.evaluate((element) => element.setAttribute("data-probe", "same-node"));
    await expect.poll(() => harness.readReceipts.map((receipt) => receipt.route)).toEqual([READ_MATCH]);
    await expect(page.locator("[data-probe]")).toHaveCount(1);
    expect((await heading.boundingBox())?.x).toBe(left);

    // After the answer: still the same node, still open, and the page behind it has not moved.
    await page.waitForTimeout(holdMs + 1_000);
    await expect(page.locator("[data-probe]")).toHaveCount(1);
    await expect(modal).toBeVisible();
    expect((await heading.boundingBox())?.x).toBe(left);
    expect(harness.readReceipts).toEqual([{ route: READ_MATCH, body: {} }]);
    // No watermark was moved, and nothing else was written.
    expect(harness.seenReceipts).toEqual([]);

    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await page.getByRole("button", { name: /^Notiser/ }).click();
    // The confirmed mark lowered the count by exactly one, without a new read: one dot and "1 oläst" remain.
    await expect(page.getByRole("button", { name: "Notiser, 1 oläst" })).toBeVisible();
    await expect(dots(page)).toHaveCount(1);
  });
}

test("the dot goes at once and the count follows only when the server has answered", async ({ page }) => {
  harness.readMarkDelayMs = 1500;
  await page.goto("/sparade");
  await page.getByRole("button", { name: "Notiser, 2 olästa" }).click();
  const panel = page.getByRole("dialog", { name: "Notiser" });

  // A modified click opens the ad elsewhere (headless Chromium may or may not surface that tab) and leaves the
  // panel open on this page, so the row can be watched.
  await panel.getByRole("link", { name: ADS.saved.title }).click({ modifiers: ["Control"] });

  await expect(dots(page)).toHaveCount(1);
  await expect(panel.getByText("2 olästa")).toBeVisible();
  await expect(page.locator(".jp-notif-badge")).toHaveText("2");
  await expect.poll(() => harness.readReceipts.map((receipt) => receipt.route)).toEqual([READ_FOLLOWED]);

  await expect(panel.getByText("1 oläst")).toBeVisible();
  await expect(page.locator(".jp-notif-badge")).toHaveText("1");
  await expect(panel).toBeVisible();
  for (const other of page.context().pages()) if (other !== page) await other.close();
});

test("a refused mark still opens the ad and puts the dot back", async ({ page }) => {
  harness.refuseNextReadMark = true;
  await page.goto("/sparade");
  await page.getByRole("button", { name: "Notiser, 2 olästa" }).click();
  const panel = page.getByRole("dialog", { name: "Notiser" });

  await panel.getByRole("link", { name: ADS.open.title }).click();

  await expect(page).toHaveURL(`/jobb/${ADS.open.id}`);
  await expect(page.getByRole("dialog", { name: ADS.open.title })).toContainText("Öppna annonsen");
  await expect.poll(() => harness.readReceipts.length).toBe(1);
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: /^Notiser/ }).click();
  // Refused: the row is unread at the next read, and the count never moved.
  await expect(page.getByRole("button", { name: "Notiser, 2 olästa" })).toBeVisible();
  await expect(dots(page)).toHaveCount(2);
});

const DISMISS_FOLLOWED = `POST /api/v1/me/notification-feed/followed-company-ads/${ADS.saved.id}/dismiss`;

// #2151: the X in a real browser. It is a button beside the row link, so a click on it opens nothing; the row goes at once, the
// count follows the server, and exactly one dismissal is sent. The modal-safe action shape is proved for the read mark above.
test("the X takes just that notification out, opens nothing, and the count follows the server", async ({ page }) => {
  harness.readMarkDelayMs = 800;
  await page.goto("/sparade");
  await page.getByRole("button", { name: "Notiser, 2 olästa" }).click();
  const panel = page.getByRole("dialog", { name: "Notiser" });

  await panel.getByRole("button", { name: `Ta bort notisen: Bevakat företag, ${ADS.saved.title}, ${ADS.saved.company}` }).click();

  await expect(panel.getByRole("link", { name: ADS.saved.title })).toHaveCount(0);
  await expect(panel.getByRole("link", { name: ADS.open.title })).toBeVisible();
  await expect(page).toHaveURL("/sparade");
  await expect(panel.getByText("2 olästa")).toBeVisible();
  await expect.poll(() => harness.readReceipts.map((receipt) => receipt.route)).toEqual([DISMISS_FOLLOWED]);
  await expect(panel.getByText("1 oläst")).toBeVisible();
  await expect(page.locator(".jp-notif-badge")).toHaveText("1");
  await expect(panel).toBeVisible();
  // Focus went to the other row's X, not to the page.
  await expect(panel.getByRole("button", { name: `Ta bort notisen: Ny matchning, ${ADS.open.title}, ${ADS.open.company}` })).toBeFocused();
  expect(harness.requests.filter((route) => route.startsWith("POST ") && /\/seen/.test(route))).toEqual([]);
});

test("a refused dismissal puts the row back with a notice", async ({ page }) => {
  harness.refuseNextReadMark = true;
  await page.goto("/sparade");
  await page.getByRole("button", { name: "Notiser, 2 olästa" }).click();
  const panel = page.getByRole("dialog", { name: "Notiser" });

  await panel.getByRole("button", { name: `Ta bort notisen: Bevakat företag, ${ADS.saved.title}, ${ADS.saved.company}` }).click();

  await expect(panel.getByRole("alert")).toContainText("Notisen kunde inte tas bort");
  await expect(panel.getByRole("link", { name: ADS.saved.title })).toBeVisible();
  await expect(page.getByRole("button", { name: "Notiser, 2 olästa" })).toBeVisible();
});
