import { createHash } from "node:crypto";
import { readdirSync, readFileSync } from "node:fs";
import { basename, dirname, extname, join, relative, resolve, sep } from "node:path";
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
 *
 * GitHub's: `GitHub_Logos.zip` from https://brand.github.com/GitHub_Logos.zip (downloaded 2026-09-26
 * on Klas's "Ja, ladda ner"), entry "GitHub Logos/PNG/GitHub_Invertocat_Black.png".
 *
 * `rendered` is the square the row draws the mark in, which `provider-buttons.test.tsx` pins.
 */
const WEB = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
const SRC = join(WEB, "src");
const MARKS_DIR = join(WEB, "public", "provider-marks");

const MARKS = [
  {
    file: join(MARKS_DIR, "google-g-light-square-4x.png"),
    sha256: "2bc2ae8e4c67de66d74bf1deed12cd8f22981270266a487576b671b0b4df361c",
    width: 160,
    height: 160,
    rendered: 40,
    consumer: "components/auth/provider-buttons.tsx",
  },
  {
    file: join(MARKS_DIR, "github-invertocat-black.png"),
    sha256: "2a2f5cbcc74c7fa83c40127dd8b0e42c23f1157131aebe28492aa1ac27bbdc6d",
    width: 294,
    height: 288,
    rendered: 20,
    consumer: "components/auth/provider-buttons.tsx",
  },
] as const;

const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

function sourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) sourceFiles(full, acc);
    else if (/\.(ts|tsx|css)$/.test(entry.name) && !entry.name.includes(".test.")) acc.push(full);
  }
  return acc;
}

describe("the provider-marks directory", () => {
  // A mark committed but never listed above would ship with no hash pin and no consumer check.
  it("holds exactly the marks pinned here", () => {
    expect([...readdirSync(MARKS_DIR)].sort()).toEqual(MARKS.map((mark) => basename(mark.file)).sort());
  });
});

describe.each(MARKS)("the mark $file", (mark) => {
  const bytes = readFileSync(mark.file);

  it("is byte-identical with the provider's asset", () => {
    expect(createHash("sha256").update(bytes).digest("hex")).toBe(mark.sha256);
  });

  it(`is the ${mark.width}x${mark.height} PNG, sharp up to DPR 4 at the row's ${mark.rendered} px`, () => {
    expect(bytes.subarray(0, 8).equals(PNG_SIGNATURE)).toBe(true);
    expect(bytes.subarray(12, 16).toString("ascii")).toBe("IHDR");
    expect(bytes.readUInt32BE(16)).toBe(mark.width);
    expect(bytes.readUInt32BE(20)).toBe(mark.height);
    expect(Math.min(mark.width, mark.height)).toBeGreaterThanOrEqual(4 * mark.rendered);
  });

  it("has exactly one consumer in src outside the tests", () => {
    const name = basename(mark.file, extname(mark.file));
    const consumers = sourceFiles(SRC)
      .filter((file) => readFileSync(file, "utf8").includes(name))
      .map((file) => relative(SRC, file).split(sep).join("/"));

    expect(consumers).toEqual([mark.consumer]);
  });
});
