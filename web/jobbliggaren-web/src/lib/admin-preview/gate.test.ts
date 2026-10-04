import { describe, expect, it } from "vitest";
import {
  ADMIN_PREVIEW_EXTENSION,
  ADMIN_PREVIEW_FLAG,
  ADMIN_PREVIEW_SENTINEL,
  DEFAULT_PAGE_EXTENSIONS,
  adminPreviewEnabled,
  pageExtensionsFor,
} from "./gate.cjs";

describe("admin preview gate (ADR 0150 D5)", () => {
  it.each([
    [undefined, false],
    ["", false],
    ["1", false],
    ["yes", false],
    ["TRUE", false],
    [" true", false],
    ["true", true],
  ])("reads %j as %s: only the exact string turns the preview on", (value, expected) => {
    expect(adminPreviewEnabled({ [ADMIN_PREVIEW_FLAG]: value })).toBe(expected);
  });

  it("adds the preview extension only when the flag is on, and keeps Next's defaults either way", () => {
    expect(pageExtensionsFor({})).toEqual(DEFAULT_PAGE_EXTENSIONS);
    expect(pageExtensionsFor({ [ADMIN_PREVIEW_FLAG]: "true" })).toEqual([
      ADMIN_PREVIEW_EXTENSION,
      ...DEFAULT_PAGE_EXTENSIONS,
    ]);
    expect(DEFAULT_PAGE_EXTENSIONS).toEqual(["tsx", "ts", "jsx", "js"]);
  });

  it("marks fixtures with a domain no mailbox can have (RFC 6761)", () => {
    expect(ADMIN_PREVIEW_SENTINEL.endsWith(".invalid")).toBe(true);
  });
});
