import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import {
  EMAIL_KINDS,
  type EmailDeliveryDto,
  type EmailFailureReason,
  type EmailFailuresDto,
  type EmailKindsDto,
} from "@/lib/dto/admin-email";
import type { AdminEmailPeriod, AdminEmailRead } from "@/lib/admin/view-models";
import { AdminEmailDelivery } from "./admin-email-delivery";

/** What a screen reader reads for an unknown value: the dash is hidden from it, and its words are not. */
const UNKNOWN = "–Uppgift saknas";
const ASKED_AT = "2026-10-19T18:59:46+00:00";

const NOT_OBSERVABLE =
  "Scaleways leveransstatistik visar inga utskick de senaste 32 dygnen, så siffrorna kan inte bedömas. Kontrollera läsnyckelns behörighet enligt runbooken för admin-e-post.";
const READ_KEY_MISSING = "Läsnyckeln är inte installerad. Se runbooken för admin-e-post.";
const LIST_UNKNOWN = "Uppgift saknas";
const NOT_CONFIGURED = "Räknas inte i den här miljön.";
const EMPTY = "Inga misslyckade utskick under perioden.";

const observed = (sent: number, failed: number, period: AdminEmailPeriod = "h24"): EmailDeliveryDto =>
  ({ period, status: "Observed", reason: null, observedAt: ASKED_AT, sent, failed });
const listed = (failures: Extract<EmailFailuresDto, { status: "Observed" }>["failures"], period: AdminEmailPeriod = "h24"): EmailFailuresDto =>
  ({ period, status: "Observed", reason: null, observedAt: ASKED_AT, failures, limit: 10 });
const row = (createdAt: string, recipient: string, reason: EmailFailureReason) => ({ createdAt, recipient, reason });

/** Our own count per kind (#1981 PR 3), observed: every kind handed over, none failed. */
const counted: EmailKindsDto = {
  period: "h24", status: "Observed", from: "2026-10-18", coverage: "Complete", coveredFrom: null,
  kinds: EMAIL_KINDS.map((kind) => ({ kind, accepted: 4, failed: 0, latestFailure: null })),
};

type Unanswered = Exclude<EmailDeliveryDto["status"], "Observed" | "NotObserved" | "Failed">;
const unansweredSummary = (status: Unanswered): EmailDeliveryDto =>
  ({ period: "h24", status, reason: null, observedAt: null, sent: null, failed: null });
const unansweredList = (status: Unanswered): EmailFailuresDto =>
  ({ period: "h24", status, reason: null, observedAt: null, failures: null, limit: 10 });

function show({
  summary = { kind: "loaded", data: observed(57, 2) },
  failures = { kind: "loaded", data: listed([row("2026-10-19T14:32:51+00:00", "k•••@example.com", "MailboxNotFound")]) },
  kinds = { kind: "loaded", data: counted },
  period = "h24",
}: {
  readonly summary?: AdminEmailRead<EmailDeliveryDto>;
  readonly failures?: AdminEmailRead<EmailFailuresDto>;
  readonly kinds?: AdminEmailRead<EmailKindsDto>;
  readonly period?: AdminEmailPeriod | null;
} = {}) {
  render(<AdminEmailDelivery pagePath="/admin/e-post" period={period} summary={summary} failures={failures} kinds={kinds} />);
  return screen.getByRole("region", { name: "Leverans" });
}

const values = (section: HTMLElement) => within(section).getAllByRole("definition").map((value) => value.textContent);
const failuresTable = () => screen.getByRole("table", { name: /^Misslyckade utskick/ });
const failureRows = () => within(failuresTable()).getAllByRole("row").slice(1);
const announcements = () => [...screen.queryAllByRole("alert"), ...screen.queryAllByRole("status")];

