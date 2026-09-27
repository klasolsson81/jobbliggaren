import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TerminalMoveDialog } from "./terminal-move-dialog";

describe("TerminalMoveDialog (#1827 — the consequence before a terminal move)", () => {
  it("names the move and states what is deleted, with a button naming the action", () => {
    render(
      <TerminalMoveDialog
        open
        onOpenChange={vi.fn()}
        target="Rejected"
        onConfirm={vi.fn()}
      />,
    );

    const dialog = screen.getByRole("dialog", { name: "Markera som Nekad?" });
    expect(dialog).toHaveAccessibleDescription(
      "Annonstexten och eventuella kontaktuppgifter i den sparade kopian raderas och kommer inte tillbaka om du ångrar.",
    );
    expect(
      screen.getByRole("button", { name: "Markera som Nekad" }),
    ).toBeInTheDocument();
  });

  it("confirming closes the dialog and hands the move to the caller", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    const onConfirm = vi.fn();
    render(
      <TerminalMoveDialog
        open
        onOpenChange={onOpenChange}
        target="Withdrawn"
        onConfirm={onConfirm}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Markera som Återtagen" }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it("cancelling closes the dialog and moves nothing", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    const onConfirm = vi.fn();
    render(
      <TerminalMoveDialog
        open
        onOpenChange={onOpenChange}
        target="Accepted"
        onConfirm={onConfirm}
      />,
    );

    await user.click(screen.getByRole("button", { name: "Avbryt" }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(onConfirm).not.toHaveBeenCalled();
  });
});
