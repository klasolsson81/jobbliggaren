import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ChangePasswordCard } from "./change-password-card";
import type { ActionResult } from "@/lib/actions/_action-result";

const changePasswordActionMock =
  vi.fn<(current: string, next: string) => Promise<ActionResult>>();

vi.mock("@/lib/actions/me", () => ({
  changePasswordAction: (current: string, next: string) =>
    changePasswordActionMock(current, next),
}));

// The card reuses <ReAuthDialog>: the dialog owns the CURRENT password (its re-auth
// field), the card injects the new + confirm fields and gates submit on
// new >= 12 && new === confirm. `render` is auto-wrapped in the Swedish catalog.
const CURRENT = "Current123456";
const NEW = "NyttL0senord123456";

describe("ChangePasswordCard", () => {
  beforeEach(() => {
    changePasswordActionMock.mockReset();
    changePasswordActionMock.mockResolvedValue({ success: true });
  });

  async function openDialog(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole("button", { name: "Byt lösenord" }));
    // Scope to the dialog so the trigger (same label) is never matched.
    return within(await screen.findByRole("dialog"));
  }

  it("renders the trigger without the dialog open", () => {
    render(<ChangePasswordCard />);
    expect(screen.getByRole("button", { name: "Byt lösenord" })).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("keeps submit disabled until new meets the floor and matches confirm", async () => {
    const user = userEvent.setup();
    render(<ChangePasswordCard />);
    const dialog = await openDialog(user);
    const submit = dialog.getByRole("button", { name: "Byt lösenord" });

    await user.type(dialog.getByLabelText("Nuvarande lösenord"), CURRENT);
    expect(submit).toBeDisabled(); // new + confirm still empty

    await user.type(dialog.getByLabelText("Nytt lösenord"), NEW);
    expect(submit).toBeDisabled(); // confirm still empty

    await user.type(dialog.getByLabelText("Bekräfta nytt lösenord"), "annanhemlis1");
    expect(submit).toBeDisabled(); // mismatch

    await user.clear(dialog.getByLabelText("Bekräfta nytt lösenord"));
    await user.type(dialog.getByLabelText("Bekräfta nytt lösenord"), NEW);
    expect(submit).toBeEnabled(); // floor + match + current present
  });

  it("keeps submit disabled when the new password is below the 12-char floor", async () => {
    const user = userEvent.setup();
    render(<ChangePasswordCard />);
    const dialog = await openDialog(user);

    // 11 chars — matching confirm, but below the floor.
    const elevenChars = "elvateckenx";
    expect(elevenChars).toHaveLength(11);
    await user.type(dialog.getByLabelText("Nuvarande lösenord"), CURRENT);
    await user.type(dialog.getByLabelText("Nytt lösenord"), elevenChars);
    await user.type(dialog.getByLabelText("Bekräfta nytt lösenord"), elevenChars);

    expect(dialog.getByRole("button", { name: "Byt lösenord" })).toBeDisabled();
  });

  it("shows a mismatch message and marks confirm invalid when the entries differ", async () => {
    const user = userEvent.setup();
    render(<ChangePasswordCard />);
    const dialog = await openDialog(user);

    await user.type(dialog.getByLabelText("Nytt lösenord"), NEW);
    await user.type(dialog.getByLabelText("Bekräfta nytt lösenord"), "annanhemlis12");

    expect(dialog.getByText("Lösenorden matchar inte.")).toBeInTheDocument();
    expect(dialog.getByLabelText("Bekräfta nytt lösenord")).toHaveAttribute(
      "aria-invalid",
      "true",
    );
    expect(dialog.getByRole("button", { name: "Byt lösenord" })).toBeDisabled();
  });

  it("disambiguates the three show-password toggles for screen readers", async () => {
    const user = userEvent.setup();
    render(<ChangePasswordCard />);
    const dialog = await openDialog(user);

    // Each password field's toggle has a distinct accessible name (WCAG 2.4.6).
    expect(dialog.getByRole("button", { name: "Visa nuvarande lösenord" })).toBeInTheDocument();
    expect(dialog.getByRole("button", { name: "Visa nytt lösenord" })).toBeInTheDocument();
    expect(dialog.getByRole("button", { name: "Visa bekräfta nytt lösenord" })).toBeInTheDocument();
  });

  it("calls changePasswordAction with the current + new password on submit", async () => {
    const user = userEvent.setup();
    render(<ChangePasswordCard />);
    const dialog = await openDialog(user);

    await user.type(dialog.getByLabelText("Nuvarande lösenord"), CURRENT);
    await user.type(dialog.getByLabelText("Nytt lösenord"), NEW);
    await user.type(dialog.getByLabelText("Bekräfta nytt lösenord"), NEW);
    await user.click(dialog.getByRole("button", { name: "Byt lösenord" }));

    await waitFor(() =>
      expect(changePasswordActionMock).toHaveBeenCalledWith(CURRENT, NEW),
    );
    expect(changePasswordActionMock).toHaveBeenCalledTimes(1);
  });

  it("closes the dialog and shows the success confirmation on success", async () => {
    const user = userEvent.setup();
    render(<ChangePasswordCard />);
    const dialog = await openDialog(user);

    await user.type(dialog.getByLabelText("Nuvarande lösenord"), CURRENT);
    await user.type(dialog.getByLabelText("Nytt lösenord"), NEW);
    await user.type(dialog.getByLabelText("Bekräfta nytt lösenord"), NEW);
    await user.click(dialog.getByRole("button", { name: "Byt lösenord" }));

    // Stay-on-page: the dialog closes and a role=status confirmation appears.
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(screen.getByRole("status")).toHaveTextContent(
      /Lösenordet är ändrat/,
    );
  });

  it("shows the server error and stays open when the action fails", async () => {
    changePasswordActionMock.mockResolvedValueOnce({
      success: false,
      error: "Lösenordet är felaktigt.",
    });
    const user = userEvent.setup();
    render(<ChangePasswordCard />);
    const dialog = await openDialog(user);

    await user.type(dialog.getByLabelText("Nuvarande lösenord"), "FelLosen12345");
    await user.type(dialog.getByLabelText("Nytt lösenord"), NEW);
    await user.type(dialog.getByLabelText("Bekräfta nytt lösenord"), NEW);
    await user.click(dialog.getByRole("button", { name: "Byt lösenord" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Lösenordet är felaktigt.");
    // The success confirmation does not appear on failure (the live regions are
    // persistent, so assert on the text rather than the role).
    expect(screen.queryByText(/Lösenordet är ändrat/)).not.toBeInTheDocument();
  });

  it("resets the new + confirm fields after close and reopen", async () => {
    const user = userEvent.setup();
    render(<ChangePasswordCard />);
    let dialog = await openDialog(user);

    await user.type(dialog.getByLabelText("Nytt lösenord"), NEW);
    await user.type(dialog.getByLabelText("Bekräfta nytt lösenord"), NEW);
    await user.click(dialog.getByRole("button", { name: "Avbryt" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

    dialog = await openDialog(user);
    expect(dialog.getByLabelText("Nytt lösenord")).toHaveValue("");
    expect(dialog.getByLabelText("Bekräfta nytt lösenord")).toHaveValue("");
  });
});
