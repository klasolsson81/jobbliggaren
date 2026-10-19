import { beforeEach, describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svAdmin from "../../../../../messages/sv/admin.json";
import { emailAllowanceFixture } from "@/test/fixtures/admin-overview";
import type { EmailAllowanceRead } from "@/lib/api/admin-email";

const { load } = vi.hoisted(() => ({ load: vi.fn<() => Promise<EmailAllowanceRead>>() }));
vi.mock("@/lib/api/admin-email", () => ({ loadEmailAllowance: load }));
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({ locale: "sv", messages: { admin: svAdmin }, namespace: namespace as "admin" }),
}));
import AdminEmailPage from "./page";

async function renderPage() {
  render(await AdminEmailPage());
}

/** What a screen reader reads for an unknown value: the dash is hidden from it, and its words are not. */
const UNKNOWN = "–Uppgift saknas";

const allowanceRegion = () => screen.getByRole("region", { name: /^Utskick (i |denna)/ });
const deliveryRegion = () => screen.getByRole("region", { name: "Leverans" });

beforeEach(() => {
  load.mockReset();
  load.mockResolvedValue({ kind: "ok", data: emailAllowanceFixture() });
});

describe("/admin/e-post — the month against the free allowance first, delivery after (#1981 PR 1)", () => {
  it("is headed E-postleverans with its lede and no aside, then the allowance, then Leverans", async () => {
    await renderPage();

    expect(screen.getByRole("heading", { level: 1, name: "E-postleverans" })).toBeInTheDocument();
    expect(screen.getByText("Utskick mot gratiskvoten och per mejltyp.")).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { level: 2 }).map((heading) => heading.textContent)).toEqual([
      "Utskick i oktober 2026",
      "Leverans",
    ]);
    expect(screen.getByRole("heading", { level: 3, name: "Senaste misslyckade utskick" })).toHaveClass("jp-adminh3");
    // The h1's row carries nothing beside it: the period group is the delivery section's.
    expect(document.querySelector(".jp-adminhead__aside")).toBeNull();
    expect(within(deliveryRegion()).getByRole("radiogroup", { name: "Period" })).toBeInTheDocument();
  });

  it("shows the allowance as three summary values, never as a card, a meter or a percentage", async () => {
    await renderPage();

    const allowance = allowanceRegion();
    expect(within(allowance).getAllByRole("term").map((term) => term.textContent)).toEqual([
      "Skickade",
      "Ingår gratis",
      "Beräknad kostnad inkl. moms",
    ]);
    expect(within(allowance).getAllByRole("definition").map((value) => value.textContent)).toEqual([
      "1 301",
      "300",
      "3,49 kr",
    ]);
    expect(allowance.querySelector(".jp-admincard, .jp-adminkpi__value, progress, meter, [role='progressbar']")).toBeNull();
    expect(document.body.textContent ?? "").not.toMatch(/\d\s?%/);
  });

  it("keeps a failed allowance read in its own region and renders the rest of the page", async () => {
    load.mockResolvedValue({ kind: "error" });
    await renderPage();

    const allowance = allowanceRegion();
    expect(within(allowance).getAllByRole("definition").map((value) => value.textContent)).toEqual([UNKNOWN, UNKNOWN, UNKNOWN]);
    expect(within(allowance).getByText("Uppgifterna kunde inte hämtas. Försök igen om en stund.")).not.toHaveAttribute("role");
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Utskick mot gratiskvoten: Uppgifterna kunde inte hämtas. Försök igen om en stund.",
    );
    expect(screen.getByRole("heading", { level: 2, name: "Utskick denna månad" })).toBeInTheDocument();
    expect(within(deliveryRegion()).getByRole("table", { name: "Utskick per mejltyp" })).toBeInTheDocument();
  });

  it.each([
    ["unauthorized", "Du är inte inloggad längre. Logga in och försök igen."],
    ["forbidden", "Din session saknar Admin-rollen."],
  ] as const)("says why a %s read failed, since trying again does not help it", async (kind, line) => {
    load.mockResolvedValue({ kind });
    await renderPage();

    expect(within(allowanceRegion()).getByText(line)).toBeInTheDocument();
  });

  it("keeps the delivery section unbuilt: its period group is disabled and described by Kommer snart", async () => {
    await renderPage();

    const group = within(deliveryRegion()).getByRole("radiogroup", { name: "Period" });
    for (const option of within(group).getAllByRole("radio")) expect(option).toBeDisabled();
    const describedBy = group.getAttribute("aria-describedby") ?? "";
    expect(document.getElementById(describedBy)?.textContent).toBe("Kommer snart");
    expect(within(deliveryRegion()).getAllByRole("definition").map((value) => value.textContent)).toEqual([
      UNKNOWN,
      UNKNOWN,
      UNKNOWN,
    ]);
  });

  it("keeps the per-type table with one Kommer snart row and the failures list as one line", async () => {
    await renderPage();

    const table = screen.getByRole("table", { name: "Utskick per mejltyp" });
    expect(within(table).getAllByRole("columnheader").map((th) => th.textContent)).toEqual([
      "Mejltyp",
      "Skickade",
      "Fel",
      "Senaste fel",
    ]);
    const bodyRows = within(table).getAllByRole("row").slice(1);
    expect(bodyRows).toHaveLength(1);
    expect(bodyRows[0]?.textContent).toBe("Kommer snart");

    const failures = screen.getByRole("region", { name: "Senaste misslyckade utskick" });
    expect(within(failures).getByText("Kommer snart")).toBeInTheDocument();
  });

  it("names the provider only where an outcome from it is shown, and never SMTP", async () => {
    await renderPage();

    expect(within(deliveryRegion()).queryByText(/scaleway/i)).toBeNull();
    expect(document.body.textContent ?? "").not.toMatch(/smtp|strato/i);
  });
});
