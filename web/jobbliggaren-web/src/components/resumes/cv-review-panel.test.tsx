import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { CvReviewPanel } from "./cv-review-panel";
import type {
  CvReviewDto,
  CvCriterionVerdictDto,
  CvReviewCategoryDto,
  CriterionVerdict,
  RubricCategory,
} from "@/lib/dto/parsed-resume";

/**
 * IA-redesign (B.1–B.4). Tre lager top-down:
 *   1. "Att åtgärda" — alla Underkänt/Delvis över ALLA kategorier, severitets-
 *      sorterade (Underkänt före Delvis), kritiska först (criticalFails = intern
 *      sortnyckel, inte en separat region).
 *   2. Per kategori — band + räknare + ENBART Godkänt-verdikten.
 *   3. "Ej bedömt" — kollapsad disclosure längst ned (demoterad, aldrig dold —
 *      honesty-invarianten ADR 0074).
 * Ingen opak totalpoäng (Goodhart, §5). Summary utan "v1" (C).
 */

const PARSED_ID = "11111111-1111-4111-8111-111111111111";

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
              note: null,
              observation: null,
            },
          ],
    notAssessedReason:
      v === "NotAssessed" ? `Bedöms inte: ${name}.` : null,
    userStatus: null,
    userStatusStaleAt: null,
    isIgnorable: false,
    ...overrides,
  };
}

function category(
  cat: RubricCategory,
  counts: Pick<
    CvReviewCategoryDto,
    "passCount" | "warnCount" | "failCount" | "notAssessedCount"
  >,
): CvReviewCategoryDto {
  return { category: cat, band: "Competitive", ...counts };
}

/**
 * Fixture med en blandning över flera kategorier:
 *  - Content: ett kritiskt Underkänt (A1), ett Godkänt (A2), ett Ej bedömt (A3)
 *  - Language: ett vanligt Underkänt (C1), ett Delvis (C2)
 *  - Structure: ett kritiskt Delvis (B1), ett Godkänt (B2)
 * criticalFails = A1 (Fail, kritiskt) + B1 (Warn, kritiskt).
 */
function makeReview(overrides: Partial<CvReviewDto> = {}): CvReviewDto {
  const a1 = verdict("A1", "Mätbara resultat", "Content", "Fail");
  const a2 = verdict("A2", "Kontaktuppgifter", "Content", "Pass");
  const a3 = verdict("A3", "Karriärutveckling", "Content", "NotAssessed");
  const c1 = verdict("C1", "Stavning", "Language", "Fail");
  const c2 = verdict("C2", "Meningsbyggnad", "Language", "Warn");
  const b1 = verdict("B1", "Sektionsordning", "Structure", "Warn");
  const b2 = verdict("B2", "Tydliga rubriker", "Structure", "Pass");

  return {
    rubricVersion: "1.0.0",
    profile: "Ats",
    categories: [
      category("Content", {
        passCount: 1,
        warnCount: 0,
        failCount: 1,
        notAssessedCount: 1,
      }),
      category("Language", {
        passCount: 0,
        warnCount: 1,
        failCount: 1,
        notAssessedCount: 0,
      }),
      category("Structure", {
        passCount: 1,
        warnCount: 1,
        failCount: 0,
        notAssessedCount: 0,
      }),
    ],
    verdicts: [a1, a2, a3, c1, c2, b1, b2],
    criticalFails: [a1, b1],
    assessedCount: 6,
    totalCount: 42,
    ...overrides,
  };
}

