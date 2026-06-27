import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import RegistreraPage from "./page";

// #265 — public registration is OPEN. `/registrera` no longer redirects to
// `/vantelista`; it renders the live RegisterForm. RegisterForm reads
// useSearchParams and wires registerAction via useActionState — both stubbed so
// mounting never calls fetch(). A hoisted ref lets a test vary the query string
// to assert next-param deep-link propagation through the mounted form.
const { searchParamsRef } = vi.hoisted(() => ({
  searchParamsRef: { current: new URLSearchParams() },
}));

vi.mock("next/navigation", () => ({
  useSearchParams: () => searchParamsRef.current,
}));

vi.mock("@/lib/auth/actions", () => ({
  registerAction: vi.fn().mockResolvedValue(null),
}));

describe("RegistreraPage (#265 — open public registration)", () => {
  it("renders the live RegisterForm with no redirect to /vantelista", () => {
    searchParamsRef.current = new URLSearchParams();
    render(<RegistreraPage />);

    // Page heading from pages.auth.register.title.
    expect(
      screen.getByRole("heading", { level: 1, name: "Skapa konto" }),
    ).toBeInTheDocument();

    // RegisterForm fields are present (email + password).
    expect(screen.getByLabelText("E-postadress")).toBeInTheDocument();
    expect(screen.getByLabelText("Lösenord")).toBeInTheDocument();

    // Live submit button (distinct from the heading by role).
    expect(
      screen.getByRole("button", { name: "Skapa konto" }),
    ).toBeInTheDocument();

    // Cross-link points to login, not to a waitlist.
    const loginLink = screen.getByRole("link", { name: "Logga in" });
    expect(loginLink).toHaveAttribute("href", "/logga-in");
  });

  it("propagates the next deep-link param into the mounted form", () => {
    searchParamsRef.current = new URLSearchParams("next=/cv");
    const { container } = render(<RegistreraPage />);

    const nextField = container.querySelector('input[name="next"]');
    expect(nextField).not.toBeNull();
    expect(nextField).toHaveValue("/cv");
  });
});
