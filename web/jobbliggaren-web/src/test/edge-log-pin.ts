import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

/**
 * The half of an app-surface edge-log inventory fact that every surface shares.
 *
 * Each surface owns its own inventory — which keys it emits, and a verdict per key — because only
 * that route's builders can produce it. What is NOT per-surface is the join to the deploy side:
 * the walk to the repo root, the read of `AppSurfaceScrubbedParameters` out of the C# pin, and the
 * liveness check on the fact that binds that array to the Caddyfile. Three copies of a
 * cross-language regex and a filesystem walk would drift silently, which is the failure this whole
 * mechanism exists to prevent — so they live here once.
 *
 * Nothing here parses the Caddyfile, and nothing here should. Its `log` block's PLACEMENT is
 * load-bearing (global options configures the default logger; the same lines in the site block
 * configure a different one and leave the leak open), and a position-blind parser is the exact
 * defect `code-reviewer` and `security-auditor` each raised against an earlier version of the C#
 * class. That parse is owned by `CaddyfileTokenScrubbingPinTests` and by nothing else. The chain
 * is: a surface's inventory -> this array -> the Caddyfile, each link read by whoever can read it
 * safely.
 */

export const PIN_RELATIVE = path.join(
  "tests",
  "Jobbliggaren.Architecture.Tests",
  "CaddyfileTokenScrubbingPinTests.cs"
);

export const PIN_LIST = "AppSurfaceScrubbedParameters";

export const PIN_FACT = "TheCaddyfile_FiltersEveryAppSurfaceParameterThatCarriesPersonalData";

/** The query keys a built href actually carries, read out of the string rather than the source. */
export function emittedKeys(href: string): ReadonlySet<string> {
  const start = href.indexOf("?");
  if (start < 0) return new Set<string>();
  return new Set(new URLSearchParams(href.slice(start + 1)).keys());
}

function repoRoot(): string {
  const from = path.dirname(fileURLToPath(import.meta.url));
  let dir = from;
  for (;;) {
    if (existsSync(path.join(dir, "Jobbliggaren.sln"))) return dir;
    const parent = path.dirname(dir);
    if (parent === dir) {
      throw new Error(
        `Could not find Jobbliggaren.sln by walking up from ${from}. An edge-log inventory fact ` +
          `binds its route's key inventory to ${PIN_RELATIVE}, so it needs the .NET half of the ` +
          `checkout present.`
      );
    }
    dir = parent;
  }
}

function pinSource(): string {
  const full = path.join(repoRoot(), PIN_RELATIVE);
  if (!existsSync(full)) {
    throw new Error(
      `${PIN_RELATIVE} is not at ${full}. The edge-log chain runs surface inventory -> that ` +
        `file's ${PIN_LIST} -> the Caddyfile; if the pin moved, re-make the join deliberately ` +
        `rather than deleting it.`
    );
  }
  return readFileSync(full, "utf8");
}

const PIN_SOURCE = pinSource();

/**
 * The pin's app-surface list, read as source text for the reason the pin itself gives about the
 * Caddyfile: the file is the artefact, and a parser clever enough to normalise it could hide the
 * spelling this join exists to compare. Finding nothing THROWS — an empty list would make a
 * subset check pass over zero iterations, which is the failure the join is here to prevent.
 */
export function pinnedAppSurfaceParameters(): ReadonlyArray<string> {
  // `\b` so a member merely ENDING in this name (LegacyAppSurfaceScrubbedParameters) cannot be
  // matched in its place — a wrong-but-successful read bypasses the throw below.
  const body = new RegExp(`\\b${PIN_LIST}\\s*=([^;]*);`).exec(PIN_SOURCE)?.[1];
  if (body === undefined) {
    throw new Error(
      `Could not read the ${PIN_LIST} array out of ${PIN_RELATIVE}. It was renamed or ` +
        `reshaped. Re-make this join deliberately, do not delete it.`
    );
  }
  return [...body.matchAll(/"([^"]*)"/g)]
    .map((m) => m[1])
    .filter((v): v is string => v !== undefined && v.length > 0);
}

/** Whether the pin still carries the fact that binds its array to the Caddyfile. */
export function pinCarriesTheCaddyfileFact(): boolean {
  return PIN_SOURCE.includes(PIN_FACT);
}
