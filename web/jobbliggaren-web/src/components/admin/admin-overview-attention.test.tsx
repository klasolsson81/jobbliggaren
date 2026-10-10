import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { overviewSnapshotFixture, OVERVIEW_TIME } from "@/test/fixtures/admin-overview";
import type { AdminOverviewSnapshot, BackupObservationData } from "@/lib/dto/admin-overview";
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

  it.each([
    [{ backgroundJobs: { state: "Silent", sampledAt: SAMPLED, heartbeatAt: "2026-10-08T09:50:00Z" } }, "Worker: Inget livstecken sedan 2026-10-08 11:50"],
    [{ backgroundJobs: { state: "NoServer", sampledAt: SAMPLED, heartbeatAt: null } }, "Worker: Inget livstecken registrerat"],
    [{ volatileStore: { state: "Unreachable", sampledAt: SAMPLED } }, "Tillfälligt lager: Svarar inte"],
    [{ jobImport: { state: "Overdue", sampledAt: SAMPLED, lastRunAt: "2026-10-08T09:20:00Z", errors: 0 } }, "Annonsimport: Senaste körning 2026-10-08 11:20, äldre än 25 minuter"],
    [{ jobImport: { state: "NoneRecorded", sampledAt: SAMPLED, lastRunAt: null, errors: null } }, "Annonsimport: Ingen körning registrerad"],
  ] as const)("raises the edge and words %j as the Tjänster card does", (patch, name) => {
    show(withServices(patch));

    expect(attention()).toHaveAttribute("data-state", "raised");
    expect(within(attention()).getByRole("link", { name })).toHaveAttribute("href", "#admin-overview-services");
    expect(document.getElementById("admin-overview-services")).toHaveTextContent("Tjänster");
  });

  it("does not raise the edge for errors in a recorded run, which the Tjänster card itself marks", () => {
    show(withServices({ jobImport: { state: "Recorded", sampledAt: SAMPLED, lastRunAt: "2026-10-08T09:50:00Z", errors: 3 } }));

    expect(attention()).toHaveAttribute("data-state", "unknown");
    expect(within(attention()).queryAllByRole("link")).toEqual([]);
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
