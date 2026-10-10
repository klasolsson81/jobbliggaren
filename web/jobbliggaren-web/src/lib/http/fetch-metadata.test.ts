import { describe, expect, it } from "vitest";
import { isCrossSiteDocumentNavigation, isEmbedded, isPrefetch } from "./fetch-metadata";

const MAIL_CLICK = { "sec-fetch-site": "cross-site", "sec-fetch-mode": "navigate", "sec-fetch-dest": "document" };

describe("isCrossSiteDocumentNavigation", () => {
  it("is a top-level document navigation another site started", () => {
    expect(isCrossSiteDocumentNavigation(new Headers(MAIL_CLICK))).toBe(true);
  });

  it.each<[string, Record<string, string>]>([
    ["no Fetch Metadata at all", {}],
    ["a same-origin navigation", { ...MAIL_CLICK, "sec-fetch-site": "same-origin" }],
    ["a same-site navigation", { ...MAIL_CLICK, "sec-fetch-site": "same-site" }],
    ["an address-bar navigation", { ...MAIL_CLICK, "sec-fetch-site": "none" }],
    ["a missing site", { "sec-fetch-mode": "navigate", "sec-fetch-dest": "document" }],
    ["a fetch", { ...MAIL_CLICK, "sec-fetch-mode": "cors" }],
    ["a frame", { ...MAIL_CLICK, "sec-fetch-dest": "iframe" }],
    ["a prefetch", { ...MAIL_CLICK, "sec-purpose": "prefetch;prerender" }],
  ])("is not %s", (_label, headers) => {
    expect(isCrossSiteDocumentNavigation(new Headers(headers))).toBe(false);
  });
});

describe("isPrefetch and isEmbedded", () => {
  it.each<Record<string, string>>([{ "next-router-prefetch": "1" }, { "sec-purpose": "prefetch" }, { purpose: "prefetch" }])(
    "reads %j as a prefetch",
    (headers) => {
      expect(isPrefetch(new Headers(headers))).toBe(true);
    }
  );

  it("lets a client that sends neither header through as not embedded", () => {
    expect(isEmbedded(new Headers())).toBe(false);
  });

  it.each<Record<string, string>>([{ "sec-fetch-mode": "no-cors" }, { "sec-fetch-dest": "image" }])("reads %j as embedded", (headers) => {
    expect(isEmbedded(new Headers(headers))).toBe(true);
  });
});
