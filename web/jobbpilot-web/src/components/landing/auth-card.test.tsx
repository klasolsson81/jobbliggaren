import { describe, it, expect, vi } from "vitest";
import { useState } from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { AuthCard, type AuthMode } from "./auth-card";

vi.mock("@/lib/auth/actions", () => ({
  loginAction: vi.fn(),
  registerAction: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(),
}));

function Harness({ initial }: { initial: AuthMode }) {
  const [mode, setMode] = useState<AuthMode>(initial);
  return <AuthCard mode={mode} onModeChange={setMode} />;
}

describe("AuthCard (F6 Prompt 1)", () => {
  it("renderar två tabs (Logga in + Skapa konto)", () => {
    render(<Harness initial="login" />);
    const tabs = screen.getAllByRole("tab");
    expect(tabs).toHaveLength(2);
    expect(tabs[0]).toHaveTextContent("Logga in");
    expect(tabs[1]).toHaveTextContent("Skapa konto");
  });

  it("default mode=login visar LoginForm (Lösenord + Logga in-knapp)", () => {
    render(<Harness initial="login" />);
    expect(screen.getByLabelText("Lösenord")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /Logga in/i }),
    ).toBeInTheDocument();
    // Inga register-only fält
    expect(screen.queryByText(/datapolicy/i)).not.toBeInTheDocument();
  });

  it("klick på Skapa konto-tab flippar till RegisterForm", () => {
    render(<Harness initial="login" />);
    fireEvent.click(screen.getByRole("tab", { name: "Skapa konto" }));
    expect(
      screen.getByRole("button", { name: /Skapa konto/i }),
    ).toBeInTheDocument();
    expect(screen.getByText(/datapolicy/i)).toBeInTheDocument();
  });

  it("renderar tre OAuth-knappar (Google/LinkedIn/Microsoft)", () => {
    render(<Harness initial="login" />);
    expect(screen.getByText("Google")).toBeInTheDocument();
    expect(screen.getByText("LinkedIn")).toBeInTheDocument();
    expect(screen.getByText("Microsoft")).toBeInTheDocument();
  });

  it("OAuth-knappar pekar mot /logga-in?provider=<id> (stub)", () => {
    render(<Harness initial="login" />);
    const links = screen
      .getAllByRole("link")
      .filter((l) => /Google|LinkedIn|Microsoft/.test(l.textContent ?? ""));
    expect(links).toHaveLength(3);
    expect(links[0]).toHaveAttribute("href", "/logga-in?provider=google");
    expect(links[1]).toHaveAttribute("href", "/logga-in?provider=linkedin");
    expect(links[2]).toHaveAttribute("href", "/logga-in?provider=microsoft");
  });

  it("separator-copy är 'eller logga in med' i login-mode", () => {
    render(<Harness initial="login" />);
    expect(screen.getByText("eller logga in med")).toBeInTheDocument();
  });

  it("separator-copy är 'eller fortsätt med' i register-mode", () => {
    render(<Harness initial="register" />);
    expect(screen.getByText("eller fortsätt med")).toBeInTheDocument();
  });
});
