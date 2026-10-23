import { afterEach, describe, it, expect, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useOccupationSearch } from "./use-occupation-search";
import type { OccupationSearchResult } from "@/lib/dto/occupation-search";
import type {
  OccupationSearchOutcome,
  OccupationSearchResolver,
} from "@/lib/occupation-search/resolve-occupation-search";

// Real v30 labels and ids: the answer names the word it was given, so a stale answer is visible.
const answerFor = (word: string): OccupationSearchResult => ({
  rows: [
    {
      kind: "Group",
      groupConceptId: "DJh5_yyF_hEM",
      groupLabel: "Mjukvaru- och systemutvecklare m.fl.",
      matchedNames: [word],
      labelHit: false,
      headWord: null,
      variants: [],
      exactMatch: false,
    },
  ],
  totalRows: 1,
});

const ok = (word: string): OccupationSearchOutcome => ({ kind: "ok", result: answerFor(word) });
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

const originalFetch = global.fetch;
afterEach(() => {
  global.fetch = originalFetch;
});

describe("useOccupationSearch (#2198)", () => {
  it("waits out the debounce, then answers the trimmed word once", async () => {
    const resolve = vi.fn<OccupationSearchResolver>(async (word) => ok(word));
    const { result } = renderHook(() => useOccupationSearch("  backend  ", resolve));

    expect(result.current).toEqual({ kind: "pending" });
    await sleep(150);
    expect(resolve).not.toHaveBeenCalled();

    await waitFor(() => expect(result.current.kind).toBe("ok"));
    expect(resolve).toHaveBeenCalledTimes(1);
    expect(resolve).toHaveBeenCalledWith("backend", expect.any(AbortSignal));
    expect(result.current).toEqual({ kind: "ok", word: "backend", result: answerFor("backend") });
  });

  it("is idle and sends nothing below two characters, whitespace included", async () => {
    const resolve = vi.fn<OccupationSearchResolver>(async (word) => ok(word));
    const empty = renderHook(() => useOccupationSearch("", resolve));
    const one = renderHook(() => useOccupationSearch("s", resolve));
    const padded = renderHook(() => useOccupationSearch("  s  ", resolve));

    await sleep(400);
    expect(empty.result.current).toEqual({ kind: "idle" });
    expect(one.result.current).toEqual({ kind: "idle" });
    expect(padded.result.current).toEqual({ kind: "idle" });
    expect(resolve).not.toHaveBeenCalled();
  });

  it("never shows a previous word's answer: it is pending from the moment the word changes", async () => {
    const resolve = vi.fn<OccupationSearchResolver>(async (word) => ok(word));
    const { result, rerender } = renderHook(
      ({ text }: { text: string }) => useOccupationSearch(text, resolve),
      { initialProps: { text: "proj" } },
    );
    await waitFor(() => expect(result.current.kind).toBe("ok"));

    rerender({ text: "projektledare" });
    // Synchronously after the change, with no answer for the new word yet.
    expect(result.current).toEqual({ kind: "pending" });
    await waitFor(() =>
      expect(result.current).toEqual({
        kind: "ok",
        word: "projektledare",
        result: answerFor("projektledare"),
      }),
    );
  });

  it("drops a change inside the debounce, and aborts an attempt in flight", async () => {
    const signals: AbortSignal[] = [];
    let release: (() => void) | undefined;
    const resolve = vi.fn<OccupationSearchResolver>((word, signal) => {
      signals.push(signal);
      if (word === "sjuk") {
        return new Promise((res) => {
          release = () => res(ok(word));
        });
      }
      return Promise.resolve(ok(word));
    });
    const { result, rerender } = renderHook(
      ({ text }: { text: string }) => useOccupationSearch(text, resolve),
      { initialProps: { text: "sj" } },
    );
    // Inside the debounce: "sj" is never sent.
    rerender({ text: "sjuk" });
    await waitFor(() => expect(resolve).toHaveBeenCalledWith("sjuk", expect.any(AbortSignal)));

    // "sjuk" is in flight; the next word aborts it, and its late answer is never shown.
    rerender({ text: "sjuksköterska" });
    expect(signals[0]?.aborted).toBe(true);
    release?.();
    await waitFor(() => expect(result.current.kind).toBe("ok"));
    expect(result.current).toMatchObject({ kind: "ok", word: "sjuksköterska" });
    expect(resolve.mock.calls.map(([word]) => word)).toEqual(["sjuk", "sjuksköterska"]);
  });

  it("goes back to idle when the text drops below the gate", async () => {
    const resolve = vi.fn<OccupationSearchResolver>(async (word) => ok(word));
    const { result, rerender } = renderHook(
      ({ text }: { text: string }) => useOccupationSearch(text, resolve),
      { initialProps: { text: "chef" } },
    );
    await waitFor(() => expect(result.current.kind).toBe("ok"));

    rerender({ text: "c" });
    expect(result.current).toEqual({ kind: "idle" });
  });

  it("maps a resolver that throws (not an abort) to unavailable", async () => {
    const resolve = vi.fn<OccupationSearchResolver>(async () => {
      throw new Error("unexpected");
    });
    const { result } = renderHook(() => useOccupationSearch("chef", resolve));

    await waitFor(() => expect(result.current).toEqual({ kind: "unavailable" }));
  });

  // Through the real resolver and a stubbed fetch, so the status mapping is the shipped one.
  describe("with the shipped resolver", () => {
    it("401 is unauthorized", async () => {
      global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
      const { result } = renderHook(() => useOccupationSearch("chef"));

      await waitFor(() => expect(result.current).toEqual({ kind: "unauthorized" }));
    });

    it.each([
      ["429", () => Promise.resolve(new Response(null, { status: 429, headers: { "Retry-After": "5" } }))],
      ["502", () => Promise.resolve(new Response(null, { status: 502 }))],
      ["a network failure", () => Promise.reject(new TypeError("Failed to fetch"))],
    ])("%s is unavailable", async (_name, respond) => {
      global.fetch = vi.fn().mockImplementation(respond);
      const { result } = renderHook(() => useOccupationSearch("chef"));

      await waitFor(() => expect(result.current).toEqual({ kind: "unavailable" }));
    });

    it("200 is the parsed answer", async () => {
      global.fetch = vi.fn().mockResolvedValue(Response.json(answerFor("chef")));
      const { result } = renderHook(() => useOccupationSearch("chef"));

      await waitFor(() =>
        expect(result.current).toEqual({ kind: "ok", word: "chef", result: answerFor("chef") }),
      );
    });
  });
});
