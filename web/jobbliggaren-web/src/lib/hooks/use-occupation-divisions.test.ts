import { describe, it, expect, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useOccupationDivisions } from "./use-occupation-divisions";
import type { OccupationDivisions } from "@/lib/dto/company-criteria";
import type { OccupationDivisionsResolver } from "@/lib/company-criteria/resolve-occupation-divisions";

const answer = (word: string): OccupationDivisions => ({
  word,
  occupations: [
    {
      occupationGroupConceptId: "og-" + word,
      label: word,
      matchedOn: word,
      state: "tooFewAds",
      totalAds: 3,
      divisions: null,
      belowThresholdAdCount: null,
      belowThresholdSharePercent: null,
      withoutDivisionAdCount: null,
      withoutDivisionSharePercent: null,
      profiledAt: "2026-09-14T03:35:00+00:00",
    },
  ],
});

describe("useOccupationDivisions", () => {
  it("resolves the word after the debounce and exposes the answer for that word", async () => {
    const resolve = vi.fn<OccupationDivisionsResolver>(async (word) => answer(word));
    const { result } = renderHook(() => useOccupationDivisions("sjuksköterska", resolve));

    expect(result.current).toBeNull();
    await waitFor(() => expect(result.current?.word).toBe("sjuksköterska"));
    expect(resolve).toHaveBeenCalledTimes(1);
  });

  it("is inert without a resolver and below the two-character floor", async () => {
    const resolve = vi.fn<OccupationDivisionsResolver>(async (word) => answer(word));
    const none = renderHook(() => useOccupationDivisions("sjuksköterska", undefined));
    const short = renderHook(() => useOccupationDivisions("s", resolve));

    await new Promise((r) => setTimeout(r, 500));
    expect(none.result.current).toBeNull();
    expect(short.result.current).toBeNull();
    expect(resolve).not.toHaveBeenCalled();
  });

  it("never exposes a previous word's answer once the word has changed", async () => {
    const resolve = vi.fn<OccupationDivisionsResolver>(async (word) => answer(word));
    const { result, rerender } = renderHook(
      ({ word }: { word: string }) => useOccupationDivisions(word, resolve),
      { initialProps: { word: "sjuksköterska" } },
    );
    await waitFor(() => expect(result.current?.word).toBe("sjuksköterska"));

    rerender({ word: "sjuksköterskan" });
    // Synchronously after the change: the old answer is gone, not lingering for the debounce.
    expect(result.current).toBeNull();
    await waitFor(() => expect(result.current?.word).toBe("sjuksköterskan"));
  });

  it("aborts the attempt in flight when the word changes inside the debounce", async () => {
    const seen: string[] = [];
    const resolve = vi.fn<OccupationDivisionsResolver>(async (word) => {
      seen.push(word);
      return answer(word);
    });
    const { result, rerender } = renderHook(
      ({ word }: { word: string }) => useOccupationDivisions(word, resolve),
      { initialProps: { word: "sjuk" } },
    );
    rerender({ word: "sjuksk" });
    rerender({ word: "sjuksköterska" });

    await waitFor(() => expect(result.current?.word).toBe("sjuksköterska"));
    expect(seen).toEqual(["sjuksköterska"]);
  });

  it("degrades to null when the resolver throws (never a stale or invented answer)", async () => {
    const resolve = vi.fn<OccupationDivisionsResolver>(async () => {
      throw new Error("network");
    });
    const { result } = renderHook(() => useOccupationDivisions("sjuksköterska", resolve));

    await waitFor(() => expect(resolve).toHaveBeenCalled());
    // Let the rejection settle: the answer for the word is then null, not absent.
    await new Promise((r) => setTimeout(r, 50));
    expect(result.current).toBeNull();
  });
});
