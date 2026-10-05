import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AdminEmailChangeRequestOutcome } from "@/lib/admin/account-email-change";
import type { AdminAddressedAccount } from "@/lib/admin/view-models";
import type { CodeProof, ReauthRequestResult } from "@/lib/auth/reauth-action-state";
import { AdminAccountEditForm, type AdminEditExit } from "./admin-account-edit-form";

// #1975 — the edit form's gate (design-reviewer item 3): "Fortsätt" checks the address before the step-up dialog
// opens, in the self-service order, so a refusal keeps the dialog closed and no code is asked for.

const ACCOUNT: AdminAddressedAccount = {
  id: "00000000-0000-4000-8000-000000000007",
  email: "konto.a@example.test",
  role: "user",
  status: "active",
  emailConfirmed: true,
  registeredAt: "2026-09-28T12:02:00Z",
  applicationCount: 4,
  deletionEarliest: null,
  savedSearchCount: 3,
  resumeCount: 1,
};

const SELF_EMAIL = "admin@example.test";
const NEW = "ny.adress@example.test";

const requestCodeMock = vi.fn<() => Promise<ReauthRequestResult>>();
const requestMock = vi.fn<(newEmail: string, proof: CodeProof) => Promise<AdminEmailChangeRequestOutcome>>();
const onExit = vi.fn<(exit: AdminEditExit) => void>();
const onCancel = vi.fn();

function renderForm() {
  render(
    <AdminAccountEditForm
      account={ACCOUNT}
      selfEmail={SELF_EMAIL}
      requestCode={requestCodeMock}
      request={requestMock}
      returnPath="/admin/anvandare"
      onExit={onExit}
      focusAfterExit={() => {}}
      onCancel={onCancel}
    />,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  requestCodeMock.mockResolvedValue({ ok: true, challengeId: "step-up-challenge" });
  requestMock.mockResolvedValue({
    ok: true,
    value: { state: "pending", completableFrom: "2026-10-08T12:00:00Z", expiresAt: "2026-10-09T12:00:00Z" },
  });
});

describe("AdminAccountEditForm (#1975)", () => {
  it("shows the account's address and role, and asks for the new address with nothing announced at rest", () => {
    renderForm();

    expect(screen.getByRole("heading", { level: 3, name: "Ändra e-postadress" })).toBeInTheDocument();
    expect(screen.getByText("Nuvarande adress").nextElementSibling).toHaveTextContent(ACCOUNT.email);
    const field = screen.getByLabelText("Ny e-postadress");
    expect(field).toHaveAttribute("type", "email");
    expect(field).not.toHaveAttribute("placeholder");
    expect(field).not.toHaveAttribute("aria-invalid");
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByRole("status")).toBeNull();
  });

  it.each([
    ["an empty field", "", "Skriv in den nya e-postadressen."],
    ["a malformed address", "ny.exempel.test", "Skriv den nya e-postadressen i rätt format, till exempel namn@exempel.se."],
    ["the account's own address in another case", "  KONTO.A@example.test ", "Den nya adressen är samma som kontots nuvarande. Skriv in en annan adress."],
  ])("refuses %s when Fortsätt is pressed, and the dialog stays closed", async (_label, typed, copy) => {
    const user = userEvent.setup();
    renderForm();

    if (typed) await user.type(screen.getByLabelText("Ny e-postadress"), typed);
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));

    const field = screen.getByLabelText("Ny e-postadress");
    expect(screen.getByRole("alert")).toHaveTextContent(copy);
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field).toHaveAccessibleDescription(
      `Adressen byts när kontoägaren har använt koden som skickas dit. ${copy}`,
    );
    await waitFor(() => expect(field).toHaveFocus());
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(requestCodeMock).not.toHaveBeenCalled();
  });

  it("opens the step-up on Enter in the field, naming the trimmed address, and asks for no code until told", async () => {
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText("Ny e-postadress"), `  ${NEW}  {Enter}`);

    const dialog = await screen.findByRole("dialog", { name: "Ändra e-postadress" });
    expect(dialog).toHaveTextContent(`En kod skickas till ${NEW} och ett meddelande till ${ACCOUNT.email}.`);
    expect(requestCodeMock).not.toHaveBeenCalled();
  });

  it("clears an earlier refusal once the address passes", async () => {
    const user = userEvent.setup();
    renderForm();

    await user.click(screen.getByRole("button", { name: "Fortsätt" }));
    expect(screen.getByRole("alert")).toBeInTheDocument();
    await user.type(screen.getByLabelText("Ny e-postadress"), NEW);
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));

    await screen.findByRole("dialog", { name: "Ändra e-postadress" });
    expect(screen.getByLabelText("Ny e-postadress", { selector: "input" })).not.toHaveAttribute("aria-invalid");
  });

  it("sends the trimmed address with the administrator's code, and leaves the form on a pending change", async () => {
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText("Ny e-postadress"), ` ${NEW} `);
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));
    const dialog = await screen.findByRole("dialog", { name: "Ändra e-postadress" });
    await user.click(within(dialog).getByRole("button", { name: "Skicka kod" }));
    await user.type(await within(dialog).findByLabelText("Sexsiffrig kod"), "123456");
    await user.click(within(dialog).getByRole("button", { name: "Bekräfta koden" }));

    await waitFor(() => expect(onExit).toHaveBeenCalledWith({ kind: "requested", newEmail: NEW }));
    expect(requestMock).toHaveBeenCalledWith(NEW, { challengeId: "step-up-challenge", code: "123456" });
    expect(requestCodeMock).toHaveBeenCalledTimes(1);
  });

  it("leaves the form when Avbryt is pressed", async () => {
    renderForm();

    await userEvent.click(screen.getByRole("button", { name: "Avbryt" }));

    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(requestCodeMock).not.toHaveBeenCalled();
  });
});
