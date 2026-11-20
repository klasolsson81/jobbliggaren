import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
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
    lastViewedAt: "2026-11-20T10:00:00Z",
    isQuickLink,
  };
}

const HEADING_ID = "mina-sidor-title";

/** The section as the page renders it: under Mina sidor's focusable h1, which takes focus when a list empties. */
function Section(props: { juniorEnabled: boolean; searches: ReadonlyArray<RecentJobSearchDto> }) {
  return (
    <NextIntlClientProvider locale="sv" messages={svMessages}>
      <h1 id={HEADING_ID} tabIndex={-1}>
        Mina sidor
      </h1>
      <QuickLinksSection {...props} headingId={HEADING_ID} />
    </NextIntlClientProvider>
  );
}

const group = (name: string) => screen.getByRole("heading", { level: 3, name }).closest("section")!;

beforeEach(() => {
  juniorAction.mockReset();
  searchAction.mockReset();
});

describe("QuickLinksSection (#2265, ADR 0188)", () => {
  it("lists the links the hero shows, each with a remove button named after it", () => {
    render(<Section juniorEnabled searches={[search("a", "backend", true), search("b", "frontend", false)]} />);

    const current = within(group("Dina snabblänkar"));
    expect(current.getByText("Juniora IT-jobb")).toBeInTheDocument();
    expect(current.getByText("backend")).toBeInTheDocument();
    expect(current.queryByText("frontend")).toBeNull();
    expect(current.getByRole("button", { name: "Ta bort snabblänken: Juniora IT-jobb" })).toHaveTextContent("Ta bort");
  });

  it("offers the junior link and the unmarked searches while there is room, each named after what it adds", () => {
    render(<Section juniorEnabled={false} searches={[search("a", "backend", true), search("b", "frontend", false)]} />);

    const add = within(group("Lägg till en snabblänk"));
    const buttons = add.getAllByRole("button").map((button) => button.getAttribute("aria-label"));
    expect(buttons).toEqual(["Lägg till som snabblänk: Juniora IT-jobb", "Lägg till som snabblänk: frontend"]);
    for (const button of add.getAllByRole("button")) expect(button.textContent).toBe("Lägg till");
  });

  it("says the cap is reached instead of offering an add", () => {
    render(
      <Section
        juniorEnabled
        searches={[search("a", "backend", true), search("b", "frontend", true), search("c", "devops", false)]}
      />,
    );

    const add = within(group("Lägg till en snabblänk"));
    expect(add.queryByRole("button")).toBeNull();
    expect(add.getByText("Du kan ha högst 3 snabblänkar. Ta bort en för att lägga till en ny.")).toBeInTheDocument();
  });

  it("explains an empty add list both ways", () => {
    const { unmount } = render(<Section juniorEnabled searches={[]} />);
    expect(within(group("Lägg till en snabblänk")).getByText(/Gör en sökning under Jobb/)).toBeInTheDocument();
    unmount();

    render(<Section juniorEnabled={false} searches={[]} />);
    // The junior link alone is addable; no search sentence is shown in its place.
    expect(within(group("Lägg till en snabblänk")).getAllByRole("button")).toHaveLength(1);
  });

  it("removes the junior link and announces it", async () => {
    juniorAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    render(<Section juniorEnabled searches={[]} />);

    await user.click(screen.getByRole("button", { name: "Ta bort snabblänken: Juniora IT-jobb" }));

    expect(juniorAction).toHaveBeenCalledWith(false);
    expect(await screen.findByText("Snabblänken har tagits bort: Juniora IT-jobb")).toBeInTheDocument();
  });

  it("sends focus to the page's h1 when the last link is removed", async () => {
    juniorAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    const { rerender } = render(<Section juniorEnabled searches={[]} />);

    await user.click(screen.getByRole("button", { name: "Ta bort snabblänken: Juniora IT-jobb" }));
    await screen.findByText("Snabblänken har tagits bort: Juniora IT-jobb");
    rerender(<Section juniorEnabled={false} searches={[]} />);

    await waitFor(() => expect(screen.getByRole("heading", { level: 1 })).toHaveFocus());
  });

  it("sends focus to the page's h1 when the last addable link is added", async () => {
    juniorAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    const { rerender } = render(<Section juniorEnabled={false} searches={[]} />);

    await user.click(screen.getByRole("button", { name: "Lägg till som snabblänk: Juniora IT-jobb" }));
    await screen.findByText("Snabblänken har lagts till: Juniora IT-jobb");
    rerender(<Section juniorEnabled searches={[]} />);

    await waitFor(() => expect(screen.getByRole("heading", { level: 1 })).toHaveFocus());
  });

  it("adds a search and announces it", async () => {
    searchAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    render(<Section juniorEnabled searches={[search("b", "frontend", false)]} />);

    await user.click(screen.getByRole("button", { name: "Lägg till som snabblänk: frontend" }));

    expect(searchAction).toHaveBeenCalledWith("b", true);
    expect(await screen.findByText("Snabblänken har lagts till: frontend")).toBeInTheDocument();
  });

  it("says why when a change is refused", async () => {
    searchAction.mockResolvedValue({ success: false, error: "Du har redan tre snabblänkar." });
    const user = userEvent.setup();
    render(<Section juniorEnabled searches={[search("b", "frontend", false)]} />);

    await user.click(screen.getByRole("button", { name: "Lägg till som snabblänk: frontend" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Du har redan tre snabblänkar.");
  });
});
