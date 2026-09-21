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
});
