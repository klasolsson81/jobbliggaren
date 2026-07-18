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

  it("must-have-sammanfattning (PR-B2): Match → 'Du uppfyller alla ska-krav'", () => {
    render(
      <JobAdMatchSection
        match={detail({ mustHaveCoverage: row("Match", ["B-körkort"]) })}
      />
    );
    expect(
      screen.getByText("Du uppfyller alla ska-krav i annonsen.")
    ).toBeInTheDocument();
  });

  it("must-have-sammanfattning (PR-B2): NoMatch → 'Du uppfyller inte annonsens ska-krav'", () => {
    render(
      <JobAdMatchSection
        match={detail({ mustHaveCoverage: row("NoMatch", [], ["Java"]) })}
      />
    );
    expect(
      screen.getByText("Du uppfyller inte annonsens ska-krav.")
    ).toBeInTheDocument();
  });

  it("must-have-sammanfattning (PR-B2): Vacuous → 'Annonsen anger inga särskilda ska-krav'", () => {
    render(
      <JobAdMatchSection match={detail({ mustHaveCoverage: row("Vacuous") })} />
    );
    expect(
      screen.getByText("Annonsen anger inga särskilda ska-krav.")
    ).toBeInTheDocument();
  });

  it("utan CV (must-have NotAssessed) → 'ladda upp CV'-signpost → /cv/importera, ingen summering", () => {
    // PR-B2: utan CV kan man inte nå Stark/Topp → signposten driver CV-upload.
    render(
      <JobAdMatchSection
        match={detail({
          grade: "Good",
          mustHaveCoverage: row("NotAssessed"),
          skillOverlap: row("NotAssessed"),
          niceToHaveCoverage: row("NotAssessed"),
        })}
      />
    );
    expect(
      screen.getByText(/Det krävs för Stark match och Toppmatch/)
    ).toBeInTheDocument();
    const link = screen.getByRole("link", { name: "Ladda upp CV" });
    expect(link).toHaveAttribute("href", "/cv/importera");
    // Must-have-summeringen visas INTE när CV saknas (signposten ersätter den).
    expect(
      screen.queryByText(/Du uppfyller/)
    ).not.toBeInTheDocument();
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

  // #300 PR-5 (ADR 0084) — Related-match: chip + "därför lägre"-förklaring på
  // Yrke-raden.
  it("Related → chip 'Relaterat yrke' + Yrke-raden förklarar VARFÖR lägre (neutral, ingen siffra)", () => {
    const { container } = render(
      <JobAdMatchSection
        match={detail({
          grade: "Related",
          ssykOverlap: row("Match", ["Systemutvecklare"]),
        })}
      />
    );
    // Chippen visas (neutral kategori).
    expect(screen.getByText("Relaterat yrke")).toBeInTheDocument();
    // Yrke-raden bär den neutrala "därför lägre"-copyn i stället för den
    // generiska "Du har:"-bevisformen.
    expect(
      screen.getByText(
        "Liknande yrke, inte ett du valt. Därför rankas annonsen under dina exakta träffar."
      )
    ).toBeInTheDocument();
    expect(
      screen.queryByText("Du har: Systemutvecklare")
    ).not.toBeInTheDocument();
    // Neutral ink (ej röd) + ingen siffra (Goodhart).
    const section = container.querySelector(".jp-modal__matchsection");
    expect(
      container.querySelector(".text-danger-600, .text-danger-700")
    ).toBeNull();
    expect(section?.textContent ?? "").not.toMatch(/\d/);
  });

  it("Related-förklaringen visas BARA på Yrke-raden, inte på andra dimensioner", () => {
    render(
      <JobAdMatchSection
        match={detail({
          grade: "Related",
          ssykOverlap: row("Match", ["Systemutvecklare"]),
          regionFit: row("Match", ["Göteborg"]),
        })}
      />
    );
    // Region-raden behåller sin generiska bevisform (förklaringen är yrkes-scoped).
    expect(screen.getByText("Du har: Göteborg")).toBeInTheDocument();
  });
});

