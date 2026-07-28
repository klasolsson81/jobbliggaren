import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
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
 * are consumed by the criterion dialog (Smarta bevakningar) as well as the new bransch popover. Without
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

  it("says so when nothing matches, and says how to get back to the whole list", async () => {
    renderPicker();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "zzz");
    // The empty state is this control's most common failure mode — SNI's vocabulary is not the
    // user's — so a full stop is not an answer. It has to point back at the tree.
    expect(
      screen.getByText("Inga träffar. Rensa sökfältet för att bläddra i hela listan."),
    ).toBeInTheDocument();
  });

  it("announces the number of matches for every query", async () => {
    renderPicker();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "verksamhet");
    // Without this a sighted user cannot see how much is below the fold, and a screen-reader user
    // gets no signal at all that the list changed while typing.
    expect(screen.getByRole("status")).toHaveTextContent("3 träffar");
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

  it("still offers Rensa without a heading, once something is selected", async () => {
    const { onClear } = renderPicker({ selected: new Set(["62010"]) });
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Rensa" }));
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
