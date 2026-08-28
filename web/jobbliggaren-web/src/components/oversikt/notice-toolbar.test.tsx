import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen } from "@testing-library/react";
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

  it("uppdatera-kontrollen och 'Markera alla' samexisterar", () => {
    // Föräldern är space-between med två barn; vänstergruppen får inte svälja knappen
    // till höger eller tvärtom.
    render(<NoticeToolbar lastUpdated="x" notices={[notice()]} />);
    expect(screen.getByRole("button", { name: /Uppdatera/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Markera alla/ })).toBeInTheDocument();
  });
});
