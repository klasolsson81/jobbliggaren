import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { SiteFooter } from "./site-footer";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: vi.fn(), push: vi.fn(), prefetch: vi.fn() }),
  usePathname: () => "/",
  useSearchParams: () => new URLSearchParams(),
}));
vi.mock("@/i18n/set-locale-action", () => ({ setLocaleAction: vi.fn().mockResolvedValue(undefined) }));

// The footer's links are next/link. jsdom never prefetches, so the prop itself is what is pinned: this stub writes the
// prefetch value it was given onto the anchor.
vi.mock("next/link", () => ({
  default: ({ href, prefetch, children, ...rest }: { href: string; prefetch?: boolean | null; children: ReactNode }) => (
    <a href={href} data-prefetch={prefetch === undefined ? "default" : String(prefetch)} {...rest}>
      {children}
    </a>
  ),
}));

describe("SiteFooter prefetch (ADR 0176 F10, F15)", () => {
  it("never prefetches the catalogue, which is read per request on every page the footer is on", () => {
    render(<SiteFooter />);

    expect(screen.getByRole("link", { name: "Lediga jobb" })).toHaveAttribute("data-prefetch", "false");
  });

  it("leaves every other footer link on Next's default", () => {
    render(<SiteFooter />);

    for (const link of screen.getAllByRole("link").filter((l) => l.getAttribute("href") !== "/lediga-jobb")) {
      expect(link).toHaveAttribute("data-prefetch", "default");
    }
  });
});
