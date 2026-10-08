import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AppShell } from "./app-shell";
import type { LandingStatsDto } from "@/lib/dto/landing";
import { LOGOUT_PATH } from "@/lib/auth/login-paths";

// usePathname styr aria-current på nav-länkarna — mockas per route.
const pathnameMock = vi.fn<() => string>();
vi.mock("next/navigation", () => ({
  usePathname: () => pathnameMock(),
}));

// HeaderStats kör polling-setInterval i useEffect — mockas till en trivial
// markör (ingen nätverk/timer) så app-shell-testerna kan bevisa att den är
// MONTERAD i headern utan att trigga polling. Polling-/delta-logiken testas
// isolerat i header-stats.test.tsx.
vi.mock("@/components/shell/header-stats", () => ({
  HeaderStats: () => <div data-testid="header-stats" />,
}));

const STATS_FIXTURE: LandingStatsDto = {
  activeCount: 45_580,
  newToday: 312,
  isStale: false,
  refreshedAt: "2026-05-24T03:00:00+00:00",
};

describe("AppShell (v3 header-shell)", () => {
  beforeEach(() => {
    pathnameMock.mockReset();
    pathnameMock.mockReturnValue("/jobb");
  });

  it("renderar header-nav utan sidebar", () => {
    render(
      <AppShell email="klas.olsson@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
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

  it("#582 — header-nav har en Företag-snabblänk till /foretag", () => {
    render(
      <AppShell email="k@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
        <p />
      </AppShell>,
    );
    const nav = screen.getByRole("navigation", { name: "Huvudnavigation" });
    expect(
      within(nav).getByRole("link", { name: "Företag" }),
    ).toHaveAttribute("href", "/foretag");
  });

  it("markerar aktiv nav-länk via aria-current=page", () => {
    pathnameMock.mockReturnValue("/ansokningar/123");
    render(
      <AppShell email="k@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
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

  it("öppnar Inställningar från en neutral ikonknapp: inga initialer, huvudet säger vem som är inloggad", async () => {
    const user = userEvent.setup();
    render(
      <AppShell email="klas.olsson@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
        <p />
      </AppShell>,
    );

    // ADR 0142 "Page form", "Inställningar" (design M6): an icon, named by the shared label, and no text
    // of its own, so no initials of the address.
    const trigger = screen.getByRole("button", { name: "Inställningar" });
    expect(trigger).toHaveClass("jp-icon-btn");
    expect(trigger.textContent).toBe("");
    expect(trigger).toHaveAttribute("aria-haspopup", "dialog");
    expect(trigger).toHaveAttribute("aria-expanded", "false");

    await user.click(trigger);

    expect(trigger).toHaveAttribute("aria-expanded", "true");
    const menu = screen.getByRole("dialog", { name: "Inställningar" });
    const head = document.getElementById(menu.getAttribute("aria-describedby") ?? "");
    expect(head).not.toBeNull();
    expect(within(head!).getByText("Inloggad som")).toBeInTheDocument();
    expect(within(head!).getByText("klas.olsson@example.se")).toBeInTheDocument();
    // The address's local part is no longer shown as if it were a name.
    expect(within(menu).queryByText("klas.olsson")).not.toBeInTheDocument();
    expect(
      within(menu).getByRole("link", { name: "Inställningar" }),
    ).toHaveAttribute("href", "/mina-sidor");
    const logout = within(menu).getByRole("button", { name: /Logga ut/ }).closest("form");
    expect(logout).toHaveAttribute("action", LOGOUT_PATH);
    expect(logout).toHaveAttribute("method", "post");
  });

  it("only administrators can switch links while the header and page stay mounted", async () => {
    const user = userEvent.setup();
    const { unmount } = render(<AppShell email="member@example.test" isAdmin={false} initialStats={STATS_FIXTURE}><p>Content</p></AppShell>);
    expect(screen.queryByRole("button", { name: "Adminmeny" })).toBeNull();
    unmount();
    render(<AppShell email="admin@example.test" isAdmin initialStats={STATS_FIXTURE}><p>Content</p></AppShell>);
    const main = screen.getByRole("main");
    const stats = screen.getByTestId("header-stats");
    const account = screen.getByRole("button", { name: "Inställningar" });
    const toggle = screen.getByRole("button", { name: "Adminmeny" });
    expect(toggle).toHaveAttribute("aria-pressed", "false");
    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-pressed", "true");
    const nav = screen.getByRole("navigation", { name: "Admin-navigation" });
    expect(within(nav).getAllByRole("link")).toHaveLength(7);
    expect(within(nav).getByRole("link", { name: "Granskning" })).toHaveAttribute("href", "/admin/granskning");
    expect(screen.queryByRole("navigation", { name: "Huvudnavigation" })).toBeNull();
    expect(screen.getByRole("main")).toBe(main);
    expect(screen.getByTestId("header-stats")).toBe(stats);
    expect(screen.getByRole("button", { name: "Inställningar" })).toBe(account);
    await user.click(account);
    expect(within(screen.getByRole("dialog", { name: "Inställningar" })).queryByRole("link", { name: "Granskning" })).toBeNull();
    await user.click(account);
    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByRole("navigation", { name: "Huvudnavigation" })).toBeInTheDocument();
  });

  it("starts with admin links on an admin deep link and switches the mobile links too", async () => {
    pathnameMock.mockReturnValue("/admin/granskning");
    const user = userEvent.setup();
    render(<AppShell email="admin@example.test" isAdmin initialStats={STATS_FIXTURE}><p /></AppShell>);
    const toggle = screen.getByRole("button", { name: "Adminmeny" });
    expect(toggle).toHaveAttribute("aria-pressed", "true");
    const nav = screen.getByRole("navigation", { name: "Admin-navigation" });
    expect(within(nav).getByRole("link", { name: "Granskning" })).toHaveAttribute("aria-current", "page");
    await user.click(screen.getByRole("button", { name: "Öppna meny" }));
    const drawer = screen.getByRole("dialog", { name: "Meny" });
    expect(within(drawer).getByRole("link", { name: "Användare" })).toHaveAttribute("href", "/admin/anvandare");
    await user.keyboard("{Escape}");
    toggle.focus();
    await user.keyboard(" ");
    expect(toggle).toHaveFocus();
    await user.click(screen.getByRole("button", { name: "Öppna meny" }));
    expect(within(screen.getByRole("dialog", { name: "Meny" })).getByRole("link", { name: "Jobb" })).toHaveAttribute("href", "/jobb");
  });

  it("öppnar mobil-drawern med samma länkar och stänger via Stäng-knappen", async () => {
    const user = userEvent.setup();
    render(
      <AppShell email="k@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
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
      within(drawer).getByRole("link", { name: "Inställningar" }),
    ).toHaveAttribute("href", "/mina-sidor");

    await user.click(
      within(drawer).getByRole("button", { name: "Stäng meny" }),
    );
    expect(screen.queryByRole("dialog", { name: "Meny" })).not.toBeInTheDocument();
  });

  // #1440 follow-up: a modified click opens the destination elsewhere and
  // leaves the user here, so the surface must not be dismissed. The predicate
  // and its matrix live in lib/nav/modified-click.ts -- these pin the
  // BINDINGS, one pair per surface.
  it("ctrl-klick i användarmenyn lämnar menyn öppen", async () => {
    const user = userEvent.setup();
    render(
      <AppShell email="k@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
        <p />
      </AppShell>,
    );

    await user.click(screen.getByRole("button", { name: "Inställningar" }));
    const menu = screen.getByRole("dialog", { name: "Inställningar" });
    const link = within(menu).getByRole("link", { name: "Inställningar" });

    fireEvent.click(link, { ctrlKey: true });

    expect(
      screen.getByRole("dialog", { name: "Inställningar" }),
    ).toBeInTheDocument();
  });

  it("vanlig klick i användarmenyn stänger den", async () => {
    const user = userEvent.setup();
    render(
      <AppShell email="k@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
        <p />
      </AppShell>,
    );

    await user.click(screen.getByRole("button", { name: "Inställningar" }));
    const menu = screen.getByRole("dialog", { name: "Inställningar" });

    fireEvent.click(within(menu).getByRole("link", { name: "Inställningar" }));

    expect(
      screen.queryByRole("dialog", { name: "Inställningar" }),
    ).not.toBeInTheDocument();
  });

  it("ctrl-klick i mobil-drawern lämnar den öppen OCH låter fokus vara", async () => {
    const user = userEvent.setup();
    render(
      <AppShell email="k@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
        <p />
      </AppShell>,
    );

    const burger = screen.getByRole("button", { name: "Öppna meny" });
    await user.click(burger);
    const drawer = screen.getByRole("dialog", { name: "Meny" });
    const link = within(drawer).getByRole("link", { name: "Inställningar" });

    fireEvent.click(link, { ctrlKey: true });

    // Focus first: if this regresses, the dialog assertion below throws and the
    // focus line never runs. Ordering it first makes the crossing reach it.
    // handleNav pulls focus to the hamburger -- damage the other two lack.
    expect(burger).not.toHaveFocus();
    expect(screen.getByRole("dialog", { name: "Meny" })).toBeInTheDocument();
  });

  it("vanlig klick i mobil-drawern stänger den", async () => {
    const user = userEvent.setup();
    render(
      <AppShell email="k@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
        <p />
      </AppShell>,
    );

    await user.click(screen.getByRole("button", { name: "Öppna meny" }));
    const drawer = screen.getByRole("dialog", { name: "Meny" });

    fireEvent.click(within(drawer).getByRole("link", { name: "Inställningar" }));

    expect(screen.queryByRole("dialog", { name: "Meny" })).not.toBeInTheDocument();
  });

  it("markerar Inställningar som aktuell sida i drawern när man står på /mina-sidor", async () => {
    pathnameMock.mockReturnValue("/mina-sidor");
    const user = userEvent.setup();
    render(
      <AppShell email="k@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
        <p />
      </AppShell>,
    );

    await user.click(screen.getByRole("button", { name: "Öppna meny" }));
    const drawer = screen.getByRole("dialog", { name: "Meny" });

    expect(
      within(drawer).getByRole("link", { name: "Inställningar" }),
    ).toHaveAttribute("aria-current", "page");
  });

  it("monterar HeaderStats i den delade HeaderStrip-headern", () => {
    // LP-5b #259: efter HeaderStrip-refaktorn ska HeaderStats fortfarande sitta
    // i headern (server-fed initialStats → klient-polling, ADR 0064). Polling-
    // beteendet testas isolerat i header-stats.test.tsx; här bevisas montering.
    render(
      <AppShell email="k@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
        <p />
      </AppShell>,
    );

    const banner = screen.getByRole("banner");
    expect(within(banner).getByTestId("header-stats")).toBeInTheDocument();
  });

  it("renderar children OCH @modal-slot tillsammans i <main>", () => {
    // (app)/layout passerar `{children}{modal}` som shellens children (ADR 0053
    // @modal parallel-route-slot). Bevisar att HeaderStrip-refaktorn inte
    // förflyttade slotten — båda renderas i content-arean.
    render(
      <AppShell email="k@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
        <p>Sidinnehåll</p>
        <div data-testid="modal-slot">Modalinnehåll</div>
      </AppShell>,
    );

    const main = screen.getByRole("main");
    expect(main).toHaveTextContent("Sidinnehåll");
    expect(within(main).getByTestId("modal-slot")).toHaveTextContent(
      "Modalinnehåll",
    );
  });

  it("#1852 — en URL-ändring monterar inte om sidan i <main>", () => {
    // An ad opened from /sparade moves the URL to /jobb/<id> while the page behind the modal stays.
    pathnameMock.mockReturnValue("/sparade");
    const { rerender } = render(
      <AppShell email="k@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
        <button type="button" data-testid="row">Annons</button>
      </AppShell>,
    );
    const row = screen.getByTestId("row");
    row.focus();

    pathnameMock.mockReturnValue("/jobb/1");
    rerender(
      <AppShell email="k@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
        <button type="button" data-testid="row">Annons</button>
      </AppShell>,
    );

    expect(screen.getByTestId("row")).toBe(row);
    expect(row).toHaveFocus();
  });
});
