import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useDraftMatchCount } from "./use-draft-match-count";
import type { DraftMatchCountRequest } from "@/lib/dto/match-count";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function draft(over: Partial<DraftMatchCountRequest> = {}): DraftMatchCountRequest {
  return {
    occupationGroups: [],
    regions: [],
    municipalities: [],
    employmentTypes: [],
    ...over,
  };
}

describe("useDraftMatchCount", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("hämtar count vid 200 efter debounce", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(jsonResponse({ count: 1846 }));
    const { result } = renderHook(() =>
      useDraftMatchCount(draft({ occupationGroups: ["grp_dev"] })),
    );
    await waitFor(() => expect(result.current.count).toBe(1846));
    expect(result.current.loading).toBe(false);
  });

  it("POSTar utkastets fyra dimensioner (aldrig kompetenser) till preview-routen", async () => {
    const spy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(jsonResponse({ count: 5 }));
    renderHook(() =>
      useDraftMatchCount(
        draft({ occupationGroups: ["grp_dev"], municipalities: ["kommun_0180"] }),
      ),
    );
    await waitFor(() => expect(spy).toHaveBeenCalled());

    const [url, init] = spy.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/me/match-count-preview");
    expect(init.method).toBe("POST");
    const body = JSON.parse(init.body as string) as Record<string, unknown>;
    expect(body.occupationGroups).toEqual(["grp_dev"]);
    expect(body.municipalities).toEqual(["kommun_0180"]);
    // Kompetenser/erfarenhet ingår aldrig i count-requesten.
    expect(body).not.toHaveProperty("skills");
    expect(body).not.toHaveProperty("occupationExperience");
  });

  it("degraderar till null vid non-2xx (aldrig ett falskt 0)", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(jsonResponse({}, 502));
    const { result } = renderHook(() =>
      useDraftMatchCount(draft({ occupationGroups: ["grp_dev"] })),
    );
    await waitFor(() => expect(globalThis.fetch).toHaveBeenCalled());
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.count).toBeNull();
  });

  it("refetchar när utkastets mängd ändras", async () => {
    const spy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(jsonResponse({ count: 1 }));
    const { result, rerender } = renderHook(
      ({ d }: { d: DraftMatchCountRequest }) => useDraftMatchCount(d),
      { initialProps: { d: draft({ occupationGroups: ["a"] }) } },
    );
    await waitFor(() => expect(result.current.count).toBe(1));
    rerender({ d: draft({ occupationGroups: ["a", "b"] }) });
    await waitFor(() => expect(spy).toHaveBeenCalledTimes(2));
  });

  it("fetchar inte när enabled=false (stängd modal)", async () => {
    const spy = vi.spyOn(globalThis, "fetch");
    const { result } = renderHook(() =>
      useDraftMatchCount(draft({ occupationGroups: ["a"] }), false),
    );
    // Ge debounce-fönstret gott om tid att INTE fyra.
    await new Promise((r) => setTimeout(r, 600));
    expect(spy).not.toHaveBeenCalled();
    expect(result.current.count).toBeNull();
  });

  it("refetchar INTE när bara array-referensen ändras (samma mängd)", async () => {
    const spy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(jsonResponse({ count: 1 }));
    const { result, rerender } = renderHook(
      ({ d }: { d: DraftMatchCountRequest }) => useDraftMatchCount(d),
      { initialProps: { d: draft({ occupationGroups: ["a"] }) } },
    );
    await waitFor(() => expect(result.current.count).toBe(1));
    // Ny objekt-/array-referens men identiskt innehåll → ingen ny förfrågan.
    rerender({ d: draft({ occupationGroups: ["a"] }) });
    await new Promise((r) => setTimeout(r, 600));
    expect(spy).toHaveBeenCalledTimes(1);
  });
});
