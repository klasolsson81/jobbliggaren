import { expect, test, type Page } from "@playwright/test";
import { mkdirSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join, win32 } from "node:path";
import {
  APP_ORIGIN,
  PROVIDER_EMAIL_ID,
  SESSION_COOKIE,
  SESSION_ID,
  startHarness,
  DMARC_LONG_DOMAIN,
  DMARC_LONG_IPV6,
  type DeliveryScenario,
  type FailuresScenario,
  type Harness,
} from "./servers";

/**
 * Leverans on `/admin/e-post` and the overview's failed-sends line, in a real browser (#1981 PR 2 and PR 3): the period
 * links (each its own URL, focus kept, the allowance untouched by a change, a deep link), every state the provider's
 * two reads and our own count per mail kind can be in, the section's announcement, and the document's `no-store`; and
 * the DMARC reports' own section, which the period links never drive (#1981 PR 4b).
 * With ADMIN_OVERVIEW_SCREENSHOT_DIR set, each state is rendered at 1280 and 375 px in both themes into that directory.
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
  "Scaleways leveransstatistik visar inga utskick de senaste 32 dygnen, så siffrorna kan inte bedömas. Kontrollera läsnyckelns behörighet enligt runbooken för admin-e-post.";
const READ_KEY_MISSING = "Läsnyckeln är inte installerad. Se runbooken för admin-e-post.";
/** The list's line where the summary's already gives its cause, or where the list could not be judged. */
const LIST_UNKNOWN = "Uppgift saknas";
const OUR_READ_FAILED = "Uppgifterna kunde inte hämtas. Försök igen om en stund.";

