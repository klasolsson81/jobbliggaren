import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";

vi.mock("@/lib/auth/actions", () => ({ logoutAction: vi.fn() }));

import { MinaSidorNav, type MinaSidorSection } from "./mina-sidor-nav";

const SECTIONS: ReadonlyArray<[MinaSidorSection, string, string]> = [
  ["matchning", "Matchning", "/mina-sidor"],
  ["notiser", "Notiser", "/mina-sidor/notiser"],
  ["konto", "Konto", "/mina-sidor/konto"],
  ["sekretess", "Sekretess och data", "/mina-sidor/sekretess"],
];

describe("MinaSidorNav", () => {
  it("links every section, in Klas's order, each to a real URL", () => {
    render(<MinaSidorNav active="matchning" />);
    const nav = screen.getByRole("navigation", { name: "Mina sidor" });
    const links = within(nav).getAllByRole("link");
    expect(links.map((link) => [link.textContent, link.getAttribute("href")])).toEqual(
      SECTIONS.map(([, label, href]) => [label, href]),
    );
  });

  it.each(SECTIONS)("marks %s as the current page and no other", (section, label) => {
    render(<MinaSidorNav active={section} />);
    const nav = screen.getByRole("navigation", { name: "Mina sidor" });
    const current = within(nav)
      .getAllByRole("link")
      .filter((link) => link.getAttribute("aria-current") === "page");
    expect(current.map((link) => link.textContent)).toEqual([label]);
  });

  it("puts Logga ut under the menu as a form button, outside the navigation landmark", () => {
    render(<MinaSidorNav active="konto" />);
    const button = screen.getByRole("button", { name: "Logga ut" });
    expect(button).toHaveAttribute("type", "submit");
    expect(button.closest("form")).not.toBeNull();
    expect(button.closest("nav")).toBeNull();
  });
});
