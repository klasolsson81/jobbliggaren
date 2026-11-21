import { useTranslations } from "next-intl";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";
import { QuickLinkPicker } from "@/components/job-ads/quick-link-picker";

const SECTION_TITLE_ID = "snabblankar-title";

interface QuickLinksSectionProps {
  juniorEnabled: boolean;
  searches: ReadonlyArray<RecentJobSearchDto>;
}

/**
 * ADR 0188 (#2265, amended for #2275) — the quick-link settings: the same tick-box list as the /jobb hero's "Anpassa",
 * here with every recent search.
 */
export function QuickLinksSection({ juniorEnabled, searches }: QuickLinksSectionProps) {
  const t = useTranslations("jobads.quickLinks");
  const tPages = useTranslations("pages");

  return (
    <section className="jp-card" aria-labelledby={SECTION_TITLE_ID}>
      <h2 id={SECTION_TITLE_ID} className="jp-card__title">
        {tPages("minaSidor.sections.snabblankar")}
      </h2>
      <p className="text-body text-text-primary">{t("settings.lede")}</p>
      <QuickLinkPicker juniorEnabled={juniorEnabled} searches={searches} emptyNote={t("settings.noSearches")} flush />
    </section>
  );
}
