import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { backupFixture, overviewSnapshotFixture } from "@/test/fixtures/admin-overview";
import type { AdminOverviewSnapshot, BackupObservationData } from "@/lib/dto/admin-overview";
import { AdminOverview } from "./admin-overview";

// The fixture's run ended 2026-10-08 00:19:07Z, which is 02:19 in Stockholm; its timer is armed for the
// next night. "Now" is always passed in, so every age below is exact.
const SAMPLED = "2026-10-08T09:58:02Z";
const NOW = Date.parse(SAMPLED) + 28_000;
const card = () => screen.getByRole("region", { name: "Backup" });

function show(backup: AdminOverviewSnapshot["backup"], now = NOW) {
  return render(<AdminOverview observations={{ ...overviewSnapshotFixture(), backup }} now={now} />);
}
function observed(data: Partial<BackupObservationData> = {}, extra: { refreshFailed?: boolean } = {}): AdminOverviewSnapshot["backup"] {
  return { kind: "loaded", sampledAt: SAMPLED, refreshFailed: extra.refreshFailed ?? false, data: { ...backupFixture(), ...data } };
}

describe("the Backup card", () => {
  it("shows the last run's end and age, the next scheduled run, and when the host looked", () => {
    show(observed());

    expect(card()).toHaveTextContent("Senaste lyckade körning2026-10-08 02:19 (för 10 timmar sedan)");
    expect(card()).toHaveTextContent("Nästa planerade körning2026-10-09 02:17");
    expect(card()).toHaveTextContent("Uppgift från 2026-10-08 11:58");
    expect(card()).not.toHaveTextContent("Äldre än 26 timmar");
    expect(card()).not.toHaveTextContent("äldre än fem minuter");
  });

  it("keeps Extern kopia and Behålls, and says plainly that nothing observes them", () => {
    show(observed());

    for (const label of ["Extern kopia", "Behålls"]) {
      const value = within(card()).getByText(label).nextElementSibling;
      expect(value).toHaveTextContent("Saknar verifierad datakälla");
    }
  });

  it("does not present a successful run as proof that a restore works", () => {
    show(observed());

    expect(card()).toHaveTextContent("inte att en återställning fungerar");
    // The only place the word may appear is the line that says no verified source exists.
    expect((card().textContent ?? "").replaceAll("Saknar verifierad datakälla", "")).not.toMatch(/verifierad/i);
    expect(card().textContent).not.toMatch(/Fungerar|Frisk|OK\b/);
    expect(card().textContent).not.toMatch(/\d\s?%/);
  });

  it("marks a run older than twenty-six hours without hiding when it was", () => {
    show(observed({ lastSuccess: { state: "recorded", completedAt: "2026-10-06T00:19:07Z", overdue: true } }));

    expect(card()).toHaveTextContent("2026-10-06 02:19");
    expect(within(card()).getByText("Äldre än 26 timmar")).toHaveClass("jp-pill--warning");
  });

  it.each([
    ["no stamp", { lastSuccess: { state: "missing" } }, "Ingen lyckad körning registrerad"],
    ["an unreadable stamp", { lastSuccess: { state: "unreadable" } }, "Uppgiften kunde inte läsas"],
    ["an invalid stamp", { lastSuccess: { state: "invalid" } }, "Uppgiften är ogiltig"],
  ] as const)("says %s in words and invents no time", (_name, data, words) => {
    show(observed(data));

    expect(card()).toHaveTextContent(words);
    expect(card()).not.toHaveTextContent(/2026-10-0\d 0\d:\d\d \(/);
  });

  it.each([
    ["a disabled timer", { timer: { state: "inactive" } }, "Ingen körning planerad"],
    ["a timer that is not installed", { timer: { state: "notInstalled" } }, "Schemat finns inte på värden"],
  ] as const)("says %s in words and invents no date", (_name, data, words) => {
    show(observed(data));

    expect(within(card()).getByText("Nästa planerade körning").nextElementSibling).toHaveTextContent(words);
  });

  it("shows an unknown timer as an unknown value, never as a date or a zero", () => {
    show(observed({ timer: { state: "unknown" } }));

    const next = within(card()).getByText("Nästa planerade körning").nextElementSibling;
    expect(next).toHaveTextContent("Uppgift saknas");
    expect(next?.textContent).not.toMatch(/\d{4}-\d{2}-\d{2}/);
  });

  it("shows the state the box is in until the backup is switched on, as plainly as any other", () => {
    show(observed({ lastSuccess: { state: "missing" }, timer: { state: "inactive" } }));

    expect(card()).toHaveTextContent("Ingen lyckad körning registrerad");
    expect(card()).toHaveTextContent("Ingen körning planerad");
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("drops the run's age, and marks the observation old, when the host has stopped reporting", () => {
    show(observed(), Date.parse(SAMPLED) + 300_001);

    expect(card()).toHaveTextContent("Senaste lyckade körning2026-10-08 02:19");
    expect(card().textContent).not.toMatch(/sedan/);
    expect(card()).toHaveTextContent("Uppgift från 2026-10-08 11:58");
    expect(card()).toHaveTextContent("Uppgiften är äldre än fem minuter.");
  });

  it("trusts the API's own judgement that the sample is old even when the browser clock disagrees", () => {
    show(observed({ stale: true }));

    expect(card().textContent).not.toMatch(/sedan/);
  });

  it("keeps the last good value at once and says the refresh failed", () => {
    show(observed({}, { refreshFailed: true }));

    expect(card()).toHaveTextContent("2026-10-08 02:19");
    expect(card()).toHaveTextContent("Uppdateringen misslyckades. Senaste uppgiften visas.");
  });

  it("tells a host that has not reported apart from a failure and from a capability that is not built", () => {
    show({ kind: "awaiting" });

    expect(card()).toHaveTextContent("Värden har inte rapporterat någon observation ännu.");
    expect(card()).not.toHaveTextContent("Kommer snart");
    expect(card()).not.toHaveTextContent("kunde inte hämtas");
    expect(within(card()).getByText("Nästa planerade körning").nextElementSibling).toHaveTextContent("Uppgift saknas");
    expect(screen.queryByRole("alert")).toBeNull();
    expect(within(card()).queryByText("Uppgift från", { exact: false })).toBeNull();
  });

  it("shows a failed source in the card and announces it once for the page", () => {
    show({ kind: "failed" });

    expect(card()).toHaveTextContent("Uppgifterna kunde inte hämtas");
    expect(within(card()).queryByRole("alert")).toBeNull();
    expect(screen.getAllByRole("alert")).toHaveLength(1);
    expect(within(card()).getByText("Nästa planerade körning").nextElementSibling).toHaveTextContent("Uppgift saknas");
  });

  it("shows loading as a status line and no value", () => {
    show({ kind: "loading" });

    expect(card()).toHaveTextContent("Hämtar uppgifter");
    expect(within(card()).queryByRole("status")).toBeNull();
    expect(card().textContent).not.toMatch(/\d{4}-\d{2}-\d{2}/);
  });

  it("leaves every other card exactly as it was", () => {
    show(observed());

    expect(screen.getByRole("region", { name: "Användare totalt" })).toHaveTextContent("12");
    expect(screen.getByRole("region", { name: "Server" })).toHaveTextContent("5,0 %");
  });
});
