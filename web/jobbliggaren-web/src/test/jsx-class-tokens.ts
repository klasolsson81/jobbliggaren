import { readdirSync } from "node:fs";
import { resolve } from "node:path";
import * as ts from "typescript";

/**
 * Reading JSX class tokens out of source, for the fitness functions that ask which elements carry a
 * class: `app/content-rail.test.ts` and `app/job-row-grid.test.ts`. Both ask their own question of
 * the same reading, so it is written once.
 */

const literals = (node: ts.Node, into: Set<string>): void => {
  if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) {
    for (const t of node.text.split(/\s+/)) if (t) into.add(t);
    return;
  }
  if (ts.isTemplateExpression(node)) {
    for (const t of node.head.text.split(/\s+/)) if (t) into.add(t);
    for (const span of node.templateSpans) {
      for (const t of span.literal.text.split(/\s+/)) if (t) into.add(t);
      literals(span.expression, into);
    }
    return;
  }
  ts.forEachChild(node, (c) => literals(c, into));
};

/** Every class token in an element's `className` initializer: string and template literals, at any depth. */
export function classTokens(attrs: ts.JsxAttributes): Set<string> {
  const into = new Set<string>();
  for (const a of attrs.properties) {
    if (ts.isJsxAttribute(a) && ts.isIdentifier(a.name) && a.name.text === "className" && a.initializer) {
      literals(a.initializer, into);
    }
  }
  return into;
}

/** Every production `.tsx` file under `dir`; test and spec files are left out. */
export function sourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const child = resolve(dir, entry.name);
    if (entry.isDirectory()) sourceFiles(child, acc);
    else if (/\.tsx$/.test(entry.name) && !/\.(test|spec)\.tsx$/.test(entry.name)) acc.push(child);
  }
  return acc;
}
