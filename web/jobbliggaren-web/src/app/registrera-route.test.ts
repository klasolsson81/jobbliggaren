import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import {
  LOGIN_LINK_ROUTE,
  LOGIN_LINK_ROUTE_HEADERS,
} from "@/lib/security/security-headers";
import nextConfig from "../../next.config";

/**
 * `/registrera` is gone as a page and survives as a 308 to `/logga-in` (ADR 0142): one page logs in
 * and creates an account. The redirect is a shim for bookmarks and old mail, never a routing layer,
 * so nothing in `src/` may still point at it: a producer would cost a round trip and show the user
 * a URL that is not where they land.
 */
const APP = resolve(dirname(fileURLToPath(import.meta.url)));
const SRC = resolve(APP, "..");

function sourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      sourceFiles(full, acc);
    } else if (/\.(tsx?|css)$/.test(entry.name) && !/\.(test|spec)\.tsx?$/.test(entry.name)) {
      acc.push(full);
    }
  }
  return acc;
}

describe("/registrera", () => {
  it("answers 308 to /logga-in", async () => {
    const redirects = await nextConfig.redirects!();

    expect(redirects).toContainEqual({
      source: "/registrera",
      destination: "/logga-in",
      permanent: true,
    });
  });

  it("has no page left to shadow the redirect", () => {
    const pages = readdirSync(join(APP, "(auth)"), { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => entry.name);

    expect(pages).not.toContain("registrera");
    expect(pages).toContain("logga-in");
  });

  it("has no producer left in src/, in code or in a comment", () => {
    const files = sourceFiles(SRC);
    expect(files.length).toBeGreaterThan(100);

    const offenders = files
      .filter((file) => readFileSync(file, "utf8").includes("/registrera"))
      .map((file) => relative(SRC, file).split(sep).join("/"));

    expect(offenders).toEqual([]);
  });
});

describe("the login link route's headers in next.config", () => {
  it("come AFTER the global block, because the later entry is the one served for a shared key", async () => {
    const entries = await nextConfig.headers!();
    const sources = entries.map((entry) => entry.source);

    expect(sources).toEqual(["/(.*)", LOGIN_LINK_ROUTE]);
    expect(entries[1]!.headers).toEqual(LOGIN_LINK_ROUTE_HEADERS.map((header) => ({ ...header })));
  });
});
