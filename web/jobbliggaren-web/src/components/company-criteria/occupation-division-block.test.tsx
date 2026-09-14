import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OccupationDivisionBlock } from "./occupation-division-block";
import { buildSniNodes, flattenCriterionOptions } from "@/lib/company-criteria/criterion-options";
import type { CriterionReference, OccupationDivisions } from "@/lib/dto/company-criteria";

const REFERENCE: CriterionReference = {
  sniVersion: "2025",
  kommunVersion: "2026",
  sni: [
    {
      code: "J",
      name: "Informations- och kommunikationsverksamhet",
      divisions: [
        {
          code: "62",
          name: "Dataprogrammering, datakonsultverksamhet",
          leaves: [
            { code: "62100", name: "Dataprogrammering" },
            { code: "62201", name: "Datakonsultverksamhet" },
          ],
        },
      ],
    },
    {
      code: "N",
      name: "Uthyrning, fastighetsservice, resetjänster",
      divisions: [
        {
          code: "78",
          name: "Arbetsförmedling, bemanning",
          leaves: [{ code: "78200", name: "Personaluthyrning" }],
        },
      ],
    },
  ],
  lan: [],
};
const OPTIONS = flattenCriterionOptions(buildSniNodes(REFERENCE));

const PROFILED: OccupationDivisions = {
  word: "systemutvecklare",
  occupations: [
    {
      occupationGroupConceptId: "DJh5_yyF_hEM",
      label: "Mjukvaru- och systemutvecklare m.fl.",
      matchedOn: "Systemutvecklare/Programmerare",
      state: "profiled",
      totalAds: 2249,
      divisions: [
        { code: "78", adCount: 615, sharePercent: 27 },
        { code: "62", adCount: 375, sharePercent: 17 },
      ],
      notInRegisterAdCount: 395,
      notInRegisterSharePercent: 18,
      profiledAt: "2026-09-14T03:35:00+00:00",
    },
  ],
};

const TWO: OccupationDivisions = {
  word: "sjuksköterska",
  occupations: [
    {
      occupationGroupConceptId: "Z8ci_bBE_tmx",
      label: "Grundutbildade sjuksköterskor",
      matchedOn: "Sjuksköterska, grundutbildad",
      state: "profiled",
      totalAds: 3317,
      divisions: [{ code: "78", adCount: 1146, sharePercent: 35 }],
      notInRegisterAdCount: 10,
      notInRegisterSharePercent: 0,
      profiledAt: "2026-09-14T03:35:00+00:00",
    },
    {
      occupationGroupConceptId: "6zAR_EHM_Kwj",
      label: "Övriga specialistsjuksköterskor",
      matchedOn: "Medicinskt ansvarig sjuksköterska",
      state: "tooFewAds",
      totalAds: 12,
      divisions: null,
      notInRegisterAdCount: null,
      notInRegisterSharePercent: null,
      profiledAt: "2026-09-14T03:35:00+00:00",
    },
  ],
};

function renderBlock(data: OccupationDivisions) {
  const onToggle = vi.fn();
  const view = render(
    <OccupationDivisionBlock data={data} options={OPTIONS} selected={new Set()} onToggle={onToggle} />,
  );
  return { ...view, onToggle };
}

