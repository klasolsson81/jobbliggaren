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
 * IA-redesign (B.1–B.4), omviktad i #1062 Q1. Tre lager top-down:
 *   1. "Att åtgärda" — alla Underkänt/Delvis över ALLA kategorier, severitets-
 *      sorterade (Underkänt före Delvis), kritiska först (criticalFails = intern
 *      sortnyckel, inte en separat region). Sidans huvudinnehåll: h2, inget kort.
 *   2. "Bedömning per dimension" — EN rad per kategori: band med sin täckning + en
 *      demoterad räknarrad, med Godkänt-verdikten bakom en disclosure.
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
              isExcerpt: false,
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
  /** `null` = kategorin har inget bedömt kriterium och bär därför inget band
   * (#1062 B1). Explicit parameter så att ett obandat kort måste väljas, inte
   * uppstå av att ett fält glöms. */
  band: CvReviewCategoryDto["band"] = "Competitive",
): CvReviewCategoryDto {
  return { category: cat, band, ...counts };
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

/** Kategorins rad i lager 2. Raderna ersatte korten i #1062 Q1; rubriken är den
 * stabila ankaren i båda formerna. */
function dimensionRow(cat: string): HTMLElement {
  return screen
    .getByRole("heading", { name: cat, level: 3 })
    .closest(".jp-cvreview__dimension") as HTMLElement;
}

type DimensionCounts = Pick<
  CvReviewCategoryDto,
  "passCount" | "warnCount" | "failCount" | "notAssessedCount"
>;

/** Alla fyra räknarna nollskilda SAMTIDIGT. `makeReview`s Innehåll bär `warnCount: 0`,
 * så en regel som aldrig renderade "Delvis" hade passerat mot den fixturen. */
const ALL_NONZERO: DimensionCounts = {
  passCount: 5,
  warnCount: 2,
  failCount: 1,
  notAssessedCount: 3,
};

/** En granskning med EN kategori vars räknare är `counts` OCH vars verdikt-lista
 * summerar till dem. Räknarna kommer ur `categories` och raderna ur `verdicts`, så en
 * fixtur som lät dem gå isär hade mätt räknarraden mot ett underlag motorn aldrig
 * producerar. */
function makeTallyReview(counts: DimensionCounts): CvReviewDto {
  const rows = (n: number, prefix: string, v: CriterionVerdict) =>
    Array.from({ length: n }, (_, i) =>
      verdict(`${prefix}${i + 1}`, `${prefix}-kriterium ${i + 1}`, "Content", v),
    );
  const assessed = counts.passCount + counts.warnCount + counts.failCount;
  return makeReview({
    verdicts: [
      ...rows(counts.passCount, "P", "Pass"),
      ...rows(counts.warnCount, "W", "Warn"),
      ...rows(counts.failCount, "F", "Fail"),
      ...rows(counts.notAssessedCount, "N", "NotAssessed"),
    ],
    criticalFails: [],
    categories: [category("Content", counts)],
    assessedCount: assessed,
    totalCount: assessed + counts.notAssessedCount,
  });
}

function tallyOf(row: HTMLElement): HTMLElement {
  return row.querySelector(".jp-cvreview__tally") as HTMLElement;
}

function renderTally(counts: DimensionCounts) {
  return render(
    <CvReviewPanel
      review={makeTallyReview(counts)}
      target={{ kind: "parsed", parsedId: PARSED_ID }}
      profile="Ats"
    />,
  );
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
    // #1062 minor 3: meningen är knuten till sitt UNDERLAG. Pre-fix stod "Inget
    // kräver åtgärd just nu." ensamt medan 36 av 42 kriterier aldrig bedömdes — en
    // rad som läses som ett utlåtande om hela CV:t men bara bär de 6 bedömda.
    // Fixturen bär avsiktligt assessedCount 6 av totalCount 42.
    expect(
      screen.getByText(/Inget av de 6 bedömda kriterierna kräver åtgärd\./),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/36 kriterier kunde inte bedömas\./),
    ).toBeInTheDocument();
  });

  it("utelämnar ej-bedömt-meningen när allt faktiskt bedömdes", () => {
    // Kontrafaktum till testet ovan: utan det hade "36 kriterier kunde inte
    // bedömas" kunnat renderas ovillkorligt och ändå passerat.
    const everythingAssessed = makeReview({
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
      assessedCount: 1,
      totalCount: 1,
    });
    const { container } = render(
      <CvReviewPanel
        review={everythingAssessed}
        target={{ kind: "parsed", parsedId: PARSED_ID }}
        profile="Ats"
      />,
    );
    expect(container.textContent ?? "").not.toMatch(/kunde inte bedömas/);
  });

  it("noll bedömda kriterier: meningen påstår inget om CV:t, bara om underlaget", () => {
    // ICU-grenen `=0`. Ett CV där ingenting kunde bedömas får INTE läsa "inget kräver
    // åtgärd" — det vore ett utlåtande om ett CV granskningen aldrig läste. Samma
    // §5-familj som B1, och fullt producerbar (en degraderad parse).
    const nothingAssessed = makeReview({
      verdicts: [verdict("A8", "Profiltext", "Content", "NotAssessed")],
      criticalFails: [],
      categories: [
        category(
          "Content",
          { passCount: 0, warnCount: 0, failCount: 0, notAssessedCount: 1 },
          null,
        ),
      ],
      assessedCount: 0,
      totalCount: 1,
    });
    render(
      <CvReviewPanel
        review={nothingAssessed}
        target={{ kind: "parsed", parsedId: PARSED_ID }}
        profile="Ats"
      />,
    );

    expect(
      screen.getByText(
        /Inget kriterium kunde bedömas, så granskningen pekar inte ut något/,
      ),
    ).toBeInTheDocument();
    // singularis-grenen av todoEmptyUnassessed: "1 kriterium", aldrig "1 kriterier".
    expect(screen.getByText(/1 kriterium kunde inte bedömas\./)).toBeInTheDocument();
  });
});

