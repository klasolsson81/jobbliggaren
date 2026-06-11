"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Search } from "lucide-react";
import type { JobAdSortBy, SuggestionDto } from "@/lib/dto/job-ads";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import {
  buildJobbHref,
  DEFAULT_SORT_BY,
  type JobbUrlState,
} from "@/lib/job-ads/search-params";
import { composeSuggestionChip } from "@/lib/job-ads/chip-composition";
import { JobAdTypeahead } from "./job-ad-typeahead";

/**
 * Hero-sökruta med typeahead-chip-komponist (ADR 0067 Beslut 5b, Fas E2d).
 * Client-ö som ENHANCERAR hero-GET-formuläret: utan JS submittar `<form
 * action="/jobb" method="get">` natively (q + hidden inputs bär dimensionerna)
 * — progressive enhancement, CLAUDE.md §5.2. Med JS:
 *
 * - **Taxonomi-förslag** (Län/Kommun/Yrkesområde/Yrkesgrupp) → strukturerat
 *   filter-chip på rätt dimension-param via `composeSuggestionChip` +
 *   `router.push`. Chipet renderas i toolbarens chip-rad + popover-räknarna
 *   ur URL:en (E2g: URL är ENDA sanningen — ingen egen chip-state-kopia).
 * - **Title-förslag / fri text** → `q` (residual-fritext, recall-bevarande
 *   FTS-hybrid; aldrig hårt dimensions-AND, ADR 0062/D2).
 *
 * Island-topologi (CTO VAL 1 = Variant A, 2026-06-11): separat ö bredvid
 * `JobbHeroFilters` (pills/popovers) — båda skriver samma URL via
 * `buildJobbHref` (SPOT). Sammanslagning avvisad (SRP, Martin kap. 7).
 */

interface JobbHeroSearchProps {
  taxonomy: TaxonomyTree | null;
  q: string;
  occupationGroup: ReadonlyArray<string>;
  region: ReadonlyArray<string>;
  municipality: ReadonlyArray<string>;
  sortBy: JobAdSortBy;
  pageSize?: string;
}

export function JobbHeroSearch({
  taxonomy,
  q,
  occupationGroup,
  region,
  municipality,
  sortBy,
  pageSize,
}: JobbHeroSearchProps) {
  const router = useRouter();
  const [, startTransition] = useTransition();

  // Inputens utkast. Bas = den committade q:n ur URL:en. Extern URL-q-ändring
  // (recent-sökning-navigering, "Rensa alla filter") speglas i fältet via
  // prev-prop-sentinel UNDER render — Reacts dokumenterade "justera state vid
  // prop-skifte"-mönster ("You Might Not Need an Effect"), ingen effect, ingen
  // set-state-in-effect-lintträff. Konsekvent med E2g (URL = sanningen).
  const [draft, setDraft] = useState(q);
  const [lastCommittedQ, setLastCommittedQ] = useState(q);
  if (q !== lastCommittedQ) {
    setLastCommittedQ(q);
    setDraft(q);
  }

  const current: JobbUrlState = {
    q,
    occupationGroup,
    region,
    municipality,
    sortBy,
    pageSize,
  };

  function navigate(next: JobbUrlState) {
    startTransition(() => router.push(buildJobbHref(next)));
  }

  function submitFreeText() {
    navigate({ ...current, q: draft });
  }

  function onSelectSuggestion(suggestion: SuggestionDto) {
    const next = composeSuggestionChip(suggestion, current, taxonomy);
    if (suggestion.kind === "Title") {
      // Title-träff blev söktermen → behåll den i fältet.
      setDraft(suggestion.label);
    } else {
      // Dimension-chip lagt i URL → rensa taxonomi-utkastet, visa committad q.
      setDraft(q);
    }
    navigate(next);
  }

  return (
    <form
      action="/jobb"
      method="get"
      className="jp-hero__searchblock"
      onSubmit={(e) => {
        e.preventDefault();
        submitFreeText();
      }}
    >
      <label htmlFor="jobb-q" className="jp-hero__searchlabels">
        Sök efter yrke, arbetsgivare eller ort
      </label>
      <div className="jp-hero__searchrow">
        <JobAdTypeahead
          id="jobb-q"
          name="q"
          value={draft}
          onChange={setDraft}
          onSelect={onSelectSuggestion}
          inputClassName="jp-hero__input"
          wrapperClassName="jp-hero__searchfield"
        />
        <button type="submit" className="jp-hero__searchbtn">
          <Search size={18} aria-hidden="true" /> Sök
        </button>
      </div>

      {/* No-JS-fallback: aktiva filter bärs som hidden inputs så en native
          GET-submit inte tappar dem (`page` utelämnas → ny sökterm = sida 1).
          Med JS går allt via router.push ovan; hidden inputs är då redundanta
          men ofarliga. */}
      {occupationGroup.map((v) => (
        <input
          key={`occupationGroup-${v}`}
          type="hidden"
          name="occupationGroup"
          value={v}
        />
      ))}
      {region.map((v) => (
        <input key={`region-${v}`} type="hidden" name="region" value={v} />
      ))}
      {municipality.map((v) => (
        <input
          key={`municipality-${v}`}
          type="hidden"
          name="municipality"
          value={v}
        />
      ))}
      {sortBy !== DEFAULT_SORT_BY && (
        <input type="hidden" name="sortBy" value={sortBy} />
      )}
      {pageSize && <input type="hidden" name="pageSize" value={pageSize} />}
    </form>
  );
}
