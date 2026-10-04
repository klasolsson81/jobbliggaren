import type {
  JobAdMatchDetail,
  MatchCause,
  MatchConceptGroup,
  MatchRegisterConcept,
  MatchSkillDimensionDetail,
} from "@/lib/dto/job-ad-match";
import { classifyOrtConcept, type OrtGranularity } from "@/lib/job-ads/ort-granularity";

/**
 * The job card's match section as a checklist (#1963, ADR 0076 Amendment 2026-10-03 (b),
 * DESIGN.md §3 and §8). Pure: the section component turns this model into markup and copy.
 * No verdict, grade or evidence is computed here — the model only decides what is shown.
 */

export type MatchDimensionKey =
  | "ssykOverlap"
  | "titleSimilarity"
  | "regionFit"
  | "employmentFit";

export type RowTone = "match" | "warn";

/** The status word: a rendered verdict, or "Related" for the Yrke row under a Related grade. */
export type RowWord = "Match" | "Partial" | "NoMatch" | "Related";

/** The three (dimension, cause) pairs a rendered row can carry. */
export type RowCause =
  | { dimension: "regionFit"; cause: "AdSilent" | "RemoteOverride" }
  | { dimension: "employmentFit"; cause: "AdSilent" };

export type RowValue =
  | { kind: "names"; names: string[] }
  | { kind: "cause"; cause: RowCause }
  | { kind: "titleSummary"; verdict: "Match" | "Partial" | "NoMatch" }
  | { kind: "unnamed"; count: number };

export interface DimensionRow {
  key: MatchDimensionKey;
  tone: RowTone;
  word: RowWord;
  value: RowValue;
  /** Register entries the snapshot cannot name, beside named ones (#1598); `0` when none. */
  unnamedCount: number;
}

export type ChipTone = "met" | "missingRequired" | "missing";

export interface SkillChip {
  key: string;
  label: string;
  tone: ChipTone;
}

export type SkillGroupKind = "must" | "nice" | "profileMatched" | "profileMissing";

export interface SkillGroup {
  kind: SkillGroupKind;
  chips: SkillChip[];
}

export type RequirementsNote = "both" | "mustHave" | "niceToHave";

export type SkillChecklist =
  | { state: "notAssessed" }
  | {
      state: "assessed";
      groups: SkillGroup[];
      /** Distinct skills per side over every rendered chip; `null` without concept identity or chips. */
      counts: { matched: number; missing: number } | null;
      requirementsNote: RequirementsNote | null;
    };

export type MatchChecklist =
  | { kind: "noStatedOccupation" }
  | {
      kind: "checklist";
      /** The ad states no occupation group: no grade and no Yrke row, so the reason stands alone. */
      occupationSilent: boolean;
      rows: DimensionRow[];
      skills: SkillChecklist;
    };

/** Missing profile skills shown before "Visa {n} till". */
export const PROFILE_MISSING_VISIBLE = 6;

/** Index from which the "Finns inte i din profil" chips start collapsed, or `null` when all show. */
export function overflowStart(chipCount: number): number | null {
  return chipCount > PROFILE_MISSING_VISIBLE ? PROFILE_MISSING_VISIBLE : null;
}

interface BuildOptions {
  /** Resolves an employment-type concept id to its catalogue name (`useCodedTaxonomyName`). */
  codedName: (conceptId: string) => string;
  /** conceptId → kommun/län; omitted when the taxonomy was unavailable. */
  ortGranularityByConceptId?: Record<string, OrtGranularity>;
}

export function buildMatchChecklist(
  match: JobAdMatchDetail,
  options: BuildOptions,
): MatchChecklist {
  if (match.grade === null && match.ssykOverlap.cause === "PreferenceUnstated") {
    return { kind: "noStatedOccupation" };
  }
  return {
    kind: "checklist",
    occupationSilent:
      match.ssykOverlap.verdict === "NotAssessed" && match.ssykOverlap.cause === "AdSilent",
    rows: buildDimensionRows(match, options),
    skills: buildSkillChecklist(match),
  };
}

function isRendered(verdict: string): verdict is "Match" | "Partial" | "NoMatch" {
  return verdict === "Match" || verdict === "Partial" || verdict === "NoMatch";
}

