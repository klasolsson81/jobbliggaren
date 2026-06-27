import { describe, it, expect } from "vitest";
import { createFormatter } from "next-intl";
import { render, screen } from "@testing-library/react";
import { FailedJobsTable } from "./failed-jobs-table";
import type {
  FailedJobsResponse,
  FailedJobStatusDto,
} from "@/lib/dto/admin";

const format = createFormatter({ locale: "sv", timeZone: "Europe/Stockholm" });

function item(overrides: Partial<FailedJobStatusDto> = {}): FailedJobStatusDto {
  return {
    jobId: "job-12345",
    jobType: "NightlyMatchScanJob",
    failedAt: "2026-05-11T08:32:15.000Z",
    errorCategory: "DbUpdateException",
    ...overrides,
  };
}

function response(
  overrides: Partial<FailedJobsResponse> = {},
): FailedJobsResponse {
  return {
    totalCount: 1,
    returned: 1,
    items: [item()],
    ...overrides,
  };
}

describe("FailedJobsTable", () => {
  it("renders a calm civic empty state when totalCount is 0 (no exclamation)", () => {
    render(
      <FailedJobsTable
        data={{ totalCount: 0, returned: 0, items: [] }}
        format={format}
      />,
    );
    expect(screen.getByText("Inga misslyckade jobb")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    // Civic copy rule: no exclamation marks.
    expect(document.body.textContent ?? "").not.toContain("!");
  });

  it("renders one row per failed job", () => {
    render(
      <FailedJobsTable
        data={response({
          totalCount: 2,
          returned: 2,
          items: [item(), item({ jobId: "job-99999" })],
        })}
        format={format}
      />,
    );
    expect(screen.getAllByRole("row")).toHaveLength(3); // 1 header + 2 data
  });

  it("shows the PII-free errorCategory (exception type name)", () => {
    render(<FailedJobsTable data={response()} format={format} />);
    expect(screen.getByText("DbUpdateException")).toBeInTheDocument();
  });

  it("renders only the four allowed fields — no raw/extra job data leaks", () => {
    // A backend regression that smuggled a message or stack trace into an extra
    // property must never reach the DOM. The component reads only the four DTO
    // fields; assert nothing resembling a message/stack/argument is rendered.
    const leakyItem = {
      ...item(),
      // These keys are NOT on the DTO type; cast through unknown to simulate a
      // backend that erroneously returns more than the contract allows.
      errorMessage: "Sensitive: connection string=Host=db;Password=hunter2",
      stackTrace: "at Foo.Bar() in /src/Foo.cs:line 42",
      arguments: ["personnummer 990101-1234"],
    } as unknown as FailedJobStatusDto;

    render(
      <FailedJobsTable
        data={{ totalCount: 1, returned: 1, items: [leakyItem] }}
        format={format}
      />,
    );

    const text = document.body.textContent ?? "";
    expect(text).not.toContain("Password=hunter2");
    expect(text).not.toContain("stackTrace");
    expect(text).not.toContain("/src/Foo.cs");
    expect(text).not.toContain("990101-1234");
    // The allowed category label is still shown.
    expect(screen.getByText("DbUpdateException")).toBeInTheDocument();
  });

  it("renders exactly four column headers (no detail/expand column)", () => {
    render(<FailedJobsTable data={response()} format={format} />);
    const labels = screen
      .getAllByRole("columnheader")
      .map((h) => h.textContent);
    expect(labels).toEqual([
      "Jobb-id",
      "Jobbtyp",
      "Tidpunkt",
      "Felkategori",
    ]);
  });

  it("surfaces truncation honestly when totalCount exceeds returned", () => {
    render(
      <FailedJobsTable
        data={response({ totalCount: 130, returned: 50 })}
        format={format}
      />,
    );
    expect(
      screen.getByText("Visar de senaste 50 av 130 misslyckade jobben."),
    ).toBeInTheDocument();
  });

  it("shows a plain count note when nothing is truncated", () => {
    render(
      <FailedJobsTable
        data={response({ totalCount: 1, returned: 1 })}
        format={format}
      />,
    );
    expect(screen.getByText("1 misslyckat jobb totalt.")).toBeInTheDocument();
  });

  it("formats failedAt as YYYY-MM-DD HH:mm in Europe/Stockholm", () => {
    render(<FailedJobsTable data={response()} format={format} />);
    expect(screen.getByText("2026-05-11 10:32")).toBeInTheDocument();
  });

  it("exposes an aria-label on the table for screen readers", () => {
    render(<FailedJobsTable data={response()} format={format} />);
    expect(
      screen.getByRole("table", { name: "Misslyckade jobb" }),
    ).toBeInTheDocument();
  });
});
