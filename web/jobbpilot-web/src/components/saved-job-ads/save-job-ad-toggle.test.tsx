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
      screen.getByRole("button", { name: /Spara annonsen som bokmärke/i })
    ).toBeInTheDocument();
    expect(screen.getByText("Spara")).toBeInTheDocument();
  });

  it("renders 'Sparad' when initialSaved is true", () => {
    render(<SaveJobAdToggle jobAdId="j1" initialSaved={true} />);
    expect(
      screen.getByRole("button", { name: /Ta bort bokmärke för annonsen/i })
    ).toBeInTheDocument();
    expect(screen.getByText("Sparad")).toBeInTheDocument();
  });

  it("calls saveJobAdAction on click when not saved (optimistic flip)", async () => {
    saveActionMock.mockResolvedValue({ success: true });
    render(<SaveJobAdToggle jobAdId="j1" initialSaved={false} />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: /Spara annonsen som bokmärke/i })
    );

    expect(saveActionMock).toHaveBeenCalledWith("j1");
    expect(await screen.findByText("Sparad")).toBeInTheDocument();
  });

  it("calls unsaveJobAdAction on click when already saved", async () => {
    unsaveActionMock.mockResolvedValue({ success: true });
    render(<SaveJobAdToggle jobAdId="j1" initialSaved={true} />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: /Ta bort bokmärke för annonsen/i })
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
      screen.getByRole("button", { name: /Spara annonsen som bokmärke/i })
    );

    expect(
      await screen.findByText(/Kunde inte spara annonsen/i)
    ).toBeInTheDocument();
    // Tillbaka till "Spara" efter rollback
    expect(screen.getByText("Spara")).toBeInTheDocument();
  });
});
