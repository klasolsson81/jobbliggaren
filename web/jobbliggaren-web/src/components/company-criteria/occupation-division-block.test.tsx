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

// The share's "27 %" is one token in sv (NBSP before the sign, DESIGN.md §8). An accessible name
// keeps the byte, so role queries spell it; `getByText` normalises whitespace, so text queries use a
// plain space and one raw `textContent` read pins the byte.
const NBSP = " ";

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
      // 2249 - 615 - 375 - 395: the ads in huvudgrupper under the share cut, which the surface must
      // name — without this line the four numbers above read as the whole.
      belowThresholdAdCount: 864,
      belowThresholdSharePercent: 38,
      withoutDivisionAdCount: 395,
      withoutDivisionSharePercent: 18,
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
      belowThresholdAdCount: 2161,
      belowThresholdSharePercent: 65,
      // 10 / 3317 rounds to 0 %: the one share that must never print as a zero.
      withoutDivisionAdCount: 10,
      withoutDivisionSharePercent: 0,
      profiledAt: "2026-09-14T03:35:00+00:00",
    },
    {
      occupationGroupConceptId: "6zAR_EHM_Kwj",
      label: "Övriga specialistsjuksköterskor",
      matchedOn: "Medicinskt ansvarig sjuksköterska",
      state: "tooFewAds",
      totalAds: 12,
      divisions: null,
      belowThresholdAdCount: null,
      belowThresholdSharePercent: null,
      withoutDivisionAdCount: null,
      withoutDivisionSharePercent: null,
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
  it("says the word is an occupation, lists the divisions with share and count, and accounts for the rest", () => {
    const { container } = renderBlock(PROFILED);
    expect(
      screen.getByText("Mjukvaru- och systemutvecklare m.fl. är ett yrke, inte en bransch."),
    ).toBeInTheDocument();
    expect(
      screen.getByText("I de 2 249 annonser vi sett för yrket finns arbetsgivarna i:"),
    ).toBeInTheDocument();
    // Row names: code first (the level cue), the tree's sentence-cased name, then the share.
    expect(
      screen.getByRole("checkbox", {
        name: `78 Arbetsförmedling, bemanning, 27${NBSP}% (615 annonser)`,
      }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", {
        name: `62 Dataprogrammering, datakonsultverksamhet, 17${NBSP}% (375 annonser)`,
      }),
    ).toBeInTheDocument();
    expect(screen.getAllByRole("checkbox")).toHaveLength(2);
    // The rest of the denominator, as plain lines — never checkboxes.
    expect(
      screen.getByText(`Övriga branscher, var för sig för små att visa: 38 % (864 annonser)`),
    ).toBeInTheDocument();
    expect(
      screen.getByText(`Arbetsgivare utan bransch i registret: 18 % (395 annonser)`),
    ).toBeInTheDocument();
    // The byte itself: the number and the sign never break apart.
    expect(
      screen.getByText(`Arbetsgivare utan bransch i registret: 18 % (395 annonser)`).textContent,
    ).toContain(`18${NBSP}%`);
    // Positive control for the "no empty box" test below: with rows, the bordered box IS drawn.
    expect(container.querySelector(".rounded-md.border")).not.toBeNull();
  });

  it("cites the match last, so the heading and the intro read as one paragraph", () => {
    renderBlock(PROFILED);
    const group = screen.getByRole("group");
    expect(group.lastElementChild).toHaveTextContent("träff på Systemutvecklare/Programmerare");
  });

  it("ticks a division exactly as the tree would: onToggle with the division's leaf codes", async () => {
    const { onToggle } = renderBlock(PROFILED);
    const user = userEvent.setup();
    await user.click(
      screen.getByRole("checkbox", {
        name: `62 Dataprogrammering, datakonsultverksamhet, 17${NBSP}% (375 annonser)`,
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
        name: `62 Dataprogrammering, datakonsultverksamhet, 17${NBSP}% (375 annonser)`,
      }),
    ).toHaveAttribute("aria-checked", "mixed");
  });

  it("leaves the below-threshold line out when nothing sits under the cut", () => {
    renderBlock({
      ...PROFILED,
      occupations: [
        {
          ...PROFILED.occupations[0]!,
          belowThresholdAdCount: 0,
          belowThresholdSharePercent: 0,
        },
      ],
    });
    expect(screen.queryByText(/Övriga branscher/)).not.toBeInTheDocument();
    expect(screen.getByText(/Arbetsgivare utan bransch i registret/)).toBeInTheDocument();
  });

  it("refuses honestly under the floor — a count, never a zero list", () => {
    renderBlock({
      word: "djursjukskötare",
      occupations: [{ ...TWO.occupations[1]!, label: "Djursjukskötare m.fl." }],
    });
    expect(
      screen.getByText("För få annonser (12) för att säga var arbetsgivarna finns."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
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
          belowThresholdAdCount: null,
          belowThresholdSharePercent: null,
          withoutDivisionAdCount: null,
          withoutDivisionSharePercent: null,
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
  it("lists a group whose profile is absent with the short status, never a number", () => {
    renderBlock({
      ...TWO,
      occupations: [
        ...TWO.occupations,
        {
          occupationGroupConceptId: "rev",
          label: "Revisorer m.fl.",
          matchedOn: "Revisor",
          state: "notProfiled",
          totalAds: null,
          divisions: null,
          belowThresholdAdCount: null,
          belowThresholdSharePercent: null,
          withoutDivisionAdCount: null,
          withoutDivisionSharePercent: null,
          profiledAt: null,
        },
      ],
    });
    expect(screen.getByRole("button", { name: /Revisorer m\.fl\./ })).toHaveTextContent(
      "fördelningen kan inte visas",
    );
  });

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

  it("renders the chosen group's map once chosen, moves focus to its heading, and offers a way back", async () => {
    renderBlock(TWO);
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /Grundutbildade sjuksköterskor/ }));

    const heading = screen.getByText("Grundutbildade sjuksköterskor är ett yrke, inte en bransch.");
    expect(heading).toBeInTheDocument();
    // The activated button unmounted with its view; focus lands on the group's name, not <body>.
    expect(heading).toHaveFocus();
    // The thousands group is a non-breaking space in sv (CLAUDE.md §10), which the role-name matcher
    // does not normalise — hence the regex.
    expect(
      screen.getByRole("checkbox", {
        name: /^78 Arbetsförmedling, bemanning, 35 % \(1.146 annonser\)$/,
      }),
    ).toBeInTheDocument();

    // A step back destroys nothing, so it does not wear the clear action's danger colour.
    const back = screen.getByRole("button", { name: "Byt yrke" });
    expect(back).toHaveClass("jp-textaction");
    expect(back).not.toHaveClass("jp-clearlink");
    await user.click(back);
    const question = screen.getByText("Vilket yrke menar du?");
    expect(question).toHaveFocus();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });

  it("prints a rounded-zero share as 'under 1 %' beside its count, never as a zero", async () => {
    renderBlock(TWO);
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /Grundutbildade sjuksköterskor/ }));
    expect(
      screen.getByText(`Arbetsgivare utan bransch i registret: under 1 % (10 annonser)`),
    ).toBeInTheDocument();
    expect(screen.queryByText(/0 % \(10 annonser\)/)).not.toBeInTheDocument();
  });

  it("keeps a division the reference tree does not carry out of the list, and draws no empty box", () => {
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
    // Scoped to the group: the confirm step's choice buttons wear the same classes, and this
    // fixture has one occupation so none render — the scope keeps that true under any fixture.
    // The positive control (box drawn when rows survive) sits in the first test above.
    expect(group.querySelector(".rounded-md.border")).toBeNull();
    // The plain lines still answer the intro's colon.
    expect(within(group).getByText(/Arbetsgivare utan bransch i registret/)).toBeInTheDocument();
  });
});
