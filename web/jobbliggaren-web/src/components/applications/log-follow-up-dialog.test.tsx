import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { LogFollowUpDialog } from "./log-follow-up-dialog";
import {
  dismissApplicationToast,
  getApplicationToastSnapshot,
} from "@/lib/applications/toast-store";

const logFollowUpAction = vi.hoisted(() =>
  vi.fn(async () => ({ success: true as const })),
);
vi.mock("@/lib/actions/applications", () => ({
  logFollowUpAction,
}));

const APP_ID = "11111111-2222-3333-4444-555555555555";

function renderDialog(
  props: Partial<React.ComponentProps<typeof LogFollowUpDialog>> = {},
) {
  const onOpenChange = vi.fn();
  render(
    <LogFollowUpDialog
      open
      onOpenChange={onOpenChange}
      applicationId={APP_ID}
      contextTitle="Backend-utvecklare"
      contextCompany="Volvo"
      toastCompany="Volvo"
      {...props}
    />,
  );
  return { onOpenChange };
}

beforeEach(() => {
  logFollowUpAction.mockReset();
  logFollowUpAction.mockResolvedValue({ success: true as const });
  dismissApplicationToast();
});

describe("LogFollowUpDialog (design §9, #630 PR 7)", () => {
  it("renderar titel, kontextrad, notering + hjälptext — INGEN exempeltext som placeholder", () => {
    renderDialog();
    expect(
      screen.getByRole("dialog", { name: "Logga uppföljning" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Backend-utvecklare · Volvo"),
    ).toBeInTheDocument();
    const textarea = screen.getByLabelText("Notering (valfri)");
    expect(textarea).not.toHaveAttribute("placeholder");
    expect(
      screen.getByText(/Väntetiden räknas om från i dag/),
    ).toBeInTheDocument();
  });

  it("Spara → logFollowUpAction + uppföljningstoast (utan Ångra) + stängning", async () => {
    const { onOpenChange } = renderDialog();
    fireEvent.change(screen.getByLabelText("Notering (valfri)"), {
      target: { value: "Ringde kontaktpersonen." },
    });
    fireEvent.click(screen.getByRole("button", { name: "Spara uppföljning" }));

    await waitFor(() =>
      expect(logFollowUpAction).toHaveBeenCalledWith(
        APP_ID,
        "Ringde kontaktpersonen.",
      ),
    );
    await waitFor(() =>
      expect(getApplicationToastSnapshot()).toMatchObject({
        kind: "followUpLogged",
        company: "Volvo",
      }),
    );
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("fel visas inline i dialogen (role=alert); dialogen förblir öppen", async () => {
    logFollowUpAction.mockResolvedValueOnce({
      success: false as const,
      error: "Kunde inte logga uppföljningen.",
    } as never);
    const { onOpenChange } = renderDialog();
    fireEvent.click(screen.getByRole("button", { name: "Spara uppföljning" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Kunde inte logga uppföljningen.",
    );
    expect(onOpenChange).not.toHaveBeenCalled();
    expect(getApplicationToastSnapshot()).toBeNull();
  });

  it("ankrad position sätter top-stil (nära klicket, §9)", () => {
    renderDialog({ top: 220 });
    const content = document.querySelector('[data-slot="dialog-content"]');
    expect(content).toHaveStyle({ top: "220px" });
  });

  it("Avbryt stänger utan att spara", () => {
    const { onOpenChange } = renderDialog();
    fireEvent.click(screen.getByRole("button", { name: "Avbryt" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(logFollowUpAction).not.toHaveBeenCalled();
  });
});
