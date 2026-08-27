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
 * is what this replaces. Regenerate that count with:
 *
 *   git grep -ohIE -i '\bi (dag|går)\b' <ref> -- 'web/jobbliggaren-web/messages/sv/*.json'
 *
 * `-i` must come BEFORE the pattern. After the pathspec git reads it as another
 * pathspec, the grep runs case-sensitively, and the count comes back 12 — silently
 * missing `oversikt.json`'s sentence-initial "I dag". That is not hypothetical: it is
 * how the 12 was produced during this PR's own review.
 *
 * The file set is DERIVED from the directory, never listed. `document-title-coverage.test.ts`
 * states the doctrine in this repo: "a list is the silent hole". A catalogue added later is
 * swept without anyone remembering to extend this file.
 *
 * `messages/en/` is not swept: it spells the word "today", so it has no form to diverge.
 */

const HERE = dirname(fileURLToPath(import.meta.url));
const SV = join(HERE, "../../../messages/sv");

// `\b` before `i` rejects "vi dag…"; `\b` after `dag`/`går` rejects "i dagar", "1 dag".
// Case-insensitive so the sentence-initial "I dag" — the site the original #1168 sweep
// missed, because it grepped lowercase only — cannot slip back in.
// No `g` flag, deliberately: `.test()` on a global regex is stateful via `lastIndex` and
// would return alternating answers across calls.
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
  it("matches what it claims to match, and rejects what it claims to reject", () => {
    // Positive control on the pattern that carries the invariant. Without it, a typo in
    // SPACED leaves `offenders` empty for the WRONG reason and the suite stays green:
    // the vacuity floor below keys on CLOSED, so it would not notice
    // (dotnet-architect, 2026-08-26).
    expect(SPACED.test("Väntetiden räknas om från i dag")).toBe(true);
    expect(SPACED.test("Senast ändrad i går")).toBe(true);
    expect(SPACED.test("I dag")).toBe(true);

    // The word boundaries the comment above claims, asserted rather than asserted-by-comment.
    for (const notAMatch of ["1 dag", "i dagar", "vi dagar", "i dagsläget", "# dgr"]) {
      expect(SPACED.test(notAMatch)).toBe(false);
    }
  });

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
