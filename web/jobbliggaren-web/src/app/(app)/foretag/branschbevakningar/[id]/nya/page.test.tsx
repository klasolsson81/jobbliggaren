import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createTranslator } from "next-intl";
import svPages from "../../../../../../../messages/sv/pages.json";
import svJobads from "../../../../../../../messages/sv/jobads.json";
import type { JobAdDto } from "@/lib/dto/job-ads";
import type { NewCriterionAds } from "@/lib/dto/company-criteria";
import { MATCH_SETTINGS_HREF } from "@/lib/nav/match-settings-href";
import NewCriterionAdsPage from "./page";

/**
 * `/foretag/branschbevakningar/[id]/nya` (#1663, ADR 0175 D7) — the destination of a criterion's
 * "N nya" pill.
 *
 * <para/> The load-bearing assertion is the ACKNOWLEDGEMENT, for the reason the follow page's test
 * gives: this page is the criterion watermark's only writer, and no backend test sees the call site.
 * So `after()` runs its callback rather than swallowing it.
 *
 * <para/> PREMISE (AGENTS.md §5 `Tests:`). Every fixture is a shape `ListNewCriterionAdsQueryHandler`
 * returns: `NewCriterionAdsDto.Refused` / `.NotScanned` / `.Nothing` for the three closed or empty
 * answers, and a cut window with its token otherwise. The one shape it cannot return is declared
 * where it is used.
 */

const getServerSession = vi.fn();
const getSessionId = vi.fn();
const getNewCriterionAds = vi.fn();
const getCriterionReference = vi.fn();
const markCriterionNewAdsSeen = vi.fn();
const getCompanyWatchCriteria = vi.fn();
const getCriterionNewAdCounts = vi.fn();

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages, jobads: svJobads },
      namespace: namespace as
        | "pages"
        | "pages.foretag"
        | "jobads.companyWatches"
        | "jobads.ui.match"
        | undefined,
    }),
}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
  notFound: () => {
    throw new Error("NEXT_NOT_FOUND");
  },
}));

// Runs the callback, deliberately: this is the seam the acknowledgement rides.
const after = vi.fn((cb: () => unknown) => cb());
vi.mock("next/server", () => ({ after: (cb: () => unknown) => after(cb) }));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
  getSessionId: () => getSessionId(),
}));

vi.mock("@/lib/api/company-criteria", () => ({
  getNewCriterionAds: (...a: unknown[]) => getNewCriterionAds(...a),
  getCriterionReference: (...a: unknown[]) => getCriterionReference(...a),
  markCriterionNewAdsSeen: (...a: unknown[]) => markCriterionNewAdsSeen(...a),
  getCompanyWatchCriteria: (...a: unknown[]) => getCompanyWatchCriteria(...a),
  getCriterionNewAdCounts: (...a: unknown[]) => getCriterionNewAdCounts(...a),
}));

// The dialog's own behaviour is tested with it; here only its presence is, by its trigger's name.
vi.mock("@/components/common/info-dialog", () => ({
  InfoDialog: ({ ariaLabel }: { ariaLabel?: string }) => (
    <button type="button" aria-label={ariaLabel} />
  ),
}));

const ID = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";
const WINDOW = "2026-10-18T03:55:12.123456Z";
const SESSION = "session-abc";

const CRITERION = {
  id: ID,
  sniCodes: ["62010"],
  municipalityCodes: ["0180"],
  label: "IT i Stockholm",
};

const SOURCE = svPages.foretag.criteria.ads.source;

function ad(id: string, title: string): JobAdDto {
  return {
    id,
    title,
    companyName: "Volvo AB",
    url: "https://example.test/ad",
    source: "Platsbanken",
    status: "Active",
    publishedAt: "2026-10-17T10:00:00Z",
    expiresAt: null,
    createdAt: "2026-10-17T10:00:00Z",
  };
}

type Row = NewCriterionAds["rows"][number];

