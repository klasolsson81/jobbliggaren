import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { buildContinuationDocument, CONTINUATION_STYLE, escapeHtml } from "./continuation-document";
import { safeRedirectPath } from "./safe-redirect";

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

/** The document's one style element's text; fails unless there is exactly one, in the head. */
function styleOf(html: string): string {
  const doc = parse(html);
  expect(doc.querySelectorAll("style")).toHaveLength(1);
  expect(doc.head.querySelectorAll("style")).toHaveLength(1);
  return doc.head.querySelector("style")?.textContent ?? "";
}

/** CSS with its comments removed, spaces collapsed and letters lowercased, so a pin reads the rules alone. */
function normalise(css: string): string {
  let out = "";
  let at = 0;
  for (;;) {
    const open = css.indexOf("/*", at);
    if (open < 0) break;
    out += css.slice(at, open);
    const close = css.indexOf("*/", open + 2);
    at = close < 0 ? css.length : close + 2;
  }
  out += css.slice(at);
  return out.replace(/[ ]+/g, " ").toLowerCase();
}

/** The style's rules, one per line as the constant writes them: selector, then property to value. At-rules are skipped. */
function rulesOf(css: string): Map<string, Map<string, string>> {
  const rules = new Map<string, Map<string, string>>();
  for (const line of css.split("\n")) {
    const open = line.indexOf("{");
    if (open < 0 || line.startsWith("@")) continue;
    const declarations = new Map<string, string>();
    for (const declaration of line.slice(open + 1, line.lastIndexOf("}")).split(";")) {
      const colon = declaration.indexOf(":");
      if (colon > 0) declarations.set(declaration.slice(0, colon).trim(), declaration.slice(colon + 1).trim());
    }
    rules.set(line.slice(0, open).trim(), declarations);
  }
  return rules;
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

  it("requests no subresource: no script, no image, no frame, one style of its own, and only a data: icon", () => {
    const doc = parse(buildContinuationDocument(DOC));

    expect(doc.querySelectorAll("script, img, iframe, object, embed, video, audio, source")).toHaveLength(0);
    expect(doc.querySelectorAll("style")).toHaveLength(1);
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

  it("keeps a target the redirect guard never produces inside the attributes it is written into", () => {
    const hostile = `/cv?a="><script>x</script>&b='`;
    const doc = parse(buildContinuationDocument({ ...DOC, target: hostile }));

    expect(safeRedirectPath(hostile)).not.toMatch(/["<>]/);
    expect(doc.querySelectorAll("script")).toHaveLength(0);
    expect(doc.querySelector("a")?.getAttribute("href")).toBe(hostile);
    expect(doc.querySelector('meta[http-equiv="refresh"]')?.getAttribute("content")).toBe(`0;url=${hostile}`);
  });
});

// DESIGN.md §11.6 (#1746): one constant style. security-auditor's conditions on 6a's m-1 and senior-cto-advisor's
// ruling (docs/reviews/2026-09-27-1746-form-{security-auditor,cto}.md) are what these rows pin.
describe("the continuation document's style", () => {
  it("is the exported constant, written after the charset and the referrer policy", () => {
    const html = buildContinuationDocument(DOC);
    const head = parse(html).head;
    const index = [...head.children].findIndex((element) => element.tagName === "STYLE");

    expect(styleOf(html)).toBe(CONTINUATION_STYLE);
    expect(index).toBeGreaterThan(1);
  });

  it("writes the same style whatever the document is given", () => {
    const other = {
      lang: "en",
      title: "Log in with GitHub | Jobbliggaren",
      heading: "Log in with GitHub",
      continueLabel: "Continue",
      target: "/oversikt",
    };

    expect(styleOf(buildContinuationDocument(other))).toBe(styleOf(buildContinuationDocument(DOC)));
  });

  it("requests nothing through the style: no url, no image function, no import and no font face", () => {
    const css = normalise(CONTINUATION_STYLE);

    for (const fetching of ["url(", "src(", "image-set(", "cross-fade(", "@import", "@font-face"]) {
      expect(css).not.toContain(fetching);
    }
    expect(css).not.toMatch(/(^|[^-a-z])image\(/);
  });

  it("spells the style without an escape, which CSS resolves before it matches url(", () => {
    expect(CONTINUATION_STYLE).not.toContain(String.fromCharCode(92));
  });

  it("hides main, the body's only element, through one step-end animation and nothing else", () => {
    const body = parse(buildContinuationDocument(DOC)).body;
    const css = normalise(CONTINUATION_STYLE);
    const keyframes = css.indexOf("@keyframes");
    expect(keyframes).toBeGreaterThanOrEqual(0);
    // The keyframes block is the only nested block in the constant: it ends at the first "}}" after it opens.
    const outside = css.slice(0, keyframes) + css.slice(css.indexOf("}}", keyframes) + 2);

    expect([...body.children].map((element) => element.tagName)).toEqual(["MAIN"]);
    for (const hiding of ["visibility:hidden", "opacity:0", "display:none"]) {
      expect(outside.replace(/ /g, "")).not.toContain(hiding);
    }
    expect(css).toContain("\n@keyframes jbl-hold{from,to{visibility:hidden}}\n");
    expect(rulesOf(css).get("main")?.get("animation")).toBe("jbl-hold 2s step-end");
    expect(css.match(/animation/g)).toHaveLength(1);
  });

  it("hides nothing outside the style: no hidden attribute and no style attribute", () => {
    expect(parse(buildContinuationDocument(DOC)).querySelectorAll("[hidden], [style]")).toHaveLength(0);
  });

  it("keeps the hold under reduced motion: the style has no prefers-reduced-motion rule", () => {
    expect(normalise(CONTINUATION_STYLE)).not.toContain("prefers-reduced-motion");
  });
});

// DESIGN.md §11.6 point 1: every literal is a token's light value, joined to globals.css as §11.5 does for mail.
describe("the continuation document's style mirrors the design tokens", () => {
  const HERE = dirname(fileURLToPath(import.meta.url));
  const globals = normalise(readFileSync(resolve(HERE, "../../app/globals.css"), "utf-8"));

  /** The first :root block: the light theme's token definitions. */
  function lightTokens(): Map<string, string> {
    const start = globals.indexOf(":root {");
    expect(start).toBeGreaterThanOrEqual(0);
    const open = globals.indexOf("{", start);
    let depth = 0;
    let end = open;
    for (let i = open; i < globals.length; i++) {
      if (globals[i] === "{") depth++;
      if (globals[i] === "}" && --depth === 0) {
        end = i;
        break;
      }
    }
    const tokens = new Map<string, string>();
    for (const declaration of globals.slice(open + 1, end).split(";")) {
      const colon = declaration.indexOf(":");
      const name = declaration.slice(0, colon).trim();
      if (colon > 0 && name.startsWith("--jp-")) tokens.set(name, declaration.slice(colon + 1).trim());
    }
    return tokens;
  }

  function resolved(tokens: Map<string, string>, name: string): string {
    let value = tokens.get(name) ?? "";
    for (let hops = 0; value.startsWith("var(") && hops < 5; hops++) {
      value = tokens.get(value.slice(4, value.indexOf(")")).trim()) ?? "";
    }
    return value;
  }

  // One row per colour declaration: its selector, its property, its literal and the tokens the literal copies.
  const MIRROR: ReadonlyArray<readonly [string, string, string, readonly string[]]> = [
    [":root", "background", "#f4f6fa", ["--jp-canvas", "--jp-surface-2"]],
    [":root", "color", "#0c1a2e", ["--jp-ink-1"]],
    ["h1", "color", "#133f73", ["--jp-heading-1"]],
    ["a", "color", "#15603f", ["--jp-accent-700"]],
    ["a:focus-visible", "outline", "#15603f", ["--jp-accent-700"]],
  ];

  it("writes each colour literal where its row says, and no literal anywhere else", () => {
    const css = normalise(CONTINUATION_STYLE);
    const rules = rulesOf(css);

    for (const [selector, property, hex] of MIRROR) {
      expect(rules.get(selector)?.get(property)?.match(/#[0-9a-f]+/g)).toEqual([hex]);
    }
    expect(css.match(/#[0-9a-f]{3,8}/g)).toHaveLength(MIRROR.length);
  });

  it("writes no colour through a function: the style calls none", () => {
    expect(normalise(CONTINUATION_STYLE)).not.toContain("(");
  });

  it.each(MIRROR)("%s %s: %s is the light value of %j", (_selector, _property, hex, names) => {
    const tokens = lightTokens();
    for (const name of names) expect(resolved(tokens, name)).toBe(hex);
  });

  it("is light only while theme-provider.tsx keeps dark mode off", () => {
    const provider = readFileSync(resolve(HERE, "../../components/theme-provider.tsx"), "utf-8");

    expect(rulesOf(normalise(CONTINUATION_STYLE)).get(":root")?.get("color-scheme")).toBe("light");
    expect(provider).toContain("const DARK_MODE_ENABLED: boolean = false;");
  });

  it("uses the system tail of --jp-font-sans, after the web font", () => {
    const tokens = lightTokens();
    const stack = tokens.get("--jp-font-sans") ?? "";
    const tail = stack.slice(stack.indexOf(",") + 1).replace(/, /g, ",").trim();
    const css = normalise(CONTINUATION_STYLE);
    const shorthand = "font:16px/1.55 ";
    const after = css.slice(css.indexOf(shorthand) + shorthand.length);

    expect(stack.startsWith("var(--font-sans)")).toBe(true);
    expect(after.slice(0, after.search(/[;}]/)).replace(/, /g, ",").trim()).toBe(tail);
  });
});

describe("escapeHtml", () => {
  it("escapes the five characters that can leave an attribute or open an element", () => {
    expect(escapeHtml(`&<>"'`)).toBe("&amp;&lt;&gt;&quot;&#39;");
  });
});
