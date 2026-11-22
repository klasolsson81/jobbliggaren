import { readdirSync, readFileSync } from "node:fs";
import { basename, dirname, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { HTTP_METHODS } from "next/dist/server/web/http";
import * as ts from "typescript";
import { describe, expect, it } from "vitest";

/**
 * Next's webpack build checks the parameters of every page's, layout's and route handler's entry functions against
 * the types Next passes them (`next-types-plugin`). A parameter that may be left out (`props?`, a default such as
 * `= {}`) widens it to `… | undefined`, which fails that check with TS2344. Turbopack's build accepts these signatures.
 *
 * The rule reads the syntax of every page, layout and route file under `src/app`, found on the filesystem.
 */

const APP_ROOT = dirname(fileURLToPath(import.meta.url));
const ENTRY_KINDS = ["page", "layout", "route"] as const;
type EntryKind = (typeof ENTRY_KINDS)[number];
const HTTP_METHOD_NAMES: ReadonlySet<string> = new Set(HTTP_METHODS);

/** Each checked export, by name, and how many leading parameters Next's check reads. */
const COMPONENT_CHECKS: ReadonlyMap<string, number> = new Map([
  ["default", 1],
  ["generateMetadata", 2],
  ["generateViewport", 2],
  ["generateStaticParams", 1],
]);
const ROUTE_CHECKS: ReadonlyMap<string, number> = new Map<string, number>([
  ...HTTP_METHODS.map((method) => [method, 2] as const),
  ["generateStaticParams", 1],
]);
const checksFor = (kind: EntryKind) => (kind === "route" ? ROUTE_CHECKS : COMPONENT_CHECKS);

const toPosix = (p: string) => p.split(sep).join("/");
const entryKind = (name: string): EntryKind | undefined =>
  ENTRY_KINDS.find((kind) => new RegExp(`^${kind}\\.[^.]+$`).test(name));

function entryFiles(dir: string, acc: { file: string; kind: EntryKind }[] = []): { file: string; kind: EntryKind }[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = resolve(dir, entry.name);
    const kind = entryKind(entry.name);
    if (entry.isDirectory()) entryFiles(full, acc);
    else if (kind !== undefined) acc.push({ file: full, kind });
  }
  return acc;
}

function admitsAbsentValue(type: ts.TypeNode): boolean {
  if (type.kind === ts.SyntaxKind.UndefinedKeyword || type.kind === ts.SyntaxKind.VoidKeyword) return true;
  if (ts.isLiteralTypeNode(type) && type.literal.kind === ts.SyntaxKind.NullKeyword) return true;
  if (ts.isParenthesizedTypeNode(type)) return admitsAbsentValue(type.type);
  return ts.isUnionTypeNode(type) && type.types.some(admitsAbsentValue);
}

function isOptional(parameter: ts.ParameterDeclaration): boolean {
  return (
    parameter.questionToken !== undefined ||
    parameter.initializer !== undefined ||
    (parameter.type !== undefined && admitsAbsentValue(parameter.type))
  );
}

const hasModifier = (node: ts.Node, kind: ts.SyntaxKind) =>
  ts.canHaveModifiers(node) && (ts.getModifiers(node) ?? []).some((modifier) => modifier.kind === kind);

/** The same-file function declaration an `export const M = name` alias names, if that is what it is. */
function aliasTarget(source: ts.SourceFile, declaration: ts.VariableDeclaration): ts.FunctionDeclaration | undefined {
  const initializer = declaration.initializer;
  if (initializer === undefined || !ts.isIdentifier(initializer)) return undefined;
  return source.statements.find(
    (statement): statement is ts.FunctionDeclaration =>
      ts.isFunctionDeclaration(statement) && statement.name?.text === initializer.text
  );
}

