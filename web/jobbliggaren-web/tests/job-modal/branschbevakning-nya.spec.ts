import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { expect, test, type Locator, type Page } from "@playwright/test";
import {
  CRITERIA,
  FOLLOWED_COMPANY,
  NYA_IDS,
  NYA_TOKENS,
  criterionListRow,
  type NyaState,
} from "./fixtures";
import { APP_ORIGIN, SESSION_COOKIE, SESSION_ID, startHarness, type Harness } from "./servers";

/**
 * A Branschbevakning's "N nya" (#1663 PR 2, ADR 0175 D7) in a real browser: the outline pill on Översikt, in both
 * card shapes, and `/foretag/branschbevakningar/[id]/nya` in every state its DTO can be in. The page is the
 * criterion watermark's only writer, so the load-bearing assertion is the seen write: exactly one, carrying the
 * server's token verbatim, in each state that shows rows, and none in any other — and none from Översikt, whose
 * pill is never prefetched.
 *
 * The colour figures are design-reviewer's form read of 2026-10-18 (D-1): `--jp-surface` / `--jp-info` in both
 * themes. With VISUAL_SHOTS_DIR set, every state is also saved as a screenshot, and the measurements the design
 * verdict needs beside them as `measurements.json`.
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
  expect(harness.misses, "the pages read a backend route the harness does not answer").toEqual([]);
});

/** A measurement for the design verdict, merged into VISUAL_SHOTS_DIR/measurements.json at once: a failed test restarts its worker. */
function record(key: string, value: unknown) {
  const dir = process.env.VISUAL_SHOTS_DIR;
  if (!dir) return;
  mkdirSync(dir, { recursive: true });
  const file = join(dir, "measurements.json");
  const all = existsSync(file) ? (JSON.parse(readFileSync(file, "utf8")) as Record<string, unknown>) : {};
  writeFileSync(file, JSON.stringify({ ...all, [key]: value }, null, 2));
}

/**
 * The route's streamed boundary has been revealed. React reveals it on a timer after `load`, and the hidden `S:`
 * container briefly outlives the fallback, so for that window every node a locator names exists twice: once in
 * place and once hidden.
 */
async function settled(page: Page) {
  await expect(page.locator('[role="status"][aria-busy="true"]')).toHaveCount(0);
  await expect(page.locator(".jp-skeleton")).toHaveCount(0);
  await expect(page.locator('div[hidden][id^="S:"]')).toHaveCount(0);
}

async function shot(page: Page, name: string) {
  const dir = process.env.VISUAL_SHOTS_DIR;
  if (!dir) return;
  mkdirSync(dir, { recursive: true });
  await page.screenshot({ path: join(dir, `${name}.png`), fullPage: true });
}

async function forceDark(page: Page) {
  await page.evaluate(() => document.documentElement.setAttribute("data-theme", "dark"));
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  // The pill's background has a 90 ms transition; read it after the transition, not inside it.
  await page.waitForTimeout(300);
}

const overflowX = (page: Page) =>
  page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

const paint = (locator: Locator) =>
  locator.evaluate((el) => {
    const style = getComputedStyle(el);
    return { background: style.backgroundColor, color: style.color, border: style.borderTopColor };
  });

const LIGHT = { background: "rgb(255, 255, 255)", color: "rgb(27, 83, 150)", border: "rgb(27, 83, 150)" };
const DARK = { background: "rgb(27, 43, 71)", color: "rgb(143, 190, 239)", border: "rgb(143, 190, 239)" };

// ── Översikt ──────────────────────────────────────────────────────────────────────────────────────

const criteriaCard = (page: Page) =>
  page.locator("section.jp-ov-card", { has: page.locator("#oversikt-card-criteria") });
const companiesCard = (page: Page) =>
  page.locator("section.jp-ov-card", { has: page.locator("#oversikt-card-companies") });
const nyaHref = (id: string) => `/foretag/branschbevakningar/${id}/nya`;

