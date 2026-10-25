import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { render, screen, within } from "@testing-library/react";
import type { EmailDmarcDto } from "@/lib/dto/admin-email";
import type { AdminEmailRead } from "@/lib/admin/view-models";
import {
  DMARC_SOURCES,
  LONG_IPV6,
  cappedDmarc,
  dmarcFailure,
  dmarcSource,
  dmarcStaleIngestion,
  failedDmarc,
  noReportsDmarc,
  observedDmarc,
  unreadDmarc,
} from "@/test/fixtures/admin-email-dmarc";
import enAdmin from "../../../messages/en/admin.json";
import { AdminEmailDmarc } from "./admin-email-dmarc";

/** What a screen reader reads for an unknown value: the dash is hidden from it, and its words are not. */
const UNKNOWN = "–Uppgift saknas";
const RUNBOOK = "Se runbooken för admin-e-post.";
const FIXED =
  "Rapporterna, som vi inte har kontrollerat, visar autentisering, inte om meddelandena hamnade i inkorgen eller bland skräpposten.";
const EVERY_COUNT = { skipped: 1, rejected: 2, volumeCapped: 3, abandoned: 4, retryPending: 5 };

function show(read: AdminEmailRead<EmailDmarcDto>) {
  render(<AdminEmailDmarc read={read} />);
  return screen.getByRole("region", { name: "DMARC-rapporter" });
}
const loaded = (data: EmailDmarcDto) => show({ kind: "loaded", data });
const values = (section: HTMLElement) => within(section).getAllByRole("definition").map((value) => value.textContent);
const lines = (section: HTMLElement) =>
  [...section.querySelectorAll(":scope > div > p.jp-adminsoon")].map((line) => line.textContent);
const announcements = (section: HTMLElement) =>
  [...within(section).queryAllByRole("alert"), ...within(section).queryAllByRole("status")].map((node) => node.textContent);
const table = (section: HTMLElement) => within(section).getByRole("table");

