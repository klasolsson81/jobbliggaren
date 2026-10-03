import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { render as rawRender } from "@testing-library/react/pure";
import userEvent from "@testing-library/user-event";
import { NextIntlClientProvider } from "next-intl";
import enMessages from "../../../messages/en";
import { JobAdMatchSection } from "./job-ad-match-section";
import type {
  JobAdMatchDetail,
  MatchCause,
  MatchCodedDimensionDetail,
  MatchConceptGroup,
  MatchDimensionDetail,
  MatchRegisterDimensionDetail,
  MatchSkillDimensionDetail,
  MatchVerdict,
} from "@/lib/dto/job-ad-match";

// The job card's match section (#1963). The rules — which rows show, how skills group and count —
// are unit-tested in src/lib/job-ads/match-checklist.test.ts; these tests read what is rendered.
// Fixtures follow the producers in src/Jobbliggaren.Infrastructure/Matching/MatchScorer.cs, graded
// by MatchGradeCalculator (no grade unless Yrke is Match; a stated Ort or employment type the ad
// contradicts floors the grade at Basic).

type Entry = [conceptId: string, label: string | null];

function register(
  verdict: MatchVerdict,
  matched: Entry[] = [],
  missing: Entry[] = [],
  cause: MatchCause | null = null,
): MatchRegisterDimensionDetail {
  const map = (entries: Entry[]) => entries.map(([conceptId, label]) => ({ conceptId, label }));
  return { verdict, matched: map(matched), missing: map(missing), cause };
}

// "kpPX_CNN_gDU" is Tillsvidareanställning in the klass2 taxonomy; the catalogue names it (#1537).
function coded(
  verdict: MatchVerdict,
  matchedConceptIds: string[] = [],
  missingConceptIds: string[] = [],
  cause: MatchCause | null = null,
): MatchCodedDimensionDetail {
  return { verdict, matchedConceptIds, missingConceptIds, cause };
}

function title(verdict: MatchVerdict): MatchDimensionDetail {
  return { verdict, matched: [], missing: [] };
}

function group(display: string, ...members: Array<[string, string]>): MatchConceptGroup {
  return { display, members: members.map(([conceptId, member]) => ({ conceptId, display: member })) };
}

function skill(
  verdict: MatchVerdict,
  matched: MatchConceptGroup[] = [],
  missing: MatchConceptGroup[] = [],
): MatchSkillDimensionDetail {
  const displays = (groups: MatchConceptGroup[]) => groups.flatMap((g) => g.members.map((m) => m.display));
  return { verdict, matched: displays(matched), missing: displays(missing), conceptEvidence: { matched, missing } };
}

function detail(over: Partial<JobAdMatchDetail> = {}): JobAdMatchDetail {
  return {
    grade: "Top",
    ssykOverlap: register("Match", [["DJh5_yyF_hEM", "Mjukvaru- och systemutvecklare m.fl."]]),
    titleSimilarity: title("NotAssessed"),
    regionFit: register("Match", [["PVZL_BQT_XtL", "Göteborg"]]),
    employmentFit: coded("Match", ["kpPX_CNN_gDU"]),
    skillOverlap: skill(
      "Partial",
      [group("C#", ["rPUY_2rX_2yN", "C#"], ["jBKc_5Yx_Y6T", "C#, programmeringsspråk"])],
      [group("TypeScript", ["ts_1", "TypeScript"])],
    ),
    mustHaveCoverage: skill("Vacuous"),
    niceToHaveCoverage: skill("Vacuous"),
    ...over,
  };
}

const rowFor = (label: string) => {
  const row = screen.getByText(label, { selector: "span" }).closest("li");
  if (row === null) throw new Error(`no row for ${label}`);
  return row;
};

