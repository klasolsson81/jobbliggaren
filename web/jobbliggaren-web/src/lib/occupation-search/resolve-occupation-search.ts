import {
  occupationSearchResultSchema,
  type OccupationSearchResult,
} from "@/lib/dto/occupation-search";

/**
 * The client half of the occupation picker's search (#2198): one typed word in, the answer or the
 * reason there is none out. The caller owns the debounce and the `AbortController` (AGENTS.md §4,
 * short-lived client reads). An abort rejects rather than resolving, so the caller can tell "this
 * word was superseded" from "search does not work".
 *
 * 401 is its own outcome because it names a different cause and a different action; a 429, any
 * other status, a network failure and a body that does not parse are all "search does not work
 * right now", and the picker falls back to browsing for every one of them.
 */
export type OccupationSearchOutcome =
  | { readonly kind: "ok"; readonly result: OccupationSearchResult }
  | { readonly kind: "unauthorized" }
  | { readonly kind: "unavailable" };

export type OccupationSearchResolver = (
  q: string,
  signal: AbortSignal,
) => Promise<OccupationSearchOutcome>;

/** `SearchOccupationsQuery.MinQueryLength`: the API answers 400 below it, so nothing is sent. */
export const OCCUPATION_SEARCH_MIN_LENGTH = 2;

/** `SearchOccupationsQuery.MaxQueryLength`: the field stops there, and the API answers 400 above it. */
export const OCCUPATION_SEARCH_MAX_LENGTH = 100;

const UNAVAILABLE: OccupationSearchOutcome = { kind: "unavailable" };

function isAbort(error: unknown, signal: AbortSignal): boolean {
  return signal.aborted || (error instanceof Error && error.name === "AbortError");
}

export const resolveOccupationSearch: OccupationSearchResolver = async (q, signal) => {
  let res: Response;
  try {
    res = await fetch(`/api/me/occupation-search?${new URLSearchParams({ q })}`, {
      signal,
      cache: "no-store",
    });
  } catch (error) {
    if (isAbort(error, signal)) throw error;
    return UNAVAILABLE;
  }

  if (res.status === 401) return { kind: "unauthorized" };
  if (!res.ok) return UNAVAILABLE;

  let body: unknown;
  try {
    body = await res.json();
  } catch (error) {
    if (isAbort(error, signal)) throw error;
    return UNAVAILABLE;
  }
  const parsed = occupationSearchResultSchema.safeParse(body);
  return parsed.success ? { kind: "ok", result: parsed.data } : UNAVAILABLE;
};