/** One watch, its last scan Scanned with `newAds` new — or refused, so the count is null. */
function oneCriterion(newAds: number | null, refused = false, criterion: (typeof CRITERIA)[keyof typeof CRITERIA] = CRITERIA.it) {
  harness.criteria = [criterionListRow(criterion, refused)];
  harness.criterionNewAdCounts = { status: 200, body: { items: [{ criterionId: criterion.id, newAds }] } };
}

/** Three watches: 3 new, 0 new, and one refused as too broad (null), newest first as the list handler orders them. */
function threeCriteria(first: (typeof CRITERIA)[keyof typeof CRITERIA] = CRITERIA.it) {
  harness.criteria = [criterionListRow(first), criterionListRow(CRITERIA.hosting), criterionListRow(CRITERIA.broad, true)];
  harness.criterionNewAdCounts = {
    status: 200,
    body: {
      items: [
        { criterionId: first.id, newAds: 3 },
        { criterionId: CRITERIA.hosting.id, newAds: 0 },
        { criterionId: CRITERIA.broad.id, newAds: null },
      ],
    },
  };
}

async function openOversikt(page: Page) {
  await page.goto("/oversikt");
  await settled(page);
  await expect(criteriaCard(page)).toBeVisible();
}

const PILL_STATES = [
  { count: 1, text: /^1 ny$/, name: /^1 ny annons i branschbevakningen IT-konsulter i Göteborg$/ },
  { count: 3, text: /^3 nya$/, name: /^3 nya annonser i branschbevakningen IT-konsulter i Göteborg$/ },
  { count: 1234, text: /^1\s234 nya$/, name: /^1\s234 nya annonser i branschbevakningen IT-konsulter i Göteborg$/ },
] as const;

for (const state of PILL_STATES) {
  test(`one watch with ${state.count} new: the outline pill in the card's head names the watch and links to its /nya`, async ({ page }) => {
    oneCriterion(state.count);
    await page.setViewportSize({ width: 1280, height: 900 });
    await openOversikt(page);

    const pill = criteriaCard(page).locator(".jp-ov-card__head a.jp-ov-card__pill");
    await expect(pill).toHaveCount(1);
    await expect(pill).toHaveText(state.text);
    await expect(pill).toHaveAccessibleName(state.name);
    await expect(pill).toHaveAttribute("href", nyaHref(CRITERIA.it.id));
    expect(await paint(pill)).toEqual(LIGHT);
    expect((await pill.boundingBox())?.height).toBe(24);
    record(`oversikt.one.N${state.count}.text`, await pill.evaluate((el) =>
      [...(el.textContent ?? "")].map((ch) => (ch.charCodeAt(0) > 127 ? `U+${ch.charCodeAt(0).toString(16).toUpperCase().padStart(4, "0")}` : ch)).join(""),
    ));
    await shot(page, `oversikt-one-N${state.count}-1280-light`);
  });
}

const NO_PILL = [
  { name: "a counted 0", setup: () => oneCriterion(0), shotName: "oversikt-one-N0-1280-light" },
  { name: "a null count (refused as too broad)", setup: () => oneCriterion(null, true), shotName: "oversikt-one-null-toobroad-1280-light" },
  {
    name: "a failed count read",
    setup: () => {
      oneCriterion(3);
      harness.criterionNewAdCounts = { status: 500, body: { title: "Internal Server Error", status: 500 } };
    },
    shotName: "oversikt-one-countread-500-1280-light",
  },
] as const;

for (const state of NO_PILL) {
  test(`one watch with ${state.name}: the card renders and carries no pill`, async ({ page }) => {
    state.setup();
    await page.setViewportSize({ width: 1280, height: 900 });
    await openOversikt(page);
    await expect(criteriaCard(page).getByText(CRITERIA.it.label)).toBeVisible();
    await expect(criteriaCard(page).locator(".jp-ov-card__pill")).toHaveCount(0);
    await shot(page, state.shotName);
  });
}

