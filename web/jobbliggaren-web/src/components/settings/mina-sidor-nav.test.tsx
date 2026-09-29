import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { Bell, Shield, Target, UserRound, type LucideIcon } from "lucide-react";

vi.mock("@/lib/auth/actions", () => ({ logoutAction: vi.fn() }));

import { MinaSidorNav, type MinaSidorSection } from "./mina-sidor-nav";

const SECTIONS: ReadonlyArray<[MinaSidorSection, string, string]> = [
  ["matchning", "Matchning", "/mina-sidor"],
  ["notiser", "Notiser", "/mina-sidor/notiser"],
  ["konto", "Konto", "/mina-sidor/konto"],
  ["sekretess", "Sekretess och data", "/mina-sidor/sekretess"],
];

// Klas 2026-09-28 (#1916): Target, Bell, a person and Shield; the person is UserRound, the
// header's profile glyph.
const GLYPHS: ReadonlyArray<[string, LucideIcon]> = [
  ["Matchning", Target],
  ["Notiser", Bell],
  ["Konto", UserRound],
  ["Sekretess och data", Shield],
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

  it.each(GLYPHS)("gives %s its own glyph, hidden from assistive technology", (label, Glyph) => {
    render(<MinaSidorNav active="matchning" />);
    const icons = Array.from(screen.getByRole("link", { name: label }).querySelectorAll("svg"));
    expect(icons).toHaveLength(1);
    const [icon] = icons;
    expect(icon).toHaveAttribute("aria-hidden", "true");
    const expected = render(<Glyph />).container.querySelector("svg");
    expect(icon?.innerHTML).toBe(expected?.innerHTML);
  });

  it("puts Logga ut under the menu as a form button, outside the navigation landmark", () => {
    render(<MinaSidorNav active="konto" />);
    const button = screen.getByRole("button", { name: "Logga ut" });
    expect(button).toHaveAttribute("type", "submit");
    expect(button.closest("form")).not.toBeNull();
    expect(button.closest("nav")).toBeNull();
  });
});
