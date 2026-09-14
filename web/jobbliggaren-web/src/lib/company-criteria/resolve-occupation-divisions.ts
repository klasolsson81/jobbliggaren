import {
  occupationDivisionsSchema,
  type OccupationDivisions,
} from "@/lib/dto/company-criteria";

/**
 * The picker's occupation resolver (#1682): a typed word in, the occupation groups it denotes and
 * their measured huvudgrupp shares out, or `null` for anything that is not a usable answer. The
 * picker takes it as a PROP — data, not a mode flag — so the kommun axis simply does not pass one
 * and the SNI axis passes this. Abortable: the caller owns the debounce and the `AbortController`
 * (AGENTS.md §4, short-lived client reads).
 */
export type OccupationDivisionsResolver = (
  word: string,
  signal: AbortSignal,
) => Promise<OccupationDivisions | null>;

/** Shortest word worth sending — the API validates the same floor and answers 400 below it. */
export const OCCUPATION_MIN_WORD_LENGTH = 2;

export const resolveOccupationDivisions: OccupationDivisionsResolver = async (word, signal) => {
  const params = new URLSearchParams({ q: word });
  const res = await fetch(`/api/me/occupation-divisions?${params}`, { signal });
  if (!res.ok) return null;
  const parsed = occupationDivisionsSchema.safeParse(await res.json());
  return parsed.success ? parsed.data : null;
};
