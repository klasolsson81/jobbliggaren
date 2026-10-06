import { expect, test } from "@playwright/test";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

/**
 * Where the line a page shows after it reloaded itself into a new build lands (#1988, ADR 0148 D7), in a
 * real browser. A reload restores the scroll position at the first layout after the load, anchored to the
 * content, so a line inserted before then pushes the anchor down and the page lands scrolled by the line's
 * height, under the sticky header. Here the load is held back by one image until the notice has read its
 * stamp — the order a subresource that finishes after hydration produces — and then released.
 */

// The two keys `stampAndReload` writes (src/lib/stale-build/stale-build-reload.ts), as the product writes them.
const RELOAD_KEY = "jp-stale-build-reload-at";
const NOTICE_KEY = "jp-stale-build-reloaded-at";
const HOLD_PATH = "/__reload-notice-hold.gif";
const HOLD_FLAG = "reload-notice-hold";
const GIF = Buffer.from("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7", "base64");

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
  expect(harness.misses, "the pages read a backend route the harness does not answer").toEqual([]);
});

for (const path of ["/sparade", "/matchningar"]) {
  test(`${path}: a line read before the load lands at the top of the content, below the header`, async ({ page }) => {
    let release: () => void = () => {};
    const released = new Promise<void>((resolve) => {
      release = resolve;
    });
    await page.route(`**${HOLD_PATH}*`, async (route) => {
      await released;
      await route.fulfill({ status: 200, contentType: "image/gif", body: GIF });
    });
    // Only the reload carries the flag, so the first visit loads normally.
    await page.addInitScript(
      ({ holdPath, holdFlag }) => {
        if (sessionStorage.getItem(holdFlag) !== "1") return;
        sessionStorage.removeItem(holdFlag);
        const image = new Image();
        image.src = `${holdPath}?${Date.now()}`;
        Object.assign(window, { reloadNoticeHold: image });
      },
      { holdPath: HOLD_PATH, holdFlag: HOLD_FLAG },
    );

    await page.goto(path);
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    await page.waitForLoadState("networkidle");

    const loaded = page.waitForEvent("load");
    await page
      .evaluate(
        ({ reloadKey, noticeKey, holdFlag }) => {
          sessionStorage.setItem(holdFlag, "1");
          const now = String(Date.now());
          sessionStorage.setItem(reloadKey, now);
          sessionStorage.setItem(noticeKey, now);
          location.reload();
        },
        { reloadKey: RELOAD_KEY, noticeKey: NOTICE_KEY, holdFlag: HOLD_FLAG },
      )
      .catch(() => {});

    // The premise: the notice has read its stamp while the document is still loading.
    await page.waitForFunction((noticeKey) => sessionStorage.getItem(noticeKey) === null, NOTICE_KEY, { polling: 25 });
    expect(await page.evaluate(() => document.readyState)).toBe("interactive");

    release();
    await loaded;
    await expect(page.locator(".jp-banner")).toBeVisible();
    await page.evaluate(() => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(resolve, 300)))));

    const placed = await page.evaluate(() => {
      const line = document.querySelector(".jp-banner")?.getBoundingClientRect();
      const header = document.querySelector(".jp-header")?.getBoundingClientRect();
      if (!line || !header) return null;
      const hit = document.elementFromPoint(line.left + 12, line.top + line.height / 2);
      return { scrollY: Math.round(window.scrollY), belowHeader: line.top >= header.bottom, hitsLine: Boolean(hit?.closest(".jp-banner")) };
    });
    expect(placed).toEqual({ scrollY: 0, belowHeader: true, hitsLine: true });
  });
}
