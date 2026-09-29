import { describe, it, expect, vi, beforeEach } from "vitest";
import { useState } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ExperienceField } from "./experience-field";

beforeEach(() => {
  vi.clearAllMocks();
});

/** The field is named by an element outside it, as its dialog's title names it (#1918 Binding 4). */
function Named({
  value,
  onChange,
}: {
  value: number | null;
  onChange: (next: number | null) => void;
}) {
  return (
    <>
      <h2 id="experience-title">Antal års erfarenhet</h2>
      <ExperienceField value={value} onChange={onChange} labelledBy="experience-title" />
    </>
  );
}

/** Stateful host so the controlled input reflects each keystroke (mirrors the
 *  real dialog host) — required for multi-digit typing/clamping. */
function StatefulHost({
  initial = null,
  onChange,
}: {
  initial?: number | null;
  onChange: (next: number | null) => void;
}) {
  const [value, setValue] = useState<number | null>(initial);
  return (
    <Named
      value={value}
      onChange={(next) => {
        setValue(next);
        onChange(next);
      }}
    />
  );
}

describe("ExperienceField (STEG 3 / ADR 0079)", () => {
  it("takes its name from the element it is labelled by, with no label or hint of its own", () => {
    render(<Named value={null} onChange={vi.fn()} />);
    const input = screen.getByRole("spinbutton", { name: "Antal års erfarenhet" });
    expect(input).toHaveValue(null);
    expect(input).toHaveAttribute("aria-labelledby", "experience-title");
    expect(screen.queryByText("Antal års erfarenhet", { selector: "label" })).toBeNull();
    // Inget placeholder-exempel (hård Klas-regel) och ingen hint (DESIGN.md §8 regel 3).
    expect(input).not.toHaveAttribute("placeholder");
    expect(input).not.toHaveAttribute("aria-describedby");
  });

  it("ett angivet värde visas i fältet", () => {
    render(<Named value={5} onChange={vi.fn()} />);
    expect(screen.getByRole("spinbutton", { name: "Antal års erfarenhet" })).toHaveValue(5);
  });

  it("inmatning emitterar ett heltal", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<StatefulHost onChange={onChange} />);
    await user.type(screen.getByRole("spinbutton", { name: "Antal års erfarenhet" }), "8");
    expect(onChange).toHaveBeenLastCalledWith(8);
  });

  it("tömt fält emitterar null (ej angivet), aldrig 0", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<StatefulHost initial={3} onChange={onChange} />);
    await user.clear(screen.getByRole("spinbutton", { name: "Antal års erfarenhet" }));
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it("klampar över taket (70)", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<StatefulHost onChange={onChange} />);
    await user.type(screen.getByRole("spinbutton", { name: "Antal års erfarenhet" }), "99");
    expect(onChange).toHaveBeenLastCalledWith(70);
  });
});
