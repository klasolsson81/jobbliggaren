import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { CvCriterionVerdict } from "./cv-criterion-verdict";
import type { CvCriterionVerdictDto } from "@/lib/dto/parsed-resume";

/**
 * Förklarbarhets-invarianten (ADR 0074): varje verdikt VISAR sin citerade evidens.
 * IA-redesign (B.3): den läsbara rubriken (`name`) leder raden, koden (`criterionId`)
 * demoteras till en dämpad sekundär referens. Kategori-etiketten visas som rad-
 * kontext bara när verdiktet är utlyft ur sitt kategori-kort (prop-styrt).
 */

function makeVerdict(
  overrides: Partial<CvCriterionVerdictDto> = {},
): CvCriterionVerdictDto {
  return {
    criterionId: "A1",
    name: "Mätbara resultat",
    category: "Content",
    verdict: "Fail",
    evidence: [
      {
        kind: "TextSpan",
        start: 0,
        length: 8,
        quote: "ansvarade för budget",
        note: "saknar mätbart utfall",
        observation: null,
        isExcerpt: false,
      },
    ],
    notAssessedReason: null,
    userStatus: null,
    userStatusStaleAt: null,
    isIgnorable: false,
    ...overrides,
  };
}

describe("CvCriterionVerdict — rubrik leder, kod demoteras (B.3)", () => {
  it("renderar den läsbara rubriken (name) som primär rad-rubrik", () => {
    render(<CvCriterionVerdict verdict={makeVerdict()} />);
    expect(screen.getByText("Mätbara resultat")).toBeInTheDocument();
  });

  it("behåller criterionId som en dämpad sekundär mono-referens (kvar, men ej primär)", () => {
    const { container } = render(<CvCriterionVerdict verdict={makeVerdict()} />);
    const id = container.querySelector(".jp-criterion__id");
    expect(id).not.toBeNull();
    expect(id).toHaveTextContent("A1");
    // Koden bärs av .jp-criterion__id, rubriken av .jp-criterion__name — skilda
    // noder så att koden aldrig kan råka bli den primära etiketten.
    expect(id).not.toHaveTextContent("Mätbara resultat");
  });

  it("visar verdict-etiketten som text, aldrig enbart färg (WCAG 1.4.1)", () => {
    render(<CvCriterionVerdict verdict={makeVerdict({ verdict: "Fail" })} />);
    expect(screen.getByText("Underkänt")).toBeInTheDocument();
  });

  it("renderar den citerade evidensen (förklarbarhets-invarianten)", () => {
    render(<CvCriterionVerdict verdict={makeVerdict()} />);
    expect(screen.getByText("ansvarade för budget")).toBeInTheDocument();
    expect(screen.getByText("saknar mätbart utfall")).toBeInTheDocument();
  });

  it("låter diagnosen LEDA och citatet stödja (#1062 minor 6)", () => {
    // Mätt före fixen: noten stod UNDER utdraget och på --text-caption (13px) medan
    // utdraget låg på --text-body-sm på en fylld platta — på ett Underkänt låg alltså
    // det användaren behöver först både sist och minst. Ordningen mäts på DOM-ordningen
    // inom evidens-punkten; att båda finns var redan sant före fixen och skiljer inte
    // de två formerna åt.
    const { container } = render(<CvCriterionVerdict verdict={makeVerdict()} />);
    const item = container.querySelector(
      ".jp-criterion__evidence-item",
    ) as HTMLElement;
    expect(Array.from(item.children).map((n) => n.className)).toEqual([
      "jp-criterion__note",
      "jp-criterion__quote",
    ]);
  });

  it("håller utdrags-meningen kvar EFTER citatet när evidensen är kapad", () => {
    // Inversionen får inte flytta motorns egen upplysning om utdraget in i citatet —
    // den är motorns ord om användarens text, inte användarens (#1062 B2).
    const { container } = render(
      <CvCriterionVerdict
        verdict={makeVerdict({
          evidence: [
            {
              kind: "TextSpan",
              start: 0,
              length: 8,
              quote: "ansvarade för budget",
              note: "saknar mätbart utfall",
              observation: null,
              isExcerpt: true,
            },
          ],
        })}
      />,
    );
    const item = container.querySelector(
      ".jp-criterion__evidence-item",
    ) as HTMLElement;
    expect(Array.from(item.children).map((n) => n.className)).toEqual([
      "jp-criterion__note",
      "jp-criterion__quote",
      "sr-only",
    ]);
    expect(container.querySelector(".sr-only")?.closest("blockquote")).toBeNull();
  });
});

describe("CvCriterionVerdict — kategori-kontext (utlyfta rader)", () => {
  it("visar kategori-etiketten när categoryLabel-propen ges", () => {
    const { container } = render(
      <CvCriterionVerdict verdict={makeVerdict()} categoryLabel="Innehåll" />,
    );
    const category = container.querySelector(".jp-criterion__category");
    expect(category).not.toBeNull();
    expect(category).toHaveTextContent("Innehåll");
  });

  it("visar INGEN kategori-tagg när propen utelämnas (inne i kategori-kortet)", () => {
    const { container } = render(<CvCriterionVerdict verdict={makeVerdict()} />);
    expect(container.querySelector(".jp-criterion__category")).toBeNull();
  });
});

describe("CvCriterionVerdict — NotAssessed", () => {
  it("visar den ärliga orsaken (aldrig ett påhittat utfall)", () => {
    render(
      <CvCriterionVerdict
        verdict={makeVerdict({
          verdict: "NotAssessed",
          evidence: [],
          notAssessedReason: "Vi bedömer inte karriärutveckling i den här versionen.",
        })}
      />,
    );
    expect(screen.getByText("Ej bedömt")).toBeInTheDocument();
    expect(
      screen.getByText(
        "Vi bedömer inte karriärutveckling i den här versionen.",
      ),
    ).toBeInTheDocument();
    // De-jargon: aldrig "v1" i etiketten (STEG 2 äger detta).
    expect(screen.queryByText(/v1/)).toBeNull();
  });
});
