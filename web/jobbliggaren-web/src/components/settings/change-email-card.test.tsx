import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { CodeProof, ReauthOutcome, ReauthRequestResult } from "@/lib/auth/reauth-action-state";
import { ChangeEmailCard } from "./change-email-card";

// #1740 — the card of change-email by two codes (Klas 2026-09-22): the address is checked when
// "Fortsätt" is pressed, the shared dialog re-authenticates, and the card takes the code mailed to the
// new address. `render` is auto-wrapped in the Swedish catalogue.

const requestEmailChangeMock =
  vi.fn<(newEmail: string, proof: CodeProof) => Promise<ReauthOutcome<{ challengeId: string }>>>();
const confirmEmailChangeMock =
  vi.fn<(newEmail: string, proof: CodeProof) => Promise<ReauthOutcome<null>>>();
const requestReauthCodeMock = vi.fn<() => Promise<ReauthRequestResult>>();

vi.mock("@/lib/actions/me", () => ({
  requestEmailChangeAction: (newEmail: string, proof: CodeProof) =>
    requestEmailChangeMock(newEmail, proof),
  confirmEmailChangeAction: (newEmail: string, proof: CodeProof) =>
    confirmEmailChangeMock(newEmail, proof),
}));
vi.mock("@/lib/auth/reauth-actions", () => ({
  requestReauthCode: () => requestReauthCodeMock(),
}));

const CURRENT = "gammal@exempel.se";
const NEW = "ny.adress@exempel.se";
const REAUTH_CHALLENGE = "reauth-challenge-under-test";
const CHANGE_CHALLENGE = "change-challenge-under-test";
const SPENT = "Koden du skrev in är förbrukad, så du behöver en ny kod när du försöker igen.";
const TAKEN = `E-postadressen används redan av ett annat konto. Skriv in en annan adress. ${SPENT}`;

type User = ReturnType<typeof userEvent.setup>;

/** Types the new address, presses "Fortsätt" and re-authenticates in the dialog. */
async function reauthenticate(user: User, typed = NEW) {
  await user.type(screen.getByLabelText("Ny e-postadress"), typed);
  await user.click(screen.getByRole("button", { name: "Fortsätt" }));
  const dialog = await screen.findByRole("dialog", { name: "Byt e-postadress" });
  await user.click(within(dialog).getByRole("button", { name: "Skicka kod" }));
  await user.type(await within(dialog).findByLabelText("Sexsiffrig kod"), "111111");
  await user.click(within(dialog).getByRole("button", { name: "Bekräfta koden" }));
}

/** Reaches the card's own code step and returns its field. */
async function toCodeStep(user: User) {
  await reauthenticate(user);
  return screen.findByLabelText("Kod till den nya adressen");
}

async function confirmWith(user: User, code = "222222") {
  await user.type(await toCodeStep(user), code);
  await user.click(screen.getByRole("button", { name: "Byt adress" }));
}

