import { StrictMode } from "react";
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

  // -----------------------------------------------------------------
  // WCAG 2.1 SC 3.2.2 On Input (level A), technique H84. Changing the
  // picker's VALUE must not navigate; navigating is its own act.
  // -----------------------------------------------------------------
  describe("month picker", () => {
    // Three options, because the resync case below needs a month that is neither
    // the one on screen nor the uncommitted draft.
    const pickerOptions: MonthOption[] = [
      { value: "2026-05", label: "maj 2026" },
      { value: "2026-04", label: "april 2026" },
      { value: "2026-03", label: "mars 2026" },
    ];

    // The label follows the month rather than being hardcoded. Both the pending
    // line and the announcement interpolate `monthLabel`, so a fixed label would
    // let them assert "maj 2026" for a March report and read as correct.
    function labelFor(month: string): string {
      const option = pickerOptions.find((o) => o.value === month);
      if (!option) throw new Error(`no fixture label for ${month}`);
      return option.label;
    }

    function pickerView(selectedMonth: string) {
      return (
        <ActivityReportView
          rows={[row()]}
          selectedMonth={selectedMonth}
          monthLabel={labelFor(selectedMonth)}
          monthOptions={pickerOptions}
          afUrl="https://arbetsformedlingen.se/example"
        />
      );
    }

    function renderPicker(selectedMonth: string) {
      return render(pickerView(selectedMonth));
    }

    it("does not navigate while the value is being changed", () => {
      renderPicker("2026-05");
      const picker = screen.getByLabelText("Månad");

      // The measured defect, reproduced: a CLOSED <select> on Windows/Chrome
      // commits on every arrow key. The picker lists the last twelve months, so
      // arrowing from the newest option to the oldest is ELEVEN steps, and it
      // fired eleven router.push calls and eleven fetches, one per keystroke.
      // Eleven change events go in here for that reason — the number in this
      // comment is the number the loop runs — and the count that matters is zero.
      // Each step is keydown THEN change, which is what a real arrow key emits.
      // Firing only `change` left the Enter guard unpinned: deleting
      // `if (event.key !== "Enter") return;` survived all six of these tests,
      // because none of them pressed a key that was not Enter — and that mutant
      // reintroduces the defect almost exactly, one push per keystroke from the
      // second arrow onward. Measured by code-reviewer, not predicted.
      const stepped = Array.from({ length: 11 }, (_, i) =>
        i % 2 === 0 ? "2026-04" : "2026-05",
      );
      for (const value of stepped) {
        fireEvent.keyDown(picker, { key: "ArrowDown" });
        fireEvent.change(picker, { target: { value } });
      }

      expect(push).not.toHaveBeenCalled();
      // The control still tracks what the user typed; only the navigation waits.
      expect(picker).toHaveValue("2026-04");
    });

    it("navigates on Enter", () => {
      renderPicker("2026-05");
      const picker = screen.getByLabelText("Månad");

      fireEvent.change(picker, { target: { value: "2026-04" } });
      fireEvent.keyDown(picker, { key: "Enter" });

      expect(push).toHaveBeenCalledTimes(1);
      expect(push).toHaveBeenCalledWith("/aktivitetsrapport?month=2026-04");
    });

    it("navigates when focus leaves the field", () => {
      renderPicker("2026-05");
      const picker = screen.getByLabelText("Månad");

      fireEvent.change(picker, { target: { value: "2026-04" } });
      fireEvent.blur(picker);

      expect(push).toHaveBeenCalledTimes(1);
      expect(push).toHaveBeenCalledWith("/aktivitetsrapport?month=2026-04");
    });

    it("does not navigate when focus leaves a value that ended up unchanged", () => {
      renderPicker("2026-05");
      const picker = screen.getByLabelText("Månad");

      // Tabbing through the card, and arrowing away and back again, are both
      // ordinary. Without the equality guard each would refetch the month that
      // is already on screen.
      fireEvent.blur(picker);
      fireEvent.change(picker, { target: { value: "2026-04" } });
      fireEvent.change(picker, { target: { value: "2026-05" } });
      fireEvent.blur(picker);
      fireEvent.keyDown(picker, { key: "Enter" });

      expect(push).not.toHaveBeenCalled();
    });

    it("follows the month the server resolved, so the control cannot disagree with the report below it", () => {
      // Under StrictMode, because the render-phase state adjustment is the part
      // most able to misbehave under a double render and the app runs with it on
      // (Next's default, not overridden in next.config.ts). The adjustment is
      // idempotent — it assigns values rather than deriving from the previous
      // state — so the second render computes the same thing and queues the same
      // update.
      const { rerender } = render(
        <StrictMode>{pickerView("2026-05")}</StrictMode>,
      );
      const picker = screen.getByLabelText("Månad");

      fireEvent.change(picker, { target: { value: "2026-04" } });
      expect(picker).toHaveValue("2026-04");

      // A DIFFERENT month arriving from the server must win over the uncommitted
      // draft: the back button, or a bookmarked ?month=, both re-render this
      // component in place with a new prop.
      //
      // The first version of this test re-rendered with the SAME month still on
      // screen and expected the draft to be discarded. That transition is not
      // producible: `page.tsx` echoes the month the backend resolved, and every
      // value this picker can emit is one the backend returns unchanged, so a
      // committed April never comes back as May. Asserting it would have been a
      // production fact resting on a premise production cannot produce
      // (CLAUDE.md §5, Tests).
      rerender(<StrictMode>{pickerView("2026-03")}</StrictMode>);

      expect(picker).toHaveValue("2026-03");
    });

    it("says in words when the report updates, and ties that to the select", () => {
      renderPicker("2026-05");
      const picker = screen.getByLabelText("Månad");

      // Navigation is no longer implied by the control's own behaviour, so the
      // affordance has to be stated. Asserted through the aria-describedby LINK
      // rather than by accessible-description text: jsdom fuses adjacent element
      // text in a way Chromium does not, so the link is the fact that transfers.
      //
      // The verb is "klickar utanför fältet", not "lämnar fältet". design-reviewer
      // measured why: "leave the field" is focus vocabulary, and a mouse user has
      // no model of being IN a field — they click elsewhere. This one sentence
      // carries both the WCAG advisement and the whole mouse affordance, so if it
      // is not actionable the construction rests on nothing.
      const describedBy = picker.getAttribute("aria-describedby") ?? "";
      expect(describedBy).not.toBe("");
      expect(document.getElementById(describedBy)).toHaveTextContent(
        "Rapporten uppdateras när du trycker Enter eller klickar utanför fältet.",
      );
    });

    it("says, while a draft is uncommitted, which month the report below still shows", () => {
      renderPicker("2026-05");
      const picker = screen.getByLabelText("Månad");
      const pending = document.getElementById(
        "aktivitetsrapport-month-pending",
      );

      // Always mounted, so toggling it cannot shift the counter, the CTA or the
      // cards under it. Empty until a draft exists — the height is reserved, the
      // sentence is not.
      expect(pending).toBeInTheDocument();
      expect(pending).toHaveTextContent("");
      expect(picker.getAttribute("aria-describedby")).not.toContain("pending");

      fireEvent.change(picker, { target: { value: "2026-04" } });

      expect(pending).toHaveTextContent(
        "Rapporten nedan visar fortfarande maj 2026.",
      );
      // It joins the field's description only while it carries text; an empty
      // description would otherwise be announced as part of the field forever.
      expect(picker.getAttribute("aria-describedby")).toContain(
        "aktivitetsrapport-month-pending",
      );
    });

    it("announces the month only once the report has actually changed to it", () => {
      const { rerender } = renderPicker("2026-05");
      const picker = screen.getByLabelText("Månad");
      // By id, not by role: every CopyButton already carries its own sr-only
      // `role="status"`, six per card, so a role query finds seven regions and
      // cannot say which one is the report's.
      const status = document.getElementById(
        "aktivitetsrapport-month-announcer",
      );
      expect(status).toBeInTheDocument();

      // Mounted empty at first paint. A live region that appears together with
      // its content is the trap that makes announcements unreliable, so the node
      // is persistent and the text is what changes.
      expect(status).toHaveTextContent("");

      // A draft alone must not announce: nothing has changed below yet.
      fireEvent.change(picker, { target: { value: "2026-04" } });
      expect(status).toHaveTextContent("");

      // The month ARRIVING is the honest anchor — not the navigation starting.
      rerender(pickerView("2026-03"));
      expect(status).toHaveTextContent("Rapporten visar mars 2026.");
    });
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
