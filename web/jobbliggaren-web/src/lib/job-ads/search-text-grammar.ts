import { Q_MIN_LENGTH } from "@/lib/dto/job-ads";

/**
 * #2143 (ADR 0167) — the minus-word grammar, mirrored from the backend's `SearchQueryParser` so
 * the field never shows a word as excluded that the server reads as positive, or the reverse.
 *
 * A word whose first character after any leading `+` is dash punctuation or U+2212 MINUS SIGN
 * excludes; its leading run of `+`, quotes and dashes and its trailing quotes are not part of the
 * term, so a term never starts with an operator the server's full-text search would read. A dash inside a word
 * (`e-handel`) does not exclude. The canonical q form of an excluded term is `-term`.
 */

/** Mirrors `ParsedSearchQuery.MaxExcludedTerms`: the server keeps at most this many. */
export const Q_MAX_EXCLUDED_TERMS = 5;

const STARTS_WITH_DASH = /^\+*[\p{Pd}\u2212]/u;
const LEADING_OPERATORS = /^[+"\p{Pd}\u2212]+/u;
const TRAILING_QUOTES = /"+$/;

export type WordClass =
  | { kind: "positive"; word: string }
  | { kind: "excluded"; term: string }
  | { kind: "operator" };

export function classifyWord(token: string): WordClass {
  if (STARTS_WITH_DASH.test(token)) {
    const term = token.replace(LEADING_OPERATORS, "").replace(TRAILING_QUOTES, "");
    return term.length === 0 ? { kind: "operator" } : { kind: "excluded", term };
  }
  const word = token.replace(/^\++/, "");
  return word.length === 0 ? { kind: "operator" } : { kind: "positive", word };
}

export function excludedQWord(term: string): string {
  return `-${term}`;
}

/** The bare term of an excluded q-word, or null when the word is not an exclusion. */
export function excludedTermOf(word: string): string | null {
  const cls = classifyWord(word);
  return cls.kind === "excluded" ? cls.term : null;
}

export function isExcludedTermTooShort(term: string): boolean {
  return term.length < Q_MIN_LENGTH;
}

/**
 * The positive part of a q string — what the server ranks on and what its minimum length is
 * measured against. Excluded and bare-operator words are left out.
 */
export function positiveQ(q: string): string {
  return q
    .split(/\s+/)
    .filter((w) => w.length > 0 && classifyWord(w).kind === "positive")
    .join(" ");
}
