import { describe, it, expect } from "vitest";
import svMessages from "../../messages/sv";
import enMessages from "../../messages/en";
import { isServerOnlyNamespace, pickClientMessages } from "./client-messages";

const SERVER_ONLY = [
  "content-cv-granskning",
  "content-faq",
  "content-legal",
  "content-matchning",
  "content-tips",
  "metadata",
  "errors",
];

describe("pickClientMessages", () => {
  it("keeps exactly the declared namespaces", () => {
    const client = pickClientMessages(svMessages, ["common", "landing"]);
    expect(Object.keys(client).sort()).toEqual(["common", "landing"]);
  });

  it("returns an empty payload for an empty declaration (the root boundary)", () => {
    // Root wraps every route, so its payload is added to every document on top
    // of the nested boundary's own set — an empty declaration must really mean
    // empty, not "fall back to everything".
    expect(Object.keys(pickClientMessages(svMessages, []))).toEqual([]);
  });

  it("strips server-only namespaces even when a declaration names them", () => {
    // Defence in depth: the fitness function rejects such a declaration at test
    // time (R6), but the function must not ship 50 KB of legal copy to a client
    // if one ever slips through.
    const client = pickClientMessages(svMessages, [
      "common",
      ...SERVER_ONLY,
    ]);
    expect(Object.keys(client)).toEqual(["common"]);
  });

  it("classifies every content-* namespace as server-only, including future ones", () => {
    expect(isServerOnlyNamespace("content-anything-new")).toBe(true);
    expect(isServerOnlyNamespace("metadata")).toBe(true);
    expect(isServerOnlyNamespace("errors")).toBe(true);
    expect(isServerOnlyNamespace("common")).toBe(false);
    expect(isServerOnlyNamespace("admin")).toBe(false);
  });

  it("ignores a declared namespace the catalog does not have", () => {
    // A typo must not crash the layout — next-intl surfaces the resulting
    // MISSING_MESSAGE, and the fitness function catches the typo at test time.
    expect(Object.keys(pickClientMessages(svMessages, ["common", "nope"]))).toEqual(["common"]);
  });

  it("does not mutate the source catalog", () => {
    const before = Object.keys(svMessages).length;
    pickClientMessages(svMessages, ["common"]);
    expect(Object.keys(svMessages)).toHaveLength(before);
    expect(svMessages).toHaveProperty("content-legal");
    expect(svMessages).toHaveProperty("admin");
  });

  it("picks en identically (both locales share the top-level namespace set)", () => {
    // Asserts no locale-only namespace drift: the same declaration must yield
    // the same top-level set in both catalogs.
    const declaration = ["common", "landing", "pages", "admin"];
    expect(Object.keys(pickClientMessages(enMessages, declaration)).sort()).toEqual(
      Object.keys(pickClientMessages(svMessages, declaration)).sort()
    );
    for (const ns of SERVER_ONLY) {
      expect(pickClientMessages(enMessages, [ns])).not.toHaveProperty(ns);
    }
  });
});
