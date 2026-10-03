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

describe("HarAnsoktButton (#1863, #1855, #1963)", () => {
  it("rests as a button in the info tone, not a pressed toggle", () => {
    render(<HarAnsoktButton jobAdId="j1" initialApplied={false} />);
    const button = screen.getByRole("button", { name: "Markera som ansökt" });
    expect(button).toHaveClass("jp-btn", "jp-btn--info-soft");
    expect(button).not.toHaveAttribute("aria-pressed");
  });

  it("shows an ad applied before this mount as a status with a link to the applications list", () => {
    render(<HarAnsoktButton jobAdId="j1" initialApplied={true} />);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    const link = screen.getByRole("link", { name: "Visa ansökan" });
    // The server only says that an application exists, and one ad can carry several.
    expect(link).toHaveAttribute("href", "/ansokningar");
    expect(link).toHaveAccessibleDescription("Ansökt");
  });

  it("marks the ad, then links to the application the action created and moves focus to the link", async () => {
    createActionMock.mockResolvedValue({ success: true, applicationId: "a-123" });
    render(<HarAnsoktButton jobAdId="j1" initialApplied={false} />);

    await userEvent.setup().click(screen.getByRole("button", { name: "Markera som ansökt" }));

    expect(createActionMock).toHaveBeenCalledWith("j1");
    const link = await screen.findByRole("link", { name: "Visa ansökan" });
    expect(link).toHaveFocus();
    expect(await screen.findByRole("link", { name: "Visa ansökan" })).toHaveAttribute(
      "href",
      "/ansokningar/a-123",
    );
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("rolls back on failure: the button returns, takes focus and the error is announced", async () => {
    createActionMock.mockResolvedValue({ success: false, error: "Kunde inte registrera ansökan." });
    render(<HarAnsoktButton jobAdId="j1" initialApplied={false} />);

    await userEvent.setup().click(screen.getByRole("button", { name: "Markera som ansökt" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Kunde inte registrera ansökan.");
    const button = screen.getByRole("button", { name: "Markera som ansökt" });
    expect(button).toHaveFocus();
    expect(screen.queryByRole("link", { name: "Visa ansökan" })).not.toBeInTheDocument();
  });
});
