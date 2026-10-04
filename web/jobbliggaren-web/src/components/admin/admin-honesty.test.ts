import { describe, it, expect } from "vitest";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * The admin surface claims nothing it has not observed (ADR 0150 D2/D8). The design handoff it
 * was built from carries stale infrastructure names and fabricated health, delivery and backup
 * figures; this guard keeps them out of the admin copy and the admin source.
 *
 * Reads source text. It does not prove a rendered page is honest — the page tests do that per
 * state — it stops the handoff's vocabulary from reaching the copy at all: a provider named before
 * an outcome from it is shown, a percentage before one is measured, a retention or statutory claim
 * the product does not make.
 */

const HERE = dirname(fileURLToPath(import.meta.url));
const SRC = resolve(HERE, "..", "..");
const WEB = resolve(SRC, "..");

const DENYLIST: ReadonlyArray<{ pattern: RegExp; why: string }> = [
  // Word-bounded: "administrator" carries the same five letters.
  { pattern: /\bstrato\b/i, why: "a provider named without an observation behind it" },
  { pattern: /\bsmtp\b/i, why: "the product sends over Scaleway's HTTPS API (ADR 0131), never SMTP" },
  { pattern: /netcup|nürnberg/i, why: "a host named without an observation behind it" },
  { pattern: /\bseq\b/i, why: "a log source and retention the page does not read" },
  { pattern: /\d\s?%/, why: "a percentage before one is measured" },
  { pattern: /artikel 17|article 17/i, why: "a statutory claim the product does not make (#1977)" },
];

function filesUnder(dir: string, keep: (file: string) => boolean): string[] {
  const out: string[] = [];
  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) out.push(...filesUnder(path, keep));
    else if (keep(path)) out.push(path);
  }
  return out;
}

const SOURCES = [
  resolve(WEB, "messages", "sv", "admin.json"),
  resolve(WEB, "messages", "en", "admin.json"),
  ...filesUnder(resolve(SRC, "components", "admin"), (f) => /\.tsx?$/.test(f) && !/\.test\./.test(f)),
  ...filesUnder(resolve(SRC, "app", "(admin)"), (f) => /\.tsx?$/.test(f) && !/\.test\./.test(f)),
];

describe("admin honesty guard (ADR 0150 D2/D8)", () => {
  it("reads the admin catalogs and the admin source", () => {
    // Non-vacuity: a collapsed scan would pass against nothing.
    expect(SOURCES.length).toBeGreaterThanOrEqual(12);
  });

  it.each(DENYLIST.map((entry) => [entry.pattern.source, entry] as const))(
    "carries no %s",
    (_, { pattern, why }) => {
      const offenders = SOURCES.filter((file) => pattern.test(readFileSync(file, "utf8")))
        .map((file) => relative(WEB, file));
      expect(offenders, why).toEqual([]);
    },
  );
});
