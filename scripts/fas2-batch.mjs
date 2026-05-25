#!/usr/bin/env node
// F-Pre Punkt 6 Fas 2 — förfining av Klas-valda arketyper från Fas 1-rev1.
// Klas-val 2026-05-25 STOPP B-rev1: vortex (B v2) + kompass-stjärna (D v2).
// 6 förfinings-prompts per arketyp = 12 genereringar. Counter 22 → 34.
// Återstår efter Fas 2: 16 (Fas 3: 10, buffer: 6).

import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const SCRIPT = path.join(__dirname, "generate-brand-assets.mjs");

// --- VORTEX-förfining (Skatteverket-anda) -----------------------------------

const VORTEX_BASE = "Professional Swedish civic-service logotype on plain white background. To the left sits an abstract geometric vortex or pinwheel symbol mark, with the wordmark JobbPilot in deep navy blue (#0A2647) modern sans-serif typeface to the right, vertically centered to the symbol. Lockup similar in spirit to Skatteverket, the Swedish Tax Agency. Sharp clean vector aesthetic, civic-utility tone, official Swedish public-agency identity. No gradients, no glow, no shadows, no 3D, no decorative ornaments, no emoji, no soft blurry shapes.";

const VORTEX_PROMPTS = [
  ["fas2-vortex-4blade-sharp", `${VORTEX_BASE} The symbol is constructed from EXACTLY 4 sharp angular blade shapes radiating from a small dark center point, rotational symmetry, in two solid colors of deep navy (#0A2647) and warm Swedish yellow (#FFCD00) alternating. Each blade is the same shape and size. Clean straight edges, NOT curved or blob-like. Crisp geometric pinwheel.`],
  ["fas2-vortex-3blade-open", `${VORTEX_BASE} The symbol is constructed from EXACTLY 3 curved blade shapes radiating from a small dark center, rotational symmetry, with generous white space between them, in deep navy (#0A2647) and warm Swedish yellow (#FFCD00). Clean smooth curves but not blob-like. Open, balanced, breathable composition.`],
  ["fas2-vortex-4blade-green", `${VORTEX_BASE} The symbol is constructed from EXACTLY 4 angular blade shapes radiating from a small dark center, rotational symmetry, in two solid colors of deep navy (#0A2647) and muted leaf green (#2C8A3F) alternating. Clean straight edges. Swedish institutional palette.`],
  ["fas2-vortex-pinwheel", `${VORTEX_BASE} The symbol is a clean geometric pinwheel constructed from 4 identical right-triangle blades meeting at a center point, rotational symmetry, in solid deep navy (#0A2647) and warm Swedish yellow (#FFCD00) alternating. Strict geometric construction, like a pinwheel toy or weather-vane viewed flat.`],
  ["fas2-vortex-circular", `${VORTEX_BASE} The symbol is a circular medallion containing a 4-blade vortex inside, with a thin navy outer ring, blades alternating navy (#0A2647) and warm Swedish yellow (#FFCD00), small white center dot. Crisp geometric construction, no blur, no decorative shapes.`],
  ["fas2-vortex-twobladecurl", `${VORTEX_BASE} The symbol is constructed from EXACTLY 2 large curling blade shapes that interlock around a center point, in two solid colors deep navy (#0A2647) and warm Swedish yellow (#FFCD00). Clean smooth curves with a clear interlocking yin-yang-style composition but more geometric and angular. NOT blob-shapes.`],
];

// --- COMPASS-förfining (4-point star, navigation-metafor) -------------------

const COMPASS_BASE = "Professional Swedish civic-service logotype on plain white background. To the left sits an abstract geometric compass-star symbol mark, the SAME HEIGHT as the wordmark next to it. To the right sits the wordmark JobbPilot in deep navy blue (#0A2647) modern sans-serif typeface, vertically centered to the symbol. Sharp clean vector aesthetic, civic-utility tone, official Swedish public-agency identity, restrained and institutional. No gradients, no glow, no shadows, no 3D, no decorative ornaments, no emoji.";

const COMPASS_PROMPTS = [
  ["fas2-compass-4point-equal", `${COMPASS_BASE} The compass-star symbol has EXACTLY 4 equal-sized pointed diamond shapes radiating up/down/left/right from a small white center dot, all 4 points in solid deep navy (#0A2647). Clean angular construction, sharp tips, no curves. Strict 4-fold symmetry.`],
  ["fas2-compass-4point-twotone", `${COMPASS_BASE} The compass-star symbol has 4 equal-sized pointed diamond shapes radiating up/down/left/right from a small center dot, with the top and bottom diamonds in deep navy (#0A2647) and the left and right diamonds in muted leaf green (#2C8A3F). Sharp tips, clean angular construction, strict 4-fold symmetry.`],
  ["fas2-compass-4point-thin", `${COMPASS_BASE} The compass-star symbol has 4 long thin elegant diamond points radiating up/down/left/right from a small white center, in solid deep navy (#0A2647). Each diamond is twice as long as it is wide, giving an elegant slender compass-rose appearance. Crisp construction.`],
  ["fas2-compass-4point-accent", `${COMPASS_BASE} The compass-star symbol has 4 equal-sized pointed diamond shapes radiating from a center, with a single accent dot in warm Swedish yellow (#FFCD00) in the center surrounded by white space. The 4 diamonds are in solid deep navy (#0A2647). Sharp angular construction.`],
  ["fas2-compass-4point-bluyellow", `${COMPASS_BASE} The compass-star symbol has 4 equal-sized pointed diamond shapes radiating up/down/left/right, with the top and bottom in deep navy (#0A2647) and the left and right in warm Swedish yellow (#FFCD00). Sharp tips, clean angular construction, strict 4-fold symmetry. Swedish flag palette.`],
  ["fas2-compass-4point-innerring", `${COMPASS_BASE} The compass-star symbol has 4 equal-sized pointed diamond shapes radiating from a center, with a thin navy outline circle around the center point, in solid deep navy (#0A2647). Clean angular construction, like a navigation rose-of-the-winds, restrained Swedish civic identity.`],
];

const RUNS = [...VORTEX_PROMPTS, ...COMPASS_PROMPTS].map(([id, p]) => ["fas2", id, p, "1:1"]);

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
    if (i < RUNS.length - 1) {
      console.log("  ...sleeping 12s (rate-limit avoidance)");
      await new Promise((r) => setTimeout(r, 12000));
    }
  }
  console.log("\n✓ Fas 2 klar.");
}

main().catch((err) => {
  console.error("BATCH ERROR:", err.message);
  process.exit(1);
});