describe("JobAdMatchSection — per-ska-krav-checklista (#5b / STEG 2)", () => {
  it("Ska-krav (Partial) → per-krav-checklista: uppfyllda + saknade var sin rad, INTE generisk 'Du har:'", () => {
    render(
      <JobAdMatchSection
        match={detail({
          mustHaveCoverage: row(
            "Partial",
            ["B-körkort", "Truckkort"],
            ["Svetslicens"]
          ),
          // Neutralisera meriterande så statustexterna inte korsräknas.
          niceToHaveCoverage: row("Vacuous"),
        })}
      />
    );
    // Varje krav på egen rad.
    expect(screen.getByText("B-körkort")).toBeInTheDocument();
    expect(screen.getByText("Truckkort")).toBeInTheDocument();
    expect(screen.getByText("Svetslicens")).toBeInTheDocument();
    // Status (sr-only): två uppfyllda, ett ej uppfyllt.
    expect(screen.getAllByText("Uppfyllt").length).toBe(2);
    expect(screen.getByText("Ej uppfyllt")).toBeInTheDocument();
    // INTE den generiska bevisformen för ska-krav.
    expect(screen.queryByText(/Du har: B-körkort/)).not.toBeInTheDocument();
    expect(
      screen.queryByText(/Annonsen efterfrågar även: Svetslicens/)
    ).not.toBeInTheDocument();
  });

  it("saknade krav använder NEUTRAL ink (jp-modal__matchrow-missing), ALDRIG röd/danger", () => {
    const { container } = render(
      <JobAdMatchSection
        match={detail({
          mustHaveCoverage: row("NoMatch", [], ["Svetslicens"]),
          niceToHaveCoverage: row("Vacuous"),
        })}
      />
    );
    // Ett saknat krav är inget fel: neutral ink, aldrig danger-färg (CTO/§5).
    expect(screen.getByText("Svetslicens")).toHaveClass(
      "jp-modal__matchrow-missing"
    );
    expect(
      container.querySelector(".text-danger-600, .text-danger-700")
    ).toBeNull();
  });

  it("Vacuous (annonsen anger inga krav) → ingen checklista, bara verdict + footer", () => {
    render(
      <JobAdMatchSection
        match={detail({
          mustHaveCoverage: row("Vacuous"),
          niceToHaveCoverage: row("Vacuous"),
        })}
      />
    );
    // En tom checklista vore vilseledande → ingen status renderas.
    expect(screen.queryByText("Uppfyllt")).not.toBeInTheDocument();
    expect(screen.queryByText("Ej uppfyllt")).not.toBeInTheDocument();
    // Footern bär den ärliga summan i stället.
    expect(
      screen.getByText("Annonsen anger inga särskilda ska-krav.")
    ).toBeInTheDocument();
  });

  it("Meriterande (nice-to-have) renderas också som per-krav-checklista", () => {
    render(
      <JobAdMatchSection
        match={detail({
          mustHaveCoverage: row("Vacuous"),
          niceToHaveCoverage: row("Partial", ["Franska"], ["Tyska"]),
        })}
      />
    );
    expect(screen.getByText("Franska")).toBeInTheDocument();
    expect(screen.getByText("Tyska")).toBeInTheDocument();
    expect(screen.getByText("Uppfyllt")).toBeInTheDocument();
    expect(screen.getByText("Ej uppfyllt")).toBeInTheDocument();
  });
});

describe("JobAdMatchSection — titel-dimensionen (#5a / STEG 4)", () => {
  it("titel (Match) → per-verdict-sammanfattning, ALDRIG råa Snowball-stammar", () => {
    render(
      <JobAdMatchSection
        match={detail({ titleSimilarity: row("Match", ["snickar"], []) })}
      />
    );
    expect(
      screen.getByText("Din roll stämmer med annonsens titel.")
    ).toBeInTheDocument();
    // Lexem-stammen är intern scoring-detalj — visas aldrig i UI.
    expect(screen.queryByText(/snickar/)).not.toBeInTheDocument();
  });

  it("titel (NoMatch) → neutral fras (yrket/SSYK är primär signal)", () => {
    render(
      <JobAdMatchSection
        match={detail({
          titleSimilarity: row("NoMatch", [], ["elektrikerstam"]),
        })}
      />
    );
    expect(
      screen.getByText("Din titel skiljer sig från annonsens.")
    ).toBeInTheDocument();
    expect(screen.queryByText(/elektrikerstam/)).not.toBeInTheDocument();
  });

  it("titel (Partial) → 'stämmer delvis'-fras", () => {
    render(
      <JobAdMatchSection
        match={detail({
          titleSimilarity: row("Partial", ["snickar"], ["murarstam"]),
        })}
      />
    );
    expect(
      screen.getByText("Din roll stämmer delvis med annonsens titel.")
    ).toBeInTheDocument();
  });

  it("titel (NotAssessed, ingen roll i CV:t) → uppdaterad reason", () => {
    render(
      <JobAdMatchSection
        match={detail({ titleSimilarity: row("NotAssessed") })}
      />
    );
    expect(
      screen.getByText("Ingen roll i ditt CV att jämföra.")
    ).toBeInTheDocument();
  });
});

