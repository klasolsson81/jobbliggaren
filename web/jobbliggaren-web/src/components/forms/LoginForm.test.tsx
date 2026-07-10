import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { LoginForm } from "./LoginForm";

// next/navigation: useSearchParams must be mocked in jsdom (no Next router context).
vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(),
}));

// loginAction is wired via useActionState. We mock the module so the form's
// formAction invokes our spy instead of calling fetch().
type AuthActionState = {
  error?: string;
  emailNotConfirmed?: boolean;
} | null;
const loginActionMock =
  vi.fn<
    (prevState: AuthActionState, formData: FormData) => Promise<AuthActionState>
  >();

vi.mock("@/lib/auth/actions", () => ({
  loginAction: (prevState: AuthActionState, formData: FormData) =>
    loginActionMock(prevState, formData),
}));

// The ResendConfirmationButton (rendered on the emailNotConfirmed state) imports the resend server
// action; mock it so importing LoginForm does not pull in the real fetch/env module.
vi.mock("@/lib/actions/resend-confirmation", () => ({
  resendConfirmationAction: vi.fn().mockResolvedValue({ success: true }),
}));

describe("LoginForm", () => {
  beforeEach(() => {
    loginActionMock.mockReset();
    loginActionMock.mockResolvedValue(null);
  });

  it("renders email, password and submit", () => {
    render(<LoginForm />);
    expect(screen.getByLabelText("E-postadress")).toBeInTheDocument();
    expect(screen.getByLabelText("Lösenord")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Logga in" })).toBeInTheDocument();
  });

  it("submits with the entered credentials", async () => {
    const user = userEvent.setup();
    render(<LoginForm />);

    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "hemligt1");
    await user.click(screen.getByRole("button", { name: "Logga in" }));

    expect(loginActionMock).toHaveBeenCalledTimes(1);
    const call = loginActionMock.mock.calls[0];
    if (!call) throw new Error("loginAction was not invoked");
    const formData = call[1];
    expect(formData).toBeInstanceOf(FormData);
    expect(formData.get("email")).toBe("anna@example.se");
    expect(formData.get("password")).toBe("hemligt1");
    // Post-login-default ändrad /mig → /jobb (senior-cto-advisor 2026-05-16,
    // Beslut 3: man loggar in för att söka jobb, inte ändra inställningar).
    expect(formData.get("next")).toBe("/jobb");
  });

  it("renders the 'Håll mig inloggad' checkbox, unticked by default (valid consent)", () => {
    render(<LoginForm />);
    const checkbox = screen.getByRole("checkbox", { name: "Håll mig inloggad" });
    expect(checkbox).toBeInTheDocument();
    // A pre-ticked box is invalid consent (GDPR Art. 7) — must start unchecked.
    expect(checkbox).not.toBeChecked();
    expect(checkbox).toHaveAttribute("name", "rememberMe");
  });

  it("does not post rememberMe when the box stays unticked", async () => {
    const user = userEvent.setup();
    render(<LoginForm />);

    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "hemligt1");
    await user.click(screen.getByRole("button", { name: "Logga in" }));

    const formData = loginActionMock.mock.calls[0]?.[1];
    if (!formData) throw new Error("loginAction was not invoked");
    // An unchecked native checkbox posts nothing → the action reads it as false.
    expect(formData.get("rememberMe")).toBeNull();
  });

  it("posts rememberMe=on when the box is ticked", async () => {
    const user = userEvent.setup();
    render(<LoginForm />);

    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "hemligt1");
    await user.click(screen.getByRole("checkbox", { name: "Håll mig inloggad" }));
    await user.click(screen.getByRole("button", { name: "Logga in" }));

    const formData = loginActionMock.mock.calls[0]?.[1];
    if (!formData) throw new Error("loginAction was not invoked");
    // A checked native checkbox posts the literal "on".
    expect(formData.get("rememberMe")).toBe("on");
  });

  it("shows server error as role=alert when action returns { error }", async () => {
    loginActionMock.mockResolvedValueOnce({
      error: "Inloggningen misslyckades. Kontrollera e-post och lösenord.",
    });

    const user = userEvent.setup();
    render(<LoginForm />);

    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "fel");
    await user.click(screen.getByRole("button", { name: "Logga in" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(
      "Inloggningen misslyckades. Kontrollera e-post och lösenord."
    );
  });

  it("#733 — shows the resend-confirmation button when the action returns emailNotConfirmed", async () => {
    loginActionMock.mockResolvedValueOnce({
      error:
        "Bekräfta din e-postadress för att logga in. Vi har skickat en länk till din inkorg.",
      emailNotConfirmed: true,
    });

    const user = userEvent.setup();
    render(<LoginForm />);

    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "hemligt1");
    await user.click(screen.getByRole("button", { name: "Logga in" }));

    expect(
      await screen.findByRole("button", { name: "Skicka en ny bekräftelselänk" })
    ).toBeInTheDocument();
  });

  it("does not show the resend-confirmation button on an ordinary login error", async () => {
    loginActionMock.mockResolvedValueOnce({
      error: "Inloggningen misslyckades. Kontrollera e-post och lösenord.",
    });

    const user = userEvent.setup();
    render(<LoginForm />);

    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "fel");
    await user.click(screen.getByRole("button", { name: "Logga in" }));

    await screen.findByRole("alert");
    expect(
      screen.queryByRole("button", { name: "Skicka en ny bekräftelselänk" })
    ).not.toBeInTheDocument();
  });

  it("marks email and password as required (HTML attribute + aria-required)", () => {
    render(<LoginForm />);
    // Native `required` plus explicit `aria-required="true"` — the design-a11y
    // skill §5 mandates aria-required in addition to the HTML attribute so the
    // required state is announced consistently across assistive tech.
    const email = screen.getByLabelText("E-postadress");
    const password = screen.getByLabelText("Lösenord");
    expect(email).toBeRequired();
    expect(password).toBeRequired();
    expect(email).toHaveAttribute("aria-required", "true");
    expect(password).toHaveAttribute("aria-required", "true");
  });

  it("flyttar focus till email-fältet när action returnerar { error } (TD-45)", async () => {
    loginActionMock.mockResolvedValueOnce({
      error: "Inloggningen misslyckades. Kontrollera e-post och lösenord.",
    });

    const user = userEvent.setup();
    render(<LoginForm />);

    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "fel");
    await user.click(screen.getByRole("button", { name: "Logga in" }));

    // Vänta på att error renderas så useEffect-cykeln för focus-flytt hinner köras.
    await screen.findByRole("alert");

    // Screen reader läser role="alert" automatiskt. Focus-flytt är för
    // keyboard-användare som scrollat förbi felmeddelandet — visuell anchor +
    // direkt recovery-action (skriva om credentials).
    expect(screen.getByLabelText("E-postadress")).toHaveFocus();
  });
});
