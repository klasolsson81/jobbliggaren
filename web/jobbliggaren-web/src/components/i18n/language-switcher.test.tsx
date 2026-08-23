import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { LanguageSwitcher } from "./language-switcher";
import { locales } from "@/i18n/routing";

const setLocaleAction = vi.hoisted(() => vi.fn(async () => undefined));
vi.mock("@/i18n/set-locale-action", () => ({ setLocaleAction }));

const refresh = vi.hoisted(() => vi.fn());
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh, push: vi.fn(), prefetch: vi.fn() }),
  usePathname: () => "/",
  useSearchParams: () => new URLSearchParams(),
}));

// The global test provider renders with locale "sv".
beforeEach(() => {
  setLocaleAction.mockClear();
  refresh.mockClear();
});

const openMenu = async () => {
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: /Språk/i }));
  return user;
};

describe("LanguageSwitcher (menu, #1476)", () => {
  it("names itself 'Språk' AND shows the current language (WCAG 2.5.3)", () => {
    render(<LanguageSwitcher />);
    // The accessible name CONTAINS the visible label rather than replacing it:
    // an aria-label of just "Språk" would fail Label in Name, because the word
    // a user says ("Svenska") would not be in the name.
    const trigger = screen.getByRole("button", { name: /Språk/i });
    expect(trigger).toHaveAccessibleName(/Svenska/);
    expect(trigger).toHaveTextContent("Svenska");
  });

  it("uses full language names, never the SV/EN codes", () => {
    // The previous switcher rendered exactly "SVEN" — two mono codes. A visitor
    // who does not read Swedish has to recognise their own language, so the codes
    // are the thing this must not regress to (DESIGN.md §7: no flags, no emoji).
    const { container } = render(<LanguageSwitcher />);
    expect(container.textContent).not.toBe("SVEN");
    expect(container.textContent).toContain("Svenska");
    expect(screen.queryByText("SV")).toBeNull();
    expect(screen.queryByText("EN")).toBeNull();
  });

  it("offers one menuitemradio per locale, with the active one checked", async () => {
    render(<LanguageSwitcher />);
    await openMenu();
    const options = await screen.findAllByRole("menuitemradio");
    // Rendered from `locales`, not hardcoded: a third locale needs no change here.
    expect(options).toHaveLength(locales.length);
    expect(
      screen.getByRole("menuitemradio", { name: "Svenska" }),
    ).toHaveAttribute("aria-checked", "true");
    expect(
      screen.getByRole("menuitemradio", { name: "English" }),
    ).toHaveAttribute("aria-checked", "false");
  });

  it("a language is a CHOICE, not an action — menuitemradio, never menuitem", async () => {
    // DropdownMenuItem would give role="menuitem" with no aria-checked, which
    // says "this performs something" and leaves the current locale unannounced.
    render(<LanguageSwitcher />);
    await openMenu();
    await screen.findAllByRole("menuitemradio");
    expect(screen.queryByRole("menuitem")).toBeNull();
  });

  it("sets the cookie and refreshes when switching to the other locale", async () => {
    render(<LanguageSwitcher />);
    const user = await openMenu();
    await user.click(
      await screen.findByRole("menuitemradio", { name: "English" }),
    );
    expect(setLocaleAction).toHaveBeenCalledWith("en");
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
  });

  it("does nothing when choosing the already-active locale", async () => {
    render(<LanguageSwitcher />);
    const user = await openMenu();
    await user.click(
      await screen.findByRole("menuitemradio", { name: "Svenska" }),
    );
    expect(setLocaleAction).not.toHaveBeenCalled();
    expect(refresh).not.toHaveBeenCalled();
  });
});