describe("Leverans's structure (#1981 PR 2)", () => {
  it("is a section headed Leverans, with the period links under the heading and the summary right after them", () => {
    const section = show();

    const heading = within(section).getByRole("heading", { level: 2, name: "Leverans" });
    const head = heading.parentElement;
    expect(head).toHaveClass("jp-adminsection__head");
    const nav = within(head!).getByRole("navigation", { name: "Period" });
    expect(nav).toHaveClass("jp-subnav");
    expect(head?.nextElementSibling?.firstElementChild?.tagName).toBe("DL");
    expect(within(section).getByRole("heading", { level: 3, name: "Senaste misslyckade utskick" })).toHaveClass("jp-adminh3");
    expect(screen.queryByRole("heading", { level: 1 })).toBeNull();
    expect(within(section).queryByRole("radiogroup")).toBeNull();
  });

  it("offers each period as a link to its own URL, the current one marked as current", () => {
    show({ period: "d3" });

    const links = within(screen.getByRole("navigation", { name: "Period" })).getAllByRole("link");
    expect(links.map((link) => [link.textContent, link.getAttribute("href"), link.getAttribute("aria-current")])).toEqual([
      ["24 tim", "/admin/e-post?period=h24", null],
      ["3 dygn", "/admin/e-post?period=d3", "true"],
      ["7 dygn", "/admin/e-post?period=d7", null],
    ]);
    expect(links.map((link) => link.getAttribute("data-active"))).toEqual(["false", "true", "false"]);
  });

  it("marks no period while the route loads, and names the failures table without one", () => {
    show({ period: null, summary: { kind: "loading" }, failures: { kind: "loading" } });

    expect(screen.getByRole("navigation", { name: "Period" }).querySelector("[aria-current]")).toBeNull();
    expect(failuresTable()).toHaveAccessibleName("Misslyckade utskick");
  });

  it("ends with our own count per mail kind, a sub-section of its own after the failures, and says Kommer snart nowhere", () => {
    const section = show();

    const subsections = [...section.querySelectorAll(":scope > section")];
    expect(subsections.map((sub) => sub.querySelector("h3")?.textContent)).toEqual([
      "Senaste misslyckade utskick",
      "Utskick per mejltyp",
    ]);
    expect(section.lastElementChild).toBe(subsections[1]);
    expect(within(subsections[1] as HTMLElement).getAllByRole("rowheader")).toHaveLength(7);
    expect(section.textContent).not.toContain("Kommer snart");
  });
});

describe("Leverans's summary", () => {
  it("shows Skickade and Misslyckade only, a failed count above zero in the danger colour", () => {
    const section = show();

    expect(within(section).getAllByRole("term").map((term) => term.textContent)).toEqual(["Skickade", "Misslyckade"]);
    expect(values(section)).toEqual(["57", "2"]);
    expect(within(section).getByText("2")).toHaveClass("jp-admin-danger");
  });

  it("shows an observed zero as a value, in neutral ink", () => {
    const section = show({ summary: { kind: "loaded", data: observed(0, 0) }, failures: { kind: "loaded", data: listed([]) } });

    expect(values(section)).toEqual(["0", "0"]);
    expect(section.querySelector(".jp-admin-danger")).toBeNull();
  });

  it("says where the figures come from and when we asked, to the minute", () => {
    const section = show();

    const note = within(section).getByText(/^Räknat av Scaleway för projektet\./);
    expect(note).toHaveTextContent("Räknat av Scaleway för projektet. Hämtat 2026-10-19 20:59.");
    expect(note.querySelector("time")).toHaveAttribute("dateTime", ASKED_AT);
    expect(section.textContent).not.toMatch(/Uppgift från|till och med|fram till/);
  });

  it("announces nothing while both reads are observed", () => {
    show();
    expect(announcements()).toEqual([]);
  });
});

