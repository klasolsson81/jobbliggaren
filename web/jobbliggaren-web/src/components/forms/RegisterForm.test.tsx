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
type AuthActionState = { error?: string; pendingConfirmation?: boolean } | null;
const registerActionMock =
  vi.fn<
    (prevState: AuthActionState, formData: FormData) => Promise<AuthActionState>
  >();

vi.mock("@/lib/auth/actions", () => ({
  registerAction: (prevState: AuthActionState, formData: FormData) =>
    registerActionMock(prevState, formData),
}));

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

  it("renders the 'Håll mig inloggad' checkbox, unticked by default (valid consent)", () => {
    render(<RegisterForm />);
    const checkbox = screen.getByRole("checkbox", { name: "Håll mig inloggad" });
    expect(checkbox).toBeInTheDocument();
    // A pre-ticked box is invalid consent (GDPR Art. 7) — must start unchecked.
    expect(checkbox).not.toBeChecked();
    expect(checkbox).toHaveAttribute("name", "rememberMe");
  });

  it("posts rememberMe=on only when the box is ticked", async () => {
    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("checkbox", { name: "Håll mig inloggad" }));
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    const formData = registerActionMock.mock.calls[0]?.[1];
    if (!formData) throw new Error("registerAction was not invoked");
    expect(formData.get("displayName")).toBe("Anna Andersson");
    expect(formData.get("email")).toBe("anna@example.se");
    expect(formData.get("password")).toBe("password1");
    expect(formData.get("rememberMe")).toBe("on");
  });

  it("marks name, email and password as required (HTML attribute + aria-required)", () => {
    render(<RegisterForm />);
    for (const label of ["Namn", "E-postadress", "Lösenord"]) {
      const field = screen.getByLabelText(label);
      expect(field).toBeRequired();
      expect(field).toHaveAttribute("aria-required", "true");
    }
  });

  it("#714 — shows the check-inbox panel (not the form) when the action returns pendingConfirmation", async () => {
    registerActionMock.mockResolvedValue({ pendingConfirmation: true });
    const user = userEvent.setup();
    render(<RegisterForm />);

    await user.type(screen.getByLabelText("Namn"), "Anna Andersson");
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "password1");
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    // The status panel replaces the form; the submit button is gone.
    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent("Kontrollera din inkorg"),
    );
    expect(screen.queryByRole("button", { name: "Skapa konto" })).not.toBeInTheDocument();
  });
});
