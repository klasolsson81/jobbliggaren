import { describe, it, expect, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RequiresYouCard } from "./requires-you-card";
import { RecentEventsCard } from "./recent-events-card";
import messages from "../../../messages/sv";
import type { SectionNoticeData } from "./notice-section";

const COPY = messages.oversikt;

const followUp: SectionNoticeData = {
  id: "n-followup",
  source: "applications",
  type: "followup",
  kind: "warning",
  label: "Uppföljning",
  text: "Du har 2 ansökningar som inte fått svar.",
  cta: "Visa ansökningar",
  href: "/ansokningar",
  time: "idag",
};
const interview: SectionNoticeData = {
  id: "n-interview",
  source: "applications",
  type: "interviews",
  kind: "brand",
  label: "Intervju",
  text: "Stena Line har bekräftat intervjutid.",
  cta: "Öppna ärende",
  href: "/ansokningar",
  time: "igår",
};
const match: SectionNoticeData = {
  id: "n-match",
  source: "jobads",
  type: "matches",
  kind: "info",
  label: "Matchning",
  text: "Det finns 245 annonser som matchar dina val.",
  cta: "Visa annonser",
  href: "/jobb",
  time: "idag",
};

beforeEach(() => window.localStorage.clear());

function requiresYou() {
  return screen.getByRole("region", { name: COPY.cards.requiresYou });
}

describe("RequiresYouCard", () => {
  it("renders one row per notice with an icon box in its kind, label, time, text and an emphasised row CTA", () => {
    render(<RequiresYouCard notices={[followUp, interview]} />);
    const rows = [...requiresYou().querySelectorAll<HTMLElement>(".jp-ov-action")];
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveAttribute("data-kind", "warning");
    expect(rows[1]).toHaveAttribute("data-kind", "brand");
    expect(rows[0]!.querySelector<HTMLElement>(".jp-ov-action__icon svg")).not.toBeNull();
    expect(within(rows[0]!).getByText("Uppföljning")).toBeInTheDocument();
    expect(within(rows[0]!).getByText("idag")).toBeInTheDocument();
    const cta = within(rows[0]!).getByRole("link", { name: /Visa ansökningar/ });
    expect(cta).toHaveAttribute("href", "/ansokningar");
    expect(cta.className).toContain("jp-btn--emphasis");
    expect(cta.className).not.toContain("jp-btn--primary");
    expect(within(requiresYou()).getByText("2 olästa")).toBeInTheDocument();
  });

  it("empty: the card stays, carries the empty row and data-empty for the neutral bar", () => {
    render(<RequiresYouCard notices={[]} />);
    expect(requiresYou()).toHaveAttribute("data-empty", "true");
    expect(within(requiresYou()).getByText(COPY.cards.requiresYouEmpty)).toBeInTheDocument();
    expect(within(requiresYou()).getByText("inga olästa")).toBeInTheDocument();
  });

  it("dismiss moves the row behind the read foot and focus to the foot's toggle", async () => {
    const user = userEvent.setup();
    render(<RequiresYouCard notices={[followUp]} />);
    await user.click(within(requiresYou()).getByRole("button", { name: COPY.notices.dismiss }));

    expect(requiresYou().querySelectorAll<HTMLElement>(".jp-ov-action")).toHaveLength(0);
    expect(within(requiresYou()).getByText("1 läst notis")).toBeInTheDocument();
    const toggle = within(requiresYou()).getByRole("button", { name: COPY.notices.showRead });
    expect(document.activeElement).toBe(toggle);
    expect(requiresYou()).toHaveAttribute("data-empty", "true");
    // The empty row yields to the read foot: the card is not "empty", it is "all read".
    expect(within(requiresYou()).queryByText(COPY.cards.requiresYouEmpty)).toBeNull();
  });

  it("Visa shows the read row muted with a restore control; restoring the last one moves focus to the card", async () => {
    const user = userEvent.setup();
    render(<RequiresYouCard notices={[followUp]} />);
    await user.click(within(requiresYou()).getByRole("button", { name: COPY.notices.dismiss }));
    await user.click(within(requiresYou()).getByRole("button", { name: COPY.notices.showRead }));

    const readRow = requiresYou().querySelector<HTMLElement>(".jp-ov-action--read");
    expect(readRow).not.toBeNull();
    await user.click(within(readRow as HTMLElement).getByRole("button", { name: COPY.notices.restore }));

    expect(requiresYou().querySelectorAll<HTMLElement>(".jp-ov-action--read")).toHaveLength(0);
    expect(within(requiresYou()).queryByText(/läst notis/)).toBeNull();
    expect(document.activeElement).toBe(requiresYou());
  });

  it("a switched-off type is removed entirely and counts nowhere", () => {
    window.localStorage.setItem(
      "jp-oversikt-notice-prefs",
      JSON.stringify({ "applications:followup": false }),
    );
    render(<RequiresYouCard notices={[followUp, interview]} />);
    expect(within(requiresYou()).queryByText("Uppföljning")).toBeNull();
    expect(within(requiresYou()).getByText("1 oläst")).toBeInTheDocument();
  });
});

describe("RecentEventsCard", () => {
  function events() {
    return screen.getByRole("region", { name: COPY.cards.events });
  }

  it("renders hairline rows with a text-link CTA, the time and a dismiss control", () => {
    render(<RecentEventsCard notices={[match]} />);
    const row = events().querySelector<HTMLElement>(".jp-ov-event") as HTMLElement;
    expect(row).toHaveAttribute("data-kind", "info");
    expect(within(row).getByText("Matchning")).toBeInTheDocument();
    const cta = within(row).getByRole("link", { name: /Visa annonser/ });
    expect(cta).toHaveAttribute("href", "/jobb");
    expect(cta.className).not.toContain("jp-btn");
    expect(within(row).getByText("idag")).toBeInTheDocument();
    expect(within(row).getByRole("button", { name: COPY.notices.dismiss })).toBeInTheDocument();
  });

  it("empty: the card stays with its empty row", () => {
    render(<RecentEventsCard notices={[]} />);
    expect(within(events()).getByText(COPY.cards.eventsEmpty)).toBeInTheDocument();
    expect(events()).toHaveAttribute("data-span", "12");
  });

  it("the read foot keeps the guest ledger's toggle class — MarkAllReadRow's focus target", async () => {
    const user = userEvent.setup();
    render(<RecentEventsCard notices={[match]} />);
    await user.click(within(events()).getByRole("button", { name: COPY.notices.dismiss }));
    expect(events().querySelector<HTMLElement>("li.jp-notice-foot button.jp-notice-foot__toggle")).not.toBeNull();
  });
});
