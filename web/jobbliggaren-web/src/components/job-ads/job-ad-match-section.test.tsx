import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { render as rawRender } from "@testing-library/react/pure";
import { NextIntlClientProvider } from "next-intl";
import enMessages from "../../../messages/en";
import { JobAdMatchSection } from "./job-ad-match-section";
import type {
  JobAdMatchDetail,
  MatchCause,
  MatchCodedDimensionDetail,
  MatchDimensionDetail,
  MatchRegisterDimensionDetail,
  MatchVerdict,
} from "@/lib/dto/job-ad-match";

function row(
  verdict: MatchVerdict,
  matched: string[] = [],
  missing: string[] = []
): MatchDimensionDetail {
  return { verdict, matched, missing };
}

// Yrke och Region bär {conceptId, label} (#1598). `null` som label är det snapshoten TAPPAT
// — samma form som `ITaxonomyReadModel.ResolveLabelsAsync` emitterar för ett id utan rad i
// `taxonomy_concepts`, alltså ett tillstånd produktionen faktiskt producerar. Id:t är
// syntetiskt men aldrig tomt: en post utan namn måste ändå EXISTERA, och det är just det
// testerna nedan mäter.
function registerRow(
  verdict: MatchVerdict,
  matched: Array<string | null> = [],
  missing: Array<string | null> = [],
  cause: MatchCause | null = null
): MatchRegisterDimensionDetail {
  const entries = (labels: Array<string | null>, side: string) =>
    labels.map((label, i) => ({
      conceptId: label === null ? `LOST_${side}_${i}` : `id_${label}`,
      label,
    }));
  return {
    verdict,
    matched: entries(matched, "matched"),
    missing: entries(missing, "missing"),
    cause,
  };
}

// Anställningsform bär conceptId, inte visningstext (#1537) — komponenten namnger dem via
// katalogen. "kpPX_CNN_gDU" är Tillsvidareanställning i klass2-taxonomin.
function codedRow(
  verdict: MatchVerdict,
  matchedConceptIds: string[] = [],
  missingConceptIds: string[] = [],
  cause: MatchCause | null = null
): MatchCodedDimensionDetail {
  return { verdict, matchedConceptIds, missingConceptIds, cause };
}

