import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { emailAllowanceFixture } from "@/test/fixtures/admin-overview";
import type { EmailAllowanceDto, EmailAllowanceFailure } from "@/lib/dto/admin-email";
import { AdminEmailAllowance, type AdminEmailAllowanceRegion } from "./admin-email-allowance";

/** What a screen reader reads for an unknown value: the dash is hidden from it, and its words are not. */
const UNKNOWN = "–Uppgift saknas";
const NBSP = " ";

const NO_USAGE = {
  observedAt: null, month: null, sent: null, billable: null, estimatedExtraCostOre: null,
  aboveHalfOfSendLimit: null, webhooksCount: null,
} as const;

const base = emailAllowanceFixture();
if (base.usage.status !== "Observed") throw new Error("The fixture is an observed month");
const observed = base.usage;

function withUsage(patch: Partial<typeof observed>): EmailAllowanceDto {
  return { ...base, usage: { ...observed, ...patch } };
}
function withPlan(plan: EmailAllowanceDto["plan"]): EmailAllowanceDto {
  return { ...base, usage: { ...observed, billable: null, estimatedExtraCostOre: null }, plan };
}
function failedUsage(reason: EmailAllowanceFailure, plan: EmailAllowanceDto["plan"] = base.plan): EmailAllowanceDto {
  return { ...base, usage: { status: "Failed", reason, ...NO_USAGE }, plan };
}
const NOT_CONFIGURED: EmailAllowanceDto = {
  ...base,
  usage: { status: "NotObserved", reason: "NotConfigured", ...NO_USAGE },
  plan: { status: "NotObserved", reason: "NotConfigured", active: null, matches: null },
};

function show(region: AdminEmailAllowanceRegion, failed?: string) {
  render(<AdminEmailAllowance region={region} failed={failed} />);
  return screen.getByRole("region", { name: /^Utskick/ });
}
const loaded = (data: EmailAllowanceDto) => show({ kind: "loaded", data });
const values = (region: HTMLElement) => within(region).getAllByRole("definition").map((value) => value.textContent);
const stateLines = (region: HTMLElement) =>
  Array.from(region.querySelectorAll(".jp-adminsoon"), (line) => line.textContent);
const noteLines = (region: HTMLElement) =>
  Array.from(region.querySelectorAll(".jp-adminmailsum__note p"), (line) => line.textContent);

const SOURCE = "Räknat av Scaleway för projektet. Uppgift från 2026-10-08 11:59.";
const PRICE = `Pris utöver gratiskvoten: 0,25${NBSP}€ per 1${NBSP}000 mejl före moms, enligt Scaleways prislista läst 2026-10-01.`;
const CONVERSION = "Kostnaden är omräknad till kronor med 11,145 kr per euro, ECB:s referenskurs 2026-10-16, och inkluderar svensk moms.";
const LIMIT = `Sändgränsen är 10${NBSP}000 mejl i månaden.`;

