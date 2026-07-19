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

describe("HarAnsoktButton (PR5)", () => {
  it("renderar 'Markera som ansökt' när initialApplied=false", () => {
    render(<HarAnsoktButton jobAdId="j1" initialApplied={false} />);
    expect(
      screen.getByRole("button", { name: "Markera som ansökt" })
    ).toBeInTheDocument();
    expect(screen.getByText("Markera som ansökt")).toBeInTheDocument();
  });

  it("renderar 'Ansökt' när initialApplied=true (paritet server-state)", () => {
    render(<HarAnsoktButton jobAdId="j1" initialApplied={true} />);
    expect(
      screen.getByRole("button", { name: "Ansökt" })
    ).toBeInTheDocument();
    expect(screen.getByText("Ansökt")).toBeInTheDocument();
  });

  it("kallar action vid klick + uppdaterar UI optimistic", async () => {
    createActionMock.mockResolvedValue({
      success: true,
      applicationId: "a-123",
    });
    render(<HarAnsoktButton jobAdId="j1" initialApplied={false} />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: "Markera som ansökt" })
    );

    expect(createActionMock).toHaveBeenCalledWith("j1");
    expect(await screen.findByText("Ansökt")).toBeInTheDocument();
  });

  it("idempotent — klick när redan applied gör inget", async () => {
    render(<HarAnsoktButton jobAdId="j1" initialApplied={true} />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: "Ansökt" })
    );

    expect(createActionMock).not.toHaveBeenCalled();
  });

  it("rullbar vid fel — knappen återställs + felmeddelande", async () => {
    createActionMock.mockResolvedValue({
      success: false,
      error: "Kunde inte registrera ansökan.",
    });
    render(<HarAnsoktButton jobAdId="j1" initialApplied={false} />);

    const user = userEvent.setup();
    await user.click(
      screen.getByRole("button", { name: "Markera som ansökt" })
    );

    expect(
      await screen.findByText(/Kunde inte registrera ansökan/i)
    ).toBeInTheDocument();
    expect(screen.getByText("Markera som ansökt")).toBeInTheDocument();
  });

  it("accessible name is the visible label in both states (WCAG 2.5.3, no aria override)", () => {
    // Fresh mounts, not rerender: `applied` seeds mount-only from the prop via useState.
    const { unmount } = render(
      <HarAnsoktButton jobAdId="j1" initialApplied={false} />
    );
    expect(screen.getByRole("button")).toHaveAccessibleName("Markera som ansökt");
    unmount();
    render(<HarAnsoktButton jobAdId="j1" initialApplied={true} />);
    // Applied: visible "Ansökt" is the name (previously "Du har markerat…" — a 2.5.3 break).
    expect(screen.getByRole("button")).toHaveAccessibleName("Ansökt");
  });
});
