import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OccupationSearchResults, occupationSearchBox } from "./occupation-search-results";
import type { OccupationSearchResult } from "@/lib/dto/occupation-search";
import {
  BYGG_GROUP,
  EL_GROUP,
  IT_GROUP,
  USK_HEMTJANST_GROUP,
  UX_GROUP,
  answer,
  elingenjorRow,
  groupRow,
  projektledareRow,
  underskoterskaWordRow,
} from "@/test/fixtures/occupation-search";

function renderResults(
  result: OccupationSearchResult | null,
  selected: ReadonlyArray<string> = [],
) {
  const onToggle = vi.fn<(id: string) => void>();
  const view = render(
    <OccupationSearchResults result={result} selected={selected} onToggle={onToggle} />,
  );
  return { onToggle, ...view };
}

/** A host that owns the selection, as the dialog and the rail do. */
function Host({ result, initial = [] }: { result: OccupationSearchResult; initial?: ReadonlyArray<string> }) {
  const [selected, setSelected] = useState<ReadonlyArray<string>>(initial);
  return (
    <OccupationSearchResults
      result={result}
      selected={selected}
      onToggle={(id) =>
        setSelected((prev) => (prev.includes(id) ? prev.filter((v) => v !== id) : [...prev, id]))
      }
    />
  );
}

const hits = () => screen.getByRole("group", { name: "Träffar" });

describe("OccupationSearchResults — group rows (#2198)", () => {
  it("names the row by its group and describes it by the names that reached it", () => {
    renderResults(answer([groupRow(IT_GROUP, ["Backend-utvecklare"])]));

    const row = screen.getByRole("checkbox", { name: IT_GROUP.label });
    expect(row).toHaveAccessibleDescription("träff på Backend-utvecklare");
    expect(row).toHaveAttribute("aria-checked", "false");
  });

  it("spells out at most three names in server order, semicolon-separated, then how many more", () => {
    renderResults(
      answer([
        groupRow(IT_GROUP, [
          "Mjukvaruutvecklare",
          "Backend-utvecklare",
          "Frontend-utvecklare",
          "Fullstack-utvecklare",
          "Spelutvecklare",
        ]),
      ]),
    );

    expect(screen.getByRole("checkbox", { name: IT_GROUP.label })).toHaveAccessibleDescription(
      "träff på Mjukvaruutvecklare; Backend-utvecklare; Frontend-utvecklare och 2 till",
    );
  });

  it("gives a row reached only by its own label no second line", () => {
    renderResults(answer([groupRow(UX_GROUP, [], { labelHit: true })]));

    const row = screen.getByRole("checkbox", { name: UX_GROUP.label });
    expect(row).toHaveAccessibleDescription("");
    expect(row).not.toHaveAttribute("aria-describedby");
    expect(row.textContent).toBe(UX_GROUP.label);
  });

  it("toggles the row's group id", async () => {
    const user = userEvent.setup();
    const { onToggle } = renderResults(answer([groupRow(IT_GROUP, ["Backend-utvecklare"])]));

    await user.click(screen.getByRole("checkbox", { name: IT_GROUP.label }));

    expect(onToggle).toHaveBeenCalledWith(IT_GROUP.id);
  });
});