describe("AdminEmailAllowance — an observed month (#1981 PR 1, ADR 0150 Amendment 2026-10-19)", () => {
  it("heads the region with the month from the DTO and shows the three values in the summary row", () => {
    const region = loaded(base);

    expect(within(region).getByRole("heading", { level: 2 })).toHaveTextContent("Utskick i oktober 2026");
    expect(within(region).getAllByRole("term").map((term) => term.textContent)).toEqual([
      "Skickade",
      "Ingår gratis",
      "Beräknad kostnad inkl. moms",
    ]);
    expect(values(region)).toEqual([`1${NBSP}301`, "300", `3,49${NBSP}kr`]);
    expect(region.querySelector("dl")).toHaveClass("jp-adminmailsum");
  });

  it("notes where the figures come from, the price and the send limit, and says nothing else", () => {
    const region = loaded(base);

    expect(noteLines(region)).toEqual([SOURCE, PRICE, CONVERSION, LIMIT]);
    expect(within(region).getByText("2026-10-08 11:59").tagName).toBe("TIME");
    expect(within(region).getByText("2026-10-08 11:59")).toHaveAttribute("datetime", observed.observedAt);
    expect(stateLines(region)).toEqual([]);
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it.each([
    ["within the free allowance", { sent: 142, billable: 0, estimatedExtraCostOre: 0 }, ["142", "300", `0,00${NBSP}kr`]],
    ["exactly the free allowance", { sent: 300, billable: 0, estimatedExtraCostOre: 0 }, ["300", "300", `0,00${NBSP}kr`]],
    ["nothing sent", { sent: 0, billable: 0, estimatedExtraCostOre: 0 }, ["0", "300", `0,00${NBSP}kr`]],
  ] as const)("shows a computed zero cost as a value %s", (_name, patch, expected) => {
    expect(values(loaded(withUsage(patch)))).toEqual(expected);
  });

  it("keeps a cost above zero in neutral ink, with no card, meter or percentage", () => {
    const region = loaded(base);

    expect(region.querySelector(".jp-admin-danger, .jp-admin-warning")).toBeNull();
    expect(region.querySelector(".jp-admincard, .jp-adminkpi__value, .jp-adminmeter, progress, meter, [role='progressbar']")).toBeNull();
    expect(region.textContent ?? "").not.toMatch(/\d\s?%/);
  });

  it("names no month the DTO does not name: the browser's clock never decides it", () => {
    const region = loaded(withUsage({ month: null }));

    expect(within(region).getByRole("heading", { level: 2 })).toHaveTextContent("Utskick denna månad");
  });

  it("says so in words when more than half of the send limit is used, and the plain limit sentence gives way", () => {
    const region = loaded(withUsage({ sent: 5200, billable: 4900, estimatedExtraCostOre: 1707, aboveHalfOfSendLimit: true }));

    expect(stateLines(region)).toEqual([`Över hälften av sändgränsen på 10${NBSP}000 mejl i månaden är använd.`]);
    expect(noteLines(region)).toEqual([SOURCE, PRICE, CONVERSION]);
  });

  it.each([
    [1, "1 webhook är registrerad hos Scaleway. Ingen ska finnas, se runbooken för admin-e-post."],
    [2, "2 webhooks är registrerade hos Scaleway. Ingen ska finnas, se runbooken för admin-e-post."],
    [null, "Antalet webhooks kunde inte läsas."],
  ] as const)("says what the webhook count %s means", (count, line) => {
    expect(stateLines(loaded(withUsage({ webhooksCount: count })))).toEqual([line]);
  });

  it("says nothing of webhooks when none is registered", () => {
    expect(loaded(base).textContent).not.toMatch(/webhook/i);
  });
});

describe("AdminEmailAllowance — the plan in force", () => {
  it.each([
    [
      "another plan",
      { status: "Observed", reason: null, active: "Scale", matches: false },
      "Scaleway anger abonnemanget Scale, men villkoren gäller Essential. Kostnaden beräknas inte.",
    ],
    [
      "a plan this code does not know",
      { status: "Observed", reason: null, active: "Unknown", matches: false },
      "Abonnemanget hos Scaleway kunde inte bestämmas. Kostnaden beräknas inte.",
    ],
    [
      "a plan read that failed",
      { status: "Failed", reason: "ProviderError", active: null, matches: null },
      "Abonnemanget kunde inte läsas från Scaleway. Kostnaden beräknas inte.",
    ],
  ] as const)("keeps the count and leaves the allowance and the cost unknown under %s, with the cause", (_name, plan, line) => {
    const region = loaded(withPlan(plan));

    expect(values(region)).toEqual([`1${NBSP}301`, UNKNOWN, UNKNOWN]);
    expect(stateLines(region)).toEqual([line]);
    expect(noteLines(region)).toEqual([SOURCE, PRICE, LIMIT]);
  });

  it("announces a failed plan read once, as the source and its state, and keeps the region's own line quiet", () => {
    const region = loaded(withPlan({ status: "Failed", reason: "Forbidden", active: null, matches: null }));

    expect(screen.getByRole("alert")).toHaveTextContent(
      "Utskick mot gratiskvoten: Abonnemanget kunde inte läsas från Scaleway. Kostnaden beräknas inte.",
    );
    expect(region.querySelector(".jp-adminsoon")).not.toHaveAttribute("role");
  });

  it("does not announce a plan that merely differs: it is an observation, not a failure", () => {
    loaded(withPlan({ status: "Observed", reason: null, active: "Scale", matches: false }));

    expect(screen.queryByRole("alert")).toBeNull();
  });
});

describe("AdminEmailAllowance — no provider in this deployment (notConfigured)", () => {
  it("says it is not counted here in one line, with every value an en-dash and nothing else", () => {
    const region = loaded(NOT_CONFIGURED);

    expect(values(region)).toEqual([UNKNOWN, UNKNOWN, UNKNOWN]);
    expect(stateLines(region)).toEqual(["Räknas inte i den här miljön."]);
    expect(region.querySelector(".jp-adminmailsum__note")).toBeNull();
    expect(region.textContent).not.toMatch(/Scaleway|Kommer snart|rapporterat|webhook|Sändgränsen|Pris/);
    expect(within(region).getByRole("heading", { level: 2 })).toHaveTextContent("Utskick denna månad");
    expect(screen.queryByRole("alert")).toBeNull();
  });
});

describe("AdminEmailAllowance — the provider did not answer the count", () => {
  it.each([
    ["Timeout", "Scaleway svarade inte. Försök igen om en stund."],
    ["Unreachable", "Scaleway svarade inte. Försök igen om en stund."],
    ["ProviderError", "Scaleway svarade inte. Försök igen om en stund."],
    ["RateLimited", "Scaleway begränsar läsningarna just nu. Försök igen om en stund."],
    [
      "Unauthorized",
      "Scaleway nekade nyckeln, och då stoppas troligen även utskicken. Kontrollera nyckelns utgångsdatum enligt runbooken för admin-e-post.",
    ],
    ["Forbidden", "Scaleway nekade läsningen. Kontrollera nyckelns behörighet enligt runbooken för admin-e-post."],
    ["Malformed", "Scaleways svar kunde inte läsas. Se runbooken för admin-e-post."],
  ] as const)("words %s by its cause and remedy, and keeps the price, since the terms are ours", (reason, line) => {
    const region = loaded(failedUsage(reason));

    expect(values(region)).toEqual([UNKNOWN, "300", UNKNOWN]);
    expect(stateLines(region)).toEqual([line]);
    expect(noteLines(region)).toEqual([PRICE]);
    expect(screen.getByRole("alert")).toHaveTextContent(`Utskick mot gratiskvoten: ${line}`);
    expect(region.querySelector(".jp-adminsoon")).not.toHaveAttribute("role");
    expect(region.textContent).not.toContain(`0,00${NBSP}kr`);
  });

  it("says both causes when the plan read failed too, and announces them in one message", () => {
    const region = loaded(failedUsage("RateLimited", { status: "Failed", reason: "RateLimited", active: null, matches: null }));

    expect(values(region)).toEqual([UNKNOWN, UNKNOWN, UNKNOWN]);
    expect(stateLines(region)).toEqual([
      "Scaleway begränsar läsningarna just nu. Försök igen om en stund.",
      "Abonnemanget kunde inte läsas från Scaleway. Kostnaden beräknas inte.",
    ]);
    expect(screen.getAllByRole("alert")).toHaveLength(1);
  });
});

describe("AdminEmailAllowance — our own read", () => {
  it("shows a failed read as the region's failed state: en-dashes, the shared line and one announcement", () => {
    const region = show({ kind: "failed" });

    expect(values(region)).toEqual([UNKNOWN, UNKNOWN, UNKNOWN]);
    expect(stateLines(region)).toEqual(["Uppgifterna kunde inte hämtas. Försök igen om en stund."]);
    expect(region.querySelector(".jp-adminmailsum__note")).toBeNull();
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Utskick mot gratiskvoten: Uppgifterna kunde inte hämtas. Försök igen om en stund.",
    );
    expect(within(region).getByRole("heading", { level: 2 })).toHaveTextContent("Utskick denna månad");
  });

  it("takes the caller's line for a refusal", () => {
    const region = show({ kind: "failed" }, "Din session saknar Admin-rollen.");

    expect(stateLines(region)).toEqual(["Din session saknar Admin-rollen."]);
  });

  it("shows a pending read with en-dashes and announces it as a status, never as zero", () => {
    const region = show({ kind: "loading" });

    expect(values(region)).toEqual([UNKNOWN, UNKNOWN, UNKNOWN]);
    expect(screen.getByRole("status")).toHaveTextContent("Utskick mot gratiskvoten: Hämtar uppgifter…");
    expect(screen.queryByRole("alert")).toBeNull();
  });
});
