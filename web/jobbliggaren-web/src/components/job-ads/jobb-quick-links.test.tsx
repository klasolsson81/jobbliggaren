import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
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

function search(id: string, q: string): RecentJobSearchDto {
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
    isQuickLink: true,
  };
}

const BACKEND = search("11111111-1111-1111-1111-111111111111", "backend");

function Hero(props: { juniorCount: number | null; searches: ReadonlyArray<RecentJobSearchDto> }) {
  return (
    <NextIntlClientProvider locale="sv" messages={svMessages}>
      <h1 id={HEADING_ID} tabIndex={-1}>
        Sök jobb
      </h1>
      <JobbQuickLinks {...props} headingId={HEADING_ID} />
    </NextIntlClientProvider>
  );
}

beforeEach(() => {
  juniorAction.mockReset();
  searchAction.mockReset();
});

describe("JobbQuickLinks (#2265, ADR 0188)", () => {
  it("shows the junior link first, with its count, as a plain anchor to /junior", () => {
    render(<Hero juniorCount={1234} searches={[BACKEND]} />);

    const nav = screen.getByRole("navigation", { name: "Snabblänkar" });
    const links = Array.from(nav.querySelectorAll("a"));
    expect(links.map((link) => link.getAttribute("href"))).toEqual(["/junior", expect.stringMatching(/^\/jobb\?/)]);
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

  it("renders no navigation when there is no link to show", () => {
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

  it("sends focus to the hero's h1 when the last link goes", async () => {
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

  it("keeps the link and says why when the removal fails", async () => {
    searchAction.mockResolvedValue({ success: false, error: "Snabblänken kunde inte ändras. Försök igen." });
    const user = userEvent.setup();
    render(<Hero juniorCount={null} searches={[BACKEND]} />);

    await user.click(screen.getByRole("button", { name: "Ta bort snabblänken: backend" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Snabblänken kunde inte ändras. Försök igen.");
    expect(screen.getByRole("link", { name: "backend" })).toBeInTheDocument();
  });
});
