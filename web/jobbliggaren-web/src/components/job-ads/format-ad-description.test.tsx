import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import {
  formatAdDescription,
  parseAdDescription,
} from "./format-ad-description";

describe("parseAdDescription — block-detektering", () => {
  it("returnerar tom array för tom input", () => {
    expect(parseAdDescription("")).toEqual([]);
    expect(parseAdDescription("   ")).toEqual([]);
    expect(parseAdDescription("\n\n  \n")).toEqual([]);
  });

  it("delar text på dubbla radbrytningar till stycken", () => {
    const raw = "Första stycket är lite längre och slutar med punkt.\n\nAndra stycket finns också här.";
    const blocks = parseAdDescription(raw);
    expect(blocks).toHaveLength(2);
    expect(blocks[0]).toEqual({
      kind: "paragraph",
      text: "Första stycket är lite längre och slutar med punkt.",
    });
    expect(blocks[1]).toEqual({
      kind: "paragraph",
      text: "Andra stycket finns också här.",
    });
  });

  it("normaliserar CRLF och CR till LF", () => {
    const raw = "Stycke ett som är längre.\r\n\r\nStycke två lika långt.";
    const blocks = parseAdDescription(raw);
    expect(blocks).toHaveLength(2);
  });

  it("ignorerar block med bara whitespace", () => {
    const raw = "Stycke ett som är längre.\n   \n\nStycke två lika långt.";
    const blocks = parseAdDescription(raw);
    expect(blocks).toHaveLength(2);
  });
});

describe("parseAdDescription — heading-detektering", () => {
  it("känner igen kort enrads-block utan terminator som heading", () => {
    const raw = "Kvalifikationer\n\nDu har minst 3 års erfarenhet av .NET.";
    const blocks = parseAdDescription(raw);
    expect(blocks[0]).toEqual({ kind: "heading", text: "Kvalifikationer" });
    expect(blocks[1]?.kind).toBe("paragraph");
  });

  it("känner igen vanliga Platsbanken-rubriker", () => {
    const labels = [
      "Beskrivning",
      "Övrigt",
      "Om arbetsgivaren",
      "Kvalifikationer",
      "Kontakt",
    ];
    for (const label of labels) {
      const blocks = parseAdDescription(`${label}\n\nLite text efter rubriken.`);
      expect(blocks[0]).toEqual({ kind: "heading", text: label });
    }
  });

  it("behandlar lång enrads-block som paragraph (>60 tecken)", () => {
    const longLine =
      "Detta är en relativt lång rad utan slutpunkt som ändå borde bli paragraph";
    const blocks = parseAdDescription(longLine);
    expect(blocks[0]?.kind).toBe("paragraph");
  });

  it("behandlar enrads-block med terminator som paragraph", () => {
    const samples = [
      "Korta meningen.",
      "Spännande tjänst!",
      "Är du rätt person?",
      "Saker, ting och annat,",
    ];
    for (const s of samples) {
      const blocks = parseAdDescription(s);
      expect(blocks).toHaveLength(1);
      expect(blocks[0]?.kind).toBe("paragraph");
    }
  });

  it("känner igen kort enrads-block med kolon-suffix som heading", () => {
    // "Your responsibilities:", "Kvalifikationer:", "Tjänsten innebär:" är
    // alla giltiga rubriker — kolon avslutar rubrik, inte mening.
    const labels = [
      "Tjänsten innebär:",
      "Your responsibilities:",
      "Kvalifikationer:",
    ];
    for (const label of labels) {
      const blocks = parseAdDescription(`${label}\n\nText efter.`);
      expect(blocks[0]).toEqual({ kind: "heading", text: label });
    }
  });

  it("behandlar flerrads-block som paragraph även om varje rad är kort", () => {
    const raw = "Stockholm\nGöteborg\nMalmö";
    const blocks = parseAdDescription(raw);
    expect(blocks).toHaveLength(1);
    expect(blocks[0]).toEqual({
      kind: "paragraph",
      text: "Stockholm\nGöteborg\nMalmö",
    });
  });
});

describe("parseAdDescription — bullet-list-detektering", () => {
  it("känner igen rena bullet-block med dash-prefix", () => {
    const raw = "- Punkt ett\n- Punkt två\n- Punkt tre";
    const blocks = parseAdDescription(raw);
    expect(blocks[0]).toEqual({
      kind: "list",
      items: ["Punkt ett", "Punkt två", "Punkt tre"],
    });
  });

  it("känner igen bullet-block med bullet-tecken", () => {
    const raw = "• Ett\n• Två\n• Tre";
    const blocks = parseAdDescription(raw);
    expect(blocks[0]).toEqual({
      kind: "list",
      items: ["Ett", "Två", "Tre"],
    });
  });

  it("känner igen bullet-block med asterisk-prefix", () => {
    const raw = "* Ett\n* Två";
    const blocks = parseAdDescription(raw);
    expect(blocks[0]).toEqual({ kind: "list", items: ["Ett", "Två"] });
  });

  it("behandlar blandade rader (text + bullet) som paragraph", () => {
    const raw = "Detta är vanlig text.\n- Och denna är bullet";
    const blocks = parseAdDescription(raw);
    expect(blocks[0]?.kind).toBe("paragraph");
  });

  it("integration: heading + heading + bullet-list (kolon avslutar rubrik)", () => {
    const raw =
      "Kvalifikationer\n\nDu har följande kompetenser:\n\n- C# och .NET\n- SQL och relationsdatabaser\n- Erfarenhet av agila metoder";
    const blocks = parseAdDescription(raw);
    expect(blocks).toHaveLength(3);
    expect(blocks[0]?.kind).toBe("heading");
    expect(blocks[1]?.kind).toBe("heading");
    expect(blocks[2]?.kind).toBe("list");
  });
});

describe("formatAdDescription — rendering", () => {
  it("returnerar null för tom input", () => {
    expect(formatAdDescription("")).toBeNull();
  });

  it("renderar h3 för headings med rätt CSS-klass", () => {
    const { container } = render(
      <>{formatAdDescription("Kvalifikationer\n\nDu har minst 3 års erfarenhet.")}</>,
    );
    const h3 = container.querySelector(".jp-ad-desc__h3");
    expect(h3).not.toBeNull();
    expect(h3?.textContent).toBe("Kvalifikationer");
  });

  it("renderar p för stycken med rätt CSS-klass", () => {
    const { container } = render(
      <>{formatAdDescription("Detta är ett vanligt stycke med text.")}</>,
    );
    const p = container.querySelector(".jp-ad-desc__p");
    expect(p?.textContent).toBe("Detta är ett vanligt stycke med text.");
  });

  it("renderar ul/li för bullet-list", () => {
    const { container } = render(
      <>{formatAdDescription("- Ett\n- Två\n- Tre")}</>,
    );
    const ul = container.querySelector(".jp-ad-desc__list");
    expect(ul).not.toBeNull();
    expect(ul?.querySelectorAll("li")).toHaveLength(3);
  });

  it("escaper HTML i texten (XSS-säker)", () => {
    const xss = "<script>alert('xss')</script>";
    const { container } = render(<>{formatAdDescription(xss)}</>);
    // React escapar children — texten ska bli synlig som-är, inga script-element
    expect(container.querySelector("script")).toBeNull();
    expect(container.textContent).toContain("<script>alert('xss')</script>");
  });
});