describe("AdminEmailDmarc: figures (#1981 PR 4b)", () => {
  it("is its own h2 section with the two summary values, the six columns and one row header per row", () => {
    const section = loaded(observedDmarc());

    expect(within(section).getByRole("heading", { level: 2, name: "DMARC-rapporter" })).toBeInTheDocument();
    expect(within(section).getAllByRole("term").map((term) => term.textContent)).toEqual(["Meddelanden", "DMARC-underkända"]);
    expect(values(section)).toEqual(["51", "3"]);
    expect(within(table(section)).getAllByRole("columnheader").map((head) => head.textContent)).toEqual([
      "IP-adress", "Avsändardomän", "Meddelanden", "DKIM-godkända", "SPF-godkända", "DMARC-underkända",
    ]);
    expect(within(table(section)).getAllByRole("rowheader").map((head) => head.textContent)).toEqual(
      DMARC_SOURCES.map((source) => source.address),
    );
    expect(announcements(section)).toEqual([]);
  });

  it("carries the flag in words, with the danger colour only on a failing count above zero", () => {
    const section = loaded(observedDmarc());
    const [failing, dkimOnly, spfOnly] = within(table(section)).getAllByRole("row").slice(1);

    expect(section.querySelector("dd .jp-admin-danger")).toHaveTextContent("3");
    expect(failing?.querySelectorAll(".jp-admin-danger")).toHaveLength(1);
    expect(failing).toHaveTextContent("198.51.100.7jobbliggaren.se3003");
    // A source that passes on one mechanism is not failing: its zeroes stay in neutral ink.
    expect(dkimOnly).toHaveTextContent("192.0.2.10mail.jobbliggaren.se404000");
    expect(spfOnly).toHaveTextContent(`${LONG_IPV6}jobbliggaren.se8080`);
    expect(dkimOnly?.querySelector(".jp-admin-danger")).toBeNull();
    expect(spfOnly?.querySelector(".jp-admin-danger")).toBeNull();
    expect(section.querySelector(".jp-pill, svg")).toBeNull();
  });

  it("leaves a zero failing count in neutral ink in the summary too", () => {
    const section = loaded(observedDmarc({
      sources: [dmarcSource("192.0.2.10", "jobbliggaren.se", 51, 51, 51, 0)], sourceCount: 1, failingMessages: 0,
    }));
    expect(values(section)).toEqual(["51", "0"]);
    expect(section.querySelector(".jp-admin-danger")).toBeNull();
  });

  it("names the reports and the span they cover to the minute in Swedish time, then the fixed sentence", () => {
    const section = loaded(observedDmarc());
    const [p1, p2] = [...section.querySelectorAll(".jp-adminmailsum__note p")];

    expect(p1).toHaveTextContent(
      "Enligt 2 rapporter till domänens rapportadress, för tiden 2026-10-23 02:00 till 2026-10-24 02:00. Hämtat från brevlådan 2026-10-25 09:23.",
    );
    expect([...(p1?.querySelectorAll("time") ?? [])].map((time) => time.getAttribute("datetime"))).toEqual([
      "2026-10-23T00:00:00.000Z", "2026-10-24T00:00:00.000Z", "2026-10-25T08:23:09.123Z",
    ]);
    expect(p2).toHaveTextContent(FIXED);
    expect(table(section)).toHaveAccessibleName("DMARC-resultat per IP-adress för tiden 2026-10-23 02:00 till 2026-10-24 02:00");
  });

  it("names one report in the singular", () => {
    const section = loaded(observedDmarc({ reports: 1 }));
    expect(section.querySelector(".jp-adminmailsum__note p")).toHaveTextContent("Enligt en rapport till domänens rapportadress");
  });

  it("shows a span across the change to winter time in each instant's own offset", () => {
    const section = loaded(observedDmarc({ coveredFrom: "2026-10-24T00:00:00+00:00", coveredTo: "2026-10-26T00:00:00+00:00" }));
    expect(section.querySelector(".jp-adminmailsum__note p")).toHaveTextContent("för tiden 2026-10-24 02:00 till 2026-10-26 01:00.");
  });

  it("says how many rows it shows of how many when the sources are capped", () => {
    const section = loaded(cappedDmarc(31));
    expect(within(table(section)).getAllByRole("rowheader")).toHaveLength(25);
    expect(within(section).getByText("Visar 25 av 31 rader.")).toBeInTheDocument();
  });

  it("wraps a long IPv6 address at its colons without changing its text or its name", () => {
    const head = within(table(loaded(observedDmarc()))).getAllByRole("rowheader")[2];
    expect(head?.textContent).toBe(LONG_IPV6);
    expect(head).toHaveAccessibleName(LONG_IPV6);
    expect(head?.querySelectorAll("wbr")).toHaveLength(7);
    expect(head?.querySelector(".jp-admindmarc__addr")).not.toBeNull();
  });

  it("gives two rows that share an address distinct names, and leaves a unique address bare", () => {
    const section = loaded(observedDmarc({
      sources: [
        dmarcSource("198.51.100.7", "jobbliggaren.se", 3, 0, 0, 3),
        dmarcSource("198.51.100.7", "mail.jobbliggaren.se", 2, 0, 0, 2),
        dmarcSource("192.0.2.10", "jobbliggaren.se", 46, 46, 46, 0),
      ],
      sourceCount: 3,
      failingMessages: 5,
    }));
    const heads = within(table(section)).getAllByRole("rowheader");
    expect(heads.map((head) => head.textContent)).toEqual([
      "198.51.100.7, jobbliggaren.se", "198.51.100.7, mail.jobbliggaren.se", "192.0.2.10",
    ]);
  });
});

