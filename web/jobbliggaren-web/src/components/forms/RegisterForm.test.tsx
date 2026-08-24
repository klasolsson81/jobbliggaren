import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RegisterForm } from "./RegisterForm";

// next/navigation: useSearchParams must be mocked in jsdom (no Next router context).
vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(),
}));

// registerAction is wired via useActionState. We mock the module so the form's
// formAction invokes our spy instead of calling fetch().
type AuthActionState = {
  error?: string;
  pendingConfirmation?: boolean;
  registrationsClosed?: boolean;
  email?: string;
  field?: "displayName" | "acceptTerms" | "password";
  values?: {
    displayName?: string;
    email?: string;
    rememberMe?: boolean;
    acceptTerms?: boolean;
  };
} | null;
const registerActionMock =
  vi.fn<
    (prevState: AuthActionState, formData: FormData) => Promise<AuthActionState>
  >();

vi.mock("@/lib/auth/actions", () => ({
  registerAction: (prevState: AuthActionState, formData: FormData) =>
    registerActionMock(prevState, formData),
}));

// The check-inbox panel renders ResendConfirmationButton, which imports the resend server action;
// mock it so importing RegisterForm does not pull in the real fetch/env module.
vi.mock("@/lib/actions/resend-confirmation", () => ({
  resendConfirmationAction: vi.fn().mockResolvedValue({ success: true }),
}));

// #1479 — the accessible name of the terms checkbox, and the reason every test that reaches
// the action ticks it: the box is `required`, so a submit without it never fires the action.
const TERMS = "Jag godkänner användarvillkoren och integritetspolicyn.";

