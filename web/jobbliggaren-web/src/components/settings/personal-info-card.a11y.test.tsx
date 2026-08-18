import { describe, it, expect } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { PersonalInfoCard } from "./personal-info-card";

// #1117 — the error this card can now show points at ONE input with ONE fix, so it has to be
// wired to that input rather than only rendered near it: aria-invalid on the control,
// aria-describedby pointing at the message, and focus moved there.
//
// BOTH polarities are pinned. The positive alone would pass if the card marked the input invalid
// for every failure, which would tell a screen-reader user her name is wrong when the network
// dropped. `errorField` is the discriminator that separates the two, and its absence is a
// behavioural claim, not a default.

const noop = () => {};

function renderCard(overrides: Partial<React.ComponentProps<typeof PersonalInfoCard>> = {}) {
  return render(
    <PersonalInfoCard
      displayName="Anna Andersson"
      email="anna@exempel.se"
      isPending={false}
      error={null}
      errorField={null}
      savedAt={null}
      onDisplayNameChange={noop}
      onSubmit={(e) => e.preventDefault()}
      {...overrides}
    />,
  );
}

describe("PersonalInfoCard field-level error association (#1117)", () => {
  it("leaves the name input unmarked when there is no error", () => {
    renderCard();

    const input = screen.getByLabelText("Namn");
    expect(input).not.toHaveAttribute("aria-invalid");
    expect(input).not.toHaveAttribute("aria-describedby");
  });

  it("marks the name input invalid and describes it by the message when the error names the field", async () => {
    renderCard({
      error: "Namnet får inte innehålla ett personnummer.",
      errorField: "displayName",
    });

    const input = screen.getByLabelText("Namn");
    const alert = screen.getByRole("alert");

    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input.getAttribute("aria-describedby")).toBe(alert.id);
    expect(alert.id).not.toBe("");
    await waitFor(() => expect(input).toHaveFocus());
  });

  it("does NOT mark the input invalid for a failure that is not about the field", () => {
    // Same error channel, different cause: the message is shown, but nothing claims the name
    // input is wrong and focus is not stolen from wherever the user was.
    renderCard({
      error: "Kunde inte nå servern. Kontrollera din nätverksanslutning.",
      errorField: null,
    });

    const input = screen.getByLabelText("Namn");

    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(input).not.toHaveAttribute("aria-invalid");
    expect(input).not.toHaveAttribute("aria-describedby");
    expect(input).not.toHaveFocus();
  });
});
