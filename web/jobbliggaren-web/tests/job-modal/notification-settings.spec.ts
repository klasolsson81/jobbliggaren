import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { expect, test, type Page } from "@playwright/test";
import {
  APP_ORIGIN,
  DEFAULT_NOTIFICATION_SETTINGS,
  SESSION_COOKIE,
  SESSION_ID,
  startHarness,
  type Harness,
  type NotificationSettings,
} from "./servers";

/**
 * /mina-sidor/notiser in a real browser (#2107, ADR 0164): the e-mail switch comes first and is in view
 * on a phone, the consent switches stay usable while e-mail is off (security-auditor V2), the switches'
 * values survive a reload, a refused write reverts and leaves focus on the switch that caused it
 * (design-reviewer B2), and the card never scrolls sideways at phone widths.
 *
 * #2130: the two bell switches ("som notis i appen") sit beside the e-mail ones and answer to neither the
 * e-mail switch nor the e-mail types; the matches one exists only while the match consent is on.
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

const MAIL = "E-postnotiser";
const MATCH = "Matcha nya annonser åt mig";
const TOP = "Toppmatchningar via e-post";
const SUMMARY = "Sammanfattning via e-post";
const FOLLOW = "Nya annonser via e-post";
const PAUSED = "Skickas inte medan e-postnotiser är av.";
const CADENCE = "Hur ofta";
const FEED_MATCH = "Nya matchningar som notis i appen";
const FEED_FOLLOW = "Nya annonser som notis i appen";
const FEED_MATCH_ROUTE = "PUT /api/v1/me/notification-feed/matches";
const FEED_FOLLOW_ROUTE = "PUT /api/v1/me/notification-feed/followed-company-ads";

const switchNamed = (page: Page, name: string) => page.getByRole("switch", { name, exact: true });
const refusal = (page: Page) =>
  page.getByRole("alert").filter({ hasText: "Ändringen kunde inte sparas. Försök igen." });

async function open(page: Page, settings: Partial<NotificationSettings>) {
  harness.notificationSettings = { ...DEFAULT_NOTIFICATION_SETTINGS, ...settings };
  await page.goto("/mina-sidor/notiser");
  await expect(switchNamed(page, MAIL)).toBeVisible();
}

async function shot(page: Page, name: string) {
  const dir = process.env.VISUAL_SHOTS_DIR;
  if (!dir) return;
  mkdirSync(dir, { recursive: true });
  await page.screenshot({ path: join(dir, `${name}.png`), fullPage: true });
}

const STATES: ReadonlyArray<{
  name: string;
  settings: Partial<NotificationSettings>;
  mailTypeSwitches: 0 | 1;
  cadence: boolean;
  paused: boolean;
}> = [
  { name: "new-account", settings: {}, mailTypeSwitches: 0, cadence: false, paused: false },
  {
    name: "all-on",
    settings: { backgroundMatchNotificationsEnabled: true, followedCompanyNotificationsEnabled: true },
    mailTypeSwitches: 1,
    cadence: true,
    paused: false,
  },
  {
    name: "toppmatch-on-summary-off",
    settings: { backgroundMatchNotificationsEnabled: true, matchSummaryMailEnabled: false },
    mailTypeSwitches: 1,
    cadence: false,
    paused: false,
  },
  {
    name: "cadence-via-follow-only",
    settings: { followedCompanyNotificationsEnabled: true },
    mailTypeSwitches: 0,
    cadence: true,
    paused: false,
  },
  {
    name: "email-off-with-saved-choices",
    settings: {
      notificationMailEnabled: false,
      backgroundMatchNotificationsEnabled: true,
      followedCompanyNotificationsEnabled: true,
      matchSummaryMailEnabled: false,
    },
    mailTypeSwitches: 0,
    cadence: false,
    paused: true,
  },
];

for (const width of [1280, 375] as const) {
  for (const state of STATES) {
    test(`renders ${state.name} at ${width} px`, async ({ page }) => {
      await page.setViewportSize({ width, height: width === 375 ? 667 : 900 });
      await open(page, state.settings);

      await expect(switchNamed(page, TOP)).toHaveCount(state.mailTypeSwitches);
      await expect(switchNamed(page, SUMMARY)).toHaveCount(state.mailTypeSwitches);
      // The bell's matches switch follows the consent, never the e-mail switch; the follow one is always there.
      await expect(switchNamed(page, FEED_MATCH)).toHaveCount(
        state.settings.backgroundMatchNotificationsEnabled ? 1 : 0,
      );
      await expect(switchNamed(page, FEED_FOLLOW)).toHaveCount(1);
      await expect(page.getByRole("radiogroup", { name: CADENCE })).toHaveCount(state.cadence ? 1 : 0);
      await expect(page.getByText(PAUSED, { exact: true })).toHaveCount(state.paused ? 1 : 0);
      await shot(page, `${state.name}-${width}`);
    });
  }
}

test("the e-mail switch is the first control and is in view on a phone without scrolling", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 667 });
  await open(page, { backgroundMatchNotificationsEnabled: true, followedCompanyNotificationsEnabled: true });

  await expect(page.getByRole("switch").first()).toHaveAccessibleName(MAIL);
  expect(await page.evaluate(() => window.scrollY)).toBe(0);
  const box = await switchNamed(page, MAIL).boundingBox();
  expect(box).not.toBeNull();
  expect(box!.y + box!.height).toBeLessThanOrEqual(667);
});

for (const width of [320, 375] as const) {
  test(`the page does not scroll sideways at ${width} px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 800 });
    await open(page, { backgroundMatchNotificationsEnabled: true, followedCompanyNotificationsEnabled: true });

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);
  });
}

test("with e-mail off, both consent switches stay usable and the follow switch says it is not sent", async ({ page }) => {
  await open(page, {
    notificationMailEnabled: false,
    backgroundMatchNotificationsEnabled: true,
    followedCompanyNotificationsEnabled: true,
  });
  await shot(page, "email-off-consents-usable-1280");

  await expect(switchNamed(page, MATCH)).toBeEnabled();
  await expect(switchNamed(page, FOLLOW)).toBeEnabled();
  await expect(switchNamed(page, FOLLOW)).toHaveAccessibleDescription(new RegExp(`${PAUSED}$`));
  await expect(switchNamed(page, TOP)).toHaveCount(0);
  await expect(switchNamed(page, SUMMARY)).toHaveCount(0);

  await switchNamed(page, FOLLOW).click();
  await expect(switchNamed(page, FOLLOW)).toHaveAttribute("aria-checked", "false");
  await expect.poll(() => harness.notificationSettings.followedCompanyNotificationsEnabled).toBe(false);
  expect(harness.notificationSettings.notificationMailEnabled).toBe(false);
});

test("Toppmatch on with the summary off survives a reload", async ({ page }) => {
  await open(page, { backgroundMatchNotificationsEnabled: true });

  await switchNamed(page, SUMMARY).click();
  await expect(page.getByText(/^Sparat \d{2}:\d{2}$/)).toBeVisible();
  await page.reload();

  await expect(switchNamed(page, TOP)).toHaveAttribute("aria-checked", "true");
  await expect(switchNamed(page, SUMMARY)).toHaveAttribute("aria-checked", "false");
  expect(harness.settingsWrites).toEqual([
    { route: "PUT /api/v1/me/match-summary-mail", body: { enabled: false } },
  ]);
});

test("e-mail off and on again brings back the saved choices, across a reload", async ({ page }) => {
  await open(page, { backgroundMatchNotificationsEnabled: true, matchSummaryMailEnabled: false });

  await switchNamed(page, MAIL).click();
  await expect(switchNamed(page, TOP)).toHaveCount(0);
  await expect.poll(() => harness.notificationSettings.notificationMailEnabled).toBe(false);
  await page.reload();
  await expect(switchNamed(page, MAIL)).toHaveAttribute("aria-checked", "false");

  await switchNamed(page, MAIL).click();
  await expect(switchNamed(page, TOP)).toHaveAttribute("aria-checked", "true");
  await expect(switchNamed(page, SUMMARY)).toHaveAttribute("aria-checked", "false");
});

test("a refused save reverts the switch and says so under it", async ({ page }) => {
  await open(page, { backgroundMatchNotificationsEnabled: true });
  harness.refuseNextSettingsWrite = true;

  await switchNamed(page, TOP).click();

  await expect(refusal(page)).toBeVisible();
  await expect(switchNamed(page, TOP)).toHaveAttribute("aria-checked", "true");
  await shot(page, "refused-1280");
});

test("a refused e-mail-on that hides the focused switch leaves focus on the e-mail switch", async ({ page }) => {
  await open(page, { notificationMailEnabled: false, backgroundMatchNotificationsEnabled: true });
  harness.refuseNextSettingsWrite = true;
  harness.settingsWriteDelayMs = 1_500;

  await switchNamed(page, MAIL).click();
  await switchNamed(page, TOP).focus();
  await expect(switchNamed(page, TOP)).toBeFocused();

  await expect(refusal(page)).toBeVisible();
  await expect(switchNamed(page, TOP)).toHaveCount(0);
  await expect(switchNamed(page, MAIL)).toBeFocused();
});

test("a new account has both bell switches on and the matches one hidden until the consent is given", async ({ page }) => {
  await open(page, {});
  await expect(switchNamed(page, FEED_FOLLOW)).toHaveAttribute("aria-checked", "true");
  await expect(switchNamed(page, FEED_MATCH)).toHaveCount(0);

  await switchNamed(page, MATCH).click();

  await expect(switchNamed(page, FEED_MATCH)).toHaveAttribute("aria-checked", "true");
  await shot(page, "feed-matches-after-consent-1280");
});

test("the bell switches answer to neither the e-mail switch nor the e-mail types", async ({ page }) => {
  await open(page, {
    notificationMailEnabled: false,
    backgroundMatchNotificationsEnabled: true,
    followedCompanyNotificationsEnabled: true,
  });

  await expect(switchNamed(page, FEED_MATCH)).toBeEnabled();
  await expect(switchNamed(page, FEED_FOLLOW)).toBeEnabled();
  // They carry no "not sent while e-mail is off" line: they are not e-mail.
  await expect(switchNamed(page, FEED_FOLLOW)).not.toHaveAccessibleDescription(new RegExp(PAUSED));

  await switchNamed(page, MAIL).click();
  await expect(switchNamed(page, TOP)).toBeVisible();
  await expect(switchNamed(page, FEED_MATCH)).toHaveAttribute("aria-checked", "true");
  await expect(switchNamed(page, FEED_FOLLOW)).toHaveAttribute("aria-checked", "true");
  expect(harness.settingsWrites.map((write) => write.route)).toEqual(["PUT /api/v1/me/notification-mail"]);
});

test("a bell switch off survives a reload and writes only its own route", async ({ page }) => {
  await open(page, { backgroundMatchNotificationsEnabled: true });

  await switchNamed(page, FEED_FOLLOW).click();
  await expect(page.getByText(/^Sparat \d{2}:\d{2}$/)).toBeVisible();
  await page.reload();

  await expect(switchNamed(page, FEED_FOLLOW)).toHaveAttribute("aria-checked", "false");
  await expect(switchNamed(page, FEED_MATCH)).toHaveAttribute("aria-checked", "true");
  expect(harness.settingsWrites).toEqual([{ route: FEED_FOLLOW_ROUTE, body: { enabled: false } }]);

  await switchNamed(page, FEED_MATCH).click();
  await expect.poll(() => harness.notificationSettings.notificationFeedMatchesEnabled).toBe(false);
  await page.reload();
  await expect(switchNamed(page, FEED_MATCH)).toHaveAttribute("aria-checked", "false");
  await expect(switchNamed(page, FEED_FOLLOW)).toHaveAttribute("aria-checked", "false");
  expect(harness.settingsWrites.map((write) => write.route)).toEqual([FEED_FOLLOW_ROUTE, FEED_MATCH_ROUTE]);
});

test("withdrawing the match consent removes the bell's matches switch and keeps its value for the next consent", async ({ page }) => {
  await open(page, { backgroundMatchNotificationsEnabled: true, notificationFeedMatchesEnabled: false });
  await expect(switchNamed(page, FEED_MATCH)).toHaveAttribute("aria-checked", "false");

  await switchNamed(page, MATCH).click();
  await expect(switchNamed(page, FEED_MATCH)).toHaveCount(0);
  await switchNamed(page, MATCH).click();

  // The saved choice comes back as it was left, and nothing but the consent was written.
  await expect(switchNamed(page, FEED_MATCH)).toHaveAttribute("aria-checked", "false");
  expect(harness.notificationSettings.notificationFeedMatchesEnabled).toBe(false);
  expect(harness.settingsWrites.map((write) => write.route)).not.toContain(FEED_MATCH_ROUTE);
});

test("a refused bell switch reverts and says so under it", async ({ page }) => {
  await open(page, { backgroundMatchNotificationsEnabled: true });
  harness.refuseNextSettingsWrite = true;

  await switchNamed(page, FEED_FOLLOW).click();

  await expect(refusal(page)).toBeVisible();
  await expect(switchNamed(page, FEED_FOLLOW)).toHaveAttribute("aria-checked", "true");
  expect(harness.notificationSettings.notificationFeedFollowedAdsEnabled).toBe(true);
  await shot(page, "feed-refused-1280");
});

test("captures the card in the dark theme, with a background other than the light theme's", async ({ page }) => {
  await open(page, { backgroundMatchNotificationsEnabled: true, followedCompanyNotificationsEnabled: true });
  const card = page.locator("section.jp-card").filter({ has: switchNamed(page, MAIL) });
  const background = () => card.evaluate((element) => getComputedStyle(element).backgroundColor);
  const light = await background();

  await page.evaluate(() => {
    document.documentElement.dataset.theme = "dark";
  });

  await expect.poll(background).not.toBe(light);
  await shot(page, "dark-1280");
});
