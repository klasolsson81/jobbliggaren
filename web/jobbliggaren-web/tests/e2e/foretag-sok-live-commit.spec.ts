import { test, expect, type Page } from "@playwright/test";
import { loginAs, ensureConfirmedTestUser } from "./helpers/auth";

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";
const RUN_ID = Date.now();

/**
 * `/foretag/sok` live commit (#1125) — the two guarantees a real browser is the ONLY place to check.
 *
 * 1. A filter commit actually APPLIES. Next's client router cache collapses repeated query keys to
 *    the last value, so `?kommun=A&kommun=B` and `?kommun=B` share one cache entry: removing the
 *    FIRST of two chips navigates to a URL the cache believes it already holds, no RSC request is
 *    made, and the page never re-renders — the URL says one filter while the chips and the results
 *    below still show the other. Upstream vercel/next.js#92152 and its fix PR #93368 (both open on
 *    2026-07-29; we run 16.2.9). `commit()` works around it with `router.refresh()`.
 * 2. A second filter change in a row is ANNOUNCED. An axis-named announcement produces a
 *    byte-identical string, React bails out on `Object.is`, the DOM never mutates and `aria-live`
 *    never fires — every change after the first is silent to a screen reader (WCAG 4.1.3).
 *
 * jsdom can pin neither: it has no router cache, and `router.push` is a synchronous mock there, so
 * the `useOptimistic` overlay never outlives its transition. `foretag-sok-searchbar.test.tsx` pins
 * the CALL SITES (`refresh` is called; the announcement names the object) — this pins the EFFECTS.
 *
 * Lane note, stated rather than implied: `e2e.yml` is observe-only (`continue-on-error: true` and
 * outside the required `ci` aggregate), so a regression here is REPORTED, not blocked, until that
 * lane is ratcheted by an explicit decision.
 */

const ORT_TRIGGER = "Välj ort eller län";

test.beforeAll(async () => {
  await ensureConfirmedTestUser(BACKEND_URL, RUN_ID);
});

/** The applied kommun codes, in URL order. */
const kommunParams = (page: Page) =>
  new URL(page.url()).searchParams.getAll("kommun");

const chipLabels = (page: Page) =>
  page
    .locator("ul.jp-chiplist .jp-chip__label")
    .allTextContents()
    .then((xs) => xs.map((x) => x.trim()));

/**
 * Next's cache key, as PR #93368 describes it: repeated keys collapse to the last value, key order
 * preserved. Asserted rather than assumed, so that if the href builder ever stops producing the
 * colliding shape this test says so out loud instead of quietly measuring a transition that was
 * never at risk.
 */
function collapse(search: string): string {
  const out = new Map<string, string>();
  for (const [k, v] of new URLSearchParams(search)) out.set(k, v);
  return [...out].map(([k, v]) => `${k}=${v}`).join("&");
}

/** Open the ort cascade and tick `count` kommuner in the first län, returning their labels. */
async function pickKommuner(page: Page, count: number): Promise<string[]> {
  await page.getByRole("button", { name: ORT_TRIGGER }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();

  // The left column is a role="group" of plain buttons, one per län. Selected by POSITION, not by
  // name: the fixture in the unit suite calls its län "Stockholms län" while the real SCB asset
  // calls it "Stockholm", and a name-matched selector would pass there and time out here.
  const lan = dialog.getByRole("group").getByRole("button").first();
  await expect(
    lan,
    "the SCB reference must load — it ships as an embedded asset, so an empty popover is a real failure, not a missing fixture",
  ).toBeVisible();
  await lan.click();

  const picked: string[] = [];
  for (let i = 0; i < count; i++) {
    const box = dialog
      .getByRole("checkbox")
      .filter({ hasNotText: /^Hela / })
      .nth(i);
    picked.push(((await box.textContent()) ?? "").trim());
    await box.click();
    // The chip comes from the OPTIMISTIC overlay and is therefore instant; the URL only moves
    // when the navigation commits. Waiting on the chip alone would read a stale URL, which is
    // how the first version of this helper reported zero applied kommuner.
    await expect(page.locator("ul.jp-chiplist .jp-chip__label")).toHaveCount(i + 1);
    await expect
      .poll(() => new URL(page.url()).searchParams.getAll("kommun").length)
      .toBe(i + 1);
  }
  await page.keyboard.press("Escape");
  return picked;
}

test.describe("/foretag/sok — a filter commit applies", () => {
  test("removing the first of two chips re-renders the page, not just the URL", async ({
    page,
  }) => {
    await loginAs(page, RUN_ID);
    await page.goto("/foretag/sok");

    const picked = await pickKommuner(page, 2);
    expect(picked).toHaveLength(2);

    const before = kommunParams(page);
    expect(before).toHaveLength(2);

    // The transition under test must be the COLLIDING one, or this proves nothing.
    const currentSearch = new URL(page.url()).search;
    const targetSearch = `?kommun=${before[1]}`;
    expect(
      collapse(targetSearch),
      "this spec exists for the cache-key collision; if the href builder no longer produces it, re-derive the case rather than deleting the test",
    ).toBe(collapse(currentSearch));

    // Remove the FIRST chip.
    await page.locator("ul.jp-chiplist .jp-chip__remove").first().click();

    // The URL moves — polled, because it commits with the navigation while the chip row is
    // repainted instantly from the overlay.
    await expect.poll(() => kommunParams(page)).toEqual([before[1]]);
    // ...and so does the PAGE. With the defect the chips snap back to two and STAY there, so
    // this is the assertion that separates "the URL changed" from "the filter applied".
    await expect(page.locator("ul.jp-chiplist .jp-chip__label")).toHaveCount(1);
    expect(await chipLabels(page)).toEqual([picked[1]]);
  });
});

test.describe("/foretag/sok — a second filter change is announced", () => {
  test("two picks in a row put two DIFFERENT sentences in the live region", async ({
    page,
  }) => {
    await loginAs(page, RUN_ID);
    await page.goto("/foretag/sok");

    const region = page.locator("p[aria-live='polite'].sr-only");
    await expect(region).toHaveCount(1);
    // Present and EMPTY at first paint: a live region mounted with its content already in place is
    // not reliably announced.
    await expect(region).toHaveText("");

    await page.getByRole("button", { name: ORT_TRIGGER }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByRole("group").getByRole("button").first().click();

    const first = dialog.getByRole("checkbox").filter({ hasNotText: /^Hela / }).nth(0);
    const second = dialog.getByRole("checkbox").filter({ hasNotText: /^Hela / }).nth(1);
    const firstName = ((await first.textContent()) ?? "").trim();
    const secondName = ((await second.textContent()) ?? "").trim();

    await first.click();
    await expect(region).toHaveText(new RegExp(firstName));
    const firstText = await region.textContent();

    await second.click();
    await expect(region).toHaveText(new RegExp(secondName));
    const secondText = await region.textContent();

    // The assertion that matters: had the announcement named the AXIS, these two would be equal,
    // the DOM would never have mutated, and the second change would have been silent.
    expect(secondText).not.toBe(firstText);
  });
});
