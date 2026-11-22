import { readdirSync, readFileSync } from "node:fs";
import { basename, dirname, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import * as ts from "typescript";
import { describe, expect, it } from "vitest";

/**
 * Next's webpack build checks every page and layout against `PageProps`/`LayoutProps`: the first parameter of the
 * default export, `generateMetadata` and `generateViewport` must be a type Next can pass. A parameter that may be left
 * out (`props?`, a default such as `= {}`) or that is typed to admit `undefined` widens it to `… | undefined`, which
 * fails that check with TS2344. Turbopack's build accepts these signatures, so this file is what catches them.
 *
 * The rule reads the syntax of every `page.tsx` and `layout.tsx` under `src/app`, found on the filesystem.
 */

const APP_ROOT = dirname(fileURLToPath(import.meta.url));
const ENTRY_FILE_NAMES = new Set(["page.tsx", "layout.tsx"]);
const CHECKED_EXPORTS = new Set(["default", "generateMetadata", "generateViewport"]);
const toPosix = (p: string) => p.split(sep).join("/");

function entryFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = resolve(dir, entry.name);
    if (entry.isDirectory()) entryFiles(full, acc);
    else if (ENTRY_FILE_NAMES.has(entry.name)) acc.push(full);
  }
  return acc;
}

function admitsUndefined(type: ts.TypeNode): boolean {
  if (type.kind === ts.SyntaxKind.UndefinedKeyword) return true;
  if (ts.isParenthesizedTypeNode(type)) return admitsUndefined(type.type);
  return ts.isUnionTypeNode(type) && type.types.some(admitsUndefined);
}

function isOptional(parameter: ts.ParameterDeclaration): boolean {
  return (
    parameter.questionToken !== undefined ||
    parameter.initializer !== undefined ||
    (parameter.type !== undefined && admitsUndefined(parameter.type))
  );
}

/** The checked functions an entry file exports as function declarations, by export name. */
function checkedFunctions(source: ts.SourceFile): Map<string, ts.FunctionDeclaration> {
  const found = new Map<string, ts.FunctionDeclaration>();
  for (const statement of source.statements) {
    if (!ts.isFunctionDeclaration(statement)) continue;
    const modifiers = ts.getModifiers(statement) ?? [];
    if (!modifiers.some((modifier) => modifier.kind === ts.SyntaxKind.ExportKeyword)) continue;
    const name = modifiers.some((modifier) => modifier.kind === ts.SyntaxKind.DefaultKeyword)
      ? "default"
      : statement.name?.text;
    if (name !== undefined && CHECKED_EXPORTS.has(name)) found.set(name, statement);
  }
  return found;
}

/** Every other export of a checked name, which `checkedFunctions` cannot read. */
function unreadExports(source: ts.SourceFile): string[] {
  const unread: string[] = [];
  for (const statement of source.statements) {
    if (ts.isExportAssignment(statement)) unread.push("default");
    if (ts.isExportDeclaration(statement) && statement.exportClause && ts.isNamedExports(statement.exportClause)) {
      for (const element of statement.exportClause.elements) {
        if (CHECKED_EXPORTS.has(element.name.text)) unread.push(element.name.text);
      }
    }
    if (
      ts.isVariableStatement(statement) &&
      (ts.getModifiers(statement) ?? []).some((modifier) => modifier.kind === ts.SyntaxKind.ExportKeyword)
    ) {
      for (const declaration of statement.declarationList.declarations) {
        if (ts.isIdentifier(declaration.name) && CHECKED_EXPORTS.has(declaration.name.text)) {
          unread.push(declaration.name.text);
        }
      }
    }
  }
  return unread;
}

/** The checked exports whose first parameter Next's check reads as `… | undefined`. */
function optionalFirstParameters(source: ts.SourceFile): string[] {
  return [...checkedFunctions(source)]
    .filter(([, declaration]) => declaration.parameters[0] !== undefined && isOptional(declaration.parameters[0]))
    .map(([name]) => name);
}

const parse = (fileName: string, text: string) =>
  ts.createSourceFile(fileName, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);

const entries = entryFiles(APP_ROOT)
  .map((file) => ({ key: toPosix(relative(APP_ROOT, file)), source: parse(basename(file), readFileSync(file, "utf8")) }))
  .sort((a, b) => a.key.localeCompare(b.key));

describe("the props Next passes to a page or layout are a required first parameter", () => {
  it("finds the entry files on the filesystem, the four pages this rule was written for among them", () => {
    expect(entries.map((entry) => entry.key)).toEqual(
      expect.arrayContaining([
        "(admin)/admin/anvandare/page.tsx",
        "(marketing-inner)/integritet/page.tsx",
        "(public-job)/junior/page.tsx",
        "(public-job)/lediga-jobb/page.tsx",
      ])
    );
  });

  it.each(entries)("$key exports its default and checked functions as function declarations", ({ source }) => {
    expect(checkedFunctions(source).has("default")).toBe(true);
    expect(unreadExports(source)).toEqual([]);
  });

  it.each(entries)("$key takes no optional first parameter in a checked function", ({ source }) => {
    expect(optionalFirstParameters(source)).toEqual([]);
  });

  it("flags the signatures that fail Next's check and passes the ones that do not", () => {
    const flagged = (text: string) => optionalFirstParameters(parse("page.tsx", text));

    expect(flagged("export default async function P({ s = Promise.resolve({}) }: { s?: Promise<object> } = {}) {}")).toEqual([
      "default",
    ]);
    expect(flagged("export async function generateMetadata(props?: { s: Promise<object> }) {}")).toEqual([
      "generateMetadata",
    ]);
    expect(flagged("export function generateViewport(props: ({ s: Promise<object> } | undefined)) {}")).toEqual([
      "generateViewport",
    ]);
    expect(flagged("export default function P({ s }: { s: Promise<object> }) {}")).toEqual([]);
    expect(flagged("export default function P() {}")).toEqual([]);
    expect(flagged("export function helper(props?: object) {}")).toEqual([]);
  });

  it("refuses an export shape it cannot read, so no entry file is skipped in silence", () => {
    const unread = (text: string) => unreadExports(parse("page.tsx", text));

    expect(unread("const P = () => null; export default P;")).toEqual(["default"]);
    expect(unread("export const generateMetadata = async (props?: object) => ({});")).toEqual(["generateMetadata"]);
    expect(unread("function P() {} export { P as default };")).toEqual(["default"]);
  });
});
