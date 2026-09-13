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
    const cta = within(card()).getByRole("link", { name: COPY.cards.matchingCtaAria });
    expect(cta).toHaveAttribute("href", HREF);
    expect(cta.className).toContain("jp-btn--primary");
    // 2.5.3 Label in Name: the visible text opens the accessible name.
    expect(cta.textContent?.trim()).toBe(COPY.cards.matchingCta);
    expect(COPY.cards.matchingCtaAria.startsWith(COPY.cards.matchingCta)).toBe(true);
  });

  it("a counted zero is a real answer: 0, the zero copy beneath, and an EMPHASISED way to the whole list — never the solid level", () => {
    render(<MatchingCard matchCount={0} matchHref={HREF} hasStatedOccupation span={4} />);
    expect(text(card().querySelector(".jp-ov-num__value"))).toBe("0");
    expect(within(card()).getByText(COPY.notices.matchTextZero)).toBeInTheDocument();
    expect(within(card()).queryByText(COPY.cards.matchingBasis)).toBeNull();
    // The facet-filtered list is empty by construction, so the solid CTA to it is gone.
    expect(within(card()).queryByRole("link", { name: COPY.cards.matchingCtaAria })).toBeNull();
    const cta = within(card()).getByRole("link", { name: COPY.cards.matchingCtaZero });
    expect(cta).toHaveAttribute("href", "/jobb");
    expect(cta.className).toContain("jp-btn--emphasis");
    expect(cta.className).not.toContain("jp-btn--primary");
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
    expect(within(card()).queryByRole("link", { name: COPY.cards.matchingCtaAria })).toBeNull();
  });

  it("the span prop reaches the grid attribute", () => {
    render(<MatchingCard matchCount={1} matchHref={HREF} hasStatedOccupation span={6} />);
    expect(card()).toHaveAttribute("data-span", "6");
  });
});