describe("OccupationSearchResults — word rows (#2198)", () => {
  const twoRows = answer([groupRow(IT_GROUP, ["Backend-utvecklare"]), projektledareRow]);

  it("is a pressed/unpressed button controlling the right column, never a checkbox", () => {
    renderResults(twoRows);

    const row = screen.getByRole("button", { name: "Projektledare" });
    expect(row).toHaveAccessibleDescription("3 yrkesgrupper");
    expect(row).toHaveAttribute("aria-pressed", "false");
    expect(row).not.toHaveAttribute("aria-checked");
    const controlled = document.getElementById(row.getAttribute("aria-controls") ?? "");
    expect(controlled).not.toBeNull();
    expect(screen.queryByRole("checkbox", { name: "Projektledare" })).toBeNull();
  });

  it("carries the selection dot only when one of its variants' groups is selected", () => {
    const { unmount } = renderResults(twoRows);
    const unselected = screen.getByRole("button", { name: "Projektledare" });
    expect(unselected.querySelector('[aria-hidden="true"].rounded-full')).toBeNull();
    unmount();

    renderResults(twoRows, [BYGG_GROUP.id]);
    const withDot = screen.getByRole("button", { name: "Projektledare" });
    expect(withDot.querySelector('[aria-hidden="true"].rounded-full')).not.toBeNull();
  });

  it("opens on activation, shows variants naming their groups, and moves focus to the heading", async () => {
    const user = userEvent.setup();
    const { onToggle } = renderResults(twoRows);

    await user.click(screen.getByRole("button", { name: "Projektledare" }));

    const row = screen.getByRole("button", { name: "Projektledare" });
    expect(row).toHaveAttribute("aria-pressed", "true");
    const column = screen.getByRole("group", { name: "Projektledare" });
    expect(column.id).toBe(row.getAttribute("aria-controls"));
    const heading = within(column).getByText("Projektledare");
    expect(heading).toHaveFocus();
    expect(heading).toHaveAttribute("tabindex", "-1");
    expect(within(column).getAllByRole("checkbox")).toHaveLength(4);
    expect(within(column).getByRole("checkbox", { name: "Projektledare, el" })).toHaveAccessibleDescription(
      `Yrkesgrupp: ${EL_GROUP.label}`,
    );
    expect(within(column).getByRole("checkbox", { name: "Projektledare, IT" })).toHaveAccessibleDescription(
      `Yrkesgrupp: ${IT_GROUP.label}`,
    );
    // Opening chose nothing.
    expect(onToggle).not.toHaveBeenCalled();
  });

  it("reaches the first variant from the heading with Tab, by keyboard alone", async () => {
    const user = userEvent.setup();
    renderResults(twoRows);

    screen.getByRole("button", { name: "Projektledare" }).focus();
    await user.keyboard("{Enter}");
    expect(within(screen.getByRole("group", { name: "Projektledare" })).getByText("Projektledare")).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("checkbox", { name: "Projektledare, IT" })).toHaveFocus();
  });

  it("checks variants that share a group together, and a variant toggles its group id", async () => {
    const user = userEvent.setup();
    render(<Host result={answer([projektledareRow])} />);

    await user.click(screen.getByRole("checkbox", { name: "Projektledare, el" }));

    expect(screen.getByRole("checkbox", { name: "Projektledare, el" })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("checkbox", { name: "Projektledare, telekom" })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("checkbox", { name: "Projektledare, IT" })).toHaveAttribute("aria-checked", "false");
    expect(
      screen.getByRole("checkbox", { name: "Projektledare, bygg och anläggning" }),
    ).toHaveAttribute("aria-checked", "false");
  });

  it("checks a group row and the variant that selects the same group together (undersköterska)", async () => {
    const user = userEvent.setup();
    render(
      <Host
        result={answer([
          groupRow(USK_HEMTJANST_GROUP, ["Undersköterska, hemtjänst, äldreboende och habilitering"]),
          underskoterskaWordRow,
        ])}
      />,
    );

    await user.click(screen.getByRole("checkbox", { name: USK_HEMTJANST_GROUP.label }));
    await user.click(screen.getByRole("button", { name: "Undersköterska" }));

    expect(
      screen.getByRole("checkbox", { name: "Undersköterska, hemtjänst, äldreboende och habilitering" }),
    ).toHaveAttribute("aria-checked", "true");
    expect(
      screen.getByRole("checkbox", { name: "Undersköterska, vård- och specialavdelning och mottagning" }),
    ).toHaveAttribute("aria-checked", "false");
  });
});