describe("JobAdMatchSection — the dimension rows (#1963)", () => {
  it("names its region Matchning and shows the grade chip when there is a grade", () => {
    render(<JobAdMatchSection match={detail()} />);
    const region = screen.getByRole("region", { name: "Matchning" });
    expect(within(region).getByText("Toppmatch")).toBeInTheDocument();
  });

  it("renders each assessed dimension with its value and status word", () => {
    render(<JobAdMatchSection match={detail()} />);
    expect(rowFor("Yrke")).toHaveTextContent("Mjukvaru- och systemutvecklare m.fl.Matchar");
    expect(rowFor("Ort")).toHaveTextContent("GöteborgMatchar");
    expect(rowFor("Anställningsform")).toHaveTextContent(
      "Tillsvidareanställning (inkl. eventuell provanställning)Matchar",
    );
    expect(rowFor("Ort")).toHaveAttribute("data-tone", "match");
  });

  it("does not render a dimension without an assessment (no CV role → no Titel row)", () => {
    render(<JobAdMatchSection match={detail()} />);
    expect(screen.queryByText("Titel")).not.toBeInTheDocument();
    expect(screen.queryByText("Ej bedömt")).not.toBeInTheDocument();
  });

  it("words a contradicted Ort in the warning tone with the ad's place as its value", () => {
    render(
      <JobAdMatchSection
        match={detail({ grade: "Basic", regionFit: register("NoMatch", [], [["zdoY_6u5_Krt", "Trollhättan"]]) })}
      />,
    );
    const ort = rowFor("Ort");
    expect(ort).toHaveTextContent("TrollhättanMatchar inte");
    expect(ort).toHaveAttribute("data-tone", "warn");
  });

  it("keeps an ad-silent Ort row, its reason as the value (Klas 2026-10-03)", () => {
    render(<JobAdMatchSection match={detail({ grade: "Basic", regionFit: register("NoMatch", [], [], "AdSilent") })} />);
    expect(rowFor("Ort")).toHaveTextContent("Annonsen anger varken län eller kommun.Matchar inte");
  });

  it("keeps an ad-silent Anställningsform row the same way", () => {
    render(<JobAdMatchSection match={detail({ grade: "Basic", employmentFit: coded("NoMatch", [], [], "AdSilent") })} />);
    expect(rowFor("Anställningsform")).toHaveTextContent("Annonsen anger ingen anställningsform.Matchar inte");
  });

  it("shows a remote ad's Ort as a match carrying the remote reason (RemoteOverride)", () => {
    render(<JobAdMatchSection match={detail({ regionFit: register("Match", [], [], "RemoteOverride") })} />);
    expect(rowFor("Ort")).toHaveTextContent("Annonsen erbjuder distansarbete.Matchar");
  });

  it("names an ad without an occupation group in one neutral line, with no Yrke row and no chip", () => {
    render(<JobAdMatchSection match={detail({ grade: null, ssykOverlap: register("NotAssessed", [], [], "AdSilent") })} />);
    expect(screen.getByText("Annonsen anger inget yrke.")).toBeInTheDocument();
    expect(screen.queryByText("Yrke")).not.toBeInTheDocument();
    expect(screen.queryByText("Toppmatch")).not.toBeInTheDocument();
  });

  it("words the Yrke row Liknande yrke under a Related grade, beside the Relaterat yrke chip", () => {
    render(<JobAdMatchSection match={detail({ grade: "Related" })} />);
    expect(screen.getByText("Relaterat yrke")).toBeInTheDocument();
    expect(rowFor("Yrke")).toHaveTextContent("Liknande yrke");
    expect(rowFor("Yrke")).toHaveAttribute("data-tone", "warn");
  });

  it.each([
    ["Match", "Din roll stämmer med annonsens titel.", "Matchar"],
    ["Partial", "Din roll stämmer delvis med annonsens titel.", "Delvis"],
    ["NoMatch", "Din roll skiljer sig från annonsens titel.", "Matchar inte"],
  ] as const)("renders a %s title (ScoreTitle with a CV role) as its summary", (verdict, summary, word) => {
    render(<JobAdMatchSection match={detail({ titleSimilarity: title(verdict) })} />);
    expect(rowFor("Titel")).toHaveTextContent(`${summary}${word}`);
  });

  it("counts register entries it cannot name, and never shows their ids (#1598)", () => {
    render(
      <JobAdMatchSection
        match={detail({
          grade: null,
          ssykOverlap: register("NoMatch", [], [["kTH4_ZnA_xxx", "Lagerarbetare"], ["LOST_1", null]]),
        })}
      />,
    );
    const yrke = rowFor("Yrke");
    expect(yrke).toHaveTextContent("Lagerarbetare");
    expect(within(yrke).getByText("Annonsen anger ett yrke som saknas i vårt register.")).toBeInTheDocument();
    expect(yrke).not.toHaveTextContent("LOST_1");
  });

  it("orders Ort names municipality first, then county", () => {
    render(
      <JobAdMatchSection
        match={detail({
          regionFit: register("Match", [["zdoY_6u5_Krt", "Västra Götalands län"], ["PVZL_BQT_XtL", "Göteborg"]]),
        })}
        ortGranularityByConceptId={{ PVZL_BQT_XtL: "municipality", zdoY_6u5_Krt: "region" }}
      />,
    );
    expect(rowFor("Ort")).toHaveTextContent("Göteborg, Västra Götalands län");
  });
});

