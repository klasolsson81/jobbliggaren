import { readFileSync, readdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, it, expect } from "vitest";

/**
 * The Swedish catalogue spells "today" and "yesterday" ONE way (#1168).
 *
 * The form is closed up, `idag` / `igår`, per Klas-direktiv 2026-08-26; the rule itself
 * lives in `.claude/skills/jobbpilot-design-copy/references/locale-formatting.md`.
 *
 * Why a test and not only that rule: the rule was already written, and the divergence
 * shipped anyway. Measured at `3490d4b4`: 13 spaced against 4 closed, with both forms
 * reachable in ONE viewport — `/oversikt` rendered `i dag` from `oversikt.json` while
 * the header above it rendered `nya idag` from `common.json`. A convention with no gate
 * is what this replaces.
 *
 * The file set is DERIVED from the directory, never listed. `document-title-coverage.test.ts`
 * states the doctrine in this repo: "a list is the silent hole". A catalogue added later is
 * swept without anyone remembering to extend this file.
 *
 * LIMIT, named rather than implied: this reaches `messages/sv/` and nothing else. Swedish
 * rendered from outside the catalogue — `lib/guest/mock-data.ts` carries three such labels —
 * is a §5 hardcoded-UI-string concern and is deliberately not gated here.
 *
 * `messages/en/` is not swept: it spells the word "today", so it has no form to diverge.
 */

const SV = join(dirname(fileURLToPath(import.meta.url)), "../../../messages/sv");

// `\b` before `i` rejects "vi dag…"; `\b` after `dag`/`går` rejects "i dagar", "1 dag".
// Case-insensitive so the sentence-initial "I dag" — the site the original #1168 sweep
// missed, because it grepped lowercase only — cannot slip back in.
const SPACED = /\bi (dag|går)\b/iu;
const CLOSED = /\b(idag|igår)\b/iu;

function catalogueFiles(): readonly string[] {
  return readdirSync(SV)
    .filter((f) => f.endsWith(".json"))
    .sort();
}

function offendingLines(file: string, pattern: RegExp): readonly string[] {
  return readFileSync(join(SV, file), "utf8")
    .split(/\r?\n/)
    .map((line, i) => ({ line, n: i + 1 }))
    .filter(({ line }) => pattern.test(line))
    .map(({ line, n }) => `${file}:${n}: ${line.trim()}`);
}

describe("sv catalogue — one spelling of today/yesterday (#1168)", () => {
  it("sweeps a non-empty catalogue that actually carries the word", () => {
    // Vacuity guard: without this, an empty or moved directory makes the assertion
    // below pass while measuring nothing at all.
    const files = catalogueFiles();
    expect(files.length).toBeGreaterThan(5);

    const carrying = files.filter((f) => offendingLines(f, CLOSED).length > 0);
    expect(carrying.length).toBeGreaterThan(0);
  });

  it("spells it closed up everywhere — no `i dag` / `i går`", () => {
    const offenders = catalogueFiles().flatMap((f) => offendingLines(f, SPACED));
    expect(offenders).toEqual([]);
  });
});
