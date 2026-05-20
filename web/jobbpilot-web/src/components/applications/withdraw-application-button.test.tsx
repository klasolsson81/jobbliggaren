import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { WithdrawApplicationButton } from "./withdraw-application-button";
import type { ActionResult } from "@/lib/actions/applications";

const transitionStatusActionMock =
  vi.fn<(id: string, target: string) => Promise<ActionResult>>();

vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction: (id: string, target: string) =>
    transitionStatusActionMock(id, target),
}));

describe("WithdrawApplicationButton (ADR 0047 Area 5 — destruktiv bekräftelse)", () => {
  beforeEach(() => {
    transitionStatusActionMock.mockReset();
    transitionStatusActionMock.mockResolvedValue({ success: true });
  });

  it("öppnar bekräftelse-dialog FÖRE handling, ej direkt transition", async () => {
    const user = userEvent.setup();
    render(
      <WithdrawApplicationButton
        applicationId="app-1"
        currentStatus="Submitted"
      />
    );

    await user.click(screen.getByRole("button", { name: "Återta ansökan" }));

    // Konsekvenstext visas FÖRE handling; ingen transition ännu.
    expect(
      screen.getByRole("dialog", { name: "Återta ansökan?" })
    ).toBeInTheDocument();
    expect(
      screen.getByText(/avslutas och kan inte ändras vidare/)
    ).toBeInTheDocument();
    expect(transitionStatusActionMock).not.toHaveBeenCalled();
  });

  it("bekräftelse triggar Withdrawn-transition (domän-korrekt, ej hard-delete)", async () => {
    const user = userEvent.setup();
    render(
      <WithdrawApplicationButton
        applicationId="app-1"
        currentStatus="Submitted"
      />
    );

    await user.click(screen.getByRole("button", { name: "Återta ansökan" }));
    const dialog = screen.getByRole("dialog");
    // within(dialog)-scoped: confirm-knappen i dialogen delar namn med
    // trigger-knappen — scoped query är robust mot .at(-1)-skörhet
    // (F5 code-reviewer m2).
    await user.click(
      within(dialog).getByRole("button", { name: "Återta ansökan" })
    );

    await waitFor(() =>
      expect(transitionStatusActionMock).toHaveBeenCalledWith(
        "app-1",
        "Withdrawn"
      )
    );
  });

  it("Avbryt stänger dialogen utan transition", async () => {
    const user = userEvent.setup();
    render(
      <WithdrawApplicationButton
        applicationId="app-1"
        currentStatus="Submitted"
      />
    );

    await user.click(screen.getByRole("button", { name: "Återta ansökan" }));
    await user.click(screen.getByRole("button", { name: "Avbryt" }));

    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument()
    );
    expect(transitionStatusActionMock).not.toHaveBeenCalled();
  });

  it("visar serverfel i dialogen vid misslyckad transition", async () => {
    transitionStatusActionMock.mockResolvedValue({
      success: false,
      error: "Statusbytet misslyckades.",
    });
    const user = userEvent.setup();
    render(
      <WithdrawApplicationButton
        applicationId="app-1"
        currentStatus="Submitted"
      />
    );

    await user.click(screen.getByRole("button", { name: "Återta ansökan" }));
    const dialog = screen.getByRole("dialog");
    await user.click(
      within(dialog).getByRole("button", { name: "Återta ansökan" })
    );

    expect(
      await screen.findByText("Statusbytet misslyckades.")
    ).toBeInTheDocument();
  });
});