// #1062 Q1: lagren har rätt ORDNING och hade fel VIKT. Mätt på levererad kod:
// kategorikorten tog 2059px av 3577px docH (58 %) på ett svagt CV och 2225px av 3396px
// (66 %) på ett rent, och de 15 raderna i dem var 15 `Godkänt` — verdikt som redan är
// avklarade — medan de två åtgärdbara fynden fick ~450px.
describe("CvReviewPanel — lager 2 är rader, inte kort (#1062 Q1)", () => {
  function renderDefault() {
    return render(
      <CvReviewPanel review={makeReview()} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );
  }

  it("renderar en rad per kategori och inget Card alls", () => {
    const { container } = renderDefault();
    expect(container.querySelectorAll("[data-slot='card']")).toHaveLength(0);
    expect(container.querySelectorAll(".jp-cvreview__dimension")).toHaveLength(3);
  });

  it("lager 1 och lager 2 bär var sin h2 — jämlikar under sidans h1", () => {
    // Före fixen ägde panelen en h2 som upprepade sidans h1, vilket sköt ned "Att
    // åtgärda" till h3 — samma nivå som kategorikortens rubriker. Rangen bar alltså
    // peer-läsningen som Q1 river, inte bara typografin.
    renderDefault();
    expect(
      screen.getByRole("heading", { name: "Att åtgärda (4)", level: 2 }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "Bedömning per dimension", level: 2 }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "Innehåll", level: 3 }),
    ).toBeInTheDocument();
    // …och panelens gamla h2 finns inte kvar som en fjärde rubrik ovanför dem.
    expect(
      screen.queryByRole("heading", { name: "Granskning per kriterium" }),
    ).toBeNull();
  });

  it("håller de Godkända bakom en STÄNGD disclosure — demoterade, aldrig dolda", () => {
    renderDefault();
    const details = dimensionRow("Innehåll").querySelector(
      "details.jp-cvreview__pass",
    ) as HTMLDetailsElement;
    expect(details).not.toBeNull();
    expect(details.open).toBe(false);
    // Innehåll har ETT Godkänt (A2). Det står i DOM:en även stängt — samma gräns som
    // honesty-invarianten drar för "Ej bedömt".
    expect(within(details).getByText("1 godkänt kriterium")).toBeInTheDocument();
    expect(within(details).getByText("Kontaktuppgifter")).toBeInTheDocument();
  });

  it("lyfter fortfarande ut det åtgärdbara och det ej bedömda ur dimensionen", () => {
    renderDefault();
    const scope = within(dimensionRow("Innehåll"));
    expect(scope.queryByText("Mätbara resultat")).toBeNull(); // A1 Fail → lager 1
    expect(scope.queryByText("Karriärutveckling")).toBeNull(); // A3 NotAssessed → lager 3
  });

  it("renderar INGEN disclosure på en dimension utan Godkänt", () => {
    // Språk: 0 Pass. En <summary> som öppnar tomrum är en affordans som ljuger.
    renderDefault();
    expect(
      dimensionRow("Språk").querySelector("details.jp-cvreview__pass"),
    ).toBeNull();
  });

  it("täckningsberättelsen leder i eget element, skild från hederlighetsklausulen", () => {
    const { container } = renderDefault();
    expect(
      container.querySelector(".jp-cvreview__coverage")?.textContent,
    ).toBe("6 av 42 kriterier bedöms.");
    expect(
      container.querySelector(".jp-cvreview__coverage-note")?.textContent ?? "",
    ).toMatch(/räknas som ej bedömda och sänker inte omdömet\./);
  });
});

