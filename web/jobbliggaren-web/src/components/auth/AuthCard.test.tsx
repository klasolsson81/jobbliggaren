import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AuthCard } from "./AuthCard";

// next/navigation: useSearchParams must be mocked in jsdom (no Next router
// context). A hoisted ref lets individual tests vary the query string to assert
// next-param deep-link propagation through both forms.
const { searchParamsRef, loginActionMock, registerActionMock } = vi.hoisted(
  () => ({
    searchParamsRef: { current: new URLSearchParams() },
    loginActionMock: vi.fn<(prev: unknown, data: FormData) => Promise<unknown>>(),
    registerActionMock:
      vi.fn<(prev: unknown, data: FormData) => Promise<unknown>>(),
  }),
);

vi.mock("next/navigation", () => ({
  useSearchParams: () => searchParamsRef.current,
}));

// The forms wire loginAction/registerAction via useActionState. Mock the module
// so their formAction invokes our spies instead of calling fetch().
vi.mock("@/lib/auth/actions", () => ({
  loginAction: (prev: unknown, data: FormData) => loginActionMock(prev, data),
  registerAction: (prev: unknown, data: FormData) =>
    registerActionMock(prev, data),
}));

describe("AuthCard", () => {
  beforeEach(() => {
    searchParamsRef.current = new URLSearchParams();
    loginActionMock.mockReset();
    loginActionMock.mockResolvedValue(null);
    registerActionMock.mockReset();
    registerActionMock.mockResolvedValue(null);
  });

  it("defaults to the register tab with the live RegisterForm mounted", () => {
    render(<AuthCard />);
    const registerTab = screen.getByRole("tab", { name: "Skapa konto" });
    const loginTab = screen.getByRole("tab", { name: "Logga in" });
    expect(registerTab).toHaveAttribute("aria-selected", "true");
    expect(loginTab).toHaveAttribute("aria-selected", "false");
    // Live register form: submit button (role=button) is distinct from the
    // same-named tab (role=tab). Login form is not mounted while inactive.
    expect(
      screen.getByRole("button", { name: "Skapa konto" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Logga in" }),
    ).not.toBeInTheDocument();
  });

  it("wires the APG tablist a11y contract", () => {
    const { container } = render(<AuthCard />);
    expect(
      screen.getByRole("tablist", { name: "Logga in eller skapa konto" }),
    ).toBeInTheDocument();

    const registerTab = screen.getByRole("tab", { name: "Skapa konto" });
    const loginTab = screen.getByRole("tab", { name: "Logga in" });
    // Roving tabindex: only the active tab is in the page tab sequence.
    expect(registerTab).toHaveAttribute("tabindex", "0");
    expect(loginTab).toHaveAttribute("tabindex", "-1");

    // aria-controls resolves to an existing panel; panel points back via
    // aria-labelledby. Both panel containers exist (active + hidden) so
    // aria-controls never dangles.
    const controls = registerTab.getAttribute("aria-controls");
    expect(controls).toBeTruthy();
    const registerPanel = controls ? document.getElementById(controls) : null;
    expect(registerPanel).not.toBeNull();
    expect(registerPanel).toHaveAttribute("role", "tabpanel");
    expect(registerPanel).toHaveAttribute("aria-labelledby", registerTab.id);
    expect(container.querySelectorAll('[role="tabpanel"]')).toHaveLength(2);
  });

  it("moves selection with ArrowRight/ArrowLeft (roving focus, automatic activation)", async () => {
    const user = userEvent.setup();
    render(<AuthCard />);
    await user.tab(); // focus the register tab (tabindex 0)
    expect(screen.getByRole("tab", { name: "Skapa konto" })).toHaveFocus();

    await user.keyboard("{ArrowRight}");
    const loginTab = screen.getByRole("tab", { name: "Logga in" });
    expect(loginTab).toHaveFocus();
    expect(loginTab).toHaveAttribute("aria-selected", "true");
    expect(
      screen.getByRole("button", { name: "Logga in" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Skapa konto" }),
    ).not.toBeInTheDocument();

    await user.keyboard("{ArrowLeft}");
    const registerTab = screen.getByRole("tab", { name: "Skapa konto" });
    expect(registerTab).toHaveFocus();
    expect(registerTab).toHaveAttribute("aria-selected", "true");
  });

  it("wraps with ArrowRight from the last tab to the first", async () => {
    const user = userEvent.setup();
    render(<AuthCard />);
    await user.tab();
    await user.keyboard("{ArrowRight}"); // -> login (last)
    await user.keyboard("{ArrowRight}"); // wrap -> register (first)
    const registerTab = screen.getByRole("tab", { name: "Skapa konto" });
    expect(registerTab).toHaveFocus();
    expect(registerTab).toHaveAttribute("aria-selected", "true");
  });

  it("jumps to first/last tab with Home/End", async () => {
    const user = userEvent.setup();
    render(<AuthCard />);
    await user.tab();
    await user.keyboard("{End}");
    expect(screen.getByRole("tab", { name: "Logga in" })).toHaveFocus();
    await user.keyboard("{Home}");
    expect(screen.getByRole("tab", { name: "Skapa konto" })).toHaveFocus();
  });

  // Under automatic activation the arrow keys already select the focused tab,
  // so there is no focus-without-activation path to isolate. This guards that
  // native <button> Enter/Space activation does not break that selection.
  it("keeps the focused tab active on Enter and Space (native button semantics)", async () => {
    const user = userEvent.setup();
    render(<AuthCard />);
    await user.tab();
    await user.keyboard("{ArrowRight}"); // login focused + active
    await user.keyboard("{Enter}");
    expect(screen.getByRole("tab", { name: "Logga in" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    expect(
      screen.getByRole("button", { name: "Logga in" }),
    ).toBeInTheDocument();

    await user.keyboard("{ArrowLeft}"); // register focused + active
    await user.keyboard(" ");
    expect(screen.getByRole("tab", { name: "Skapa konto" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
  });

  it("switches tabs on click", async () => {
    const user = userEvent.setup();
    render(<AuthCard />);
    await user.click(screen.getByRole("tab", { name: "Logga in" }));
    expect(screen.getByRole("tab", { name: "Logga in" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    expect(
      screen.getByRole("button", { name: "Logga in" }),
    ).toBeInTheDocument();
  });

  it("preserves the next deep-link param across both forms", async () => {
    searchParamsRef.current = new URLSearchParams("next=/installningar");
    const user = userEvent.setup();
    const { container } = render(<AuthCard />);
    // Register form (default tab) carries the hidden next.
    expect(container.querySelector('input[name="next"]')).toHaveValue(
      "/installningar",
    );
    await user.click(screen.getByRole("tab", { name: "Logga in" }));
    // Login form carries the same next after switching.
    expect(container.querySelector('input[name="next"]')).toHaveValue(
      "/installningar",
    );
  });

  it("defaults next to /jobb when no param is present", () => {
    const { container } = render(<AuthCard />);
    expect(container.querySelector('input[name="next"]')).toHaveValue("/jobb");
  });

  it("submits the register form via registerAction with credentials + next", async () => {
    searchParamsRef.current = new URLSearchParams("next=/jobb");
    const user = userEvent.setup();
    render(<AuthCard />);
    await user.type(screen.getByLabelText("E-postadress"), "ny@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "hemligt8tecken");
    await user.click(screen.getByRole("button", { name: "Skapa konto" }));

    expect(registerActionMock).toHaveBeenCalledTimes(1);
    const call = registerActionMock.mock.calls[0];
    if (!call) throw new Error("registerAction was not invoked");
    const data = call[1];
    expect(data).toBeInstanceOf(FormData);
    expect(data.get("email")).toBe("ny@example.se");
    expect(data.get("password")).toBe("hemligt8tecken");
    expect(data.get("next")).toBe("/jobb");
  });

  it("submits the login form via loginAction after switching tabs", async () => {
    const user = userEvent.setup();
    render(<AuthCard />);
    await user.click(screen.getByRole("tab", { name: "Logga in" }));
    await user.type(screen.getByLabelText("E-postadress"), "anna@example.se");
    await user.type(screen.getByLabelText("Lösenord"), "hemligt1");
    await user.click(screen.getByRole("button", { name: "Logga in" }));

    expect(loginActionMock).toHaveBeenCalledTimes(1);
    const call = loginActionMock.mock.calls[0];
    if (!call) throw new Error("loginAction was not invoked");
    const data = call[1];
    expect(data.get("email")).toBe("anna@example.se");
    expect(data.get("password")).toBe("hemligt1");
  });

  it("renders no OAuth, no Namn field, and no placeholder text", () => {
    const { container } = render(<AuthCard />);
    expect(screen.queryByText(/fortsätt med/i)).not.toBeInTheDocument();
    expect(
      screen.queryByText(/Google|GitHub|LinkedIn/i),
    ).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/namn/i)).not.toBeInTheDocument();
    container.querySelectorAll("input").forEach((input) => {
      expect(input).not.toHaveAttribute("placeholder");
    });
  });

  it("describes the register password field with the min-length hint", () => {
    render(<AuthCard />);
    const password = screen.getByLabelText("Lösenord");
    expect(password).toHaveAttribute("aria-describedby", "password-hint");
    const hint = document.getElementById("password-hint");
    expect(hint).toHaveTextContent("Minst 8 tecken.");
  });

  it("shows the register fine-print only on the register tab", async () => {
    const user = userEvent.setup();
    render(<AuthCard />);
    expect(
      screen.getByText(/Genom att skapa konto godkänner du/i),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("tab", { name: "Logga in" }));
    expect(
      screen.queryByText(/Genom att skapa konto godkänner du/i),
    ).not.toBeInTheDocument();
  });
});
