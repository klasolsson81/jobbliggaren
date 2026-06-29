import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { ApplicationStats } from "./application-stats";
import type { ApplicationStatsDto } from "@/lib/dto/application-stats";

// The test harness aliases @testing-library/react to inject NextIntlClientProvider
// with the real Swedish catalog, so the `statistik` + `applications` namespaces
// resolve without a manual provider.

function makeStats(overrides: Partial<ApplicationStatsDto> = {}): ApplicationStatsDto {
  return {
    totalApplications: 5,
    totalSent: 5,
    statusCounts: [
      { status: "Draft", count: 0 },
      { status: "Submitted", count: 1 },
      { status: "Acknowledged", count: 1 },
      { status: "InterviewScheduled", count: 1 },
      { status: "Interviewing", count: 0 },
      { status: "OfferReceived", count: 0 },
      { status: "Accepted", count: 1 },
      { status: "Rejected", count: 1 },
      { status: "Withdrawn", count: 0 },
      { status: "Ghosted", count: 0 },
    ],
    responseRate: { numerator: 3, denominator: 5, percent: 60 },
    interviewRate: { numerator: 2, denominator: 5, percent: 40 },
    rejectionRate: { numerator: 1, denominator: 5, percent: 20 },
    funnel: [
      { stage: "Sent", count: 5, percentOfSent: 100 },
      { stage: "Responded", count: 3, percentOfSent: 60 },
      { stage: "Interview", count: 2, percentOfSent: 40 },
      { stage: "Offer", count: 1, percentOfSent: 20 },
      { stage: "Accepted", count: 1, percentOfSent: 20 },
    ],
    offFunnelExitCount: 1,
    monthlyApplications: Array.from({ length: 12 }, (_, i) => ({
      year: 2025,
      month: ((6 + i) % 12) + 1,
      count: i === 11 ? 5 : 0,
    })),
    ...overrides,
  };
}

describe("ApplicationStats", () => {
  it("renders the empty state when there are no applications", () => {
    render(<ApplicationStats data={makeStats({ totalApplications: 0, totalSent: 0 })} />);

    // The empty state shows the create/search CTAs and no stats tables.
    expect(screen.getByRole("link", { name: /Skapa första ansökan/i })).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("shows the summary with both denominators (total vs sent)", () => {
    render(<ApplicationStats data={makeStats({ totalApplications: 7, totalSent: 5 })} />);

    // "Du har 7 ansökningar totalt, varav 5 skickade."
    expect(screen.getByText(/7 ansökningar totalt, varav 5 skickade/)).toBeInTheDocument();
  });

  it("renders each rate with its base (numerator of denominator)", () => {
    render(<ApplicationStats data={makeStats()} />);

    // Response rate 3 of 5 — the base is always shown next to the percentage (§5).
    expect(screen.getByText("3 av 5")).toBeInTheDocument();
    expect(screen.getByText("1 av 5")).toBeInTheDocument(); // rejection
  });

  it("renders all five funnel stages", () => {
    render(<ApplicationStats data={makeStats()} />);

    // Some stage labels (Skickad/Erbjudande/Accepterad) intentionally match a
    // status label too, so scope the query to the funnel section.
    const funnelSection = screen
      .getByRole("heading", { name: "Från skickad till accepterad" })
      .closest("section")!;
    for (const label of ["Skickad", "Svar", "Intervju", "Erbjudande", "Accepterad"]) {
      expect(within(funnelSection).getByText(label)).toBeInTheDocument();
    }
  });

  it("shows the off-funnel limitation note when there are off-funnel exits", () => {
    render(<ApplicationStats data={makeStats({ offFunnelExitCount: 2 })} />);

    expect(screen.getByText(/utanför processen/)).toBeInTheDocument();
  });

  it("hides the limitation note when there are no off-funnel exits", () => {
    render(<ApplicationStats data={makeStats({ offFunnelExitCount: 0 })} />);

    expect(screen.queryByText(/utanför processen/)).not.toBeInTheDocument();
  });

  it("renders twelve month rows in the monthly table", () => {
    render(<ApplicationStats data={makeStats()} />);

    // The monthly table is the last table; assert the series has 12 data rows.
    const tables = screen.getAllByRole("table");
    const monthlyTable = tables[tables.length - 1]!;
    const bodyRows = monthlyTable.querySelectorAll("tbody tr");
    expect(bodyRows.length).toBe(12);
  });

  it("shows the rates-empty message when nothing has been sent", () => {
    render(
      <ApplicationStats
        data={makeStats({
          totalApplications: 3,
          totalSent: 0,
          responseRate: { numerator: 0, denominator: 0, percent: 0 },
          interviewRate: { numerator: 0, denominator: 0, percent: 0 },
          rejectionRate: { numerator: 0, denominator: 0, percent: 0 },
          funnel: [
            { stage: "Sent", count: 0, percentOfSent: 0 },
            { stage: "Responded", count: 0, percentOfSent: 0 },
            { stage: "Interview", count: 0, percentOfSent: 0 },
            { stage: "Offer", count: 0, percentOfSent: 0 },
            { stage: "Accepted", count: 0, percentOfSent: 0 },
          ],
          offFunnelExitCount: 0,
        })}
      />,
    );

    // No sent applications → outcome rates show a neutral message, funnel hidden.
    expect(screen.getByText(/inga skickade ansökningar ännu/i)).toBeInTheDocument();
    // "Svar" is funnel-only (no status label uses it) → its absence proves the
    // funnel section is not rendered. (Erbjudande/Skickad would be ambiguous —
    // the status breakdown still lists them.)
    expect(screen.queryByText("Svar")).not.toBeInTheDocument();
  });
});
