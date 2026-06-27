import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { HeaderStrip } from "./header-strip";

describe("HeaderStrip (shared logged-in shell chrome, LP-5b #259)", () => {
  it("renders a single banner with the brand linked home and an accessible name", () => {
    render(
      <HeaderStrip brandHref="/oversikt" brandLabel="Jobbliggaren, startsida">
        <nav aria-label="Testnav">child</nav>
      </HeaderStrip>,
    );

    const banner = screen.getByRole("banner");
    const brand = within(banner).getByRole("link", {
      name: "Jobbliggaren, startsida",
    });
    expect(brand).toHaveAttribute("href", "/oversikt");
  });

  it("composes the surface-specific content in as children", () => {
    render(
      <HeaderStrip brandHref="/" brandLabel="Jobbliggaren, startsida">
        <button type="button">Surface action</button>
      </HeaderStrip>,
    );

    expect(
      screen.getByRole("button", { name: "Surface action" }),
    ).toBeInTheDocument();
  });

  it("consumes the shared .jp-header strip contract (parity with app/guest)", () => {
    const { container } = render(
      <HeaderStrip brandHref="/" brandLabel="Jobbliggaren, startsida">
        <span>child</span>
      </HeaderStrip>,
    );
    expect(container.querySelector("header.jp-header")).not.toBeNull();
    expect(container.querySelector("div.jp-header__inner")).not.toBeNull();
  });
});
