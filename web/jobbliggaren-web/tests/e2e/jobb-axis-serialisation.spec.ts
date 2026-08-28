import { test, expect, type Page } from "@playwright/test";
import { loginAs, ensureConfirmedTestUser } from "./helpers/auth";

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5049";
const RUN_ID = Date.now();

/**
 * `/jobb` axis serialisation — the guarantee only a real browser can check.
 *
 * Next's client router cache keys a route by its URL and collapses REPEATED query
 * keys to the last value, so under the old contract
 * `?employmentType=A&employmentType=B` and `?employmentType=B` were ONE cache
 * entry. Unticking a non-last value therefore navigated to a URL the cache
 * believed it already held: no RSC request, no re-render, and the filter panel
 * snapped back to a state the URL no longer described. Upstream
 * vercel/next.js#92152 and its fix PR #93368, both open on 2026-08-01 (we run
 * 16.2.11).
 *
 * Measured on this surface before the fix, against the running stack: of four
 * transitions, the two whose collapsed keys matched produced ZERO RSC
 * navigations and left every checkbox ticked while the URL carried one fewer.
 * After it: 4/4 navigate, and the panel matches the URL.
 *
 * **Why the gesture is the Klass-2 Filter panel and not a toolbar chip x.**
 * `removeChip` goes through the toolbar's `commit()`, which appends
 * `?commit=true`; that extra param makes the target differ from the cached entry,
 * so the collision cannot arise on that gesture at all. The colliding paths are
 * the ones that push BARE — every hero popover, and the toolbar's `navigate()`.
 * A spec driving the chip x would have passed on `main` and pinned nothing.
 *
 * jsdom cannot see any of this: it has no router cache, and `router.push` is a
 * synchronous mock there. `search-params.test.ts` pins that two applied states
 * cannot collapse to one key (with the counterfactual that the old form DID), and
 * `TaxonomyConceptIdGrammarTests` (in `Jobbliggaren.Application.UnitTests`) pins
 * that every shipped conceptId is accepted by the query validator a /jobb search
 * hits, whose charset excludes the separator. This file pins the EFFECT, in a real browser.
 *
 * Lane note, stated rather than implied: `e2e.yml` is observe-only
 * (`continue-on-error: true`, outside the required `ci` aggregate), so a
 * regression here is REPORTED, not blocked, until that lane is ratcheted by an
 * explicit decision.
 */

/**
 * Real JobTech conceptIds from the committed klass2 taxonomy, paired with the
 * label each one renders as. Invented ids would not resolve to a rendered option.
 *
 * The pairing is safe to hard-code precisely here: `klass2-taxonomy.json` is
 * FROZEN and hand-curated by construction (its own note — employment-type and
 * worktime-extent are flat, parentless, legally-stable sets, deliberately not
 * generated), unlike the regenerated snapshots. Since #1537 the rendered label
 * comes from `messages/sv/jobads.json` rather than from that file directly, and
 * `src/lib/i18n/coded-taxonomy.test.ts` holds the two byte-identical — so the
 * pairing above still holds, by that gate.
 *
 * The label is needed because the rendered rows carry no conceptId attribute, and
 * the panel renders in the collation order of the DISPLAYED name, NOT the order the
 * codes appear in the URL — so picking a row by index would silently exercise a
 * different transition than the one named. This spec runs under `sv`, where that
 * order is the same sequence the taxonomy ships (measured, `coded-taxonomy.test.ts`).
 */
const EMPLOYMENT_A = { id: "PFZr_Syz_cUq", label: "Vanlig anställning" };
const EMPLOYMENT_B = {
  id: "kpPX_CNN_gDU",
  label: "Tillsvidareanställning (inkl. eventuell provanställning)",
};
const EMPLOYMENT_C = { id: "1paU_aCR_nGn", label: "Behovsanställning" };
const SEPARATOR = ".";

test.beforeAll(async () => {
  await ensureConfirmedTestUser(BACKEND_URL, RUN_ID);
});

/**
 * Next's cache key, as fix PR #93368 describes it: repeated keys collapse to the
 * last value, first-appearance key order preserved.
 */
function collapse(search: string): string {
  const out = new Map<string, string>();
  for (const [k, v] of new URLSearchParams(search)) out.set(k, v);
  return [...out].map(([k, v]) => `${k}=${v}`).join("&");
}

