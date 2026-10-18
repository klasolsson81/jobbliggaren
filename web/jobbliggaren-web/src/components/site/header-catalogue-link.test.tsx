import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { HeaderCatalogueLink } from "./header-catalogue-link";

const nav = vi.hoisted(() => ({ pathname: "/" }));
vi.mock("next/navigation", () => ({ usePathname: () => nav.pathname }));

// jsdom never prefetches, so the prop itself is what is pinned: this stub writes the value it was given.
vi.mock("next/link", () => ({
  default: ({ href, prefetch, children, ...rest }: { href: string; prefetch?: boolean | null; children: ReactNode }) => (
    <a href={href} data-prefetch={String(prefetch)} {...rest}>
      {children}
    </a>
  ),
}));

describe("HeaderCatalogueLink (ADR 0176 F14, stage-A constraint 2)", () => {
  it("links the catalogue without prefetch", () => {
    nav.pathname = "/";
    render(<HeaderCatalogueLink>Lediga jobb</HeaderCatalogueLink>);

    const link = screen.getByRole("link", { name: "Lediga jobb" });
    expect(link).toHaveAttribute("href", "/lediga-jobb");
    expect(link).toHaveAttribute("data-prefetch", "false");
    expect(link).toHaveClass("jp-nav__link");
  });

  it("is the current page on exactly /lediga-jobb", () => {
    nav.pathname = "/lediga-jobb";
    render(<HeaderCatalogueLink>Lediga jobb</HeaderCatalogueLink>);

    expect(screen.getByRole("link", { name: "Lediga jobb" })).toHaveAttribute("aria-current", "page");
  });

  it.each(["/", "/lediga-jobb/stockholm", "/lediga-jobbx", "/jobb/0b0b0b0b-1111-4222-8333-444444444444", "/om"])(
    "is not the current page on %s: never a prefix match",
    (pathname) => {
      nav.pathname = pathname;
      render(<HeaderCatalogueLink>Lediga jobb</HeaderCatalogueLink>);

      expect(screen.getByRole("link", { name: "Lediga jobb" })).not.toHaveAttribute("aria-current");
    }
  );
});