describe("RegisterForm", () => {
  beforeEach(() => {
    registerActionMock.mockReset();
    registerActionMock.mockResolvedValue(null);
  });

  it("renders name, email, password and submit", () => {
    render(<RegisterForm />);
    expect(screen.getByLabelText("Namn")).toBeInTheDocument();
    expect(screen.getByLabelText("E-postadress")).toBeInTheDocument();
    expect(screen.getByLabelText("Lösenord")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Skapa konto" })
    ).toBeInTheDocument();
  });

  it("renders NO remember-me control — it belongs on the login page (#1478)", () => {
    render(<RegisterForm />);
    expect(screen.queryByRole("checkbox", { name: "Håll mig inloggad" })).toBeNull();
    expect(screen.getAllByRole("checkbox")).toHaveLength(1);
  });

  it("posts no rememberMe at all, so a new account's session is not persistent", async () => {
    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    const formData = registerActionMock.mock.calls[0]?.[1];
    if (!formData) throw new Error("registerAction was not invoked");
    expect(formData.get("displayName")).toBe("Anna Andersson");
    expect(formData.get("email")).toBe("anna@example.se");
    expect(formData.get("password")).toBe("password1");
    expect(formData.get("rememberMe")).toBeNull();
  });

  it("marks name, email and password as required (HTML attribute + aria-required)", () => {
    render(<RegisterForm />);
    for (const label of ["Namn", "E-postadress", "Lösenord"]) {
      const field = screen.getByLabelText(label);
      expect(field).toBeRequired();
      expect(field).toHaveAttribute("aria-required", "true");
    }
  });

  it("ADR 0083 — replaces the form with a focused status panel when registration is closed", async () => {
    // The channel matters as much as the copy: a deliberate pre-launch state must not render as a
    // validation error above a live submit button, which would invite a retry that cannot succeed.
    registerActionMock.mockResolvedValue({ registrationsClosed: true });
    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    const panel = await screen.findByRole("status");
    expect(panel).toHaveTextContent("Registreringen är inte öppen ännu.");
    expect(screen.queryByRole("button", { name: "Skapa konto" })).not.toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();

    // Focus must move to the panel. Submitting unmounts the form, so without this the focused
    // element leaves the DOM and focus falls to <body> — the next Tab restarts at the skip link —
    // and a live region that mounts already filled is routinely missed by NVDA/JAWS (WCAG 4.1.3).
    await waitFor(() => expect(panel).toHaveFocus());
  });

  it("#714 — shows the check-inbox panel (not the form) when the action returns pendingConfirmation", async () => {
    registerActionMock.mockResolvedValue({ pendingConfirmation: true });
    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    // The status panel replaces the form; the submit button is gone. Query the panel heading rather
    // than role=status — #733 adds ResendConfirmationButton, whose own persistent aria-live region
    // makes role=status ambiguous inside the panel.
    await waitFor(() =>
      expect(
        screen.getByRole("heading", { name: "Kontrollera din inkorg" }),
      ).toBeInTheDocument(),
    );
    expect(screen.queryByRole("button", { name: "Skapa konto" })).not.toBeInTheDocument();
  });

  it("#733 — offers the resend-confirmation button inside the check-inbox panel", async () => {
    registerActionMock.mockResolvedValue({
      pendingConfirmation: true,
      email: "anna@example.se",
    });
    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    expect(
      await screen.findByRole("button", { name: "Skicka en ny bekräftelselänk" })
    ).toBeInTheDocument();
  });

  // #1117 — the refusal names one input with one fix, so it must be wired to that input and
  // not merely rendered near it. Both polarities: the discriminator is only meaningful if its
  // ABSENCE is pinned too, otherwise stamping every failure would pass the positive case while
  // telling a screen-reader user her name is wrong when the network dropped.
  it("wires aria-invalid, aria-describedby and focus when the action names the field", async () => {
    registerActionMock.mockResolvedValue({
      error: "Namnet far inte innehalla ett personnummer.",
      field: "displayName",
    });

    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna 811218-9876");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    const alert = await screen.findByRole("alert");
    const nameInput = screen.getByLabelText("Namn");

    expect(nameInput).toHaveAttribute("aria-invalid", "true");
    expect(alert.id).not.toBe("");
    expect(nameInput.getAttribute("aria-describedby")).toContain(alert.id);
    await waitFor(() => expect(nameInput).toHaveFocus());
  });

  it("leaves the name input unmarked for a failure that is not about the field", async () => {
    registerActionMock.mockResolvedValue({ error: "Kunde inte na servern." });

    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    await screen.findByRole("alert");
    const nameInput = screen.getByLabelText("Namn");

    expect(nameInput).not.toHaveAttribute("aria-invalid");
    expect(nameInput.getAttribute("aria-describedby")).toBe("name-hint");
  });

  // #1479 — the acceptance lives in the FORM, not in the surface that mounts it.
  it("renders the terms checkbox, unticked by default and required", () => {
    render(<RegisterForm />);
    const box = screen.getByRole("checkbox", {
      name: TERMS,
    });
    // Unticked: a pre-ticked box is not an acceptance the user performed.
    expect(box).not.toBeChecked();
    expect(box).toBeRequired();
    expect(box).toHaveAttribute("aria-required", "true");
    expect(box).toHaveAttribute("name", "acceptTerms");
  });

  it("names the two policies as real links to their live routes", () => {
    render(<RegisterForm />);
    expect(
      screen.getByRole("link", { name: "användarvillkoren" }),
    ).toHaveAttribute("href", "/villkor");
    expect(
      screen.getByRole("link", { name: "integritetspolicyn" }),
    ).toHaveAttribute("href", "/integritet");
  });

  it("describes the terms checkbox with the new-tab warning, and keeps it out of the name", () => {
    // The links open in a new tab, which has to be announced somewhere. In each link's own
    // accessible name it would be read back twice inside the checkbox's name, so it lives in
    // the hint — which only counts as announced if the checkbox actually points at it.
    render(<RegisterForm />);
    const box = screen.getByRole("checkbox", {
      name: TERMS,
    });
    const hintId = (box.getAttribute("aria-describedby") ?? "").split(" ")[0] ?? "";
    expect(hintId).not.toBe("");
    expect(document.getElementById(hintId)).toHaveTextContent(
      "Länkarna öppnas i en ny flik.",
    );
  });

  it("posts acceptTerms=on when the box is ticked", async () => {
    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    const formData = registerActionMock.mock.calls[0]?.[1];
    if (!formData) throw new Error("registerAction was not invoked");
    expect(formData.get("acceptTerms")).toBe("on");
  });

  it("does not reach the action at all when the box is left unticked", async () => {
    // The client-side half of the gate: `required` + constraint validation, which jsdom
    // implements, so this measures the block rather than the attribute. The server-side half
    // is pinned in lib/auth/actions.test.ts — a caller that never rendered the form.
    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    expect(registerActionMock).not.toHaveBeenCalled();
  });

  it("wires aria-invalid, aria-describedby and focus when the action refuses on the terms", async () => {
    // Same both-polarities discipline as the displayName pair above: the server-side refusal is
    // only reachable past a browser that skipped `required`, and it has to name its own input.
    registerActionMock.mockResolvedValue({
      error:
        "Du måste godkänna användarvillkoren och integritetspolicyn för att skapa konto.",
      field: "acceptTerms",
    });

    const user = userEvent.setup();
    render(<RegisterForm />);
    const box = screen.getByRole("checkbox", {
      name: TERMS,
    });

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(box);
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    const alert = await screen.findByRole("alert");
    expect(box).toHaveAttribute("aria-invalid", "true");
    expect(alert.id).not.toBe("");
    expect(box.getAttribute("aria-describedby")).toContain(alert.id);
    await waitFor(() => expect(box).toHaveFocus());
  });

  // React 19 resets an uncontrolled `<form action={…}>` after EVERY action, so a failed submit used
  // to destroy the name, the address and the ticked terms box. The action echoes the non-secret
  // fields back and the form re-seeds itself from that echo.
  it("keeps the name, address and ticked terms after a failure — but never the password", async () => {
    registerActionMock.mockResolvedValue({
      error: "Kunde inte na servern.",
      values: {
        displayName: "Anna Andersson",
        email: "anna@example.se",
        acceptTerms: true,
      },
    });

    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    await screen.findByRole("alert");

    await waitFor(() => {
      expect(screen.getByLabelText("Namn")).toHaveValue("Anna Andersson");
    });
    expect(screen.getByLabelText("E-postadress")).toHaveValue("anna@example.se");
    expect(screen.getByRole("checkbox", { name: TERMS })).toBeChecked();
    // The counterfactual, and it is what keeps the three assertions above from measuring nothing:
    // the password WAS typed and IS gone, so the reset demonstrably ran on this very submit. It is
    // deliberately absent from the echo — a re-seeded password would be a plaintext secret riding
    // a payload for no gain, so it is the one field a retry retypes.
    expect(screen.getByLabelText("Lösenord")).toHaveValue("");
  });

  it("does not re-tick the terms box when the submit did not carry the acceptance", async () => {
    // The echo restores an acceptance the user performed; it must never manufacture one. The
    // server-side refusal is reachable past a client that skipped `required`, and its echo reports
    // the box as it arrived: unticked.
    registerActionMock.mockResolvedValue({
      error: "Du måste godkänna användarvillkoren och integritetspolicyn för att skapa konto.",
      field: "acceptTerms",
      values: {
        displayName: "Anna Andersson",
        email: "anna@example.se",
        acceptTerms: false,
      },
    });

    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    await screen.findByRole("alert");
    expect(screen.getByRole("checkbox", { name: TERMS })).not.toBeChecked();
  });

  // #1117 sends focus to the named input. A failure that names NO input had nowhere to send it: the
  // submit button is disabled during the action, so focus fell to <body> and the next Tab restarted
  // at the skip link. The message is the only honest target.
  it("moves focus to the message when the failure names no field", async () => {
    registerActionMock.mockResolvedValue({ error: "Kunde inte na servern." });

    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    const alert = await screen.findByRole("alert");
    await waitFor(() => expect(alert).toHaveFocus());
    // Programmatic focus only — the message must not join the Tab order.
    expect(alert).toHaveAttribute("tabindex", "-1");
  });

  it("leaves focus on the named input when the failure DOES name one", async () => {
    // The counterfactual for the move above: a field error still lands on the field, which is the
    // control the user has to change. Without this, focusing the message unconditionally would
    // silently undo #1117 and the test above would not notice.
    registerActionMock.mockResolvedValue({
      error: "Namnet far inte innehalla ett personnummer.",
      field: "displayName",
    });

    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna 811218-9876");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    await screen.findByRole("alert");
    await waitFor(() => expect(screen.getByLabelText("Namn")).toHaveFocus());
  });

  it("wires aria-invalid, aria-describedby and focus when the password is refused as breached", async () => {
    // A breached password is a refusal about ONE field, fixed by changing it — the same wiring
    // reset-password gives the identical refusal.
    registerActionMock.mockResolvedValue({
      error: "Lösenordet finns i kända läckor. Välj ett annat.",
      field: "password",
      values: {
        displayName: "Anna Andersson",
        email: "anna@example.se",
        acceptTerms: true,
      },
    });

    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    const alert = await screen.findByRole("alert");
    const password = screen.getByLabelText("Lösenord");

    expect(password).toHaveAttribute("aria-invalid", "true");
    expect(password.getAttribute("aria-describedby")).toContain(alert.id);
    await waitFor(() => expect(password).toHaveFocus());
    // The message itself must NOT take focus here — it is not a nameless failure any more.
    expect(alert).not.toHaveFocus();
  });

  it("leaves the password unmarked for a failure that is not about it", async () => {
    registerActionMock.mockResolvedValue({ error: "Kunde inte na servern." });

    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: TERMS }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    await screen.findByRole("alert");
    const password = screen.getByLabelText("Lösenord");
    expect(password).not.toHaveAttribute("aria-invalid");
    expect(password.getAttribute("aria-describedby")).toBe("password-hint");
  });

  it("leaves the terms checkbox unmarked for a failure that is not about it", async () => {
    registerActionMock.mockResolvedValue({ error: "Kunde inte na servern." });

    const user = userEvent.setup();
    render(<RegisterForm />);
    const box = screen.getByRole("checkbox", {
      name: TERMS,
    });

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(box);
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    await screen.findByRole("alert");
    expect(box).not.toHaveAttribute("aria-invalid");
  });
});