test("three watches: one pill on the row that has new ads, after its name, and none in the wide card's head", async ({ page }) => {
  threeCriteria();
  await page.setViewportSize({ width: 1280, height: 900 });
  await openOversikt(page);

  const card = criteriaCard(page);
  await expect(card.locator(".jp-ov-card__head")).toContainText("3 branschbevakningar");
  await expect(card.locator(".jp-ov-card__head .jp-ov-card__pill")).toHaveCount(0);
  await expect(card.getByText("En bevakning som matchar fler företag än vi kan räkna annonser för får inga tal här.")).toBeVisible();

  const rows = card.locator("li.jp-ov-criteria__row");
  await expect(rows).toHaveCount(3);
  await expect(card.locator(".jp-ov-card__pill")).toHaveCount(1);
  const pill = rows.nth(0).locator(".jp-ov-criteria__nameline > a.jp-ov-card__pill");
  await expect(pill).toHaveText("3 nya");
  await expect(pill).toHaveAccessibleName("3 nya annonser i branschbevakningen IT-konsulter i Göteborg");
  await expect(pill).toHaveAttribute("href", nyaHref(CRITERIA.it.id));
  expect(await paint(pill)).toEqual(LIGHT);
  expect((await pill.boundingBox())?.height).toBe(24);
  for (const row of [1, 2]) await expect(rows.nth(row).locator(".jp-ov-card__pill")).toHaveCount(0);

  // DOM order: the name, then the pill.
  const order = await rows.nth(0).locator(".jp-ov-criteria__nameline").evaluate((el) =>
    [...el.children].map((child) => child.className),
  );
  expect(order).toEqual(["jp-ov-criteria__name", "jp-ov-card__pill tabular-nums"]);
  await shot(page, "oversikt-three-3-0-null-1280-light");
});

test("Översikt neither prefetches /nya nor writes a seen mark, also when the pill is hovered", async ({ page }) => {
  threeCriteria();
  await openOversikt(page);
  const pill = criteriaCard(page).locator(".jp-ov-card__pill");
  await pill.hover();
  await page.waitForLoadState("networkidle");
  await page.waitForTimeout(1_000);

  expect(harness.requests.filter((route) => route.includes("/new-ads"))).toEqual([]);
  expect(harness.seenReceipts).toEqual([]);
});

test("the follow pill beside it keeps its own fill, and the criterion pill stays the outline", async ({ page }) => {
  oneCriterion(3);
  harness.companyWatches = [FOLLOWED_COMPANY];
  harness.followedCompanyAdNewCount = 2;
  await page.setViewportSize({ width: 1280, height: 900 });
  await openOversikt(page);

  const followPill = companiesCard(page).locator(".jp-ov-card__head a.jp-ov-card__pill");
  const criterionPill = criteriaCard(page).locator(".jp-ov-card__head a.jp-ov-card__pill");
  await expect(followPill).toHaveText("2 nya");
  await expect(followPill).toHaveAttribute("href", "/foretag/bevakade/nya");
  expect(await paint(criterionPill)).toEqual(LIGHT);
  record("oversikt.followPill.rest.light", await paint(followPill));
  await shot(page, "oversikt-follow-pill-beside-1280-light");

  const followRest = await paint(followPill);
  await followPill.hover();
  await page.waitForTimeout(300);
  const followHovered = await paint(followPill);
  record("oversikt.followPill.hover.light", followHovered);
  expect(followHovered.color).toBe(followRest.color);
  await shot(page, "oversikt-follow-pill-hover-1280-light");
  await criterionPill.hover();
  await page.waitForTimeout(300);
  const hovered = await paint(criterionPill);
  record("oversikt.criterionPill.hover.light", hovered);
  expect(hovered.color).toBe(LIGHT.color);
  expect(hovered.background).not.toBe(LIGHT.background);
  await shot(page, "oversikt-criterion-pill-hover-1280-light");

  await forceDark(page);
  await page.mouse.move(0, 0);
  await page.waitForTimeout(300);
  const followRestDark = await paint(followPill);
  record("oversikt.followPill.rest.dark", followRestDark);
  await followPill.hover();
  await page.waitForTimeout(300);
  const followHoveredDark = await paint(followPill);
  record("oversikt.followPill.hover.dark", followHoveredDark);
  expect(followHoveredDark.color).toBe(followRestDark.color);
  await criterionPill.hover();
  await page.waitForTimeout(300);
  const hoveredDark = await paint(criterionPill);
  record("oversikt.criterionPill.hover.dark", hoveredDark);
  expect(hoveredDark.color).toBe(DARK.color);
  expect(hoveredDark.background).not.toBe(DARK.background);
});

