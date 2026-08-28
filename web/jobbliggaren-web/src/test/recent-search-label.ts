import type { RecentSearchLabel } from "@/lib/dto/recent-searches";

/**
 * The label the backend derives for a free-text recent search: `DeriveLabel`'s q branch,
 * which returns the query verbatim in a single Named part with no overflow.
 *
 * `buildRecentSearchLabel(queryLabel(x), copy)` renders exactly `x` in every locale, which
 * is why fixtures that only need "a label that displays as this text" use it. Tests that
 * exercise the composition itself build their own shapes — those belong in
 * `recent-search-label.test.ts`, against the composer, not against a component.
 */
export function queryLabel(text: string): RecentSearchLabel {
  return {
    kind: "Query",
    join: "None",
    parts: [{ kind: "Named", text, conceptId: null, moreCount: 0 }],
  };
}
