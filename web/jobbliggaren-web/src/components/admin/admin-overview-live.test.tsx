import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, cleanup } from "@testing-library/react";
import { overviewSnapshotFixture, OVERVIEW_TIME } from "@/test/fixtures/admin-overview";
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
    const partial = { ...overviewSnapshotFixture(), audit: { kind: "failed" } };
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(partial)));
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
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(partial)));
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(mutations).toEqual([]);

    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(overviewSnapshotFixture())));
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
});