const leverans = (page: Page) => page.getByRole("region", { name: "Leverans", exact: true });
const allowance = (page: Page) => page.getByRole("region", { name: /^Utskick (i |denna)/ });
const periods = (page: Page) => leverans(page).getByRole("navigation", { name: "Period", exact: true });
const periodLink = (page: Page, name: string) => periods(page).getByRole("link", { name, exact: true });
const failuresTable = (page: Page) => page.getByRole("table", { name: /^Misslyckade utskick/ });
const failureRows = (page: Page) => failuresTable(page).locator("tbody tr");
const kindsSection = (page: Page) => leverans(page).getByRole("region", { name: "Utskick per mejltyp", exact: true });
const kindsTable = (page: Page) => page.getByRole("table", { name: /^(Antal u|U)tskick per mejltyp/ });
const kindsRows = (page: Page) => kindsTable(page).locator("tbody tr");
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
  test("open on 7 dygn, ask every Leverans read for it, and mark it alone", async ({ page }) => {
    await page.goto(PAGE);

    await expect(periods(page).getByRole("link")).toHaveText(["24 tim", "3 dygn", "7 dygn"]);
    await expect(periodLink(page, "7 dygn")).toHaveAttribute("aria-current", "true");
    await expect(periods(page).locator("[aria-current]")).toHaveCount(1);
    await expect(leverans(page).getByRole("definition")).toHaveText(["384", "5"]);
    expect(harness.emailQueries.sort()).toEqual([
      "/api/v1/admin/email/delivery?period=d7", "/api/v1/admin/email/failures?period=d7", "/api/v1/admin/email/kinds?period=d7",
    ]);
  });

  test("a deep link to ?period=h24 opens on 24 tim, never on 7 dygn", async ({ page }) => {
    await page.goto(`${PAGE}?period=h24`);

    await expect(periodLink(page, "24 tim")).toHaveAttribute("aria-current", "true");
    await expect(periodLink(page, "7 dygn")).not.toHaveAttribute("aria-current", /.*/);
    await expect(leverans(page).getByRole("definition")).toHaveText(["57", "2"]);
    await expect(failuresTable(page)).toHaveAccessibleName("Misslyckade utskick, senaste 24 tim");
    expect(harness.emailQueries.sort()).toEqual([
      "/api/v1/admin/email/delivery?period=h24", "/api/v1/admin/email/failures?period=h24", "/api/v1/admin/email/kinds?period=h24",
    ]);
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
      const dmarcText = await dmarc(page).innerText();
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
        // The DMARC reports keep their own window: a period change leaves the section as it was.
        expect(await dmarc(page).innerText()).toBe(dmarcText);
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
      expect(harness.dmarcQueries.every((query) => query === "/api/v1/admin/email/dmarc")).toBe(true);
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
    list: [LIST_UNKNOWN], alert: null },
  { state: "not-observable", delivery: "notObservable", values: [UNKNOWN, UNKNOWN], line: NOT_OBSERVABLE,
    list: [LIST_UNKNOWN], alert: `Leverans: ${NOT_OBSERVABLE}` },
  { state: "list-unjudged", delivery: "observed", failures: "unjudged", values: ["384", "5"], line: null,
    list: [LIST_UNKNOWN], alert: `Leverans: ${LIST_UNKNOWN}` },
  { state: "read-key-missing", delivery: "readKeyMissing", values: [UNKNOWN, UNKNOWN], line: READ_KEY_MISSING,
    list: [LIST_UNKNOWN], alert: `Leverans: ${READ_KEY_MISSING}` },
  ...([
    ["no-answer", "Timeout", "Scaleway svarade inte. Försök igen om en stund."],
    ["unreachable", "Unreachable", "Scaleway svarade inte. Försök igen om en stund."],
    ["provider-error", "ProviderError", "Scaleway svarade inte. Försök igen om en stund."],
    ["rate-limited", "RateLimited", "Scaleway begränsar läsningarna just nu. Försök igen om en stund."],
    ["key-refused", "Unauthorized", "Scaleway nekade läsnyckeln. Kontrollera dess utgångsdatum enligt runbooken för admin-e-post."],
    ["read-refused", "Forbidden", "Scaleway nekade läsningen. Kontrollera läsnyckelns behörighet enligt runbooken för admin-e-post."],
    ["malformed", "Malformed", "Scaleways svar kunde inte läsas. Se runbooken för admin-e-post."],
  ] as const).map(([state, delivery, line]) => ({
    state, delivery, values: [UNKNOWN, UNKNOWN], line, list: [LIST_UNKNOWN], alert: `Leverans: ${line}`,
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
      // Each region fails on its own: our own count still shows its seven kinds, and the allowance its month.
      await expect(kindsTable(page).getByRole("rowheader")).toHaveCount(7);
      await expect(allowance(page).getByRole("definition")).toHaveText(["142", "300", /^0,00\skr$/]);
      await verify(page, expected.state);
    });
  }

  test("our own failed reads stay in Leverans and say so once", async ({ page }) => {
    harness.overviewReads.delivery = "error";
    harness.overviewReads.failures = "error";
    await page.goto(PAGE);

    await expect(leverans(page).getByRole("definition")).toHaveText([UNKNOWN, UNKNOWN]);
    await expect(leverans(page).getByText(OUR_READ_FAILED, { exact: true })).toHaveCount(1);
    await expect(failureRows(page)).toHaveText([LIST_UNKNOWN]);
    await expect(alerts(page)).toHaveText([`Leverans: ${OUR_READ_FAILED}`]);
    await expect(allowance(page).getByRole("definition")).toHaveText(["142", "300", /^0,00\skr$/]);
    await verifyEverywhere(page, PAGE, "our-read-failed", async (current) => {
      await expect(alerts(current)).toHaveText([`Leverans: ${OUR_READ_FAILED}`]);
    });
  });

  test("a read key that is not installed says so across the whole page at once, each line once", async ({ page }) => {
    harness.overviewEmail = "readKeyMissing";
    harness.emailDelivery = "readKeyMissing";
    await verifyEverywhere(page, PAGE, "read-key-missing-page", async (current) => {
      await expect(allowance(current).locator(".jp-adminsoon")).toHaveText([READ_KEY_MISSING]);
      await expect(leverans(current).locator("dl + p.jp-adminsoon")).toHaveText(READ_KEY_MISSING);
      await expect(leverans(current).getByText(READ_KEY_MISSING, { exact: true })).toHaveCount(1);
      await expect(failureRows(current)).toHaveText([LIST_UNKNOWN]);
      await expect(alerts(current)).toHaveText([
        `Utskick mot gratiskvoten: ${READ_KEY_MISSING}`, `Leverans: ${READ_KEY_MISSING}`,
      ]);
    });
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
    // The API's form for a local part that starts with neither a letter nor a digit, and its form for no address at all.
    await expect(failuresTable(page).getByRole("cell", { name: "•••@example.org", exact: true })).toBeVisible();
    await expect(failuresTable(page).getByRole("cell", { name: "•••", exact: true })).toBeVisible();
    await expect(leverans(page).getByText("Visar de 10 senaste.", { exact: true })).toBeVisible();
    await expect(failuresTable(page).locator(".jp-pill, .jp-admin-danger, .jp-admin-warning")).toHaveCount(0);
    await verify(page, "capped");
  });
});

