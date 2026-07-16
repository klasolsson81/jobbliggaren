import type { CriterionReference } from "@/lib/dto/company-criteria";

/**
 * #560 PR-3 — derive a human display label for a criterion from its RAW codes, using the SCB
 * reference tree the FE already holds (the label is deliberately NOT resolved server-side — one
 * authority, no drift). A criterion carrying leaves under the "Dataprogrammering" division and the
 * Stockholm kommun renders "Dataprogrammering m.fl. · Stockholm m.fl.".
 *
 * The SNI side names the DIVISIONS (huvudgrupper) that cover the selected leaves — a division name is
 * far more legible than a list of five-digit codes, and a whole-division pick reads as exactly its
 * name. The kommun side names the selected kommuner. Each axis shows the first name plus a
 * "m.fl."-suffix when more than one distinct name is covered.
 *
 * Returns null when nothing resolves (an all-stale code set against a newer reference snapshot) — the
 * caller then falls back to the count summary ("3 branscher, 2 kommuner"). Pure and locale-config
 * injected (the "m.fl." suffix + the " · " separator come from i18n via the caller) so it stays unit-
 * testable without a translator.
 */
export interface DisplayLabelCopy {
  /** The "and others" suffix, e.g. "m.fl.". */
  readonly moreSuffix: string;
  /** The separator between the two axes, e.g. " · ". */
  readonly separator: string;
}

export function deriveDisplayLabel(
  sniCodes: ReadonlyArray<string>,
  municipalityCodes: ReadonlyArray<string>,
  reference: Pick<CriterionReference, "sni" | "lan">,
  copy: DisplayLabelCopy,
): string | null {
  const sniPart = formatNames(divisionNamesFor(sniCodes, reference), copy.moreSuffix);
  const kommunPart = formatNames(kommunNamesFor(municipalityCodes, reference), copy.moreSuffix);

  const parts = [sniPart, kommunPart].filter((p): p is string => p !== null);
  return parts.length > 0 ? parts.join(copy.separator) : null;
}

/**
 * The distinct division names (in tree order) whose leaves intersect the selected SNI codes. A
 * division is named once even when several of its leaves are selected.
 */
function divisionNamesFor(
  sniCodes: ReadonlyArray<string>,
  reference: Pick<CriterionReference, "sni">,
): string[] {
  const selected = new Set(sniCodes);
  const names: string[] = [];
  for (const section of reference.sni) {
    for (const division of section.divisions) {
      if (division.leaves.some((leaf) => selected.has(leaf.code))) {
        names.push(division.name);
      }
    }
  }
  return names;
}

/** The selected kommun names, in tree order. */
function kommunNamesFor(
  municipalityCodes: ReadonlyArray<string>,
  reference: Pick<CriterionReference, "lan">,
): string[] {
  const selected = new Set(municipalityCodes);
  const names: string[] = [];
  for (const lan of reference.lan) {
    for (const kommun of lan.kommuner) {
      if (selected.has(kommun.code)) names.push(kommun.name);
    }
  }
  return names;
}

/** First name, plus the "m.fl." suffix when more than one name is covered. Empty → null. */
function formatNames(names: ReadonlyArray<string>, moreSuffix: string): string | null {
  if (names.length === 0) return null;
  if (names.length === 1) return names[0]!;
  return `${names[0]!} ${moreSuffix}`;
}
