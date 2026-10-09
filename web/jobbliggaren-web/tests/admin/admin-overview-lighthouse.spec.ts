import { chromium, expect, test, type Browser } from "@playwright/test";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { APP_ORIGIN, HARNESS_PORTS, SESSION_COOKIE, SESSION_ID, startHarness } from "./servers";

type Audit = { readonly numericValue?: number; readonly details?: { readonly items?: ReadonlyArray<Record<string, unknown>> } };
type Report = {
  readonly requestedUrl: string;
  readonly finalDisplayedUrl: string;
  readonly categories: Readonly<Record<string, { readonly score: number | null }>>;
  readonly audits: Readonly<Record<string, Audit>>;
};
const median = (values: ReadonlyArray<number>) => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)] ?? 0;

test("overview and registration directory meet local Lighthouse budgets", async () => {
  test.setTimeout(300_000);
  const harness = await startHarness();
  let browser: Browser | undefined;
  const summaries: unknown[] = [];
  const observations: unknown[] = [];
  try {
    const cliRequire = createRequire(require.resolve("@lhci/cli/package.json"));
    const localRequire = createRequire(cliRequire.resolve("lighthouse"));
    const lighthouse = (await import(pathToFileURL(cliRequire.resolve("lighthouse")).href) as {
      default: (url: string, options: unknown, config: unknown) => Promise<{ lhr: Report }>;
    }).default;
    const desktopConfig = (await import(pathToFileURL(localRequire.resolve("./config/desktop-config.js")).href) as { default: unknown }).default;
    const port = HARNESS_PORTS.backend + 1;
    browser = await chromium.launch({ headless: true,
      args: ["--ignore-certificate-errors", "--remote-debugging-port=" + port] });
    const config = JSON.parse(readFileSync(join(__dirname, "../../lighthouserc.json"), "utf8")) as {
      ci: { assert: { assertions: Record<string, unknown> } };
    };
    const connected = await chromium.connectOverCDP("http://127.0.0.1:" + port);
    const context = connected.contexts()[0];
    if (!context) throw new Error("Lighthouse requires the default Chromium context");
    await context.addCookies([{ name: SESSION_COOKIE, value: SESSION_ID, url: APP_ORIGIN,
      secure: true, httpOnly: true, sameSite: "Strict" }]);
    const control = await context.newPage();
    for (const path of ["/admin", "/admin/anvandare?registeredFrom=2026-09-01T22%3A00%3A00Z&registeredBefore=2026-10-08T10%3A00%3A00Z"]) {
      await control.goto(APP_ORIGIN + path);
      if (path === "/admin") {
        await expect(control.getByRole("region", { name: "Användare totalt", exact: true })
          .getByRole("link", { name: "5", exact: true })).toBeVisible();
        await expect(control.getByRole("region", { name: "Senaste händelser", exact: true }))
          .toContainText("Application.StatusTransitioned");
        await expect(control.getByRole("region", { name: "Kräver uppmärksamhet", exact: true })
          .getByRole("link", { name: "1 bakgrundsjobb har misslyckats" })).toBeVisible();
      } else {
        await expect(control.getByRole("table", { name: "Konton" })).toContainText("konto.b@example.test");
        await expect(control.getByRole("button", { name: "Rensa period" })).toBeVisible();
      }
      await control.goto("about:blank");
      const requiredReads = path === "/admin"
        ? ["GET /api/v1/admin/overview/accounts", "GET /api/v1/admin/audit-log", "GET /api/v1/admin/jobs/failed"]
        : ["POST /api/v1/admin/accounts/search"];
      const reports: Report[] = [];
      for (let run = 0; run < 3; run++) {
        const requestStart = harness.requests.length;
        const report = (await lighthouse(APP_ORIGIN + path, {
          port,
          onlyCategories: ["performance", "accessibility", "best-practices"],
          formFactor: "desktop",
          screenEmulation: { mobile: false, width: 1280, height: 900, deviceScaleFactor: 1, disabled: false },
          throttlingMethod: "simulate",
          disableStorageReset: false,
          clearStorageTypes: ["file_systems", "shader_cache", "service_workers", "cache_storage"],
          logLevel: "error",
        }, desktopConfig)).lhr;
        reports.push(report);
        const reads = harness.requests.slice(requestStart);
        observations.push({ path, run, normalContentPreflight: true, requestedUrl: report.requestedUrl,
          finalDisplayedUrl: report.finalDisplayedUrl, requiredReads: requiredReads.filter(route => reads.includes(route)),
          scriptResources: report.audits["network-requests"]?.details?.items?.filter(row => row.resourceType === "Script")
            .map(row => ({ url: row.url, transferSize: row.transferSize })) });
        expect(report.requestedUrl).toBe(APP_ORIGIN + path);
        expect(report.finalDisplayedUrl).toBe(APP_ORIGIN + path);
        for (const route of requiredReads) expect(reads).toContain(route);
      }
      const numeric = (id: string) => median(reports.map(report => report.audits[id]?.numericValue ?? Number.POSITIVE_INFINITY));
      const scores = Object.fromEntries(["performance", "accessibility", "best-practices"].map(key =>
        [key, median(reports.map(report => report.categories[key]?.score ?? 0))]));
      const resources = reports.map(report => {
        const rows = report.audits["resource-summary"]?.details?.items;
        if (!Array.isArray(rows)) throw new Error("Missing Lighthouse resource observations");
        return rows;
      });
      const measured = Object.fromEntries(Object.entries(config.ci.assert.assertions)
        .filter(([id, rule]) => id.startsWith("resource-summary:") && Array.isArray(rule))
        .map(([id, rule]) => {
          const [, resourceType, field] = id.split(":");
          const budget = (rule as [string, { maxNumericValue: number }])[1].maxNumericValue;
          const value = median(resources.map(rows => {
            const row = rows.find(item => item.resourceType === resourceType);
            const observed = row?.[field === "size" ? "transferSize" : "requestCount"];
            if (typeof observed !== "number" || !Number.isFinite(observed) || observed < 0)
              throw new Error("Missing or invalid Lighthouse resource observation: " + id);
            return observed;
          }));
          return [id, { value, budget }];
        }));
      summaries.push({ path, finalDisplayedUrls: reports.map(report => report.finalDisplayedUrl), runs: 3, scores, lcp: numeric("largest-contentful-paint"),
        cls: numeric("cumulative-layout-shift"), tbt: numeric("total-blocking-time"), resources: measured });
      expect(scores.performance).toBeGreaterThan(0.9);
      expect(scores.accessibility).toBe(1);
      expect(scores["best-practices"]).toBeGreaterThan(0.9);
      expect(numeric("largest-contentful-paint")).toBeLessThanOrEqual(2500);
      expect(numeric("cumulative-layout-shift")).toBeLessThanOrEqual(0.1);
      for (const [id, { value, budget }] of Object.entries(measured) as [string, { value: number; budget: number }][])
        expect.soft(value, id).toBeLessThanOrEqual(budget);
    }
    expect(harness.misses).toEqual([]);
  } finally {
    try {
      const directory = process.env.ADMIN_OVERVIEW_SCREENSHOT_DIR;
      if (directory) {
        mkdirSync(directory, { recursive: true });
        writeFileSync(join(directory, "lighthouse.json"), JSON.stringify({ measuredAt: new Date().toISOString(),
          method: "authenticated default Chromium context; secure synthetic cookie; normal-content preflight and backend-read witnesses; bundled Lighthouse cache/origin reset on each audited target; cookies retained; bundled Lighthouse desktop preset; 1280x900; simulated desktop throttling; three-run medians",
          observations, summaries, misses: harness.misses }, null, 2) + String.fromCharCode(10));
      }
    } finally {
      try { await browser?.close(); }
      finally { await harness.stop(); }
    }
  }
});
