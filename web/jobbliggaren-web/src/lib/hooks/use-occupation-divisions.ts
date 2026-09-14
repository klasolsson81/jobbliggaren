"use client";

import { useEffect, useState } from "react";
import type { OccupationDivisions } from "@/lib/dto/company-criteria";
import {
  OCCUPATION_MIN_WORD_LENGTH,
  type OccupationDivisionsResolver,
} from "@/lib/company-criteria/resolve-occupation-divisions";

/**
 * Debounced, abortable read of the occupation block's data for the picker's current filter word
 * (#1682). The shape is `use-criterion-preview-count.ts`'s: a `setTimeout` inside a `useEffect`,
 * one `AbortController` per attempt, and no state written synchronously in the effect body — every
 * write happens inside the timer or after the await. 400 ms is the family's debounce (FacetCounts /
 * CriterionCountPreview), and the rate-limit bucket on the other side is derived against it.
 *
 * The answer is stored WITH the word it answers and exposed only while that word is still the
 * current one: a block that answered a previous word would be a claim about the wrong thing, and
 * the deriver matches whole stemmed words, so mid-word there is nothing to show anyway. Without a
 * resolver (the kommun axis) the hook is inert.
 */
const DEBOUNCE_MS = 400;

interface Answer {
  readonly word: string;
  readonly data: OccupationDivisions | null;
}

export function useOccupationDivisions(
  word: string,
  resolve: OccupationDivisionsResolver | undefined,
): { readonly data: OccupationDivisions | null; readonly loading: boolean } {
  const [answer, setAnswer] = useState<Answer | null>(null);
  const [pendingWord, setPendingWord] = useState<string | null>(null);

  useEffect(() => {
    if (!resolve || word.length < OCCUPATION_MIN_WORD_LENGTH) return;

    const controller = new AbortController();
    const timer = setTimeout(() => {
      setPendingWord(word);
      void (async () => {
        let data: OccupationDivisions | null = null;
        try {
          data = await resolve(word, controller.signal);
        } catch {
          data = null;
        }
        if (controller.signal.aborted) return;
        setAnswer({ word, data });
        setPendingWord(null);
      })();
    }, DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [word, resolve]);

  return {
    data: answer !== null && answer.word === word ? answer.data : null,
    loading: pendingWord === word,
  };
}
