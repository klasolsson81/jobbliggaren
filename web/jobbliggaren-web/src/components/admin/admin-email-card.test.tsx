import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { emailAllowanceFixture, overviewSnapshotFixture, OVERVIEW_TIME } from "@/test/fixtures/admin-overview";
import type { AdminOverviewSnapshot } from "@/lib/dto/admin-overview";
import type { EmailAllowanceDto } from "@/lib/dto/admin-email";
import { AdminOverview } from "./admin-overview";

const NOW = Date.parse(OVERVIEW_TIME);
const NO_USAGE = {
  observedAt: null, month: null, sent: null, billable: null, estimatedExtraCostCents: null,
  aboveHalfOfSendLimit: null, webhooksCount: null,
} as const;

const base = emailAllowanceFixture();
if (base.usage.status !== "Observed") throw new Error("The fixture is an observed month");
const observedUsage = base.usage;

function show(email: AdminOverviewSnapshot["email"], now = NOW) {
  render(<AdminOverview observations={{ ...overviewSnapshotFixture(), email }} now={now} />);
  return screen.getByRole("region", { name: "E-post" });
}
const loaded = (data: EmailAllowanceDto, sampledAt = "2026-10-08T09:59:30+00:00") =>
  show({ kind: "loaded", data, sampledAt, refreshFailed: false });
const value = (card: HTMLElement) => card.querySelector(".jp-adminkpi__value")?.textContent;

describe("the overview's e-post card from the allowance (#1981 PR 1, ADR 0150 Amendment 2026-10-19)", () => {
  it("leads with this month's sends against the free allowance, the cost under it, and when it was counted", () => {
    const card = loaded(base);

    expect(value(card)).toBe("1 301");
    expect(card.querySelector(".jp-adminkpi__unit")).toHaveTextContent("av 300 gratis i oktober 2026");
    expect(card).toHaveTextContent("Beräknad kostnad 0,26 € före moms");
    expect(card).toHaveTextContent("Uppgift från 2026-10-08 11:59");
    expect(within(card).getByRole("link", { name: "E-postleverans" })).toHaveAttribute("href", "/admin/e-post");
  });

  it("drops delivery's lines instead of en-dashing them, and never says the last 24 hours", () => {
    const card = loaded(base);

    expect(card).not.toHaveTextContent(/misslyckade|utan mottagare|senaste 24 tim|Mest skickade/);
    expect(within(card).queryByRole("list")).toBeNull();
  });

  it("shows a computed zero cost as a value", () => {
    const card = loaded({ ...base, usage: { ...observedUsage, sent: 142, billable: 0, estimatedExtraCostCents: 0 } });

    expect(value(card)).toBe("142");
    expect(card).toHaveTextContent("Beräknad kostnad 0,00 € före moms");
  });

  it("says this month where the month cannot be named", () => {
    const card = loaded({ ...base, usage: { ...observedUsage, month: null } });

    expect(card.querySelector(".jp-adminkpi__unit")).toHaveTextContent("av 300 gratis denna månad");
  });

  it("keeps the count and en-dashes the allowance and the cost while another plan is in force", () => {
    const card = loaded({
      ...base,
      usage: { ...observedUsage, billable: null, estimatedExtraCostCents: null },
      plan: { status: "Observed", reason: null, active: "Scale", matches: false },
    });

    expect(value(card)).toBe("1 301");
    expect(card.querySelector(".jp-adminkpi__unit")?.textContent).toBe("av –Uppgift saknas gratis i oktober 2026");
    expect(card.querySelector(".jp-adminkpi__sub")?.textContent).toBe("Beräknad kostnad –Uppgift saknas före moms");
  });

  it("says it is not counted here where no provider is configured: an en-dash, never Kommer snart or awaiting", () => {
    const card = loaded({
      ...base,
      usage: { status: "NotObserved", reason: "NotConfigured", ...NO_USAGE },
      plan: { status: "NotObserved", reason: "NotConfigured", active: null, matches: null },
    }, OVERVIEW_TIME);

    expect(value(card)).toBe("–Uppgift saknas");
    expect(card.querySelector(".jp-adminkpi__unit")).toBeNull();
    expect(card).toHaveTextContent("Räknas inte i den här miljön.");
    expect(card).not.toHaveTextContent(/Kommer snart|rapporterat|Beräknad kostnad/);
  });

  it("says why the provider did not answer the count, with the number an en-dash and its unit hidden", () => {
    const card = loaded({ ...base, usage: { status: "Failed", reason: "Forbidden", ...NO_USAGE } }, OVERVIEW_TIME);

    expect(value(card)).toBe("–Uppgift saknas");
    expect(card.querySelector(".jp-adminkpi__unit")).toBeNull();
    expect(card).toHaveTextContent(
      "Scaleway nekade läsningen. Kontrollera nyckelns behörighet enligt runbooken för admin-e-post.",
    );
    expect(card).not.toHaveTextContent("Beräknad kostnad");
  });

  it("shows our own failed read as the source's failure, quietly, beside the page's one announcement", () => {
    const card = show({ kind: "failed" });

    expect(value(card)).toBe("–Uppgift saknas");
    expect(within(card).getByText("Uppgifterna kunde inte hämtas. Försök igen om en stund.")).not.toHaveAttribute("role");
  });

  it("marks an old observation and a failed refresh as the other sources do", () => {
    const card = show({ kind: "loaded", data: base, sampledAt: "2026-10-08T09:59:30+00:00", refreshFailed: true }, NOW + 300_001);

    expect(card).toHaveTextContent("Uppdateringen misslyckades. Senaste uppgiften visas.");
    expect(card).toHaveTextContent("Uppgiften är äldre än fem minuter.");
  });

  it("is never an attention item, and the attention card still says email errors cannot be judged", () => {
    const card = loaded({ ...base, usage: { ...observedUsage, sent: 9000, billable: 8700, estimatedExtraCostCents: 218, aboveHalfOfSendLimit: true } });
    const attention = screen.getByRole("region", { name: "Kräver uppmärksamhet" });

    expect(card.querySelector(".jp-admin-danger, .jp-admin-warning")).toBeNull();
    expect(attention).toHaveTextContent("E-postfel kan ännu inte bedömas.");
    expect(within(attention).queryByRole("link", { name: /utskick/i })).toBeNull();
  });
});