/** A cut window. `matchingAssessed` follows the rows, as the handler builds both from one resolution. */
function rowsResult(
  rows: Row[],
  overrides: Partial<Pick<NewCriterionAds, "acknowledgedThrough" | "truncated">> = {},
) {
  return {
    kind: "ok" as const,
    data: {
      criterion: CRITERION,
      rows,
      matchingAssessed: rows.every((r) => r.matchesYou !== null),
      acknowledgedThrough: WINDOW as string | null,
      truncated: false,
      tooBroad: false,
      notMaterialised: false,
      ...overrides,
    } satisfies NewCriterionAds,
  };
}

/** `NewCriterionAdsDto.Refused` / `.NotScanned` / `.Nothing`. */
function closedResult(state: "tooBroad" | "notMaterialised" | "nothing") {
  return {
    kind: "ok" as const,
    data: {
      criterion: CRITERION,
      rows: [],
      matchingAssessed: false,
      acknowledgedThrough: null,
      truncated: false,
      tooBroad: state === "tooBroad",
      notMaterialised: state === "notMaterialised",
    } satisfies NewCriterionAds,
  };
}

const TWO_ROWS: Row[] = [
  { ad: ad("a1", "Systemutvecklare"), matchesYou: true },
  { ad: ad("a2", "Lagerarbetare"), matchesYou: false },
];

const OK_STATES = [
  ["rows", () => rowsResult(TWO_ROWS)],
  ["nothing new", () => closedResult("nothing")],
  ["too broad", () => closedResult("tooBroad")],
  ["not scanned", () => closedResult("notMaterialised")],
] as const;

async function renderPage() {
  render(await NewCriterionAdsPage({ params: Promise.resolve({ id: ID }) }));
}

const NOTHING_NEW = svPages.foretag.newAds.emptyTitle;
const GRADE_LABELS: string[] = Object.values(svJobads.ui.match.grade);

describe("/foretag/branschbevakningar/[id]/nya — the acknowledgement", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getServerSession.mockResolvedValue({ id: "u1" });
    getSessionId.mockResolvedValue(SESSION);
    getCriterionReference.mockResolvedValue({ kind: "error" });
  });

  it("acknowledges the window the server cut, verbatim, for this criterion and with the session", async () => {
    getNewCriterionAds.mockResolvedValue(rowsResult(TWO_ROWS));

    await renderPage();

    expect(markCriterionNewAdsSeen).toHaveBeenCalledTimes(1);
    // Verbatim: re-serialising through Date would drop the microseconds and acknowledge below the
    // hit the token was cut at.
    expect(markCriterionNewAdsSeen).toHaveBeenCalledWith(ID, WINDOW, SESSION);
    // Read during render, never inside the callback: an `after()` callback cannot read cookies
    // (#741).
    expect(getSessionId.mock.invocationCallOrder[0]).toBeLessThan(
      after.mock.invocationCallOrder[0]!,
    );
  });

  it.each([
    ["error", { kind: "error" as const }],
    ["rateLimited", { kind: "rateLimited" as const, retryAfterSeconds: 60 }],
    ["forbidden", { kind: "forbidden" as const }],
  ])("acknowledges nothing when the read answered %s", async (_kind, result) => {
    getNewCriterionAds.mockResolvedValue(result);

    await renderPage();

    expect(markCriterionNewAdsSeen).not.toHaveBeenCalled();
  });

  it("acknowledges nothing for a criterion that is not the caller's", async () => {
    getNewCriterionAds.mockResolvedValue({ kind: "notFound" });

    await expect(renderPage()).rejects.toThrow("NEXT_NOT_FOUND");
    expect(markCriterionNewAdsSeen).not.toHaveBeenCalled();
  });

  it.each([
    ["nothing new", "nothing"],
    ["too broad", "tooBroad"],
    ["not scanned", "notMaterialised"],
  ] as const)("acknowledges nothing when the criterion has %s", async (_name, state) => {
    getNewCriterionAds.mockResolvedValue(closedResult(state));

    await renderPage();

    expect(markCriterionNewAdsSeen).not.toHaveBeenCalled();
  });

  // The cap arm where the window's last instant is a tie group: rows on screen, no token.
  it("acknowledges nothing when the server named no window, even with rows on screen", async () => {
    getNewCriterionAds.mockResolvedValue(
      rowsResult(TWO_ROWS, { acknowledgedThrough: null, truncated: true }),
    );

    await renderPage();

    expect(screen.getByRole("heading", { level: 2, name: "Systemutvecklare" })).toBeInTheDocument();
    expect(markCriterionNewAdsSeen).not.toHaveBeenCalled();
  });

  // UNREACHABLE STATE, declared. A closed state beside a token is rejected by the
  // `NewCriterionAdsDto` constructor and by `newCriterionAdsSchema`'s refinement, so `src/` cannot
  // produce it. Asserted only as the read side degrading safely: the page acknowledges nothing.
  it.each(["tooBroad", "notMaterialised"] as const)(
    "acknowledges nothing for %s even if a token arrived beside it",
    async (state) => {
      const closed = closedResult(state);
      getNewCriterionAds.mockResolvedValue({
        ...closed,
        data: { ...closed.data, acknowledgedThrough: WINDOW },
      });

      await renderPage();

      expect(markCriterionNewAdsSeen).not.toHaveBeenCalled();
    },
  );

  it("redirects an unauthenticated read to sign-in", async () => {
    getNewCriterionAds.mockResolvedValue({ kind: "unauthorized" });

    await expect(renderPage()).rejects.toThrow("NEXT_REDIRECT:/logga-in");
    expect(markCriterionNewAdsSeen).not.toHaveBeenCalled();
  });
});

