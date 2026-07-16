import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useCriterionPreviewCount } from "./use-criterion-preview-count";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const bothAxes = { sniCodes: ["62010"], municipalityCodes: ["0180"] };

describe("useCriterionPreviewCount", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("hämtar magnitude vid 200 efter debounce (båda axlar valda)", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      jsonResponse({ magnitude: 412, saturated: false }),
    );
    const { result } = renderHook(() => useCriterionPreviewCount(bothAxes));

    await waitFor(() =>
      expect(result.current.preview).toEqual({ magnitude: 412, saturated: false }),
    );
    expect(result.current.loading).toBe(false);
  });

  it("POSTar båda kod-axlarna till preview-routen", async () => {
    const spy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(jsonResponse({ magnitude: 5, saturated: false }));
    renderHook(() => useCriterionPreviewCount(bothAxes));

    await waitFor(() => expect(spy).toHaveBeenCalled());
    const [url, init] = spy.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("/api/me/criterion-preview-count");
    expect(init.method).toBe("POST");
    const body = JSON.parse(init.body as string) as Record<string, unknown>;
    expect(body.sniCodes).toEqual(["62010"]);
    expect(body.municipalityCodes).toEqual(["0180"]);
  });

  it("propagerar saturated (10 000+) oförändrat", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      jsonResponse({ magnitude: 10000, saturated: true }),
    );
    const { result } = renderHook(() => useCriterionPreviewCount(bothAxes));

    await waitFor(() =>
      expect(result.current.preview).toEqual({ magnitude: 10000, saturated: true }),
    );
  });

  it("anropar ALDRIG endpointen när SNI-axeln är tom (API 400:ar en saknad axel)", async () => {
    const spy = vi.spyOn(globalThis, "fetch");
    const { result } = renderHook(() =>
      useCriterionPreviewCount({ sniCodes: [], municipalityCodes: ["0180"] }),
    );

    await new Promise((r) => setTimeout(r, 600));
    expect(spy).not.toHaveBeenCalled();
    expect(result.current.preview).toBeNull();
    expect(result.current.loading).toBe(false);
  });

  it("anropar ALDRIG endpointen när kommun-axeln är tom", async () => {
    const spy = vi.spyOn(globalThis, "fetch");
    renderHook(() =>
      useCriterionPreviewCount({ sniCodes: ["62010"], municipalityCodes: [] }),
    );

    await new Promise((r) => setTimeout(r, 600));
    expect(spy).not.toHaveBeenCalled();
  });

  it("degraderar till null vid non-2xx (aldrig ett falskt 0)", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(jsonResponse({}, 502));
    const { result } = renderHook(() => useCriterionPreviewCount(bothAxes));

    await waitFor(() => expect(globalThis.fetch).toHaveBeenCalled());
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.preview).toBeNull();
  });

  it("fetchar inte när enabled=false (stängd dialog)", async () => {
    const spy = vi.spyOn(globalThis, "fetch");
    const { result } = renderHook(() => useCriterionPreviewCount(bothAxes, false));

    await new Promise((r) => setTimeout(r, 600));
    expect(spy).not.toHaveBeenCalled();
    expect(result.current.preview).toBeNull();
  });

  it("refetchar INTE när bara array-referensen ändras (samma mängd)", async () => {
    const spy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(jsonResponse({ magnitude: 1, saturated: false }));
    const { result, rerender } = renderHook(
      ({ d }: { d: typeof bothAxes }) => useCriterionPreviewCount(d),
      { initialProps: { d: { sniCodes: ["62010"], municipalityCodes: ["0180"] } } },
    );
    await waitFor(() => expect(result.current.preview).not.toBeNull());
    rerender({ d: { sniCodes: ["62010"], municipalityCodes: ["0180"] } });
    await new Promise((r) => setTimeout(r, 600));
    expect(spy).toHaveBeenCalledTimes(1);
  });
});
