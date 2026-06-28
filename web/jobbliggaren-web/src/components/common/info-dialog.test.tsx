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
