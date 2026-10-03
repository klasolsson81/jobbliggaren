import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import svMessages from "../../../messages/sv";
import { LOGOUT_PATH } from "@/lib/auth/login-paths";
import AdminLayout from "./layout";

vi.mock("next/navigation", () => ({
  redirect: (path: string) => {
    throw new Error(`REDIRECT:${path}`);
  },
  usePathname: () => "/admin",
}));

vi.mock("next-intl/server", () => ({
  getLocale: async () => "sv",
  getMessages: async () => svMessages,
  getTranslations: async () => (key: string) => key,
}));

// A server component reading the request catalog; this layout's own provider carries a narrower set.
vi.mock("@/components/site/site-footer", () => ({ SiteFooter: () => null }));

vi.mock("@/lib/auth/session", () => ({
  ROLES: { Admin: "Admin" },
  getServerSession: async () => ({ userId: "u-1", email: "admin@example.se", roles: ["Admin"] }),
}));

describe("(admin)/layout — Logga ut (#1956)", () => {
  it("posts natively to the logout route, as the other two logout forms do", async () => {
    render(await AdminLayout({ children: null }));

    const form = screen.getByRole("button", { name: "nav.logout" }).closest("form");
    expect(form).toHaveAttribute("action", LOGOUT_PATH);
    expect(form).toHaveAttribute("method", "post");
  });
});
