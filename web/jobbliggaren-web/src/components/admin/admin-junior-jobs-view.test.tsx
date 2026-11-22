import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import type { ActionResult } from "@/lib/actions/_action-result";
import type { AdminJuniorChoice, AdminJuniorJobRow, AdminJuniorJobsPage, AdminJuniorJobsQuery } from "@/lib/admin/junior-jobs";
import { AdminJuniorJobsView, type AdminJuniorJobsViewProps } from "./admin-junior-jobs-view";

const BASE = "/admin/juniorjobb";
const FIRST = "00000000-0000-4000-8000-000000000901";
const SECOND = "00000000-0000-4000-8000-000000000902";
const THIRD = "00000000-0000-4000-8000-000000000903";

const ROW: AdminJuniorJobRow = {
  id: FIRST,
  title: "Systemutvecklare, nyexaminerad välkommen",
  company: "Arbetsgivare A",
  municipality: "Göteborg",
  publishedAt: "2026-11-19T08:15:00+00:00",
  expiresAt: "2026-12-17T10:00:00+00:00",
  verdict: "unclear",
  ruleId: "text-new-graduate",
  decision: null,
};

const page = (items: ReadonlyArray<AdminJuniorJobRow>, patch: Partial<AdminJuniorJobsPage> = {}): AdminJuniorJobsPage => ({
  items,
  page: 1,
  totalPages: items.length === 0 ? 0 : 1,
  totalCount: items.length,
  counts: { unclear: 7, shown: 4, decided: 3 },
  ...patch,
});

function renderView(props: Partial<AdminJuniorJobsViewProps> & { readonly query?: AdminJuniorJobsQuery } = {}) {
  const onDecide = vi.fn<(id: string, decision: AdminJuniorChoice) => Promise<ActionResult>>(async () => ({ success: true }));
  const view = render(
    <AdminJuniorJobsView
      basePath={BASE}
      query={{ tab: "unclear", page: 1 }}
      region={{ kind: "loaded", data: page([ROW]) }}
      onDecide={onDecide}
      {...props}
    />,
  );
  return { ...view, onDecide };
}

const tabs = () => within(screen.getByRole("navigation", { name: "Välj flik" })).getAllByRole("link");
const row = (title: string) => {
  const item = screen.getByRole("heading", { level: 3, name: title }).closest("li");
  if (item === null) throw new Error(`No row for ${title}`);
  return item;
};
/** A row's facts as term and value, in the order the row shows them. */
const facts = (title: string) =>
  [...row(title).querySelectorAll("dl > div")].map((pair) => [
    pair.querySelector("dt")?.textContent ?? "",
    pair.querySelector("dd")?.textContent ?? "",
  ]);
/** Each button's label as it is named: the busy form beside it is hidden from the name. */
const choices = (title: string) =>
  within(within(row(title)).getByRole("group", { name: title }))
    .getAllByRole("button")
    .map((button) => [...button.querySelectorAll(".jp-adminbusy > span:not([aria-hidden])")].map((span) => span.textContent).join(""));