describe("OccupationDivisionBlock — one occupation", () => {
  it("says the word is an occupation, cites the match, and lists the divisions with share and count", () => {
    renderBlock(PROFILED);
    expect(
      screen.getByText("Mjukvaru- och systemutvecklare m.fl. är ett yrke, inte en bransch."),
    ).toBeInTheDocument();
    expect(screen.getByText("träff på Systemutvecklare/Programmerare")).toBeInTheDocument();
    expect(
      screen.getByText("I de 2 249 annonser vi sett för yrket finns arbetsgivarna i:"),
    ).toBeInTheDocument();
    // Row names: code first (the level cue), the tree's sentence-cased name, then the share.
    expect(
      screen.getByRole("checkbox", { name: "78 Arbetsförmedling, bemanning, 27 % (615 annonser)" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", {
        name: "62 Dataprogrammering, datakonsultverksamhet, 17 % (375 annonser)",
      }),
    ).toBeInTheDocument();
    // The not-in-register bucket is a plain line, never a checkbox.
    expect(
      screen.getByText("Arbetsgivare utanför registret: 18 % (395 annonser)"),
    ).toBeInTheDocument();
    expect(screen.getAllByRole("checkbox")).toHaveLength(2);
  });

  it("ticks a division exactly as the tree would: onToggle with the division's leaf codes", async () => {
    const { onToggle } = renderBlock(PROFILED);
    const user = userEvent.setup();
    await user.click(
      screen.getByRole("checkbox", {
        name: "62 Dataprogrammering, datakonsultverksamhet, 17 % (375 annonser)",
      }),
    );
    expect(onToggle).toHaveBeenCalledWith(["62100", "62201"]);
  });

  it("reflects a partially selected division as mixed", () => {
    render(
      <OccupationDivisionBlock
        data={PROFILED}
        options={OPTIONS}
        selected={new Set(["62100"])}
        onToggle={vi.fn()}
      />,
    );
    expect(
      screen.getByRole("checkbox", {
        name: "62 Dataprogrammering, datakonsultverksamhet, 17 % (375 annonser)",
      }),
    ).toHaveAttribute("aria-checked", "mixed");
  });

  it("refuses honestly under the floor — a count, never a zero list", () => {
    const [profiledCandidate] = TWO.occupations;
    renderBlock({
      word: "djursjukskötare",
      occupations: [{ ...TWO.occupations[1]!, label: "Djursjukskötare m.fl." }],
    });
    expect(
      screen.getByText("För få annonser (12) för att säga var arbetsgivarna finns."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(profiledCandidate).toBeDefined();
  });

  it("says it does not know when the profile is absent or over-age", () => {
    renderBlock({
      word: "revisor",
      occupations: [
        {
          occupationGroupConceptId: "rev",
          label: "Revisorer m.fl.",
          matchedOn: "Revisor",
          state: "notProfiled",
          totalAds: null,
          divisions: null,
          notInRegisterAdCount: null,
          notInRegisterSharePercent: null,
          profiledAt: null,
        },
      ],
    });
    expect(screen.getByText("Fördelningen kan inte visas just nu.")).toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });

  it("renders nothing for a word the taxonomy does not know", () => {
    const { container } = renderBlock({ word: "chef", occupations: [] });
    expect(container).toBeEmptyDOMElement();
  });
});

describe("OccupationDivisionBlock — several occupations (the confirm step, ADR 0040)", () => {
  it("asks which occupation is meant and never picks one itself", () => {
    renderBlock(TWO);
    expect(screen.getByText("Vilket yrke menar du?")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Grundutbildade sjuksköterskor/ })).toHaveTextContent(
      "3 317 annonser vi sett",
    );
    expect(screen.getByRole("button", { name: /Övriga specialistsjuksköterskor/ })).toHaveTextContent(
      "för få annonser (12)",
    );
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });

  it("renders the chosen group's map once chosen, with a way back to the choice", async () => {
    renderBlock(TWO);
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /Grundutbildade sjuksköterskor/ }));

    expect(
      screen.getByText("Grundutbildade sjuksköterskor är ett yrke, inte en bransch."),
    ).toBeInTheDocument();
    // The thousands group is a non-breaking space in sv (CLAUDE.md §10), which the role-name matcher
    // does not normalise — hence the regex.
    expect(
      screen.getByRole("checkbox", { name: /^78 Arbetsförmedling, bemanning, 35 % \(1.146 annonser\)$/ }),
    ).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Byt yrke" }));
    expect(screen.getByText("Vilket yrke menar du?")).toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });

  it("keeps a division the reference tree does not carry out of the list", () => {
    renderBlock({
      ...PROFILED,
      occupations: [
        {
          ...PROFILED.occupations[0]!,
          divisions: [{ code: "99", adCount: 5, sharePercent: 50 }],
        },
      ],
    });
    const group = screen.getByRole("group");
    expect(within(group).queryByRole("checkbox")).not.toBeInTheDocument();
  });
});
