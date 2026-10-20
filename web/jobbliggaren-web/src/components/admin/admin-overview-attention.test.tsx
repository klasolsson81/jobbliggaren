import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { emailDeliveryFixture, overviewSnapshotFixture, OVERVIEW_TIME } from "@/test/fixtures/admin-overview";
import type { AdminOverviewSnapshot, BackupObservationData } from "@/lib/dto/admin-overview";
import type { EmailDeliveryDto } from "@/lib/dto/admin-email";
import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import { AdminOverview } from "./admin-overview";

const NOW = Date.parse(OVERVIEW_TIME);
const SAMPLED = "2026-10-08T09:59:30Z";
const attention = () => screen.getByRole("region", { name: "Kräver uppmärksamhet" });

/** The fixture with nothing in need of attention: no failed job, no pending deletion, both host cards positive. */
function calm(): AdminOverviewSnapshot {
  const snapshot = overviewSnapshotFixture();
  if (snapshot.accounts.kind !== "loaded") throw new Error("Expected observed accounts");
  return {
    ...snapshot,
    accounts: { ...snapshot.accounts, data: { ...snapshot.accounts.data, counts: { ...snapshot.accounts.data.counts, pendingDeletion: 0 } } },
    jobs: { kind: "loaded", data: { totalCount: 0 }, sampledAt: OVERVIEW_TIME, refreshFailed: false },
  };
}
function withBackup(data: Partial<BackupObservationData>): AdminOverviewSnapshot {
  const snapshot = calm();
  if (snapshot.backup.kind !== "loaded") throw new Error("Expected an observed backup");
  return { ...snapshot, backup: { ...snapshot.backup, data: { ...snapshot.backup.data, ...data } } };
}
function withServices(patch: Partial<ServicesObservationDto>): AdminOverviewSnapshot {
  const snapshot = calm();
  if (snapshot.services.kind !== "loaded") throw new Error("Expected observed services");
  return { ...snapshot, services: { ...snapshot.services, data: { ...snapshot.services.data, ...patch } } };
}
const show = (snapshot: AdminOverviewSnapshot) => render(<AdminOverview observations={snapshot} now={NOW} />);

