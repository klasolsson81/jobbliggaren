import { describe, expect, it } from "vitest";
import { DEFAULT_REDIRECT_PATH, safeRedirectPath } from "./safe-redirect";

describe("safeRedirectPath", () => {
  it("passes a same-site deep link through", () => {
    expect(safeRedirectPath("/ansokningar/abc-123")).toBe("/ansokningar/abc-123");
  });

  it.each([
    ["a protocol-relative URL", "//evil.example/path"],
    ["a backslash protocol-relative URL", "/\\evil.example/path"],
    ["an absolute URL", "https://evil.example/path"],
    ["a scheme without slashes", "javascript:alert(1)"],
    ["a relative path", "ansokningar"],
  ])("falls back for %s", (_label, raw) => {
    expect(safeRedirectPath(raw)).toBe(DEFAULT_REDIRECT_PATH);
  });

  it.each([
    ["null", null],
    ["undefined", undefined],
    ["the empty string", ""],
  ])("falls back for %s", (_label, raw) => {
    expect(safeRedirectPath(raw)).toBe(DEFAULT_REDIRECT_PATH);
  });

  it.each(["/", "/jobb"])("sends the passive landing %s to the start page", (raw) => {
    expect(safeRedirectPath(raw)).toBe(DEFAULT_REDIRECT_PATH);
  });

  it("keeps a path that only begins like a passive landing", () => {
    expect(safeRedirectPath("/jobb/123")).toBe("/jobb/123");
  });

  // Built from char codes so no escape sequence has to survive a tool or editor layer.
  const TAB = String.fromCharCode(9);
  const LF = String.fromCharCode(10);
  const CR = String.fromCharCode(13);
  const NUL = String.fromCharCode(0);
  const DEL = String.fromCharCode(127);
  const BACKSLASH = String.fromCharCode(92);

  // The URL parser deletes every tab and newline before it reads the input, and it reads a backslash as a
  // slash, so each of these becomes a protocol-relative URL once a browser resolves it.
  const offSiteOnceParsed: ReadonlyArray<readonly [string, string]> = [
    ["a tab between the slashes", `/${TAB}/evil.example`],
    ["a line feed between the slashes", `/${LF}/evil.example`],
    ["a carriage return between the slashes", `/${CR}/evil.example`],
    ["a tab before a backslash", `/${TAB}${BACKSLASH}evil.example`],
  ];

  it.each(offSiteOnceParsed)("falls back for %s", (_label, raw) => {
    expect(safeRedirectPath(raw)).toBe(DEFAULT_REDIRECT_PATH);
  });

  it.each([
    ["a NUL", `/cv${NUL}`],
    ["a DEL", `/cv${DEL}`],
    ["a backslash inside the path", `/cv${BACKSLASH}evil.example`],
  ])("falls back for a raw control character or backslash: %s", (_label, raw) => {
    expect(safeRedirectPath(raw)).toBe(DEFAULT_REDIRECT_PATH);
  });

  it("sends a passive landing that only normalises to one to the start page", () => {
    expect(safeRedirectPath("/./jobb")).toBe(DEFAULT_REDIRECT_PATH);
  });

  it("keeps the query and fragment of a same-site target", () => {
    expect(safeRedirectPath("/ansokningar/abc-123?flik=status#logg")).toBe(
      "/ansokningar/abc-123?flik=status#logg",
    );
  });

  it.each([
    ...offSiteOnceParsed.map(([, raw]) => raw),
    "//evil.example/path",
    `/${BACKSLASH}evil.example/path`,
    "https://evil.example/path",
    "/ansokningar/abc-123",
    "/%09/evil.example",
  ])("resolves on the site's own origin: %j", (raw) => {
    const base = "https://jobbliggaren.se";
    expect(new URL(safeRedirectPath(raw), base).origin).toBe(base);
  });
});
