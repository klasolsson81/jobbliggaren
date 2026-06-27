import { describe, it, expect } from "vitest";
import { createFormatter } from "next-intl";
import { render, screen } from "@testing-library/react";
import { RecurringJobsTable } from "./recurring-jobs-table";
import type { RecurringJobStatusDto } from "@/lib/dto/admin";

// Real next-intl formatter, Europe/Stockholm — same object getFormatter()
// returns, keeping the date assertions timezone-stable in CI.
const format = createFormatter({ locale: "sv", timeZone: "Europe/Stockholm" });

function job(
  overrides: Partial<RecurringJobStatusDto> = {},
): RecurringJobStatusDto {
  return {
    id: "nightly-match-scan",
    cron: "0 3 * * *",
    lastExecution: "2026-05-11T08:32:15.000Z",
    lastJobState: "Succeeded",
    nextExecution: "2026-05-12T01:00:00.000Z",
    ...overrides,
  };
}

describe("RecurringJobsTable", () => {
  it("renders the empty state when there are no jobs", () => {
    render(<RecurringJobsTable jobs={[]} format={format} />);
    expect(screen.getByText("Inga schemalagda jobb")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("renders one row per job", () => {
    render(
      <RecurringJobsTable
        jobs={[job(), job({ id: "cleanup-expired" })]}
        format={format}
      />,
    );
    // 1 header row + 2 data rows
    expect(screen.getAllByRole("row")).toHaveLength(3);
  });

  it("renders the job id and cron schedule verbatim", () => {
    render(<RecurringJobsTable jobs={[job()]} format={format} />);
    expect(screen.getByText("nightly-match-scan")).toBeInTheDocument();
    expect(screen.getByText("0 3 * * *")).toBeInTheDocument();
  });

  it("formats lastExecution as YYYY-MM-DD HH:mm in Europe/Stockholm", () => {
    render(<RecurringJobsTable jobs={[job()]} format={format} />);
    // 08:32Z in May = 10:32 local (CEST, UTC+2)
    expect(screen.getByText("2026-05-11 10:32")).toBeInTheDocument();
  });

  it("shows 'Aldrig körd' when lastExecution is null", () => {
    render(
      <RecurringJobsTable
        jobs={[job({ lastExecution: null, lastJobState: null })]}
        format={format}
      />,
    );
    // Both the cell text and the badge resolve to the same label; assert ≥1.
    expect(screen.getAllByText("Aldrig körd").length).toBeGreaterThanOrEqual(1);
  });

  it("shows 'Inget schema' when cron is null", () => {
    render(
      <RecurringJobsTable jobs={[job({ cron: null })]} format={format} />,
    );
    expect(screen.getByText("Inget schema")).toBeInTheDocument();
  });

  it("renders the localized status badge for the last job state", () => {
    render(<RecurringJobsTable jobs={[job({ lastJobState: "Failed" })]} format={format} />);
    expect(screen.getByText("Misslyckades")).toBeInTheDocument();
  });

  it("renders the expected column headers", () => {
    render(<RecurringJobsTable jobs={[job()]} format={format} />);
    const labels = screen
      .getAllByRole("columnheader")
      .map((h) => h.textContent);
    expect(labels).toEqual([
      "Jobb-id",
      "Schema",
      "Senaste körning",
      "Status",
      "Nästa körning",
    ]);
  });

  it("exposes an aria-label on the table for screen readers", () => {
    render(<RecurringJobsTable jobs={[job()]} format={format} />);
    expect(
      screen.getByRole("table", { name: "Schemalagda jobb" }),
    ).toBeInTheDocument();
  });
});
