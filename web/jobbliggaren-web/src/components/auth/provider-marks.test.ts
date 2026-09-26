import { createHash } from "node:crypto";
import { readdirSync, readFileSync } from "node:fs";
import { basename, dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * The provider marks' exception (DESIGN.md §3, ADR 0142, Klas 2026-09-25): each mark is the provider's
 * own asset, byte-identical, with exactly one consumer. The hash is the zip entry's, so a re-exported,
 * recoloured or redrawn file fails here rather than in review.
 *
 * Google's: `signin-assets.zip` from developers.google.com/identity/branding-guidelines (downloaded
 * 2026-09-25), entry "Android + Web/PNG @4x/Light/Theme=Light, Show text=No, Shape=Square,
 * Platform=Android+Web@4x.png".
 */
const WEB = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
const SRC = join(WEB, "src");

const MARKS = [
  {
    file: join(WEB, "public", "provider-marks", "google-g-light-square-4x.png"),
    sha256: "2bc2ae8e4c67de66d74bf1deed12cd8f22981270266a487576b671b0b4df361c",
    consumer: "components/auth/provider-buttons.tsx",
  },
] as const;

function sourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) sourceFiles(full, acc);
    else if (/\.(ts|tsx|css)$/.test(entry.name) && !entry.name.includes(".test.")) acc.push(full);
  }
  return acc;
}

describe.each(MARKS)("the mark $file", (mark) => {
  const bytes = readFileSync(mark.file);

  it("is byte-identical with the provider's asset", () => {
    expect(createHash("sha256").update(bytes).digest("hex")).toBe(mark.sha256);
  });

  it("is the 160x160 raster the row's 40x40 rendering is sharp up to DPR 4 with", () => {
    expect(bytes.readUInt32BE(16)).toBe(160);
    expect(bytes.readUInt32BE(20)).toBe(160);
  });

  it("has exactly one consumer in src outside the tests", () => {
    const name = basename(mark.file, ".png");
    const consumers = sourceFiles(SRC)
      .filter((file) => readFileSync(file, "utf8").includes(name))
      .map((file) => relative(SRC, file).split(sep).join("/"));

    expect(consumers).toEqual([mark.consumer]);
  });
});