describe("Leverans's failures table", () => {
  it("lists time, reason and masked recipient, newest first, the time to the minute in neutral ink", () => {
    show({
      failures: {
        kind: "loaded",
        data: listed([
          row("2026-10-19T14:32:51+00:00", "k•••@example.com", "MailboxNotFound"),
          row("2026-10-19T09:05:00+00:00", "•••", "SoftBounce"),
        ]),
      },
    });

    const table = failuresTable();
    expect(table).toHaveAccessibleName("Misslyckade utskick, senaste 24 tim");
    expect(within(table).getAllByRole("columnheader").map((th) => [th.textContent, th.getAttribute("scope")])).toEqual([
      ["Tid", "col"],
      ["Orsak", "col"],
      ["Mottagare (maskerad)", "col"],
    ]);
    expect(failureRows().map((tableRow) => within(tableRow).getAllByRole("cell").map((cell) => cell.textContent))).toEqual([
      ["2026-10-19 16:32", "Adressen finns inte", "k•••@example.com"],
      ["2026-10-19 11:05", "Tillfälligt avvisat av mottagarens server", "•••"],
    ]);
    expect(table.querySelector("time")).toHaveAttribute("dateTime", "2026-10-19T14:32Z");
    expect(table.textContent).not.toMatch(/\d{2}:\d{2}:\d{2}/);
    expect(table.querySelector(".jp-pill, .jp-admin-danger, .jp-admin-warning")).toBeNull();
  });

  it.each([
    ["MailboxNotFound", "Adressen finns inte"],
    ["HardBounce", "Avvisat av mottagarens server"],
    ["MailboxFull", "Mottagarens inkorg är full"],
    ["SoftBounce", "Tillfälligt avvisat av mottagarens server"],
    ["Spam", "Bedömt som skräppost"],
    ["Blocklisted", "Adressen är spärrad hos Scaleway"],
    ["Other", "Annan eller okänd orsak"],
  ] as const)("words %s as %s", (reason, label) => {
    show({ failures: { kind: "loaded", data: listed([row("2026-10-19T14:32:51+00:00", "k•••@example.com", reason)]) } });
    expect(within(failureRows()[0]!).getAllByRole("cell")[1]).toHaveTextContent(label);
  });

  it("names each period in the table's caption", () => {
    show({ period: "d7", summary: { kind: "loaded", data: observed(1, 0, "d7") }, failures: { kind: "loaded", data: listed([], "d7") } });
    expect(failuresTable()).toHaveAccessibleName("Misslyckade utskick, senaste 7 dygn");
  });

  it("says there were no failed sends only after an observed read with no rows", () => {
    show({ failures: { kind: "loaded", data: listed([]) } });

    expect(failureRows().map((tableRow) => tableRow.textContent)).toEqual([EMPTY]);
    expect(failureRows()[0]?.querySelector("td")).toHaveClass("jp-admintable__soon");
  });

  it("says it shows only the latest when the read is at its limit, and not below it", () => {
    const ten = Array.from({ length: 10 }, (_, index) =>
      row(`2026-10-19T${String(14 - index).padStart(2, "0")}:00:00+00:00`, "k•••@example.com", "Other"));
    show({ failures: { kind: "loaded", data: listed(ten) } });
    expect(failureRows()).toHaveLength(10);
    expect(screen.getByText("Visar de 10 senaste.")).toHaveClass("jp-adminmailsum__note");
  });

  it("does not say so below the limit", () => {
    show();
    expect(screen.queryByText(/^Visar de/)).toBeNull();
  });
});

