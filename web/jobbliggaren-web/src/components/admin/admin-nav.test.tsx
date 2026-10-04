import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { AdminNav } from "./admin-nav";

// usePathname drives aria-current on the nav links — mocked per route.
const pathnameMock = vi.fn<() => string>();
vi.mock("next/navigation", () => ({
  usePathname: () => pathnameMock(),
}));

const ITEMS = [
  ["Översikt", "/admin"],
  ["Användare", "/admin/anvandare"],
  ["Feedback", "/admin/feedback"],
  ["Loggar", "/admin/loggar"],
  ["E-postleverans", "/admin/e-post"],
  ["Bakgrundsjobb", "/admin/jobb"],
  ["Granskning", "/admin/granskning"],
] as const;

function navLinks() {
  const nav = screen.getByRole("navigation", { name: "Admin-navigation" });
  return within(nav).getAllByRole("link");
}

describe("AdminNav", () => {
  beforeEach(() => {
    pathnameMock.mockReset();
    pathnameMock.mockReturnValue("/admin/granskning");
  });

  it("renders the seven admin pages in the designed order inside a labelled navigation landmark", () => {
    render(<AdminNav />);

    expect(navLinks().map((link) => [link.textContent, link.getAttribute("href")])).toEqual(
      ITEMS.map(([label, href]) => [label, href]),
    );
  });

  it("marks only the current page with aria-current=page", () => {
    pathnameMock.mockReturnValue("/admin/jobb");
    render(<AdminNav />);

    const current = navLinks().filter((link) => link.getAttribute("aria-current") === "page");
    expect(current.map((link) => link.textContent)).toEqual(["Bakgrundsjobb"]);
  });

  it("marks the overview only on the bare /admin, never on the pages it prefixes", () => {
    pathnameMock.mockReturnValue("/admin/anvandare");
    render(<AdminNav />);

    expect(screen.getByRole("link", { name: "Översikt" })).not.toHaveAttribute("aria-current");
    expect(screen.getByRole("link", { name: "Användare" })).toHaveAttribute("aria-current", "page");
  });

  it("marks the overview on /admin", () => {
    pathnameMock.mockReturnValue("/admin");
    render(<AdminNav />);

    const current = navLinks().filter((link) => link.getAttribute("aria-current") === "page");
    expect(current.map((link) => link.textContent)).toEqual(["Översikt"]);
  });

  it("treats a nested route as active via the path-prefix match", () => {
    pathnameMock.mockReturnValue("/admin/loggar/applikationsfel");
    render(<AdminNav />);

    const current = navLinks().filter((link) => link.getAttribute("aria-current") === "page");
    expect(current.map((link) => link.textContent)).toEqual(["Loggar"]);
  });
});