describe("AdminJuniorJobsView (#2264)", () => {
  it("names the page and links the three tabs with their counts, with no lede under the heading", () => {
    const { container } = renderView();

    expect(screen.getByRole("heading", { level: 1, name: "Juniorjobb" })).toBeInTheDocument();
    expect(container.querySelector(".jp-lede")).toBeNull();
    expect(tabs().map((link) => [link.textContent, link.getAttribute("href"), link.getAttribute("aria-current")])).toEqual([
      ["Att avgöra (7)", BASE, "true"],
      ["Visas på /junior (4)", `${BASE}?flik=visas`, null],
      ["Beslutade (3)", `${BASE}?flik=beslutade`, null],
    ]);
  });

  it.each([
    ["shown", "visas"],
    ["decided", "beslutade"],
  ] as const)("marks the %s tab as the open one, and says nothing more about it", (tab, slug) => {
    const { container } = renderView({ query: { tab, page: 1 } });

    expect(screen.getByRole("link", { current: true })).toHaveAttribute("href", `${BASE}?flik=${slug}`);
    expect(container.querySelector(".jp-lede")).toBeNull();
  });

  it("shows the ad's title as a plain link to its page, the employer, and the kommun, the dates and the rule as facts", () => {
    renderView();

    const item = row(ROW.title);
    const link = within(item).getByRole("link", { name: ROW.title });
    expect(link).toHaveAttribute("href", `/jobb/${FIRST}`);
    expect(within(item).getByText("Arbetsgivare A")).toHaveClass("jp-adminjunior__employer");
    expect(facts(ROW.title)).toEqual([
      ["Ort", "Göteborg"],
      ["Publicerad", "19 nov. 2026"],
      ["Sista dag", "17 dec. 2026"],
      ["Varför", "Nyexaminerad i texten"],
    ]);
    // The kommun's term is for a screen reader; the pin names the axis on screen.
    expect(within(item).getByText("Ort")).toHaveClass("sr-only");
  });

  it("leaves out a kommun and a last day the ad does not have", () => {
    renderView({ region: { kind: "loaded", data: page([{ ...ROW, municipality: null, expiresAt: null }]) } });

    const item = row(ROW.title);
    expect(within(item).queryByText("Sista dag")).toBeNull();
    expect(within(item).queryByText("Göteborg")).toBeNull();
  });

  it("names a rule it does not know as an unknown rule, never by its id", () => {
    renderView({ region: { kind: "loaded", data: page([{ ...ROW, ruleId: "title-intern" }]) } });

    expect(within(row(ROW.title)).getByText("Okänd regel")).toBeInTheDocument();
    expect(screen.queryByText(/title-intern/)).toBeNull();
  });

  it("offers Junior and Inte junior in Att avgöra", () => {
    renderView();

    expect(choices(ROW.title)).toEqual(["Junior", "Inte junior"]);
  });

  it("offers Inte junior in Visas, and the hand-back too on a decided row, which shows the rules' outcome and your decision", () => {
    const decided = { ...ROW, id: SECOND, title: "Applikationsutvecklare", ruleId: "candidate-only", decision: "junior" as const };
    renderView({
      query: { tab: "shown", page: 1 },
      region: { kind: "loaded", data: page([{ ...ROW, verdict: "junior", ruleId: "title-junior" }, decided]) },
    });

    expect(choices(ROW.title)).toEqual(["Inte junior"]);
    expect(facts(ROW.title).map(([term]) => term)).toEqual(["Ort", "Publicerad", "Sista dag", "Varför"]);
    expect(choices("Applikationsutvecklare")).toEqual(["Inte junior", "Lämna till reglerna"]);
    expect(facts("Applikationsutvecklare").slice(3)).toEqual([
      ["Reglerna", "Oavgjord · Juniorord utan avgörande regel"],
      ["Ditt beslut", "Junior"],
    ]);
  });

  it("offers the other decision and the hand-back in Beslutade, and names what the rules came to for each verdict", () => {
    renderView({
      query: { tab: "decided", page: 1 },
      region: {
        kind: "loaded",
        data: page([
          { ...ROW, title: "Junior testare", verdict: "junior", ruleId: "title-junior", decision: "notJunior" },
          { ...ROW, id: SECOND, title: "Systemutvecklare med handledaransvar", verdict: "notJunior", ruleId: "text-mentor-only", decision: "notJunior" },
          { ...ROW, id: THIRD, title: "Applikationsutvecklare", verdict: "unclear", ruleId: "candidate-only", decision: "junior" },
        ]),
      },
    });

    expect(choices("Junior testare")).toEqual(["Junior", "Lämna till reglerna"]);
    expect(choices("Applikationsutvecklare")).toEqual(["Inte junior", "Lämna till reglerna"]);
    expect(facts("Junior testare").slice(3)).toEqual([
      ["Reglerna", "Junior · Juniorord i rubriken"],
      ["Ditt beslut", "Inte junior"],
    ]);
    expect(facts("Systemutvecklare med handledaransvar").slice(3)).toEqual([
      ["Reglerna", "Inte junior · Ska handleda juniorer"],
      ["Ditt beslut", "Inte junior"],
    ]);
    expect(facts("Applikationsutvecklare").slice(3)).toEqual([
      ["Reglerna", "Oavgjord · Juniorord utan avgörande regel"],
      ["Ditt beslut", "Junior"],
    ]);
    expect(screen.queryByText("Varför")).toBeNull();
  });

  it.each([
    ["unclear", "Inga annonser att avgöra just nu."],
    ["shown", "Inga annonser visas på /junior just nu."],
    ["decided", "Inga beslutade annonser just nu."],
  ] as const)("says the %s tab is empty, and keeps its counts", (tab, empty) => {
    renderView({ query: { tab, page: 1 }, region: { kind: "loaded", data: page([]) } });

    expect(screen.getByText(empty)).toBeInTheDocument();
    expect(screen.queryByRole("list")).toBeNull();
    expect(tabs().map((link) => link.textContent)).toEqual(["Att avgöra (7)", "Visas på /junior (4)", "Beslutade (3)"]);
  });

  it("offers the first page when the page asked for is past the last one", () => {
    renderView({ query: { tab: "shown", page: 4 }, region: { kind: "loaded", data: page([], { page: 4, totalCount: 30, totalPages: 2 }) } });

    expect(screen.getByText(/Sidan finns inte\./)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Till första sidan" })).toHaveAttribute("href", `${BASE}?flik=visas`);
  });

  it("says the queue could not be read as an alert, and shows the tabs without counts it does not have", () => {
    renderView({ region: { kind: "failed" } });

    expect(screen.getByRole("alert")).toHaveTextContent("Uppgifterna kunde inte hämtas. Försök igen om en stund.");
    expect(tabs().map((link) => link.textContent)).toEqual(["Att avgöra", "Visas på /junior", "Beslutade"]);
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("says why a refused read failed", () => {
    renderView({ region: { kind: "failed" }, failed: "Din session saknar Admin-rollen." });

    expect(screen.getByRole("alert")).toHaveTextContent("Din session saknar Admin-rollen.");
  });

  it("pages with Föregående and Nästa, inert at the first page", () => {
    renderView({ query: { tab: "decided", page: 1 }, region: { kind: "loaded", data: page([ROW], { totalCount: 30, totalPages: 2 }) } });

    const pager = screen.getByRole("navigation", { name: "Sidnavigering" });
    expect(within(pager).getByRole("status")).toHaveTextContent("Sida 1 av 2");
    expect(within(pager).getByRole("link", { name: "Föregående" })).toHaveAttribute("aria-disabled", "true");
    expect(within(pager).getByRole("link", { name: "Nästa" })).toHaveAttribute("href", `${BASE}?flik=beslutade&sida=2`);
  });

  it("sends the decision for the row, shows Sparar… on the pressed button while it runs and ignores a second press", async () => {
    let settle: (result: ActionResult) => void = () => {};
    const { onDecide } = renderView();
    onDecide.mockImplementation(() => new Promise((resolve) => (settle = resolve)));

    const [junior, notJunior] = within(row(ROW.title)).getAllByRole("button");
    await act(async () => {
      fireEvent.click(junior!);
    });

    expect(onDecide).toHaveBeenCalledWith(FIRST, "junior");
    expect(junior).toHaveAccessibleName("Sparar…");
    expect(junior).toHaveAttribute("aria-disabled", "true");
    expect(notJunior).toHaveAttribute("aria-disabled", "true");
    fireEvent.click(notJunior!);
    expect(onDecide).toHaveBeenCalledTimes(1);

    await act(async () => settle({ success: true }));
    expect(junior).toHaveAccessibleName("Junior");
    expect(junior).not.toHaveAttribute("aria-disabled");
    expect(screen.getByRole("status")).toHaveTextContent(`Markerad som junior: ${ROW.title}`);
  });

  it("sends null to hand a decided row back to the rules", async () => {
    const { onDecide } = renderView({ query: { tab: "decided", page: 1 }, region: { kind: "loaded", data: page([{ ...ROW, decision: "notJunior" }]) } });

    await act(async () => {
      fireEvent.click(within(row(ROW.title)).getByRole("button", { name: "Lämna till reglerna" }));
    });

    expect(onDecide).toHaveBeenCalledWith(FIRST, null);
  });

  it("shows a refusal in the row it was asked from, and keeps the row and its buttons", async () => {
    const { onDecide } = renderView();
    onDecide.mockResolvedValue({ success: false, error: "Annonsen är stängd eller borttagen och kan inte avgöras." });

    await act(async () => {
      fireEvent.click(within(row(ROW.title)).getByRole("button", { name: "Inte junior" }));
    });

    expect(within(row(ROW.title)).getByRole("alert")).toHaveTextContent(
      "Annonsen är stängd eller borttagen och kan inte avgöras.",
    );
    expect(choices(ROW.title)).toEqual(["Junior", "Inte junior"]);
  });

  it("keeps focus in a row that stays: on the pressed button when the decision flips, on the first one when its button left", async () => {
    const decided = { ...ROW, verdict: "junior" as const, decision: "junior" as const };
    const { onDecide, rerender } = renderView({ query: { tab: "decided", page: 1 }, region: { kind: "loaded", data: page([decided]) } });

    const flip = within(row(ROW.title)).getByRole("button", { name: "Inte junior" });
    flip.focus();
    await act(async () => {
      fireEvent.click(flip);
    });
    rerender(
      <AdminJuniorJobsView
        basePath={BASE}
        query={{ tab: "decided", page: 1 }}
        region={{ kind: "loaded", data: page([{ ...decided, decision: "notJunior" }]) }}
        onDecide={onDecide}
      />,
    );
    expect(within(row(ROW.title)).getByRole("button", { name: "Junior" })).toHaveFocus();

    rerender(
      <AdminJuniorJobsView basePath={BASE} query={{ tab: "shown", page: 1 }} region={{ kind: "loaded", data: page([decided]) }} onDecide={onDecide} />,
    );
    const handBack = within(row(ROW.title)).getByRole("button", { name: "Lämna till reglerna" });
    handBack.focus();
    await act(async () => {
      fireEvent.click(handBack);
    });
    // Handed back to a rule that shows it: the row stays in Visas and its hand-back button leaves.
    rerender(
      <AdminJuniorJobsView
        basePath={BASE}
        query={{ tab: "shown", page: 1 }}
        region={{ kind: "loaded", data: page([{ ...decided, decision: null }]) }}
        onDecide={onDecide}
      />,
    );
    expect(within(row(ROW.title)).getByRole("button", { name: "Inte junior" })).toHaveFocus();
    expect(onDecide.mock.calls).toEqual([
      [FIRST, "notJunior"],
      [FIRST, null],
    ]);
  });

  it("moves focus to the next row's title when a decided row leaves, and to the h1 when the last one does", async () => {
    const second = { ...ROW, id: SECOND, title: "Utvecklare till kundteamet" };
    const { onDecide, rerender } = renderView({ region: { kind: "loaded", data: page([ROW, second]) } });
    const props = { basePath: BASE, query: { tab: "unclear", page: 1 } as const, onDecide };

    const junior = within(row(ROW.title)).getByRole("button", { name: "Junior" });
    junior.focus();
    await act(async () => {
      fireEvent.click(junior);
    });
    // The page read after the decision no longer holds the row; its button leaves with it.
    rerender(<AdminJuniorJobsView {...props} region={{ kind: "loaded", data: page([second]) }} />);
    expect(screen.getByRole("link", { name: second.title })).toHaveFocus();

    const last = within(row(second.title)).getByRole("button", { name: "Inte junior" });
    last.focus();
    await act(async () => {
      fireEvent.click(last);
    });
    rerender(<AdminJuniorJobsView {...props} region={{ kind: "loaded", data: page([]) }} />);
    expect(screen.getByRole("heading", { level: 1, name: "Juniorjobb" })).toHaveFocus();
    expect(screen.getByText("Inga annonser att avgöra just nu.")).toBeInTheDocument();
    expect(screen.getByText(`Markerad som inte junior: ${second.title}`)).toBeInTheDocument();
  });
});
