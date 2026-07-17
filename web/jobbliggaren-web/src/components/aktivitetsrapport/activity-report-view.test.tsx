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
    adRemoved: false,
    ...overrides,
  };
}

// #892 (CTO R1): en raderad annons rad visar den bevarade snapshot-identiteten
// och MÅSTE bära borttagen-markören — utan dödssignal ser raden levande ut.
describe("removed-ad marker (#892)", () => {
  it("renderar markören när adRemoved är true", () => {
    render(
      <ActivityReportView
        rows={[row({ adRemoved: true })]}
        selectedMonth="2026-05"
        monthLabel="maj 2026"
        monthOptions={monthOptions}
        afUrl="https://arbetsformedlingen.se"
      />,
    );
    expect(screen.getByText("Annonsen är borttagen")).toBeInTheDocument();
  });

  it("renderar INGEN markör för en levande annons", () => {
    render(
      <ActivityReportView
        rows={[row()]}
        selectedMonth="2026-05"
        monthLabel="maj 2026"
        monthOptions={monthOptions}
        afUrl="https://arbetsformedlingen.se"
      />,
    );
    expect(screen.queryByText("Annonsen är borttagen")).toBeNull();
  });
});

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
    expect(push).toHaveBeenCalledWith("/aktivitetsrapport?month=2026-04");
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

  it("renders the advert as a new-tab link and still offers a copy button", () => {
    renderView([row({ url: "https://example.se/ad/9" })]);
    const link = screen.getByRole("link", { name: "Öppna annonsen i ny flik" });
    expect(link).toHaveAttribute("href", "https://example.se/ad/9");
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", expect.stringContaining("noopener"));
    expect(
      screen.getByRole("button", { name: "Kopiera Länk till annons" }),
    ).toBeInTheDocument();
  });

  it("filters by employer or title once the list is long enough", () => {
    const rows = Array.from({ length: 6 }, (_, i) =>
      row({
        applicationId: `id-${i}`,
        employer: i === 0 ? "Skatteverket" : `Bolag ${i}`,
        title: i === 0 ? "Systemutvecklare" : `Roll ${i}`,
      }),
    );
    renderView(rows);
    expect(screen.getAllByRole("listitem")).toHaveLength(6);

    const filter = screen.getByLabelText("Filtrera på arbetsgivare eller titel");
    fireEvent.change(filter, { target: { value: "skatteverket" } });
    expect(screen.getAllByRole("listitem")).toHaveLength(1);

    fireEvent.change(filter, { target: { value: "finns-inte" } });
    expect(screen.queryByRole("listitem")).not.toBeInTheDocument();
    expect(screen.getByText("Inga ansökningar matchar filtret.")).toBeInTheDocument();
  });

  it("hides the filter for short lists", () => {
    renderView([row(), row({ applicationId: "b" })]);
    expect(
      screen.queryByLabelText("Filtrera på arbetsgivare eller titel"),
    ).not.toBeInTheDocument();
  });
});
