import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HarAnsoktButton } from "./har-ansokt-button";

const createActionMock = vi.fn();

vi.mock("@/lib/actions/applications", () => ({
  createApplicationFromJobAdAction: (...args: unknown[]) =>
    createActionMock(...args),
}));

beforeEach(() => {
  createActionMock.mockReset();
});

describe("HarAnsoktButton", () => {
  it("renders 'Har ansökt'-knapp i idle-state", () => {
    render(<HarAnsoktButton jobAdId="j1" />);
    expect(
      screen.getByRole("button", { name: /Har ansökt/i })
    ).toBeInTheDocument();
  });

  it("kallar createApplicationFromJobAdAction vid klick", async () => {
    createActionMock.mockResolvedValue({
      success: true,
      applicationId: "a-123",
    });
    render(<HarAnsoktButton jobAdId="j1" />);

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /Har ansökt/i }));

    expect(createActionMock).toHaveBeenCalledWith("j1");
  });

  it("visar success-tillstånd med länk till ansökan", async () => {
    createActionMock.mockResolvedValue({
      success: true,
      applicationId: "a-456",
    });
    render(<HarAnsoktButton jobAdId="j1" />);

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /Har ansökt/i }));

    expect(await screen.findByText(/Sparad som ansökan/i)).toBeInTheDocument();
    const link = screen.getByRole("link", { name: /Öppna ansökan/i });
    expect(link).toHaveAttribute("href", "/ansokningar/a-456");
  });

  it("visar felmeddelande vid backend-fel", async () => {
    createActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte registrera ansökan.",
    });
    render(<HarAnsoktButton jobAdId="j1" />);

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /Har ansökt/i }));

    expect(
      await screen.findByText(/Kunde inte registrera ansökan/i)
    ).toBeInTheDocument();
    // Knappen finns kvar (kan klickas igen)
    expect(
      screen.getByRole("button", { name: /Har ansökt/i })
    ).toBeInTheDocument();
  });
});
