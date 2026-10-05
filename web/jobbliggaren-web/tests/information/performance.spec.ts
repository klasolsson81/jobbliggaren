import { test, expect, chromium, type Browser } from "@playwright/test";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { createRequire } from "node:module";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { INFORMATION_PATHS } from "../../src/components/information/destinations";
import { APP_ORIGIN, startHarness, type Harness } from "./servers";

test("three Lighthouse runs per information page record the median and repository budgets", async () => {
  test.skip(process.env.INFORMATION_LIGHTHOUSE !== "1", "Opt-in lab measurement; browser tests cover navigation separately.");
  test.setTimeout(1_200_000);
  const cliRequire = createRequire(require.resolve("@lhci/cli/package.json"));
  const directory = process.env.INFORMATION_EVIDENCE_DIR ?? "C:/tmp/jbl-information-evidence/lighthouse";
  mkdirSync(directory, { recursive: true });
  const execute = promisify(execFile);
  const paths = process.env.INFORMATION_LIGHTHOUSE_ROUTE ? INFORMATION_PATHS.filter(path => path === process.env.INFORMATION_LIGHTHOUSE_ROUTE) : INFORMATION_PATHS;
  expect(paths.length).toBeGreaterThan(0);
  let harness: Harness | undefined;
  let chrome: Browser | undefined;
  const results: unknown[] = [];
  try {
    harness = await startHarness(true);
    chrome = await chromium.launch({ executablePath: process.env.CHROME_PATH, args: ["--remote-debugging-port=3153", "--ignore-certificate-errors"] });
    for (const path of paths) {
      const runs: { performance: number; accessibility: number; bestPractices: number; seo: number; lcp: number; cls: number; tbt: number; document: number; script: number; stylesheet: number; image: number; font: number; total: number; thirdParty: number }[] = [];
      for (let run = 1; run <= 3; run++) {
        const output = join(directory, `${path.slice(1)}-${run}.json`);
        await execute(process.execPath, [cliRequire.resolve("lighthouse/cli/index.js"), `${APP_ORIGIN}${path}`, "--quiet", "--output=json", `--output-path=${output}`, "--port=3153"], { timeout: 120_000 });
        const report = JSON.parse(readFileSync(output, "utf8")) as {
          runtimeError?: { message: string };
          categories: Record<string, { score: number }>;
          audits: Record<string, { numericValue: number; details?: { items: { resourceType: string; transferSize: number; requestCount: number }[] } }>;
        };
        expect(report.runtimeError).toBeUndefined();
        const size = (type: string) => report.audits["resource-summary"]?.details?.items.find(item => item.resourceType === type)?.transferSize ?? 0;
        runs.push({
          performance: report.categories.performance!.score * 100,
          accessibility: report.categories.accessibility!.score * 100,
          bestPractices: report.categories["best-practices"]!.score * 100,
          seo: report.categories.seo!.score * 100,
          lcp: report.audits["largest-contentful-paint"]!.numericValue,
          cls: report.audits["cumulative-layout-shift"]!.numericValue,
          tbt: report.audits["total-blocking-time"]!.numericValue,
          document: size("document"), script: size("script"), stylesheet: size("stylesheet"), image: size("image"), font: size("font"), total: size("total"),
          thirdParty: report.audits["resource-summary"]?.details?.items.find(item => item.resourceType === "third-party")?.requestCount ?? 0,
        });
      }
      const median = Object.fromEntries(Object.keys(runs[0]!).map(key => [key, runs.map(run => run[key as keyof typeof run]).sort((a, b) => a - b)[1]]));
      results.push({ path, runs, median });
      writeFileSync(join(directory, "results.json"), JSON.stringify(results, null, 2));
      // Lab budgets remain observe-only per ADR0045. Persist actual failures for review.
      process.stdout.write(`Lighthouse ${path}: ${JSON.stringify(median)}\n`);
    }
    expect(harness.misses).toEqual([]);
  } finally { await chrome?.close(); await harness?.stop(); }
});