test.describe("rendered states (#1981 PR 2, DESIGN.md §12)", () => {
  for (const expected of SUMMARY_STATES) {
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
      await expect(failuresTable(current).getByRole("cell", { name: "•••@example.org", exact: true })).toBeVisible();
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
      await expect(leverans(page).getByText("Hämtar uppgifter…", { exact: true })).toHaveCount(3);
      await expect(allowance(page).getByText("Hämtar uppgifter…", { exact: true })).toBeVisible();
      await expect(periods(page).locator("[aria-current]")).toHaveCount(0);
      await expect(kindsSection(page).getByRole("heading", { level: 3, name: "Utskick per mejltyp" })).toBeVisible();
      await expect(kindsRows(page)).toHaveText(["Hämtar uppgifter…"]);
      await expect(page.getByRole("status").filter({ hasText: /\S/ })).toHaveText([
        "Utskick mot gratiskvoten: Hämtar uppgifter…", "Leverans: Hämtar uppgifter…", "DMARC-rapporter: Hämtar uppgifter…",
      ]);
      await expect(page.locator("main")).not.toContainText("Kommer snart");
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

  /** `dated`: the line has a time to be dated by, which every answered read has except a deployment without Scaleway. */
  const OVERVIEW_STATES: ReadonlyArray<{
    readonly state: string;
    readonly set: (h: Harness) => void;
    readonly text: string;
    readonly raised: boolean;
    readonly dated: boolean;
  }> = [
    { state: "failed-sends", set: (h) => { h.emailDelivery = "observed"; },
      text: "2 utskick har misslyckats de senaste 24 timmarna", raised: true, dated: true },
    { state: "no-failed-sends", set: (h) => { h.emailDelivery = "noneFailed"; },
      text: "Inga misslyckade utskick hos Scaleway de senaste 24 timmarna.", raised: false, dated: true },
    { state: "not-observable", set: (h) => { h.emailDelivery = "notObservable"; },
      text: `Misslyckade utskick${NOT_OBSERVABLE}`, raised: false, dated: true },
    { state: "read-key-missing", set: (h) => { h.emailDelivery = "readKeyMissing"; },
      text: `Misslyckade utskick${READ_KEY_MISSING}`, raised: false, dated: true },
    { state: "provider-failed", set: (h) => { h.emailDelivery = "Timeout"; },
      text: "Misslyckade utskickScaleway svarade inte. Försök igen om en stund.", raised: false, dated: true },
    { state: "our-read-failed", set: (h) => { h.overviewReads.delivery = "error"; },
      text: `Misslyckade utskick${OUR_READ_FAILED}`, raised: false, dated: false },
    { state: "not-configured", set: (h) => { h.emailDelivery = "notConfigured"; },
      text: "Misslyckade utskickRäknas inte i den här miljön.", raised: false, dated: false },
  ];
  /** The failed-sends line's date note: the card's last note, since the line is the card's last source. */
  const deliveryNote = (page: Page) => attention(page).locator(".jp-adminkpi__sub").last();

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
      // Dated as Leverans dates the same read, never "Uppgift från"; a line with no answer has no date at all.
      if (expected.dated) {
        await expect(deliveryNote(page)).toHaveText(/^Hämtat \d{4}-\d\d-\d\d \d\d:\d\d/);
        await expect(deliveryNote(page)).not.toContainText("Uppgift från");
      } else {
        await expect(attention(page)).not.toContainText("Hämtat");
      }
      expect(harness.emailQueries.filter((query) => query.includes("/failures"))).toEqual([]);
    });
  }

  for (const expected of OVERVIEW_STATES) {
    test(`renders the attention card's ${expected.state} line at 1280 and 375 px in both themes`, async ({ page }) => {
      harness.overviewEmpty = true;
      expected.set(harness);
      await verifyEverywhere(page, "/admin", `overview-${expected.state}`, async (current) => {
        await expect(attention(current)).toContainText(expected.text);
      });
    });
  }

  test("renders the e-post card's missing read key at 1280 and 375 px in both themes", async ({ page }) => {
    harness.overviewEmpty = true;
    harness.overviewEmail = "readKeyMissing";
    await verifyEverywhere(page, "/admin", "overview-email-card-read-key-missing", async (current) => {
      await expect(card(current, "E-post")).toContainText(READ_KEY_MISSING);
      await expect(card(current, "E-post")).not.toContainText("Räknas inte i den här miljön.");
    });
  });
});

const KIND_LABELS = [
  "Inloggning och koder", "Adressbyte begärt", "Adressbyte genomfört", "Matchningar", "Bevakade företag", "Ny feedback",
  "Svar på feedback",
];
const KINDS_NOT_OBSERVABLE =
  "Jobbliggarens räkning visar inga utskick under perioden, fast Scaleway har skickat. Se runbooken för admin-e-post.";
const REFUSED = "Din session saknar Admin-rollen.";
const COUNTED_BY_US = "Räknat av Jobbliggaren vid varje försök att lämna över ett mejl till Scaleway";
/** The harness's per-day handovers for each kind, which the longer periods scale (`servers.ts`). */
const HANDOVERS = [12, 3, 2, 35, 8, 4, 1];
const PERIOD_DAYS = { h24: 1, d3: 3, d7: 7 } as const;
const swedishDate = (ms: number) => new Intl.DateTimeFormat("sv-SE", { timeZone: "Europe/Stockholm" }).format(ms);

/** Writes what a screen reader is told, beside the renders, since a screenshot cannot show it. */
function record(name: string, value: unknown) {
  const directory = screenshotDirectory();
  if (directory === null) return;
  mkdirSync(directory, { recursive: true });
  writeFileSync(join(directory, name), typeof value === "string" ? value : JSON.stringify(value, null, 2));
}

