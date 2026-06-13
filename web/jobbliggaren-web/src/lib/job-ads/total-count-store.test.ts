import { describe, it, expect, beforeEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import {
  publishTotalCount,
  useTotalCount,
  resetTotalCountForTest,
} from "./total-count-store";

// E2c (CTO VAL 2) — totalCount-delning mellan toolbar-ön (publicerar) och
// hero-öns "Visa N annonser"-knapp (prenumererar). SPOT: talet ägs av
// PagedResult.TotalCount.
describe("total-count-store", () => {
  beforeEach(() => {
    resetTotalCountForTest();
  });

  it("startar som null (inget list-svar publicerat)", () => {
    const { result } = renderHook(() => useTotalCount());
    expect(result.current).toBeNull();
  });

  it("publicering når prenumeranten", () => {
    const { result } = renderHook(() => useTotalCount());
    act(() => publishTotalCount(1234));
    expect(result.current).toBe(1234);
  });

  it("senaste publicering vinner", () => {
    const { result } = renderHook(() => useTotalCount());
    act(() => {
      publishTotalCount(10);
      publishTotalCount(42);
    });
    expect(result.current).toBe(42);
  });

  it("oförändrat värde re-notifierar inte (referensstabilt snapshot)", () => {
    const { result } = renderHook(() => useTotalCount());
    act(() => publishTotalCount(7));
    const first = result.current;
    act(() => publishTotalCount(7));
    expect(result.current).toBe(first);
  });
});
