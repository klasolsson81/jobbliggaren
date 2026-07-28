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
    expect(
      screen.getByRole("checkbox", {
        name: "Informations- och kommunikationsverksamhet",
      }),
    ).toBeInTheDocument();
    // Children stay collapsed — the whole point of a tree over a 944-row dump.
    expect(
      screen.queryByRole("checkbox", { name: "Datakonsultverksamhet" }),
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
      "Informations- och kommunikationsverksamhet",
      "Dataprogrammering, datakonsultverksamhet",
      "Datakonsultverksamhet",
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
        name: "Dataprogrammering, datakonsultverksamhet",
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
        name: "Dataprogrammering, datakonsultverksamhet",
      }),
    ).toHaveAttribute("aria-checked", "mixed");
  });

  it("is keyboard-operable: Space toggles a filtered row", async () => {
    const { onToggle } = renderPicker();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "system");
    screen.getByRole("checkbox", { name: "Systemutveckling" }).focus();
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
      screen.getByRole("checkbox", { name: "Systemutveckling" }),
    ).toHaveClass("jp-criterionrow");
  });

  it("says so when nothing matches", async () => {
    renderPicker();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Sök bransch"), "zzz");
    expect(screen.getByText("Inga träffar.")).toBeInTheDocument();
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
