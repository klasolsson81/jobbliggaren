import { beforeEach, describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svAdmin from "../../../../../messages/sv/admin.json";
import { emailAllowanceFixture } from "@/test/fixtures/admin-overview";
import type { EmailAllowanceRead, EmailDeliveryRead, EmailFailuresRead } from "@/lib/api/admin-email";
import type { EmailDeliveryPeriod } from "@/lib/dto/admin-email";

const { allowance, delivery, failures } = vi.hoisted(() => ({
  allowance: vi.fn<() => Promise<EmailAllowanceRead>>(),
  delivery: vi.fn<(period: EmailDeliveryPeriod) => Promise<EmailDeliveryRead>>(),
  failures: vi.fn<(period: EmailDeliveryPeriod) => Promise<EmailFailuresRead>>(),
}));
vi.mock("@/lib/api/admin-email", () => ({
  loadEmailAllowance: allowance,
  loadEmailDelivery: delivery,
  loadEmailFailures: failures,
}));
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({ locale: "sv", messages: { admin: svAdmin }, namespace: namespace as "admin" }),
}));
import AdminEmailPage from "./page";
import AdminEmailLoading from "./loading";

async function renderPage(period?: string | string[]) {
  render(await AdminEmailPage({ searchParams: Promise.resolve(period === undefined ? {} : { period }) }));
}

/** What a screen reader reads for an unknown value: the dash is hidden from it, and its words are not. */
const UNKNOWN = "–Uppgift saknas";
const ASKED_AT = "2026-10-19T18:59:46+00:00";
/** The fixture's month as the allowance shows it, with the non-breaking spaces the Swedish formats use. */
const ALLOWANCE_VALUES = ["1 301", "300", "3,49 kr"];

const allowanceRegion = () => screen.getByRole("region", { name: /^Utskick (i |denna)/ });
const deliveryRegion = () => screen.getByRole("region", { name: "Leverans" });
const deliveryValues = () => within(deliveryRegion()).getAllByRole("definition").map((value) => value.textContent);

beforeEach(() => {
  allowance.mockReset();
  delivery.mockReset();
  failures.mockReset();
  allowance.mockResolvedValue({ kind: "ok", data: emailAllowanceFixture() });
  delivery.mockImplementation(async (period) => ({
    kind: "ok", data: { period, status: "Observed", reason: null, observedAt: ASKED_AT, sent: 163, failed: 3 },
  }));
  failures.mockImplementation(async (period) => ({
    kind: "ok",
    data: {
      period, status: "Observed", reason: null, observedAt: ASKED_AT, limit: 10,
      failures: [{ createdAt: "2026-10-19T14:32:51+00:00", recipient: "k•••@example.com", reason: "HardBounce" }],
    },
  }));
});

describe("/admin/e-post — the month against the free allowance first, Leverans after (#1981)", () => {
  it("is headed E-postleverans with its lede and no aside, then the allowance, then Leverans", async () => {
    await renderPage();

    expect(screen.getByRole("heading", { level: 1, name: "E-postleverans" })).toBeInTheDocument();
    expect(screen.getByText("Utskick mot gratiskvoten, leverans och misslyckade utskick.")).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { level: 2 }).map((heading) => heading.textContent)).toEqual([
      "Utskick i oktober 2026",
      "Leverans",
    ]);
    expect(screen.getByRole("heading", { level: 3, name: "Senaste misslyckade utskick" })).toHaveClass("jp-adminh3");
    // The h1's row carries nothing beside it: the period links are Leverans's.
    expect(document.querySelector(".jp-adminhead__aside")).toBeNull();
    expect(within(deliveryRegion()).getByRole("navigation", { name: "Period" })).toBeInTheDocument();
  });

  it("shows the allowance as three summary values, never as a card, a meter or a percentage", async () => {
    await renderPage();

    const region = allowanceRegion();
    expect(within(region).getAllByRole("term").map((term) => term.textContent)).toEqual([
      "Skickade",
      "Ingår gratis",
      "Beräknad kostnad inkl. moms",
    ]);
    expect(within(region).getAllByRole("definition").map((value) => value.textContent)).toEqual(ALLOWANCE_VALUES);
    expect(region.querySelector(".jp-admincard, .jp-adminkpi__value, progress, meter, [role='progressbar']")).toBeNull();
    expect(document.body.textContent ?? "").not.toMatch(/\d\s?%/);
  });
});

describe("/admin/e-post's period (#1981 PR 2)", () => {
  it("reads 7 dygn without a period, asks both reads for it and marks it", async () => {
    await renderPage();

    expect(delivery).toHaveBeenCalledWith("d7");
    expect(failures).toHaveBeenCalledWith("d7");
    expect(screen.getByRole("link", { name: "7 dygn" })).toHaveAttribute("aria-current", "true");
  });

  it.each(["h24", "d3", "d7"] as const)("reads ?period=%s, asks both reads for it and marks it alone", async (period) => {
    await renderPage(period);

    expect(delivery.mock.calls).toEqual([[period]]);
    expect(failures.mock.calls).toEqual([[period]]);
    const current = screen.getByRole("navigation", { name: "Period" }).querySelectorAll("[aria-current='true']");
    expect([...current].map((link) => link.getAttribute("href"))).toEqual([`/admin/e-post?period=${period}`]);
  });

  it.each([["an unknown value", "d30"], ["another case", "H24"], ["an empty value", ""]])(
    "falls back to 7 dygn for %s, and never sends it on",
    async (_name, value) => {
      await renderPage(value);

      expect(delivery).toHaveBeenCalledWith("d7");
      expect(failures).toHaveBeenCalledWith("d7");
    },
  );

  it("reads a repeated parameter by its first value", async () => {
    await renderPage(["h24", "d7"]);
    expect(delivery).toHaveBeenCalledWith("h24");
  });

  it("shows the period's counts and its failures", async () => {
    await renderPage("d3");

    expect(deliveryValues()).toEqual(["163", "3"]);
    expect(screen.getByRole("table", { name: "Misslyckade utskick, senaste 3 dygn" })).toHaveTextContent(
      "2026-10-19 16:32Avvisat av mottagarens serverk•••@example.com",
    );
  });
});

