import { describe, expect, it } from "vitest";
import { render, renderHook, screen, within } from "@testing-library/react";
import { EMAIL_KINDS, type EmailKindCount, type EmailKindsDto } from "@/lib/dto/admin-email";
import type { AdminEmailRead } from "@/lib/admin/view-models";
import { AdminEmailKinds, useEmailKindsLine } from "./admin-email-kinds";

const LABELS = [
  "Inloggning och koder",
  "Adressbyte begärt",
  "Adressbyte genomfört",
  "Matchningar",
  "Bevakade företag",
  "Ny feedback",
  "Svar på feedback",
];
const NOT_OBSERVABLE =
  "Jobbliggarens räkning visar inga utskick under perioden, fast Scaleway har skickat. Se runbooken för admin-e-post.";
const NOT_CONFIGURED = "Räknas inte i den här miljön.";
const OUR_READ_FAILED = "Uppgifterna kunde inte hämtas. Försök igen om en stund.";

/** Every kind with a count, the first with two failure classes on its latest day and the fourth with one. */
function rows(overrides: Partial<Record<number, Partial<EmailKindCount>>> = {}): EmailKindCount[] {
  return EMAIL_KINDS.map((kind, index) => ({
    kind,
    accepted: [1204, 3, 2, 118, 26, 4, 1][index] ?? 0,
    failed: index === 0 ? 2 : index === 3 ? 1 : 0,
    latestFailure: index === 0
      ? { day: "2026-10-22", outcomes: ["Refused", "Timeout"] }
      : index === 3 ? { day: "2026-10-21", outcomes: ["Other"] } : null,
    ...overrides[index],
  }));
}

type ObservedKinds = Extract<EmailKindsDto, { readonly status: "Observed" }>;
const observed = (kinds: EmailKindCount[] = rows()): ObservedKinds =>
  ({ period: "d3", status: "Observed", from: "2026-10-20", coverage: "Complete", coveredFrom: null, kinds });

function show(kinds: AdminEmailRead<EmailKindsDto> = { kind: "loaded", data: observed() }) {
  render(<AdminEmailKinds kinds={kinds} />);
  return screen.getByRole("region", { name: "Utskick per mejltyp" });
}

const table = () => screen.getByRole("table", { name: /^(Antal u|U)tskick per mejltyp/ });
const bodyRows = () => within(table()).getAllByRole("row").slice(1);
const cells = (row: HTMLElement) => [...row.querySelectorAll("th, td")].map((cell) => cell.textContent);

describe("Utskick per mejltyp's structure (#1981 PR 3)", () => {
  it("is a sub-section headed by an h3, with the note and then the table", () => {
    const section = show();

    const heading = within(section).getByRole("heading", { level: 3, name: "Utskick per mejltyp" });
    expect(heading).toHaveClass("jp-adminh3");
    expect(heading.nextElementSibling).toHaveClass("jp-adminmailsum__note");
    expect(heading.nextElementSibling?.nextElementSibling).toHaveClass("jp-admintable-scroll");
    expect(within(section).getByRole("region", { name: /^Utskick per mejltyp från/ })).toContainElement(table());
  });

  it("heads its columns Mejltyp, Överlämnade, Fel and Senaste fel, the counts as numbers", () => {
    show();

    const headers = within(table()).getAllByRole("columnheader");
    expect(headers.map((th) => [th.textContent, th.getAttribute("scope")])).toEqual([
      ["Mejltyp", "col"],
      ["Överlämnade", "col"],
      ["Fel", "col"],
      ["Senaste fel", "col"],
    ]);
    expect(headers.map((th) => th.classList.contains("jp-admintable__num"))).toEqual([false, true, true, false]);
  });

  it("names each of the seven kinds in a row header, in a fixed order, and has no total row", () => {
    show();

    const headers = within(table()).getAllByRole("rowheader");
    expect(headers.map((th) => th.textContent)).toEqual(LABELS);
    expect(headers.every((th) => th.tagName === "TH" && th.getAttribute("scope") === "row")).toBe(true);
    expect(bodyRows()).toHaveLength(7);
    expect(table().textContent).not.toMatch(/totalt|summa/i);
  });

  it("keeps all seven rows when every count is zero", () => {
    show({ kind: "loaded", data: observed(EMAIL_KINDS.map((kind) => ({ kind, accepted: 0, failed: 0, latestFailure: null }))) });

    expect(bodyRows().map(cells)).toEqual(LABELS.map((label) => [label, "0", "0", "Inget"]));
    expect(table().querySelector(".jp-admin-danger")).toBeNull();
  });
});

describe("Utskick per mejltyp's rows", () => {
  it("shows each kind's handovers, failures and latest failure, a failure count above zero in the danger colour", () => {
    show();

    expect(bodyRows().map(cells)).toEqual([
      ["Inloggning och koder", "1 204", "2", "2026-10-22: avvisat av Scaleway och inget svar i tid"],
      ["Adressbyte begärt", "3", "0", "Inget"],
      ["Adressbyte genomfört", "2", "0", "Inget"],
      ["Matchningar", "118", "1", "2026-10-21: annat fel"],
      ["Bevakade företag", "26", "0", "Inget"],
      ["Ny feedback", "4", "0", "Inget"],
      ["Svar på feedback", "1", "0", "Inget"],
    ]);
    const failed = bodyRows().map((row) => row.querySelectorAll("td")[1]);
    expect(failed.map((cell) => cell?.querySelector(".jp-admin-danger")?.textContent ?? null)).toEqual([
      "2", null, null, "1", null, null, null,
    ]);
  });

  it("dates the latest failure with its day as a time element, never a time of day", () => {
    show();

    const time = bodyRows()[0]?.querySelector("time");
    expect(time).toHaveAttribute("dateTime", "2026-10-22");
    expect(time).toHaveTextContent(/^2026-10-22$/);
  });

  it("lists every class seen that day in a fixed order, each in its own words", () => {
    show({
      kind: "loaded",
      data: observed(rows({
        0: { latestFailure: { day: "2026-10-22", outcomes: ["Refused", "RateLimited", "Unreachable", "ProviderError", "Timeout", "Other"] } },
      })),
    });

    expect(cells(bodyRows()[0]!)[3]).toBe(
      "2026-10-22: avvisat av Scaleway, över Scaleways taktgräns, Scaleway nåddes inte, fel hos Scaleway, inget svar i tid och annat fel",
    );
  });

  it("says a kind with no failure had none in words, never with the dash that means unknown", () => {
    show();
    expect(table().textContent).not.toMatch(/[–—]/);
    expect(within(table()).getAllByText("Inget")).toHaveLength(5);
  });
});

