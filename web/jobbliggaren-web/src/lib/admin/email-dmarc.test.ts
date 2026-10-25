import { afterEach, describe, expect, it, vi } from "vitest";
import { DMARC_LATEST_FAILURES, type EmailDmarcDto } from "@/lib/dto/admin-email";
import {
  LONG_IPV6,
  cappedDmarc,
  dmarcFailure,
  dmarcIngestion,
  dmarcSource,
  failedDmarc,
  noReportsDmarc,
  observedDmarc,
  unreadDmarc,
} from "@/test/fixtures/admin-email-dmarc";
import { dmarcAddressParts, dmarcView, type DmarcLine } from "./email-dmarc";

const loaded = (data: EmailDmarcDto) => dmarcView({ kind: "loaded", data });
const kinds = (lines: ReadonlyArray<DmarcLine>) => lines.map((line) => (line.kind === "mailbox" ? `mailbox:${line.which}` : line.kind));
const EVERY_COUNT = { skipped: 1, rejected: 2, volumeCapped: 3, abandoned: 4, retryPending: 5 };

afterEach(() => {
  vi.useRealTimers();
});

describe("dmarcView: states with no figure (#1981 PR 4b)", () => {
  it("loading: one status line and a loading row", () => {
    const view = dmarcView({ kind: "loading" });
    expect(kinds(view.lines)).toEqual(["loading"]);
    expect(view.announced?.announce).toBe("status");
    expect(view.emptyRow).toBe("loading");
    expect(view.figures).toBeNull();
  });

  it.each([[undefined, null], ["Din session saknar Admin-rollen.", "Din session saknar Admin-rollen."]] as const)(
    "our own failed read (line %s) alerts with the refusal's words when it has them",
    (line, refusal) => {
      const view = dmarcView({ kind: "failed", line });
      expect(view.lines).toEqual([{ kind: "failed", refusal, announce: "alert" }]);
      expect(view.announced).toEqual(view.lines[0]);
      expect(view.emptyRow).toBe("unknown");
    },
  );

  it.each([
    ["NotObservable", "notObservable", null],
    ["NotConfigured", "notConfigured", null],
    ["Misconfigured", "misconfigured", "alert"],
  ] as const)("%s: its one line, announced only when it is a fault", (status, kind, announce) => {
    const view = loaded(unreadDmarc(status));
    expect(view.lines).toEqual([{ kind, announce }]);
    expect(view.announced === null).toBe(announce === null);
    expect(view.figures).toBeNull();
    expect(view.note).toBeNull();
    expect(view.emptyRow).toBe("unknown");
  });

  it.each(DMARC_LATEST_FAILURES)("never read successfully, %s: the failure line alerts", (failure) => {
    const view = loaded(failedDmarc(failure));
    const [line] = view.lines;
    expect(view.lines).toHaveLength(1);
    expect(line).toMatchObject({ kind: "failure", failure, announce: "alert" });
    expect(view.announced).toEqual(line);
    expect(view.figures).toBeNull();
  });

  it("an unfinished first run says when it began; no other failure carries a start", () => {
    for (const failure of DMARC_LATEST_FAILURES) {
      const line = loaded(failedDmarc(failure)).lines[0];
      expect(line?.kind === "failure" && line.startedAt).toBe(failure === "Incomplete" ? "2026-10-25T08:23:09.123Z" : null);
    }
  });

  it("names the next sign-in exactly when the API gives one, never from the outcome", () => {
    for (const failure of DMARC_LATEST_FAILURES) {
      const line = loaded(failedDmarc(failure)).lines[0];
      const expected = failure === "AuthRefused" || failure === "AuthUnconfirmed" ? "2026-10-26T08:23:09.123Z" : null;
      expect(line?.kind === "failure" && line.resumesAt).toBe(expected);
    }
  });
});

describe("dmarcView: no report in the window", () => {
  it("fresh and without a conflict: the no-report line, unannounced, and its mailbox lines after it", () => {
    const view = loaded(noReportsDmarc({ mailbox: EVERY_COUNT }));
    expect(kinds(view.lines)).toEqual([
      "noReports", "mailbox:unread", "mailbox:volumeCapped", "mailbox:abandoned", "mailbox:retryPending",
    ]);
    expect(view.announced).toBeNull();
    expect(view.figures).toBeNull();
    expect(view.emptyRow).toBe("unknown");
  });

  it("with a conflict: the conflict line instead, since a conflicting report is still a report", () => {
    expect(kinds(loaded(noReportsDmarc({ conflictingReports: 2 })).lines)).toEqual(["conflicting"]);
  });

  it("stale: the stale line alerts in place of the no-report line", () => {
    const view = loaded(noReportsDmarc({ ingestion: dmarcIngestion({ stale: true }) }));
    expect(view.lines).toEqual([{ kind: "stale", since: "2026-10-25T08:23:09.123Z", announce: "alert" }]);
    expect(view.announced).toEqual(view.lines[0]);
  });

  it("after a failed run: the failure line alerts in place of the no-report line", () => {
    const view = loaded(noReportsDmarc({ ingestion: dmarcFailure("Timeout"), conflictingReports: 1 }));
    expect(kinds(view.lines)).toEqual(["failure", "conflicting"]);
    expect(view.announced?.kind).toBe("failure");
  });
});

