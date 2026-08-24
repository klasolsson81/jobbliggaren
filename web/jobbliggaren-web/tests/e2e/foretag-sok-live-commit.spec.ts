import { test, expect, type Page } from "@playwright/test";
import { loginAs, ensureConfirmedTestUser } from "./helpers/auth";

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";
const RUN_ID = Date.now();

/**
 * `/foretag/sok` live commit (#1125) — the two guarantees a real browser is the ONLY place to check.
 *
 * 1. A filter commit actually APPLIES. Next's client router cache keys a route by its URL and
 *    collapses REPEATED query keys to the last value, so under the old URL contract
 *    `?kommun=A&kommun=B` and `?kommun=B` shared one cache entry: removing the FIRST of two chips
 *    navigated to a URL the cache believed it already held, no RSC request was made, and the page
 *    never re-rendered. Upstream vercel/next.js#92152 and its fix PR #93368 (both open on
 *    2026-07-29; we run 16.2.9). The collision is removed STRUCTURALLY — each axis is now ONE param
 *    with its codes joined (`lib/company-search/search-params.ts`), so two applied states cannot
 *    share a cache key. A `router.refresh()` workaround was tried first and measured WRONG: correct
 *    for one removal, but two or more in quick succession were undone entirely once server latency
 *    passed ~600 ms, which is inside the range this surface already measures.
 * 2. A second filter change in a row is ANNOUNCED. An axis-named announcement produces a
 *    byte-identical string, React bails out on `Object.is`, the DOM never mutates and `aria-live`
 *    never fires — every change after the first is silent to a screen reader (WCAG 4.1.3).
 *
 * jsdom can pin neither: it has no router cache, and `router.push` is a synchronous mock there, so
 * the `useOptimistic` overlay never outlives its transition. `search-params.test.ts` pins that two
 * applied states cannot collapse to one cache key, and `foretag-sok-searchbar.test.tsx` pins that
 * the announcement names the object — this file pins the EFFECTS of both, in a real browser.
 *
 * Lane note, stated rather than implied: `e2e.yml` is observe-only (`continue-on-error: true` and
 * outside the required `ci` aggregate), so a regression here is REPORTED, not blocked, until that
 * lane is ratcheted by an explicit decision.
 */

const ORT_TRIGGER = "Välj ort eller län";

test.beforeAll(async () => {
  await ensureConfirmedTestUser(BACKEND_URL, RUN_ID);
});

/**
 * The applied kommun CODES — parsed, not read off the params.
 *
 * Since #1134 the axis is ONE param whose codes are joined, so `getAll("kommun")` returns a single
 * element and counting params counts one. Reading params instead of codes is what made the first
 * version of this spec unrunnable after the rebase: its docblock was rewritten to the joined
 * contract and its body was left on the repeated one. Both forms are accepted here for the same
 * reason `parseCodeAxis` accepts both — a legacy link is still a valid way to arrive.
 */
const kommunCodes = (page: Page) =>
  new URL(page.url())
    .searchParams.getAll("kommun")
    .flatMap((v) => v.split("-"))
    .filter((v) => v.length > 0);

const chipLabels = (page: Page) =>
  page
    .locator("ul.jp-chiplist .jp-chip__label")
    .allTextContents()
    .then((xs) => xs.map((x) => x.trim()));

/**
 * Next's cache key, as PR #93368 describes it: repeated keys collapse to the last value, key order
 * preserved.
 *
 * Its role here INVERTED at #1134. It used to assert that the transition under test still collides,
 * because back then it did and the spec existed to prove the workaround repaired it. Under
 * one-param-per-axis there is no collision left to confirm, so asserting one would fail — and
 * deleting the oracle would leave the spec unable to say WHY the removal is expected to work. It now
 * asserts the property the URL contract delivers: the two states have DIFFERENT cache keys.
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
    await expect.poll(() => kommunCodes(page).length).toBe(i + 1);
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

    const before = kommunCodes(page);
    expect(before).toHaveLength(2);

    // The URL contract's property, asserted where it matters rather than assumed from the unit
    // suite: the state we are LEAVING and the state we are going TO must have different cache
    // keys. Under the old repeated form these two were equal, the navigation was served from the
    // cache and the page never re-rendered — that is the defect this whole spec exists for.
    const currentSearch = new URL(page.url()).search;
    const targetSearch = `?kommun=${before[1]}`;
    expect(
      collapse(targetSearch),
      "the two states must not share a router cache key — if they do, the axis serialisation has regressed to the repeated form",
    ).not.toBe(collapse(currentSearch));

    // Remove the FIRST chip.
    await page.locator("ul.jp-chiplist .jp-chip__remove").first().click();

    // The URL moves — polled, because it commits with the navigation while the chip row is
    // repainted instantly from the overlay.
    await expect.poll(() => kommunCodes(page)).toEqual([before[1]]);
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

    // Scoped to the FILTER region, which is what this spec is about. Since #1092 the surface also
    // carries the load-cycle region from `ForetagSokAnnouncer`, and it matches the same tag, role
    // and class — the unscoped locator resolved to two elements, failing `toHaveCount(1)` and
    // turning every later `region` call into a strict-mode violation. `aria-atomic` is the
    // distinguishing attribute: the announcer swaps whole sentences and sets it, this one does not.
    const region = page.locator("p[aria-live='polite'].sr-only:not([aria-atomic])");
    await expect(region).toHaveCount(1);
    // Positive control on the split: if the two regions are ever merged or the announcer loses its
    // attribute, this drops to 0 and the assertion above starts passing for the wrong element.
    await expect(
      page.locator("p[aria-live='polite'].sr-only[aria-atomic='true']"),
    ).toHaveCount(1);
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
