import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { SetupCallout } from "./setup-callout";

describe("SetupCallout", () => {
  it("renderar åtgärds-grupplabel, kicker och CTA till match-setup-modalen", () => {
    render(<SetupCallout />);

    expect(screen.getByText("Kräver åtgärd")).toBeInTheDocument();
    // Mono-kicker.
    expect(screen.getByText("Matchning")).toBeInTheDocument();
    // Fet ledmening.
    expect(screen.getByText(/Matchningen är inte klar/)).toBeInTheDocument();

    const cta = screen.getByRole("link", { name: /Ställ in matchning/ });
    expect(cta).toHaveAttribute("href", "/oversikt?matchsetup=1");
    expect(screen.getByText("Tar ett par minuter")).toBeInTheDocument();
  });

  it("är inte avfärdbart (ingen knapp)", () => {
    render(<SetupCallout />);
    expect(screen.queryByRole("button")).toBeNull();
  });
});
