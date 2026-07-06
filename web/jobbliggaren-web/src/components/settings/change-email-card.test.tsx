import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ChangeEmailCard } from "./change-email-card";
import type { ActionResult } from "@/lib/actions/_action-result";

const changeEmailActionMock =
  vi.fn<(current: string, newEmail: string) => Promise<ActionResult>>();

vi.mock("@/lib/actions/me", () => ({
  changeEmailAction: (current: string, newEmail: string) =>
    changeEmailActionMock(current, newEmail),
}));

// The card reuses <ReAuthDialog>: the dialog owns the CURRENT password (its re-auth
// field), the card injects the single new-email field and gates submit on
// "valid email AND different from current". `render` is auto-wrapped in the Swedish
// catalog.
const CURRENT_EMAIL = "gammal@exempel.se";
const CURRENT_PASSWORD = "Current123456";
const NEW_EMAIL = "ny.adress@exempel.se";

describe("ChangeEmailCard", () => {
  beforeEach(() => {
    changeEmailActionMock.mockReset();
    changeEmailActionMock.mockResolvedValue({ success: true });
  });

  async function openDialog(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole("button", { name: "Byt e-postadress" }));
    // Scope to the dialog so the card title/trigger are never matched.
    return within(await screen.findByRole("dialog"));
  }

  it("renders the trigger without the dialog open", () => {
    render(<ChangeEmailCard currentEmail={CURRENT_EMAIL} />);
    expect(
      screen.getByRole("button", { name: "Byt e-postadress" }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("keeps submit disabled until the new email is valid, different, and the password is present", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT_EMAIL} />);
    const dialog = await openDialog(user);
    const submit = dialog.getByRole("button", { name: "Skicka bekräftelselänk" });

    expect(submit).toBeDisabled(); // everything empty

    await user.type(dialog.getByLabelText("Nuvarande lösenord"), CURRENT_PASSWORD);
    expect(submit).toBeDisabled(); // new email still empty

    await user.type(dialog.getByLabelText("Ny e-postadress"), "not-an-email");
    expect(submit).toBeDisabled(); // malformed email

    await user.clear(dialog.getByLabelText("Ny e-postadress"));
    await user.type(dialog.getByLabelText("Ny e-postadress"), NEW_EMAIL);
    expect(submit).toBeEnabled(); // valid + different + password present
  });

  it("keeps submit disabled when the new email equals the current one (case-insensitive)", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT_EMAIL} />);
    const dialog = await openDialog(user);

    await user.type(dialog.getByLabelText("Nuvarande lösenord"), CURRENT_PASSWORD);
    // Same address, different case + surrounding space — still the same account.
    await user.type(dialog.getByLabelText("Ny e-postadress"), "  GAMMAL@Exempel.SE  ");

    expect(
      dialog.getByRole("button", { name: "Skicka bekräftelselänk" }),
    ).toBeDisabled();
  });

  it("calls changeEmailAction with the current password + new email on submit", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT_EMAIL} />);
    const dialog = await openDialog(user);

    await user.type(dialog.getByLabelText("Nuvarande lösenord"), CURRENT_PASSWORD);
    await user.type(dialog.getByLabelText("Ny e-postadress"), NEW_EMAIL);
    await user.click(dialog.getByRole("button", { name: "Skicka bekräftelselänk" }));

    await waitFor(() =>
      expect(changeEmailActionMock).toHaveBeenCalledWith(CURRENT_PASSWORD, NEW_EMAIL),
    );
    expect(changeEmailActionMock).toHaveBeenCalledTimes(1);
  });

  it("closes the dialog and shows the link-sent confirmation on success", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT_EMAIL} />);
    const dialog = await openDialog(user);

    await user.type(dialog.getByLabelText("Nuvarande lösenord"), CURRENT_PASSWORD);
    await user.type(dialog.getByLabelText("Ny e-postadress"), NEW_EMAIL);
    await user.click(dialog.getByRole("button", { name: "Skicka bekräftelselänk" }));

    // Stay-on-page: the dialog closes and a role=status confirmation appears. The
    // copy says a link was SENT (not that the email changed — that needs the link).
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(screen.getByRole("status")).toHaveTextContent(
      /Vi har skickat en bekräftelselänk/,
    );
  });

  it("shows the server error and stays open when the action fails (address taken)", async () => {
    changeEmailActionMock.mockResolvedValueOnce({
      success: false,
      error: "E-postadressen används redan av ett annat konto.",
    });
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT_EMAIL} />);
    const dialog = await openDialog(user);

    await user.type(dialog.getByLabelText("Nuvarande lösenord"), CURRENT_PASSWORD);
    await user.type(dialog.getByLabelText("Ny e-postadress"), NEW_EMAIL);
    await user.click(dialog.getByRole("button", { name: "Skicka bekräftelselänk" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("E-postadressen används redan av ett annat konto.");
    // The confirmation does not appear on failure (persistent live region → assert
    // on the text, not the role).
    expect(
      screen.queryByText(/Vi har skickat en bekräftelselänk/),
    ).not.toBeInTheDocument();
  });

  it("resets the new-email field after close and reopen", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT_EMAIL} />);
    let dialog = await openDialog(user);

    await user.type(dialog.getByLabelText("Ny e-postadress"), NEW_EMAIL);
    await user.click(dialog.getByRole("button", { name: "Avbryt" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

    dialog = await openDialog(user);
    expect(dialog.getByLabelText("Ny e-postadress")).toHaveValue("");
  });
});