function rowCause(dimension: MatchDimensionKey, cause: MatchCause | null): RowCause | null {
  if (dimension === "regionFit" && (cause === "AdSilent" || cause === "RemoteOverride")) {
    return { dimension, cause };
  }
  if (dimension === "employmentFit" && cause === "AdSilent") {
    return { dimension, cause };
  }
  return null;
}

/** Kommun before län before unclassified; unnamed entries are only counted (#1598). */
function orderedOrtNames(
  entries: ReadonlyArray<MatchRegisterConcept>,
  granularityByConceptId: Record<string, OrtGranularity>,
): string[] {
  const municipalities: string[] = [];
  const regions: string[] = [];
  const plain: string[] = [];
  for (const entry of entries) {
    if (entry.label === null) continue;
    const granularity = classifyOrtConcept(entry.conceptId, granularityByConceptId);
    if (granularity === "municipality") municipalities.push(entry.label);
    else if (granularity === "region") regions.push(entry.label);
    else plain.push(entry.label);
  }
  return [...municipalities, ...regions, ...plain];
}

function registerValue(
  names: string[],
  unnamed: number,
): { value: RowValue; unnamedCount: number } | null {
  if (names.length > 0) return { value: { kind: "names", names }, unnamedCount: unnamed };
  if (unnamed > 0) return { value: { kind: "unnamed", count: unnamed }, unnamedCount: 0 };
  return null;
}

export function buildDimensionRows(
  match: JobAdMatchDetail,
  { codedName, ortGranularityByConceptId }: BuildOptions,
): DimensionRow[] {
  const rows: DimensionRow[] = [];
  const push = (
    key: MatchDimensionKey,
    verdict: "Match" | "Partial" | "NoMatch",
    shown: { value: RowValue; unnamedCount: number } | null,
  ) => {
    // A row with nothing to show is never drawn: a status word over an empty value states
    // nothing the reader can check (#1598).
    if (shown === null) return;
    const related = key === "ssykOverlap" && match.grade === "Related";
    rows.push({
      key,
      tone: verdict === "Match" && !related ? "match" : "warn",
      word: related ? "Related" : verdict,
      value: shown.value,
      unnamedCount: shown.unnamedCount,
    });
  };

  const ssyk = match.ssykOverlap;
  if (isRendered(ssyk.verdict)) {
    const entries = [...ssyk.matched, ...ssyk.missing];
    const names = entries.flatMap((entry) => (entry.label === null ? [] : [entry.label]));
    push("ssykOverlap", ssyk.verdict, registerValue(names, entries.length - names.length));
  }

  const title = match.titleSimilarity;
  if (isRendered(title.verdict)) {
    push("titleSimilarity", title.verdict, {
      value: { kind: "titleSummary", verdict: title.verdict },
      unnamedCount: 0,
    });
  }

  const region = match.regionFit;
  if (isRendered(region.verdict)) {
    const cause = rowCause("regionFit", region.cause);
    if (cause !== null) {
      push("regionFit", region.verdict, { value: { kind: "cause", cause }, unnamedCount: 0 });
    } else {
      const entries = [...region.matched, ...region.missing];
      const names = orderedOrtNames(entries, ortGranularityByConceptId ?? {});
      const named = entries.filter((entry) => entry.label !== null).length;
      push("regionFit", region.verdict, registerValue(names, entries.length - named));
    }
  }

  const employment = match.employmentFit;
  if (isRendered(employment.verdict)) {
    const cause = rowCause("employmentFit", employment.cause);
    if (cause !== null) {
      push("employmentFit", employment.verdict, {
        value: { kind: "cause", cause },
        unnamedCount: 0,
      });
    } else {
      const names = [...employment.matchedConceptIds, ...employment.missingConceptIds].map(
        codedName,
      );
      push("employmentFit", employment.verdict, registerValue(names, 0));
    }
  }

  return rows;
}

interface ChipSource {
  label: string;
  /** Member concept ids; empty for display-only legacy evidence. */
  ids: string[];
}

function sideSources(
  row: MatchSkillDimensionDetail,
  side: "matched" | "missing",
  identity: boolean,
): ChipSource[] {
  if (identity && row.conceptEvidence != null) {
    return row.conceptEvidence[side].map((group: MatchConceptGroup) => ({
      label: group.display,
      ids: group.members.map((member) => member.conceptId),
    }));
  }
  return row[side].map((display) => ({ label: display, ids: [] }));
}

