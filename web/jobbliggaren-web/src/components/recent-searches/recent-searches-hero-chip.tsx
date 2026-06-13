"use client";

import { useRouter } from "next/navigation";
import { Clock } from "lucide-react";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { buildJobbHref } from "@/lib/job-ads/search-params";
import { HeroChip } from "@/components/job-ads/hero-chip";

interface RecentSearchesHeroChipProps {
  items: ReadonlyArray<RecentJobSearchDto>;
}

function buildHrefFor(item: RecentJobSearchDto): string {
  return buildJobbHref({
    q: item.q ?? "",
    occupationGroup: item.occupationGroupList,
    region: item.regionList,
    municipality: item.municipalityList,
    // Klass 2 (ADR 0067 B2) — replay bär anställningsform/omfattning så
    // "Kör igen" inte tyst tappar filtret (backend-DTO bär listorna sedan #60).
    employmentType: item.employmentTypeList,
    worktimeExtent: item.worktimeExtentList,
    sortBy: item.sortBy,
  });
}

/**
 * ADR 0060 / ADR 0055 amend — "Senaste sökningar"-hero-chip på /jobb.
 * Auto-fångade sökningar; klick på rad → kör om sökningen med samma filter.
 * Klas-direktiv 2026-05-20 (anti-AI-trope): INGEN "NY"-pill på rader.
 *
 * Per-sökning-träffräknaren ("(N)" / "(N, M nya)") är TILLFÄLLIGT borttagen:
 * `currentCount`/`newCount` på DTO:n är 0 så länge sidan hämtar med
 * `includeCount=false` (slow N+1-COUNT, TD-94 — se ListRecentSearchesQueryHandler).
 * En synlig "(0)" vore desinformation (husets degraderingskontrakt,
 * facet-counts/route.ts) — hellre ingen siffra. Återinförs via lat klient-
 * hämtning (CTO-beslut 2026-06-13, samma mönster som useFacetCounts).
 */
export function RecentSearchesHeroChip({ items }: RecentSearchesHeroChipProps) {
  const router = useRouter();

  return (
    <HeroChip
      label="Senaste sökningar"
      icon={<Clock size={14} aria-hidden="true" />}
      count={items.length > 0 ? items.length : null}
      items={items}
      getKey={(it) => it.id}
      emptyText="Inga senaste sökningar än. Sök under Jobb så sparas de här."
      footerHref="/sokningar"
      footerLabel="Visa alla senaste sökningar"
      renderItem={(item, onClose) => {
        const href = buildHrefFor(item);
        return (
          <button
            type="button"
            onClick={() => {
              onClose();
              router.push(href);
            }}
            style={{
              display: "flex",
              alignItems: "center",
              width: "100%",
              padding: "10px 16px",
              background: "transparent",
              border: "none",
              textAlign: "left",
              cursor: "pointer",
              color: "var(--jp-ink-1)",
              fontSize: 14.5,
              gap: 12,
            }}
            onMouseOver={(e) => {
              e.currentTarget.style.background = "var(--jp-surface-3)";
            }}
            onMouseOut={(e) => {
              e.currentTarget.style.background = "transparent";
            }}
          >
            <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
              {item.label}
            </span>
          </button>
        );
      }}
    />
  );
}