for (const shape of ["one", "three"] as const) {
  test(`the ${shape === "one" ? "one-watch" : "wide"} card's pill shows a focus ring in both themes`, async ({ page }) => {
    if (shape === "one") oneCriterion(3);
    else threeCriteria();
    await page.setViewportSize({ width: 1280, height: 900 });
    await openOversikt(page);
    const pill = criteriaCard(page).locator("a.jp-ov-card__pill");

    for (const theme of ["light", "dark"] as const) {
      if (theme === "dark") await forceDark(page);
      await pill.focus();
      const ring = await pill.evaluate((el) => {
        const style = getComputedStyle(el);
        return {
          focusVisible: el.matches(":focus-visible"),
          outline: `${style.outlineStyle} ${style.outlineWidth} ${style.outlineColor}`,
          offset: style.outlineOffset,
        };
      });
      record(`oversikt.${shape}.focus.${theme}`, ring);
      expect(ring.focusVisible).toBe(true);
      expect(ring.outline).toMatch(/^solid 2px rgb/);
      await shot(page, `oversikt-${shape}-pill-focus-1280-${theme}`);
    }
  });
}

// DESIGN.md §5: 44px under 768, and nothing scrolls sideways at any width.
for (const width of [1024, 1023, 769, 768, 375, 320]) {
  for (const shape of ["one", "three"] as const) {
    test(`at ${width} px the ${shape === "one" ? "one-watch" : "wide"} card's pill is ${width <= 768 ? 44 : 24} px high and nothing scrolls sideways`, async ({ page }) => {
      if (shape === "one") oneCriterion(3);
      else threeCriteria();
      await page.setViewportSize({ width, height: 900 });
      await openOversikt(page);

      const pill = criteriaCard(page).locator("a.jp-ov-card__pill");
      await expect(pill).toHaveCount(1);
      const height = (await pill.boundingBox())?.height;
      record(`oversikt.${shape}.pillHeight.${width}`, height);
      expect(height).toBe(width <= 768 ? 44 : 24);

      const card = await criteriaCard(page).evaluate((el) => ({ scroll: el.scrollWidth, client: el.clientWidth }));
      const doc = await page.evaluate(() => ({
        scroll: document.documentElement.scrollWidth,
        client: document.documentElement.clientWidth,
      }));
      record(`oversikt.${shape}.overflow.${width}`, { document: doc, card });
      expect(doc.scroll).toBeLessThanOrEqual(doc.client);
      expect(card.scroll).toBeLessThanOrEqual(card.client);
      await shot(page, `oversikt-${shape}-pill-${width}-light`);
    });
  }
}

for (const width of [1280, 375]) {
  for (const shape of ["one", "three"] as const) {
    test(`forced dark at ${width} px: the ${shape === "one" ? "one-watch" : "wide"} card's pill is the dark outline`, async ({ page }) => {
      if (shape === "one") oneCriterion(3);
      else threeCriteria();
      await page.setViewportSize({ width, height: 900 });
      await openOversikt(page);
      await forceDark(page);

      const pill = criteriaCard(page).locator("a.jp-ov-card__pill");
      const colours = await paint(pill);
      record(`oversikt.${shape}.paint.dark.${width}`, colours);
      expect(colours).toEqual(DARK);
      expect((await pill.boundingBox())?.height).toBe(width <= 768 ? 44 : 24);
      expect(await overflowX(page)).toBeLessThanOrEqual(0);
      await shot(page, `oversikt-${shape}-pill-${width}-dark`);
    });
  }
}

