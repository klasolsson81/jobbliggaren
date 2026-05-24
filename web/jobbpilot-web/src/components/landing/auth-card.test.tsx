import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { AuthCard } from "./auth-card";

vi.mock("@/lib/auth/actions", () => ({
  loginAction: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(),
}));

describe("AuthCard (Steg 5 closed-beta)", () => {
  it("renderar login-rubrik + LoginForm (Lösenord + Logga in-knapp)", () => {
    render(<AuthCard />);
    expect(screen.getByRole("heading", { name: "Logga in" })).toBeInTheDocument();
    expect(screen.getByLabelText("Lösenord")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /Logga in/i }),
    ).toBeInTheDocument();
  });

  it("INGEN Skapa konto-tab eller register-form (closed beta)", () => {
    render(<AuthCard />);
    expect(
      screen.queryByRole("tab", { name: /Skapa konto/i }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/datapolicy/i)).not.toBeInTheDocument();
  });

  it("renderar tre OAuth-knappar (Google/LinkedIn/Microsoft)", () => {
    render(<AuthCard />);
    expect(screen.getByText("Google")).toBeInTheDocument();
    expect(screen.getByText("LinkedIn")).toBeInTheDocument();
    expect(screen.getByText("Microsoft")).toBeInTheDocument();
  });

  it("OAuth-knappar pekar mot /logga-in?provider=<id> (stub)", () => {
    render(<AuthCard />);
    const links = screen
      .getAllByRole("link")
      .filter((l) => /Google|LinkedIn|Microsoft/.test(l.textContent ?? ""));
    expect(links).toHaveLength(3);
    expect(links[0]).toHaveAttribute("href", "/logga-in?provider=google");
    expect(links[1]).toHaveAttribute("href", "/logga-in?provider=linkedin");
    expect(links[2]).toHaveAttribute("href", "/logga-in?provider=microsoft");
  });

  it("har länk till /vantelista för 'Inget konto'-rad", () => {
    render(<AuthCard />);
    const link = screen.getByRole("link", {
      name: /Anmäl dig till väntelistan/i,
    });
    expect(link).toHaveAttribute("href", "/vantelista");
  });

  it("separator-copy är 'eller logga in med'", () => {
    render(<AuthCard />);
    expect(screen.getByText("eller logga in med")).toBeInTheDocument();
  });
});
