import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RecentSearchesHeroChip } from "./recent-searches-hero-chip";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { queryLabel } from "@/test/recent-search-label";

const countsMock = vi.fn<() => ReadonlyMap<string, { currentCount: number; newCount: number }> | null>(
  () => null,
);

vi.mock("@/lib/hooks/use-recent-search-counts", () => ({
  useRecentSearchCounts: () => countsMock(),
}));

function makeDto(extra: Partial<RecentJobSearchDto>): RecentJobSearchDto {
  return {
    id: "id-1",
    q: null,
    occupationGroupList: [],
    municipalityList: [],
    regionList: [],
    employmentTypeList: [],
    worktimeExtentList: [],
    employerList: [],
    remote: false,
    occupationGroupLabels: [],
    municipalityLabels: [],
    regionLabels: [],
    sortBy: "PublishedAtDesc",
    label: queryLabel("default"),
    currentCount: 0,
    newCount: 0,
    lastViewedAt: "2026-05-20T19:00:00Z",
    ...extra,
  };
}

beforeEach(() => {
  countsMock.mockReset();
  countsMock.mockReturnValue(null);
});

describe("RecentSearchesHeroChip", () => {
  it("trigger visar count i parentes när items finns", () => {
    render(
      <RecentSearchesHeroChip
        items={[
          makeDto({ id: "a1", label: queryLabel("backend"), currentCount: 42 }),
          makeDto({ id: "a2", label: queryLabel("designer"), currentCount: 8 }),
        ]}
      />,
    );
    expect(
      screen.getByRole("button", { name: /Senaste sökningar/ }),
    ).toBeInTheDocument();
    expect(screen.getByText("(2)")).toBeInTheDocument();
  });

  it("dropdown-rad visar INGEN träffräknare när hooken ännu inte laddat (graceful null — aldrig falsk (0))", async () => {
    const user = userEvent.setup();
    countsMock.mockReturnValue(null);
    render(
      <RecentSearchesHeroChip
        items={[
          makeDto({ id: "a1", label: queryLabel("backend"), currentCount: 42, newCount: 0 }),
          makeDto({ id: "a2", label: queryLabel("designer"), currentCount: 8, newCount: 3 }),
        ]}
      />,
    );
    await user.click(screen.getByRole("button", { name: /Senaste sökningar/ }));
    expect(screen.getByText("backend")).toBeInTheDocument();
    expect(screen.getByText("designer")).toBeInTheDocument();
    // DTO:ns currentCount ignoreras — utan hook-data, ingen siffra (aldrig "(0)").
    expect(screen.queryByText("(42)")).not.toBeInTheDocument();
    expect(screen.queryByText("(8, 3 nya)")).not.toBeInTheDocument();
    expect(screen.queryByText(/nya\)/)).not.toBeInTheDocument();
  });

  it("dropdown-rad visar lat-hämtad '(N)' / '(N, M nya)' från hook-map:en", async () => {
    const user = userEvent.setup();
    countsMock.mockReturnValue(
      new Map([
        ["a1", { currentCount: 42, newCount: 0 }],
        ["a2", { currentCount: 8, newCount: 3 }],
      ]),
    );
    render(
      <RecentSearchesHeroChip
        items={[
          makeDto({ id: "a1", label: queryLabel("backend") }),
          makeDto({ id: "a2", label: queryLabel("designer") }),
        ]}
      />,
    );
    await user.click(screen.getByRole("button", { name: /Senaste sökningar/ }));
    expect(screen.getByText("(42)")).toBeInTheDocument();
    expect(screen.getByText("(8, 3 nya)")).toBeInTheDocument();
  });

  it("INGEN 'NY'-pill renderas i dropdown (Klas-direktiv anti-AI-trope)", async () => {
    const user = userEvent.setup();
    const { container } = render(
      <RecentSearchesHeroChip
        items={[makeDto({ id: "a1", label: queryLabel("backend"), newCount: 5 })]}
      />,
    );
    await user.click(screen.getByRole("button", { name: /Senaste sökningar/ }));
    expect(container.querySelector(".jp-pill--success")).toBeNull();
    expect(container.querySelector(".jp-job__newflag")).toBeNull();
    expect(screen.queryByText(/^NY$/)).not.toBeInTheDocument();
  });

  it("radens href är /jobb-URL:en byggd från filter, och klick stänger dropdownen", async () => {
    const user = userEvent.setup();
    render(
      <RecentSearchesHeroChip
        items={[
          makeDto({
            id: "a1",
            label: queryLabel("backend"),
            q: "backend",
            occupationGroupList: ["MVqp_eS8_kDZ"],
          }),
        ]}
      />,
    );
    await user.click(screen.getByRole("button", { name: /Senaste sökningar/ }));
    // By role, not by class: what this consumer owns is the URL, and a URL the
    // user cannot ctrl-click is the defect the row stopped being a button for.
    const row = screen.getByRole("link", { name: /backend/ });
    const url = row.getAttribute("href") ?? "";
    expect(url).toMatch(/^\/jobb\?/);
    expect(url).toContain("q=backend");
    expect(url).toContain("occupationGroup=MVqp_eS8_kDZ");
    await user.click(row);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("tom-tillstånd visar civic-utility hint", async () => {
    const user = userEvent.setup();
    render(<RecentSearchesHeroChip items={[]} />);
    await user.click(screen.getByRole("button", { name: /Senaste sökningar/ }));
    expect(
      screen.getByText(/Inga senaste sökningar än/),
    ).toBeInTheDocument();
  });
});
