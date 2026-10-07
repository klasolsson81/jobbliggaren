// @vitest-environment node
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

const rootRequire = createRequire(import.meta.url);
const fromPackage = (parent, name) => createRequire(parent.resolve(name));
const eslintRequire = fromPackage(rootRequire, "eslint-config-next");
const pluginRequire = fromPackage(eslintRequire, "@next/eslint-plugin-next");
const globRequire = fromPackage(pluginRequire, "fast-glob");
const micromatchRequire = fromPackage(globRequire, "micromatch");
const braces = micromatchRequire("braces");
const cliRequire = fromPackage(rootRequire, "@lhci/cli/package.json");
const utilsRequire = fromPackage(cliRequire, "@lhci/utils/src/lighthouserc.js");
const { loadRcFile } = utilsRequire("./lighthouserc.js");
const tooDeep = "Input nesting exceeds maximum depth (100)";
const nested = (depth, open = "{", close = "}") =>
  open.repeat(depth) + "a,b" + close.repeat(depth);

// GHSA-vfj7-8cjw-p6xm: these patterns fit the upstream 10,000-character cap.
describe("installed braces nesting repair", () => {
  it.each(["parse", "compile", "expand", "stringify"])(
    "%s rejects the advisory payload before recursive stack exhaustion",
    (method) => {
      const pattern = nested(4000);
      expect(pattern.length).toBeLessThan(10000);
      expect(() => braces[method](pattern)).toThrow(new SyntaxError(tooDeep));
    },
  );

  it.each([
    nested(101),
    nested(101, "(", ")"),
    nested(51, "({", "})"),
  ])("bounds the complete parser stack for %s", (pattern) => {
    expect(() => braces.parse(pattern)).toThrow(new SyntaxError(tooDeep));
  });

  it("accepts the boundary and preserves normal compilation and expansion", () => {
    expect(() => braces.compile(nested(100))).not.toThrow();
    expect(braces.compile("src/{app,components}/**/*.{ts,tsx}")).toBe(
      "src/(app|components)/**/*.(ts|tsx)",
    );
    expect(braces.expand("file-{1..3}-{a,b}")).toEqual([
      "file-1-a", "file-1-b", "file-2-a", "file-2-b", "file-3-a", "file-3-b",
    ]);
    expect(braces.stringify(braces.parse("a/{b,c}/d"))).toBe("a/{b,c}/d");
  });

  it("does not count quoted or escaped literal braces as nesting", () => {
    const literal = "{".repeat(101);
    expect(braces.compile('"' + literal + '"')).toBe(literal);
    expect(braces.compile("\\{".repeat(101))).toBe(literal);
  });

  // A caller-supplied oversized AST is unreachable through the repaired parser,
  // pinned above. Assert only safe rejection if that invariant is bypassed.
  it.each(["compile", "expand", "stringify"])(
    "%s safely rejects a malformed external AST",
    (method) => {
      let ast = { type: "root", nodes: [] };
      for (let depth = 0; depth < 4000; depth++) {
        ast = { type: "root", nodes: [ast] };
      }
      expect(() => braces[method](ast)).toThrow(new SyntaxError(tooDeep));
    },
  );

  it("uses the patched instance through the production shadcn dependency", () => {
    const shadcnRequire = fromPackage(rootRequire, "shadcn");
    const shadcnGlob = fromPackage(shadcnRequire, "fast-glob");
    const shadcnMatch = fromPackage(shadcnGlob, "micromatch");
    expect(() => shadcnMatch("braces")(nested(4000))).toThrow(new SyntaxError(tooDeep));
  });
});

describe("Lighthouse YAML dependency repair", () => {
  it("loads real YAML settings through the installed LHCI adapter", () => {
    const directory = mkdtempSync(join(tmpdir(), "jbl-lhci-yaml-"));
    try {
      const path = join(directory, "lighthouserc.yaml");
      writeFileSync(path, "ci:\n  collect:\n    numberOfRuns: 3\n    url:\n      - http://localhost:3000\n  assert:\n    assertions:\n      categories.performance: [error, {minScore: 0.9}]\n");
      expect(loadRcFile(path)).toEqual({
        ci: {
          collect: { numberOfRuns: 3, url: ["http://localhost:3000"] },
          assert: { assertions: { "categories:performance": ["error", { minScore: 0.9 }] } },
        },
      });
    } finally {
      rmSync(directory, { recursive: true, force: true });
    }
  });

  it("preserves the repository JSON configuration", () => {
    const path = new URL("../lighthouserc.json", import.meta.url);
    const expected = JSON.parse(readFileSync(path, "utf8"));
    expect(loadRcFile(fileURLToPath(path))).toEqual(expected);
  });

  it("rejects executable YAML tags with the replacement safe default schema", () => {
    const directory = mkdtempSync(join(tmpdir(), "jbl-lhci-yaml-"));
    try {
      const path = join(directory, "lighthouserc.yaml");
      writeFileSync(path, "ci: !!js/function 'function () { return 1; }'");
      expect(() => loadRcFile(path)).toThrow(/unknown tag/);
    } finally {
      rmSync(directory, { recursive: true, force: true });
    }
  });
});