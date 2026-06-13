import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { GuestWelcomeModal } from "./guest-welcome-modal";

// Server Action mock — jsdom har ingen riktig server, så vi mockar set-cookien.
vi.mock("@/lib/guest/guest-mode-actions", () => ({
  markGuestWelcomeSeen: vi.fn(async () => {}),
}));

// useRouter().refresh() — undvik next/navigation-routing-fel i jsdom.
vi.mock("next/navigation", async () => {
  const actual =
    await vi.importActual<typeof import("next/navigation")>("next/navigation");
  return {
    ...actual,
    useRouter: () => ({ refresh: vi.fn(), push: vi.fn(), replace: vi.fn() }),
  };
});

describe("<GuestWelcomeModal />", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renderar inte när showWelcome=false (cookie redan satt)", () => {
    render(<GuestWelcomeModal showWelcome={false} />);
    expect(
      screen.queryByText(/välkommen till demoläget/i)
    ).not.toBeInTheDocument();
  });

  it("renderar TLDR-innehåll och en primary Börja utforska-knapp när showWelcome=true", () => {
    render(<GuestWelcomeModal showWelcome={true} />);
    expect(screen.getByText("Välkommen till demoläget")).toBeInTheDocument();
    expect(
      screen.getByText(/Det här kan du göra som gäst/i)
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Det här kräver ett konto/i)
    ).toBeInTheDocument();
    // En primary CTA per DESIGN.md §6 + WCAG 2.4.6 (design-reviewer B2).
    expect(
      screen.getByRole("button", { name: /börja utforska/i })
    ).toBeInTheDocument();
  });

  it("stänger via Börja utforska-knappen och anropar Server Action", async () => {
    const user = userEvent.setup();
    const { markGuestWelcomeSeen } = await import(
      "@/lib/guest/guest-mode-actions"
    );

    render(<GuestWelcomeModal showWelcome={true} />);
    await user.click(
      screen.getByRole("button", { name: /börja utforska/i })
    );

    expect(markGuestWelcomeSeen).toHaveBeenCalledTimes(1);
  });

  it("modalen innehåller inga emoji eller utropstecken (civic-utility)", () => {
    const { container } = render(<GuestWelcomeModal showWelcome={true} />);
    const text = container.textContent ?? "";
    expect(text).not.toMatch(/!/);
    expect(text).not.toMatch(
      /[\u{1F300}-\u{1FAFF}\u{2600}-\u{27BF}]/u
    );
  });
});
