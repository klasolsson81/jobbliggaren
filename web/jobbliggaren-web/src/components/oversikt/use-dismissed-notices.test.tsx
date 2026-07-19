import { describe, it, expect, beforeEach } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useDismissedNotices } from "./use-dismissed-notices";

const LS_KEY = "jp-oversikt-dismissed-notices";

describe("useDismissedNotices", () => {
  beforeEach(() => window.localStorage.clear());

  it("dismiss lägger till ett id och speglar det i `dismissed` + localStorage", () => {
    const { result } = renderHook(() => useDismissedNotices());
    expect(result.current.dismissed.has("a")).toBe(false);
    act(() => result.current.dismiss("a"));
    expect(result.current.dismissed.has("a")).toBe(true);
    expect(
      JSON.parse(window.localStorage.getItem(LS_KEY) ?? "[]"),
    ).toContain("a");
  });

  it("dismissMany lägger till flera id i en skrivning", () => {
    const { result } = renderHook(() => useDismissedNotices());
    act(() => result.current.dismissMany(["a", "b", "c"]));
    expect(result.current.dismissed.has("a")).toBe(true);
    expect(result.current.dismissed.has("b")).toBe(true);
    expect(result.current.dismissed.has("c")).toBe(true);
  });

  it("dismissMany med tom lista är en no-op", () => {
    const { result } = renderHook(() => useDismissedNotices());
    act(() => result.current.dismissMany([]));
    expect(result.current.dismissed.size).toBe(0);
    expect(window.localStorage.getItem(LS_KEY)).toBeNull();
  });

  it("restore tar bort ett id (av-markerar)", () => {
    const { result } = renderHook(() => useDismissedNotices());
    act(() => result.current.dismiss("a"));
    expect(result.current.dismissed.has("a")).toBe(true);
    act(() => result.current.restore("a"));
    expect(result.current.dismissed.has("a")).toBe(false);
  });

  it("hydrerar från befintlig localStorage", () => {
    window.localStorage.setItem(LS_KEY, JSON.stringify(["x", "y"]));
    const { result } = renderHook(() => useDismissedNotices());
    expect(result.current.dismissed.has("x")).toBe(true);
    expect(result.current.dismissed.has("y")).toBe(true);
  });

  it("degraderar till tom mängd vid korrupt JSON", () => {
    window.localStorage.setItem(LS_KEY, "{inte json");
    const { result } = renderHook(() => useDismissedNotices());
    expect(result.current.dismissed.size).toBe(0);
  });
});
