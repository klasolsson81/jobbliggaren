import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AdminEmailDelivery as Delivery } from "@/lib/admin/view-models";
import { AdminEmailDelivery } from "./admin-email-delivery";

const DELIVERY: Delivery = {
  totals: { sent: 1234, failed: 2, noRecipient: 0 },
  types: [
    { type: "login-challenge", sent: 31, failed: 1, lastError: "Mottagarens server svarade inte." },
    { type: "email-changed-notification", sent: 4, failed: 0, lastError: null },
  ],
  failures: [
    {
      id: "x1",
      occurredAt: "2026-10-04T04:41:00Z",
      recipient: "k•••@example.test",
      outcome: "bounced",
      message: "Adressen finns inte.",
      type: "match-notification",
    },
  ],
};

describe("AdminEmailDelivery with outcomes", () => {
  it("shows the totals, a failure count above zero in the danger colour", () => {
    render(<AdminEmailDelivery region={{ kind: "loaded", data: DELIVERY }} />);

    expect(screen.getAllByRole("definition").map((value) => value.textContent)).toEqual(["1 234", "2", "0"]);
    expect(within(screen.getAllByRole("definition")[1]!).getByText("2")).toHaveClass("jp-admin-danger");
    expect(within(screen.getAllByRole("definition")[2]!).getByText("0")).not.toHaveClass("jp-admin-danger");
  });

  it("lists each email type and the latest failures", () => {
    render(<AdminEmailDelivery region={{ kind: "loaded", data: DELIVERY }} />);

    const rows = within(screen.getByRole("table", { name: "Utskick per mejltyp" })).getAllByRole("row").slice(1);
    expect(rows.map((row) => within(row).getAllByRole("cell").map((cell) => cell.textContent))).toEqual([
      ["login-challenge", "31", "1", "Mottagarens server svarade inte."],
      ["email-changed-notification", "4", "0", "–"],
    ]);
    expect(within(screen.getByRole("region", { name: "Senaste misslyckade utskick" })).getByRole("listitem")).toHaveTextContent(
      "2026-10-04 06:41k•••@example.testStudsadeAdressen finns inte.match-notification",
    );
  });

  it("switches the period only while loaded and handled", async () => {
    const onPeriodChange = vi.fn();
    const { unmount } = render(
      <AdminEmailDelivery region={{ kind: "loaded", data: DELIVERY }} period="d7" onPeriodChange={onPeriodChange} />,
    );
    await userEvent.click(screen.getByRole("radio", { name: "24 tim" }));
    expect(onPeriodChange).toHaveBeenCalledWith("h24");
    unmount();

    render(<AdminEmailDelivery region={{ kind: "loading" }} onPeriodChange={onPeriodChange} />);
    for (const option of screen.getAllByRole("radio")) expect(option).toBeDisabled();
  });

  it("says so when nothing was sent in the period", () => {
    render(
      <AdminEmailDelivery region={{ kind: "loaded", data: { totals: { sent: 0, failed: 0, noRecipient: 0 }, types: [], failures: [] } }} />,
    );

    expect(within(screen.getByRole("table", { name: "Utskick per mejltyp" })).getAllByRole("row")[1]).toHaveTextContent(
      "Inga utskick under perioden.",
    );
    expect(screen.getByRole("region", { name: "Senaste misslyckade utskick" })).toHaveTextContent(
      "Inga misslyckade utskick under perioden.",
    );
  });

  it("shows a failed load in both lists, announces it once and keeps the totals unknown", () => {
    render(<AdminEmailDelivery region={{ kind: "failed" }} />);

    expect(screen.getAllByRole("alert")).toHaveLength(1);
    expect(screen.getAllByText("Uppgifterna kunde inte hämtas. Försök igen om en stund.")).toHaveLength(3);
    expect(screen.getAllByRole("definition").map((value) => value.textContent)).toEqual(["–", "–", "–"]);
  });
});