for (const shape of ["one", "three"] as const) {
  for (const width of [1280, 320]) {
    test(`a 120-character label with a pill in the ${shape === "one" ? "one-watch" : "wide"} card at ${width} px`, async ({ page }) => {
      expect(CRITERIA.longLabel.label).toHaveLength(120);
      if (shape === "one") oneCriterion(3, false, CRITERIA.longLabel);
      else threeCriteria(CRITERIA.longLabel);
      await page.setViewportSize({ width, height: 900 });
      await openOversikt(page);

      const pill = criteriaCard(page).locator("a.jp-ov-card__pill");
      await expect(pill).toHaveAccessibleName(`3 nya annonser i branschbevakningen ${CRITERIA.longLabel.label}`);
      await expect(criteriaCard(page).getByText(CRITERIA.longLabel.label)).toBeVisible();
      const card = await criteriaCard(page).evaluate((el) => el.scrollWidth - el.clientWidth);
      expect(card).toBeLessThanOrEqual(0);
      expect(await overflowX(page)).toBeLessThanOrEqual(0);
      await shot(page, `oversikt-${shape}-label120-${width}-light`);
    });
  }
}

// ── /foretag/branschbevakningar/[id]/nya ─────────────────────────────────────────────────────────

const SOURCE = "Källa: annonserna från Arbetsförmedlingens Platsbanken, urvalet från SCB:s företagsregister. Egen bearbetning.";
const CONSUMPTION = "Listan visar det som var nytt när du öppnade sidan.";
const ACKNOWLEDGED = "Nästa gång visas bara det som tillkommit sedan dess.";
const SEAT = "Urvalet utgår från företagets registrerade säteskommun. Annonsens arbetsort kan vara en annan.";
const TRUNCATED = "Det finns fler nya annonser än de som visas. De visas nästa gång du öppnar sidan.";
const NOT_STATED = "Du har inte angett vilka yrken du söker inom.";
// The derivation lists kommuner in the reference tree's order: Stockholms län (01) before Västra Götaland (14).
const LONG_HEADING = "Dataprogrammering, datakonsultverksamhet o.d. m.fl. · Stockholm m.fl.";

const seenRoute = (state: NyaState) => `POST /api/v1/me/company-watch-criteria/${NYA_IDS[state]}/new-ads/seen`;
const newAdsReads = (state: NyaState) =>
  harness.requests.filter((route) => route === `GET /api/v1/me/company-watch-criteria/${NYA_IDS[state]}/new-ads`).length;

async function openNya(page: Page, state: NyaState) {
  await page.goto(`/foretag/branschbevakningar/${NYA_IDS[state]}/nya`);
  await settled(page);
  await expect(page.locator("main h1, main [role=alert]").first()).toBeVisible();
}

/** The binding every /nya state keeps: no follow-axis nav, no follow-axis words. */
async function expectNoFollowAxis(page: Page) {
  await expect(page.locator(".jp-subnav")).toHaveCount(0);
  await expect(page.locator("main")).not.toContainText(/Bevakar|bevakade företag/i);
}

/** No seen write: given time to land after the response, none did. */
async function expectNoSeenWrite(page: Page) {
  await page.waitForLoadState("networkidle");
  await page.waitForTimeout(1_500);
  expect(harness.seenReceipts).toEqual([]);
}

async function expectOneSeenWrite(page: Page, state: keyof typeof NYA_TOKENS) {
  await expect.poll(() => harness.seenReceipts.length).toBe(1);
  await page.waitForTimeout(1_000);
  expect(harness.seenReceipts).toEqual([{ route: seenRoute(state), body: { seenThrough: NYA_TOKENS[state] } }]);
}

