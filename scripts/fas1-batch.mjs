#!/usr/bin/env node
// F-Pre Punkt 6 Fas 1 batch — kör resterande 9 genereringar sekventiellt.
// Prompt A redan kört v1 (1/30 använd). Återstår: A v2, A v3, B v1-3, C v1-2, D v1-2.

import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const SCRIPT = path.join(__dirname, "generate-brand-assets.mjs");

const PROMPT_A = "Minimalist Swedish public-sector logotype on plain white background. Bold geometric capital letter J in deep navy blue (#0A2647), constructed from clean straight lines and a single right-angled hook at the bottom. Letter sits beside the wordmark JobbPilot in a sturdy modern sans-serif typeface, navy color, tight letter-spacing. Letter J mark is roughly the same height as the wordmark cap-height. Treatment is restrained, civic, institutional, similar in spirit to GOV.UK or Swedish 1177 Vardguiden branding. No gradients, no glow, no shadows, no decorative flourishes, no 3D, no emoji, no abstract symbols. Studio product-render on clean white background, sharp vector aesthetic, looks like an SVG logo asset for an official Swedish government-style service.";

const PROMPT_B = "Swedish civic service brand mark on plain white. A solid rounded-square badge in deep navy (#0A2647) approximately 64 by 64 pixels with a clean white capital letter J centered inside, drawn with even stroke weight and a subtle hook at the bottom. To the right of the badge sits the wordmark JobbPilot in dark navy modern sans-serif similar to Inter or Hanken Grotesk, aligned to the badge vertical center. Quiet, official, reliable, civic-utility tone reminiscent of Swedish public agencies. No gradients, no glow, no neon, no abstract icons, no decorative motifs. Logo asset on white studio background.";

const PROMPT_C = "Clean modern logotype for a Swedish jobs-application service. The capital letter J is drawn in the same humanist sans-serif style as the wordmark next to it, Hanken Grotesk inspired, with consistent stroke weight and a clean curved hook at the bottom, no serifs. Both the J mark and the wordmark JobbPilot are deep navy (#0A2647) on a flat white background. The J mark is the same x-height as the wordmark. The full lockup reads as one harmonious typographic unit, civic and trustworthy, like a Swedish public service logo. No decorative elements, no gradients, no glow, no extra graphics.";

const PROMPT_D_PLAIN = "Swedish civic-service brand mark on plain white background. Capital letter J in deep navy (#0A2647), modern sans-serif, with a subtle thin horizontal baseline-line beneath it extending the J width, no other ornamentation. Beside the J sits the wordmark JobbPilot in matching navy sans-serif. Layout is balanced, quiet, official. Inspired by Swedish public agency branding (Digg, 1177). No gradients, no glow, no symbols, no abstract shapes, no emoji.";

const PROMPT_D_ACCENT = "Swedish civic-service brand mark on plain white background. Capital letter J in deep navy (#0A2647), modern sans-serif, with a single thin horizontal underline beneath it in muted Swedish-leaf green (#2C8A3F) extending the J width, no other ornamentation. Beside the J sits the wordmark JobbPilot in matching deep navy sans-serif. Layout is balanced, quiet, official, with the green underline as the only color accent. Inspired by Swedish public agency branding (Digg, 1177 Vardguiden) using restrained Swedish national palette. No gradients, no glow, no symbols, no abstract shapes, no emoji.";

// Counter står på 2/30. promptA-v1 + promptA-v2 redan klara.
// Återstår 8 för Fas 1 totalt 10.
const RUNS = [
  ["fas1", "promptA-v3", PROMPT_A, "1:1"],
  ["fas1", "promptB-v1", PROMPT_B, "1:1"],
  ["fas1", "promptB-v2", PROMPT_B, "1:1"],
  ["fas1", "promptB-v3", PROMPT_B, "1:1"],
  ["fas1", "promptC-v1", PROMPT_C, "1:1"],
  ["fas1", "promptC-v2", PROMPT_C, "1:1"],
  ["fas1", "promptD-plain", PROMPT_D_PLAIN, "1:1"],
  ["fas1", "promptD-accent", PROMPT_D_ACCENT, "1:1"],
];

function runChild(args) {
  return new Promise((resolve, reject) => {
    const child = spawn("node", [SCRIPT, ...args], { stdio: "inherit" });
    child.on("close", (code) => {
      if (code === 0) resolve();
      else reject(new Error(`Child exited ${code}`));
    });
  });
}

async function main() {
  for (let i = 0; i < RUNS.length; i++) {
    const [phase, promptId, prompt, aspect] = RUNS[i];
    console.log(`\n=== Batch ${i + 1}/${RUNS.length}: ${phase}/${promptId} ===`);
    await runChild([phase, promptId, prompt, "1", aspect]);
    // Replicate reduced-tier rate-limit = 6 req/min = 10s/req. 12s för marginal.
    if (i < RUNS.length - 1) {
      console.log("  ...sleeping 12s (rate-limit avoidance)");
      await new Promise((r) => setTimeout(r, 12000));
    }
  }
  console.log("\n✓ Fas 1 klar.");
}

main().catch((err) => {
  console.error("BATCH ERROR:", err.message);
  process.exit(1);
});