describe("ChangeEmailCard", () => {
  beforeEach(() => {
    requestReauthCodeMock.mockReset();
    requestReauthCodeMock.mockResolvedValue({ ok: true, challengeId: REAUTH_CHALLENGE });
    requestEmailChangeMock.mockReset();
    requestEmailChangeMock.mockResolvedValue({ ok: true, value: { challengeId: CHANGE_CHALLENGE } });
    confirmEmailChangeMock.mockReset();
    confirmEmailChangeMock.mockResolvedValue({ ok: true, value: null });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("states the current address and asks for the new one, announcing nothing at rest", () => {
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    expect(screen.getByRole("heading", { level: 2, name: "Byt e-postadress" })).toBeInTheDocument();
    expect(screen.getByText(`Din e-postadress är ${CURRENT}.`)).toBeInTheDocument();
    expect(screen.getByLabelText("Ny e-postadress")).toHaveAccessibleDescription(
      "Formatet är namn@domän.se."
    );
    expect(screen.getByRole("button", { name: "Fortsätt" })).toBeEnabled();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it.each([
    ["an empty field", "", "Skriv in den nya e-postadressen."],
    [
      "a malformed address",
      "ny.exempel.se",
      "Kontrollera den nya e-postadressen. Den ska ha formatet namn@domän.se.",
    ],
    [
      "the current address in another case",
      "GAMMAL@exempel.se",
      "Den nya adressen är samma som din nuvarande. Skriv in en annan adress.",
    ],
  ])("checks %s when Fortsätt is pressed, and the dialog stays closed", async (_label, typed, copy) => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    if (typed) await user.type(screen.getByLabelText("Ny e-postadress"), typed);
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));

    const field = screen.getByLabelText("Ny e-postadress");
    expect(screen.getByRole("alert")).toHaveTextContent(copy);
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field).toHaveAccessibleDescription(`Formatet är namn@domän.se. ${copy}`);
    await waitFor(() => expect(field).toHaveFocus());
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(requestReauthCodeMock).not.toHaveBeenCalled();
  });

  it("opens the dialog on Enter in the field, naming the new address there and the current one as the recipient", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await user.type(screen.getByLabelText("Ny e-postadress"), `${NEW}{Enter}`);

    const dialog = await screen.findByRole("dialog", { name: "Byt e-postadress" });
    expect(dialog).toHaveTextContent(`Du byter till ${NEW}.`);
    expect(dialog).toHaveTextContent(`skickar vi en sexsiffrig kod till ${CURRENT}`);
  });

  it("takes the second code in the card once the dialog has re-authenticated, focus on its field", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    const codeField = await toCodeStep(user);

    expect(requestEmailChangeMock).toHaveBeenCalledWith(NEW, {
      challengeId: REAUTH_CHALLENGE,
      code: "111111",
    });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await waitFor(() => expect(codeField).toHaveFocus());
    expect(codeField).toHaveAccessibleDescription(
      `Vi har skickat en sexsiffrig kod till ${NEW}. Skriv in den här för att byta adress. Koden gäller i 15 minuter. ` +
        "Titta även i skräpposten. Kommer inget mejl inom några minuter kan du börja om och kontrollera adressen."
    );
    // The address is named here, never an input that looks editable.
    expect(screen.queryByLabelText("Ny e-postadress")).not.toBeInTheDocument();
  });

  it("sends the address pressed last when the dialog re-opens on its code step", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await user.type(screen.getByLabelText("Ny e-postadress"), "forsta@exempel.se");
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));
    await user.click(await screen.findByRole("button", { name: "Skicka kod" }));
    await screen.findByLabelText("Sexsiffrig kod");
    await user.click(screen.getByRole("button", { name: "Avbryt" }));
    await user.clear(screen.getByLabelText("Ny e-postadress"));
    await user.type(screen.getByLabelText("Ny e-postadress"), NEW);
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));

    const dialog = await screen.findByRole("dialog", { name: "Byt e-postadress" });
    expect(dialog).toHaveTextContent(`Du byter till ${NEW}.`);
    await user.type(within(dialog).getByLabelText("Sexsiffrig kod"), "111111");
    await user.click(within(dialog).getByRole("button", { name: "Bekräfta koden" }));

    await waitFor(() => expect(requestEmailChangeMock).toHaveBeenCalledTimes(1));
    expect(requestEmailChangeMock).toHaveBeenCalledWith(NEW, expect.anything());
    expect(requestReauthCodeMock).toHaveBeenCalledTimes(1);
  });

  it("changes the address on the second code, and the receipt says so", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await confirmWith(user);

    expect(confirmEmailChangeMock).toHaveBeenCalledWith(NEW, {
      challengeId: CHANGE_CHALLENGE,
      code: "222222",
    });
    const receipt = await screen.findByText(
      `Adressen är bytt, och du loggar in med ${NEW} från och med nu. Du är utloggad på alla andra enheter.`
    );
    expect(receipt).toHaveAttribute("role", "status");
    await waitFor(() => expect(receipt).toHaveFocus());
    expect(screen.getByLabelText("Ny e-postadress")).toHaveValue("");
  });

  it("puts a wrong code on the field, which is typed again", async () => {
    confirmEmailChangeMock.mockResolvedValue({
      ok: false,
      kind: "wrongCode",
      error: "Koden stämmer inte. Använd koden från mejlet till den nya adressen och kontrollera siffrorna.",
    });
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await confirmWith(user);

    expect(await screen.findByRole("alert")).toHaveTextContent("Använd koden från mejlet till den nya adressen");
    const field = screen.getByLabelText("Kod till den nya adressen");
    expect(field).toHaveValue("");
    expect(field).toHaveAttribute("aria-invalid", "true");
    await waitFor(() => expect(field).toHaveFocus());
  });

  it("refuses a malformed code without sending it", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await confirmWith(user, "12");

    expect(screen.getByRole("alert")).toHaveTextContent("Koden är sex siffror.");
    expect(confirmEmailChangeMock).not.toHaveBeenCalled();
  });

  it.each([
    ["expired", "Koden går inte att använda längre. Börja om för att få nya koder."],
    [
      "burned",
      "Du har skrivit fel kod tre gånger, så koden går inte att använda längre. Börja om för att få nya koder.",
    ],
  ] as const)("replaces the field with a panel on a %s code; Börja om is the way on", async (reason, copy) => {
    confirmEmailChangeMock.mockResolvedValue({ ok: false, kind: "deadCode", reason });
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await confirmWith(user);

    const panel = (await screen.findByText(copy)).closest('[role="status"]');
    await waitFor(() => expect(panel).toHaveFocus());
    expect(screen.queryByLabelText("Kod till den nya adressen")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Byt adress" })).not.toBeInTheDocument();
    const startOver = screen.getByRole("button", { name: "Börja om" });
    expect(startOver).toHaveAttribute("data-variant", "default");

    await user.click(startOver);

    const field = screen.getByLabelText("Ny e-postadress");
    expect(field).toHaveValue(NEW);
    await waitFor(() => expect(field).toHaveFocus());
  });

  it("goes back to the address, kept, when it was taken meanwhile", async () => {
    confirmEmailChangeMock.mockResolvedValue({
      ok: false,
      kind: "operationRefused",
      error: TAKEN,
      channel: "field",
    });
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await confirmWith(user);

    expect(await screen.findByRole("alert")).toHaveTextContent(TAKEN);
    const field = screen.getByLabelText("Ny e-postadress");
    expect(field).toHaveValue(NEW);
    expect(field).toHaveAttribute("aria-invalid", "true");
    await waitFor(() => expect(field).toHaveFocus());
  });

  it("ends a refused confirm in the dead panel, since the change code is spent", async () => {
    const incomplete = `Bytet kunde inte slutföras, och din adress är oförändrad. Vänta en stund. ${SPENT}`;
    confirmEmailChangeMock.mockResolvedValue({
      ok: false,
      kind: "operationRefused",
      error: incomplete,
      channel: "status",
      terminal: true,
    });
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await confirmWith(user);

    expect(await screen.findByRole("status")).toHaveTextContent(incomplete);
    expect(screen.getByRole("button", { name: "Börja om" })).toHaveAttribute("data-variant", "default");
  });

  it("claims nothing when the outcome is unknown: a reload link, and no Börja om", async () => {
    const unknown = "Vi kan inte se om adressen byttes. Ladda om sidan.";
    confirmEmailChangeMock.mockResolvedValue({ ok: false, kind: "outcomeUnknown", error: unknown });
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await confirmWith(user);

    const panel = await screen.findByRole("status");
    expect(panel).toHaveTextContent(unknown);
    await waitFor(() => expect(panel).toHaveFocus());
    // A full load, never a client navigation: the page must ask the server whether the session lives.
    expect(within(panel).getByRole("link", { name: "Ladda om sidan" })).toHaveAttribute("href", "/mina-sidor");
    expect(screen.queryByRole("button", { name: "Börja om" })).not.toBeInTheDocument();
  });

  it("sends a lapsed session to the login page and back", async () => {
    confirmEmailChangeMock.mockResolvedValue({ ok: false, kind: "notLoggedIn" });
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await confirmWith(user);

    const panel = await screen.findByRole("status");
    expect(panel).toHaveTextContent("Du är inte inloggad längre. Logga in igen och börja om.");
    expect(within(panel).getByRole("link", { name: "Logga in" })).toHaveAttribute(
      "href",
      "/logga-in?next=/mina-sidor"
    );
  });

  it("keeps the field on a status at verify, and the message takes focus", async () => {
    confirmEmailChangeMock.mockResolvedValue({
      ok: false,
      kind: "status",
      error: "Det går inte att kontrollera koden just nu. Försök igen om några minuter.",
    });
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await confirmWith(user);

    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("Det går inte att kontrollera koden just nu.");
    await waitFor(() => expect(status).toHaveFocus());
    expect(screen.getByLabelText("Kod till den nya adressen")).toBeInTheDocument();
  });

  it("treats a code past its lifetime as dead without sending it", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await user.type(await toCodeStep(user), "222222");
    const sent = Date.now();
    vi.spyOn(Date, "now").mockReturnValue(sent + 15 * 60 * 1000);
    await user.click(screen.getByRole("button", { name: "Byt adress" }));

    expect(
      await screen.findByText("Koden går inte att använda längre. Börja om för att få nya koder.")
    ).toBeInTheDocument();
    expect(confirmEmailChangeMock).not.toHaveBeenCalled();
  });

  it("locks both buttons while the change is confirmed", async () => {
    let settle: (outcome: ReauthOutcome<null>) => void = () => {};
    confirmEmailChangeMock.mockImplementation(
      () => new Promise((resolve) => (settle = resolve))
    );
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await confirmWith(user);

    expect(await screen.findByRole("button", { name: "Byter…" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Börja om" })).toBeDisabled();
    settle({ ok: true, value: null });
    expect(await screen.findByText(/Adressen är bytt/)).toBeInTheDocument();
  });

  it("starts over at the address, kept, with a new re-authentication", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await toCodeStep(user);
    await user.click(screen.getByRole("button", { name: "Börja om" }));

    const field = screen.getByLabelText("Ny e-postadress");
    expect(field).toHaveValue(NEW);
    await waitFor(() => expect(field).toHaveFocus());
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));
    // The re-authentication code was spent on the request, so the dialog asks for a new one.
    const dialog = await screen.findByRole("dialog", { name: "Byt e-postadress" });
    expect(within(dialog).getByRole("button", { name: "Skicka kod" })).toBeInTheDocument();
  });

  describe("after the re-authentication code was spent on the request", () => {
    it("puts a refused address back on its field, which keeps it", async () => {
      requestEmailChangeMock.mockResolvedValue({
        ok: false,
        kind: "operationRefused",
        error: TAKEN,
        channel: "field",
      });
      const user = userEvent.setup();
      render(<ChangeEmailCard currentEmail={CURRENT} />);

      await reauthenticate(user);

      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
      const field = screen.getByLabelText("Ny e-postadress");
      expect(screen.getByRole("alert")).toHaveTextContent(TAKEN);
      expect(field).toHaveValue(NEW);
      await waitFor(() => expect(field).toHaveFocus());
    });

    it("shows a status in the slot, and the form stays", async () => {
      const cooldown = `Det går inte att begära ett adressbyte just nu. Försök igen senare. ${SPENT}`;
      requestEmailChangeMock.mockResolvedValue({
        ok: false,
        kind: "operationRefused",
        error: cooldown,
        channel: "status",
      });
      const user = userEvent.setup();
      render(<ChangeEmailCard currentEmail={CURRENT} />);

      await reauthenticate(user);

      const status = await screen.findByText(cooldown);
      expect(status).toHaveAttribute("role", "status");
      await waitFor(() => expect(status).toHaveFocus());
      expect(screen.getByRole("button", { name: "Fortsätt" })).toBeInTheDocument();
    });

    it("replaces the card with the delivered panel when mail stops after the code", async () => {
      const mailOff = `E-postutskick är inte aktiverat just nu, så vi kan inte skicka någon kod. Din adress är oförändrad. Försök igen senare. ${SPENT}`;
      requestEmailChangeMock.mockResolvedValue({ ok: false, kind: "refused", error: mailOff });
      const user = userEvent.setup();
      render(<ChangeEmailCard currentEmail={CURRENT} />);

      await reauthenticate(user);

      const status = await screen.findByText(mailOff);
      expect(status).toHaveAttribute("role", "status");
      expect(screen.queryByText(/Du bekräftar bytet med två koder/)).not.toBeInTheDocument();
      const heading = screen.getByRole("heading", { level: 2, name: "Byt e-postadress" });
      await waitFor(() => expect(heading.parentElement).toHaveFocus());
    });

    it("closes the form for the day when no request can succeed", async () => {
      const budget = `Du har nått gränsen för hur många nya adresser du kan byta till per dygn. Vänta ett dygn och försök igen. ${SPENT}`;
      requestEmailChangeMock.mockResolvedValue({
        ok: false,
        kind: "operationRefused",
        error: budget,
        channel: "status",
        terminal: true,
      });
      const user = userEvent.setup();
      render(<ChangeEmailCard currentEmail={CURRENT} />);

      await reauthenticate(user);

      // By its text: until the dialog has closed, its resend region is a status too.
      const panel = (await screen.findByText(budget)).closest('[role="status"]');
      await waitFor(() => expect(panel).toHaveFocus());
      expect(screen.queryByLabelText("Ny e-postadress")).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Fortsätt" })).not.toBeInTheDocument();
    });
  });

  it("replaces the card with the delivered panel when no mail can be delivered", async () => {
    requestReauthCodeMock.mockResolvedValue({ ok: false, kind: "refused" });
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await user.type(screen.getByLabelText("Ny e-postadress"), NEW);
    await user.click(screen.getByRole("button", { name: "Fortsätt" }));
    await user.click(await screen.findByRole("button", { name: "Skicka kod" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(screen.getByRole("status")).toHaveTextContent(
      "E-postutskick är inte aktiverat just nu, så vi kan inte skicka någon kod."
    );
    // The promise of two codes is not repeated above its own denial.
    expect(screen.queryByText(/Du bekräftar bytet med två koder/)).not.toBeInTheDocument();
    const heading = screen.getByRole("heading", { level: 2, name: "Byt e-postadress" });
    await waitFor(() => expect(heading.parentElement).toHaveFocus());
  });

  it("never puts either challenge id in the page", async () => {
    const user = userEvent.setup();
    render(<ChangeEmailCard currentEmail={CURRENT} />);

    await toCodeStep(user);

    expect(document.body.innerHTML).not.toContain(REAUTH_CHALLENGE);
    expect(document.body.innerHTML).not.toContain(CHANGE_CHALLENGE);
  });
});