const ROW_STATES = [
  { state: "assessed", heading: LONG_HEADING, rows: 5, summary: "5 nya annonser, varav 2 matchar dig.", assessed: true, truncated: false },
  { state: "noneMatching", heading: "Backend i Göteborg", rows: 3, summary: "3 nya annonser, varav 0 matchar dig.", assessed: true, truncated: false },
  { state: "notAssessed", heading: "Konsulter i Härryda", rows: 4, summary: "4 nya annonser.", assessed: false, truncated: false },
  { state: "truncatedAssessed", heading: "Programmering i Stockholm", rows: 100, summary: "Visar 100 nya annonser, varav 7 matchar dig.", assessed: true, truncated: true },
  { state: "truncatedNotAssessed", heading: "Programmering i Göteborg", rows: 100, summary: "Visar 100 nya annonser.", assessed: false, truncated: true },
] as const;

for (const row of ROW_STATES) {
  test(`/nya ${row.state}: the rows as h2 cards under one h1, its notes, and exactly one seen write with the token verbatim`, async ({ page }) => {
    // The profile says what the window's grading says: no stated occupation, no assessment.
    harness.occupationStated = row.assessed;
    await page.setViewportSize({ width: 1280, height: 900 });
    await openNya(page, row.state);

    await expect(page.locator("h1")).toHaveCount(1);
    await expect(page.locator("main h1")).toHaveText(row.heading);
    await expect(page.locator("main .jp-job__title")).toHaveCount(row.rows);
    await expect(page.locator("main h2.jp-job__title")).toHaveCount(row.rows);
    await expect(page.locator("main h2")).toHaveCount(row.rows);
    await expect(page.locator("main .jp-matchline").first()).toHaveText(row.summary);
    const consumption = page.locator("main .jp-transparency-note", { hasText: CONSUMPTION });
    if (row.truncated) await expect(consumption).not.toContainText(ACKNOWLEDGED);
    else await expect(consumption).toContainText(ACKNOWLEDGED);
    await expect(page.getByText(SEAT)).toBeVisible();
    await expect(page.getByRole("button", { name: "Hur är annonserna utvalda?" })).toBeVisible();
    await expect(page.getByText(NOT_STATED)).toHaveCount(row.assessed ? 0 : 1);
    await expect(page.getByRole("radiogroup", { name: "Filtrera nya annonser" })).toHaveCount(row.assessed ? 1 : 0);
    await expect(page.getByText(TRUNCATED)).toHaveCount(row.truncated ? 1 : 0);
    await expect(page.locator("main p", { hasText: SOURCE })).toHaveText(SOURCE);
    await expect(page.getByRole("link", { name: "Tillbaka till bevakningen" })).toHaveAttribute("href", `/foretag/branschbevakningar/${NYA_IDS[row.state]}`);
    await expectNoFollowAxis(page);

    await expectOneSeenWrite(page, row.state);
    record(`nya.${row.state}.newAdsReadsPerView`, newAdsReads(row.state));
    record(`nya.${row.state}.headings`, await page.evaluate(() => ({
      h1: document.querySelectorAll("h1").length,
      h2: document.querySelectorAll("h2").length,
    })));
    record(`nya.${row.state}.seenReceipts`, [...harness.seenReceipts]);
    await shot(page, `nya-${row.state}-1280-light`);
  });
}

