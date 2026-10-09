import { useState } from "react";
import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { StarRating } from "./star-rating";

const QUESTION = "Hur fungerar sidan Jobb för dig?";

function Harness({
  onChange,
  onCommit,
  clearable,
}: {
  onChange?: (value: number | null) => void;
  onCommit?: (value: number) => void;
  clearable?: boolean;
}) {
  const [value, setValue] = useState<number | null>(null);
  return (
    <StarRating
      question={QUESTION}
      value={value}
      clearable={clearable}
      onChange={(next) => {
        onChange?.(next);
        setValue(next);
      }}
      onCommit={
        onCommit === undefined
          ? undefined
          : (next) => {
              onCommit(next);
              setValue(next);
            }
      }
    />
  );
}

const star = (rating: number) => screen.getByRole("radio", { name: `${rating} av 5` });
const box = (rating: number) => star(rating).closest("label")!;
const fills = () => [1, 2, 3, 4, 5].map((rating) => box(rating).querySelector("svg")?.getAttribute("fill"));

describe("StarRating", () => {
  it("is a radio group named by the question it is given, with five options named n av 5", () => {
    render(<Harness />);
    const group = screen.getByRole("group", { name: QUESTION });
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

  it("draws five outlined stars before a rating", () => {
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
    expect(fills()).toEqual(["currentColor", "currentColor", "currentColor", "none", "none"]);
    // The readout is for the eye: the radio's own name carries it to a screen reader.
    const readout = screen.getByText("3 av 5");
    expect(readout).toHaveAttribute("aria-hidden", "true");
  });

  it("fills the stars up to the one the mouse points at, and returns to the rating when it leaves", async () => {
    render(<Harness />);
    const user = userEvent.setup();
    await user.click(star(2));

    await user.hover(box(4));
    expect(fills()).toEqual(["currentColor", "currentColor", "currentColor", "currentColor", "none"]);
    expect(star(2)).toBeChecked();

    await user.unhover(box(4));
    expect(fills()).toEqual(["currentColor", "currentColor", "none", "none", "none"]);
  });

  it("leaves no preview behind after a touch", async () => {
    render(<Harness />);
    const user = userEvent.setup();

    await user.pointer({ keys: "[TouchA]", target: box(4) });

    expect(star(4)).toBeChecked();
    expect(fills()).toEqual(["currentColor", "currentColor", "currentColor", "currentColor", "none"]);
    await user.pointer({ keys: "[TouchA]", target: screen.getByRole("button", { name: "Ta bort betyget" }) });
    expect(fills()).toEqual(["none", "none", "none", "none", "none"]);
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

  it("without clearable, writes no readout and offers no clear button", async () => {
    render(<Harness clearable={false} />);
    const user = userEvent.setup();

    await user.click(star(3));

    expect(star(3)).toBeChecked();
    expect(screen.queryByText("3 av 5")).toBeNull();
    expect(screen.queryByRole("button")).toBeNull();
  });
});

describe("StarRating with onCommit", () => {
  it("commits a clicked star without reporting a change, and commits the chosen star again when it is clicked again", async () => {
    const onChange = vi.fn();
    const onCommit = vi.fn();
    render(<Harness onChange={onChange} onCommit={onCommit} clearable={false} />);
    const user = userEvent.setup();

    await user.click(box(4));
    expect(onCommit).toHaveBeenCalledTimes(1);
    expect(onCommit).toHaveBeenLastCalledWith(4);
    expect(star(4)).toBeChecked();

    await user.click(box(4));
    expect(onCommit).toHaveBeenCalledTimes(2);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("moves the choice with the arrow keys without committing, then commits it with Enter or Space", async () => {
    const onChange = vi.fn();
    const onCommit = vi.fn();
    render(<Harness onChange={onChange} onCommit={onCommit} clearable={false} />);
    const user = userEvent.setup();
    act(() => star(1).focus());

    await user.keyboard("{ArrowRight}{ArrowRight}");

    expect(onChange.mock.calls).toEqual([[2], [3]]);
    expect(onCommit).not.toHaveBeenCalled();
    expect(star(3)).toBeChecked();
    expect(star(3)).toHaveFocus();
    expect(fills()).toEqual(["currentColor", "currentColor", "currentColor", "none", "none"]);

    await user.keyboard("{Enter}");
    expect(onCommit.mock.calls).toEqual([[3]]);

    await user.keyboard(" ");
    expect(onCommit.mock.calls).toEqual([[3], [3]]);
  });

  it("commits a star clicked after the arrow keys moved the choice", async () => {
    const onCommit = vi.fn();
    render(<Harness onCommit={onCommit} clearable={false} />);
    const user = userEvent.setup();
    act(() => star(1).focus());
    await user.keyboard("{ArrowRight}");

    await user.click(box(5));

    expect(onCommit.mock.calls).toEqual([[5]]);
  });
});
