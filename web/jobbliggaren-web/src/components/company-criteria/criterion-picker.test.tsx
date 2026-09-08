import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CriterionPicker } from "./criterion-picker";
import {
  buildSniNodes,
  flattenCriterionOptions,
} from "@/lib/company-criteria/criterion-options";
import { toggleGroup } from "@/lib/company-criteria/criterion-selection";
import type { CriterionReference } from "@/lib/dto/company-criteria";

/**
 * The shared picker had NO component test before #999, and #999 changes its filter semantics — which
 * are consumed by the criterion dialog (Branschbevakningar) as well as the new bransch popover. Without
 * these, the three-level filter would ship unguarded on a surface outside the PR's own verification.
 */
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
            { code: "62010", name: "Datakonsultverksamhet" },
            { code: "62020", name: "Systemutveckling" },
          ],
        },
      ],
    },
  ],
  lan: [],
};

const NODES = buildSniNodes(REFERENCE);
const OPTIONS = flattenCriterionOptions(NODES);

function renderPicker(
  props: Partial<React.ComponentProps<typeof CriterionPicker>> = {},
) {
  const onToggle = vi.fn();
  const onClear = vi.fn();
  const view = render(
    <CriterionPicker
      nodes={NODES}
      options={OPTIONS}
      selected={new Set()}
      onToggle={onToggle}
      onClear={onClear}
      filterLabel="Sök bransch"
      filterHint="Skriv för att smalna av listan över branscher."
      groupAria="Branscher"
      // AXIS copy, host-supplied since #1146 — the fixture speaks the SNI axis,
      // matching `filterLabel`/`groupAria` above.
      expandAria={(name) => `Visa branscher i ${name}`}
      collapseAria={(name) => `Dölj branscher i ${name}`}
      selectedCountLabel="0 valda branscher"
      optionsUnavailable="Registret kunde inte laddas."
      {...props}
    />,
  );
  return { ...view, onToggle, onClear };
}

describe("CriterionPicker — the browse view", () => {
  it("renders the tree when the filter is empty, top level only until expanded", () => {
    renderPicker();
    // No code prefix here, deliberately: the tree conveys level by nesting and by each subtree's own
    // `role="group"`, so it needs no textual level cue. Only the FLAT filter view does.
    expect(
      screen.getByRole("checkbox", {
        name: "Informations- och kommunikationsverksamhet",
      }),
    ).toBeInTheDocument();
    // Children stay collapsed — the whole point of a tree over a 944-row dump.
    expect(
      screen.queryByRole("checkbox", { name: "62010 Datakonsultverksamhet" }),
    ).not.toBeInTheDocument();
  });

  it("shows the degraded notice instead of an empty box when the tree is empty", () => {
    renderPicker({ nodes: [], options: [] });
    expect(screen.getByText("Registret kunde inte laddas.")).toBeInTheDocument();
  });

  // #1146 — the expand toggle's accessible name comes from the HOST's axis copy, and
  // nothing pinned that until now. The defect this replaced was a single
  // component-owned string that said "underkategorier" over a län; it was caught by a
  // human reading the two hosts, not by a gate. This is the gate.
  //
  // The name is load-bearing rather than decorative: the toggle's only child is an
  // `aria-hidden` chevron, so without the label the button has NO accessible name at
  // all (WCAG 4.1.2) — `aria-expanded` carries state, never a name.
  it("names the expand toggle with the HOST's axis copy, not a shared string", async () => {
    const user = userEvent.setup();
    renderPicker({
      expandAria: (name) => `Visa kommuner i ${name}`,
      collapseAria: (name) => `Dölj kommuner i ${name}`,
    });

    const toggle = screen.getByRole("button", {
      name: "Visa kommuner i Informations- och kommunikationsverksamhet",
    });
    expect(toggle).toHaveAttribute("aria-expanded", "false");

    // The name follows the STATE, so both halves of the host's contract are exercised.
    await user.click(toggle);
    expect(
      screen.getByRole("button", {
        name: "Dölj kommuner i Informations- och kommunikationsverksamhet",
      }),
    ).toHaveAttribute("aria-expanded", "true");
  });
});