describe("AdminEmailDmarc: every state without figures is one line, never a zero", () => {
  it.each([
    ["NotConfigured", "Läses inte i den här miljön.", []],
    ["NotObservable", "Brevlådan har inte lästs än.", []],
    ["Misconfigured", `Lösenordet till brevlådan är tomt. ${RUNBOOK}`, [`DMARC-rapporter: Lösenordet till brevlådan är tomt. ${RUNBOOK}`]],
  ] as const)("%s", (status, line, announced) => {
    const section = loaded(unreadDmarc(status));
    expect(values(section)).toEqual([UNKNOWN, UNKNOWN]);
    expect(lines(section)).toEqual([line]);
    expect(within(table(section)).getAllByRole("row")).toHaveLength(2);
    expect(section.querySelector("td.jp-admintable__soon")).toHaveTextContent("Uppgift saknas");
    expect(table(section)).toHaveAccessibleName("DMARC-resultat per IP-adress");
    expect(section.querySelector(".jp-adminmailsum__note")).toBeNull();
    expect(announcements(section)).toEqual(announced);
  });

  it.each([
    ["Unreachable", `Brevlådan gick inte att nå. ${RUNBOOK}`],
    ["Timeout", `Brevlådan svarade inte i tid. ${RUNBOOK}`],
    ["TlsFailed", `Brevlådans certifikat kunde inte verifieras. ${RUNBOOK}`],
    ["Protocol", `Brevlådans svar kunde inte läsas. ${RUNBOOK}`],
    ["AuthRefused", `Brevlådan nekade inloggningen. Nästa inloggningsförsök görs tidigast 2026-10-26 09:23. ${RUNBOOK}`],
    ["AuthUnconfirmed", `Lösenordet skickades, men brevlådan gav inget svar. Nästa inloggningsförsök görs tidigast 2026-10-26 09:23. ${RUNBOOK}`],
    ["Incomplete", `Den första inläsningen av brevlådan började 2026-10-25 09:23 och har inte avslutats. ${RUNBOOK}`],
  ] as const)("never read successfully, %s: its reason, announced, with no figure", (failure, line) => {
    const section = loaded(failedDmarc(failure));
    expect(values(section)).toEqual([UNKNOWN, UNKNOWN]);
    expect(lines(section)).toEqual([line]);
    expect(announcements(section)).toEqual([`DMARC-rapporter: ${line}`]);
    expect(section.textContent).not.toMatch(/fel lösenord|felaktigt/i);
  });

  it("carries the next sign-in in a time element with its instant", () => {
    const line = loaded(failedDmarc("AuthUnconfirmed")).querySelector("p.jp-adminsoon time");
    expect(line).toHaveAttribute("datetime", "2026-10-26T08:23:09.123Z");
  });

  it("no report in a fresh window: its line, unannounced, the figures unknown and the table's one row", () => {
    const section = loaded(noReportsDmarc());
    expect(values(section)).toEqual([UNKNOWN, UNKNOWN]);
    expect(lines(section)).toEqual(["Ingen rapport täcker de senaste 7 dygnen."]);
    expect(announcements(section)).toEqual([]);
    expect(section.querySelector("td.jp-admintable__soon")).toHaveTextContent("Uppgift saknas");
  });

  it("no report while stale: the stale line, announced, in place of the no-report line", () => {
    const section = loaded(noReportsDmarc({ ingestion: dmarcStaleIngestion() }));
    const stale = `Brevlådan har inte lästs sedan 2026-10-25 06:23. ${RUNBOOK}`;
    expect(lines(section)).toEqual([stale]);
    expect(announcements(section)).toEqual([`DMARC-rapporter: ${stale}`]);
  });
});

describe("AdminEmailDmarc: the read's cause and the mailbox's counts with figures", () => {
  it("keeps the figures after a failed run, says the reason, and keeps when they were fetched", () => {
    const section = loaded(observedDmarc({ ingestion: dmarcFailure("Protocol") }));
    expect(values(section)).toEqual(["51", "3"]);
    expect(lines(section)).toEqual([`Brevlådans svar kunde inte läsas. ${RUNBOOK}`]);
    expect(section.querySelector(".jp-adminmailsum__note p")).toHaveTextContent("Hämtat från brevlådan 2026-10-25 07:23.");
  });

  it("says when a stale read was last fetched in its line, and not again in the note", () => {
    const section = loaded(observedDmarc({ ingestion: dmarcStaleIngestion() }));
    expect(lines(section)).toEqual([`Brevlådan har inte lästs sedan 2026-10-25 06:23. ${RUNBOOK}`]);
    expect(section.querySelector(".jp-adminmailsum__note")).not.toHaveTextContent("Hämtat");
    expect(section.querySelector(".jp-adminmailsum__note")).toHaveTextContent(FIXED);
  });

  it("lists a conflict and each mailbox count in their order, and announces none of them", () => {
    const section = loaded(observedDmarc({ conflictingReports: 2, mailbox: EVERY_COUNT }));
    expect(lines(section)).toEqual([
      `2 rapporter finns i flera versioner med olika innehåll, och ingen av versionerna räknas. ${RUNBOOK}`,
      `3 mejl till brevlådan de senaste 7 dygnen kunde inte läsas som rapport. ${RUNBOOK}`,
      `3 mejl till brevlådan de senaste 7 dygnen kunde inte sparas, eftersom gränsen för sparade rapporter per dygn var nådd. ${RUNBOOK}`,
      `4 mejl till brevlådan de senaste 7 dygnen gick inte att läsa efter upprepade försök. ${RUNBOOK}`,
      "5 mejl till brevlådan väntar på ett nytt försök.",
    ]);
    // Report content never raises an announcement (OQ9 condition 2).
    expect(announcements(section)).toEqual([]);
  });

  it("names one mail and one report in the singular", () => {
    const section = loaded(noReportsDmarc({ conflictingReports: 1, mailbox: { ...EVERY_COUNT, skipped: 1, rejected: 0 } }));
    expect(lines(section)[0]).toBe(`En rapport finns i flera versioner med olika innehåll, och ingen av versionerna räknas. ${RUNBOOK}`);
    expect(lines(section)[1]).toBe(`Ett mejl till brevlådan de senaste 7 dygnen kunde inte läsas som rapport. ${RUNBOOK}`);
  });
});

