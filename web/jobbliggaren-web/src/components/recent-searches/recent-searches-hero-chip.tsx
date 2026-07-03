"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Clock } from "lucide-react";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { buildRecentSearchHref } from "@/lib/job-ads/recent-search-href";
import { HeroChip } from "@/components/job-ads/hero-chip";
import { useRecentSearchCounts } from "@/lib/hooks/use-recent-search-counts";

interface RecentSearchesHeroChipProps {
  items: ReadonlyArray<RecentJobSearchDto>;
}

/**
 * ADR 0060 / ADR 0055 amend — "Senaste sökningar"-hero-chip på /jobb.
 * Auto-fångade sökningar; klick på rad → kör om sökningen med samma filter.
 * Klas-direktiv 2026-05-20 (anti-AI-trope): INGEN "NY"-pill på rader.
 *
 * Per-sökning-träffräknaren ("(N)" / "(N, M nya)") hämtas LAT klient-side
 * (B, CTO-beslut 2026-06-13) — först när dropdownen öppnas, via
 * `useRecentSearchCounts` (off-critical-path; den slow N+1-COUNT:en är TD-94).
 * `currentCount`/`newCount` på DTO:n är 0 vid sidladdning (`includeCount=false`)
 * och används INTE här — talet kommer enbart från hook-map:en. Saknas det
 * (laddar/timeout/fel) visas ingen siffra — ALDRIG en falsk "(0)" (husets
 * degraderingskontrakt, facet-counts/route.ts).
 */
export function RecentSearchesHeroChip({ items }: RecentSearchesHeroChipProps) {
  const router = useRouter();
  const t = useTranslations("jobads.recent");
  const [open, setOpen] = useState(false);
  // Lat hämtning: counten beräknas först när panelen öppnas (slow N+1 undviks
  // på /jobb-laddningar där användaren aldrig öppnar chippen).
  const counts = useRecentSearchCounts(open);

  return (
    <HeroChip
      label={t("chip.label")}
      icon={<Clock size={14} aria-hidden="true" />}
      count={items.length > 0 ? items.length : null}
      items={items}
      getKey={(it) => it.id}
      emptyText={t("chip.empty")}
      footerHref="/sokningar"
      footerLabel={t("chip.footer")}
      onOpenChange={setOpen}
      renderItem={(item, onClose) => {
        const href = buildRecentSearchHref(item);
        const count = counts?.get(item.id);
        const countText =
          count === undefined
            ? null
            : count.newCount > 0
              ? t("chip.countWithNew", {
                  currentCount: String(count.currentCount),
                  newCount: String(count.newCount),
                })
              : t("chip.count", { currentCount: String(count.currentCount) });
        return (
          <button
            type="button"
            onClick={() => {
              onClose();
              router.push(href);
            }}
            /* Konstant space-between (även innan counten laddat) så label-
               positionen inte hoppar när talet poppar in (civic = lugn, inga
               shifts — design-reviewer Minor B). */
            className="jp-popover__rowbtn"
          >
            <span className="truncate">
              {item.label}
            </span>
            {/* Spannet renderas alltid (tomt tills counten laddat) så raden inte
                reflowar vid pop-in. */}
            <span className="jp-popover__rowcount">
              {countText ?? ""}
            </span>
          </button>
        );
      }}
    />
  );
}
