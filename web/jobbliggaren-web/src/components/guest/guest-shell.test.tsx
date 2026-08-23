import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { GuestShell } from "./guest-shell";

// usePathname drives aria-current on the guest nav links — mocked per route.
const pathnameMock = vi.fn<() => string>();
vi.mock("next/navigation", () => ({
  usePathname: () => pathnameMock(),
  // The header's LanguageSwitcher reads useRouter to refresh after the cookie
  // write (ADR 0078); without it the shell cannot render at all.
  useRouter: () => ({ refresh: vi.fn(), push: vi.fn(), prefetch: vi.fn() }),
}));

vi.mock("@/i18n/set-locale-action", () => ({
  setLocaleAction: vi.fn(async () => undefined),
}));

describe("GuestShell (LP-5b #259 — composes the shared HeaderStrip)", () => {
  beforeEach(() => {
    pathnameMock.mockReset();
    pathnameMock.mockReturnValue("/gast/oversikt");
  });

  it("renders the shared banner with the brand linked home and the demo nav", () => {
    render(
      <GuestShell>
        <p>Innehåll</p>
      </GuestShell>,
    );

    const banner = screen.getByRole("banner");
    expect(
      within(banner).getByRole("link", { name: "Jobbliggaren, startsida" }),
    ).toHaveAttribute("href", "/");
    expect(
      screen.getByRole("navigation", { name: "Demonavigation" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("main")).toHaveTextContent("Innehåll");
  });

  it("marks the active guest nav link via aria-current=page", () => {
    pathnameMock.mockReturnValue("/gast/jobb");
    render(
      <GuestShell>
        <p />
      </GuestShell>,
    );

    const nav = screen.getByRole("navigation", { name: "Demonavigation" });
    expect(within(nav).getByRole("link", { name: "Jobb" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(
      within(nav).getByRole("link", { name: "Översikt" }),
    ).not.toHaveAttribute("aria-current");
  });

  it("shows the log-in and create-account CTAs in the header", () => {
    render(
      <GuestShell>
        <p />
      </GuestShell>,
    );

    expect(screen.getByRole("link", { name: "Logga in" })).toHaveAttribute(
      "href",
      "/logga-in",
    );
    expect(screen.getByRole("link", { name: "Skapa konto" })).toHaveAttribute(
      "href",
      "/registrera",
    );
  });

  it("carries the language control, because guests cannot reach Inställningar", () => {
    // Bites on revert: dropping the mount leaves the guest surfaces with no way to
    // change language at all. Why that matters: language-switcher.tsx's docblock.
    render(
      <GuestShell>
        <p />
      </GuestShell>,
    );

    const banner = screen.getByRole("banner");
    expect(
      within(banner).getByRole("button", { name: /Språk/i }),
    ).toBeInTheDocument();
  });

  it("renders children AND the @modal slot together inside <main>", () => {
    // The (guest) layout passes `{children}{modal}` as the shell's children
    // (ADR 0053 @modal parallel-route slot). Proving both render inside <main>
    // guards that the HeaderStrip refactor did not displace the slot.
    render(
      <GuestShell>
        <p>Sidinnehåll</p>
        <div data-testid="modal-slot">Modalinnehåll</div>
      </GuestShell>,
    );

    const main = screen.getByRole("main");
    expect(main).toHaveTextContent("Sidinnehåll");
    expect(within(main).getByTestId("modal-slot")).toHaveTextContent(
      "Modalinnehåll",
    );
  });
});