test.describe("Utskick per mejltyp (#1981 PR 3)", () => {
  test("is our own count's sub-section, after the failures, with the seven kinds as row headers in a fixed order", async ({ page }) => {
    await page.goto(PAGE);

    await expect(leverans(page).getByRole("heading", { level: 3 })).toHaveText(["Senaste misslyckade utskick", "Utskick per mejltyp"]);
    await expect(kindsTable(page).getByRole("columnheader")).toHaveText(["Mejltyp", "Överlämnade", "Fel", "Senaste fel"]);
    await expect(kindsTable(page).getByRole("rowheader")).toHaveCount(7);
    await expect(kindsTable(page).getByRole("rowheader")).toHaveText(KIND_LABELS);
    await expect(kindsRows(page)).toHaveCount(7);
    await expect(kindsTable(page)).not.toContainText(/totalt|summa|–/i);
    record("epost-kinds-aria-d7.txt", await kindsSection(page).ariaSnapshot());
  });

  for (const period of ["h24", "d3", "d7"] as const) {
    test(`shows the ${period} counts from the period's first day, every failure class and Inget, at 1280 and 375 px in both themes`, async ({ page }) => {
      await verifyEverywhere(page, `${PAGE}?period=${period}`, `kinds-observed-${period}`, async (current) => {
        const from = swedishDate(Date.now() - PERIOD_DAYS[period] * 86_400_000);
        const today = swedishDate(Date.now());
        await expect(kindsSection(current).locator(".jp-adminmailsum__note")).toHaveText(`${COUNTED_BY_US}, från och med ${from}.`);
        await expect(kindsTable(current)).toHaveAccessibleName(`Utskick per mejltyp från och med ${from}`);
        const scale = PERIOD_DAYS[period];
        const rows = await kindsRows(current).evaluateAll((trs) =>
          trs.map((tr) => [...tr.querySelectorAll("th, td")].map((cell) => (cell.textContent ?? "").replace(/\u00a0/g, " "))));
        expect(rows).toEqual([
          [KIND_LABELS[0], String(HANDOVERS[0]! * scale), "2", `${today}: avvisat av Scaleway och inget svar i tid`],
          [KIND_LABELS[1], String(HANDOVERS[1]! * scale), "0", "Inget"],
          [KIND_LABELS[2], String(HANDOVERS[2]! * scale), "1", `${from}: över Scaleways taktgräns`],
          [KIND_LABELS[3], String(HANDOVERS[3]! * scale), "3", `${today}: Scaleway nåddes inte och fel hos Scaleway`],
          [KIND_LABELS[4], String(HANDOVERS[4]! * scale), "0", "Inget"],
          [KIND_LABELS[5], String(HANDOVERS[5]! * scale), "1", `${from}: annat fel`],
          [KIND_LABELS[6], String(HANDOVERS[6]! * scale), "0", "Inget"],
        ]);
        await expect(kindsTable(current).locator("tbody td .jp-admin-danger")).toHaveText(["2", "1", "3", "1"]);
        await expect(kindsTable(current).locator("tbody time").first()).toHaveAttribute("datetime", today);
        await expect(alerts(current)).toHaveCount(0);
      });
    });
  }

  const KINDS_STATES: ReadonlyArray<{
    readonly state: string;
    readonly set: (h: Harness) => void;
    readonly ready: (page: Page) => Promise<void>;
  }> = [
    { state: "none-failed", set: (h) => { h.emailKinds = "noneFailed"; }, ready: async (page) => {
      await expect(kindsRows(page).locator("td:last-child")).toHaveText(Array(7).fill("Inget"));
      await expect(kindsTable(page).locator(".jp-admin-danger")).toHaveCount(0);
    } },
    { state: "nothing-sent", set: (h) => { h.emailKinds = "nothingSent"; }, ready: async (page) => {
      await expect(kindsRows(page)).toHaveText(KIND_LABELS.map((label) => `${label}00Inget`));
    } },
    { state: "partial", set: (h) => { h.emailKinds = "partial"; }, ready: async (page) => {
      await expect(kindsSection(page).locator(".jp-adminmailsum__note"))
        .toHaveText(new RegExp(`^${COUNTED_BY_US}, sedan räkningen började \\d{4}-\\d\\d-\\d\\d \\d\\d:\\d\\d\\.$`));
      await expect(kindsTable(page)).toHaveAccessibleName(/^Utskick per mejltyp sedan \d{4}-\d\d-\d\d \d\d:\d\d$/);
      await expect(kindsRows(page)).toHaveCount(7);
    } },
    { state: "not-configured", set: (h) => { h.emailKinds = "notConfigured"; }, ready: async (page) => {
      await expect(kindsRows(page)).toHaveText(["Räknas inte i den här miljön."]);
      await expect(alerts(page)).toHaveCount(0);
    } },
    { state: "not-observable", set: (h) => { h.emailKinds = "notObservable"; }, ready: async (page) => {
      await expect(kindsRows(page)).toHaveText([KINDS_NOT_OBSERVABLE]);
      await expect(alerts(page)).toHaveText([`Utskick per mejltyp: ${KINDS_NOT_OBSERVABLE}`]);
    } },
    { state: "our-read-failed", set: (h) => { h.overviewReads.kinds = "error"; }, ready: async (page) => {
      await expect(kindsRows(page)).toHaveText([OUR_READ_FAILED]);
    } },
    { state: "refused", set: (h) => { h.overviewReads.kinds = "forbidden"; }, ready: async (page) => {
      await expect(kindsRows(page)).toHaveText([REFUSED]);
      await expect(alerts(page)).toHaveText([`Utskick per mejltyp: ${REFUSED}`]);
    } },
  ];

  for (const expected of KINDS_STATES) {
    test(`renders the ${expected.state} count at 1280 and 375 px in both themes, never a zero it did not observe`, async ({ page }) => {
      expected.set(harness);
      await verifyEverywhere(page, PAGE, `kinds-${expected.state}`, async (current) => {
        await expected.ready(current);
        // Outside an observed count: the column heads stay, one line, no note and no row of figures.
        if (!["none-failed", "nothing-sent", "partial"].includes(expected.state)) {
          await expect(kindsTable(current).getByRole("columnheader")).toHaveCount(4);
          await expect(kindsRows(current)).toHaveCount(1);
          await expect(kindsRows(current).locator("td.jp-admintable__soon")).toHaveCount(1);
          await expect(kindsSection(current).locator(".jp-adminmailsum__note")).toHaveCount(0);
          await expect(kindsTable(current)).toHaveAccessibleName("Antal utskick per mejltyp");
        }
        // The provider's regions are untouched by our own count's state.
        await expect(leverans(current).getByRole("definition")).toHaveText(["384", "5"]);
      });
    });
  }

  const ANNOUNCEMENTS: ReadonlyArray<{ readonly state: string; readonly set: (h: Harness) => void; readonly alerts: string[] }> = [
    { state: "only-ours-failed", set: (h) => { h.overviewReads.kinds = "error"; },
      alerts: [`Utskick per mejltyp: ${OUR_READ_FAILED}`] },
    { state: "only-leverans-failed", set: (h) => { h.overviewReads.delivery = h.overviewReads.failures = "error"; },
      alerts: [`Leverans: ${OUR_READ_FAILED}`] },
    { state: "both-failed", set: (h) => { h.overviewReads.delivery = h.overviewReads.failures = h.overviewReads.kinds = "error"; },
      alerts: [`Leverans: ${OUR_READ_FAILED} Utskick per mejltyp: ${OUR_READ_FAILED}`] },
    { state: "all-refused", set: (h) => { h.mode = "forbidden"; },
      alerts: [`Utskick mot gratiskvoten: ${REFUSED}`, `Leverans: ${REFUSED}`, `DMARC-rapporter: ${REFUSED}`] },
    { state: "only-dmarc-failed", set: (h) => { h.overviewReads.dmarc = "error"; },
      alerts: [`DMARC-rapporter: ${OUR_READ_FAILED}`] },
    { state: "dmarc-and-leverans-failed", set: (h) => { h.overviewReads.delivery = h.overviewReads.failures = h.overviewReads.dmarc = "error"; },
      alerts: [`Leverans: ${OUR_READ_FAILED}`, `DMARC-rapporter: ${OUR_READ_FAILED}`] },
  ];

  for (const expected of ANNOUNCEMENTS) {
    test(`announces ${expected.state} with each region's own source, a shared cause once, at 1280 and 375 px in both themes`, async ({ page }) => {
      expected.set(harness);
      await verifyEverywhere(page, PAGE, `announce-${expected.state}`, async (current) => {
        await expect(alerts(current)).toHaveText(expected.alerts);
      });
      record(`epost-announce-${expected.state}.json`, await alerts(page).allTextContents());
    });
  }

  test("keeps each kind's name and its column head in view while the table scrolls in its region at 375 px, on the page's own ground", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(PAGE);
    const region = page.getByRole("region", { name: /^Utskick per mejltyp från och med/ });
    await region.scrollIntoViewIfNeeded();
    for (const theme of ["light", "dark"] as const) {
      if (theme === "dark") await darken(page);
      await region.evaluate((element) => { element.scrollLeft = 0; });
      const header = kindsTable(page).getByRole("rowheader").first();
      const head = kindsTable(page).getByRole("columnheader", { name: "Mejltyp" });
      const atRest = await header.boundingBox();
      await region.evaluate((element) => { element.scrollLeft = element.scrollWidth; });
      await expect.poll(async () => region.evaluate((element) => element.scrollLeft)).toBeGreaterThan(100);
      const regionBox = await region.boundingBox();
      for (const cell of [header, head]) {
        const scrolled = await cell.boundingBox();
        expect(scrolled?.x, "the kind's column stays at the region's start").toBeCloseTo(regionBox?.x ?? -1, 0);
        expect(scrolled?.x).toBeCloseTo(atRest?.x ?? -1, 0);
      }
      // The cells cover the figures that scroll under them in the colour of what the table stands on.
      const backgrounds = await header.evaluate((cell) => {
        let ground = cell.closest(".jp-admintable-scroll")?.parentElement ?? null;
        while (ground !== null && getComputedStyle(ground).backgroundColor === "rgba(0, 0, 0, 0)") ground = ground.parentElement;
        return { cell: getComputedStyle(cell).backgroundColor, ground: ground === null ? null : getComputedStyle(ground).backgroundColor };
      });
      expect(backgrounds.cell).toBe(backgrounds.ground);
      const directory = screenshotDirectory();
      if (directory !== null) {
        mkdirSync(directory, { recursive: true });
        await region.screenshot({ path: join(directory, `epost-kinds-sticky-${theme}-375.png`), animations: "disabled" });
      }
    }
  });
});

