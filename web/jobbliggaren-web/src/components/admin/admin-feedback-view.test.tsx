import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { DEFAULT_FEEDBACK_QUERY, type AdminFeedbackQuery } from "@/lib/admin/feedback";
import type { AdminFeedbackItem, AdminFeedbackListPage, AdminFeedbackPageSummary } from "@/lib/admin/view-models";
import { AdminFeedbackView, type AdminFeedbackViewProps } from "./admin-feedback-view";

const BASE = "/admin/feedback";
const FIRST = "00000000-0000-4000-8000-000000000501";
const SECOND = "00000000-0000-4000-8000-000000000502";

const LIST: AdminFeedbackListPage = {
  items: [
    {
      id: FIRST,
      page: "applications",
      rating: 2,
      excerpt: "När jag sparar en ansökan visas den gamla statusen.",
      status: "new",
      submittedAt: "2026-10-04T05:12:00Z",
      notice: "failed",
    },
    {
      id: SECOND,
      page: "job-ad",
      rating: null,
      excerpt: null,
      status: "declined",
      submittedAt: "2026-10-02T09:03:00Z",
      notice: null,
    },
  ],
  page: 1,
  totalPages: 1,
  totalCount: 2,
  counts: { all: 7, new: 3, inProgress: 0, resolved: 2, declined: 2 },
};

const ITEM: AdminFeedbackItem = {
  id: FIRST,
  page: "applications",
  rating: 2,
  comment: "När jag sparar en ansökan visas den gamla statusen.",
  status: "new",
  submittedAt: "2026-10-04T05:12:00Z",
  statusChangedAt: null,
  reporterEmail: "konto.b@example.test",
  client: {
    viewportWidth: 390,
    viewportHeight: 664,
    screenWidth: 390,
    screenHeight: 844,
    pixelRatio: 2.5,
    theme: "dark",
    deviceClass: "mobile",
    os: "ios",
    browser: "safari",
  },
  appVersion: "4f2a91c",
  notice: { state: "accepted", attempts: 1, nextAttemptAt: "2026-10-04T05:12:00Z" },
};

const SUMMARY: ReadonlyArray<AdminFeedbackPageSummary> = [
  { page: "jobs", submissions: 4, raters: 3, ratings: [0, 1, 0, 1, 1], mean: 3.6666 },
  { page: "cv-review", submissions: 2, raters: 0, ratings: [0, 0, 0, 0, 0], mean: null },
];

const noCommand = async () => null;

/** The element at the index, which the test has just rendered. */
function nth<T>(items: ReadonlyArray<T>, index: number): T {
  const item = items[index];
  if (item === undefined) throw new Error(`nothing at ${index}`);
  return item;
}

function present<T>(value: T | null): T {
  if (value === null) throw new Error("not rendered");
  return value;
}

function renderView(overrides: Partial<AdminFeedbackViewProps> = {}) {
  const props: AdminFeedbackViewProps = {
    basePath: BASE,
    query: DEFAULT_FEEDBACK_QUERY,
    availability: { kind: "loaded", data: "open" },
    list: { kind: "loaded", data: LIST },
    detail: null,
    summary: { kind: "loaded", data: SUMMARY },
    onStatus: noCommand,
    onRequeue: noCommand,
    ...overrides,
  };
  return render(<AdminFeedbackView {...props} />);
}

const listRegion = () => screen.getByRole("region", { name: "Inskick" });
const statusNav = () => screen.getByRole("navigation", { name: "Filtrera på status" });

