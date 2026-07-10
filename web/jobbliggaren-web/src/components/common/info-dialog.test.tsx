import { describe, it, expect } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { InfoDialog } from "./info-dialog";

describe("InfoDialog", () => {
  it("renders the glyph-only trigger and opens with the given title and paragraphs", async () => {
    render(<InfoDialog title="Om hjälpen" paragraphs={["Första", "Andra"]} />);

    // Accessible name from the shared common.dialog string via aria-label;
    // no visible text — the decorative HelpCircle glyph is the whole trigger.
    const trigger = screen.getByRole("button", { name: "Vad är detta?" });
    expect(trigger).toBeInTheDocument();
    expect(trigger).toHaveTextContent("");
    expect(trigger.querySelector("svg")).not.toBeNull();

    fireEvent.click(trigger);

    await waitFor(() =>
      expect(screen.getByRole("dialog")).toBeInTheDocument(),
    );
    expect(screen.getByText("Om hjälpen")).toBeInTheDocument();
    expect(screen.getByText("Första")).toBeInTheDocument();
    expect(screen.getByText("Andra")).toBeInTheDocument();
  });

  it("ariaLabel overrides the accessible name for context-bearing per-control help (#419 pt7)", () => {
    render(
      <InfoDialog
        title="Antal år"
        paragraphs={["Förklaring"]}
        ariaLabel="Vad är detta? Antal år"
      />,
    );

    // WCAG 2.4.4/2.5.3 — unique, context-bearing name replaces the generic
    // fallback so co-existing "?" triggers don't collapse to the same string.
    expect(
      screen.getByRole("button", { name: "Vad är detta? Antal år" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Vad är detta?" }),
    ).not.toBeInTheDocument();
  });

  it("closes via the shared close button", async () => {
    render(<InfoDialog title="Titel" paragraphs={["Text"]} />);
    fireEvent.click(screen.getByRole("button", { name: "Vad är detta?" }));
    await waitFor(() => expect(screen.getByRole("dialog")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("button", { name: "Stäng" }));
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });
});