// ── DMARC-rapporter (#1981 PR 4b) ───────────────────────────────────────────────────────────

const dmarc = (page: Page) => page.getByRole("region", { name: "DMARC-rapporter", exact: true });
const dmarcTable = (page: Page) => page.getByRole("table", { name: /^DMARC-resultat per IP-adress/ });
const dmarcLines = (page: Page) => dmarc(page).locator(":scope > div > p.jp-adminsoon");
const RUNBOOK = "Se runbooken för admin-e-post.";
/** WCAG 1.4.12's spacing, as its bookmarklets apply it. */
const TEXT_SPACING =
  "* { line-height: 1.5 !important; letter-spacing: 0.12em !important; word-spacing: 0.16em !important; } p { margin-bottom: 2em !important; }";
const MINUTE = String.raw`\d{4}-\d\d-\d\d \d\d:\d\d`;
const DMARC_FIXED =
  "Rapporterna, som vi inte har kontrollerat, visar autentisering, inte om meddelandena hamnade i inkorgen eller bland skräpposten.";
const dmarcStale = new RegExp(`^Brevlådan har inte lästs sedan ${MINUTE}\\. ${RUNBOOK}$`);
const resumes = (reason: string) => new RegExp(`^${reason} Nästa inloggningsförsök görs tidigast ${MINUTE}\\. ${RUNBOOK}$`);

