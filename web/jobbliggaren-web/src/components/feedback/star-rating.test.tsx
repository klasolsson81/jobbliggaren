import { useState } from "react";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { StarButtons, StarRating } from "./star-rating";

const QUESTION = "Hur fungerar sidan Jobb för dig?";

const fillsIn = (boxes: HTMLElement[]) => boxes.map((box) => box.querySelector("svg")?.getAttribute("fill"));
const FILLED = "currentColor";

function RadioHarness({
  onChange,
  clearable,
}: {
  onChange?: (value: number | null) => void;
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
    />
  );
}

describe("StarRating (the dialog's radios)", () => {
  const star = (rating: number) => screen.getByRole("radio", { name: `${rating} av 5` });
  const box = (rating: number) => star(rating).closest("label")!;
  const fills = () => fillsIn([1, 2, 3, 4, 5].map(box));

  it("is a radio group named by the question it is given, with five options named n av 5", () => {
    render(<RadioHarness />);
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
    const { container } = render(<RadioHarness />);
    const icons = container.querySelectorAll("svg");
    expect(icons).toHaveLength(5);
    for (const icon of icons) {
      expect(icon).toHaveAttribute("fill", "none");
      expect(icon).toHaveAttribute("aria-hidden", "true");
    }
  });

  it("marks the choice by more than colour and leaves focus on the chosen star", async () => {
    const onChange = vi.fn();
    render(<RadioHarness onChange={onChange} />);
    const user = userEvent.setup();

    await user.click(star(3));

    expect(onChange).toHaveBeenCalledWith(3);
    expect(star(3)).toBeChecked();
    expect(star(3)).toHaveFocus();
    expect(fills()).toEqual([FILLED, FILLED, FILLED, "none", "none"]);
    // The readout is for the eye: the radio's own name carries it to a screen reader.
    const readout = screen.getByText("3 av 5");
    expect(readout).toHaveAttribute("aria-hidden", "true");
  });

  it("moves the choice with the arrow keys, which reports each star it passes", async () => {
    const onChange = vi.fn();
    render(<RadioHarness onChange={onChange} />);
    const user = userEvent.setup();
    await user.tab();

    await user.keyboard("{ArrowRight}{ArrowRight}");

    expect(onChange.mock.calls).toEqual([[2], [3]]);
    expect(star(3)).toBeChecked();
    expect(fills()).toEqual([FILLED, FILLED, FILLED, "none", "none"]);
  });

  it("fills the stars up to the one the mouse points at, and returns to the rating when it leaves", async () => {
    render(<RadioHarness />);
    const user = userEvent.setup();
    await user.click(star(2));

    await user.hover(box(4));
    expect(fills()).toEqual([FILLED, FILLED, FILLED, FILLED, "none"]);
    expect(star(2)).toBeChecked();

    await user.unhover(box(4));
    expect(fills()).toEqual([FILLED, FILLED, "none", "none", "none"]);
  });

  it("leaves no preview behind after a touch", async () => {
    render(<RadioHarness />);
    const user = userEvent.setup();

    await user.pointer({ keys: "[TouchA]", target: box(4) });

    expect(star(4)).toBeChecked();
    expect(fills()).toEqual([FILLED, FILLED, FILLED, FILLED, "none"]);
    await user.pointer({ keys: "[TouchA]", target: screen.getByRole("button", { name: "Ta bort betyget" }) });
    expect(fills()).toEqual(["none", "none", "none", "none", "none"]);
  });

  it("offers no clear button before a rating is set", () => {
    render(<RadioHarness />);
    expect(screen.queryByRole("button", { name: "Ta bort betyget" })).toBeNull();
  });

  it("clears the rating and moves focus to the first star", async () => {
    const onChange = vi.fn();
    render(<RadioHarness onChange={onChange} />);
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
    render(<RadioHarness clearable={false} />);
    const user = userEvent.setup();

    await user.click(star(3));

    expect(star(3)).toBeChecked();
    expect(screen.queryByText("3 av 5")).toBeNull();
    expect(screen.queryByRole("button")).toBeNull();
  });
});

