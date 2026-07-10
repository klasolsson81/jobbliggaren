import { describe, it, expect } from "vitest";
import svMessages from "../../messages/sv";
import enMessages from "../../messages/en";
import { pickClientMessages } from "./client-messages";

const STRIPPED = [
  "content-cv-granskning",
  "content-faq",
  "content-legal",
  "content-matchning",
  "content-tips",
  "metadata",
  "errors",
];

describe("pickClientMessages", () => {
  it("strips the server-only namespaces from the client payload", () => {
    const client = pickClientMessages(svMessages);
    for (const ns of STRIPPED) {
      expect(client).not.toHaveProperty(ns);
    }
    // Any content-* namespace (prefix rule), including future ones.
    for (const ns of Object.keys(client)) {
      expect(ns.startsWith("content-")).toBe(false);
    }
  });

  it("strips admin by default (used client-side only in the (admin) group)", () => {
    expect(pickClientMessages(svMessages)).not.toHaveProperty("admin");
  });

  it("keeps admin when includeAdmin is set (the (admin) group provider)", () => {
    const adminClient = pickClientMessages(svMessages, { includeAdmin: true });
    expect(adminClient).toHaveProperty("admin");
    // Still strips the globally-server-only namespaces on admin routes.
    for (const ns of STRIPPED) {
      expect(adminClient).not.toHaveProperty(ns);
    }
  });

  it("keeps the namespaces client components actually use", () => {
    const client = pickClientMessages(svMessages);
    for (const ns of ["pages", "common", "jobads", "applications", "settings", "validation"]) {
      expect(client).toHaveProperty(ns);
    }
  });

  it("does not mutate the source catalog", () => {
    const before = Object.keys(svMessages).length;
    pickClientMessages(svMessages);
    expect(Object.keys(svMessages)).toHaveLength(before);
    expect(svMessages).toHaveProperty("content-legal");
    expect(svMessages).toHaveProperty("admin");
  });

  it("prunes en identically (both locales share the top-level namespace set)", () => {
    // en carries -40.3% by the same prune; assert its client set matches sv's
    // top-level namespaces so a locale-only namespace drift can't slip through.
    expect(Object.keys(pickClientMessages(enMessages)).sort()).toEqual(
      Object.keys(pickClientMessages(svMessages)).sort()
    );
    for (const ns of STRIPPED) {
      expect(pickClientMessages(enMessages)).not.toHaveProperty(ns);
    }
    expect(pickClientMessages(enMessages)).not.toHaveProperty("admin");
    expect(pickClientMessages(enMessages, { includeAdmin: true })).toHaveProperty(
      "admin"
    );
  });
});
