import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RecentSearchList } from "./recent-search-list";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";

const deleteActionMock = vi.fn();
const countsMock = vi.fn<() => ReadonlyMap<string, { currentCount: number; newCount: number }> | null>(
  () => null,
);

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

vi.mock("@/lib/actions/recent-searches", () => ({
  deleteRecentSearchAction: (...args: unknown[]) => deleteActionMock(...args),
}));

// Mocka lat-count-hooken (annars läcker ett riktigt fetch ur unit-testet).
vi.mock("@/lib/hooks/use-recent-search-counts", () => ({
  useRecentSearchCounts: () => countsMock(),
}));

function makeDto(id: string, label: string, newCount = 0): RecentJobSearchDto {
  return {
    id,
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
    label,
    currentCount: 10,
    newCount,
    lastViewedAt: "2026-05-20T19:00:00Z",
  };
}

beforeEach(() => {
  deleteActionMock.mockReset();
  countsMock.mockReset();
  countsMock.mockReturnValue(null);
});

describe("RecentSearchList", () => {
  it("renders the empty-state when items is empty", () => {
    render(<RecentSearchList items={[]} />);
    expect(screen.getByText("Inga senaste sökningar")).toBeInTheDocument();
    expect(screen.getByText(/sparas här automatiskt/)).toBeInTheDocument();
  });

  it("renders one row per item with civic-utility list semantics", () => {
    render(
      <RecentSearchList
        items={[
          makeDto("a1", "backend Stockholm"),
          makeDto("a2", "designer Göteborg"),
        ]}
      />,
    );
    expect(
      screen.getByRole("list", { name: "Senaste sökningar" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: /backend Stockholm/ }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: /designer Göteborg/ }),
    ).toBeInTheDocument();
  });

  it("feeds the lazy count into the matching row when the hook map has resolved", () => {
    countsMock.mockReturnValue(
      new Map([["a1", { currentCount: 1234, newCount: 0 }]]),
    );
    render(
      <RecentSearchList
        items={[
          makeDto("a1", "backend Stockholm"),
          makeDto("a2", "designer Göteborg"),
        ]}
      />,
    );
    // a1 får sin lat-hämtade siffra (sv-SE tusenavgränsare), a2 saknas i map:en
    // → ingen siffra (aldrig falsk "(0)"). Anchored så bara <b>-talet matchas.
    expect(screen.getByText(/^1\s?234$/)).toBeInTheDocument();
    expect(screen.getByText(/träffar/)).toBeInTheDocument();
  });

  it("optimistically removes a row after a successful delete-action", async () => {
    const user = userEvent.setup();
    deleteActionMock.mockResolvedValue({ success: true });
    render(
      <RecentSearchList
        items={[
          makeDto("a1", "backend Stockholm"),
          makeDto("a2", "designer Göteborg"),
        ]}
      />,
    );
    // Ta bort första rad
    const deleteButtons = screen.getAllByRole("button", { name: /Ta bort/ });
    await user.click(deleteButtons[0]!);
    expect(
      screen.queryByRole("heading", { name: /backend Stockholm/ }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: /designer Göteborg/ }),
    ).toBeInTheDocument();
  });

  it("shows civic-utility error-alert when delete fails (row stays visible)", async () => {
    const user = userEvent.setup();
    deleteActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte ta bort sökningen. Försök igen.",
    });
    render(<RecentSearchList items={[makeDto("a1", "backend Stockholm")]} />);
    await user.click(screen.getByRole("button", { name: /Ta bort/ }));
    expect(screen.getByRole("alert")).toHaveTextContent(/Kunde inte ta bort/);
    expect(
      screen.getByRole("heading", { name: /backend Stockholm/ }),
    ).toBeInTheDocument();
  });

  it("collapses to empty-state once all items are deleted optimistically", async () => {
    const user = userEvent.setup();
    deleteActionMock.mockResolvedValue({ success: true });
    render(<RecentSearchList items={[makeDto("a1", "ensam rad")]} />);
    await user.click(screen.getByRole("button", { name: /Ta bort/ }));
    expect(screen.getByText("Inga senaste sökningar")).toBeInTheDocument();
  });
});
