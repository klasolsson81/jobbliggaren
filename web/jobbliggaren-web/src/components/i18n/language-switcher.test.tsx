import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { LanguageSwitcher } from "./language-switcher";

const setLocaleAction = vi.fn().mockResolvedValue(undefined);
vi.mock("@/i18n/set-locale-action", () => ({
  setLocaleAction: (locale: string) => setLocaleAction(locale),
}));

const refresh = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh }),
}));

// The global test provider (vitest alias) renders with locale "sv", so "sv" is
// the active locale here.
describe("LanguageSwitcher", () => {
  beforeEach(() => {
    setLocaleAction.mockClear();
    refresh.mockClear();
  });

  it("marks the active locale as pressed and the other as not", () => {
    render(<LanguageSwitcher />);
    expect(
      screen.getByRole("button", { name: "Svenska" }),
    ).toHaveAttribute("aria-pressed", "true");
    expect(
      screen.getByRole("button", { name: "English" }),
    ).toHaveAttribute("aria-pressed", "false");
  });

  it("exposes a labelled group with no flags or emoji (civic)", () => {
    render(<LanguageSwitcher />);
    const group = screen.getByRole("group", { name: "Språk" });
    expect(group).toBeInTheDocument();
    // Visible labels are the short language codes, not flags.
    expect(group.textContent).toBe("SVEN");
  });

  it("sets the cookie and refreshes when switching to the other locale", async () => {
    const user = userEvent.setup();
    render(<LanguageSwitcher />);
    await user.click(screen.getByRole("button", { name: "English" }));
    expect(setLocaleAction).toHaveBeenCalledWith("en");
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
  });

  it("does nothing when clicking the already-active locale", async () => {
    const user = userEvent.setup();
    render(<LanguageSwitcher />);
    await user.click(screen.getByRole("button", { name: "Svenska" }));
    expect(setLocaleAction).not.toHaveBeenCalled();
    expect(refresh).not.toHaveBeenCalled();
  });

  it("default variant renders the light-surface .jp-lang markup", () => {
    const { container } = render(<LanguageSwitcher />);
    expect(container.querySelector(".jp-lang")).not.toBeNull();
    expect(container.querySelector(".jp-lang__btn")).not.toBeNull();
    expect(container.querySelector(".jp-foot__lang")).toBeNull();
  });

  it('variant="footer" renders the deep-green .jp-foot__lang markup, same a11y/logic', async () => {
    const user = userEvent.setup();
    const { container } = render(<LanguageSwitcher variant="footer" />);
    // Consumes #254's dormant footer-lang classes, not the light .jp-lang path.
    expect(container.querySelector(".jp-foot__lang")).not.toBeNull();
    expect(container.querySelector(".jp-foot__lang-btn")).not.toBeNull();
    expect(container.querySelector(".jp-lang")).toBeNull();
    // a11y preserved: labelled group, active locale pressed.
    const group = screen.getByRole("group", { name: "Språk" });
    expect(
      screen.getByRole("button", { name: "Svenska" }),
    ).toHaveAttribute("aria-pressed", "true");
    expect(group).toContainElement(screen.getByRole("button", { name: "English" }));
    // logic preserved: switching still writes the cookie and refreshes.
    await user.click(screen.getByRole("button", { name: "English" }));
    expect(setLocaleAction).toHaveBeenCalledWith("en");
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
  });
});
