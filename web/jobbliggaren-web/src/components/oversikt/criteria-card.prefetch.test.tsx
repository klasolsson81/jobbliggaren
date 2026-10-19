import { describe, it, expect, vi } from "vitest";
import type { ReactNode } from "react";
import { render, screen } from "@testing-library/react";
import { CriteriaCard } from "./criteria-card";
import type { ApiResult } from "@/lib/dto/_helpers";
import type {
  CompanyWatchCriterion,
  CriterionNewAdCounts,
  ListCompanyWatchCriteriaResult,
} from "@/lib/dto/company-criteria";

// jsdom never prefetches, so the prop itself is what is pinned: this stub renders an <a> and writes
// the prefetch value it was given onto it. A prefetch of /nya would render the page, and the page
// acknowledges the window it shows.
vi.mock("next/link", () => ({
  default: ({
    href,
    prefetch,
    children,
    ...rest
  }: {
    href: string;
    prefetch?: boolean | null;
    children: ReactNode;
  }) => (
    <a href={href} data-prefetch={prefetch === undefined ? "default" : String(prefetch)} {...rest}>
      {children}
    </a>
  ),
}));

function criterion(id: string, label: string): CompanyWatchCriterion {
  return {
    id,
    sniCodes: ["62010"],
    municipalityCodes: ["1480"],
    label,
    createdAt: "2026-08-01T08:00:00+00:00",
    updatedAt: "2026-08-01T08:00:00+00:00",
    ads: { magnitude: 42, saturated: false, tooBroad: false, notMaterialised: false },
    matching: { count: 7, tooBroad: false, notMaterialised: false },
  };
}
const ok = (items: CompanyWatchCriterion[]): ApiResult<ListCompanyWatchCriteriaResult> => ({
  kind: "ok",
  data: items,
});
const counts = (...ids: string[]): ApiResult<CriterionNewAdCounts> => ({
  kind: "ok",
  data: { items: ids.map((criterionId) => ({ criterionId, newAds: 3 })) },
});

describe("CriteriaCard 'N nya' pill prefetch (ADR 0175 D7)", () => {
  it("one watch: the head pill passes prefetch={false}", () => {
    render(<CriteriaCard criteria={ok([criterion("a", "Första")])} reference={null} newAdCounts={counts("a")} />);
    expect(screen.getByRole("link", { name: /^3 nya annonser/ })).toHaveAttribute("data-prefetch", "false");
  });

  it("two or more: every row pill passes prefetch={false}", () => {
    render(
      <CriteriaCard
        criteria={ok([criterion("a", "Första"), criterion("b", "Andra")])}
        reference={null}
        newAdCounts={counts("a", "b")}
      />,
    );
    const pills = screen.getAllByRole("link", { name: /^3 nya annonser/ });
    expect(pills).toHaveLength(2);
    for (const pill of pills) expect(pill).toHaveAttribute("data-prefetch", "false");
  });
});
