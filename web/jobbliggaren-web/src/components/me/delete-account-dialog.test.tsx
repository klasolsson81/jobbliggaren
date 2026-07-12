import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DeleteAccountDialog } from "./delete-account-dialog";
import type { ActionResult } from "@/lib/actions/me";
import type { DeleteMyAccountInput } from "@/lib/actions/me-schemas";

// #822 — the action no longer takes the expected address as an argument: it resolves it
// from the session server-side (a Server Action argument is client-controlled).
const deleteAccountActionMock =
  vi.fn<(input: DeleteMyAccountInput) => Promise<ActionResult>>();

vi.mock("@/lib/actions/me", () => ({
  deleteAccountAction: (input: DeleteMyAccountInput) =>
    deleteAccountActionMock(input),
}));

// PR2c-1 — DeleteAccountDialog is now a thin wrapper over the generic
// <ReAuthDialog>. These tests still assert the delete-specific behaviour end to
// end (typed email-confirmation + password, action wiring, server error, PII
// safety), proving the refactor is behaviour-neutral (#595). The password now
// travels with the delete operation itself; the action is mocked here so the
// test verifies exactly the { confirmEmail, password } payload it receives.
describe("DeleteAccountDialog", () => {
  beforeEach(() => {
    deleteAccountActionMock.mockReset();
    deleteAccountActionMock.mockResolvedValue({ success: true });
  });

  it("renders trigger button initially without modal open", () => {
    render(<DeleteAccountDialog currentEmail="anna@example.se" />);

    expect(
      screen.getByRole("button", { name: "Radera konto permanent" })
    ).toBeInTheDocument();
    expect(screen.queryByText(/Skriv din e-postadress/)).not.toBeInTheDocument();
  });

  it("opens dialog with title, description and disabled submit by default", async () => {
    const user = userEvent.setup();
    render(<DeleteAccountDialog currentEmail="anna@example.se" />);

    await user.click(
      screen.getByRole("button", { name: "Radera konto permanent" })
    );

    expect(
      screen.getByRole("heading", { name: "Radera konto permanent" })
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Den här åtgärden går inte att ångra/)
    ).toBeInTheDocument();
    // Submit-knappen disabled tills email-match + password ifyllt
    expect(
      screen.getByRole("button", { name: "Radera mitt konto" })
    ).toBeDisabled();
  });

  it("enables submit when email matches and password is filled", async () => {
    const user = userEvent.setup();
    render(<DeleteAccountDialog currentEmail="anna@example.se" />);

    await user.click(
      screen.getByRole("button", { name: "Radera konto permanent" })
    );

    await user.type(
      screen.getByLabelText(/Skriv din e-postadress/),
      "anna@example.se"
    );
    await user.type(screen.getByLabelText("Lösenord"), "S3kret!pass");

    expect(
      screen.getByRole("button", { name: "Radera mitt konto" })
    ).not.toBeDisabled();
  });

  // #595 — the password field is the shared PasswordInput (show/hide toggle).
  // The RHF `register("password")` ref + `watch("password")` must still reach
  // the native input through PasswordInput → ui/Input (React 19 ref-as-prop
  // spread). These two tests pin that the swap did not sever that path.
  it("masks the password by default and reveals it via the toggle (#595)", async () => {
    const user = userEvent.setup();
    render(<DeleteAccountDialog currentEmail="anna@example.se" />);

    await user.click(
      screen.getByRole("button", { name: "Radera konto permanent" })
    );

    const password = screen.getByLabelText("Lösenord");
    expect(password).toHaveAttribute("type", "password");

    await user.click(screen.getByRole("button", { name: "Visa lösenord" }));
    expect(password).toHaveAttribute("type", "text");

    await user.click(screen.getByRole("button", { name: "Dölj lösenord" }));
    expect(password).toHaveAttribute("type", "password");
  });

  it("still enables submit after revealing the password (ref/watch intact, #595)", async () => {
    const user = userEvent.setup();
    render(<DeleteAccountDialog currentEmail="anna@example.se" />);

    await user.click(
      screen.getByRole("button", { name: "Radera konto permanent" })
    );

    await user.type(
      screen.getByLabelText(/Skriv din e-postadress/),
      "anna@example.se"
    );
    // Reveal first, then type — the revealed (type="text") input must still be
    // wired to RHF so `watch` drives the local activation guard.
    await user.click(screen.getByRole("button", { name: "Visa lösenord" }));
    await user.type(screen.getByLabelText("Lösenord"), "S3kret!pass");

    expect(
      screen.getByRole("button", { name: "Radera mitt konto" })
    ).not.toBeDisabled();
  });

  it("keeps submit disabled when email mismatches", async () => {
    const user = userEvent.setup();
    render(<DeleteAccountDialog currentEmail="anna@example.se" />);

    await user.click(
      screen.getByRole("button", { name: "Radera konto permanent" })
    );

    await user.type(
      screen.getByLabelText(/Skriv din e-postadress/),
      "fel@example.se"
    );
    await user.type(screen.getByLabelText("Lösenord"), "S3kret!pass");

    expect(
      screen.getByRole("button", { name: "Radera mitt konto" })
    ).toBeDisabled();
  });

  // #822 — the typed-confirmation gate must FAIL CLOSED when the expected address is
  // absent. While GET /api/v1/me returned an empty email, the match degenerated to
  // "" === "" and the gate INVERTED: an empty field armed the delete, while a user who
  // typed their address correctly was blocked. The backend now supplies the address;
  // this pins the safeguard so the inversion cannot return through any future
  // regression that empties it. An irreversible action never loses its confirmation
  // (ASVS V6.2.5).
  it("keeps submit disabled when the expected email is empty, even with an empty field", async () => {
    const user = userEvent.setup();
    render(<DeleteAccountDialog currentEmail="" />);

    await user.click(
      screen.getByRole("button", { name: "Radera konto permanent" })
    );

    await user.type(screen.getByLabelText("Lösenord"), "S3kret!pass");

    // Confirm-email field left empty — the pre-#822 gate would have armed here.
    expect(
      screen.getByRole("button", { name: "Radera mitt konto" })
    ).toBeDisabled();
  });

  it("matches email case-insensitively (uppercase input)", async () => {
    const user = userEvent.setup();
    render(<DeleteAccountDialog currentEmail="anna@example.se" />);

    await user.click(
      screen.getByRole("button", { name: "Radera konto permanent" })
    );

    await user.type(
      screen.getByLabelText(/Skriv din e-postadress/),
      "ANNA@EXAMPLE.SE"
    );
    await user.type(screen.getByLabelText("Lösenord"), "S3kret!pass");

    expect(
      screen.getByRole("button", { name: "Radera mitt konto" })
    ).not.toBeDisabled();
  });

  it("calls deleteAccountAction with sanitized values on submit", async () => {
    const user = userEvent.setup();
    render(<DeleteAccountDialog currentEmail="anna@example.se" />);

    await user.click(
      screen.getByRole("button", { name: "Radera konto permanent" })
    );
    await user.type(
      screen.getByLabelText(/Skriv din e-postadress/),
      "anna@example.se"
    );
    await user.type(screen.getByLabelText("Lösenord"), "S3kret!pass");
    await user.click(screen.getByRole("button", { name: "Radera mitt konto" }));

    await waitFor(() => {
      expect(deleteAccountActionMock).toHaveBeenCalledTimes(1);
    });
    expect(deleteAccountActionMock).toHaveBeenCalledWith({
      confirmEmail: "anna@example.se",
      password: "S3kret!pass",
    });
  });

  it("shows server error when action returns { success:false, error }", async () => {
    deleteAccountActionMock.mockResolvedValueOnce({
      success: false,
      error: "Lösenordet är felaktigt.",
    });

    const user = userEvent.setup();
    render(<DeleteAccountDialog currentEmail="anna@example.se" />);

    await user.click(
      screen.getByRole("button", { name: "Radera konto permanent" })
    );
    await user.type(
      screen.getByLabelText(/Skriv din e-postadress/),
      "anna@example.se"
    );
    await user.type(screen.getByLabelText("Lösenord"), "WrongPwd!");
    await user.click(screen.getByRole("button", { name: "Radera mitt konto" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Lösenordet är felaktigt.");
  });

  it("does NOT log password or email to console (PII safety)", async () => {
    const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    deleteAccountActionMock.mockResolvedValueOnce({
      success: false,
      error: "Lösenordet är felaktigt.",
    });

    const user = userEvent.setup();
    render(<DeleteAccountDialog currentEmail="anna@example.se" />);

    await user.click(
      screen.getByRole("button", { name: "Radera konto permanent" })
    );
    await user.type(
      screen.getByLabelText(/Skriv din e-postadress/),
      "anna@example.se"
    );
    await user.type(screen.getByLabelText("Lösenord"), "SuperSecretPwd123!");
    await user.click(screen.getByRole("button", { name: "Radera mitt konto" }));

    await screen.findByRole("alert");

    // Ingen console.error-anrop med PII i argumenten
    for (const call of consoleSpy.mock.calls) {
      const stringified = JSON.stringify(call);
      expect(stringified).not.toContain("SuperSecretPwd123!");
    }
    consoleSpy.mockRestore();
  });

  // Reset-on-close is now split: ReAuthDialog resets the password (RHF reset) and
  // this wrapper resets the typed email via `onOpenChange`. Pin that reopening
  // clears both, so a previous attempt never leaks into the next.
  it("resets the typed email and password after close + reopen", async () => {
    const user = userEvent.setup();
    render(<DeleteAccountDialog currentEmail="anna@example.se" />);

    await user.click(
      screen.getByRole("button", { name: "Radera konto permanent" })
    );
    await user.type(
      screen.getByLabelText(/Skriv din e-postadress/),
      "anna@example.se"
    );
    await user.type(screen.getByLabelText("Lösenord"), "S3kret!pass");

    // Close via Avbryt and wait for the dialog content to unmount.
    await user.click(screen.getByRole("button", { name: "Avbryt" }));
    await waitFor(() =>
      expect(screen.queryByLabelText("Lösenord")).not.toBeInTheDocument()
    );

    // Reopen — both fields must be empty again.
    await user.click(
      screen.getByRole("button", { name: "Radera konto permanent" })
    );
    expect(screen.getByLabelText(/Skriv din e-postadress/)).toHaveValue("");
    expect(screen.getByLabelText("Lösenord")).toHaveValue("");
  });
});