const DMARC_STATES: ReadonlyArray<{
  readonly state: string;
  readonly set: (h: Harness) => void;
  readonly values: ReadonlyArray<string | RegExp>;
  readonly lines: ReadonlyArray<string | RegExp>;
  readonly announced: boolean;
}> = [
  { state: "observed", set: () => {}, values: ["51", "3"], lines: [], announced: false },
  { state: "capped", set: (h) => { h.emailDmarc = "capped"; }, values: [/^2\s206$/, "6"], lines: [
    `En rapport finns i flera versioner med olika innehåll, och ingen av versionerna räknas. ${RUNBOOK}`,
    `2 mejl till brevlådan de senaste 7 dygnen kunde inte läsas som rapport. ${RUNBOOK}`,
    `2 mejl till brevlådan de senaste 7 dygnen kunde inte sparas, eftersom gränsen för sparade rapporter per dygn var nådd. ${RUNBOOK}`,
    `Ett mejl till brevlådan de senaste 7 dygnen gick inte att läsa efter upprepade försök. ${RUNBOOK}`,
    "3 mejl till brevlådan väntar på ett nytt försök.",
  ], announced: false },
  { state: "none-failing", set: (h) => { h.emailDmarc = "noneFailing"; }, values: ["51", "0"], lines: [], announced: false },
  { state: "observed-stale", set: (h) => { h.emailDmarc = "observedStale"; }, values: ["51", "3"], lines: [dmarcStale], announced: true },
  { state: "observed-protocol", set: (h) => { h.dmarcFailure = "Protocol"; }, values: ["51", "3"],
    lines: [`Brevlådans svar kunde inte läsas. ${RUNBOOK}`], announced: true },
  { state: "observed-unanswered", set: (h) => { h.dmarcFailure = "AuthUnconfirmed"; }, values: ["51", "3"],
    lines: [resumes("Lösenordet skickades, men brevlådan gav inget svar\\.")], announced: true },
  { state: "no-reports", set: (h) => { h.emailDmarc = "noReports"; }, values: [UNKNOWN, UNKNOWN],
    lines: ["Ingen rapport täcker de senaste 7 dygnen."], announced: false },
  { state: "no-reports-conflicting", set: (h) => { h.emailDmarc = "noReportsConflicting"; }, values: [UNKNOWN, UNKNOWN],
    lines: [`En rapport finns i flera versioner med olika innehåll, och ingen av versionerna räknas. ${RUNBOOK}`], announced: false },
  { state: "no-reports-stale", set: (h) => { h.emailDmarc = "noReportsStale"; }, values: [UNKNOWN, UNKNOWN],
    lines: [dmarcStale], announced: true },
  { state: "no-reports-timeout", set: (h) => { h.emailDmarc = "noReports"; h.dmarcFailure = "Timeout"; }, values: [UNKNOWN, UNKNOWN],
    lines: [`Brevlådan svarade inte i tid. ${RUNBOOK}`], announced: true },
  { state: "no-reports-refused", set: (h) => { h.emailDmarc = "noReports"; h.dmarcFailure = "AuthRefused"; }, values: [UNKNOWN, UNKNOWN],
    lines: [resumes("Brevlådan nekade inloggningen\\.")], announced: true },
  { state: "not-observable", set: (h) => { h.emailDmarc = "notObservable"; }, values: [UNKNOWN, UNKNOWN],
    lines: ["Brevlådan har inte lästs än."], announced: false },
  { state: "not-configured", set: (h) => { h.emailDmarc = "notConfigured"; }, values: [UNKNOWN, UNKNOWN],
    lines: ["Läses inte i den här miljön."], announced: false },
  { state: "misconfigured", set: (h) => { h.emailDmarc = "misconfigured"; }, values: [UNKNOWN, UNKNOWN],
    lines: [`Lösenordet till brevlådan är tomt. ${RUNBOOK}`], announced: true },
  ...([
    ["unreachable", "Unreachable", `Brevlådan gick inte att nå. ${RUNBOOK}`],
    ["timeout", "Timeout", `Brevlådan svarade inte i tid. ${RUNBOOK}`],
    ["tls-failed", "TlsFailed", `Brevlådans certifikat kunde inte verifieras. ${RUNBOOK}`],
    ["protocol", "Protocol", `Brevlådans svar kunde inte läsas. ${RUNBOOK}`],
    ["auth-refused", "AuthRefused", resumes("Brevlådan nekade inloggningen\\.")],
    ["auth-unconfirmed", "AuthUnconfirmed", resumes("Lösenordet skickades, men brevlådan gav inget svar\\.")],
    ["incomplete", "Incomplete", new RegExp(`^Den första inläsningen av brevlådan började ${MINUTE} och har inte avslutats\\. ${RUNBOOK}$`)],
  ] as const).map(([state, failure, line]) => ({
    state: `failed-${state}`,
    set: (h: Harness) => { h.emailDmarc = "failed"; h.dmarcFailure = failure; },
    values: [UNKNOWN, UNKNOWN],
    lines: [line],
    announced: true,
  })),
  { state: "our-read-failed", set: (h) => { h.overviewReads.dmarc = "error"; }, values: [UNKNOWN, UNKNOWN],
    lines: [OUR_READ_FAILED], announced: true },
  { state: "refused", set: (h) => { h.overviewReads.dmarc = "forbidden"; }, values: [UNKNOWN, UNKNOWN],
    lines: [REFUSED], announced: true },
];

