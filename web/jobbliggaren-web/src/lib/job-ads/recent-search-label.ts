import type { RecentSearchLabel, RecentSearchLabelPart } from "@/lib/dto/recent-searches";

/**
 * The locale words a recent-search label is built from. Injected by the caller, the way
 * `deriveDisplayLabel` (lib/company-criteria/display-label.ts) takes its `DisplayLabelCopy`
 * — so this stays a pure function, unit-testable without a translator.
 *
 * Proper nouns are NOT in here and never should be: place names, region names and
 * occupation-group names stay Swedish in every locale. Only the words AROUND them move
 * (#1430) — and the coded terms below, which arrive as ids (#1537).
 */
export interface RecentSearchLabelCopy {
  /** No dimension narrows the search, e.g. "Alla annonser" / "All job ads". */
  readonly all: string;
  /** The distance facet where it LEADS the label, e.g. "Distans" / "Remote". */
  readonly remoteLeading: string;
  /** The distance facet after another part, e.g. "distans" / "remote". */
  readonly remoteInline: string;
  /** One employer where it LEADS the label, e.g. "En arbetsgivare" / "One employer". */
  readonly employerLeading: string;
  /** One employer after another part, e.g. "en arbetsgivare" / "one employer". */
  readonly employerInline: string;
  /**
   * Several employers, counted rather than named, e.g. "3 arbetsgivare" / "3 employers".
   * Only ever called with a count of two or more — a single employer takes the positional
   * word above — so the copy needs no singular form.
   */
  readonly employerCount: (count: number) => string;
  /** Joins the final two alternatives of a union. Carries its own spacing, e.g. " eller ". */
  readonly or: string;
  /** Separates the earlier parts, and separates conjoined axes. Carries its own spacing. */
  readonly separator: string;
  /**
   * What a part stands for beyond the name it shows, e.g. "+3 till" / "+3 more". Unlike
   * `or` and `separator` this does NOT carry its own spacing — the caller puts the space
   * between the name and this.
   */
  readonly more: (count: number) => string;
  /**
   * The name for a coded taxonomy concept, e.g. "Heltid" / "Full time".
   * Takes the id because that is all a `Coded` part carries; resolving it is the caller's,
   * so this file stays a pure function with no translator of its own.
   */
  readonly coded: (conceptId: string) => string;
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
  | "label.employerLeading"
  | "label.employerInline"
  | "label.employerCount"
  | "label.or"
  | "label.separator"
  | "label.more";

/**
 * Resolves the label copy once, so each of the three call sites stays a single line and none
 * of them can drift into its own wording. (Three callers, four rendered surfaces — the
 * `/sokningar` row spends the same string on its heading and its remove button.)
 */
export function recentSearchLabelCopy(
  t: (key: RecentSearchLabelKey, values?: { count: number }) => string,
  coded: (conceptId: string) => string,
): RecentSearchLabelCopy {
  return {
    all: t("label.all"),
    remoteLeading: t("label.remoteLeading"),
    remoteInline: t("label.remoteInline"),
    employerLeading: t("label.employerLeading"),
    employerInline: t("label.employerInline"),
    employerCount: (count) => t("label.employerCount", { count }),
    or: t("label.or"),
    separator: t("label.separator"),
    more: (count) => t("label.more", { count }),
    coded,
  };
}

function renderPart(
  part: RecentSearchLabelPart,
  index: number,
  copy: RecentSearchLabelCopy,
): string {
  // Position, not a flag on the wire: which word the distance facet renders as depends on
  // where the part sits, and the catalogue owns both forms per locale.
  if (part.kind === "Remote") {
    return index === 0 ? copy.remoteLeading : copy.remoteInline;
  }

  // Same shape as the distance facet, for a stronger reason: the value is an org.nr, and for a
  // sole trader that is the holder's personnummer, so the part never names it (Klas 2026-08-23,
  // #1471). One employer takes the positional word; several are counted, never listed.
  if (part.kind === "Employer") {
    if (part.moreCount > 0) return copy.employerCount(part.moreCount + 1);
    return index === 0 ? copy.employerLeading : copy.employerInline;
  }

  // `Named` carries a resolved name, Swedish in every locale; `Coded` carries only an id.
  // The overflow suffix is the same either way — it counts selections, not characters.
  const name = part.kind === "Coded" ? copy.coded(part.conceptId) : part.text;

  return part.moreCount > 0 ? `${name} ${copy.more(part.moreCount)}` : name;
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
