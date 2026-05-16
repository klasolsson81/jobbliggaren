/**
 * Frontend visual verification — temp screenshot-loop.
 *
 * Kör Playwright headless mot en lokal dev-server och tar screenshots av
 * publika sidor i tre viewports (1280 / 1920 / 3440). Bilderna sparas UTANFÖR
 * repot i c:/tmp/jobbpilot-visual/<tidsstämpel>/ och är self-cleaning: vid
 * varje körning raderas ALLA tidigare körningars mappar FÖRST (cleanup får
 * inte vara ett kom-ihåg-steg — se docs/runbooks/frontend-visual-verification.md).
 *
 * Auth-gated sidor screenshottas INTE här (kräver backend/session) — de
 * verifieras vid live-deploy mot dev-backend per runbookens tre-nivå-policy.
 *
 * Kör:  pnpm dev   (separat terminal)
 *       pnpm visual-verify
 */
import { chromium } from "@playwright/test";
import { mkdirSync, rmSync, existsSync } from "node:fs";
import { join } from "node:path";

const BASE_URL = process.env.VISUAL_BASE_URL ?? "http://localhost:3000";
const ROOT = process.env.VISUAL_OUT_ROOT ?? "C:/tmp/jobbpilot-visual";

// Publika sidor — inga auth-gated routes (de saknar backend lokalt).
const PAGES: { path: string; name: string }[] = [
  { path: "/", name: "landing" },
  { path: "/logga-in", name: "logga-in" },
  { path: "/registrera", name: "registrera" },
  { path: "/vantelista", name: "vantelista" },
];

const VIEWPORTS = [
  { w: 1280, h: 800, tag: "1280" },
  { w: 1920, h: 1080, tag: "1920" },
  { w: 3440, h: 1440, tag: "3440" },
];

const THEMES = ["light", "dark"] as const;

function timestamp(): string {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}`;
}

async function main(): Promise<void> {
  // Self-cleaning: radera ALLA tidigare körningar innan ny mapp skapas.
  if (existsSync(ROOT)) {
    rmSync(ROOT, { recursive: true, force: true });
  }
  const outDir = join(ROOT, timestamp());
  mkdirSync(outDir, { recursive: true });

  const browser = await chromium.launch();
  let count = 0;

  for (const theme of THEMES) {
    for (const vp of VIEWPORTS) {
      const context = await browser.newContext({
        viewport: { width: vp.w, height: vp.h },
        colorScheme: theme === "dark" ? "dark" : "light",
      });
      const page = await context.newPage();
      for (const target of PAGES) {
        await page.goto(`${BASE_URL}${target.path}`, {
          waitUntil: "networkidle",
        });
        const file = join(outDir, `${target.name}__${theme}__${vp.tag}.png`);
        await page.screenshot({ path: file, fullPage: true });
        count++;
      }
      await context.close();
    }
  }

  await browser.close();
  console.log(
    `[visual-verify] ${count} screenshots → ${outDir}\n` +
      `[visual-verify] Raderas automatiskt vid nästa körning. ` +
      `Auth-gated sidor: verifieras vid live-deploy (se runbook).`,
  );
}

main().catch((err) => {
  console.error("[visual-verify] FEL:", err);
  process.exit(1);
});
