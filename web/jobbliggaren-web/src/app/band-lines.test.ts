import { readdirSync, readFileSync } from "node:fs";
import { dirname, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import * as ts from "typescript";
import { describe, expect, it } from "vitest";

const SOURCE_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");

function sourceFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const file = resolve(dir, entry.name);
    return entry.isDirectory()
      ? sourceFiles(file)
      : /\.tsx$/.test(entry.name) && !/\.(test|spec)\.tsx$/.test(entry.name) ? [file] : [];
  });
}

function hasClass(node: ts.JsxElement, name: string): boolean {
  const attribute = node.openingElement.attributes.properties.find(
    (attr): attr is ts.JsxAttribute => ts.isJsxAttribute(attr) && attr.name.getText() === "className",
  );
  const value = attribute?.initializer;
  return !!value && ts.isStringLiteral(value) && value.text.split(/\s+/).includes(name);
}

function checkBands(source: string, file: string): string[] {
  const ast = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const findings: string[] = [];
  const visit = (node: ts.Node): void => {
    if (ts.isJsxElement(node)) {
      const family = hasClass(node, "jp-pagehero") ? "jp-pagehero" : hasClass(node, "jp-hero") ? "jp-hero" : null;
      if (family) {
        const lines: ts.JsxElement[] = [];
        const descendants = (child: ts.Node): void => {
          if (ts.isJsxElement(child) && hasClass(child, family + "__lede")) lines.push(child);
          ts.forEachChild(child, descendants);
        };
        ts.forEachChild(node, descendants);
        const at = ast.getLineAndCharacterOfPosition(node.getStart()).line + 1;
        if (lines.length !== 1) findings.push(file + ":" + at + " has " + lines.length + " band lines");
        for (const line of lines) {
          if (line.openingElement.tagName.getText() !== "p") findings.push(file + ":" + at + " line is not plain paragraph text");
          for (let parent = line.parent; parent !== node; parent = parent.parent) {
            if (ts.isConditionalExpression(parent) || ts.isBinaryExpression(parent) || ts.isCallExpression(parent)) {
              findings.push(file + ":" + at + " line is conditional or repeated");
              break;
            }
          }
          const nested = (child: ts.Node): void => {
            // The titleless skeleton reserves its unknown line with a decorative span.
            if ((ts.isJsxElement(child) && child !== line) || ts.isJsxSelfClosingElement(child)) {
              const tag = ts.isJsxElement(child) ? child.openingElement.tagName.getText() : child.tagName.getText();
              if (tag !== "span" || !file.endsWith("page-hero-skeleton.tsx")) findings.push(file + ":" + at + " line contains markup");
            }
            ts.forEachChild(child, nested);
          };
          ts.forEachChild(line, nested);
        }
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(ast);
  return findings;
}

describe("one plain-text line under every green band (#1917)", () => {
  it("scans every production band, including shared renderers and loading boundaries", () => {
    const files = sourceFiles(SOURCE_ROOT);
    const bands = files.filter((file) => /className="jp-(pagehero|hero)(?:[ "])/.test(readFileSync(file, "utf8")));
    expect(bands.length, "the source scan must reach the current band surfaces").toBeGreaterThanOrEqual(36);
    expect(files.flatMap((file) => checkBands(readFileSync(file, "utf8"), relative(SOURCE_ROOT, file)))).toEqual([]);
  });

  it("ignores comments and string mentions and refuses zero, two, conditional or linked lines", () => {
    const wrap = (line: string) => '<section className="jp-pagehero"><h1>Titel</h1>' + line + '</section>';
    const line = '<p className="jp-pagehero__lede">Text.</p>';
    expect(checkBands(wrap(line), "probe.tsx")).toEqual([]);
    expect(checkBands('// className="jp-pagehero"\nconst mention = "jp-pagehero";', "probe.tsx")).toEqual([]);
    expect(checkBands(wrap(""), "probe.tsx")).toHaveLength(1);
    expect(checkBands(wrap(line + line), "probe.tsx")).toHaveLength(1);
    expect(checkBands(wrap('{visible && ' + line + '}'), "probe.tsx")).toHaveLength(1);
    expect(checkBands(wrap('<p className="jp-pagehero__lede"><a href="/">Text.</a></p>'), "probe.tsx")).toHaveLength(1);
  });
});