describe("CriterionPicker — the filter view (#999: all three levels)", () => {
  it("matches section, division AND leaf, not leaves only", async () => {
    renderPicker();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "verksamhet");

    for (const name of [
      "J Informations- och kommunikationsverksamhet",
      "62 Dataprogrammering, datakonsultverksamhet",
      "62010 Datakonsultverksamhet",
    ]) {
      expect(screen.getByRole("checkbox", { name })).toBeInTheDocument();
    }
  });

  it("renders the code VISIBLY, not only in the authored name", async () => {
    // The row's name comes from `aria-label`, which is what makes it identical in every environment —
    // but it also decouples the name from the JSX. Delete the visible code span and every name
    // assertion above still passes while the row loses the level cue that is the whole remedy for the
    // filtered view. This is the assertion that notices. (Not a WCAG 2.5.3 break: removing visible
    // text leaves the name a superset of it. The direction that DOES break label-in-name is adding
    // visible text without updating the label, which the component comment states.)
    renderPicker();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "datapro");
    const row = screen.getByRole("checkbox", {
      name: "62 Dataprogrammering, datakonsultverksamhet",
    });
    expect(within(row).getByText("62")).toHaveClass("jp-mono");
  });

  it("toggles a matched parent as its whole expansion, not as itself", async () => {
    const { onToggle } = renderPicker();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "datapro");
    await user.click(
      screen.getByRole("checkbox", {
        name: "62 Dataprogrammering, datakonsultverksamhet",
      }),
    );
    expect(onToggle).toHaveBeenCalledWith(["62010", "62020"]);
  });

  it("renders a partially selected parent as mixed", async () => {
    renderPicker({ selected: new Set(["62010"]) });
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "datapro");

    expect(
      screen.getByRole("checkbox", {
        name: "62 Dataprogrammering, datakonsultverksamhet",
      }),
    ).toHaveAttribute("aria-checked", "mixed");
  });

  it("is keyboard-operable: Space toggles a filtered row", async () => {
    const { onToggle } = renderPicker();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "system");
    screen.getByRole("checkbox", { name: "62020 Systemutveckling" }).focus();
    await user.keyboard(" ");
    expect(onToggle).toHaveBeenCalledWith(["62020"]);
  });

  it("carries the touch-target hook on every row, browse view and filter view alike", async () => {
    // jsdom has no cascade, so this cannot assert 44px — the height is pinned by the rendered
    // measurement in the PR body (38px before, 44px after, at 375 and 768). What it CAN pin is the
    // hook the media query attaches to, which is the part a refactor silently drops.
    renderPicker();
    expect(
      screen.getByRole("checkbox", {
        name: "Informations- och kommunikationsverksamhet",
      }),
    ).toHaveClass("jp-criterionrow");

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "system");
    expect(
      screen.getByRole("checkbox", { name: "62020 Systemutveckling" }),
    ).toHaveClass("jp-criterionrow");
  });

  it("carries EVERY filtering outcome in the one persistent live region", async () => {
    renderPicker();
    const user = userEvent.setup();
    const field = screen.getByLabelText("Sök bransch");

    // The region exists before anything is typed — a live region that mounts with its content
    // already in place is announced unreliably, so the text must change INSIDE a region that was
    // already there. Exactly one such region, in every state.
    expect(screen.getByRole("status")).toHaveTextContent("");

    await user.type(field, "verksamhet");
    expect(screen.getAllByRole("status")).toHaveLength(1);
    expect(screen.getByRole("status")).toHaveTextContent("3 träffar");

    await user.clear(field);
    await user.type(field, "zzz");
    // The empty state is this control's most common failure mode — SNI's vocabulary is not the
    // user's — so a full stop is not an answer; it says how to get back. And it lives in the SAME
    // region rather than a second one inserted beneath.
    expect(screen.getAllByRole("status")).toHaveLength(1);
    expect(screen.getByRole("status")).toHaveTextContent(
      "Inga träffar. Rensa sökfältet för att bläddra i hela listan.",
    );
    // No empty bordered box under a message that already says there is nothing.
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });

  it("keeps the selection counter's region mounted and empty before the first pick", () => {
    // Two things, and the second is why the first matters. The counter is polite, AND its region is
    // rendered before there is anything to announce: gating it on `hasSelection` made the 0→1
    // transition — the first pick — a region that mounts with its content already in place, which is
    // the form the house has ruled unreliable. Mutation (xv) walked through the POPOVER's counter,
    // which is pinned in `foretag-sok-searchbar.test.tsx`; this covers the picker's own, which the
    // criterion dialog renders.
    const { container, rerender } = renderPicker({ selectedCountLabel: "1 vald bransch" });
    // NOT just `p[aria-live]` — the filter-count region above is also a polite <p>, and querying
    // for the first match would assert on it instead, passing whether or not this counter exists.
    const region = container.querySelector("p[aria-live='polite']:not([role])");
    expect(region).not.toBeNull();
    expect(region).toHaveTextContent("");

    rerender(
      <CriterionPicker
        nodes={NODES}
        options={OPTIONS}
        selected={new Set(["62010"])}
        onToggle={vi.fn()}
        filterLabel="Sök bransch"
        groupAria="Branscher"
        expandAria={(name) => `Visa branscher i ${name}`}
        collapseAria={(name) => `Dölj branscher i ${name}`}
        selectedCountLabel="1 vald bransch"
        optionsUnavailable="Registret kunde inte laddas."
      />,
    );
    expect(screen.getByText("1 vald bransch")).toHaveAttribute("aria-live", "polite");
  });

  it("falls back to the tree above the cardinality ceiling, and says why", async () => {
    // The ceiling is on RESULT SIZE, not query length: the control this replaced gated on two
    // characters, which blocks `c` (507 matches) while admitting `er` (665). Below the ceiling the
    // TREE renders, so the fallback is never a dead end.
    const wide: CriterionReference = {
      sniVersion: "2025",
      kommunVersion: "2026",
      sni: [
        {
          code: "A",
          name: "Bred avdelning",
          divisions: [
            {
              code: "01",
              name: "Bred huvudgrupp",
              leaves: Array.from({ length: 400 }, (_, i) => ({
                code: `0${1000 + i}`,
                name: `Bred detaljgrupp ${i}`,
              })),
            },
          ],
        },
      ],
      lan: [],
    };
    const nodes = buildSniNodes(wide);
    renderPicker({ nodes, options: flattenCriterionOptions(nodes) });
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "bred");

    expect(screen.getByRole("status")).toHaveTextContent("402 träffar");
    expect(screen.getByRole("status")).toHaveTextContent("Skriv mer");
    // The tree, not the 402-row list: the top-level node is present (tree rows carry no code prefix)
    // and its children are collapsed.
    expect(
      screen.getByRole("checkbox", { name: "Bred avdelning" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("checkbox", { name: "Bred huvudgrupp" }),
    ).not.toBeInTheDocument();
  });
});

