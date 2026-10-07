import { describe, it, expect, vi, beforeEach } from "vitest";
import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NextIntlClientProvider } from "next-intl";
import enMessages from "../../../messages/en";
import { RecentSearchList } from "./recent-search-list";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { queryLabel } from "@/test/recent-search-label";
import type { RecentSearchCounts } from "@/lib/hooks/use-recent-search-counts";

const deleteActionMock = vi.fn();
const countsMock = vi.fn<() => RecentSearchCounts>(() => null);

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
    employerList: [],
    remote: false,
    occupationGroupLabels: [],
    municipalityLabels: [],
    regionLabels: [],
    sortBy: "PublishedAtDesc",
    label: queryLabel(label),
    currentCount: 10,
    newCount,
    lastViewedAt: "2026-05-20T19:00:00Z",
  };
}

const HEADING_ID = "sokningar-heading";

/** The list as the page renders it: inside `main`, under the page's focusable h1. */
function Page({ items }: { items: ReadonlyArray<RecentJobSearchDto> }) {
  return (
    <main id="main" tabIndex={-1}>
      <h1 id={HEADING_ID} tabIndex={-1}>
        Senaste sökningar
      </h1>
      <RecentSearchList items={items} headingId={HEADING_ID} />
    </main>
  );
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => {
    resolve = r;
  });
  return { promise, resolve };
}

const region = () => document.querySelector('p[role="status"]');

async function removeByKeyboard(button: HTMLElement) {
  button.focus();
  await userEvent.setup().keyboard("{Enter}");
}

beforeEach(() => {
  deleteActionMock.mockReset();
  countsMock.mockReset();
  countsMock.mockReturnValue(null);
});

describe("RecentSearchList", () => {
  it("renders the empty-state when items is empty", () => {
    render(<RecentSearchList headingId={HEADING_ID} items={[]} />);
    expect(screen.getByText("Inga senaste sökningar")).toBeInTheDocument();
    expect(screen.getByText(/sparas här automatiskt/)).toBeInTheDocument();
  });

  it("renders one row per item with civic-utility list semantics", () => {
    render(
      <RecentSearchList
        headingId={HEADING_ID}
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
        headingId={HEADING_ID}
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

  it("reserves every row's count line while the counts are pending", () => {
    countsMock.mockReturnValue(undefined);
    const { container } = render(
      <RecentSearchList
        headingId={HEADING_ID}
        items={[
          makeDto("a1", "backend Stockholm"),
          makeDto("a2", "designer Göteborg"),
        ]}
      />,
    );
    expect(container.querySelectorAll(".jp-job__meta--search-count[aria-hidden='true']")).toHaveLength(2);
    expect(screen.queryByText(/träffar/)).not.toBeInTheDocument();
  });

  it("optimistically removes a row after a successful delete-action", async () => {
    const user = userEvent.setup();
    deleteActionMock.mockResolvedValue({ success: true });
    render(
      <RecentSearchList
        headingId={HEADING_ID}
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
    render(<RecentSearchList headingId={HEADING_ID} items={[makeDto("a1", "backend Stockholm")]} />);
    await user.click(screen.getByRole("button", { name: /Ta bort/ }));
    expect(screen.getByRole("alert")).toHaveTextContent(/Kunde inte ta bort/);
    expect(
      screen.getByRole("heading", { name: /backend Stockholm/ }),
    ).toBeInTheDocument();
  });

  it("collapses to empty-state once all items are deleted optimistically", async () => {
    const user = userEvent.setup();
    deleteActionMock.mockResolvedValue({ success: true });
    render(<RecentSearchList headingId={HEADING_ID} items={[makeDto("a1", "ensam rad")]} />);
    await user.click(screen.getByRole("button", { name: /Ta bort/ }));
    expect(screen.getByText("Inga senaste sökningar")).toBeInTheDocument();
  });
});

// #2029 (WCAG 2.1 SC 2.4.3, 4.1.3): the remove button leaves with its row, so focus has to be put
// somewhere on purpose, and the removal has to be said.
describe("focus and status after a removal (#2029)", () => {
  it("a removed row hands focus to the next row's Kör igen, and the receipt reads the row's label", async () => {
    deleteActionMock.mockResolvedValue({ success: true });
    render(
      <Page
        items={[
          makeDto("a1", "backend Stockholm"),
          makeDto("a2", "designer Göteborg"),
          makeDto("a3", "testare Malmö"),
        ]}
      />,
    );

    await removeByKeyboard(
      screen.getByRole("button", { name: "Ta bort sökningen: backend Stockholm" }),
    );

    const [nextRunAgain] = screen.getAllByRole("link", { name: "Kör igen" });
    expect(nextRunAgain?.closest("article")).toHaveTextContent("designer Göteborg");
    expect(nextRunAgain).toHaveFocus();
    expect(region()).toHaveTextContent("Sökningen har tagits bort: backend Stockholm");
  });

  it("the last removal hands focus to the page's h1 through the region that was there before it", async () => {
    deleteActionMock.mockResolvedValue({ success: true });
    render(<Page items={[makeDto("a1", "ensam rad")]} />);
    const before = region();
    expect(before).toHaveTextContent("");

    await removeByKeyboard(screen.getByRole("button", { name: "Ta bort sökningen: ensam rad" }));

    expect(screen.getByText("Inga senaste sökningar")).toBeInTheDocument();
    expect(region()).toBe(before);
    expect(before).toHaveTextContent("Sökningen har tagits bort: ensam rad");
    expect(screen.getByRole("heading", { level: 1 })).toHaveFocus();
  });

  it("keeps the remove button enabled while the delete runs, and a second press sends no second delete", async () => {
    const pending = deferred<{ success: true }>();
    deleteActionMock.mockReturnValue(pending.promise);
    render(<Page items={[makeDto("a1", "backend Stockholm"), makeDto("a2", "designer Göteborg")]} />);
    const user = userEvent.setup();
    const button = () => screen.getByRole("button", { name: "Ta bort sökningen: backend Stockholm" });

    await user.click(button());
    expect(button()).not.toBeDisabled();
    expect(button()).toHaveAttribute("aria-disabled", "true");
    await user.click(button());
    expect(deleteActionMock).toHaveBeenCalledTimes(1);

    await act(async () => pending.resolve({ success: true }));
  });

  it("says the removal in English on the English page", async () => {
    deleteActionMock.mockResolvedValue({ success: true });
    render(
      <NextIntlClientProvider locale="en" messages={enMessages} timeZone="Europe/Stockholm">
        <Page items={[makeDto("a1", "backend Stockholm")]} />
      </NextIntlClientProvider>,
    );

    await removeByKeyboard(
      screen.getByRole("button", { name: "Remove the search: backend Stockholm" }),
    );

    expect(region()).toHaveTextContent("The search has been removed: backend Stockholm");
  });
});