describe("CvReviewPanel — Att åtgärda (aggregering + sortering)", () => {
  it("aggregerar ALLA Underkänt/Delvis över alla kategorier och utelämnar Godkänt/Ej bedömt", () => {
    render(
      <CvReviewPanel review={makeReview()} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );

    const todo = screen
      .getByRole("heading", { name: /Att åtgärda/ })
      .closest(".jp-cvreview__todo");
    expect(todo).not.toBeNull();
    const scope = within(todo as HTMLElement);

    // Åtgärdbara (Fail/Warn) finns; Godkänt och Ej bedömt finns INTE här.
    expect(scope.getByText("Mätbara resultat")).toBeInTheDocument(); // A1 Fail
    expect(scope.getByText("Stavning")).toBeInTheDocument(); // C1 Fail
    expect(scope.getByText("Meningsbyggnad")).toBeInTheDocument(); // C2 Warn
    expect(scope.getByText("Sektionsordning")).toBeInTheDocument(); // B1 Warn
    expect(scope.queryByText("Kontaktuppgifter")).toBeNull(); // A2 Pass
    expect(scope.queryByText("Karriärutveckling")).toBeNull(); // A3 NotAssessed
  });

  it("räknar antalet åtgärdbara i rubriken", () => {
    render(
      <CvReviewPanel review={makeReview()} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );
    // 2 Fail + 2 Warn = 4.
    expect(
      screen.getByRole("heading", { name: "Att åtgärda (4)" }),
    ).toBeInTheDocument();
  });

  it("sorterar Underkänt före Delvis, och kritiska först inom severiteten", () => {
    render(
      <CvReviewPanel review={makeReview()} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );

    const todo = screen
      .getByRole("heading", { name: /Att åtgärda/ })
      .closest(".jp-cvreview__todo") as HTMLElement;
    const names = Array.from(
      todo.querySelectorAll(".jp-criterion__name"),
    ).map((n) => n.textContent);

    // Förväntad ordning:
    //  1. A1 "Mätbara resultat"  — Fail + kritisk
    //  2. C1 "Stavning"          — Fail (icke-kritisk)
    //  3. B1 "Sektionsordning"   — Warn + kritisk
    //  4. C2 "Meningsbyggnad"    — Warn (icke-kritisk)
    expect(names).toEqual([
      "Mätbara resultat",
      "Stavning",
      "Sektionsordning",
      "Meningsbyggnad",
    ]);
  });

  it("visar en lugn positiv rad (ingen utropstecken) när inget kräver åtgärd", () => {
    const allPass = makeReview({
      verdicts: [verdict("A2", "Kontaktuppgifter", "Content", "Pass")],
      criticalFails: [],
      categories: [
        category("Content", {
          passCount: 1,
          warnCount: 0,
          failCount: 0,
          notAssessedCount: 0,
        }),
      ],
    });
    render(
      <CvReviewPanel review={allPass} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );
    expect(
      screen.getByRole("heading", { name: "Att åtgärda (0)" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Inget kräver åtgärd just nu.")).toBeInTheDocument();
  });
});

describe("CvReviewPanel — kategori-kort visar enbart Godkänt", () => {
  it("renderar bara Pass-verdikten inne i kategori-korten (åtgärdbara är utlyfta)", () => {
    render(
      <CvReviewPanel review={makeReview()} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );

    const contentCard = screen
      .getByRole("heading", { name: "Innehåll", level: 3 })
      .closest("[data-slot='card']") as HTMLElement;
    const scope = within(contentCard);

    // Godkänt-verdiktet visas i kortet …
    expect(scope.getByText("Kontaktuppgifter")).toBeInTheDocument();
    // … men det åtgärdbara (A1 Fail) och Ej bedömt (A3) gör det INTE.
    expect(scope.queryByText("Mätbara resultat")).toBeNull();
    expect(scope.queryByText("Karriärutveckling")).toBeNull();
  });

  it("behåller alla fyra räknarna i kategori-kortet (information är design)", () => {
    render(
      <CvReviewPanel review={makeReview()} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );
    const contentCard = screen
      .getByRole("heading", { name: "Innehåll", level: 3 })
      .closest("[data-slot='card']") as HTMLElement;
    // Räknar-etiketterna sitter i <dt class="jp-cvreview__count-label"> — scope
    // dit så att Pall-verdiktets pill-etikett "Godkänt" inte ger en dubbelmatch.
    const labels = Array.from(
      contentCard.querySelectorAll(".jp-cvreview__count-label"),
    ).map((n) => n.textContent);
    expect(labels).toEqual(["Godkänt", "Delvis", "Underkänt", "Ej bedömt"]);
  });
});

describe("CvReviewPanel — Ej bedömt (kollapsad, men aldrig dold)", () => {
  it("renderar Ej bedömt som en disclosure stängd som default", () => {
    const { container } = render(
      <CvReviewPanel review={makeReview()} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );
    const details = container.querySelector("details.jp-cvreview__unassessed");
    expect(details).not.toBeNull();
    // Stängd som default — inget `open`-attribut.
    expect((details as HTMLDetailsElement).open).toBe(false);
  });

  it("summary räknar de ej bedömda och bär den ärliga orsaken inuti", () => {
    render(
      <CvReviewPanel review={makeReview()} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );
    // En NotAssessed i fixturen (A3).
    expect(screen.getByText("Ej bedömt (1)")).toBeInTheDocument();
    // Den ärliga orsaken renderas (i DOM:en även när disclosure är stängd) —
    // honesty-invarianten: demoterad, aldrig dold/om-etiketterad.
    expect(
      screen.getByText("Bedöms inte: Karriärutveckling."),
    ).toBeInTheDocument();
  });

  it("renderar ingen Ej bedömt-disclosure när det inte finns några", () => {
    const noUnassessed = makeReview({
      verdicts: [verdict("A2", "Kontaktuppgifter", "Content", "Pass")],
      criticalFails: [],
      categories: [
        category("Content", {
          passCount: 1,
          warnCount: 0,
          failCount: 0,
          notAssessedCount: 0,
        }),
      ],
    });
    const { container } = render(
      <CvReviewPanel review={noUnassessed} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );
    expect(container.querySelector("details.jp-cvreview__unassessed")).toBeNull();
  });
});

// Fas 4b PR-8.4 (CTO-bind Q3/Q4): den kanoniska granskningen (befordrad Resume) bär
// statusledgern och renderar därför en per-anmärkning statuskontroll i FOTEN på varje
// ÅTGÄRDBART verdikt. Den parsade stagingen har ingen ledger → inga kontroller. Kontrollens
// grupp-aria-label ("Vad vill du göra med den här anmärkningen") är den stabila markören.
const CANONICAL_ID = "22222222-2222-4222-8222-222222222222";
const STATUS_GROUP = "Vad vill du göra med den här anmärkningen";

describe("CvReviewPanel — statuskontroller (kanonisk vs parsad target)", () => {
  it("kanonisk target renderar en statuskontroll per åtgärdbart verdikt (Fail/Warn)", () => {
    render(
      <CvReviewPanel
        review={makeReview()}
        target={{ kind: "canonical", resumeId: CANONICAL_ID }}
        profile="Ats"
      />,
    );
    // 2 Fail + 2 Warn = 4 åtgärdbara → 4 kontroller. Godkänt/Ej bedömt får ingen.
    expect(
      screen.getAllByRole("group", { name: STATUS_GROUP }),
    ).toHaveLength(4);
  });

  it("kanonisk target: Markera-som-åtgärdad-knappen finns på åtgärdbara anmärkningar", () => {
    render(
      <CvReviewPanel
        review={makeReview()}
        target={{ kind: "canonical", resumeId: CANONICAL_ID }}
        profile="Ats"
      />,
    );
    expect(
      screen.getAllByRole("button", { name: /Markera som åtgärdad/ }),
    ).toHaveLength(4);
  });

  it("parsad target renderar INGA statuskontroller (ingen statusledger)", () => {
    render(
      <CvReviewPanel
        review={makeReview()}
        target={{ kind: "parsed", parsedId: PARSED_ID }}
        profile="Ats"
      />,
    );
    expect(
      screen.queryAllByRole("group", { name: STATUS_GROUP }),
    ).toHaveLength(0);
    expect(
      screen.queryByRole("button", { name: /Markera som åtgärdad/ }),
    ).not.toBeInTheDocument();
  });
});

describe("CvReviewPanel — copy + invarianter", () => {
  it("summary säger 'bedöms.' utan versions-token 'v1' (C)", () => {
    render(
      <CvReviewPanel review={makeReview()} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );
    expect(
      screen.getByText(/6 av 42 kriterier bedöms\./),
    ).toBeInTheDocument();
    // Rubrik-versionstaggen står kvar, men ingen "v1"-jargong i prosan.
    expect(screen.getByText("Rubrik 1.0.0")).toBeInTheDocument();
  });

  it("renderar ALDRIG en opak 0–100-poäng eller totalsumma (Goodhart, §5)", () => {
    const { container } = render(
      <CvReviewPanel review={makeReview()} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );
    const text = container.textContent ?? "";
    expect(text).not.toMatch(/poäng|betyg|score|\/\s*100|av\s*100/i);
  });

  it("degraderar civilt när review är null (role=status, sid-skalet kvar)", () => {
    render(<CvReviewPanel review={null} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />);
    expect(
      screen.getByRole("heading", { name: "Granskning" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent(
      /Granskningen kunde inte laddas just nu/,
    );
  });
});
