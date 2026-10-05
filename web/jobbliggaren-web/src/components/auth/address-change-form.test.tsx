import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AddressChangeState } from "@/lib/auth/address-change";
import { CODE_MAX_ATTEMPTS } from "@/lib/auth/login-flow";
import { AddressChangeForm } from "./address-change-form";

// #1975 — /adressbyte's form in every state design-reviewer bound (items 10–13): the fields and their order, the
// browser's check, the one refusal, not yet, the two statuses, the unknown outcome and the receipt. `render` is
// auto-wrapped in the Swedish catalogue.

const actionMock = vi.fn<(prev: AddressChangeState, formData: FormData) => Promise<AddressChangeState>>();

vi.mock("@/lib/auth/address-change-actions", () => ({
  completeAddressChange: (prev: AddressChangeState, formData: FormData) => actionMock(prev, formData),
}));

const CURRENT = "anna@exempel.se";
const NEW = "anna.ny@exempel.se";
const CODE = "482915";
const VALUES = { currentEmail: CURRENT, newEmail: NEW };

type User = ReturnType<typeof userEvent.setup>;

const current = () => screen.getByLabelText("Kontots nuvarande e-postadress");
const next = () => screen.getByLabelText("Ny e-postadress");
const code = () => screen.getByLabelText("Sexsiffrig kod");
const submit = () => screen.getByRole("button", { name: "Byt adress" });

async function fillAndSend(user: User) {
  await user.type(current(), CURRENT);
  await user.type(next(), NEW);
  await user.type(code(), CODE);
  await user.click(submit());
}

/** The attempt budget in words, as the refusal states it; a budget with no word here is a copy decision to make. */
const ATTEMPTS: Readonly<Record<number, string>> = {
  1: "ett felaktigt försök",
  2: "två felaktiga försök",
  3: "tre felaktiga försök",
};

