"use client";

import { useRouter } from "next/navigation";
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
  const router = useRouter();
  const t = useTranslations("jobads.saved");

  return (
    <HeroChip
      label={t("chip.label")}
      icon={<Bookmark size={14} aria-hidden="true" />}
      count={items.length > 0 ? items.length : null}
      items={items}
      getKey={(it) => it.id}
      emptyText={t("chip.empty")}
      footerHref="/sparade"
      footerLabel={t("chip.footer")}
      renderItem={(item, onClose) => {
        const title = item.jobAd?.title ?? t("removed");
        const company = item.jobAd?.company;
        const href = `/jobb/${item.jobAdId}`;
        return (
          <button
            type="button"
            onClick={() => {
              onClose();
              router.push(href);
            }}
            className="jp-popover__rowbtn"
          >
            <span
              style={{
                overflow: "hidden",
                textOverflow: "ellipsis",
                whiteSpace: "nowrap",
                opacity: item.jobAd ? 1 : 0.6,
              }}
            >
              {title}
            </span>
            {company && (
              <span
                className="text-micro text-text-primary shrink-0 truncate"
                style={{
                  maxWidth: 140,
                }}
              >
                {company}
              </span>
            )}
          </button>
        );
      }}
    />
  );
}
