import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NextIntlClientProvider } from "next-intl";
import svMessages from "../../../messages/sv";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { queryLabel } from "@/test/recent-search-label";
import { JobbQuickLinks } from "./jobb-quick-links";

const { juniorAction, searchAction } = vi.hoisted(() => ({
  juniorAction: vi.fn(),
  searchAction: vi.fn(),
}));
vi.mock("@/lib/actions/quick-links", () => ({
  setJuniorQuickLinkAction: juniorAction,
  setRecentSearchQuickLinkAction: searchAction,
}));

const HEADING_ID = "jobb-hero-title";

function search(id: string, q: string, isQuickLink = true): RecentJobSearchDto {
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

const BACKEND = search("11111111-1111-1111-1111-111111111111", "backend");

/**
 * The hero as the page renders it. `juniorEnabled` defaults to a state the page produces: on beside a count, and
 * null (a failed profile read, so no "Anpassa") without one.
 */
function Hero(props: {
  juniorCount: number | null;
  searches: ReadonlyArray<RecentJobSearchDto> | null;
  juniorEnabled?: boolean | null;
}) {
  const juniorEnabled = props.juniorEnabled === undefined ? (props.juniorCount === null ? null : true) : props.juniorEnabled;
  return (
    <NextIntlClientProvider locale="sv" messages={svMessages}>
      <h1 id={HEADING_ID} tabIndex={-1}>
        Sök jobb
      </h1>
      <JobbQuickLinks {...props} juniorEnabled={juniorEnabled} headingId={HEADING_ID} />
    </NextIntlClientProvider>
  );
}

const unmarked = (n: number) =>
  Array.from({ length: n }, (_, i) => search(`2222222${i}-2222-2222-2222-222222222222`, `sökning ${i}`, false));

beforeEach(() => {
  juniorAction.mockReset();
  searchAction.mockReset();
});

describe("JobbQuickLinks (#2265, ADR 0188)", () => {
  it("shows the junior link first, with its count, as a link to /jobb with the junior filter", () => {
    render(<Hero juniorCount={1234} searches={[BACKEND]} />);

    const nav = screen.getByRole("navigation", { name: "Snabblänkar" });
    const links = Array.from(nav.querySelectorAll("a"));
    expect(links.map((link) => link.getAttribute("href"))).toEqual(["/jobb?junior=on", expect.stringMatching(/^\/jobb\?/)]);
    expect(links[0]?.textContent?.replace(/\u00a0/g, " ")).toBe("Juniora IT-jobb · 1 234 just nu");
    expect(screen.getByRole("link", { name: /Juniora IT-jobb/ })).toBe(links[0]);
    expect(screen.getByRole("link", { name: "backend" })).toBe(links[1]);
  });

  it("names the junior link with a comma where the separator stands, and hides the separator", () => {
    render(<Hero juniorCount={152} searches={[]} />);

    const link = screen.getByRole("link", { name: "Juniora IT-jobb, 152 just nu" });
    expect(link.querySelector('[aria-hidden="true"]')?.textContent).toBe(" · ");
  });

  it("names each × after its link", () => {
    render(<Hero juniorCount={152} searches={[BACKEND]} />);

    expect(screen.getByRole("button", { name: "Ta bort snabblänken: Juniora IT-jobb" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ta bort snabblänken: backend" })).toBeInTheDocument();
  });

  it("renders no navigation when there is no link and no Anpassa, as after a failed profile read", () => {
    render(<Hero juniorCount={null} searches={[]} />);

    expect(screen.queryByRole("navigation")).toBeNull();
  });

  it("removes the junior link at once, announces it and moves focus to the next link", async () => {
    juniorAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    render(<Hero juniorCount={152} searches={[BACKEND]} />);

    await user.click(screen.getByRole("button", { name: "Ta bort snabblänken: Juniora IT-jobb" }));

    expect(juniorAction).toHaveBeenCalledWith(false);
    await waitFor(() => expect(screen.queryByRole("link", { name: /Juniora IT-jobb/ })).toBeNull());
    expect(screen.getByRole("status")).toHaveTextContent("Snabblänken har tagits bort: Juniora IT-jobb");
    expect(screen.getByRole("link", { name: "backend" })).toHaveFocus();
  });

  it("sends focus to the hero's h1 when the last link goes and there is no Anpassa", async () => {
    searchAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    render(<Hero juniorCount={null} searches={[BACKEND]} />);

    await user.click(screen.getByRole("button", { name: "Ta bort snabblänken: backend" }));

    expect(searchAction).toHaveBeenCalledWith(BACKEND.id, false);
    await waitFor(() => expect(screen.queryByRole("navigation")).toBeNull());
    expect(screen.getByRole("heading", { level: 1 })).toHaveFocus();
    expect(screen.getByRole("status")).toHaveTextContent("Snabblänken har tagits bort: backend");
  });

  it("keeps the status when the page re-renders without the last link", async () => {
    searchAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    const { rerender } = render(<Hero juniorCount={null} searches={[BACKEND]} />);

    await user.click(screen.getByRole("button", { name: "Ta bort snabblänken: backend" }));
    await waitFor(() => expect(screen.queryByRole("navigation")).toBeNull());
    rerender(<Hero juniorCount={null} searches={[]} />);

    expect(screen.getByRole("status")).toHaveTextContent("Snabblänken har tagits bort: backend");
  });

  it("shows a removed link again once a server render lists it, as after adding it back elsewhere", async () => {
    searchAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    const { rerender } = render(<Hero juniorCount={null} searches={[BACKEND]} />);

    await user.click(screen.getByRole("button", { name: "Ta bort snabblänken: backend" }));
    await waitFor(() => expect(screen.queryByRole("link", { name: "backend" })).toBeNull());
    rerender(<Hero juniorCount={null} searches={[{ ...BACKEND }]} />);

    expect(screen.getByRole("link", { name: "backend" })).toBeInTheDocument();
  });

  it("sends focus to Anpassa when the last link goes, the control that adds them", async () => {
    searchAction.mockResolvedValue({ success: true });
    const user = userEvent.setup();
    render(<Hero juniorCount={null} juniorEnabled={false} searches={[BACKEND]} />);

    await user.click(screen.getByRole("button", { name: "Ta bort snabblänken: backend" }));

    await waitFor(() => expect(screen.getByRole("button", { name: "Välj snabblänkar" })).toHaveFocus());
  });

  it("ends the row with Anpassa, which opens the list to pick links from", async () => {
    const user = userEvent.setup();
    render(<Hero juniorCount={152} juniorEnabled searches={[BACKEND, search("22222222-2222-2222-2222-222222222222", "frontend", false)]} />);

    const settings = screen.getByRole("button", { name: "Anpassa snabblänkar" });
    expect(settings).toHaveTextContent("Anpassa");
    await user.click(settings);

    const dialog = screen.getByRole("dialog", { name: "Snabblänkar" });
    expect(within(dialog).getAllByRole("checkbox").map((b) => (b as HTMLInputElement).checked)).toEqual([true, true, false]);
    expect(within(dialog).queryByRole("link", { name: "Alla sökningar" })).toBeNull();
  });

  it("offers Alla sökningar only when the list leaves searches out", async () => {
    const user = userEvent.setup();
    render(<Hero juniorCount={152} searches={[BACKEND, ...unmarked(7)]} />);

    await user.click(screen.getByRole("button", { name: "Anpassa snabblänkar" }));

    const dialog = screen.getByRole("dialog", { name: "Snabblänkar" });
    expect(within(dialog).getAllByRole("checkbox")).toHaveLength(1 + 1 + 6);
    expect(within(dialog).getByRole("link", { name: "Alla sökningar" })).toHaveAttribute("href", "/mina-sidor/snabblankar");
  });

  it("says in the list how to get a search to pick, without sending the user to Jobb", async () => {
    const user = userEvent.setup();
    render(<Hero juniorCount={null} juniorEnabled={false} searches={[]} />);

    await user.click(screen.getByRole("button", { name: "Välj snabblänkar" }));

    expect(screen.getByText("Gör en sökning, så kan du lägga till den här.")).toBeInTheDocument();
  });

  it("offers no Anpassa when the recent searches could not be read, and still shows the junior link", () => {
    render(<Hero juniorCount={152} juniorEnabled searches={null} />);

    expect(screen.getByRole("link", { name: "Juniora IT-jobb, 152 just nu" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /snabblänkar$/ })).toBeNull();
  });

  it("offers Välj snabblänkar on its own when there is no link", () => {
    render(<Hero juniorCount={null} juniorEnabled={false} searches={[]} />);

    expect(screen.getByRole("navigation", { name: "Snabblänkar" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Välj snabblänkar" })).toBeInTheDocument();
    expect(screen.queryByRole("link")).toBeNull();
  });

  it("keeps the link and says why when the removal fails", async () => {
    searchAction.mockResolvedValue({ success: false, error: "Snabblänken kunde inte ändras. Försök igen." });
    const user = userEvent.setup();
    render(<Hero juniorCount={null} searches={[BACKEND]} />);

    await user.click(screen.getByRole("button", { name: "Ta bort snabblänken: backend" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Snabblänken kunde inte ändras. Försök igen.");
    expect(screen.getByRole("link", { name: "backend" })).toBeInTheDocument();
  });
});