for (const state of ["assessed", "noneMatching"] as const) {
  test(`/nya ${state}: the switch to Matchande annonser filters in the browser and reads nothing again`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await openNya(page, state);
    await expectOneSeenWrite(page, state);
    const reads = newAdsReads(state);

    await page.getByRole("radio", { name: "Matchande annonser" }).click();
    await expect(page.getByRole("radio", { name: "Matchande annonser" })).toHaveAttribute("aria-checked", "true");
    await expect(page.getByText("Filtrerat: endast matchande annonser")).toBeVisible();
    if (state === "assessed") {
      await expect(page.locator("main .jp-job__title")).toHaveCount(2);
    } else {
      await expect(page.locator("main .jp-job__title")).toHaveCount(0);
      await expect(page.getByText("Ingen av de nya annonserna matchar din profil")).toBeVisible();
      await expect(page.getByRole("button", { name: "Visa alla nya annonser" })).toBeVisible();
    }
    await page.waitForLoadState("networkidle");
    expect(newAdsReads(state)).toBe(reads);
    expect(harness.seenReceipts).toHaveLength(1);
    await shot(page, `nya-${state}-switch-matching-1280-light`);
  });
}

const CLOSED_STATES = [
  { state: "nothing", body: "Alla aktiva annonser från företagen i bevakningen finns kvar.", title: "Inget nytt just nu", primary: { name: "Visa alla annonser", href: "/annonser?" }, ghost: null },
  { state: "tooBroad", body: "Bevakningen matchar fler företag än vi kan räkna annonser för.", title: null, primary: { name: "Tillbaka till bevakningen", href: "" }, ghost: { name: "Ändra bevakningen", href: "/foretag/branschbevakningar" } },
  { state: "notMaterialised", body: "Nya annonser för bevakningen kan inte visas än. Vi letar efter dem en gång per natt.", title: null, primary: { name: "Tillbaka till bevakningen", href: "" }, ghost: null },
  { state: "neverScanned", body: "Nya annonser för bevakningen kan inte visas än. Vi letar efter dem en gång per natt.", title: null, primary: { name: "Tillbaka till bevakningen", href: "" }, ghost: null },
] as const;

for (const closed of CLOSED_STATES) {
  test(`/nya ${closed.state}: its own block, the attribution, and no seen write`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await openNya(page, closed.state);

    await expect(page.locator("h1")).toHaveCount(1);
    const block = page.locator("main .jp-empty");
    await expect(block).toHaveCount(1);
    await expect(block).toContainText(closed.body);
    if (closed.title) await expect(block.locator(".jp-empty__title")).toHaveText(closed.title);
    else await expect(block.locator(".jp-empty__title")).toHaveCount(0);
    await expect(page.getByText("Inget nytt just nu")).toHaveCount(closed.title ? 1 : 0);
    const primary = block.locator("a.jp-btn--primary");
    await expect(primary).toHaveText(closed.primary.name);
    const base = `/foretag/branschbevakningar/${NYA_IDS[closed.state]}`;
    if (closed.primary.href === "/annonser?") await expect(primary).toHaveAttribute("href", new RegExp(`^${base}/annonser`));
    else await expect(primary).toHaveAttribute("href", base);
    await expect(block.locator("a.jp-btn--ghost")).toHaveCount(closed.ghost ? 1 : 0);
    if (closed.ghost) await expect(block.locator("a.jp-btn--ghost")).toHaveAttribute("href", closed.ghost.href);
    await expect(page.locator("main .jp-job__title")).toHaveCount(0);
    await expect(page.locator("main p", { hasText: SOURCE })).toHaveText(SOURCE);
    await expectNoFollowAxis(page);

    await expectNoSeenWrite(page);
    await shot(page, `nya-${closed.state}-1280-light`);
  });
}

const REFUSALS = [
  { state: "error", body: "Försök igen om en stund." },
  { state: "rateLimited", body: "För många förfrågningar just nu. Vänta en stund och ladda om sidan." },
] as const;

for (const refusal of REFUSALS) {
  test(`/nya ${refusal.state}: the error shell with its back link, and no seen write`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await openNya(page, refusal.state);

    const alert = page.locator("main [role=alert]");
    await expect(alert).toContainText("Kunde inte ladda nya annonser");
    await expect(alert).toContainText(refusal.body);
    await expect(page.getByRole("link", { name: "Tillbaka till bevakningen" })).toHaveAttribute("href", `/foretag/branschbevakningar/${NYA_IDS[refusal.state]}`);
    await expectNoFollowAxis(page);
    await expectNoSeenWrite(page);
    await shot(page, `nya-${refusal.state}-1280-light`);
  });
}