describe("Utskick per mejltyp's note and caption", () => {
  it("names the counter and the period's first day, as the date it is", () => {
    const section = show({ kind: "loaded", data: { ...observed(), from: "2026-03-29" } });

    const note = within(section).getByText(/^Räknat av Jobbliggaren/);
    expect(note).toHaveTextContent(
      "Räknat av Jobbliggaren vid varje försök att lämna över ett mejl till Scaleway, från och med 2026-03-29.",
    );
    expect(note.querySelector("time")).toHaveAttribute("dateTime", "2026-03-29");
    expect(table()).toHaveAccessibleName("Utskick per mejltyp från och med 2026-03-29");
    expect(section.textContent).not.toMatch(/Scaleways leveransstatistik|Räknat av Scaleway/);
  });

  it("names when the count began, to the minute in Swedish time, where it began inside the period", () => {
    const section = show({
      kind: "loaded",
      data: { ...observed(), coverage: "Partial", coveredFrom: "2026-10-23T05:12:34+00:00" },
    });

    const note = within(section).getByText(/^Räknat av Jobbliggaren/);
    expect(note).toHaveTextContent(
      "Räknat av Jobbliggaren vid varje försök att lämna över ett mejl till Scaleway, sedan räkningen började 2026-10-23 07:12.",
    );
    expect(note.querySelector("time")).toHaveAttribute("dateTime", "2026-10-23T05:12:34+00:00");
    expect(note.textContent).not.toContain("från och med");
    expect(table()).toHaveAccessibleName("Utskick per mejltyp sedan 2026-10-23 07:12");
    expect(bodyRows()).toHaveLength(7);
  });
});

describe("Utskick per mejltyp's states (ADR 0150 D2): no zero and no row it did not observe", () => {
  it.each<readonly [string, AdminEmailRead<EmailKindsDto>, string]>([
    ["a deployment without Scaleway", {
      kind: "loaded", data: { period: "d3", status: "NotObserved", from: null, coverage: null, coveredFrom: null, kinds: null },
    }, NOT_CONFIGURED],
    ["a count with no sends while the provider sent", {
      kind: "loaded", data: { period: "d3", status: "NotObservable", from: "2026-10-20", coverage: null, coveredFrom: null, kinds: null },
    }, NOT_OBSERVABLE],
    ["our own read failing", { kind: "failed" }, OUR_READ_FAILED],
    ["our own read refused", { kind: "failed", line: "Din session saknar Admin-rollen." }, "Din session saknar Admin-rollen."],
    ["our own read pending", { kind: "loading" }, "Hämtar uppgifter…"],
  ])("shows %s as one line under the column heads, with no note", (_name, kinds, line) => {
    const section = show(kinds);

    expect(within(table()).getAllByRole("columnheader")).toHaveLength(4);
    expect(bodyRows()).toHaveLength(1);
    const cell = bodyRows()[0]?.querySelector("td");
    expect(cell).toHaveClass("jp-admintable__soon");
    expect(cell).toHaveAttribute("colSpan", "4");
    expect(cell).toHaveTextContent(line);
    expect(within(section).getAllByText(line)).toHaveLength(1);
    expect(within(table()).queryAllByRole("rowheader")).toEqual([]);
    expect(table().textContent).not.toMatch(/\d/);
    expect(section.querySelector(".jp-adminmailsum__note")).toBeNull();
    // The table's name never repeats the section's, so the two regions are told apart.
    expect(table()).toHaveAccessibleName("Antal utskick per mejltyp");
    // The section's line announces it; the region's own line is quiet.
    expect(screen.queryAllByRole("alert")).toEqual([]);
    expect(screen.queryAllByRole("status")).toEqual([]);
  });
});

describe("useEmailKindsLine", () => {
  it.each<readonly [string, AdminEmailRead<EmailKindsDto>, ReturnType<typeof useEmailKindsLine>]>([
    ["an observed count", { kind: "loaded", data: observed() }, null],
    ["a deployment without Scaleway", {
      kind: "loaded", data: { period: "h24", status: "NotObserved", from: null, coverage: null, coveredFrom: null, kinds: null },
    }, { text: NOT_CONFIGURED, announce: null }],
    ["an unobservable count", {
      kind: "loaded", data: { period: "h24", status: "NotObservable", from: "2026-10-22", coverage: null, coveredFrom: null, kinds: null },
    }, { text: NOT_OBSERVABLE, announce: "alert" }],
    ["a failed read", { kind: "failed" }, { text: OUR_READ_FAILED, announce: "alert" }],
    ["a pending read", { kind: "loading" }, { text: "Hämtar uppgifter…", announce: "status" }],
  ])("gives %s its line and announcement", (_name, read, expected) => {
    expect(renderHook(() => useEmailKindsLine(read)).result.current).toEqual(expected);
  });
});
