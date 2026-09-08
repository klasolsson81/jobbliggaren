import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { CriterionBreadth } from "./criterion-breadth";

// Real SNI 2025 leaf codes, read from
// `src/Jobbliggaren.Infrastructure/CompanyRegister/Reference/sni-2025.v1.json`: huvudgrupp 62
// ("Dataprogrammering, datakonsultverksamhet o.d.") has exactly these four leaves. Both fixtures
// are shapes the write path produces — the picker expands a huvudgrupp selection to its leaf codes
// FE-side and the wire carries leaves only (`lib/dto/company-criteria.ts`), so "one leaf" and "the
// whole huvudgrupp" differ by the LENGTH of the array and by nothing else. That is the entire
// premise of this component.
const ONE_LEAF = ["62100"];
const WHOLE_HUVUDGRUPP = ["62100", "62201", "62202", "62900"];
const ONE_KOMMUN = ["1480"];

function text(): string {
  return (
    document.querySelector(".jp-criterion-breadth")?.textContent ?? ""
  ).replace(/\s+/g, " ").trim();
}

describe("CriterionBreadth", () => {
  it("renderar båda axlarna i singular med husets avdelare", () => {
    render(<CriterionBreadth sniCodes={ONE_LEAF} municipalityCodes={ONE_KOMMUN} />);
    expect(text()).toBe("1 bransch · 1 kommun");
  });

  it("plural på båda axlarna", () => {
    render(
      <CriterionBreadth
        sniCodes={WHOLE_HUVUDGRUPP}
        municipalityCodes={["1480", "1481", "0180"]}
      />,
    );
    expect(text()).toBe("4 branscher · 3 kommuner");
  });

  // ⛔ THE defect this component exists to answer (design-reviewer B5, 2026-09-08). Both watches
  // render the IDENTICAL heading, because `deriveDisplayLabel` names the huvudgrupp covering the
  // leaves and says nothing about how many were picked — `display-label.test.ts` pins that on
  // purpose. If this assertion ever fails, `/oversikt` and the two detail pages have gone back to
  // showing a narrow watch and a broad one as the same thing.
  it("skiljer ett enda löv från hela huvudgruppen — samma rubrik, olika breddrad", () => {
    const { unmount } = render(
      <CriterionBreadth sniCodes={ONE_LEAF} municipalityCodes={ONE_KOMMUN} />,
    );
    const narrow = text();
    unmount();

    render(
      <CriterionBreadth sniCodes={WHOLE_HUVUDGRUPP} municipalityCodes={ONE_KOMMUN} />,
    );
    const broad = text();

    expect(narrow).toBe("1 bransch · 1 kommun");
    expect(broad).toBe("4 branscher · 1 kommun");
    expect(narrow).not.toBe(broad);
  });

  // The count is `sniCodes.length` and NOT `decomposeSelection(...).length`, which the edit dialog
  // uses and which collapses a fully selected node to one option (`criterion-options.ts`). Under
  // that rule this fixture would read "1 bransch" and the test above would fail. #1711 owns the
  // divergence; this line is the guard that a harmonisation cannot land here unnoticed.
  it("räknar RÅA löv, aldrig pickerns dekomponerade tal", () => {
    render(
      <CriterionBreadth sniCodes={WHOLE_HUVUDGRUPP} municipalityCodes={ONE_KOMMUN} />,
    );
    expect(text()).toContain("4 branscher");
  });

  // The class is the CSS rule's only hook (`globals.css` `.jp-criterion-breadth`) and the rendered
  // verification's only selector. A renamed class would silently drop both.
  it("bär klassen som stilregeln och den renderade verifieringen hänger på", () => {
    render(<CriterionBreadth sniCodes={ONE_LEAF} municipalityCodes={ONE_KOMMUN} />);
    const node = document.querySelector(".jp-criterion-breadth");
    expect(node).not.toBeNull();
    expect(node?.tagName).toBe("P");
  });

  // Extent is a PROPERTY of the watch, not a status about it: a live region would announce a fact
  // that never changes without a navigation, and nothing here is interactive.
  it("lägger till ingen roll, ingen live-region och inget fokuserbart element", () => {
    render(<CriterionBreadth sniCodes={ONE_LEAF} municipalityCodes={ONE_KOMMUN} />);
    const node = document.querySelector(".jp-criterion-breadth")!;
    expect(node.getAttribute("role")).toBeNull();
    expect(node.getAttribute("aria-live")).toBeNull();
    expect(screen.queryAllByRole("link")).toHaveLength(0);
    expect(screen.queryAllByRole("button")).toHaveLength(0);
  });
});