// #1062 Q2 — räknarnas MEDIUM byter, informationen gör det inte. Det pinnade beslutet
// ("information är design": fyra räknare, aldrig en enda sammanfattande siffra) står
// kvar; det som ändrades är att de fyra boxade talen i --text-h3/bold/tonfärg blev en
// rad i brödtext, och att NOLLOR undertrycks — 8 av 16 celler var `0` på ett rent CV.
// Varje räknare mäts i BÅDA riktningarna: den renderas med sitt tal när den är
// nollskild, och den finns inte alls när den är noll. Ett ensidigt test kan inte skilja
// "undertrycker nollor" från "renderar aldrig".
describe("CvReviewPanel — räknarna per dimension (#1062 Q2)", () => {
  const COUNTERS = [
    { label: "Godkänt", value: ALL_NONZERO.passCount, zero: { ...ALL_NONZERO, passCount: 0 } },
    { label: "Delvis", value: ALL_NONZERO.warnCount, zero: { ...ALL_NONZERO, warnCount: 0 } },
    { label: "Underkänt", value: ALL_NONZERO.failCount, zero: { ...ALL_NONZERO, failCount: 0 } },
    {
      label: "Ej bedömt",
      value: ALL_NONZERO.notAssessedCount,
      zero: { ...ALL_NONZERO, notAssessedCount: 0 },
    },
  ];

  it.each(COUNTERS)("renderar $label med sitt tal när räknaren är nollskild", ({ label, value }) => {
    renderTally(ALL_NONZERO);
    const item = within(tallyOf(dimensionRow("Innehåll")))
      .getByText(label)
      .closest(".jp-cvreview__tally-item") as HTMLElement;
    expect(item.textContent).toBe(`${label}${value}`);
  });

  it.each(COUNTERS)("utelämnar $label helt när räknaren är noll", ({ label, zero }) => {
    renderTally(zero);
    const tally = tallyOf(dimensionRow("Innehåll"));
    expect(within(tally).queryByText(label)).toBeNull();
    // …och de tre andra står kvar: undertryckningen är per räknare, inte per rad.
    expect(tally.querySelectorAll(".jp-cvreview__tally-item")).toHaveLength(3);
  });

  it("renderar alla fyra samtidigt när alla fyra är nollskilda", () => {
    renderTally(ALL_NONZERO);
    const labels = Array.from(
      tallyOf(dimensionRow("Innehåll")).querySelectorAll("dt"),
    ).map((n) => n.textContent);
    expect(labels).toEqual(["Godkänt", "Delvis", "Underkänt", "Ej bedömt"]);
  });

  it("skriver aldrig ut en nolla i raden", () => {
    renderTally({ ...ALL_NONZERO, warnCount: 0, failCount: 0 });
    const values = Array.from(
      tallyOf(dimensionRow("Innehåll")).querySelectorAll("dd"),
    ).map((n) => n.textContent);
    expect(values).toEqual(["5", "3"]);
  });

  it("utelämnar hela raden på den obandade dimensionen, där meningen ovanför bär talet", () => {
    // Den enda platsen där raden bara skulle upprepa en mening ordagrant: CategoryBand
    // skriver redan "Inget av de 8 kriterierna kunde bedömas". Villkoret är DEN
    // meningens villkor (band===null && assessed===0), inte assessed===0 ensamt.
    const unmeasured = makeReview({
      verdicts: [verdict("E1", "Layout", "VisualQuality", "NotAssessed")],
      criticalFails: [],
      categories: [
        category(
          "VisualQuality",
          { passCount: 0, warnCount: 0, failCount: 0, notAssessedCount: 8 },
          null,
        ),
      ],
    });
    render(
      <CvReviewPanel
        review={unmeasured}
        target={{ kind: "parsed", parsedId: PARSED_ID }}
        profile="Visual"
      />,
    );
    const row = dimensionRow("Visuell kvalitet");
    expect(row.querySelector(".jp-cvreview__tally")).toBeNull();
    expect(row.textContent ?? "").toMatch(
      /Inget av de 8 kriterierna kunde bedömas\./,
    );
  });

  it("behåller raden på en obandad dimension som ändå har bedömda kriterier", () => {
    // Kontrafaktum till testet ovan: undertryckningen får inte bita på `band === null`
    // ensamt. En rubrikbump med en nollviktad nivå ger weightSum===0 med bedömda
    // kriterier kvar, och då är räknarna det enda som säger hur de föll.
    const zeroWeighted = makeReview({
      verdicts: [verdict("E1", "Layout", "VisualQuality", "Pass")],
      criticalFails: [],
      categories: [
        category(
          "VisualQuality",
          { passCount: 3, warnCount: 0, failCount: 0, notAssessedCount: 5 },
          null,
        ),
      ],
    });
    render(
      <CvReviewPanel
        review={zeroWeighted}
        target={{ kind: "parsed", parsedId: PARSED_ID }}
        profile="Visual"
      />,
    );
    const tally = tallyOf(dimensionRow("Visuell kvalitet"));
    expect(tally).not.toBeNull();
    expect(
      Array.from(tally.querySelectorAll("dt")).map((n) => n.textContent),
    ).toEqual(["Godkänt", "Ej bedömt"]);
  });
});

