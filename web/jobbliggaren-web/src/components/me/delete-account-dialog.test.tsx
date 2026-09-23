import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useRef } from "react";
import type { ReauthHandOff } from "@/components/forms/reauth-code-dialog";
import type { CodeProof, ReauthOutcome, ReauthRequestResult } from "@/lib/auth/reauth-action-state";
import { DeleteAccountDialog } from "./delete-account-dialog";

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
const CHALLENGE = "challenge-under-test";

function Harness({ onHandOff }: { onHandOff: (handOff: ReauthHandOff<never>) => void }) {
  const target = useRef<HTMLDivElement>(null);
  return (
    <>
      <DeleteAccountDialog currentEmail={ADDRESS} onHandOff={onHandOff} handOffTarget={target} />
      <div ref={target} tabIndex={-1} data-testid="hand-off-target" />
    </>
  );
}

async function openAndRequest(user: ReturnType<typeof userEvent.setup>, typed = ADDRESS) {
  await user.click(screen.getByRole("button", { name: "Radera konto" }));
  await user.type(screen.getByLabelText("Skriv din e-postadress för att bekräfta"), typed);
  await user.click(screen.getByRole("button", { name: "Skicka kod" }));
}

describe("DeleteAccountDialog", () => {
  beforeEach(() => {
    deleteAccountActionMock.mockReset();
    requestReauthCodeMock.mockReset();
    requestReauthCodeMock.mockResolvedValue({ ok: true, challengeId: CHALLENGE });
  });

  it("states what deletion does, including the 30 days, before anything is asked", async () => {
    const user = userEvent.setup();
    render(<Harness onHandOff={vi.fn()} />);

    await user.click(screen.getByRole("button", { name: "Radera konto" }));

    const dialog = screen.getByRole("dialog", { name: "Radera ditt konto" });
    expect(dialog).toHaveTextContent(/I 30 dagar kan du få kontot återställt/);
    expect(dialog).toHaveTextContent(`skickar vi en sexsiffrig kod till ${ADDRESS}`);
    // Nothing focusable before the typed field: the restore address is text, not a link.
    expect(within(dialog).queryByRole("link")).not.toBeInTheDocument();
  });

  it("checks the typed address when the button is pressed, never by disabling it", async () => {
    const user = userEvent.setup();
    render(<Harness onHandOff={vi.fn()} />);

    await openAndRequest(user, "fel@example.se");

    const field = screen.getByLabelText("Skriv din e-postadress för att bekräfta");
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Skriv din e-postadress som den står under fältet."
    );
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field).toHaveFocus();
    expect(requestReauthCodeMock).not.toHaveBeenCalled();
  });

  it("accepts the address in another case and with spaces, then asks for the code", async () => {
    const user = userEvent.setup();
    render(<Harness onHandOff={vi.fn()} />);

    await openAndRequest(user, "  Anna@Example.SE ");

    await waitFor(() => expect(requestReauthCodeMock).toHaveBeenCalledTimes(1));
    expect(await screen.findByLabelText("Sexsiffrig kod")).toHaveFocus();
    expect(
      screen.getByText(`Vi har skickat en sexsiffrig kod till ${ADDRESS}. Koden gäller i 15 minuter.`)
    ).toBeInTheDocument();
  });

  it("names kontakt@ in the code field's hint as plain text: a link there would add a tab stop", async () => {
    const user = userEvent.setup();
    render(<Harness onHandOff={vi.fn()} />);

    await openAndRequest(user);
    const field = await screen.findByLabelText("Sexsiffrig kod");

    expect(field).toHaveAccessibleDescription(
      expect.stringContaining(
        "Du kan också mejla kontakt@jobbliggaren.se och be oss radera kontot."
      )
    );
    expect(within(screen.getByRole("dialog")).queryByRole("link")).not.toBeInTheDocument();
  });

  it("hands the action the confirmed address and the proof, and nothing else", async () => {
    deleteAccountActionMock.mockResolvedValue({
      ok: false,
      kind: "operationRefused",
      error: "Kontot raderades inte.",
      channel: "status",
    });
    const user = userEvent.setup();
    render(<Harness onHandOff={vi.fn()} />);

    await openAndRequest(user);
    await user.type(await screen.findByLabelText("Sexsiffrig kod"), "123456");
    await user.click(screen.getByRole("button", { name: "Radera mitt konto" }));

    await waitFor(() => expect(deleteAccountActionMock).toHaveBeenCalledTimes(1));
    expect(deleteAccountActionMock).toHaveBeenCalledWith(ADDRESS, {
      challengeId: CHALLENGE,
      code: "123456",
    });
  });

  it("closes on a refusal after the code and hands it to the section, focus following", async () => {
    deleteAccountActionMock.mockResolvedValue({
      ok: false,
      kind: "operationRefused",
      error: "Kontot raderades inte.",
      channel: "status",
    });
    const onHandOff = vi.fn();
    const user = userEvent.setup();
    render(<Harness onHandOff={onHandOff} />);

    await openAndRequest(user);
    await user.type(await screen.findByLabelText("Sexsiffrig kod"), "123456");
    await user.click(screen.getByRole("button", { name: "Radera mitt konto" }));

    await waitFor(() =>
      expect(onHandOff).toHaveBeenCalledWith({
        kind: "operationRefused",
        error: "Kontot raderades inte.",
        channel: "status",
      })
    );
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await waitFor(() => expect(screen.getByTestId("hand-off-target")).toHaveFocus());
  });

  it("closes and hands over when no mail can be delivered, before any code exists", async () => {
    requestReauthCodeMock.mockResolvedValue({ ok: false, kind: "refused" });
    const onHandOff = vi.fn();
    const user = userEvent.setup();
    render(<Harness onHandOff={onHandOff} />);

    await openAndRequest(user);

    await waitFor(() => expect(onHandOff).toHaveBeenCalledWith({ kind: "refused" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("re-opens on the code step within the code's lifetime, and still sends the confirmed address", async () => {
    deleteAccountActionMock.mockResolvedValue({ ok: false, kind: "status", error: "Försök igen." });
    const user = userEvent.setup();
    render(<Harness onHandOff={vi.fn()} />);

    await openAndRequest(user);
    await screen.findByLabelText("Sexsiffrig kod");
    await user.click(screen.getByRole("button", { name: "Avbryt" }));
    await user.click(screen.getByRole("button", { name: "Radera konto" }));

    // No second code was asked for, and the typed field is not shown again.
    expect(requestReauthCodeMock).toHaveBeenCalledTimes(1);
    expect(screen.queryByLabelText("Skriv din e-postadress för att bekräfta")).not.toBeInTheDocument();
    await user.type(screen.getByLabelText("Sexsiffrig kod"), "654321");
    await user.click(screen.getByRole("button", { name: "Radera mitt konto" }));

    await waitFor(() =>
      expect(deleteAccountActionMock).toHaveBeenCalledWith(ADDRESS, {
        challengeId: CHALLENGE,
        code: "654321",
      })
    );
  });

  it("re-opens empty after a refusal has closed it", async () => {
    deleteAccountActionMock.mockResolvedValue({
      ok: false,
      kind: "operationRefused",
      error: "Kontot raderades inte.",
      channel: "status",
    });
    const user = userEvent.setup();
    render(<Harness onHandOff={vi.fn()} />);

    await openAndRequest(user);
    await user.type(await screen.findByLabelText("Sexsiffrig kod"), "123456");
    await user.click(screen.getByRole("button", { name: "Radera mitt konto" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Radera konto" }));

    expect(await screen.findByLabelText("Skriv din e-postadress för att bekräfta")).toHaveValue("");
  });

  it("never puts the challenge id in the page", async () => {
    const user = userEvent.setup();
    render(<Harness onHandOff={vi.fn()} />);

    await openAndRequest(user);
    await screen.findByLabelText("Sexsiffrig kod");

    expect(document.body.innerHTML).not.toContain(CHALLENGE);
  });
});
