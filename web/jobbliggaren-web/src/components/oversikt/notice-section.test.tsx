import { describe, it, expect, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NoticeSection, type SectionNoticeData } from "./notice-section";

function n(
  id: string,
  type: string,
  overrides: Partial<SectionNoticeData> = {},
): SectionNoticeData {
  return {
    id,
    source: "jobads",
    type,
    kind: "info",
    label: type.toUpperCase(),
    text: `Notis ${id}`,
    cta: "Visa",
    href: "/jobb",
    time: "i dag",
    ...overrides,
  };
}

const prefTypes = [
  { id: "deadlines", label: "Deadlines för sparade annonser" },
  { id: "matches", label: "Nya matchande annonser" },
  { id: "latestsearch", label: "Senaste sökningen" },
];

function renderSection(notices: SectionNoticeData[]) {
  return render(
    <NoticeSection
      source="jobads"
      titleId="s-jobads"
      title="Jobbannonser"
      notices={notices}
      emptyBody="Nya matchningar och deadlines dyker upp här."
      prefTypes={prefTypes}
    />,
  );
}

describe("NoticeSection", () => {
  beforeEach(() => window.localStorage.clear());

  it("visar olästa-räknaren och renderar olästa rader", () => {
    renderSection([
      n("a", "matches"),
      n("b", "deadlines", { kind: "warning" }),
    ]);
    expect(screen.getByText("2 olästa")).toBeInTheDocument();
    expect(screen.getByText("Notis a")).toBeInTheDocument();
    expect(screen.getByText("Notis b")).toBeInTheDocument();
  });

  it("åtgärdsnotiser (warning) sorteras före info", () => {
    renderSection([
      n("info1", "matches"),
      n("warn1", "deadlines", { kind: "warning" }),
    ]);
    const rows = screen.getAllByRole("listitem");
    expect(within(rows[0]!).getByText("Notis warn1")).toBeInTheDocument();
  });

  it("tomt-läge när inga notiser finns — sektionen döljs aldrig", () => {
    renderSection([]);
    expect(screen.getByText("inga olästa")).toBeInTheDocument();
    expect(screen.getByText("Inget nytt just nu")).toBeInTheDocument();
    expect(
      screen.getByText("Nya matchningar och deadlines dyker upp här."),
    ).toBeInTheDocument();
  });

  it("dismiss flyttar notisen till läst-läge; foten dyker upp och räknaren nollas", async () => {
    const user = userEvent.setup();
    renderSection([n("a", "matches")]);
    await user.click(screen.getByRole("button", { name: "Markera som läst" }));
    expect(screen.queryByText("Notis a")).toBeNull();
    expect(screen.getByText("1 läst notis")).toBeInTheDocument();
    expect(screen.getByText("inga olästa")).toBeInTheDocument();
  });

  it("Visa/Dölj togglar lästa rader; RotateCcw återställer notisen", async () => {
    const user = userEvent.setup();
    renderSection([n("a", "matches")]);
    await user.click(screen.getByRole("button", { name: "Markera som läst" }));
    // Lästa rader dolda som default.
    expect(screen.queryByText("Notis a")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Visa" }));
    expect(screen.getByText("Notis a")).toBeInTheDocument();
    // Återställ (av-markera).
    await user.click(screen.getByRole("button", { name: "Återställ notis" }));
    expect(screen.getByText("1 oläst")).toBeInTheDocument();
    expect(screen.queryByText("1 läst notis")).toBeNull();
  });

  it("kugghjul öppnar popovern; avbockad typ filtrerar bort raderna och räknas inte", async () => {
    const user = userEvent.setup();
    renderSection([n("a", "matches")]);

    const gear = screen.getByRole("button", { name: "Notisinställningar" });
    expect(gear).toHaveAttribute("aria-expanded", "false");
    await user.click(gear);
    expect(gear).toHaveAttribute("aria-expanded", "true");

    const checkbox = screen.getByRole("checkbox", {
      name: "Nya matchande annonser",
    });
    expect(checkbox).toBeChecked();
    await user.click(checkbox);

    expect(screen.queryByText("Notis a")).toBeNull();
    expect(screen.getByText("inga olästa")).toBeInTheDocument();
  });
});