describe("CvReviewPanel — bandet står aldrig utan sitt underlag (#1062 B1/M1/M2)", () => {
  it("renderar INGEN bandpill när kategorin saknar bedömda kriterier", () => {
    // B1, mätt på levererad kod: ?profile=Visual gav ett FELFRITT CV
    // "VisualQuality band=NotReady pass=0 warn=0 fail=0 na=8" — alltså rubrikens
    // BOTTENETIKETT, röd "Ej redo", på en dimension där ingenting kunde mätas.
    // Det är CLAUDE.md §5 ordagrant: "Ej bedömt" får demoteras men ALDRIG renderas
    // som en låg grad.
    const unmeasured = makeReview({
      verdicts: [verdict("E1", "Layout", "VisualQuality", "NotAssessed")],
      criticalFails: [],
      categories: [
        category(
          "VisualQuality",
          { passCount: 0, warnCount: 0, failCount: 0, notAssessedCount: 8 },
          null,
        ),
      ],
    });
    render(
      <CvReviewPanel
        review={unmeasured}
        target={{ kind: "parsed", parsedId: PARSED_ID }}
        profile="Visual"
      />,
    );

    const card = dimensionRow("Visuell kvalitet");
    const text = card.textContent ?? "";
    expect(text).not.toMatch(/Ej redo|Behöver omarbetning|Konkurrenskraftigt|Toppskikt/);
    // Frånvaron skrivs ut i klartext — den förmedlas inte genom att en pill saknas.
    expect(text).toMatch(/Ingen bedömning\. Inget av de 8 kriterierna kunde bedömas\./);
  });

  it("renderar bandet MED sin täckning när kategorin är bedömd", () => {
    // M2, mätt: ATS-läsbarhet stod "Toppskikt" på BÅDE ett svagt och ett rent CV,
    // båda gånger av 2 bedömda kriterier av 10 — nämnaren var osynlig, så 3 av 4
    // band var identiska mellan ett CV med 2 Underkänt och ett med 0. M1 är samma
    // rot: bandet är en VIKTAD poäng, räknarna under det en OVIKTAD tally.
    render(
      <CvReviewPanel review={makeReview()} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );

    const card = dimensionRow("Innehåll");
    const band = card.querySelector(".jp-cvreview__band") as HTMLElement;
    expect(band).not.toBeNull();
    // Content: pass 1 + warn 0 + fail 1 = 2 bedömda, notAssessed 1 → 3 totalt.
    expect(band.textContent ?? "").toMatch(
      /Konkurrenskraftigt\s*2 av 3 kriterier bedömda/,
    );
  });

  it("frånvaromeningen grindas på att inget bedömdes, inte på att bandet saknas", () => {
    // Meningen PÅSTÅR "inget av de N kriterierna kunde bedömas". Backend håller
    // band===null och assessed===0 ekvivalenta idag — men bara därför att rubrikens
    // vikter alla är > 0. En rubrikbump med en nollviktad nivå ger weightSum===0 med
    // bedömda kriterier kvar, och då hade sidan skrivit ut ett påstående som räknarna
    // på raden under motbevisar: B1:s felklass, inverterad.
    const zeroWeighted = makeReview({
      verdicts: [verdict("E1", "Layout", "VisualQuality", "Pass")],
      criticalFails: [],
      categories: [
        category(
          "VisualQuality",
          { passCount: 3, warnCount: 0, failCount: 0, notAssessedCount: 5 },
          null,
        ),
      ],
    });
    render(
      <CvReviewPanel
        review={zeroWeighted}
        target={{ kind: "parsed", parsedId: PARSED_ID }}
        profile="Visual"
      />,
    );

    const card = dimensionRow("Visuell kvalitet");
    const text = card.textContent ?? "";
    expect(text).not.toMatch(/Ingen bedömning/);
    expect(text).toMatch(/3 av 8 kriterier bedömda/);
    // …och fortfarande ingen pill: bandet fick vi inte, så vi påstår det inte.
    expect(text).not.toMatch(/Ej redo|Behöver omarbetning|Konkurrenskraftigt|Toppskikt/);
  });

  it("bär täckningen i SAMMA block som pillen, inte någon annanstans på raden", () => {
    // Kontrafaktum: utan detta hade täckningen kunnat renderas var som helst på
    // raden och testet ovan hade ändå passerat på card.textContent.
    render(
      <CvReviewPanel review={makeReview()} target={{ kind: "parsed", parsedId: PARSED_ID }} profile="Ats" />,
    );

    const band = dimensionRow("Språk").querySelector(
      ".jp-cvreview__band",
    ) as HTMLElement;
    expect(band.querySelector(".jp-cvreview__band-coverage")).not.toBeNull();
  });
});

