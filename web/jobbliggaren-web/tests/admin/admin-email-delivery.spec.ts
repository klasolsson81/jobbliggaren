import { expect, test, type Page } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { createRequire } from "node:module";
import { join, win32 } from "node:path";
import {
  APP_ORIGIN,
  PROVIDER_EMAIL_ID,
  SESSION_COOKIE,
  SESSION_ID,
  startHarness,
  type DeliveryScenario,
  type FailuresScenario,
  type Harness,
} from "./servers";

/**
 * Leverans on `/admin/e-post` and the overview's failed-sends line, in a real browser (#1981 PR 2): the period links
 * (each its own URL, focus kept, the allowance untouched by a change, a deep link), every state the two reads can be
 * in, and the document's `no-store`. With ADMIN_OVERVIEW_SCREENSHOT_DIR set, each state is rendered at 1280 and
 * 375 px in both themes into that directory.
 */

let harness: Harness;
test.beforeAll(async () => { harness = await startHarness(); });
test.afterAll(async () => { await harness.stop(); });
test.beforeEach(async ({ context, page }) => {
  harness.reset();
  await page.setViewportSize({ width: 1280, height: 900 });
  await context.addCookies([{ name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN,
    secure: true, httpOnly: true, sameSite: "Strict" }]);
});
test.afterEach(() => expect(harness.misses, "the pages read a backend route the harness does not answer").toEqual([]));

const PAGE = "/admin/e-post";
const UNKNOWN = "–Uppgift saknas";
const EMPTY = "Inga misslyckade utskick under perioden.";
const NOT_OBSERVABLE =
  "Scaleway visar inga utskick för läsnyckeln de senaste 32 dagarna, så siffrorna kan inte bedömas. Kontrollera läsnyckelns behörighet enligt runbooken för admin-e-post.";
const READ_KEY_MISSING = "Läsnyckeln är inte installerad, så uppgifterna kan inte hämtas. Se runbooken för admin-e-post.";

const leverans = (page: Page) => page.getByRole("region", { name: "Leverans", exact: true });
const allowance = (page: Page) => page.getByRole("region", { name: /^Utskick (i |denna)/ });
const periods = (page: Page) => leverans(page).getByRole("navigation", { name: "Period", exact: true });
const periodLink = (page: Page, name: string) => periods(page).getByRole("link", { name, exact: true });
const failuresTable = (page: Page) => page.getByRole("table", { name: /^Misslyckade utskick/ });
const failureRows = (page: Page) => failuresTable(page).locator("tbody tr");
/** Alerts with content: Next's route announcer is an empty role=alert that is always there. */
const alerts = (page: Page) => page.getByRole("alert").filter({ hasText: /\S/ });
const card = (page: Page, name: string) => page.getByRole("region", { name, exact: true });

const cliRequire = createRequire(require.resolve("@lhci/cli/package.json"));
const axePath = createRequire(cliRequire.resolve("lighthouse")).resolve("axe-core/axe.min.js");

type AxeResult = { violations: { id: string; impact: string | null; nodes: { target: string[] }[] }[] };

/** The screenshot directory, which must be an absolute C:/tmp path outside the repository; null when unset. */
function screenshotDirectory(): string | null {
  const requested = process.env.ADMIN_OVERVIEW_SCREENSHOT_DIR;
  if (!requested) return null;
  const directory = win32.resolve(requested);
  if (!win32.isAbsolute(requested) || !/^c:\\tmp\\/i.test(directory)) {
    throw new Error("ADMIN_OVERVIEW_SCREENSHOT_DIR must be an absolute C:/tmp directory");
  }
  return directory;
}

