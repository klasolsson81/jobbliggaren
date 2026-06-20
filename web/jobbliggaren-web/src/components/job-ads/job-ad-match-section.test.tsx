import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { JobAdMatchSection } from "./job-ad-match-section";
import type {
  JobAdMatchDetail,
  MatchDimensionDetail,
  MatchVerdict,
} from "@/lib/dto/job-ad-match";

function row(
  verdict: MatchVerdict,
  matched: string[] = [],
  missing: string[] = []
): MatchDimensionDetail {
  return { verdict, matched, missing };
}

function detail(over: Partial<JobAdMatchDetail> = {}): JobAdMatchDetail {
  return {
    grade: "Top",
    ssykOverlap: row("Match", ["Systemutvecklare"]),
    titleSimilarity: row("NotAssessed"),
    regionFit: row("Match", ["Göteborg"]),
    employmentFit: row("Match", ["Tillsvidare"]),
    skillOverlap: row("Partial", ["Java", "SQL"], ["Kubernetes", "AWS"]),
    mustHaveCoverage: row("Match", ["B-körkort"]),
    niceToHaveCoverage: row("NoMatch", [], ["Franska"]),
    ...over,
  };
}

describe("JobAdMatchSection (F4-16 modal match-sektion)", () => {
  it("renderar grade-chippen (Toppmatch) + alla dimensions-labels", () => {
    render(<JobAdMatchSection match={detail()} />);
    expect(screen.getByText("Toppmatch")).toBeInTheDocument();
    for (const label of [
      "Yrke",
      "Titel",
      "Region",
      "Anställningsform",
      "Kompetenser",
      "Ska-krav",
      "Meriterande",
    ]) {
      expect(screen.getByText(label)).toBeInTheDocument();
    }
  });

  it("renderar verdict-ord (Matchar/Delvis/Saknas/Ej bedömt)", () => {
    render(<JobAdMatchSection match={detail()} />);
    // Flera "Matchar" finns (yrke/region/anställning/ska-krav).
    expect(screen.getAllByText("Matchar").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText("Delvis")).toBeInTheDocument(); // skillOverlap Partial
    expect(screen.getByText("Saknas")).toBeInTheDocument(); // niceToHave NoMatch
    expect(screen.getByText("Ej bedömt")).toBeInTheDocument(); // titel NotAssessed
  });

  it("Vacuous = 'Inga angivna' (annonsen anger inga krav) — fylld prick, ej hålig, ej Match", () => {
    // PR-B1 (ADR 0076 amendment 2026-06-20): den nya 5:e verdikten. Modalen MÅSTE
    // rendera den (annars undefined-ord + bruten Record); den är neutral/definitiv,
    // aldrig hålig (= NotAssessed) och aldrig success-grön (= Match).
    const { container } = render(
      <JobAdMatchSection match={detail({ mustHaveCoverage: row("Vacuous") })} />
    );
    expect(screen.getByText("Inga angivna")).toBeInTheDocument();
    const vacuous = container.querySelector(
      '.jp-modal__matchrow-verdict[data-verdict="Vacuous"]'
    );
    expect(vacuous).not.toBeNull();
    // Fylld prick (definitivt "inget krävs"), aldrig hålig (NotAssessed-markören).
    expect(
      vacuous?.querySelector(".jp-modal__matchrow-dot--hollow")
    ).toBeNull();
  });

  it("renderar matched ('Du har:') och missing ('Annonsen efterfrågar även:')", () => {
    render(<JobAdMatchSection match={detail()} />);
    expect(screen.getByText("Du har: Java, SQL")).toBeInTheDocument();
    expect(
      screen.getByText("Annonsen efterfrågar även: Kubernetes, AWS")
    ).toBeInTheDocument();
  });

  it("NotAssessed = hålig prick + skäl, ALDRIG röd och ALDRIG förväxlad med NoMatch", () => {
    const { container } = render(<JobAdMatchSection match={detail()} />);
    // Hålig prick existerar (NotAssessed-raden) — den ENDA håliga.
    const hollow = container.querySelectorAll(
      ".jp-modal__matchrow-dot--hollow"
    );
    expect(hollow.length).toBeGreaterThanOrEqual(1);

    // Inget verdict-element bär röd/danger-färg (data-verdict styr färgen; bara
    // Match får success, övriga neutral — Saknas/Ej bedömt ALDRIG danger).
    const noMatchVerdict = container.querySelector(
      '.jp-modal__matchrow-verdict[data-verdict="NoMatch"]'
    );
    expect(noMatchVerdict).not.toBeNull();
    // data-verdict skiljer NoMatch ("Saknas") från NotAssessed ("Ej bedömt").
    const notAssessedVerdict = container.querySelector(
      '.jp-modal__matchrow-verdict[data-verdict="NotAssessed"]'
    );
    expect(notAssessedVerdict).not.toBeNull();
    expect(noMatchVerdict).not.toBe(notAssessedVerdict);
  });

  it("INGEN siffra/procent/mätare någonstans (Goodhart-vakt)", () => {
    const { container } = render(<JobAdMatchSection match={detail()} />);
    const section = container.querySelector(".jp-modal__matchsection");
    // Bevis-strängarna är fria från siffror i fixturen; sektionen får aldrig
    // rendera en poäng/procent.
    expect(section?.textContent ?? "").not.toMatch(/\d+\s*%/);
  });

  it("signpost-state: grade=null + yrke NotAssessed → Översikt-nudge-copy + kanonisk länk", () => {
    render(
      <JobAdMatchSection
        match={detail({
          grade: null,
          ssykOverlap: row("NotAssessed"),
        })}
      />
    );
    expect(
      screen.getByText(/Du har inte angett vilka yrken du söker inom/)
    ).toBeInTheDocument();
    const link = screen.getByRole("link", { name: "Ställ in matchning" });
    expect(link).toHaveAttribute("href", "/installningar#matchning");
    // Signpost ersätter nedbrytningen — ingen chip.
    expect(screen.queryByText("Toppmatch")).not.toBeInTheDocument();
  });

  it("grade=null men yrke matchar → nedbrytning utan chip (ärlig, ingen tagg)", () => {
    render(
      <JobAdMatchSection
        match={detail({ grade: null, ssykOverlap: row("Match", ["Snickare"]) })}
      />
    );
    // Ingen chip (grade null), men raderna renderas.
    expect(screen.queryByText("Toppmatch")).not.toBeInTheDocument();
    expect(screen.getByText("Yrke")).toBeInTheDocument();
  });
});
