import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import {
  ActivityReportView,
  type ActivityReportRow,
  type MonthOption,
} from "./activity-report-view";

const push = vi.hoisted(() => vi.fn());
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

beforeEach(() => {
  push.mockClear();
  Object.assign(navigator, {
    clipboard: { writeText: vi.fn().mockResolvedValue(undefined) },
  });
});

const monthOptions: MonthOption[] = [
  { value: "2026-05", label: "maj 2026" },
  { value: "2026-04", label: "april 2026" },
];

function row(overrides: Partial<ActivityReportRow> = {}): ActivityReportRow {
  return {
    applicationId: "11111111-1111-1111-1111-111111111111",
    appliedDate: "2026-05-18",
    employer: "Skatteverket",
    title: "Systemutvecklare",
    location: "Stockholm",
    source: "Platsbanken",
    url: "https://example.se/ad/1",
    ...overrides,
  };
}

function renderView(rows: ActivityReportRow[]) {
  return render(
    <ActivityReportView
      rows={rows}
      selectedMonth="2026-05"
      monthLabel="maj 2026"
      monthOptions={monthOptions}
      afUrl="https://arbetsformedlingen.se/example"
    />,
  );
}

describe("ActivityReportView", () => {
  it("renders one card per application and a copy button per non-empty field", () => {
    renderView([row(), row({ applicationId: "22", title: "Testare" })]);
    expect(screen.getAllByRole("listitem")).toHaveLength(2);
    // employer/title/location/appliedAt/howApplied/link = 6 copy buttons per card.
    expect(
      screen.getAllByRole("button", { name: /^Kopiera / }),
    ).toHaveLength(12);
  });

  it("flags fewer than six applications with the discreet minimum line", () => {
    renderView([row()]);
    expect(screen.getByText("1 ansökan i maj 2026.")).toBeInTheDocument();
    expect(
      screen.getByText("Arbetsförmedlingen vill se minst 6."),
    ).toBeInTheDocument();
  });

  it("pluralises the counter for several applications", () => {
    renderView([row(), row({ applicationId: "b" }), row({ applicationId: "c" })]);
    expect(screen.getByText("3 ansökningar i maj 2026.")).toBeInTheDocument();
  });

  it("navigates to the selected month on picker change", () => {
    renderView([row()]);
    fireEvent.change(screen.getByLabelText("Månad"), {
      target: { value: "2026-04" },
    });
    expect(push).toHaveBeenCalledWith(
      "/ansokningar/aktivitetsrapport?month=2026-04",
    );
  });

  it("opens the AF activity report in a new tab via the CTA", () => {
    renderView([row()]);
    const cta = screen.getByRole("link", {
      name: /Öppna Arbetsförmedlingens aktivitetsrapport/,
    });
    expect(cta).toHaveAttribute("href", "https://arbetsformedlingen.se/example");
    expect(cta).toHaveAttribute("target", "_blank");
    expect(cta).toHaveAttribute("rel", expect.stringContaining("noopener"));
  });

  it("renders a neutral placeholder and no copy button for an empty field", () => {
    renderView([row({ location: null, url: null })]);
    expect(screen.getByText("Saknas")).toBeInTheDocument();
    // location empty → only employer/title/appliedAt/howApplied copyable = 4.
    expect(
      screen.getAllByRole("button", { name: /^Kopiera / }),
    ).toHaveLength(4);
  });

  it("defaults 'Hur du sökte' from the source and keeps it editable", () => {
    renderView([row({ source: "LinkedIn" })]);
    const input = screen.getByLabelText("Hur du sökte") as HTMLInputElement;
    expect(input.value).toBe("Via LinkedIn");
    fireEvent.change(input, { target: { value: "Via kontakt" } });
    expect(input.value).toBe("Via kontakt");
  });

  it("shows a calm empty state for a month with no applications (no exclamation)", () => {
    renderView([]);
    expect(screen.queryByRole("listitem")).not.toBeInTheDocument();
    expect(screen.getByText(/Inga ansökningar att rapportera/)).toBeInTheDocument();
    expect(document.body.textContent ?? "").not.toContain("!");
  });
});
