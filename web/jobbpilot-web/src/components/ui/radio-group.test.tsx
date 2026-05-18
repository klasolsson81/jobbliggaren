import { useState } from "react";
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RadioGroup, RadioGroupItem } from "./radio-group";

function StatefulHarness() {
  const [value, setValue] = useState("");
  return (
    <RadioGroup aria-label="Status" value={value} onValueChange={setValue}>
      <RadioGroupItem id="s-a" value="a">
        Alfa
      </RadioGroupItem>
      <RadioGroupItem id="s-b" value="b">
        Beta
      </RadioGroupItem>
    </RadioGroup>
  );
}

function Harness({
  value = "",
  onValueChange = () => {},
  disabled = false,
}: {
  value?: string;
  onValueChange?: (v: string) => void;
  disabled?: boolean;
}) {
  return (
    <RadioGroup
      aria-label="Status"
      value={value}
      onValueChange={onValueChange}
      disabled={disabled}
    >
      <RadioGroupItem id="r-a" value="a">
        Alfa
      </RadioGroupItem>
      <RadioGroupItem id="r-b" value="b">
        Beta
      </RadioGroupItem>
    </RadioGroup>
  );
}

describe("RadioGroup", () => {
  it("renders role=radiogroup with radio items (Radix primitive)", () => {
    render(<Harness />);

    expect(screen.getByRole("radiogroup")).toBeInTheDocument();
    expect(screen.getAllByRole("radio")).toHaveLength(2);
    expect(screen.getByRole("radio", { name: "Alfa" })).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Beta" })).toBeInTheDocument();
  });

  it("calls onValueChange with the item value on click", async () => {
    const onValueChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onValueChange={onValueChange} />);

    await user.click(screen.getByRole("radio", { name: "Beta" }));

    expect(onValueChange).toHaveBeenCalledWith("b");
  });

  it("reflects the controlled value as the checked radio", () => {
    render(<Harness value="a" />);

    expect(screen.getByRole("radio", { name: "Alfa" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Beta" })).not.toBeChecked();
  });

  it("exposes a roving tabindex (at most one tab-stop, Radix roving focus)", async () => {
    const user = userEvent.setup();
    render(<StatefulHarness />);

    const radios = screen.getAllByRole("radio");
    // Radix roving focus: max ETT element är ett tab-stopp (tabindex=0),
    // övriga -1. Före interaktion kan Radix lämna alla -1 (ingen vald) —
    // invarianten är "aldrig fler än ett tab-stopp", inte vilket.
    const tabStops = radios.filter(
      (el) => el.getAttribute("tabindex") === "0"
    );
    expect(tabStops.length).toBeLessThanOrEqual(1);

    // Efter val blir det valda elementet tab-stoppet (de andra -1).
    await user.click(screen.getByRole("radio", { name: "Beta" }));
    const afterSelect = screen
      .getAllByRole("radio")
      .filter((el) => el.getAttribute("tabindex") === "0");
    expect(afterSelect).toHaveLength(1);
    expect(afterSelect[0]).toHaveAccessibleName("Beta");
  });

  it("does not call onValueChange when the group is disabled", async () => {
    const onValueChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness disabled onValueChange={onValueChange} />);

    await user.click(screen.getByRole("radio", { name: "Alfa" }));

    expect(onValueChange).not.toHaveBeenCalled();
  });

  it("renders radio items with the civic rounded-pill radius token (no per-component radius)", () => {
    render(<Harness />);

    // Civic-token-regel: ingen ny radius-token per komponent — items bär
    // den delade rounded-pill-utilityn.
    const item = screen.getByRole("radio", { name: "Alfa" });
    expect(item).toHaveClass("rounded-pill");
  });
});