describe("the attention card beside the Backup and Tjänster cards (#2084)", () => {
  it("adds nothing while both cards show only positive states, keeps its edge unknown and says no all-clear", () => {
    show(calm());

    expect(attention()).toHaveAttribute("data-state", "unknown");
    expect(within(attention()).queryAllByRole("link")).toEqual([]);
    expect(attention()).not.toHaveTextContent(/Backup|Worker|Tillfälligt lager|Annonsimport|Tjänster/);
    expect(attention()).not.toHaveTextContent("Inget kräver uppmärksamhet");
  });

  it("lists the box's present state, no run recorded and a timer that is off, each linked to the Backup card", () => {
    show(withBackup({ lastSuccess: { state: "missing" }, timer: { state: "inactive" } }));

    expect(attention()).toHaveAttribute("data-state", "raised");
    const missing = within(attention()).getByRole("link", { name: "Backup: Ingen lyckad körning registrerad" });
    const off = within(attention()).getByRole("link", { name: "Backup: Ingen körning planerad" });
    for (const link of [missing, off]) expect(link).toHaveAttribute("href", "#admin-overview-backup");
    // The anchor lands on the card's own heading.
    expect(document.getElementById("admin-overview-backup")).toHaveTextContent("Backup");
  });

  it.each([
    [{ lastSuccess: { state: "unreadable" } }, "Backup: Tidsstämpeln kunde inte läsas"],
    [{ lastSuccess: { state: "invalid" } }, "Backup: Tidsstämpeln är ogiltig"],
    [{ lastSuccess: { state: "recorded", completedAt: "2026-10-06T00:19:07+00:00", overdue: true } }, "Backup: Äldre än 26 timmar"],
    [{ timer: { state: "notInstalled" } }, "Backup: Schemat finns inte på servern"],
  ] as const)("raises the edge and words %j as the Backup card does", (data, name) => {
    show(withBackup(data));

    expect(attention()).toHaveAttribute("data-state", "raised");
    expect(within(attention()).getByRole("link", { name })).toHaveAttribute("href", "#admin-overview-backup");
  });

  // The instants and the limit are kept whole with a no-break space, as the Tjänster card keeps them.
  it.each([
    [{ backgroundJobs: { state: "Silent", sampledAt: SAMPLED, heartbeatAt: "2026-10-08T09:50:00Z" } }, "Worker: Inget livstecken sedan 2026-10-08\u00A011:50"],
    [{ backgroundJobs: { state: "NoServer", sampledAt: SAMPLED, heartbeatAt: null } }, "Worker: Inget livstecken registrerat"],
    [{ volatileStore: { state: "Unreachable", sampledAt: SAMPLED } }, "Tillfälligt lager: Svarar inte"],
    [{ jobImport: { state: "Overdue", sampledAt: SAMPLED, lastRunAt: "2026-10-08T09:20:00Z", errors: 0 } }, "Annonsimport: Senaste körning 2026-10-08\u00A011:20, äldre än 25\u00A0minuter"],
    [{ jobImport: { state: "NoneRecorded", sampledAt: SAMPLED, lastRunAt: null, errors: null } }, "Annonsimport: Ingen körning registrerad"],
    [{ jobImport: { state: "Recorded", sampledAt: SAMPLED, lastRunAt: "2026-10-08T09:50:00Z", errors: 3 } }, "Annonsimport: 3 fel i körningen"],
  ] as const)("raises the edge and words %j as the Tjänster card does", (patch, name) => {
    show(withServices(patch));

    expect(attention()).toHaveAttribute("data-state", "raised");
    expect(within(attention()).getByRole("link", { name })).toHaveAttribute("href", "#admin-overview-services");
    expect(document.getElementById("admin-overview-services")).toHaveTextContent("Tjänster");
  });

  it("lists both lines of an overdue run with errors, each linked to the Tjänster card", () => {
    show(withServices({ jobImport: { state: "Overdue", sampledAt: SAMPLED, lastRunAt: "2026-10-08T09:20:00Z", errors: 2 } }));

    expect(attention()).toHaveAttribute("data-state", "raised");
    const lines = within(attention()).getAllByRole("link").map((link) => [link.textContent, link.getAttribute("href")]);
    expect(lines).toEqual([
      ["Annonsimport: Senaste körning 2026-10-08\u00A011:20, äldre än 25\u00A0minuter", "#admin-overview-services"],
      ["Annonsimport: 2 fel i körningen", "#admin-overview-services"],
    ]);
    // The instant never breaks inside itself, as on the card.
    expect(within(attention()).getAllByRole("link")[0]?.querySelector(".jp-adminusers__date")?.textContent).toBe("2026-10-08\u00A011:20");
  });

  it("raises nothing from a warning the page has withdrawn as too old, and names the row undetermined instead", () => {
    const stale = withServices({ jobImport: { state: "Overdue", sampledAt: SAMPLED, lastRunAt: "2026-10-08T09:20:00Z", errors: 2 } });
    const { unmount } = render(<AdminOverview observations={stale} now={Date.parse(SAMPLED) + 120_000} />);
    expect(attention()).toHaveAttribute("data-state", "raised");
    unmount();

    render(<AdminOverview observations={stale} now={Date.parse(SAMPLED) + 121_000} />);
    expect(attention()).toHaveAttribute("data-state", "unknown");
    expect(within(attention()).queryAllByRole("link")).toEqual([]);
    expect(attention()).toHaveTextContent("AnnonsimportKunde inte avgöras");
    expect(within(screen.getByRole("region", { name: "Tjänster" })).getByText("Annonsimport", { selector: "dt" }).nextElementSibling)
      .toHaveTextContent(/^– Kunde inte avgöras$/);
  });

  it("says an undetermined row could not be determined, without a link, a warning or a raised edge", () => {
    show(withServices({ volatileStore: { state: "Unknown", sampledAt: null } }));

    expect(attention()).toHaveAttribute("data-state", "unknown");
    expect(attention()).toHaveTextContent("Tillfälligt lagerKunde inte avgöras");
    expect(within(attention()).queryAllByRole("link")).toEqual([]);
  });

  it("says an unknown backup timer is unknown rather than passing over it", () => {
    show(withBackup({ timer: { state: "unknown" } }));

    expect(attention()).toHaveAttribute("data-state", "unknown");
    expect(attention()).toHaveTextContent("Backup: Nästa planerade körningUppgift saknas");
  });

  it.each([
    ["failed", "Uppgifterna kunde inte hämtas"],
    ["loading", "Hämtar uppgifter"],
  ] as const)("says a %s source could not be read, in the form the card uses for its other sources", (kind, words) => {
    show({ ...calm(), backup: { kind }, services: { kind } });

    expect(attention()).toHaveAttribute("data-state", "unknown");
    expect(attention()).toHaveTextContent(`Backup${words}`);
    expect(attention()).toHaveTextContent(`Tjänster${words}`);
    expect(within(attention()).queryAllByRole("link")).toEqual([]);
    expect(within(attention()).queryByRole("alert")).toBeNull();
  });

  it("says a backup host that has not reported has not reported", () => {
    show({ ...calm(), backup: { kind: "awaiting" } });

    expect(attention()).toHaveTextContent("BackupServern har inte rapporterat ännu.");
    expect(attention()).toHaveAttribute("data-state", "unknown");
  });

  it("keeps listing the failed jobs and pending deletions beside the new lines", () => {
    const snapshot = overviewSnapshotFixture();
    if (snapshot.backup.kind !== "loaded") throw new Error("Expected an observed backup");
    show({ ...snapshot, backup: { ...snapshot.backup, data: { ...snapshot.backup.data, timer: { state: "inactive" } } } });

    expect(within(attention()).getAllByRole("link").map((link) => link.textContent)).toEqual([
      "3 bakgrundsjobb har misslyckats", "2 konton väntar på radering", "Backup: Ingen körning planerad",
    ]);
  });
});