describe("AdminFeedbackView — the list (#1979)", () => {
  it("shows each submission as a link to its own URL, with its status, page, rating, time, excerpt and notice", () => {
    renderView();

    const links = within(listRegion()).getAllByRole("link");
    expect(links.map((link) => link.getAttribute("href"))).toEqual([`${BASE}?id=${FIRST}`, `${BASE}?id=${SECOND}`]);
    expect(links[0]).toHaveTextContent(
      "NyAnsökningar2 av 52026-10-04 07:12När jag sparar en ansökan visas den gamla statusen.Avisering: Misslyckades",
    );
    expect(links[1]).toHaveTextContent("AvstårJobbannonsInget betyg2026-10-02 11:03");
    expect(links[1]).not.toHaveTextContent("Avisering");
    // The list never carries an address: an address is read one submission at a time.
    expect(listRegion()).not.toHaveTextContent("@");
  });

  it("marks the open submission, whatever the case of its id in the URL", () => {
    renderView({ query: { ...DEFAULT_FEEDBACK_QUERY, id: FIRST.toUpperCase() } });

    const [first, second] = within(listRegion()).getAllByRole("link");
    expect(first).toHaveAttribute("aria-current", "true");
    expect(second).not.toHaveAttribute("aria-current");
  });

  it("keeps every other part of the URL in each submission's link", () => {
    const query: AdminFeedbackQuery = { status: "new", page: "applications", pageNumber: 2, id: SECOND, window: 7 };
    renderView({ query, list: { kind: "loaded", data: { ...LIST, page: 2, totalPages: 2 } } });

    expect(within(listRegion()).getAllByRole("link")[0]).toHaveAttribute(
      "href",
      `${BASE}?status=ny&sida=applications&sidnr=2&fonster=7&id=${FIRST}`,
    );
  });

  it("filters by status with one link per status, each counted, the chosen one marked", () => {
    renderView({ query: { ...DEFAULT_FEEDBACK_QUERY, status: "resolved", pageNumber: 3, id: FIRST } });

    const links = within(statusNav()).getAllByRole("link");
    expect(links.map((link) => [link.textContent, link.getAttribute("href")])).toEqual([
      ["Alla (7)", `${BASE}?id=${FIRST}`],
      ["Ny (3)", `${BASE}?status=ny&id=${FIRST}`],
      ["Pågår (0)", `${BASE}?status=pagar&id=${FIRST}`],
      ["Åtgärdad (2)", `${BASE}?status=atgardad&id=${FIRST}`],
      ["Avstår (2)", `${BASE}?status=avstar&id=${FIRST}`],
    ]);
    expect(links.filter((link) => link.getAttribute("aria-current") === "true").map((link) => link.textContent)).toEqual([
      "Åtgärdad (2)",
    ]);
  });

  it("pages with links, and offers no page that does not exist", () => {
    renderView({ list: { kind: "loaded", data: { ...LIST, page: 2, totalPages: 3, totalCount: 60 } } });

    const pager = screen.getByRole("navigation", { name: "Sidnavigering" });
    expect(within(pager).getByRole("status")).toHaveTextContent("Sida 2 av 3");
    expect(within(pager).getByRole("link", { name: "Föregående" })).toHaveAttribute("href", BASE);
    expect(within(pager).getByRole("link", { name: "Nästa" })).toHaveAttribute("href", `${BASE}?sidnr=3`);
  });

  it("shows no pager for one page, and no link back from the first page", () => {
    const { unmount } = renderView();
    expect(screen.queryByRole("navigation", { name: "Sidnavigering" })).toBeNull();
    unmount();

    renderView({ list: { kind: "loaded", data: { ...LIST, totalPages: 2, totalCount: 30 } } });
    const pager = screen.getByRole("navigation", { name: "Sidnavigering" });
    expect(within(pager).queryByRole("link", { name: "Föregående" })).toBeNull();
  });

  it.each([
    ["no filter", DEFAULT_FEEDBACK_QUERY, "Inga inskick."],
    ["a status filter", { ...DEFAULT_FEEDBACK_QUERY, status: "inProgress" as const }, "Inga inskick matchar filtret."],
    ["a page filter", { ...DEFAULT_FEEDBACK_QUERY, page: "cv" as const }, "Inga inskick matchar filtret."],
  ])("says so when %s finds nothing, keeping the counts", (_label, query, line) => {
    renderView({
      query,
      list: { kind: "loaded", data: { ...LIST, items: [], totalCount: 0, totalPages: 0, counts: { ...LIST.counts, inProgress: 0 } } },
    });

    expect(listRegion()).toHaveTextContent(line);
    expect(within(statusNav()).getByRole("link", { name: "Pågår (0)" })).toBeInTheDocument();
  });

  it("says a page past the last does not exist, and links to the first", () => {
    renderView({
      query: { ...DEFAULT_FEEDBACK_QUERY, pageNumber: 9 },
      list: { kind: "loaded", data: { ...LIST, items: [], page: 9, totalPages: 1, totalCount: 2 } },
    });

    expect(listRegion()).toHaveTextContent("Sidan finns inte.");
    expect(within(listRegion()).getByRole("link", { name: "Till första sidan" })).toHaveAttribute("href", BASE);
  });

  it("shows a failed list as one alert, with no count in the filter, and keeps the summary", () => {
    renderView({ list: { kind: "failed" } });

    expect(within(listRegion()).getByRole("alert")).toHaveTextContent(
      "Uppgifterna kunde inte hämtas. Försök igen om en stund.",
    );
    expect(statusNav().textContent ?? "").not.toMatch(/\d/);
    expect(screen.getByRole("table", { name: "Betyg per sida de senaste 30 dygnen" })).toBeInTheDocument();
  });

  it("shows the page filter in force, and the link that lifts it", () => {
    renderView({ query: { ...DEFAULT_FEEDBACK_QUERY, page: "cv-review", status: "new", pageNumber: 2 } });

    const scope = present(screen.getByText("Sida: CV-granskning").parentElement);
    expect(within(scope).getByRole("link", { name: "Visa alla sidor" })).toHaveAttribute(
      "href",
      `${BASE}?status=ny`,
    );
  });
});