describe("Leverans's states (ADR 0150 D2): unknown is never zero and never empty", () => {
  it("shows a read key that sees no sends as unknown, says why once, and never says the list is empty", () => {
    const section = show({
      summary: { kind: "loaded", data: unansweredSummary("NotObservable") },
      failures: { kind: "loaded", data: unansweredList("NotObservable") },
    });

    expect(values(section)).toEqual([UNKNOWN, UNKNOWN]);
    expect(within(section).getByText(NOT_OBSERVABLE)).not.toHaveAttribute("role");
    expect(failureRows().map((tableRow) => tableRow.textContent)).toEqual(["Uppgift saknas"]);
    expect(section.textContent).not.toContain(EMPTY);
    expect(screen.getByRole("alert")).toHaveTextContent(`Leverans: ${NOT_OBSERVABLE}`);
    expect(screen.getByRole("alert").textContent).not.toContain("Uppgift saknas");
    expect(section.textContent).not.toMatch(/Räknat av Scaleway/);
  });

  it("keeps an observed count beside a list that cannot be judged, which is unknown and never empty", () => {
    const section = show({ failures: { kind: "loaded", data: unansweredList("NotObservable") } });

    expect(values(section)).toEqual(["57", "2"]);
    expect(failureRows().map((tableRow) => tableRow.textContent)).toEqual(["Uppgift saknas"]);
    expect(section.textContent).not.toContain(EMPTY);
    expect(screen.getByRole("alert")).toHaveTextContent("Leverans: Uppgift saknas");
  });

  it("says a missing read key is its own state, never that the environment does not count", () => {
    const section = show({
      summary: { kind: "loaded", data: unansweredSummary("ReadKeyMissing") },
      failures: { kind: "loaded", data: unansweredList("ReadKeyMissing") },
    });

    expect(values(section)).toEqual([UNKNOWN, UNKNOWN]);
    expect(within(section).getAllByText(READ_KEY_MISSING)).toHaveLength(1);
    expect(failureRows().map((tableRow) => tableRow.textContent)).toEqual([LIST_UNKNOWN]);
    expect(section.textContent).not.toContain(NOT_CONFIGURED);
    expect(screen.getByRole("alert")).toHaveTextContent(`Leverans: ${READ_KEY_MISSING}`);
  });

  it("says a deployment without Scaleway does not count, once, and announces nothing", () => {
    const section = show({
      summary: { kind: "loaded", data: { period: "h24", status: "NotObserved", reason: "NotConfigured", observedAt: null, sent: null, failed: null } },
      failures: { kind: "loaded", data: { period: "h24", status: "NotObserved", reason: "NotConfigured", observedAt: null, failures: null, limit: 10 } },
    });

    expect(values(section)).toEqual([UNKNOWN, UNKNOWN]);
    expect(within(section).getAllByText(NOT_CONFIGURED)).toHaveLength(1);
    expect(failureRows().map((tableRow) => tableRow.textContent)).toEqual([LIST_UNKNOWN]);
    expect(section.textContent).not.toContain(EMPTY);
    expect(announcements()).toEqual([]);
  });

  it.each([
    ["Timeout", "Scaleway svarade inte. Försök igen om en stund."],
    ["RateLimited", "Scaleway begränsar läsningarna just nu. Försök igen om en stund."],
    ["Unauthorized", "Scaleway nekade läsnyckeln. Kontrollera dess utgångsdatum enligt runbooken för admin-e-post."],
    ["Forbidden", "Scaleway nekade läsningen. Kontrollera läsnyckelns behörighet enligt runbooken för admin-e-post."],
    ["Malformed", "Scaleways svar kunde inte läsas. Se runbooken för admin-e-post."],
  ] as const)("words a read that failed with %s by its cause once, and announces it once", (reason, line) => {
    const section = show({
      summary: { kind: "loaded", data: { period: "h24", status: "Failed", reason, observedAt: null, sent: null, failed: null } },
      failures: { kind: "loaded", data: { period: "h24", status: "Failed", reason, observedAt: null, failures: null, limit: 10 } },
    });

    expect(values(section)).toEqual([UNKNOWN, UNKNOWN]);
    expect(within(section).getByText(line)).not.toHaveAttribute("role");
    expect(failureRows().map((tableRow) => tableRow.textContent)).toEqual([LIST_UNKNOWN]);
    expect(screen.getAllByRole("alert")).toHaveLength(1);
    expect(screen.getByRole("alert").textContent).toBe(`Leverans: ${line}`);
    expect(section.textContent).not.toContain(EMPTY);
  });

  it("fails each region on its own: a failed list beside an observed summary", () => {
    const section = show({ failures: { kind: "failed" } });

    expect(values(section)).toEqual(["57", "2"]);
    expect(failureRows().map((tableRow) => tableRow.textContent)).toEqual(["Uppgifterna kunde inte hämtas. Försök igen om en stund."]);
    expect(screen.getByRole("alert")).toHaveTextContent("Leverans: Uppgifterna kunde inte hämtas. Försök igen om en stund.");
  });

  it("says why our own read was refused, in place of the shared line, once", () => {
    const section = show({
      summary: { kind: "failed", line: "Din session saknar Admin-rollen." },
      failures: { kind: "failed", line: "Din session saknar Admin-rollen." },
    });

    expect(values(section)).toEqual([UNKNOWN, UNKNOWN]);
    expect(within(section).getAllByText("Din session saknar Admin-rollen.")).toHaveLength(1);
    expect(failureRows().map((tableRow) => tableRow.textContent)).toEqual([LIST_UNKNOWN]);
    expect(screen.getByRole("alert").textContent).toBe("Leverans: Din session saknar Admin-rollen.");
  });

  it("shows loading in the summary, the list and the per-kind table, announced once as a status", () => {
    const section = show({ period: null, summary: { kind: "loading" }, failures: { kind: "loading" }, kinds: { kind: "loading" } });

    expect(values(section)).toEqual([UNKNOWN, UNKNOWN]);
    expect(within(section).getAllByText("Hämtar uppgifter…")).toHaveLength(3);
    const kinds = screen.getByRole("region", { name: "Utskick per mejltyp" });
    expect(within(kinds).getByRole("heading", { level: 3, name: "Utskick per mejltyp" })).toBeInTheDocument();
    expect(within(kinds).getAllByText("Hämtar uppgifter…")).toHaveLength(1);
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.getAllByRole("status").map((status) => status.textContent)).toEqual(["Leverans: Hämtar uppgifter…"]);
    expect(section.textContent).not.toContain("Kommer snart");
  });
});

