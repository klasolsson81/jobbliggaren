import { useTranslations } from "next-intl";
import { codedTaxonomyName } from "./coded-taxonomy";

/**
 * Names a coded taxonomy concept where the caller has only the id — the recent-search
 * label's `Coded` part and the match detail's `employmentFit` both ship codes and no text.
 *
 * The unresolved answer is the catalogue's own unknown-code copy rather than the bare id: an
 * id is the external system's ubiquitous language and the opposite of explainable
 * (CLAUDE.md §5). `codedTaxonomyName`'s other callers pass the backend's source label
 * instead, because on those channels the wire still carries one.
 *
 * A hook rather than four inlined compositions: the pairing of the two catalogue keys is one
 * knowledge piece, and four copies of it drift (CLAUDE.md §9.1).
 *
 * No `"use client"`: `useTranslations` is synchronous in a Server Component too, and
 * `JobAdMatchSection` is a non-async RSC that must stay one. A directive here would pull it
 * across the boundary as a side effect of naming a concept.
 */
export function useCodedTaxonomyName(): (conceptId: string) => string {
  const tEnum = useTranslations("jobads.enums");
  const tUi = useTranslations("jobads.ui");
  return (conceptId) =>
    codedTaxonomyName(tEnum, conceptId, tUi("toolbar.unknownCode", { code: conceptId }));
}
