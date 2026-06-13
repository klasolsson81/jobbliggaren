import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RecentSearchesHeroChip } from "./recent-searches-hero-chip";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";

const pushMock = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
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
    occupationGroupLabels: [],
    municipalityLabels: [],
    regionLabels: [],
    sortBy: "PublishedAtDesc",
    label: "default",
    currentCount: 0,
    newCount: 0,
    lastViewedAt: "2026-05-20T19:00:00Z",
    ...extra,
  };
}

beforeEach(() => {
  pushMock.mockClear();
});

describe("RecentSearchesHeroChip", () => {
  it("trigger visar count i parentes när items finns", () => {
    render(
      <RecentSearchesHeroChip
        items={[
          makeDto({ id: "a1", label: "backend", currentCount: 42 }),
          makeDto({ id: "a2", label: "designer", currentCount: 8 }),
        ]}
      />,
    );
    expect(
      screen.getByRole("button", { name: /Senaste sökningar/ }),
    ).toBeInTheDocument();
    expect(screen.getByText("(2)")).toBeInTheDocument();
  });

  it("dropdown-rad visar '(N)' när newCount === 0 och '(N, M nya)' när newCount > 0", async () => {
    const user = userEvent.setup();
    render(
      <RecentSearchesHeroChip
        items={[
          makeDto({ id: "a1", label: "backend", currentCount: 42, newCount: 0 }),
          makeDto({ id: "a2", label: "designer", currentCount: 8, newCount: 3 }),
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
        items={[makeDto({ id: "a1", label: "backend", newCount: 5 })]}
      />,
    );
    await user.click(screen.getByRole("button", { name: /Senaste sökningar/ }));
    expect(container.querySelector(".jp-pill--success")).toBeNull();
    expect(container.querySelector(".jp-job__newflag")).toBeNull();
    expect(screen.queryByText(/^NY$/)).not.toBeInTheDocument();
  });

  it("klick på rad → router.push med /jobb-URL byggd från filter, dropdown stänger", async () => {
    const user = userEvent.setup();
    render(
      <RecentSearchesHeroChip
        items={[
          makeDto({
            id: "a1",
            label: "backend",
            q: "backend",
            occupationGroupList: ["MVqp_eS8_kDZ"],
          }),
        ]}
      />,
    );
    await user.click(screen.getByRole("button", { name: /Senaste sökningar/ }));
    await user.click(screen.getByText("backend"));
    expect(pushMock).toHaveBeenCalledTimes(1);
    const url = pushMock.mock.calls[0]?.[0] as string;
    expect(url).toMatch(/^\/jobb\?/);
    expect(url).toContain("q=backend");
    expect(url).toContain("occupationGroup=MVqp_eS8_kDZ");
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