describe("/foretag/branschbevakningar/[id]/nya — the reads", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getServerSession.mockResolvedValue({ id: "u1" });
    getSessionId.mockResolvedValue(SESSION);
    getCriterionReference.mockResolvedValue({ kind: "error" });
    getNewCriterionAds.mockResolvedValue(rowsResult(TWO_ROWS));
  });

  it("reads this criterion's window and the reference, and neither the list nor the count route", async () => {
    await renderPage();

    expect(getNewCriterionAds).toHaveBeenCalledTimes(1);
    expect(getNewCriterionAds).toHaveBeenCalledWith(ID);
    expect(getCriterionReference).toHaveBeenCalledTimes(1);
    expect(getCompanyWatchCriteria).not.toHaveBeenCalled();
    expect(getCriterionNewAdCounts).not.toHaveBeenCalled();
  });

  it("heads the page with the criterion's own label", async () => {
    await renderPage();

    expect(screen.getByRole("heading", { level: 1, name: "IT i Stockholm" })).toBeInTheDocument();
    expect(
      screen.getByText(svPages.foretag.criteria.newAds.lede),
    ).toBeInTheDocument();
  });

  it("falls back to the neutral title for an unnamed watch whose reference did not load", async () => {
    const result = rowsResult(TWO_ROWS);
    getNewCriterionAds.mockResolvedValue({
      ...result,
      data: { ...result.data, criterion: { ...CRITERION, label: null } },
    });

    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: svPages.foretag.criteria.row.untitled }),
    ).toBeInTheDocument();
  });
});

