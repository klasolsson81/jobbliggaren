import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CriterionPicker } from "./criterion-picker";
import { buildSniNodes, flattenCriterionOptions } from "@/lib/company-criteria/criterion-options";
import type { CriterionReference, OccupationDivisions } from "@/lib/dto/company-criteria";
import type { OccupationDivisionsResolver } from "@/lib/company-criteria/resolve-occupation-divisions";

// #1682 — the picker's occupation block through the picker itself: the resolver is a PROP (data,
// not a mode flag), the block renders outside the zero-hits box, ticking goes through the picker's
// own `onToggle`, and the picker keeps its one filter live region.
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
        { code: "78", name: "Arbetsförmedling, bemanning", leaves: [{ code: "78200", name: "Personaluthyrning" }] },
      ],
    },
  ],
  lan: [],
};
const NODES = buildSniNodes(REFERENCE);
const OPTIONS = flattenCriterionOptions(NODES);

const SYSTEMUTVECKLARE: OccupationDivisions = {
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

const SJUKSKOTERSKA: OccupationDivisions = {
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

const resolver: OccupationDivisionsResolver = async (word) => {
  if (word === "systemutvecklare") return SYSTEMUTVECKLARE;
  if (word === "sjuksköterska") return SJUKSKOTERSKA;
  return { word, occupations: [] };
};

// `null` means "no resolver", deliberately: an `undefined` argument would fall back to the default
// parameter and silently pass the resolver in.
function renderPicker(resolve: OccupationDivisionsResolver | null = resolver) {
  const onToggle = vi.fn();
  const view = render(
    <CriterionPicker
      nodes={NODES}
      options={OPTIONS}
      selected={new Set()}
      onToggle={onToggle}
      filterLabel="Sök bransch"
      groupAria="Branscher"
      expandAria={(name) => `Visa branscher i ${name}`}
      collapseAria={(name) => `Dölj branscher i ${name}`}
      selectedCountLabel="0 val"
      optionsUnavailable="Registret kunde inte laddas."
      resolveOccupations={resolve ?? undefined}
    />,
  );
  return { ...view, onToggle };
}

const SLOW = { timeout: 3000 };

describe("CriterionPicker — the occupation block (#1682)", () => {
  it("answers an occupation word that matches no SNI name: the block survives the zero-hits gate", async () => {
    renderPicker();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "systemutvecklare");

    // The filter itself found nothing, and says so in its ONE live region …
    expect(screen.getByRole("status")).toHaveTextContent("Inga träffar");
    // … while the block appears beside it with the measured answer.
    await waitFor(() =>
      expect(
        screen.getByText("Mjukvaru- och systemutvecklare m.fl. är ett yrke, inte en bransch."),
      ).toBeInTheDocument(),
    );
    expect(screen.getAllByRole("status")).toHaveLength(1);
    expect(
      screen.getByRole("checkbox", { name: "78 Arbetsförmedling, bemanning, 27 % (615 annonser)" }),
    ).toBeInTheDocument();
  });

  it("ticks a division through the picker's own onToggle, with the division's leaf codes", async () => {
    const { onToggle } = renderPicker();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "systemutvecklare");
    const row = await screen.findByRole("checkbox", {
      name: "62 Dataprogrammering, datakonsultverksamhet, 17 % (375 annonser)",
    });
    await user.click(row);
    expect(onToggle).toHaveBeenCalledWith(["62100", "62201"]);
  });

  it("asks which occupation is meant, remembers the choice for that word, and forgets it when the word changes", async () => {
    renderPicker();
    const user = userEvent.setup();
    const filter = screen.getByLabelText("Sök bransch");
    // `paste`, not `type`: user-event's keyboard layer does not carry "ö" through jsdom the way the
    // ASCII words above go through, and what this test is about is the word, not the keystrokes.
    await user.click(filter);
    await user.paste("sjuksköterska");
    await screen.findByText("Vilket yrke menar du?", undefined, SLOW);

    await user.click(screen.getByRole("button", { name: /Grundutbildade sjuksköterskor/ }));
    expect(
      screen.getByText("Grundutbildade sjuksköterskor är ett yrke, inte en bransch."),
    ).toBeInTheDocument();

    await user.clear(filter);
    await user.type(filter, "systemutvecklare");
    await screen.findByText(
      "Mjukvaru- och systemutvecklare m.fl. är ett yrke, inte en bransch.",
      undefined,
      SLOW,
    );
    expect(screen.queryByText("Vilket yrke menar du?")).not.toBeInTheDocument();

    // Back to the two-group word: the earlier choice does not carry over.
    await user.clear(filter);
    await user.paste("sjuksköterska");
    await screen.findByText("Vilket yrke menar du?", undefined, SLOW);
  });

  it("renders no block without a resolver (the kommun axis) and none for a word that is no occupation", async () => {
    const { unmount } = renderPicker(null);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "systemutvecklare");
    await new Promise((r) => setTimeout(r, 500));
    expect(screen.queryByRole("group", { name: /är ett yrke/ })).not.toBeInTheDocument();
    unmount();

    renderPicker();
    await user.type(screen.getByLabelText("Sök bransch"), "data");
    await new Promise((r) => setTimeout(r, 500));
    expect(screen.queryByText(/är ett yrke, inte en bransch/)).not.toBeInTheDocument();
    // The SNI rows still render as before.
    expect(
      within(screen.getByRole("group", { name: "Branscher" })).getByRole("checkbox", {
        name: "62100 Dataprogrammering",
      }),
    ).toBeInTheDocument();
  });
});