describe("AdminFeedbackView — whether feedback is open (#1979)", () => {
  it.each([
    ["disabled", "Feedback är avstängd."],
    ["noRecipient", "Feedback är stängd: ingen mottagare för aviseringar är inställd."],
    ["cannotDeliver", "Feedback är stängd: e-posttjänsten skickar inte."],
  ] as const)("says why it is closed (%s)", (data, line) => {
    renderView({ availability: { kind: "loaded", data } });

    expect(screen.getByText(line)).toBeInTheDocument();
  });

  it("says nothing while it is open, and reads a failed read as unknown, never as open", () => {
    const { unmount } = renderView();
    expect(screen.queryByText(/Feedback är/)).toBeNull();
    unmount();

    renderView({ availability: { kind: "failed" } });
    expect(screen.getByText("Det går inte att se om feedback är öppen.")).toHaveAttribute("data-state", "unknown");
  });
});

describe("AdminFeedbackView — the summary (#1979)", () => {
  const table = () => screen.getByRole("table", { name: "Betyg per sida de senaste 30 dygnen" });

  it("counts the ratings per page with the mean to one decimal, and a page with no rating as an en-dash", () => {
    renderView();

    expect(within(table()).getAllByRole("columnheader").map((th) => th.textContent)).toEqual([
      "Sida",
      "Antal betyg",
      "1Betyg 1",
      "2Betyg 2",
      "3Betyg 3",
      "4Betyg 4",
      "5Betyg 5",
      "Medel",
      "Inskick",
    ]);
    const rows = within(table()).getAllByRole("row").slice(1);
    expect(within(nth(rows, 0)).getAllByRole("cell").map((cell) => cell.textContent)).toEqual([
      "Jobb",
      "3",
      "0",
      "1",
      "0",
      "1",
      "1",
      "3,7",
      "4",
    ]);
    expect(nth(within(nth(rows, 1)).getAllByRole("cell"), 7)).toHaveTextContent("–Uppgift saknas");
    expect(table()).not.toHaveTextContent("%");
  });

  it("filters the list to a page from its name, and marks the page in force", () => {
    renderView({ query: { ...DEFAULT_FEEDBACK_QUERY, page: "jobs", pageNumber: 2 } });

    expect(within(table()).getByRole("link", { name: "Jobb" })).toHaveAttribute("href", `${BASE}?sida=jobs`);
    expect(within(table()).getByRole("link", { name: "Jobb" })).toHaveAttribute("aria-current", "true");
    expect(within(table()).getByRole("link", { name: "CV-granskning" })).toHaveAttribute("href", `${BASE}?sida=cv-review`);
  });

  it("chooses its window with one link per window, the chosen one marked", () => {
    renderView({ query: { ...DEFAULT_FEEDBACK_QUERY, window: 7, id: FIRST } });

    const windows = screen.getByRole("navigation", { name: "Period" });
    expect(within(windows).getAllByRole("link").map((link) => [link.textContent, link.getAttribute("href")])).toEqual([
      ["7 dygn", `${BASE}?fonster=7&id=${FIRST}`],
      ["30 dygn", `${BASE}?id=${FIRST}`],
      ["90 dygn", `${BASE}?fonster=90&id=${FIRST}`],
    ]);
    expect(within(windows).getByRole("link", { name: "7 dygn" })).toHaveAttribute("aria-current", "true");
  });

  it.each([
    ["empty", "Inga inskick under perioden."],
    ["failed", "Uppgifterna kunde inte hämtas. Försök igen om en stund."],
    ["loading", "Hämtar uppgifter…"],
  ] as const)("shows one line for the %s state and no table", (kind, line) => {
    renderView({ summary: { kind } });

    const summary = screen.getByRole("region", { name: "Betyg per sida" });
    expect(summary).toHaveTextContent(line);
    expect(within(summary).queryByRole("table")).toBeNull();
  });
});