describe("/foretag/branschbevakningar/[id]/nya — every state the page answers", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getServerSession.mockResolvedValue({ id: "u1" });
    getSessionId.mockResolvedValue(SESSION);
    getCriterionReference.mockResolvedValue({ kind: "error" });
  });

  it.each(OK_STATES)("%s: the attribution renders, last in the container", async (_name, result) => {
    getNewCriterionAds.mockResolvedValue(result());

    await renderPage();

    const source = screen.getByText(SOURCE);
    expect(source.parentElement?.lastElementChild).toBe(source);
  });

  it.each(OK_STATES)("%s: the watch's breadth and the way back render", async (_name, result) => {
    getNewCriterionAds.mockResolvedValue(result());

    await renderPage();

    const breadth = document.querySelector(".jp-criterion-breadth");
    expect(breadth?.textContent?.replace(/\s+/g, " ").trim()).toBe("1 bransch · 1 kommun");
    const back = screen.getAllByRole("link", { name: svPages.foretag.criteria.ads.backLink });
    expect(back.length).toBeGreaterThanOrEqual(1);
    for (const link of back) {
      expect(link).toHaveAttribute("href", `/foretag/branschbevakningar/${ID}`);
    }
  });

  it.each(OK_STATES)("%s: nothing names followed companies, and there is no sub-nav", async (_name, result) => {
    getNewCriterionAds.mockResolvedValue(result());

    await renderPage();

    // ADR 0117 D3 and the verb lock: this page is the criterion axis, never the follow axis.
    expect(document.body.textContent).not.toMatch(/bevakar|bevakade företag/i);
    expect(document.querySelector(".jp-subnav")).toBeNull();
  });

  it("nothing new: says so, names where the ads are, and links to them", async () => {
    getNewCriterionAds.mockResolvedValue(closedResult("nothing"));

    await renderPage();

    expect(screen.getByText(NOTHING_NEW)).toBeInTheDocument();
    expect(screen.getByText(svPages.foretag.criteria.newAds.emptyBody)).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: svPages.foretag.criteria.ads.showAll }),
    ).toHaveAttribute("href", `/foretag/branschbevakningar/${ID}/annonser`);
    expect(screen.queryByText(svPages.foretag.criteria.ads.tooBroadNoList)).toBeNull();
    expect(screen.queryByText(svPages.foretag.criteria.newAds.notScanned)).toBeNull();
  });

  it("too broad: refuses, with the way back and the way to narrow it, and never says nothing new", async () => {
    getNewCriterionAds.mockResolvedValue(closedResult("tooBroad"));

    await renderPage();

    expect(screen.getByText(svPages.foretag.criteria.ads.tooBroadNoList)).toBeInTheDocument();
    // The route's standing back link, and the block's primary action.
    expect(
      screen.getAllByRole("link", { name: svPages.foretag.criteria.ads.backLink }),
    ).toHaveLength(2);
    expect(
      screen.getByRole("link", { name: svPages.foretag.criteria.ads.matchingTooBroadCta }),
    ).toHaveAttribute("href", "/foretag/branschbevakningar");
    expect(screen.queryByText(NOTHING_NEW)).toBeNull();
    expect(screen.queryByText(svPages.foretag.criteria.newAds.notScanned)).toBeNull();
  });

  it("not scanned: says when the ads are looked for, offers no advice that cannot work, never says nothing new", async () => {
    getNewCriterionAds.mockResolvedValue(closedResult("notMaterialised"));

    await renderPage();

    expect(screen.getByText(svPages.foretag.criteria.newAds.notScanned)).toBeInTheDocument();
    expect(
      screen.getAllByRole("link", { name: svPages.foretag.criteria.ads.backLink }),
    ).toHaveLength(2);
    expect(
      screen.queryByRole("link", { name: svPages.foretag.criteria.ads.matchingTooBroadCta }),
    ).toBeNull();
    expect(screen.queryByText(NOTHING_NEW)).toBeNull();
    expect(screen.queryByText(svPages.foretag.criteria.ads.tooBroadNoList)).toBeNull();
  });

  it.each([
    ["error", { kind: "error" as const }, svPages.foretag.criteria.ads.loadErrorBody],
    ["forbidden", { kind: "forbidden" as const }, svPages.foretag.criteria.ads.loadErrorBody],
    [
      "rateLimited",
      { kind: "rateLimited" as const, retryAfterSeconds: 60 },
      svPages.foretag.criteria.ads.rateLimited,
    ],
  ])("%s: the civil error, with the way back", async (_kind, result, body) => {
    getNewCriterionAds.mockResolvedValue(result);

    await renderPage();

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent(svPages.foretag.newAds.loadErrorTitle);
    expect(alert).toHaveTextContent(body);
    expect(
      screen.getByRole("link", { name: svPages.foretag.criteria.ads.backLink }),
    ).toHaveAttribute("href", `/foretag/branschbevakningar/${ID}`);
    expect(screen.queryByText(NOTHING_NEW)).toBeNull();
  });
});