/** The applied employmentType codes — parsed, so BOTH URL forms read alike. */
const employmentCodes = (page: Page) =>
  new URL(page.url())
    .searchParams.getAll("employmentType")
    .flatMap((v) => v.split(SEPARATOR))
    .filter((v) => v.length > 0);

const tickedLabels = (page: Page) =>
  page
    .getByRole("checkbox")
    .evaluateAll((els) =>
      els
        .filter((e) => e.getAttribute("aria-checked") === "true")
        .map((e) => (e.textContent ?? "").trim())
    );

async function openFilterPanel(page: Page): Promise<void> {
  await page
    .locator("button.jp-hero-pill")
    .filter({ hasText: /^Filter/ })
    .first()
    .click();
  await expect(page.getByRole("checkbox").first()).toBeVisible();
}

test.describe("/jobb — a filter removal applies", () => {
  test("unticking a non-last value re-renders the page, not just the URL", async ({
    page,
  }) => {
    await loginAs(page, RUN_ID);
    await page.goto("/jobb");
    await openFilterPanel(page);

    // Build the starting state THROUGH THE UI, one tick at a time, so the start
    // URL is whatever the app's own builder writes.
    //
    // This is load-bearing and was got wrong first: an earlier version handed the
    // start state to `page.goto` already in the joined form. That made the test
    // pass even when the builder was mutated back to the repeated form, because
    // the joined start and the repeated target never collapse alike — it pinned
    // nothing. The collision needs BOTH sides produced by the builder, which is
    // exactly what a real user's session produces. Verified by mutation: with
    // `setAxis` reverted to `params.append`, this test now fails.
    for (const option of [EMPLOYMENT_A, EMPLOYMENT_B, EMPLOYMENT_C]) {
      await page
        .getByRole("checkbox")
        .filter({ hasText: option.label })
        .first()
        .click();
      // The URL commits with the navigation while the panel repaints instantly
      // from the optimistic overlay, so waiting on the tick alone would read a
      // stale URL.
      await expect
        .poll(() => employmentCodes(page))
        .toContain(option.id);
    }

    const before = employmentCodes(page);
    expect(before).toHaveLength(3);
    expect(await tickedLabels(page)).toHaveLength(3);
    const beforeSearch = new URL(page.url()).search;

    // Untick the value that is FIRST in the URL — precisely the transition that
    // collided, because removing it leaves the LAST value unchanged.
    const firstInUrl = [EMPLOYMENT_A, EMPLOYMENT_B, EMPLOYMENT_C].find(
      (o) => o.id === before[0]
    )!;
    await page
      .getByRole("checkbox")
      .filter({ hasText: firstInUrl.label })
      .first()
      .click();

    await expect.poll(() => employmentCodes(page).length).toBe(2);
    const afterSearch = new URL(page.url()).search;

    // The property the URL contract delivers, asserted on the two states the APP
    // produced rather than on strings this test composed: the state we left and
    // the state we went to must not share a router cache key. Under the repeated
    // form these collapsed to the same key, the navigation was served from cache,
    // and the page never re-rendered.
    expect(
      collapse(afterSearch),
      "the two states share a router cache key — the axis serialisation has regressed to the repeated form"
    ).not.toBe(collapse(beforeSearch));

    // ...and the PAGE moved too. With the defect the panel stayed on three ticks
    // and STAYED there, so this is what separates "the URL changed" from "the
    // filter applied".
    await expect.poll(async () => (await tickedLabels(page)).length).toBe(2);
  });

  test("a link shared in the pre-2026-08-01 repeated form still applies both filters", async ({
    page,
  }) => {
    await loginAs(page, RUN_ID);
    // Back-compat: the parser accepts both shapes, so no redirect and no
    // migration are needed and every previously shared link keeps working.
    await page.goto(
      `/jobb?employmentType=${EMPLOYMENT_A.id}&employmentType=${EMPLOYMENT_B.id}`
    );
    await openFilterPanel(page);

    expect(employmentCodes(page)).toEqual([EMPLOYMENT_A.id, EMPLOYMENT_B.id]);
    expect(await tickedLabels(page)).toHaveLength(2);
  });
});
