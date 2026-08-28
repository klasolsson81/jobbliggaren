import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { NoticeData } from "./notice-row";
import { NoticeToolbar } from "./notice-toolbar";
import type { SectionNoticeData } from "./notice-section";

// Uppdatera-kontrollen kör `router.refresh()`; utan mock kastar next/navigation utanför
// en App Router-kontext (samma mönster som recent-search-row.test.tsx).
const refreshMock = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: refreshMock }),
}));

const DISMISS_KEY = "jp-oversikt-dismissed-notices";
const PREFS_KEY = "jp-oversikt-notice-prefs";

function notice(overrides: Partial<NoticeData> = {}): SectionNoticeData {
  return {
    id: "n-1",
    source: "jobads",
    type: "matches",
    kind: "info",
    label: "Matchning",
    text: "En notis.",
    cta: "Visa",
    href: "/jobb",
    time: "idag",
    ...overrides,
  };
}

const applicationsNotice: SectionNoticeData = {
  id: "b",
  source: "applications",
  type: "followup",
  kind: "warning",
  label: "Uppföljning",
  text: "En notis.",
  cta: "Visa",
  href: "/ansokningar",
  time: "idag",
};

describe("NoticeToolbar", () => {
  beforeEach(() => {
    window.localStorage.clear();
    refreshMock.mockClear();
  });

  it("renderar 'senast uppdaterad'-stämpeln", () => {
    render(<NoticeToolbar lastUpdated="2026-07-19" notices={[]} />);
    expect(screen.getByText(/senast uppdaterad/)).toBeInTheDocument();
    expect(screen.getByText("2026-07-19")).toBeInTheDocument();
  });

  it("döljer 'Markera alla' när inget avfärdbart är synligt", () => {
    render(<NoticeToolbar lastUpdated="x" notices={[]} />);
    expect(
      screen.queryByRole("button", { name: /Markera alla/ }),
    ).toBeNull();
  });

  it("visar 'Markera alla' och avfärdar alla synliga vid klick", async () => {
    const user = userEvent.setup();
    render(
      <NoticeToolbar
        lastUpdated="x"
        notices={[notice({ id: "a" }), applicationsNotice]}
      />,
    );

    await user.click(
      screen.getByRole("button", { name: /Markera alla som lästa/ }),
    );

    const stored = JSON.parse(
      window.localStorage.getItem(DISMISS_KEY) ?? "[]",
    );
    expect(stored).toContain("a");
    expect(stored).toContain("b");
    // Inget avfärdbart kvar → knappen försvinner.
    expect(
      screen.queryByRole("button", { name: /Markera alla/ }),
    ).toBeNull();
  });

  it("flyttar fokus till första sektionens kugghjul efter 'Markera alla' (WCAG 2.4.3)", async () => {
    const user = userEvent.setup();
    render(
      <>
        <NoticeToolbar lastUpdated="x" notices={[notice({ id: "a" })]} />
        <button type="button" className="jp-section__gear" aria-label="Notisinställningar" />
      </>,
    );
    await user.click(
      screen.getByRole("button", { name: /Markera alla som lästa/ }),
    );
    expect(
      screen.getByRole("button", { name: "Notisinställningar" }),
    ).toHaveFocus();
  });

  it("räknar inte en pref-avstängd typ som synlig", () => {
    window.localStorage.setItem(
      PREFS_KEY,
      JSON.stringify({ "jobads:matches": false }),
    );
    render(<NoticeToolbar lastUpdated="x" notices={[notice({ id: "a" })]} />);
    expect(
      screen.queryByRole("button", { name: /Markera alla/ }),
    ).toBeNull();
  });

  it("räknar inte en icke-avfärdbar notis", () => {
    render(
      <NoticeToolbar
        lastUpdated="x"
        notices={[notice({ id: "a", dismissible: false })]}
      />,
    );
    expect(
      screen.queryByRole("button", { name: /Markera alla/ }),
    ).toBeNull();
  });
  it("visar en uppdatera-kontroll bredvid stämpeln, alltid (#1549)", () => {
    // Villkoret Klas satte: en tidpunkt användaren inte kan påverka ska bort. Kontrollen
    // är därför INTE villkorad på att det finns notiser — den hör till stämpeln.
    render(<NoticeToolbar lastUpdated="2026-07-19 · 08:05" notices={[]} />);
    expect(screen.getByRole("button", { name: /Uppdatera/ })).toBeEnabled();
  });

  it("klick begär en ny render av sidan", async () => {
    // `/oversikt` är force-dynamic, så en refresh ger ett nytt `new Date()` och därmed
    // en ny stämpel — kontrollen behöver ingen egen hämtväg.
    const user = userEvent.setup();
    render(<NoticeToolbar lastUpdated="x" notices={[]} />);

    await user.click(screen.getByRole("button", { name: /Uppdatera/ }));

    expect(refreshMock).toHaveBeenCalledTimes(1);
  });

  it("vänstergruppen bär stämpeln och Uppdatera — men inte Markera alla", () => {
    // Föräldern är space-between med två barn. Assertionen är STRUKTURELL, inte bara
    // närvaro: hamnar Markera alla i vänstergruppen kollapsar radens layout, och ett
    // närvarotest hade inte sett det.
    const { container } = render(
      <NoticeToolbar lastUpdated="2026-07-19 · 08:05" notices={[notice()]} />,
    );
    const left = container.querySelector<HTMLElement>(
      ".jp-oversikt-toolbar__left",
    );
    expect(left).not.toBeNull();

    expect(within(left!).getByRole("button", { name: /Uppdatera/ })).toBeInTheDocument();
    expect(within(left!).getByText(/2026-07-19/)).toBeInTheDocument();
    expect(within(left!).queryByRole("button", { name: /Markera alla/ })).toBeNull();
    expect(screen.getByRole("button", { name: /Markera alla/ })).toBeInTheDocument();
  });

  it("bär kvittot själv efter en uppdatering (#1549)", async () => {
    // Stämpeln har minutupplösning och en refresh går på under en sekund, så två klick
    // i samma minut lämnar den oförändrad. Utan kvitto ser kontrollen verkningslös ut
    // (design-reviewer Major). Kvittot ligger på knappen, som är fokuserad — därför
    // hörs det också av en skärmläsare utan live-region.
    const user = userEvent.setup();
    render(<NoticeToolbar lastUpdated="x" notices={[]} />);

    await user.click(screen.getByRole("button", { name: /Uppdatera/ }));

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Uppdaterad/ })).toBeInTheDocument(),
    );
  });

  it("blir ALDRIG disabled — den är idempotent och får inte tappa fokus", async () => {
    // design-reviewer mätte i Chromium att `disabled` på den just aktiverade knappen
    // kastar fokus till <body> och aldrig ger tillbaka det. Husets form för idempotenta
    // kontroller är att inte disabla alls (company-follow-button.tsx, Klas PR5).
    const user = userEvent.setup();
    render(<NoticeToolbar lastUpdated="x" notices={[]} />);
    const button = screen.getByRole("button", { name: /Uppdatera/ });

    await user.click(button);

    expect(button).not.toBeDisabled();
    expect(document.activeElement).toBe(button);
  });
});
