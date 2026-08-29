import { describe, it, expect, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { NoticeData } from "./notice-row";
import { MarkAllReadRow } from "./mark-all-read-row";
import type { SectionNoticeData } from "./notice-section";

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

describe("MarkAllReadRow", () => {
  beforeEach(() => window.localStorage.clear());

  // ── Flyttade från notice-toolbar.test.tsx (#1557) ────────────────────────
  // Kontrollen bytte hem, inte beteende. Assertionerna följer med i stället för att
  // stå kvar mot en komponent som inte längre renderar den: kvar hade de gått gröna
  // utan att kunna falla av det skäl de finns.

  it("döljer 'Markera alla' när inget avfärdbart är synligt", () => {
    render(<MarkAllReadRow notices={[]} />);
    expect(screen.queryByRole("button", { name: /Markera alla/ })).toBeNull();
  });

  it("visar 'Markera alla' och avfärdar alla synliga vid klick", async () => {
    const user = userEvent.setup();
    render(<MarkAllReadRow notices={[notice({ id: "a" }), applicationsNotice]} />);

    await user.click(
      screen.getByRole("button", { name: /Markera alla som lästa/ }),
    );

    const stored = JSON.parse(window.localStorage.getItem(DISMISS_KEY) ?? "[]");
    expect(stored).toContain("a");
    expect(stored).toContain("b");
    // Inget avfärdbart kvar → knappen försvinner.
    expect(screen.queryByRole("button", { name: /Markera alla/ })).toBeNull();
  });

  it("räknar inte en pref-avstängd typ som synlig", () => {
    window.localStorage.setItem(
      PREFS_KEY,
      JSON.stringify({ "jobads:matches": false }),
    );
    render(<MarkAllReadRow notices={[notice({ id: "a" })]} />);
    expect(screen.queryByRole("button", { name: /Markera alla/ })).toBeNull();
  });

  it("räknar inte en icke-avfärdbar notis", () => {
    render(<MarkAllReadRow notices={[notice({ id: "a", dismissible: false })]} />);
    expect(screen.queryByRole("button", { name: /Markera alla/ })).toBeNull();
  });

  // ── Nytt i #1557 ─────────────────────────────────────────────────────────

  it("flyttar fokus till SISTA läst-växeln, inte den första (WCAG 2.4.3)", async () => {
    // Kontrollen avmonterar sig själv, så utan förflyttning faller fokus till <body>.
    // Målet är sista `.jp-notice-foot__toggle` och inte kugghjulet som förr: härifrån,
    // sist på sidan, hade kugghjulet blivit ett hopp till dokumentets topp. Två växlar
    // renderas därför, för ett test mot bara en kan inte skilja "sista" från "första".
    const user = userEvent.setup();
    render(
      <>
        <button type="button" className="jp-notice-foot__toggle">
          Visa först
        </button>
        <button type="button" className="jp-notice-foot__toggle">
          Visa sist
        </button>
        <MarkAllReadRow notices={[notice({ id: "a" })]} />
      </>,
    );

    await user.click(
      screen.getByRole("button", { name: /Markera alla som lästa/ }),
    );

    expect(screen.getByRole("button", { name: "Visa sist" })).toHaveFocus();
  });

  it("behåller raden och kvitto-regionen efter klicket, även när knappen är borta", () => {
    // #1556:s lärdom, ärvd: en live-region som monteras samtidigt som sitt innehåll
    // annonseras opålitligt. Raden är därför villkorad på SYNLIGA notiser, inte på
    // avfärdbara — annars hade hela regionen försvunnit i samma render som kvittot
    // skulle ha lästs upp.
    const dismissed = notice({ id: "a" });
    window.localStorage.setItem(DISMISS_KEY, JSON.stringify(["a"]));
    const { container } = render(<MarkAllReadRow notices={[dismissed]} />);

    expect(screen.queryByRole("button", { name: /Markera alla/ })).toBeNull();
    expect(container.querySelector(".jp-notice-bulk")).not.toBeNull();
    expect(screen.getByRole("status")).toBeInTheDocument();
  });

  it("renderar ingenting när ingen notis är synlig", () => {
    // Ingen notis ⇒ inget kvitto att bära och inget att avfärda. Raden ska då inte
    // lämna en tom 24px-marginal efter sig.
    const { container } = render(<MarkAllReadRow notices={[]} />);
    expect(container.querySelector(".jp-notice-bulk")).toBeNull();
  });

  it("kopplar hinten till knappen med aria-describedby (DESIGN.md §9)", () => {
    // En visuellt intilliggande hjälptext som inte är programmatiskt kopplad finns
    // inte för en skärmläsare — och hinten är hela skälet att våga klicka.
    const { container } = render(<MarkAllReadRow notices={[notice({ id: "a" })]} />);
    const button = screen.getByRole("button", { name: /Markera alla som lästa/ });
    const hintId = button.getAttribute("aria-describedby");

    expect(hintId).not.toBeNull();
    const hint = container.querySelector(`#${hintId}`);
    expect(hint).not.toBeNull();
    expect(hint).toHaveTextContent(/till i morgon/);
    expect(hint).toHaveTextContent(/Ingenting tas bort/);
  });

  it("namnger antalet i live-regionen efter klicket (WCAG 4.1.3)", async () => {
    // Fokus landar på "Visa", vars eget namn inte nämner avfärdandet, så utan kvitto
    // säger ingenting att något hände. Antalet, inte bara att det hände: två notiser
    // markerade som lästa är ett annat utfall än en.
    const user = userEvent.setup();
    render(
      <>
        <button type="button" className="jp-notice-foot__toggle">
          Visa
        </button>
        <MarkAllReadRow notices={[notice({ id: "a" }), applicationsNotice]} />
      </>,
    );

    await user.click(
      screen.getByRole("button", { name: /Markera alla som lästa/ }),
    );

    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent(
        "2 notiser markerade som lästa",
      ),
    );
  });

  it("bär knapp, hint och kvitto i EN rad, i den ordningen", () => {
    // Strukturell, inte närvaro: hinten hör under knappen den beskriver, och kvittot
    // sist. Ett närvarotest hade inte sett att de hamnat i skilda containrar.
    const { container } = render(<MarkAllReadRow notices={[notice({ id: "a" })]} />);
    const row = container.querySelector<HTMLElement>(".jp-notice-bulk");
    expect(row).not.toBeNull();

    expect(
      within(row!).getByRole("button", { name: /Markera alla som lästa/ }),
    ).toBeInTheDocument();
    expect(within(row!).getByRole("status")).toBeInTheDocument();
    expect(row!.querySelector(".jp-notice-bulk__hint")).not.toBeNull();

    const kids = [...row!.children].map((c) => c.className);
    expect(kids[0]).toContain("jp-btn");
    expect(kids[1]).toContain("jp-notice-bulk__hint");
    expect(kids[2]).toContain("jp-notice-bulk__receipt");
  });
});