function checkedFunctions(source: ts.SourceFile, checks: ReadonlyMap<string, number>): Map<string, ts.FunctionDeclaration> {
  const found = new Map<string, ts.FunctionDeclaration>();
  for (const statement of source.statements) {
    if (ts.isFunctionDeclaration(statement) && hasModifier(statement, ts.SyntaxKind.ExportKeyword)) {
      const name = hasModifier(statement, ts.SyntaxKind.DefaultKeyword) ? "default" : statement.name?.text;
      if (name !== undefined && checks.has(name)) found.set(name, statement);
    }
    if (ts.isVariableStatement(statement) && hasModifier(statement, ts.SyntaxKind.ExportKeyword)) {
      for (const declaration of statement.declarationList.declarations) {
        const target = aliasTarget(source, declaration);
        if (ts.isIdentifier(declaration.name) && checks.has(declaration.name.text) && target !== undefined) {
          found.set(declaration.name.text, target);
        }
      }
    }
  }
  return found;
}

function unreadExports(source: ts.SourceFile, checks: ReadonlyMap<string, number>): string[] {
  const unread: string[] = [];
  for (const statement of source.statements) {
    if (ts.isExportAssignment(statement) && checks.has("default")) unread.push("default");
    if (ts.isExportDeclaration(statement) && statement.exportClause === undefined) unread.push("export *");
    if (ts.isExportDeclaration(statement) && statement.exportClause && ts.isNamedExports(statement.exportClause)) {
      for (const element of statement.exportClause.elements) {
        if (checks.has(element.name.text)) unread.push(element.name.text);
      }
    }
    if (ts.isVariableStatement(statement) && hasModifier(statement, ts.SyntaxKind.ExportKeyword)) {
      for (const declaration of statement.declarationList.declarations) {
        if (
          ts.isIdentifier(declaration.name) &&
          checks.has(declaration.name.text) &&
          aliasTarget(source, declaration) === undefined
        ) {
          unread.push(declaration.name.text);
        }
      }
    }
  }
  return unread;
}

function optionalParameters(source: ts.SourceFile, checks: ReadonlyMap<string, number>): string[] {
  return [...checkedFunctions(source, checks)].flatMap(([name, declaration]) =>
    declaration.parameters
      .slice(0, checks.get(name))
      .flatMap((parameter, index) => (isOptional(parameter) ? [`${name}#${index + 1}`] : []))
  );
}

const parse = (fileName: string, text: string) =>
  ts.createSourceFile(fileName, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);

const entries = entryFiles(APP_ROOT)
  .map(({ file, kind }) => ({
    key: toPosix(relative(APP_ROOT, file)),
    checks: checksFor(kind),
    isRoute: kind === "route",
    source: parse(basename(file), readFileSync(file, "utf8")),
  }))
  .sort((a, b) => a.key.localeCompare(b.key));

