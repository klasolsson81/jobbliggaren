import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { JOBB_AXIS_SEPARATOR } from "./search-params";

/**
 * The load-bearing guard on `/jobb`'s URL axis separator.
 *
 * Since 2026-08-01 each `/jobb` filter axis is ONE query param whose values are
 * joined by {@link JOBB_AXIS_SEPARATOR} (see `search-params.ts` for the
 * router-cache collision that motivates it). That contract is only sound while no
 * value can itself contain the separator: one that did would serialise to a
 * string parsing back as two values, silently WIDENING the filter while the chip
 * still shows the original — no crash, no log, no CI signal. `serializeJobbAxis`
 * drops such a value defensively, but dropping is damage control; this test is
 * the guarantee.
 *
 * **Why the assertion is over the corpus rather than over a hand-built sample.**
 * The hazard does not arrive as code. JobTech publishes no grammar for
 * conceptIds — no charset, no length, no generation rule — so "a `-` cannot
 * occur" and "a `-` can occur" are both unestablished, which is precisely why the
 * separator is `.` (not in base64url at all) rather than `-` (legal in it). The
 * ids enter this repo when someone regenerates these snapshots and commits them
 * as a multi-megabyte diff nobody reads line by line (ADR 0043 Beslut B fixes the
 * cadence as manual regeneration). These files are the sole source of the id
 * space — `TaxonomySnapshotSeeder` is the only writer of `TaxonomyConcept` — so
 * asserting over the whole corpus makes this a TOTAL guard over that space, and
 * makes the next refresh fail HERE rather than in a user's filter.
 *
 * It therefore reads the shipped JSON deliberately rather than a fixture: a
 * fixture would assert a property of a file this repo wrote for itself, which is
 * the one place the hazard cannot come from.
 */

const TAXONOMY_DIR = join(
  process.cwd(),
  "..",
  "..",
  "src",
  "Jobbliggaren.Infrastructure",
  "Taxonomy"
);

const CORPUS_FILES = [
  "taxonomy-snapshot.json",
  "klass2-taxonomy.json",
  "jobad-skill-taxonomy.v30.json",
  "occupation-substitutability.json",
] as const;

/** Every string under a `conceptId`-ish key, at any depth. */
function collectConceptIds(node: unknown, into: Set<string>): void {
  if (Array.isArray(node)) {
    for (const child of node) collectConceptIds(child, into);
    return;
  }
  if (node !== null && typeof node === "object") {
    for (const [key, value] of Object.entries(node as Record<string, unknown>)) {
      if (typeof value === "string" && /conceptid$/i.test(key)) into.add(value);
      else collectConceptIds(value, into);
    }
  }
}

describe("JobTech conceptId corpus vs the /jobb axis separator", () => {
  const idsByFile = new Map<string, Set<string>>();
  for (const file of CORPUS_FILES) {
    const ids = new Set<string>();
    collectConceptIds(
      JSON.parse(readFileSync(join(TAXONOMY_DIR, file), "utf-8")),
      ids
    );
    idsByFile.set(file, ids);
  }

  it.each(CORPUS_FILES)(
    "%s contains no conceptId carrying the separator",
    (file) => {
      const ids = idsByFile.get(file)!;
      // A file that yields nothing would make the assertion below vacuously
      // true — the exact fail-open shape this repo has been burned by. Pin that
      // the corpus was actually read.
      expect(
        ids.size,
        `no conceptIds were read out of ${file} — the shape changed, so this guard is measuring nothing`
      ).toBeGreaterThan(0);

      const offenders = [...ids].filter((id) =>
        id.includes(JOBB_AXIS_SEPARATOR)
      );
      expect(
        offenders,
        `these conceptIds contain ${JSON.stringify(JOBB_AXIS_SEPARATOR)}, so joining an axis on it would parse back as extra values and silently widen the filter. Change JOBB_AXIS_SEPARATOR to a character absent from the corpus; do NOT relax this test.`
      ).toEqual([]);
    }
  );

  it("the corpus is large enough that this guard is not accidentally narrow", () => {
    const total = [...idsByFile.values()].reduce((n, s) => n + s.size, 0);
    // Measured 2026-08-01: 23 968 ids across the four files. Asserted as a floor,
    // not an equality — the snapshots are regenerated on purpose and growth must
    // not fail the build, while a collapse to a handful would mean the reader
    // broke and the separator guard above stopped covering the space.
    expect(total).toBeGreaterThan(20_000);
  });
});
