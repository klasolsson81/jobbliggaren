import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StatusCard } from "./status-card";
import type { ActionResult } from "@/lib/actions/applications";
import type { ApplicationStatus } from "@/lib/types/applications";

const transitionStatusActionMock =
  vi.fn<
    (applicationId: string, targetStatus: ApplicationStatus) => Promise<ActionResult>
  >();

vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction: (applicationId: string, targetStatus: ApplicationStatus) =>
    transitionStatusActionMock(applicationId, targetStatus),
}));

const baseProps = {
  applicationId: "app-1",
  createdAt: "2026-05-01",
  updatedAt: "2026-05-10",
};

describe("StatusCard", () => {
  beforeEach(() => {
    transitionStatusActionMock.mockReset();
    transitionStatusActionMock.mockResolvedValue({ success: true });
  });

  it("renders current status label with StatusPill always visible", () => {
    render(<StatusCard {...baseProps} currentStatus="Submitted" />);

    expect(screen.getByText("Nuvarande status:")).toBeInTheDocument();
    // "Skickad" is the Swedish label for Submitted
    expect(screen.getByText("Skickad")).toBeInTheDocument();
  });

  it("hides 'Ändra status' button when no allowed transitions exist", () => {
    // Accepted has an empty transition list
    render(<StatusCard {...baseProps} currentStatus="Accepted" />);

    expect(
      screen.queryByRole("button", { name: "Ändra status" })
    ).not.toBeInTheDocument();
    // Current status still rendered
    expect(screen.getByText("Accepterad")).toBeInTheDocument();
  });

  it("shows 'Ändra status' button with aria-expanded=false when transitions exist", () => {
    render(<StatusCard {...baseProps} currentStatus="Submitted" />);

    const btn = screen.getByRole("button", { name: "Ändra status" });
    expect(btn).toHaveAttribute("aria-expanded", "false");
    expect(btn).toHaveAttribute("aria-controls", "status-change-region");
  });

  it("expands disclosure region with transition buttons and aria-expanded=true on click", async () => {
    const user = userEvent.setup();
    render(<StatusCard {...baseProps} currentStatus="Submitted" />);

    await user.click(screen.getByRole("button", { name: "Ändra status" }));

    const toggle = screen.getByRole("button", { name: "Avbryt ändring" });
    expect(toggle).toHaveAttribute("aria-expanded", "true");

    // Submitted -> Acknowledged, Rejected, Withdrawn
    expect(
      screen.getByRole("button", { name: "Bekräftad" })
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Nekad" })).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Återtagen" })
    ).toBeInTheDocument();
    // Disclosure region repeats the current status
    const region = document.getElementById("status-change-region");
    expect(region).not.toBeNull();
    expect(region).toHaveTextContent("Nuvarande status är");
    expect(region).toHaveTextContent("Skickad");
  });

  it("calls transitionStatusAction directly for a non-destructive transition", async () => {
    const user = userEvent.setup();
    render(<StatusCard {...baseProps} currentStatus="Submitted" />);

    await user.click(screen.getByRole("button", { name: "Ändra status" }));
    await user.click(screen.getByRole("button", { name: "Bekräftad" }));

    await waitFor(() => {
      expect(transitionStatusActionMock).toHaveBeenCalledTimes(1);
    });
    expect(transitionStatusActionMock).toHaveBeenCalledWith(
      "app-1",
      "Acknowledged"
    );
    // No confirmation dialog for non-destructive transitions
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("opens confirmation dialog for a destructive transition without calling action", async () => {
    const user = userEvent.setup();
    render(<StatusCard {...baseProps} currentStatus="Submitted" />);

    await user.click(screen.getByRole("button", { name: "Ändra status" }));
    await user.click(screen.getByRole("button", { name: "Nekad" }));

    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("Markera som Nekad?");
    expect(dialog).toHaveTextContent("Skickad");
    expect(dialog).toHaveTextContent("Nekad");
    expect(dialog).toHaveTextContent("Det går inte att ångra");
    // Action NOT called until confirmation
    expect(transitionStatusActionMock).not.toHaveBeenCalled();
  });

  it("calls action only after confirming a destructive transition", async () => {
    const user = userEvent.setup();
    render(<StatusCard {...baseProps} currentStatus="Submitted" />);

    await user.click(screen.getByRole("button", { name: "Ändra status" }));
    await user.click(screen.getByRole("button", { name: "Nekad" }));
    await screen.findByRole("dialog");
    await user.click(
      screen.getByRole("button", { name: "Markera som Nekad" })
    );

    await waitFor(() => {
      expect(transitionStatusActionMock).toHaveBeenCalledTimes(1);
    });
    expect(transitionStatusActionMock).toHaveBeenCalledWith("app-1", "Rejected");
  });

  it("closes confirmation dialog without calling action when 'Avbryt' is clicked", async () => {
    const user = userEvent.setup();
    render(<StatusCard {...baseProps} currentStatus="Submitted" />);

    await user.click(screen.getByRole("button", { name: "Ändra status" }));
    await user.click(screen.getByRole("button", { name: "Återtagen" }));
    await screen.findByRole("dialog");
    await user.click(screen.getByRole("button", { name: "Avbryt" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(transitionStatusActionMock).not.toHaveBeenCalled();
  });

  it("shows role=alert with error text when action fails", async () => {
    transitionStatusActionMock.mockResolvedValueOnce({
      success: false,
      error: "Övergången är inte tillåten.",
    });
    const user = userEvent.setup();
    render(<StatusCard {...baseProps} currentStatus="Submitted" />);

    await user.click(screen.getByRole("button", { name: "Ändra status" }));
    await user.click(screen.getByRole("button", { name: "Bekräftad" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Övergången är inte tillåten.");
  });

  it("'Avbryt ändring' toggles disclosure closed (aria-expanded=false) and clears error", async () => {
    transitionStatusActionMock.mockResolvedValueOnce({
      success: false,
      error: "Övergången är inte tillåten.",
    });
    const user = userEvent.setup();
    render(<StatusCard {...baseProps} currentStatus="Submitted" />);

    await user.click(screen.getByRole("button", { name: "Ändra status" }));
    await user.click(screen.getByRole("button", { name: "Bekräftad" }));
    await screen.findByRole("alert");

    await user.click(screen.getByRole("button", { name: "Avbryt ändring" }));

    const btn = screen.getByRole("button", { name: "Ändra status" });
    expect(btn).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