describe("AdminEmailDmarc: our own read", () => {
  it("while loading: a status line, the loading row and no figure", () => {
    const section = show({ kind: "loading" });
    expect(values(section)).toEqual([UNKNOWN, UNKNOWN]);
    expect(within(section).getAllByText("Hämtar uppgifter…")).toHaveLength(2);
    expect(announcements(section)).toEqual(["DMARC-rapporter: Hämtar uppgifter…"]);
  });

  it("failed: the shared line, announced under the region's own name", () => {
    const section = show({ kind: "failed" });
    expect(lines(section)).toEqual(["Uppgifterna kunde inte hämtas. Försök igen om en stund."]);
    expect(announcements(section)).toEqual(["DMARC-rapporter: Uppgifterna kunde inte hämtas. Försök igen om en stund."]);
  });

  it("refused: the refusal's own words", () => {
    const section = show({ kind: "failed", line: "Din session saknar Admin-rollen." });
    expect(announcements(section)).toEqual(["DMARC-rapporter: Din session saknar Admin-rollen."]);
  });
});

describe("AdminEmailDmarc: what the section never says or carries", () => {
  const every: ReadonlyArray<EmailDmarcDto> = [
    observedDmarc({ conflictingReports: 1, mailbox: EVERY_COUNT }),
    cappedDmarc(),
    noReportsDmarc(),
    failedDmarc("AuthRefused"),
    unreadDmarc("NotConfigured"),
  ];

  it.each(every.map((read) => [read.status, read] as const))("%s: no link, no address in an attribute, no Kommer snart", (_status, read) => {
    const section = loaded(read);
    expect(section.querySelectorAll("a, [href], [src], [srcset], [action]")).toHaveLength(0);
    const sources = read.status === "Observed" ? read.sources : [];
    const attributes = [...section.querySelectorAll("*")].flatMap((node) => [...node.attributes].map((attribute) => attribute.value));
    for (const source of sources) {
      expect(attributes.filter((value) => value.includes(source.address) || value.includes(source.headerFrom))).toEqual([]);
    }
    expect(section.textContent).not.toContain("Kommer snart");
  });

  it("names no provider, host, transport or mailbox address, and no percentage, in its own copy", () => {
    const section = loaded(observedDmarc({ conflictingReports: 1, mailbox: EVERY_COUNT, ingestion: dmarcFailure("AuthRefused") }));
    const copy = [...section.querySelectorAll("h2, p, caption, dt, thead th")].map((node) => node.textContent).join(" ");
    expect(copy).not.toMatch(/scaleway|jobbliggaren|strato|smtp|@|%/i);
    expect(copy).not.toMatch(/Skickade|Överlämnade/);
  });

  it("says in English too that the reports are not checked by us", () => {
    expect(enAdmin.email.dmarc.fixed).toBe(
      "The reports, which we have not checked, show authentication, not whether the messages reached the inbox or the spam folder.",
    );
  });

  it("is a server component that never sets inner HTML", () => {
    const here = dirname(fileURLToPath(import.meta.url));
    for (const file of [resolve(here, "admin-email-dmarc.tsx"), resolve(here, "..", "..", "lib", "admin", "email-dmarc.ts")]) {
      const source = readFileSync(file, "utf8");
      expect(source).not.toContain("dangerouslySetInnerHTML");
      expect(source).not.toMatch(/^\s*["']use client["']/m);
    }
  });
});
