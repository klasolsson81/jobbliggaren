import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CvReviewPanel } from "./cv-review-panel";
import type {
  CvReviewDto,
  CvCriterionVerdictDto,
  CvReviewCategoryDto,
  CriterionVerdict,
  RubricCategory,
} from "@/lib/dto/parsed-resume";

/**
 * The CV review as a ledger (#2083): identity row, dimension strip, outcome filter and one table
 * grouped by dimension. The filter lives in the URL — read with `useSearchParams`, written with
 * `history.replaceState` — so the navigation hooks are mocked with a store the replaceState spy
 * updates, which is how Next syncs its router to a native history call.
 *
 * Invariants: no total score; a band never stands without its coverage; Ej bedömt is shown by
 * default and never relabelled; every Godkänt, Delvis and Underkänt shows its evidence.
 */

const nav = vi.hoisted(() => {
  let search = "";
  const listeners = new Set<() => void>();
  return {
    get search() {
      return search;
    },
    set(next: string) {
      search = next;
      listeners.forEach((listener) => listener());
    },
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
});

vi.mock("next/navigation", async (importOriginal) => {
  const React = await import("react");
  return {
    ...(await importOriginal<typeof import("next/navigation")>()),
    usePathname: () => "/cv/resume-1/granska",
    useSearchParams: () =>
      new URLSearchParams(React.useSyncExternalStore(nav.subscribe, () => nav.search)),
  };
});

const replaceState = vi.fn();

beforeEach(() => {
  nav.set("");
  replaceState.mockReset();
  replaceState.mockImplementation((_data: unknown, _unused: string, url?: string | URL | null) => {
    nav.set(new URL(String(url), "http://localhost").search);
  });
  vi.spyOn(window.history, "replaceState").mockImplementation(replaceState);
});

function verdict(
  criterionId: string,
  name: string,
  category: RubricCategory,
  v: CriterionVerdict,
  overrides: Partial<CvCriterionVerdictDto> = {},
): CvCriterionVerdictDto {
  return {
    criterionId,
    name,
    category,
    verdict: v,
    evidence:
      v === "NotAssessed"
        ? []
        : [
            {
              kind: "TextSpan",
              start: 0,
              length: 4,
              quote: `citat-${criterionId}`,
              note: `diagnos-${criterionId}`,
              observation: null,
              isExcerpt: false,
            },
          ],
    notAssessedReason: v === "NotAssessed" ? `Bedöms inte: ${name}.` : null,
    userStatus: null,
    userStatusStaleAt: null,
    isIgnorable: false,
    ...overrides,
  };
}

function category(
  cat: RubricCategory,
  band: CvReviewCategoryDto["band"],
  counts: Pick<CvReviewCategoryDto, "passCount" | "warnCount" | "failCount" | "notAssessedCount">,
): CvReviewCategoryDto {
  return { category: cat, band, ...counts };
}

/**
 * Content (in rubric order): A1 Pass, A2 Fail, A3 Ej bedömt, A4 Fail (critical), A5 Delvis.
 * Language: C1 Fail, C2 Delvis. Structure: B1 Godkänt, B2 Ej bedömt.
 * AtsParsability: one Ej bedömt and no band.
 */
function makeReview(overrides: Partial<CvReviewDto> = {}): CvReviewDto {
  const a4 = verdict("A4", "Mätbara resultat", "Content", "Fail");
  return {
    rubricVersion: "2.3.0",
    profile: "Ats",
    categories: [
      category("Content", "Competitive", { passCount: 1, warnCount: 1, failCount: 2, notAssessedCount: 1 }),
      category("Language", "NeedsRework", { passCount: 0, warnCount: 1, failCount: 1, notAssessedCount: 0 }),
      category("Structure", null, { passCount: 1, warnCount: 0, failCount: 0, notAssessedCount: 1 }),
      category("AtsParsability", null, { passCount: 0, warnCount: 0, failCount: 0, notAssessedCount: 1 }),
    ],
    verdicts: [
      verdict("A1", "Action verbs", "Content", "Pass"),
      verdict("A2", "Profiltext", "Content", "Fail"),
      verdict("A3", "Karriärutveckling", "Content", "NotAssessed"),
      a4,
      verdict("A5", "Anti-klyschor", "Content", "Warn", { isIgnorable: true }),
      verdict("C1", "Stavning", "Language", "Fail"),
      verdict("C2", "Ton", "Language", "Warn"),
      verdict("B1", "Sektioner", "Structure", "Pass"),
      verdict("B2", "Längd", "Structure", "NotAssessed"),
      verdict("D2", "Tabeller", "AtsParsability", "NotAssessed"),
    ],
    criticalFails: [a4],
    assessedCount: 6,
    totalCount: 10,
    ...overrides,
  };
}

function renderCanonical(review: CvReviewDto | null = makeReview(), notice?: React.ReactNode) {
  return render(
    <CvReviewPanel
      review={review}
      target={{ kind: "canonical", resumeId: "resume-1" }}
      profile="Ats"
      documentName="Mitt CV"
      notice={notice}
    />,
  );
}

function table() {
  return screen.getByRole("table", { name: "Granskning per kriterium" });
}

/** Criterion ids of the visible rows, in order. */
function rowIds(): string[] {
  return Array.from(document.querySelectorAll(".jp-cvledger__id")).map((el) => el.textContent ?? "");
}

function stripCell(name: string) {
  return within(screen.getByRole("group", { name: "Filtrera på dimension" })).getByRole("button", { name });
}

describe("CvReviewPanel — identity row", () => {
  it("leads with the coverage sentence, then the CV's name and the rubric version", () => {
    renderCanonical();
    expect(screen.getByText("6 av 10 kriterier är bedömda.")).toBeInTheDocument();
    const meta = document.querySelector(".jp-cvledger__meta");
    expect(Array.from(meta?.children ?? []).map((el) => el.textContent)).toEqual([
      "Mitt CV",
      "Rubrik 2.3.0",
    ]);
  });

  it("carries a chosen outcome filter across the profile switch, but never the dimension", async () => {
    const user = userEvent.setup();
    renderCanonical();
    const visual = () => screen.getByRole("link", { name: "Visuell profil" });
    expect(visual()).toHaveAttribute("href", "/cv/resume-1/granska?profile=Visual");

    await user.click(stripCell("Språk"));
    await user.click(screen.getByRole("radio", { name: /Att åtgärda/ }));
    expect(visual()).toHaveAttribute("href", "/cv/resume-1/granska?profile=Visual&visa=todo");
  });
});

describe("CvReviewPanel — headings and the table's name", () => {
  it("canonical: no heading of its own; the table carries the review's name", () => {
    renderCanonical();
    expect(screen.queryByRole("heading", { name: "Granskning per kriterium" })).not.toBeInTheDocument();
    expect(table()).toBeInTheDocument();
    expect(within(table()).getAllByRole("columnheader").map((th) => th.textContent)).toEqual([
      "Status",
      "Kriterium",
      "Underlag",
      "Åtgärd",
    ]);
  });

  it("staging: its own h2, three columns and no status control", () => {
    render(
      <CvReviewPanel review={makeReview()} target={{ kind: "parsed", parsedId: "p-1" }} profile="Ats" />,
    );
    expect(screen.getByRole("heading", { level: 2, name: "Granskning per kriterium" })).toBeInTheDocument();
    const staging = screen.getByRole("table", { name: "Granskning per kriterium" });
    expect(within(staging).getAllByRole("columnheader")).toHaveLength(3);
    expect(screen.queryByRole("button", { name: /Markera som åtgärdad/ })).not.toBeInTheDocument();
  });
});

describe("CvReviewPanel — the dimension strip", () => {
  it("shows a band only beside its coverage", () => {
    renderCanonical();
    const content = stripCell("Innehåll");
    expect(content).toHaveAttribute("aria-pressed", "false");
    expect(content).toHaveAccessibleDescription(
      "Konkurrenskraftigt 4 av 5 kriterier bedömda Underkänt 2 Delvis 1 Godkänt 1 Ej bedömt 1",
    );
  });

  it("says Ingen bedömning where nothing could be assessed, never a low band", () => {
    renderCanonical();
    expect(stripCell("ATS-läsbarhet")).toHaveAccessibleDescription(
      "Ingen bedömning 0 av 1 kriterier bedömda Underkänt 0 Delvis 0 Godkänt 0 Ej bedömt 1",
    );
  });

  it("an unbanded dimension with assessed criteria shows its coverage without a verdict word", () => {
    renderCanonical();
    const description = stripCell("Struktur").getAttribute("aria-describedby") ?? "";
    const detail = document.getElementById(description);
    expect(detail?.textContent).toContain("1 av 2 kriterier bedömda");
    expect(detail?.textContent).not.toContain("Ingen bedömning");
    expect(detail?.querySelector(".jp-pill")).toBeNull();
  });
});

describe("CvReviewPanel — the ledger at rest", () => {
  it("starts on Alla, so Ej bedömt is shown by default with its reason", () => {
    renderCanonical();
    expect(screen.getByRole("radio", { name: "Alla 10" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Att åtgärda 5" })).not.toBeChecked();
    expect(screen.getByRole("radio", { name: "Godkänt 2" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Ej bedömt 3" })).toBeInTheDocument();
    expect(screen.getByText("Bedöms inte: Karriärutveckling.")).toBeInTheDocument();
  });

  it("orders a group Underkänt, Delvis, Godkänt, Ej bedömt — critical first, then rubric order", () => {
    renderCanonical();
    expect(rowIds()).toEqual(["A4", "A2", "A5", "A1", "A3", "C1", "C2", "B1", "B2", "D2"]);
  });

  it("names each group with its dimension and size, without its band", () => {
    renderCanonical();
    const groups = within(table()).getAllByRole("rowheader").filter((th) => th.classList.contains("jp-cvledger__group"));
    expect(groups.map((th) => th.textContent)).toEqual([
      "Innehåll5 kriterier",
      "Språk2 kriterier",
      "Struktur2 kriterier",
      "ATS-läsbarhet1 kriterium",
    ]);
    expect(groups[0]?.querySelector(".jp-pill")).toBeNull();
  });

  it("shows evidence on every Godkänt, Delvis and Underkänt row", () => {
    renderCanonical();
    for (const id of ["A1", "A2", "A4", "A5", "C1", "C2", "B1"]) {
      expect(screen.getByText(`diagnos-${id}`)).toBeInTheDocument();
      expect(screen.getByText(`citat-${id}`).tagName).toBe("BLOCKQUOTE");
    }
  });

  it("offers a status control on Underkänt and Delvis rows only, named by the criterion", () => {
    renderCanonical();
    expect(screen.getAllByRole("button", { name: /Markera som åtgärdad/ })).toHaveLength(5);
    expect(screen.getByRole("group", { name: "Mätbara resultat" })).toBeInTheDocument();
    expect(screen.queryByRole("group", { name: "Action verbs" })).not.toBeInTheDocument();
    // §5: the ignore offer only where the backend accepts it.
    expect(screen.getAllByRole("button", { name: /Ignorera regeln/ })).toHaveLength(1);
  });

  it("marks an excerpt with a hidden ellipsis and says so outside the quote", () => {
    renderCanonical(
      makeReview({
        verdicts: [
          verdict("A2", "Profiltext", "Content", "Fail", {
            evidence: [
              { kind: "TextSpan", start: 0, length: 4, quote: "Lång profil", note: null, observation: null, isExcerpt: true },
            ],
          }),
        ],
        criticalFails: [],
      }),
    );
    const quote = screen.getByText("Lång profil", { exact: false });
    expect(quote.querySelector(".jp-criterion__quote-excerpt")).toHaveAttribute("aria-hidden", "true");
    const note = screen.getByText("Utdrag, citatet fortsätter i ditt CV.");
    expect(note).toHaveClass("sr-only");
    expect(quote.contains(note)).toBe(false);
  });

  it("explains the dimension verdict behind the ? help", async () => {
    const user = userEvent.setup();
    renderCanonical();
    await user.click(screen.getByRole("button", { name: "Hur räknas omdömet per dimension?" }));
    expect(await screen.findByRole("dialog", { name: "Bedömning per dimension" })).toBeInTheDocument();
  });

  it("never shows a total score", () => {
    const { container } = renderCanonical();
    expect(container.textContent ?? "").not.toMatch(/poäng|betyg|score|\/\s*100|av\s*100|%/i);
  });

  it("places the notice between the strip and the filter", () => {
    renderCanonical(makeReview(), <p data-testid="notice">notis</p>);
    const notice = screen.getByTestId("notice");
    const strip = screen.getByRole("group", { name: "Filtrera på dimension" });
    const filter = screen.getByRole("radiogroup", { name: "Visa kriterier" });
    expect(strip.compareDocumentPosition(notice) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(notice.compareDocumentPosition(filter) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});

describe("CvReviewPanel — filtering", () => {
  it("Att åtgärda keeps the Underkänt and Delvis rows, writes the URL and announces the count", async () => {
    const user = userEvent.setup();
    renderCanonical();
    await user.click(screen.getByRole("radio", { name: "Att åtgärda 5" }));

    expect(replaceState).toHaveBeenLastCalledWith({}, "", "/cv/resume-1/granska?visa=todo");
    expect(rowIds()).toEqual(["A4", "A2", "A5", "C1", "C2"]);
    // Groups without a match are not shown; the shown count reads "x av y".
    expect(screen.queryByText("Struktur", { selector: ".jp-cvledger__groupname" })).not.toBeInTheDocument();
    expect(screen.getByText("3 av 5")).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("5 kriterier visas.");
  });

  it("Ej bedömt keeps only the rows that could not be assessed", async () => {
    const user = userEvent.setup();
    renderCanonical();
    await user.click(screen.getByRole("radio", { name: "Ej bedömt 3" }));
    expect(rowIds()).toEqual(["A3", "B2", "D2"]);
  });

  it("a dimension filters the table and the counts, and clicking it again clears it", async () => {
    const user = userEvent.setup();
    renderCanonical();
    await user.click(stripCell("Språk"));

    expect(stripCell("Språk")).toHaveAttribute("aria-pressed", "true");
    expect(replaceState).toHaveBeenLastCalledWith({}, "", "/cv/resume-1/granska?dim=Language");
    expect(rowIds()).toEqual(["C1", "C2"]);
    expect(screen.getByRole("radio", { name: "Alla 2" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Att åtgärda 2" })).toBeInTheDocument();

    await user.click(stripCell("Språk"));
    expect(stripCell("Språk")).toHaveAttribute("aria-pressed", "false");
    expect(rowIds()).toHaveLength(10);
  });

  it("Visa alla dimensioner clears the dimension and puts focus back on its cell", async () => {
    const user = userEvent.setup();
    renderCanonical();
    expect(screen.queryByRole("button", { name: "Visa alla dimensioner" })).not.toBeInTheDocument();

    await user.click(stripCell("Struktur"));
    await user.click(screen.getByRole("button", { name: "Visa alla dimensioner" }));

    expect(stripCell("Struktur")).toHaveFocus();
    expect(stripCell("Struktur")).toHaveAttribute("aria-pressed", "false");
    expect(screen.queryByRole("button", { name: "Visa alla dimensioner" })).not.toBeInTheDocument();
  });

  it("says so when a combination matches nothing", async () => {
    const user = userEvent.setup();
    renderCanonical();
    await user.click(stripCell("Språk"));
    await user.click(screen.getByRole("radio", { name: /Ej bedömt/ }));
    expect(screen.getByText("Inga kriterier i det här urvalet.")).toBeInTheDocument();
    expect(rowIds()).toEqual([]);
  });

  it("reads the filter from the URL on arrival, ignoring a dimension this profile does not have", () => {
    nav.set("?profile=Ats&dim=VisualQuality&visa=pass");
    renderCanonical();
    expect(screen.getByRole("radio", { name: "Godkänt 2" })).toBeChecked();
    expect(rowIds()).toEqual(["A1", "B1"]);
    // Arrival is not announced; only a change the user makes is.
    expect(screen.getByRole("status")).toHaveTextContent("");
  });
});

describe("CvReviewPanel — review could not be loaded", () => {
  it("keeps the identity row and the profile switch, and says so where the strip would stand", () => {
    renderCanonical(null, <p data-testid="notice">notis</p>);
    expect(screen.getByRole("status")).toHaveTextContent("Granskningen kunde inte laddas just nu");
    expect(screen.getByRole("link", { name: "Visuell profil" })).toBeInTheDocument();
    expect(screen.getByText("Mitt CV")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    expect(screen.getByTestId("notice")).toBeInTheDocument();
  });
});
