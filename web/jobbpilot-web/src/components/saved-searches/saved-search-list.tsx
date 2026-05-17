"use client";

import Link from "next/link";
import { Button } from "@/components/ui/button";
import { DeleteSavedSearchDialog } from "./delete-saved-search-dialog";
import { getJobAdSortLabel } from "@/lib/job-ads/status";
import type { SavedSearchDto } from "@/lib/dto/saved-searches";

interface SavedSearchListProps {
  savedSearches: ReadonlyArray<SavedSearchDto>;
}

function criteriaSummary(s: SavedSearchDto): string {
  // ADR 0043 Approach A — visa svenska NAMN (ssykLabels/regionLabels från
  // backend reverse-lookup), aldrig råa concept-id. concept-id är borta ur
  // UI:t (Anticorruption Layer, Evans 2003 kap. 14). Stale id → backend
  // "Okänd kod (<id>)". Fallback till råa listan om labels saknas (t.ex.
  // äldre cache-svar) så raden aldrig blir tom mitt i en lista.
  const parts: string[] = [];
  if (s.q) parts.push(`sökord "${s.q}"`);

  // Wire-svar går alltid genom schemat (default []), men defensiv ?? []
  // håller komponenten robust om en label-lista saknas helt.
  const ssykNames =
    (s.ssykLabels ?? []).length > 0
      ? (s.ssykLabels ?? []).map((l) => l.label)
      : s.ssyk;
  if (ssykNames.length > 0) parts.push(ssykNames.join(", "));

  const regionNames =
    (s.regionLabels ?? []).length > 0
      ? (s.regionLabels ?? []).map((l) => l.label)
      : s.region;
  if (regionNames.length > 0) parts.push(regionNames.join(", "));

  // parts kan aldrig bli tomt: backend SearchCriteria-invarianten garanterar
  // minst ett kriterium, och sorteringsetiketten läggs alltid till sist.
  parts.push(getJobAdSortLabel(s.sortBy).toLowerCase());
  return parts.join(" · ");
}

function SavedSearchRow({ savedSearch }: { savedSearch: SavedSearchDto }) {
  return (
    <li className="flex flex-col gap-2 border-b border-border-default px-1 py-4 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex flex-col gap-1">
        <span className="text-body font-medium text-text-primary">
          {savedSearch.name}
        </span>
        <span className="text-body-sm text-text-secondary">
          {criteriaSummary(savedSearch)}
        </span>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <Button asChild size="sm" variant="outline">
          <Link href={`/sokningar/${savedSearch.id}`}>Kör</Link>
        </Button>

        <DeleteSavedSearchDialog
          savedSearchId={savedSearch.id}
          savedSearchName={savedSearch.name}
        />
      </div>
    </li>
  );
}

export function SavedSearchList({ savedSearches }: SavedSearchListProps) {
  if (savedSearches.length === 0) {
    return (
      <div className="border-y border-border-default px-1 py-12 text-center">
        <p className="text-body text-text-primary">
          Du har inga sparade sökningar
        </p>
        <p className="mt-1 text-body-sm text-text-secondary">
          Gör en sökning under Jobb och välj Spara sökning för att lägga till
          den här.
        </p>
      </div>
    );
  }

  return (
    <ul
      className="flex flex-col border-t border-border-default"
      aria-label="Sparade sökningar"
    >
      {savedSearches.map((s) => (
        <SavedSearchRow key={s.id} savedSearch={s} />
      ))}
    </ul>
  );
}
