import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import {
  ADMIN_PREVIEW_SENTINEL as SENTINEL,
  filesHolding,
  previewRoutesIn,
  verdict,
} from "./assert-admin-preview-build.mjs";

describe("previewRoutesIn", () => {
  it("picks the preview group's entries and nothing else", () => {
    const manifest = {
      "/(admin)/admin/(oversikt)/page": "app/(admin)/admin/(oversikt)/page.js",
      "/(admin-preview)/admin/forhandsvisning/page": "app/(admin-preview)/admin/forhandsvisning/page.js",
      "/(admin-preview)/admin/forhandsvisning/anvandare/page": "x.js",
      "/(app)/jobb/page": "y.js",
    };
    expect(previewRoutesIn(manifest)).toEqual([
      "/(admin-preview)/admin/forhandsvisning/page",
      "/(admin-preview)/admin/forhandsvisning/anvandare/page",
    ]);
  });

  it("also picks a route at the preview's path that sits in another group", () => {
    expect(
      previewRoutesIn({
        "/(admin)/admin/forhandsvisning/page": "a.js",
        "/(admin)/(nested)/admin/forhandsvisning/jobb/page": "b.js",
        "/(admin)/admin/forhandsvisningar/page": "c.js",
      }),
    ).toEqual(["/(admin)/admin/forhandsvisning/page", "/(admin)/(nested)/admin/forhandsvisning/jobb/page"]);
  });

  it("keeps an intercepting segment under the preview's path, in a group or not", () => {
    expect(
      previewRoutesIn({
        "/admin/forhandsvisning/(.)x/page": "a.js",
        "/admin/forhandsvisning/(..)x/page": "b.js",
        "/(admin)/admin/forhandsvisning/(.)x/page": "c.js",
        "/(app)/@modal/(.)jobb/[id]/page": "d.js",
      }),
    ).toEqual([
      "/admin/forhandsvisning/(.)x/page",
      "/admin/forhandsvisning/(..)x/page",
      "/(admin)/admin/forhandsvisning/(.)x/page",
    ]);
  });
});

describe("filesHolding", () => {
  let dir = "";
  afterEach(() => {
    if (dir !== "") rmSync(dir, { recursive: true, force: true });
  });

  it("finds the needle in text files at any depth and skips other files", () => {
    dir = mkdtempSync(join(tmpdir(), "assert-preview-"));
    mkdirSync(join(dir, "chunks", "ssr"), { recursive: true });
    writeFileSync(join(dir, "chunks", "ssr", "a.js"), `const e = "ann@${SENTINEL}";`);
    writeFileSync(join(dir, "chunks", "b.json"), `{"clean": true}`);
    writeFileSync(join(dir, "image.png"), `binary ${SENTINEL}`);

    expect(filesHolding([dir, join(dir, "missing")], SENTINEL)).toEqual([join(dir, "chunks", "ssr", "a.js")]);
  });
});

describe("verdict", () => {
  it("passes a build without the flag that holds no preview", () => {
    expect(verdict({ flagOn: false, routes: [], leaks: [] }).ok).toBe(true);
  });

  it("fails a build without the flag that holds a preview route", () => {
    const result = verdict({ flagOn: false, routes: ["/(admin-preview)/admin/forhandsvisning/page"], leaks: [] });
    expect(result.ok).toBe(false);
    expect(result.message).toContain("/(admin-preview)/admin/forhandsvisning/page");
  });

  it("fails a build without the flag that holds a fixture value", () => {
    const result = verdict({ flagOn: false, routes: [], leaks: [".next/server/chunks/a.js"] });
    expect(result.ok).toBe(false);
    expect(result.message).toContain(".next/server/chunks/a.js");
  });

  it("announces a build with the flag as one never to deploy", () => {
    const result = verdict({ flagOn: true, routes: ["/(admin-preview)/admin/forhandsvisning/page"], leaks: [] });
    expect(result.ok).toBe(true);
    expect(result.message).toContain("NEVER DEPLOY");
  });

  it("fails a build with the flag that holds no preview route, because the compile lock did not work", () => {
    expect(verdict({ flagOn: true, routes: [], leaks: [] }).ok).toBe(false);
  });
});