/** Checks the page against WCAG 2.1 AA and its width, then renders it into the screenshot directory. */
async function verify(page: Page, state: string) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), `${state} scrolls sideways`).toBe(true);
  await page.addScriptTag({ path: axePath });
  const result = await page.evaluate(async () => {
    const engine = (window as unknown as { axe: { run: (target: Document, options: unknown) => Promise<AxeResult> } }).axe;
    return engine.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] } });
  });
  expect(result.violations, `${state} has accessibility violations`).toEqual([]);
  const directory = screenshotDirectory();
  if (directory === null) return;
  mkdirSync(directory, { recursive: true });
  const theme = await page.evaluate(() => document.documentElement.getAttribute("data-theme") ?? "light");
  await page.screenshot({
    path: join(directory, `epost-${state}-${theme}-${page.viewportSize()?.width ?? 1280}.png`),
    fullPage: true,
    animations: "disabled",
  });
}

/**
 * The dark theme as the house's own specs set it: on the root, after the page has loaded. The links and the segments
 * fade their colours, so the check waits for every transition to end rather than measuring a colour mid-fade.
 */
async function darken(page: Page) {
  await page.evaluate(async () => {
    document.documentElement.dataset.theme = "dark";
    await new Promise((resolve) => requestAnimationFrame(resolve));
    await Promise.all(document.getAnimations().map((animation) => animation.finished.catch(() => undefined)));
  });
}

/** Renders the same state at 1280 and 375 px, in the light and the dark theme. */
async function verifyEverywhere(page: Page, path: string, state: string, ready: (page: Page) => Promise<void>) {
  for (const width of [1280, 375]) {
    await page.setViewportSize({ width, height: width === 375 ? 812 : 900 });
    await page.goto(path);
    await ready(page);
    await verify(page, state);
    await darken(page);
    await verify(page, state);
  }
}

