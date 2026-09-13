import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { MatchingCard } from "./matching-card";
import messages from "../../../messages/sv";

const COPY = messages.oversikt;
const HREF = "/jobb?occupationGroup=grp_dev&region=region_AB";

function card() {
  return screen.getByRole("region", { name: COPY.cards.matching });
}
function text(el: Element | null): string {
  return (el?.textContent ?? "").replace(/\s+/g, " ").trim();
}

describe("MatchingCard", () => {
  it("a count renders as the big number with its unit, the basis line, and the solid CTA to that count's list", () => {
    render(<MatchingCard matchCount={245} matchHref={HREF} hasStatedOccupation span={4} />);
    expect(text(card().querySelector(".jp-ov-num__value"))).toBe("245");
    expect(text(card().querySelector(".jp-ov-num__unit"))).toBe("annonser matchar dina val");
    expect(within(card()).getByText(COPY.cards.matchingBasis)).toBeInTheDocument();
    const cta = within(card()).getByRole("link", { name: COPY.cards.matchingCta });
    expect(cta).toHaveAttribute("href", HREF);
    expect(cta.className).toContain("jp-btn--primary");
  });

  it("a counted zero is a real answer: 0, the zero copy beneath, the CTA kept", () => {
    render(<MatchingCard matchCount={0} matchHref={HREF} hasStatedOccupation span={4} />);
    expect(text(card().querySelector(".jp-ov-num__value"))).toBe("0");
    expect(within(card()).getByText(COPY.notices.matchTextZero)).toBeInTheDocument();
    expect(within(card()).queryByText(COPY.cards.matchingBasis)).toBeNull();
    expect(within(card()).getByRole("link", { name: COPY.cards.matchingCta })).toHaveAttribute(
      "href",
      HREF,
    );
  });

  it("an unmeasured count renders an en-dash, the unavailable copy and NO CTA", () => {
    render(<MatchingCard matchCount={null} matchHref={HREF} hasStatedOccupation span={4} />);
    expect(text(card().querySelector(".jp-ov-num__value"))).toBe(COPY.cards.unmeasured);
    expect(card().querySelector(".jp-ov-num__unit")).toBeNull();
    expect(within(card()).getByText(COPY.cards.matchingUnavailable)).toBeInTheDocument();
    expect(within(card()).queryByRole("link")).toBeNull();
  });

  it("no stated occupation: the setup callout takes the card, with the page's only settings link", () => {
    render(<MatchingCard matchCount={42} matchHref={HREF} hasStatedOccupation={false} span={4} />);
    expect(card().querySelector(".jp-ov-num")).toBeNull();
    expect(within(card()).getByText(/Matchningen är inte klar/)).toBeInTheDocument();
    const cta = within(card()).getByRole("link", { name: /Ställ in matchning/ });
    expect(cta).toHaveAttribute("href", "/oversikt?matchsetup=1");
    expect(within(card()).getByText(COPY.notices.calloutHint)).toBeInTheDocument();
    expect(within(card()).queryByRole("link", { name: COPY.cards.matchingCta })).toBeNull();
  });

  it("the span prop reaches the grid attribute", () => {
    render(<MatchingCard matchCount={1} matchHref={HREF} hasStatedOccupation span={6} />);
    expect(card()).toHaveAttribute("data-span", "6");
  });
});
