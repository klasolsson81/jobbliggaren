import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { FEEDBACK_PAGE_KEYS, FEEDBACK_ROUTES, isFeedbackPageKey } from "./page-keys";

function repoRoot(): string {
  let dir = path.dirname(fileURLToPath(import.meta.url));
  for (;;) {
    if (existsSync(path.join(dir, "Jobbliggaren.sln"))) return dir;
    const parent = path.dirname(dir);
    if (parent === dir) throw new Error("Could not find Jobbliggaren.sln walking up from the test file.");
    dir = parent;
  }
}

/** The backend's closed set, read out of the SmartEnum's own declarations rather than restated. */
function backendPageKeys(): string[] {
  const source = readFileSync(path.join(repoRoot(), "src", "Jobbliggaren.Domain", "Feedback", "FeedbackPage.cs"), "utf8");
  return [...source.matchAll(/new\("([a-z-]+)",\s*\d+\)/g)].map((match) => match[1]!);
}

describe("feedback page keys", () => {
  it("are exactly the backend's closed set", () => {
    const backend = backendPageKeys();
    expect(backend).toHaveLength(20);
    expect([...FEEDBACK_PAGE_KEYS].sort()).toEqual([...backend].sort());
  });

  it("are each carried by at least one route except general, and every route key is a page key", () => {
    const mapped = new Set<string>(FEEDBACK_ROUTES.flatMap((route) => ("key" in route ? [route.key] : [])));
    expect([...mapped].sort()).toEqual(FEEDBACK_PAGE_KEYS.filter((key) => key !== "general").sort());
    for (const route of FEEDBACK_ROUTES) {
      if ("key" in route) expect(isFeedbackPageKey(route.key), route.pattern).toBe(true);
      else expect(route.exempt.trim().length, route.pattern).toBeGreaterThan(0);
    }
  });

  it("lists each pattern once", () => {
    const patterns = FEEDBACK_ROUTES.map((route) => route.pattern);
    expect(new Set(patterns).size).toBe(patterns.length);
  });
});