describe("AdminFeedbackView — the open submission (#1979)", () => {
  const detail = () => screen.getByRole("region", { name: "Valt inskick" });

  it("opens nothing while the URL names nothing", () => {
    renderView();
    expect(screen.queryByRole("region", { name: "Valt inskick" })).toBeNull();
  });

  it("shows the whole text, the reporter, the page, the rating and the time; a status never changed has no line", () => {
    renderView({ query: { ...DEFAULT_FEEDBACK_QUERY, id: FIRST }, detail: { kind: "loaded", data: ITEM } });

    expect(within(detail()).getByText("När jag sparar en ansökan visas den gamla statusen.")).toBeInTheDocument();
    const facts = (term: string) => within(detail()).getByText(term, { selector: "dt" }).nextElementSibling;
    expect(facts("Avsändare")).toHaveTextContent("konto.b@example.test");
    expect(facts("Sida")).toHaveTextContent("Ansökningar");
    expect(facts("Betyg")).toHaveTextContent("2 av 5");
    expect(facts("Skickat")).toHaveTextContent("2026-10-04 07:12");
    expect(within(detail()).queryByText("Status ändrad")).toBeNull();
  });

  it("says what the browser reported, sizes as width × height", () => {
    renderView({ query: { ...DEFAULT_FEEDBACK_QUERY, id: FIRST }, detail: { kind: "loaded", data: ITEM } });

    const reported = present(within(detail()).getByText("Fönster").closest("dl"));
    const rows = within(reported).getAllByRole("definition").map((dd) => dd.textContent);
    expect(rows).toEqual(["390 × 664", "390 × 844", "2,5", "Mörkt", "Mobil", "iOS", "Safari", "4f2a91c"]);
  });

  it("shows what is unknown as an en-dash, never as 0, and what is absent in words", () => {
    const unknown: AdminFeedbackItem = {
      ...ITEM,
      rating: null,
      comment: null,
      reporterEmail: null,
      statusChangedAt: "2026-10-05T08:00:00Z",
      client: {
        viewportWidth: 390,
        viewportHeight: null,
        screenWidth: null,
        screenHeight: null,
        pixelRatio: null,
        theme: null,
        deviceClass: null,
        os: null,
        browser: null,
      },
      appVersion: null,
      notice: null,
    };
    renderView({ query: { ...DEFAULT_FEEDBACK_QUERY, id: FIRST }, detail: { kind: "loaded", data: unknown } });

    expect(within(detail()).getByText("Ingen text.")).toBeInTheDocument();
    const fact = (term: string) => within(detail()).getByText(term, { selector: "dt" }).nextElementSibling;
    expect(fact("Avsändare")).toHaveTextContent("–Uppgift saknas");
    expect(fact("Betyg")).toHaveTextContent("Inget betyg");
    expect(fact("Status ändrad")).toHaveTextContent("2026-10-05 10:00");
    expect(fact("Läge")).toHaveTextContent("–Uppgift saknas");
    expect(within(detail()).queryByText("Försök")).toBeNull();
    const reported = present(within(detail()).getByText("Fönster").closest("dl"));
    for (const value of within(reported).getAllByRole("definition")) {
      expect(value).toHaveTextContent("–Uppgift saknas");
    }
    expect(detail().textContent ?? "").not.toMatch(/\b0\b/);
  });

  it.each([
    ["empty", "Inskicket finns inte."],
    ["failed", "Uppgifterna kunde inte hämtas. Försök igen om en stund."],
  ] as const)("shows one line in the %s state, while the list stays", (kind, line) => {
    renderView({ query: { ...DEFAULT_FEEDBACK_QUERY, id: FIRST }, detail: { kind } });

    expect(detail()).toHaveTextContent(line);
    expect(within(listRegion()).getAllByRole("link")).toHaveLength(2);
  });

  it("keeps replies an unbuilt action: disabled, described by its Kommer snart line, never a primary", () => {
    renderView({ query: { ...DEFAULT_FEEDBACK_QUERY, id: FIRST }, detail: { kind: "loaded", data: ITEM } });

    const reply = within(detail()).getByRole("textbox", { name: "Svar" });
    expect(reply).toBeDisabled();
    expect(reply).toHaveAccessibleDescription("Kommer snart");
    const send = within(detail()).getByRole("button", { name: "Skicka svar" });
    expect(send).toBeDisabled();
    expect(send).toHaveAccessibleDescription("Kommer snart");
    expect(send).not.toHaveClass("jp-btn--primary");
  });
});

describe("AdminFeedbackView — every region in the preview's states (ADR 0150 D2)", () => {
  it.each(["loading", "failed", "unavailable"] as const)(
    "renders %s in every region at once, with no submission and no count",
    (kind) => {
      const region = { kind } as const;
      renderView({
        query: { ...DEFAULT_FEEDBACK_QUERY, id: FIRST },
        availability: region,
        list: region,
        detail: region,
        summary: region,
        onStatus: vi.fn(),
        onRequeue: vi.fn(),
      });

      expect(within(listRegion()).queryAllByRole("link")).toEqual([]);
      expect(screen.queryByRole("table")).toBeNull();
      expect(document.body.textContent ?? "").not.toMatch(/\(\d+\)/);
    },
  );
});
