"use client";

// "use client": a debounced, abortable browser fetch held in component state.

import { useEffect, useState } from "react";
import type { OccupationSearchResult } from "@/lib/dto/occupation-search";
import {
  OCCUPATION_SEARCH_MIN_LENGTH,
  resolveOccupationSearch,
  type OccupationSearchOutcome,
  type OccupationSearchResolver,
} from "@/lib/occupation-search/resolve-occupation-search";

/**
 * The occupation picker's search for the field's current text (#2198). The shape is
 * `use-occupation-divisions.ts`'s: a `setTimeout` inside a `useEffect`, one `AbortController` per
 * attempt, and every state write inside the timer or after the await. 300 ms is the suggest
 * family's debounce, and the rate-limit bucket on the other side is derived against it.
 *
 * The answer is stored WITH the trimmed word it answers, and `pending` is derived from the two
 * disagreeing rather than kept as a flag: rows that answered a previous word are a claim about the
 * wrong thing, so they are never returned once the word has moved on. The gate is trimmed length
 * alone. Splitting into words is the server's, and a second tokenizer here would be a second
 * authority for it.
 */
const DEBOUNCE_MS = 300;

export type OccupationSearchState =
  | { readonly kind: "idle" }
  | { readonly kind: "pending" }
  | { readonly kind: "ok"; readonly word: string; readonly result: OccupationSearchResult }
  | { readonly kind: "unauthorized" }
  | { readonly kind: "unavailable" };

interface Answer {
  readonly word: string;
  readonly outcome: OccupationSearchOutcome;
}

const IDLE: OccupationSearchState = { kind: "idle" };
const PENDING: OccupationSearchState = { kind: "pending" };

export function useOccupationSearch(
  text: string,
  resolve: OccupationSearchResolver = resolveOccupationSearch,
): OccupationSearchState {
  const [answer, setAnswer] = useState<Answer | null>(null);
  const word = text.trim();
  const searchable = word.length >= OCCUPATION_SEARCH_MIN_LENGTH;

  useEffect(() => {
    if (!searchable) return;

    const controller = new AbortController();
    const timer = setTimeout(() => {
      void (async () => {
        let outcome: OccupationSearchOutcome;
        try {
          outcome = await resolve(word, controller.signal);
        } catch {
          // An abort is this attempt being superseded; anything else the resolver let through is
          // a search that does not work.
          if (controller.signal.aborted) return;
          outcome = { kind: "unavailable" };
        }
        if (controller.signal.aborted) return;
        setAnswer({ word, outcome });
      })();
    }, DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [word, searchable, resolve]);

  if (!searchable) return IDLE;
  if (answer === null || answer.word !== word) return PENDING;
  switch (answer.outcome.kind) {
    case "ok":
      return { kind: "ok", word: answer.word, result: answer.outcome.result };
    case "unauthorized":
      return { kind: "unauthorized" };
    case "unavailable":
      return { kind: "unavailable" };
  }
}
