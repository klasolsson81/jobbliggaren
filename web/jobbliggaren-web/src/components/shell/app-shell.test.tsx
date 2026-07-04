import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AppShell } from "./app-shell";
import type { LandingStatsDto } from "@/lib/dto/landing";

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

  it("öppnar användarmenyn vid klick på avatar (initialer från e-post)", async () => {
    const user = userEvent.setup();
    render(
      <AppShell email="klas.olsson@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
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
      <AppShell email="k@example.se" isAdmin={false} initialStats={STATS_FIXTURE}>
        <p />
      </AppShell>,
    );

    await user.click(screen.getByRole("button", { name: "Användarmeny" }));
    expect(
      screen.queryByRole("link", { name: /Granskning/ }),
    ).not.toBeInTheDocument();
    unmount();

    render(
      <AppShell email="k@example.se" isAdmin initialStats={STATS_FIXTURE}>
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
      within(drawer).getByRole("link", { name: /Inställningar/ }),
    ).toHaveAttribute("href", "/installningar");

    await user.click(
      within(drawer).getByRole("button", { name: "Stäng meny" }),
    );
    expect(screen.queryByRole("dialog", { name: "Meny" })).not.toBeInTheDocument();
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
});
