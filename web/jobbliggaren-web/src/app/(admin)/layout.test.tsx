import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import svMessages from "../../../messages/sv";
import { LOGOUT_PATH } from "@/lib/auth/login-paths";
import { ADMIN_RETURN_HEADER } from "@/lib/auth/admin-return";
import AdminLayout from "./layout";

vi.mock("next/navigation", () => ({
  redirect: (path: string) => {
    throw new Error(`REDIRECT:${path}`);
  },
  usePathname: () => "/admin",
}));

// What the proxy wrote, or what a client forged when the proxy did not run.
const requestHeaders = vi.hoisted(() => ({ current: {} as Record<string, string> }));
vi.mock("next/headers", () => ({ headers: async () => new Headers(requestHeaders.current) }));

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
  requestHeaders.current = {};
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

describe("(admin)/layout — the return path through login (#1979)", () => {
  const ID = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

  it("sends a visitor without a session back to the submission the notice mail named", async () => {
    session.current = null;
    requestHeaders.current = { [ADMIN_RETURN_HEADER]: `/admin/feedback?id=${ID}` };

    await expect(AdminLayout({ children: null })).rejects.toThrow(
      `REDIRECT:/logga-in?next=${encodeURIComponent(`/admin/feedback?id=${ID}`)}`
    );
  });

  it("keeps an admin pathname without a query", async () => {
    session.current = null;
    requestHeaders.current = { [ADMIN_RETURN_HEADER]: "/admin/anvandare" };

    await expect(AdminLayout({ children: null })).rejects.toThrow(
      /^REDIRECT:\/logga-in\?next=%2Fadmin%2Fanvandare$/
    );
  });

  it.each([
    ["another origin", "https://evil.example/admin"],
    ["a protocol-relative path", "//evil.example/admin"],
    ["a path outside /admin", "/oversikt"],
    ["a query the proxy never writes", `/admin/feedback?id=${ID}&status=ny`],
    ["an id that is not a GUID", "/admin/feedback?id=namn.efternamn%40example.test"],
    ["an id on another admin page", `/admin/anvandare?id=${ID}`],
    ["a backslash", "/admin\\evil.example"],
  ])("gives the plain login page for a forged header carrying %s", async (_label, value) => {
    session.current = null;
    requestHeaders.current = { [ADMIN_RETURN_HEADER]: value };

    await expect(AdminLayout({ children: null })).rejects.toThrow(/^REDIRECT:\/logga-in$/);
  });
});
