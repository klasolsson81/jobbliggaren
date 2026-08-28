import type { RecentSearchLabel, RecentSearchLabelPart } from "@/lib/dto/recent-searches";

/**
 * The locale words a recent-search label is built from. Injected by the caller, the way
 * `deriveDisplayLabel` (lib/company-criteria/display-label.ts) takes its `DisplayLabelCopy`
 * — so this stays a pure function, unit-testable without a translator.
 *
 * Proper nouns are NOT in here and never should be: place names, region names and
 * occupation-group names arrive resolved from the taxonomy and stay Swedish in every
 * locale. Only the words AROUND them move (#1430).
 */
export interface RecentSearchLabelCopy {
  /** No dimension narrows the search, e.g. "Alla annonser" / "All job ads". */
  readonly all: string;
  /** The distance facet where it LEADS the label, e.g. "Distans" / "Remote". */
  readonly remoteLeading: string;
  /** The distance facet after another part. Swedish lowercases it here; English does not. */
  readonly remoteInline: string;
  /** Joins the final two alternatives of a union. Carries its own spacing, e.g. " eller ". */
  readonly or: string;
  /** Separates the earlier parts, and separates conjoined axes. Carries its own spacing. */
  readonly separator: string;
  /** What a part stands for beyond the name it shows, e.g. "+3 till" / "+3 more". */
  readonly more: (count: number) => string;
}

/**
 * The catalogue keys this label reads, under the `jobads.recent` namespace. Narrow union
 * rather than `string`, for the same reason `applicationStatusLabel` types its translator
 * that way: under `strictFunctionTypes` a `t` that accepts only real keys is not assignable
 * to one that accepts any string, so the narrow type is what actually type-checks.
 */
type RecentSearchLabelKey =
  | "label.all"
  | "label.remoteLeading"
  | "label.remoteInline"
  | "label.or"
  | "label.separator"
  | "label.more";

/**
 * Resolves the label copy once, so each of the four render sites stays a single line and
 * none of them can drift into its own wording.
 */
export function recentSearchLabelCopy(
  t: (key: RecentSearchLabelKey, values?: { count: number }) => string,
): RecentSearchLabelCopy {
  return {
    all: t("label.all"),
    remoteLeading: t("label.remoteLeading"),
    remoteInline: t("label.remoteInline"),
    or: t("label.or"),
    separator: t("label.separator"),
    more: (count) => t("label.more", { count }),
  };
}

function renderPart(
  part: RecentSearchLabelPart,
  index: number,
  copy: RecentSearchLabelCopy,
): string {
  // Position, not a flag on the wire: Swedish capitalises the distance word only where it
  // leads. Deriving it from the part order keeps the rule in the locale that has it.
  if (part.kind === "Remote") {
    return index === 0 ? copy.remoteLeading : copy.remoteInline;
  }

  const text = part.text ?? "";
  return part.moreCount > 0 ? `${text} ${copy.more(part.moreCount)}` : text;
}

/**
 * Renders the recent search's label from the structure the backend derived.
 *
 * The backend owns WHICH dimension names the row and HOW the parts relate — that is a
 * derivation over the search criteria, and naming the wrong one makes the label describe a
 * strict subset or superset of what the click returns (`GeoUnionLabelParityTests` measures
 * it). This function owns only the words, and never re-derives the branch: `label.kind`
 * says which one won, including the query branch, even though `q` also rides the wire.
 *
 * The join is SEMANTIC. `Disjunction` is the geo union (kommun ∨ län ∨ distans), so its
 * parts are alternatives. `Conjunction` is the orthogonal refinement axes, which all hold
 * at once — calling those alternatives would state something false. Which word renders each
 * belongs to the locale.
 */
export function buildRecentSearchLabel(
  label: RecentSearchLabel,
  copy: RecentSearchLabelCopy,
): string {
  if (label.kind === "All") return copy.all;

  // `parts` is non-empty for every other kind — the zod schema refuses the alternative
  // rather than letting this function invent a label for a shape it does not know.
  const parts = label.parts.map((part, index) => renderPart(part, index, copy));
  if (parts.length === 1) return parts[0]!;

  if (label.join === "Disjunction") {
    const head = parts.slice(0, -1).join(copy.separator);
    return `${head}${copy.or}${parts[parts.length - 1]!}`;
  }

  return parts.join(copy.separator);
}