describe("the attention card's failed sends (#1981 PR 2)", () => {
  const NO_FIGURES = { observedAt: null, sent: null, failed: null } as const;
  const withDelivery = (data: EmailDeliveryDto, sampledAt = SAMPLED): AdminOverviewSnapshot =>
    ({ ...calm(), delivery: { kind: "loaded", data, sampledAt, refreshFailed: false } });
  const UNREAD_SOURCE = "Misslyckade utskick";
  /** An API read instant no other source in the fixture carries, so its note is told apart from theirs. */
  const READ_AT = "2026-10-08T09:58:00Z";

  it("links a count above 0 to the page's 24 tim view, and raises the edge", () => {
    show(withDelivery(emailDeliveryFixture(3)));

    const link = within(attention()).getByRole("link", { name: "3 utskick har misslyckats de senaste 24 timmarna" });
    expect(link).toHaveAttribute("href", "/admin/e-post?period=h24");
    expect(attention()).toHaveAttribute("data-state", "raised");
    expect(attention()).toHaveTextContent("Hämtat 2026-10-08 11:59");
  });

  it("says a zero at Scaleway in words, without a link, and raises nothing", () => {
    show(withDelivery(emailDeliveryFixture(0)));

    expect(attention()).toHaveTextContent("Inga misslyckade utskick hos Scaleway de senaste 24 timmarna.");
    expect(within(attention()).queryAllByRole("link")).toEqual([]);
    expect(attention()).toHaveAttribute("data-state", "unknown");
  });

  it.each([
    [
      "a read key that sees no sends",
      { period: "h24", status: "NotObservable", reason: null, ...NO_FIGURES },
      "Scaleways leveransstatistik visar inga utskick de senaste 32 dygnen, så siffrorna kan inte bedömas. Kontrollera läsnyckelns behörighet enligt runbooken för admin-e-post.",
    ],
    [
      "a read key that is not installed",
      { period: "h24", status: "ReadKeyMissing", reason: null, ...NO_FIGURES },
      "Läsnyckeln är inte installerad. Se runbooken för admin-e-post.",
    ],
    [
      "a provider that did not answer",
      { period: "h24", status: "Failed", reason: "Timeout", ...NO_FIGURES },
      "Scaleway svarade inte. Försök igen om en stund.",
    ],
    [
      "a refused read key",
      { period: "h24", status: "Failed", reason: "Unauthorized", ...NO_FIGURES },
      "Scaleway nekade läsnyckeln. Kontrollera dess utgångsdatum enligt runbooken för admin-e-post.",
    ],
  ] as const)("names the source and says why for %s: never a zero and never an all-clear", (_name, data, line) => {
    show(withDelivery(data, READ_AT));

    expect(attention()).toHaveTextContent(`${UNREAD_SOURCE}${line}`);
    expect(attention()).not.toHaveTextContent(/Inga misslyckade utskick|0 utskick/);
    expect(attention()).toHaveAttribute("data-state", "unknown");
    expect(attention()).toHaveTextContent("Hämtat 2026-10-08 11:58");
    expect(within(attention()).queryByRole("alert")).toBeNull();
  });

  it("says a deployment without Scaleway does not count here, with no time to date it", () => {
    show(withDelivery({ period: "h24", status: "NotObserved", reason: "NotConfigured", ...NO_FIGURES }, READ_AT));

    expect(attention()).toHaveTextContent(`${UNREAD_SOURCE}Räknas inte i den här miljön.`);
    expect(attention()).not.toHaveTextContent(/Inga misslyckade utskick|Hämtat|2026-10-08 11:58/);
  });

  it.each([["failed", "Uppgifterna kunde inte hämtas. Försök igen om en stund."], ["loading", "Hämtar uppgifter…"]] as const)(
    "names the source and says our own read is %s",
    (kind, line) => {
      show({ ...calm(), delivery: { kind } });

      expect(attention()).toHaveTextContent(`${UNREAD_SOURCE}${line}`);
      expect(attention()).not.toHaveTextContent("Inga misslyckade utskick");
      expect(attention()).toHaveAttribute("data-state", "unknown");
    },
  );

  it("keeps an old count marked as old, as every other source", () => {
    show({ ...calm(), delivery: { kind: "loaded", data: emailDeliveryFixture(1), sampledAt: SAMPLED, refreshFailed: true } });

    expect(within(attention()).getByRole("link", { name: "1 utskick har misslyckats de senaste 24 timmarna" })).toBeInTheDocument();
    expect(attention()).toHaveTextContent("Uppdateringen misslyckades. Senaste uppgiften visas.");
  });

  /** The note that dates the line: the paragraph around the read's own time, which no other source carries. */
  const deliveryNote = () => attention().querySelector(`time[datetime="${READ_AT}"]`)?.closest("p") ?? null;
  const observedAtRead = (failed: number): EmailDeliveryDto =>
    ({ period: "h24", status: "Observed", reason: null, observedAt: READ_AT, sent: 57, failed });
  const refreshFailed = (): AdminOverviewSnapshot =>
    ({ ...calm(), delivery: { kind: "loaded", data: observedAtRead(1), sampledAt: READ_AT, refreshFailed: true } });

  it.each<readonly [string, AdminOverviewSnapshot]>([
    ["a count above 0", withDelivery(observedAtRead(2), READ_AT)],
    ["a zero at Scaleway", withDelivery(observedAtRead(0), READ_AT)],
    ["a read key that sees no sends", withDelivery({ period: "h24", status: "NotObservable", reason: null, ...NO_FIGURES }, READ_AT)],
    ["a read key that is not installed", withDelivery({ period: "h24", status: "ReadKeyMissing", reason: null, ...NO_FIGURES }, READ_AT)],
    ["a provider that did not answer", withDelivery({ period: "h24", status: "Failed", reason: "Timeout", ...NO_FIGURES }, READ_AT)],
    ["a refresh that failed", refreshFailed()],
  ])("dates the line Hämtat, never Uppgift från, for %s (design-reviewer Major 1)", (_name, snapshot) => {
    show(snapshot);

    expect(deliveryNote()).toHaveTextContent(/^Hämtat 2026-10-08 11:58/);
    expect(deliveryNote()).not.toHaveTextContent("Uppgift från");
  });

  it("keeps the old and the failed-refresh lines under Hämtat", () => {
    render(<AdminOverview observations={refreshFailed()} now={Date.parse(READ_AT) + 6 * 60_000} />);

    expect(deliveryNote()).toHaveTextContent(
      "Hämtat 2026-10-08 11:58Uppdateringen misslyckades. Senaste uppgiften visas.Uppgiften är äldre än fem minuter.",
    );
  });

  it("is never on the e-post card: the card stays the month's cost", () => {
    show(withDelivery(emailDeliveryFixture(4)));
    expect(screen.getByRole("region", { name: "E-post" })).not.toHaveTextContent(/misslyckade|24 timmarna/i);
  });
});
