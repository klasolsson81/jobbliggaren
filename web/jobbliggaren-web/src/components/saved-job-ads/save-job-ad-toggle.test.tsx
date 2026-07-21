import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SaveJobAdToggle } from "./save-job-ad-toggle";

const saveActionMock = vi.fn();
const unsaveActionMock = vi.fn();

vi.mock("@/lib/actions/saved-job-ads", () => ({
  saveJobAdAction: (...args: unknown[]) => saveActionMock(...args),
  unsaveJobAdAction: (...args: unknown[]) => unsaveActionMock(...args),
}));

beforeEach(() => {
  saveActionMock.mockReset();
  unsaveActionMock.mockReset();
});

describe("SaveJobAdToggle", () => {
  it("renders 'Spara' when initialSaved is false", () => {
    render(<SaveJobAdToggle jobAdId="j1" initialSaved={false} />);
    expect(
      screen.getByRole("button", { name: "Spara" })
    ).toBeInTheDocument();
    expect(screen.getByText("Spara")).toBeInTheDocument();
  });

  it("renders 'Sparad' when initialSaved is true", () => {
    render(<SaveJobAdToggle jobAdId="j1" initialSaved={true} />);
    expect(
      screen.getByRole("button", { name: "Sparad" })
    ).toBeInTheDocument();
    expect(screen.getByText("Sparad")).toBeInTheDocument();
    // #1000 (V1) — blue state-tint when saved (matches the SPARAD tag).
    expect(screen.getByRole("button", { name: "Sparad" })).toHaveClass(
      "jp-btn--on-saved"
    );
  });

  it("calls saveJobAdAction on click when not saved (optimistic flip)", async () => {
    saveActionMock.mockResolvedValue({ success: true });
    render(<SaveJobAdToggle jobAdId="j1" initialSaved={false} />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: "Spara" })
    );

    expect(saveActionMock).toHaveBeenCalledWith("j1");
    expect(await screen.findByText("Sparad")).toBeInTheDocument();
  });

  it("calls unsaveJobAdAction on click when already saved", async () => {
    unsaveActionMock.mockResolvedValue({ success: true });
    render(<SaveJobAdToggle jobAdId="j1" initialSaved={true} />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: "Sparad" })
    );

    expect(unsaveActionMock).toHaveBeenCalledWith("j1");
    expect(await screen.findByText("Spara")).toBeInTheDocument();
  });

  it("rolls back optimistic state on failure", async () => {
    saveActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte spara annonsen. Försök igen.",
    });
    render(<SaveJobAdToggle jobAdId="j1" initialSaved={false} />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: "Spara" })
    );

    expect(
      await screen.findByText(/Kunde inte spara annonsen/i)
    ).toBeInTheDocument();
    // Tillbaka till "Spara" efter rollback
    expect(screen.getByText("Spara")).toBeInTheDocument();
  });

  it("default variant's accessible name is the visible label (WCAG 2.5.3)", () => {
    // Fresh mounts, not rerender: `saved` seeds mount-only from the prop via useState.
    const { unmount } = render(
      <SaveJobAdToggle jobAdId="j1" initialSaved={false} />
    );
    expect(screen.getByRole("button")).toHaveAccessibleName("Spara");
    unmount();
    render(<SaveJobAdToggle jobAdId="j1" initialSaved={true} />);
    // Saved: visible "Sparad" is the accessible name (previously "Ta bort bokmärke…" — a 2.5.3 break).
    expect(screen.getByRole("button")).toHaveAccessibleName("Sparad");
  });

  it("keeps an aria-label on the icon-only compact variant (no visible text → 2.5.3 N/A)", () => {
    const { unmount } = render(
      <SaveJobAdToggle jobAdId="j1" initialSaved={false} variant="compact" />
    );
    expect(screen.getByRole("button")).toHaveAccessibleName(
      "Spara annonsen som bokmärke"
    );
    unmount();
    render(<SaveJobAdToggle jobAdId="j1" initialSaved={true} variant="compact" />);
    expect(screen.getByRole("button")).toHaveAccessibleName(
      "Ta bort bokmärke för annonsen"
    );
  });
});
