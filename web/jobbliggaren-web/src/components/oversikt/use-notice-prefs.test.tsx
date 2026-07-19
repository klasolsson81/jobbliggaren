import { describe, it, expect, beforeEach } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useNoticePrefs } from "./use-notice-prefs";

const LS_KEY = "jp-oversikt-notice-prefs";

describe("useNoticePrefs", () => {
  beforeEach(() => window.localStorage.clear());

  it("alla typer påslagna som default (saknad nyckel)", () => {
    const { result } = renderHook(() => useNoticePrefs());
    expect(result.current.isEnabled("jobads", "matches")).toBe(true);
  });

  it("toggle stänger av en påslagen typ, toggle igen slår på den", () => {
    const { result } = renderHook(() => useNoticePrefs());
    act(() => result.current.toggle("jobads", "matches"));
    expect(result.current.isEnabled("jobads", "matches")).toBe(false);
    act(() => result.current.toggle("jobads", "matches"));
    expect(result.current.isEnabled("jobads", "matches")).toBe(true);
  });

  it("persisterar under source:type-nyckeln med värdet false", () => {
    const { result } = renderHook(() => useNoticePrefs());
    act(() => result.current.toggle("applications", "offers"));
    const stored = JSON.parse(window.localStorage.getItem(LS_KEY) ?? "{}");
    expect(stored["applications:offers"]).toBe(false);
  });

  it("en toggle påverkar bara den egna typen", () => {
    const { result } = renderHook(() => useNoticePrefs());
    act(() => result.current.toggle("jobads", "deadlines"));
    expect(result.current.isEnabled("jobads", "deadlines")).toBe(false);
    expect(result.current.isEnabled("jobads", "matches")).toBe(true);
  });

  it("degraderar till allt-påslaget vid korrupt JSON", () => {
    window.localStorage.setItem(LS_KEY, "inte-json[");
    const { result } = renderHook(() => useNoticePrefs());
    expect(result.current.isEnabled("companies", "followedads")).toBe(true);
  });
});