describe("the parameters Next passes to an entry function are required", () => {
  it("finds the entry files on the filesystem, the four pages this rule was written for among them", () => {
    expect(entries.map((entry) => entry.key)).toEqual(
      expect.arrayContaining([
        "(admin)/admin/anvandare/page.tsx",
        "(marketing-inner)/integritet/page.tsx",
        "(public-job)/junior/page.tsx",
        "(public-job)/lediga-jobb/page.tsx",
        "api/feedback/route.ts",
      ])
    );
  });

  it("matches an entry file by Next's own page, layout and route rule", () => {
    const names = [
      "page.tsx",
      "page.ts",
      "layout.jsx",
      "route.ts",
      "page.test.tsx",
      "route.test.ts",
      "page.preview.tsx",
      "layout.preview.tsx",
    ];

    expect(names.filter((name) => entryKind(name) !== undefined)).toEqual(["page.tsx", "page.ts", "layout.jsx", "route.ts"]);
  });

  it.each(entries)("$key exports its entry functions in a shape this rule reads", ({ source, checks, isRoute }) => {
    const read = [...checkedFunctions(source, checks).keys()];

    expect(isRoute ? read.some((name) => HTTP_METHOD_NAMES.has(name)) : read.includes("default")).toBe(true);
    expect(unreadExports(source, checks)).toEqual([]);
  });

  it.each(entries)("$key takes no optional parameter in a checked function", ({ source, checks }) => {
    expect(optionalParameters(source, checks)).toEqual([]);
  });

  it("flags the page and layout signatures that fail Next's check and passes the ones that do not", () => {
    const flagged = (text: string) => optionalParameters(parse("page.tsx", text), COMPONENT_CHECKS);

    expect(flagged("export default async function P({ s = Promise.resolve({}) }: { s?: Promise<object> } = {}) {}")).toEqual([
      "default#1",
    ]);
    expect(flagged("export async function generateMetadata(props?: { s: Promise<object> }) {}")).toEqual([
      "generateMetadata#1",
    ]);
    expect(flagged("export function generateViewport(props: ({ s: Promise<object> } | undefined)) {}")).toEqual([
      "generateViewport#1",
    ]);
    expect(flagged("export default function P(props: { s: Promise<object> } | null) {}")).toEqual(["default#1"]);
    expect(flagged("export default function P(props: { s: Promise<object> } | void) {}")).toEqual(["default#1"]);
    expect(flagged("export async function generateMetadata(props: { s: Promise<object> }, parent?: object) {}")).toEqual([
      "generateMetadata#2",
    ]);
    expect(flagged("export async function generateStaticParams(props?: { params: object }) {}")).toEqual([
      "generateStaticParams#1",
    ]);
    expect(flagged("export default function P({ s }: { s: Promise<object> }, extra?: object) {}")).toEqual([]);
    expect(flagged("export default function P({ s }: { s: Promise<object> }) {}")).toEqual([]);
    expect(flagged("export default function P() {}")).toEqual([]);
    expect(flagged("export function helper(props?: object) {}")).toEqual([]);
  });

  it("flags the route handler signatures that fail Next's check and passes the ones that do not", () => {
    const flagged = (text: string) => optionalParameters(parse("route.ts", text), ROUTE_CHECKS);

    expect(flagged("export async function GET(req?: Request) {}")).toEqual(["GET#1"]);
    expect(flagged("export async function GET(req: Request, ctx?: { params: Promise<object> }) {}")).toEqual(["GET#2"]);
    expect(flagged("function reject(req?: Request) {} export const PUT = reject;")).toEqual(["PUT#1"]);
    expect(flagged("export function generateStaticParams(props?: { params: object }) {}")).toEqual([
      "generateStaticParams#1",
    ]);
    expect(flagged("function methodNotAllowed() {} export const PUT = methodNotAllowed;")).toEqual([]);
    expect(flagged("export async function GET(req: Request, ctx: { params: Promise<object> }) {}")).toEqual([]);
    expect(flagged("export function helper(req?: Request) {}")).toEqual([]);
  });

  it("refuses an export shape it cannot read, so no entry file is skipped in silence", () => {
    const unreadInPage = (text: string) => unreadExports(parse("page.tsx", text), COMPONENT_CHECKS);
    const unreadInRoute = (text: string) => unreadExports(parse("route.ts", text), ROUTE_CHECKS);

    expect(unreadInPage("const P = () => null; export default P;")).toEqual(["default"]);
    expect(unreadInPage("export const generateMetadata = async (props?: object) => ({});")).toEqual(["generateMetadata"]);
    expect(unreadInPage("function P() {} export { P as default };")).toEqual(["default"]);
    expect(unreadInPage('export * from "./metadata";')).toEqual(["export *"]);
    expect(unreadInRoute('import { imported } from "./handlers"; export const PUT = imported;')).toEqual(["PUT"]);
    expect(unreadInRoute("export const GET = async (req?: Request) => new Response();")).toEqual(["GET"]);
    expect(unreadInRoute("function methodNotAllowed() {} export const PUT = methodNotAllowed;")).toEqual([]);
  });
});