describe("/foretag/branschbevakningar/[id]/nya — the rows", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getServerSession.mockResolvedValue({ id: "u1" });
    getSessionId.mockResolvedValue(SESSION);
    getCriterionReference.mockResolvedValue({ kind: "error" });
  });

  it("lists every new ad as an h2, in the order the server sent, with no per-row mark", async () => {
    getNewCriterionAds.mockResolvedValue(rowsResult(TWO_ROWS));

    await renderPage();

    const headings = screen.getAllByRole("heading", { level: 2 }).map((h) => h.textContent);
    expect(headings).toEqual(["Systemutvecklare", "Lagerarbetare"]);
    for (const label of GRADE_LABELS) expect(screen.queryByText(label)).toBeNull();
    expect(screen.queryByText(NOTHING_NEW)).toBeNull();
  });

  it("states both numbers and what counts as a match", async () => {
    getNewCriterionAds.mockResolvedValue(rowsResult(TWO_ROWS));

    await renderPage();

    expect(screen.getByText("2 nya annonser, varav 1 matchar dig.")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: svJobads.companyWatches.filter.onlyMatchedHelpAria }),
    ).toBeInTheDocument();
  });

  it("explains the seat selection, with its dialog", async () => {
    getNewCriterionAds.mockResolvedValue(rowsResult(TWO_ROWS));

    await renderPage();

    expect(screen.getByText(svPages.foretag.criteria.ads.seatExplainer)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: svPages.foretag.criteria.ads.seatHelpAria }),
    ).toBeInTheDocument();
  });

  it("discloses the consumption while the ads are on screen", async () => {
    getNewCriterionAds.mockResolvedValue(rowsResult(TWO_ROWS));

    await renderPage();

    expect(
      screen.getByText(
        `${svPages.foretag.newAds.consumptionNote} ${svPages.foretag.newAds.consumptionNoteAcknowledged}`,
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(svPages.foretag.newAds.truncated)).toBeNull();
  });

  it("claims no total when truncated, keeps the remainder for next time, and says so at the foot", async () => {
    getNewCriterionAds.mockResolvedValue(rowsResult(TWO_ROWS, { truncated: true }));

    await renderPage();

    expect(screen.queryByText("2 nya annonser, varav 1 matchar dig.")).toBeNull();
    expect(screen.getByText("Visar 2 nya annonser, varav 1 matchar dig.")).toBeInTheDocument();
    // The remainder is newer than the window and returns, so "only what has been added since"
    // would be false here.
    expect(screen.getByText(svPages.foretag.newAds.consumptionNote)).toBeInTheDocument();
    expect(
      screen.queryByText(new RegExp(svPages.foretag.newAds.consumptionNoteAcknowledged)),
    ).toBeNull();
    expect(screen.getByText(svPages.foretag.newAds.truncated)).toBeInTheDocument();
  });

  it("says matching is not assessed, offers the setting, and offers no switch", async () => {
    getNewCriterionAds.mockResolvedValue(
      rowsResult([{ ad: ad("a1", "Systemutvecklare"), matchesYou: null }]),
    );

    await renderPage();

    expect(screen.getByText("1 ny annons.")).toBeInTheDocument();
    expect(screen.getByText(/inte angett vilka yrken/)).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: svJobads.ui.match.settingsCta }),
    ).toHaveAttribute("href", MATCH_SETTINGS_HREF);
    expect(
      screen.queryByRole("button", { name: svJobads.companyWatches.filter.onlyMatchedHelpAria }),
    ).toBeNull();
    expect(screen.queryByRole("radiogroup")).toBeNull();
    expect(screen.getByRole("heading", { level: 2, name: "Systemutvecklare" })).toBeInTheDocument();
  });

  it("filters the matching arm in the browser, without a second read", async () => {
    getNewCriterionAds.mockResolvedValue(rowsResult(TWO_ROWS));

    await renderPage();
    await userEvent.click(screen.getByRole("radio", { name: svPages.foretag.newAds.view.matching }));

    // A navigation-driven arm would read again after the watermark moved and land on nothing.
    expect(getNewCriterionAds).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("heading", { level: 2, name: "Systemutvecklare" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Lagerarbetare" })).toBeNull();
    expect(
      screen.getByText(svJobads.companyWatches.filter.activeOnlyMatched),
    ).toBeInTheDocument();
    // The counts stay over the whole set.
    expect(screen.getByText("2 nya annonser, varav 1 matchar dig.")).toBeInTheDocument();
  });
});