describe("/admin/e-post's regions fail on their own (ADR 0150 D2)", () => {
  it("keeps a failed allowance read in its own region and renders Leverans", async () => {
    allowance.mockResolvedValue({ kind: "error" });
    await renderPage();

    expect(within(allowanceRegion()).getAllByRole("definition").map((value) => value.textContent)).toEqual([UNKNOWN, UNKNOWN, UNKNOWN]);
    expect(within(allowanceRegion()).getByText("Uppgifterna kunde inte hämtas. Försök igen om en stund.")).not.toHaveAttribute("role");
    expect(screen.getByRole("alert")).toHaveTextContent("Utskick mot gratiskvoten: Uppgifterna kunde inte hämtas. Försök igen om en stund.");
    expect(deliveryValues()).toEqual(["163", "3"]);
  });

  it("keeps a failed delivery read in Leverans and renders the allowance", async () => {
    delivery.mockResolvedValue({ kind: "error" });
    failures.mockResolvedValue({ kind: "error" });
    await renderPage();

    expect(within(allowanceRegion()).getAllByRole("definition").map((value) => value.textContent)).toEqual(ALLOWANCE_VALUES);
    expect(deliveryValues()).toEqual([UNKNOWN, UNKNOWN]);
    expect(screen.getByRole("alert")).toHaveTextContent("Leverans: Uppgifterna kunde inte hämtas. Försök igen om en stund.");
    expect(within(deliveryRegion()).queryByText("Inga misslyckade utskick under perioden.")).toBeNull();
  });

  it.each([
    ["unauthorized", "Du är inte inloggad längre. Logga in och försök igen."],
    ["forbidden", "Din session saknar Admin-rollen."],
  ] as const)("says why a %s read failed, in each region, since trying again does not help it", async (kind, line) => {
    allowance.mockResolvedValue({ kind });
    delivery.mockResolvedValue({ kind });
    failures.mockResolvedValue({ kind });
    await renderPage();

    expect(within(allowanceRegion()).getByText(line)).toBeInTheDocument();
    // The list's cause is the summary's, which says it once.
    expect(within(deliveryRegion()).getAllByText(line)).toHaveLength(1);
  });

  it("says a missing read key in the allowance and in Leverans, never that the environment does not count", async () => {
    const missing = "Läsnyckeln är inte installerad. Se runbooken för admin-e-post.";
    allowance.mockResolvedValue({
      kind: "ok",
      data: {
        ...emailAllowanceFixture(),
        usage: { status: "ReadKeyMissing", reason: null, observedAt: null, month: null, sent: null, billable: null,
          estimatedExtraCostOre: null, aboveHalfOfSendLimit: null, webhooksCount: null },
        plan: { status: "ReadKeyMissing", reason: null, active: null, matches: null },
      },
    });
    delivery.mockResolvedValue({ kind: "ok", data: { period: "d7", status: "ReadKeyMissing", reason: null, observedAt: null, sent: null, failed: null } });
    failures.mockResolvedValue({ kind: "ok", data: { period: "d7", status: "ReadKeyMissing", reason: null, observedAt: null, failures: null, limit: 10 } });
    await renderPage();

    expect(within(allowanceRegion()).getByText(missing)).toBeInTheDocument();
    expect(within(deliveryRegion()).getAllByText(missing)).toHaveLength(1);
    expect(document.body.textContent).not.toContain("Räknas inte i den här miljön.");
  });

  it("names the provider only where an outcome from it is shown, and never SMTP", async () => {
    await renderPage();
    expect(document.body.textContent ?? "").not.toMatch(/smtp|strato/i);
  });
});

describe("/admin/e-post while its reads are pending", () => {
  it("shows the allowance, the summary and the list loading, the per-type table Kommer snart, and no period chosen", () => {
    render(<AdminEmailLoading />);

    expect(within(allowanceRegion()).getAllByRole("definition").map((value) => value.textContent)).toEqual([UNKNOWN, UNKNOWN, UNKNOWN]);
    expect(deliveryValues()).toEqual([UNKNOWN, UNKNOWN]);
    expect(within(deliveryRegion()).getAllByText("Hämtar uppgifter…")).toHaveLength(2);
    expect(within(screen.getByRole("table", { name: "Utskick per mejltyp" })).getByText("Kommer snart")).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Misslyckade utskick" })).not.toHaveTextContent("Kommer snart");
    expect(screen.getByRole("navigation", { name: "Period" }).querySelector("[aria-current]")).toBeNull();
    expect(screen.getAllByRole("status").map((status) => status.textContent)).toEqual([
      "Utskick mot gratiskvoten: Hämtar uppgifter…",
      "Leverans: Hämtar uppgifter…",
    ]);
  });
});