describe("Leverans's failures list beside the summary's line (#1981 PR 2, design-reviewer Minor 3)", () => {
  const TIMEOUT = "Scaleway svarade inte. Försök igen om en stund.";
  const OUR_READ = "Uppgifterna kunde inte hämtas. Försök igen om en stund.";
  const timedOutSummary: EmailDeliveryDto =
    { period: "h24", status: "Failed", reason: "Timeout", observedAt: null, sent: null, failed: null };
  const timedOutList: EmailFailuresDto =
    { period: "h24", status: "Failed", reason: "Timeout", observedAt: null, failures: null, limit: 10 };

  it("says only Uppgift saknas where its line would repeat the summary's, which says why once", () => {
    const section = show({
      summary: { kind: "loaded", data: timedOutSummary },
      failures: { kind: "loaded", data: timedOutList },
    });

    expect(section.querySelector("dl + p.jp-adminsoon")).toHaveTextContent(TIMEOUT);
    expect(within(section).getAllByText(TIMEOUT)).toHaveLength(1);
    expect(failureRows().map((tableRow) => tableRow.textContent)).toEqual([LIST_UNKNOWN]);
    expect(screen.getByRole("alert").textContent).toBe(`Leverans: ${TIMEOUT}`);
  });

  it.each<readonly [string, AdminEmailRead<EmailDeliveryDto>, AdminEmailRead<EmailFailuresDto>, string, string]>([
    ["a provider that did not answer the summary and our own failed list read",
      { kind: "loaded", data: timedOutSummary }, { kind: "failed" }, TIMEOUT, OUR_READ],
    ["our own failed summary read and a missing read key on the list",
      { kind: "failed" }, { kind: "loaded", data: unansweredList("ReadKeyMissing") }, OUR_READ, READ_KEY_MISSING],
  ])("shows each region's own line where the causes differ: %s", (_name, summary, failures, summaryText, listText) => {
    const section = show({ summary, failures });

    expect(section.querySelector("dl + p.jp-adminsoon")).toHaveTextContent(summaryText);
    expect(failureRows().map((tableRow) => tableRow.textContent)).toEqual([listText]);
  });
});

describe("Leverans's announcement with our own count per mail kind (#1981 PR 3)", () => {
  const OUR_READ = "Uppgifterna kunde inte hämtas. Försök igen om en stund.";
  const REFUSED = "Din session saknar Admin-rollen.";
  const KINDS_NOT_OBSERVABLE =
    "Jobbliggarens räkning visar inga utskick under perioden, fast Scaleway har skickat. Se runbooken för admin-e-post.";
  const announcements = () => [...screen.queryAllByRole("alert"), ...screen.queryAllByRole("status")].map((line) => line.textContent);

  it("names the per-kind read as its own source where only it failed", () => {
    show({ kinds: { kind: "failed" } });

    expect(announcements()).toEqual([`Utskick per mejltyp: ${OUR_READ}`]);
    expect(screen.getByRole("alert")).toBeInTheDocument();
  });

  it("names Leverans alone where only the provider's reads failed", () => {
    show({ summary: { kind: "failed" }, failures: { kind: "failed" } });

    expect(announcements()).toEqual([`Leverans: ${OUR_READ}`]);
  });

  it("names each source in one line where both failed, each on its own", () => {
    show({ summary: { kind: "failed" }, failures: { kind: "failed" }, kinds: { kind: "failed" } });

    expect(screen.getAllByRole("alert")).toHaveLength(1);
    expect(announcements()).toEqual([`Leverans: ${OUR_READ} Utskick per mejltyp: ${OUR_READ}`]);
  });

  it("reads a refused session once, since it is every region's cause", () => {
    const refused = { kind: "failed", line: REFUSED } as const;
    const section = show({ summary: refused, failures: refused, kinds: refused });

    expect(announcements()).toEqual([`Leverans: ${REFUSED}`]);
    // Each region still says it where it is seen: the summary, and our own count under its own heading.
    expect(within(screen.getByRole("region", { name: "Utskick per mejltyp" })).getByText(REFUSED)).toBeInTheDocument();
    expect(within(section).getAllByText(REFUSED)).toHaveLength(2);
  });

  it("announces a count that cannot be told from a stopped one as an alert, under its own source", () => {
    show({
      kinds: { kind: "loaded", data: { period: "h24", status: "NotObservable", from: "2026-10-22", coverage: null, coveredFrom: null, kinds: null } },
    });

    expect(screen.getByRole("alert").textContent).toBe(`Utskick per mejltyp: ${KINDS_NOT_OBSERVABLE}`);
  });

  it("announces nothing for a deployment without Scaleway, which every region says on screen", () => {
    show({
      summary: { kind: "loaded", data: { period: "h24", status: "NotObserved", reason: "NotConfigured", observedAt: null, sent: null, failed: null } },
      failures: { kind: "loaded", data: { period: "h24", status: "NotObserved", reason: "NotConfigured", observedAt: null, failures: null, limit: 10 } },
      kinds: { kind: "loaded", data: { period: "h24", status: "NotObserved", from: null, coverage: null, coveredFrom: null, kinds: null } },
    });

    expect(announcements()).toEqual([]);
    expect(screen.getByRole("table", { name: "Antal utskick per mejltyp" })).toHaveTextContent("Räknas inte i den här miljön.");
  });
});
