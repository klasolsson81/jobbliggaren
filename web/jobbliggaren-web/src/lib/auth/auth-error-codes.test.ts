import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { AUTH_ERROR_CODES } from "./auth-error-codes";

/**
 * #1293 — the client compares an `Auth.*` code in ONE spelling only: the one in
 * `auth-error-codes.ts`, which the C# suite joins to the backend's constants. A literal typed
 * inline at a call site is outside that join, so a backend rename would kill that arm silently.
 *
 * Test files are excluded ON PURPOSE. A fixture must keep its own hand-written literal: one that
 * imported the constant would stay green under the very rename this exists to catch.
 */
const SRC = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const MODULE = join("lib", "auth", "auth-error-codes.ts");

const isTestFile = (name: string): boolean => /\.(test|spec)\.tsx?$/.test(name);

function sourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      sourceFiles(full, acc);
    } else if (/\.tsx?$/.test(entry.name) && !isTestFile(entry.name)) {
      acc.push(full);
    }
  }
  return acc;
}

/** Comments may name a code as prose; only a literal in live code is a second spelling. */
function withoutComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/\/\/[^\n]*/g, "");
}

const INLINE_LITERAL = /["'`]Auth\.[A-Za-z]+["'`]/g;

describe("AUTH_ERROR_CODES", () => {
  it("is the only place in src/ where an Auth.* code is spelled as a literal", () => {
    const files = sourceFiles(SRC);
    // A scan that read nothing would pass over zero files.
    expect(files.length).toBeGreaterThan(100);

    const offenders = files
      .filter((file) => relative(SRC, file) !== MODULE)
      .flatMap((file) =>
        (withoutComments(readFileSync(file, "utf8")).match(INLINE_LITERAL) ?? []).map(
          (literal) => `${relative(SRC, file).split(sep).join("/")}: ${literal}`
        )
      );

    expect(offenders).toEqual([]);
  });

  it("finds the literals in the module itself, so the pattern is not blind", () => {
    const found = withoutComments(readFileSync(join(SRC, MODULE), "utf8")).match(INLINE_LITERAL);

    expect(found?.length).toBe(Object.keys(AUTH_ERROR_CODES).length);
  });

  it("writes every member in the one form the C# join harvests: a bare key, a double-quoted value", () => {
    const source = withoutComments(readFileSync(join(SRC, MODULE), "utf8"));
    const harvested = [...source.matchAll(/(\w+)\s*:\s*"(Auth\.[A-Za-z]+)"/g)].map((match) => [
      match[1],
      match[2],
    ]);

    expect(harvested).toEqual(Object.entries(AUTH_ERROR_CODES));
  });

  it("names each key after the code it carries", () => {
    for (const [key, value] of Object.entries(AUTH_ERROR_CODES)) {
      expect(value).toBe(`Auth.${key}`);
    }
  });
});
