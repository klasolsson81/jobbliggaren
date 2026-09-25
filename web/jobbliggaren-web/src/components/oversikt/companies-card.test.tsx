import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { CompaniesCard } from "./companies-card";
import messages from "../../../messages/sv";
import { buildCompanyJobsHref } from "@/lib/job-ads/company-jobs-href";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { CompanyWatch, ListCompanyWatchesResult } from "@/lib/dto/company-follows";

function watch(overrides: Partial<CompanyWatch> = {}): CompanyWatch {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    organizationNumber: "5566524301",
    isProtectedIdentity: false,
    companyName: "Friday Väst AB",
    followedAt: "2026-07-01T10:00:00Z",
    activeAdCount: 131,
    matchingAdCount: 9,
    filter: null,
    ...overrides,
  };
}
const ok = (items: CompanyWatch[]): ApiResult<ListCompanyWatchesResult> => ({
  kind: "ok",
  data: items,
});
const COPY = messages.oversikt;

function card() {
  return screen.getByRole("region", { name: COPY.companySummary.heading });
}
function text(el: Element | null): string {
  return (el?.textContent ?? "").replace(/\s+/g, " ").trim();
}

describe("CompaniesCard", () => {
  it("the matching sum is the big number; the anchor sentence links the active sum; the solid CTA goes to the matching ads", () => {
    render(<CompaniesCard watches={ok([watch()])} newAdCount={0} span={4} />);
    expect(text(card().querySelector(".jp-ov-num__value"))).toBe("9");
    expect(text(card().querySelector(".jp-ov-num__unit"))).toBe("matchande annonser");
    expect(text(card().querySelector(".jp-ov-sub"))).toBe("1 bevakat företag · 131 aktiva annonser");
    expect(within(card()).getByRole("link", { name: "131 aktiva annonser" })).toHaveAttribute(
      "href",
      buildCompanyJobsHref(["5566524301"], "all"),
    );
    const cta = within(card()).getByRole("link", { name: COPY.cards.companiesCtaAria });
    expect(cta).toHaveAttribute("href", buildCompanyJobsHref(["5566524301"], "matching"));
    expect(cta.className).toContain("jp-ov-cta--follow");
    expect(cta.textContent?.trim()).toBe(COPY.cards.matchingCta);
    expect(COPY.cards.companiesCtaAria.startsWith(COPY.cards.matchingCta)).toBe(true);
  });

  it("no company name is ever rendered", () => {
    render(<CompaniesCard watches={ok([watch()])} newAdCount={3} span={4} />);
    expect(card().textContent).not.toContain("Friday");
  });

  it("the 'N nya' pill links to the new ads and its accessible name opens with the visible text", () => {
    render(<CompaniesCard watches={ok([watch()])} newAdCount={61} span={4} />);
    const pill = within(card()).getByRole("link", { name: /^61 nya annonser från bevakade företag/ });
    expect(pill).toHaveAttribute("href", "/foretag/bevakade/nya");
    expect(text(pill)).toBe("61 nya");
  });

  it("zero new ads: no pill at all", () => {
    render(<CompaniesCard watches={ok([watch()])} newAdCount={0} span={4} />);
    expect(card().querySelector(".jp-ov-card__pill")).toBeNull();
  });

  it("matching not assessed: the ACTIVE sum is the number with its own unit, and there is no CTA — never a false zero", () => {
    render(<CompaniesCard watches={ok([watch({ matchingAdCount: null })])} newAdCount={0} span={4} />);
    expect(text(card().querySelector(".jp-ov-num__value"))).toBe("131");
    expect(text(card().querySelector(".jp-ov-num__unit"))).toBe("aktiva annonser");
    expect(within(card()).queryByRole("link", { name: COPY.cards.companiesCtaAria })).toBeNull();
  });

  it("a counted matching zero renders 0 and carries no CTA — a zero is a negation, not a destination", () => {
    render(<CompaniesCard watches={ok([watch({ matchingAdCount: 0 })])} newAdCount={0} span={4} />);
    expect(text(card().querySelector(".jp-ov-num__value"))).toBe("0");
    expect(within(card()).queryByRole("link", { name: COPY.cards.companiesCtaAria })).toBeNull();
  });

  it("one unlinkable watch removes every ad link and drops the CTA", () => {
    render(
      <CompaniesCard
        watches={ok([
          watch({ id: "a" }),
          watch({ id: "b", organizationNumber: null, isProtectedIdentity: true, activeAdCount: 2, matchingAdCount: 1 }),
        ])}
        newAdCount={0}
        span={4}
      />,
    );
    expect(text(card().querySelector(".jp-ov-num__value"))).toBe("10");
    expect(within(card()).queryByRole("link", { name: /aktiva annonser/ })).toBeNull();
    expect(within(card()).queryByRole("link", { name: COPY.cards.companiesCtaAria })).toBeNull();
    expect(within(card()).getByText(COPY.companySummary.notLinkable)).toBeInTheDocument();
  });

  it("a per-watch filter is disclosed beneath the numbers", () => {
    render(
      <CompaniesCard
        watches={ok([
          watch({ filter: { municipalities: [], regions: [], onlyMatched: true, remote: false } }),
        ])}
        newAdCount={0}
        span={4}
      />,
    );
    expect(within(card()).getByText(/1 bevakning har ett notisfilter/)).toBeInTheDocument();
  });

  it("empty: the empty copy and an emphasised link to the company search", () => {
    render(<CompaniesCard watches={ok([])} newAdCount={0} span={4} />);
    expect(card().querySelector(".jp-ov-num")).toBeNull();
    expect(within(card()).getByText(COPY.companySummary.emptyTitle)).toBeInTheDocument();
    expect(within(card()).getByRole("link", { name: COPY.companySummary.emptyCta })).toHaveAttribute(
      "href",
      "/foretag/sok",
    );
  });

  it("unavailable: an en-dash, the unavailable copy, no links — 'could not be read' is not 'you follow nobody'", () => {
    render(<CompaniesCard watches={{ kind: "error" }} newAdCount={5} span={4} />);
    expect(text(card().querySelector(".jp-ov-num__value"))).toBe(COPY.cards.unmeasured);
    expect(within(card()).getByText(COPY.companySummary.unavailable)).toBeInTheDocument();
    expect(within(card()).queryByText(COPY.companySummary.emptyTitle)).toBeNull();
    expect(within(card()).queryByRole("link")).toBeNull();
  });
});