describe("JobAdMatchSection — RegionFit granularitet (Spår 3 PR-D)", () => {
  // label → granularitet (härledd FE-side ur taxonomin, architect NOTE-2).
  const granularity = {
    Göteborg: "municipality" as const,
    Solna: "municipality" as const,
    "Stockholms län": "region" as const,
    "Västra Götalands län": "region" as const,
  };

  it("kommun-träff och län-träff skiljs åt i RegionFit-beviset", () => {
    render(
      <JobAdMatchSection
        match={detail({ regionFit: row("Match", ["Göteborg", "Stockholms län"]) })}
        ortGranularityByLabel={granularity}
      />
    );
    expect(screen.getByText("Kommun som matchar: Göteborg")).toBeInTheDocument();
    expect(
      screen.getByText("Län som matchar: Stockholms län")
    ).toBeInTheDocument();
    // Den generiska "Du har:"-formen används INTE för Region-radens orter när
    // kartan finns (de splittas till kommun-/län-fraser i stället).
    expect(
      screen.queryByText(/Du har: Göteborg/)
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText(/Du har:.*Stockholms län/)
    ).not.toBeInTheDocument();
  });

  it("missing ort skiljer kommun och län (annonsens ort som inte är angiven)", () => {
    render(
      <JobAdMatchSection
        match={detail({ regionFit: row("NoMatch", [], ["Solna", "Västra Götalands län"]) })}
        ortGranularityByLabel={granularity}
      />
    );
    expect(screen.getByText("Annonsens kommun: Solna")).toBeInTheDocument();
    expect(
      screen.getByText("Annonsens län: Västra Götalands län")
    ).toBeInTheDocument();
  });

  it("Gotland-fall (label saknas i kartan) faller till coarser/plain län-hinken, ingen krasch", () => {
    // Okänd/tvetydig label klassas som "region" i splitten (plain text i
    // län-hinken) — aldrig en krasch, aldrig felaktig kommun-kategori.
    render(
      <JobAdMatchSection
        match={detail({ regionFit: row("Match", ["Gotland"]) })}
        ortGranularityByLabel={granularity}
      />
    );
    expect(screen.getByText("Län som matchar: Gotland")).toBeInTheDocument();
  });

  it("utan granularitets-karta faller RegionFit till generisk bevisform (bakåtkompat)", () => {
    render(
      <JobAdMatchSection
        match={detail({ regionFit: row("Match", ["Göteborg"]) })}
      />
    );
    expect(screen.getByText("Du har: Göteborg")).toBeInTheDocument();
    expect(screen.queryByText(/Kommun som matchar/)).not.toBeInTheDocument();
  });

  // #552-grinden (ADR 0076-amendment): en angiven ort-/anställningsform-preferens
  // mot en annons som INTE anger dimensionen ger NoMatch med TOM matched/missing.
  // Bevisraden måste förklara annonsens tystnad — aldrig en tom cell.
  describe("adUnspecifiedReason (#552 — NoMatch med tom evidens)", () => {
    it("RegionFit NoMatch utan evidens → 'Annonsen anger ingen region.' i neutral ink", () => {
      const { container } = render(
        <JobAdMatchSection match={detail({ regionFit: row("NoMatch") })} />
      );
      const reason = screen.getByText("Annonsen anger ingen region.");
      expect(reason).toBeInTheDocument();
      // Neutral ink (ink-2), aldrig röd — annonsens tystnad är inget fel.
      expect(reason.className).toContain("jp-modal__matchrow-missing");
      // Verdiktet är fortfarande Saknas (graden golvas — det är grindens poäng).
      const verdict = container.querySelector(
        '.jp-modal__matchrow-verdict[data-verdict="NoMatch"]'
      );
      expect(verdict).not.toBeNull();
    });

    it("EmploymentFit NoMatch utan evidens → 'Annonsen anger ingen anställningsform.'", () => {
      render(
        <JobAdMatchSection match={detail({ employmentFit: row("NoMatch") })} />
      );
      expect(
        screen.getByText("Annonsen anger ingen anställningsform.")
      ).toBeInTheDocument();
    });

    it("förklaringen visas ÄVEN med granularitets-karta (grenen ligger före granularitets-grenen)", () => {
      render(
        <JobAdMatchSection
          match={detail({ regionFit: row("NoMatch") })}
          ortGranularityByLabel={granularity}
        />
      );
      expect(screen.getByText("Annonsen anger ingen region.")).toBeInTheDocument();
    });

    it("explicit ort-mismatch (missing bär annonsens ort) tar INTE den nya grenen", () => {
      render(
        <JobAdMatchSection
          match={detail({ regionFit: row("NoMatch", [], ["Stockholms län"]) })}
        />
      );
      expect(
        screen.getByText("Annonsen efterfrågar även: Stockholms län")
      ).toBeInTheDocument();
      expect(
        screen.queryByText("Annonsen anger ingen region.")
      ).not.toBeInTheDocument();
    });
  });
});
