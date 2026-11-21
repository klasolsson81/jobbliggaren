import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NextIntlClientProvider } from "next-intl";
import svMessages from "../../../messages/sv";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { queryLabel } from "@/test/recent-search-label";
import { QuickLinkPicker } from "./quick-link-picker";

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

function Picker(props: { juniorEnabled: boolean; searches: ReadonlyArray<RecentJobSearchDto>; unmarkedLimit?: number }) {
  return (
    <NextIntlClientProvider locale="sv" messages={svMessages}>
      <QuickLinkPicker {...props} />
    </NextIntlClientProvider>
  );
}

const box = (name: string) => screen.getByRole("checkbox", { name });

beforeEach(() => {
  juniorAction.mockReset();
  searchAction.mockReset();
});

describe("QuickLinkPicker (#2275)", () => {
  it("lists the junior link first, then the searches that are quick links, then the others, ticked as they are", () => {
    render(
      <Picker
        juniorEnabled
        searches={[search("a", "frontend", false), search("b", "backend", true), search("c", "devops", false)]}
      />,
    );

    const boxes = screen.getAllByRole("checkbox");
    expect(boxes.map((b) => b.closest("label")?.textContent)).toEqual(["Juniora IT-jobb", "backend", "frontend", "devops"]);
    expect(boxes.map((b) => (b as HTMLInputElement).checked)).toEqual([true, true, false, false]);
  });

  it("offers only as many of the other searches as asked, and always the marked ones", () => {
    render(
      <Picker
        juniorEnabled={false}
        searches={[search("a", "one", false), search("b", "two", false), search("c", "three", true)]}
        unmarkedLimit={1}
      />,
    );

    expect(screen.getAllByRole("checkbox").map((b) => b.closest("label")?.textContent)).toEqual([
      "Juniora IT-jobb",
      "three",
      "one",
    ]);
  });

  it("adds a search with a tick, showing it ticked while the write runs", async () => {
    let answer: (value: { success: true }) => void = () => {};
    searchAction.mockReturnValue(new Promise((resolve) => (answer = resolve)));
    const user = userEvent.setup();
    render(<Picker juniorEnabled={false} searches={[search("a", "frontend", false)]} />);

    await user.click(box("frontend"));

    expect(searchAction).toHaveBeenCalledWith("a", true);
    expect(box("frontend")).toBeChecked();
    answer({ success: true });
  });

  it("removes the junior link with an untick", async () => {
    juniorAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    render(<Picker juniorEnabled searches={[]} />);

    await user.click(box("Juniora IT-jobb"));

    expect(juniorAction).toHaveBeenCalledWith(false);
  });

  it("makes the other boxes unavailable at the cap and says why", () => {
    render(
      <Picker
        juniorEnabled
        searches={[search("a", "one", true), search("b", "two", true), search("c", "three", false)]}
      />,
    );

    expect(box("three")).toBeDisabled();
    expect(box("one")).toBeEnabled();
    expect(screen.getByText("Du kan ha högst 3 snabblänkar. Kryssa ur en för att välja en annan.")).toBeInTheDocument();
  });

  it("unticks a refused add again, says why and keeps focus on the box", async () => {
    searchAction.mockResolvedValue({ success: false, error: "Du har redan 3 snabblänkar. Ta bort en för att lägga till en ny." });
    const user = userEvent.setup();
    render(<Picker juniorEnabled={false} searches={[search("a", "frontend", false)]} />);

    await user.click(box("frontend"));

    expect(await screen.findByRole("alert")).toHaveTextContent("Du har redan 3 snabblänkar.");
    await waitFor(() => expect(box("frontend")).not.toBeChecked());
    expect(box("frontend")).toHaveFocus();
  });

  it("keeps every row where it was when a tick re-renders the page, so focus stays on the box", async () => {
    searchAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    const before = [search("b", "frontend", false), search("a", "backend", true), search("c", "devops", false)];
    const { rerender } = render(<Picker juniorEnabled={false} searches={before} unmarkedLimit={1} />);

    await user.click(box("backend"));
    // The server's answer: backend is no longer a quick link, and the first unmarked search now comes before it.
    rerender(
      <Picker
        juniorEnabled={false}
        searches={[search("b", "frontend", false), search("a", "backend", false), search("c", "devops", false)]}
        unmarkedLimit={1}
      />,
    );

    expect(screen.getAllByRole("checkbox").map((b) => b.closest("label")?.textContent)).toEqual([
      "Juniora IT-jobb",
      "backend",
      "frontend",
    ]);
    expect(box("backend")).not.toBeChecked();
    expect(box("backend")).toHaveFocus();
  });

  it("counts a link marked in another tab towards the cap once the page knows of it", () => {
    const { rerender } = render(
      <Picker juniorEnabled searches={[search("a", "one", true), search("b", "two", false)]} />,
    );
    expect(box("two")).toBeEnabled();

    rerender(
      <Picker
        juniorEnabled
        searches={[search("x", "elsewhere", true), search("a", "one", true), search("b", "two", false)]}
      />,
    );

    expect(box("two")).toBeDisabled();
  });

  it("says how to get searches to pick when there are none", () => {
    render(<Picker juniorEnabled searches={[]} />);

    expect(screen.getByText("Gör en sökning under Jobb, så kan du lägga till den här.")).toBeInTheDocument();
  });
});
