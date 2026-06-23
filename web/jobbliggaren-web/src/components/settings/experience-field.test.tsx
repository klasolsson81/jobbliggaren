import { describe, it, expect, vi, beforeEach } from "vitest";
import { useState } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ExperienceField } from "./experience-field";

beforeEach(() => {
  vi.clearAllMocks();
});

/** Stateful host so the controlled input reflects each keystroke (mirrors the
 *  real wizard/dialog hosts) — required for multi-digit typing/clamping. */
function StatefulHost({
  initial = null,
  onChange,
}: {
  initial?: number | null;
  onChange: (next: number | null) => void;
}) {
  const [value, setValue] = useState<number | null>(initial);
  return (
    <ExperienceField
      value={value}
      onChange={(next) => {
        setValue(next);
        onChange(next);
      }}
    />
  );
}

describe("ExperienceField (STEG 3 / ADR 0079)", () => {
  it("renderar label + hjälptext, inget exempel-värde i fältet", () => {
    render(<ExperienceField value={null} onChange={vi.fn()} />);
    const input = screen.getByLabelText("Antal års erfarenhet");
    expect(input).toHaveValue(null);
    // Inget placeholder-exempel (hård Klas-regel).
    expect(input).not.toHaveAttribute("placeholder");
    expect(
      screen.getByText(/Ungefärligt antal år du arbetat/)
    ).toBeInTheDocument();
  });

  it("ett angivet värde visas i fältet", () => {
    render(<ExperienceField value={5} onChange={vi.fn()} />);
    expect(screen.getByLabelText("Antal års erfarenhet")).toHaveValue(5);
  });

  it("inmatning emitterar ett heltal", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<StatefulHost onChange={onChange} />);
    await user.type(screen.getByLabelText("Antal års erfarenhet"), "8");
    expect(onChange).toHaveBeenLastCalledWith(8);
  });

  it("tömt fält emitterar null (ej angivet), aldrig 0", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<StatefulHost initial={3} onChange={onChange} />);
    await user.clear(screen.getByLabelText("Antal års erfarenhet"));
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it("klampar över taket (70)", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<StatefulHost onChange={onChange} />);
    await user.type(screen.getByLabelText("Antal års erfarenhet"), "99");
    expect(onChange).toHaveBeenLastCalledWith(70);
  });
});