test.describe("the period links", () => {
  test("open on 7 dygn, ask both reads for it, and mark it alone", async ({ page }) => {
    await page.goto(PAGE);

    await expect(periods(page).getByRole("link")).toHaveText(["24 tim", "3 dygn", "7 dygn"]);
    await expect(periodLink(page, "7 dygn")).toHaveAttribute("aria-current", "true");
    await expect(periods(page).locator("[aria-current]")).toHaveCount(1);
    await expect(leverans(page).getByRole("definition")).toHaveText(["384", "5"]);
    expect(harness.emailQueries.sort()).toEqual(["/api/v1/admin/email/delivery?period=d7", "/api/v1/admin/email/failures?period=d7"]);
  });

  test("a deep link to ?period=h24 opens on 24 tim, never on 7 dygn", async ({ page }) => {
    await page.goto(`${PAGE}?period=h24`);

    await expect(periodLink(page, "24 tim")).toHaveAttribute("aria-current", "true");
    await expect(periodLink(page, "7 dygn")).not.toHaveAttribute("aria-current", /.*/);
    await expect(leverans(page).getByRole("definition")).toHaveText(["57", "2"]);
    await expect(failuresTable(page)).toHaveAccessibleName("Misslyckade utskick, senaste 24 tim");
    expect(harness.emailQueries.sort()).toEqual(["/api/v1/admin/email/delivery?period=h24", "/api/v1/admin/email/failures?period=h24"]);
  });

  test("an unknown period falls back to 7 dygn, and never reaches the API", async ({ page }) => {
    await page.goto(`${PAGE}?period=d30`);

    await expect(periodLink(page, "7 dygn")).toHaveAttribute("aria-current", "true");
    expect(harness.emailQueries.every((query) => query.endsWith("?period=d7"))).toBe(true);
  });

  for (const width of [1280, 375]) {
    test(`Enter on each period keeps focus on its link, keeps the page in place and never touches the allowance, at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 900 });
      await page.goto(PAGE);
      await expect(allowance(page).getByRole("definition")).toHaveText(["142", "300", /^0,00\skr$/]);
      // A slow provider, so the change is seen mid-flight: the page keeps its last answer until the new one arrives.
      harness.emailDelay = 1_200;
      await expect(page.locator("main")).not.toContainText("Hämtar uppgifter…");
      await page.evaluate(() => {
        const seen = window as unknown as { __loadingSeen: boolean; __allowanceLost: boolean };
        seen.__loadingSeen = false;
        seen.__allowanceLost = false;
        new MutationObserver(() => {
          if (document.querySelector("main")?.textContent?.includes("Hämtar uppgifter…")) seen.__loadingSeen = true;
          const allowanceHeading = [...document.querySelectorAll("h2")].find((heading) => heading.textContent?.startsWith("Utskick"));
          if (!allowanceHeading?.closest("section")?.textContent?.includes("142")) seen.__allowanceLost = true;
        }).observe(document.body, { subtree: true, childList: true, characterData: true });
      });

      for (const [name, period, values] of [["24 tim", "h24", ["57", "2"]], ["3 dygn", "d3", ["163", "3"]], ["7 dygn", "d7", ["384", "5"]]] as const) {
        const link = periodLink(page, name);
        await link.scrollIntoViewIfNeeded();
        const scrolled = await page.evaluate(() => window.scrollY);
        await link.focus();
        await page.keyboard.press("Enter");
        await expect(page).toHaveURL(new RegExp(`\\?period=${period}$`));
        await expect(link).toHaveAttribute("aria-current", "true");
        await expect(leverans(page).getByRole("definition")).toHaveText([...values]);
        await expect(link).toBeFocused();
        expect(await page.evaluate(() => window.scrollY)).toBe(scrolled);
        await verify(page, `period-${period}`);
      }

      const seen = await page.evaluate(() => {
        const flags = window as unknown as { __loadingSeen: boolean; __allowanceLost: boolean };
        return { loading: flags.__loadingSeen, allowanceLost: flags.__allowanceLost };
      });
      expect(seen, "a loading state, or an allowance without its figures, during a period change").toEqual({
        loading: false, allowanceLost: false,
      });
      await expect(alerts(page)).toHaveCount(0);
    });
  }
});

test.describe("the document", () => {
  test("is never stored, and carries no second of a failed send, only its minute", async ({ page }) => {
    const response = await page.goto(`${PAGE}?period=d7`);
    expect(response?.status()).toBe(200);
    expect(response?.headers()["cache-control"]).toContain("no-store");
    await expect(failureRows(page)).toHaveCount(5);

    const html = (await response?.text()) ?? "";
    const answered = harness.failuresAnswers.at(-1) as { failures: { createdAt: string; recipient: string }[] };
    expect(answered.failures.length).toBe(5);
    for (const row of answered.failures) {
      // The row's instant to the second never reaches the page, only its minute.
      expect(html).not.toContain(row.createdAt);
      expect(html).toContain(row.createdAt.slice(0, 16));
    }
    for (const time of await failuresTable(page).locator("time").all()) {
      await expect(time).toHaveAttribute("datetime", /^\d{4}-\d\d-\d\dT\d\d:\d\dZ$/);
      await expect(time).toHaveText(/^\d{4}-\d\d-\d\d \d\d:\d\d$/);
    }
  });

  test("refuses a failures answer that carries the provider's id: the list fails, and the id never reaches the page", async ({ page }) => {
    harness.emailFailures = "providerId";
    const response = await page.goto(PAGE);

    await expect(failureRows(page)).toHaveText(["Uppgifterna kunde inte hämtas. Försök igen om en stund."]);
    await expect(leverans(page).getByRole("definition")).toHaveText(["384", "5"]);
    expect(JSON.stringify(harness.failuresAnswers.at(-1))).toContain(PROVIDER_EMAIL_ID);
    expect((await response?.text()) ?? "").not.toContain(PROVIDER_EMAIL_ID);
    expect(await page.content()).not.toContain(PROVIDER_EMAIL_ID);
  });
});

const SUMMARY_STATES: ReadonlyArray<{
  readonly state: string;
  readonly delivery: DeliveryScenario;
  readonly failures?: FailuresScenario;
  readonly values: ReadonlyArray<string | RegExp>;
  readonly line: string | null;
  readonly list: ReadonlyArray<string>;
  readonly alert: string | null;
}> = [
  { state: "observed", delivery: "observed", values: ["384", "5"], line: null, list: [], alert: null },
  { state: "nothing-sent", delivery: "nothingSent", values: ["0", "0"], line: null, list: [EMPTY], alert: null },
  { state: "none-failed", delivery: "noneFailed", values: ["384", "0"], line: null, list: [EMPTY], alert: null },
  { state: "not-configured", delivery: "notConfigured", values: [UNKNOWN, UNKNOWN], line: "Räknas inte i den här miljön.",
    list: ["Räknas inte i den här miljön."], alert: null },
  { state: "not-observable", delivery: "notObservable", values: [UNKNOWN, UNKNOWN], line: NOT_OBSERVABLE,
    list: ["Uppgift saknas"], alert: `Leverans: ${NOT_OBSERVABLE}` },
  { state: "list-unjudged", delivery: "observed", failures: "unjudged", values: ["384", "5"], line: null,
    list: ["Uppgift saknas"], alert: "Leverans: Uppgift saknas" },
  { state: "read-key-missing", delivery: "readKeyMissing", values: [UNKNOWN, UNKNOWN], line: READ_KEY_MISSING,
    list: [READ_KEY_MISSING], alert: `Leverans: ${READ_KEY_MISSING}` },
  ...([
    ["no-answer", "Timeout", "Scaleway svarade inte. Försök igen om en stund."],
    ["unreachable", "Unreachable", "Scaleway svarade inte. Försök igen om en stund."],
    ["provider-error", "ProviderError", "Scaleway svarade inte. Försök igen om en stund."],
    ["rate-limited", "RateLimited", "Scaleway begränsar läsningarna just nu. Försök igen om en stund."],
    ["key-refused", "Unauthorized", "Scaleway nekade läsnyckeln. Kontrollera dess utgångsdatum enligt runbooken för admin-e-post."],
    ["read-refused", "Forbidden", "Scaleway nekade läsningen. Kontrollera läsnyckelns behörighet enligt runbooken för admin-e-post."],
    ["malformed", "Malformed", "Scaleways svar kunde inte läsas. Se runbooken för admin-e-post."],
  ] as const).map(([state, delivery, line]) => ({
    state, delivery, values: [UNKNOWN, UNKNOWN], line, list: [line], alert: `Leverans: ${line}`,
  })),
];

test.describe("each state, never a zero or an empty list it did not observe (ADR 0150 D2)", () => {
  for (const expected of SUMMARY_STATES) {
    test(`Leverans in the ${expected.state} state`, async ({ page }) => {
      harness.emailDelivery = expected.delivery;
      harness.emailFailures = expected.failures ?? null;
      await page.goto(PAGE);

      const section = leverans(page);
      await expect(section.getByRole("definition")).toHaveText([...expected.values]);
      if (expected.line !== null) await expect(section.locator("dl + p.jp-adminsoon")).toHaveText(expected.line);
      else await expect(section.locator("dl + p.jp-adminsoon")).toHaveCount(0);
      if (expected.list.length > 0) await expect(failureRows(page)).toHaveText([...expected.list]);
      if (!expected.list.includes(EMPTY)) await expect(section).not.toContainText(EMPTY);
      if (expected.alert === null) await expect(alerts(page)).toHaveCount(0);
      else await expect(alerts(page)).toHaveText([expected.alert]);
      await expect(page.getByRole("table", { name: "Utskick per mejltyp" }).locator("tbody")).toHaveText("Kommer snart");
      // Each region fails on its own: the allowance still shows its month.
      await expect(allowance(page).getByRole("definition")).toHaveText(["142", "300", /^0,00\skr$/]);
      await verify(page, expected.state);
    });
  }

  test("our own failed reads stay in Leverans and say so once", async ({ page }) => {
    harness.overviewReads.delivery = "error";
    harness.overviewReads.failures = "error";
    await page.goto(PAGE);

    const line = "Uppgifterna kunde inte hämtas. Försök igen om en stund.";
    await expect(leverans(page).getByRole("definition")).toHaveText([UNKNOWN, UNKNOWN]);
    await expect(leverans(page).getByText(line, { exact: true })).toHaveCount(2);
    await expect(alerts(page)).toHaveText([`Leverans: ${line}`]);
    await expect(allowance(page).getByRole("definition")).toHaveText(["142", "300", /^0,00\skr$/]);
    await verify(page, "our-read-failed");
  });

  test("the failures list at its limit names every reason, keeps a long domain in its column, and says it shows the latest", async ({ page }) => {
    harness.emailFailures = "capped";
    await page.goto(PAGE);

    await expect(failureRows(page)).toHaveCount(10);
    await expect(failureRows(page).locator("td:nth-child(2)")).toHaveText([
      "Adressen finns inte", "Avvisat av mottagarens server", "Mottagarens inkorg är full",
      "Tillfälligt avvisat av mottagarens server", "Bedömt som skräppost", "Adressen är spärrad hos Scaleway",
      "Annan eller okänd orsak", "Avvisat av mottagarens server", "Adressen finns inte",
      "Tillfälligt avvisat av mottagarens server",
    ]);
    await expect(failuresTable(page)).toContainText("p•••@post.ett-mycket-langt-underdomannamn-for-smala-skarmar.example.test");
    await expect(leverans(page).getByText("Visar de 10 senaste.", { exact: true })).toBeVisible();
    await expect(failuresTable(page).locator(".jp-pill, .jp-admin-danger, .jp-admin-warning")).toHaveCount(0);
    await verify(page, "capped");
  });
});

test.describe("rendered states (#1981 PR 2, DESIGN.md §12)", () => {
  for (const expected of SUMMARY_STATES.filter((state) => state.state !== "unreachable" && state.state !== "provider-error")) {
    test(`renders ${expected.state} at 1280 and 375 px in both themes`, async ({ page }) => {
      harness.emailDelivery = expected.delivery;
      harness.emailFailures = expected.failures ?? null;
      await verifyEverywhere(page, PAGE, `state-${expected.state}`, async (current) => {
        await expect(leverans(current).getByRole("definition")).toHaveText([...expected.values]);
      });
    });
  }

  test("renders the list at its limit at 1280 and 375 px in both themes", async ({ page }) => {
    harness.emailFailures = "capped";
    await verifyEverywhere(page, PAGE, "state-capped", async (current) => {
      await expect(failureRows(current)).toHaveCount(10);
    });
  });

  test("renders each period's view from its deep link at 1280 and 375 px in both themes", async ({ page }) => {
    for (const period of ["h24", "d3", "d7"] as const) {
      await verifyEverywhere(page, `${PAGE}?period=${period}`, `deep-${period}`, async (current) => {
        await expect(periods(current).locator("[aria-current='true']")).toHaveAttribute("href", `${PAGE}?period=${period}`);
      });
    }
  });

  test("renders the loading state at 1280 and 375 px in both themes, with no period chosen", async ({ page }) => {
    for (const width of [1280, 375]) {
      await page.setViewportSize({ width, height: width === 375 ? 812 : 900 });
      harness.emailDelay = 8_000;
      await page.goto(`${PAGE}?period=h24`, { waitUntil: "commit" });
      await expect(leverans(page).getByText("Hämtar uppgifter…", { exact: true })).toHaveCount(2);
      await expect(allowance(page).getByText("Hämtar uppgifter…", { exact: true })).toBeVisible();
      await expect(periods(page).locator("[aria-current]")).toHaveCount(0);
      await expect(page.getByRole("table", { name: "Utskick per mejltyp" }).locator("tbody")).toHaveText("Kommer snart");
      await verify(page, "loading");
      await darken(page);
      await verify(page, "loading");
      harness.emailDelay = 0;
      await page.goto("about:blank");
    }
  });
});

test.describe("the overview's failed-sends line", () => {
  const attention = (page: Page) => card(page, "Kräver uppmärksamhet");

  test("links failed sends to the page's 24 tim view, raises the edge, and never reads the failures list", async ({ page }) => {
    await page.goto("/admin");

    const link = attention(page).getByRole("link", { name: "2 utskick har misslyckats de senaste 24 timmarna", exact: true });
    await expect(link).toHaveAttribute("href", "/admin/e-post?period=h24");
    await expect(attention(page)).toHaveAttribute("data-state", "raised");
    await expect(card(page, "E-post")).not.toContainText(/misslyckade|24 timmarna/i);
    expect(harness.emailQueries).toEqual(["/api/v1/admin/email/delivery?period=h24"]);

    await link.click();
    await expect(page).toHaveURL(/\/admin\/e-post\?period=h24$/);
    await expect(periodLink(page, "24 tim")).toHaveAttribute("aria-current", "true");
    expect(harness.emailQueries.filter((query) => query.includes("/failures"))).toEqual(["/api/v1/admin/email/failures?period=h24"]);
  });

  const OVERVIEW_STATES: ReadonlyArray<{
    readonly state: string;
    readonly set: (h: Harness) => void;
    readonly text: string;
    readonly raised: boolean;
  }> = [
    { state: "failed-sends", set: (h) => { h.emailDelivery = "observed"; },
      text: "2 utskick har misslyckats de senaste 24 timmarna", raised: true },
    { state: "no-failed-sends", set: (h) => { h.emailDelivery = "noneFailed"; },
      text: "Inga misslyckade utskick hos Scaleway de senaste 24 timmarna.", raised: false },
    { state: "not-observable", set: (h) => { h.emailDelivery = "notObservable"; },
      text: `Misslyckade utskick${NOT_OBSERVABLE}`, raised: false },
    { state: "read-key-missing", set: (h) => { h.emailDelivery = "readKeyMissing"; },
      text: `Misslyckade utskick${READ_KEY_MISSING}`, raised: false },
    { state: "provider-failed", set: (h) => { h.emailDelivery = "Timeout"; },
      text: "Misslyckade utskickScaleway svarade inte. Försök igen om en stund.", raised: false },
    { state: "our-read-failed", set: (h) => { h.overviewReads.delivery = "error"; },
      text: "Misslyckade utskickUppgifterna kunde inte hämtas. Försök igen om en stund.", raised: false },
  ];

  for (const expected of OVERVIEW_STATES) {
    test(`says ${expected.state} in the attention card, and never a zero it did not observe`, async ({ page }) => {
      // Nothing else in need of attention, so the edge is the line's alone.
      harness.overviewEmpty = true;
      expected.set(harness);
      await page.goto("/admin");

      await expect(attention(page)).toContainText(expected.text);
      await expect(attention(page)).toHaveAttribute("data-state", expected.raised ? "raised" : "unknown");
      if (expected.state !== "no-failed-sends") await expect(attention(page)).not.toContainText("Inga misslyckade utskick");
      await expect(attention(page)).not.toContainText("Inget kräver uppmärksamhet");
      expect(harness.emailQueries.filter((query) => query.includes("/failures"))).toEqual([]);
    });
  }

  for (const expected of OVERVIEW_STATES.filter((state) => state.state !== "our-read-failed" && state.state !== "read-key-missing")) {
    test(`renders the attention card's ${expected.state} line at 1280 and 375 px in both themes`, async ({ page }) => {
      harness.overviewEmpty = true;
      expected.set(harness);
      await verifyEverywhere(page, "/admin", `overview-${expected.state}`, async (current) => {
        await expect(attention(current)).toContainText(expected.text);
      });
    });
  }
});
