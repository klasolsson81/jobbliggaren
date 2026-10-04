/**
 * The admin preview's artifact lock (ADR 0150 D5 (c), #1973), chained after `next build` in the
 * `build` script, so it runs wherever a build runs: locally, in CI and in the web image.
 *
 * The flag is resolved the way `next build` resolved it: Next's own `.env*` loader, then the shared
 * gate. A flag set in `.env.local` therefore reads the same here as in `next.config.ts`.
 * - Flag off: the build holds no preview route and no fixture value, or the build fails.
 * - Flag on: the build holds the preview route, so the compile lock is shown to work, and the
 *   build is announced as one that is never to be deployed.
 *
 * Only `.next/server` and `.next/static` are read: they are what a deployed image runs, and
 * `.next/cache` may keep chunks from an earlier build with the flag on.
 */
import { createRequire } from "node:module";
import { existsSync, readdirSync, readFileSync, realpathSync } from "node:fs";
import { dirname, extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const gate = require("../src/lib/admin-preview/gate.cjs");

/**
 * The reserved domain every fixture row carries (RFC 6761 `.invalid`): a build output holding it
 * holds fixture data, whichever row was imported. Defined here rather than in the gate, so a
 * production module that imports the gate never carries it into a build.
 */
export const ADMIN_PREVIEW_SENTINEL = "forhandsvisning.invalid";

const TEXT_EXTENSIONS = new Set([".js", ".mjs", ".cjs", ".json", ".html", ".rsc", ".body", ".meta", ".map", ".txt", ".css"]);

/**
 * The app-paths manifest's preview entries, keyed like "/(admin-preview)/admin/forhandsvisning/page":
 * the preview's group, or the preview's path in any group.
 */
export function previewRoutesIn(manifest) {
  return Object.keys(manifest).filter((route) => {
    const path = route.replace(/\/\((?!\.)[^/]*\)(?=\/|$)/g, "");
    return (
      route.includes("(admin-preview)") ||
      path === gate.ADMIN_PREVIEW_ROUTE ||
      path.startsWith(`${gate.ADMIN_PREVIEW_ROUTE}/`)
    );
  });
}

/** Every text file under the given directories whose content contains the needle. */
export function filesHolding(dirs, needle) {
  const hits = [];
  const walk = (dir) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const path = join(dir, entry.name);
      if (entry.isDirectory()) walk(path);
      else if (TEXT_EXTENSIONS.has(extname(entry.name)) && readFileSync(path, "utf8").includes(needle)) {
        hits.push(path);
      }
    }
  };
  for (const dir of dirs) if (existsSync(dir)) walk(dir);
  return hits;
}

/**
 * The decision, apart from the file system.
 * @param {{ flagOn: boolean, routes: string[], leaks: string[] }} build
 * @returns {{ ok: boolean, message: string }}
 */
export function verdict({ flagOn, routes, leaks }) {
  if (flagOn) {
    if (routes.length === 0) {
      return {
        ok: false,
        message: `${gate.ADMIN_PREVIEW_FLAG} is on, but the build holds no preview route: the compile lock did not work.`,
      };
    }
    return {
      ok: true,
      message: [
        "ADMIN PREVIEW BUILD: NEVER DEPLOY.",
        `${gate.ADMIN_PREVIEW_FLAG} is on, so this build serves fictional data at ${gate.ADMIN_PREVIEW_ROUTE} with no login.`,
      ].join("\n"),
    };
  }
  if (routes.length > 0 || leaks.length > 0) {
    return {
      ok: false,
      message: [
        `${gate.ADMIN_PREVIEW_FLAG} is off, but the build holds the admin preview:`,
        ...routes.map((route) => `  route: ${route}`),
        ...leaks.map((file) => `  fixture value in: ${file}`),
      ].join("\n"),
    };
  }
  return { ok: true, message: "Admin preview: absent from this build, as it must be." };
}

function main() {
  const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
  // Next's own loader, resolved through Next itself: the version `next build` just used.
  const { loadEnvConfig } = createRequire(require.resolve("next/package.json"))("@next/env");
  loadEnvConfig(root, false, { info: () => {}, error: (...args) => console.error(...args) });

  const next = join(root, ".next");
  const manifestPath = join(next, "server", "app-paths-manifest.json");
  if (!existsSync(manifestPath)) {
    console.error(`No build output at ${manifestPath}: run next build first.`);
    process.exit(1);
  }
  const flagOn = gate.adminPreviewEnabled(process.env);
  const routes = previewRoutesIn(JSON.parse(readFileSync(manifestPath, "utf8")));
  const leaks = flagOn
    ? []
    : filesHolding([join(next, "server"), join(next, "static")], ADMIN_PREVIEW_SENTINEL);

  const result = verdict({ flagOn, routes, leaks });
  (result.ok ? console.log : console.error)(result.message);
  if (!result.ok) process.exit(1);
}

if (process.argv[1] !== undefined && realpathSync(fileURLToPath(import.meta.url)) === realpathSync(process.argv[1])) {
  main();
}
