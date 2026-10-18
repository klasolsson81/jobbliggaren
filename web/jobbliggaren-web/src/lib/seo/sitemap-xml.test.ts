import { describe, expect, it } from "vitest";
import { escapeXml, MAX_URLS_PER_SITEMAP, SITEMAP_NAMESPACE, sitemapIndexXml, urlsetXml } from "./sitemap-xml";

// ADR 0174 — the sitemap's XML: escaped by construction, loc and nothing else, one entry per line, never over a
// file's limit.

function parse(xml: string): Document {
  const doc = new DOMParser().parseFromString(xml, "application/xml");
  expect(doc.getElementsByTagName("parsererror")).toHaveLength(0);
  return doc;
}

/** Every element name in the document, so an element the rule does not allow cannot hide. */
function elementNames(doc: Document): Set<string> {
  return new Set(Array.from(doc.getElementsByTagName("*"), (element) => element.localName));
}

describe("escapeXml", () => {
  it.each([
    ["&", "&amp;"],
    ["<", "&lt;"],
    [">", "&gt;"],
    ['"', "&quot;"],
    ["'", "&apos;"],
    ["a&b<c>d\"e'f", "a&amp;b&lt;c&gt;d&quot;e&apos;f"],
    ["https://jobbliggaren.se/jobb/0b0b0b0b-1111-4222-8333-444444444444", "https://jobbliggaren.se/jobb/0b0b0b0b-1111-4222-8333-444444444444"],
  ])("escapes %j", (input, escaped) => {
    expect(escapeXml(input)).toBe(escaped);
  });
});

describe("urlsetXml", () => {
  it("is exactly a prolog, the sitemaps.org urlset and one url per line", () => {
    expect(urlsetXml(["https://a.test/x", "https://a.test/y"])).toBe(
      [
        '<?xml version="1.0" encoding="UTF-8"?>',
        '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
        "<url><loc>https://a.test/x</loc></url>",
        "<url><loc>https://a.test/y</loc></url>",
        "</urlset>",
        "",
      ].join("\n")
    );
  });

  it("parses, in the sitemaps.org namespace, with no element but urlset, url and loc", () => {
    const doc = parse(urlsetXml(["https://a.test/?q=1&r=2", "https://a.test/<x>"]));

    expect(doc.documentElement.namespaceURI).toBe(SITEMAP_NAMESPACE);
    expect(elementNames(doc)).toEqual(new Set(["urlset", "url", "loc"]));
    expect(Array.from(doc.getElementsByTagName("loc"), (loc) => loc.textContent)).toEqual([
      "https://a.test/?q=1&r=2",
      "https://a.test/<x>",
    ]);
  });

  it("is an empty urlset for no pages", () => {
    const doc = parse(urlsetXml([]));

    expect(doc.getElementsByTagName("url")).toHaveLength(0);
  });

  it("holds exactly the limit, and refuses one more", () => {
    const atLimit = Array.from({ length: MAX_URLS_PER_SITEMAP }, (_, i) => `https://a.test/${i}`);

    expect(urlsetXml(atLimit).match(/<loc>/g)).toHaveLength(MAX_URLS_PER_SITEMAP);
    expect(() => urlsetXml([...atLimit, "https://a.test/one-more"])).toThrow(RangeError);
  });
});

describe("sitemapIndexXml", () => {
  it("parses, in the sitemaps.org namespace, with no element but sitemapindex, sitemap and loc", () => {
    const doc = parse(sitemapIndexXml(["https://a.test/sitemap-sidor.xml", "https://a.test/sitemap-jobb-1.xml"]));

    expect(doc.documentElement.localName).toBe("sitemapindex");
    expect(doc.documentElement.namespaceURI).toBe(SITEMAP_NAMESPACE);
    expect(elementNames(doc)).toEqual(new Set(["sitemapindex", "sitemap", "loc"]));
  });
});
