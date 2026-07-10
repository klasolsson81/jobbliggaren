import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DiscardDraftButton } from "./discard-draft-button";
import type { ActionResult } from "@/lib/actions/resumes";

/**
 * Fas 4b PR-8.3 — hubbens åtgärdskorts "Ta bort utkastet". Confirm-dialog-ö som
 * speglar destructive-confirm-mönstret (specifik knapp-text, ALDRIG "Är du säker"/
 * "Bekräfta"). `discardParsedResumeAction` mockas så testet inte når nätverket.
 */

const discardParsedResumeActionMock =
  vi.fn<(parsedId: string) => Promise<ActionResult>>();

vi.mock("@/lib/actions/resumes", () => ({
  discardParsedResumeAction: (parsedId: string) =>
    discardParsedResumeActionMock(parsedId),
}));

const PARSED_ID = "11111111-1111-4111-8111-111111111111";

describe("DiscardDraftButton", () => {
  beforeEach(() => {
    discardParsedResumeActionMock.mockReset();
    discardParsedResumeActionMock.mockResolvedValue({ success: true });
  });

  it("renderar trigger-knappen utan öppen dialog initialt", () => {
    render(<DiscardDraftButton parsedId={PARSED_ID} />);
    expect(
      screen.getByRole("button", { name: "Ta bort utkastet" }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("öppnar bekräfta-dialogen med specifik knapp-text 'Ta bort utkastet' (aldrig 'Bekräfta')", async () => {
    const user = userEvent.setup();
    render(<DiscardDraftButton parsedId={PARSED_ID} />);

    await user.click(screen.getByRole("button", { name: "Ta bort utkastet" }));

    const dialog = screen.getByRole("dialog");
    // Titeln bär frågan; bekräfta-knappen bär den specifika handlingen.
    expect(within(dialog).getByText("Ta bort utkastet?")).toBeInTheDocument();
    expect(
      within(dialog).getByRole("button", { name: "Ta bort utkastet" }),
    ).toBeInTheDocument();
    // Aldrig en generisk "Bekräfta" (destructive-confirm-doktrin).
    expect(
      within(dialog).queryByRole("button", { name: "Bekräfta" }),
    ).not.toBeInTheDocument();
  });

  it("bekräftelse anropar discardParsedResumeAction med parsedId", async () => {
    const user = userEvent.setup();
    render(<DiscardDraftButton parsedId={PARSED_ID} />);

    await user.click(screen.getByRole("button", { name: "Ta bort utkastet" }));
    const dialog = screen.getByRole("dialog");
    await user.click(
      within(dialog).getByRole("button", { name: "Ta bort utkastet" }),
    );

    await waitFor(() =>
      expect(discardParsedResumeActionMock).toHaveBeenCalledTimes(1),
    );
    expect(discardParsedResumeActionMock).toHaveBeenCalledWith(PARSED_ID);
  });

  it("visar felet i en live region (role=alert) när action returnerar { success:false }", async () => {
    discardParsedResumeActionMock.mockResolvedValueOnce({
      success: false,
      error: "Kunde inte ta bort utkastet.",
    });
    const user = userEvent.setup();
    render(<DiscardDraftButton parsedId={PARSED_ID} />);

    await user.click(screen.getByRole("button", { name: "Ta bort utkastet" }));
    const dialog = screen.getByRole("dialog");
    await user.click(
      within(dialog).getByRole("button", { name: "Ta bort utkastet" }),
    );

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Kunde inte ta bort utkastet.");
    // Dialogen hålls öppen vid fel.
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("Avbryt stänger dialogen utan att anropa action", async () => {
    const user = userEvent.setup();
    render(<DiscardDraftButton parsedId={PARSED_ID} />);

    await user.click(screen.getByRole("button", { name: "Ta bort utkastet" }));
    const dialog = screen.getByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Avbryt" }));

    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
    expect(discardParsedResumeActionMock).not.toHaveBeenCalled();
  });
});
