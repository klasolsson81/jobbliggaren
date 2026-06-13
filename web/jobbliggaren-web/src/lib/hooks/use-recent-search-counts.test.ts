import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useRecentSearchCounts } from "./use-recent-search-counts";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("useRecentSearchCounts", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("returnerar null och hämtar inte när enabled=false", () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch");
    const { result } = renderHook(() => useRecentSearchCounts(false));
    expect(result.current).toBeNull();
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it("bygger en id→count-map vid 200 + giltigt svar", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      jsonResponse([
        { id: "a1", currentCount: 42, newCount: 0 },
        { id: "a2", currentCount: 8, newCount: 3 },
      ]),
    );
    const { result } = renderHook(() => useRecentSearchCounts(true));
    await waitFor(() => expect(result.current).not.toBeNull());
    expect(result.current?.get("a1")).toEqual({ currentCount: 42, newCount: 0 });
    expect(result.current?.get("a2")).toEqual({ currentCount: 8, newCount: 3 });
  });

  it("degraderar till null vid non-2xx (aldrig falsk (0))", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(jsonResponse({}, 502));
    const { result } = renderHook(() => useRecentSearchCounts(true));
    // Ge effekten en tick att köra; resultatet ska förbli null.
    await waitFor(() =>
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/me/recent-searches/counts",
        expect.objectContaining({ signal: expect.any(AbortSignal) }),
      ),
    );
    expect(result.current).toBeNull();
  });

  it("degraderar till null vid shape-mismatch", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      jsonResponse([{ id: "a1", currentCount: -1, newCount: 0 }]),
    );
    const { result } = renderHook(() => useRecentSearchCounts(true));
    await waitFor(() => expect(globalThis.fetch).toHaveBeenCalled());
    expect(result.current).toBeNull();
  });
});