/** Vacuous with nothing on either side, in both the legacy arrays and the evidence. */
function provenEmpty(row: MatchSkillDimensionDetail): boolean {
  return (
    row.verdict === "Vacuous" &&
    row.matched.length === 0 &&
    row.missing.length === 0 &&
    (row.conceptEvidence == null ||
      (row.conceptEvidence.matched.length === 0 && row.conceptEvidence.missing.length === 0))
  );
}

/** Distinct skills among chip id sets: sets that share a concept id are one skill. */
function distinctSkills(idSets: ReadonlyArray<ReadonlyArray<string>>): number {
  const parent = idSets.map((_, index) => index);
  const find = (index: number): number => {
    let root = index;
    let next = parent[root];
    while (next !== undefined && next !== root) {
      root = next;
      next = parent[root];
    }
    return root;
  };
  const owner = new Map<string, number>();
  idSets.forEach((ids, index) => {
    for (const id of ids) {
      const seen = owner.get(id);
      if (seen === undefined) owner.set(id, index);
      else parent[find(index)] = find(seen);
    }
  });
  return new Set(idSets.map((_, index) => find(index))).size;
}

export function buildSkillChecklist(match: JobAdMatchDetail): SkillChecklist {
  // One predicate gates all three skill rows (no confirmed skills, MatchScorer.ScoreConceptCoverage).
  if (match.skillOverlap.verdict === "NotAssessed") return { state: "notAssessed" };

  const rows = [match.mustHaveCoverage, match.niceToHaveCoverage, match.skillOverlap];
  const identity = rows.every((row) => row.conceptEvidence != null);

  const groups: SkillGroup[] = [];
  const metIds: string[][] = [];
  const missingIds: string[][] = [];

  const requirement = (
    kind: "must" | "nice",
    row: MatchSkillDimensionDetail,
    missingTone: ChipTone,
  ) => {
    if (row.verdict === "NotAssessed") return;
    const met = sideSources(row, "matched", identity);
    const unmet = sideSources(row, "missing", identity);
    const chips: SkillChip[] = [
      ...met.map((source, index) => ({
        key: `${kind}:met:${source.ids.join("+") || index}`,
        label: source.label,
        tone: "met" as const,
      })),
      ...unmet.map((source, index) => ({
        key: `${kind}:unmet:${source.ids.join("+") || index}`,
        label: source.label,
        tone: missingTone,
      })),
    ];
    met.forEach((source) => metIds.push(source.ids));
    unmet.forEach((source) => missingIds.push(source.ids));
    if (chips.length > 0) groups.push({ kind, chips });
  };

  requirement("must", match.mustHaveCoverage, "missingRequired");
  requirement("nice", match.niceToHaveCoverage, "missing");

  // A profile chip that repeats a requirement chip on the same side is already shown there.
  // Only shared concept identity decides; display strings are not identities (ADR 0076).
  const profile = (
    kind: "profileMatched" | "profileMissing",
    side: "matched" | "missing",
    shownIds: ReadonlyArray<ReadonlyArray<string>>,
    tone: ChipTone,
  ) => {
    const taken = new Set(shownIds.flat());
    const kept = sideSources(match.skillOverlap, side, identity).filter(
      (source) => !identity || !source.ids.some((id) => taken.has(id)),
    );
    kept.forEach((source) => (side === "matched" ? metIds : missingIds).push(source.ids));
    if (kept.length > 0) {
      groups.push({
        kind,
        chips: kept.map((source, index) => ({
          key: `${kind}:${source.ids.join("+") || index}`,
          label: source.label,
          tone,
        })),
      });
    }
  };

  profile("profileMatched", "matched", [...metIds], "met");
  profile("profileMissing", "missing", [...missingIds], "missing");

  const mustEmpty = provenEmpty(match.mustHaveCoverage);
  const niceEmpty = provenEmpty(match.niceToHaveCoverage);
  const requirementsNote: RequirementsNote | null =
    mustEmpty && niceEmpty ? "both" : mustEmpty ? "mustHave" : niceEmpty ? "niceToHave" : null;

  const chipCount = metIds.length + missingIds.length;
  const counts =
    identity && chipCount > 0
      ? { matched: distinctSkills(metIds), missing: distinctSkills(missingIds) }
      : null;

  return { state: "assessed", groups, counts, requirementsNote };
}
