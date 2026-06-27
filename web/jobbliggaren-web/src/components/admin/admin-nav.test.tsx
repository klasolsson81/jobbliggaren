import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { AdminNav } from "./admin-nav";

// usePathname drives aria-current on the nav links — mocked per route.
const pathnameMock = vi.fn<() => string>();
vi.mock("next/navigation", () => ({
  usePathname: () => pathnameMock(),
}));

describe("AdminNav", () => {
  beforeEach(() => {
    pathnameMock.mockReset();
    pathnameMock.mockReturnValue("/admin/granskning");
  });

  it("renders both admin nav links inside a labelled navigation landmark", () => {
    render(<AdminNav />);

    const nav = screen.getByRole("navigation", { name: "Admin-navigation" });
    expect(
      within(nav).getByRole("link", { name: "Granskning" }),
    ).toHaveAttribute("href", "/admin/granskning");
    expect(within(nav).getByRole("link", { name: "Jobb" })).toHaveAttribute(
      "href",
      "/admin/jobb",
    );
  });

  it("marks the active link via aria-current=page and leaves the inactive one unset", () => {
    pathnameMock.mockReturnValue("/admin/jobb");
    render(<AdminNav />);

    const nav = screen.getByRole("navigation", { name: "Admin-navigation" });
    expect(within(nav).getByRole("link", { name: "Jobb" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    expect(
      within(nav).getByRole("link", { name: "Granskning" }),
    ).not.toHaveAttribute("aria-current");
  });

  it("treats a nested route as active via the path-prefix match", () => {
    pathnameMock.mockReturnValue("/admin/granskning/123");
    render(<AdminNav />);

    const nav = screen.getByRole("navigation", { name: "Admin-navigation" });
    expect(
      within(nav).getByRole("link", { name: "Granskning" }),
    ).toHaveAttribute("aria-current", "page");
    expect(within(nav).getByRole("link", { name: "Jobb" })).not.toHaveAttribute(
      "aria-current",
    );
  });
});