describe("JobAdMatchSection — the no-occupation notice", () => {
  it("replaces the section with the notice and the card's only link to the matching settings", () => {
    render(
      <JobAdMatchSection
        match={detail({
          grade: null,
          ssykOverlap: register("NotAssessed", [], [], "PreferenceUnstated"),
          skillOverlap: skill("NotAssessed"),
          mustHaveCoverage: skill("NotAssessed"),
          niceToHaveCoverage: skill("NotAssessed"),
        })}
      />,
    );
    expect(screen.getByText("Du har inte angett vilka yrken du söker inom.")).toBeInTheDocument();
    const link = screen.getByRole("link", { name: "Gå till Ställ in matchning" });
    expect(link).toHaveAttribute("href", "/mina-sidor");
    expect(screen.queryByText("Kompetenser")).not.toBeInTheDocument();
    expect(screen.queryByText("Ort")).not.toBeInTheDocument();
  });
});

describe("JobAdMatchSection — the skills box", () => {
  const requirements = (): Partial<JobAdMatchDetail> => ({
    grade: "Basic",
    mustHaveCoverage: skill(
      "Partial",
      [group("C, programmeringsspråk", ["o8wR_57f_jv9", "C, programmeringsspråk"])],
      [group("VHDL, programmeringsspråk", ["vhdl_1", "VHDL, programmeringsspråk"])],
    ),
    niceToHaveCoverage: skill(
      "Partial",
      [group("C++", ["cpp_esco", "C++"])],
      [group("Realtidssystem", ["rt_1", "Realtidssystem"])],
    ),
    skillOverlap: skill(
      "Partial",
      [
        group("C, programmeringsspråk", ["o8wR_57f_jv9", "C, programmeringsspråk"]),
        group("C#", ["rPUY_2rX_2yN", "C#"], ["jBKc_5Yx_Y6T", "C#, programmeringsspråk"]),
        group("C++", ["cpp_esco", "C++"]),
      ],
      [
        group("Realtidssystem", ["rt_1", "Realtidssystem"]),
        group("VHDL, programmeringsspråk", ["vhdl_1", "VHDL, programmeringsspråk"]),
        group("Teknisk fysik", ["tf_1", "Teknisk fysik"]),
      ],
    ),
  });

  it("names each group's list and counts distinct skills", () => {
    render(<JobAdMatchSection match={detail(requirements())} />);
    expect(screen.getByText("3 matchar · 3 finns inte i din profil")).toBeInTheDocument();
    for (const name of ["Obligatoriska krav", "Meriterande", "Matchar också din profil", "Finns inte i din profil"]) {
      expect(screen.getByRole("list", { name: `Kompetenser: ${name}` })).toBeInTheDocument();
    }
  });

  it("speaks the status of each requirement chip, since met and missing share one list (WCAG 1.3.1)", () => {
    render(<JobAdMatchSection match={detail(requirements())} />);
    const must = screen.getByRole("list", { name: "Kompetenser: Obligatoriska krav" });
    const [met, unmet] = within(must).getAllByRole("listitem");
    expect(met).toHaveTextContent("Uppfyllt: C, programmeringsspråk");
    expect(met).toHaveAttribute("data-tone", "met");
    expect(unmet).toHaveTextContent("Saknas: VHDL, programmeringsspråk");
    expect(unmet).toHaveAttribute("data-tone", "missingRequired");
    const nice = screen.getByRole("list", { name: "Kompetenser: Meriterande" });
    expect(within(nice).getAllByRole("listitem")[1]).toHaveAttribute("data-tone", "missing");
  });

  it("shows a skill already listed as a requirement once, and the group display only", () => {
    render(<JobAdMatchSection match={detail(requirements())} />);
    const also = screen.getByRole("list", { name: "Kompetenser: Matchar också din profil" });
    expect(within(also).getAllByRole("listitem").map((li) => li.textContent)).toEqual(["C#"]);
    expect(screen.queryByText(/\(C#, programmeringsspråk\)/)).not.toBeInTheDocument();
    const missing = screen.getByRole("list", { name: "Kompetenser: Finns inte i din profil" });
    expect(within(missing).getAllByRole("listitem").map((li) => li.textContent)).toEqual(["Teknisk fysik"]);
  });

  it("keeps the taxonomy's own qualifiers (Klas 2026-10-03)", () => {
    render(
      <JobAdMatchSection
        match={detail({
          skillOverlap: skill("NoMatch", [], [group("Swift (datorprogrammering)", ["swift_esco", "Swift (datorprogrammering)"])]),
        })}
      />,
    );
    expect(screen.getByText("Swift (datorprogrammering)")).toBeInTheDocument();
  });

  it("collapses the missing profile skills past six behind a toggle named by its visible text", async () => {
    const missing = Array.from({ length: 20 }, (_, i) => group(`Kompetens ${i}`, [`k_${i}`, `Kompetens ${i}`]));
    render(<JobAdMatchSection match={detail({ skillOverlap: skill("NoMatch", [], missing) })} />);
    const list = screen.getByRole("list", { name: "Kompetenser: Finns inte i din profil" });
    const items = within(list).getAllByRole("listitem");
    expect(items.filter((item) => item.hasAttribute("data-overflow"))).toHaveLength(14);

    const toggle = screen.getByRole("button", { name: "Visa 14 till" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(toggle).toHaveAttribute("aria-controls", list.id);
    expect(toggle).toHaveAccessibleDescription("Finns inte i din profil");

    await userEvent.setup().click(toggle);
    expect(screen.getByRole("button", { name: "Visa färre" })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText("0 matchar · 20 finns inte i din profil")).toBeInTheDocument();
  });

  it("does not collapse when the toggle would hide only one chip", () => {
    const missing = Array.from({ length: 7 }, (_, i) => group(`Kompetens ${i}`, [`k_${i}`, `Kompetens ${i}`]));
    render(<JobAdMatchSection match={detail({ skillOverlap: skill("NoMatch", [], missing) })} />);
    expect(screen.queryByRole("button", { name: /Visa \d+ till/ })).not.toBeInTheDocument();
  });

  it("states the missing extracted requirements once, inside the box, and points to the ad text", () => {
    render(<JobAdMatchSection match={detail()} />);
    expect(
      screen.getByText("Uppgifter om kompetenskrav saknas i matchningsunderlaget. Läs kraven i annonstexten."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/alla krav/i)).not.toBeInTheDocument();
  });

  it("without confirmed skills says where they are chosen, with no counter and no link (one predicate)", () => {
    render(
      <JobAdMatchSection
        match={detail({
          grade: "Good",
          skillOverlap: skill("NotAssessed"),
          mustHaveCoverage: skill("NotAssessed"),
          niceToHaveCoverage: skill("NotAssessed"),
        })}
      />,
    );
    expect(
      screen.getByText("Välj kompetenser under Matchning på Mina sidor för att se vilka krav som matchar."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/matchar ·/)).not.toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("carries no percentage, ratio or 'x av y' (Goodhart guard, ADR 0076 Decision 4)", () => {
    const { container } = render(<JobAdMatchSection match={detail(requirements())} />);
    expect(container.textContent).not.toMatch(/%|\d+\s*\/\s*\d+|\d+ av \d+/);
  });
});

describe("JobAdMatchSection — English", () => {
  it("renders under the en locale with its own plural", () => {
    rawRender(
      <NextIntlClientProvider locale="en" messages={enMessages}>
        <JobAdMatchSection
          match={detail({ grade: "Basic", regionFit: register("NoMatch", [], [], "AdSilent") })}
        />
      </NextIntlClientProvider>,
    );
    expect(screen.getByText("The ad does not state a region.")).toBeInTheDocument();
    expect(screen.getByText("Does not match")).toBeInTheDocument();
    expect(screen.getByText("1 matches · 1 not in your profile")).toBeInTheDocument();
  });
});
