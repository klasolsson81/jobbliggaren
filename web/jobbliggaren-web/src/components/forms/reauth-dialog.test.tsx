import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ReAuthDialog, type ReAuthDialogProps } from "./reauth-dialog";
import type { ActionResult } from "@/lib/actions/_action-result";

// The generic re-auth shell (PR2c-1). It owns the password field, the submit
// gating, the pending lock and the server-error line; consumers inject any extra
// fields via `children` and gate with `canSubmit`. `render` is auto-wrapped in
// the Swedish catalog (src/test/render-intl.tsx), so `account.reauth.passwordLabel`
// resolves to "Lösenord" and the PasswordInput toggle to "Visa lösenord".

function renderReAuth(props?: Partial<ReAuthDialogProps>) {
  const action =
    props?.action ??
    vi
      .fn<(password: string) => Promise<ActionResult>>()
      .mockResolvedValue({ success: true });
  render(
    <ReAuthDialog
      trigger={<button type="button">Öppna</button>}
      title="Bekräfta med lösenord"
      description="Ange ditt lösenord för att fortsätta."
      confirmLabel="Bekräfta"
      pendingLabel="Bekräftar…"
      cancelLabel="Avbryt"
      {...props}
      action={action}
    />,
  );
  return { action };
}

function openDialog(user: ReturnType<typeof userEvent.setup>) {
  return user.click(screen.getByRole("button", { name: "Öppna" }));
}

describe("ReAuthDialog", () => {
  it("keeps submit disabled until a password is entered", async () => {
    const user = userEvent.setup();
    renderReAuth();
    await openDialog(user);

    const submit = screen.getByRole("button", { name: "Bekräfta" });
    expect(submit).toBeDisabled();

    await user.type(screen.getByLabelText("Lösenord"), "hemligt123");
    expect(submit).toBeEnabled();
  });

  it("stays disabled when canSubmit returns false, even with a password", async () => {
    const canSubmit = vi.fn(() => false);
    const user = userEvent.setup();
    renderReAuth({ canSubmit });
    await openDialog(user);

    await user.type(screen.getByLabelText("Lösenord"), "hemligt123");

    expect(screen.getByRole("button", { name: "Bekräfta" })).toBeDisabled();
    // The gate is evaluated against the current password value.
    expect(canSubmit).toHaveBeenCalledWith("hemligt123");
  });

  it("hands the entered password to the action on submit", async () => {
    const action = vi
      .fn<(password: string) => Promise<ActionResult>>()
      .mockResolvedValue({ success: true });
    const user = userEvent.setup();
    renderReAuth({ action });
    await openDialog(user);

    await user.type(screen.getByLabelText("Lösenord"), "hemligt123");
    await user.click(screen.getByRole("button", { name: "Bekräfta" }));

    await waitFor(() => expect(action).toHaveBeenCalledWith("hemligt123"));
    expect(action).toHaveBeenCalledTimes(1);
  });

  it("shows a role=alert error wired to the password field when the action fails", async () => {
    const action = vi
      .fn<(password: string) => Promise<ActionResult>>()
      .mockResolvedValue({ success: false, error: "Lösenordet är felaktigt." });
    const user = userEvent.setup();
    renderReAuth({ action });
    await openDialog(user);

    await user.type(screen.getByLabelText("Lösenord"), "fel");
    await user.click(screen.getByRole("button", { name: "Bekräfta" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Lösenordet är felaktigt.");

    const password = screen.getByLabelText("Lösenord");
    expect(password).toHaveAttribute("aria-invalid", "true");
    expect(password).toHaveAttribute("aria-describedby", alert.id);
  });

  it("locks the password toggle and both buttons while the action is pending", async () => {
    let resolveAction!: (result: ActionResult) => void;
    const action = vi.fn<(password: string) => Promise<ActionResult>>(
      () =>
        new Promise<ActionResult>((resolve) => {
          resolveAction = resolve;
        }),
    );
    const user = userEvent.setup();
    renderReAuth({ action });
    await openDialog(user);

    await user.type(screen.getByLabelText("Lösenord"), "hemligt123");
    await user.click(screen.getByRole("button", { name: "Bekräfta" }));

    // Pending: the submit label swaps and every control is locked (the buttons
    // explicitly, the password input + its toggle via the disabled fieldset).
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Bekräftar…" })).toBeDisabled(),
    );
    expect(screen.getByRole("button", { name: "Avbryt" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Visa lösenord" })).toBeDisabled();
    expect(screen.getByLabelText("Lösenord")).toBeDisabled();

    // Settle the transition so the test unmounts cleanly.
    resolveAction({ success: false, error: "klart" });
    await screen.findByRole("alert");
  });

  it("overrides the password label when passwordLabel is given", async () => {
    const user = userEvent.setup();
    renderReAuth({ passwordLabel: "Nuvarande lösenord" });
    await openDialog(user);

    expect(screen.getByLabelText("Nuvarande lösenord")).toBeInTheDocument();
  });

  it("renders children after the password field when childrenPosition is 'after'", async () => {
    const user = userEvent.setup();
    renderReAuth({
      childrenPosition: "after",
      passwordLabel: "Nuvarande lösenord",
      children: <input aria-label="Nytt lösenord" />,
    });
    await openDialog(user);

    const current = screen.getByLabelText("Nuvarande lösenord");
    const next = screen.getByLabelText("Nytt lösenord");
    // The re-auth (current) field renders BEFORE the injected new-password field.
    expect(
      current.compareDocumentPosition(next) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it("calls onSuccess and closes the dialog on a successful action (stay-on-page)", async () => {
    const onSuccess = vi.fn();
    const action = vi
      .fn<(password: string) => Promise<ActionResult>>()
      .mockResolvedValue({ success: true });
    const user = userEvent.setup();
    renderReAuth({ action, onSuccess });
    await openDialog(user);

    await user.type(screen.getByLabelText("Lösenord"), "hemligt123");
    await user.click(screen.getByRole("button", { name: "Bekräfta" }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalledTimes(1));
    // The dialog closed on success — the password field is gone.
    await waitFor(() =>
      expect(screen.queryByLabelText("Lösenord")).not.toBeInTheDocument(),
    );
  });

  it("does not call onSuccess and keeps the dialog open on a failed action", async () => {
    const onSuccess = vi.fn();
    const action = vi
      .fn<(password: string) => Promise<ActionResult>>()
      .mockResolvedValue({ success: false, error: "Fel." });
    const user = userEvent.setup();
    renderReAuth({ action, onSuccess });
    await openDialog(user);

    await user.type(screen.getByLabelText("Lösenord"), "fel");
    await user.click(screen.getByRole("button", { name: "Bekräfta" }));

    await screen.findByRole("alert");
    expect(onSuccess).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Lösenord")).toBeInTheDocument();
  });
});