describe("AddressChangeForm (#1975)", () => {
  beforeEach(() => {
    actionMock.mockReset();
    actionMock.mockResolvedValue(null);
  });

  it("asks for the current address, the new one and the code, in that order, each with its hint, and nothing at rest", () => {
    render(<AddressChangeForm />);

    const fields = [current(), next(), code()];
    expect(fields[0]!.compareDocumentPosition(fields[1]!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(fields[1]!.compareDocumentPosition(fields[2]!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    expect(current()).toHaveAttribute("type", "email");
    expect(current()).toHaveAttribute("autocomplete", "username");
    expect(current()).toHaveAccessibleDescription("Adressen du byter från.");
    expect(next()).toHaveAttribute("type", "email");
    expect(next()).toHaveAttribute("autocomplete", "email");
    expect(next()).toHaveAccessibleDescription("Adressen som koden skickades till.");
    for (const field of [current(), next()]) {
      expect(field).toHaveAttribute("spellcheck", "false");
      expect(field).toHaveAttribute("autocapitalize", "none");
      expect(field).toBeRequired();
      expect(field).toHaveAttribute("aria-required", "true");
      expect(field).not.toHaveAttribute("placeholder");
      expect(field).not.toHaveAttribute("aria-invalid");
    }
    expect(code()).toHaveAttribute("autocomplete", "one-time-code");
    expect(code()).toHaveAccessibleDescription("Titta även i skräpposten.");

    expect(submit()).toHaveAccessibleDescription("När adressen är bytt loggas du ut på alla enheter.");
    expect(submit()).toHaveClass("w-full");
    expect(submit().closest("form")).toHaveAttribute("novalidate");
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("checks the fields when Byt adress is pressed, under each field, and sends nothing until they pass", async () => {
    const user = userEvent.setup();
    render(<AddressChangeForm />);

    await user.click(submit());

    expect(current()).toHaveAttribute("aria-invalid", "true");
    expect(current()).toHaveAccessibleDescription("Adressen du byter från. Skriv in kontots nuvarande e-postadress.");
    expect(next()).toHaveAccessibleDescription("Adressen som koden skickades till. Skriv in den nya e-postadressen.");
    expect(code()).toHaveAttribute("aria-invalid", "true");
    expect(code()).toHaveAccessibleDescription("Titta även i skräpposten. Skriv in koden från mejlet.");
    await waitFor(() => expect(current()).toHaveFocus());
    expect(actionMock).not.toHaveBeenCalled();
  });

  it.each([
    ["a malformed address", { currentEmail: "anna.exempel.se" }, "Kontots nuvarande e-postadress", "Skriv e-postadressen i rätt format, till exempel namn@exempel.se."],
    ["the current address again, in another case", { newEmail: "ANNA@EXEMPEL.SE" }, "Ny e-postadress", "Den nya adressen är samma som den nuvarande. Kontrollera adresserna."],
    ["a code that is not six digits", { code: "48291" }, "Sexsiffrig kod", "Koden är sex siffror. Kontrollera siffrorna och försök igen."],
  ])("refuses %s on its own field, with focus there", async (_label, typed, label, copy) => {
    const user = userEvent.setup();
    render(<AddressChangeForm />);
    const values = { currentEmail: CURRENT, newEmail: NEW, code: CODE, ...typed };

    await user.type(current(), values.currentEmail);
    await user.type(next(), values.newEmail);
    await user.type(code(), values.code);
    await user.click(submit());

    const field = screen.getByLabelText(label);
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("alert")).toHaveTextContent(copy);
    await waitFor(() => expect(field).toHaveFocus());
    expect(actionMock).not.toHaveBeenCalled();
  });

  it("sends the three fields as typed to the action", async () => {
    const user = userEvent.setup();
    render(<AddressChangeForm />);

    await fillAndSend(user);

    await waitFor(() => expect(actionMock).toHaveBeenCalledTimes(1));
    const data = actionMock.mock.lastCall![1];
    expect([data.get("currentEmail"), data.get("newEmail"), data.get("code")]).toEqual([CURRENT, NEW, CODE]);
  });

  it("answers every refusal with one message for the form, marking no field, the addresses kept and the code cleared", async () => {
    actionMock.mockResolvedValue({ kind: "refused", values: VALUES });
    const user = userEvent.setup();
    render(<AddressChangeForm />);

    await fillAndSend(user);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(
      "Adressbytet gick inte att genomföra. Kontrollera båda adresserna och koden och försök igen. " +
        "Koden slutar gälla efter tre felaktiga försök eller när tiden i mejlet har gått ut. " +
        "Skriv då till kontakt@jobbliggaren.se.",
    );
    expect(within(alert).getByRole("link", { name: "kontakt@jobbliggaren.se" })).toHaveAttribute(
      "href",
      "mailto:kontakt@jobbliggaren.se",
    );
    await waitFor(() => expect(alert).toHaveFocus());
    expect(document.querySelectorAll("[aria-invalid]")).toHaveLength(0);
    await waitFor(() => expect(current()).toHaveValue(CURRENT));
    expect(next()).toHaveValue(NEW);
    expect(code()).toHaveValue("");
    expect(document.body.innerHTML).not.toContain(CODE);
  });

  it("states the attempt budget the refusal speaks of from the constant that mirrors the backend's", async () => {
    // CODE_MAX_ATTEMPTS mirrors LoginChallengePolicy.MaxAttempts; the copy reads it, and this pins what it then says.
    expect(ATTEMPTS[CODE_MAX_ATTEMPTS]).toBeDefined();
    actionMock.mockResolvedValue({ kind: "refused", values: VALUES });
    const user = userEvent.setup();
    render(<AddressChangeForm />);

    await fillAndSend(user);

    expect(await screen.findByRole("alert")).toHaveTextContent(`efter ${ATTEMPTS[CODE_MAX_ATTEMPTS]} eller`);
  });

  it("says when the address can change, in Swedish time, as a status that takes focus", async () => {
    actionMock.mockResolvedValue({ kind: "notYet", completableFrom: "2026-10-08T12:00:00+00:00", values: VALUES });
    const user = userEvent.setup();
    render(<AddressChangeForm />);

    await fillAndSend(user);

    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("Adressen kan bytas tidigast 2026-10-08 kl 14:00. Försök igen då med samma kod.");
    await waitFor(() => expect(status).toHaveFocus());
    expect(next()).toHaveValue(NEW);
  });

  it.each([
    [{ kind: "tooManyAttempts", values: VALUES }, "För många försök. Vänta en stund och försök igen."],
    [{ kind: "unavailable", values: VALUES }, "Det går inte att byta adress just nu. Försök igen om några minuter."],
  ] as const)("keeps %o in the status slot, without the danger colour, the addresses kept", async (state, copy) => {
    actionMock.mockResolvedValue(state);
    const user = userEvent.setup();
    render(<AddressChangeForm />);

    await fillAndSend(user);

    const status = await screen.findByText(copy);
    expect(status).toHaveAttribute("role", "status");
    expect(status).not.toHaveClass("text-danger-600");
    await waitFor(() => expect(status).toHaveFocus());
    expect(current()).toHaveValue(CURRENT);
  });

  it("claims nothing when the outcome is unknown, advises no retry and names where to write", async () => {
    actionMock.mockResolvedValue({ kind: "unknown", values: VALUES });
    const user = userEvent.setup();
    render(<AddressChangeForm />);

    await fillAndSend(user);

    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent(
      "Vi kan inte se om adressen byttes. Skriv till kontakt@jobbliggaren.se så tar vi reda på det.",
    );
    expect(status).not.toHaveTextContent(/försök igen/i);
    expect(within(status).getByRole("link", { name: "kontakt@jobbliggaren.se" })).toBeInTheDocument();
    await waitFor(() => expect(status).toHaveFocus());
  });

  it("replaces the form with the receipt and a way to log in, with no address in the link, and moves focus there", async () => {
    actionMock.mockResolvedValue({ kind: "done" });
    const user = userEvent.setup();
    render(<AddressChangeForm />);

    await fillAndSend(user);

    const heading = await screen.findByRole("heading", { level: 2, name: "Adressen är bytt" });
    const panel = heading.parentElement!;
    expect(panel).toHaveTextContent("Du är utloggad på alla enheter. Logga in med den nya adressen.");
    expect(screen.getByRole("link", { name: "Logga in" })).toHaveAttribute("href", "/logga-in");
    await waitFor(() => expect(panel).toHaveFocus());
    expect(screen.queryByRole("button", { name: "Byt adress" })).toBeNull();
  });

  it("marks the fields the action refused, as it does without JavaScript", async () => {
    actionMock.mockResolvedValue({
      kind: "invalid",
      errors: { newEmail: "same" },
      values: { currentEmail: CURRENT, newEmail: CURRENT },
    });
    const user = userEvent.setup();
    render(<AddressChangeForm />);

    await fillAndSend(user);

    await waitFor(() => expect(next()).toHaveAttribute("aria-invalid", "true"));
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Den nya adressen är samma som den nuvarande. Kontrollera adresserna.",
    );
    await waitFor(() => expect(next()).toHaveFocus());
    expect(next()).toHaveValue(CURRENT);
  });
});
