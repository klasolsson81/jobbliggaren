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
});