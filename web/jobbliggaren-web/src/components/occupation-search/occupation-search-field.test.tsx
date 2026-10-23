import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { render, screen } from "@testing-library/react";
import { render as rawRender } from "@testing-library/react/pure";
import { NextIntlClientProvider } from "next-intl";
import userEvent from "@testing-library/user-event";
import enMessages from "../../../messages/en";
import { OccupationSearchField } from "./occupation-search-field";
import { OCCUPATION_SEARCH_FIELD_ATTRIBUTE } from "./keep-host-open";
import type { OccupationSearchState } from "@/lib/hooks/use-occupation-search";
import { ruleAnswer } from "@/test/fixtures/occupation-search";

function Controlled({ state, initial = "" }: { state: OccupationSearchState; initial?: string }) {
  const [value, setValue] = useState(initial);
  return <OccupationSearchField id="occ-search" value={value} onChange={setValue} state={state} />;
}

describe("OccupationSearchField (#2198)", () => {
  it("is a labelled search field with no hint, no autocomplete and the API's length cap", () => {
    render(<Controlled state={{ kind: "idle" }} />);

    const input = screen.getByRole("searchbox", { name: "Sök yrke" });
    expect(input).toHaveAttribute("type", "search");
    expect(input).toHaveAttribute("autocomplete", "off");
    expect(input).toHaveAttribute("maxlength", "100");
    expect(input).not.toHaveAttribute("aria-describedby");
    expect(input).not.toHaveAttribute("placeholder");
    expect(input).toHaveAttribute(OCCUPATION_SEARCH_FIELD_ATTRIBUTE);
    expect(input).not.toHaveAttribute("role");
  });

  it.each<[string, OccupationSearchState, string]>([
    ["idle", { kind: "idle" }, ""],
    ["pending", { kind: "pending" }, "Söker…"],
    ["an answer, counted by its total", { kind: "ok", word: "chef", result: ruleAnswer.chef }, "60 träffar"],
    ["one hit", { kind: "ok", word: "ux", result: ruleAnswer.ux }, "1 träff"],
    [
      "no hits",
      { kind: "ok", word: "chaufför", result: { rows: [], totalRows: 0 } },
      "Inga träffar. Välj bland yrkesområdena nedan.",
    ],
    [
      "unavailable",
      { kind: "unavailable" },
      "Sökningen fungerar inte just nu. Välj bland yrkesområdena nedan.",
    ],
    ["unauthorized", { kind: "unauthorized" }, "Du är inte inloggad. Logga in och försök igen."],
  ])("says %s in the one status region", (_name, state, text) => {
    render(<Controlled state={state} />);

    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("aria-live", "polite");
    expect(status).toHaveAttribute("aria-atomic", "true");
    expect(status.textContent).toBe(text);
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("keeps the same status element across states, so a change is announced", () => {
    const { rerender } = render(<Controlled state={{ kind: "idle" }} />);
    const before = screen.getByRole("status");

    rerender(<Controlled state={{ kind: "pending" }} />);
    rerender(<Controlled state={{ kind: "unavailable" }} />);

    expect(screen.getByRole("status")).toBe(before);
  });

  it("Escape with text clears it and keeps focus; Escape on an empty field is left to the host", async () => {
    const user = userEvent.setup();
    const hostEscapes = vi.fn();
    render(
      <div
        onKeyDown={(e) => {
          if (e.key === "Escape" && !e.defaultPrevented) hostEscapes();
        }}
      >
        <Controlled state={{ kind: "idle" }} initial="" />
      </div>,
    );
    const input = screen.getByRole("searchbox", { name: "Sök yrke" });

    await user.type(input, "chef");
    await user.keyboard("{Escape}");
    expect(input).toHaveValue("");
    expect(input).toHaveFocus();
    expect(hostEscapes).not.toHaveBeenCalled();

    await user.keyboard("{Escape}");
    expect(hostEscapes).toHaveBeenCalledTimes(1);
  });

  it("says in English that the names are Swedish", () => {
    rawRender(
      <NextIntlClientProvider locale="en" messages={enMessages} timeZone="Europe/Stockholm">
        <Controlled state={{ kind: "ok", word: "chef", result: ruleAnswer.chef }} />
      </NextIntlClientProvider>,
    );

    expect(
      screen.getByRole("searchbox", { name: "Search occupations (names in Swedish)" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("status").textContent).toBe("60 matches");
  });
});
