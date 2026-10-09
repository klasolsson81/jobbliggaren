import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { FEEDBACK_PAGE_KEYS, FEEDBACK_ROUTES, feedbackPageKeyFor, isFeedbackPageKey } from "./page-keys";

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
    expect(backend).toHaveLength(19);
    expect([...FEEDBACK_PAGE_KEYS].sort()).toEqual([...backend].sort());
  });

  it("are each carried by at least one route, and every route key is a page key", () => {
    const mapped = new Set(FEEDBACK_ROUTES.flatMap((route) => ("key" in route ? [route.key] : [])));
    expect([...mapped].sort()).toEqual([...FEEDBACK_PAGE_KEYS].sort());
    for (const route of FEEDBACK_ROUTES) {
      if ("key" in route) expect(isFeedbackPageKey(route.key), route.pattern).toBe(true);
      else expect(route.exempt.trim().length, route.pattern).toBeGreaterThan(0);
    }
  });

  it("lists each pattern once", () => {
    const patterns = FEEDBACK_ROUTES.map((route) => route.pattern);
    expect(new Set(patterns).size).toBe(patterns.length);
  });

  it.each([
    ["/oversikt", "overview"],
    ["/jobb", "jobs"],
    ["/jobb/3f2504e0-4f89-41d3-9a0c-0305e82c3301", "job-ad"],
    ["/jobb/", "jobs"],
    ["/ansokningar/abc", "application"],
    ["/foretag/branschbevakningar/abc/annonser", "industry-watches"],
    ["/cv/importera", "cv-import"],
    ["/cv/abc/granska", "cv-review"],
    ["/cv/granska/abc", "cv-review"],
    ["/mina-sidor/sekretess", "my-pages"],
  ])("resolves %s to %s", (pathname, key) => {
    expect(feedbackPageKeyFor(pathname)).toBe(key);
  });

  it.each([
    "/foretag",
    "/cv/ny",
    "/cv/abc",
    "/cv/abc/mall",
    "/cv/slutfor/abc",
    "/cv/granska/abc/forbattra",
    "/admin/feedback",
    "/",
    "/jobb/abc/extra",
  ])("gives no key for %s", (pathname) => {
    expect(feedbackPageKeyFor(pathname)).toBeNull();
  });
});
