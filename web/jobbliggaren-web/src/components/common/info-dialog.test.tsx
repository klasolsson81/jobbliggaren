import { describe, it, expect } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { InfoDialog } from "./info-dialog";

describe("InfoDialog", () => {
  it("shows the shared trigger and opens with the given title and paragraphs", async () => {
    render(<InfoDialog title="Om hjälpen" paragraphs={["Första", "Andra"]} />);

    const trigger = screen.getByRole("button", { name: "Vad är detta?" });
    expect(trigger).toBeInTheDocument();

    fireEvent.click(trigger);

    await waitFor(() =>
      expect(screen.getByRole("dialog")).toBeInTheDocument(),
    );
    expect(screen.getByText("Om hjälpen")).toBeInTheDocument();
    expect(screen.getByText("Första")).toBeInTheDocument();
    expect(screen.getByText("Andra")).toBeInTheDocument();
  });

  it("renders the icon by default and omits it when showIcon is false", () => {
    const { rerender } = render(
      <InfoDialog title="Titel" paragraphs={["Text"]} />,
    );
    // Default: decorative HelpCircle icon present inside the trigger button.
    expect(
      screen.getByRole("button", { name: "Vad är detta?" }).querySelector("svg"),
    ).not.toBeNull();

    // #337 text-only placement convention: no icon, button still accessible.
    rerender(
      <InfoDialog title="Titel" paragraphs={["Text"]} showIcon={false} />,
    );
    const trigger = screen.getByRole("button", { name: "Vad är detta?" });
    expect(trigger).toBeInTheDocument();
    expect(trigger.querySelector("svg")).toBeNull();
  });

  it("icon-only mode: renders just the glyph, accessible name from aria-label, opens both paragraphs (#408)", async () => {
    render(
      <InfoDialog
        iconOnly
        title="Om matchningsfiltret"
        paragraphs={["Första stycket", "Andra stycket"]}
      />,
    );
    // Accessible name still "Vad är detta?" (shared common.dialog string) via
    // aria-label; no visible text on the dense control row.
    const trigger = screen.getByRole("button", { name: "Vad är detta?" });
    expect(trigger).toBeInTheDocument();
    expect(trigger).toHaveTextContent("");
    // The decorative HelpCircle glyph is present.
    expect(trigger.querySelector("svg")).not.toBeNull();

    fireEvent.click(trigger);
    await waitFor(() =>
      expect(screen.getByRole("dialog")).toBeInTheDocument(),
    );
    // Both verbatim paragraphs render (criterion 9: help + relatedToggleHelp).
    expect(screen.getByText("Första stycket")).toBeInTheDocument();
    expect(screen.getByText("Andra stycket")).toBeInTheDocument();
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
