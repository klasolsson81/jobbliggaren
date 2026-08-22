"use client";

import { useTranslations } from "next-intl";
import { Bookmark } from "lucide-react";
import type { SavedJobAdDto } from "@/lib/dto/saved-job-ads";
import { HeroChip } from "@/components/job-ads/hero-chip";

interface SavedJobAdsHeroChipProps {
  items: ReadonlyArray<SavedJobAdDto>;
}

/**
 * F6 P5 Punkt 2 PR5 — "Sparade annonser"-hero-chip på `/jobb` (paritet
 * RecentSearchesHeroChip + Platsbanken-direktiv: chips till höger i hero).
 * Klick på rad → navigera till `/jobb/{jobAdId}` (öppnar modalen).
 * Tom-text guidar till modal-footer-toggle.
 */
export function SavedJobAdsHeroChip({ items }: SavedJobAdsHeroChipProps) {
  const t = useTranslations("jobads.saved");

  return (
    <HeroChip
      label={t("chip.label")}
      icon={<Bookmark size={14} aria-hidden="true" />}
      count={items.length > 0 ? items.length : null}
      items={items}
      getKey={(it) => it.id}
      getHref={(it) => `/jobb/${it.jobAdId}`}
      getLabel={(it) => it.jobAd?.title ?? t("removed")}
      // ADR 0048 Beslut c: annonsen är soft-deletad, raden står kvar dämpad.
      isMuted={(it) => !it.jobAd}
      emptyText={t("chip.empty")}
      footerHref="/sparade"
      footerLabel={t("chip.footer")}
      renderTrailing={(it) =>
        it.jobAd?.company ? (
          <span
            className="text-micro text-text-primary shrink-0 truncate"
            style={{
              maxWidth: 140,
            }}
          >
            {it.jobAd.company}
          </span>
        ) : null
      }
    />
  );
}
