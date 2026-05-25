#!/usr/bin/env node
// F-Pre Punkt 6 Fas 1-rev1 batch — symbol-led pivot efter Klas-override 2026-05-25.
// 6 prompts × 2 variants = 12 genereringar. Counter står på 10/50 (10 sunk).
// Slut-counter efter Fas 1-rev1: 22/50. Återstår 28 (Fas 2: 12, Fas 3: 10, buffer: 6).
//
// Referens-rama (Klas-screenshots 2026-05-25):
// - Arbetsförmedlingen: lime-grön öppen ring + navy wordmark
// - Försäkringskassan: stiliserad vit symbol i mörkgrön box
// - Skatteverket: blå+gul vortex/spiral + navy wordmark
//
// DNA: abstrakt geometrisk symbol-mark + wordmark "JobbPilot" till höger,
// flerfärgad palett (navy + svensk leaf-grön ELLER navy + svensk blå-gul),
// civic-utility-ton, INGA gradients/glow/3D/AI-cliché. Symbol till vänster
// om wordmark, ungefär samma height (Arbetsförmedlingen-typ-lockup).

import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const SCRIPT = path.join(__dirname, "generate-brand-assets.mjs");

// PROMPT A — Öppen-ring (Arbetsförmedlingen-paritet: möjlighet/förbindelse)
const PROMPT_A = "Professional Swedish public-service logotype on plain white background. An abstract geometric symbol mark on the left: an open circular ring drawn with even thick stroke, in muted leaf green (#2C8A3F), with a small clean opening on the right side that suggests connection or opportunity. To the right of the ring sits the wordmark JobbPilot in dark navy (#0A2647) modern sans-serif typeface, all caps or title case, vertically aligned to the symbol center. Lockup similar in spirit to Arbetsformedlingen, the Swedish Public Employment Service, with a green ring and navy wordmark. No gradients, no glow, no shadows, no 3D, no decorative ornaments, no emoji, no abstract pile of shapes. Studio render, sharp vector aesthetic, official Swedish civic-service identity.";

// PROMPT B — Stiliserad vortex/spiral (Skatteverket-paritet: insamling/process)
const PROMPT_B = "Professional Swedish civic-service logotype on plain white background. An abstract geometric symbol mark on the left: a stylized vortex or spiral motif constructed from four to six curved blade-like shapes radiating from a center point, in two colors of deep navy blue (#0A2647) and warm Swedish yellow (#FFCD00), suggesting flow or convergence. To the right of the symbol sits the wordmark JobbPilot in deep navy modern sans-serif typeface. Symbol and wordmark vertically centered to each other. Lockup similar in spirit to Skatteverket, the Swedish Tax Agency. No gradients, no glow, no shadows, no 3D, no decorative ornaments, no emoji. Sharp vector aesthetic, civic-utility tone, official Swedish public-agency identity.";

// PROMPT C — Sköld/badge med geometrisk innehåll (Försäkringskassan-paritet)
const PROMPT_C = "Professional Swedish civic-service logotype. A solid rounded-square badge on the left in deep forest green (#1F5C3A), approximately the same height as the wordmark, containing a clean white abstract geometric mark inside that suggests a stylized path or rising arrow (not a letter, not an emoji, not a pile of shapes). To the right of the green badge sits the wordmark JobbPilot in deep navy (#0A2647) modern sans-serif typeface on white background. Layout balanced, official, civic. Lockup similar in spirit to Forsakringskassan, the Swedish Social Insurance Agency. No gradients, no glow, no decorative ornaments, no 3D, no emoji.";

// PROMPT D — Kompass-stjärna (navigation utan pil-cliché)
const PROMPT_D = "Professional Swedish civic-service logotype on plain white background. An abstract geometric symbol mark on the left: a stylized four-pointed compass-star constructed from clean straight lines and one inner circle, in two colors of deep navy (#0A2647) for the outer star and muted leaf green (#2C8A3F) for the inner circle, suggesting navigation and direction. To the right of the symbol sits the wordmark JobbPilot in deep navy modern sans-serif typeface, vertically centered to the symbol. Symbol is NOT a pointed arrow, NOT a chevron, NOT a paper-plane. Sharp vector aesthetic, official Swedish civic-utility tone, restrained, institutional. No gradients, no glow, no 3D, no decorative ornaments, no emoji.";

// PROMPT E — Karriär-trappa / stegrande path (rutt-metafor)
const PROMPT_E = "Professional Swedish civic-service logotype on plain white background. An abstract geometric symbol mark on the left: three to four ascending rectangular blocks of increasing height forming a staircase or step-pattern motif, drawn with clean straight lines, in deep navy (#0A2647) with the tallest top block in muted leaf green (#2C8A3F), suggesting career progress or growth. To the right of the symbol sits the wordmark JobbPilot in deep navy modern sans-serif typeface, vertically centered. Lockup civic, official, restrained. No gradients, no glow, no 3D, no decorative ornaments, no emoji, no abstract piles.";

// PROMPT F — Stiliserad portal/dörr (möjlighet-metafor)
const PROMPT_F = "Professional Swedish civic-service logotype on plain white background. An abstract geometric symbol mark on the left: a clean rounded-rectangular portal or doorway shape with a thin opening on the right side, in two colors of deep navy (#0A2647) outline and a soft warm Swedish yellow (#FFCD00) inner fill, suggesting an opening to opportunity. Approximately the same height as the wordmark. To the right of the portal sits the wordmark JobbPilot in deep navy modern sans-serif typeface, vertically centered. Sharp vector aesthetic, civic-utility tone, official Swedish public-agency identity. No gradients, no glow, no 3D, no decorative ornaments, no emoji.";

// 6 prompts × 2 variants = 12 genereringar
const RUNS = [
  ["fas1rev1", "promptA-ring-v1", PROMPT_A, "1:1"],
  ["fas1rev1", "promptA-ring-v2", PROMPT_A, "1:1"],
  ["fas1rev1", "promptB-vortex-v1", PROMPT_B, "1:1"],
  ["fas1rev1", "promptB-vortex-v2", PROMPT_B, "1:1"],
  ["fas1rev1", "promptC-badge-v1", PROMPT_C, "1:1"],
  ["fas1rev1", "promptC-badge-v2", PROMPT_C, "1:1"],
  ["fas1rev1", "promptD-compass-v1", PROMPT_D, "1:1"],
  ["fas1rev1", "promptD-compass-v2", PROMPT_D, "1:1"],
  ["fas1rev1", "promptE-stairs-v1", PROMPT_E, "1:1"],
  ["fas1rev1", "promptE-stairs-v2", PROMPT_E, "1:1"],
  ["fas1rev1", "promptF-portal-v1", PROMPT_F, "1:1"],
  ["fas1rev1", "promptF-portal-v2", PROMPT_F, "1:1"],
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
    if (i < RUNS.length - 1) {
      console.log("  ...sleeping 12s (rate-limit avoidance)");
      await new Promise((r) => setTimeout(r, 12000));
    }
  }
  console.log("\n✓ Fas 1-rev1 klar.");
}

main().catch((err) => {
  console.error("BATCH ERROR:", err.message);
  process.exit(1);
});
