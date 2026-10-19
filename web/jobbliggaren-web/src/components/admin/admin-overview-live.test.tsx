import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, cleanup } from "@testing-library/react";
import { overviewSnapshotFixture, OVERVIEW_TIME } from "@/test/fixtures/admin-overview";
import type { HostObservationDto } from "@/lib/dto/admin-host";
import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import { AdminOverviewLive } from "./admin-overview-live";

const NOW = Date.parse(OVERVIEW_TIME);
let hidden: boolean;
const fetchMock = vi.fn();
beforeEach(() => {
  hidden = false;
  vi.useFakeTimers();
  vi.setSystemTime(NOW);
  vi.spyOn(document, "hidden", "get").mockImplementation(() => hidden);
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => { cleanup(); vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });

async function visibility(value: boolean) {
  hidden = value;
  await act(async () => { document.dispatchEvent(new Event("visibilitychange")); });
}
/**
 * The fixture with the host and the services sampled just now. Both samplers take a new sample every 30 s, so in a
 * test that advances the clock a reply carrying the fixture's fixed sample time would be a sampler that has stopped.
 */
function sampledNow(snapshot: ReturnType<typeof overviewSnapshotFixture>) {
  const sampledAt = new Date(Date.now()).toISOString();
  const host = snapshot.host.kind === "loaded" ? snapshot.host : null;
  const services = snapshot.services.kind === "loaded" ? snapshot.services : null;
  if (!host || !services) return snapshot;
  const data: HostObservationDto = {
    ...host.data,
    cpu: { ...host.data.cpu, sampledAt },
    memory: { ...host.data.memory, sampledAt },
    disk: { ...host.data.disk, sampledAt },
  };
  const rows: ServicesObservationDto = {
    ...services.data,
    backgroundJobs: { ...services.data.backgroundJobs, sampledAt },
    volatileStore: { ...services.data.volatileStore, sampledAt },
    jobImport: { ...services.data.jobImport, sampledAt },
  };
  return { ...snapshot, host: { ...host, data, sampledAt }, services: { ...services, data: rows, sampledAt } };
}
function show() { return render(<AdminOverviewLive initial={overviewSnapshotFixture()} initialNow={NOW} />); }

describe("admin overview visible refresh", () => {
  it("uses SSR data initially and refreshes only after sixty seconds", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(overviewSnapshotFixture())));
    show();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(fetchMock).toHaveBeenCalledWith("/api/admin/oversikt", expect.objectContaining({ cache: "no-store", signal: expect.any(AbortSignal) }));
  });
  it.each([401, 403])("clears all privileged data and stops refresh on %i", async (status) => {
    fetchMock.mockResolvedValue(new Response(null, { status }));
    show();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(screen.queryByRole("region", { name: "Användare totalt" })).toBeNull();
    expect(screen.queryByText("AccountSuspendedEvent")).toBeNull();
    expect(screen.getByRole("alert")).toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    await act(async () => { await vi.advanceTimersByTimeAsync(120_000); });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
  it("retains ordinary failed source data with its failed-refresh notice", async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 502 }));
    show();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(screen.getByRole("region", { name: "Användare totalt" })).toHaveTextContent("12");
    expect(screen.getByRole("region", { name: "Användare totalt" })).toHaveTextContent("Uppdateringen misslyckades");
  });
  it("avoids overlap, aborts hidden requests, refreshes on return and aborts on unmount", async () => {
    const signals: AbortSignal[] = [];
    fetchMock.mockImplementation((_path: string, init: { signal: AbortSignal }) => {
      signals.push(init.signal);
      return new Promise((_resolve, reject) => init.signal.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")), { once: true }));
    });
    const mounted = show();
    await act(async () => { await vi.advanceTimersByTimeAsync(180_000); });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    await visibility(true);
    expect(signals[0]?.aborted).toBe(true);
    await act(async () => { await vi.advanceTimersByTimeAsync(180_000); });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    await visibility(false);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    mounted.unmount();
    expect(signals[1]?.aborted).toBe(true);
  });
  it("announces a partial refresh failure once and its recovery through one polite status region", async () => {
    const partial = () => ({ ...sampledNow(overviewSnapshotFixture()), audit: { kind: "failed" } });
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(partial())));
    show();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("aria-live", "polite");
    expect(status).toHaveAttribute("aria-atomic", "true");
    expect(status).toHaveTextContent("Granskningshändelser: Uppdateringen misslyckades.");
    expect(status).not.toHaveTextContent("Kontostatistik");
    expect(status).not.toHaveTextContent("Misslyckade bakgrundsjobb");
    expect(screen.getByText("AccountSuspendedEvent")).toBeInTheDocument();

    const mutations: MutationRecord[] = [];
    const observer = new MutationObserver((records) => mutations.push(...records));
    observer.observe(status, { childList: true, characterData: true, subtree: true });
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(partial())));
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(mutations).toEqual([]);

    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(sampledNow(overviewSnapshotFixture()))));
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(status).toHaveTextContent("Granskningshändelser: Uppgifterna är aktuella.");
    expect(mutations.length).toBeGreaterThan(0);
    observer.disconnect();
  });
  it("shows a host that has not reported, then announces the Backup source once it does (#1982)", async () => {
    const waiting = { ...overviewSnapshotFixture(), backup: { kind: "awaiting" } };
    render(<AdminOverviewLive initial={waiting as ReturnType<typeof overviewSnapshotFixture>} initialNow={NOW} />);
    const card = screen.getByRole("region", { name: "Backup" });
    expect(card).toHaveTextContent("Servern har inte rapporterat ännu.");
    expect(screen.getByRole("status")).toBeEmptyDOMElement();

    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(overviewSnapshotFixture())));
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });

    expect(screen.getByRole("status")).toHaveTextContent("Backup: Uppgifterna är aktuella.");
    expect(screen.getByRole("region", { name: "Backup" })).toHaveTextContent("2026-10-08 02:19");
  });
  it("keeps the last Backup value when a refresh fails, marks it at once, and announces it", async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 502 }));
    show();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });

    const card = screen.getByRole("region", { name: "Backup" });
    expect(card).toHaveTextContent("2026-10-08 02:19");
    expect(card).toHaveTextContent("Uppdateringen misslyckades. Senaste uppgiften visas.");
    expect(screen.getByRole("status")).toHaveTextContent("Backup: Uppdateringen misslyckades.");
  });
  it("replaces a Backup value with the host's own news that it has gone quiet, rather than keeping it as if refreshed", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ ...overviewSnapshotFixture(), backup: { kind: "awaiting" } })));
    show();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });

    const card = screen.getByRole("region", { name: "Backup" });
    expect(card).toHaveTextContent("Servern har inte rapporterat ännu.");
    expect(card).not.toHaveTextContent("2026-10-08 02:19");
    expect(screen.getByRole("status")).toHaveTextContent("Backup: Servern har inte rapporterat ännu.");
  });
  it("announces the five-minute age transition without duplicating the account-card notices", async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 502 }));
    show();
    await act(async () => { await vi.advanceTimersByTimeAsync(360_000); });
    const status = screen.getByRole("status");
    expect(status).toHaveTextContent("Kontostatistik: Uppdateringen misslyckades. Senaste uppgiften visas. Uppgiften är äldre än fem minuter.");
    expect(status.textContent?.match(/Kontostatistik/g)).toHaveLength(1);
    expect(status.textContent?.match(/Granskningshändelser/g)).toHaveLength(1);
    expect(status.textContent?.match(/Misslyckade bakgrundsjobb/g)).toHaveLength(1);
    expect(screen.getByRole("region", { name: "Användare totalt" })).toHaveTextContent("12");
  });
  it("announces the Server's age in its own words when the sampler goes quiet (#1982)", async () => {
    // The actor: the sampler stopping, so the API answers Stale with the readings' own, old, sample time.
    const old = new Date(NOW - 120_000).toISOString();
    const quiet = overviewSnapshotFixture();
    if (quiet.host.kind !== "loaded") throw new Error("Expected a loaded host");
    const data: HostObservationDto = {
      ...quiet.host.data,
      cpu: { ...quiet.host.data.cpu, state: "Stale", sampledAt: old },
      memory: { ...quiet.host.data.memory, state: "Stale", sampledAt: old },
      disk: { ...quiet.host.data.disk, state: "Stale", sampledAt: old },
    };
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ ...quiet, host: { ...quiet.host, data, sampledAt: old } })));
    show();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });

    const status = screen.getByRole("status");
    expect(status).toHaveTextContent("Serverns mätvärden: Mätningen är äldre än 2 minuter.");
    expect(status).not.toHaveTextContent("fem minuter");
    expect(screen.getByRole("region", { name: "Server" })).toHaveTextContent("Mätningen är äldre än 2 minuter.");
  });
  it("announces a kept Server reading that passes the API's limit, in the card's own words (#1982)", async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 502 }));
    show();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });

    const status = screen.getByRole("status");
    // Only the Server and Tjänster crossed a limit in the second refresh, each its own; the other sources keep their
    // five minutes.
    expect(status).toHaveTextContent("Serverns mätvärden: Uppdateringen misslyckades. Senaste uppgiften visas. Mätningen är äldre än 2 minuter.");
    // Every services row has been withdrawn, so no last value is shown, and the live region says what the card says.
    expect(status).toHaveTextContent("Tjänster: Uppgifterna kunde inte hämtas. Försök igen om en stund. Uppgiften är äldre än 2 minuter.");
    expect(status.textContent).not.toMatch(/Tjänster: Uppdateringen misslyckades/);
    expect(status).not.toHaveTextContent("Kontostatistik");
    expect(status).not.toHaveTextContent("fem minuter");
    expect(screen.getByRole("region", { name: "Server" })).toHaveTextContent("Mätningen är äldre än 2 minuter.");
    // A kept services row past the limit is withdrawn, never shown as an old verdict; the note still dates it.
    const services = screen.getByRole("region", { name: "Tjänster" });
    expect(services).toHaveTextContent("Uppgiften är äldre än 2 minuter.");
    expect(services).toHaveTextContent("Worker– Kunde inte avgörasTillfälligt lager– Kunde inte avgörasAnnonsimport– Kunde inte avgöras");
    expect(services).not.toHaveTextContent(/Senaste livstecken|Senaste körning|Svarar/);
    expect(services).not.toHaveTextContent("Senaste uppgiften visas");
    expect(services).toHaveTextContent("Uppgifterna kunde inte hämtas. Försök igen om en stund.");
  });
  it("keeps the last services rows when a refresh fails, marks the failure at once and announces it (#1982)", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ ...sampledNow(overviewSnapshotFixture()), services: { kind: "failed" } })));
    show();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });

    const card = screen.getByRole("region", { name: "Tjänster" });
    expect(card).toHaveTextContent("Senaste livstecken 2026-10-08 11:59");
    expect(card).toHaveTextContent("Uppdateringen misslyckades. Senaste uppgiften visas.");
    expect(card).not.toHaveTextContent("äldre än");
    expect(screen.getByRole("status")).toHaveTextContent("Tjänster: Uppdateringen misslyckades. Senaste uppgiften visas.");
    expect(screen.getByRole("status")).not.toHaveTextContent("Serverns mätvärden");
  });
  it("announces the provider's own failure on the e-post card in the card's words, and its recovery (#1981)", async () => {
    const fresh = sampledNow(overviewSnapshotFixture());
    if (fresh.email.kind !== "loaded") throw new Error("Expected a loaded e-post source");
    const failed = { ...fresh, email: { ...fresh.email, sampledAt: new Date(NOW + 60_000).toISOString(), data: {
      ...fresh.email.data,
      usage: { status: "Failed" as const, reason: "Timeout" as const, observedAt: null, month: null, sent: null, billable: null,
        estimatedExtraCostOre: null, aboveHalfOfSendLimit: null, webhooksCount: null },
    } } };
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(failed)));
    show();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });

    expect(screen.getByRole("status")).toHaveTextContent("Utskick mot gratiskvoten: Scaleway svarade inte. Försök igen om en stund.");
    expect(screen.getByRole("region", { name: "E-post" })).toHaveTextContent("Scaleway svarade inte.");

    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(fresh)));
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(screen.getByRole("status")).toHaveTextContent("Utskick mot gratiskvoten: Uppgifterna är aktuella.");
  });
  it.each([
    [
      { status: "NotObservable" as const, reason: null },
      "Misslyckade utskick: Scaleway visar inga utskick för läsnyckeln de senaste 32 dagarna, så siffrorna kan inte bedömas.",
    ],
    [
      { status: "ReadKeyMissing" as const, reason: null },
      "Misslyckade utskick: Läsnyckeln är inte installerad, så uppgifterna kan inte hämtas.",
    ],
    [
      { status: "Failed" as const, reason: "Forbidden" as const },
      "Misslyckade utskick: Scaleway nekade läsningen. Kontrollera läsnyckelns behörighet",
    ],
  ])("announces the failed sends' unread state %o in the attention line's words (#1981 PR 2)", async (state, announced) => {
    const fresh = sampledNow(overviewSnapshotFixture());
    const unread = { ...fresh, delivery: {
      kind: "loaded" as const, sampledAt: new Date(NOW + 60_000).toISOString(), refreshFailed: false,
      data: { period: "h24" as const, ...state, observedAt: null, sent: null, failed: null },
    } };
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(unread)));
    show();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });

    expect(screen.getByRole("status")).toHaveTextContent(announced);
  });
  it("announces a missing read key on the e-post card as its own state, never as not counted (#1981 PR 2)", async () => {
    const fresh = sampledNow(overviewSnapshotFixture());
    if (fresh.email.kind !== "loaded") throw new Error("Expected a loaded e-post source");
    const missing = { ...fresh, email: { ...fresh.email, sampledAt: new Date(NOW + 60_000).toISOString(), data: {
      ...fresh.email.data,
      usage: { status: "ReadKeyMissing" as const, reason: null, observedAt: null, month: null, sent: null, billable: null,
        estimatedExtraCostOre: null, aboveHalfOfSendLimit: null, webhooksCount: null },
      plan: { status: "ReadKeyMissing" as const, reason: null, active: null, matches: null },
    } } };
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(missing)));
    show();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });

    expect(screen.getByRole("status")).toHaveTextContent("Utskick mot gratiskvoten: Läsnyckeln är inte installerad");
    expect(screen.getByRole("status")).not.toHaveTextContent("Räknas inte");
  });
  it("replaces kept services rows with the API's own news once it answers again (#1982)", async () => {
    const quiet = sampledNow(overviewSnapshotFixture());
    if (quiet.services.kind !== "loaded") throw new Error("Expected loaded services");
    const silent = { ...quiet, services: { ...quiet.services, data: {
      ...quiet.services.data,
      backgroundJobs: { state: "Silent" as const, sampledAt: quiet.services.sampledAt, heartbeatAt: "2026-10-08T09:55:00Z" },
    } } };
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(silent)));
    show();
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });

    const card = screen.getByRole("region", { name: "Tjänster" });
    expect(card).toHaveTextContent("Inget livstecken sedan 2026-10-08 11:55");
    expect(card).not.toHaveTextContent("Senaste livstecken");
  });
});