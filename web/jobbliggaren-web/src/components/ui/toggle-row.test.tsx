import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { ToggleRow } from "./toggle-row";

describe("ToggleRow (F6 Prompt 2)", () => {
  it("renderar label + switch-knapp med korrekt aria-checked", () => {
    render(
      <ToggleRow
        label="Veckosammanfattning via e-post"
        checked={true}
        onChange={() => {}}
      />,
    );
    expect(
      screen.getByText("Veckosammanfattning via e-post"),
    ).toBeInTheDocument();
    const sw = screen.getByRole("switch", {
      name: "Veckosammanfattning via e-post",
    });
    expect(sw).toHaveAttribute("aria-checked", "true");
  });

  it("klick togglar checked-state via onChange", () => {
    const onChange = vi.fn();
    render(
      <ToggleRow
        label="Notifikationer"
        checked={false}
        onChange={onChange}
      />,
    );
    fireEvent.click(screen.getByRole("switch", { name: "Notifikationer" }));
    expect(onChange).toHaveBeenCalledWith(true);
  });

  it("renderar description-text + aria-describedby när description ges", () => {
    render(
      <ToggleRow
        label="Notifikationer"
        description="Få mejl vid viktiga händelser"
        checked={false}
        onChange={() => {}}
      />,
    );
    const sw = screen.getByRole("switch");
    expect(sw).toHaveAttribute("aria-describedby");
    expect(
      screen.getByText("Få mejl vid viktiga händelser"),
    ).toBeInTheDocument();
  });

  it("disabled=true gör switchen otillgänglig och onChange triggas inte", () => {
    const onChange = vi.fn();
    render(
      <ToggleRow
        label="Notifikationer"
        checked={false}
        onChange={onChange}
        disabled
      />,
    );
    const sw = screen.getByRole("switch");
    expect(sw).toBeDisabled();
    fireEvent.click(sw);
    expect(onChange).not.toHaveBeenCalled();
  });
});
