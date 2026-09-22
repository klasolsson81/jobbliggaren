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
 * A retired route survives as a permanent (308) redirect: a shim for bookmarks and mail already sent,
 * never a routing layer. So for each one the redirect exists, no route directory is left to shadow
 * it, and nothing in `src/` still points at it, in code or in a comment: a producer would cost a round
 * trip and show the user a URL that is not where they land.
 */
const APP = resolve(dirname(fileURLToPath(import.meta.url)));
const SRC = resolve(APP, "..");

interface RetiredRoute {
  /** The retired path, as its redirect's `source`. */
  path: string;
  destination: string;
  /** Whether the paths below it redirect too (a route that had sub-pages or was linked with one). */
  withSubpaths: boolean;
}

const RETIRED: RetiredRoute[] = [
  // ADR 0142: one page logs in and creates an account.
  { path: "/registrera", destination: "/logga-in", withSubpaths: false },
  // #1740 (ADR 0142 D7): Mina sidor replaces Inställningar. Permanent, with no removal trigger: until
  // #1740 the notification mails' withdrawal link (GDPR Art. 7(3)) pointed here, and no measurement can
  // show that no inbox still holds one.
  { path: "/installningar", destination: "/mina-sidor", withSubpaths: true },
  // ADR 0057 pointed it at /installningar; it now goes straight to where that went.
  { path: "/mig", destination: "/mina-sidor", withSubpaths: true },
];

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

function directoryNames(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      acc.push(entry.name);
      directoryNames(join(dir, entry.name), acc);
    }
  }
  return acc;
}

/** The path as a whole segment: `/mig` must not match `/migrate`, nor `/mig-x`. */
function producerPattern(path: string): RegExp {
  return new RegExp(`${path.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}(?![\\w-])`);
}

describe.each(RETIRED)("the retired route $path", ({ path, destination, withSubpaths }) => {
  it(`answers 308 to ${destination}`, async () => {
    const redirects = await nextConfig.redirects!();

    expect(redirects).toContainEqual({ source: path, destination, permanent: true });
    if (withSubpaths) {
      expect(redirects).toContainEqual({
        source: `${path}/:path*`,
        destination: `${destination}/:path*`,
        permanent: true,
      });
    }
  });

  it("has no route directory left to shadow the redirect, and the destination exists", () => {
    const names = directoryNames(APP);

    expect(names).not.toContain(path.slice(1));
    expect(names).toContain(destination.slice(1));
  });

  it("has no producer left in src/, in code or in a comment", () => {
    const files = sourceFiles(SRC);
    // A scan that read nothing would pass over zero files.
    expect(files.length).toBeGreaterThan(100);

    const pattern = producerPattern(path);
    const offenders = files
      .filter((file) => pattern.test(readFileSync(file, "utf8")))
      .map((file) => relative(SRC, file).split(sep).join("/"));

    expect(offenders).toEqual([]);
  });
});

describe("the redirects", () => {
  it("never land on another redirect: a shim is not answered by a shim", async () => {
    const redirects = await nextConfig.redirects!();
    const sources = new Set(redirects.map((redirect) => redirect.source));

    const chained = redirects.filter((redirect) => sources.has(redirect.destination));

    expect(chained).toEqual([]);
  });

  it("see a segment only as a whole: the producer scan is not blind, and not over-eager", () => {
    const pattern = producerPattern("/mig");

    expect(pattern.test('href="/mig"')).toBe(true);
    expect(pattern.test("/mig/profil")).toBe(true);
    expect(pattern.test("/migrations")).toBe(false);
    expect(pattern.test("/mig-x")).toBe(false);
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