describe("CriterionPicker — optional heading and help (#999)", () => {
  it("omits both when the caller does not pass them (a popover names the axis already)", () => {
    renderPicker();
    expect(screen.queryByRole("heading")).not.toBeInTheDocument();
  });

  it("renders both for the dialog, which stacks two pickers and needs them", () => {
    renderPicker({ heading: "Branscher", help: "Välj en eller flera branscher." });
    expect(
      screen.getByRole("heading", { name: "Branscher" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Välj en eller flera branscher."),
    ).toBeInTheDocument();
  });

  it("renders the POPOVER configuration — all four omitted — without dangling wiring", () => {
    // `renderPicker` passes the dialog's configuration, so the popover's (every optional prop absent)
    // was only ever exercised indirectly through the searchbar tests. The one thing that can go wrong
    // silently is `aria-describedby` pointing at a hint id that is no longer rendered.
    render(
      <CriterionPicker
        nodes={NODES}
        options={OPTIONS}
        selected={new Set()}
        onToggle={vi.fn()}
        filterLabel="Sök bransch"
        groupAria="Branscher"
        expandAria={(name) => `Visa branscher i ${name}`}
        collapseAria={(name) => `Dölj branscher i ${name}`}
        optionsUnavailable="Registret kunde inte laddas."
      />,
    );
    const field = screen.getByLabelText("Sök bransch");
    expect(field).not.toHaveAttribute("aria-describedby");
    expect(screen.queryByRole("heading")).not.toBeInTheDocument();
  });

  it("omits Rensa when no onClear is given, even with a selection to clear", () => {
    // With an EMPTY selection the inline header never renders at all, so asserting the absence of
    // "Rensa" there would pass whether or not `onClear` was supplied — a test that cannot fail of
    // what it names. A non-empty selection is what makes the absence mean something.
    render(
      <CriterionPicker
        nodes={NODES}
        options={OPTIONS}
        selected={new Set(["62010"])}
        onToggle={vi.fn()}
        filterLabel="Sök bransch"
        groupAria="Branscher"
        expandAria={(name) => `Visa branscher i ${name}`}
        collapseAria={(name) => `Dölj branscher i ${name}`}
        optionsUnavailable="Registret kunde inte laddas."
      />,
    );
    expect(screen.queryByRole("button", { name: "Rensa val" })).not.toBeInTheDocument();
  });

  it("says nothing about matches when there is no catalogue to match against", async () => {
    // A degraded reference shows "Registret kunde inte laddas" in the box. Announcing "0 träffar"
    // over it would claim a search ran against a catalogue that is not there.
    renderPicker({ nodes: [], options: [] });
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "data");
    // The count line is the live region and it stays silent; the degraded notice is what shows.
    expect(screen.getByRole("status")).toHaveTextContent("");
    expect(screen.queryByText(/träffar/)).not.toBeInTheDocument();
    expect(screen.getByText("Registret kunde inte laddas.")).toBeInTheDocument();
  });

  it("still offers Rensa without a heading, once something is selected", async () => {
    const { onClear } = renderPicker({ selected: new Set(["62010"]) });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Rensa val" }));
    expect(onClear).toHaveBeenCalled();
  });
});

describe("CriterionPicker — the selection contract", () => {
  it("toggleGroup over a filtered parent adds the whole expansion (composition check)", () => {
    // The picker hands `leafCodes` to the caller's toggle; the caller runs `toggleGroup`. Pinning the
    // composition keeps the two halves from drifting into "parent code stored as itself".
    expect(toggleGroup(new Set(), ["62010", "62020"])).toEqual(
      new Set(["62010", "62020"]),
    );
    expect(toggleGroup(new Set(["62010", "62020"]), ["62010", "62020"])).toEqual(
      new Set(),
    );
  });
});

/**
 * #1115 — the alias surface. SNI classifies ACTIVITIES, so the words users hold ("systemutveckl")
 * matched 0 of the real 944 node names. These rows exist so a concept the user has a different word
 * for is reachable, and every one of them pins a property that keeps the layer a LOOKUP AID rather
 * than the SNI↔SSYK crosswalk #560 bind 4 forbids: the SNI concept stays the row, the alias is shown
 * as the search word that led to it, and the emitted selection is still the node's own leaf codes.
 */
const ALIASED_REFERENCE: CriterionReference = {
  ...REFERENCE,
  sni: [
    {
      code: "J",
      name: "Informations- och kommunikationsverksamhet",
      // A section-level alias. The wire and the projection both carry one, so leaving it unpinned
      // let the whole level be cut without a red test.
      aliases: ["it-bransch"],
      divisions: [
        {
          code: "62",
          name: "Dataprogrammering, datakonsultverksamhet",
          // A DIVISION-level alias, and this is the level that matters most: 7 of the 8 authored
          // term-instances in the shipped asset sit on a division (`mjukvara`, `apputveckling`,
          // `byggare`, `rörmokare`, `snickare`). With aliases pinned on a leaf only, deleting
          // `aliases: division.aliases` from buildSniNodes left all 196 tests green — measured.
          aliases: ["mjukvara"],
          leaves: [
            {
              code: "62100",
              name: "Dataprogrammering",
              // Shaped like the real SCB entries under 62201: a classified sentence whose everyday
              // synonym sits in a parenthesis, which is what the rendering has to dig out.
              aliases: [
                "Agil systemutveckling",
                "Datakonsultverksamhet, (IT-konsult, ITkonsult), systemdesign",
              ],
            },
            { code: "62201", name: "Datakonsultverksamhet" },
          ],
        },
      ],
    },
  ],
};

const ALIASED_NODES = buildSniNodes(ALIASED_REFERENCE);
const ALIASED_OPTIONS = flattenCriterionOptions(ALIASED_NODES);

function renderAliased(
  props: Partial<React.ComponentProps<typeof CriterionPicker>> = {},
) {
  return renderPicker({
    nodes: ALIASED_NODES,
    options: ALIASED_OPTIONS,
    ...props,
  });
}

describe("CriterionPicker — the alias surface (#1115)", () => {
  it("surfaces a row whose NAME does not contain the query, via its alias", async () => {
    renderAliased();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "agil");

    // "Datakonsultverksamhet" contains no "agil" — the row is here only because of the alias.
    expect(
      screen.getByRole("checkbox", { name: /62100 Dataprogrammering/ }),
    ).toBeInTheDocument();
  });

  it("shows WHY the row matched, and puts it in the accessible name too", async () => {
    renderAliased();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "agil");

    // Visible: a row that contains none of the typed characters would otherwise be a result with no
    // reason. The row's aria-label is author-set, so anything visible added must be added there too
    // or the accessible name stops containing the visible text (WCAG 2.5.3).
    expect(screen.getByText("träff på Agil systemutveckling")).toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", {
        name: "62100 Dataprogrammering, träff på Agil systemutveckling",
      }),
    ).toBeInTheDocument();
  });

  it("does NOT annotate a row the query already explains", async () => {
    renderAliased();
    const user = userEvent.setup();
    // "data" is deliberately a query the row matches BOTH ways: its name is "Dataprogrammering" and
    // its alias "Datakonsultverksamhet, (IT-konsult, ITkonsult), systemdesign" contains it too. A
    // rule that annotated whenever an alias happened to match would fire here; only one that
    // annotates on the reason the row APPEARED stays silent.
    await user.type(screen.getByLabelText("Sök bransch"), "data");

    expect(
      screen.getByRole("checkbox", { name: "62100 Dataprogrammering" }),
    ).toBeInTheDocument();
    expect(screen.queryByText(/^träff på /)).not.toBeInTheDocument();
  });

  it("keeps aliases silent below the word threshold, while names still match", async () => {
    renderAliased();
    const user = userEvent.setup();
    // "ag" is a fragment, not a word. Were aliases to answer it, `st` went 275 -> 318 against the
    // real catalogue and crossed MAX_FILTER_MATCHES — the regression ALIAS_MIN_QUERY prevents.
    await user.type(screen.getByLabelText("Sök bransch"), "ag");

    expect(screen.queryByText(/^träff på /)).not.toBeInTheDocument();
    expect(
      screen.queryByRole("checkbox", { name: /Dataprogrammering/ }),
    ).not.toBeInTheDocument();
    // The name surface is untouched by the threshold: "da" still matches at two characters.
    await user.clear(screen.getByLabelText("Sök bransch"));
    await user.type(screen.getByLabelText("Sök bransch"), "da");
    expect(
      screen.getByRole("checkbox", { name: /62100 Dataprogrammering/ }),
    ).toBeInTheDocument();
  });

  it("emits the node's own leaf codes when an alias-matched row is toggled", async () => {
    const { onToggle } = renderAliased();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "agil");
    await user.click(
      screen.getByRole("checkbox", { name: /62100 Dataprogrammering/ }),
    );

    // The alias never reaches the selection — bind 4's actual guarantee is that the user picks the
    // SNI node, and what leaves this component is that node's code and nothing else.
    expect(onToggle).toHaveBeenCalledWith(["62100"]);
  });

  it("answers at EXACTLY the threshold, not one character above it", async () => {
    renderAliased();
    const user = userEvent.setup();
    // Three characters is the boundary itself. Every other alias query in this file is 4+, so
    // ALIAS_MIN_QUERY could be raised to 4 and the suite would stay green while every 3-character
    // alias query silently stopped answering. "gil" appears in no node NAME and in one alias.
    await user.type(screen.getByLabelText("Sök bransch"), "gil");

    expect(
      screen.getByRole("checkbox", {
        name: "62100 Dataprogrammering, träff på Agil systemutveckling",
      }),
    ).toBeInTheDocument();
  });

  it("carries aliases from a DIVISION, not leaves only", async () => {
    renderAliased();
    const user = userEvent.setup();
    // The level that carries 7 of the 8 authored terms in the shipped asset. Measured: with the
    // fixture hanging aliases on a leaf alone, deleting `aliases: division.aliases` from
    // buildSniNodes left all 196 tests green, so `mjukvara`, `rörmokare`, `snickare`, `byggare` and
    // `apputveckling` could have stopped resolving without a single red test.
    await user.type(screen.getByLabelText("Sök bransch"), "mjukvara");

    expect(
      screen.getByRole("checkbox", {
        name: "62 Dataprogrammering, datakonsultverksamhet, träff på mjukvara",
      }),
    ).toBeInTheDocument();
  });

  it("carries aliases from a SECTION too", async () => {
    renderAliased();
    const user = userEvent.setup();
    // The wire and the projection both carry a section-level alias, so the level is pinned rather
    // than left supported-but-untested — the shape that rots.
    await user.type(screen.getByLabelText("Sök bransch"), "it-bransch");

    expect(
      screen.getByRole("checkbox", {
        name: "J Informations- och kommunikationsverksamhet, träff på it-bransch",
      }),
    ).toBeInTheDocument();
  });

  it("shows the SEGMENT that contains the query, not the whole SCB sentence", async () => {
    renderAliased();
    const user = userEvent.setup();
    // SCB writes classified sentences and the everyday synonym usually sits in a parenthesis.
    // Rendering the whole term and clipping it showed the OPENING — the one part that never has to
    // contain the query. Measured on the shipped asset: `it-konsult` produced 8 rows of which 0
    // displayed the typed word, one reading "Datakonsultverksamhet · matchar Datakonsultverksamhet, (…".
    await user.type(screen.getByLabelText("Sök bransch"), "it-konsult");

    expect(screen.getByText("träff på IT-konsult")).toBeInTheDocument();
    expect(
      screen.queryByText(/träff på Datakonsultverksamhet/),
    ).not.toBeInTheDocument();
  });

  it("matches a compound TAIL, which is how Swedish users type", async () => {
    renderAliased();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "utveckling");

    // "utveckling" is not a word-prefix of "systemutveckling" — it is a substring. A word-prefix
    // rule measured 10 -> 1 rows for `konsult` and 2 -> 0 for `omsorg` against the real catalogue,
    // which is why alias matching is substring above the threshold rather than prefix.
    expect(
      screen.getByRole("checkbox", {
        name: "62100 Dataprogrammering, träff på Agil systemutveckling",
      }),
    ).toBeInTheDocument();
  });
});
