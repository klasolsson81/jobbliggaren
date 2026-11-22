import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * #2285 (security-auditor Minor 3, #2285 plan read) — the web half of the backend's
 * `RecentSearchCaptureConsumerTests`. A search is stored only by the Server Action, and the action is called only by the
 * /jobb hero and toolbar on a deliberate search; the API helper is called only by the action. So no render, no filter
 * pick and no GET reaches the capture, which is what the privacy policy's "att bara bocka i ett filter räcker inte"
 * rests on. `strict-cookie-bounce-render-writes.test.ts` cannot see this: it does not see a write awaited during render.
 */
const SRC = resolve(dirname(fileURLToPath(import.meta.url)), "../..");

function sourceFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) return sourceFiles(path);
    return /\.tsx?$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name) ? [path] : [];
  });
}

// Code, not prose: a docblock that names the action is no caller of it.
const withoutComments = (source: string) =>
  source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/^\s*\/\/.*$/gm, "");

function filesNaming(identifier: RegExp): string[] {
  return sourceFiles(SRC)
    .filter((file) => identifier.test(withoutComments(readFileSync(file, "utf8"))))
    .map((file) => relative(SRC, file).split(sep).join("/"))
    .sort();
}

describe("who can store a recent search", () => {
  it("the Server Action is named by its module, the hero and the toolbar only", () => {
    expect(
      filesNaming(/\bcaptureRecentSearchAction\b/),
      "A new caller of captureRecentSearchAction stores searches from somewhere else. Only a deliberate search may " +
        "(ADR 0060 Amendment 2026-11-22): update the ADR and this list together.",
    ).toEqual([
      "components/job-ads/jobb-hero-search.tsx",
      "components/job-ads/jobb-results-toolbar.tsx",
      "lib/actions/recent-searches.ts",
    ]);
  });

  it("the API helper is named by its module and the Server Action only", () => {
    expect(
      filesNaming(/\bcaptureRecentSearch\b/),
      "captureRecentSearch is reached only through the Server Action, the POST Next checks against the Origin.",
    ).toEqual(["lib/actions/recent-searches.ts", "lib/api/recent-searches.ts"]);
  });
});