test("/nya for another user's id: the 404 page and its title, and no seen write", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(`/foretag/branschbevakningar/${NYA_IDS.foreign}/nya`);
  await settled(page);
  await expect(page.locator("main h1")).toBeVisible();
  await expect(page).toHaveTitle("Sidan finns inte | Jobbliggaren");
  record("nya.foreign.h1", await page.locator("main h1").textContent());
  await expectNoFollowAxis(page);
  await expectNoSeenWrite(page);
  await shot(page, "nya-foreign-404-1280-light");
});

test("/nya behind a slow read shows its loading skeleton, then the rows and one seen write", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(`/foretag/branschbevakningar/${NYA_IDS.slow}/nya`, { waitUntil: "commit" });
  await expect(page.locator(".jp-pagehero .jp-skeleton, main .jp-skeleton").first()).toBeVisible();
  await expect(page.getByRole("status").filter({ hasText: "Laddar…" })).toHaveCount(1);
  await expect(page.getByText("Nya annonser från företagen som matchar din bevakning.")).toBeVisible();
  await shot(page, "nya-loading-skeleton-1280-light");

  await expect(page.locator("main h1")).toHaveText("Långsam läsning", { timeout: 15_000 });
  await expect(page.locator("main .jp-job__title")).toHaveCount(2);
  await expectOneSeenWrite(page, "slow");
});

test("/nya at 320 px: the long derived m.fl. heading wraps and nothing scrolls sideways", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 900 });
  await openNya(page, "assessed");
  await expect(page.locator("main h1")).toHaveText(LONG_HEADING);
  const doc = await page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth,
    client: document.documentElement.clientWidth,
  }));
  record("nya.assessed.overflow.320", doc);
  expect(doc.scroll).toBeLessThanOrEqual(doc.client);
  await shot(page, "nya-assessed-longheading-320-light");
});

for (const width of [1920, 3440]) {
  for (const state of ["nothing", "tooBroad", "notMaterialised", "neverScanned", "error", "rateLimited", "foreign"] as const) {
    test(`/nya ${state} at ${width} px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 1000 });
      await page.goto(`/foretag/branschbevakningar/${NYA_IDS[state]}/nya`);
      await settled(page);
      await expect(page.locator("main h1, main [role=alert]").first()).toBeVisible();
      expect(await overflowX(page)).toBeLessThanOrEqual(0);
      await shot(page, `nya-${state}-${width}-light`);
    });
  }
}

for (const width of [1280, 375]) {
  test(`/nya assessed in forced dark at ${width} px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await openNya(page, "assessed");
    await forceDark(page);
    await expect(page.locator("main .jp-job__title")).toHaveCount(5);
    expect(await overflowX(page)).toBeLessThanOrEqual(0);
    await shot(page, `nya-assessed-${width}-dark`);
  });
}

// ── B1: the catalogue lede ──────────────────────────────────────────────────────────────────────

test("the catalogue lede on /foretag/branschbevakningar at 1280 px", async ({ page }) => {
  harness.criteria = [criterionListRow(CRITERIA.it), criterionListRow(CRITERIA.hosting)];
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto("/foretag/branschbevakningar");
  await settled(page);
  const lede = page.locator(".jp-pagehero__lede");
  await expect(lede).toBeVisible();
  record("catalogue.lede", await lede.evaluate((el) => {
    const range = document.createRange();
    range.selectNodeContents(el);
    const tops = new Set([...range.getClientRects()].map((rect) => Math.round(rect.top)));
    const style = getComputedStyle(el);
    return { text: el.textContent, lines: tops.size, height: el.getBoundingClientRect().height, lineHeight: style.lineHeight, width: el.getBoundingClientRect().width, maxWidth: style.maxWidth };
  }));
  await shot(page, "catalogue-lede-1280-light");
});
