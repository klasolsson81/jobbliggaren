import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { DrawerStatusActions } from "./drawer-status-actions";
import {
  dismissApplicationToast,
  getApplicationToastSnapshot,
} from "@/lib/applications/toast-store";

const transitionStatusAction = vi.hoisted(() =>
  vi.fn(async () => ({ success: true as const })),
);
vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction,
}));

const APP_ID = "11111111-2222-3333-4444-555555555555";

beforeEach(() => {
  transitionStatusAction.mockReset();
  transitionStatusAction.mockResolvedValue({ success: true as const });
  dismissApplicationToast();
});

describe("DrawerStatusActions (§8.3–8.5, #630 PR 7)", () => {
  it("primär-CTA:n flyttar till nästa steg och publicerar ångra-toasten", async () => {
    render(
      <DrawerStatusActions
        applicationId={APP_ID}
        status="Submitted"
        displayName="Volvo"
      />,
    );
    fireEvent.click(
      screen.getByRole("button", { name: "Flytta till Bekräftad" }),
    );
    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(
        APP_ID,
        "Acknowledged",
      ),
    );
    await waitFor(() =>
      expect(getApplicationToastSnapshot()).toMatchObject({
        kind: "statusChange",
        company: "Volvo",
        from: "Submitted",
        to: "Acknowledged",
      }),
    );
  });

  it("Ghosted: CTA:n är 'Återaktivera som Skickad' (prototyp-facit)", async () => {
    render(
      <DrawerStatusActions
        applicationId={APP_ID}
        status="Ghosted"
        displayName="Volvo"
      />,
    );
    fireEvent.click(
      screen.getByRole("button", { name: "Återaktivera som Skickad" }),
    );
    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(APP_ID, "Submitted"),
    );
  });

  it("terminala statusar: ingen primär-CTA, men stegväljare + park kvarstår", () => {
    render(
      <DrawerStatusActions
        applicationId={APP_ID}
        status="Rejected"
        displayName="Volvo"
      />,
    );
    expect(screen.queryByText(/Flytta till/)).not.toBeInTheDocument();
    expect(document.querySelectorAll(".jp-steppicker__step")).toHaveLength(7);
    // Nekad är aktiv i park-raden → markerad och disabled.
    const rejected = screen.getByRole("button", { name: "Nekad" });
    expect(rejected).toHaveAttribute("aria-pressed", "true");
    expect(rejected).toBeDisabled();
  });

  it("stegväljaren byter status DIREKT, även bakåt (fria byten, D3)", async () => {
    render(
      <DrawerStatusActions
        applicationId={APP_ID}
        status="Interviewing"
        displayName="Volvo"
      />,
    );
    // Bakåt: Intervjuar (steg 5) → Skickad (steg 2).
    fireEvent.click(screen.getByRole("button", { name: /Skickad/ }));
    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(APP_ID, "Submitted"),
    );
  });

  it("nuvarande steg är disabled med aria-current (self-transition = no-op)", () => {
    render(
      <DrawerStatusActions
        applicationId={APP_ID}
        status="Submitted"
        displayName="Volvo"
      />,
    );
    const current = document.querySelector('[data-state="current"]');
    expect(current).toHaveAttribute("aria-current", "step");
    expect(current).toBeDisabled();
    expect(current).toHaveTextContent("Nu");
  });

  it("klarade steg markeras done; framtida är klickbara", () => {
    render(
      <DrawerStatusActions
        applicationId={APP_ID}
        status="Interviewing"
        displayName="Volvo"
      />,
    );
    const steps = [...document.querySelectorAll(".jp-steppicker__step")];
    expect(
      steps.filter((s) => s.getAttribute("data-state") === "done"),
    ).toHaveLength(4);
    expect(
      steps.filter((s) => s.getAttribute("data-state") === "future"),
    ).toHaveLength(2);
  });

  it("park-knappen Ghosted transitionerar och Nekad bär dangertext-klassen", async () => {
    render(
      <DrawerStatusActions
        applicationId={APP_ID}
        status="Submitted"
        displayName="Volvo"
      />,
    );
    expect(screen.getByRole("button", { name: "Nekad" })).toHaveClass(
      "jp-parkbtn--danger",
    );
    fireEvent.click(screen.getByRole("button", { name: "Inget svar" }));
    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(APP_ID, "Ghosted"),
    );
  });

  it("fel visas inline i panelen (role=alert), ingen toast", async () => {
    transitionStatusAction.mockResolvedValueOnce({
      success: false as const,
      error: "Statusbytet misslyckades.",
    } as never);
    render(
      <DrawerStatusActions
        applicationId={APP_ID}
        status="Submitted"
        displayName="Volvo"
      />,
    );
    fireEvent.click(
      screen.getByRole("button", { name: "Flytta till Bekräftad" }),
    );
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Statusbytet misslyckades.",
    );
    expect(getApplicationToastSnapshot()).toBeNull();
  });
});
