import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { NotesSection } from "./notes-section";
import type { NoteDto } from "@/lib/types/applications";

vi.mock("@/lib/actions/applications", () => ({
  addNoteAction: vi.fn().mockResolvedValue({ success: true }),
}));

const baseNote = (overrides: Partial<NoteDto> = {}): NoteDto => ({
  id: "n1",
  content: "Första raden av en anteckning\nAndra raden som ska döljas",
  createdAt: "2026-05-10T08:00:00Z",
  ...overrides,
});

describe("NotesSection — disclosure-mönster (Prompt 4)", () => {
  it("renderar kompakt vy med första raden synlig och resten dold", () => {
    render(
      <NotesSection applicationId="app-1" notes={[baseNote()]} />,
    );
    expect(screen.getByText("Första raden av en anteckning")).toBeInTheDocument();
    expect(
      screen.queryByText("Andra raden som ska döljas"),
    ).not.toBeInTheDocument();
  });

  it("klick på kompakt rad expanderar och visar full text", () => {
    render(
      <NotesSection applicationId="app-1" notes={[baseNote()]} />,
    );
    const row = screen.getByRole("button", { expanded: false });
    fireEvent.click(row);
    expect(screen.getByRole("button", { expanded: true })).toBeInTheDocument();
    // Full text (med whitespace-pre-line) syns i body.
    expect(
      screen.getByText(/Andra raden som ska döljas/),
    ).toBeInTheDocument();
  });

  it("endast EN rad expanderad åt gången (single-expand)", () => {
    render(
      <NotesSection
        applicationId="app-1"
        notes={[
          baseNote({ id: "n1", content: "Not A" }),
          baseNote({ id: "n2", content: "Not B" }),
        ]}
      />,
    );
    const rows = screen.getAllByRole("button", { name: /Not / });
    fireEvent.click(rows[0]!);
    expect(rows[0]).toHaveAttribute("aria-expanded", "true");
    fireEvent.click(rows[1]!);
    expect(rows[0]).toHaveAttribute("aria-expanded", "false");
    expect(rows[1]).toHaveAttribute("aria-expanded", "true");
  });

  it("Esc-tangent kollapsar aktiv expanderad rad", () => {
    render(
      <NotesSection applicationId="app-1" notes={[baseNote()]} />,
    );
    const row = screen.getByRole("button", { expanded: false });
    fireEvent.click(row);
    expect(screen.getByRole("button", { expanded: true })).toBeInTheDocument();
    fireEvent.keyDown(window, { key: "Escape" });
    expect(screen.getByRole("button", { expanded: false })).toBeInTheDocument();
  });

  it("default visar '+ Lägg till anteckning'-knapp, ej form", () => {
    render(<NotesSection applicationId="app-1" notes={[]} />);
    expect(
      screen.getByRole("button", { name: /Lägg till anteckning/ }),
    ).toBeInTheDocument();
    // Textarea med name=content syns ej initialt.
    expect(screen.queryByLabelText("Notering")).not.toBeInTheDocument();
  });

  it("klick på Lägg till-knapp expanderar form, Avbryt kollapsar", () => {
    render(<NotesSection applicationId="app-1" notes={[]} />);
    fireEvent.click(
      screen.getByRole("button", { name: /Lägg till anteckning/ }),
    );
    expect(screen.getByLabelText("Notering")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Avbryt" }));
    expect(screen.queryByLabelText("Notering")).not.toBeInTheDocument();
  });

  it("renderar empty-state när inga anteckningar", () => {
    render(<NotesSection applicationId="app-1" notes={[]} />);
    expect(screen.getByText("Inga anteckningar ännu.")).toBeInTheDocument();
  });
});
