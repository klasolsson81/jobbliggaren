import { describe, it, expect, vi, beforeEach } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ChangeEmailCard } from "./change-email-card";
import type { RefusableActionResult } from "@/lib/actions/_action-result";

const changeEmailActionMock =
  vi.fn<(current: string, newEmail: string) => Promise<RefusableActionResult>>();

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

  it("surfaces the same-email gate reason only while the new address equals the current one", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT_EMAIL} />);
    const dialog = await openDialog(user);
    const sameEmailMessage = "Den nya adressen måste skilja sig från din nuvarande.";
    const field = dialog.getByLabelText("Ny e-postadress");

    // Empty field → the persistent live region is empty (zero height), no reason shown.
    expect(dialog.queryByText(sameEmailMessage)).not.toBeInTheDocument();

    // Same address (different case + surrounding space) → the reason is surfaced and
    // the field is marked invalid.
    await user.type(field, "  GAMMAL@Exempel.SE  ");
    expect(dialog.getByText(sameEmailMessage)).toBeInTheDocument();
    expect(field).toHaveAttribute("aria-invalid", "true");

    // A different valid address → the reason disappears and invalid clears.
    await user.clear(field);
    await user.type(field, NEW_EMAIL);
    expect(dialog.queryByText(sameEmailMessage)).not.toBeInTheDocument();
    expect(field).not.toHaveAttribute("aria-invalid");
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

  it("keeps the retry affordance alive when an ordinary failure is shown", async () => {
    // COUNTERFACTUAL for the refusal panel below: only a `refused` failure may remove the
    // retry affordance. Its own `it` so a regression names which property broke. The card
    // trigger is aria-hidden behind the open dialog, so the affordance that exists in this
    // state is the live submit inside it.
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

    await screen.findByRole("alert");
    const stillOpen = within(screen.getByRole("dialog"));
    expect(
      stillOpen.getByRole("button", { name: "Skicka bekräftelselänk" }),
    ).toBeEnabled();
  });

  // #734 B-ii — the delivery-refusal panel. Reached only when the backend answers 503 with
  // title Auth.EmailDeliveryUnavailable, i.e. no configured sender can deliver at all.
  const REFUSAL_COPY =
    "E-postutskick är inte aktiverat just nu, så vi kan inte skicka någon bekräftelselänk. Din adress är oförändrad. Försök igen senare.";

  /**
   * Radix restores focus from `FocusScope`'s cleanup inside a `setTimeout(…, 0)`, i.e. AFTER
   * the card's own focus effect. A bare `waitFor(...toHaveFocus())` resolves on the first
   * passing poll and therefore cannot tell "focus held" from "focus was stolen a tick later".
   * Flushing the macrotask queue first puts the assertion on the far side of that threshold.
   */
  async function flushRadixFocusRestore() {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }

  it("replaces the card with a focused status panel and removes the trigger when delivery is refused", async () => {
    changeEmailActionMock.mockResolvedValueOnce({
      success: false,
      refused: true,
      error: REFUSAL_COPY,
    });
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT_EMAIL} />);
    const dialog = await openDialog(user);

    await user.type(dialog.getByLabelText("Nuvarande lösenord"), CURRENT_PASSWORD);
    await user.type(dialog.getByLabelText("Ny e-postadress"), NEW_EMAIL);
    await user.click(dialog.getByRole("button", { name: "Skicka bekräftelselänk" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

    // The explanation is delivered...
    const panel = await screen.findByText(REFUSAL_COPY);
    expect(panel).toHaveAttribute("role", "status");
    // ...focus moves to the WRAPPER that carries the heading with it, and survives Radix's
    // own restore attempt (role=status mounted already filled is missed by NVDA/JAWS, so the
    // focus move is what actually delivers the message)...
    await flushRadixFocusRestore();
    expect(panel.parentElement).toHaveFocus();
    expect(
      within(panel.parentElement as HTMLElement).getByRole("heading", {
        name: "Byt e-postadress",
      }),
    ).toBeInTheDocument();
    // ...it is NOT the red retryable error channel...
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    // ...and the retry affordance is gone, which is half of the defect this closes.
    expect(
      screen.queryByRole("button", { name: "Byt e-postadress" }),
    ).not.toBeInTheDocument();
  });

  it("does not publish the delivery promise above its own denial", async () => {
    // The string in messages/ is untouched (release-checklist §2.6 point 5.5 condition (a)
    // forbids softening it); this pins only that it is not RENDERED in the refused state.
    changeEmailActionMock.mockResolvedValueOnce({
      success: false,
      refused: true,
      error: REFUSAL_COPY,
    });
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT_EMAIL} />);
    const dialog = await openDialog(user);

    await user.type(dialog.getByLabelText("Nuvarande lösenord"), CURRENT_PASSWORD);
    await user.type(dialog.getByLabelText("Ny e-postadress"), NEW_EMAIL);
    await user.click(dialog.getByRole("button", { name: "Skicka bekräftelselänk" }));

    await screen.findByText(REFUSAL_COPY);
    expect(
      screen.queryByText(/Vi skickar en bekräftelselänk till den nya adressen/),
    ).not.toBeInTheDocument();
    // The heading stays — the card is still identifiable in the settings page outline.
    expect(
      screen.getByRole("heading", { name: "Byt e-postadress" }),
    ).toBeInTheDocument();
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