describe("CvReviewPanel — citerat utdrag markeras (#1062 B2)", () => {
  it("ritar markören när evidensen är ett utdrag, och aldrig annars", () => {
    // Motorn skriver ALDRIG in "…" i citatet (två pinnade backend-invarianter), så
    // markören måste komma härifrån — annars implicerar ett kapat citat att det är
    // hela användarens mening.
    const withExcerpt = makeReview({
      verdicts: [
        verdict("A8", "Profiltext", "Content", "Pass", {
          evidence: [
            {
              kind: "TextSpan",
              start: 0,
              length: 10,
              quote: "Erfaren systemutvecklare",
              note: null,
              observation: null,
              isExcerpt: true,
            },
          ],
        }),
        verdict("A2", "Kontaktuppgifter", "Content", "Pass"),
      ],
      criticalFails: [],
      categories: [
        category("Content", {
          passCount: 2,
          warnCount: 0,
          failCount: 0,
          notAssessedCount: 0,
        }),
      ],
    });
    const { container } = render(
      <CvReviewPanel
        review={withExcerpt}
        target={{ kind: "parsed", parsedId: PARSED_ID }}
        profile="Ats"
      />,
    );

    expect(
      container.querySelectorAll(".jp-criterion__quote-excerpt"),
    ).toHaveLength(1);
    // A2:s citat bär isExcerpt: false och får därför ingen markör — det är
    // kontrafaktumet som gör räkningen ovan till en mätning.
    expect(container.querySelectorAll(".jp-criterion__quote")).toHaveLength(2);

    const mark = container.querySelector(".jp-criterion__quote-excerpt");
    expect(mark).not.toBeNull();
    // Ellipsen är dekorativ och MÅSTE vara dold — annars annonseras utdraget två gånger.
    expect(mark?.textContent ?? "").toMatch(/…/);
    expect(mark?.getAttribute("aria-hidden")).toBe("true");

    // …och den talade meningen ligger UTANFÖR blockquote:n. Inne i den hade en
    // skärmläsare hört motorns egen upplysning som en del av användarens citat —
    // precis den klass av påstående den här PR:en stänger.
    const spoken = container.querySelector(".sr-only");
    expect(spoken?.textContent).toMatch(/Utdrag, citatet fortsätter i ditt CV\./);
    expect(spoken?.closest("blockquote")).toBeNull();
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
// grupp-aria-label ("Status för anmärkningen") är den stabila markören.
const CANONICAL_ID = "22222222-2222-4222-8222-222222222222";
const STATUS_GROUP = "Status för anmärkningen";

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
    // Panelen bär inget eget innehålls-h2 sedan #1062 Q1 — regionen NAMNGES i stället,
    // så den står kvar som en landmark på en sida full av parse-artefakter utan att
    // upprepa sidans h1.
    expect(
      screen.getByRole("region", { name: "Granskning per kriterium" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent(
      /Granskningen kunde inte laddas just nu/,
    );
  });
});
