import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { CodeProof, ReauthOutcome, ReauthRequestResult } from "@/lib/auth/reauth-action-state";
import { DeleteAccountSection } from "./delete-account-section";

const deleteAccountActionMock =
  vi.fn<(confirmEmail: string, proof: CodeProof) => Promise<ReauthOutcome<never>>>();
const requestReauthCodeMock = vi.fn<() => Promise<ReauthRequestResult>>();

vi.mock("@/lib/actions/me", () => ({
  deleteAccountAction: (confirmEmail: string, proof: CodeProof) =>
    deleteAccountActionMock(confirmEmail, proof),
}));
vi.mock("@/lib/auth/reauth-actions", () => ({
  requestReauthCode: () => requestReauthCodeMock(),
}));

const ADDRESS = "anna@example.se";
type User = ReturnType<typeof userEvent.setup>;

async function request(user: User) {
  await user.click(screen.getByRole("button", { name: "Radera konto" }));
  await user.type(screen.getByLabelText("Skriv din e-postadress för att bekräfta"), ADDRESS);
  await user.click(screen.getByRole("button", { name: "Skicka kod" }));
}

async function spendCode(user: User) {
  await request(user);
  await user.type(await screen.findByLabelText("Sexsiffrig kod"), "123456");
  await user.click(screen.getByRole("button", { name: "Radera mitt konto" }));
}

describe("DeleteAccountSection", () => {
  beforeEach(() => {
    deleteAccountActionMock.mockReset();
    requestReauthCodeMock.mockReset();
    requestReauthCodeMock.mockResolvedValue({ ok: true, challengeId: "challenge-under-test" });
  });

  it("names kontakt@ in place of the control when no mail can be delivered", async () => {
    requestReauthCodeMock.mockResolvedValue({ ok: false, kind: "refused" });
    const user = userEvent.setup();
    render(<DeleteAccountSection currentEmail={ADDRESS} />);

    await request(user);

    const status = await screen.findByText(/Vill du radera ditt konto kan du mejla/);
    expect(within(status).getByRole("link", { name: "kontakt@jobbliggaren.se" })).toHaveAttribute(
      "href",
      "mailto:kontakt@jobbliggaren.se"
    );
    expect(screen.queryByRole("button", { name: "Radera konto" })).not.toBeInTheDocument();
    const heading = screen.getByRole("heading", { level: 3, name: "Radera ditt konto" });
    await waitFor(() => expect(heading.parentElement).toHaveFocus());
  });

  it("claims nothing when the outcome is unknown: a reload link, and the control is gone", async () => {
    const unknown = "Vi kan inte se om kontot raderades. Ladda om sidan.";
    deleteAccountActionMock.mockResolvedValue({ ok: false, kind: "outcomeUnknown", error: unknown });
    const user = userEvent.setup();
    render(<DeleteAccountSection currentEmail={ADDRESS} />);

    await spendCode(user);

    expect(await screen.findByText(unknown)).toHaveAttribute("role", "status");
    expect(screen.getByRole("link", { name: "Ladda om sidan" })).toHaveAttribute("href", "/mina-sidor");
    expect(screen.queryByRole("button", { name: "Radera konto" })).not.toBeInTheDocument();
    const heading = screen.getByRole("heading", { level: 3, name: "Radera ditt konto" });
    await waitFor(() => expect(heading.parentElement).toHaveFocus());
  });

  it("shows a refusal after the code under the control, which stays", async () => {
    const refused =
      "Kontot raderades inte. Koden du skrev in är förbrukad, så du behöver en ny kod när du försöker igen.";
    deleteAccountActionMock.mockResolvedValue({
      ok: false,
      kind: "operationRefused",
      error: refused,
      channel: "status",
    });
    const user = userEvent.setup();
    render(<DeleteAccountSection currentEmail={ADDRESS} />);

    await spendCode(user);

    const message = await screen.findByText(refused);
    const region = message.closest('[role="status"]');
    await waitFor(() => expect(region).toHaveFocus());
    expect(screen.getByRole("button", { name: "Radera konto" })).toBeInTheDocument();
  });
});
