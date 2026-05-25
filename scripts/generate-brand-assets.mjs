#!/usr/bin/env node
// F-Pre Punkt 6 — Replicate Flux 1.1 Pro Ultra brand-asset generator.
// Raw fetch mot Replicate REST API (ingen 'replicate'-npm-dep per Klas-direktiv).
// CTO-dom 2026-05-24-fpre-punkt6 Beslut 5/6: Flux 1.1 Pro Ultra, 30-bild-cap.
//
// Usage:
//   node scripts/generate-brand-assets.mjs <phase> <prompt-id> "<prompt>" [variants] [aspect_ratio]
//
// Hard cap: 30 generations totalt. Räknas via web/jobbpilot-web/public/brand/raw/generations-used.txt.

import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(__dirname, "..");
// Flux-raw-output ligger UTANFÖR Next.js public/ — undviker prod-bundle-bloat
// (security-auditor M1 2026-05-25). Mappen är iteration-historik, ej runtime-asset.
const OUT_DIR = path.join(REPO_ROOT, "tmp/brand-iterations/raw");
const COUNTER = path.join(OUT_DIR, "generations-used.txt");
const HARD_CAP = 50; // Klas-direktiv 2026-05-25: ökat från 30 efter Fas 1-pivot (symbol-led istället för text-led).

const MODEL_PATH = "black-forest-labs/flux-1.1-pro-ultra";
const API_URL = `https://api.replicate.com/v1/models/${MODEL_PATH}/predictions`;

async function readEnvToken() {
  const envPath = path.join(REPO_ROOT, ".env");
  const env = await fs.readFile(envPath, "utf-8");
  for (const line of env.split(/\r?\n/)) {
    const m = line.match(/^\s*REPLICATE_API_TOKEN\s*=\s*(.+?)\s*$/);
    if (m) return m[1].replace(/^['"]|['"]$/g, "");
  }
  throw new Error("REPLICATE_API_TOKEN not found in .env");
}

async function readCounter() {
  try {
    const txt = await fs.readFile(COUNTER, "utf-8");
    return parseInt(txt.trim(), 10) || 0;
  } catch {
    return 0;
  }
}

async function writeCounter(n) {
  await fs.writeFile(COUNTER, String(n) + "\n", "utf-8");
}

async function createPrediction(token, prompt, aspectRatio) {
  const body = {
    input: {
      prompt,
      aspect_ratio: aspectRatio,
      output_format: "png",
      safety_tolerance: 2,
      raw: false,
    },
  };
  for (let attempt = 1; attempt <= 5; attempt++) {
    const res = await fetch(API_URL, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify(body),
    });
    if (res.ok) return res.json();
    const text = await res.text();
    if (res.status === 429) {
      let retryAfter = 12;
      try {
        const parsed = JSON.parse(text);
        if (parsed.retry_after) retryAfter = parsed.retry_after + 11; // extra marginal
      } catch {}
      console.log(`  [429] throttled, sleeping ${retryAfter}s (attempt ${attempt}/5)`);
      await new Promise((r) => setTimeout(r, retryAfter * 1000));
      continue;
    }
    throw new Error(`Create failed ${res.status}: ${text}`);
  }
  throw new Error("Create failed after 5 retries (429)");
}

async function pollPrediction(token, getUrl, maxMs = 180_000) {
  const start = Date.now();
  while (Date.now() - start < maxMs) {
    const res = await fetch(getUrl, {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!res.ok) throw new Error(`Poll failed ${res.status}`);
    const data = await res.json();
    if (data.status === "succeeded") return data;
    if (data.status === "failed" || data.status === "canceled") {
      throw new Error(`Prediction ${data.status}: ${data.error || "unknown"}`);
    }
    await new Promise((r) => setTimeout(r, 2000));
  }
  throw new Error("Prediction timeout (180s)");
}

async function downloadImage(url, outPath) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`Download failed ${res.status}`);
  const buf = Buffer.from(await res.arrayBuffer());
  await fs.writeFile(outPath, buf);
  return buf.length;
}

async function runOne({ token, phase, promptId, prompt, variant, aspectRatio }) {
  const used = await readCounter();
  if (used >= HARD_CAP) {
    throw new Error(`HARD CAP reached: ${used}/${HARD_CAP} generations`);
  }
  const ts = new Date().toISOString().replace(/[:.]/g, "-");
  const tag = `${phase}-${promptId}-v${variant}-${ts}`;
  const outPath = path.join(OUT_DIR, `${tag}.png`);

  console.log(`[${used + 1}/${HARD_CAP}] ${tag} (aspect ${aspectRatio})`);
  console.log(`  prompt: ${prompt.slice(0, 100)}...`);

  const start = Date.now();
  const pred = await createPrediction(token, prompt, aspectRatio);
  const getUrl = pred.urls?.get;
  if (!getUrl) throw new Error("No urls.get in prediction response");

  const result = await pollPrediction(token, getUrl);
  const imageUrl = Array.isArray(result.output) ? result.output[0] : result.output;
  if (!imageUrl) throw new Error("No output URL in result");

  const bytes = await downloadImage(imageUrl, outPath);
  const elapsedMs = Date.now() - start;

  await writeCounter(used + 1);

  const logLine = `${ts} | ${tag} | ${aspectRatio} | ${bytes}B | ${elapsedMs}ms | ${imageUrl}\n`;
  await fs.appendFile(path.join(OUT_DIR, "generation-log.txt"), logLine);

  console.log(`  ✓ ${outPath} (${(bytes / 1024).toFixed(1)}KB, ${(elapsedMs / 1000).toFixed(1)}s)`);
  return { tag, outPath, bytes, elapsedMs };
}

async function main() {
  const [, , phase, promptId, prompt, variantsArg = "1", aspectRatio = "1:1"] = process.argv;
  if (!phase || !promptId || !prompt) {
    console.error('Usage: node generate-brand-assets.mjs <phase> <prompt-id> "<prompt>" [variants] [aspect_ratio]');
    process.exit(1);
  }
  const variants = parseInt(variantsArg, 10) || 1;
  const token = await readEnvToken();

  await fs.mkdir(OUT_DIR, { recursive: true });

  for (let v = 1; v <= variants; v++) {
    await runOne({ token, phase, promptId, prompt, variant: v, aspectRatio });
  }

  const used = await readCounter();
  console.log(`\nTotal generations used: ${used}/${HARD_CAP}`);
}

main().catch((err) => {
  console.error("ERROR:", err.message);
  process.exit(1);
});