describe("OccupationSearchResults — the right column (#2198, design-reviewer Major 1)", () => {
  it("opens row 1 by itself when it holds the exact word, and selects nothing", () => {
    const { onToggle } = renderResults(
      answer([elingenjorRow, groupRow(EL_GROUP, ["Projektledare, el"])]),
    );

    expect(screen.getByRole("button", { name: "Elingenjör" })).toHaveAttribute("aria-pressed", "true");
    const column = screen.getByRole("group", { name: "Elingenjör" });
    const variants = within(column).getAllByRole("checkbox");
    expect(variants).toHaveLength(2);
    expect(variants[0]).toHaveAccessibleName("Elingenjör");
    expect(variants[1]).toHaveAccessibleName("Elingenjör, fartyg/Fartygseltekniker");
    expect(onToggle).toHaveBeenCalledTimes(0);
    // Opening by default does not take focus from the field the user is typing in.
    expect(document.body).toHaveFocus();
  });

  it("opens a Word row that is the only row by itself, and selects nothing", () => {
    const { onToggle } = renderResults(answer([projektledareRow]));

    expect(screen.getByRole("button", { name: "Projektledare" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("group", { name: "Projektledare" })).toBeInTheDocument();
    expect(onToggle).toHaveBeenCalledTimes(0);
  });

  it("waits with the idle line when Word rows exist but row 1 does not open, even an exact one further down", () => {
    renderResults(
      answer([groupRow(IT_GROUP, ["Backend-utvecklare"]), { ...projektledareRow, exactMatch: true }]),
    );

    expect(screen.getByRole("button", { name: "Projektledare" })).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByText("Välj ett yrke med flera yrkesgrupper till vänster.")).toBeInTheDocument();
    expect(screen.queryByRole("group", { name: "Projektledare" })).toBeNull();
    expect(document.querySelector(".jp-occsearch__box--single")).toBeNull();
  });

  it("is one full-width column when the answer has no Word row", () => {
    renderResults(answer([groupRow(IT_GROUP, ["Backend-utvecklare"]), groupRow(UX_GROUP, ["UX-designer"])]));

    const box = document.querySelector(".jp-occsearch__box");
    expect(box).toHaveClass("jp-occsearch__box--single");
    expect(box?.children).toHaveLength(1);
    expect(screen.queryByText("Välj ett yrke med flera yrkesgrupper till vänster.")).toBeNull();
  });

  it("starts a new answer from its own default, not from the row opened in the last one", async () => {
    const user = userEvent.setup();
    const first = answer([groupRow(IT_GROUP, ["Backend-utvecklare"]), projektledareRow]);
    const { rerender, onToggle } = renderResults(first);
    await user.click(screen.getByRole("button", { name: "Projektledare" }));

    rerender(
      <OccupationSearchResults
        result={answer([groupRow(IT_GROUP, ["Backend-utvecklare"]), projektledareRow])}
        selected={[]}
        onToggle={onToggle}
      />,
    );

    expect(screen.getByRole("button", { name: "Projektledare" })).toHaveAttribute("aria-pressed", "false");
  });

  it("keeps the whole left column before the right one in DOM order", () => {
    renderResults(answer([elingenjorRow, groupRow(EL_GROUP, ["Projektledare, el"])], 60));

    const left = hits();
    const right = screen.getByRole("group", { name: "Elingenjör" });
    expect(left.compareDocumentPosition(right) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(left.contains(right)).toBe(false);
  });
});

describe("OccupationSearchResults — what search mode never shows (#2198)", () => {
  it("has no Välj alla in either column", () => {
    renderResults(answer([projektledareRow, groupRow(IT_GROUP, ["Backend-utvecklare"])]));

    expect(screen.queryByRole("checkbox", { name: /Välj alla/ })).toBeNull();
    expect(screen.queryByText(/Välj alla/)).toBeNull();
  });

  it("names the hit column without a count; the count is the status line's", () => {
    renderResults(answer([groupRow(IT_GROUP, ["Backend-utvecklare"])], 60));

    expect(hits()).toBeInTheDocument();
    expect(screen.queryByRole("group", { name: /60/ })).toBeNull();
  });

  it("shows nothing while pending: an empty box, no rows from an earlier word", () => {
    renderResults(null);

    const box = document.querySelector(".jp-occsearch__box");
    expect(box).toBeEmptyDOMElement();
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(screen.queryByRole("button")).toBeNull();
  });
});

describe("OccupationSearchResults — the cap line (#2198)", () => {
  it("says how many rows are not shown, as the last element of the left column, out of the tab order", () => {
    renderResults(answer([groupRow(IT_GROUP, ["Backend-utvecklare"]), groupRow(UX_GROUP, ["UX-designer"])], 60));

    const line = screen.getByText("58 träffar visas inte. Skriv mer för att avgränsa.");
    expect(hits().lastElementChild).toBe(line);
    expect(line.tagName).toBe("P");
    expect(line).not.toHaveAttribute("tabindex");
  });

  it("uses the singular for one", () => {
    renderResults(answer([groupRow(IT_GROUP, ["Backend-utvecklare"])], 2));

    expect(screen.getByText("1 träff visas inte. Skriv mer för att avgränsa.")).toBeInTheDocument();
  });

  it("is absent when every row is shown", () => {
    renderResults(answer([groupRow(IT_GROUP, ["Backend-utvecklare"])]));

    expect(screen.queryByText(/visas inte/)).toBeNull();
  });
});

describe("occupationSearchBox (#2198)", () => {
  it("gives browsing back on idle, no hits, unavailable and 401; holds the box while pending or with rows", () => {
    const rows = answer([groupRow(IT_GROUP, ["Backend-utvecklare"])]);
    expect(occupationSearchBox({ kind: "idle" })).toEqual({ kind: "browse" });
    expect(occupationSearchBox({ kind: "unavailable" })).toEqual({ kind: "browse" });
    expect(occupationSearchBox({ kind: "unauthorized" })).toEqual({ kind: "browse" });
    expect(occupationSearchBox({ kind: "ok", word: "chaufför", result: answer([]) })).toEqual({ kind: "browse" });
    expect(occupationSearchBox({ kind: "pending" })).toEqual({ kind: "pending" });
    expect(occupationSearchBox({ kind: "ok", word: "backend", result: rows })).toEqual({
      kind: "rows",
      result: rows,
    });
  });
});