describe("dmarcView: figures", () => {
  it("fresh: the figures, the note with when it was fetched, every row, and nothing announced", () => {
    const view = loaded(observedDmarc());
    expect(view.figures).toEqual({ messages: 51, failing: 3 });
    expect(view.lines).toEqual([]);
    expect(view.announced).toBeNull();
    expect(view.note).toEqual({
      reports: 2, from: "2026-10-23T00:00:00.000Z", to: "2026-10-24T00:00:00.000Z", fetchedAt: "2026-10-25T08:23:09.123Z",
    });
    expect(view.rows?.map((row) => row.address)).toEqual(["198.51.100.7", "192.0.2.10", LONG_IPV6]);
    expect(view.emptyRow).toBeNull();
    expect(view.cap).toBeNull();
  });

  it("stale without a failure: the stale line alerts, and the note drops its fetched sentence", () => {
    const view = loaded(observedDmarc({ ingestion: dmarcIngestion({ stale: true }) }));
    expect(kinds(view.lines)).toEqual(["stale"]);
    expect(view.note?.fetchedAt).toBeNull();
    expect(view.figures).not.toBeNull();
  });

  it("after a failed run: the failure line alerts, and the figures and their fetched time stay", () => {
    const view = loaded(observedDmarc({ ingestion: dmarcFailure("AuthUnconfirmed") }));
    expect(view.lines).toEqual([{
      kind: "failure", failure: "AuthUnconfirmed", startedAt: null, resumesAt: "2026-10-26T08:23:09.123Z", announce: "alert",
    }]);
    expect(view.note?.fetchedAt).toBe("2026-10-25T06:23:09.123Z");
    expect(view.figures).toEqual({ messages: 51, failing: 3 });
  });

  it("orders the lines cause first, then the conflict, then the mailbox counts, and never announces a count", () => {
    const view = loaded(observedDmarc({ ingestion: dmarcFailure("Protocol"), conflictingReports: 1, mailbox: EVERY_COUNT }));
    expect(kinds(view.lines)).toEqual([
      "failure", "conflicting", "mailbox:unread", "mailbox:volumeCapped", "mailbox:abandoned", "mailbox:retryPending",
    ]);
    expect(view.lines.filter((line) => line.announce !== null)).toHaveLength(1);
    expect(view.lines.find((line) => line.kind === "mailbox" && line.which === "unread")).toMatchObject({ count: 3 });
  });

  it("names the cap when more sources were counted than shown", () => {
    expect(loaded(cappedDmarc(31)).cap).toEqual({ shown: 25, total: 31 });
  });

  it("gives an address that heads two rows its domain in the row's name, and leaves a unique one bare", () => {
    const view = loaded(observedDmarc({
      sources: [
        dmarcSource("198.51.100.7", "jobbliggaren.se", 3, 0, 0, 3),
        dmarcSource("198.51.100.7", "mail.jobbliggaren.se", 2, 0, 0, 2),
        dmarcSource("192.0.2.10", "jobbliggaren.se", 46, 46, 46, 0),
      ],
      sourceCount: 3,
      failingMessages: 5,
    }));
    expect(view.rows?.map((row) => [row.key, row.sharesAddress])).toEqual([
      ["198.51.100.7 jobbliggaren.se", true],
      ["198.51.100.7 mail.jobbliggaren.se", true],
      ["192.0.2.10 jobbliggaren.se", false],
    ]);
  });

  it("reads no clock: the same read gives the same view in another year", () => {
    const before = loaded(observedDmarc({ ingestion: dmarcFailure("AuthRefused") }));
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2030-01-01T00:00:00Z"));
    expect(loaded(observedDmarc({ ingestion: dmarcFailure("AuthRefused") }))).toEqual(before);
  });
});

describe("dmarcAddressParts", () => {
  it.each([
    [LONG_IPV6, ["2001:", "db8:", "ffff:", "ffff:", "ffff:", "ffff:", "ffff:", "ffff"]],
    ["2001:db8::1", ["2001:", "db8::", "1"]],
    ["2001:db8::5efe:192.0.2.1", ["2001:", "db8::", "5efe:", "192.0.2.1"]],
    ["198.51.100.7", ["198.51.100.7"]],
  ])("breaks %s only after a colon that does not begin a double colon", (address, parts) => {
    expect(dmarcAddressParts(address)).toEqual(parts);
    expect(dmarcAddressParts(address).join("")).toBe(address);
  });
});