function detail(over: Partial<JobAdMatchDetail> = {}): JobAdMatchDetail {
  return {
    grade: "Top",
    ssykOverlap: registerRow("Match", ["Systemutvecklare"]),
    titleSimilarity: row("NotAssessed"),
    regionFit: registerRow("Match", ["Göteborg"]),
    employmentFit: codedRow("Match", ["kpPX_CNN_gDU"]),
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

  it("signpost-state: grade=null + yrket obesvarat AV ANVÄNDAREN → Översikt-nudge-copy + kanonisk länk", () => {
    render(
      <JobAdMatchSection
        match={detail({
          grade: null,
          ssykOverlap: registerRow("NotAssessed", [], [], "PreferenceUnstated"),
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
        match={detail({ grade: null, ssykOverlap: registerRow("Match", ["Snickare"]) })}
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
          ssykOverlap: registerRow("Match", ["Systemutvecklare"]),
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
          ssykOverlap: registerRow("Match", ["Systemutvecklare"]),
          regionFit: registerRow("Match", ["Göteborg"]),
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
  // conceptId → granularitet (härledd FE-side ur taxonomin, architect NOTE-2).
  // Nycklarna speglar `registerRow`s id-form (`id_<label>`) — kartan slår upp
  // postens id, aldrig dess namn.
  const granularity = {
    id_Göteborg: "municipality" as const,
    id_Solna: "municipality" as const,
    "id_Stockholms län": "region" as const,
    "id_Västra Götalands län": "region" as const,
  };

  it("kommun-träff och län-träff skiljs åt i RegionFit-beviset", () => {
    render(
      <JobAdMatchSection
        match={detail({ regionFit: registerRow("Match", ["Göteborg", "Stockholms län"]) })}
        ortGranularityByConceptId={granularity}
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
        match={detail({ regionFit: registerRow("NoMatch", [], ["Solna", "Västra Götalands län"]) })}
        ortGranularityByConceptId={granularity}
      />
    );
    expect(screen.getByText("Annonsens kommun: Solna")).toBeInTheDocument();
    expect(
      screen.getByText("Annonsens län: Västra Götalands län")
    ).toBeInTheDocument();
  });

  it("concept-id som saknas i kartan får ort-ramen, inte ett län-påstående (#1598)", () => {
    // Före #1598 föll den i län-hinken och renderades "Län som matchar: Gotland"
    // — ett explicit län-PÅSTÅENDE om en post vi inte kunde klassa. En post vars
    // id finns i kartan påverkas inte (nästa test).
    render(
      <JobAdMatchSection
        match={detail({ regionFit: registerRow("Match", ["Gotland"]) })}
        ortGranularityByConceptId={granularity}
      />
    );
    expect(screen.getByText("Ort som matchar: Gotland")).toBeInTheDocument();
    expect(screen.queryByText(/Län som matchar: Gotland/)).not.toBeInTheDocument();
  });

  it("plain-hinken gäller MISSING-halvan också, i annonsens ram", () => {
    // Den halva som varken var pinnad eller renderad förut (`design-reviewer`
    // 2026-08-31). "Annonsens ort" är samma meningsram som syskonen, med den
    // o-granulära termen — aldrig kompetens-verbet "efterfrågar", som dessutom
    // hade dinglat på "även" när plain-hinken är den enda missing-hinken.
    render(
      <JobAdMatchSection
        match={detail({
          regionFit: registerRow("NoMatch", [], ["Gotland", "Stockholms län"]),
        })}
        ortGranularityByConceptId={granularity}
      />
    );
    expect(screen.getByText("Annonsens ort: Gotland")).toBeInTheDocument();
    expect(
      screen.getByText("Annonsens län: Stockholms län")
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/Annonsen efterfrågar även: Gotland/)
    ).not.toBeInTheDocument();
  });

  it("en post vars id finns i kartan behåller sitt granularitets-prefix", () => {
    // Den halvan som fäller en regression där plain-hinken slukar allt.
    render(
      <JobAdMatchSection
        match={detail({ regionFit: registerRow("Match", ["Stockholms län"]) })}
        ortGranularityByConceptId={granularity}
      />
    );
    expect(
      screen.getByText("Län som matchar: Stockholms län")
    ).toBeInTheDocument();
    expect(screen.queryByText(/Du har: Stockholms län/)).not.toBeInTheDocument();
  });

  it("utan granularitets-karta faller RegionFit till generisk bevisform (bakåtkompat)", () => {
    render(
      <JobAdMatchSection
        match={detail({ regionFit: registerRow("Match", ["Göteborg"]) })}
      />
    );
    expect(screen.getByText("Du har: Göteborg")).toBeInTheDocument();
    expect(screen.queryByText(/Kommun som matchar/)).not.toBeInTheDocument();
  });

  // #552-grinden (ADR 0076-amendment): en angiven ort-/anställningsform-preferens
  // mot en annons som INTE anger dimensionen ger NoMatch med TOM matched/missing.
  // Bevisraden måste förklara annonsens tystnad — aldrig en tom cell. Skälet KOMMER
  // numera från servern (`cause: "AdSilent"`); komponenten härleder det inte längre.
  describe("AdSilent (#552 — NoMatch med tom evidens)", () => {
    it("RegionFit NoMatch utan evidens → 'Annonsen anger varken län eller kommun.' i neutral ink", () => {
      const { container } = render(
        <JobAdMatchSection
          match={detail({
            regionFit: registerRow("NoMatch", [], [], "AdSilent"),
          })}
        />
      );
      const reason = screen.getByText("Annonsen anger varken län eller kommun.");
      expect(reason).toBeInTheDocument();
      // Neutral ink, aldrig röd — annonsens tystnad är inget fel.
      expect(reason.className).toContain("jp-modal__matchrow-missing");
      // Verdiktet är fortfarande Saknas (graden golvas — det är grindens poäng).
      const verdict = container.querySelector(
        '.jp-modal__matchrow-verdict[data-verdict="NoMatch"]'
      );
      expect(verdict).not.toBeNull();
    });

    it("EmploymentFit namnger sina koder ur katalogen, inte som råa id (#1537)", () => {
      render(<JobAdMatchSection match={detail()} />);
      expect(
        screen.getByText(
          /Tillsvidareanställning \(inkl\. eventuell provanställning\)/
        )
      ).toBeInTheDocument();
      // Negativt, och det är den halvan som fäller en regression som tappar
      // `.map(codedName)`: id:t typkontrollerar grönt men får aldrig nå raden.
      expect(screen.queryByText(/kpPX_CNN_gDU/)).toBeNull();
    });

    it("EmploymentFit namnger dem på engelska under locale en (#1537)", () => {
      // `render` går genom shimen som hårdkodar locale="sv"; det engelska fallet
      // renderas via `/pure`, som alias-ankaret lämnar oomskrivet.
      rawRender(
        <NextIntlClientProvider
          locale="en"
          messages={enMessages}
          timeZone="Europe/Stockholm"
        >
          <JobAdMatchSection match={detail()} />
        </NextIntlClientProvider>
      );
      expect(
        screen.getByText(
          /Permanent employment \(including any trial employment\)/
        )
      ).toBeInTheDocument();
    });

    it("EmploymentFit NoMatch utan evidens → 'Annonsen anger ingen anställningsform.'", () => {
      render(
        <JobAdMatchSection
          match={detail({
            employmentFit: codedRow("NoMatch", [], [], "AdSilent"),
          })}
        />
      );
      expect(
        screen.getByText("Annonsen anger ingen anställningsform.")
      ).toBeInTheDocument();
    });

    it("förklaringen visas ÄVEN med granularitets-karta (grenen ligger före granularitets-grenen)", () => {
      render(
        <JobAdMatchSection
          match={detail({
            regionFit: registerRow("NoMatch", [], [], "AdSilent"),
          })}
          ortGranularityByConceptId={granularity}
        />
      );
      expect(screen.getByText("Annonsen anger varken län eller kommun.")).toBeInTheDocument();
    });

    it("explicit ort-mismatch (missing bär annonsens ort) tar INTE den nya grenen", () => {
      render(
        <JobAdMatchSection
          match={detail({ regionFit: registerRow("NoMatch", [], ["Stockholms län"]) })}
        />
      );
      expect(
        screen.getByText("Annonsen efterfrågar även: Stockholms län")
      ).toBeInTheDocument();
      expect(
        screen.queryByText("Annonsen anger varken län eller kommun.")
      ).not.toBeInTheDocument();
    });
  });

  // De tre defekterna orsaks-koden stänger. Var och en är en rad vars ORSAK wire:t
  // inte kunde uttrycka, så klienten härledde den — och härledde fel.
  describe("orsaker wire:t förut inte kunde uttrycka", () => {
    it("distans-annons: bevis-cellen är inte längre tom under ordet Matchar", () => {
      // Defekten i sin exakta form: verdict Match, båda listorna tomma OCH
      // granularitets-kartan satt, så raden gick till RegionFitEvidence vars alla
      // spans är length>0-gatade och renderade INGENTING.
      const { container } = render(
        <JobAdMatchSection
          match={detail({
            regionFit: registerRow("Match", [], [], "RemoteOverride"),
          })}
          ortGranularityByConceptId={granularity}
        />
      );
      expect(
        screen.getByText(
          "Annonsen erbjuder distansarbete och matchar därför oavsett ort."
        )
      ).toBeInTheDocument();
      // Och verdiktet står kvar — orsaken förklarar raden, den ändrar den inte.
      expect(
        container.querySelector(
          '.jp-modal__matchrow-verdict[data-verdict="Match"]'
        )
      ).not.toBeNull();
    });

    it("län-only-annons som rymmer din kommun påstår INTE att du saknar region", () => {
      render(
        <JobAdMatchSection
          match={detail({
            regionFit: registerRow(
              "NotAssessed",
              [],
              [],
              "RegionContainsPreferredMunicipality"
            ),
          })}
          ortGranularityByConceptId={granularity}
        />
      );
      expect(
        screen.getByText(
          "Annonsen anger bara län, inte kommun. Länet innehåller en kommun du valt."
        )
      ).toBeInTheDocument();
      // Den bärande negativa halvan: det var precis den här meningen som sas till
      // en användare som HADE angett en kommun.
      expect(
        screen.queryByText("Du har inte angett någon ort.")
      ).not.toBeInTheDocument();
    });

    it("annons utan yrkesgrupp ersätter INTE hela sektionen med en skylt om dig", () => {
      render(
        <JobAdMatchSection
          match={detail({
            grade: null,
            ssykOverlap: registerRow("NotAssessed", [], [], "AdSilent"),
          })}
        />
      );
      // Nedbrytningen finns kvar och Yrke-raden namnger den tysta sidan.
      expect(screen.getByText("Annonsen anger inget yrke.")).toBeInTheDocument();
      expect(screen.getByText("Kompetenser")).toBeInTheDocument();
      // Skylten (och dess CTA till en inställning användaren redan fyllt i) är borta.
      // Andra meningen är unik för skylten — radens egen fras delar bara den första.
      expect(
        screen.queryByText(/Ställ in det för att se hur väl/)
      ).not.toBeInTheDocument();
      expect(
        screen.queryByRole("link", { name: "Ställ in matchning" })
      ).not.toBeInTheDocument();
    });

    it("och skylten tänds fortfarande när det verkligen är DU som inte angett yrke", () => {
      // Motpolen. Utan den mäter testet ovan inte skillnaden mellan de två
      // orsakerna, bara att skylten kan vara borta.
      render(
        <JobAdMatchSection
          match={detail({
            grade: null,
            ssykOverlap: registerRow("NotAssessed", [], [], "PreferenceUnstated"),
          })}
        />
      );
      expect(
        screen.getByText(/Ställ in det för att se hur väl/)
      ).toBeInTheDocument();
      expect(screen.queryByText("Kompetenser")).not.toBeInTheDocument();
    });

    it("en dimension utan orsak renderar sitt bevis som förut", () => {
      // Falsifieraren för hela blocket: skickar servern ingen orsak ska ingen
      // orsaks-mening synas, och den vanliga bevisformen ska stå kvar.
      render(
        <JobAdMatchSection
          match={detail({
            regionFit: registerRow("NoMatch", [], ["Stockholms län"]),
          })}
        />
      );
      expect(
        screen.getByText("Annonsen efterfrågar även: Stockholms län")
      ).toBeInTheDocument();
      expect(
        screen.queryByText("Annonsen anger varken län eller kommun.")
      ).not.toBeInTheDocument();
    });
  });

  // #1598 — ett concept-id taxonomi-snapshoten tappat. Raden CITERAR något; den
  // får aldrig renderas som en rad som citerade ingenting, och id:t får aldrig
  // renderas alls.
  describe("onämnbara register-koncept (#1598)", () => {
    it("en region annonsen anger men vi inte kan namnge RÄKNAS, och id:t syns aldrig", () => {
      render(
        <JobAdMatchSection
          match={detail({ regionFit: registerRow("NoMatch", [], [null]) })}
        />
      );
      expect(
        screen.getByText("Annonsen anger en ort som saknas i vårt register.")
      ).toBeInTheDocument();
      // Den bärande negativa halvan: interpolerar någon in id:t igen faller detta.
      expect(screen.queryByText(/LOST_missing_0/)).toBeNull();
    });

    it("och den påstår INTE att annonsen saknar region (defekt (a))", () => {
      // Raden ser tom ut här — de NAMNGIVNA listorna ÄR tomma — men servern skickade
      // ingen orsak, för annonsen angav en region. Meningen får därför inte synas.
      render(
        <JobAdMatchSection
          match={detail({ regionFit: registerRow("NoMatch", [], [null]) })}
        />
      );
      expect(
        screen.queryByText("Annonsen anger varken län eller kommun.")
      ).not.toBeInTheDocument();
    });

    it("en tyst annons påstår fortfarande att den är tyst (#552 oförändrat)", () => {
      // Motpolen: servern säger AdSilent → meningen SKA renderas. Utan detta mäter
      // testet ovan inte skillnaden mellan de två tillstånden.
      render(
        <JobAdMatchSection
          match={detail({
            regionFit: registerRow("NoMatch", [], [], "AdSilent"),
          })}
        />
      );
      expect(
        screen.getByText("Annonsen anger varken län eller kommun.")
      ).toBeInTheDocument();
      expect(
        screen.queryByText(/saknas i vårt register/)
      ).not.toBeInTheDocument();
    });

    it("blandad lista: det namngivna visas OCH det onämnbara räknas", () => {
      // Formen som avgjorde wire-valet: porten är item-nycklad, så en dimension
      // kan bära ett namngivbart och ett driftat koncept samtidigt. Den droppande
      // versionen underräknade tyst — två saker annonsen ber om blev en.
      render(
        <JobAdMatchSection
          match={detail({
            regionFit: registerRow("NoMatch", [], ["Stockholms län", null]),
          })}
        />
      );
      expect(
        screen.getByText("Annonsen efterfrågar även: Stockholms län")
      ).toBeInTheDocument();
      expect(
        screen.getByText("Annonsen anger en ort som saknas i vårt register.")
      ).toBeInTheDocument();
    });

    it("räknar plural — och två är producerbart bara på Region-raden", () => {
      // `ScoreOrtUnion` bygger `new List<string>(2)` och kan lägga BÅDE annonsens län
      // och dess kommun i samma lista (`MatchScorer.cs`), så två onämnbara orter är ett
      // tillstånd produktionen faktiskt producerar. `ScoreSsykMembership` emitterar
      // `[adValue]` — ETT id — så samma pin på Yrke-raden hade vilat på ett tillstånd
      // som inte finns (AGENTS.md §5 `Tests:`). Yrke-raden pinnas därför vid ett.
      render(
        <JobAdMatchSection
          match={detail({
            grade: "Basic",
            regionFit: registerRow("NoMatch", [], [null, null]),
          })}
        />
      );
      expect(
        screen.getByText("Annonsen anger 2 orter som saknas i vårt register.")
      ).toBeInTheDocument();
    });

    it("räknas även när granularitets-kartan finns (den slukar inte posten)", () => {
      render(
        <JobAdMatchSection
          match={detail({ regionFit: registerRow("Match", [null]) })}
          ortGranularityByConceptId={granularity}
        />
      );
      expect(
        screen.getByText("Annonsen anger en ort som saknas i vårt register.")
      ).toBeInTheDocument();
      expect(screen.queryByText(/Län som matchar/)).not.toBeInTheDocument();
    });

    it("räknaren hamnar på RÄTT rad — båda register-raderna samtidigt, olika antal", () => {
      // Varje annan assertion i blocket är dokument-scopad, så en förväxling mellan de
      // två radernas räknare skulle passera dem alla: texten finns i dokumentet oavsett
      // vilken rad som bär den (`dotnet-architect` 2026-08-31). Olika antal per rad, och
      // scopat till raden, gör förväxlingen synlig.
      const { container } = render(
        <JobAdMatchSection
          match={detail({
            grade: "Basic",
            ssykOverlap: registerRow("NoMatch", [], [null]),
            regionFit: registerRow("NoMatch", [], [null, null]),
          })}
        />
      );
      const rowFor = (label: string) => {
        const cell = within(container as HTMLElement).getByText(label);
        return cell.closest(".jp-modal__matchrow") as HTMLElement;
      };
      // Ett på Yrke, två på Region — båda producerbara per sin egen scorer-gren, och
      // olika, så en förväxling mellan radernas räknare inte kan passera.
      expect(
        within(rowFor("Yrke")).getByText(
          "Annonsen anger ett yrke som saknas i vårt register."
        )
      ).toBeInTheDocument();
      expect(
        within(rowFor("Region")).getByText(
          "Annonsen anger 2 orter som saknas i vårt register."
        )
      ).toBeInTheDocument();
    });

    it("renderas på engelska under locale en", () => {
      rawRender(
        <NextIntlClientProvider
          locale="en"
          messages={enMessages}
          timeZone="Europe/Stockholm"
        >
          <JobAdMatchSection
            match={detail({ regionFit: registerRow("NoMatch", [], [null]) })}
          />
        </NextIntlClientProvider>
      );
      expect(
        screen.getByText(
          "The ad states a location that is missing from our register."
        )
      ).toBeInTheDocument();
    });
  });
});
