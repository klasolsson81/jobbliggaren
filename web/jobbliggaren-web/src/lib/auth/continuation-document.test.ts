import { describe, expect, it } from "vitest";
import { buildContinuationDocument, escapeHtml } from "./continuation-document";

const DOC = {
  lang: "sv",
  title: "Logga in med Google | Jobbliggaren",
  heading: "Logga in med Google",
  continueLabel: "Fortsätt",
  target: "/ansokningar/abc-123?flik=status",
};

function parse(html: string): Document {
  return new DOMParser().parseFromString(html, "text/html");
}

describe("the continuation document", () => {
  it("declares its charset first and its referrer policy before anything that fetches or navigates", () => {
    const head = parse(buildContinuationDocument(DOC)).head;
    const first = head.children[0];
    const second = head.children[1];

    expect(first?.tagName).toBe("META");
    expect(first?.getAttribute("charset")).toBe("utf-8");
    expect(second?.getAttribute("name")).toBe("referrer");
    expect(second?.getAttribute("content")).toBe("no-referrer");
  });

  it("puts the charset inside the first 1024 bytes, where a browser looks for it", () => {
    const html = buildContinuationDocument(DOC);
    expect(Buffer.from(html, "utf8").subarray(0, 1024).toString("utf8")).toContain('<meta charset="utf-8">');
  });

  it("requests no subresource: no script, no style, no image, no frame, and only a data: icon", () => {
    const doc = parse(buildContinuationDocument(DOC));

    expect(doc.querySelectorAll("script, style, img, iframe, object, embed, video, audio, source")).toHaveLength(0);
    const links = [...doc.querySelectorAll("link")];
    expect(links.map((l) => [l.getAttribute("rel"), l.getAttribute("href")])).toEqual([["icon", "data:,"]]);
    expect(doc.querySelectorAll("[src]")).toHaveLength(0);
    expect(doc.querySelectorAll("[style]")).toHaveLength(0);
  });

  it("sends the refresh and the one link to the same target", () => {
    const doc = parse(buildContinuationDocument(DOC));

    const refresh = doc.querySelector('meta[http-equiv="refresh"]')?.getAttribute("content");
    const links = doc.querySelectorAll("a[href]");
    expect(refresh).toBe(`0;url=${DOC.target}`);
    expect(links).toHaveLength(1);
    expect(links[0]?.getAttribute("href")).toBe(DOC.target);
    expect(links[0]?.getAttribute("rel")).toBe("noreferrer");
    expect(links[0]?.textContent).toBe("Fortsätt");
  });

  it("is a page a screen reader can name: lang, viewport, title, one main and one h1", () => {
    const doc = parse(buildContinuationDocument(DOC));

    expect(doc.documentElement.getAttribute("lang")).toBe("sv");
    expect(doc.querySelector('meta[name="viewport"]')?.getAttribute("content")).toBe(
      "width=device-width, initial-scale=1"
    );
    expect(doc.title).toBe(DOC.title);
    expect(doc.querySelectorAll("main")).toHaveLength(1);
    expect(doc.querySelectorAll("h1")).toHaveLength(1);
    expect(doc.querySelector("main h1")?.textContent).toBe(DOC.heading);
  });

  it("escapes the target for the attributes it is written into", () => {
    const hostile = `/cv?a="><script>x</script>&b='`;
    const doc = parse(buildContinuationDocument({ ...DOC, target: hostile }));

    expect(doc.querySelectorAll("script")).toHaveLength(0);
    expect(doc.querySelector("a")?.getAttribute("href")).toBe(hostile);
    expect(doc.querySelector('meta[http-equiv="refresh"]')?.getAttribute("content")).toBe(`0;url=${hostile}`);
  });
});

describe("escapeHtml", () => {
  it("escapes the five characters that can leave an attribute or open an element", () => {
    expect(escapeHtml(`&<>"'`)).toBe("&amp;&lt;&gt;&quot;&#39;");
  });
});