test.describe("DMARC-rapporter (#1981 PR 4b)", () => {
  test("is a section of its own after Leverans, with the summary, six columns and the address as each row's header", async ({ page }) => {
    await page.goto(PAGE);

    await expect(page.locator("main").getByRole("heading", { level: 2 })).toHaveText([/^Utskick (i |denna)/, "Leverans", "DMARC-rapporter"]);
    await expect(leverans(page).getByRole("region", { name: "DMARC-rapporter" })).toHaveCount(0);
    await expect(dmarc(page).getByRole("term")).toHaveText(["Meddelanden", "DMARC-underkända"]);
    await expect(dmarc(page).getByRole("definition")).toHaveText(["51", "3"]);
    await expect(dmarcTable(page).getByRole("columnheader")).toHaveText([
      "IP-adress", "Avsändardomän", "Meddelanden", "DKIM-godkända", "SPF-godkända", "DMARC-underkända",
    ]);
    await expect(dmarcTable(page).getByRole("rowheader")).toHaveText(["198.51.100.7", "192.0.2.10", DMARC_LONG_IPV6]);
    await expect(dmarcTable(page).locator("tbody tr")).toHaveCount(3);
    await expect(dmarc(page).locator(".jp-admin-danger")).toHaveText(["3", "3"]);
    await expect(dmarc(page).locator("a, [href]")).toHaveCount(0);
    await expect(dmarc(page)).not.toContainText(/Skickade|Överlämnade|Kommer snart/);
    await expect(dmarc(page).locator(".jp-adminmailsum__note p").nth(1)).toHaveText(DMARC_FIXED);
    expect(harness.dmarcQueries).toEqual(["/api/v1/admin/email/dmarc"]);
    record("epost-dmarc-aria.txt", await dmarc(page).ariaSnapshot());
  });

  for (const expected of DMARC_STATES) {
    test(`renders the ${expected.state} state at 1280 and 375 px in both themes, never a zero it did not observe`, async ({ page }) => {
      expected.set(harness);
      await verifyEverywhere(page, PAGE, `dmarc-${expected.state}`, async (current) => {
        await expect(dmarc(current).getByRole("definition")).toHaveText([...expected.values]);
        await expect(dmarcLines(current)).toHaveText([...expected.lines]);
        const own = alerts(current).filter({ hasText: /^DMARC-rapporter: / });
        await expect(own).toHaveCount(expected.announced ? 1 : 0);
        if (expected.announced) await expect(own).toHaveText(new RegExp(`^DMARC-rapporter: ${escape(expected.lines[0])}`));
        // The column heads always stay; without figures the table holds one line and no note.
        await expect(dmarcTable(current).getByRole("columnheader")).toHaveCount(6);
        if (expected.values[0] === UNKNOWN) {
          await expect(dmarcTable(current).locator("tbody tr")).toHaveText(["Uppgift saknas"]);
          await expect(dmarc(current).locator(".jp-adminmailsum__note")).toHaveCount(0);
          await expect(dmarcTable(current)).toHaveAccessibleName("DMARC-resultat per IP-adress");
        } else {
          await expect(dmarc(current).locator(".jp-adminmailsum__note p").nth(1)).toHaveText(DMARC_FIXED);
        }
        // Leverans is untouched by the DMARC region's state, and the reverse.
        await expect(leverans(current).getByRole("definition")).toHaveText(["384", "5"]);
      });
    });
  }

  test("says how many rows it shows of how many at the cap", async ({ page }) => {
    harness.emailDmarc = "capped";
    await page.goto(PAGE);
    await expect(dmarcTable(page).locator("tbody tr")).toHaveCount(25);
    await expect(dmarc(page).getByText("Visar 25 av 31 rader.", { exact: true })).toBeVisible();
    await expect(dmarcTable(page).getByRole("row").filter({ hasText: DMARC_LONG_DOMAIN })).toHaveCount(1);
  });

  test("gives two rows that share an address distinct names", async ({ page }) => {
    harness.emailDmarc = "sharedAddress";
    await page.goto(PAGE);
    const heads = dmarcTable(page).getByRole("rowheader");
    // The address sits in an inline-block, so Chromium's name puts a space before the hidden ", domain"; the names
    // differ, which is the rule (test-writer F10).
    await expect(heads.nth(0)).toHaveAccessibleName(/^198\.51\.100\.7 ?, jobbliggaren\.se$/);
    await expect(heads.nth(1)).toHaveAccessibleName(/^198\.51\.100\.7 ?, mail\.jobbliggaren\.se$/);
    await expect(heads.nth(2)).toHaveAccessibleName("192.0.2.10");
  });

  test("shows a span across the change to winter time in each instant's own offset", async ({ page }) => {
    harness.emailDmarc = "dst";
    await page.goto(PAGE);
    await expect(dmarc(page).locator(".jp-adminmailsum__note p").first()).toContainText("för tiden 2026-10-24 02:00 till 2026-10-26 01:00.");
    await expect(dmarcTable(page)).toHaveAccessibleName("DMARC-resultat per IP-adress för tiden 2026-10-24 02:00 till 2026-10-26 01:00");
  });

  for (const spaced of [false, true]) test(`keeps a long IPv6 address whole, wrapped at its colons, in a frozen column of at most half the region at 375 px${spaced ? " with 1.4.12 spacing" : ""}`, async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(PAGE);
    if (spaced) await page.addStyleTag({ content: TEXT_SPACING });
    const region = page.getByRole("region", { name: /^DMARC-resultat per IP-adress för tiden/ });
    await region.scrollIntoViewIfNeeded();
    for (const theme of ["light", "dark"] as const) {
      if (theme === "dark") await darken(page);
      await region.evaluate((element) => { element.scrollLeft = 0; });
      const header = dmarcTable(page).getByRole("rowheader").nth(2);
      await expect(header).toHaveAccessibleName(DMARC_LONG_IPV6);
      const shape = await header.evaluate((cell) => {
        const span = cell.querySelector(".jp-admindmarc__addr");
        const range = document.createRange();
        if (span !== null) range.selectNodeContents(span);
        const scroller = cell.closest(".jp-admintable-scroll");
        return {
          width: cell.getBoundingClientRect().width,
          region: scroller?.getBoundingClientRect().width ?? 0,
          lines: new Set([...range.getClientRects()].map((rect) => Math.round(rect.top))).size,
          text: span?.textContent ?? "",
        };
      });
      expect(shape.text).toBe(DMARC_LONG_IPV6);
      expect(shape.lines, "the address wraps at its colons").toBeGreaterThan(1);
      expect(shape.width, "the frozen column stays at most half the region").toBeLessThanOrEqual(shape.region / 2 + 1);
      const atRest = await header.boundingBox();
      await region.evaluate((element) => { element.scrollLeft = element.scrollWidth; });
      await expect.poll(async () => region.evaluate((element) => element.scrollLeft)).toBeGreaterThan(50);
      const regionBox = await region.boundingBox();
      const scrolled = await header.boundingBox();
      expect(scrolled?.x, "the address column stays at the region's start").toBeCloseTo(regionBox?.x ?? -1, 0);
      expect(scrolled?.x).toBeCloseTo(atRest?.x ?? -1, 0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      const directory = screenshotDirectory();
      if (directory !== null) {
        mkdirSync(directory, { recursive: true });
        await region.screenshot({ path: join(directory, `epost-dmarc-sticky-${theme}${spaced ? "-spaced" : ""}-375.png`), animations: "disabled" });
      }
    }
  });

  test("is never read by the overview", async ({ page }) => {
    await page.goto("/admin");
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    expect(harness.dmarcQueries).toEqual([]);
  });
});

/** A line as a pattern: a string matched literally, a pattern by its own source. */
function escape(line: string | RegExp | undefined): string {
  if (line === undefined) return "";
  if (line instanceof RegExp) return line.source.replace(/^\^/, "").replace(/\$$/, "");
  return line.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
