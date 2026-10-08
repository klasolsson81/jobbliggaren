import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
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

vi.mock("@/lib/api/landing", () => ({ fetchLandingStats: async () => null }));

type Session = { userId: string; email: string; roles: string[] } | null;
const ADMIN_SESSION: Session = { userId: "u-1", email: "admin@example.se", roles: ["Admin"] };
const session = vi.hoisted(() => ({ current: null as Session }));

vi.mock("@/lib/auth/session", () => ({
  ROLES: { Admin: "Admin" },
  getServerSession: async () => session.current,
}));

afterEach(() => {
  session.current = ADMIN_SESSION;
});
session.current = ADMIN_SESSION;

describe("(admin)/layout — Logga ut (#1956)", () => {
  it("posts natively to the logout route, as the other two logout forms do", async () => {
    render(await AdminLayout({ children: null }));

    fireEvent.click(screen.getByRole("button", { name: "Inställningar" }));
    const form = screen.getByRole("button", { name: "Logga ut" }).closest("form");
    expect(form).toHaveAttribute("action", LOGOUT_PATH);
    expect(form).toHaveAttribute("method", "post");
  });
});

describe("(admin)/layout — the gate", () => {
  it("sends a visitor without a session to the login page", async () => {
    session.current = null;

    await expect(AdminLayout({ children: null })).rejects.toThrow(/^REDIRECT:\/logga-in$/);
  });

  it("sends a signed-in account without the Admin role to the start page", async () => {
    // An ordinary account carries no role at all: only Admin is ever seeded.
    session.current = { userId: "u-2", email: "medlem@example.test", roles: [] };

    await expect(AdminLayout({ children: null })).rejects.toThrow(/^REDIRECT:\/$/);
  });
});