describe("StarButtons (the row's answer)", () => {
  const star = (rating: number) => screen.getByRole("button", { name: `${rating} av 5` });
  const fills = () => fillsIn([1, 2, 3, 4, 5].map(star));

  function renderStars({ value = null }: { value?: number | null } = {}) {
    const onCommit = vi.fn<(value: number) => void>();
    render(<StarButtons question={QUESTION} value={value} sending={false} onCommit={onCommit} />);
    return onCommit;
  }

  it("is a group named by the question with five buttons named n av 5, none of them a radio", () => {
    renderStars();
    const group = screen.getByRole("group", { name: QUESTION });
    const buttons = within(group).getAllByRole("button");
    expect(buttons.map((button) => button.getAttribute("aria-label"))).toEqual([
      "1 av 5",
      "2 av 5",
      "3 av 5",
      "4 av 5",
      "5 av 5",
    ]);
    expect(buttons.every((button) => button.getAttribute("type") === "button")).toBe(true);
    expect(screen.queryByRole("radio")).toBeNull();
    expect(fills()).toEqual(["none", "none", "none", "none", "none"]);
  });

  it("sends the star that is pressed, also the one pressed again", async () => {
    const onCommit = renderStars();
    const user = userEvent.setup();

    await user.click(star(4));
    await user.click(star(4));

    expect(onCommit.mock.calls).toEqual([[4], [4]]);
  });

  it("sends with the keyboard: Tab reaches each star, Enter and Space send the one focus is on", async () => {
    const onCommit = renderStars();
    const user = userEvent.setup();

    await user.tab();
    await user.tab();
    await user.tab();
    expect(star(3)).toHaveFocus();
    await user.keyboard("{Enter}");
    await user.keyboard(" ");

    expect(onCommit.mock.calls).toEqual([[3], [3]]);
  });

  it("fills the stars up to the one focus stands on, and back when focus leaves", async () => {
    renderStars();
    const user = userEvent.setup();

    await user.tab();
    await user.tab();
    expect(star(2)).toHaveFocus();
    expect(fills()).toEqual([FILLED, FILLED, "none", "none", "none"]);

    await user.tab();
    expect(fills()).toEqual([FILLED, FILLED, FILLED, "none", "none"]);
    await user.click(document.body);
    expect(fills()).toEqual(["none", "none", "none", "none", "none"]);
  });

  it("fills the stars up to the one the mouse points at, and back to the kept rating when it leaves", async () => {
    renderStars({ value: 2 });
    const user = userEvent.setup();
    expect(fills()).toEqual([FILLED, FILLED, "none", "none", "none"]);

    await user.hover(star(4));
    expect(fills()).toEqual([FILLED, FILLED, FILLED, FILLED, "none"]);

    await user.unhover(star(4));
    expect(fills()).toEqual([FILLED, FILLED, "none", "none", "none"]);
  });

  it("leaves no preview behind after a touch", async () => {
    renderStars();
    const user = userEvent.setup();

    await user.pointer({ keys: "[TouchA]", target: star(4) });

    // The press focused the star, which is the only fill left; leaving it clears that too.
    expect(fills()).toEqual([FILLED, FILLED, FILLED, FILLED, "none"]);
    await user.click(document.body);
    expect(fills()).toEqual(["none", "none", "none", "none", "none"]);
  });

  it("writes Skickar… beside the stars only while sending, as text for the eye alone", () => {
    const { rerender } = render(<StarButtons question={QUESTION} value={3} sending onCommit={() => {}} />);
    expect(screen.getByText("Skickar…")).toHaveAttribute("aria-hidden", "true");

    rerender(<StarButtons question={QUESTION} value={3} sending={false} onCommit={() => {}} />);
    expect(screen.queryByText("Skickar…")).toBeNull();
  });
});
