import { chromium, expect, test } from "@playwright/test";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { APP_ORIGIN, HARNESS_PORTS, SESSION_COOKIE, SESSION_ID, startHarness } from "./servers";

type Audit = { readonly numericValue?: number; readonly details?: { readonly items?: ReadonlyArray<Record<string, unknown>> } };
type Report = {
  readonly categories: Readonly<Record<string, { readonly score: number | null }>>;
  readonly audits: Readonly<Record<string, Audit>>;
};
const median = (values: ReadonlyArray<number>) => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)] ?? 0;

test("overview and registration directory meet local Lighthouse budgets", async () => {
  test.setTimeout(300_000);
  const harness = await startHarness();
  const cliRequire = createRequire(require.resolve("@lhci/cli/package.json"));
  const localRequire = createRequire(cliRequire.resolve("lighthouse"));
  const lighthouse = (await import(pathToFileURL(cliRequire.resolve("lighthouse")).href) as {
    default: (url: string, options: unknown, config: unknown) => Promise<{ lhr: Report }>;
  }).default;
  const desktopConfig = (await import(pathToFileURL(localRequire.resolve("./config/desktop-config.js")).href) as { default: unknown }).default;
  const port = HARNESS_PORTS.backend + 1;
  const browser = await chromium.launch({ headless: true,
    args: ["--ignore-certificate-errors", `--remote-debugging-port=${port}`] });
  const config = JSON.parse(readFileSync(join(__dirname, "../../lighthouserc.json"), "utf8")) as {
    ci: { assert: { assertions: Record<string, unknown> } };
  };
  const summaries: unknown[] = [];
  try {
    for (const path of ["/admin", "/admin/anvandare?registeredFrom=2026-09-01T22%3A00%3A00Z&registeredBefore=2026-10-08T10%3A00%3A00Z"]) {
      const reports: Report[] = [];
      for (let run = 0; run < 3; run++) {
        reports.push((await lighthouse(APP_ORIGIN + path, {
          port,
          onlyCategories: ["performance", "accessibility", "best-practices"],
          formFactor: "desktop",
          screenEmulation: { mobile: false, width: 1280, height: 900, deviceScaleFactor: 1, disabled: false },
          throttlingMethod: "simulate",
          extraHeaders: { Cookie: `${SESSION_COOKIE}=${SESSION_ID}` },
          logLevel: "error",
        }, desktopConfig)).lhr);
      }
      const numeric = (id: string) => median(reports.map(report => report.audits[id]?.numericValue ?? Number.POSITIVE_INFINITY));
      const scores = Object.fromEntries(["performance", "accessibility", "best-practices"].map(key =>
        [key, median(reports.map(report => report.categories[key]?.score ?? 0))]));
      const resources = reports.map(report => report.audits["resource-summary"]?.details?.items ?? []);
      const measured = Object.fromEntries(Object.entries(config.ci.assert.assertions)
        .filter(([id, rule]) => id.startsWith("resource-summary:") && Array.isArray(rule))
        .map(([id, rule]) => {
          const [, resourceType, field] = id.split(":");
          const budget = (rule as [string, { maxNumericValue: number }])[1].maxNumericValue;
          const value = median(resources.map(rows => {
            const row = rows.find(item => item.resourceType === resourceType);
            return Number(row?.[field === "size" ? "transferSize" : "requestCount"] ?? 0);
          }));
          return [id, { value, budget }];
        }));
      summaries.push({ path, runs: 3, scores, lcp: numeric("largest-contentful-paint"),
        cls: numeric("cumulative-layout-shift"), tbt: numeric("total-blocking-time"), resources: measured });
      expect(scores.performance).toBeGreaterThan(0.9);
      expect(scores.accessibility).toBe(1);
      expect(scores["best-practices"]).toBeGreaterThan(0.9);
      expect(numeric("largest-contentful-paint")).toBeLessThanOrEqual(2500);
      expect(numeric("cumulative-layout-shift")).toBeLessThanOrEqual(0.1);
      for (const { value, budget } of Object.values(measured) as { value: number; budget: number }[])
        expect(value).toBeLessThanOrEqual(budget);
    }
    expect(harness.misses).toEqual([]);
  } finally {
    const directory = process.env.ADMIN_OVERVIEW_SCREENSHOT_DIR;
    if (directory) {
      mkdirSync(directory, { recursive: true });
      writeFileSync(join(directory, "lighthouse.json"), JSON.stringify({ measuredAt: new Date().toISOString(), method: "bundled Lighthouse desktop preset; 1280x900; simulated desktop throttling; three-run medians", summaries }, null, 2) + "\n");
    }
    await browser.close();
    await harness.stop();
  }
});