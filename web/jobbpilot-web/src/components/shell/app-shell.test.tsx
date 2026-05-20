import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AppShell } from "./app-shell";

// usePathname styr aria-current på nav-länkarna — mockas per route.
const pathnameMock = vi.fn<() => string>();
vi.mock("next/navigation", () => ({
  usePathname: () => pathnameMock(),
}));

// Server-action mockas per repo-mönster (jfr status-edit-card.test) —
// logoutAction anropas inte i dessa tester men måste vara importbar.
vi.mock("@/lib/auth/actions", () => ({
  logoutAction: vi.fn(),
}));

describe("AppShell (v3 header-shell)", () => {
  beforeEach(() => {
    pathnameMock.mockReset();
    pathnameMock.mockReturnValue("/jobb");
  });

  it("renderar header-nav utan sidebar", () => {
    render(
      <AppShell email="klas.olsson@example.se" isAdmin={false}>
        <p>Innehåll</p>
      </AppShell>,
    );

    expect(
      screen.getByRole("navigation", { name: "Huvudnavigation" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("complementary", { name: "Sidonavigation" }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole("main")).toHaveTextContent("Innehåll");
  });

  it("markerar aktiv nav-länk via aria-current=page", () => {
    pathnameMock.mockReturnValue("/ansokningar/123");
    render(
      <AppShell email="k@example.se" isAdmin={false}>
        <p />
      </AppShell>,
    );

    const nav = screen.getByRole("navigation", { name: "Huvudnavigation" });
    expect(
      within(nav).getByRole("link", { name: "Mina ansökningar" }),
    ).toHaveAttribute("aria-current", "page");
    expect(within(nav).getByRole("link", { name: "Jobb" })).not.toHaveAttribute(
      "aria-current",
    );
  });

  it("öppnar användarmenyn vid klick på avatar (initialer från e-post)", async () => {
    const user = userEvent.setup();
    render(
      <AppShell email="klas.olsson@example.se" isAdmin={false}>
        <p />
      </AppShell>,
    );

    const trigger = screen.getByRole("button", { name: "Användarmeny" });
    expect(trigger).toHaveTextContent("KO");
    expect(trigger).toHaveAttribute("aria-expanded", "false");

    await user.click(trigger);

    expect(trigger).toHaveAttribute("aria-expanded", "true");
    const menu = screen.getByRole("group", { name: "Användarmeny" });
    expect(within(menu).getByText("klas.olsson@example.se")).toBeInTheDocument();
    expect(
      within(menu).getByRole("link", { name: /Inställningar/ }),
    ).toHaveAttribute("href", "/installningar");
    expect(
      within(menu).getByRole("button", { name: /Logga ut/ }),
    ).toBeInTheDocument();
  });

  it("döljer Granskning för icke-admin men visar den för admin", async () => {
    const user = userEvent.setup();
    const { unmount } = render(
      <AppShell email="k@example.se" isAdmin={false}>
        <p />
      </AppShell>,
    );

    await user.click(screen.getByRole("button", { name: "Användarmeny" }));
    expect(
      screen.queryByRole("link", { name: /Granskning/ }),
    ).not.toBeInTheDocument();
    unmount();

    render(
      <AppShell email="k@example.se" isAdmin>
        <p />
      </AppShell>,
    );
    await user.click(screen.getByRole("button", { name: "Användarmeny" }));
    expect(
      screen.getByRole("link", { name: /Granskning/ }),
    ).toHaveAttribute("href", "/admin/granskning");
  });

  it("öppnar mobil-drawern med samma länkar och stänger via Stäng-knappen", async () => {
    const user = userEvent.setup();
    render(
      <AppShell email="k@example.se" isAdmin={false}>
        <p />
      </AppShell>,
    );

    const burger = screen.getByRole("button", { name: "Öppna meny" });
    await user.click(burger);

    const drawer = screen.getByRole("dialog", { name: "Meny" });
    expect(
      within(drawer).getByRole("link", { name: /Jobb/ }),
    ).toHaveAttribute("href", "/jobb");
    expect(
      within(drawer).getByRole("link", { name: /Inställningar/ }),
    ).toHaveAttribute("href", "/installningar");

    await user.click(
      within(drawer).getByRole("button", { name: "Stäng meny" }),
    );
    expect(screen.queryByRole("dialog", { name: "Meny" })).not.toBeInTheDocument();
  });
});
