import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PasswordInput } from "./PasswordInput";

// `render` is the Intl shim (vitest.config alias) → real Swedish catalog, so the
// toggle's aria-label resolves to the actual auth.showPassword/hidePassword copy.
describe("PasswordInput (#586 show/hide toggle)", () => {
  it("masks by default and reveals/hides via the toggle", async () => {
    const user = userEvent.setup();
    render(<PasswordInput id="password" name="password" aria-label="Lösenord" />);
    const input = screen.getByLabelText("Lösenord");

    // Masked by default.
    expect(input).toHaveAttribute("type", "password");

    // Toggle is a non-submitting button, initially "show", not pressed.
    const show = screen.getByRole("button", { name: "Visa lösenord" });
    expect(show).toHaveAttribute("type", "button");
    expect(show).toHaveAttribute("aria-pressed", "false");

    // Reveal.
    await user.click(show);
    expect(input).toHaveAttribute("type", "text");
    const hide = screen.getByRole("button", { name: "Dölj lösenord" });
    expect(hide).toHaveAttribute("aria-pressed", "true");

    // Hide again.
    await user.click(hide);
    expect(input).toHaveAttribute("type", "password");
    expect(
      screen.getByRole("button", { name: "Visa lösenord" }),
    ).toHaveAttribute("aria-pressed", "false");
  });

  it("forwards field props to the underlying input", () => {
    render(
      <PasswordInput
        name="password"
        autoComplete="current-password"
        required
        aria-required="true"
        aria-label="Lösenord"
      />,
    );
    const input = screen.getByLabelText("Lösenord");
    expect(input).toHaveAttribute("name", "password");
    expect(input).toHaveAttribute("autocomplete", "current-password");
    expect(input).toBeRequired();
    expect(input).toHaveAttribute("aria-required", "true");
  });
});
