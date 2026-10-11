import { expect, test } from "@playwright/test";
import { ADS } from "./fixtures";
import { APP_ORIGIN, QUIET_NOTIFICATION_FEED, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

/**
 * The header bell (#1666) in a real browser: the badge and the button's name carry the unread count,
 * a row opens its ad in the intercepted job modal, and "Markera alla som lästa" sends each source's
 * token, as the exact string the feed returned, to that source's existing /seen endpoint. Microsecond
 * tokens pin the string pass-through: a token re-serialised through a JS Date would lose them.
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
  const bell = page.getByRole("button", { name: "Aviseringar, 2 olästa" });
  await expect(bell).toBeVisible();
  await expect(page.locator(".jp-notif-badge")).toHaveText("2");

  await bell.click();
  const panel = page.getByRole("dialog", { name: "Aviseringar" });
  await expect(panel.getByText("2 olästa")).toBeVisible();
  await expect(panel.getByText("Toppmatch")).toBeVisible();
  await expect(panel.getByText("Bevakar")).toBeVisible();

  await panel.getByRole("link", { name: ADS.open.title }).click();
  await expect(page).toHaveURL(`/jobb/${ADS.open.id}`);
  await expect(page.getByRole("dialog", { name: ADS.open.title })).toContainText("Öppna annonsen");
  await expect(page.getByRole("dialog", { name: "Aviseringar" })).toHaveCount(0);
});

test("Markera alla som lästa sends each token unchanged to its own seen endpoint", async ({ page }) => {
  await page.goto("/sparade");
  await page.getByRole("button", { name: "Aviseringar, 2 olästa" }).click();
  const panel = page.getByRole("dialog", { name: "Aviseringar" });
  harness.notificationFeed = {
    ...QUIET_NOTIFICATION_FEED,
    items: UNREAD_FEED.items.map((item) => ({ ...item, isUnread: false })),
  };

  await panel.getByRole("button", { name: "Markera alla som lästa" }).click();

  await expect(panel.getByText("Inga olästa")).toBeVisible();
  expect(harness.seenReceipts).toEqual([
    { route: "POST /api/v1/me/matches/seen", body: { seenThrough: MATCH_TOKEN } },
    { route: "POST /api/v1/me/followed-company-ads/seen", body: { seenThrough: FOLLOW_TOKEN } },
  ]);
  await expect(page.locator(".jp-notif-badge")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Aviseringar" })).toBeVisible();
});

test("opening the bell writes nothing", async ({ page }) => {
  await page.goto("/sparade");
  await page.getByRole("button", { name: "Aviseringar, 2 olästa" }).click();
  await expect(page.getByRole("dialog", { name: "Aviseringar" }).getByText("2 olästa")).toBeVisible();

  expect(harness.seenReceipts).toEqual([]);
  expect(harness.requests.filter((route) => route.startsWith("POST ") && route.includes("/seen"))).toEqual([]);
});
