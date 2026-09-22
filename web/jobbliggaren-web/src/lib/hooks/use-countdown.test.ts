import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useCountdown } from "./use-countdown";

describe("useCountdown", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("counts whole seconds down to zero and stops there", () => {
    const { result } = renderHook(() => useCountdown(2, 1));
    expect(result.current).toBe(2);

    act(() => vi.advanceTimersByTime(1000));
    expect(result.current).toBe(1);

    act(() => vi.advanceTimersByTime(1000));
    expect(result.current).toBe(0);

    act(() => vi.advanceTimersByTime(5000));
    expect(result.current).toBe(0);
  });

  it("starts again from the initial seconds when the key changes", () => {
    const { result, rerender } = renderHook(
      ({ seconds, key }) => useCountdown(seconds, key),
      { initialProps: { seconds: 3, key: 1 } }
    );
    act(() => vi.advanceTimersByTime(3000));
    expect(result.current).toBe(0);

    rerender({ seconds: 60, key: 2 });

    expect(result.current).toBe(60);
  });

  it("reads new initial seconds only with a new key: the running count is not reset by a re-render", () => {
    const { result, rerender } = renderHook(
      ({ seconds, key }) => useCountdown(seconds, key),
      { initialProps: { seconds: 10, key: 1 } }
    );
    act(() => vi.advanceTimersByTime(4000));

    rerender({ seconds: 10, key: 1 });

    expect(result.current).toBe(6);
  });

  it("does not tick at all when it starts at zero", () => {
    const { result } = renderHook(() => useCountdown(0, 1));

    act(() => vi.advanceTimersByTime(3000));

    expect(result.current).toBe(0);
    expect(vi.getTimerCount()).toBe(0);
  });
});
