import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * #2094 (dotnet-architect): since the Strict-cookie bounce, another site that opens a protected or admin page in a
 * top-level window gets that page rendered signed in, so a write the page makes at render is one another site can
 * trigger. ADR 0018 Amendment 2026-10-10 accepts the writes listed here and no others; a new one reopens it.
 *
 * What this sees: a deferred `after(() => …)` call in a page or component under `(app)`, `(admin)` or
 * `src/components`, which is how every render-time write is made today. It does not see a write awaited directly
 * during render.
 */
const SRC = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const SCANNED = ["app/(app)", "app/(admin)", "components"].map((dir) => join(SRC, dir));

const ACCEPTED = [
  "app/(app)/@modal/(.)jobb/[id]/page.tsx",
  "app/(app)/foretag/bevakade/nya/page.tsx",
  "app/(app)/jobb/[id]/page.tsx",
  "app/(app)/matchningar/page.tsx",
  "components/job-ads/jobb-results.tsx",
];

const AFTER_CALL = /\bafter\(\s*(?:async\s*)?\(\)\s*=>/;

function sourceFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) return sourceFiles(path);
    return /\.tsx?$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name) ? [path] : [];
  });
}

describe("render-time writes another site can trigger through the bounce", () => {
  it("are exactly the ones ADR 0018 Amendment 2026-10-10 accepts", () => {
    const writers = SCANNED.flatMap(sourceFiles)
      .filter((file) => AFTER_CALL.test(readFileSync(file, "utf8")))
      .map((file) => relative(SRC, file).split(sep).join("/"))
      .sort();

    expect(
      writers,
      "A page under (app) or (admin) gained or lost a write at render. Another site can trigger it through the " +
        "Strict-cookie bounce: update ADR 0018 Amendment 2026-10-10 and this list together."
    ).toEqual(ACCEPTED);
  });
});
