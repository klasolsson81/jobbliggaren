import { useState } from "react";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { StarRating } from "./star-rating";

function Harness({ onChange }: { onChange?: (value: number | null) => void }) {
  const [value, setValue] = useState<number | null>(null);
  return (
    <StarRating
      value={value}
      onChange={(next) => {
        onChange?.(next);
        setValue(next);
      }}
    />
  );
}

const star = (rating: number) => screen.getByRole("radio", { name: `${rating} av 5` });
const box = (rating: number) => star(rating).closest("label")!;

describe("StarRating", () => {
  it("is a radio group named by the question, with five options named n av 5", () => {
    render(<Harness />);
    const group = screen.getByRole("group", { name: "Hur fungerar den här sidan för dig?" });
    const radios = within(group).getAllByRole("radio");
    expect(radios.map((radio) => radio.getAttribute("aria-label"))).toEqual([
      "1 av 5",
      "2 av 5",
      "3 av 5",
      "4 av 5",
      "5 av 5",
    ]);
    expect(radios.every((radio) => !(radio as HTMLInputElement).checked)).toBe(true);
  });

  it("draws outline stars only (DESIGN.md §7)", () => {
    const { container } = render(<Harness />);
    const icons = container.querySelectorAll("svg");
    expect(icons).toHaveLength(5);
    for (const icon of icons) {
      expect(icon).toHaveAttribute("fill", "none");
      expect(icon).toHaveAttribute("aria-hidden", "true");
    }
  });

  it("marks the choice by more than colour and leaves focus on the chosen star", async () => {
    const onChange = vi.fn();
    render(<Harness onChange={onChange} />);
    const user = userEvent.setup();

    await user.click(star(3));

    expect(onChange).toHaveBeenCalledWith(3);
    expect(star(3)).toBeChecked();
    expect(star(3)).toHaveFocus();
    expect([1, 2, 3, 4, 5].map((rating) => box(rating).hasAttribute("data-lit"))).toEqual([
      true,
      true,
      true,
      false,
      false,
    ]);
    expect([1, 2, 3, 4, 5].map((rating) => box(rating).hasAttribute("data-selected"))).toEqual([
      false,
      false,
      true,
      false,
      false,
    ]);
    // The heavier stroke up to the choice.
    expect(box(3).querySelector("svg")).toHaveAttribute("stroke-width", "2.75");
    expect(box(4).querySelector("svg")).toHaveAttribute("stroke-width", "2");
    // The readout is for the eye: the radio's own name carries it to a screen reader.
    const readout = screen.getByText("3 av 5");
    expect(readout).toHaveAttribute("aria-hidden", "true");
  });

  it("offers no clear button before a rating is set", () => {
    render(<Harness />);
    expect(screen.queryByRole("button", { name: "Ta bort betyget" })).toBeNull();
  });

  it("clears the rating and moves focus to the first star", async () => {
    const onChange = vi.fn();
    render(<Harness onChange={onChange} />);
    const user = userEvent.setup();
    await user.click(star(4));

    await user.click(screen.getByRole("button", { name: "Ta bort betyget" }));

    expect(onChange).toHaveBeenLastCalledWith(null);
    expect(screen.getAllByRole("radio").some((radio) => (radio as HTMLInputElement).checked)).toBe(false);
    expect(star(1)).toHaveFocus();
    expect(screen.queryByRole("button", { name: "Ta bort betyget" })).toBeNull();
    expect(screen.queryByText("4 av 5")).toBeNull();
  });
});
