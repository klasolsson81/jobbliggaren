import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NextIntlClientProvider } from "next-intl";
import svMessages from "../../../messages/sv";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { queryLabel } from "@/test/recent-search-label";
import { QuickLinksSection } from "./quick-links-section";

const { juniorAction, searchAction } = vi.hoisted(() => ({
  juniorAction: vi.fn(),
  searchAction: vi.fn(),
}));
vi.mock("@/lib/actions/quick-links", () => ({
  setJuniorQuickLinkAction: juniorAction,
  setRecentSearchQuickLinkAction: searchAction,
}));

function search(id: string, q: string, isQuickLink: boolean): RecentJobSearchDto {
  return {
    id,
    q,
    occupationGroupList: [],
    occupationNameList: [],
    municipalityList: [],
    regionList: [],
    employmentTypeList: [],
    worktimeExtentList: [],
    employerList: [],
    remote: false,
    occupationGroupLabels: [],
    occupationNameLabels: [],
    municipalityLabels: [],
    regionLabels: [],
    sortBy: "PublishedAtDesc",
    label: queryLabel(q),
    currentCount: 0,
    newCount: 0,
    lastViewedAt: "2026-11-21T10:00:00Z",
    isQuickLink,
  };
}

function Section(props: { juniorEnabled: boolean; searches: ReadonlyArray<RecentJobSearchDto> }) {
  return (
    <NextIntlClientProvider locale="sv" messages={svMessages}>
      <QuickLinksSection {...props} />
    </NextIntlClientProvider>
  );
}

const labels = () => screen.getAllByRole("checkbox").map((box) => box.closest("label")?.textContent);

beforeEach(() => {
  juniorAction.mockReset();
  searchAction.mockReset();
});

describe("QuickLinksSection (#2265, amended for #2275)", () => {
  it("is a card titled Snabblänkar with the same tick-box list as the hero's Anpassa", () => {
    render(<Section juniorEnabled searches={[search("a", "frontend", false), search("b", "backend", true)]} />);

    expect(screen.getByRole("region", { name: "Snabblänkar" })).toBeInTheDocument();
    expect(screen.getByText("Snabblänkarna visas överst under Jobb.")).toBeInTheDocument();
    expect(labels()).toEqual(["Juniora IT-jobb", "backend", "frontend"]);
    expect(screen.getAllByRole("checkbox").map((box) => (box as HTMLInputElement).checked)).toEqual([true, true, false]);
  });

  it("offers every recent search, not only the six the hero's list shows", () => {
    const searches = Array.from({ length: 8 }, (_, i) => search(`s${i}`, `sökning ${i}`, false));
    render(<Section juniorEnabled={false} searches={searches} />);

    expect(labels()).toHaveLength(1 + 8);
  });

  it("adds a search with a tick", async () => {
    searchAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    render(<Section juniorEnabled={false} searches={[search("a", "frontend", false)]} />);

    await user.click(screen.getByRole("checkbox", { name: "frontend" }));

    expect(searchAction).toHaveBeenCalledWith("a", true);
  });
});
